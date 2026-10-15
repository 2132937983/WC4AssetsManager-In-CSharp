using System.Text;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>
/// 布局文档：宽松 XML 解析 + canonic 序列化 + 表单/模板/虚拟展开管理。
/// 完全对应 layout_布局编辑器.html 的 parseXML / dumpXML / buildForms / buildExpansion。
/// </summary>
public class LayoutDocument
{
    public LayoutNode Root { get; private set; } = new() { Tag = "#root" };

    /// <summary>顶层表单（Form / Template / TmpWindow）。</summary>
    public List<LayoutNode> Forms { get; } = new();

    /// <summary>模板索引（Template 的 id → 节点）。</summary>
    public Dictionary<string, LayoutNode> Templates { get; } = new(StringComparer.Ordinal);

    public string? SourceName { get; set; }

    private static readonly HashSet<string> ContainerTags = new(StringComparer.Ordinal)
    {
        "Form", "Group", "GroupBox", "Template", "TmpWindow", "HGroup", "VGroup",
        "Repeater", "SlideList", "ScrollViewer", "PlaceHolder"
    };

    public static bool IsContainer(LayoutNode n) => ContainerTags.Contains(n.Tag);

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

    public static bool IsFalseValue(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        return t is "false" or "fasle" or "0" or "no" or "off";
    }

    // ============================================================= 解析 =============================================================

    public void Load(string text, string? name = null)
    {
        SourceName = name;
        Root = Parse(text);
        Forms.Clear();
        Templates.Clear();

        var level = Root.Children;
        if (level.Count == 1 && level[0].Tag == "Layouts") level = level[0].Children;
        foreach (var c in level)
            if (c.Tag is "Form" or "Template" or "TmpWindow") Forms.Add(c);

        foreach (var f in Forms)
            if (f.Tag == "Template" && f.Get("id") is { } tid) Templates[tid] = f;
    }

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
                if (t.Length > 0) cur.Text = (cur.Text ?? "") + t;
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
                PushAttr(name, val);
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

    // ============================================================= 序列化 =============================================================

    private static string Esc(string v) => v
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");

    public string Dump()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        foreach (var c in Root.Children) DumpNode(sb, c, 0);
        return sb.ToString();
    }

    private static void DumpNode(StringBuilder sb, LayoutNode node, int indent)
    {
        var pad = new string(' ', indent * 2);
        sb.Append(pad).Append('<').Append(node.Tag);
        foreach (var kv in node.Attributes) sb.Append(' ').Append(kv.Key).Append("=\"").Append(Esc(kv.Value)).Append('"');

        if (node.Children.Count == 0 && node.Text == null)
        {
            sb.Append(" />\n");
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

    /// <summary>为指定表单构建 PlaceHolder / Repeater 的运行时展开。</summary>
    public void BuildExpansion(LayoutNode node)
    {
        if (node.Tag == "PlaceHolder" && node.Get("templateid") is { } tplId)
        {
            node.Expanded = Templates.TryGetValue(tplId, out var tpl)
                ? tpl.Children.ConvertAll(c => c.CloneVirtual(node))
                : new List<LayoutNode>();
        }

        if (node.Tag == "Repeater")
        {
            int count = 0;
            if (int.TryParse(node.Get("itemcount"), out var ic)) count = Math.Max(0, ic);
            else count = node.Children.Count;

            node.Items = new List<LayoutNode>();
            for (int i = 0; i < count; i++)
            {
                var cell = new LayoutNode { Tag = "#cell", IsVirtual = true, Owner = node, IsCell = true };
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

    /// <summary>取控件主图引用（按控件类型的属性优先级），对应 imageRefOf。</summary>
    public static (string Ref, string Key)? ImageRefOf(LayoutNode n)
    {
        string[] list = n.Tag switch
        {
            "Button" => new[] { "normalimage", "normal", "image", "texture" },
            "CheckButton" => new[] { "normalimage", "normal", "image" },
            "Label" => new[] { "backimage", "image" },
            "Image" => new[] { "texture", "image", "res" },
            "ProgressBar" => new[] { "barimage", "image" },
            "Slider" => new[] { "thumbimage", "barimage", "image" },
            "Animation" => new[] { "texture", "image", "res" },
            _ => new[] { "normalimage", "backimage", "texture", "image", "res" }
        };
        foreach (var k in list)
        {
            var v = n.Get(k);
            if (!string.IsNullOrEmpty(v)) return (v, k);
        }
        return null;
    }
}
