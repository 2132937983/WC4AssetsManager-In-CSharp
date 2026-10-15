using System.Diagnostics;
using System.Xml;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Parsers;

public class TacticalMapParser : IDisposable
{
    private byte[]? _tacticalMapSurfaceData;
    private List<TacticalMapImageDef> _imageDefinitions = new();

    private static List<TacticalMapImageDef>? _cachedDefinitions;
    private static byte[]? _cachedSurfaceData;
    private static bool _isParsed;
    private static readonly object _parseLock = new();

    public IReadOnlyList<TacticalMapImageDef> ImageDefinitions => _imageDefinitions;
    public byte[]? SurfaceData => _tacticalMapSurfaceData;
    public static bool IsParsed => _isParsed;

    public TacticalMapParser()
    {
    }

    public void EnsureLoaded(string resourcePath)
    {
        lock (_parseLock)
        {
            if (_isParsed)
            {
                if (_cachedDefinitions != null)
                {
                    _imageDefinitions = new List<TacticalMapImageDef>(_cachedDefinitions);
                }
                _tacticalMapSurfaceData = _cachedSurfaceData;
                return;
            }

            ParseTacticalMapFromAssets();
            if (_tacticalMapSurfaceData == null || _tacticalMapSurfaceData.Length == 0)
            {
                ParseTacticalMap(resourcePath);
            }

            _isParsed = true;
            _cachedDefinitions = new List<TacticalMapImageDef>(_imageDefinitions);
            _cachedSurfaceData = _tacticalMapSurfaceData;
        }
    }

    public void EnsureLoadedFromAssets()
    {
        lock (_parseLock)
        {
            if (_isParsed)
            {
                if (_cachedDefinitions != null)
                {
                    _imageDefinitions = new List<TacticalMapImageDef>(_cachedDefinitions);
                }
                _tacticalMapSurfaceData = _cachedSurfaceData;
                return;
            }

            ParseTacticalMapFromAssets();

            _isParsed = true;
            _cachedDefinitions = new List<TacticalMapImageDef>(_imageDefinitions);
            _cachedSurfaceData = _tacticalMapSurfaceData;
        }
    }

    private void ParseTacticalMapFromAssets()
    {
        try
        {
            var am = AssetManager.Default;
            if (!am.IsLoaded)
            {
                Debug.WriteLine("[TacticalMapParser] AssetManager 未加载，跳过从 assets 加载战术地图");
                return;
            }

            string[] supportedExtensions = { ".webp", ".png", ".jpg", ".jpeg", ".bmp" };
            AssetEntry? imageEntry = null;
            foreach (var ext in supportedExtensions)
            {
                var entry = am.Find($"tacticalmap{ext}");
                if (entry != null)
                {
                    imageEntry = entry;
                    break;
                }
            }

            var xmlEntry = am.Find("tacticalmap.xml");

            if (imageEntry == null)
            {
                Debug.WriteLine("[TacticalMapParser] assets 中未找到战术地图图片，支持的格式: webp, png, jpg, jpeg, bmp");
                return;
            }

            if (xmlEntry == null)
            {
                Debug.WriteLine("[TacticalMapParser] assets 中未找到 tacticalmap.xml");
                return;
            }

            Debug.WriteLine($"[TacticalMapParser] 从 AssetManager 加载战术地图图片: {imageEntry.RelativePath}");

            ParseXmlConfigFromText(am.ReadText(xmlEntry));
            LoadImageFromBytes(am.ReadBytes(imageEntry));

            Debug.WriteLine($"[TacticalMapParser] 成功解析 {_imageDefinitions.Count} 个图片定义 (from AssetManager)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 从 AssetManager 解析战术地图失败: {ex.Message}");
        }
    }

    private void ParseTacticalMap(string resourcePath)
    {
        try
        {
            string dataPath = Path.Combine(resourcePath, "Data");
            if (!Directory.Exists(dataPath))
                dataPath = Path.Combine(resourcePath, "data");

            string[] supportedExtensions = { ".webp", ".png", ".jpg", ".jpeg", ".bmp" };
            string? imagePath = null;

            foreach (var ext in supportedExtensions)
            {
                string tempPath = Path.Combine(dataPath, $"tacticalmap{ext}");
                if (File.Exists(tempPath))
                {
                    imagePath = tempPath;
                    break;
                }
            }

            string xmlPath = Path.Combine(dataPath, "tacticalmap.xml");

            if (string.IsNullOrEmpty(imagePath))
            {
                Debug.WriteLine("[TacticalMapParser] 战术地图文件不存在，支持的格式: webp, png, jpg, jpeg, bmp");
                return;
            }

            if (!File.Exists(xmlPath))
            {
                Debug.WriteLine($"[TacticalMapParser] 战术地图配置文件不存在: {xmlPath}");
                return;
            }

            Debug.WriteLine($"[TacticalMapParser] 加载战术地图图片: {Path.GetFileName(imagePath)}");

            ParseXmlConfig(xmlPath);
            LoadImage(imagePath);

            Debug.WriteLine($"[TacticalMapParser] 成功解析 {_imageDefinitions.Count} 个图片定义");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 解析战术地图失败: {ex.Message}");
        }
    }

