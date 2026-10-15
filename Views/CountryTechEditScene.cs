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

namespace WC4MapEditor.Views;

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

    // ---- 右侧面板：基本属性 / INI 文本 切换 ----
    private Button _propToggleBtn = null!;
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
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
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
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        _root.Children.Add(MakeTopBar());

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _categoryCombo = new ComboBox
        {
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 13
        };
        ApplyListPalette(_categoryCombo);
        _categoryCombo.ItemContainerStyle = MakeListItemStyle();
        _categoryCombo.SelectionChanged += (_, _) => { RefreshList(); FitView(); };
        Grid.SetRow(_categoryCombo, 0);
        leftGrid.Children.Add(_categoryCombo);

        _searchBox = new TextBox
        {
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White
        };
        _searchBox.Text = "搜索 (Id / 名称 / Type)...";
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = ""; };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 名称 / Type)..."; };
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(_searchBox, 1);
        leftGrid.Children.Add(_searchBox);

        _listBox = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13
        };
        _listBox.ItemContainerStyle = MakeListItemStyle();
        ApplyListPalette(_listBox);
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 2);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);
        _root.Children.Add(MakeSplitter());

        var canvasBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
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

        var canvasSplitter = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(canvasSplitter, 1); Grid.SetColumn(canvasSplitter, 3);
        _root.Children.Add(canvasSplitter);

        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        Grid.SetRow(right, 1); Grid.SetColumn(right, 4);
        BuildRightPanel();
        var rightGrid = new Grid();
        rightGrid.Children.Add(_propScroll);
        rightGrid.Children.Add(_iniScroll);
        _propToggleBtn = new Button
        {
            Content = "INI 文本",
            Width = 72, Height = 20,
            Margin = new Thickness(0, 4, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 12,
            Cursor = Cursors.Hand
        };
        _propToggleBtn.Click += (_, _) => TogglePropPanel();
        rightGrid.Children.Add(_propToggleBtn);
        right.Child = rightGrid;
        _root.Children.Add(right);

        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 5);
        _statusText = new TextBlock
        {
            Text = $"已加载 {_parser.Items.Count} 条国家科技",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        status.Child = _statusText;
        _root.Children.Add(status);

        Content = _root;
    }

    private Grid MakeTopBar()
    {
        var bar = new Grid();
        Grid.SetRow(bar, 0); Grid.SetColumnSpan(bar, 5);
        bar.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        panel.Children.Add(MakeTopBtn("返回", (_, _) => _window.ReturnToMainScene()));
        panel.Children.Add(MakeTopBtn("新增", OnAdd));
        panel.Children.Add(MakeTopBtn("删除", OnDelete));
        panel.Children.Add(MakeTopBtn("保存", OnSave));
        panel.Children.Add(MakeTopBtn("重载", OnReload));
        panel.Children.Add(MakeTopBtn("按NeedId生成连线", OnGenerateLines));
        panel.Children.Add(MakeTopBtn("适配视图", OnFit));
        panel.Children.Add(MakeCheck("预览NeedId", _showNeedGuides));
        panel.Children.Add(MakeCheck("自动连线", _autoUpdateLines));
        panel.Children.Add(MakeTopBtn("校验", OnValidate));
        Grid.SetColumn(panel, 0);
        bar.Children.Add(panel);
        return bar;
    }

    private static GridSplitter MakeSplitter()
    {
        var sp = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(sp, 1); Grid.SetColumn(sp, 1);
        return sp;
    }

    // 与资源管理器(AssetBrowserScene)列表项样式一致：选中蓝底黑字，悬停深蓝灰底
    private static Style MakeListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(6, 3, 6, 3)));

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(100, 150, 220))));
        selected.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.Black));
        style.Triggers.Add(selected);

        var hover = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 60, 80))));
        style.Triggers.Add(hover);

        return style;
    }

    // 覆盖系统画刷键：ComboBox/ListBox 弹层深底、hover/选中蓝底黑字（模板触发器走这些动态资源）
    private static void ApplyListPalette(FrameworkElement c)
    {
        c.Resources[SystemColors.WindowBrushKey] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        c.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(Color.FromRgb(100, 150, 220));
        c.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
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

        AddSection("基本信息");
        AddNum("Id", "科技ID(Id)", 0, int.MaxValue);
        AddText("NameIni", "名称(Name / INI skill_name_{Id})");
        AddNum("CategoryId", "分类ID(CategoryId)", 0, int.MaxValue);
        AddNum("Categorys", "类别(Categorys 0~4)", 0, 4);
        AddNum("Type", "类型/图标(Type)", 0, int.MaxValue);
        AddNum("ResearchLv", "研发等级需求(ResearchLv)", 0, int.MaxValue);
        AddNum("Position", "UI位置(Position)", 0, int.MaxValue);

        AddSection("消耗");
        AddNum("CostMoney", "消耗金钱(CostMoney)", 0, int.MaxValue);
        AddNum("CostGear", "消耗零件(CostGear)", 0, int.MaxValue);
        AddNum("CostAtomic", "消耗核能(CostAtomic)", 0, int.MaxValue);
        AddNum("CostMerit", "消耗功勋(CostMerit)", 0, int.MaxValue);

        AddSection("其它");
        AddNum("Chance", "概率(Chance %)", 0, 100);
        AddNum("Value", "效果值(Value)", int.MinValue, int.MaxValue);

        AddSection("引用 / 连线（逗号分隔）");
        AddText("NeedId", "前置科技需求(NeedId)");
        AddText("Lines", "连线坐标1(Lines)");
        AddText("Lines2", "连线坐标2(Lines2)");

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
            Height = 220
        };
        _propPanel.Children.Add(_jsonPreview);

        // INI 文本编辑面板（按 Type 显示/修改 countrytech_{type} / countrytech_desc_{type}）
        var iniPanel = new StackPanel { Margin = new Thickness(0) };
        iniPanel.Children.Add(new TextBlock
        {
            Text = "INI 文本（按 Type）",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
            FontSize = 13, FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 18, 0, 4)
        });
        _iniTypeText = new TextBlock { Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(8, 0, 8, 8) };
        iniPanel.Children.Add(_iniTypeText);

        _iniNameBox = MakeIniTextBox(false);
        _iniNameBox.LostFocus += (_, _) => { if (_current != null) _parser.SetTechNameByType(_current.Type, _iniNameBox.Text); };
        iniPanel.Children.Add(MakeIniRow("名称 (countrytech_{type})", _iniNameBox));

        _iniDescBox = MakeIniTextBox(true);
        _iniDescBox.LostFocus += (_, _) => { if (_current != null) _parser.SetTechDescByType(_current.Type, _iniDescBox.Text); };
        iniPanel.Children.Add(MakeIniRow("介绍 (countrytech_desc_{type})", _iniDescBox));

        _iniScroll = new ScrollViewer
        {
            Content = iniPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };
    }

    private static TextBox MakeIniTextBox(bool multiline)
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
            AcceptsReturn = multiline,
            Margin = new Thickness(8, 2, 8, 6)
        };
        if (multiline) { tb.Height = 90; tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        return tb;
    }

    private static FrameworkElement MakeIniRow(string label, TextBox content)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lbl = new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) };
        Grid.SetRow(lbl, 0); Grid.SetRow(content, 1);
        grid.Children.Add(lbl); grid.Children.Add(content);
        border.Child = grid;
        return border;
    }

    private void TogglePropPanel()
    {
        _showIniPanel = !_showIniPanel;
        _propScroll.Visibility = _showIniPanel ? Visibility.Collapsed : Visibility.Visible;
        _iniScroll.Visibility = _showIniPanel ? Visibility.Visible : Visibility.Collapsed;
        _propToggleBtn.Content = _showIniPanel ? "基本属性" : "INI 文本";
        if (_showIniPanel) LoadIniPanel();
    }

    private void LoadIniPanel()
    {
        if (_current == null) { _iniTypeText.Text = ""; _iniNameBox.Text = ""; _iniDescBox.Text = ""; return; }
        _iniTypeText.Text = $"Type = {_current.Type}";
        _iniNameBox.Text = _parser.GetTechNameByType(_current.Type);
        _iniDescBox.Text = _parser.GetTechDescByType(_current.Type);
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

    private CheckBox MakeCheck(string label, bool initial)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = initial,
            Foreground = Brushes.White,
            Margin = new Thickness(6, 0, 6, 0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (_, _) => RenderCanvas();
        cb.Unchecked += (_, _) => RenderCanvas();
        return cb;
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitView();

    private void AddSection(string title, UIElement? right = null)
    {
        if (right == null)
        {
            _propPanel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 10, 0, 4)
            });
            return;
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tb = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 10, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        grid.Children.Add(tb);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        _propPanel.Children.Add(grid);
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
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

    private NumericUpDown AddNum(string key, string label, double min = int.MinValue, double max = int.MaxValue, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
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
        tb.TextChanged += (_, _) =>
        {
            if (_loadingUi || _current == null) return;
            ApplyPropCurrent();
            if (key == "NameIni") _parser.SetTechName(_current.Id, tb.Text);
        };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshCategories()
    {
        var sel = _categoryCombo.SelectedValue;
        _categoryCombo.Items.Clear();
        _categoryCombo.Items.Add(new CategoryItem { Id = -1, Name = "全部分类" });
        foreach (var cat in _parser.Items.Select(t => t.CategoryId).Distinct().OrderBy(x => x))
            _categoryCombo.Items.Add(new CategoryItem { Id = cat, Name = $"分类 {cat}" });
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
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
            if (string.IsNullOrEmpty(name)) name = $"科技_{t.Id}";
            _listBox.Items.Add(new CountryTechListEntry
            {
                Id = t.Id,
                Name = name,
                CategoryId = t.CategoryId,
                ResearchLv = t.ResearchLv
            });
        }
        SetStatus($"已加载 {_parser.Items.Count} 条国家科技，列表 {_listBox.Items.Count} 条");
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
        catch { _jsonPreview.Text = "(序列化失败)"; }
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
        _parser.SetTechName(newId, $"新科技{newId}");
        RefreshCategories();
        RefreshList();
        foreach (CountryTechListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增科技 ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一条科技", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除科技 [{_current.Id}] {_parser.GetTechName(_current.Id)}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveTech(id);
        _current = null;
        RefreshCategories();
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除科技 ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功：\nCountryTechSettings.json: {_parser.ConfigPath}\nstringtable: {_parser.StringTablePath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 CountryTechSettings.json？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshCategories();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void OnGenerateLines(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先选中一条科技", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_current.NeedId.Count == 0) { MessageBox.Show("该科技没有前置需求(NeedId)，无需生成连线", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        int applied = 0;
        if (_current.NeedId.Count >= 1)
        {
            var src = _parser.GetById(_current.NeedId[0]);
            if (src != null) { _current.Lines = GenerateOrthogonalPath(src, _current); applied++; }
            else SetStatus($"⚠ NeedId[0]={_current.NeedId[0]} 找不到前置科技");
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
        SetStatus($"已根据 NeedId 生成 {applied} 条正交连线（未保存）");
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
            string pfx = $"[Id={t.Id}]";
            if (seenIds.Contains(t.Id)) problems.Add($"{pfx} ID 重复");
            else seenIds.Add(t.Id);

            if (t.Categorys < 0 || t.Categorys > 4) problems.Add($"{pfx} Categorys={t.Categorys} 不在 0~4");

            if (!typeByCat.TryGetValue(t.CategoryId, out var set))
            {
                set = new HashSet<int>();
                typeByCat[t.CategoryId] = set;
            }
            if (set.Contains(t.Type)) problems.Add($"{pfx} 分类 {t.CategoryId} 下 Type={t.Type} 重复");
            else set.Add(t.Type);

            if (t.NeedId.Count > 2) problems.Add($"{pfx} NeedId 数量 {t.NeedId.Count} > 2，连线仅支持 2 条");

            if (t.Lines.Count % 2 != 0) problems.Add($"{pfx} Lines 数量 {t.Lines.Count} 不是偶数");
            if (t.Lines2.Count % 2 != 0) problems.Add($"{pfx} Lines2 数量 {t.Lines2.Count} 不是偶数");

            foreach (var nid in t.NeedId)
                if (_parser.GetById(nid) == null) problems.Add($"{pfx} NeedId={nid} 找不到前置科技");

            if (t.CostMoney < 0 || t.CostGear < 0 || t.CostAtomic < 0) problems.Add($"{pfx} 消耗出现负值");
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

        for (int level = lmin; level <= lmax; level++)
        {
            var g = Grid2World(level, pmin - 0.45);
            AddText(g.wx, g.wy, "L" + level, new SolidColorBrush(Color.FromRgb(0x5d, 0x78, 0x87)), 12);
        }
        for (int pos = pmin; pos <= pmax; pos++)
        {
            var g = Grid2World(lmin - 0.45, pos);
            AddText(g.wx, g.wy, "P" + pos, new SolidColorBrush(Color.FromRgb(0x5d, 0x78, 0x87)), 12);
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
                var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = new SolidColorBrush(Color.FromRgb(0x82, 0xA7, 0xB4)) };
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
                        Stroke = new SolidColorBrush(Color.FromRgb(0x9b, 0x6b, 0xa7)),
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
            var ghost = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = new SolidColorBrush(Colors.Red), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 } };
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
            Stroke = new SolidColorBrush(Color.FromRgb(0x16, 0x8D, 0xA3)),
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
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
                    StrokeThickness = Math.Max(1.5, 2.5 * _scale),
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(sel, center.X - sel.Width / 2);
                Canvas.SetTop(sel, center.Y - sel.Height / 2);
                _canvas.Children.Add(sel);
            }

            var name = _parser.GetTechNameByType(node.Type);
            if (string.IsNullOrEmpty(name)) name = "Tech_" + node.Type;
            AddScreenTextPair(center.X, center.Y + imgH / 2 + 2, node.Id.ToString() + "  ", new SolidColorBrush(Color.FromRgb(0x21, 0x37, 0x46)), 12, name, Brushes.Black, 11, node.Id);
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
            Fill = selected ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xB2)) : new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(Color.FromRgb(0x4e, 0x71, 0x80)),
            StrokeThickness = Math.Max(1, 2 * _scale),
            Tag = node.Id
        });
        var fallbackName = _parser.GetTechNameByType(node.Type);
        if (string.IsNullOrEmpty(fallbackName)) fallbackName = "Tech_" + node.Type;
        AddScreenTextPair(center.X, center.Y + 14, node.Id.ToString() + "  ", new SolidColorBrush(Color.FromRgb(0x21, 0x37, 0x46)), 12, fallbackName, Brushes.Black, 11, node.Id);
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
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42))
        };
        if (hit != null)
        {
            var name = _parser.GetTechNameByType(hit.Type);
            if (string.IsNullOrEmpty(name)) name = "Tech_" + hit.Type;
            var item = new MenuItem { Header = $"连接至此节点 [Id={hit.Id} {name}]", Foreground = Brushes.White };
            item.Click += (_, _) => ConnectToNode(hit);
            menu.Items.Add(item);
            var unlink = new MenuItem { Header = "解绑（清空选中节点 NeedId）", Foreground = Brushes.White };
            unlink.Click += (_, _) => DisconnectNodes();
            menu.Items.Add(unlink);
        }
        else
        {
            var (level, position) = EventGrid(pt);
            var item = new MenuItem { Header = $"在此新增 (L{level}, P{position})", Foreground = Brushes.White };
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
            MessageBox.Show("请先按住 Ctrl 选中一个或多个子节点，再右键父节点选「连接至此节点」。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
        SetStatus($"已将 {children.Count} 个节点的 NeedId 设为 {target.Id}（未保存）");
    }

    // 清空所有选中节点的 NeedId（断开与父级的连接）
    private void DisconnectNodes()
    {
        var nodes = _parser.Items.Where(t => _selectedIds.Contains(t.Id)).ToList();
        if (nodes.Count == 0)
        {
            MessageBox.Show("请先按住 Ctrl 选中要解绑的节点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
        SetStatus($"已解绑 {nodes.Count} 个节点的 NeedId（未保存）");
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
                    MessageBox.Show("目标网格已被本组另一科技占用，移动取消。", "不能移动", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    SetStatus($"节点已移动（自动调整 {changed} 条连线，未保存）");
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
        public override string ToString() => $"[{Id}] {Name}  (分类:{CategoryId} 等级:{ResearchLv})";
    }
}
