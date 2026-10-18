using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>布局中一个图片引用的解析结果：命中的图集页（或散图）与源矩形。</summary>
internal sealed class LayoutImageRec
{
    public SKBitmap? Page { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>来源图集名（散图为 null）。</summary>
    public string? Atlas { get; init; }

    public bool Ready => Page != null && W > 0 && H > 0;
    public SKRect SrcRect => new(X, Y, X + W, Y + H);
}

/// <summary>
/// 布局编辑器的图片来源，对齐游戏的加载顺序：
/// <list type="number">
/// <item><c>image_resource.xml</c> 里列出的 UI 图集（它们放在 <c>assets/image/</c> 下，旧版只扫根目录所以几乎全部缺图）；</item>
/// <item>根目录下的其它图集（anim_* / buildings_hd / terrain …）；</item>
/// <item><c>image/</c> 下尚未列入的图集；</item>
/// <item>散图：<c>texture="image/xxx.png"</c> 这类路径，自动尝试 .png/.webp/.jpg（游戏里不少 png 实际是 webp）。</item>
/// </list>
/// 同名图片以先登记者为准，与游戏按资源表顺序加载的行为一致。用户额外导入的文件夹 / 文件优先级最高。
/// </summary>
internal static class LayoutImageProvider
{
    private sealed record AtlasHit(string Atlas, int X, int Y, int W, int H);

    private static Dictionary<string, AtlasHit>? _index;
    private static readonly List<string> _atlasNames = new();
    private static readonly Dictionary<string, SKBitmap?> _pages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LayoutImageRec?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();

    // ---------- 额外资源（用户导入的文件夹 / 文件） ----------
    private static readonly List<string> _extraRoots = new();
    private static readonly Dictionary<string, (SKBitmap Page, int X, int Y, int W, int H, string Atlas)> _extraAtlas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SKBitmap> _extraLoose = new(StringComparer.OrdinalIgnoreCase);
    private static int _extraAtlasCount, _extraImageCount;

    private static readonly string[] ImageExtensions = { ".png", ".webp", ".jpg", ".jpeg", ".bmp" };

    /// <summary>图集名列表（含 image/ 前缀的相对路径，不带扩展名），按加载优先级排序。</summary>
    public static IReadOnlyList<string> AtlasNames { get { EnsureIndex(); return _atlasNames; } }

    /// <summary>索引里的全部图片名（给图片选择器）。</summary>
    public static IEnumerable<string> ImageNames { get { var idx = EnsureIndex(); return idx.Keys.Concat(_extraAtlas.Keys).Concat(_extraLoose.Keys).Distinct(StringComparer.OrdinalIgnoreCase); } }

    public static int ImageCount { get { return EnsureIndex().Count; } }
    public static int ExtraAtlasCount => _extraAtlasCount;
    public static int ExtraImageCount => _extraImageCount;

    /// <summary>图集的显示名（去掉目录与扩展名）。</summary>
    public static string AtlasDisplayName(string atlas) => atlas.Replace('\\', '/').Split('/')[^1];

    // ============================================================= 索引 =============================================================

