using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 位图字体字形（.fnt 二进制逐条 12B）。
/// </summary>
public struct BmGlyph
{
    public int Code;
    public int X, Y, W, H;
    public int BearingX, BearingY;
    public int Advance;
}

/// <summary>
/// 位图字体：lineHeight + 字形表 + 纹理页。
/// </summary>
public sealed class BmFont
{
    public int LineHeight;
    public readonly Dictionary<int, BmGlyph> Glyphs = new();
    public SKBitmap? Page;
    public bool Ready => Page != null && Glyphs.Count > 0;
}

/// <summary>字体元数据（推断自命名约定）。</summary>
public struct FontMeta
{
    public double Size;
    public double Outline;
}

/// <summary>
/// 布局编辑器字体：优先用 AssetManager 定位并加载 assets 内的
/// <c>font/NotoSans_tw.otf</c>（与游戏运行时文字字形一致），
/// 找不到再退回系统中文黑体，保证任何环境下都有可用字形。
/// 同时支持位图字体（.fnt 二进制）解析与字体元数据推断。
/// </summary>
internal static class LayoutFontProvider
{
    private const string PreferredFontRelative = "font/NotoSans_tw.otf";

    private static readonly string[] FallbackFiles =
    {
        @"C:\Windows\Fonts\msyh.ttc",
        @"C:\Windows\Fonts\msyh.ttf",
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\simsun.ttc"
    };

    private static SKTypeface? _typeface;
    private static bool _resolved;

    /// <summary>当前字体（惰性解析，失败时回退系统字体，绝不返回 null）。</summary>
    public static SKTypeface Typeface
    {
        get
        {
            if (!_resolved)
            {
                _resolved = true;
                _typeface = Resolve();
            }
            return _typeface ?? SKTypeface.Default;
        }
    }

    /// <summary>当前字体来源描述（用于状态栏/调试）。</summary>
    public static string Source { get; private set; } = "未解析";

    private static SKTypeface? Resolve()
    {
        // 1) AssetManager 定位（assets 根目录下的 font/NotoSans_tw.otf）
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
            {
                try { manager.ScanDefault(); } catch { /* ignore */ }
            }

            var entry = manager.Find(PreferredFontRelative);
            if (entry != null && File.Exists(entry.FullPath))
            {
                var tf = SKTypeface.FromFile(entry.FullPath);
                if (tf != null)
                {
                    Source = $"assets/{PreferredFontRelative}";
                    return tf;
                }
            }

            // 2) 直接按 assets 根目录拼路径兜底
            var root = manager.AssetsRoot;
            if (!string.IsNullOrEmpty(root))
            {
                var path = System.IO.Path.Combine(root, "font", "NotoSans_tw.otf");
                if (File.Exists(path))
                {
                    var tf = SKTypeface.FromFile(path);
                    if (tf != null)
                    {
                        Source = path;
                        return tf;
                    }
                }
            }
        }
        catch { /* 落到系统字体 */ }

