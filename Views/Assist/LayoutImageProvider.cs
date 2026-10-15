using System.IO;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Views.Assist;

/// <summary>布局中一个图片引用的解析结果：命中的图集页（或散图）与源矩形。</summary>
internal sealed class LayoutImageRec
{
    public SKBitmap? Page { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }

    public bool Ready => Page != null && W > 0 && H > 0;
    public SKRect SrcRect => new(X, Y, X + W, Y + H);
}

/// <summary>
/// 布局编辑器的图片资源提供器：扫描 assets 中所有图集 xml 建立「图片名 → 图块」索引，
/// 找不到时回退到散图文件（自动尝试 .png/.webp 与 image/ 前缀），行为对齐 HTML 的 RT/resolveImg。
/// </summary>
internal static class LayoutImageProvider
{
    private static Dictionary<string, (string Atlas, int X, int Y, int W, int H)>? _index;
    private static readonly List<string> _atlasNames = new();
    private static readonly Dictionary<string, SKBitmap?> _pages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LayoutImageRec?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();

    // ---------- 额外资源（用户导入的文件夹 / 文件） ----------
    private static readonly List<string> _extraRoots = new();
    private static readonly Dictionary<string, (SKBitmap Page, int X, int Y, int W, int H)> _extraAtlas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SKBitmap> _extraLoose = new(StringComparer.OrdinalIgnoreCase);
    private static int _extraAtlasCount, _extraImageCount;

    public static IReadOnlyList<string> AtlasNames { get { EnsureIndex(); return _atlasNames; } }
    public static int ImageCount { get { EnsureIndex(); return _index!.Count; } }
    public static int ExtraAtlasCount => _extraAtlasCount;
    public static int ExtraImageCount => _extraImageCount;

    private static readonly string[] ImageExtensions = { ".png", ".webp", ".jpg", ".jpeg", ".bmp" };

    /// <summary>
    /// 导入一个资源文件夹：扫描顶层 .xml（图集 / 布局）、图片散图，
    /// 返回发现的布局 XML 列表 (name, text)。对齐 html 的 ingestFileList。
    /// </summary>
    public static List<(string Name, string Text)> IngestFolder(string folder)
    {
        var layouts = new List<(string, string)>();
        lock (_gate)
        {
            if (Directory.Exists(folder) && !_extraRoots.Contains(folder))
                _extraRoots.Add(folder);

            foreach (var xml in SafeEnumerateFiles(folder, "*.xml"))
            {
                string text;
                try { text = File.ReadAllText(xml); } catch { continue; }
                if (text.Contains("<Texture", StringComparison.OrdinalIgnoreCase) &&
                    (text.Contains("<Image", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)))
                {
                    RegisterExtraAtlas(xml, text, folder);
                }
                else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                {
                    var name = IOPath.GetFileName(xml) ?? "";
                    layouts.Add((name, text));
                }
            }

            foreach (var ext in ImageExtensions)
                foreach (var img in SafeEnumerateFiles(folder, "*" + ext))
                    RegisterExtraLoose(img);

            _cache.Clear();
        }
        return layouts;
    }

