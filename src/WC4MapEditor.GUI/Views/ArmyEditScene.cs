using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 兵种编辑场景（对应 Python 工具的 Unit 编辑器，三文件联动：
/// ArmySettings.json + def_armypos.xml + stringtable 的 unit_name_{Id}）。
/// </summary>
public class ArmyEditScene : UserControl
{
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly ArmySettingParser _parser = ArmySettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private ArmySettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _checkBoxes = new(StringComparer.Ordinal);
    private TextBox _iniNameBox = null!;
    private TextBox _jsonPreview = null!;

    public ArmyEditScene(MainWindow window)
    {
        EditorTheme.Apply(this);
        _window = window;
        BuildUI();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_parser.Items.Count > 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
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

        var topBar = EditorTheme.TopBar(Loc.T("Army.Title"), (_, _) => _window.ReturnToMainScene(),
            EditorTheme.Button(Loc.T("Common.Add"), OnAdd),
            EditorTheme.DangerButton(Loc.T("Common.Delete"), OnDelete),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.Button(Loc.T("Common.Validate"), OnValidate),
            EditorTheme.Button(Loc.T("Army.Simulate"), OnSimulate));
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        _root.Children.Add(topBar);

        // 左栏：搜索 + 列表
        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = EditorTheme.SearchBox(Loc.T("Army.SearchPlaceholder"), out _searchBox);
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

        // 右栏：属性面板
        BuildRightPanel();
        Grid.SetRow(_propScroll, 1); Grid.SetColumn(_propScroll, 2);
        _root.Children.Add(_propScroll);

        var status = EditorTheme.StatusBar(out _statusText);
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

        AddSection(Loc.T("Army.Section.Text"));
        AddRow(Loc.T("Army.Field.IniName"), AddIniName());

        AddSection(Loc.T("Army.Section.Xml"));
        AddNum("_xml_x", Loc.T("Army.Field.XmlX"), -9999, 9999);
        AddNum("_xml_y", Loc.T("Army.Field.XmlY"), -9999, 9999);
        AddNum("_xml_scale", Loc.T("Army.Field.XmlScale"), 0.01, 10, 0.05);

        AddSection(Loc.T("Common.BasicInfo"));
        AddNum("Id", Loc.T("Army.Field.Id"), 0, int.MaxValue);
        AddText("Name", Loc.T("Army.Field.Name"));
        AddText("Anim", Loc.T("Army.Field.Anim"));
        AddNum("Army", Loc.T("Army.Field.Army"), 0, int.MaxValue);
        AddNum("SubType", Loc.T("Army.Field.SubType"), 0, int.MaxValue);
        AddNum("Elite", Loc.T("Army.Field.Elite"), 0, int.MaxValue);
        AddNum("Type", Loc.T("Army.Field.Type"), 0, int.MaxValue);
        AddNum("Ranking", Loc.T("Army.Field.Ranking"), 0, int.MaxValue);

        AddSection(Loc.T("Army.Section.Combat"));
        AddNum("MinAttack", Loc.T("Army.Field.MinAttack"), 0, int.MaxValue);
        AddNum("MaxAttack", Loc.T("Army.Field.MaxAttack"), 0, int.MaxValue);
        AddNum("MinRange", Loc.T("Army.Field.MinRange"), 0, int.MaxValue);
        AddNum("MaxRange", Loc.T("Army.Field.MaxRange"), 0, int.MaxValue);
        AddNum("HP", Loc.T("Army.Field.HP"), 0, int.MaxValue);
        AddNum("Defence", Loc.T("Army.Field.Defence"), 0, int.MaxValue);
        AddNum("Mobility", Loc.T("Army.Field.Mobility"), 0, int.MaxValue);
        AddNum("MeritExp", Loc.T("Army.Field.MeritExp"), 0, int.MaxValue);

        AddSection(Loc.T("Army.Section.Cost"));
        AddNum("CostMoney", Loc.T("Army.Field.CostMoney"), 0, int.MaxValue);
        AddNum("CostGear", Loc.T("Army.Field.CostGear"), 0, int.MaxValue);
        AddNum("CostAtomic", Loc.T("Army.Field.CostAtomic"), 0, int.MaxValue);
        AddNum("CostPoints", Loc.T("Army.Field.CostPoints"), 0, int.MaxValue);

        AddSection(Loc.T("Army.Section.Formation"));
        AddNum("Carrier", Loc.T("Army.Field.Carrier"), 0, int.MaxValue);
        AddNum("BuildTime", Loc.T("Army.Field.BuildTime"), 0, int.MaxValue);
        AddNum("BuildCD", Loc.T("Army.Field.BuildCD"), 0, int.MaxValue);
        AddNum("AOE1", Loc.T("Army.Field.AOE1"), 0, int.MaxValue);
        AddNum("AOE2", Loc.T("Army.Field.AOE2"), 0, int.MaxValue);
        AddNum("MaxElite", Loc.T("Army.Field.MaxElite"), 0, int.MaxValue);
        AddNum("MaxFormation", Loc.T("Army.Field.MaxFormation"), 0, int.MaxValue);
        AddCheck("Formation", Loc.T("Army.Field.Formation"));

        AddSection(Loc.T("Army.Section.Lists"));
        AddList("Feature", Loc.T("Army.Field.Feature"));
        AddList("FeatureLevel", Loc.T("Army.Field.FeatureLevel"));
        AddList("Country", Loc.T("Army.Field.Country"));
        AddList("FormationChance", Loc.T("Army.Field.FormationChance"));

        AddSection(Loc.T("Common.JsonPreview"));
        _jsonPreview = EditorTheme.MonoTextBox(200);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    // Add* 只负责把控件登记进字典并挂上变更回调，外观全部交给 EditorTheme 的隐式样式
    private void AddSection(string title) => _propPanel.Children.Add(EditorTheme.SectionHeader(title));

    private void AddRow(string label, UIElement content) => _propPanel.Children.Add(EditorTheme.FormRow(label, content, LabelWidth));

    private NumericUpDown AddNum(string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _numBoxes[key] = ctrl;
        AddRow(label, ctrl);
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddList(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddIniName()
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _iniNameBox = tb;
        return tb;
    }

    private void AddCheck(string key, string label)
    {
        var cb = new CheckBox { Content = label, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        cb.Unchecked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _checkBoxes[key] = cb;
        AddRow(label, cb);
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(u =>
        {
            if (!searching) return true;
            if (u.Id.ToString().Contains(q)) return true;
            if ((u.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (_parser.GetUnitName(u.Id).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        });

        int? keepId = _current?.Id;
        _listBox.Items.Clear();
        foreach (var u in items)
        {
            var name = _parser.GetUnitName(u.Id);
            if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(u.Name) ? Loc.F("Army.UnnamedEntry", u.Id) : u.Name;
            var entry = new ArmyListEntry { Id = u.Id, Name = name, Army = u.Army, Type = u.Type };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && u.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus(Loc.F("Army.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not ArmyListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        LoadUiFromCurrent();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = nb.MinValue;
        foreach (var cb in _checkBoxes.Values) cb.IsChecked = false;
        _iniNameBox.Text = "";
        _jsonPreview.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 属性读写 =============================================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        SetNum("Id", _current.Id);
        SetNum("Army", _current.Army);
        SetNum("SubType", _current.SubType);
        SetNum("Elite", _current.Elite);
        SetNum("Type", _current.Type);
        SetNum("Ranking", _current.Ranking);
        SetNum("MinAttack", _current.MinAttack);
        SetNum("MaxAttack", _current.MaxAttack);
        SetNum("MinRange", _current.MinRange);
        SetNum("MaxRange", _current.MaxRange);
        SetNum("HP", _current.HP);
        SetNum("Defence", _current.Defence);
        SetNum("Mobility", _current.Mobility);
        SetNum("MeritExp", _current.MeritExp);
        SetNum("CostMoney", _current.CostMoney);
        SetNum("CostGear", _current.CostGear);
        SetNum("CostAtomic", _current.CostAtomic);
        SetNum("CostPoints", _current.CostPoints);
        SetNum("Carrier", _current.Carrier);
        SetNum("BuildTime", _current.BuildTime);
        SetNum("BuildCD", _current.BuildCD);
        SetNum("AOE1", _current.AOE1);
        SetNum("AOE2", _current.AOE2);
        SetNum("MaxElite", _current.MaxElite);
        SetNum("MaxFormation", _current.MaxFormation);
        _textBoxes["Name"].Text = _current.Name ?? "";
        _textBoxes["Anim"].Text = _current.Anim ?? "";
        _textBoxes["Feature"].Text = FormatList(_current.Feature);
        _textBoxes["FeatureLevel"].Text = FormatList(_current.FeatureLevel);
        _textBoxes["Country"].Text = FormatList(_current.Country);
        _textBoxes["FormationChance"].Text = FormatList(_current.FormationChance);
        _checkBoxes["Formation"].IsChecked = _current.Formation;
        _iniNameBox.Text = _parser.GetUnitName(_current.Id);

        var p = _parser.GetPos(_current.Id);
        SetNum("_xml_x", p?.PosX ?? 0);
        SetNum("_xml_y", p?.PosY ?? 0);
        SetNum("_xml_scale", p?.Scale ?? 1.0);

        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyProp()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.Army = GetInt("Army");
        _current.SubType = GetInt("SubType");
        _current.Elite = GetInt("Elite");
        _current.Type = GetInt("Type");
        _current.Ranking = GetInt("Ranking");
        _current.MinAttack = GetInt("MinAttack");
        _current.MaxAttack = GetInt("MaxAttack");
        _current.MinRange = GetInt("MinRange");
        _current.MaxRange = GetInt("MaxRange");
        _current.HP = GetInt("HP");
        _current.Defence = GetInt("Defence");
        _current.Mobility = GetInt("Mobility");
        _current.MeritExp = GetInt("MeritExp");
        _current.CostMoney = GetInt("CostMoney");
        _current.CostGear = GetInt("CostGear");
        _current.CostAtomic = GetInt("CostAtomic");
        _current.CostPoints = GetInt("CostPoints");
        _current.Carrier = GetInt("Carrier");
        _current.BuildTime = GetInt("BuildTime");
        _current.BuildCD = GetInt("BuildCD");
        _current.AOE1 = GetInt("AOE1");
        _current.AOE2 = GetInt("AOE2");
        _current.MaxElite = GetInt("MaxElite");
        _current.MaxFormation = GetInt("MaxFormation");
        _current.Name = _textBoxes["Name"].Text;
        _current.Anim = _textBoxes["Anim"].Text;
        _current.Feature = ParseIntList(_textBoxes["Feature"].Text);
        _current.FeatureLevel = ParseIntList(_textBoxes["FeatureLevel"].Text);
        _current.Country = ParseIntList(_textBoxes["Country"].Text);
        _current.FormationChance = ParseIntList(_textBoxes["FormationChance"].Text);
        _current.Formation = _checkBoxes["Formation"].IsChecked == true;

        _parser.SetUnitName(_current.Id, _iniNameBox.Text);
        var p = _parser.EnsurePosDefault(_current.Id);
        p.PosX = GetInt("_xml_x");
        p.PosY = GetInt("_xml_y");
        p.Scale = GetNum("_xml_scale");

        if (_listBox.SelectedItem is ArmyListEntry le && le.Id == _current.Id)
        {
            var idx = _listBox.SelectedIndex;
            _listBox.Items[idx] = new ArmyListEntry
            {
                Id = _current.Id,
                Name = string.IsNullOrEmpty(_iniNameBox.Text) ? _current.Name : _iniNameBox.Text,
                Army = _current.Army,
                Type = _current.Type
            };
            _listBox.SelectedIndex = idx;
        }
        SetStatus(Loc.F("Army.Updated", _current.Id));
        UpdateJsonPreview();
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
        try { _jsonPreview.Text = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }); }
        catch { _jsonPreview.Text = Loc.T("Common.SerializeFailed"); }
    }

    // ============================================================= 操作 =============================================================
    private void OnAdd(object sender, RoutedEventArgs e)
    {
        int newId = _parser.GetNextId();
        ArmySettingData unit;
        var src = _current;
        if (src != null)
        {
            unit = new ArmySettingData
            {
                Id = newId,
                Name = src.Name,
                Anim = src.Anim,
                Army = src.Army,
                SubType = src.SubType,
                Elite = src.Elite,
                Type = src.Type,
                Ranking = src.Ranking,
                Feature = new List<int>(src.Feature),
                FeatureLevel = new List<int>(src.FeatureLevel),
                MinAttack = src.MinAttack,
                MaxAttack = src.MaxAttack,
                MinRange = src.MinRange,
                MaxRange = src.MaxRange,
                HP = src.HP,
                Defence = src.Defence,
                Mobility = src.Mobility,
                CostMoney = src.CostMoney,
                CostGear = src.CostGear,
                CostAtomic = src.CostAtomic,
                CostPoints = src.CostPoints,
                MaxElite = src.MaxElite,
                MaxFormation = src.MaxFormation,
                Carrier = src.Carrier,
                BuildTime = src.BuildTime,
                BuildCD = src.BuildCD,
                AOE1 = src.AOE1,
                AOE2 = src.AOE2,
                Country = new List<int>(src.Country),
                FormationChance = new List<int>(src.FormationChance),
                MeritExp = src.MeritExp,
                Formation = src.Formation
            };
            var sp = _parser.GetPos(src.Id);
            if (sp != null)
            {
                var np = _parser.EnsurePosDefault(newId);
                np.PosX = sp.PosX; np.PosY = sp.PosY; np.Scale = sp.Scale;
            }
        }
        else
        {
            unit = new ArmySettingData { Id = newId, Name = $"NewUnit{newId}", Anim = $"NewUnit{newId}", HP = 100, Type = 1 };
        }
        _parser.Items.Add(unit);
        _parser.EnsurePosDefault(newId);
        _parser.SetUnitName(newId, $"新建兵种_{newId}");   // 写入 stringtable 的游戏文本，不属于界面语言
        RefreshList();
        foreach (ArmyListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("Army.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("Army.DeleteConfirm", _current.Id, _parser.GetUnitName(_current.Id)),
            Loc.T("Army.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveUnit(id);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("Army.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus("✔ " + Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("Army.SaveSuccessDetail", _parser.ConfigPath, _parser.XmlPosPath, _parser.StringTablePath),
                Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "ArmySettings.json / def_armypos.xml"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var u in _parser.Items)
        {
            string pfx = $"[Id={u.Id}]";
            if (!seenIds.Add(u.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (u.HP <= 0) problems.Add($"{pfx} {Loc.F("Army.Validate.HpNotPositive", u.HP)}");
            if (u.MinAttack > u.MaxAttack) problems.Add($"{pfx} {Loc.F("Army.Validate.AttackOrder", u.MinAttack, u.MaxAttack)}");
            if (u.MinRange > u.MaxRange) problems.Add($"{pfx} {Loc.F("Army.Validate.RangeOrder", u.MinRange, u.MaxRange)}");
            if (u.CostMoney < 0 || u.CostGear < 0 || u.CostAtomic < 0) problems.Add($"{pfx} {Loc.T("Army.Validate.NegativeCost")}");
            if (string.IsNullOrEmpty(_parser.GetUnitName(u.Id))) problems.Add($"{pfx} {Loc.F("Army.Validate.NameMissing", u.Id)}");
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

    // ============================================================= 编队/等级数值模拟 =============================================================
    // 与 Python 工具 simulate_stats 完全一致的公式
    private static readonly (int addHp, int addAtk, int addDef)[] LevelData =
    {
        (0, 0, 0),      // 占位（等级从 1 开始）
        (0, 0, 0),      // 1级
        (30, 6, 2),     // 2级
        (45, 8, 4),     // 3级
        (65, 10, 6),    // 4级
        (80, 14, 8),    // 5级
        (120, 30, 14)   // 6级
    };

    private static readonly (double atkPct, double hpPct)[] FormationData =
    {
        (1, 1),         // 占位（编队从 1 开始）
        (1, 1),         // 单编队
        (1.25, 1.6),    // 两编队
        (1.5, 2.1),     // 三编队
        (1.75, 2.5)     // 四编队
    };

    private async void OnSimulate(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) ?? _window;
        using var dlg = new UnitStatsSimulatorDialog(owner) { HasCurrentUnit = _current != null };
        var result = await dlg.ShowAsync();
        if (result == null) return;

        int level = Math.Clamp(result.Level, 1, LevelData.Length - 1);
        int formation = Math.Clamp(result.Formation, 1, FormationData.Length - 1);

        List<ArmySettingData> targets;
        if (result.ApplyToAll)
        {
            targets = _parser.Items.ToList();
        }
        else if (_current != null)
        {
            targets = new List<ArmySettingData> { _current };
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (addHp, addAtk, addDef) = LevelData[level];
        var (atkPct, hpPct) = FormationData[formation];
        foreach (var u in targets)
        {
            u.HP = (int)(u.HP * hpPct + 0.5) + addHp;
            u.MinAttack = (int)(u.MinAttack * atkPct + 0.5) + addAtk;
            u.MaxAttack = (int)(u.MaxAttack * atkPct + 0.5) + addAtk;
            u.Defence += addDef;
        }

        if (_current != null && targets.Contains(_current)) LoadUiFromCurrent();
        SetStatus(Loc.F("Army.Simulated", level, formation, targets.Count));
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[ArmyEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class ArmyListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Army { get; set; }
        public int Type { get; set; }
        public override string ToString() => Loc.F("Army.ListEntry", Id, Name, Army, Type);
    }
}
