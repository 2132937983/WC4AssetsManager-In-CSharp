using System.Text;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>
/// 布局文档：宽松 XML 解析 + 规范化序列化 + 表单 / 模板 / 虚拟展开管理。
/// <para>
/// 解析器刻意宽松：游戏的 layout.xml 里有未加引号的值、裸 id、<c>visible="fasle"</c> 这类手误，
/// 都要能读进来并原样写回（属性顺序与未知属性一律保留），编辑器才不会"修坏"原文件。
/// </para>
/// </summary>
public class LayoutDocument
{
    public LayoutNode Root { get; private set; } = new() { Tag = "#root" };

    /// <summary>顶层表单（Form / Template / TmpWindow）。</summary>
    public List<LayoutNode> Forms { get; } = new();

    /// <summary>模板索引（Template 的 id → 节点）。</summary>
    public Dictionary<string, LayoutNode> Templates { get; } = new(StringComparer.Ordinal);

    public string? SourceName { get; set; }

    /// <summary>来源文件完整路径（从 assets 或导入的文件加载时记录，用于"保存"覆盖原文件）。</summary>
    public string? SourcePath { get; set; }

    /// <summary>&lt;Layouts&gt; 根元素（没有时为 null，顶层直接是表单）。</summary>
    public LayoutNode? LayoutsElement { get; private set; }

    public static bool IsContainer(LayoutNode n) => LayoutSchema.IsContainer(n.Tag);

    /// <summary>正常参与布局的子节点（Repeater/PlaceHolder 的原始子节点是运行时模板，不单独摆放）。</summary>
    public static List<LayoutNode> RealKids(LayoutNode n)
        => (n.Tag == "Repeater" || n.Tag == "PlaceHolder") ? new List<LayoutNode>() : n.Children;

    /// <summary>运行时虚拟子节点：Repeater→格子；PlaceHolder→模板克隆；否则 null。</summary>
    public static List<LayoutNode>? ExpandedKids(LayoutNode n)
    {
        if (n.Tag == "Repeater") return n.Items;
        if (n.Tag == "PlaceHolder") return n.Expanded;
        return null;
    }

    public static bool VisibleKid(LayoutNode n)
    {
        var v = n.Get("visible");
        return !(v != null && IsFalseValue(v));
    }

    /// <summary>visible 等布尔属性的"假"写法，含游戏文件里出现过的手误。</summary>
    public static bool IsFalseValue(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        return t is "false" or "fasle" or "flase" or "0" or "no" or "off";
    }

    /// <summary>节点是否被 visible 属性显式隐藏。</summary>
    public static bool IsHidden(LayoutNode n) => n.Get("visible") is { } v && IsFalseValue(v);

    // ============================================================= 解析 =============================================================

    public void Load(string text, string? name = null, string? path = null)
    {
        SourceName = name;
        SourcePath = path;
        Root = Parse(text);
        RebuildIndex();
    }

    /// <summary>重建表单列表与模板索引（增删顶层表单后调用）。</summary>
    public void RebuildIndex()
    {
        Forms.Clear();
        Templates.Clear();
        LayoutsElement = null;

        var level = Root.Children;
        if (level.Count == 1 && level[0].Tag == "Layouts")
        {
            LayoutsElement = level[0];
            level = level[0].Children;
        }
        foreach (var c in level)
            if (c.Tag is "Form" or "Template" or "TmpWindow") Forms.Add(c);

        foreach (var f in Forms)
            if (f.Tag == "Template" && f.Get("id") is { } tid) Templates[tid] = f;
    }

    /// <summary>新建空文档（一个 &lt;Layouts&gt; 根）。</summary>
    public void NewEmpty(string name = "layout.xml")
    {
        SourceName = name;
        SourcePath = null;
        Root = new LayoutNode { Tag = "#root" };
        var layouts = new LayoutNode { Tag = "Layouts", Parent = Root };
        Root.Children.Add(layouts);
        RebuildIndex();
    }

    /// <summary>顶层表单的容器（&lt;Layouts&gt; 或根）。</summary>
    public LayoutNode TopLevelContainer => LayoutsElement ?? Root;

