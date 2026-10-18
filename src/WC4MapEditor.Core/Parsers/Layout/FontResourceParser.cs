using System.Globalization;
using System.Xml.Linq;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>font_resource.xml 里一条字体定义。</summary>
public sealed class LayoutFontDef
{
    public string Name { get; init; } = "";

    /// <summary>true = 位图字体（.fnt + .png），false = 矢量字体（NotoSans 系列 otf）。</summary>
    public bool IsBitmap { get; init; }

    /// <summary>矢量字体的字号（布局坐标单位）。</summary>
    public double Size { get; init; }

    /// <summary>描边宽度（0 = 无）。</summary>
    public double Outline { get; init; }

    /// <summary>基线 Y 偏移。</summary>
    public double OffsetY { get; init; }

    /// <summary>true = 字体文件按语言分（NotoSans_cn / NotoSans_tw …）。</summary>
    public bool LangSpecific { get; init; }

    /// <summary>字体文件主名（NotoSans / NotoSans_Lang / NotoSans_City_tw）。</summary>
    public string File { get; init; } = "NotoSans";

    /// <summary>位图字体是否用 _hd 版本的 .fnt。</summary>
    public bool FileHd { get; init; }

    /// <summary>位图字体内容缩放：2 表示贴图是 2 倍分辨率，渲染时要缩到 1/2。</summary>
    public double ContentScale { get; init; } = 1;
}

/// <summary>
/// 解析 assets/font_resource.xml：
/// <c>&lt;FontResource&gt;&lt;PAD&gt;&lt;HD&gt;&lt;file …/&gt;&lt;/HD&gt;&lt;SD&gt;…&lt;/SD&gt;&lt;/PAD&gt;&lt;PHONE&gt;&lt;HD&gt;…&lt;/HD&gt;&lt;/PHONE&gt;&lt;/FontResource&gt;</c>
/// <para>
/// 游戏按设备（PAD / PHONE）与清晰度（HD / SD）选一组字体定义。手机上的 HD 配置里
/// font_text_4 是 28px，而 PAD/HD 是 56px —— 布局坐标是以手机 HD 为基准写的
/// （例如 lbl_campaign 高 36 配 font_text_4），所以编辑器默认用 PHONE/HD。
/// </para>
/// </summary>
public static class FontResourceParser
{
    public const string ProfilePhoneHd = "PHONE/HD";
    public const string ProfilePadHd = "PAD/HD";
    public const string ProfilePadSd = "PAD/SD";

    public static readonly string[] Profiles = { ProfilePhoneHd, ProfilePadHd, ProfilePadSd };

    /// <summary>解析指定配置（如 "PHONE/HD"）下的字体表；配置不存在时返回空表。</summary>
    public static Dictionary<string, LayoutFontDef> Parse(string xml, string profile)
    {
        var result = new Dictionary<string, LayoutFontDef>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(xml)) return result;

        var parts = profile.Split('/');
        if (parts.Length != 2) return result;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return result; }

        var group = doc.Root?.Element(parts[0])?.Element(parts[1]);
        if (group == null) return result;

        foreach (var file in group.Elements("file"))
        {
            string name = (string?)file.Attribute("name") ?? "";
            if (name.Length == 0) continue;

            string? sizeAttr = (string?)file.Attribute("size");
            bool hasSize = double.TryParse(sizeAttr, NumberStyles.Any, CultureInfo.InvariantCulture, out double size);
            bool fileHd = string.Equals((string?)file.Attribute("filehd"), "true", StringComparison.OrdinalIgnoreCase);

            result[name] = new LayoutFontDef
            {
                Name = name,
                // 没写 size 的都是位图字体（font_num_* / font_ascii / font_fight_num …）
                IsBitmap = !hasSize,
                Size = hasSize ? size : 0,
                Outline = Num(file, "outline", 0),
                OffsetY = Num(file, "offsety", 0),
                LangSpecific = string.Equals((string?)file.Attribute("lang"), "true", StringComparison.OrdinalIgnoreCase),
                File = (string?)file.Attribute("file") ?? "NotoSans",
                FileHd = fileHd,
                ContentScale = Num(file, "contentscale", fileHd && hasSize == false ? 1 : 1)
            };
        }

        return result;
    }

    private static double Num(XElement e, string attr, double def)
        => double.TryParse((string?)e.Attribute(attr), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : def;
}