    private static Dictionary<string, AtlasHit> EnsureIndex()
    {
        lock (_gate)
        {
            if (_index != null) return _index;

            var index = new Dictionary<string, AtlasHit>(StringComparer.OrdinalIgnoreCase);
            _atlasNames.Clear();
            try
            {
                var manager = AssetManager.Default;
                if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }

                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1) image_resource.xml 的 HD 列表（权威顺序）
                var res = manager.Find("image_resource.xml");
                if (res != null)
                {
                    try
                    {
                        string text = manager.ReadText(res);
                        foreach (Match m in Regex.Matches(text, "<file\\b[^>]*\\bname\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                        {
                            string file = m.Groups[1].Value;
                            string stem = file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
                            string? baseName = manager.Find("image/" + stem + ".xml") != null ? "image/" + stem
                                             : manager.Find(stem + ".xml") != null ? stem : null;
                            if (baseName != null && seen.Add(baseName)) ordered.Add(baseName);
                        }
                    }
                    catch { /* 列表坏了就只靠扫描 */ }
                }

                // 2) 根目录与 image/ 下所有图集 xml（含 <Texture）
                foreach (var entry in manager.ListByExtension(".xml"))
                {
                    var rel = entry.RelativePath.Replace('\\', '/');
                    int slash = rel.IndexOf('/');
                    bool rootLevel = slash < 0;
                    bool imageDir = rel.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && rel.IndexOf('/', 6) < 0;
                    if (!rootLevel && !imageDir) continue;

                    var baseName = rel[..^4];
                    if (seen.Contains(baseName)) continue;

                    string text;
                    try { text = manager.ReadText(entry); } catch { continue; }
                    if (!text.Contains("<Texture", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!text.Contains("<Image", StringComparison.OrdinalIgnoreCase)) continue;
                    seen.Add(baseName);
                    ordered.Add(baseName);
                }

                foreach (var baseName in ordered)
                {
                    HdAtlasParser parser;
                    try { parser = HdAtlasParser.Get(baseName); } catch { continue; }
                    if (parser.ImageDefinitions.Count == 0) continue;
                    _atlasNames.Add(baseName);
                    foreach (var def in parser.ImageDefinitions)
                    {
                        if (!index.ContainsKey(def.Name))
                            index[def.Name] = new AtlasHit(baseName, def.X, def.Y, def.Width, def.Height);
                    }
                }
            }
            catch { /* 索引失败时退化为空 */ }

            _index = index;
            return _index;
        }
    }

    /// <summary>某图集的全部图片定义（图库用）。</summary>
    public static IReadOnlyList<HdAtlasImageDef> AtlasImages(string atlas)
    {
        try { return HdAtlasParser.Get(atlas).ImageDefinitions; }
        catch { return Array.Empty<HdAtlasImageDef>(); }
    }

    // ============================================================= 解析 =============================================================

    /// <summary>解析图片引用；找不到返回 null。动画资源名（anim_xxx）解析为该图集的第一帧。</summary>
    public static LayoutImageRec? Resolve(string? imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;

        lock (_gate)
        {
            if (_cache.TryGetValue(imageRef, out var cached)) return cached;
        }

        LayoutImageRec? result = null;

        var s = imageRef.Replace('\\', '/').Trim();
        var noPrefix = s.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? s[6..] : s;
        var baseName = noPrefix.Split('/')[^1];
        var stem = StripExt(baseName);

        // 优先查额外导入的资源
        if (_extraAtlas.TryGetValue(baseName, out var ea) || _extraAtlas.TryGetValue(stem, out ea) || _extraAtlas.TryGetValue(stem + ".png", out ea))
        {
            result = new LayoutImageRec { Page = ea.Page, X = ea.X, Y = ea.Y, W = ea.W, H = ea.H, Atlas = ea.Atlas };
            goto done;
        }
        if (_extraLoose.TryGetValue(stem, out var el))
        {
            result = new LayoutImageRec { Page = el, X = 0, Y = 0, W = el.Width, H = el.Height };
            goto done;
        }

        var index = EnsureIndex();
        if (index.TryGetValue(s, out var hit) || index.TryGetValue(noPrefix, out hit) || index.TryGetValue(baseName, out hit)
            || index.TryGetValue(stem + ".png", out hit))
        {
            var page = GetAtlasPage(hit.Atlas);
            result = new LayoutImageRec { Page = page, X = hit.X, Y = hit.Y, W = hit.W, H = hit.H, Atlas = hit.Atlas };
            goto done;
        }

        // 动画资源：res="anim_glow" → 图集 anim_glow 的第一帧
        var animAtlas = _atlasNames.FirstOrDefault(a => string.Equals(AtlasDisplayName(a), stem, StringComparison.OrdinalIgnoreCase));
        if (animAtlas != null)
        {
            var defs = AtlasImages(animAtlas);
            var first = defs.FirstOrDefault(d => d.Name == "1.png") ?? defs.FirstOrDefault();
            if (first != null)
            {
                result = new LayoutImageRec { Page = GetAtlasPage(animAtlas), X = first.X, Y = first.Y, W = first.Width, H = first.Height, Atlas = animAtlas };
                goto done;
            }
        }

        // 散图回退：原路径 → image/ 前缀 → 各种扩展名
        {
            var dir = noPrefix.Contains('/') ? noPrefix[..noPrefix.LastIndexOf('/')] + "/" : "";
            var candidates = new List<string>();
            foreach (var ext in ImageExtensions)
            {
                candidates.Add(s.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? s : StripExt(s) + ext);
                candidates.Add("image/" + dir + stem + ext);
                candidates.Add(dir + stem + ext);
            }
            foreach (var cand in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var file = FindFile(cand);
                if (file == null) continue;
                var bmp = DecodeFile(file);
                if (bmp == null) continue;
                result = new LayoutImageRec { Page = bmp, X = 0, Y = 0, W = bmp.Width, H = bmp.Height };
                goto done;
            }
        }

    done:
        lock (_gate) { _cache[imageRef] = result; }
        return result;
    }