    /// <summary>宽松解析：容忍未加引号的值、裸 id、单引号、"fasle" 等。</summary>
    public static LayoutNode Parse(string text)
    {
        var root = new LayoutNode { Tag = "#root" };
        var cur = root;
        int i = 0, n = text.Length;

        while (i < n)
        {
            int lt = text.IndexOf('<', i);
            if (lt < 0) break;

            if (lt > i)
            {
                var t = text.Substring(i, lt - i).Trim();
                if (t.Length > 0) cur.Text = (cur.Text ?? "") + Unescape(t);
            }

            if (lt + 1 < n && text[lt + 1] == '?')
            {
                int e = text.IndexOf("?>", lt, StringComparison.Ordinal);
                i = e < 0 ? n : e + 2;
                continue;
            }
            if (lt + 3 < n && text[lt + 1] == '!' && text[lt + 2] == '-' && text[lt + 3] == '-')
            {
                int e = text.IndexOf("-->", lt, StringComparison.Ordinal);
                i = e < 0 ? n : e + 3;
                continue;
            }
            if (lt + 1 < n && text[lt + 1] == '!')
            {
                int e = text.IndexOf('>', lt);
                i = e < 0 ? n : e + 1;
                continue;
            }

            int gt = -1;
            char q = '\0';
            for (int k = lt + 1; k < n; k++)
            {
                char c = text[k];
                if (q != '\0') { if (c == q && text[k - 1] != '\\') q = '\0'; }
                else if (c == '"' || c == '\'') q = c;
                else if (c == '>') { gt = k; break; }
            }
            if (gt < 0) break;

            var head = text.Substring(lt + 1, gt - lt - 1);
            bool selfClose = head.EndsWith('/');
            if (selfClose) head = head.Substring(0, head.Length - 1);
            i = gt + 1;

            if (head.StartsWith('/'))
            {
                var name = head.Substring(1).Trim();
                if (cur.Parent != null && (name.Length == 0 || cur.Tag == name)) cur = cur.Parent;
                continue;
            }

            var node = ParseTagHead(head);
            node.Parent = cur;
            cur.Children.Add(node);
            if (!selfClose) cur = node;
        }

        return root;
    }

    private static LayoutNode ParseTagHead(string s)
    {
        var node = new LayoutNode();
        int k = 0;
        while (k < s.Length && !char.IsWhiteSpace(s[k])) k++;
        node.Tag = s.Substring(0, k);

        void PushAttr(string name, string val)
        {
            for (int idx = 0; idx < node.Attributes.Count; idx++)
            {
                if (string.Equals(node.Attributes[idx].Key, name, StringComparison.Ordinal))
                {
                    node.Attributes[idx] = new KeyValuePair<string, string>(name, val);
                    return;
                }
            }
            node.Attributes.Add(new KeyValuePair<string, string>(name, val));
        }

        while (k < s.Length)
        {
            while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
            if (k >= s.Length) break;

            int p = k;
            while (p < s.Length && s[p] != '=' && !char.IsWhiteSpace(s[p])) p++;
            var name = s.Substring(k, p - k);
            k = p;
            while (k < s.Length && char.IsWhiteSpace(s[k])) k++;

            if (k < s.Length && s[k] == '=')
            {
                k++;
                while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                string val;
                if (k < s.Length && (s[k] == '"' || s[k] == '\''))
                {
                    char qc = s[k++];
                    int e = s.IndexOf(qc, k);
                    val = e < 0 ? s.Substring(k) : s.Substring(k, e - k);
                    k = e < 0 ? s.Length : e + 1;
                }
                else
                {
                    int e = k;
                    while (e < s.Length && !char.IsWhiteSpace(s[e])) e++;
                    val = s.Substring(k, e - k);
                    k = e;
                }
                PushAttr(name, Unescape(val));
            }
            else if (name.Length > 0)
            {
                // 裸 token：未加引号的 id，或布尔属性
                if (node.Get("id") == null && name is not ("true" or "false")) PushAttr("id", name);
                else PushAttr(name, "true");
            }
        }

        return node;
    }

    private static string Unescape(string v)
    {
        if (v.IndexOf('&') < 0) return v;
        return v.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
    }

    // ============================================================= 序列化 =============================================================

