using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace WC4MapEditor.Rendering.Helpers;

public sealed class TextureDiskCache
{
    private static TextureDiskCache? _instance;
    private static readonly object _lock = new();

    private readonly string _cacheDirectory;
    private readonly Dictionary<string, SKImage> _memoryCache = new();
    private readonly List<string> _accessOrder = new();
    private const int MaxMemoryCacheSize = 50;
    private long _cacheHits;
    private long _cacheMisses;

    public string CacheStats
    {
        get
        {
            var total = _cacheHits + _cacheMisses;
            var hitRate = total > 0 ? (_cacheHits * 100.0 / total).ToString("F1") : "0.0";
            return $"磁盘缓存: 命中 {_cacheHits}, 未命中 {_cacheMisses}, 命中率 {hitRate}%";
        }
    }

    public static TextureDiskCache Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new TextureDiskCache();
                }
            }
            return _instance;
        }
    }

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public TextureDiskCache()
    {
        _cacheDirectory = Path.Combine(".", "Cache", "Textures");
        EnsureCacheDirectory();
        Debug.WriteLine($"[TextureDiskCache] 初始化完成，缓存目录: {Path.GetFullPath(_cacheDirectory)}");
    }

    public static void Initialize()
    {
        var _ = Instance;
        Debug.WriteLine("[TextureDiskCache] 手动初始化完成");
    }

    private void EnsureCacheDirectory()
    {
        if (!Directory.Exists(_cacheDirectory))
            Directory.CreateDirectory(_cacheDirectory);
    }

    private string GetCacheFilePath(string cacheKey)
    {
        var hash = GetMd5Hash(cacheKey);
        return Path.Combine(_cacheDirectory, $"{hash}.png");
    }

    private static string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        var sb = new StringBuilder();
        foreach (var b in hashBytes)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    public SKImage? GetTexture(string cacheKey)
    {
        lock (_lock)
        {
            if (_memoryCache.TryGetValue(cacheKey, out var cached))
            {
                _cacheHits++;
                _accessOrder.Remove(cacheKey);
                _accessOrder.Add(cacheKey);
                return cached;
            }

            var cacheFilePath = GetCacheFilePath(cacheKey);
            if (File.Exists(cacheFilePath))
            {
                try
                {
                    using var stream = File.OpenRead(cacheFilePath);
                    using var bitmap = SKBitmap.Decode(stream);
                    if (bitmap != null)
                    {
                        var image = SKImage.FromBitmap(bitmap);
                        if (image != null)
                        {
                            AddToMemoryCache(cacheKey, image);
                            _cacheHits++;
                            return image;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TextureDiskCache] 读取缓存失败: {cacheKey} - {ex.Message}");
                    try { File.Delete(cacheFilePath); } catch { }
                }
            }

            _cacheMisses++;
            return null;
        }
    }

    public void SaveTexture(string cacheKey, SKImage image)
    {
        lock (_lock)
        {
            var cacheFilePath = GetCacheFilePath(cacheKey);
            try
            {
                using var stream = File.OpenWrite(cacheFilePath);
                image.Encode(SKEncodedImageFormat.Png, 90).SaveTo(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TextureDiskCache] 保存缓存失败: {cacheKey} - {ex.Message}");
            }

            AddToMemoryCache(cacheKey, image);
        }
    }

    private void AddToMemoryCache(string cacheKey, SKImage image)
    {
        while (_memoryCache.Count >= MaxMemoryCacheSize)
        {
            if (_accessOrder.Count > 0)
            {
                var oldestKey = _accessOrder[0];
                _accessOrder.RemoveAt(0);
                if (_memoryCache.Remove(oldestKey, out var oldest))
                    oldest.Dispose();
            }
            else break;
        }

        if (!_memoryCache.ContainsKey(cacheKey))
        {
            _memoryCache[cacheKey] = image;
            _accessOrder.Add(cacheKey);
        }
    }

    public void ClearMemoryCache()
    {
        lock (_lock)
        {
            foreach (var image in _memoryCache.Values)
                image.Dispose();
            _memoryCache.Clear();
            _accessOrder.Clear();
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            foreach (var image in _memoryCache.Values)
                image.Dispose();
            _memoryCache.Clear();
            _accessOrder.Clear();

            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    foreach (var filePath in Directory.GetFiles(_cacheDirectory))
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                }
            }
            catch { }

            _cacheHits = 0;
            _cacheMisses = 0;
        }
    }

    public double GetCacheSizeMB()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return 0;
            long totalBytes = 0;
            foreach (var file in Directory.GetFiles(_cacheDirectory))
                totalBytes += new FileInfo(file).Length;
            return totalBytes / (1024.0 * 1024.0);
        }
        catch { return 0; }
    }
}
