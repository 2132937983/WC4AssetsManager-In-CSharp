using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Views;

/// <summary>
/// 建筑与设施编辑场景（对应 Python 工具的 BuildingFacility 编辑器，双模合一：
/// BuildingSettings.json + FacilitySettings.json，名称直接来自 JSON）。
/// </summary>
public class BuildingFacilityEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly BuildingFacilitySettingParser _parser = BuildingFacilitySettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;
    private ContentControl _propHost = null!;
    private ScrollViewer _buildingScroll = null!;
    private ScrollViewer _facilityScroll = null!;

    private string _mode = "building";
    private BuildingSettingData? _building;
    private FacilitySettingData? _facility;
    private bool _loadingUi;

    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _checkBoxes = new(StringComparer.Ordinal);
    private NumericUpDown _facilityIdBox = null!;
    private Image _buildingPreview = null!;
    private TextBlock _buildingPreviewTip = null!;
    private TextBox _buildingJson = null!;
    private TextBox _facilityJson = null!;

    public BuildingFacilityEditScene(MainWindow window)
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
        topPanel.Children.Add(MakeTopBtn("新增", OnAdd));
        topPanel.Children.Add(MakeTopBtn("删除", OnDelete));
        topPanel.Children.Add(MakeTopBtn("保存", OnSave));
        topPanel.Children.Add(MakeTopBtn("重载", OnReload));
        topPanel.Children.Add(MakeTopBtn("校验", OnValidate));

        var buildingRadio = new RadioButton
        {
            Content = "建筑 (BuildingSettings)",
            Foreground = Brushes.White,
            GroupName = "mode",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 8, 0)
        };
        var facilityRadio = new RadioButton
        {
            Content = "设施 (FacilitySettings)",
            Foreground = Brushes.White,
            GroupName = "mode",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        buildingRadio.Checked += (_, _) => SwitchMode("building");
        facilityRadio.Checked += (_, _) => SwitchMode("facility");
        topPanel.Children.Add(buildingRadio);
        topPanel.Children.Add(facilityRadio);

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
        _searchBox.Text = "搜索 (Id / 名称)...";
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = ""; };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 名称)..."; };
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

        _propHost = new ContentControl();
        Grid.SetRow(_propHost, 1); Grid.SetColumn(_propHost, 2);
        BuildPanels();
        _root.Children.Add(_propHost);

        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _statusText = new TextBlock
        {
            Text = "就绪",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        status.Child = _statusText;
        _root.Children.Add(status);

        Content = _root;
        _propHost.Content = _buildingScroll;
    }

    private void BuildPanels()
    {
        _buildingScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var bp = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        AddSection(bp, "贴图预览 (buildings_hd)");
        AddRow(bp, "建筑贴图", BuildBuildingPreview());
        AddSection(bp, "基本信息");
        AddNum(bp, "B.Id", "建筑ID(Id)", 0, int.MaxValue);
        AddText(bp, "B.Name", "名称(Name)");
        AddNum(bp, "B.Type", "类型(Type)", 0, int.MaxValue);
        AddSection(bp, "产出");
        AddNum(bp, "B.ProduceMoney", "产出金钱(ProduceMoney)", 0, int.MaxValue);
        AddNum(bp, "B.ProduceGear", "产出零件(ProduceGear)", 0, int.MaxValue);
        AddNum(bp, "B.ProduceAtomic", "产出核能(ProduceAtomic)", 0, int.MaxValue);
        AddSection(bp, "关联");
        AddNum(bp, "B.ArmyId", "关联兵种(ArmyId)", 0, int.MaxValue);
        AddRow(bp, "关联设施(FacilityId)", BuildFacilityIdRow());
        AddSection(bp, "其它");
        AddText(bp, "B.ResName", "资源名(ResName，可空)");
        AddNum(bp, "B.StyleCount", "样式数量(StyleCount)", 0, int.MaxValue);
        AddNum(bp, "B.MeritExp", "功勋经验(MeritExp)", 0, int.MaxValue);
        _buildingJson = AddJsonPreview(bp);
        _buildingScroll.Content = bp;

        _facilityScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var fp = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        AddSection(fp, "基本信息");
        AddNum(fp, "F.Id", "设施ID(Id)", 0, int.MaxValue);
        AddText(fp, "F.Name", "名称(Name)");
        AddNum(fp, "F.Level", "等级(Level)", 0, int.MaxValue);
        AddNum(fp, "F.Type", "类型(Type)", 0, int.MaxValue);
        AddRow(fp, "类型含义", new TextBlock
        {
            Text = "1:城池 2:工厂 3:科研所 4:机场 5:导弹",
            Foreground = Brushes.Gray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        AddSection(fp, "恢复");
        AddNum(fp, "F.CityRecovery", "城池恢复(CityRecovery)", 0, int.MaxValue);
        AddNum(fp, "F.ArmyRecovery", "部队恢复(ArmyRecovery)", 0, int.MaxValue);
        AddSection(fp, "产出");
        AddNum(fp, "F.ProduceMoney", "产出金钱(ProduceMoney)", 0, int.MaxValue);
        AddNum(fp, "F.ProduceGear", "产出零件(ProduceGear)", 0, int.MaxValue);
        AddNum(fp, "F.ProduceAtomic", "产出核能(ProduceAtomic)", 0, int.MaxValue);
        AddSection(fp, "消耗");
        AddNum(fp, "F.CostMoney", "消耗金钱(CostMoney)", 0, int.MaxValue);
        AddNum(fp, "F.CostGear", "消耗零件(CostGear)", 0, int.MaxValue);
        AddNum(fp, "F.CostAtomic", "消耗核能(CostAtomic)", 0, int.MaxValue);
        AddNum(fp, "F.Only", "仅限(Only)", 0, int.MaxValue);
        AddSection(fp, "解锁列表 (逗号分隔)");
        AddText(fp, "F.UnlockArmy", "解锁兵种(UnlockArmy)");
        AddText(fp, "F.UnlockArmyType", "解锁兵种类型(UnlockArmyType)");
        _facilityJson = AddJsonPreview(fp);
        _facilityScroll.Content = fp;
    }

    private UIElement BuildFacilityIdRow()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        _facilityIdBox = new NumericUpDown { MinValue = 0, MaxValue = int.MaxValue, Increment = 1, Width = 120 };
        _facilityIdBox.ValueChanged += (_, _) => { if (!_loadingUi && _building != null) ApplyBuilding(); };
        _numBoxes["B.FacilityId"] = _facilityIdBox;
        var jump = new Button
        {
            Content = "跳转到该设施",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 2, 10, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 12
        };
        jump.Click += (_, _) => JumpToFacility((int)Math.Round(_facilityIdBox.Value));
        panel.Children.Add(_facilityIdBox);
        panel.Children.Add(jump);
        return panel;
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

    private static void AddSection(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 10, 0, 4)
        });
    }

    private static void AddRow(StackPanel panel, string label, UIElement content)
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
        panel.Children.Add(border);
    }

    private NumericUpDown AddNum(StackPanel panel, string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _numBoxes[key] = ctrl;
        AddRow(panel, label, ctrl);
        return ctrl;
    }

    private TextBox AddText(StackPanel panel, string key, string label)
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
        AddRow(panel, label, tb);
        return tb;
    }

    private static TextBox AddJsonPreview(StackPanel panel)
    {
        AddSection(panel, "JSON 预览");
        var tb = new TextBox
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
            Height = 180
        };
        panel.Children.Add(tb);
        return tb;
    }

    // ============================================================= 模式切换 =============================================================
    private void SwitchMode(string mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _propHost.Content = mode == "building" ? _buildingScroll : _facilityScroll;
        _searchBox.Text = "搜索 (Id / 名称)...";
        RefreshList();
        ShowEmpty();
        SetStatus(mode == "building" ? "已切换到 建筑" : "已切换到 设施");
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

        _listBox.Items.Clear();
        if (_mode == "building")
        {
            foreach (var b in _parser.Buildings.Where(b =>
                !searching || b.Id.ToString().Contains(q) || (b.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                _listBox.Items.Add(new ItemEntry { Id = b.Id, Display = $"[{b.Id}] {b.Name}" });
            }
            SetStatus($"建筑 {_parser.Buildings.Count} 条，列表 {_listBox.Items.Count} 条");
        }
        else
        {
            foreach (var f in _parser.Facilities.Where(f =>
                !searching || f.Id.ToString().Contains(q) || (f.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                _listBox.Items.Add(new ItemEntry { Id = f.Id, Display = $"[{f.Id}] {f.Name}" });
            }
            SetStatus($"设施 {_parser.Facilities.Count} 条，列表 {_listBox.Items.Count} 条");
        }
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not ItemEntry entry) { ShowEmpty(); return; }
        if (_mode == "building")
        {
            _building = _parser.GetBuildingById(entry.Id);
            if (_building == null) { ShowEmpty(); return; }
            LoadBuilding();
        }
        else
        {
            _facility = _parser.GetFacilityById(entry.Id);
            if (_facility == null) { ShowEmpty(); return; }
            LoadFacility();
        }
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = nb.MinValue;
        foreach (var cb in _checkBoxes.Values) cb.IsChecked = false;
        if (_buildingJson != null) _buildingJson.Text = "";
        if (_facilityJson != null) _facilityJson.Text = "";
        if (_buildingPreview != null) _buildingPreview.Source = null;
        if (_buildingPreviewTip != null) _buildingPreviewTip.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 属性读写 =============================================================
    private void LoadBuilding()
    {
        if (_building == null) return;
        _loadingUi = true;
        SetNum("B.Id", _building.Id);
        SetNum("B.Type", _building.Type);
        SetNum("B.ProduceMoney", _building.ProduceMoney);
        SetNum("B.ProduceGear", _building.ProduceGear);
        SetNum("B.ProduceAtomic", _building.ProduceAtomic);
        SetNum("B.ArmyId", _building.ArmyId);
        SetNum("B.FacilityId", _building.FacilityId);
        SetNum("B.StyleCount", _building.StyleCount);
        SetNum("B.MeritExp", _building.MeritExp);
        _textBoxes["B.Name"].Text = _building.Name ?? "";
        _textBoxes["B.ResName"].Text = _building.ResName ?? "";
        _loadingUi = false;
        RefreshBuildingPreview();
        UpdateJsonPreview();
    }

    private void LoadFacility()
    {
        if (_facility == null) return;
        _loadingUi = true;
        SetNum("F.Id", _facility.Id);
        SetNum("F.Level", _facility.Level);
        SetNum("F.Type", _facility.Type);
        SetNum("F.CityRecovery", _facility.CityRecovery);
        SetNum("F.ArmyRecovery", _facility.ArmyRecovery);
        SetNum("F.ProduceMoney", _facility.ProduceMoney);
        SetNum("F.ProduceGear", _facility.ProduceGear);
        SetNum("F.ProduceAtomic", _facility.ProduceAtomic);
        SetNum("F.CostMoney", _facility.CostMoney);
        SetNum("F.CostGear", _facility.CostGear);
        SetNum("F.CostAtomic", _facility.CostAtomic);
        SetNum("F.Only", _facility.Only);
        _textBoxes["F.Name"].Text = _facility.Name ?? "";
        _textBoxes["F.UnlockArmy"].Text = FormatList(_facility.UnlockArmy);
        _textBoxes["F.UnlockArmyType"].Text = FormatList(_facility.UnlockArmyType);
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyActive()
    {
        if (_mode == "building" && _building != null) ApplyBuilding();
        else if (_mode == "facility" && _facility != null) ApplyFacility();
    }

    private void ApplyBuilding()
    {
        if (_building == null) return;
        _building.Id = GetInt("B.Id");
        _building.Type = GetInt("B.Type");
        _building.ProduceMoney = GetInt("B.ProduceMoney");
        _building.ProduceGear = GetInt("B.ProduceGear");
        _building.ProduceAtomic = GetInt("B.ProduceAtomic");
        _building.ArmyId = GetInt("B.ArmyId");
        _building.FacilityId = GetInt("B.FacilityId");
        _building.StyleCount = GetInt("B.StyleCount");
        _building.MeritExp = GetInt("B.MeritExp");
        _building.Name = _textBoxes["B.Name"].Text;
        var res = _textBoxes["B.ResName"].Text;
        _building.ResName = string.IsNullOrWhiteSpace(res) ? null : res;

        SyncListItem(_building.Id, _building.Name);
        SetStatus($"已更新建筑 {_building.Id}（未保存）");
        UpdateJsonPreview();
    }

    private void ApplyFacility()
    {
        if (_facility == null) return;
        _facility.Id = GetInt("F.Id");
        _facility.Level = GetInt("F.Level");
        _facility.Type = GetInt("F.Type");
        _facility.CityRecovery = GetInt("F.CityRecovery");
        _facility.ArmyRecovery = GetInt("F.ArmyRecovery");
        _facility.ProduceMoney = GetInt("F.ProduceMoney");
        _facility.ProduceGear = GetInt("F.ProduceGear");
        _facility.ProduceAtomic = GetInt("F.ProduceAtomic");
        _facility.CostMoney = GetInt("F.CostMoney");
        _facility.CostGear = GetInt("F.CostGear");
        _facility.CostAtomic = GetInt("F.CostAtomic");
        _facility.Only = GetInt("F.Only");
        _facility.Name = _textBoxes["F.Name"].Text;
        _facility.UnlockArmy = ParseIntList(_textBoxes["F.UnlockArmy"].Text);
        _facility.UnlockArmyType = ParseIntList(_textBoxes["F.UnlockArmyType"].Text);

        SyncListItem(_facility.Id, _facility.Name);
        SetStatus($"已更新设施 {_facility.Id}（未保存）");
        UpdateJsonPreview();
    }

    private UIElement BuildBuildingPreview()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _buildingPreview = new Image { Width = 160, Height = 160, Stretch = Stretch.Uniform };
        border.Child = _buildingPreview;
        _buildingPreviewTip = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(border);
        panel.Children.Add(_buildingPreviewTip);
        return panel;
    }

    private void RefreshBuildingPreview()
    {
        if (_buildingPreview == null) return;
        if (_building == null || _mode != "building")
        {
            _buildingPreview.Source = null;
            _buildingPreviewTip.Text = "";
            return;
        }
        string name = $"building_{_building.Id}.png";
        var img = HdAtlasImageLoader.Load("buildings_hd", name);
        _buildingPreview.Source = img;
        _buildingPreviewTip.Text = img == null ? $"未找到贴图 {name}" : name;
    }

    private void SyncListItem(int id, string name)
    {
        if (_listBox.SelectedItem is ItemEntry le && le.Id == id)
        {
            var idx = _listBox.SelectedIndex;
            _listBox.Items[idx] = new ItemEntry { Id = id, Display = $"[{id}] {name}" };
            _listBox.SelectedIndex = idx;
        }
    }

    private void JumpToFacility(int id)
    {
        var target = _parser.GetFacilityById(id);
        if (id <= 0 || target == null)
        {
            MessageBox.Show($"未在设施库中找到 ID={id} 的设施", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _mode = "facility";
        _propHost.Content = _facilityScroll;
        _searchBox.Text = "搜索 (Id / 名称)...";
        RefreshList();
        foreach (ItemEntry item in _listBox.Items)
            if (item.Id == id) { _listBox.SelectedItem = item; _listBox.ScrollIntoView(item); break; }
        SetStatus($"已跳转到设施 {id}");
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
        bool building = _mode == "building";
        var target = building ? _buildingJson : _facilityJson;
        if (target == null) return;
        object? cur = building ? _building : _facility;
        if (cur == null) { target.Text = ""; return; }
        try { target.Text = JsonSerializer.Serialize(cur, new JsonSerializerOptions { WriteIndented = true }); }
        catch { target.Text = "(序列化失败)"; }
    }

    // ============================================================= 操作 =============================================================
    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (_mode == "building")
        {
            int newId = _parser.GetNextBuildingId();
            var b = new BuildingSettingData { Id = newId, Name = $"NewBuilding{newId}", Type = 1 };
            _parser.BuildingItems.Add(b);
            RefreshList();
            foreach (ItemEntry item in _listBox.Items)
                if (item.Id == newId) { _listBox.SelectedItem = item; break; }
            SetStatus($"新增建筑 ID={newId}（未保存）");
        }
        else
        {
            int newId = _parser.GetNextFacilityId();
            var f = new FacilitySettingData { Id = newId, Name = $"NewFacility{newId}", Level = 1, Type = 1 };
            _parser.FacilityItems.Add(f);
            RefreshList();
            foreach (ItemEntry item in _listBox.Items)
                if (item.Id == newId) { _listBox.SelectedItem = item; break; }
            SetStatus($"新增设施 ID={newId}（未保存）");
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_mode == "building")
        {
            if (_building == null) { MessageBox.Show("请先在左侧选中一个建筑", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show($"确定删除建筑 [{_building.Id}] {_building.Name}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int id = _building.Id;
            _parser.BuildingItems.Remove(_building);
            _building = null;
            RefreshList();
            ShowEmpty();
            SetStatus($"已删除建筑 ID={id}（未保存）");
        }
        else
        {
            if (_facility == null) { MessageBox.Show("请先在左侧选中一个设施", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show($"确定删除设施 [{_facility.Id}] {_facility.Name}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int id = _facility.Id;
            _parser.FacilityItems.Remove(_facility);
            _facility = null;
            RefreshList();
            ShowEmpty();
            SetStatus($"已删除设施 ID={id}（未保存）");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.BuildingConfigPath} / {_parser.FacilityConfigPath}");
            MessageBox.Show($"保存成功：\nBuildingSettings.json: {_parser.BuildingConfigPath}\nFacilitySettings.json: {_parser.FacilityConfigPath}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 BuildingSettings.json 与 FacilitySettings.json？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenB = new HashSet<int>();
        foreach (var b in _parser.Buildings)
        {
            if (!seenB.Add(b.Id)) problems.Add($"[建筑 Id={b.Id}] ID 重复");
            if (b.FacilityId != 0 && _parser.GetFacilityById(b.FacilityId) == null)
                problems.Add($"[建筑 Id={b.Id}] FacilityId={b.FacilityId} 找不到对应设施");
        }
        var seenF = new HashSet<int>();
        foreach (var f in _parser.Facilities)
        {
            if (!seenF.Add(f.Id)) problems.Add($"[设施 Id={f.Id}] ID 重复");
            if (string.IsNullOrEmpty(f.Name)) problems.Add($"[设施 Id={f.Id}] 名称为空");
        }

        if (problems.Count == 0)
            MessageBox.Show($"校验通过：建筑 {_parser.Buildings.Count} 条，设施 {_parser.Facilities.Count} 条。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
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
        Debug.WriteLine($"[BuildingFacilityEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class ItemEntry
    {
        public int Id { get; set; }
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }
}
