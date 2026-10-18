using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.Layout;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>位图字体字形（.fnt 二进制逐条 12B）。</summary>
public struct BmGlyph
{
    public int Code;
    public int X, Y, W, H;
    public int BearingX, BearingY;
    public int Advance;
}

/// <summary>位图字体：lineHeight + 字形表 + 纹理页。</summary>
public sealed class BmFont
{
    public int LineHeight;
    public readonly Dictionary<int, BmGlyph> Glyphs = new();
    public SKBitmap? Page;
    public bool Ready => Page != null && Glyphs.Count > 0;
}

/// <summary>字体元数据（来自 font_resource.xml，缺失时按命名推断）。</summary>
public struct FontMeta
{
    public double Size;
    public double Outline;
    public double OffsetY;
    public bool IsBitmap;
    public string File;
}

/// <summary>
/// 布局编辑器字体来源，全部对齐游戏：
/// <list type="bullet">
/// <item>字号 / 描边 / 偏移来自 <c>font_resource.xml</c>（按设备配置，默认 PHONE/HD —— 布局坐标就是按它写的）；</item>
/// <item>矢量字体按 stringtable 语言取 <c>font/NotoSans_{lang}.otf</c>（游戏 lang=true 的字体就是这样切换的）；</item>
/// <item>位图字体（font_num_* / font_ascii …）读 <c>.fnt</c>，filehd 的用 <c>_hd</c> 版本并按 contentscale 缩放。</item>
/// </list>
/// </summary>
internal static class LayoutFontProvider
{
    private static readonly object _gate = new();
    private static string _profile = FontResourceParser.ProfilePhoneHd;
    private static Dictionary<string, LayoutFontDef>? _defs;
    private static readonly Dictionary<string, SKTypeface?> _typefaces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BmFont?> _bmFonts = new(StringComparer.OrdinalIgnoreCase);
    private static int _bmFontFileCount = -1;

    private static readonly string[] SystemFallbackFiles =
    {
        @"C:\Windows\Fonts\msyh.ttc",
        @"C:\Windows\Fonts\msyh.ttf",
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\simsun.ttc"
    };

    /// <summary>设备配置（PHONE/HD、PAD/HD、PAD/SD），改了之后字号表重载。</summary>
    public static string Profile
    {
        get => _profile;
        set
        {
            if (string.Equals(_profile, value, StringComparison.OrdinalIgnoreCase)) return;
            lock (_gate)
            {
                _profile = value;
                _defs = null;
                _bmFonts.Clear();
            }
        }
    }

    /// <summary>当前矢量字体来源描述（状态栏 / 调试）。</summary>
    public static string Source { get; private set; } = "";

    // ============================================================= 字体定义 =============================================================