    private static string Esc(string v) => v
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>规范化输出：UTF-8 声明 + Tab 缩进（与游戏原文件一致）。</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        foreach (var c in Root.Children) DumpNode(sb, c, 0);
        return sb.ToString();
    }

    private static void DumpNode(StringBuilder sb, LayoutNode node, int indent)
    {
        var pad = new string('\t', indent);
        sb.Append(pad).Append('<').Append(node.Tag);
        foreach (var kv in node.Attributes) sb.Append(' ').Append(kv.Key).Append("=\"").Append(Esc(kv.Value)).Append('"');

        if (node.Children.Count == 0 && node.Text == null)
        {
            sb.Append("/>\n");
            return;
        }

        sb.Append('>');
        if (node.Text != null) sb.Append(Esc(node.Text));
        if (node.Children.Count > 0)
        {
            sb.Append('\n');
            foreach (var c in node.Children) DumpNode(sb, c, indent + 1);
            sb.Append(pad);
        }
        sb.Append("</").Append(node.Tag).Append(">\n");
    }

    // ============================================================= 虚拟展开 =============================================================

    /// <summary>为指定节点（通常是表单）构建 PlaceHolder / Repeater 的运行时展开。</summary>
    public void BuildExpansion(LayoutNode node)
    {
        if (node.Tag == "PlaceHolder")
        {
            node.Expanded = node.Get("templateid") is { } tplId && Templates.TryGetValue(tplId, out var tpl)
                ? tpl.Children.ConvertAll(c => { var cc = c.CloneVirtual(node); cc.Parent = node; return cc; })
                : new List<LayoutNode>();
        }

        if (node.Tag == "Repeater")
        {
            int count = LayoutEngine.CellCount(node);
            // 游戏里 itemcount=0 的 Repeater 由运行时填充；编辑器至少展开一格，让模板子控件可见可选
            if (count == 0 && node.Children.Count > 0) count = 1;
            count = Math.Min(count, 400);   // 防止手滑写了巨大数字把界面卡死

            node.Items = new List<LayoutNode>();
            for (int i = 0; i < count; i++)
            {
                var cell = new LayoutNode { Tag = "#cell", IsVirtual = true, Owner = node, IsCell = true, Parent = node };
                foreach (var c in node.Children)
                {
                    var cc = c.CloneVirtual(node);
                    cc.Parent = cell;
                    cell.Children.Add(cc);
                }
                node.Items.Add(cell);
            }
        }

        var ex = node.Expanded ?? (IEnumerable<LayoutNode>?)node.Items ?? node.Children;
        foreach (var c in ex) BuildExpansion(c);
    }

    public void BuildExpansion()
    {
        foreach (var f in Forms) BuildExpansion(f);
    }

    /// <summary>取控件主图引用（按 <see cref="LayoutSchema"/> 里该标签的图片属性优先级）。</summary>
    public static (string Ref, string Key)? ImageRefOf(LayoutNode n)
    {
        var def = LayoutSchema.Get(n.Tag);
        foreach (var k in def.ImageKeys)
        {
            var v = n.Get(k);
            if (!string.IsNullOrEmpty(v)) return (v, k);
        }
        return null;
    }

    // ============================================================= 遍历 / 定位 =============================================================

    /// <summary>深度优先枚举真实（非虚拟）节点。</summary>
    public static IEnumerable<LayoutNode> Walk(LayoutNode n)
    {
        yield return n;
        foreach (var c in n.Children)
            foreach (var d in Walk(c)) yield return d;
    }

    /// <summary>节点在文档里的路径（各级子节点下标），用于撤销后重新定位选中项。</summary>
    public List<int> PathOf(LayoutNode n)
    {
        var path = new List<int>();
        var cur = n;
        while (cur.Parent != null)
        {
            int idx = cur.Parent.Children.IndexOf(cur);
            if (idx < 0) break;
            path.Insert(0, idx);
            cur = cur.Parent;
        }
        return path;
    }

    public LayoutNode? NodeAt(IReadOnlyList<int> path)
    {
        var cur = Root;
        foreach (var idx in path)
        {
            if (idx < 0 || idx >= cur.Children.Count) return null;
            cur = cur.Children[idx];
        }
        return ReferenceEquals(cur, Root) ? null : cur;
    }

    /// <summary>所属顶层表单。</summary>
    public LayoutNode? FormOf(LayoutNode n)
    {
        var cur = n;
        while (cur != null)
        {
            if (Forms.Contains(cur)) return cur;
            cur = cur.Parent;
        }
        return null;
    }
}
