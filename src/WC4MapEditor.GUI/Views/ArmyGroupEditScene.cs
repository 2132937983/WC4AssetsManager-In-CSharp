using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.ArmyGroupSetting;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 集团军与援军编辑场景（对应 Python 工具的 ArmyGroup 编辑器，五表联动防闪退：
/// ArmyGroupSettings + ArmyGroupReinforcementSettings + EventSettings +
/// EventStageSettings + EventCalendarSettings，文本写入 stringtable 的 event_{EventId}）。
/// </summary>
public class ArmyGroupEditScene : UserControl
{
    /// <summary>表单行字段名列宽：这里的标签带英文属性名，比默认的 180 宽一点。</summary>
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly ArmyGroupSettingParser _parser = ArmyGroupSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private ArmyGroupSettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _iniDescBox = null!;
    private StackPanel _rePanel = null!;
    private TextBlock _reWarn = null!;
    private TextBlock _activityHint = null!;
    private TextBox _jsonPreview = null!;

    private EventSettingData? _curEvent;
    private EventStageSettingData? _curStage;
    private EventCalendarSettingData? _curCalendar;

    public ArmyGroupEditScene(MainWindow window)
    {
        _window = window;
        EditorTheme.Apply(this);
        BuildUI();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_listBox.Items.Count > 0) _listBox.SelectedIndex = 0;
        }, DispatcherPriority.Loaded);
    }

    // ============================================================= 布局 =============================================================
    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = EditorTheme.TopBar(Loc.T("ArmyGroup.Title"), (_, _) => _window.ReturnToMainScene(),
            EditorTheme.Button(Loc.T("ArmyGroup.CreateTemplate"), OnCreateTemplate),
            EditorTheme.DangerButton(Loc.T("Common.Delete"), OnDelete),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.Button(Loc.T("Common.Validate"), OnValidate));
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        _root.Children.Add(topBar);

        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = EditorTheme.SearchBox(Loc.T("ArmyGroup.SearchPlaceholder"), out _searchBox);
        search.Margin = new Thickness(8);
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(search, 0);
        leftGrid.Children.Add(search);

        _listBox = new ListBox();
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);

        var splitter = EditorTheme.VSplitter();
        Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        BuildRightPanel();
        Grid.SetRow(_propScroll, 1); Grid.SetColumn(_propScroll, 2);
        _root.Children.Add(_propScroll);

        var status = EditorTheme.StatusBar(out _statusText, Loc.T("Common.Ready"));
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _root.Children.Add(status);

        Content = _root;
    }

    private void BuildRightPanel()
    {
        _propPanel = new StackPanel();
        _propScroll = new ScrollViewer
        {
            Content = _propPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Intro")));
        _propPanel.Children.Add(EditorTheme.FormRow(Loc.T("ArmyGroup.Field.IntroIni"), AddIniDesc(), LabelWidth));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Core")));
        AddNum("A.Id", Loc.T("ArmyGroup.Field.Id"), 0, int.MaxValue);
        AddNum("A.EventId", Loc.T("ArmyGroup.Field.EventId"), 0, int.MaxValue);
        AddNum("A.StageId", Loc.T("ArmyGroup.Field.StageId"), 0, int.MaxValue);
        AddNum("A.Seat", Loc.T("ArmyGroup.Field.Seat"), 0, int.MaxValue);
        AddNum("A.Star", Loc.T("ArmyGroup.Field.Star"), 0, int.MaxValue);
        AddNum("A.Camp", Loc.T("ArmyGroup.Field.Camp"), 0, int.MaxValue);
        AddNum("A.Difficulty", Loc.T("ArmyGroup.Field.Difficulty"), 0, int.MaxValue);
        AddNum("A.MeritPoint", Loc.T("ArmyGroup.Field.MeritPoint"), 0, int.MaxValue);
        AddNum("A.TechAcquire", Loc.T("ArmyGroup.Field.TechAcquire"), 0, int.MaxValue);
        AddNum("A.CountryId", Loc.T("ArmyGroup.Field.CountryId"), 0, int.MaxValue);
        AddNum("A.GroupId", Loc.T("ArmyGroup.Field.GroupId"), 0, int.MaxValue);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Prize")));
        AddNum("A.PrizeGold", Loc.T("ArmyGroup.Field.PrizeGold"), 0, int.MaxValue);
        AddNum("A.PrizeIndustry", Loc.T("ArmyGroup.Field.PrizeIndustry"), 0, int.MaxValue);
        AddNum("A.PrizeEnergy", Loc.T("ArmyGroup.Field.PrizeEnergy"), 0, int.MaxValue);
        AddNum("A.PrizeTech", Loc.T("ArmyGroup.Field.PrizeTech"), 0, int.MaxValue);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Cost")));
        AddNum("A.CostMoney", Loc.T("ArmyGroup.Field.CostMoney"), 0, int.MaxValue);
        AddNum("A.CostGear", Loc.T("ArmyGroup.Field.CostGear"), 0, int.MaxValue);
        AddNum("A.CostAtomic", Loc.T("ArmyGroup.Field.CostAtomic"), 0, int.MaxValue);
        AddNum("A.Coefficient", Loc.T("ArmyGroup.Field.Coefficient"), 0, int.MaxValue);
        AddNum("A.CityNum", Loc.T("ArmyGroup.Field.CityNum"), 0, int.MaxValue);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Text")));
        AddText("A.Photo", Loc.T("ArmyGroup.Field.Photo"));
        AddText("A.ShortName", Loc.T("ArmyGroup.Field.ShortName"));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Lists")));
        AddText("A.TechCategoryIds", Loc.T("ArmyGroup.Field.TechCategoryIds"));
        AddText("A.CloseTechTypes", Loc.T("ArmyGroup.Field.CloseTechTypes"));
        AddText("A.CloseCardTypes", Loc.T("ArmyGroup.Field.CloseCardTypes"));
        AddText("A.GeneralId", Loc.T("ArmyGroup.Field.GeneralId"));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Reinforcements")));
        _reWarn = EditorTheme.Muted("");
        _reWarn.Margin = new Thickness(10, 6, 10, 4);
        _reWarn.TextWrapping = TextWrapping.Wrap;
        _propPanel.Children.Add(_reWarn);
        _rePanel = new StackPanel { Margin = new Thickness(6, 0, 6, 4) };
        _propPanel.Children.Add(_rePanel);
        var addRe = EditorTheme.Button(Loc.T("ArmyGroup.AddReinforcement"), OnAddReinforcement);
        addRe.HorizontalAlignment = HorizontalAlignment.Left;
        addRe.Margin = new Thickness(10, 4, 0, 8);
        _propPanel.Children.Add(addRe);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroup.Section.Activity")));
        _activityHint = EditorTheme.Muted("");
        _activityHint.Foreground = EditorTheme.Warning;
        _activityHint.Margin = new Thickness(10, 6, 10, 6);
        _activityHint.TextWrapping = TextWrapping.Wrap;
        _propPanel.Children.Add(_activityHint);

        // 三张底层表的名字就是 JSON 文件名，属于技术标识，不进翻译表
        _propPanel.Children.Add(TableHeader("EventSettings"));
        AddNum("E.Type", "Type", 0, int.MaxValue);
        AddNum("E.NormalId", "NormalId", 0, int.MaxValue);
        AddNum("E.NeedHQLv", "NeedHQLv", 0, int.MaxValue);
        AddNum("E.GeneralId", Loc.F("ArmyGroup.Hint.Single", "GeneralId"), 0, int.MaxValue);
        AddNum("E.DifficultStar", "DifficultStar", 0, int.MaxValue);
        AddText("E.Points", Loc.F("ArmyGroup.Hint.CommaList", "Points"));

        _propPanel.Children.Add(TableHeader("EventStageSettings"));
        AddNum("S.Id", "Id", 0, int.MaxValue);
        AddNum("S.Type", "Type", 0, int.MaxValue);
        AddNum("S.CountryId", "CountryId", 0, int.MaxValue);
        AddNum("S.UnlockStageId", "UnlockStageId", 0, int.MaxValue);
        AddNum("S.RefStageId", "RefStageId", 0, int.MaxValue);
        AddNum("S.Difficulty", "Difficulty", 0, int.MaxValue);
        AddNum("S.Seat", "Seat", 0, int.MaxValue);
        AddNum("S.GeneralID", "GeneralID", 0, int.MaxValue);
        AddNum("S.X", "X", int.MinValue, int.MaxValue);
        AddNum("S.Y", "Y", int.MinValue, int.MaxValue);
        AddNum("S.PrizeMedals", "PrizeMedals", 0, int.MaxValue);
        AddNum("S.PrizeTicket", "PrizeTicket", 0, int.MaxValue);
        AddNum("S.EnableRoundEvent", "EnableRoundEvent", 0, int.MaxValue);
        AddText("S.RoundPhoto", Loc.F("ArmyGroup.Hint.Nullable", "RoundPhoto"));
        AddText("S.PrizeItem", Loc.F("ArmyGroup.Hint.CommaList", "PrizeItem"));
        AddText("S.PrizeGold", Loc.F("ArmyGroup.Hint.CommaList", "PrizeGold"));
        AddText("S.PrizeIndustry", Loc.F("ArmyGroup.Hint.CommaList", "PrizeIndustry"));
        AddText("S.PrizeEnergy", Loc.F("ArmyGroup.Hint.CommaList", "PrizeEnergy"));
        AddText("S.PrizeTech", Loc.F("ArmyGroup.Hint.CommaList", "PrizeTech"));
        AddText("S.PrizeMerit", Loc.F("ArmyGroup.Hint.CommaList", "PrizeMerit"));

        _propPanel.Children.Add(TableHeader("EventCalendarSettings"));
        AddNum("C.Id", "Id", 0, int.MaxValue);
        AddNum("C.LastingDays", "LastingDays", 0, int.MaxValue);
        AddNum("C.NoticeDays", "NoticeDays", 0, int.MaxValue);
        AddNum("C.PeriodDays", "PeriodDays", 0, int.MaxValue);
        AddNum("C.NeedHQLv", "NeedHQLv", 0, int.MaxValue);
        AddNum("C.Version", "Version", 0, int.MaxValue);
        AddText("C.StartDate", Loc.T("ArmyGroup.Field.StartDate"));
        AddText("C.PrizeIds", Loc.F("ArmyGroup.Hint.CommaList", "PrizeIds"));
        AddText("C.HardBuffs", Loc.F("ArmyGroup.Hint.CommaList", "HardBuffs"));
        AddText("C.MyBuffPool", Loc.F("ArmyGroup.Hint.CommaList", "MyBuffPool"));
        AddText("C.HardPrizeIds", Loc.F("ArmyGroup.Hint.CommaList", "HardPrizeIds"));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.JsonPreview")));
        _jsonPreview = EditorTheme.MonoTextBox(200);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    /// <summary>底层表的小标题（分组标题条之下的二级标题）。</summary>
    private static TextBlock TableHeader(string tableName)
    {
        var tb = EditorTheme.Section("■ " + tableName);
        tb.Margin = new Thickness(10, 10, 0, 2);
        return tb;
    }

    private NumericUpDown AddNum(string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _numBoxes[key] = ctrl;
        _propPanel.Children.Add(EditorTheme.FormRow(label, ctrl, LabelWidth));
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _textBoxes[key] = tb;
        _propPanel.Children.Add(EditorTheme.FormRow(label, tb, LabelWidth));
        return tb;
    }

    private TextBox AddIniDesc()
    {
        var tb = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 70
        };
        tb.TextChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _iniDescBox = tb;
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        int? keepId = _current?.Id;
        _listBox.Items.Clear();
        foreach (var a in _parser.Items.Where(a =>
            !searching ||
            a.Id.ToString().Contains(q) ||
            (a.ShortName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.EventId.ToString().Contains(q)))
        {
            var entry = new AgListEntry { Id = a.Id, ShortName = a.ShortName, EventId = a.EventId };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && a.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus(Loc.F("ArmyGroup.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not AgListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        LoadCurrent();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = nb.MinValue;
        _iniDescBox.Text = "";
        _rePanel.Children.Clear();
        _reWarn.Text = "";
        _activityHint.Text = "";
        _jsonPreview.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 加载 =============================================================
    private void LoadCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;

        SetNum("A.Id", _current.Id);
        SetNum("A.EventId", _current.EventId);
        SetNum("A.StageId", _current.StageId);
        SetNum("A.Seat", _current.Seat);
        SetNum("A.Star", _current.Star);
        SetNum("A.Camp", _current.Camp);
        SetNum("A.Difficulty", _current.Difficulty);
        SetNum("A.MeritPoint", _current.MeritPoint);
        SetNum("A.TechAcquire", _current.TechAcquire);
        SetNum("A.CountryId", _current.CountryId);
        SetNum("A.GroupId", _current.GroupId);
        SetNum("A.PrizeGold", _current.PrizeGold);
        SetNum("A.PrizeIndustry", _current.PrizeIndustry);
        SetNum("A.PrizeEnergy", _current.PrizeEnergy);
        SetNum("A.PrizeTech", _current.PrizeTech);
        SetNum("A.CostMoney", _current.CostMoney);
        SetNum("A.CostGear", _current.CostGear);
        SetNum("A.CostAtomic", _current.CostAtomic);
        SetNum("A.Coefficient", _current.Coefficient);
        SetNum("A.CityNum", _current.CityNum);
        _textBoxes["A.Photo"].Text = _current.Photo ?? "";
        _textBoxes["A.ShortName"].Text = _current.ShortName ?? "";
        _textBoxes["A.TechCategoryIds"].Text = FormatList(_current.TechCategoryIds);
        _textBoxes["A.CloseTechTypes"].Text = FormatList(_current.CloseTechTypes);
        _textBoxes["A.CloseCardTypes"].Text = FormatList(_current.CloseCardTypes);
        _textBoxes["A.GeneralId"].Text = FormatList(_current.GeneralId);
        _iniDescBox.Text = _parser.GetEventDesc(_current.EventId);

        // 底层活动参数
        _curEvent = _parser.GetEventByEventId(_current.EventId);
        _curStage = _parser.GetStageByEventId(_current.EventId);
        _curCalendar = _parser.GetCalendarByEventId(_current.EventId);
        LoadActivity();

        _loadingUi = false;
        RebuildReinforcements();
        UpdateJsonPreview();
    }

    private void LoadActivity()
    {
        SetNum("E.Type", _curEvent?.Type ?? 0);
        SetNum("E.NormalId", _curEvent?.NormalId ?? 0);
        SetNum("E.NeedHQLv", _curEvent?.NeedHQLv ?? 0);
        SetNum("E.GeneralId", _curEvent?.GeneralId ?? 0);
        SetNum("E.DifficultStar", _curEvent?.DifficultStar ?? 0);
        _textBoxes["E.Points"].Text = FormatList(_curEvent?.Points);

        SetNum("S.Id", _curStage?.Id ?? 0);
        SetNum("S.Type", _curStage?.Type ?? 0);
        SetNum("S.CountryId", _curStage?.CountryId ?? 0);
        SetNum("S.UnlockStageId", _curStage?.UnlockStageId ?? 0);
        SetNum("S.RefStageId", _curStage?.RefStageId ?? 0);
        SetNum("S.Difficulty", _curStage?.Difficulty ?? 0);
        SetNum("S.Seat", _curStage?.Seat ?? 0);
        SetNum("S.GeneralID", _curStage?.GeneralID ?? 0);
        SetNum("S.X", _curStage?.X ?? 0);
        SetNum("S.Y", _curStage?.Y ?? 0);
        SetNum("S.PrizeMedals", _curStage?.PrizeMedals ?? 0);
        SetNum("S.PrizeTicket", _curStage?.PrizeTicket ?? 0);
        SetNum("S.EnableRoundEvent", _curStage?.EnableRoundEvent ?? 0);
        _textBoxes["S.RoundPhoto"].Text = _curStage?.RoundPhoto ?? "";
        _textBoxes["S.PrizeItem"].Text = FormatList(_curStage?.PrizeItem);
        _textBoxes["S.PrizeGold"].Text = FormatList(_curStage?.PrizeGold);
        _textBoxes["S.PrizeIndustry"].Text = FormatList(_curStage?.PrizeIndustry);
        _textBoxes["S.PrizeEnergy"].Text = FormatList(_curStage?.PrizeEnergy);
        _textBoxes["S.PrizeTech"].Text = FormatList(_curStage?.PrizeTech);
        _textBoxes["S.PrizeMerit"].Text = FormatList(_curStage?.PrizeMerit);

        SetNum("C.Id", _curCalendar?.Id ?? 0);
        SetNum("C.LastingDays", _curCalendar?.LastingDays ?? 0);
        SetNum("C.NoticeDays", _curCalendar?.NoticeDays ?? 0);
        SetNum("C.PeriodDays", _curCalendar?.PeriodDays ?? 0);
        SetNum("C.NeedHQLv", _curCalendar?.NeedHQLv ?? 0);
        SetNum("C.Version", _curCalendar?.Version ?? 0);
        _textBoxes["C.StartDate"].Text = FormatList(_curCalendar?.StartDate);
        _textBoxes["C.PrizeIds"].Text = FormatList(_curCalendar?.PrizeIds);
        _textBoxes["C.HardBuffs"].Text = FormatList(_curCalendar?.HardBuffs);
        _textBoxes["C.MyBuffPool"].Text = FormatList(_curCalendar?.MyBuffPool);
        _textBoxes["C.HardPrizeIds"].Text = FormatList(_curCalendar?.HardPrizeIds);

        var missing = new List<string>();
        if (_curEvent == null) missing.Add("EventSettings");
        if (_curStage == null) missing.Add("EventStageSettings");
        if (_curCalendar == null) missing.Add("EventCalendarSettings");
        _activityHint.Text = missing.Count == 0
            ? ""
            : Loc.F("ArmyGroup.ActivityMissing", _current?.EventId, string.Join(" / ", missing));
    }

    // ============================================================= 援军子面板 =============================================================
    private void RebuildReinforcements()
    {
        _rePanel.Children.Clear();
        if (_current == null) return;

        var list = _parser.GetReinforcements(_current.Id);
        // 没有援军会导致游戏闪退，用危险色把警告顶出来；正常情况只是普通灰字说明
        _reWarn.Foreground = list.Count == 0 ? EditorTheme.Danger : EditorTheme.TextMuted;
        _reWarn.Text = list.Count == 0
            ? Loc.T("ArmyGroup.NoReinforcementWarning")
            : Loc.F("ArmyGroup.ReinforcementCount", list.Count);

        foreach (var r in list)
        {
            var grid = new Grid();
            for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var boxId = ReNum(r, x => x.Id, (x, v) => x.Id = v, 0, int.MaxValue);
            var boxMap = ReNum(r, x => x.MapId, (x, v) => x.MapId = v, 0, int.MaxValue);
            var boxArmy = ReNum(r, x => x.ArmyId, (x, v) => x.ArmyId = v, 0, int.MaxValue);
            var boxCost = ReNum(r, x => x.CostPoint, (x, v) => x.CostPoint = v, 0, int.MaxValue);
            var boxNum = ReNum(r, x => x.Num, (x, v) => x.Num = v, 0, int.MaxValue);
            var boxAg = ReNum(r, x => x.ArmyGroupId, (x, v) => x.ArmyGroupId = v, 0, int.MaxValue);

            AddReField(grid, 0, Loc.T("ArmyGroup.Re.Id"), boxId);
            AddReField(grid, 1, "MapId", boxMap);
            AddReField(grid, 2, Loc.T("ArmyGroup.Re.ArmyId"), boxArmy);
            AddReField(grid, 3, Loc.T("ArmyGroup.Re.CostPoint"), boxCost);
            AddReField(grid, 4, Loc.T("Common.Count"), boxNum);
            // ArmyGroupId 通常在右侧单独展示，这里合并到行尾
            var tail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var agLabel = EditorTheme.Muted(Loc.T("ArmyGroup.Re.ArmyGroupId") + ":");
            agLabel.FontSize = 11;
            agLabel.VerticalAlignment = VerticalAlignment.Center;
            agLabel.Margin = new Thickness(0, 0, 3, 0);
            tail.Children.Add(agLabel);
            boxAg.Width = 70;
            tail.Children.Add(boxAg);
            var del = EditorTheme.DangerButton(Loc.T("Common.Delete"));
            del.Margin = new Thickness(8, 0, 0, 0);
            del.Padding = new Thickness(8, 2, 8, 2);
            del.MinHeight = 0;
            del.FontSize = 11;
            del.Click += (_, _) =>
            {
                _parser.Reinforcements.Remove(r);
                RebuildReinforcements();
                SetStatus(Loc.F("ArmyGroup.ReinforcementDeleted", r.Id));
            };
            tail.Children.Add(del);
            Grid.SetColumn(tail, 5);
            grid.Children.Add(tail);

            var row = EditorTheme.Card(grid, new Thickness(6));
            row.Margin = new Thickness(0, 3, 0, 3);
            _rePanel.Children.Add(row);
        }
    }

    private NumericUpDown ReNum(ArmyGroupReinforcementData r, Func<ArmyGroupReinforcementData, int> get, Action<ArmyGroupReinforcementData, int> set, double min, double max)
    {
        var box = new NumericUpDown { MinValue = min, MaxValue = max, Increment = 1, Width = 70, Height = 24 };
        box.Value = get(r);
        box.ValueChanged += (_, v) => { if (!_loadingUi) set(r, (int)Math.Round(v)); };
        return box;
    }

    private static void AddReField(Grid grid, int col, string label, NumericUpDown box)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
        var lbl = EditorTheme.Muted(label + ":");
        lbl.FontSize = 11;
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.Margin = new Thickness(0, 0, 3, 0);
        sp.Children.Add(lbl);
        sp.Children.Add(box);
        Grid.SetColumn(sp, col);
        grid.Children.Add(sp);
    }

    // ============================================================= 应用 =============================================================
    private void ApplyActive()
    {
        ApplyArmyGroup();
        ApplyActivity();
    }

    private void ApplyArmyGroup()
    {
        if (_current == null) return;
        _current.Id = GetInt("A.Id");
        _current.EventId = GetInt("A.EventId");
        _current.StageId = GetInt("A.StageId");
        _current.Seat = GetInt("A.Seat");
        _current.Star = GetInt("A.Star");
        _current.Camp = GetInt("A.Camp");
        _current.Difficulty = GetInt("A.Difficulty");
        _current.MeritPoint = GetInt("A.MeritPoint");
        _current.TechAcquire = GetInt("A.TechAcquire");
        _current.CountryId = GetInt("A.CountryId");
        _current.GroupId = GetInt("A.GroupId");
        _current.PrizeGold = GetInt("A.PrizeGold");
        _current.PrizeIndustry = GetInt("A.PrizeIndustry");
        _current.PrizeEnergy = GetInt("A.PrizeEnergy");
        _current.PrizeTech = GetInt("A.PrizeTech");
        _current.CostMoney = GetInt("A.CostMoney");
        _current.CostGear = GetInt("A.CostGear");
        _current.CostAtomic = GetInt("A.CostAtomic");
        _current.Coefficient = GetInt("A.Coefficient");
        _current.CityNum = GetInt("A.CityNum");
        _current.Photo = _textBoxes["A.Photo"].Text;
        _current.ShortName = _textBoxes["A.ShortName"].Text;
        _current.TechCategoryIds = ParseIntList(_textBoxes["A.TechCategoryIds"].Text);
        _current.CloseTechTypes = ParseIntList(_textBoxes["A.CloseTechTypes"].Text);
        _current.CloseCardTypes = ParseIntList(_textBoxes["A.CloseCardTypes"].Text);
        _current.GeneralId = ParseIntList(_textBoxes["A.GeneralId"].Text);
        _parser.SetEventDesc(_current.EventId, _iniDescBox.Text);

        if (_listBox.SelectedItem is AgListEntry le && le.Id == _current.Id)
        {
            var idx = _listBox.SelectedIndex;
            var replacement = new AgListEntry { Id = _current.Id, ShortName = _current.ShortName, EventId = _current.EventId };
            _listBox.Items[idx] = replacement;
            _listBox.SelectedIndex = idx;
        }
        SetStatus(Loc.F("ArmyGroup.Updated", _current.Id));
        UpdateJsonPreview();
    }

    private void ApplyActivity()
    {
        if (_current == null) return;
        if (_curEvent != null)
        {
            _curEvent.Type = GetInt("E.Type");
            _curEvent.NormalId = GetInt("E.NormalId");
            _curEvent.NeedHQLv = GetInt("E.NeedHQLv");
            _curEvent.GeneralId = GetInt("E.GeneralId");
            _curEvent.DifficultStar = GetInt("E.DifficultStar");
            _curEvent.Points = ParseIntList(_textBoxes["E.Points"].Text);
        }
        if (_curStage != null)
        {
            _curStage.Id = GetInt("S.Id");
            _curStage.Type = GetInt("S.Type");
            _curStage.CountryId = GetInt("S.CountryId");
            _curStage.UnlockStageId = GetInt("S.UnlockStageId");
            _curStage.RefStageId = GetInt("S.RefStageId");
            _curStage.Difficulty = GetInt("S.Difficulty");
            _curStage.Seat = GetInt("S.Seat");
            _curStage.GeneralID = GetInt("S.GeneralID");
            _curStage.X = GetInt("S.X");
            _curStage.Y = GetInt("S.Y");
            _curStage.PrizeMedals = GetInt("S.PrizeMedals");
            _curStage.PrizeTicket = GetInt("S.PrizeTicket");
            _curStage.EnableRoundEvent = GetInt("S.EnableRoundEvent");
            var rp = _textBoxes["S.RoundPhoto"].Text;
            _curStage.RoundPhoto = string.IsNullOrWhiteSpace(rp) ? null : rp;
            _curStage.PrizeItem = ParseIntList(_textBoxes["S.PrizeItem"].Text);
            _curStage.PrizeGold = ParseIntList(_textBoxes["S.PrizeGold"].Text);
            _curStage.PrizeIndustry = ParseIntList(_textBoxes["S.PrizeIndustry"].Text);
            _curStage.PrizeEnergy = ParseIntList(_textBoxes["S.PrizeEnergy"].Text);
            _curStage.PrizeTech = ParseIntList(_textBoxes["S.PrizeTech"].Text);
            _curStage.PrizeMerit = ParseIntList(_textBoxes["S.PrizeMerit"].Text);
        }
        if (_curCalendar != null)
        {
            _curCalendar.Id = GetInt("C.Id");
            _curCalendar.LastingDays = GetInt("C.LastingDays");
            _curCalendar.NoticeDays = GetInt("C.NoticeDays");
            _curCalendar.PeriodDays = GetInt("C.PeriodDays");
            _curCalendar.NeedHQLv = GetInt("C.NeedHQLv");
            _curCalendar.Version = GetInt("C.Version");
            _curCalendar.StartDate = ParseIntList(_textBoxes["C.StartDate"].Text);
            _curCalendar.PrizeIds = ParseIntList(_textBoxes["C.PrizeIds"].Text);
            _curCalendar.HardBuffs = ParseIntList(_textBoxes["C.HardBuffs"].Text);
            _curCalendar.MyBuffPool = ParseIntList(_textBoxes["C.MyBuffPool"].Text);
            _curCalendar.HardPrizeIds = ParseIntList(_textBoxes["C.HardPrizeIds"].Text);
        }
    }

    private void SetNum(string key, double v) { if (_numBoxes.TryGetValue(key, out var b)) b.Value = v; }
    private double GetNum(string key) => _numBoxes.TryGetValue(key, out var b) ? b.Value : 0;
    private int GetInt(string key) => (int)Math.Round(GetNum(key));

    private static string FormatList(List<int>? list) => list == null ? "" : string.Join(", ", list);

    private static List<int> ParseIntList(string? s)
    {
        var r = new List<int>();
        if (string.IsNullOrWhiteSpace(s)) return r;
        foreach (var p in s.Split(new[] { ',', ' ', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), out var v)) r.Add(v);
        return r;
    }

    private void UpdateJsonPreview()
    {
        if (_current == null) { _jsonPreview.Text = ""; return; }
        try
        {
            var bundle = new
            {
                ArmyGroup = _current,
                Reinforcements = _parser.GetReinforcements(_current.Id),
                Event = _curEvent,
                Stage = _curStage,
                Calendar = _curCalendar
            };
            _jsonPreview.Text = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { _jsonPreview.Text = Loc.T("Common.SerializeFailed"); }
    }

    // ============================================================= 操作 =============================================================
    private void OnCreateTemplate(object sender, RoutedEventArgs e)
    {
        int agId = _parser.GetNextArmyGroupId();
        int eventId = _parser.GetNextEventId();
        int stageId = eventId * 100 + 1;
        int calendarId = _parser.GetNextCalendarId();
        int reId = _parser.GetNextReinforcementId();
        int mapId = stageId;

        var ev = new EventSettingData { Id = eventId, Type = 8, NormalId = 0, NeedHQLv = 1, Points = new List<int>(), GeneralId = 0, DifficultStar = 0 };
        var stage = new EventStageSettingData { Id = stageId, EventId = eventId, Type = 15, RoundPhoto = null };
        var cal = new EventCalendarSettingData { Id = calendarId, EventId = eventId, StartDate = new List<int> { 2025, 6, 20 }, LastingDays = 20000, NoticeDays = 3 };
        // ShortName 与活动介绍都会写进 JSON / stringtable，是数据不是界面文字，保持原样
        var ag = new ArmyGroupSettingData
        {
            Id = agId,
            EventId = eventId,
            StageId = stageId,
            Seat = 50,
            Star = 3,
            Camp = 1,
            Difficulty = 3,
            MeritPoint = 5,
            CountryId = 0,
            PrizeGold = 10000,
            PrizeIndustry = 1000,
            PrizeEnergy = 400,
            PrizeTech = 20,
            Photo = "conquest_1001_1",
            ShortName = $"新军_{agId}",
            CostMoney = 1000,
            CostGear = 750,
            CostAtomic = 300,
            Coefficient = 100,
            CityNum = 15
        };
        var re = new ArmyGroupReinforcementData { Id = reId, MapId = mapId, ArmyId = 303001, ArmyGroupId = agId, CostPoint = 6, Num = 1 };

        _parser.Items.Add(ag);
        _parser.Reinforcements.Add(re);
        _parser.Events.Add(ev);
        _parser.Stages.Add(stage);
        _parser.Calendars.Add(cal);
        _parser.SetEventDesc(eventId, $"自动生成_集团军_{eventId}_介绍");

        RefreshList();
        foreach (AgListEntry item in _listBox.Items)
            if (item.Id == agId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("ArmyGroup.TemplateCreated", agId, eventId));
    }

    private void OnAddReinforcement(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("ArmyGroup.SelectFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        int newId = _parser.GetNextReinforcementId();
        var stage = _parser.GetStageByEventId(_current.EventId);
        int mapId = stage?.Id ?? (_current.EventId * 100 + 1);
        var re = new ArmyGroupReinforcementData
        {
            Id = newId,
            MapId = mapId,
            ArmyId = 303001,
            ArmyGroupId = _current.Id,
            CostPoint = 6,
            Num = 1
        };
        _parser.Reinforcements.Add(re);
        RebuildReinforcements();
        SetStatus(Loc.F("ArmyGroup.ReinforcementAdded", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("ArmyGroup.SelectFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("ArmyGroup.DeleteConfirm", _current.Id, _current.ShortName),
            Loc.T("ArmyGroup.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.Reinforcements.RemoveAll(r => r.ArmyGroupId == id);
        _parser.Items.Remove(_current);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("ArmyGroup.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var missing = _parser.Items.Where(a => _parser.GetReinforcements(a.Id).Count == 0).Select(a => a.Id).ToList();
        if (missing.Count > 0)
        {
            var ids = string.Join(", ", missing.Take(20));
            if (missing.Count > 20) ids += " …";
            MessageBox.Show(Loc.F("ArmyGroup.SaveBlockedMessage", missing.Count, ids),
                Loc.T("ArmyGroup.SaveBlockedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.F("ArmyGroup.SaveBlockedStatus", missing.Count));
            return;
        }

        if (_parser.SaveAll())
        {
            SetStatus(Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("ArmyGroup.SaveSuccessMessage",
                    _parser.ConfigPath, _parser.ReinforcementPath, _parser.EventPath,
                    _parser.EventStagePath, _parser.EventCalendarPath, _parser.StringTablePath),
                Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", Loc.T("ArmyGroup.FiveTables")), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("ArmyGroup.Reloaded"));
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seen = new HashSet<int>();
        foreach (var a in _parser.Items)
        {
            if (!seen.Add(a.Id)) problems.Add(Loc.F("ArmyGroup.Validate.DuplicateId", a.Id));
            if (_parser.GetReinforcements(a.Id).Count == 0) problems.Add(Loc.F("ArmyGroup.Validate.NoReinforcement", a.Id));
            if (_parser.GetEventByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventSettings"));
            if (_parser.GetStageByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventStageSettings"));
            if (_parser.GetCalendarByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventCalendarSettings"));
        }

        if (problems.Count == 0)
            MessageBox.Show(Loc.F("Common.ValidationPassed", _parser.Items.Count), Loc.T("Common.Validate"), MessageBoxButton.OK, MessageBoxImage.Information);
        else
        {
            var msg = string.Join("\n", problems.Take(80));
            if (problems.Count > 80) msg += "\n" + Loc.F("Common.ValidationMore", problems.Count, 80);
            MessageBox.Show(msg, Loc.F("Common.ValidationProblems", problems.Count), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SetStatus(Loc.F("Common.ValidationDone", problems.Count));
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[ArmyGroupEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class AgListEntry
    {
        public int Id { get; set; }
        public string ShortName { get; set; } = "";
        public int EventId { get; set; }
        public override string ToString() => Loc.F("ArmyGroup.ListEntry", Id, ShortName, EventId);
    }
}
