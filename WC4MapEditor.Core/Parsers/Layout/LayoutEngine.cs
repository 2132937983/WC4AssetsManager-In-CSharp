using System.Globalization;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>margin 解析结果（XML 顺序 L,T,R,B）。</summary>
public struct LayoutMargins
{
    public double L, T, R, B;
}

/// <summary>
/// 布局引擎：忠实还原 WC4 的 CWidget::Arrange / CGroup::ArrangeChildren 语义
/// （见 layout_布局编辑器.html 的 margins / naturalSize / layout）。
/// 图片尺寸与文本测量通过委托注入，保证 Core 层不依赖任何绘图库。
/// </summary>
public static class LayoutEngine
{
    /// <summary>解析图片引用 → 原始像素尺寸；找不到返回 null。</summary>
    public delegate (double w, double h)? ImageSizeLookup(string imageRef);

    /// <summary>测量文本宽度（给定字号）。</summary>
    public delegate double TextMeasure(string text, double fontSize);

    /// <summary>布局上下文（可选）。</summary>
    public sealed class Context
    {
        public ImageSizeLookup? ImageSize { get; set; }
        public TextMeasure? MeasureText { get; set; }
        public double DefaultFontSize { get; set; } = 24;
        /// <summary>本地化键 → 文本（text 属性查表）；无表时返回 null。</summary>
        public Func<string, string?>? StringLookup { get; set; }
        /// <summary>字体名 → 字号（font 属性查元数据）；无元数据时返回 null。</summary>
        public Func<string, double>? FontSizeLookup { get; set; }
    }

    private static readonly Dictionary<string, int> AlignH = new(StringComparer.OrdinalIgnoreCase)
    { ["left"] = 0, ["center"] = 1, ["right"] = 2 };

    private static readonly Dictionary<string, int> AlignV = new(StringComparer.OrdinalIgnoreCase)
    { ["top"] = 0, ["center"] = 1, ["bottom"] = 2 };

    /// <summary>
    /// margin：逗号分割 1~4 个值。
    /// 1 值 → 四值相同；2 值 → (L,T,0,0)；3/4 值 → 按 XML 书写顺序 L,T,R,B。
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

    private static int AlignOf(Dictionary<string, int> map, string? v)
        => v != null && map.TryGetValue(v, out var i) ? i : 0;

    // ============================================================= naturalSize =============================================================

