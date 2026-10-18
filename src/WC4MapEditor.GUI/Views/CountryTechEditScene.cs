using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 国家科技树编辑场景（对应 Python 工具的 CountryTech 编辑器）。
/// 列表 + 属性面板完整 CRUD；名称写入 stringtable 的 skill_name_{Id}。
/// </summary>
public class CountryTechEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly CountryTechSettingParser _parser = CountryTechSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private ComboBox _categoryCombo = null!;
    private TextBlock _statusText = null!;

    private CountryTechData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _jsonPreview = null!;

    // ---- 右侧面板：基本属性 / INI 文本 页签 ----
    private Button _propTabBtn = null!, _iniTabBtn = null!;
    private bool _showIniPanel;
    private ScrollViewer _iniScroll = null!;
    private TextBox _iniNameBox = null!, _iniDescBox = null!;
    private TextBlock _iniTypeText = null!;

    // ---- 科技树画布 ----
    private Canvas _canvas = null!;
    private double _scale = 1, _offsetX = 720, _offsetY = 90;
    private const double MinScale = 0.35, MaxScale = 2.5, NodeRadius = 35;

    // ---- 科技图标图集（image_countrytech_hd，按 Type 索引）----
    private readonly Dictionary<int, ImageSource> _techIcons = new();
    private bool _iconsLoaded;
    private bool _showNeedGuides = true;
    private bool _autoUpdateLines = true;
    private bool _panning, _draggingNode;
    private Point _panStart;
    private double _panStartOffX, _panStartOffY;
    private Point _dragStart;
    private CountryTechData? _dragNode;
    private int _ghostLevel, _ghostPosition;
    private readonly HashSet<int> _selectedIds = new();

    // 连线生成常量（与 Python 工具一致）
    private const double NodeStepX = 110, NodeStepY = 92, LineScaleX = 1.1, LineScaleY = 0.92;

    public CountryTechEditScene(MainWindow window)
    {
        EditorTheme.Apply(this);
        _window = window;
        BuildUI();
        LoadTechIcons();
        RefreshCategories();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_parser.Items.Count > 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
            FitView();
        }, DispatcherPriority.Loaded);
    }

    // ============================================================= 布局 =============================================================
    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        // 顶栏：返回 + 标题 | 操作按钮。两个勾选项直接改字段再重绘（旧实现只重绘、没写回字段）。
        var topBar = EditorTheme.TopBar(
            Loc.T("CountryTech.Title"),
            (_, _) => _window.ReturnToMainScene(),
            EditorTheme.Button(Loc.T("Common.Add"), OnAdd),
            EditorTheme.DangerButton(Loc.T("Common.Delete"), OnDelete),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.VSeparator(),
            EditorTheme.Button(Loc.T("CountryTech.GenerateLines"), OnGenerateLines),
            EditorTheme.Button(Loc.T("CountryTech.FitView"), OnFit),
            EditorTheme.CheckBox(Loc.T("CountryTech.ShowNeedGuides"), _showNeedGuides, v => { _showNeedGuides = v; RenderCanvas(); }),
            EditorTheme.CheckBox(Loc.T("CountryTech.AutoLines"), _autoUpdateLines, v => _autoUpdateLines = v),
            EditorTheme.VSeparator(),
            EditorTheme.Button(Loc.T("Common.Validate"), OnValidate));
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 5);
        _root.Children.Add(topBar);

        // 左栏：分类筛选 + 搜索 + 列表
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _categoryCombo = EditorTheme.ComboBox();
        _categoryCombo.Margin = new Thickness(8, 8, 8, 0);
        _categoryCombo.SelectionChanged += (_, _) => { RefreshList(); FitView(); };
        Grid.SetRow(_categoryCombo, 0);
        leftGrid.Children.Add(_categoryCombo);

        var search = EditorTheme.SearchBox(Loc.T("CountryTech.SearchPlaceholder"), out _searchBox);
        search.Margin = new Thickness(8);
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(search, 1);
        leftGrid.Children.Add(search);

        _listBox = new ListBox();
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 2);
        leftGrid.Children.Add(_listBox);

        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        left.Child = leftGrid;
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        _root.Children.Add(left);

        var leftSplitter = EditorTheme.VSplitter();
        Grid.SetRow(leftSplitter, 1); Grid.SetColumn(leftSplitter, 1);
        _root.Children.Add(leftSplitter);

        // 中间：科技树画布
        var canvasBorder = new Border
        {
            Background = EditorTheme.Bg0,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            ClipToBounds = true
        };
        Grid.SetRow(canvasBorder, 1); Grid.SetColumn(canvasBorder, 2);
        _canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseUp;
        _canvas.MouseRightButtonDown += Canvas_MouseRightButtonDown;
        _canvas.MouseWheel += Canvas_MouseWheel;
        _canvas.SizeChanged += (_, _) => RenderCanvas();
        canvasBorder.Child = _canvas;
        _root.Children.Add(canvasBorder);

        var canvasSplitter = EditorTheme.VSplitter();
        Grid.SetRow(canvasSplitter, 1); Grid.SetColumn(canvasSplitter, 3);
        _root.Children.Add(canvasSplitter);

        // 右栏：页签条（基本属性 / INI 文本）+ 两个可切换的滚动面板
        BuildRightPanel();
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        _propTabBtn = EditorTheme.TabButton(Loc.T("CountryTech.Tab.Props"), true, (_, _) => ShowIniPanel(false));
        _iniTabBtn = EditorTheme.TabButton(Loc.T("CountryTech.Tab.Ini"), false, (_, _) => ShowIniPanel(true));
        tabs.Children.Add(_propTabBtn);
        tabs.Children.Add(_iniTabBtn);
        var tabBar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = tabs
        };
        Grid.SetRow(tabBar, 0);
        rightGrid.Children.Add(tabBar);

        Grid.SetRow(_propScroll, 1);
        Grid.SetRow(_iniScroll, 1);
        rightGrid.Children.Add(_propScroll);
        rightGrid.Children.Add(_iniScroll);

        var right = EditorTheme.Panel();
        right.Child = rightGrid;
        Grid.SetRow(right, 1); Grid.SetColumn(right, 4);
        _root.Children.Add(right);

        var status = EditorTheme.StatusBar(out _statusText, Loc.F("Common.ItemsLoaded", _parser.Items.Count));
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 5);
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

        AddSection(Loc.T("Common.BasicInfo"));
        AddNum("Id", Loc.T("CountryTech.Field.Id"), 0, int.MaxValue);
        AddText("NameIni", Loc.T("CountryTech.Field.NameIni"));
        AddNum("CategoryId", Loc.T("CountryTech.Field.CategoryId"), 0, int.MaxValue);
        AddNum("Categorys", Loc.T("CountryTech.Field.Categorys"), 0, 4);
        AddNum("Type", Loc.T("CountryTech.Field.Type"), 0, int.MaxValue);
        AddNum("ResearchLv", Loc.T("CountryTech.Field.ResearchLv"), 0, int.MaxValue);
        AddNum("Position", Loc.T("CountryTech.Field.Position"), 0, int.MaxValue);

        AddSection(Loc.T("CountryTech.Section.Cost"));
        AddNum("CostMoney", Loc.T("CountryTech.Field.CostMoney"), 0, int.MaxValue);
        AddNum("CostGear", Loc.T("CountryTech.Field.CostGear"), 0, int.MaxValue);
        AddNum("CostAtomic", Loc.T("CountryTech.Field.CostAtomic"), 0, int.MaxValue);
        AddNum("CostMerit", Loc.T("CountryTech.Field.CostMerit"), 0, int.MaxValue);

        AddSection(Loc.T("CountryTech.Section.Other"));
        AddNum("Chance", Loc.T("CountryTech.Field.Chance"), 0, 100);
        AddNum("Value", Loc.T("CountryTech.Field.Value"), int.MinValue, int.MaxValue);

        AddSection(Loc.T("CountryTech.Section.Links"));
        AddText("NeedId", Loc.T("CountryTech.Field.NeedId"));
        AddText("Lines", Loc.T("CountryTech.Field.Lines"));
        AddText("Lines2", Loc.T("CountryTech.Field.Lines2"));

        AddSection(Loc.T("Common.JsonPreview"));
        _jsonPreview = EditorTheme.MonoTextBox(220);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);

        // INI 文本编辑面板（按 Type 显示/修改 countrytech_{type} / countrytech_desc_{type}）
        var iniPanel = new StackPanel();
        iniPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("CountryTech.IniSection")));
        _iniTypeText = EditorTheme.Muted("");
        _iniTypeText.Margin = new Thickness(10, 8, 10, 4);
        iniPanel.Children.Add(_iniTypeText);

        _iniNameBox = MakeIniTextBox(false);
        _iniNameBox.LostFocus += (_, _) => { if (_current != null) _parser.SetTechNameByType(_current.Type, _iniNameBox.Text); };
        iniPanel.Children.Add(EditorTheme.FormRow(Loc.T("CountryTech.IniName"), _iniNameBox, 120));

        _iniDescBox = MakeIniTextBox(true);
        _iniDescBox.LostFocus += (_, _) => { if (_current != null) _parser.SetTechDescByType(_current.Type, _iniDescBox.Text); };
        iniPanel.Children.Add(EditorTheme.FormRow(Loc.T("CountryTech.IniDesc"), _iniDescBox, 120));

        _iniScroll = new ScrollViewer
        {
            Content = iniPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed
        };
    }

    private static TextBox MakeIniTextBox(bool multiline)
    {
        var tb = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = multiline };
        if (multiline)
        {
            tb.Height = 90;
            tb.VerticalContentAlignment = VerticalAlignment.Top;
            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        return tb;
    }

    private void ShowIniPanel(bool ini)
    {
        _showIniPanel = ini;
        _propScroll.Visibility = ini ? Visibility.Collapsed : Visibility.Visible;
        _iniScroll.Visibility = ini ? Visibility.Visible : Visibility.Collapsed;
        EditorTheme.SetTabActive(_propTabBtn, !ini);
        EditorTheme.SetTabActive(_iniTabBtn, ini);
        if (ini) LoadIniPanel();
    }

    private void LoadIniPanel()
    {
        if (_current == null) { _iniTypeText.Text = ""; _iniNameBox.Text = ""; _iniDescBox.Text = ""; return; }
        _iniTypeText.Text = Loc.F("CountryTech.IniTypeLabel", _current.Type);
        _iniNameBox.Text = _parser.GetTechNameByType(_current.Type);
        _iniDescBox.Text = _parser.GetTechDescByType(_current.Type);
    }

    // ============================================================= 控件辅助 =============================================================
    private void OnFit(object sender, RoutedEventArgs e) => FitView();

    private void AddSection(string title) => _propPanel.Children.Add(EditorTheme.SectionHeader(title));

    // 这两个薄封装保留：负责把控件按 key 登记进字典，外观统一交给 EditorTheme.FormRow
    private NumericUpDown AddNum(string key, string label, double min = int.MinValue, double max = int.MaxValue, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
        _numBoxes[key] = ctrl;
        _propPanel.Children.Add(EditorTheme.FormRow(label, ctrl, 160));
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loadingUi || _current == null) return;
            ApplyPropCurrent();
            if (key == "NameIni") _parser.SetTechName(_current.Id, tb.Text);
        };
        _textBoxes[key] = tb;
        _propPanel.Children.Add(EditorTheme.FormRow(label, tb, 160));
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshCategories()
    {
        var sel = _categoryCombo.SelectedValue;
        _categoryCombo.Items.Clear();
        _categoryCombo.Items.Add(new CategoryItem { Id = -1, Name = Loc.T("CountryTech.AllCategories") });
        foreach (var cat in _parser.Items.Select(t => t.CategoryId).Distinct().OrderBy(x => x))
            _categoryCombo.Items.Add(new CategoryItem { Id = cat, Name = Loc.F("CountryTech.CategoryN", cat) });
        _categoryCombo.DisplayMemberPath = "Name";
        _categoryCombo.SelectedValuePath = "Id";
        if (sel == null && _parser.Items.Count > 0)
        {
            var firstCat = _parser.Items.Select(t => t.CategoryId).Distinct().OrderBy(x => x).First();
            _categoryCombo.SelectedValue = firstCat;
        }
        else
        {
            _categoryCombo.SelectedValue = sel ?? -1;
        }
        if (_categoryCombo.SelectedIndex < 0) _categoryCombo.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        int catFilter = _categoryCombo.SelectedValue is int v ? v : -1;
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(t =>
        {
            if (catFilter >= 0 && t.CategoryId != catFilter) return false;
            if (!searching) return true;
            if (t.Id.ToString().Contains(q)) return true;
            if (_parser.GetTechName(t.Id).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Type.ToString().Contains(q)) return true;
            if (t.CategoryId.ToString().Contains(q)) return true;
            return false;
        }).OrderBy(t => t.CategoryId).ThenBy(t => t.Id);

        _listBox.Items.Clear();
        foreach (var t in items)
        {
            var name = _parser.GetTechName(t.Id);
            if (string.IsNullOrEmpty(name)) name = Loc.F("CountryTech.UnnamedTech", t.Id);
            _listBox.Items.Add(new CountryTechListEntry
            {
                Id = t.Id,
                Name = name,
                CategoryId = t.CategoryId,
                ResearchLv = t.ResearchLv
            });
        }
        SetStatus(Loc.F("Common.ItemsLoadedFiltered", _parser.Items.Count, _listBox.Items.Count));
        RenderCanvas();
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not CountryTechListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        _selectedIds.Clear();
        _selectedIds.Add(entry.Id);
        LoadUiFromCurrent();
        RenderCanvas();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = 0;
        _jsonPreview.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 属性读写 =============================================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        SetNum("Id", _current.Id);
        SetNum("CategoryId", _current.CategoryId);
        SetNum("Categorys", _current.Categorys);
        SetNum("Type", _current.Type);
        SetNum("ResearchLv", _current.ResearchLv);
        SetNum("Position", _current.Position);
        SetNum("CostMoney", _current.CostMoney);
        SetNum("CostGear", _current.CostGear);
        SetNum("CostAtomic", _current.CostAtomic);
        SetNum("CostMerit", _current.CostMerit);
        SetNum("Chance", _current.Chance);
        SetNum("Value", _current.Value);
        _textBoxes["NameIni"].Text = _parser.GetTechName(_current.Id);
        _textBoxes["NeedId"].Text = FormatList(_current.NeedId);
        _textBoxes["Lines"].Text = FormatList(_current.Lines);
        _textBoxes["Lines2"].Text = FormatList(_current.Lines2);
        _loadingUi = false;
        UpdateJsonPreview();
        if (_showIniPanel) LoadIniPanel();
    }

    private void ApplyPropCurrent()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.CategoryId = GetInt("CategoryId");
        _current.Categorys = GetInt("Categorys");
        _current.Type = GetInt("Type");
        _current.ResearchLv = GetInt("ResearchLv");
        _current.Position = GetInt("Position");
        _current.CostMoney = GetInt("CostMoney");
        _current.CostGear = GetInt("CostGear");
        _current.CostAtomic = GetInt("CostAtomic");
        _current.CostMerit = GetInt("CostMerit");
        _current.Chance = GetInt("Chance");
        _current.Value = GetInt("Value");
        _current.NeedId = ParseIntList(_textBoxes["NeedId"].Text);
        _current.Lines = ParseDoubleList(_textBoxes["Lines"].Text);
        _current.Lines2 = ParseDoubleList(_textBoxes["Lines2"].Text);
        RenderCanvas();
        UpdateJsonPreview();
    }

    private void SetNum(string key, int v) { if (_numBoxes.TryGetValue(key, out var b)) b.Value = v; }
    private int GetInt(string key) => _numBoxes.TryGetValue(key, out var b) ? (int)Math.Round(b.Value) : 0;

    private static string FormatList<T>(List<T> list) => list == null ? "" : string.Join(", ", list);

    private static List<int> ParseIntList(string s)
    {
        var r = new List<int>();
        if (string.IsNullOrWhiteSpace(s)) return r;
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) r.Add(v);
        return r;
    }

    private static List<double> ParseDoubleList(string s)
    {
        var r = new List<double>();
        if (string.IsNullOrWhiteSpace(s)) return r;
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) r.Add(v);
        return r;
    }

    private void UpdateJsonPreview()
    {
        if (_current == null) { _jsonPreview.Text = ""; return; }
        try { _jsonPreview.Text = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }); }
        catch { _jsonPreview.Text = Loc.T("Common.SerializeFailed"); }
    }

    // ============================================================= 操作 =============================================================
    private void OnAdd(object sender, RoutedEventArgs e) => AddAt(1, 0);

    private void AddAt(int level, int position)
    {
        int catId = _categoryCombo.SelectedValue is int c && c >= 0 ? c : 1;
        int newId = _parser.GetNextIdInCategory(catId);
        var tech = new CountryTechData
        {
            Id = newId,
            CategoryId = catId,
            Categorys = 0,
            Type = 101,
            ResearchLv = level,
            Position = position,
            CostMoney = 100,
            CostGear = 50,
            CostAtomic = 25,
            CostMerit = 1,
            Chance = 100,
            Value = 0
        };
        _parser.AddTech(tech);
        // 默认名会写进 stringtable（游戏数据），不随界面语言变化
        _parser.SetTechName(newId, $"新科技{newId}");
        RefreshCategories();
        RefreshList();
        foreach (CountryTechListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("CountryTech.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("CountryTech.DeleteConfirm", _current.Id, _parser.GetTechName(_current.Id)), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveTech(id);
        _current = null;
        RefreshCategories();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("CountryTech.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus(Loc.F("CountryTech.SavedStatus", _parser.ConfigPath));
            MessageBox.Show(Loc.F("CountryTech.SavedMessage", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "CountryTechSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshCategories();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void OnGenerateLines(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_current.NeedId.Count == 0) { MessageBox.Show(Loc.T("CountryTech.NoNeedId"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }

        int applied = 0;
        if (_current.NeedId.Count >= 1)
        {
            var src = _parser.GetById(_current.NeedId[0]);
            if (src != null) { _current.Lines = GenerateOrthogonalPath(src, _current); applied++; }
            else SetStatus(Loc.F("CountryTech.NeedIdNotFound", _current.NeedId[0]));
        }
        if (_current.NeedId.Count >= 2)
        {
            var src = _parser.GetById(_current.NeedId[1]);
            if (src != null) { _current.Lines2 = GenerateOrthogonalPath(src, _current); applied++; }
        }
        _loadingUi = true;
        _textBoxes["Lines"].Text = FormatList(_current.Lines);
        _textBoxes["Lines2"].Text = FormatList(_current.Lines2);
        _loadingUi = false;
        UpdateJsonPreview();
        SetStatus(Loc.F("CountryTech.LinesGenerated", applied));
    }

    private static List<double> GenerateOrthogonalPath(CountryTechData source, CountryTechData target)
    {
        // Lines 存的是相对 target 自身的偏移（DrawStoredLine 从节点自身出发绘制），
        // 因此必须反向计算：从 target 出发指向 source，终点才能落在 source 上。
        double sx = source.Position * NodeStepX;
        double sy = (source.ResearchLv - 1) * NodeStepY;
        double tx = target.Position * NodeStepX;
        double ty = (target.ResearchLv - 1) * NodeStepY;
        int dx = (int)Math.Round((sx - tx) / LineScaleX);
        int dy = (int)Math.Round((sy - ty) / LineScaleY);
        if (dx == 0) return dy == 0 ? new List<double>() : new List<double> { 0, dy };
        if (dy == 0) return new List<double> { dx, 0 };
        int first = dy / 2;
        return new List<double> { 0, first, dx, first, dx, dy };
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        var typeByCat = new Dictionary<int, HashSet<int>>();

        foreach (var t in _parser.Items)
        {
            string pfx = $"[Id={t.Id}] ";
            if (seenIds.Contains(t.Id)) problems.Add(pfx + Loc.T("Common.DuplicateId"));
            else seenIds.Add(t.Id);

            if (t.Categorys < 0 || t.Categorys > 4) problems.Add(pfx + Loc.F("CountryTech.Validate.CategorysRange", t.Categorys));

            if (!typeByCat.TryGetValue(t.CategoryId, out var set))
            {
                set = new HashSet<int>();
                typeByCat[t.CategoryId] = set;
            }
            if (set.Contains(t.Type)) problems.Add(pfx + Loc.F("CountryTech.Validate.DuplicateType", t.CategoryId, t.Type));
            else set.Add(t.Type);

            if (t.NeedId.Count > 2) problems.Add(pfx + Loc.F("CountryTech.Validate.TooManyNeedId", t.NeedId.Count));

            if (t.Lines.Count % 2 != 0) problems.Add(pfx + Loc.F("CountryTech.Validate.OddCoords", "Lines", t.Lines.Count));
            if (t.Lines2.Count % 2 != 0) problems.Add(pfx + Loc.F("CountryTech.Validate.OddCoords", "Lines2", t.Lines2.Count));

            foreach (var nid in t.NeedId)
                if (_parser.GetById(nid) == null) problems.Add(pfx + Loc.F("CountryTech.Validate.NeedIdMissing", nid));

            if (t.CostMoney < 0 || t.CostGear < 0 || t.CostAtomic < 0) problems.Add(pfx + Loc.T("CountryTech.Validate.NegativeCost"));
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
        Debug.WriteLine($"[CountryTechEditScene] {s}");
    }

    // ============================================================= 科技树画布 =============================================================
    private (double wx, double wy) Grid2World(double level, double position) => (position * NodeStepX, (level - 1) * NodeStepY);
    private Point W2S(double wx, double wy) => new Point(wx * _scale + _offsetX, wy * _scale + _offsetY);
    private (double wx, double wy) S2W(double x, double y) => ((x - _offsetX) / _scale, (y - _offsetY) / _scale);

    private (int level, int position) EventGrid(Point screen)
    {
        var (wx, wy) = S2W(screen.X, screen.Y);
        int level = Math.Max(1, (int)Math.Round(wy / NodeStepY) + 1);
        int position = (int)Math.Round(wx / NodeStepX);
        return (level, position);
    }

    private void FitView()
    {
        if (_canvas == null) return;
        int cat = _categoryCombo.SelectedValue is int c ? c : -1;
        var nodes = (cat < 0 ? _parser.Items : _parser.Items.Where(t => t.CategoryId == cat)).ToList();
        int pmin = 0, pmax = 0, lmin = 1, lmax = 1;
        if (nodes.Count == 0) { pmin = 0; pmax = 4; lmin = 1; lmax = 4; }
        else
        {
            foreach (var n in nodes)
            {
                pmin = Math.Min(pmin, n.Position); pmax = Math.Max(pmax, n.Position);
                lmin = Math.Min(lmin, n.ResearchLv); lmax = Math.Max(lmax, n.ResearchLv);
            }
            pmin -= 1; pmax += 1; lmin = Math.Max(1, lmin - 1); lmax += 1;
        }
        var g1 = Grid2World(lmin, pmin);
        var g2 = Grid2World(lmax, pmax);
        double width = Math.Max(g2.wx - g1.wx, 1), height = Math.Max(g2.wy - g1.wy, 1);
        double cw = Math.Max(_canvas.ActualWidth, 400), ch = Math.Max(_canvas.ActualHeight, 300);
        _scale = Math.Max(MinScale, Math.Min(1.35, Math.Min((cw - 180) / (width + 160), (ch - 130) / (height + 160))));
        _offsetX = cw / 2 - (g1.wx + g2.wx) * _scale / 2;
        _offsetY = 55 - g1.wy * _scale;
        RenderCanvas();
    }

    private void AddText(double wx, double wy, string text, Brush fg, double size)
    {
        var p = W2S(wx, wy);
        var tb = new TextBlock { Text = text, Foreground = fg, FontSize = size };
        Canvas.SetLeft(tb, p.X); Canvas.SetTop(tb, p.Y);
        _canvas.Children.Add(tb);
    }

    private void AddScreenText(double x, double y, string text, Brush fg, double size, int id, double width = 0)
    {
        var tb = new TextBlock { Text = text, Foreground = fg, FontSize = size, Tag = id, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        if (width > 0) { tb.Width = width; tb.TextWrapping = TextWrapping.Wrap; Canvas.SetLeft(tb, x - width / 2); }
        else { tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2); }
        Canvas.SetTop(tb, y);
        _canvas.Children.Add(tb);
    }

    private void AddScreenTextPair(double x, double y, string left, Brush leftFg, double leftSize, string right, Brush rightFg, double rightSize, int id)
    {
        var tb = new TextBlock { Tag = id, TextWrapping = TextWrapping.NoWrap };
        tb.Inlines.Add(new Run { Text = left, Foreground = leftFg, FontSize = leftSize });
        tb.Inlines.Add(new Run { Text = right, Foreground = rightFg, FontSize = rightSize });
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
        Canvas.SetTop(tb, y);
        _canvas.Children.Add(tb);
    }

    private void RenderCanvas()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();

        int cat = _categoryCombo.SelectedValue is int c ? c : -1;
        var nodes = (cat < 0 ? _parser.Items : _parser.Items.Where(t => t.CategoryId == cat)).ToList();

        int pmin = 0, pmax = 0, lmin = 1, lmax = 1;
        if (nodes.Count == 0) { pmin = 0; pmax = 4; lmin = 1; lmax = 4; }
        else
        {
            foreach (var n in nodes)
            {
                pmin = Math.Min(pmin, n.Position); pmax = Math.Max(pmax, n.Position);
                lmin = Math.Min(lmin, n.ResearchLv); lmax = Math.Max(lmax, n.ResearchLv);
            }
            pmin -= 1; pmax += 1; lmin = Math.Max(1, lmin - 1); lmax += 1;
        }

        // 网格坐标标签 L(等级) / P(位置) 是坐标记号，不做翻译
        for (int level = lmin; level <= lmax; level++)
        {
            var g = Grid2World(level, pmin - 0.45);
            AddText(g.wx, g.wy, "L" + level, EditorTheme.TextDim, 12);
        }
        for (int pos = pmin; pos <= pmax; pos++)
        {
            var g = Grid2World(lmin - 0.45, pos);
            AddText(g.wx, g.wy, "P" + pos, EditorTheme.TextDim, 12);
        }

        var occupied = new HashSet<(int, int)>();
        foreach (var n in nodes) occupied.Add((n.ResearchLv, n.Position));
        for (int level = lmin; level <= lmax; level++)
            for (int pos = pmin; pos <= pmax; pos++)
            {
                if (occupied.Contains((level, pos))) continue;
                var gp = Grid2World(level, pos);
                var p = W2S(gp.wx, gp.wy);
                var r = Math.Max(3, 5 * _scale);
                var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = EditorTheme.BorderStrong };
                Canvas.SetLeft(dot, p.X - r); Canvas.SetTop(dot, p.Y - r);
                _canvas.Children.Add(dot);
            }

        foreach (var node in nodes)
        {
            DrawStoredLine(node, node.Lines);
            DrawStoredLine(node, node.Lines2);
        }

        if (_showNeedGuides)
        {
            foreach (var node in nodes)
            {
                foreach (var pid in node.NeedId)
                {
                    var parent = _parser.GetById(pid);
                    if (parent == null) continue;
                    if (cat >= 0 && parent.CategoryId != cat) continue;
                    var ag = Grid2World(node.ResearchLv, node.Position);
                    var a = W2S(ag.wx, ag.wy);
                    var bg = Grid2World(parent.ResearchLv, parent.Position);
                    var b = W2S(bg.wx, bg.wy);
                    _canvas.Children.Add(new Polyline
                    {
                        Points = new PointCollection { a, b },
                        Stroke = EditorTheme.Warning,
                        StrokeThickness = Math.Max(1, 2 * _scale),
                        StrokeDashArray = new DoubleCollection { 5, 4 }
                    });
                }
            }
        }

        foreach (var node in nodes)
            DrawNode(node, _selectedIds.Contains(node.Id));

        if (_draggingNode && _dragNode != null)
        {
            var gp = Grid2World(_ghostLevel, _ghostPosition);
            var p = W2S(gp.wx, gp.wy);
            var r = NodeRadius * _scale;
            var ghost = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = EditorTheme.Danger, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 } };
            Canvas.SetLeft(ghost, p.X - r); Canvas.SetTop(ghost, p.Y - r);
            _canvas.Children.Add(ghost);
        }
    }

    private void DrawStoredLine(CountryTechData node, List<double> path)
    {
        if (path == null || path.Count < 2 || path.Count % 2 != 0) return;
        var g = Grid2World(node.ResearchLv, node.Position);
        var prev = new Point(g.wx, g.wy);
        var pts = new PointCollection { W2S(prev.X, prev.Y) };
        for (int i = 0; i + 1 < path.Count; i += 2)
        {
            double px = g.wx + path[i] * LineScaleX;
            double py = g.wy + path[i + 1] * LineScaleY;
            if (prev.X != px && prev.Y != py) pts.Add(W2S(prev.X, py));
            pts.Add(W2S(px, py));
            prev = new Point(px, py);
        }
        _canvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = EditorTheme.Info,
            StrokeThickness = Math.Max(1, 4 * _scale)
        });
    }

    // 用 AM 获取 image_countrytech_hd 图集，借 tac 解析器(TacticalMapEditor)解析 xml，
    // 按 Type 裁出每个科技的子图缓存，供 DrawNode 显示。
    private void LoadTechIcons()
    {
        if (_iconsLoaded) return;
        _iconsLoaded = true;
        try
        {
            var am = AssetManager.Default;
            if (!am.IsLoaded) am.ScanDefault();
            var pngEntry = am.Find("image/image_countrytech_hd.png");
            var xmlEntry = am.Find("image/image_countrytech_hd.xml");
            if (pngEntry == null || xmlEntry == null)
            {
                Debug.WriteLine("[CountryTech] 图集资源 image_countrytech_hd 未找到");
                return;
            }

            var editor = new TacticalMapEditor();
            if (!editor.LoadFromFiles(pngEntry.FullPath, xmlEntry.FullPath) || editor.ImageData == null)
            {
                Debug.WriteLine("[CountryTech] 图集解析失败");
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(editor.ImageData);
            bmp.EndInit();
            bmp.Freeze();

            foreach (var obj in editor.Objects)
            {
                int type = ParseTechTypeFromName(obj.Name);
                if (type < 0) continue;
                int x = Math.Max(0, obj.X);
                int y = Math.Max(0, obj.Y);
                int w = Math.Min(obj.Width, bmp.PixelWidth - x);
                int h = Math.Min(obj.Height, bmp.PixelHeight - y);
                if (w <= 0 || h <= 0) continue;
                var cropped = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
                cropped.Freeze();
                _techIcons[type] = cropped;
            }
            Debug.WriteLine($"[CountryTech] 加载科技图标 {_techIcons.Count} 个");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountryTech] 加载图集失败: {ex.Message}");
        }
    }

    private static int ParseTechTypeFromName(string name)
    {
        const string prefix = "countrytech_";
        int i = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return -1;
        string s = name.Substring(i + prefix.Length);
        int dot = s.IndexOf('.');
        if (dot >= 0) s = s.Substring(0, dot);
        return int.TryParse(s, out int t) ? t : -1;
    }

    private void DrawNode(CountryTechData node, bool selected)
    {
        var g = Grid2World(node.ResearchLv, node.Position);
        var center = W2S(g.wx, g.wy);
        var radius = NodeRadius * _scale;

        if (_techIcons.TryGetValue(node.Type, out var icon) && icon is CroppedBitmap cb)
        {
            double imgH = 2 * radius;
            double imgW = imgH * cb.Width / cb.Height;
            var img = new Image { Source = icon, Width = imgW, Height = imgH, Stretch = Stretch.Fill, Tag = node.Id };
            Canvas.SetLeft(img, center.X - imgW / 2);
            Canvas.SetTop(img, center.Y - imgH / 2);
            _canvas.Children.Add(img);

            if (selected)
            {
                var sel = new Rectangle
                {
                    Width = imgW + 4, Height = imgH + 4,
                    Stroke = EditorTheme.Accent,
                    StrokeThickness = Math.Max(1.5, 2.5 * _scale),
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(sel, center.X - sel.Width / 2);
                Canvas.SetTop(sel, center.Y - sel.Height / 2);
                _canvas.Children.Add(sel);
            }

            var name = _parser.GetTechNameByType(node.Type);
            if (string.IsNullOrEmpty(name)) name = Loc.F("CountryTech.UnnamedType", node.Type);
            AddScreenTextPair(center.X, center.Y + imgH / 2 + 2, node.Id.ToString() + "  ", EditorTheme.TextMuted, 12, name, EditorTheme.Text, 11, node.Id);
            return;
        }

        var pts = new PointCollection();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 180 * (60 * i - 30);
            pts.Add(new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a)));
        }
        _canvas.Children.Add(new Polygon
        {
            Points = pts,
            Fill = selected ? EditorTheme.Selection : EditorTheme.Bg3,
            Stroke = selected ? EditorTheme.Accent : EditorTheme.BorderStrong,
            StrokeThickness = Math.Max(1, 2 * _scale),
            Tag = node.Id
        });
        var fallbackName = _parser.GetTechNameByType(node.Type);
        if (string.IsNullOrEmpty(fallbackName)) fallbackName = Loc.F("CountryTech.UnnamedType", node.Type);
        AddScreenTextPair(center.X, center.Y + 14, node.Id.ToString() + "  ", EditorTheme.TextMuted, 12, fallbackName, EditorTheme.Text, 11, node.Id);
    }

    // 用屏幕距离命中：遍历当前分类节点，点击落在节点半径内即命中。
    // 不依赖 VisualTreeHelper.HitTest（对 Image 命中不可靠，选中态节点拖不动）。
    private CountryTechData? FindNodeAt(Point p)
    {
        int cat = _categoryCombo.SelectedValue is int c ? c : -1;
        var nodes = cat < 0 ? _parser.Items : _parser.Items.Where(t => t.CategoryId == cat);
        CountryTechData? best = null;
        double bestDist = NodeRadius * _scale;
        foreach (var n in nodes)
        {
            var g = Grid2World(n.ResearchLv, n.Position);
            var s = W2S(g.wx, g.wy);
            double dx = p.X - s.X, dy = p.Y - s.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    private void SelectNodeInList(int id)
    {
        foreach (CountryTechListEntry item in _listBox.Items)
            if (item.Id == id) { _listBox.SelectedItem = item; return; }
    }

    // 右键空白处弹出菜单：在此新增节点；右键节点弹出菜单：连接至此节点（设置 NeedId）
    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var hit = FindNodeAt(pt);
        var menu = new ContextMenu();
        // 右键菜单是独立弹层，拿不到场景树上的隐式样式，需把主题字典合并进来
        menu.Resources.MergedDictionaries.Add(EditorTheme.Dictionary);
        if (hit != null)
        {
            var name = _parser.GetTechNameByType(hit.Type);
            if (string.IsNullOrEmpty(name)) name = Loc.F("CountryTech.UnnamedType", hit.Type);
            var item = new MenuItem { Header = Loc.F("CountryTech.Menu.ConnectHere", hit.Id, name) };
            item.Click += (_, _) => ConnectToNode(hit);
            menu.Items.Add(item);
            var unlink = new MenuItem { Header = Loc.T("CountryTech.Menu.Unlink") };
            unlink.Click += (_, _) => DisconnectNodes();
            menu.Items.Add(unlink);
        }
        else
        {
            var (level, position) = EventGrid(pt);
            var item = new MenuItem { Header = Loc.F("CountryTech.Menu.AddHere", level, position) };
            item.Click += (_, _) => AddAt(level, position);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // 把所有选中子节点的 NeedId 设为 target.Id（父级节点为被连接者）
    private void ConnectToNode(CountryTechData target)
    {
        var children = _parser.Items.Where(t => _selectedIds.Contains(t.Id) && t.Id != target.Id).ToList();
        if (children.Count == 0)
        {
            MessageBox.Show(Loc.T("CountryTech.ConnectHint"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var child in children)
        {
            child.NeedId = new List<int> { target.Id };
            if (_autoUpdateLines) UpdateConnectedLines(child.Id);
        }
        RefreshList();
        if (_current != null) LoadUiFromCurrent();
        RenderCanvas();
        SetStatus(Loc.F("CountryTech.Connected", children.Count, target.Id));
    }

    // 清空所有选中节点的 NeedId（断开与父级的连接）
    private void DisconnectNodes()
    {
        var nodes = _parser.Items.Where(t => _selectedIds.Contains(t.Id)).ToList();
        if (nodes.Count == 0)
        {
            MessageBox.Show(Loc.T("CountryTech.UnlinkHint"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var n in nodes)
        {
            n.NeedId.Clear();
            n.Lines.Clear();
            n.Lines2.Clear();
        }
        RefreshList();
        if (_current != null) LoadUiFromCurrent();
        RenderCanvas();
        SetStatus(Loc.F("CountryTech.Unlinked", nodes.Count));
    }

    // 左键：点节点=拖动，点空白=平移视图
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var node = FindNodeAt(pt);
        if (node != null)
        {
            _dragNode = node;
            _draggingNode = false;
            _dragStart = pt;
            _canvas.CaptureMouse();
        }
        else
        {
            _panning = true;
            _panStart = pt;
            _panStartOffX = _offsetX; _panStartOffY = _offsetY;
            _canvas.CaptureMouse();
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        if (_panning)
        {
            _offsetX = _panStartOffX + (pt.X - _panStart.X);
            _offsetY = _panStartOffY + (pt.Y - _panStart.Y);
            RenderCanvas();
            return;
        }
        if (_dragNode != null)
        {
            if (!_draggingNode && (Math.Abs(pt.X - _dragStart.X) > 4 || Math.Abs(pt.Y - _dragStart.Y) > 4))
                _draggingNode = true;
            if (_draggingNode)
            {
                (_ghostLevel, _ghostPosition) = EventGrid(pt);
                RenderCanvas();
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();
        if (_panning) { _panning = false; return; }
        if (_dragNode != null)
        {
            var node = _dragNode;
            if (_draggingNode)
            {
                var (level, position) = EventGrid(e.GetPosition(_canvas));
                bool conflict = _parser.Items.Any(t => t != node && t.CategoryId == node.CategoryId && t.ResearchLv == level && t.Position == position);
                if (conflict)
                    MessageBox.Show(Loc.T("CountryTech.MoveConflict"), Loc.T("CountryTech.MoveConflictTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                else if (level != node.ResearchLv || position != node.Position)
                {
                    node.ResearchLv = level;
                    node.Position = position;
                    int changed = _autoUpdateLines ? UpdateConnectedLines(node.Id) : 0;
                    _current = node;
                    _selectedIds.Clear();
                    _selectedIds.Add(node.Id);
                    LoadUiFromCurrent();
                    RefreshList();
                    SetStatus(Loc.F("CountryTech.NodeMoved", changed));
                }
            }
            else
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (_selectedIds.Contains(node.Id)) _selectedIds.Remove(node.Id);
                    else _selectedIds.Add(node.Id);
                    _current = node;
                    LoadUiFromCurrent();
                    RenderCanvas();
                }
                else
                {
                    SelectNodeInList(node.Id);
                }
            }
            _dragNode = null;
            _draggingNode = false;
            RenderCanvas();
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.12 : 0.892857;
        double newScale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
        if (Math.Abs(newScale - _scale) < 0.0001) return;
        var pt = e.GetPosition(_canvas);
        var (wx, wy) = S2W(pt.X, pt.Y);
        _scale = newScale;
        _offsetX = pt.X - wx * _scale;
        _offsetY = pt.Y - wy * _scale;
        RenderCanvas();
    }

    /// <summary>按 NeedId 重建被拖节点及其直接子节点的 Lines/Lines2（与 Python update_connected_lines 一致）。</summary>
    private int UpdateConnectedLines(int nodeId)
    {
        int changed = 0;
        foreach (var item in _parser.Items)
        {
            var needs = item.NeedId;
            if ((needs == null || needs.Count == 0 || !needs.Contains(nodeId)) && item.Id != nodeId) continue;
            if (needs == null || needs.Count == 0) continue;
            if (needs.Count > 2) continue;
            for (int i = 0; i < needs.Count; i++)
            {
                var parent = _parser.GetById(needs[i]);
                if (parent == null) continue;
                var np = MakeOrthogonalPath(parent, item);
                var cur = i == 0 ? item.Lines : item.Lines2;
                if (!cur.SequenceEqual(np))
                {
                    if (i == 0) item.Lines = np; else item.Lines2 = np;
                    changed++;
                }
            }
            if (needs.Count < 2 && item.Lines2.Count > 0)
            {
                item.Lines2 = new List<double>();
                changed++;
            }
        }
        return changed;
    }

    private List<double> MakeOrthogonalPath(CountryTechData parent, CountryTechData item)
    {
        // 与 GenerateOrthogonalPath 一致：相对 item 自身反向指向 parent，
        // 保证移动节点后重算的连线仍连接 parent 与 item 两点。
        var sg = Grid2World(parent.ResearchLv, parent.Position);
        var tg = Grid2World(item.ResearchLv, item.Position);
        int dx = (int)Math.Round((sg.wx - tg.wx) / LineScaleX);
        int dy = (int)Math.Round((sg.wy - tg.wy) / LineScaleY);
        if (dx == 0) return dy == 0 ? new List<double>() : new List<double> { 0, dy };
        if (dy == 0) return new List<double> { dx, 0 };
        int first = dy / 2;
        return new List<double> { 0, first, dx, first, dx, dy };
    }

    // ============================================================= 列表项类型 =============================================================
    private class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class CountryTechListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int CategoryId { get; set; }
        public int ResearchLv { get; set; }
        // 显示文字在每次取用时才翻译，切换语言后重建列表即可换文字
        public override string ToString() => Loc.F("CountryTech.ListItem", Id, Name, CategoryId, ResearchLv);
    }
}