    private static string StripExt(string name)
    {
        int dot = name.LastIndexOf('.');
        int slash = name.LastIndexOf('/');
        return dot > slash ? name[..dot] : name;
    }

    private static string? FindFile(string relativePath)
    {
        try
        {
            var manager = AssetManager.Default;
            var entry = manager.Find(relativePath);
            if (entry != null) return entry.FullPath;

            var root = manager.AssetsRoot;
            if (!string.IsNullOrEmpty(root))
            {
                var p = IOPath.Combine(root, relativePath.Replace('/', IOPath.DirectorySeparatorChar));
                if (File.Exists(p)) return p;
            }

            var rel = relativePath.Replace('/', IOPath.DirectorySeparatorChar);
            var fn = IOPath.GetFileName(relativePath);
            foreach (var extra in _extraRoots)
            {
                var p = IOPath.Combine(extra, rel);
                if (File.Exists(p)) return p;
                p = IOPath.Combine(extra, fn);
                if (File.Exists(p)) return p;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static SKBitmap? GetAtlasPage(string atlasBase)
    {
        lock (_gate)
        {
            if (_pages.TryGetValue(atlasBase, out var cached)) return cached;
        }

        SKBitmap? page = null;
        try
        {
            var parser = HdAtlasParser.Get(atlasBase);
            var data = parser.SurfaceData;
            if (data != null && data.Length > 0)
            {
                using var ms = new MemoryStream(data);
                page = SKBitmap.Decode(ms);
            }
        }
        catch { page = null; }

        lock (_gate) { _pages[atlasBase] = page; }
        return page;
    }

    private static SKBitmap? DecodeFile(string path)
    {
        string key = "file:" + path;
        lock (_gate)
        {
            if (_pages.TryGetValue(key, out var cached)) return cached;
        }
        SKBitmap? bmp = null;
        try
        {
            using var stream = File.OpenRead(path);
            bmp = SKBitmap.Decode(stream);
        }
        catch { bmp = null; }
        lock (_gate) { _pages[key] = bmp; }
        return bmp;
    }

    // ============================================================= 额外导入 =============================================================

    /// <summary>导入一个资源文件夹：扫描顶层 .xml（图集 / 布局）与散图，返回发现的布局 XML 列表 (name, text, path)。</summary>
    public static List<(string Name, string Text, string Path)> IngestFolder(string folder)
    {
        var layouts = new List<(string, string, string)>();
        lock (_gate)
        {
            if (Directory.Exists(folder) && !_extraRoots.Contains(folder)) _extraRoots.Add(folder);

            foreach (var xml in SafeEnumerateFiles(folder, "*.xml"))
            {
                string text;
                try { text = File.ReadAllText(xml); } catch { continue; }
                if (IsAtlasXml(text)) RegisterExtraAtlas(xml, text, folder);
                else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                    layouts.Add((IOPath.GetFileName(xml), text, xml));
            }

            foreach (var ext in ImageExtensions)
                foreach (var img in SafeEnumerateFiles(folder, "*" + ext))
                    RegisterExtraLoose(img);

            _cache.Clear();
        }
        return layouts;
    }

    /// <summary>导入一批文件（多选）：图集 xml / 布局 xml / 散图。</summary>
    public static List<(string Name, string Text, string Path)> IngestFiles(string[] paths)
    {
        var layouts = new List<(string, string, string)>();
        lock (_gate)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var ext = IOPath.GetExtension(path).ToLowerInvariant();
                var dir = IOPath.GetDirectoryName(path) ?? "";
                if (!_extraRoots.Contains(dir)) _extraRoots.Add(dir);

                if (ext == ".xml")
                {
                    string text;
                    try { text = File.ReadAllText(path); } catch { continue; }
                    if (IsAtlasXml(text)) RegisterExtraAtlas(path, text, dir);
                    else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                        layouts.Add((IOPath.GetFileName(path), text, path));
                }
                else if (ImageExtensions.Contains(ext))
                {
                    RegisterExtraLoose(path);
                }
            }
            _cache.Clear();
        }
        return layouts;
    }

    private static bool IsAtlasXml(string text)
        => text.Contains("<Texture", StringComparison.OrdinalIgnoreCase)
           && (text.Contains("<Image", StringComparison.OrdinalIgnoreCase) || text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase));

