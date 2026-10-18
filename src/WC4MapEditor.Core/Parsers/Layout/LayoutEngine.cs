using System.Globalization;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>margin 解析结果（XML 顺序 L,T,R,B）。</summary>
public struct LayoutMargins
{
    public double L, T, R, B;

    public override string ToString()
        => string.Join(",", new[] { L, T, R, B }.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)));
}

/// <summary>
/// 布局引擎：按 WC4 布局 XML 的实际语义求每个控件的矩形。
/// <para>
/// 规则全部来自对 assets/layout_x.xml 的归纳（引擎本身不可见），要点：
/// </para>
/// <list type="bullet">
/// <item>margin 是 WPF 式的 L,T,R,B；2 个值 = L,T；3 个值 = L,T,R；1 个值四边相同。
///   右对齐用 R、下对齐用 B；居中时 L/R 之差让中心偏移一半（gbox_modes margin=0,180 居中后恰好落在屏幕里）。</item>
/// <item>没写 halign/valign 时默认 left/top（<c>margin="12,28"</c> 这类写法就是左上角偏移）。</item>
/// <item>没写宽/高的控件<b>填满</b>父级内容框（Label、GroupBox、Group、Form…）；
///   只有带可解析贴图的 Image / Button / Animation 用贴图的自然尺寸（乘 scale）。
///   例：30×30 的红点里放一个无尺寸无对齐的 Label "3"，必须填满红点、文字居中才正确。</item>
/// <item>Label 文字默认水平、垂直都居中；htextalign / vtextalign 覆盖。</item>
/// <item>HGroup / VGroup 沿主轴按 gap 排列子项（忽略子项在主轴上的对齐），hlayoutalign / vlayoutalign 决定整排在组内的位置。</item>
/// <item>Repeater 按 itemwidth 把宽度分成 floor(w/itemwidth) 列，逐行摆单元；arrange=false 的由运行时摆放，只画第一格。</item>
/// </list>
/// 图片尺寸与文本测量通过委托注入，Core 层不依赖任何绘图库。
/// </summary>
public static class LayoutEngine
{
    /// <summary>解析图片引用 → 原始像素尺寸；找不到返回 null。</summary>
    public delegate (double w, double h)? ImageSizeLookup(string imageRef);

    /// <summary>测量文本宽度（给定字号）。</summary>
    public delegate double TextMeasure(string text, double fontSize);

    /// <summary>布局上下文。</summary>
    public sealed class Context
    {
        public ImageSizeLookup? ImageSize { get; set; }
        public TextMeasure? MeasureText { get; set; }
        public double DefaultFontSize { get; set; } = 21;

        /// <summary>stringtable 语言代码（cn / tw / en / ja / ko / de / es），决定 Label 的 es / de 字体覆盖。</summary>
        public string Language { get; set; } = "tw";

        /// <summary>本地化键 → 文本（text 属性查表）；无表时返回 null。</summary>
        public Func<string, string?>? StringLookup { get; set; }

        /// <summary>字体名 → 字号；无元数据时返回 0。</summary>
        public Func<string, double>? FontSizeLookup { get; set; }
    }

    private static readonly Dictionary<string, int> AlignH = new(StringComparer.OrdinalIgnoreCase)
    { ["left"] = 0, ["center"] = 1, ["middle"] = 1, ["right"] = 2, ["lift"] = 0 };

    private static readonly Dictionary<string, int> AlignV = new(StringComparer.OrdinalIgnoreCase)
    { ["top"] = 0, ["center"] = 1, ["middle"] = 1, ["bottom"] = 2 };

