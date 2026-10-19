using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views.Dialogs;

/// <summary>新建科技节点时用户的选择：前置节点（null = 没有前置）与科技 Type。</summary>
public sealed record TechNodeChoice(CountryTechData? Parent, int Type);

/// <summary>
/// 在科技树空格子上新增节点的对话框：选前置节点（当前分类里的节点）、输入 Type。
/// Type 在同一分类内必须唯一，重复时不能确认。
/// </summary>
public static class TechNodeDialog
{
    /// <param name="candidates">当前分类的节点（可选前置）。</param>
    /// <param name="preselected">默认选中的前置（例如画布上选中的节点），可为 null。</param>
    /// <param name="typeName">Type 对应的 INI 名称，输入时显示在旁边帮助确认。</param>
    public static TechNodeChoice? Show(Window? owner, int categoryId, int level, int position, IReadOnlyList<CountryTechData> candidates,
        CountryTechData? preselected, int suggestedType, Func<int, bool> isTypeTaken, Func<int, string> typeName)
    {
        TechNodeChoice? result = null;
        var window = EditorDialogs.CreateWindow(owner, Loc.F("CountryTech.NodeDialog.Title", level, position), 460);
        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        var info = EditorTheme.Muted(Loc.F("CountryTech.NodeDialog.Info", categoryId));
        info.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(info);

        root.Children.Add(EditorTheme.FieldLabel(Loc.T("CountryTech.NodeDialog.Parent")));
        var parent = EditorTheme.ComboBox();
        parent.Margin = new Thickness(0, 4, 0, 12);
        parent.Items.Add(new ParentItem(null, Loc.T("CountryTech.NodeDialog.NoParent")));
        foreach (var node in candidates.OrderBy(n => n.ResearchLv).ThenBy(n => n.Position))
            parent.Items.Add(new ParentItem(node, $"[{node.Id}] {typeName(node.Type)}  (L{node.ResearchLv}, P{node.Position})"));
        parent.SelectedIndex = Math.Max(0, parent.Items.Cast<ParentItem>().ToList().FindIndex(p => ReferenceEquals(p.Node, preselected)));
        root.Children.Add(parent);

        root.Children.Add(EditorTheme.FieldLabel(Loc.T("CountryTech.NodeDialog.Type")));
        var type = new NumericUpDown { MinValue = int.MinValue, MaxValue = int.MaxValue, Increment = 1, Value = suggestedType, Margin = new Thickness(0, 4, 0, 4) };
        root.Children.Add(type);
        var typeInfo = EditorTheme.Muted("");
        typeInfo.TextWrapping = TextWrapping.Wrap;
        typeInfo.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(typeInfo);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) =>
        {
            result = new TechNodeChoice((parent.SelectedItem as ParentItem)?.Node, (int)Math.Round(type.Value));
            window.Close();
        });
        ok.MinWidth = 88;
        ok.IsDefault = true;
        var cancel = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => window.Close());
        cancel.MinWidth = 88;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        void Check()
        {
            int value = (int)Math.Round(type.Value);
            bool taken = isTypeTaken(value);
            ok.IsEnabled = !taken;
            typeInfo.Foreground = taken ? EditorTheme.Danger : EditorTheme.TextMuted;
            typeInfo.Text = taken ? Loc.F("CountryTech.NodeDialog.TypeTaken", categoryId, value)
                : typeName(value) is { Length: > 0 } name ? Loc.F("CountryTech.NodeDialog.TypeName", name)
                : Loc.T("CountryTech.NodeDialog.TypeNoName");
        }

        type.ValueChanged += (_, _) => Check();
        Check();
        window.Content = root;
        window.ShowDialog();
        return result;
    }

    private sealed record ParentItem(CountryTechData? Node, string Text)
    {
        public override string ToString() => Text;
    }
}
