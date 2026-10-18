using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Helpers;

public sealed class CoastMaskProcessor
{
    private static CoastMaskProcessor? _instance;
    private static readonly object _instanceLock = new();

    private readonly Dictionary<string, (SKBitmap CoastMask, SKBitmap LandMask)> _maskCache = new();
    private readonly object _cacheLock = new();

    private SKImage? _landTexture;
    private SKImage? _hexagonLandTexture;

    private SKImage? _finalCoastAtlas;
    private readonly Dictionary<string, SKRect> _finalAtlasSpriteRects = new();
    private readonly Dictionary<string, SKSize> _finalAtlasSpriteSizes = new();
    private readonly Dictionary<string, SKPoint> _finalAtlasSpriteOrigins = new();
    private bool _finalAtlasBuilt;
    private SKImage? _finalAtlasLandTextureRef;

    private bool _initialized;

    public bool IsInitialized => _initialized;
    public bool HasFinalAtlas => _finalAtlasBuilt && _finalCoastAtlas != null;

    #region 最终图集磁盘缓存

    /// <summary>缓存子目录名</summary>
    private const string CacheFolderName = "CoastAtlas";

    /// <summary>缓存格式版本；结构变更时递增即可让旧缓存自然失效</summary>
    private const int CacheFormatVersion = 1;

    /// <summary>
    /// 缓存目录：固定在 %LOCALAPPDATA%\WC4MapEditor\Cache\CoastAtlas。
    /// <para>
    /// 刻意**不使用**相对工作目录（如 <c>Path.Combine(".", "Cache")</c>）——
    /// 否则从 IDE / 双击 exe / 命令行启动会解析到不同位置，
    /// 且当工作目录位于 bin 下时，清理 bin 会连带删除缓存。
    /// </para>
    /// </summary>
    private static string GetCacheDirectory()
    {
        string? root = null;
        try
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        catch { /* 取不到则走兜底 */ }

        if (string.IsNullOrEmpty(root))
            root = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(root, "WC4MapEditor", "Cache", CacheFolderName);
    }

    /// <summary>图集缓存的元数据</summary>
    private sealed class FinalAtlasCacheMeta
    {
        public int Version { get; set; } = CacheFormatVersion;
        public int AtlasWidth { get; set; }
        public int AtlasHeight { get; set; }
        public List<SpriteMeta> Sprites { get; set; } = new();
    }

    /// <summary>单个精灵在图集中的位置与原点</summary>
    private sealed class SpriteMeta
    {
        public string Name { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
        public float Ox { get; set; }
        public float Oy { get; set; }
    }