    private void ParseXmlConfig(string xmlPath)
    {
        try
        {
            string xmlContent = File.ReadAllText(xmlPath);
            ParseXmlConfigFromText(xmlContent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 解析 XML 配置失败: {ex.Message}");
        }
    }

    private void ParseXmlConfigFromText(string xmlContent)
    {
        try
        {
            string tempXml = $"<root>{xmlContent}</root>";

            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(tempXml);

            XmlNodeList? imageNodes = xmlDoc.SelectNodes("//Images/Image");
            if (imageNodes == null) return;

            foreach (XmlNode imageNode in imageNodes)
            {
                if (imageNode.Attributes == null) continue;

                var def = new TacticalMapImageDef();

                var nameAttr = imageNode.Attributes["name"];
                if (nameAttr != null) def.Name = nameAttr.Value;

                var xAttr = imageNode.Attributes["x"];
                if (xAttr != null)
                {
                    int.TryParse(xAttr.Value, out int x);
                    def.X = x;
                }

                var yAttr = imageNode.Attributes["y"];
                if (yAttr != null)
                {
                    int.TryParse(yAttr.Value, out int y);
                    def.Y = y;
                }

                var wAttr = imageNode.Attributes["w"];
                if (wAttr != null)
                {
                    int.TryParse(wAttr.Value, out int w);
                    def.Width = w;
                }

                var hAttr = imageNode.Attributes["h"];
                if (hAttr != null)
                {
                    int.TryParse(hAttr.Value, out int h);
                    def.Height = h;
                }

                var refxAttr = imageNode.Attributes["refx"];
                if (refxAttr != null)
                {
                    int.TryParse(refxAttr.Value, out int refx);
                    def.RefX = refx;
                }

                var refyAttr = imageNode.Attributes["refy"];
                if (refyAttr != null)
                {
                    int.TryParse(refyAttr.Value, out int refy);
                    def.RefY = refy;
                }

                if (!string.IsNullOrEmpty(def.Name) && def.Width > 0 && def.Height > 0)
                {
                    _imageDefinitions.Add(def);
                }
            }

            Debug.WriteLine($"[TacticalMapParser] 加载了 {_imageDefinitions.Count} 个图片定义");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 解析 XML 配置失败: {ex.Message}");
        }
    }

    private void LoadImage(string imagePath)
    {
        try
        {
            _tacticalMapSurfaceData = File.ReadAllBytes(imagePath);
            Debug.WriteLine($"[TacticalMapParser] 加载战术地图: {_tacticalMapSurfaceData.Length} bytes");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 加载图片失败: {ex.Message}");
        }
    }

    private void LoadImageFromBytes(byte[] data)
    {
        try
        {
            _tacticalMapSurfaceData = data;
            Debug.WriteLine($"[TacticalMapParser] 加载战术地图: {_tacticalMapSurfaceData.Length} bytes (from AssetManager)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 加载图片失败: {ex.Message}");
        }
    }

    public TacticalMapImageDef? GetImageDef(string name)
    {
        foreach (var def in _imageDefinitions)
        {
            if (def.Name == name)
                return def;
        }
        return null;
    }

    public bool HasImage(string name)
    {
        foreach (var def in _imageDefinitions)
        {
            if (def.Name == name)
                return true;
        }
        return false;
    }

    public List<string> GetAllImageNames()
    {
        var results = new List<string>();
        foreach (var def in _imageDefinitions)
        {
            results.Add(def.Name);
        }
        return results;
    }

    public List<string> SearchImages(string keyword)
    {
        var results = new List<string>();
        string lowerKeyword = keyword.ToLower();

        foreach (var def in _imageDefinitions)
        {
            if (def.Name.ToLower().Contains(lowerKeyword))
                results.Add(def.Name);
        }

        return results;
    }

    public List<TacticalMapImageDef> GetGeneralHeadImages()
    {
        var results = new List<TacticalMapImageDef>();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name.StartsWith("head_"))
                results.Add(def);
        }
        return results;
    }

    public List<TacticalMapImageDef> GetCountryFlagImages()
    {
        var results = new List<TacticalMapImageDef>();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name.StartsWith("flag_") || (def.Name.EndsWith(".png") && !def.Name.StartsWith("head_")))
                results.Add(def);
        }
        return results;
    }

    public static void ClearCache()
    {
        lock (_parseLock)
        {
            _isParsed = false;
            _cachedDefinitions = null;
            _cachedSurfaceData = null;
        }
    }

    public void Dispose()
    {
        _imageDefinitions.Clear();
        _tacticalMapSurfaceData = null;
    }
}