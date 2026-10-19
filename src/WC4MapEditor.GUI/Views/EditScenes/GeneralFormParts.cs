using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>将领表单的两个专用部件：技能槽编辑器与六维能力星级。</summary>
public static class GeneralFormParts
{
    /// <summary>星级的最高等级。</summary>
    public const int MaxStars = 6;

    /// <summary>
    /// 技能槽：原来的“技能 Id 列表”文本框仍是字段本身，下面是一排技能块（名称 + 等级、悬停看效果说明），
    /// 每块可以点击换技能、前后移动、移除；0 是空槽位。所有修改都是改写文本框内容，由表单写回记录并记历史。
    /// </summary>
    /// <param name="skillsMax">“技能槽上限（SkillsMax）”数字框；按钮把它设为当前槽位数。</param>
    public static UIElement SkillSlots(TextBox ids, Func<NumericUpDown?> skillsMax)
    {
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var summary = EditorTheme.Muted("");
        summary.FontSize = 11;
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new Thickness(10, 0, 0, 0);

        List<int> Current() => ListText.TryParseInts(ids.Text, out var values, out _) ? values : [];
        void Write(List<int> values) => ids.Text = ListText.Format(values);

        void Replace(int index)
        {
            var skills = Current();
            int? picked = IdPickerDialog.PickOne(Window.GetWindow(ids), Loc.T("Picker.Title.Skill"), ReferenceKinds.Skill.Entries!(), skills[index], allowClear: true);
            if (picked == null) return;
            skills[index] = picked.Value;
            Write(skills);
        }

        void Swap(int index, int delta)
        {
            var skills = Current();
            int target = index + delta;
            if (target < 0 || target >= skills.Count) return;
            (skills[index], skills[target]) = (skills[target], skills[index]);
            Write(skills);
        }

        void Remove(int index)
        {
            var skills = Current();
            skills.RemoveAt(index);
            Write(skills);
        }

        void Rebuild()
        {
            chips.Children.Clear();
            var skills = Current();
            for (int i = 0; i < skills.Count; i++)
            {
                int index = i;
                chips.Children.Add(Chip(skills, index, () => Replace(index), () => Swap(index, -1), () => Swap(index, 1), () => Remove(index)));
            }
            summary.Text = Loc.F("General.Skill.Summary", skills.Count, skills.Count(id => id != 0));
        }

        ids.TextChanged += (_, _) => Rebuild();

        var add = EditorTheme.Button(Loc.T("General.Skill.Add"), (_, _) =>
        {
            int? picked = IdPickerDialog.PickOne(Window.GetWindow(ids), Loc.T("Picker.Title.Skill"), ReferenceKinds.Skill.Entries!(), 0, allowClear: false);
            if (picked == null) return;
            var skills = Current();
            skills.Add(picked.Value);
            Write(skills);
        });
        var empty = EditorTheme.Button(Loc.T("General.Skill.AddEmpty"), (_, _) => Write([.. Current(), 0]));
        empty.Margin = new Thickness(6, 0, 0, 0);
        var setMax = EditorTheme.Button(Loc.T("General.Skill.SetMax"), (_, _) =>
        {
            if (skillsMax() is { } box) box.Value = Current().Count;
        });
        setMax.Margin = new Thickness(6, 0, 0, 0);
        setMax.ToolTip = Loc.T("General.Skill.SetMaxTip");
        foreach (var button in new[] { add, empty, setMax }) button.Focusable = false;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(add);
        actions.Children.Add(empty);
        actions.Children.Add(setMax);
        actions.Children.Add(summary);

        var panel = new StackPanel();
        panel.Children.Add(ids);
        panel.Children.Add(chips);
        panel.Children.Add(actions);
        panel.Loaded += (_, _) => Rebuild();
        return panel;
    }

    private static Border Chip(List<int> skills, int index, Action replace, Action moveLeft, Action moveRight, Action remove)
    {
        int id = skills[index];
        var skill = SkillSettingParser.Instance.GetById(id);
        var name = id == 0 ? Loc.T("General.Skill.Empty") : ReferenceKinds.Skill.Describe(id) ?? Loc.F("General.Skill.Unknown", id);
        var label = EditorTheme.Text_(name, 12, id == 0 ? EditorTheme.TextDim : skill == null ? EditorTheme.Warning : EditorTheme.Text);

        var pick = new Button { Content = label, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 2, 6, 2), MinHeight = 0, Cursor = Cursors.Hand, Focusable = false };
        pick.ToolTip = id == 0 ? Loc.T("General.Skill.EmptyTip")
            : skill == null ? Loc.F("General.Skill.UnknownTip", id)
            : $"{id}  {SkillSettingParser.Instance.GetSkillEffectText(skill)}";
        pick.Click += (_, _) => replace();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(pick);
        row.Children.Add(SmallButton("◀", Loc.T("JsonEditor.Tip.MoveUp"), moveLeft, index > 0));
        row.Children.Add(SmallButton("▶", Loc.T("JsonEditor.Tip.MoveDown"), moveRight, index < skills.Count - 1));
        row.Children.Add(SmallButton("✕", Loc.T("Common.Delete"), remove, true));
        return new Border
        {
            Background = id == 0 ? EditorTheme.Bg1 : EditorTheme.Bg3,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(2),
            Child = row
        };
    }

    private static Button SmallButton(string text, string tip, Action action, bool enabled)
    {
        var button = EditorTheme.ToolButton(text, (_, _) => action());
        button.ToolTip = tip;
        button.IsEnabled = enabled;
        button.Focusable = false;
        button.MinHeight = 0;
        button.MinWidth = 22;
        button.Height = 22;
        button.Padding = new Thickness(2, 0, 2, 0);
        button.FontSize = 10;
        return button;
    }

    /// <summary>
    /// 能力星级：点第 n 颗星把能力值设为 n（再点当前值的那颗星减一级，第 1 颗点两次即清零）。
    /// <paramref name="syncMax"/> 勾选时同时把这项能力的上限设为同一个值（只动正在编辑的这一项）。
    /// 数值框本身不受 0~6 限制，数据里更大的值照常显示和编辑。
    /// </summary>
    public static UIElement AbilityStars(NumericUpDown value, NumericUpDown max, CheckBox syncMax)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var stars = new TextBlock[MaxStars];

        void Paint()
        {
            int level = (int)Math.Clamp(Math.Round(value.Value), 0, MaxStars);
            for (int i = 0; i < MaxStars; i++)
            {
                stars[i].Text = i < level ? "★" : "☆";
                stars[i].Foreground = i < level ? EditorTheme.Accent : EditorTheme.TextDim;
            }
        }

        for (int i = 0; i < MaxStars; i++)
        {
            int level = i + 1;
            stars[i] = new TextBlock { FontSize = 18, Cursor = Cursors.Hand, ToolTip = Loc.T("General.Stars.Tip"), Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center };
            stars[i].MouseLeftButtonDown += (_, _) =>
            {
                int target = (int)Math.Round(value.Value) == level ? level - 1 : level;
                value.Value = target;
                if (syncMax.IsChecked == true) max.Value = target;
            };
            panel.Children.Add(stars[i]);
        }
        value.ValueChanged += (_, _) => Paint();
        panel.Loaded += (_, _) => Paint();
        return panel;
    }
}