    /// <summary>
    /// 计算源文件指纹。海岸线灰度图集、其配置、陆地纹理三者任一发生变化都会得到不同的 key，
    /// 从而使旧缓存自然失效，无需手动清理。
    /// </summary>
    private static string? ComputeSourceFingerprint()
    {
        try
        {
            string grayLevelDir = ConfigManager.Instance.GetTexturePath("MapCoastGrayLevel");
            string terrainDir = ConfigManager.Instance.GetTexturePath("MapTerrian");

            string[] sources =
            [
                Path.Combine(grayLevelDir, "coastmask_hd.webp"),
                Path.Combine(grayLevelDir, "coastmask_hd.xml"),
                Path.Combine(terrainDir, "MapLand.png"),
            ];

            var sb = new StringBuilder();
            foreach (var path in sources)
            {
                if (!File.Exists(path)) { sb.Append("missing|"); continue; }
                var fi = new FileInfo(path);
                sb.Append(Path.GetFileName(path)).Append(':')
                  .Append(fi.Length).Append(':')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append('|');
            }

            return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 计算缓存指纹失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>尝试从磁盘恢复最终图集与精灵元数据；成功返回 true。</summary>
    private bool TryLoadFinalAtlasFromCache()
    {
        try
        {
            var key = ComputeSourceFingerprint();
            if (key == null) return false;

            var dir = GetCacheDirectory();
            var atlasPath = Path.Combine(dir, key + ".webp");
            var metaPath = Path.Combine(dir, key + ".json");
            if (!File.Exists(atlasPath) || !File.Exists(metaPath)) return false;

            var meta = JsonSerializer.Deserialize<FinalAtlasCacheMeta>(File.ReadAllText(metaPath));
            if (meta == null || meta.Version != CacheFormatVersion || meta.Sprites.Count == 0) return false;

            SKImage? atlas;
            using (var fs = File.OpenRead(atlasPath))
                atlas = SKImage.FromEncodedData(fs);

            if (atlas == null) return false;
            if (atlas.Width != meta.AtlasWidth || atlas.Height != meta.AtlasHeight)
            {
                atlas.Dispose();
                Debug.WriteLine("[CoastMaskProcessor] 图集缓存尺寸不匹配，已忽略");
                return false;
            }

            _finalCoastAtlas?.Dispose();
            _finalCoastAtlas = atlas;
            _finalAtlasSpriteRects.Clear();
            _finalAtlasSpriteSizes.Clear();
            _finalAtlasSpriteOrigins.Clear();

            foreach (var s in meta.Sprites)
            {
                _finalAtlasSpriteRects[s.Name] = new SKRect(s.X, s.Y, s.X + s.W, s.Y + s.H);
                _finalAtlasSpriteSizes[s.Name] = new SKSize(s.W, s.H);
                _finalAtlasSpriteOrigins[s.Name] = new SKPoint(s.Ox, s.Oy);
            }

            _finalAtlasBuilt = true;
            _finalAtlasLandTextureRef = _landTexture;
            Debug.WriteLine($"[CoastMaskProcessor] 已从磁盘缓存加载最终图集: {meta.Sprites.Count} 个精灵, {atlas.Width}x{atlas.Height}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 读取图集缓存失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>把当前最终图集与精灵元数据写入磁盘缓存。</summary>
    private void SaveFinalAtlasToCache()
    {
        try
        {
            if (_finalCoastAtlas == null || _finalAtlasSpriteRects.Count == 0) return;

            var key = ComputeSourceFingerprint();
            if (key == null) return;

            var dir = GetCacheDirectory();
            Directory.CreateDirectory(dir);

            var atlasPath = Path.Combine(dir, key + ".webp");
            using (var fs = File.Create(atlasPath))
            using (var data = _finalCoastAtlas.Encode(SKEncodedImageFormat.Webp, 100))
                data.SaveTo(fs);

            var meta = new FinalAtlasCacheMeta
            {
                AtlasWidth = _finalCoastAtlas.Width,
                AtlasHeight = _finalCoastAtlas.Height,
                Sprites = _finalAtlasSpriteRects.Select(kv => new SpriteMeta
                {
                    Name = kv.Key,
                    X = kv.Value.Left,
                    Y = kv.Value.Top,
                    W = kv.Value.Width,
                    H = kv.Value.Height,
                    Ox = _finalAtlasSpriteOrigins.TryGetValue(kv.Key, out var o) ? o.X : kv.Value.Width / 2f,
                    Oy = _finalAtlasSpriteOrigins.TryGetValue(kv.Key, out var o2) ? o2.Y : kv.Value.Height / 2f,
                }).ToList()
            };
            File.WriteAllText(Path.Combine(dir, key + ".json"), JsonSerializer.Serialize(meta));

            Debug.WriteLine($"[CoastMaskProcessor] 最终图集已写入缓存: {atlasPath} ({meta.Sprites.Count} 个精灵)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 写入图集缓存失败: {ex.Message}");
        }
    }

    /// <summary>清空磁盘上的图集与遮罩缓存（供诊断 / 强制重建使用）</summary>
    public static void ClearDiskCache()
    {
        try
        {
            var dir = GetCacheDirectory();
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.webp")) File.Delete(f);
            foreach (var f in Directory.GetFiles(dir, "*.json")) File.Delete(f);
            foreach (var f in Directory.GetFiles(dir, "masks_*.bin.gz")) File.Delete(f);
            Debug.WriteLine("[CoastMaskProcessor] 磁盘缓存已清空");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 清空缓存失败: {ex.Message}");
        }
    }

    #endregion

    #region 遮罩（_maskCache）磁盘缓存

    /// <summary>遮罩缓存格式版本；结构变更时递增即可让旧缓存自然失效</summary>
    private const int MaskCacheFormatVersion = 1;

    /// <summary>
    /// 本次实例是否由磁盘缓存恢复。
    /// 宿主可据此跳过后续预热步骤（如 CoastHelper.PreGenerateAllHexagonCoasts）。
    /// </summary>
    public bool LoadedFromDiskCache { get; private set; }

    private static string GetMaskCachePath(string key) =>
        Path.Combine(GetCacheDirectory(), $"masks_{key}.bin.gz");

    /// <summary>
    /// 尝试从磁盘恢复整份遮罩缓存（正常渲染路径直接依赖 _maskCache，这是打开场景卡顿的主因）；成功返回 true。
    /// </summary>
    private bool TryLoadMasksFromCache()
    {
        Dictionary<string, (SKBitmap CoastMask, SKBitmap LandMask)>? loaded = null;
        try
        {
            var key = ComputeSourceFingerprint();
            if (key == null) return false;

            var path = GetMaskCachePath(key);
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[CoastMaskProcessor] 未找到遮罩缓存（首次运行属正常）: {path}");
                return false;
            }

            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new BinaryReader(gz, Encoding.UTF8);

            if (reader.ReadInt32() != MaskCacheFormatVersion) return false;
            int count = reader.ReadInt32();
            if (count <= 0) return false;

            loaded = new Dictionary<string, (SKBitmap, SKBitmap)>(count);
            for (int i = 0; i < count; i++)
            {
                int nameLen = reader.ReadInt32();
                if (nameLen <= 0 || nameLen > 1024) return false;
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));

                int w = reader.ReadInt32();
                int h = reader.ReadInt32();
                if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return false;

                var coastAlpha = reader.ReadBytes(w * h);
                var landAlpha = reader.ReadBytes(w * h);
                if (coastAlpha.Length != w * h || landAlpha.Length != w * h) return false;

                loaded[name] = (BuildMaskBitmap(coastAlpha, w, h), BuildMaskBitmap(landAlpha, w, h));
            }

            lock (_cacheLock)
            {
                foreach (var kv in _maskCache.Values)
                {
                    kv.CoastMask.Dispose();
                    kv.LandMask.Dispose();
                }
                _maskCache.Clear();
                foreach (var kv in loaded)
                    _maskCache[kv.Key] = kv.Value;
            }
            loaded = null;   // 所有权已转移给 _maskCache

            Debug.WriteLine($"[CoastMaskProcessor] 遮罩缓存已从磁盘加载: {count} 个");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 读取遮罩缓存失败: {ex.Message}");
            return false;
        }
        finally
        {
            if (loaded != null)
            {
                foreach (var kv in loaded.Values)
                {
                    kv.CoastMask.Dispose();
                    kv.LandMask.Dispose();
                }
            }
        }
    }

    /// <summary>把当前遮罩缓存写入磁盘。</summary>
    private void SaveMasksToCache()
    {
        try
        {
            KeyValuePair<string, (SKBitmap CoastMask, SKBitmap LandMask)>[] snapshot;
            lock (_cacheLock)
            {
                if (_maskCache.Count == 0) return;
                snapshot = _maskCache.ToArray();
            }

            var key = ComputeSourceFingerprint();
            if (key == null) return;

            var dir = GetCacheDirectory();
            Directory.CreateDirectory(dir);

            var path = GetMaskCachePath(key);
            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(gz, Encoding.UTF8))
            {
                writer.Write(MaskCacheFormatVersion);
                writer.Write(snapshot.Length);
                foreach (var kv in snapshot)
                {
                    var nameBytes = Encoding.UTF8.GetBytes(kv.Key);
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);
                    writer.Write(kv.Value.CoastMask.Width);
                    writer.Write(kv.Value.CoastMask.Height);
                    writer.Write(ExtractAlpha(kv.Value.CoastMask));
                    writer.Write(ExtractAlpha(kv.Value.LandMask));
                }
            }

            var fi = new FileInfo(path);
            Debug.WriteLine($"[CoastMaskProcessor] 遮罩缓存已写入磁盘: {path} ({snapshot.Length} 个, {fi.Length / 1024.0:F0} KB)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 写入遮罩缓存失败: {ex.Message}");
        }
    }

