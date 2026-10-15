using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

/// <summary>
/// 兵种编辑场景（对应 Python 工具的 Unit 编辑器，三文件联动：
/// ArmySettings.json + def_armypos.xml + stringtable 的 unit_name_{Id}）。
/// </summary>
public class ArmyEditScene : UserControl
{
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
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
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
        topPanel.Children.Add(MakeTopBtn("编队/等级数值模拟", OnSimulate));
        topPanel.Children.Add(new TextBlock
        {
            Text = "兵种编辑",
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
            Text = $"已加载 {_parser.Items.Count} 条兵种",
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

        AddSection("INI 文本 (stringtable)");
        AddRow("兵种名称 (unit_name_{Id})", AddIniName());

        AddSection("XML 坐标 (def_armypos.xml)");
        AddNum("_xml_x", "X 偏移", -9999, 9999);
        AddNum("_xml_y", "Y 偏移", -9999, 9999);
        AddNum("_xml_scale", "缩放 Scale", 0.01, 10, 0.05);

        AddSection("基本信息");
        AddNum("Id", "兵种ID(Id)", 0, int.MaxValue);
        AddText("Name", "内部名称(Name)");
        AddText("Anim", "动画(Anim)");
        AddNum("Army", "兵种ID(Army)", 0, int.MaxValue);
        AddNum("SubType", "子类型(SubType)", 0, int.MaxValue);
        AddNum("Elite", "精英(Elite)", 0, int.MaxValue);
        AddNum("Type", "类型(Type)", 0, int.MaxValue);
        AddNum("Ranking", "档次(Ranking)", 0, int.MaxValue);

        AddSection("战斗数值");
        AddNum("MinAttack", "最小攻击(MinAttack)", 0, int.MaxValue);
        AddNum("MaxAttack", "最大攻击(MaxAttack)", 0, int.MaxValue);
        AddNum("MinRange", "最小射程(MinRange)", 0, int.MaxValue);
        AddNum("MaxRange", "最大射程(MaxRange)", 0, int.MaxValue);
        AddNum("HP", "生命(HP)", 0, int.MaxValue);
        AddNum("Defence", "防御(Defence)", 0, int.MaxValue);
        AddNum("Mobility", "机动(Mobility)", 0, int.MaxValue);
        AddNum("MeritExp", "功勋经验(MeritExp)", 0, int.MaxValue);

        AddSection("成本");
        AddNum("CostMoney", "消耗金钱(CostMoney)", 0, int.MaxValue);
        AddNum("CostGear", "消耗零件(CostGear)", 0, int.MaxValue);
        AddNum("CostAtomic", "消耗核能(CostAtomic)", 0, int.MaxValue);
        AddNum("CostPoints", "消耗点数(CostPoints)", 0, int.MaxValue);

        AddSection("编队 / 其它");
        AddNum("Carrier", "载具(Carrier)", 0, int.MaxValue);
        AddNum("BuildTime", "建造时间(BuildTime)", 0, int.MaxValue);
        AddNum("BuildCD", "建造CD(BuildCD)", 0, int.MaxValue);
        AddNum("AOE1", "溅射1(AOE1)", 0, int.MaxValue);
        AddNum("AOE2", "溅射2(AOE2)", 0, int.MaxValue);
        AddNum("MaxElite", "最大精英(MaxElite)", 0, int.MaxValue);
        AddNum("MaxFormation", "最大编队(MaxFormation)", 0, int.MaxValue);
        AddCheck("Formation", "可编队(Formation)");

        AddSection("列表字段 (逗号分隔)");
        AddList("Feature", "特性(Feature)");
        AddList("FeatureLevel", "特性等级(FeatureLevel)");
        AddList("Country", "归属国家(Country)");
        AddList("FormationChance", "编队概率(FormationChance)");

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
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _numBoxes[key] = ctrl;
        AddRow(label, ctrl);
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = MakeTextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddList(string key, string label)
    {
        var tb = MakeTextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddIniName()
    {
        var tb = MakeTextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _iniNameBox = tb;
        return tb;
    }

    private static TextBox MakeTextBox() => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        BorderThickness = new Thickness(1),
        FontSize = 13,
        Padding = new Thickness(6, 3, 6, 3),
        CaretBrush = Brushes.White
    };

    private void AddCheck(string key, string label)
    {
        var cb = new CheckBox
        {
            Content = label,
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        cb.Unchecked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _checkBoxes[key] = cb;
        AddRow(label, cb);
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
            if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(u.Name) ? $"兵种_{u.Id}" : u.Name;
            var entry = new ArmyListEntry { Id = u.Id, Name = name, Army = u.Army, Type = u.Type };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && u.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus($"已加载 {_parser.Items.Count} 条兵种，列表 {_listBox.Items.Count} 条");
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
        SetStatus($"已更新兵种 {_current.Id}（未保存）");
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
        catch { _jsonPreview.Text = "(序列化失败)"; }
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
        _parser.SetUnitName(newId, $"新建兵种_{newId}");
        RefreshList();
        foreach (ArmyListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增兵种 ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个兵种", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除兵种 [{_current.Id}] {_parser.GetUnitName(_current.Id)}？\n同时删除 def_armypos.xml 中的对应坐标项。",
            "删除兵种", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveUnit(id);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除兵种 ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功：\nArmySettings.json: {_parser.ConfigPath}\ndef_armypos.xml: {_parser.XmlPosPath}\nstringtable: {_parser.StringTablePath}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 ArmySettings.json 与 def_armypos.xml？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var u in _parser.Items)
        {
            string pfx = $"[Id={u.Id}]";
            if (!seenIds.Add(u.Id)) problems.Add($"{pfx} ID 重复");
            if (u.HP <= 0) problems.Add($"{pfx} HP={u.HP} 应大于 0");
            if (u.MinAttack > u.MaxAttack) problems.Add($"{pfx} MinAttack({u.MinAttack}) > MaxAttack({u.MaxAttack})");
            if (u.MinRange > u.MaxRange) problems.Add($"{pfx} MinRange({u.MinRange}) > MaxRange({u.MaxRange})");
            if (u.CostMoney < 0 || u.CostGear < 0 || u.CostAtomic < 0) problems.Add($"{pfx} 消耗出现负值");
            if (string.IsNullOrEmpty(_parser.GetUnitName(u.Id))) problems.Add($"{pfx} 缺少兵种名(unit_name_{u.Id})");
        }

        if (problems.Count == 0)
            MessageBox.Show($"校验通过，共 {_parser.Items.Count} 条，未发现明显问题。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
        else
        {
            var msg = string.Join("\n", problems.Take(80));
            if (problems.Count > 80) msg += $"\n… 共 {problems.Count} 条，仅显示前 80 条";
            MessageBox.Show(msg, $"校验发现问题 {problems.Count} 条", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SetStatus($"校验完成：{problems.Count} 个问题");
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
            MessageBox.Show("请先在左侧选中一个兵种", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
        SetStatus($"已按 {level}级 / {formation}编队 重算 {targets.Count} 个兵种（未保存）");
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
        public override string ToString() => $"[{Id}] {Name}  (Army:{Army} Type:{Type})";
    }
}
