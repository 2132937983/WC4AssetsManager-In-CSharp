using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.ArmyGroupSetting;

namespace WC4MapEditor.Views;

/// <summary>
/// 集团军与援军编辑场景（对应 Python 工具的 ArmyGroup 编辑器，五表联动防闪退：
/// ArmyGroupSettings + ArmyGroupReinforcementSettings + EventSettings +
/// EventStageSettings + EventCalendarSettings，文本写入 stringtable 的 event_{EventId}）。
/// </summary>
public class ArmyGroupEditScene : UserControl
{
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
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
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
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        topPanel.Children.Add(MakeTopBtn("返回", (_, _) => _window.ReturnToMainScene()));
        topPanel.Children.Add(MakeTopBtn("一键生成集团军模板", OnCreateTemplate));
        topPanel.Children.Add(MakeTopBtn("删除", OnDelete));
        topPanel.Children.Add(MakeTopBtn("保存", OnSave));
        topPanel.Children.Add(MakeTopBtn("重载", OnReload));
        topPanel.Children.Add(MakeTopBtn("校验", OnValidate));
        topPanel.Children.Add(new TextBlock
        {
            Text = "集团军与援军编辑（五表联动）",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        });
        topBar.Child = topPanel;
        _root.Children.Add(topBar);

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _searchBox = new TextBox
        {
            Margin = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White
        };
        _searchBox.Text = "搜索 (Id / 简称 / 活动ID)...";
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = ""; };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 简称 / 活动ID)..."; };
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(_searchBox, 0);
        leftGrid.Children.Add(_searchBox);

        _listBox = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13
        };
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);

        var splitter = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        Grid.SetRow(right, 1); Grid.SetColumn(right, 2);
        BuildRightPanel();
        right.Child = _propScroll;
        _root.Children.Add(right);

        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _statusText = new TextBlock
        {
            Text = $"已加载 {_parser.Items.Count} 个集团军",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        status.Child = _statusText;
        _root.Children.Add(status);

        Content = _root;
    }

    private void BuildRightPanel()
    {
        _propPanel = new StackPanel { Margin = new Thickness(0) };
        _propScroll = new ScrollViewer
        {
            Content = _propPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };

        AddSection("活动介绍 (stringtable event_{EventId})");
        AddRow("活动介绍(INI)", AddIniDesc());

        AddSection("核心配置");
        AddNum("A.Id", "集团军ID(Id)", 0, int.MaxValue);
        AddNum("A.EventId", "活动ID(EventId)", 0, int.MaxValue);
        AddNum("A.StageId", "关卡ID(StageId)", 0, int.MaxValue);
        AddNum("A.Seat", "序号(Seat)", 0, int.MaxValue);
        AddNum("A.Star", "星级(Star)", 0, int.MaxValue);
        AddNum("A.Camp", "阵营(Camp)", 0, int.MaxValue);
        AddNum("A.Difficulty", "难度(Difficulty)", 0, int.MaxValue);
        AddNum("A.MeritPoint", "功勋点(MeritPoint)", 0, int.MaxValue);
        AddNum("A.TechAcquire", "科技获取(TechAcquire)", 0, int.MaxValue);
        AddNum("A.CountryId", "国家ID(CountryId)", 0, int.MaxValue);
        AddNum("A.GroupId", "分组(GroupId)", 0, int.MaxValue);

        AddSection("奖励");
        AddNum("A.PrizeGold", "奖励金币(PrizeGold)", 0, int.MaxValue);
        AddNum("A.PrizeIndustry", "奖励工业(PrizeIndustry)", 0, int.MaxValue);
        AddNum("A.PrizeEnergy", "奖励能源(PrizeEnergy)", 0, int.MaxValue);
        AddNum("A.PrizeTech", "奖励科技(PrizeTech)", 0, int.MaxValue);

        AddSection("消耗 / 规模");
        AddNum("A.CostMoney", "消耗金钱(CostMoney)", 0, int.MaxValue);
        AddNum("A.CostGear", "消耗零件(CostGear)", 0, int.MaxValue);
        AddNum("A.CostAtomic", "消耗核能(CostAtomic)", 0, int.MaxValue);
        AddNum("A.Coefficient", "系数(Coefficient)", 0, int.MaxValue);
        AddNum("A.CityNum", "城市数(CityNum)", 0, int.MaxValue);

        AddSection("文本");
        AddText("A.Photo", "配图(Photo)");
        AddText("A.ShortName", "简称(ShortName)");

        AddSection("列表字段 (逗号分隔)");
        AddText("A.TechCategoryIds", "科技分类(TechCategoryIds)");
        AddText("A.CloseTechTypes", "禁用科技(CloseTechTypes)");
        AddText("A.CloseCardTypes", "禁用卡牌(CloseCardTypes)");
        AddText("A.GeneralId", "出场将领(GeneralId)");

        AddSection("专属援军 (>0 条，否则游戏会闪退)");
        _reWarn = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 12, Margin = new Thickness(8, 2, 8, 4), TextWrapping = TextWrapping.Wrap };
        _propPanel.Children.Add(_reWarn);
        _rePanel = new StackPanel { Margin = new Thickness(4, 0, 4, 4) };
        _propPanel.Children.Add(_rePanel);
        var addRe = MakeTopBtn("增加一队援军", OnAddReinforcement);
        addRe.HorizontalAlignment = HorizontalAlignment.Left;
        addRe.Margin = new Thickness(8, 4, 0, 8);
        _propPanel.Children.Add(addRe);

        AddSection("底层活动参数 (Event / Stage / Calendar)");
        _activityHint = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 12, Margin = new Thickness(8, 2, 8, 6), TextWrapping = TextWrapping.Wrap };
        _propPanel.Children.Add(_activityHint);

        _propPanel.Children.Add(new TextBlock { Text = "■ EventSettings", Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45)), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 6, 0, 2) });
        AddNum("E.Type", "Type", 0, int.MaxValue);
        AddNum("E.NormalId", "NormalId", 0, int.MaxValue);
        AddNum("E.NeedHQLv", "NeedHQLv", 0, int.MaxValue);
        AddNum("E.GeneralId", "GeneralId(单值)", 0, int.MaxValue);
        AddNum("E.DifficultStar", "DifficultStar", 0, int.MaxValue);
        AddText("E.Points", "Points(逗号分隔)");

        _propPanel.Children.Add(new TextBlock { Text = "■ EventStageSettings", Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45)), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 10, 0, 2) });
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
        AddText("S.RoundPhoto", "RoundPhoto(可空)");
        AddText("S.PrizeItem", "PrizeItem(逗号分隔)");
        AddText("S.PrizeGold", "PrizeGold(逗号分隔)");
        AddText("S.PrizeIndustry", "PrizeIndustry(逗号分隔)");
        AddText("S.PrizeEnergy", "PrizeEnergy(逗号分隔)");
        AddText("S.PrizeTech", "PrizeTech(逗号分隔)");
        AddText("S.PrizeMerit", "PrizeMerit(逗号分隔)");

        _propPanel.Children.Add(new TextBlock { Text = "■ EventCalendarSettings", Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45)), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 10, 0, 2) });
        AddNum("C.Id", "Id", 0, int.MaxValue);
        AddNum("C.LastingDays", "LastingDays", 0, int.MaxValue);
        AddNum("C.NoticeDays", "NoticeDays", 0, int.MaxValue);
        AddNum("C.PeriodDays", "PeriodDays", 0, int.MaxValue);
        AddNum("C.NeedHQLv", "NeedHQLv", 0, int.MaxValue);
        AddNum("C.Version", "Version", 0, int.MaxValue);
        AddText("C.StartDate", "StartDate(年,月,日)");
        AddText("C.PrizeIds", "PrizeIds(逗号分隔)");
        AddText("C.HardBuffs", "HardBuffs(逗号分隔)");
        AddText("C.MyBuffPool", "MyBuffPool(逗号分隔)");
        AddText("C.HardPrizeIds", "HardPrizeIds(逗号分隔)");

        AddSection("JSON 预览");
        _jsonPreview = new TextBox
        {
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xD6, 0x6B)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 4, 8, 12),
            Height = 200
        };
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    private static Button MakeTopBtn(string label, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = label,
            Height = 32,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13
        };
        btn.Click += handler;
        return btn;
    }

    private void AddSection(string title)
    {
        _propPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 10, 0, 4)
        });
    }

    private void AddRow(string label, UIElement content)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(content);
        border.Child = grid;
        _propPanel.Children.Add(border);
    }

    private NumericUpDown AddNum(string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _numBoxes[key] = ctrl;
        AddRow(label, ctrl);
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            CaretBrush = Brushes.White
        };
        tb.TextChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddIniDesc()
    {
        var tb = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            CaretBrush = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
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
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
        SetStatus($"已加载 {_parser.Items.Count} 个集团军，列表 {_listBox.Items.Count} 条");
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
            : $"⚠ 活动 {_current?.EventId} 缺少：{string.Join(" / ", missing)}，可用「一键生成集团军模板」补建。";
    }

    // ============================================================= 援军子面板 =============================================================
    private void RebuildReinforcements()
    {
        _rePanel.Children.Clear();
        if (_current == null) return;

        var list = _parser.GetReinforcements(_current.Id);
        _reWarn.Text = list.Count == 0
            ? "⛔ 严重警告：当前集团军没有援军配置！保存会被拦截（会导致游戏闪退）。"
            : $"当前 {list.Count} 队援军。";

        foreach (var r in list)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Margin = new Thickness(4, 3, 4, 3)
            };
            var grid = new Grid();
            for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var boxId = ReNum(r, x => x.Id, (x, v) => x.Id = v, 0, int.MaxValue);
            var boxMap = ReNum(r, x => x.MapId, (x, v) => x.MapId = v, 0, int.MaxValue);
            var boxArmy = ReNum(r, x => x.ArmyId, (x, v) => x.ArmyId = v, 0, int.MaxValue);
            var boxCost = ReNum(r, x => x.CostPoint, (x, v) => x.CostPoint = v, 0, int.MaxValue);
            var boxNum = ReNum(r, x => x.Num, (x, v) => x.Num = v, 0, int.MaxValue);
            var boxAg = ReNum(r, x => x.ArmyGroupId, (x, v) => x.ArmyGroupId = v, 0, int.MaxValue);

            AddReField(grid, 0, "援军ID", boxId);
            AddReField(grid, 1, "MapId", boxMap);
            AddReField(grid, 2, "兵种ID", boxArmy);
            AddReField(grid, 3, "消耗点数", boxCost);
            AddReField(grid, 4, "数量", boxNum);
            // ArmyGroupId 通常在右侧单独展示，这里合并到行尾
            var tail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tail.Children.Add(new TextBlock { Text = "军团:", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            boxAg.Width = 70;
            tail.Children.Add(boxAg);
            var del = new Button
            {
                Content = "删除",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11
            };
            del.Click += (_, _) =>
            {
                _parser.Reinforcements.Remove(r);
                RebuildReinforcements();
                SetStatus($"已删除援军 ID={r.Id}（未保存）");
            };
            tail.Children.Add(del);
            Grid.SetColumn(tail, 5);
            grid.Children.Add(tail);

            row.Child = grid;
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
        sp.Children.Add(new TextBlock { Text = label + ":", Foreground = Brushes.LightGray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
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
        SetStatus($"已更新集团军 {_current.Id}（未保存）");
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
        catch { _jsonPreview.Text = "(序列化失败)"; }
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
        SetStatus($"已一键生成模板：集团军 {agId}（活动 {eventId}），并植入 1 条保底援军（未保存）");
    }

    private void OnAddReinforcement(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个集团军", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
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
        SetStatus($"已新增援军 ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个集团军", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除集团军 [{_current.Id}] {_current.ShortName}？\n仅删除集团军与其专属援军，不会删除活动数据。",
            "删除集团军", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.Reinforcements.RemoveAll(r => r.ArmyGroupId == id);
        _parser.Items.Remove(_current);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除集团军 ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var missing = _parser.Items.Where(a => _parser.GetReinforcements(a.Id).Count == 0).Select(a => a.Id).ToList();
        if (missing.Count > 0)
        {
            var ids = string.Join(", ", missing.Take(20));
            if (missing.Count > 20) ids += " …";
            MessageBox.Show($"⛔ 保存拦截（防闪退机制）\n\n有 {missing.Count} 个集团军没有援军配置，游戏必然闪退！\n\n缺少援军的集团军 ID：\n{ids}\n\n请在「专属援军」面板中至少添加一队援军。",
                "保存拦截", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"保存被拦截：{missing.Count} 个集团军缺少援军");
            return;
        }

        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功（五表）：\n{_parser.ConfigPath}\n{_parser.ReinforcementPath}\n{_parser.EventPath}\n{_parser.EventStagePath}\n{_parser.EventCalendarPath}\nstringtable: {_parser.StringTablePath}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的五张表？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载五张表");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seen = new HashSet<int>();
        foreach (var a in _parser.Items)
        {
            if (!seen.Add(a.Id)) problems.Add($"[集团军 Id={a.Id}] ID 重复");
            if (_parser.GetReinforcements(a.Id).Count == 0) problems.Add($"[集团军 Id={a.Id}] 没有援军配置（会闪退）");
            if (_parser.GetEventByEventId(a.EventId) == null) problems.Add($"[集团军 Id={a.Id}] 活动 {a.EventId} 在 EventSettings 中缺失");
            if (_parser.GetStageByEventId(a.EventId) == null) problems.Add($"[集团军 Id={a.Id}] 活动 {a.EventId} 在 EventStageSettings 中缺失");
            if (_parser.GetCalendarByEventId(a.EventId) == null) problems.Add($"[集团军 Id={a.Id}] 活动 {a.EventId} 在 EventCalendarSettings 中缺失");
        }

        if (problems.Count == 0)
            MessageBox.Show($"校验通过，共 {_parser.Items.Count} 个集团军，未发现问题。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
        else
        {
            var msg = string.Join("\n", problems.Take(80));
            if (problems.Count > 80) msg += $"\n… 共 {problems.Count} 条，仅显示前 80 条";
            MessageBox.Show(msg, $"校验发现问题 {problems.Count} 条", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SetStatus($"校验完成：{problems.Count} 个问题");
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
        public override string ToString() => $"[{Id}] {ShortName}  (活动ID:{EventId})";
    }
}
