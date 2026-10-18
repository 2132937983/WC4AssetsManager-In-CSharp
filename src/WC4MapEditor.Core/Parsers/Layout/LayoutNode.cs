using System.Globalization;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>
/// 布局节点（对应布局 XML 的一个元素）。属性保持书写顺序，未知属性原样保留，
/// 因此编辑后能无损回写。虚拟字段（模板展开 / Repeater 格子）用于运行时结构可视化。
/// </summary>
public class LayoutNode
{
    public string Tag { get; set; } = "";

    /// <summary>属性（保持原始书写顺序）。</summary>
    public List<KeyValuePair<string, string>> Attributes { get; } = new();

    public List<LayoutNode> Children { get; } = new();
    public LayoutNode? Parent { get; set; }

    /// <summary>元素内的文本（多数布局节点没有）。</summary>
    public string? Text { get; set; }

    // ---------- 运行时虚拟展开 ----------
    public bool IsVirtual { get; set; }
    public LayoutNode? Owner { get; set; }
    public bool IsCell { get; set; }
    /// <summary>PlaceHolder 由模板展开出来的子节点。</summary>
    public List<LayoutNode>? Expanded { get; set; }
    /// <summary>Repeater 的重复单元。</summary>
    public List<LayoutNode>? Items { get; set; }
    /// <summary>控件树折叠状态（仅 UI 用）。</summary>
    public bool Collapsed { get; set; }

    // ---------- 布局结果（每次重算被覆盖） ----------
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool HasRect { get; set; }

    // ---------- 属性读写 ----------

    public string? Get(string key)
    {
        foreach (var kv in Attributes)
            if (string.Equals(kv.Key, key, StringComparison.Ordinal)) return kv.Value;
        return null;
    }

    public bool Has(string key) => Get(key) != null;

    /// <summary>设置属性；值为空时删除该属性（与工具 setAttr 一致）。</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Attributes.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.Ordinal));
            return;
        }
        for (int i = 0; i < Attributes.Count; i++)
        {
            if (string.Equals(Attributes[i].Key, key, StringComparison.Ordinal))
            {
                Attributes[i] = new KeyValuePair<string, string>(key, value);
                return;
            }
        }
        Attributes.Add(new KeyValuePair<string, string>(key, value));
    }

    public double Num(string key)
    {
        var v = Get(key);
        if (v == null) return double.NaN;
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    public LayoutMargins Margins() => LayoutEngine.ParseMargins(Get("margin"));

    /// <summary>深拷贝（含子树），用于复制节点。</summary>
    public LayoutNode DeepClone()
    {
        var clone = new LayoutNode { Tag = Tag, Text = Text };
        foreach (var kv in Attributes) clone.Attributes.Add(kv);
        foreach (var c in Children)
        {
            var cc = c.DeepClone();
            cc.Parent = clone;
            clone.Children.Add(cc);
        }
        return clone;
    }

    /// <summary>克隆为虚拟节点（模板展开用），记录归属者。</summary>
    public LayoutNode CloneVirtual(LayoutNode owner)
    {
        var clone = new LayoutNode
        {
            Tag = Tag,
            Text = Text,
            IsVirtual = true,
            Owner = owner
        };
        foreach (var kv in Attributes) clone.Attributes.Add(kv);
        foreach (var c in Children)
        {
            var cc = c.CloneVirtual(owner);
            cc.Parent = clone;
            clone.Children.Add(cc);
        }
        return clone;
    }

    public override string ToString() => Tag + (Get("id") is { } id ? "#" + id : "");
}
