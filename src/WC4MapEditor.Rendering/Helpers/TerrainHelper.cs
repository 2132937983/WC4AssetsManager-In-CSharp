using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Rendering.Helpers;

public class TerrainHelper
{
    private readonly Dictionary<string, SKImage> _skImageCache = new();
    private readonly List<string> _skImageAccessOrder = new();
    private const int MaxSkImageCacheSize = 30;

    private readonly TextureDiskCache _diskCache = TextureDiskCache.Instance;
    private readonly bool _useDiskCache = true;

    private Dictionary<string, string> _terrainTypes = new();
    private Dictionary<string, string> _terrainMapping = new();
    private Dictionary<string, int> _terrainImageCounts = new();
    private Dictionary<string, string> _terrainImages = new();

    private readonly string _textureFolderPath;

    public TerrainHelper(string textureFolderName)
    {
        ConfigManager.Instance.Initialize();
        _textureFolderPath = ConfigManager.Instance.GetTexturePath(textureFolderName);
        Debug.WriteLine($"[TerrainHelper] 纹理文件夹路径: {_textureFolderPath}");
        LoadTerrainConfig();
    }

    private void LoadTerrainConfig()
    {
        var configPath = Path.Combine(_textureFolderPath, "manager.json");
        if (!File.Exists(configPath))
        {
            Debug.WriteLine($"[TerrainHelper] 配置文件不存在: {configPath}");
            return;
        }

        try
        {
            var jsonContent = File.ReadAllText(configPath);
            var config = JsonDocument.Parse(jsonContent);

            if (config.RootElement.TryGetProperty("terrain_types", out var typesEl))
            {
                _terrainTypes = new Dictionary<string, string>();
                foreach (var prop in typesEl.EnumerateObject())
                    _terrainTypes[prop.Name] = prop.Value.GetString() ?? "";
                Debug.WriteLine($"[TerrainHelper] 加载了 {_terrainTypes.Count} 个地形类型");
            }

            if (config.RootElement.TryGetProperty("terrain_mapping", out var mappingEl))
            {
                _terrainMapping = new Dictionary<string, string>();
                foreach (var prop in mappingEl.EnumerateObject())
                    _terrainMapping[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (config.RootElement.TryGetProperty("terrain_image_counts", out var countsEl))
            {
                _terrainImageCounts = new Dictionary<string, int>();
                foreach (var prop in countsEl.EnumerateObject())
                    _terrainImageCounts[prop.Name] = prop.Value.GetInt32();
            }

            if (config.RootElement.TryGetProperty("terrain_images", out var imagesEl))
            {
                _terrainImages = new Dictionary<string, string>();
                foreach (var prop in imagesEl.EnumerateObject())
                    _terrainImages[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerrainHelper] 加载配置失败: {ex.Message}");
        }
    }

    public SKImage? GetTerrainSkImage(int terrainId, int decorationType = 0)
    {
        var terrainTypeName = GetTerrainTypeName(terrainId);
        if (string.IsNullOrEmpty(terrainTypeName))
            return GetDefaultSkImage();

        var imageKey = GetTerrainImageKey(terrainTypeName, decorationType);

        if (_skImageCache.TryGetValue(imageKey, out var cached))
        {
            _skImageAccessOrder.Remove(imageKey);
            _skImageAccessOrder.Add(imageKey);
            return cached;
        }

        if (_useDiskCache)
        {
            var diskImage = _diskCache.GetTexture(imageKey);
            if (diskImage != null)
            {
                AddToSkImageCache(imageKey, diskImage);
                return diskImage;
            }
        }

        if (!_terrainImages.TryGetValue(imageKey, out var imageFileName) || string.IsNullOrEmpty(imageFileName))
            return GetDefaultSkImage();

        var imagePath = Path.Combine(_textureFolderPath, imageFileName);
        if (!File.Exists(imagePath))
            return GetDefaultSkImage();

        SKImage? skImage = null;
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap != null)
                skImage = SKImage.FromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerrainHelper] 加载纹理失败: {imagePath} - {ex.Message}");
            return GetDefaultSkImage();
        }

        if (skImage != null)
        {
            if (_useDiskCache)
                _diskCache.SaveTexture(imageKey, skImage);
            AddToSkImageCache(imageKey, skImage);
        }

        return skImage;
    }

    private void AddToSkImageCache(string imageKey, SKImage skImage)
    {
        if (_skImageCache.Count >= MaxSkImageCacheSize)
        {
            var oldestKey = _skImageAccessOrder[0];
            _skImageAccessOrder.RemoveAt(0);
            _skImageCache.Remove(oldestKey, out _);
        }

        _skImageCache[imageKey] = skImage;
        _skImageAccessOrder.Add(imageKey);
    }

    public string? GetTerrainTypeName(int terrainId)
    {
        var terrainIdStr = terrainId.ToString();
        return _terrainTypes.TryGetValue(terrainIdStr, out var name) ? name : null;
    }

    public int GetTerrainVariantCount(int terrainId)
    {
        var terrainTypeName = GetTerrainTypeName(terrainId);
        if (string.IsNullOrEmpty(terrainTypeName)) return 16;

        if (_terrainMapping.TryGetValue(terrainTypeName, out var mappedKey))
        {
            if (_terrainImageCounts.TryGetValue(mappedKey, out var count))
                return count;
        }

        return 16;
    }

    private string GetTerrainImageKey(string terrainTypeName, int decorationType)
    {
        if (_terrainMapping.TryGetValue(terrainTypeName, out var mappedKey))
        {
            if (_terrainImageCounts.TryGetValue(mappedKey, out var imageCount) && imageCount > 1)
            {
                var variantIndex = (decorationType % imageCount) + 1;
                return $"{mappedKey}_{variantIndex}";
            }
            return mappedKey;
        }
        return terrainTypeName;
    }

    public Dictionary<int, string> GetAllTerrainTypes()
    {
        var result = new Dictionary<int, string>();
        foreach (var kvp in _terrainTypes)
        {
            if (int.TryParse(kvp.Key, out int id))
                result[id] = kvp.Value;
        }
        return result;
    }

    public void ClearAllCaches()
    {
        var count = _skImageCache.Count;
        _skImageCache.Clear();
        _skImageAccessOrder.Clear();
        Debug.WriteLine($"[TerrainHelper] 内存缓存已清空，释放了 {count} 个纹理引用");
    }

    private static SKImage GetDefaultSkImage()
    {
        try
        {
            using var bitmap = new SKBitmap(64, 64);
            bitmap.Erase(new SKColor(128, 128, 128));
            return SKImage.FromBitmap(bitmap);
        }
        catch { return null!; }
    }
}