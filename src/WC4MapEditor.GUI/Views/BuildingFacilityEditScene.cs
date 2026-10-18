using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 建筑与设施编辑场景（对应 Python 工具的 BuildingFacility 编辑器，双模合一：
/// BuildingSettings.json + FacilitySettings.json，名称直接来自 JSON）。
/// </summary>
public class BuildingFacilityEditScene : UserControl
{
    /// <summary>表单行字段名列宽：这里的标签带英文属性名，比默认的 180 宽一点。</summary>
    private const double LabelWidth = 200;

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

        // 建筑 / 设施 两种模式的切换放在顶栏右侧、操作按钮之前
        var buildingRadio = new RadioButton
        {
            Content = Loc.T("Building.ModeBuilding"),
            GroupName = "mode",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var facilityRadio = new RadioButton
        {
            Content = Loc.T("Building.ModeFacility"),
            GroupName = "mode",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        buildingRadio.Checked += (_, _) => SwitchMode("building");
        facilityRadio.Checked += (_, _) => SwitchMode("facility");

        var topBar = EditorTheme.TopBar(Loc.T("Building.Title"), (_, _) => _window.ReturnToMainScene(),
            buildingRadio,
            facilityRadio,
            EditorTheme.VSeparator(),
            EditorTheme.Button(Loc.T("Common.Add"), OnAdd),
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

        var search = EditorTheme.SearchBox(Loc.T("Building.SearchPlaceholder"), out _searchBox);
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

        _propHost = new ContentControl();
        Grid.SetRow(_propHost, 1); Grid.SetColumn(_propHost, 2);
        BuildPanels();
        _root.Children.Add(_propHost);

        var status = EditorTheme.StatusBar(out _statusText, Loc.T("Common.Ready"));
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _root.Children.Add(status);

        Content = _root;
        _propHost.Content = _buildingScroll;
    }

    private void BuildPanels()
    {
        _buildingScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var bp = new StackPanel();
        bp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Preview")));
        bp.Children.Add(EditorTheme.FormRow(Loc.T("Building.Field.Texture"), BuildBuildingPreview(), LabelWidth));
        bp.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.BasicInfo")));
        AddNum(bp, "B.Id", Loc.T("Building.Field.BuildingId"), 0, int.MaxValue);
        AddText(bp, "B.Name", Loc.T("Building.Field.Name"));
        AddNum(bp, "B.Type", Loc.T("Building.Field.Type"), 0, int.MaxValue);
        bp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Produce")));
        AddNum(bp, "B.ProduceMoney", Loc.T("Building.Field.ProduceMoney"), 0, int.MaxValue);
        AddNum(bp, "B.ProduceGear", Loc.T("Building.Field.ProduceGear"), 0, int.MaxValue);
        AddNum(bp, "B.ProduceAtomic", Loc.T("Building.Field.ProduceAtomic"), 0, int.MaxValue);
        bp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Links")));
        AddNum(bp, "B.ArmyId", Loc.T("Building.Field.ArmyId"), 0, int.MaxValue);
        bp.Children.Add(EditorTheme.FormRow(Loc.T("Building.Field.FacilityIdLink"), BuildFacilityIdRow(), LabelWidth));
        bp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Other")));
        AddText(bp, "B.ResName", Loc.T("Building.Field.ResName"));
        AddNum(bp, "B.StyleCount", Loc.T("Building.Field.StyleCount"), 0, int.MaxValue);
        AddNum(bp, "B.MeritExp", Loc.T("Building.Field.MeritExp"), 0, int.MaxValue);
        _buildingJson = AddJsonPreview(bp);
        _buildingScroll.Content = bp;

        _facilityScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var fp = new StackPanel();
        fp.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.BasicInfo")));
        AddNum(fp, "F.Id", Loc.T("Building.Field.FacilityIdSelf"), 0, int.MaxValue);
        AddText(fp, "F.Name", Loc.T("Building.Field.Name"));
        AddNum(fp, "F.Level", Loc.T("Building.Field.Level"), 0, int.MaxValue);
        AddNum(fp, "F.Type", Loc.T("Building.Field.Type"), 0, int.MaxValue);
        var typeMeaning = EditorTheme.Muted(Loc.T("Building.TypeMeaningText"));
        typeMeaning.VerticalAlignment = VerticalAlignment.Center;
        fp.Children.Add(EditorTheme.FormRow(Loc.T("Building.Field.TypeMeaning"), typeMeaning, LabelWidth));
        fp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Recovery")));
        AddNum(fp, "F.CityRecovery", Loc.T("Building.Field.CityRecovery"), 0, int.MaxValue);
        AddNum(fp, "F.ArmyRecovery", Loc.T("Building.Field.ArmyRecovery"), 0, int.MaxValue);
        fp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Produce")));
        AddNum(fp, "F.ProduceMoney", Loc.T("Building.Field.ProduceMoney"), 0, int.MaxValue);
        AddNum(fp, "F.ProduceGear", Loc.T("Building.Field.ProduceGear"), 0, int.MaxValue);
        AddNum(fp, "F.ProduceAtomic", Loc.T("Building.Field.ProduceAtomic"), 0, int.MaxValue);
        fp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Cost")));
        AddNum(fp, "F.CostMoney", Loc.T("Building.Field.CostMoney"), 0, int.MaxValue);
        AddNum(fp, "F.CostGear", Loc.T("Building.Field.CostGear"), 0, int.MaxValue);
        AddNum(fp, "F.CostAtomic", Loc.T("Building.Field.CostAtomic"), 0, int.MaxValue);
        AddNum(fp, "F.Only", Loc.T("Building.Field.Only"), 0, int.MaxValue);
        fp.Children.Add(EditorTheme.SectionHeader(Loc.T("Building.Section.Unlock")));
        AddText(fp, "F.UnlockArmy", Loc.T("Building.Field.UnlockArmy"));
        AddText(fp, "F.UnlockArmyType", Loc.T("Building.Field.UnlockArmyType"));
        _facilityJson = AddJsonPreview(fp);
        _facilityScroll.Content = fp;
    }

    private UIElement BuildFacilityIdRow()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        _facilityIdBox = new NumericUpDown { MinValue = 0, MaxValue = int.MaxValue, Increment = 1, Width = 120 };
        _facilityIdBox.ValueChanged += (_, _) => { if (!_loadingUi && _building != null) ApplyBuilding(); };
        _numBoxes["B.FacilityId"] = _facilityIdBox;
        var jump = EditorTheme.Button(Loc.T("Building.JumpToFacility"));
        jump.Margin = new Thickness(8, 0, 0, 0);
        jump.Click += (_, _) => JumpToFacility((int)Math.Round(_facilityIdBox.Value));
        panel.Children.Add(_facilityIdBox);
        panel.Children.Add(jump);
        return panel;
    }

    // ============================================================= 控件辅助 =============================================================
    private NumericUpDown AddNum(StackPanel panel, string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _numBoxes[key] = ctrl;
        panel.Children.Add(EditorTheme.FormRow(label, ctrl, LabelWidth));
        return ctrl;
    }

    private TextBox AddText(StackPanel panel, string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi) ApplyActive(); };
        _textBoxes[key] = tb;
        panel.Children.Add(EditorTheme.FormRow(label, tb, LabelWidth));
        return tb;
    }

    private static TextBox AddJsonPreview(StackPanel panel)
    {
        panel.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.JsonPreview")));
        var tb = EditorTheme.MonoTextBox(180);
        tb.Margin = new Thickness(10, 8, 10, 12);
        panel.Children.Add(tb);
        return tb;
    }

    // ============================================================= 模式切换 =============================================================
    private void SwitchMode(string mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _propHost.Content = mode == "building" ? _buildingScroll : _facilityScroll;
        _searchBox.Text = "";
        RefreshList();
        ShowEmpty();
        SetStatus(mode == "building" ? Loc.T("Building.SwitchedToBuilding") : Loc.T("Building.SwitchedToFacility"));
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        _listBox.Items.Clear();
        if (_mode == "building")
        {
            foreach (var b in _parser.Buildings.Where(b =>
                !searching || b.Id.ToString().Contains(q) || (b.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                _listBox.Items.Add(new ItemEntry { Id = b.Id, Display = $"[{b.Id}] {b.Name}" });
            }
            SetStatus(Loc.F("Building.BuildingsLoaded", _parser.Buildings.Count, _listBox.Items.Count));
        }
        else
        {
            foreach (var f in _parser.Facilities.Where(f =>
                !searching || f.Id.ToString().Contains(q) || (f.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                _listBox.Items.Add(new ItemEntry { Id = f.Id, Display = $"[{f.Id}] {f.Name}" });
            }
            SetStatus(Loc.F("Building.FacilitiesLoaded", _parser.Facilities.Count, _listBox.Items.Count));
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
        SetStatus(Loc.F("Building.BuildingUpdated", _building.Id));
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
        SetStatus(Loc.F("Building.FacilityUpdated", _facility.Id));
        UpdateJsonPreview();
    }

    private UIElement BuildBuildingPreview()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        _buildingPreview = new Image { Width = 160, Height = 160, Stretch = Stretch.Uniform };
        var card = EditorTheme.Card(_buildingPreview, new Thickness(6));
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(0);
        _buildingPreviewTip = EditorTheme.Muted("");
        _buildingPreviewTip.FontSize = 11;
        _buildingPreviewTip.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(card);
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
        _buildingPreviewTip.Text = img == null ? Loc.F("Building.TextureNotFound", name) : name;
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
            MessageBox.Show(Loc.F("Building.FacilityNotFound", id), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _mode = "facility";
        _propHost.Content = _facilityScroll;
        _searchBox.Text = "";
        RefreshList();
        foreach (ItemEntry item in _listBox.Items)
            if (item.Id == id) { _listBox.SelectedItem = item; _listBox.ScrollIntoView(item); break; }
        SetStatus(Loc.F("Building.JumpedToFacility", id));
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
        catch { target.Text = Loc.T("Common.SerializeFailed"); }
    }

    // ============================================================= 操作 =============================================================
    private void OnAdd(object sender, RoutedEventArgs e)
    {
        // 默认名称会写进 JSON，是数据不是界面文字，保持原样
        if (_mode == "building")
        {
            int newId = _parser.GetNextBuildingId();
            var b = new BuildingSettingData { Id = newId, Name = $"NewBuilding{newId}", Type = 1 };
            _parser.BuildingItems.Add(b);
            RefreshList();
            foreach (ItemEntry item in _listBox.Items)
                if (item.Id == newId) { _listBox.SelectedItem = item; break; }
            SetStatus(Loc.F("Building.BuildingAdded", newId));
        }
        else
        {
            int newId = _parser.GetNextFacilityId();
            var f = new FacilitySettingData { Id = newId, Name = $"NewFacility{newId}", Level = 1, Type = 1 };
            _parser.FacilityItems.Add(f);
            RefreshList();
            foreach (ItemEntry item in _listBox.Items)
                if (item.Id == newId) { _listBox.SelectedItem = item; break; }
            SetStatus(Loc.F("Building.FacilityAdded", newId));
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_mode == "building")
        {
            if (_building == null) { MessageBox.Show(Loc.T("Building.SelectBuildingFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show(Loc.F("Building.DeleteBuildingConfirm", _building.Id, _building.Name), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int id = _building.Id;
            _parser.BuildingItems.Remove(_building);
            _building = null;
            RefreshList();
            ShowEmpty();
            SetStatus(Loc.F("Building.BuildingDeleted", id));
        }
        else
        {
            if (_facility == null) { MessageBox.Show(Loc.T("Building.SelectFacilityFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show(Loc.F("Building.DeleteFacilityConfirm", _facility.Id, _facility.Name), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int id = _facility.Id;
            _parser.FacilityItems.Remove(_facility);
            _facility = null;
            RefreshList();
            ShowEmpty();
            SetStatus(Loc.F("Building.FacilityDeleted", id));
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus(Loc.F("Common.SavedTo", $"{_parser.BuildingConfigPath} / {_parser.FacilityConfigPath}"));
            MessageBox.Show(Loc.F("Building.SaveSuccessMessage", _parser.BuildingConfigPath, _parser.FacilityConfigPath),
                Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "BuildingSettings.json & FacilitySettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenB = new HashSet<int>();
        foreach (var b in _parser.Buildings)
        {
            if (!seenB.Add(b.Id)) problems.Add(Loc.F("Building.Validate.BuildingDuplicateId", b.Id));
            if (b.FacilityId != 0 && _parser.GetFacilityById(b.FacilityId) == null)
                problems.Add(Loc.F("Building.Validate.FacilityMissing", b.Id, b.FacilityId));
        }
        var seenF = new HashSet<int>();
        foreach (var f in _parser.Facilities)
        {
            if (!seenF.Add(f.Id)) problems.Add(Loc.F("Building.Validate.FacilityDuplicateId", f.Id));
            if (string.IsNullOrEmpty(f.Name)) problems.Add(Loc.F("Building.Validate.FacilityNameEmpty", f.Id));
        }

        if (problems.Count == 0)
            MessageBox.Show(Loc.F("Building.ValidationPassed", _parser.Buildings.Count, _parser.Facilities.Count), Loc.T("Common.Validate"), MessageBoxButton.OK, MessageBoxImage.Information);
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
