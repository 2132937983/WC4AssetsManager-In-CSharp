using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Rendering.Skia;

public sealed class TacticalMapImageCache : IDisposable
{
    private static readonly object _lock = new();
    private static TacticalMapImageCache? _instance;

    public static TacticalMapImageCache Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new TacticalMapImageCache();
                return _instance;
            }
        }
    }

    private readonly Dictionary<string, SKImage> _images = new();

    /// <summary>
    /// 图集底图。所有图块都由 <see cref="SKImage.Subset"/> 从它零拷贝派生，
    /// 共享同一份底层像素，因此必须常驻到缓存清空为止，不能提前 Dispose。
    /// </summary>
    private SKImage? _atlasImage;

    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            var parser = ConfigManager.Instance.TacticalMapParser;
            if (parser == null)
            {
                Debug.WriteLine("[TacticalMapImageCache] TacticalMapParser 未初始化，跳过图块缓存");
                return;
            }

            var surfaceData = parser.SurfaceData;
            if (surfaceData == null || surfaceData.Length == 0)
            {
                Debug.WriteLine("[TacticalMapImageCache] 无图集数据，跳过图块缓存");
                return;
            }

            try
            {
                var sw = Stopwatch.StartNew();

                using var stream = new MemoryStream(surfaceData);
                using var atlasBitmap = SKBitmap.Decode(stream);
                if (atlasBitmap == null)
                {
                    Debug.WriteLine("[TacticalMapImageCache] 解码图集失败");
                    return;
                }

                Debug.WriteLine($"[TacticalMapImageCache] 解码图集成功: {atlasBitmap.Width}x{atlasBitmap.Height}, 耗时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // FromBitmap 会复制像素数据，因此 atlasBitmap 释放后 atlasImage 依然有效。
                var atlasImage = SKImage.FromBitmap(atlasBitmap);
                Debug.WriteLine($"[TacticalMapImageCache] 图集转为 SKImage, 耗时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 所有图块都来自同一张图集，原先逐块「新建位图 + 全图重绘 + FromBitmap 拷贝」
                // 导致 1803 次位图分配与像素搬运（实测约 10.8 秒）。
                // 改为 Subset 零拷贝共享底图像素，仅创建轻量包装对象。
                int failed = 0;
                foreach (var def in parser.ImageDefinitions)
                {
                    if (string.IsNullOrEmpty(def.Name) || def.Width <= 0 || def.Height <= 0)
                        continue;

                    int x = def.X;
                    int y = def.Y;
                    int w = Math.Min(def.Width, atlasImage.Width - x);
                    int h = Math.Min(def.Height, atlasImage.Height - y);

                    if (w <= 0 || h <= 0) continue;

                    try
                    {
                        _images[def.Name] = atlasImage.Subset(new SKRectI(x, y, x + w, y + h));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Debug.WriteLine($"[TacticalMapImageCache] 提取图块 {def.Name} 失败: {ex.Message}");
                    }
                }

                // Subset 与底图共享像素，底图必须常驻。
                _atlasImage = atlasImage;

                _initialized = true;
                Debug.WriteLine($"[TacticalMapImageCache] 缓存完成，共 {_images.Count} 个图块（失败 {failed}）, 切片耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TacticalMapImageCache] 初始化失败: {ex.Message}");
            }
        }
    }

    public SKImage? GetImage(string name)
    {
        if (!_initialized) return null;
        _images.TryGetValue(name, out var image);
        return image;
    }

    public bool HasImage(string name)
    {
        if (!_initialized) return false;
        return _images.ContainsKey(name);
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            foreach (var image in _images.Values)
                image?.Dispose();
            _images.Clear();

            // 所有 Subset 都共享底图像素，底图必须最后释放。
            _atlasImage?.Dispose();
            _atlasImage = null;

            _initialized = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearCache();
    }
}