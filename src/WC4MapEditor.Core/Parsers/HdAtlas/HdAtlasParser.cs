using System.Diagnostics;
using System.Xml;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Parsers.HdAtlas;

/// <summary>图集中单个图块的定义（&lt;Image name x y w h refx refy /&gt;）。</summary>
public class HdAtlasImageDef
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefX { get; set; }
    public int RefY { get; set; }

    public override string ToString() => $"{Name} ({X}, {Y}, {Width}x{Height})";
}

/// <summary>
/// 通用 HD 图集解析器：读取 assets 根目录下的 <c>{baseName}.xml</c> 与
/// <c>{baseName}.png/.webp/...</c>，对外提供图块定义与大图字节。
/// 与 <see cref="TacticalMapParser"/> 同一套解析约定，但以图集名参数化，
/// 因此可同时服务于 buildings_hd / image_flags_hd / image_countrytech_hd 等任意图集。
/// 实例按图集名做静态缓存，避免重复读盘。
/// </summary>
public class HdAtlasParser
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, HdAtlasParser> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按图集名（不带扩展名）获取解析器，首次访问时加载。</summary>
    public static HdAtlasParser Get(string baseName)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(baseName, out var cached)) return cached;
            var parser = new HdAtlasParser(baseName);
            parser.Load();
            _cache[baseName] = parser;
            return parser;
        }
    }

    public static void ClearCache()
    {
        lock (_lock) { _cache.Clear(); }
    }

    private static readonly string[] ImageExtensions = { ".png", ".webp", ".jpg", ".jpeg", ".bmp" };

    private readonly string _baseName;
    private readonly List<HdAtlasImageDef> _definitions = new();
    private byte[]? _surfaceData;

    public string BaseName => _baseName;
    public IReadOnlyList<HdAtlasImageDef> ImageDefinitions => _definitions;
    public byte[]? SurfaceData => _surfaceData;
    public bool IsLoaded => _surfaceData != null && _surfaceData.Length > 0;

    private HdAtlasParser(string baseName) => _baseName = baseName;

    private void Load()
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
            {
                try { manager.ScanDefault(); } catch { /* ignore */ }
            }

            var root = manager.AssetsRoot;

            // 1) 读取图集 xml（AssetManager 优先，其次直接读盘）
            string? xmlText = null;
            if (manager.IsLoaded)
            {
                var xmlEntry = manager.Find($"{_baseName}.xml");
                if (xmlEntry != null) xmlText = manager.ReadText(xmlEntry);
            }
            else if (!string.IsNullOrEmpty(root))
            {
                var xmlPath = Path.Combine(root, _baseName + ".xml");
                if (File.Exists(xmlPath)) xmlText = File.ReadAllText(xmlPath);
            }

            if (!string.IsNullOrEmpty(xmlText)) ParseXml(xmlText);

            // 2) 纹理页：优先 <Texture name="xxx.png"/> 声明的名字，否则退回图集名
            var stems = new List<string>();
            if (!string.IsNullOrEmpty(xmlText))
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    xmlText,
                    "<Texture\\b[^>]*\\bname\\s*=\\s*\"([^\"]+)\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var tex = m.Groups[1].Value.Replace('\\', '/');
                    var file = tex.Split('/')[^1];
                    int dot = file.LastIndexOf('.');
                    if (dot > 0) file = file.Substring(0, dot);
                    if (file.Length > 0) stems.Add(file);
                }
            }
            stems.Add(_baseName);

            foreach (var stem in stems)
            {
                if (TryLoadImage(manager, root, stem)) break;
            }

            Debug.WriteLine($"[HdAtlasParser] {_baseName}: {_definitions.Count} 个图块, 图片 {_surfaceData?.Length ?? 0} bytes");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasParser] 加载 {_baseName} 失败: {ex.Message}");
        }
    }

    private bool TryLoadImage(AssetManager manager, string root, string stem)
    {
        if (manager.IsLoaded)
        {
            foreach (var ext in ImageExtensions)
            {
                var entry = manager.Find(stem + ext);
                if (entry != null)
                {
                    _surfaceData = manager.ReadBytes(entry);
                    return true;
                }
            }
        }
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            foreach (var ext in ImageExtensions)
            {
                var path = Path.Combine(root, stem + ext);
                if (File.Exists(path))
                {
                    _surfaceData = File.ReadAllBytes(path);
                    return true;
                }
            }
        }
        return false;
    }

    private void ParseXml(string xmlContent)
    {
        try
        {
            // 图集 xml 没有统一根节点，统一包一层，保持与 TacticalMapParser 相同的选择路径
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml($"<root>{xmlContent}</root>");

            var imageNodes = xmlDoc.SelectNodes("//Images/Image");
            if (imageNodes == null) return;

            foreach (XmlNode node in imageNodes)
            {
                if (node.Attributes == null) continue;

                var def = new HdAtlasImageDef
                {
                    Name = node.Attributes["name"]?.Value ?? "",
                    X = ParseInt(node.Attributes["x"]?.Value),
                    Y = ParseInt(node.Attributes["y"]?.Value),
                    Width = ParseInt(node.Attributes["w"]?.Value),
                    Height = ParseInt(node.Attributes["h"]?.Value),
                    RefX = ParseInt(node.Attributes["refx"]?.Value),
                    RefY = ParseInt(node.Attributes["refy"]?.Value)
                };

                if (!string.IsNullOrEmpty(def.Name) && def.Width > 0 && def.Height > 0)
                    _definitions.Add(def);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasParser] 解析 {_baseName}.xml 失败: {ex.Message}");
        }
    }

    private static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;

    public HdAtlasImageDef? GetImageDef(string name)
    {
        foreach (var def in _definitions)
            if (string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase)) return def;
        return null;
    }

    public bool HasImage(string name) => GetImageDef(name) != null;
}