    public static (double w, double h) NaturalSize(LayoutNode n, double cw, double ch, Context? ctx = null)
    {
        double w = n.Num("width");
        double h = n.Num("height");

        if (double.IsNaN(w) || double.IsNaN(h))
        {
            double nw = 0, nh = 0;
            var ir = LayoutDocument.ImageRefOf(n);
            bool hadImage = false;

            if (ir != null)
            {
                var size = ctx?.ImageSize?.Invoke(ir.Value.Ref);
                if (size is { } s && s.w > 0)
                {
                    hadImage = true;
                    var dm = (n.Get("drawmode") ?? "").ToLowerInvariant();
                    bool fillW = dm is "hextend" or "extend" or "stretch" or "tile";
                    bool fillH = dm is "vextend" or "extend" or "stretch" or "tile";
                    nw = fillW ? Math.Max(0, cw) : s.w;
                    nh = fillH ? Math.Max(0, ch) : s.h;

                    var sc = n.Get("scale");
                    if (sc != null)
                    {
                        var p = sc.Split(',');
                        double sx = p.Length > 0 && double.TryParse(p[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 1;
                        double sy = p.Length > 1 && double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var b) ? b : sx;
                        nw *= sx == 0 ? 1 : sx;
                        nh *= sy == 0 ? 1 : sy;
                    }
                }
            }

            if (n.Tag is "Label" or "TextBox" or "EditBox")
            {
                double fontSize = FontSizeOf(n, ctx);
                string text = TextOf(n, ctx);
                double tw = ctx?.MeasureText?.Invoke(text, fontSize) ?? text.Length * fontSize * 0.6;
                nw = Math.Max(nw, Math.Ceiling(tw) + 6);
                nh = Math.Max(nh, fontSize * 1.15 + 4);
            }

            if (n.Tag is "HGroup" or "VGroup")
            {
                double gap = n.Num("gap");
                if (double.IsNaN(gap)) gap = 0;
                var kids = LayoutDocument.RealKids(n);
                double sum = 0, maxW = 0, maxH = 0;
                for (int i = 0; i < kids.Count; i++)
                {
                    var cs = NaturalSize(kids[i], cw, ch, ctx);
                    if (n.Tag == "HGroup") { sum += cs.w + (i > 0 ? gap : 0); maxH = Math.Max(maxH, cs.h); }
                    else { sum += cs.h + (i > 0 ? gap : 0); maxW = Math.Max(maxW, cs.w); }
                }
                nw = Math.Max(nw, n.Tag == "HGroup" ? sum : cw);
                nh = Math.Max(nh, n.Tag == "VGroup" ? sum : ch);
            }
            else if (n.Tag == "Repeater")
            {
                nw = Math.Max(nw, cw);
                int count = ParseIntOr(n.Get("itemcount"), LayoutDocument.RealKids(n).Count);
                if (count == 0) count = 1;
                double iw = n.Num("itemwidth"), ih = n.Num("itemheight");
                int cols = double.IsNaN(iw) ? 1 : Math.Max(1, (int)Math.Round(cw / iw));
                int rows = (int)Math.Ceiling(count / (double)cols);
                nh = Math.Max(nh, double.IsNaN(ih) ? Math.Max(0, ch) : rows * ih);
            }
            else if (n.Tag == "PlaceHolder")
            {
                var ex = LayoutDocument.ExpandedKids(n);
                if (ex != null && ex.Count > 0)
                {
                    double mx = 0, my = 0;
                    foreach (var c in ex)
                    {
                        var cs = NaturalSize(c, cw, ch, ctx);
                        mx = Math.Max(mx, cs.w);
                        my = Math.Max(my, cs.h);
                    }
                    nw = Math.Max(nw, Math.Min(cw, mx));
                    nh = Math.Max(nh, Math.Min(ch, my));
                }
            }
            else if (LayoutDocument.IsContainer(n) || (!hadImage && n.Children.Count > 0))
            {
                if (LayoutDocument.RealKids(n).Count > 0 || n.Expanded != null)
                {
                    nw = Math.Max(nw, cw);
                    nh = Math.Max(nh, ch);
                }
            }

            if (double.IsNaN(w)) w = Math.Max(0, nw);
            if (double.IsNaN(h)) h = Math.Max(0, nh);
        }

        double minw = n.Num("minwidth"), maxw = n.Num("maxwidth");
        double minh = n.Num("minheight"), maxh = n.Num("maxheight");
        if (!double.IsNaN(minw)) w = Math.Max(w, minw);
        if (!double.IsNaN(maxw)) w = Math.Min(w, maxw);
        if (!double.IsNaN(minh)) h = Math.Max(h, minh);
        if (!double.IsNaN(maxh)) h = Math.Min(h, maxh);

        return (Math.Max(1, w), Math.Max(1, h));
    }

    private static int ParseIntOr(string? s, int def)
        => int.TryParse(s, out var v) ? v : def;

    // ============================================================= layout =============================================================

    /// <summary>在父内容框 (x0,y0,W,H) 内摆放 n，并把结果写入 n 的 X/Y/W/H。</summary>
    public static void Layout(LayoutNode n, double x0, double y0, double W, double H,
        (double x, double y)? packCursor = null, Context? ctx = null)
    {
        var m = n.Margins();
        double cw = Math.Max(0, W - m.L - m.R);
        double ch = Math.Max(0, H - m.B - m.T);
        var ns = NaturalSize(n, cw, ch, ctx);
        double w = ns.w, h = ns.h;

        int ha = AlignOf(AlignH, n.Get("halign"));
        int va = AlignOf(AlignV, n.Get("valign"));
        double cx = x0 + m.L, cy = y0 + m.T;

        double ax = ha == 0 ? 0 : ha == 2 ? cw - w : (cw - w) / 2;
        double ay = va == 0 ? 0 : va == 2 ? ch - h : (ch - h) / 2;

        double x, y;
        if (packCursor is { } pc)
        {
            x = cx + ax + pc.x;
            y = cy + ay + pc.y;
        }
        else
        {
            x = ha == 0 ? cx : ha == 2 ? x0 + W - m.R - w : x0 + m.L + (W - m.R - m.L - w) / 2;
            y = va == 0 ? cy : va == 2 ? y0 + H - m.B - h : y0 + m.T + (H - m.B - m.T - h) / 2;
        }

        n.X = x; n.Y = y; n.W = w; n.H = h;
        n.HasRect = true;

        var exKids = LayoutDocument.ExpandedKids(n);
        if (n.Children.Count == 0 && (exKids == null || exKids.Count == 0)) return;

        if (n.Tag is "HGroup" or "VGroup")
        {
            double gap = n.Num("gap");
            if (double.IsNaN(gap)) gap = 0;

            double total = 0;
            foreach (var c in n.Children)
            {
                if (!LayoutDocument.VisibleKid(c)) continue;
                var cs = NaturalSize(c, w, h, ctx);
                total += (n.Tag == "HGroup" ? cs.w : cs.h) + 0; // gap 在下面按位累加
            }
            // 重新按含 gap 的方式累计（与工具一致：第 0 个不加 gap）
            total = 0;
            for (int i = 0; i < n.Children.Count; i++)
            {
                if (!LayoutDocument.VisibleKid(n.Children[i])) continue;
                var cs = NaturalSize(n.Children[i], w, h, ctx);
                total += (n.Tag == "HGroup" ? cs.w : cs.h) + (i > 0 ? gap : 0);
            }

            var align = n.Get(n.Tag == "HGroup" ? "hlayoutalign" : "vlayoutalign");
            double start = 0;
            if (align is "center" or "middle") start = (n.Tag == "HGroup" ? w - total : h - total) / 2;
            else if ((n.Tag == "HGroup" && align == "right") || (n.Tag == "VGroup" && align == "bottom"))
                start = n.Tag == "HGroup" ? w - total : h - total;

            double cur = start;
            foreach (var c in LayoutDocument.RealKids(n))
            {
                if (!LayoutDocument.VisibleKid(c))
                {
                    Layout(c, x, y, w, h, (0, 0), ctx);
                    c.W = 0; c.H = 0;
                    continue;
                }
                var cs = NaturalSize(c, w, h, ctx);
                var off = n.Tag == "HGroup" ? (cur, 0.0) : (0.0, cur);
                Layout(c, x, y, w, h, off, ctx);
                cur += (n.Tag == "HGroup" ? cs.w : cs.h) + gap;
            }
        }
        else if (n.Tag == "Repeater")
        {
            var items = n.Items ?? new List<LayoutNode>();
            double iw0 = n.Num("itemwidth"), ih0 = n.Num("itemheight");
            int cols = double.IsNaN(iw0) ? 1 : Math.Max(1, (int)Math.Round(w / iw0));
            double iw = double.IsNaN(iw0) ? w : w / cols;
            int rows = Math.Max(1, (int)Math.Ceiling(items.Count / (double)cols));
            double ih = double.IsNaN(ih0) ? h / rows : ih0;

            for (int i = 0; i < items.Count; i++)
            {
                var cell = items[i];
                double ccx = x + (i % cols) * iw;
                double ccy = y + Math.Floor(i / (double)cols) * ih;
                cell.X = ccx; cell.Y = ccy; cell.W = iw; cell.H = ih; cell.HasRect = true;
                foreach (var c in cell.Children) Layout(c, ccx, ccy, iw, ih, null, ctx);
            }
        }
        else
        {
            var list = exKids ?? n.Children;
            foreach (var c in list) Layout(c, x, y, w, h, null, ctx);
        }
    }

    // ============================================================= 文本 =============================================================

    public static string TextOf(LayoutNode n, Context? ctx = null)
    {
        var s = n.Get("string");
        if (s != null) return s;
        var t = n.Get("text");
        if (t == null) return "";
        if (ctx?.StringLookup != null)
        {
            var localized = ctx.StringLookup(t);
            if (localized != null) return localized;
        }
        return t;
    }

    public static double FontSizeOf(LayoutNode n, Context? ctx)
    {
        var fontName = n.Get("font");
        if (ctx?.FontSizeLookup != null && !string.IsNullOrEmpty(fontName))
        {
            var size = ctx.FontSizeLookup(fontName);
            if (size > 0) return size;
        }
        return ctx?.DefaultFontSize ?? 24;
    }
}
