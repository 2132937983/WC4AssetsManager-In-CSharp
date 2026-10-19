using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>
/// 引用字段的外观：数字（或逗号列表）输入框 + “…”选择按钮 + 旁边的名称。
/// 输入框仍是表单字段本身，按钮选定后只是改它的值，所以写回记录、历史、错误标记都走原来的路径。
/// </summary>
public static class ReferenceFields
{
    /// <summary>单个 Id：输入框 | 选择按钮 | 名称。</summary>
    public static UIElement Single(NumericUpDown box, ReferenceKind kind)
    {
        box.Width = 110;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        if (kind.Entries != null)
        {
            var pick = PickButton(box, kind);
            DockPanel.SetDock(pick, Dock.Left);
            row.Children.Add(pick);
        }
        row.Children.Add(NameLabel(box, kind));
        return row;
    }

    /// <summary>Id 列表：输入框 + 选择按钮，名称列表在下一行。</summary>
    public static UIElement Multi(TextBox box, ReferenceKind kind)
    {
        var panel = new StackPanel();
        var inputRow = new DockPanel();
        var pick = PickButton(box, kind);
        DockPanel.SetDock(pick, Dock.Right);
        inputRow.Children.Add(pick);
        inputRow.Children.Add(box);
        panel.Children.Add(inputRow);
        panel.Children.Add(NameLabel(box, kind));
        return panel;
    }

    /// <summary>选择按钮：打开单选对话框，选定后写进数字框。</summary>
    public static Button PickButton(NumericUpDown box, ReferenceKind kind)
    {
        var button = CreateButton(Loc.T("Picker.Tip.Pick"));
        button.Click += (_, _) =>
        {
            int? picked = IdPickerDialog.PickOne(Window.GetWindow(box), kind.PickerTitle, kind.Entries!(), (int)Math.Round(box.Value), kind.AllowClear);
            if (picked != null) box.Value = picked.Value;
        };
        return button;
    }

    /// <summary>选择按钮：打开多选对话框，确认后把 Id 列表写回文本框。</summary>
    public static Button PickButton(TextBox box, ReferenceKind kind)
    {
        var button = CreateButton(Loc.T("Picker.Tip.PickMany"));
        button.Click += (_, _) =>
        {
            ListText.TryParseInts(box.Text, out var current, out _);
            var picked = IdPickerDialog.PickMany(Window.GetWindow(box), kind.PickerTitle, kind.Entries!(), current);
            if (picked != null) box.Text = ListText.Format(picked);
        };
        return button;
    }

    /// <summary>数字框旁的名称；0 不显示，记录不存在时用提醒色。</summary>
    public static TextBlock NameLabel(NumericUpDown box, ReferenceKind kind)
    {
        var label = CreateLabel();
        void Update() => Describe(label, kind, [(int)Math.Round(box.Value)], false);
        box.ValueChanged += (_, _) => Update();
        label.Loaded += (_, _) => Update();
        return label;
    }

    /// <summary>文本框下的名称列表（“Id 名称，Id 名称…”）；输入无法解析时不显示。</summary>
    public static TextBlock NameLabel(TextBox box, ReferenceKind kind)
    {
        var label = CreateLabel();
        label.Margin = new Thickness(2, 4, 0, 0);
        void Update()
        {
            if (ListText.TryParseInts(box.Text, out var ids, out _)) Describe(label, kind, ids, true);
            else label.Text = "";
        }
        box.TextChanged += (_, _) => Update();
        label.Loaded += (_, _) => Update();
        return label;
    }

    /// <summary>把 Id 翻译成名称文字（列表时带上 Id）；找不到记录的 Id 标成提醒色。</summary>
    private static void Describe(TextBlock label, ReferenceKind kind, IReadOnlyList<int> ids, bool withIds)
    {
        var parts = new List<string>();
        bool missing = false;
        foreach (int id in ids.Where(id => id != 0))
        {
            var name = kind.Describe(id);
            if (name == null)
            {
                missing = true;
                parts.Add(Loc.F("Picker.NotFound", id));
            }
            else
            {
                parts.Add(!withIds ? name : name.Length == 0 ? id.ToString() : $"{id} {name}");
            }
        }
        label.Foreground = missing ? EditorTheme.Warning : EditorTheme.TextMuted;
        label.Text = string.Join(Loc.T("Picker.Separator"), parts);
    }

    private static TextBlock CreateLabel()
    {
        var label = EditorTheme.Muted("");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(10, 0, 0, 0);
        return label;
    }

    private static Button CreateButton(string tooltip)
    {
        var button = EditorTheme.Button("…");
        button.Width = 32;
        button.Height = 28;
        button.MinHeight = 0;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(4, 0, 0, 0);
        button.ToolTip = tooltip;
        button.Focusable = false;
        return button;
    }
}