    private static void RegisterExtraAtlas(string xmlPath, string text, string imageDir)
    {
        var baseName = IOPath.GetFileNameWithoutExtension(xmlPath);
        SKBitmap? page = null;
        var stems = new List<string> { baseName };
        var m = Regex.Match(text, "<Texture\\b[^>]*\\bname\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var file = m.Groups[1].Value.Replace('\\', '/').Split('/')[^1];
            int dot = file.LastIndexOf('.');
            if (dot > 0) file = file[..dot];
            if (file.Length > 0) stems.Insert(0, file);
        }
        foreach (var stem in stems)
        {
            foreach (var ext in ImageExtensions)
            {
                var p = IOPath.Combine(imageDir, stem + ext);
                if (File.Exists(p)) { try { page = SKBitmap.Decode(p); } catch { } break; }
            }
            if (page != null) break;
        }
        if (page == null) return;

        try
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml($"<root>{text}</root>");
            var nodes = xmlDoc.SelectNodes("//Images/Image") ?? xmlDoc.SelectNodes("//SubTextures/SubTexture");
            if (nodes == null) return;
            foreach (XmlNode n in nodes)
            {
                if (n.Attributes == null) continue;
                var name = n.Attributes["name"]?.Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                int x = int.TryParse(n.Attributes["x"]?.Value, out var xv) ? xv : 0;
                int y = int.TryParse(n.Attributes["y"]?.Value, out var yv) ? yv : 0;
                int w = int.TryParse(n.Attributes["w"]?.Value ?? n.Attributes["width"]?.Value, out var wv) ? wv : 0;
                int h = int.TryParse(n.Attributes["h"]?.Value ?? n.Attributes["height"]?.Value, out var hv) ? hv : 0;
                if (w > 0 && h > 0)
                {
                    _extraAtlas[name] = (page, x, y, w, h, baseName);
                    _extraAtlasCount++;
                }
            }
        }
        catch { }
    }

    private static void RegisterExtraLoose(string path)
    {
        try
        {
            var bmp = SKBitmap.Decode(path);
            if (bmp == null) return;
            _extraLoose[IOPath.GetFileNameWithoutExtension(path)] = bmp;
            _extraImageCount++;
        }
        catch { }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>清空缓存（资源切换后调用）。clearExtra=false 时保留额外导入的资源。</summary>
    public static void ClearCache(bool clearExtra = false)
    {
        lock (_gate)
        {
            _index = null;
            _atlasNames.Clear();
            foreach (var p in _pages.Values) p?.Dispose();
            _pages.Clear();
            _cache.Clear();
            if (clearExtra)
            {
                foreach (var p in _extraLoose.Values) p.Dispose();
                _extraLoose.Clear();
                _extraAtlas.Clear();
                _extraRoots.Clear();
                _extraAtlasCount = 0;
                _extraImageCount = 0;
            }
        }
    }
}