    private static Dictionary<string, LayoutFontDef> Defs
    {
        get
        {
            lock (_gate)
            {
                if (_defs != null) return _defs;
                _defs = new Dictionary<string, LayoutFontDef>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var manager = AssetManager.Default;
                    if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
                    var entry = manager.Find("font_resource.xml");
                    if (entry != null)
                        _defs = FontResourceParser.Parse(manager.ReadText(entry), _profile);
                }
                catch { /* 没有定义文件时全部走推断 */ }
                return _defs;
            }
        }
    }

    /// <summary>font_resource.xml 里的全部字体名（给字体选择器用）。</summary>
    public static IReadOnlyCollection<string> FontNames => Defs.Keys.ToList();

    public static LayoutFontDef? GetDef(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Defs.TryGetValue(name, out var d)) return d;
        // font_text_newgame_1 这类旧名 → font_text_1
        var alias = Regex.Replace(name, @"_newgame", "", RegexOptions.IgnoreCase);
        alias = Regex.Replace(alias, @"_(cn|tw|en|ja|ko)(_hd)?$", "", RegexOptions.IgnoreCase);
        alias = Regex.Replace(alias, @"_hd$", "", RegexOptions.IgnoreCase);
        return Defs.TryGetValue(alias, out d) ? d : null;
    }

    /// <summary>字体元数据；没有定义时按命名约定推断（size 取 PHONE/HD 口径）。</summary>
    public static FontMeta GetFontMeta(string? name)
    {
        var def = GetDef(name);
        if (def != null)
        {
            return new FontMeta
            {
                Size = def.IsBitmap ? 0 : def.Size,
                Outline = def.Outline,
                OffsetY = def.OffsetY,
                IsBitmap = def.IsBitmap,
                File = def.File
            };
        }

        var n = (name ?? "").ToLowerInvariant();
        bool bitmap = n.Contains("num") || n.Contains("ascii") || n.Contains("fight") || n.Contains("age") || n.Contains("city2") || n.Contains("editor");
        double size = 21, outline = 1;
        var m = Regex.Match(n, @"text_(\d)([ab]?)");
        if (m.Success)
        {
            size = (m.Groups[1].Value, m.Groups[2].Value) switch
            {
                ("0", _) => 14, ("1", _) => 16, ("2", "b") => 20, ("2", _) => 18,
                ("3", "a") => 21, ("3", "b") => 25, ("3", _) => 21,
                ("4", "b") => 32, ("4", _) => 28, ("5", _) => 40, _ => 21
            };
            outline = size >= 28 ? 2 : 1;
        }
        else if (n.Contains("city")) { size = 19; outline = 1; }
        return new FontMeta { Size = size, Outline = outline, OffsetY = 0, IsBitmap = bitmap, File = "NotoSans" };
    }

    // ============================================================= 矢量字体 =============================================================

    /// <summary>语言代码 → NotoSans 文件后缀（游戏只有 cn/tw/en/ja/ko 五套，其余语言用 en）。</summary>
    private static string LangSuffix(string lang) => lang.ToLowerInvariant() switch
    {
        "cn" or "zh_cn" or "zh-cn" => "cn",
        "tw" or "zh_tw" or "zh-tw" => "tw",
        "ja" or "jp" => "ja",
        "ko" or "kr" => "ko",
        _ => "en"
    };

    /// <summary>按字体定义与语言取字形文件（缓存）。永不返回 null。</summary>
    public static SKTypeface GetTypeface(string lang, LayoutFontDef? def = null)
    {
        string file = def?.File ?? "NotoSans";
        bool langSpecific = def?.LangSpecific ?? true;
        string stem = langSpecific && string.Equals(file, "NotoSans", StringComparison.OrdinalIgnoreCase)
            ? $"NotoSans_{LangSuffix(lang)}"
            : file;

        lock (_gate)
        {
            if (_typefaces.TryGetValue(stem, out var cached) && cached != null) return cached;
            var tf = LoadFontFile(stem) ?? LoadFontFile($"NotoSans_{LangSuffix(lang)}") ?? LoadFontFile("NotoSans_tw") ?? LoadSystemFallback();
            _typefaces[stem] = tf;
            return tf;
        }
    }

    /// <summary>默认字形（繁中），兼容旧调用。</summary>
    public static SKTypeface Typeface => GetTypeface("tw");

    private static SKTypeface? LoadFontFile(string stem)
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
            foreach (var ext in new[] { ".otf", ".ttf" })
            {
                var entry = manager.Find($"font/{stem}{ext}");
                string? path = entry?.FullPath;
                if (path == null && !string.IsNullOrEmpty(manager.AssetsRoot))
                {
                    var p = IOPath.Combine(manager.AssetsRoot, "font", stem + ext);
                    if (File.Exists(p)) path = p;
                }
                if (path != null && File.Exists(path))
                {
                    var tf = SKTypeface.FromFile(path);
                    if (tf != null) { Source = $"assets/font/{stem}{ext}"; return tf; }
                }
            }
        }
        catch { /* 落到系统字体 */ }
        return null;
    }

    private static SKTypeface LoadSystemFallback()
    {
        foreach (var p in SystemFallbackFiles)
        {
            if (!File.Exists(p)) continue;
            try { var tf = SKTypeface.FromFile(p); if (tf != null) { Source = p; return tf; } } catch { }
        }
        foreach (var name in new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "Noto Sans CJK SC" })
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(name, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                if (tf != null) { Source = name; return tf; }
            }
            catch { }
        }
        Source = "SKTypeface.Default";
        return SKTypeface.Default;
    }

    // ============================================================= 位图字体 =============================================================

    /// <summary>
    /// 按字体名取位图字体与绘制缩放（1/contentscale）。
    /// 找不到返回 null —— 调用方应退回矢量字体画。
    /// </summary>
    public static BmFont? GetBmFont(string? name, out double scale)
    {
        scale = 1;
        if (string.IsNullOrEmpty(name)) return null;

        var def = GetDef(name);
        bool hd = def?.FileHd ?? true;
        scale = def != null && def.ContentScale > 0 ? 1.0 / def.ContentScale : 1;

        string bare = Regex.Replace(name, @"_hd$", "", RegexOptions.IgnoreCase);
        var candidates = hd ? new[] { bare + "_hd", bare } : new[] { bare, bare + "_hd" };

        lock (_gate)
        {
            foreach (var cand in candidates)
            {
                if (_bmFonts.TryGetValue(cand, out var cached))
                {
                    if (cached != null) return cached;
                    continue;
                }
                var font = LoadBmFont(cand);
                _bmFonts[cand] = font;
                if (font != null)
                {
                    // 定义里没写 contentscale 但文件是 _hd 的：按 2 倍贴图处理（PHONE/HD 下 font_num 等皆如此）
                    if (def == null && cand.EndsWith("_hd", StringComparison.OrdinalIgnoreCase)) scale = 0.5;
                    return font;
                }
            }
        }
        return null;
    }

    private static BmFont? LoadBmFont(string baseName)
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
            var root = manager.AssetsRoot;
            if (string.IsNullOrEmpty(root)) return null;
            var fnt = IOPath.Combine(root, "font", baseName + ".fnt");
            var png = IOPath.Combine(root, "font", baseName + ".png");
            if (!File.Exists(fnt) || !File.Exists(png)) return null;
            return ParseFnt(fnt, png);
        }
        catch { return null; }
    }

    /// <summary>解析 .fnt 二进制：u32 count, u32 lineHeight；每条 12B
    /// code u16, x u16, y u16, w u8, h u8, bearingX i8, bearingY i8, advance u8, 1B 保留。</summary>
    private static BmFont? ParseFnt(string fntPath, string pngPath)
    {
        try
        {
            var data = File.ReadAllBytes(fntPath);
            if (data.Length < 8) return null;
            var font = new BmFont { LineHeight = BitConverter.ToInt32(data, 4) };
            int count = BitConverter.ToInt32(data, 0);
            for (int i = 0; i < count; i++)
            {
                int o = 8 + i * 12;
                if (o + 11 > data.Length) break;
                var g = new BmGlyph
                {
                    Code = BitConverter.ToUInt16(data, o),
                    X = BitConverter.ToUInt16(data, o + 2),
                    Y = BitConverter.ToUInt16(data, o + 4),
                    W = data[o + 6],
                    H = data[o + 7],
                    BearingX = (sbyte)data[o + 8],
                    BearingY = (sbyte)data[o + 9],
                    Advance = data[o + 10]
                };
                font.Glyphs[g.Code] = g;
            }
            using var stream = File.OpenRead(pngPath);
            font.Page = SKBitmap.Decode(stream);
            return font.Ready ? font : null;
        }
        catch { return null; }
    }

    /// <summary>assets/font 下 .fnt 文件数量（状态栏用）。</summary>
    public static int BmFontCount
    {
        get
        {
            if (_bmFontFileCount >= 0) return _bmFontFileCount;
            try
            {
                var root = AssetManager.Default.AssetsRoot;
                var dir = string.IsNullOrEmpty(root) ? null : IOPath.Combine(root, "font");
                _bmFontFileCount = dir != null && Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.fnt").Count() : 0;
            }
            catch { _bmFontFileCount = 0; }
            return _bmFontFileCount;
        }
    }

    /// <summary>清空全部缓存（资源目录变更后）。</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _defs = null;
            _bmFonts.Clear();
            _typefaces.Clear();
            _bmFontFileCount = -1;
        }
    }
}
