using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Rendering.Helpers;

public sealed class CoastHelper
{
    private static CoastHelper? _instance;
    private static readonly object _instanceLock = new();

    private readonly Dictionary<string, SKImage> _coastImageCache = new();
    private readonly List<string> _coastImageAccessOrder = new();
    private const int MaxCoastImageCacheSize = 100;

    private readonly Dictionary<string, SKImage> _hexagonCoastCache = new();
    private readonly List<string> _hexagonCoastAccessOrder = new();
    private const int MaxHexagonCacheSize = 150;

    private Dictionary<string, string> _coastTypes = new();
    private Dictionary<string, int> _coastImageCounts = new();
    private Dictionary<string, string> _coastImages = new();
    private Dictionary<string, string>? _coastMapping;
    private int _coastIdOffset = 10;

    private SKImage? _coastAtlasImage;
    private readonly Dictionary<string, SKRect> _coastAtlasSpriteRects = new();
    private readonly Dictionary<string, SKPoint> _coastAtlasSpriteOrigins = new();
    private bool _coastAtlasLoaded;

    private readonly Dictionary<string, CoastSpriteInfo> _grayLevelCoastImages = new();

    private SKImage? _grayLevelAtlasImage;
    private readonly Dictionary<string, SKRect> _grayLevelAtlasSpriteRects = new();
    private readonly Dictionary<string, SKPoint> _grayLevelAtlasSpriteOrigins = new();
    private bool _grayLevelAtlasLoaded;

    private readonly string _coastFolderPath;
    private readonly string _coastGrayLevelFolderPath;

    public class CoastSpriteInfo
    {
        public string Name { get; set; } = "";
        public int RefX { get; set; }
        public int RefY { get; set; }
    }

    public static CoastHelper Instance
    {
        get
        {
            lock (_instanceLock)
            {
                _instance ??= new CoastHelper();
            }
            return _instance;
        }
    }

    public bool IsAtlasLoaded => _coastAtlasLoaded;
    public bool IsGrayLevelAtlasLoaded => _grayLevelAtlasLoaded;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public CoastHelper()
    {
        ConfigManager.Instance.Initialize();
        _coastFolderPath = ConfigManager.Instance.GetTexturePath("MapCoast");
        _coastGrayLevelFolderPath = ConfigManager.Instance.GetTexturePath("MapCoastGrayLevel");
        Debug.WriteLine($"[CoastHelper] 海岸线纹理路径: {_coastFolderPath}");
        Debug.WriteLine($"[CoastHelper] 灰度级海岸线路径: {_coastGrayLevelFolderPath}");
        LoadCoastConfig();
        LoadGrayLevelCoastConfig();
    }

