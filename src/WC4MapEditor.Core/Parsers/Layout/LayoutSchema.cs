namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>布局属性的值类型，决定属性面板用什么编辑器。</summary>
public enum LayoutAttrKind
{
    /// <summary>任意文本。</summary>
    Text,
    /// <summary>整数。</summary>
    Int,
    /// <summary>小数（缩放、透明度等）。</summary>
    Number,
    /// <summary>true / false。</summary>
    Bool,
    /// <summary>固定枚举（见 <see cref="LayoutAttrDef.Options"/>）。</summary>
    Enum,
    /// <summary>颜色 "r,g,b,a"（0~255）。</summary>
    Color,
    /// <summary>图集里的图片名或 image/ 下的散图路径。</summary>
    Image,
    /// <summary>font_resource.xml 里的字体名。</summary>
    Font,
    /// <summary>stringtable 文本键。</summary>
    StringKey,
    /// <summary>边距 "L,T,R,B"。</summary>
    Margin,
    /// <summary>Template 的 id。</summary>
    TemplateId,
    /// <summary>动画资源（anim_*.xml 的文件名）。</summary>
    AnimRes,
    /// <summary>音效文件名。</summary>
    Sound
}

/// <summary>一个属性的定义。</summary>
public sealed record LayoutAttrDef(string Name, LayoutAttrKind Kind, string Group, string[]? Options = null, string? Default = null);

/// <summary>一个控件标签的定义。</summary>
public sealed class LayoutTagDef
{
    public string Tag { get; init; } = "";

    /// <summary>分类：Container（容器）/ Widget（普通控件）/ Game（游戏专用控件，编辑器只画占位）。</summary>
    public string Category { get; init; } = "Widget";

    /// <summary>true = 没有自己的图像、没写宽高时填满父级（表单 / 分组 / 排列组）。</summary>
    public bool IsContainer { get; init; }

    /// <summary>该标签支持的属性（不含所有控件共有的通用属性）。</summary>
    public IReadOnlyList<LayoutAttrDef> Attributes { get; init; } = Array.Empty<LayoutAttrDef>();

    /// <summary>取主图引用时按顺序尝试的属性名（为空表示该控件没有主图）。</summary>
    public IReadOnlyList<string> ImageKeys { get; init; } = Array.Empty<string>();

    /// <summary>所有可编辑属性（通用 + 专有）。</summary>
    public IEnumerable<LayoutAttrDef> AllAttributes => LayoutSchema.CommonAttributes.Concat(Attributes);

    public LayoutAttrDef? Find(string name)
        => AllAttributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// WC4 布局 XML 的标签 / 属性清单。
/// <para>
/// 数据来源不是猜测，而是对 assets/layout_x.xml 全量统计：每个标签实际出现过哪些属性、枚举值有哪些
/// （如 drawmode 只有 extend / stretch / hextend / vextend / tile；visible 里还有 "fasle" "flase" 这类手误）。
/// 属性面板按这张表出 UI，不再展示游戏里根本不存在的属性（旧版的 pressedimage / checkedimage / CheckButton 等）。
/// </para>
/// </summary>
public static class LayoutSchema
{
    public const string GroupIdentity = "Identity";
    public const string GroupLayout = "Layout";
    public const string GroupAppearance = "Appearance";
    public const string GroupImage = "Image";
    public const string GroupText = "Text";
    public const string GroupGroup = "Group";
    public const string GroupRepeater = "Repeater";
    public const string GroupWindow = "Window";
    public const string GroupAnimation = "Animation";
    public const string GroupScroll = "Scroll";
    public const string GroupMisc = "Misc";

    public static readonly string[] AlignHOptions = { "left", "center", "right" };
    public static readonly string[] AlignVOptions = { "top", "center", "bottom" };
    public static readonly string[] DrawModeOptions = { "extend", "stretch", "hextend", "vextend", "tile" };
    public static readonly string[] BoolOptions = { "true", "false" };
    public static readonly string[] ScrollModeOptions = { "vertical", "horizontal" };

