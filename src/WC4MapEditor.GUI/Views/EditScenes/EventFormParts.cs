using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>集团军事件与征服事件两个场景共用的表单部件：Buff 槽、触发类型（带用法说明）、引用检查按钮。</summary>
public static class EventFormParts
{
    /// <summary>游戏里已知的触发类型；Trigger 仍然接受任意整数（数据里有 11 这类未收录的值）。</summary>
    private const int FirstTrigger = 1, LastTrigger = 10;

    /// <summary>一个 Buff 槽（EventBuffId / Round / CountryId）的字段标题。</summary>
    public sealed record SlotLabels(string Section, string Buff, string Round, string Country);

    /// <summary>Buff 槽：Buff Id 带事件 Buff 的名称预览与选择器，作用国家带国家选择器。</summary>
    public static void AddBuffSlot<T>(RecordForm<T> f, SlotLabels labels,
        Func<T, int> getBuff, Action<T, int> setBuff, Func<T, int> getRound, Action<T, int> setRound,
        Func<T, List<int>?> getCountries, Action<T, List<int>> setCountries) where T : class
    {
        f.Section(labels.Section);
        f.Int(labels.Buff, getBuff, setBuff, decorate: box => ReferenceFields.Single(box, ReferenceKinds.EventBuff));
        f.Int(labels.Round, getRound, setRound);
        f.IntList(labels.Country, getCountries, setCountries, Loc.T("Picker.Tip.CountryList"),
            box => ReferenceFields.Multi(box, ReferenceKinds.Country));
    }

    /// <summary>触发类型：数字框 + 已知类型下拉，下面是该类型的用法说明。</summary>
    public static void AddTrigger<T>(RecordForm<T> f, string label, Func<T, int> get, Action<T, int> set) where T : class
        => f.Int(label, get, set, decorate: TriggerField);

    private static UIElement TriggerField(NumericUpDown box)
    {
        box.Width = 90;
        var combo = EditorTheme.ComboBox(240);
        combo.Margin = new Thickness(8, 0, 0, 0);
        for (int trigger = FirstTrigger; trigger <= LastTrigger; trigger++)
            combo.Items.Add(new TriggerItem(trigger, $"{trigger} - {Loc.T($"Event.Trigger.Name.{trigger}")}"));
        var guide = EditorTheme.Muted("");
        guide.TextWrapping = TextWrapping.Wrap;
        guide.Margin = new Thickness(0, 6, 0, 0);

        bool updating = false;
        void Sync()
        {
            updating = true;
            int value = (int)Math.Round(box.Value);
            combo.SelectedItem = combo.Items.Cast<TriggerItem>().FirstOrDefault(item => item.Id == value);
            guide.Text = value >= FirstTrigger && value <= LastTrigger ? Loc.T($"Event.Trigger.Guide.{value}") : Loc.F("Event.Trigger.GuideCustom", value);
            updating = false;
        }

        box.ValueChanged += (_, _) => Sync();
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedItem is TriggerItem item) box.Value = item.Id;
        };
        guide.Loaded += (_, _) => Sync();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(combo);
        var panel = new StackPanel();
        panel.Children.Add(row);
        panel.Children.Add(guide);
        return panel;
    }

    private sealed record TriggerItem(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>顶栏的“引用检查”按钮：检查事件 → Buff / 国家 / 事件 / 军队 Buff 的引用并显示报告。</summary>
    public static Button LinkCheckButton(JsonEditorScene scene)
    {
        var button = EditorTheme.Button(Loc.T("EventLink.Button"));
        button.ToolTip = Loc.T("EventLink.Tip");
        button.Focusable = false;
        button.Click += (_, _) => ShowLinkCheck(scene);
        return button;
    }

    private static void ShowLinkCheck(JsonEditorScene scene)
    {
        var report = EventLinkChecker.Check();
        var lines = new List<string>
        {
            Loc.F("EventLink.Summary", report.ArmyGroupEvents, report.ConquerEvents, report.EventBuffs),
            ""
        };
        if (report.Problems.Count == 0)
        {
            lines.Add(Loc.T("EventLink.NoProblems"));
        }
        else
        {
            lines.Add(Loc.F("EventLink.ProblemsHeader", report.Problems.Count));
            lines.AddRange(report.Problems.Take(100).Select(p => "  " + p));
            if (report.Problems.Count > 100) lines.Add(Loc.F("Common.ValidationMore", report.Problems.Count, 100));
        }
        lines.Add("");
        lines.Add(report.UnusedBuffs.Count == 0
            ? Loc.T("EventLink.NoUnused")
            : Loc.F("EventLink.Unused", report.UnusedBuffs.Count, string.Join(", ", report.UnusedBuffs.Take(60))));
        EditorDialogs.ShowText(scene.DialogOwner, Loc.T("EventLink.Title"), string.Join(Environment.NewLine, lines));
        scene.SetStatus(Loc.F("EventLink.Status", report.Problems.Count));
    }
}