    private void LoadCoastConfig()
    {
        var configPath = Path.Combine(_coastFolderPath, "CoastManager.json");
        if (!File.Exists(configPath))
        {
            Debug.WriteLine($"[CoastHelper] 警告: 配置文件不存在: {configPath}");
            return;
        }

        try
        {
            var jsonContent = File.ReadAllText(configPath);
            var config = JsonDocument.Parse(jsonContent);

            if (config.RootElement.TryGetProperty("coast_types", out var typesEl))
            {
                _coastTypes = new Dictionary<string, string>();
                foreach (var prop in typesEl.EnumerateObject())
                    _coastTypes[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (config.RootElement.TryGetProperty("coast_image_counts", out var countsEl))
            {
                _coastImageCounts = new Dictionary<string, int>();
                foreach (var prop in countsEl.EnumerateObject())
                    _coastImageCounts[prop.Name] = prop.Value.GetInt32();
            }

            if (config.RootElement.TryGetProperty("coast_images", out var imagesEl))
            {
                _coastImages = new Dictionary<string, string>();
                foreach (var prop in imagesEl.EnumerateObject())
                    _coastImages[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (config.RootElement.TryGetProperty("coast_mapping", out var mappingEl))
            {
                _coastMapping = new Dictionary<string, string>();
                foreach (var prop in mappingEl.EnumerateObject())
                    _coastMapping[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (config.RootElement.TryGetProperty("coast_id_offset", out var offsetEl))
                _coastIdOffset = offsetEl.GetInt32();

            if (config.RootElement.TryGetProperty("coast_atlas", out var atlasEl))
            {
                string? atlasImageName = null, atlasConfigName = null;
                if (atlasEl.TryGetProperty("image", out var imgEl)) atlasImageName = imgEl.GetString();
                if (atlasEl.TryGetProperty("config", out var cfgEl)) atlasConfigName = cfgEl.GetString();
                if (!string.IsNullOrEmpty(atlasImageName) && !string.IsNullOrEmpty(atlasConfigName))
                    LoadAtlasConfig(atlasImageName, atlasConfigName);
            }

            Debug.WriteLine($"[CoastHelper] 成功加载海岸线配置: {_coastTypes.Count} 种类型, {_coastImages.Count} 张图片, 图集={_coastAtlasLoaded}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 加载海岸线配置失败: {ex.Message}");
        }
    }

    private void LoadAtlasConfig(string atlasImageName, string atlasConfigName)
    {
        var atlasImagePath = Path.Combine(_coastFolderPath, atlasImageName);
        var atlasConfigPath = Path.Combine(_coastFolderPath, atlasConfigName);

        if (!File.Exists(atlasImagePath))
        {
            Debug.WriteLine($"[CoastHelper] 警告: 图集图片不存在: {atlasImagePath}");
            return;
        }

        try
        {
            using var stream = File.OpenRead(atlasImagePath);
            _coastAtlasImage = SKImage.FromEncodedData(stream);
            if (_coastAtlasImage == null) return;
            Debug.WriteLine($"[CoastHelper] 图集图片已加载: {atlasImagePath} ({_coastAtlasImage.Width}x{_coastAtlasImage.Height})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 加载图集图片失败: {ex.Message}");
            return;
        }

        if (!File.Exists(atlasConfigPath))
        {
            Debug.WriteLine($"[CoastHelper] 警告: 图集配置文件不存在: {atlasConfigPath}");
            return;
        }

        try
        {
            var rawXml = File.ReadAllText(atlasConfigPath);
            rawXml = "<root>" + rawXml + "</root>";
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);

            var imageNodes = xmlDoc.SelectNodes("//Image");
            if (imageNodes != null)
            {
                foreach (XmlNode node in imageNodes)
                {
                    if (node.Attributes == null) continue;
                    var nameAttr = node.Attributes["name"];
                    var xAttr = node.Attributes["x"];
                    var yAttr = node.Attributes["y"];
                    var wAttr = node.Attributes["w"];
                    var hAttr = node.Attributes["h"];
                    var refxAttr = node.Attributes["refx"];
                    var refyAttr = node.Attributes["refy"];

                    if (nameAttr != null && xAttr != null && yAttr != null && wAttr != null && hAttr != null)
                    {
                        var spriteName = nameAttr.Value;
                        var x = int.Parse(xAttr.Value);
                        var y = int.Parse(yAttr.Value);
                        var w = int.Parse(wAttr.Value);
                        var h = int.Parse(hAttr.Value);
                        var refx = refxAttr != null ? float.Parse(refxAttr.Value) : w / 2f;
                        var refy = refyAttr != null ? float.Parse(refyAttr.Value) : h / 2f;

                        _coastAtlasSpriteRects[spriteName] = new SKRect(x, y, x + w, y + h);
                        _coastAtlasSpriteOrigins[spriteName] = new SKPoint(refx, refy);
                    }
                }
            }

            _coastAtlasLoaded = true;
            Debug.WriteLine($"[CoastHelper] 图集XML已解析: {_coastAtlasSpriteRects.Count} 个精灵");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 解析图集XML失败: {ex.Message}");
        }
    }

    private void LoadGrayLevelCoastConfig()
    {
        var xmlPath = Path.Combine(_coastGrayLevelFolderPath, "pm_map.xml");
        if (File.Exists(xmlPath))
        {
            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);
                var spriteNodes = xmlDoc.SelectNodes("//sprite");
                if (spriteNodes != null)
                {
                    foreach (XmlNode node in spriteNodes)
                    {
                        if (node.Attributes == null) continue;
                        var nameAttr = node.Attributes["n"];
                        var refXAttr = node.Attributes["refx"];
                        var refYAttr = node.Attributes["refy"];
                        if (nameAttr != null)
                        {
                            _grayLevelCoastImages[nameAttr.Value] = new CoastSpriteInfo
                            {
                                Name = nameAttr.Value,
                                RefX = refXAttr != null ? int.Parse(refXAttr.Value) : 0,
                                RefY = refYAttr != null ? int.Parse(refYAttr.Value) : 0
                            };
                        }
                    }
                }
                Debug.WriteLine($"[CoastHelper] 成功加载灰度级海岸线配置: {_grayLevelCoastImages.Count} 个精灵");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastHelper] 加载灰度级海岸线配置失败: {ex.Message}");
            }
        }

        LoadGrayLevelAtlasConfig();
    }

    private void LoadGrayLevelAtlasConfig()
    {
        var atlasImagePath = Path.Combine(_coastGrayLevelFolderPath, "coastmask_hd.webp");
        var atlasConfigPath = Path.Combine(_coastGrayLevelFolderPath, "coastmask_hd.xml");

        if (!File.Exists(atlasImagePath)) return;

        try
        {
            using var stream = File.OpenRead(atlasImagePath);
            _grayLevelAtlasImage = SKImage.FromEncodedData(stream);
            if (_grayLevelAtlasImage == null) return;
            Debug.WriteLine($"[CoastHelper] 灰度图图集已加载: ({_grayLevelAtlasImage.Width}x{_grayLevelAtlasImage.Height})");
        }
        catch { return; }

        if (!File.Exists(atlasConfigPath)) return;

        try
        {
            var rawXml = File.ReadAllText(atlasConfigPath);
            rawXml = "<root>" + rawXml + "</root>";
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);

            var imageNodes = xmlDoc.SelectNodes("//Image");
            if (imageNodes != null)
            {
                foreach (XmlNode node in imageNodes)
                {
                    if (node.Attributes == null) continue;
                    var nameAttr = node.Attributes["name"];
                    var xAttr = node.Attributes["x"];
                    var yAttr = node.Attributes["y"];
                    var wAttr = node.Attributes["w"];
                    var hAttr = node.Attributes["h"];
                    var refxAttr = node.Attributes["refx"];
                    var refyAttr = node.Attributes["refy"];

                    if (nameAttr != null && xAttr != null && yAttr != null && wAttr != null && hAttr != null)
                    {
                        var spriteName = nameAttr.Value;
                        var x = int.Parse(xAttr.Value);
                        var y = int.Parse(yAttr.Value);
                        var w = int.Parse(wAttr.Value);
                        var h = int.Parse(hAttr.Value);
                        var refx = refxAttr != null ? float.Parse(refxAttr.Value) : w / 2f;
                        var refy = refyAttr != null ? float.Parse(refyAttr.Value) : h / 2f;

                        _grayLevelAtlasSpriteRects[spriteName] = new SKRect(x, y, x + w, y + h);
                        _grayLevelAtlasSpriteOrigins[spriteName] = new SKPoint(refx, refy);
                    }
                }
            }

            _grayLevelAtlasLoaded = true;
            Debug.WriteLine($"[CoastHelper] 灰度图图集XML已解析: {_grayLevelAtlasSpriteRects.Count} 个精灵");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 解析灰度图图集XML失败: {ex.Message}");
        }
    }

    public SKImage? GetCoastImageByDoodad(int doodad1Group, int doodad1Id)
    {
        if (doodad1Group != 31) return null;
        var mappingKey = $"{doodad1Group}_{doodad1Id}";
        if (_coastMapping == null || !_coastMapping.TryGetValue(mappingKey, out var imageFileName))
            return null;

        string? imageKey = null;
        foreach (var kvp in _coastImages)
        {
            if (kvp.Value == imageFileName) { imageKey = kvp.Key; break; }
        }
        if (imageKey == null) return null;
        return LoadCoastImage(imageKey);
    }

    public string? GetCoastSpriteName(int doodad1Group, int doodad1Id)
    {
        var mappingKey = $"{doodad1Group}_{doodad1Id}";
        if (_coastMapping != null && _coastMapping.TryGetValue(mappingKey, out var spriteName))
            return spriteName;
        return null;
    }

    private SKImage? LoadCoastImage(string imageKey)
    {
        lock (_coastImageCache)
        {
            if (_coastImageCache.TryGetValue(imageKey, out var cached))
            {
                _coastImageAccessOrder.Remove(imageKey);
                _coastImageAccessOrder.Add(imageKey);
                return cached;
            }
        }

        if (!_coastImages.TryGetValue(imageKey, out var spriteName))
            return null;

        if (_coastAtlasLoaded && _coastAtlasSpriteRects.ContainsKey(spriteName))
            return ExtractFromAtlas(imageKey, spriteName);

        return LoadCoastImageFromFile(imageKey, spriteName);
    }

    private SKImage? ExtractFromAtlas(string imageKey, string spriteName)
    {
        try
        {
            var srcRect = _coastAtlasSpriteRects[spriteName];
            var imgInfo = new SKImageInfo((int)srcRect.Width, (int)srcRect.Height);
            using var surface = SKSurface.Create(imgInfo);
            surface.Canvas.DrawImage(_coastAtlasImage!, srcRect, new SKRect(0, 0, srcRect.Width, srcRect.Height));
            var subImage = surface.Snapshot();

            if (subImage != null)
            {
                lock (_coastImageCache)
                {
                    _coastImageCache[imageKey] = subImage;
                    _coastImageAccessOrder.Add(imageKey);
                    while (_coastImageCache.Count > MaxCoastImageCacheSize)
                    {
                        var oldestKey = _coastImageAccessOrder[0];
                        _coastImageAccessOrder.RemoveAt(0);
                        if (_coastImageCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return subImage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 从图集提取子图失败 [{spriteName}]: {ex.Message}");
            return null;
        }
    }

    private SKImage? LoadCoastImageFromFile(string imageKey, string fileName)
    {
        var filePath = Path.Combine(_coastFolderPath, fileName);
        if (!File.Exists(filePath)) return null;

        try
        {
            using var stream = File.OpenRead(filePath);
            var image = SKImage.FromEncodedData(stream);
            if (image != null)
            {
                lock (_coastImageCache)
                {
                    _coastImageCache[imageKey] = image;
                    _coastImageAccessOrder.Add(imageKey);
                    while (_coastImageCache.Count > MaxCoastImageCacheSize)
                    {
                        var oldestKey = _coastImageAccessOrder[0];
                        _coastImageAccessOrder.RemoveAt(0);
                        if (_coastImageCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 加载图片失败: {filePath} - {ex.Message}");
            return null;
        }
    }

    public SKImage? GetGrayLevelCoastImage(string spriteName)
    {
        lock (_coastImageCache)
        {
            if (_coastImageCache.TryGetValue(spriteName, out var cached))
            {
                _coastImageAccessOrder.Remove(spriteName);
                _coastImageAccessOrder.Add(spriteName);
                return cached;
            }
        }

        if (_grayLevelAtlasLoaded && _grayLevelAtlasSpriteRects.ContainsKey(spriteName))
            return ExtractFromGrayLevelAtlas(spriteName);

        var filePath = Path.Combine(_coastGrayLevelFolderPath, spriteName);
        if (!File.Exists(filePath)) return null;

        try
        {
            using var stream = File.OpenRead(filePath);
            var image = SKImage.FromEncodedData(stream);
            if (image != null)
            {
                lock (_coastImageCache)
                {
                    _coastImageCache[spriteName] = image;
                    _coastImageAccessOrder.Add(spriteName);
                    while (_coastImageCache.Count > MaxCoastImageCacheSize)
                    {
                        var oldestKey = _coastImageAccessOrder[0];
                        _coastImageAccessOrder.RemoveAt(0);
                        if (_coastImageCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return image;
        }
        catch { return null; }
    }

    private SKImage? ExtractFromGrayLevelAtlas(string spriteName)
    {
        try
        {
            var srcRect = _grayLevelAtlasSpriteRects[spriteName];
            var imgInfo = new SKImageInfo((int)srcRect.Width, (int)srcRect.Height);
            using var surface = SKSurface.Create(imgInfo);
            surface.Canvas.DrawImage(_grayLevelAtlasImage!, srcRect, new SKRect(0, 0, srcRect.Width, srcRect.Height));
            var subImage = surface.Snapshot();

            if (subImage != null)
            {
                lock (_coastImageCache)
                {
                    _coastImageCache[spriteName] = subImage;
                    _coastImageAccessOrder.Add(spriteName);
                    while (_coastImageCache.Count > MaxCoastImageCacheSize)
                    {
                        var oldestKey = _coastImageAccessOrder[0];
                        _coastImageAccessOrder.RemoveAt(0);
                        if (_coastImageCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return subImage;
        }
        catch { return null; }
    }

    public CoastSpriteInfo? GetGrayLevelCoastSpriteInfo(string spriteName)
    {
        return _grayLevelCoastImages.TryGetValue(spriteName, out var info) ? info : null;
    }

    public List<string> GetGrayLevelSpriteNames()
    {
        if (_grayLevelAtlasLoaded)
            return _grayLevelAtlasSpriteRects.Keys.ToList();
        return _grayLevelCoastImages.Keys.ToList();
    }

    public SKImage? GetCoastAtlasImage() => _coastAtlasImage;

    public SKRect? GetCoastAtlasSpriteRect(string spriteName)
    {
        return _coastAtlasSpriteRects.TryGetValue(spriteName, out var rect) ? rect : null;
    }

    public SKPoint? GetCoastAtlasSpriteOrigin(string spriteName)
    {
        return _coastAtlasSpriteOrigins.TryGetValue(spriteName, out var origin) ? origin : null;
    }

    public SKImage? GetHexagonCoastImage(string spriteName)
    {
        lock (_hexagonCoastCache)
        {
            if (_hexagonCoastCache.TryGetValue(spriteName, out var cached))
            {
                _hexagonCoastAccessOrder.Remove(spriteName);
                _hexagonCoastAccessOrder.Add(spriteName);
                return cached;
            }
        }
        return ExtractHexagonFromAtlas(spriteName);
    }

    private SKImage? ExtractHexagonFromAtlas(string spriteName)
    {
        if (!_coastAtlasLoaded || !_coastAtlasSpriteRects.ContainsKey(spriteName)) return null;

        try
        {
            var srcRect = _coastAtlasSpriteRects[spriteName];
            var width = (int)srcRect.Width;
            var height = (int)srcRect.Height;

            float hexSide = width / 2f;
            float hexHeight = hexSide * (float)Math.Sqrt(3);
            if (hexHeight > height)
            {
                hexHeight = height;
                hexSide = hexHeight / (float)Math.Sqrt(3);
            }

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

            var imgInfo = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(imgInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.ClipPath(hexPath);
            canvas.DrawImage(_coastAtlasImage!, srcRect, new SKRect(0, 0, width, height));
            var hexImage = surface.Snapshot();

            if (hexImage != null)
            {
                lock (_hexagonCoastCache)
                {
                    _hexagonCoastCache[spriteName] = hexImage;
                    _hexagonCoastAccessOrder.Add(spriteName);
                    while (_hexagonCoastCache.Count > MaxHexagonCacheSize)
                    {
                        var oldestKey = _hexagonCoastAccessOrder[0];
                        _hexagonCoastAccessOrder.RemoveAt(0);
                        if (_hexagonCoastCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return hexImage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 裁剪六边形图块失败 [{spriteName}]: {ex.Message}");
            return null;
        }
    }

    public void PreGenerateAllHexagonCoasts(Action<int, string>? progressCallback)
    {
        if (!_coastAtlasLoaded)
        {
            progressCallback?.Invoke(100, "图集未加载，跳过六边形裁剪");
            return;
        }

        var spriteNames = _coastAtlasSpriteRects.Keys.ToList();
        var totalCount = spriteNames.Count;
        Debug.WriteLine($"[CoastHelper] 开始预生成六边形图块，共 {totalCount} 个...");

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"裁剪海岸线图块... ({i + 1}/{totalCount})");
                GetHexagonCoastImage(spriteName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastHelper] 预生成六边形图块失败 [{spriteName}]: {ex.Message}");
            }
        }

        progressCallback?.Invoke(100, $"海岸线图块裁剪完成 ({totalCount})");
    }

    public void ClearCache()
    {
        lock (_coastImageCache)
        {
            foreach (var image in _coastImageCache.Values)
                image?.Dispose();
            _coastImageCache.Clear();
            _coastImageAccessOrder.Clear();
        }
    }
}