    /// <summary>所有控件共有的属性。</summary>
    public static readonly IReadOnlyList<LayoutAttrDef> CommonAttributes = new[]
    {
        new LayoutAttrDef("id", LayoutAttrKind.Text, GroupIdentity),
        new LayoutAttrDef("margin", LayoutAttrKind.Margin, GroupLayout, Default: "0,0,0,0"),
        new LayoutAttrDef("halign", LayoutAttrKind.Enum, GroupLayout, AlignHOptions, "left"),
        new LayoutAttrDef("valign", LayoutAttrKind.Enum, GroupLayout, AlignVOptions, "top"),
        new LayoutAttrDef("width", LayoutAttrKind.Int, GroupLayout),
        new LayoutAttrDef("height", LayoutAttrKind.Int, GroupLayout),
        new LayoutAttrDef("visible", LayoutAttrKind.Bool, GroupAppearance, BoolOptions, "true")
    };

    private static LayoutAttrDef A(string name, LayoutAttrKind kind, string group, string[]? options = null, string? def = null)
        => new(name, kind, group, options, def);

    private static readonly LayoutAttrDef DrawMode = A("drawmode", LayoutAttrKind.Enum, GroupImage, DrawModeOptions);
    private static readonly LayoutAttrDef ColorAttr = A("color", LayoutAttrKind.Color, GroupAppearance, def: "255,255,255,255");
    private static readonly LayoutAttrDef Clipped = A("clipped", LayoutAttrKind.Bool, GroupAppearance, BoolOptions, "false");
    private static readonly LayoutAttrDef Scale = A("scale", LayoutAttrKind.Number, GroupAppearance, def: "1");
    private static readonly LayoutAttrDef FontAttr = A("font", LayoutAttrKind.Font, GroupText, def: "font_text_3");
    private static readonly LayoutAttrDef TextKey = A("text", LayoutAttrKind.StringKey, GroupText);
    private static readonly LayoutAttrDef Sound = A("sound", LayoutAttrKind.Sound, GroupMisc);

    private static readonly Dictionary<string, LayoutTagDef> _tags = new(StringComparer.Ordinal);

    static LayoutSchema()
    {
        void Add(LayoutTagDef d) => _tags[d.Tag] = d;

        // ---------- 顶层 ----------
        Add(new LayoutTagDef
        {
            Tag = "Form", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("modal", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "false"),
                A("fade", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "false"),
                A("anim", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "false"),
                A("close", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "false"),
                A("text", LayoutAttrKind.StringKey, GroupWindow),
                Sound, DrawMode,
                A("bg_tex", LayoutAttrKind.Image, GroupImage)
            }
        });
        Add(new LayoutTagDef
        {
            Tag = "TmpWindow", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("text", LayoutAttrKind.StringKey, GroupWindow),
                A("modal", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "true"),
                A("fade", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "true"),
                A("anim", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "true"),
                A("close", LayoutAttrKind.Bool, GroupWindow, BoolOptions, "true"),
                A("style", LayoutAttrKind.Text, GroupWindow),
                A("font", LayoutAttrKind.Font, GroupWindow),
                Sound, DrawMode
            }
        });
        Add(new LayoutTagDef { Tag = "Template", Category = "Container", IsContainer = true });