    /// <summary>导入一批文件（多选）：图集 xml / 布局 xml / 散图 / .fnt。</summary>
    public static List<(string Name, string Text)> IngestFiles(string[] paths)
    {
        var layouts = new List<(string, string)>();
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
                    if (text.Contains("<Texture", StringComparison.OrdinalIgnoreCase) &&
                        (text.Contains("<Image", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)))
                        RegisterExtraAtlas(path, text, dir);
                    else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                        layouts.Add((IOPath.GetFileName(path) ?? "", text));
                }
                else if (ext is ".png" or ".webp" or ".jpg" or ".jpeg" or ".bmp")
                {
                    RegisterExtraLoose(path);
                }
            }
            _cache.Clear();
        }
        return layouts;
    }

    private static void RegisterExtraAtlas(string xmlPath, string text, string imageDir)
    {
        var baseName = IOPath.GetFileNameWithoutExtension(xmlPath) ?? "";
        SKBitmap? page = null;
        var stems = new List<string> { baseName };
        var m = System.Text.RegularExpressions.Regex.Match(
            text, "<Texture\\b[^>]*\\bname\\s*=\\s*\"([^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var tex = m.Groups[1].Value.Replace('\\', '/');
            var file = tex.Split('/')[^1];
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
                    _extraAtlas[name] = (page, x, y, w, h);
                    _extraAtlasCount++;
                }
            }
            if (!_atlasNames.Contains(baseName)) _atlasNames.Add(baseName);
        }
        catch { }
    }

    private static void RegisterExtraLoose(string path)
    {
        try
        {
            var bmp = SKBitmap.Decode(path);
            if (bmp == null) return;
            var name = IOPath.GetFileNameWithoutExtension(path) ?? "";
            _extraLoose[name] = bmp;
            _extraImageCount++;
        }
        catch { }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    private static Dictionary<string, (string Atlas, int X, int Y, int W, int H)> EnsureIndex()
    {
        lock (_gate)
        {
            if (_index != null) return _index;

            var index = new Dictionary<string, (string, int, int, int, int)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var manager = AssetManager.Default;
                if (!manager.IsLoaded)
                {
                    try { manager.ScanDefault(); } catch { /* ignore */ }
                }

                foreach (var entry in manager.ListByExtension(".xml"))
                {
                    var rel = entry.RelativePath.Replace('\\', '/');
                    if (rel.Contains('/')) continue; // 只扫 assets 根目录下的图集 xml

                    string text;
                    try { text = manager.ReadText(entry); }
                    catch { continue; }

                    if (!text.Contains("<Texture", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!text.Contains("<Image", StringComparison.OrdinalIgnoreCase) &&
                        !text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)) continue;

                    var baseName = rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? rel[..^4] : rel;
                    var parser = HdAtlasParser.Get(baseName);
                    if (!parser.IsLoaded) continue;

                    _atlasNames.Add(baseName);
                    foreach (var def in parser.ImageDefinitions)
                        index[def.Name] = (baseName, def.X, def.Y, def.Width, def.Height);
                }
            }
            catch { /* 索引失败时退化为空 */ }

            _index = index;
            return _index;
        }
    }

    /// <summary>解析图片引用；找不到返回 null。</summary>
    public static LayoutImageRec? Resolve(string? imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;

        if (_cache.TryGetValue(imageRef, out var cached)) return cached;

        LayoutImageRec? result = null;

        var s = imageRef.Replace('\\', '/');
        var noPrefix = s.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? s[7..] : s;
        var baseName = noPrefix.Split('/')[^1];
        var stem = StripExt(baseName);

        // 优先查额外导入的资源
        if (_extraAtlas.TryGetValue(baseName, out var ea) || _extraAtlas.TryGetValue(stem, out ea))
        {
            result = new LayoutImageRec { Page = ea.Page, X = ea.X, Y = ea.Y, W = ea.W, H = ea.H };
            goto done;
        }
        if (_extraLoose.TryGetValue(stem, out var el))
        {
            result = new LayoutImageRec { Page = el, X = 0, Y = 0, W = el.Width, H = el.Height };
            goto done;
        }

        var index = EnsureIndex();
        if (index.TryGetValue(s, out var hit) || index.TryGetValue(noPrefix, out hit) || index.TryGetValue(baseName, out hit))
        {
            var page = GetAtlasPage(hit.Atlas);
            result = new LayoutImageRec { Page = page, X = hit.X, Y = hit.Y, W = hit.W, H = hit.H };
        }
        else
        {
            // 散图回退
            foreach (var ext in ImageExtensions)
            {
                foreach (var cand in new[] { baseName, stem + ext, "image/" + baseName, "image/" + stem + ext })
                {
                    var file = FindFile(cand);
                    if (file == null) continue;
                    var bmp = DecodeFile(file);
                    if (bmp == null) continue;
                    result = new LayoutImageRec { Page = bmp, X = 0, Y = 0, W = bmp.Width, H = bmp.Height };
                    goto done;
                }
            }
        }

    done:
        _cache[imageRef] = result;
        return result;
    }

    private static string StripExt(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
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
                var p = System.IO.Path.Combine(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (File.Exists(p)) return p;
            }

            // 额外导入的资源目录
            var rel = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            foreach (var extra in _extraRoots)
            {
                var p = System.IO.Path.Combine(extra, rel);
                if (File.Exists(p)) return p;
                var fn = System.IO.Path.GetFileName(relativePath);
                p = System.IO.Path.Combine(extra, fn);
                if (File.Exists(p)) return p;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static SKBitmap? GetAtlasPage(string atlasBase)
    {
        if (_pages.TryGetValue(atlasBase, out var cached)) return cached;

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

        _pages[atlasBase] = page;
        return page;
    }

    private static SKBitmap? DecodeFile(string path)
    {
        if (_pages.TryGetValue("file:" + path, out var cached)) return cached;
        SKBitmap? bmp = null;
        try
        {
            using var stream = File.OpenRead(path);
            bmp = SKBitmap.Decode(stream);
        }
        catch { bmp = null; }
        _pages["file:" + path] = bmp;
        return bmp;
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