    /// <summary>从 RGBA bitmap 抽取 alpha 通道（遮罩只有 alpha 有语义）。</summary>
    private static byte[] ExtractAlpha(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        var alpha = new byte[w * h];
        var ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero) return alpha;

        var rowBytes = bitmap.RowBytes;
        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * rowBytes;
            int alphaRow = y * w;
            for (int x = 0; x < w; x++)
                alpha[alphaRow + x] = Marshal.ReadByte(ptr, rowOffset + x * 4 + 3);
        }
        return alpha;
    }

    /// <summary>
    /// 把单通道 alpha 展开为 RGBA bitmap（RGB 恒为 255，与 CreateMasks 的输出保持一致）。
    /// </summary>
    private static SKBitmap BuildMaskBitmap(byte[] alpha, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var span = pixels.AsSpan();
        for (int i = 0; i < alpha.Length; i++)
        {
            int o = i * 4;
            span[o] = 255;
            span[o + 1] = 255;
            span[o + 2] = 255;
            span[o + 3] = alpha[i];
        }

        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var ptr = bmp.GetPixels();
        if (ptr == IntPtr.Zero)
        {
            bmp.Dispose();
            throw new InvalidOperationException($"无法获取像素指针: {width}x{height}");
        }

        // 拷贝进 Skia 自管内存（InstallPixels 不拷贝且需调用方保证内存存活，故不用）
        int rowBytes = bmp.RowBytes;
        if (rowBytes == width * 4)
        {
            Marshal.Copy(pixels, 0, ptr, pixels.Length);
        }
        else
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, IntPtr.Add(ptr, y * rowBytes), width * 4);
        }

        return bmp;
    }

    #endregion

    public static CoastMaskProcessor Instance
    {
        get
        {
            lock (_instanceLock)
            {
                _instance ??= new CoastMaskProcessor();
            }
            return _instance;
        }
    }

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public CoastMaskProcessor() { }

    public void Initialize(CoastHelper coastHelper)
    {
        Initialize(coastHelper, null);
    }

    public void Initialize(CoastHelper coastHelper, Action<int, string>? progressCallback)
    {
        if (coastHelper == null) return;
        if (_initialized) { progressCallback?.Invoke(100, "海岸线遮罩已缓存"); return; }

        Debug.WriteLine($"[CoastMaskProcessor] 缓存目录: {GetCacheDirectory()}");

        // ① 优先从磁盘恢复整份遮罩缓存 —— 正常渲染路径（RenderCoastFromAtlas → DrawMaskedCoast）
        //    直接依赖 _maskCache，命中即可跳过 90 个精灵的遮罩生成（实测约 4.7 秒，是打开场景卡顿的主因）
        if (TryLoadMasksFromCache())
        {
            _initialized = true;
            LoadedFromDiskCache = true;
            progressCallback?.Invoke(100, "海岸线遮罩已从本地缓存加载");
            return;
        }

        // ② 其次尝试最终图集缓存（截图路径的产物，同样可直接支撑渲染，避免再生成遮罩）
        if (TryLoadFinalAtlasFromCache())
        {
            _initialized = true;
            LoadedFromDiskCache = true;
            progressCallback?.Invoke(100, "海岸线图集已从本地缓存加载");
            return;
        }

        Debug.WriteLine("[CoastMaskProcessor] 开始初始化遮罩缓存...");
        var sw = Stopwatch.StartNew();
        var processedCount = 0;

        var spriteNames = coastHelper.GetGrayLevelSpriteNames();
        var totalCount = spriteNames.Count;

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"加工海岸线遮罩... ({i + 1}/{totalCount})");

                var maskImage = coastHelper.GetGrayLevelCoastImage(spriteName);
                if (maskImage != null)
                {
                    var masks = CreateMasks(maskImage);
                    if (masks.HasValue)
                    {
                        lock (_cacheLock)
                            _maskCache[spriteName] = masks.Value;
                        processedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 处理遮罩 {spriteName} 失败: {ex.Message}");
            }
        }

        _initialized = true;
        sw.Stop();
        progressCallback?.Invoke(100, $"海岸线遮罩完成 ({processedCount}/{totalCount})");
        Debug.WriteLine($"[CoastMaskProcessor] 遮罩缓存初始化完成: {processedCount} 个，耗时 {sw.ElapsedMilliseconds}ms");

        // 落盘：下次启动直接加载，跳过上面整段生成过程
        SaveMasksToCache();
    }

    public void DrawMaskedCoast(SKCanvas canvas, SKImage coastAtlasImage, SKRect coastSrcRect,
        string spriteName, SKRect dstRect, SKPaint paint, double zoomLevel = 1.0)
    {
        if (canvas == null || coastAtlasImage == null || string.IsNullOrEmpty(spriteName)) return;

        SKBitmap? coastMask = null, landMask = null;
        lock (_cacheLock)
        {
            if (_maskCache.TryGetValue(spriteName, out var masks))
            {
                coastMask = masks.CoastMask;
                landMask = masks.LandMask;
            }
        }

        if (coastMask == null)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        RenderWithMasks(canvas, coastAtlasImage, coastSrcRect, coastMask, landMask, dstRect, paint);
    }

    public void DrawFinalCoast(SKCanvas canvas, string spriteName, SKRect dstRect, SKPaint paint, double zoomLevel = 1.0)
    {
        if (canvas == null || string.IsNullOrEmpty(spriteName)) return;
        if (!_finalAtlasBuilt || _finalCoastAtlas == null) return;

        if (!_finalAtlasSpriteRects.TryGetValue(spriteName, out var srcRect)) return;
        
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest);
        canvas.DrawImage(_finalCoastAtlas, srcRect, dstRect, sampling, paint);
    }

    private void RenderWithMasks(SKCanvas canvas, SKImage coastAtlasImage, SKRect coastSrcRect,
        SKBitmap coastMask, SKBitmap landMask, SKRect dstRect, SKPaint paint)
    {
        var width = (int)dstRect.Width;
        var height = (int)dstRect.Height;
        if (width <= 0 || height <= 0) return;
        if (width > 4096 || height > 4096)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        var info = new SKImageInfo(width, height);
        using var tempSurface = SKSurface.Create(info);
        if (tempSurface == null)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        var tempCanvas = tempSurface.Canvas;
        tempCanvas.Clear(SKColors.Transparent);

        var nearestSampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        var linearSampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest);

        var landTextureToUse = _hexagonLandTexture ?? _landTexture;
        if (landMask != null && landTextureToUse != null)
        {
            try
            {
                using var landTempSurface = SKSurface.Create(info);
                if (landTempSurface != null)
                {
                    var landTempCanvas = landTempSurface.Canvas;
                    landTempCanvas.Clear(SKColors.Transparent);

                    using (var maskPaint = new SKPaint { IsAntialias = false })
                        landTempCanvas.DrawBitmap(landMask, new SKRect(0, 0, width, height), maskPaint);

                    using (var srcInPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.SrcIn })
                        landTempCanvas.DrawImage(landTextureToUse, new SKRect(0, 0, landTextureToUse.Width, landTextureToUse.Height),
                            new SKRect(0, 0, width, height), linearSampling, srcInPaint);

                    using var landSnapshot = landTempSurface.Snapshot();
                    if (landSnapshot != null)
                        using (var resultPaint = new SKPaint { IsAntialias = false })
                            tempCanvas.DrawImage(landSnapshot, 0, 0, linearSampling, resultPaint);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 平地纹理渲染失败: {ex.Message}");
            }
        }

        try
        {
            using var coastTempSurface = SKSurface.Create(info);
            if (coastTempSurface != null)
            {
                var coastTempCanvas = coastTempSurface.Canvas;
                coastTempCanvas.Clear(SKColors.Transparent);

                using (var maskPaint = new SKPaint { IsAntialias = false })
                    coastTempCanvas.DrawBitmap(coastMask, new SKRect(0, 0, width, height), maskPaint);

                using (var srcInPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.SrcIn })
                    coastTempCanvas.DrawImage(coastAtlasImage, coastSrcRect, new SKRect(0, 0, width, height), linearSampling, srcInPaint);

                using var coastSnapshot = coastTempSurface.Snapshot();
                if (coastSnapshot != null)
                    using (var plusPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.Plus })
                        tempCanvas.DrawImage(coastSnapshot, 0, 0, linearSampling, plusPaint);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 海岸线渲染失败: {ex.Message}");
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        try
        {
            using var snapshot = tempSurface.Snapshot();
            if (snapshot != null)
                using (var finalPaint = new SKPaint { IsAntialias = false })
                    canvas.DrawImage(snapshot, dstRect, linearSampling, finalPaint);
        }
        catch
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
        }
    }

    private (SKBitmap CoastMask, SKBitmap LandMask)? CreateMasks(SKImage maskImage)
    {
        const int GREEN_THRESHOLD = 32;
        var width = maskImage.Width;
        var height = maskImage.Height;
        var coastMask = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var landMask = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        try
        {
            using var tempBitmap = SKBitmap.FromImage(maskImage);

            IntPtr srcPtr = tempBitmap.GetPixels();
            IntPtr coastPtr = coastMask.GetPixels();
            IntPtr landPtr = landMask.GetPixels();

            if (srcPtr == IntPtr.Zero || coastPtr == IntPtr.Zero || landPtr == IntPtr.Zero)
            {
                coastMask.Dispose();
                landMask.Dispose();
                return null;
            }

            // 原实现逐像素调用 Marshal.ReadByte（3 次/像素）与 SKBitmap.SetPixel（2 次/像素），
            // 90 张遮罩累计约 740 万次 P/Invoke 与格式转换，实测约 4.7 秒。
            // 这里改为整块 Marshal.Copy 后用 Span 直写像素缓冲区。
            int srcRowBytes = tempBitmap.RowBytes;
            int dstRowBytes = coastMask.RowBytes;

            var src = new byte[srcRowBytes * height];
            Marshal.Copy(srcPtr, src, 0, src.Length);

            var coast = new byte[dstRowBytes * height];
            var land = new byte[dstRowBytes * height];

            var srcSpan = new ReadOnlySpan<byte>(src);
            var coastSpan = new Span<byte>(coast);
            var landSpan = new Span<byte>(land);

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * srcRowBytes;
                int dstRow = y * dstRowBytes;

                for (int x = 0; x < width; x++)
                {
                    int si = srcRow + x * 4;
                    int di = dstRow + x * 4;

                    byte g = srcSpan[si + 1];
                    byte r = srcSpan[si + 2];

                    // 目标为预乘 alpha 的白色：RGB 分量等于 alpha 值。
                    // 等价于原先的 SetPixel(x, y, new SKColor(255, 255, 255, alpha))。
                    byte coastAlpha = (byte)(g <= GREEN_THRESHOLD ? r : 0);
                    coastSpan[di] = coastAlpha;
                    coastSpan[di + 1] = coastAlpha;
                    coastSpan[di + 2] = coastAlpha;
                    coastSpan[di + 3] = coastAlpha;

                    byte landAlpha = (byte)(g <= GREEN_THRESHOLD ? 255 - r : 0);
                    landSpan[di] = landAlpha;
                    landSpan[di + 1] = landAlpha;
                    landSpan[di + 2] = landAlpha;
                    landSpan[di + 3] = landAlpha;
                }
            }

            Marshal.Copy(coast, 0, coastPtr, coast.Length);
            Marshal.Copy(land, 0, landPtr, land.Length);

            return (coastMask, landMask);
        }
        catch
        {
            coastMask.Dispose();
            landMask.Dispose();
            return null;
        }
    }

    public void SetLandTexture(SKImage landTexture)
    {
        if (ReferenceEquals(_landTexture, landTexture)) return;

        if (_hexagonLandTexture != null && !ReferenceEquals(_hexagonLandTexture, _landTexture))
        {
            _hexagonLandTexture.Dispose();
            _hexagonLandTexture = null;
        }

        _landTexture = landTexture;
        _hexagonLandTexture = landTexture != null ? CreateHexagonLandTexture(landTexture) : null;
    }

    private static SKImage? CreateHexagonLandTexture(SKImage landTexture)
    {
        try
        {
            var width = landTexture.Width;
            var height = landTexture.Height;
            float hexSide = width / 2f;
            float hexHeight = hexSide * (float)Math.Sqrt(3);
            if (hexHeight > height) { hexHeight = height; hexSide = hexHeight / (float)Math.Sqrt(3); }
            float verticalMargin = (height - hexHeight) / 2;
            float centerX = width / 2f;
            float centerY = height / 2f;

            var hexPath = new SKPath();
            hexPath.MoveTo(centerX - hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide, centerY);
            hexPath.LineTo(centerX + hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide, centerY);
            hexPath.Close();

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.ClipPath(hexPath);
            surface.Canvas.DrawImage(landTexture, new SKRect(0, 0, width, height));
            return surface.Snapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 创建六边形平地纹理失败: {ex.Message}");
            return landTexture;
        }
    }

    public bool HasMask(string spriteName)
    {
        lock (_cacheLock)
            return _maskCache.ContainsKey(spriteName);
    }

    public void BuildFinalCoastAtlas(CoastHelper coastHelper, Action<int, string>? progressCallback = null)
    {
        if (coastHelper == null || !_initialized || _landTexture == null) return;
        if (_finalAtlasBuilt && ReferenceEquals(_finalAtlasLandTextureRef, _landTexture))
        {
            progressCallback?.Invoke(100, "海岸线最终图集已缓存");
            return;
        }

        Debug.WriteLine("[CoastMaskProcessor] 开始构建最终海岸线合成图集...");
        var sw = Stopwatch.StartNew();

        var spriteNames = coastHelper.GetGrayLevelSpriteNames();
        var totalCount = spriteNames.Count;
        if (totalCount == 0) { progressCallback?.Invoke(100, "没有海岸线精灵"); return; }

        var compositedBitmaps = new Dictionary<string, SKBitmap>();
        var compositedSizes = new Dictionary<string, SKSize>();
        var compositedOrigins = new Dictionary<string, SKPoint>();

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"合成海岸线纹理... ({i + 1}/{totalCount})");

                var result = BuildSingleFinalCoastTexture(coastHelper, spriteName);
                if (result.HasValue)
                {
                    compositedBitmaps[spriteName] = result.Value.Bitmap;
                    compositedSizes[spriteName] = new SKSize(result.Value.Width, result.Value.Height);
                    compositedOrigins[spriteName] = result.Value.Origin;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 合成纹理失败 [{spriteName}]: {ex.Message}");
            }
        }

        if (compositedBitmaps.Count == 0) { progressCallback?.Invoke(100, "合成失败"); return; }

        progressCallback?.Invoke(95, "打包海岸线图集...");
        PackFinalCoastAtlas(compositedBitmaps, compositedSizes, compositedOrigins);

        _finalAtlasLandTextureRef = _landTexture;
        _finalAtlasBuilt = true;
        sw.Stop();
        progressCallback?.Invoke(100, $"海岸线图集完成 ({compositedBitmaps.Count} 个精灵)");
        Debug.WriteLine($"[CoastMaskProcessor] 最终图集构建完成: {compositedBitmaps.Count} 个精灵，耗时 {sw.ElapsedMilliseconds}ms");

        // 落盘缓存：下次启动可直接加载，跳过整条烘焙链路
        SaveFinalAtlasToCache();
    }

    private (SKBitmap Bitmap, int Width, int Height, SKPoint Origin)? BuildSingleFinalCoastTexture(CoastHelper coastHelper, string spriteName)
    {
        var hexCoastImage = coastHelper.GetHexagonCoastImage(spriteName);
        if (hexCoastImage == null) return null;

        lock (_cacheLock)
        {
            if (!_maskCache.TryGetValue(spriteName, out var masks)) return null;
        }

        var width = hexCoastImage.Width;
        var height = hexCoastImage.Height;
        if (width <= 0 || height <= 0) return null;

        var spriteInfo = coastHelper.GetGrayLevelCoastSpriteInfo(spriteName);
        var origin = spriteInfo != null ? new SKPoint(spriteInfo.RefX, spriteInfo.RefY) : new SKPoint(width / 2f, height / 2f);

        var info = new SKImageInfo(width, height);
        using var tempSurface = SKSurface.Create(info);
        if (tempSurface == null) return null;

        var tempCanvas = tempSurface.Canvas;
        tempCanvas.Clear(SKColors.Transparent);

        lock (_cacheLock)
        {
            if (_maskCache.TryGetValue(spriteName, out var masks))
            {
                var landTextureToUse = _hexagonLandTexture ?? _landTexture;
                if (masks.LandMask != null && landTextureToUse != null)
                {
                    try
                    {
                        using var landTempSurface = SKSurface.Create(info);
                        if (landTempSurface != null)
                        {
                            var landTempCanvas = landTempSurface.Canvas;
                            landTempCanvas.Clear(SKColors.Transparent);
                            var linearSampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest);
                            using (var maskPaint = new SKPaint { IsAntialias = false })
                                landTempCanvas.DrawBitmap(masks.LandMask, new SKRect(0, 0, width, height), maskPaint);
                            using (var srcInPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.SrcIn })
                                landTempCanvas.DrawImage(landTextureToUse, new SKRect(0, 0, landTextureToUse.Width, landTextureToUse.Height),
                                    new SKRect(0, 0, width, height), linearSampling, srcInPaint);
                            using var landSnapshot = landTempSurface.Snapshot();
                            if (landSnapshot != null)
                                using (var resultPaint = new SKPaint { IsAntialias = false })
                                    tempCanvas.DrawImage(landSnapshot, 0, 0, linearSampling, resultPaint);
                        }
                    }
                    catch { }
                }

                if (masks.CoastMask != null)
                {
                    try
                    {
                        using var coastTempSurface = SKSurface.Create(info);
                        if (coastTempSurface != null)
                        {
                            var coastTempCanvas = coastTempSurface.Canvas;
                            coastTempCanvas.Clear(SKColors.Transparent);
                            var linearSampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest);
                            using (var maskPaint = new SKPaint { IsAntialias = false })
                                coastTempCanvas.DrawBitmap(masks.CoastMask, new SKRect(0, 0, width, height), maskPaint);
                            using (var srcInPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.SrcIn })
                                coastTempCanvas.DrawImage(hexCoastImage, new SKRect(0, 0, width, height), linearSampling, srcInPaint);
                            using var coastSnapshot = coastTempSurface.Snapshot();
                            if (coastSnapshot != null)
                                using (var plusPaint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.Plus })
                                    tempCanvas.DrawImage(coastSnapshot, 0, 0, linearSampling, plusPaint);
                        }
                    }
                    catch { }
                }
            }
        }

        using var snapshot = tempSurface.Snapshot();
        if (snapshot == null) return null;
        var bitmap = SKBitmap.FromImage(snapshot);
        return bitmap != null ? (bitmap, width, height, origin) : null;
    }

    private void PackFinalCoastAtlas(Dictionary<string, SKBitmap> compositedBitmaps,
        Dictionary<string, SKSize> compositedSizes, Dictionary<string, SKPoint> compositedOrigins)
    {
        if (compositedBitmaps.Count == 0) return;

        _finalCoastAtlas?.Dispose();
        _finalCoastAtlas = null;
        _finalAtlasSpriteRects.Clear();
        _finalAtlasSpriteSizes.Clear();
        _finalAtlasSpriteOrigins.Clear();

        var sortedSprites = compositedBitmaps.Keys.OrderByDescending(n => compositedSizes[n].Height).ToList();

        int totalArea = 0, maxWidth = 0;
        foreach (var kvp in compositedBitmaps)
        {
            totalArea += (int)(kvp.Value.Width * kvp.Value.Height);
            if (kvp.Value.Width > maxWidth) maxWidth = kvp.Value.Width;
        }

        int atlasSize = 1024;
        while (atlasSize * atlasSize < totalArea * 1.2) atlasSize *= 2;
        if (atlasSize > 8192) atlasSize = 8192;

        int currentX = 0, currentY = 0, rowHeight = 0;
        const int padding = 2;

        foreach (var spriteName in sortedSprites)
        {
            var bitmap = compositedBitmaps[spriteName];
            var spriteWidth = bitmap.Width;
            var spriteHeight = bitmap.Height;

            if (currentX + spriteWidth > atlasSize)
            {
                currentX = 0;
                currentY += rowHeight + padding;
                rowHeight = 0;
            }

            while (currentY + spriteHeight > atlasSize)
            {
                var newSize = atlasSize * 2;
                if (newSize > 8192) break;
                atlasSize = newSize;
            }

            _finalAtlasSpriteRects[spriteName] = new SKRect(currentX, currentY, currentX + spriteWidth, currentY + spriteHeight);
            _finalAtlasSpriteSizes[spriteName] = compositedSizes[spriteName];
            _finalAtlasSpriteOrigins[spriteName] = compositedOrigins[spriteName];

            if (spriteHeight > rowHeight) rowHeight = spriteHeight;
            currentX += spriteWidth + padding;
        }

        try
        {
            using var atlasSurface = SKSurface.Create(new SKImageInfo(atlasSize, atlasSize));
            if (atlasSurface == null) { _finalAtlasSpriteRects.Clear(); return; }

            atlasSurface.Canvas.Clear(SKColors.Transparent);
            foreach (var kvp in _finalAtlasSpriteRects)
            {
                var name = kvp.Key;
                var rect = kvp.Value;
                if (compositedBitmaps.TryGetValue(name, out var bmp))
                    atlasSurface.Canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height), rect);
            }
            _finalCoastAtlas = atlasSurface.Snapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 图集打包失败: {ex.Message}");
            _finalAtlasSpriteRects.Clear();
            _finalCoastAtlas = null;
        }

        foreach (var kvp in compositedBitmaps)
            kvp.Value.Dispose();
        compositedBitmaps.Clear();
    }

    public static List<(int, int)> GetHexNeighbors(int col, int row, int mapWidth, int mapHeight)
    {
        var neighbors = new List<(int, int)>();
        bool isEven = col % 2 == 0;

        int[] dcol, drow;
        if (isEven) { dcol = [0, 1, 1, 0, -1, -1]; drow = [-1, -1, 0, 1, 0, -1]; }
        else { dcol = [0, 1, 1, 0, -1, -1]; drow = [-1, 0, 1, 1, 1, 0]; }

        for (int i = 0; i < 6; i++)
        {
            int nc = col + dcol[i];
            int nr = row + drow[i];
            if (nc < 0) nc = mapWidth - 1;
            else if (nc >= mapWidth) nc = 0;
            if (nr >= 0 && nr < mapHeight)
                neighbors.Add((nc, nr));
        }
        return neighbors;
    }

    public static byte CalculateCoastDecorationType(MapData mapData, int col, int row)
    {
        const int OCEAN_TILE_TYPE = 1;
        var nonOceanNeighbors = GetNonOceanNeighbors(mapData, col, row, OCEAN_TILE_TYPE);

        return nonOceanNeighbors.Count switch
        {
            0 => 10,
            1 => GetDecorationForSingleNeighbor(nonOceanNeighbors[0].Direction),
            2 => GetDecorationForTwoNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction),
            3 => GetDecorationForThreeNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction),
            4 => GetDecorationForFourNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction),
            5 => GetDecorationForFiveNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction, nonOceanNeighbors[4].Direction),
            6 => 11,
            _ => 10
        };
    }

    private struct NeighborInfo { public int Col; public int Row; public string Direction; }

    private static List<NeighborInfo> GetNonOceanNeighbors(MapData mapData, int col, int row, int oceanType)
    {
        var result = new List<NeighborInfo>();
        bool isEven = col % 2 == 0;

        int[,] evenOffsets = { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 }, { -1, -1 } };
        int[,] oddOffsets = { { 0, -1 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 } };
        var offsets = isEven ? evenOffsets : oddOffsets;
        string[] directions = ["上方", "右上方", "右下方", "下方", "左下方", "左上方"];

        for (int i = 0; i < 6; i++)
        {
            int nc = col + offsets[i, 0];
            int nr = row + offsets[i, 1];
            if (nc < 0) nc = mapData.MapWidth - 1;
            else if (nc >= mapData.MapWidth) nc = 0;
            if (nr < 0 || nr >= mapData.MapHeight) continue;

            var terrain = mapData.GetTerrainAt(nc, nr);
            if (terrain.TileType1 != oceanType)
                result.Add(new NeighborInfo { Col = nc, Row = nr, Direction = directions[i] });
        }
        return result;
    }

    private static byte GetDecorationForSingleNeighbor(string direction) => direction switch
    {
        "上方" => 73, "右上方" => 72, "右下方" => 70, "下方" => 66, "左下方" => 58, "左上方" => 42, _ => 10
    };

    private static byte GetDecorationForTwoNeighbors(string dir1, string dir2)
    {
        var dirs = new HashSet<string> { dir1, dir2 };
        if (dirs.Contains("上方") && dirs.Contains("右上方")) return 71;
        if (dirs.Contains("上方") && dirs.Contains("右下方")) return 69;
        if (dirs.Contains("上方") && dirs.Contains("下方")) return 65;
        if (dirs.Contains("上方") && dirs.Contains("左下方")) return 57;
        if (dirs.Contains("上方") && dirs.Contains("左上方")) return 41;
        if (dirs.Contains("右上方") && dirs.Contains("左上方")) return 40;
        if (dirs.Contains("右上方") && dirs.Contains("右下方")) return 68;
        if (dirs.Contains("右上方") && dirs.Contains("下方")) return 64;
        if (dirs.Contains("右上方") && dirs.Contains("左下方")) return 56;
        if (dirs.Contains("右下方") && dirs.Contains("下方")) return 62;
        if (dirs.Contains("右下方") && dirs.Contains("左下方")) return 54;
        if (dirs.Contains("右下方") && dirs.Contains("左上方")) return 38;
        if (dirs.Contains("下方") && dirs.Contains("左下方")) return 50;
        if (dirs.Contains("下方") && dirs.Contains("左上方")) return 34;
        if (dirs.Contains("左下方") && dirs.Contains("左上方")) return 26;
        return 10;
    }

    private static byte GetDecorationForThreeNeighbors(string dir1, string dir2, string dir3)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3 };
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方")) return 67;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方")) return 63;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左下方")) return 55;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左上方")) return 39;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 61;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 53;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 37;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 49;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 33;
        if (dirs.Contains("上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 25;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 60;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 52;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 36;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 48;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 32;
        if (dirs.Contains("右上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 24;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 46;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 30;
        if (dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 22;
        if (dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 18;
        return 10;
    }

    private static byte GetDecorationForFourNeighbors(string dir1, string dir2, string dir3, string dir4)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3, dir4 };
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 59;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 51;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 35;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 47;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 31;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 23;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 45;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 29;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 21;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 17;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 44;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 28;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 20;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 16;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 14;
        return 10;
    }

    private static byte GetDecorationForFiveNeighbors(string dir1, string dir2, string dir3, string dir4, string dir5)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3, dir4, dir5 };
        if (!dirs.Contains("上方")) return 12;
        if (!dirs.Contains("右上方")) return 13;
        if (!dirs.Contains("右下方")) return 15;
        if (!dirs.Contains("下方")) return 19;
        if (!dirs.Contains("左下方")) return 27;
        if (!dirs.Contains("左上方")) return 43;
        return 10;
    }

    public void DisposeAll()
    {
        lock (_cacheLock)
        {
            foreach (var masks in _maskCache.Values)
            {
                masks.CoastMask?.Dispose();
                masks.LandMask?.Dispose();
            }
            _maskCache.Clear();
        }

        _finalCoastAtlas?.Dispose();
        _finalCoastAtlas = null;
        _finalAtlasSpriteRects.Clear();
        _finalAtlasSpriteSizes.Clear();
        _finalAtlasSpriteOrigins.Clear();
        _finalAtlasBuilt = false;
        _finalAtlasLandTextureRef = null;

        if (_hexagonLandTexture != null && !ReferenceEquals(_hexagonLandTexture, _landTexture))
            _hexagonLandTexture.Dispose();
        _hexagonLandTexture = null;
        _landTexture = null;
        _initialized = false;
    }
}