        // ---------- 容器 ----------
        Add(new LayoutTagDef
        {
            Tag = "Group", Category = "Container", IsContainer = true,
            Attributes = new[] { A("image", LayoutAttrKind.Image, GroupImage), DrawMode },
            ImageKeys = new[] { "image" }
        });
        Add(new LayoutTagDef
        {
            Tag = "GroupBox", Category = "Container", IsContainer = true,
            Attributes = new[] { A("backimage", LayoutAttrKind.Image, GroupImage), DrawMode, ColorAttr, Clipped },
            ImageKeys = new[] { "backimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "HGroup", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("gap", LayoutAttrKind.Number, GroupGroup, def: "0"),
                A("hlayoutalign", LayoutAttrKind.Enum, GroupGroup, AlignHOptions, "left"),
                A("vlayoutalign", LayoutAttrKind.Enum, GroupGroup, AlignVOptions, "top")
            }
        });
        Add(new LayoutTagDef
        {
            Tag = "VGroup", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("gap", LayoutAttrKind.Number, GroupGroup, def: "0"),
                A("vlayoutalign", LayoutAttrKind.Enum, GroupGroup, AlignVOptions, "top"),
                A("hlayoutalign", LayoutAttrKind.Enum, GroupGroup, AlignHOptions, "left"),
                A("alpha", LayoutAttrKind.Number, GroupAppearance, def: "1")
            }
        });
        Add(new LayoutTagDef
        {
            Tag = "Repeater", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("itemwidth", LayoutAttrKind.Int, GroupRepeater),
                A("itemheight", LayoutAttrKind.Int, GroupRepeater),
                A("itemcount", LayoutAttrKind.Int, GroupRepeater, def: "0"),
                A("arrange", LayoutAttrKind.Bool, GroupRepeater, BoolOptions, "true"),
                A("border", LayoutAttrKind.Int, GroupRepeater),
                A("reverserender", LayoutAttrKind.Bool, GroupRepeater, BoolOptions, "false")
            }
        });
        Add(new LayoutTagDef
        {
            Tag = "PlaceHolder", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("templateid", LayoutAttrKind.TemplateId, GroupRepeater),
                A("backimage", LayoutAttrKind.Image, GroupImage)
            },
            ImageKeys = new[] { "backimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "ScrollViewer", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("scrollmode", LayoutAttrKind.Enum, GroupScroll, ScrollModeOptions, "vertical"),
                A("scrollbars", LayoutAttrKind.Bool, GroupScroll, BoolOptions, "false"),
                A("vscrollbarimage", LayoutAttrKind.Image, GroupScroll),
                A("hscrollbarimage", LayoutAttrKind.Image, GroupScroll),
                Clipped
            }
        });
        Add(new LayoutTagDef
        {
            Tag = "SlideList", Category = "Container", IsContainer = true,
            Attributes = new[]
            {
                A("ShowAmount", LayoutAttrKind.Int, GroupRepeater),
                A("GridWidth", LayoutAttrKind.Int, GroupRepeater),
                A("GridHeight", LayoutAttrKind.Int, GroupRepeater),
                A("UnitAlign", LayoutAttrKind.Text, GroupRepeater),
                A("MinScale", LayoutAttrKind.Number, GroupRepeater),
                A("ItemFade", LayoutAttrKind.Bool, GroupRepeater, BoolOptions),
                Clipped
            }
        });

        // ---------- 普通控件 ----------
        Add(new LayoutTagDef
        {
            Tag = "Image", Category = "Widget",
            Attributes = new[]
            {
                A("image", LayoutAttrKind.Image, GroupImage),
                A("texture", LayoutAttrKind.Image, GroupImage),
                DrawMode, ColorAttr, Scale,
                A("scalex", LayoutAttrKind.Number, GroupAppearance),
                A("rotate", LayoutAttrKind.Number, GroupAppearance),
                A("darkpressed", LayoutAttrKind.Bool, GroupMisc, BoolOptions)
            },
            ImageKeys = new[] { "image", "texture" }
        });
        Add(new LayoutTagDef
        {
            Tag = "Label", Category = "Widget",
            Attributes = new[]
            {
                FontAttr, TextKey,
                A("string", LayoutAttrKind.Text, GroupText),
                A("htextalign", LayoutAttrKind.Enum, GroupText, AlignHOptions, "center"),
                A("vtextalign", LayoutAttrKind.Enum, GroupText, AlignVOptions, "center"),
                A("breakwords", LayoutAttrKind.Bool, GroupText, BoolOptions, "false"),
                A("adaptive", LayoutAttrKind.Bool, GroupText, BoolOptions, "false"),
                A("verblank", LayoutAttrKind.Number, GroupText, def: "0"),
                A("horblank", LayoutAttrKind.Number, GroupText, def: "0"),
                A("linewidth", LayoutAttrKind.Int, GroupText),
                A("fontsize", LayoutAttrKind.Int, GroupText),
                A("sysfont", LayoutAttrKind.Bool, GroupText, BoolOptions),
                A("es", LayoutAttrKind.Font, GroupText),
                A("de", LayoutAttrKind.Font, GroupText),
                ColorAttr,
                A("backimage", LayoutAttrKind.Image, GroupImage),
                A("bkmargin", LayoutAttrKind.Margin, GroupImage),
                DrawMode
            },
            ImageKeys = new[] { "backimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "Button", Category = "Widget",
            Attributes = new[]
            {
                A("normalimage", LayoutAttrKind.Image, GroupImage),
                A("image", LayoutAttrKind.Image, GroupImage),
                DrawMode, Scale,
                TextKey, FontAttr,
                Sound,
                A("darkpressed", LayoutAttrKind.Bool, GroupMisc, BoolOptions),
                A("graywhendisabled", LayoutAttrKind.Bool, GroupMisc, BoolOptions)
            },
            ImageKeys = new[] { "normalimage", "image" }
        });
        Add(new LayoutTagDef
        {
            Tag = "Animation", Category = "Widget",
            Attributes = new[]
            {
                A("res", LayoutAttrKind.AnimRes, GroupAnimation),
                A("animvec", LayoutAttrKind.Text, GroupAnimation),
                A("play", LayoutAttrKind.Bool, GroupAnimation, BoolOptions, "true"),
                A("loop", LayoutAttrKind.Bool, GroupAnimation, BoolOptions, "true"),
                A("hideatend", LayoutAttrKind.Bool, GroupAnimation, BoolOptions, "false"),
                Scale
            },
            ImageKeys = new[] { "res" }
        });
        Add(new LayoutTagDef
        {
            Tag = "ProgressBar", Category = "Widget",
            Attributes = new[]
            {
                A("barimage", LayoutAttrKind.Image, GroupImage),
                A("backimage", LayoutAttrKind.Image, GroupImage),
                A("value", LayoutAttrKind.Number, GroupMisc, def: "0"),
                A("max", LayoutAttrKind.Number, GroupMisc, def: "100"),
                DrawMode
            },
            ImageKeys = new[] { "backimage", "barimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "Slider", Category = "Widget",
            Attributes = new[]
            {
                A("thumbimage", LayoutAttrKind.Image, GroupImage),
                A("barimage", LayoutAttrKind.Image, GroupImage),
                A("value", LayoutAttrKind.Number, GroupMisc, def: "0")
            },
            ImageKeys = new[] { "barimage", "thumbimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "EditBox", Category = "Widget",
            Attributes = new[]
            {
                A("text", LayoutAttrKind.Text, GroupText),
                A("fontsize", LayoutAttrKind.Int, GroupText, def: "30"),
                A("textpos", LayoutAttrKind.Text, GroupText),
                A("backimage", LayoutAttrKind.Image, GroupImage),
                DrawMode
            },
            ImageKeys = new[] { "backimage" }
        });
        Add(new LayoutTagDef
        {
            Tag = "TextBox", Category = "Widget",
            Attributes = new[]
            {
                FontAttr,
                A("MaxRows", LayoutAttrKind.Int, GroupText),
                A("RowBlank", LayoutAttrKind.Int, GroupText)
            }
        });

        // ---------- 游戏专用（编辑器只画占位框） ----------
        Add(new LayoutTagDef { Tag = "Battlefield", Category = "Game", Attributes = new[] { A("dir", LayoutAttrKind.Text, GroupMisc) } });
        Add(new LayoutTagDef { Tag = "MotionalArmy", Category = "Game", Attributes = new[] { Scale } });
        Add(new LayoutTagDef { Tag = "MotionalSoldier", Category = "Game", Attributes = new[] { Scale } });
        Add(new LayoutTagDef { Tag = "TacticalMap", Category = "Game", Attributes = new[] { Clipped } });
        Add(new LayoutTagDef { Tag = "LoadingPhoto", Category = "Game" });
    }

    /// <summary>全部已知标签（按定义顺序）。</summary>
    public static IReadOnlyCollection<LayoutTagDef> Tags => _tags.Values;

    /// <summary>可在控件树里新建的标签（不含只能放顶层的 Form / Template / TmpWindow）。</summary>
    public static readonly string[] CreatableTags =
    {
        "GroupBox", "Group", "HGroup", "VGroup", "Image", "Label", "Button", "Repeater", "PlaceHolder",
        "ScrollViewer", "Animation", "ProgressBar", "Slider", "EditBox", "TextBox", "SlideList"
    };

    /// <summary>可作为顶层表单新建的标签。</summary>
    public static readonly string[] TopLevelTags = { "Form", "TmpWindow", "Template" };

    public static bool IsKnown(string tag) => _tags.ContainsKey(tag);

    /// <summary>取标签定义；未知标签返回只有通用属性的占位定义（原样保留它的属性）。</summary>
    public static LayoutTagDef Get(string tag)
    {
        if (_tags.TryGetValue(tag, out var d)) return d;
        return new LayoutTagDef { Tag = tag, Category = "Game" };
    }

    public static bool IsContainer(string tag) => _tags.TryGetValue(tag, out var d) && d.IsContainer;
}
