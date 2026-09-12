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
                using var stream = new MemoryStream(surfaceData);
                using var surfaceBitmap = SKBitmap.Decode(stream);
                if (surfaceBitmap == null)
                {
                    Debug.WriteLine("[TacticalMapImageCache] 解码图集失败");
                    return;
                }

                Debug.WriteLine($"[TacticalMapImageCache] 解码图集成功: {surfaceBitmap.Width}x{surfaceBitmap.Height}");

                foreach (var def in parser.ImageDefinitions)
                {
                    if (string.IsNullOrEmpty(def.Name) || def.Width <= 0 || def.Height <= 0)
                        continue;

                    int x = def.X;
                    int y = def.Y;
                    int w = Math.Min(def.Width, surfaceBitmap.Width - x);
                    int h = Math.Min(def.Height, surfaceBitmap.Height - y);

                    if (w <= 0 || h <= 0) continue;

                    try
                    {
                        var subsetBitmap = new SKBitmap(w, h);
                        using (var canvas = new SKCanvas(subsetBitmap))
                        {
                            canvas.DrawBitmap(surfaceBitmap, -x, -y);
                        }

                        var image = SKImage.FromBitmap(subsetBitmap);
                        subsetBitmap.Dispose();

                        _images[def.Name] = image;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TacticalMapImageCache] 提取图块 {def.Name} 失败: {ex.Message}");
                    }
                }

                _initialized = true;
                Debug.WriteLine($"[TacticalMapImageCache] 缓存完成，共 {_images.Count} 个图块");
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