    /// <summary>
    /// margin：逗号分割 1~4 个值。1 值 → 四边相同；2 值 → (L,T)；3 值 → (L,T,R)；4 值 → L,T,R,B。
    /// 空段（如 "-44,-6,"）按 0。
    /// </summary>
    public static LayoutMargins ParseMargins(string? raw)
    {
        var v = new LayoutMargins();
        if (string.IsNullOrEmpty(raw)) return v;

        var parts = raw.Split(',');
        var nums = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            nums[i] = double.TryParse(parts[i].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;

        if (nums.Length == 1) { v.L = v.T = v.R = v.B = nums[0]; }
        else
        {
            v.L = nums.Length > 0 ? nums[0] : 0;
            v.T = nums.Length > 1 ? nums[1] : 0;
            v.R = nums.Length > 2 ? nums[2] : 0;
            v.B = nums.Length > 3 ? nums[3] : 0;
        }
        return v;
    }

    /// <summary>水平对齐：0 左 / 1 中 / 2 右；未写或写错按左。</summary>
    public static int HAlignOf(string? v) => v != null && AlignH.TryGetValue(v.Trim(), out var i) ? i : 0;

    /// <summary>垂直对齐：0 上 / 1 中 / 2 下；未写或写错按上。</summary>
    public static int VAlignOf(string? v) => v != null && AlignV.TryGetValue(v.Trim(), out var i) ? i : 0;

    /// <summary>drawmode 是否沿水平方向铺满控件。</summary>
    public static bool FillsWidth(string? drawmode)
        => (drawmode ?? "").ToLowerInvariant() is "hextend" or "extend" or "stretch" or "tile";

    /// <summary>drawmode 是否沿垂直方向铺满控件。</summary>
    public static bool FillsHeight(string? drawmode)
        => (drawmode ?? "").ToLowerInvariant() is "vextend" or "extend" or "stretch" or "tile";

    /// <summary>scale 属性 → (sx, sy)；"0.9" 或 "1,0.5"；缺省 1。</summary>
    public static (double sx, double sy) ScaleOf(LayoutNode n)
    {
        var sc = n.Get("scale");
        if (string.IsNullOrWhiteSpace(sc)) return (1, 1);
        var p = sc.Split(',');
        double sx = p.Length > 0 && double.TryParse(p[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 1;
        double sy = p.Length > 1 && double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var b) ? b : sx;
        if (sx == 0) sx = 1;
        if (sy == 0) sy = 1;
        return (Math.Abs(sx), Math.Abs(sy));
    }

    // ============================================================= naturalSize =============================================================

    /// <summary>
    /// 控件在 (cw, ch) 的可用空间里的尺寸：写了 width/height 就用它；否则按标签规则算（见类注释）。
    /// </summary>
    public static (double w, double h) NaturalSize(LayoutNode n, double cw, double ch, Context? ctx = null)
    {
        double w = n.Num("width");
        double h = n.Num("height");
        cw = Math.Max(0, cw);
        ch = Math.Max(0, ch);

        if (double.IsNaN(w) || double.IsNaN(h))
        {
            double nw = cw, nh = ch;   // 默认：填满

            switch (n.Tag)
            {
                case "Image":
                case "Button":
                case "Animation":
                {
                    var ir = LayoutDocument.ImageRefOf(n);
                    var size = ir != null ? ctx?.ImageSize?.Invoke(ir.Value.Ref) : null;
                    if (size is { } s && s.w > 0 && s.h > 0)
                    {
                        var dm = n.Get("drawmode");
                        var (sx, sy) = ScaleOf(n);
                        nw = FillsWidth(dm) ? cw : s.w * sx;
                        nh = FillsHeight(dm) ? ch : s.h * sy;
                    }
                    break;
                }
                case "Repeater":
                {
                    // 高度：有 itemheight 且有单元数时按行数算，否则填满
                    double iw = n.Num("itemwidth"), ih = n.Num("itemheight");
                    int count = CellCount(n);
                    if (!double.IsNaN(ih) && count > 0)
                    {
                        double usableW = double.IsNaN(w) ? cw : w;
                        int cols = double.IsNaN(iw) || iw <= 0 ? 1 : Math.Max(1, (int)Math.Floor(usableW / iw + 1e-6));
                        int rows = (int)Math.Ceiling(count / (double)cols);
                        nh = rows * ih;
                    }
                    break;
                }
            }

            if (double.IsNaN(w)) w = nw;
            if (double.IsNaN(h)) h = nh;
        }

        return (Math.Max(0, w), Math.Max(0, h));
    }

    /// <summary>Repeater 的单元数：itemcount 优先，否则按原始子节点数。</summary>
    public static int CellCount(LayoutNode n)
    {
        if (int.TryParse(n.Get("itemcount"), out var ic)) return Math.Max(0, ic);
        return n.Children.Count;
    }

    // ============================================================= layout =============================================================

    /// <summary>在父内容框 (x0,y0,W,H) 内摆放 n（按 margin + halign/valign），并递归摆放子项。</summary>
    public static void Layout(LayoutNode n, double x0, double y0, double W, double H, Context? ctx = null)
    {
        var m = n.Margins();
        double cw = W - m.L - m.R;
        double ch = H - m.T - m.B;
        var (w, h) = NaturalSize(n, cw, ch, ctx);

        int ha = HAlignOf(n.Get("halign"));
        int va = VAlignOf(n.Get("valign"));

        double x = ha switch
        {
            2 => x0 + W - m.R - w,
            1 => x0 + m.L + (cw - w) / 2,
            _ => x0 + m.L
        };
        double y = va switch
        {
            2 => y0 + H - m.B - h,
            1 => y0 + m.T + (ch - h) / 2,
            _ => y0 + m.T
        };

        Place(n, x, y, w, h, ctx);
    }

    /// <summary>把 n 放到绝对矩形并递归摆放子项。</summary>
    private static void Place(LayoutNode n, double x, double y, double w, double h, Context? ctx)
    {
        n.X = x; n.Y = y; n.W = w; n.H = h;
        n.HasRect = true;

        switch (n.Tag)
        {
            case "HGroup":
            case "VGroup":
                LayoutLinear(n, ctx);
                return;
            case "Repeater":
                LayoutRepeater(n, ctx);
                return;
        }

        var kids = LayoutDocument.ExpandedKids(n) ?? n.Children;
        foreach (var c in kids) Layout(c, x, y, w, h, ctx);
    }

    /// <summary>
    /// HGroup / VGroup：子项沿主轴依次排列（主轴忽略子项自己的对齐，用组的 hlayoutalign / vlayoutalign 决定整排位置），
    /// 交叉轴仍由子项自己的 halign/valign 决定。隐藏子项不占位。
    /// </summary>
    private static void LayoutLinear(LayoutNode n, Context? ctx)
    {
        bool horizontal = n.Tag == "HGroup";
        double gap = n.Num("gap");
        if (double.IsNaN(gap)) gap = 0;

        var kids = LayoutDocument.RealKids(n);
        var sizes = new List<(LayoutNode c, LayoutMargins m, double w, double h)>();
        double total = 0;
        foreach (var c in kids)
        {
            if (!LayoutDocument.VisibleKid(c))
            {
                // 隐藏项：仍给个矩形（方便在树里选中），但不占主轴空间
                Layout(c, n.X, n.Y, n.W, n.H, ctx);
                c.W = 0; c.H = 0;
                continue;
            }
            var m = c.Margins();
            var (w, h) = NaturalSize(c, n.W - m.L - m.R, n.H - m.T - m.B, ctx);
            sizes.Add((c, m, w, h));
            total += (horizontal ? w + m.L + m.R : h + m.T + m.B);
        }
        if (sizes.Count > 1) total += gap * (sizes.Count - 1);

        double extent = horizontal ? n.W : n.H;
        int align = horizontal ? HAlignOf(n.Get("hlayoutalign")) : VAlignOf(n.Get("vlayoutalign"));
        double cur = align switch
        {
            2 => extent - total,
            1 => (extent - total) / 2,
            _ => 0
        };

        foreach (var (c, m, w, h) in sizes)
        {
            double x, y;
            if (horizontal)
            {
                x = n.X + cur + m.L;
                int va = VAlignOf(c.Get("valign"));
                y = va switch
                {
                    2 => n.Y + n.H - m.B - h,
                    1 => n.Y + m.T + (n.H - m.T - m.B - h) / 2,
                    _ => n.Y + m.T
                };
                cur += w + m.L + m.R + gap;
            }
            else
            {
                y = n.Y + cur + m.T;
                int ha = HAlignOf(c.Get("halign"));
                x = ha switch
                {
                    2 => n.X + n.W - m.R - w,
                    1 => n.X + m.L + (n.W - m.L - m.R - w) / 2,
                    _ => n.X + m.L
                };
                cur += h + m.T + m.B + gap;
            }
            Place(c, x, y, w, h, ctx);
        }
    }

    /// <summary>Repeater：按 itemwidth 分列、itemheight 分行摆放运行时单元；arrange=false 时只摆第一格。</summary>
    private static void LayoutRepeater(LayoutNode n, Context? ctx)
    {
        var items = n.Items ?? new List<LayoutNode>();
        if (items.Count == 0) return;

        double iw0 = n.Num("itemwidth"), ih0 = n.Num("itemheight");
        int cols = double.IsNaN(iw0) || iw0 <= 0 ? 1 : Math.Max(1, (int)Math.Floor(n.W / iw0 + 1e-6));
        double iw = double.IsNaN(iw0) || iw0 <= 0 ? n.W : iw0;
        int rows = Math.Max(1, (int)Math.Ceiling(items.Count / (double)cols));
        double ih = double.IsNaN(ih0) || ih0 <= 0 ? n.H / rows : ih0;

        bool arrange = !LayoutDocument.IsFalseValue(n.Get("arrange") ?? "true");

        for (int i = 0; i < items.Count; i++)
        {
            var cell = items[i];
            if (!arrange && i > 0)
            {
                // 运行时自行摆放的单元：没有可预测的位置，只留第一格做模板预览
                cell.HasRect = false;
                foreach (var c in cell.Children) ClearRect(c);
                continue;
            }
            double cx = n.X + (i % cols) * iw;
            double cy = n.Y + (i / cols) * ih;
            cell.X = cx; cell.Y = cy; cell.W = iw; cell.H = ih; cell.HasRect = true;
            foreach (var c in cell.Children) Layout(c, cx, cy, iw, ih, ctx);
        }
    }

    private static void ClearRect(LayoutNode n)
    {
        n.HasRect = false;
        foreach (var c in n.Children) ClearRect(c);
        if (n.Expanded != null) foreach (var c in n.Expanded) ClearRect(c);
        if (n.Items != null) foreach (var c in n.Items) ClearRect(c);
    }

    // ============================================================= 文本 =============================================================

    /// <summary>显示文本：string 属性优先（字面量），否则 text 属性查 stringtable，查不到显示键名。</summary>
    public static string TextOf(LayoutNode n, Context? ctx = null)
    {
        var s = n.Get("string");
        if (s != null) return s;
        var t = n.Get("text");
        if (string.IsNullOrEmpty(t)) return "";
        if (ctx?.StringLookup != null)
        {
            var localized = ctx.StringLookup(t);
            if (localized != null) return localized;
        }
        return t;
    }

    /// <summary>
    /// 控件实际使用的字体名：Label 可以用 es / de 属性为西语 / 德语指定替代字体；
    /// Button 自带文字时默认 font_text_3（stringtable 注释："font_text_3 group title, buttons"）。
    /// </summary>
    public static string FontNameOf(LayoutNode n, Context? ctx)
    {
        string lang = ctx?.Language ?? "tw";
        if ((lang == "es" || lang == "de") && n.Get(lang) is { Length: > 0 } langFont) return langFont;
        var f = n.Get("font");
        if (!string.IsNullOrEmpty(f)) return f;
        return n.Tag == "Button" ? "font_text_3" : "font_text_2";
    }

    public static double FontSizeOf(LayoutNode n, Context? ctx)
    {
        // EditBox 直接写 fontsize
        double fs = n.Num("fontsize");
        if (!double.IsNaN(fs) && fs > 0) return fs;

        var fontName = FontNameOf(n, ctx);
        if (ctx?.FontSizeLookup != null)
        {
            var size = ctx.FontSizeLookup(fontName);
            if (size > 0) return size;
        }
        return ctx?.DefaultFontSize ?? 21;
    }
}