        // 3) 系统中文黑体
        foreach (var p in FallbackFiles)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var tf = SKTypeface.FromFile(p);
                if (tf != null)
                {
                    Source = p;
                    return tf;
                }
            }
            catch { /* ignore */ }
        }

        foreach (var name in new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Noto Sans CJK SC" })
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(name, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                if (tf != null)
                {
                    Source = name;
                    return tf;
                }
            }
            catch { /* ignore */ }
        }

        Source = "SKTypeface.Default";
        return SKTypeface.Default;
    }

    // ============================================================= 位图字体 =============================================================
    private static readonly Dictionary<string, BmFont> _bmFonts = new(StringComparer.OrdinalIgnoreCase);
    private static bool _bmResolved;

    /// <summary>按名查位图字体（支持去 _hd 后缀、前缀匹配）；找不到返回 null。</summary>
    public static BmFont? GetBmFont(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        EnsureBmFonts();
        if (_bmFonts.TryGetValue(name, out var f)) return f;
        var nohd = System.Text.RegularExpressions.Regex.Replace(name, @"_hd$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!string.Equals(nohd, name, StringComparison.OrdinalIgnoreCase) && _bmFonts.TryGetValue(nohd, out f)) return f;
        foreach (var kv in _bmFonts)
            if (name.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static void EnsureBmFonts()
    {
        if (_bmResolved) return;
        _bmResolved = true;
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
            var root = manager.AssetsRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var fontDir = System.IO.Path.Combine(root, "font");
            if (!Directory.Exists(fontDir)) return;

            foreach (var fnt in Directory.EnumerateFiles(fontDir, "*.fnt", SearchOption.TopDirectoryOnly))
            {
                var baseName = System.IO.Path.GetFileNameWithoutExtension(fnt);
                var png = System.IO.Path.Combine(fontDir, baseName + ".png");
                if (!File.Exists(png)) continue;
                var font = ParseFnt(fnt, png);
                if (font is { Ready: true } bf)
                {
                    _bmFonts[baseName] = bf;
                    if (System.Text.RegularExpressions.Regex.IsMatch(baseName, @"_hd$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        var shortName = System.Text.RegularExpressions.Regex.Replace(baseName, @"_hd$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!_bmFonts.ContainsKey(shortName)) _bmFonts[shortName] = bf;
                    }
                }
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>解析 .fnt 二进制：u32 count, u32 lineHeight；每条 12B
    /// code u16, x u16, y u16, w u8, h u8, bearingX i8, bearingY i8, advance u8, 1B 保留。</summary>
    private static BmFont? ParseFnt(string fntPath, string pngPath)
    {
        try
        {
            var data = File.ReadAllBytes(fntPath);
            if (data.Length < 8) return null;
            var font = new BmFont
            {
                LineHeight = BitConverter.ToInt32(data, 4)
            };
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
            return font;
        }
        catch { return null; }
    }

    // ============================================================= 字体元数据 =============================================================
    /// <summary>按字体名推断元数据（size + outline）。无数据文件时从命名约定推断。</summary>
    public static FontMeta GetFontMeta(string? name)
    {
        if (string.IsNullOrEmpty(name)) return new FontMeta { Size = 24, Outline = 0 };
        var n = name.ToLowerInvariant();

        // font_text_newgame_{1-4} → size 18/22/28/36, outline 0/0/1/2
        if (n.Contains("newgame_4") || n.Contains("text_4")) return new FontMeta { Size = 36, Outline = 2 };
        if (n.Contains("newgame_3") || n.Contains("text_3")) return new FontMeta { Size = 28, Outline = 1 };
        if (n.Contains("newgame_2") || n.Contains("text_2")) return new FontMeta { Size = 22, Outline = 0 };
        if (n.Contains("newgame_1") || n.Contains("text_1")) return new FontMeta { Size = 18, Outline = 0 };

        // font_num_{1-4}
        if (n.Contains("num_4")) return new FontMeta { Size = 36, Outline = 0 };
        if (n.Contains("num_3")) return new FontMeta { Size = 28, Outline = 0 };
        if (n.Contains("num_2")) return new FontMeta { Size = 22, Outline = 0 };
        if (n.Contains("num_1")) return new FontMeta { Size = 18, Outline = 0 };

        if (n.Contains("city")) return new FontMeta { Size = 24, Outline = 1 };
        if (n.Contains("age")) return new FontMeta { Size = 20, Outline = 0 };
        if (n.Contains("ascii")) return new FontMeta { Size = 16, Outline = 0 };
        if (n.Contains("fight")) return new FontMeta { Size = 24, Outline = 0 };

        return new FontMeta { Size = 24, Outline = 0 };
    }

    /// <summary>位图字体数量（调试/状态栏用）。</summary>
    public static int BmFontCount { get { EnsureBmFonts(); return _bmFonts.Count; } }
}
