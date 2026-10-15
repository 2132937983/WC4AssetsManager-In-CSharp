using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SkiaSharp;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

public class GeneralEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly GeneralSettingParser _parser = GeneralSettingParser.Instance;

    private Grid _root = null!;
    private Border _topBar = null!;
    private Border _leftPanel = null!;
    private Border _rightPanel = null!;
    private GridSplitter _splitter = null!;

    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private GeneralSettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;

    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private Image _headPreview = null!;
    private Image _photoPreview = null!;
    private Image _tacticalHeadPreview = null!;
    private TextBox _jsonPreview = null!;

    // 图片缓存，避免每次切换将领都重新解码 webp
    // 使用 LRU 机制限制缓存大小，防止内存无限增长导致 GC 卡顿
    private readonly LinkedList<string> _imageCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _imageCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxImageCacheSize = 50;

    // 按 Image 控件区分的取消令牌，避免头像和大图加载互相取消
    private readonly Dictionary<Image, CancellationTokenSource> _imageLoadCtsMap = new();

    public GeneralEditScene(MainWindow window)
    {
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        BuildUI();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_parser.All.Count > 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
        }, DispatcherPriority.Loaded);
    }

    // ===================================== UI 构建 =====================================
    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 顶部工具栏
        _topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid.SetRow(_topBar, 0); Grid.SetColumnSpan(_topBar, 3);
        var topSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };

        topSp.Children.Add(MakeTopBtn("← 返回", OnBack, primary: false));
        topSp.Children.Add(new Border { Width = 1 });
        topSp.Children.Add(MakeTopBtn("➕ 新增将领", OnAdd, primary: true));
        topSp.Children.Add(MakeTopBtn("❌ 删除选中", OnDelete, primary: false));
        topSp.Children.Add(MakeTopBtn("💾 保存", OnSave, primary: true));
        topSp.Children.Add(MakeTopBtn("↻ 重载", OnReload, primary: false));
        topSp.Children.Add(new Border { Width = 8 });

        // 随机参数按钮
        topSp.Children.Add(MakeTopBtn("🎲 低级随机", (_, _) => ApplyRandomTemplate(1), primary: false));
        topSp.Children.Add(MakeTopBtn("🎲 中级随机", (_, _) => ApplyRandomTemplate(2), primary: false));
        topSp.Children.Add(MakeTopBtn("🎲 高级随机", (_, _) => ApplyRandomTemplate(3), primary: false));
        topSp.Children.Add(new Border { Width = 8 });

        // 兵种专长按钮
        topSp.Children.Add(MakeTopBtn("🦶 步将", (_, _) => ApplySpecialtyTemplate("infantry"), primary: false));
        topSp.Children.Add(MakeTopBtn("🛡️ 坦将", (_, _) => ApplySpecialtyTemplate("armor"), primary: false));
        topSp.Children.Add(MakeTopBtn("💣 炮将", (_, _) => ApplySpecialtyTemplate("artillery"), primary: false));
        topSp.Children.Add(MakeTopBtn("⚓ 海将", (_, _) => ApplySpecialtyTemplate("navy"), primary: false));
        topSp.Children.Add(MakeTopBtn("✈️ 空将", (_, _) => ApplySpecialtyTemplate("airforce"), primary: false));

        var titleTb = new TextBlock
        {
            Text = "将领数据编辑",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        };
        topSp.Children.Add(titleTb);

        _topBar.Child = topSp;
        _root.Children.Add(_topBar);

        // 左栏：搜索 + 列表
        _leftPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(_leftPanel, 1); Grid.SetColumn(_leftPanel, 0);

        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
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
        _searchBox.Text = "搜索 (Id / 名称 / EName)...";
        _searchBox.GotFocus += (s, e) =>
        {
            if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = string.Empty;
        };
        _searchBox.LostFocus += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 名称 / EName)...";
        };
        _searchBox.TextChanged += (s, e) => RefreshList();
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
        _listBox.KeyDown += ListBox_KeyDown;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        _leftPanel.Child = leftGrid;
        _root.Children.Add(_leftPanel);

        // Splitter
        _splitter = new GridSplitter
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true
        };
        Grid.SetRow(_splitter, 1); Grid.SetColumn(_splitter, 1);
        _root.Children.Add(_splitter);

        // 右栏：属性面板
        _rightPanel = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        Grid.SetRow(_rightPanel, 1); Grid.SetColumn(_rightPanel, 2);
        BuildRightPanel();
        _root.Children.Add(_rightPanel);

        // 底部状态栏
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _statusText = new TextBlock
        {
            Text = $"已加载 {_parser.All.Count} 个将领配置",
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
        _propScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20)
        };
        _propPanel = new StackPanel { Orientation = Orientation.Vertical };
        _propScroll.Content = _propPanel;
        _rightPanel.Child = _propScroll;

        // 顶部预览区：头像 + 半身像 + 战术地图头像 + JSON预览（横向排列，节省垂直空间）
        var previewGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });   // 头像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });   // 半身像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });   // 战术地图头像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // JSON预览

        // 头像 (general_circle)
        var headWrap = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _headPreview = new Image
        {
            Width = 100, Height = 100,
            Stretch = Stretch.Uniform,
            Source = null
        };
        headWrap.Child = _headPreview;
        Grid.SetColumn(headWrap, 0);
        previewGrid.Children.Add(headWrap);

        // 半身像 (generalphoto)
        var photoWrap = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = 180
        };
        var photoPanel = new StackPanel { Orientation = Orientation.Vertical };
        _photoPreview = new Image { Stretch = Stretch.Uniform, Height = 160, Cursor = Cursors.Hand };
        _photoPreview.MouseLeftButtonDown += OnPhotoPreviewClick;
        photoPanel.Children.Add(_photoPreview);
        var photoTip = new TextBlock
        {
            Text = "点击编辑图片",
            Foreground = Brushes.Gray,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        photoPanel.Children.Add(photoTip);
        photoWrap.Child = photoPanel;
        Grid.SetColumn(photoWrap, 2);
        previewGrid.Children.Add(photoWrap);

        // 战术地图头像 (head_{EName} from tacticalmap)
        var tacticalHeadWrap = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _tacticalHeadPreview = new Image
        {
            Width = 100, Height = 100,
            Stretch = Stretch.Uniform,
            Source = null
        };
        tacticalHeadWrap.Child = _tacticalHeadPreview;
        Grid.SetColumn(tacticalHeadWrap, 4);
        previewGrid.Children.Add(tacticalHeadWrap);

        // JSON 预览（只读）
        var jsonBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            MaxHeight = 180
        };
        _jsonPreview = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Text = "{}"
        };
        jsonBorder.Child = _jsonPreview;
        Grid.SetColumn(jsonBorder, 6);
        previewGrid.Children.Add(jsonBorder);

        _propPanel.Children.Add(previewGrid);

        _propPanel.Children.Add(Separator());

        // ---- 基本信息 ----
        AddSection("基本信息");
        AddRow("Id", AddNum("Id", min: 1, max: 99999, changed: _ => ApplyProp()));
        AddRow("名称 (Name)", AddText("Name", changed: _ => ApplyProp()));
        AddRow("英文名 (EName)", AddText("EName", changed: _ => ApplyProp()));
        AddRow("显示图 (Photo)", AddText("Photo", changed: _ => { ApplyProp(); RefreshPreviews(); }));

        _propPanel.Children.Add(Separator());
        AddSection("战斗属性 (当前 / 上限)");

        void AddStat(string key, string label, int max)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var cur = AddNum(key, 0, max, _ => ApplyProp());
            cur.Margin = new Thickness(0);
            cur.Width = 100;
            var sep = new TextBlock { Text = "  /  ", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            var maxCtrl = AddNum(key + "Max", 0, 99, _ => ApplyProp());
            maxCtrl.Margin = new Thickness(0);
            maxCtrl.Width = 100;
            sp.Children.Add(cur);
            sp.Children.Add(sep);
            sp.Children.Add(maxCtrl);
            AddRow(label, sp);
        }
        AddStat("Infantry", "步兵", 99);
        AddStat("Armor", "装甲", 99);
        AddStat("Artillery", "火炮", 99);
        AddStat("Navy", "海军", 99);
        AddStat("AirForce", "空军", 99);
        AddStat("March", "行军", 99);

        _propPanel.Children.Add(Separator());
        AddSection("军衔与生命");
        AddRow("军衔 (MilitaryRank)", AddNum("MilitaryRank", 0, 20, _ => ApplyProp()));
        AddRow("生命等级 (Hp)", AddNum("Hp", 0, 20, _ => ApplyProp()));

        _propPanel.Children.Add(Separator());
        AddSection("商店/UI");
        AddRow("InShop", AddNum("InShop", 0, 1, _ => ApplyProp()));
        AddRow("Type", AddNum("Type", 0, 99, _ => ApplyProp()));
        AddRow("Evaluate", AddNum("Evaluate", 0, 99, _ => ApplyProp()));
        AddRow("Sequence", AddNum("Sequence", 0, 9999, _ => ApplyProp()));
        AddRow("解锁 HQ 等级", AddNum("UnlockHQLv", 0, 999, _ => ApplyProp()));
        AddRow("勋章花费", AddNum("CostMedal", 0, 999999, _ => ApplyProp()));
        AddRow("金币花费", AddNum("CostGold", 0, 99999999, _ => ApplyProp()));

        _propPanel.Children.Add(Separator());
        AddSection("技能与勋章 (逗号/空格分隔整数)");
        AddRow("技能 IDs (Skills)", AddText("Skills", changed: _ => ApplyProp()));
        AddRow("勋章 IDs (Medals)", AddText("Medals", changed: _ => ApplyProp()));
        AddRow("技能上限 (SkillsMax)", AddNum("SkillsMax", 0, 99, _ => ApplyProp()));
        AddRow("重置技能 (ResetSkills)", AddNum("ResetSkills", 0, 1, _ => ApplyProp()));

        _propPanel.Children.Add(Separator());
        AddSection("头像位移 (def_portraitpos.xml)");
        AddRow("PosX", AddNum("Portrait_PosX", -999, 999, _ => ApplyPortrait()));
        AddRow("PosY", AddNum("Portrait_PosY", -999, 999, _ => ApplyPortrait()));
        AddRow("Scale", AddNum("Portrait_Scale", 0.01, 10.0, 0.01, _ => ApplyPortrait()));

        var tip = new TextBlock
        {
            Text = "提示：新增将领时 PortraitPos 会自动创建 posx=-30, posy=40, scale=1.0；\n删除将领时 PortraitPos 中对应项会同步删除。",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _propPanel.Children.Add(tip);
    }

    // ===================================== UI 辅助 =====================================
    private Button MakeTopBtn(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var bg = primary ? new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0xC6)) : new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
        var hoverBg = primary ? new SolidColorBrush(Color.FromRgb(0x11, 0x77, 0xDD)) : new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        var b = new Button
        {
            Content = text,
            Background = bg,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += onClick;
        return b;
    }

    private void AddSection(string title)
    {
        var tb = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 10, 0, 8)
        };
        _propPanel.Children.Add(tb);
    }

    private FrameworkElement Separator() => new Border
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
        Margin = new Thickness(0, 8, 0, 8)
    };

    private void AddRow(string label, FrameworkElement value)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lb = new TextBlock
        {
            Text = label,
            Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        Grid.SetColumn(lb, 0);
        Grid.SetColumn(value, 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(lb);
        row.Children.Add(value);
        _propPanel.Children.Add(row);
    }

    private TextBox AddText(string key, Action<string> changed)
    {
        var tb = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            CaretBrush = Brushes.White,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13
        };
        tb.TextChanged += (s, e) => changed?.Invoke(tb.Text);
        _textBoxes[key] = tb;
        return tb;
    }

    private NumericUpDown AddNum(string key, double min, double max, Action<double> changed)
        => AddNum(key, min, max, 1.0, changed);

    private NumericUpDown AddNum(string key, double min, double max, double step, Action<double> changed)
    {
        var n = new NumericUpDown
        {
            MinValue = min, MaxValue = max, Increment = step
        };
        n.ValueChanged += (s, e) => changed?.Invoke(n.Value);
        _numBoxes[key] = n;
        return n;
    }

    // ===================================== 列表/选中 =====================================
    private void RefreshList()
    {
        var query = _parser.All.AsEnumerable();
        var s = _searchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(s) && !s.StartsWith("搜索"))
        {
            query = query.Where(g =>
                g.Id.ToString().Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (g.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (g.EName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        // 保持 JSON 文件读取顺序（先读的放最上面），不做排序
        var list = query.ToList();
        var previousId = (_listBox?.SelectedItem as GeneralListEntry)?.Id;
        _listBox.Items.Clear();
        foreach (var g in list)
        {
            var entry = new GeneralListEntry(g);
            _listBox.Items.Add(entry);
            if (previousId.HasValue && g.Id == previousId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus($"已加载 {_parser.All.Count} 个将领配置，列表显示 {_listBox.Items.Count} 项");
    }

    private sealed class GeneralListEntry
    {
        public int Id { get; }
        public string Name { get; }
        public string EName { get; }
        public GeneralListEntry(GeneralSettingData g) { Id = g.Id; Name = g.Name ?? ""; EName = g.EName ?? ""; }
        public override string ToString() => $"[{Id}] {Name}  ({EName})";
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = _listBox.SelectedItem as GeneralListEntry;
        if (entry == null) { _current = null; ShowEmpty(); return; }
        var g = _parser.GetById(entry.Id);
        if (g == null) { _current = null; ShowEmpty(); return; }
        _current = g;
        LoadUiFromCurrent();
    }

    private void ListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_listBox.Items.Count == 0) return;
        int idx = _listBox.SelectedIndex;
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift+Tab = 向上
                if (idx > 0) _listBox.SelectedIndex = idx - 1;
            }
            else
            {
                // Tab = 向下
                if (idx < _listBox.Items.Count - 1) _listBox.SelectedIndex = idx + 1;
            }
            if (_listBox.SelectedItem != null) _listBox.ScrollIntoView(_listBox.SelectedItem);
        }
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var kv in _textBoxes) kv.Value.Text = string.Empty;
        foreach (var kv in _numBoxes) kv.Value.Value = kv.Value.MinValue;
        _headPreview.Source = null;
        _photoPreview.Source = null;
        _tacticalHeadPreview.Source = null;
        if (_jsonPreview != null) _jsonPreview.Text = "{}";
        _loadingUi = false;
    }

    // ===================================== UI ↔ 数据 =====================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        try
        {
            SetText("Name", _current.Name);
            SetText("EName", _current.EName);
            SetText("Photo", _current.Photo);
            SetText("Skills", _current.Skills == null ? "" : string.Join(", ", _current.Skills));
            SetText("Medals", _current.Medals == null ? "" : string.Join(", ", _current.Medals));

            SetNum("Id", _current.Id);
            SetNum("MilitaryRank", _current.MilitaryRank);
            SetNum("Hp", _current.Hp);
            SetNum("InShop", _current.InShop);
            SetNum("Type", _current.Type);
            SetNum("Evaluate", _current.Evaluate);
            SetNum("Sequence", _current.Sequence);
            SetNum("UnlockHQLv", _current.UnlockHQLv);
            SetNum("CostMedal", _current.CostMedal);
            SetNum("CostGold", _current.CostGold);
            SetNum("SkillsMax", _current.SkillsMax);
            SetNum("ResetSkills", _current.ResetSkills);

            SetNum("Infantry", _current.Infantry);
            SetNum("Armor", _current.Armor);
            SetNum("Artillery", _current.Artillery);
            SetNum("Navy", _current.Navy);
            SetNum("AirForce", _current.AirForce);
            SetNum("March", _current.March);
            SetNum("InfantryMax", _current.InfantryMax);
            SetNum("ArmorMax", _current.ArmorMax);
            SetNum("ArtilleryMax", _current.ArtilleryMax);
            SetNum("NavyMax", _current.NavyMax);
            SetNum("AirForceMax", _current.AirForceMax);
            SetNum("MarchMax", _current.MarchMax);

            // PortraitPos
            var key = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
            var p = _parser.GetPortrait(key ?? "");
            SetNum("Portrait_PosX", p?.PosX ?? -30);
            SetNum("Portrait_PosY", p?.PosY ?? 40);
            SetNum("Portrait_Scale", p?.Scale ?? 1.0);

            RefreshPreviews();
            RefreshJsonPreview();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void ApplyProp()
    {
        if (_loadingUi || _current == null) return;
        try
        {
            _current.Name = GetText("Name") ?? "";
            _current.EName = GetText("EName") ?? "";
            _current.Photo = GetText("Photo") ?? "";
            _current.Id = (int)GetNum("Id");
            _current.MilitaryRank = (int)GetNum("MilitaryRank");
            _current.Hp = (int)GetNum("Hp");
            _current.InShop = (int)GetNum("InShop");
            _current.Type = (int)GetNum("Type");
            _current.Evaluate = (int)GetNum("Evaluate");
            _current.Sequence = (int)GetNum("Sequence");
            _current.UnlockHQLv = (int)GetNum("UnlockHQLv");
            _current.CostMedal = (int)GetNum("CostMedal");
            _current.CostGold = (int)GetNum("CostGold");
            _current.SkillsMax = (int)GetNum("SkillsMax");
            _current.ResetSkills = (int)GetNum("ResetSkills");

            _current.Infantry = (int)GetNum("Infantry");
            _current.Armor = (int)GetNum("Armor");
            _current.Artillery = (int)GetNum("Artillery");
            _current.Navy = (int)GetNum("Navy");
            _current.AirForce = (int)GetNum("AirForce");
            _current.March = (int)GetNum("March");
            _current.InfantryMax = (int)GetNum("InfantryMax");
            _current.ArmorMax = (int)GetNum("ArmorMax");
            _current.ArtilleryMax = (int)GetNum("ArtilleryMax");
            _current.NavyMax = (int)GetNum("NavyMax");
            _current.AirForceMax = (int)GetNum("AirForceMax");
            _current.MarchMax = (int)GetNum("MarchMax");

            _current.Skills = ParseIntList(GetText("Skills"));
            _current.Medals = ParseIntList(GetText("Medals"));

            // 列表项文字同步
            if (_listBox.SelectedItem is GeneralListEntry le && le.Id == _current.Id)
            {
                var idx = _listBox.SelectedIndex;
                _listBox.Items[idx] = new GeneralListEntry(_current);
                _listBox.SelectedIndex = idx;
            }
            SetStatus($"已更新将领 {_current.EName}（未保存）");
            RefreshJsonPreview();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void ApplyPortrait()
    {
        if (_loadingUi || _current == null) return;
        var key = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
        if (string.IsNullOrEmpty(key)) return;
        var p = _parser.EnsurePortraitDefault(key);
        p.PosX = (int)GetNum("Portrait_PosX");
        p.PosY = (int)GetNum("Portrait_PosY");
        p.Scale = GetNum("Portrait_Scale");
        SetStatus($"已更新 PortraitPos[{key}]（未保存）");
    }

    private static List<int> ParseIntList(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new List<int>();
        var list = new List<int>();
        var separators = new[] { ',', ' ', ';', '\t', '\n', '\r' };
        foreach (var part in s.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int v)) list.Add(v);
        }
        return list;
    }

    private void SetText(string key, string value)
    {
        if (_textBoxes.TryGetValue(key, out var tb)) tb.Text = value ?? "";
    }
    private string? GetText(string key) => _textBoxes.TryGetValue(key, out var tb) ? tb.Text : null;
    private void SetNum(string key, double value)
    {
        if (_numBoxes.TryGetValue(key, out var n)) n.Value = Math.Clamp(value, n.MinValue, n.MaxValue);
    }
    private double GetNum(string key) => _numBoxes.TryGetValue(key, out var n) ? n.Value : 0;

    // ===================================== 预览 =====================================
    private void RefreshPreviews()
    {
        if (_current == null) { _headPreview.Source = null; _photoPreview.Source = null; _tacticalHeadPreview.Source = null; return; }
        var key = string.IsNullOrEmpty(_current.Photo) ? _current.EName : _current.Photo;
        RefreshImageAsync(_headPreview, _parser.GetHeadPath(key));
        RefreshImageAsync(_photoPreview, _parser.GetGeneralPhotoPath(key));
        RefreshTacticalHeadAsync(key);
    }

    private void RefreshTacticalHeadAsync(string ename)
    {
        if (string.IsNullOrEmpty(ename))
        {
            _tacticalHeadPreview.Source = null;
            return;
        }

        var cacheKey = $"tactical_head_{ename}";
        if (_imageCache.TryGetValue(cacheKey, out var cached))
        {
            TouchCache(cacheKey);
            _tacticalHeadPreview.Source = cached;
            return;
        }

        if (_imageLoadCtsMap.TryGetValue(_tacticalHeadPreview, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _imageLoadCtsMap[_tacticalHeadPreview] = cts;
        var token = cts.Token;
        var imageName = $"head_{ename}";

        _ = Task.Run(() =>
        {
            var parser = Core.Config.ConfigManager.Instance.TacticalMapParser;
            if (parser == null)
            {
                Debug.WriteLine($"[GeneralEditScene] TacticalMapParser 为 null，无法加载 {imageName}");
                return null;
            }
            Debug.WriteLine($"[GeneralEditScene] TacticalMapParser 已加载，ImageDefinitions 数量: {parser.ImageDefinitions.Count}");

            var imageCache = Rendering.Skia.TacticalMapImageCache.Instance;
            if (!imageCache.IsInitialized)
            {
                Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 未初始化，正在初始化...");
                imageCache.Initialize();
                if (!imageCache.IsInitialized)
                {
                    Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 初始化失败");
                    return null;
                }
                Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 初始化成功");
            }

            var skImage = imageCache.GetImage(imageName)
                         ?? imageCache.GetImage(imageName + ".png")
                         ?? imageCache.GetImage(imageName + ".webp");
            if (skImage == null)
            {
                Debug.WriteLine($"[GeneralEditScene] 未在 TacticalMapImageCache 中找到图像: {imageName} / {imageName}.png / {imageName}.webp");
                return null;
            }
            Debug.WriteLine($"[GeneralEditScene] 找到战术地图头像: {imageName} (实际名称含后缀, {skImage.Width}x{skImage.Height})");
            return SkImageToBitmapSource(skImage);
        }, token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(cacheKey, bmp);
                _tacticalHeadPreview.Source = bmp;
            }
            else
            {
                _tacticalHeadPreview.Source = null;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static BitmapSource? SkImageToBitmapSource(SKImage skImage)
    {
        try
        {
            using var bitmap = SKBitmap.FromImage(skImage);
            if (bitmap == null) return null;
            var info = bitmap.Info;
            var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                int rowBytes = info.Width * 4;
                var pixelData = bitmap.GetPixelSpan().ToArray();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, y * bitmap.RowBytes, dstPtr, rowBytes);
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    private void RefreshImageAsync(Image imageControl, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            imageControl.Source = null;
            return;
        }
        // 缓存命中直接显示，并移到 LRU 头部（最近使用）
        if (_imageCache.TryGetValue(path, out var cached))
        {
            TouchCache(path);
            imageControl.Source = cached;
            return;
        }
        // 取消该控件之前的加载任务，避免快速滚动时任务堆积
        if (_imageLoadCtsMap.TryGetValue(imageControl, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _imageLoadCtsMap[imageControl] = cts;
        var token = cts.Token;

        // 后台加载
        _ = Task.Run(() => TryLoadImage(path), token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(path, bmp);
                imageControl.Source = bmp;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AddToCache(string path, BitmapSource bmp)
    {
        if (_imageCache.ContainsKey(path))
        {
            _imageCache[path] = bmp;
            TouchCache(path);
            return;
        }
        // 淘汰最久未使用的缓存项
        while (_imageCache.Count >= MaxImageCacheSize && _imageCacheOrder.Count > 0)
        {
            var oldest = _imageCacheOrder.Last!.Value;
            _imageCacheOrder.RemoveLast();
            _imageCacheNodes.Remove(oldest);
            if (_imageCache.Remove(oldest, out var oldBmp))
            {
                // WriteableBitmap 的内存由自身管理，从字典移除后可以被 GC 回收
                oldBmp = null;
            }
        }
        _imageCache[path] = bmp;
        var node = _imageCacheOrder.AddFirst(path);
        _imageCacheNodes[path] = node;
    }

    private void TouchCache(string path)
    {
        if (_imageCacheNodes.TryGetValue(path, out var node))
        {
            _imageCacheOrder.Remove(node);
            _imageCacheOrder.AddFirst(node);
        }
    }

    private void RefreshJsonPreview()
    {
        if (_current == null || _jsonPreview == null) return;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
                PropertyNamingPolicy = null
            };
            var json = System.Text.Json.JsonSerializer.Serialize(_current, options);
            _jsonPreview.Text = json;
        }
        catch (Exception ex)
        {
            _jsonPreview.Text = $"序列化失败: {ex.Message}";
        }
    }

    private static BitmapSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            // WPF 默认不支持 webp，用 SkiaSharp 解码再转 WriteableBitmap
            using var skia = SKBitmap.Decode(path);
            if (skia == null) return null;
            var info = skia.Info;
            var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                int rowBytes = info.Width * 4;
                // 一次性复制像素数据到临时数组，避免每行都分配临时数组
                var pixelData = skia.GetPixelSpan().ToArray();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, y * skia.RowBytes, dstPtr, rowBytes);
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }



    // ===================================== 工具栏动作 =====================================
    private void OnBack(object sender, RoutedEventArgs e)
    {
        _window.ReturnToMainScene();
    }

    private void OnPhotoPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (_current == null) return;
        var ename = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
        if (string.IsNullOrEmpty(ename))
        {
            MessageBox.Show("当前将领没有 EName/Photo，无法进入图片编辑", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var scene = new GeneralPhotoEditScene(_window, ename);
        _window.SetCurrentScene(scene);
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        using var enameDlg = new SingleInputDialog(_window)
        {
            Title = "新增将领",
            Description = "请输入将领英文名 EName（将作为 general_{EName}.webp / portraitpos 主键）：",
            DefaultValue = "",
            MinValue = 0,
            MaxValue = 0
        };
        var ename = await enameDlg.ShowAsync();
        if (string.IsNullOrWhiteSpace(ename)) return;

        if (_parser.GetByEName(ename) != null)
        {
            MessageBox.Show($"已存在 EName={ename} 的将领，不能重复添加", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var nameDlg = new SingleInputDialog(_window)
        {
            Title = "新增将领",
            Description = $"请输入将领中文名（EName={ename}）：",
            DefaultValue = ename
        };
        var cname = await nameDlg.ShowAsync() ?? ename;

        var (g, p) = _parser.AddNewGeneral(cname, ename);
        RefreshList();
        foreach (GeneralListEntry item in _listBox.Items)
            if (item.Id == g.Id) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增将领 {g.Name}({g.EName}) ID={g.Id}，PortraitPos 默认: posx={p.PosX} posy={p.PosY} scale={p.Scale}");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个将领", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定要删除将领 [{_current.Id}] {_current.Name}({_current.EName})？\n同时将删除 def_portraitpos.xml 中的对应项。",
            "删除将领", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        string ename = _current.EName;
        if (_parser.DeleteGeneral(id))
        {
            RefreshList();
            _current = null;
            ShowEmpty();
            SetStatus($"已删除将领 ID={id} EName={ename}（未保存）");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var ok = _parser.SaveAll();
        if (ok)
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}  +  {_parser.PortraitPosPath}");
            MessageBox.Show($"保存成功：\nGeneralSettings.json: {_parser.ConfigPath}\ndef_portraitpos.xml: {_parser.PortraitPosPath}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查文件权限或查看 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 GeneralSettings.json 与 def_portraitpos.xml？未保存的更改将丢失。",
            "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[GeneralEditScene] {s}");
    }

    // ===================================== 随机参数模板 =====================================
    private void ApplyRandomTemplate(int templateId)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个将领", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var templates = ConfigManager.Instance.GetGeneralRandomTemplates();
        var template = templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            MessageBox.Show($"未找到 ID={templateId} 的随机参数模板", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var random = new Random();
        _loadingUi = true; // 批量修改期间禁止触发 ApplyProp
        try
        {
            // 基础属性
            SetField("Evaluate", template.Evaluate);
            SetField("Sequence", random.Next(template.SequenceMin, template.SequenceMax + 1));
            SetField("UnlockHQLv", template.UnlockHQLv);
            SetField("CostMedal", random.Next(template.CostMedalMin, template.CostMedalMax + 1));
            SetField("MilitaryRank", random.Next(template.MilitaryRankMin, template.MilitaryRankMax + 1));
            SetField("March", random.Next(template.MarchMin, template.MarchMax + 1));
            SetField("Hp", random.Next(template.HpMin, template.HpMax + 1));

            // 兵种属性（基于当前值除以除数）
            if (_numBoxes.TryGetValue("Infantry", out var infantryBox))
                infantryBox.Value = (int)(infantryBox.Value / template.InfantryDivisor);
            if (_numBoxes.TryGetValue("Artillery", out var artilleryBox))
                artilleryBox.Value = (int)(artilleryBox.Value / template.ArtilleryDivisor);
            if (_numBoxes.TryGetValue("Armor", out var armorBox))
                armorBox.Value = (int)(armorBox.Value / template.ArmorDivisor);
            if (_numBoxes.TryGetValue("Navy", out var navyBox))
                navyBox.Value = (int)(navyBox.Value / template.NavyDivisor);
            if (_numBoxes.TryGetValue("AirForce", out var airBox))
                airBox.Value = (int)(airBox.Value / template.AirForceDivisor);

            // 技能等级随机化
            RandomizeSkillLevels(template.SkillLevelMin, template.SkillLevelMax, template.SkillCount);
        }
        finally
        {
            _loadingUi = false;
        }

        ApplyProp();
        SetStatus($"已应用随机模板: {template.Name}");
    }

    private void RandomizeSkillLevels(int minLevel, int maxLevel, int count)
    {
        var random = new Random();
        for (int i = 0; i < count; i++)
        {
            var key = $"Skill{i}";
            if (_numBoxes.TryGetValue(key, out var box) && box.Value > 0)
            {
                int skillId = (int)box.Value;
                int baseId = (skillId / 10) * 10;
                int newLevel = random.Next(minLevel, maxLevel + 1);
                box.Value = baseId + newLevel;
            }
        }
    }

    // ===================================== 兵种专长模板 =====================================
    private void ApplySpecialtyTemplate(string specialtyKey)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个将领", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var templates = ConfigManager.Instance.GetGeneralSpecialtyTemplates();
        var template = templates.FirstOrDefault(t => t.Key.Equals(specialtyKey, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            MessageBox.Show($"未找到 Key={specialtyKey} 的兵种专长模板", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var random = new Random();
        _loadingUi = true; // 批量修改期间禁止触发 ApplyProp
        try
        {
            // 设置主属性
            if (!string.IsNullOrEmpty(template.MainStat) && _numBoxes.TryGetValue(template.MainStat, out var mainBox))
                mainBox.Value = template.MainStatValue;

            // 设置副属性
            if (!string.IsNullOrEmpty(template.SubStat1) && _numBoxes.TryGetValue(template.SubStat1, out var sub1Box))
                sub1Box.Value = template.SubStat1Value;
            if (!string.IsNullOrEmpty(template.SubStat2) && _numBoxes.TryGetValue(template.SubStat2, out var sub2Box))
                sub2Box.Value = template.SubStat2Value;

            // 设置其他属性范围
            SetField("March", random.Next(template.MarchMin, template.MarchMax + 1));
            SetField("Hp", random.Next(template.HpMin, template.HpMax + 1));

            // 设置技能（从技能池中随机选择）
            if (template.SkillPool.Count > 0)
            {
                var shuffled = template.SkillPool.OrderBy(_ => random.Next()).ToList();
                int skillCount = Math.Min(template.SkillCount, shuffled.Count);
                for (int i = 0; i < skillCount; i++)
                {
                    var key = $"Skill{i}";
                    if (_numBoxes.TryGetValue(key, out var box))
                    {
                        int baseId = (shuffled[i] / 10) * 10;
                        int level = random.Next(template.SkillLevelMin, template.SkillLevelMax + 1);
                        box.Value = baseId + level;
                    }
                }
                // 清空剩余技能槽
                for (int i = skillCount; i < 5; i++)
                {
                    var key = $"Skill{i}";
                    if (_numBoxes.TryGetValue(key, out var box))
                        box.Value = 0;
                }
            }
        }
        finally
        {
            _loadingUi = false;
        }

        ApplyProp();
        SetStatus($"已应用兵种专长模板: {template.Name}");
    }

    private void SetField(string key, int value)
    {
        if (_numBoxes.TryGetValue(key, out var box))
            box.Value = value;
    }
}

// ===================================== 子控件：数字输入 =====================================
public class NumericUpDown : UserControl
{
    private readonly TextBox _textBox;
    private double _value;
    private bool _updating;

    public double MinValue { get; set; } = 0;
    public double MaxValue { get; set; } = 100;
    public double Increment { get; set; } = 1;

    public event EventHandler<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, MinValue, MaxValue);
            if (Math.Abs(v - _value) < 1e-12) return;
            _value = v;
            UpdateTextBox();
            ValueChanged?.Invoke(this, _value);
        }
    }

    public NumericUpDown()
    {
        Focusable = false;
        Height = 28;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _textBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1, 1, 0, 1),
            CaretBrush = Brushes.White,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _textBox.TextChanged += TextBox_TextChanged;
        _textBox.LostKeyboardFocus += (_, _) => UpdateTextBox();

        var rightPanel = new StackPanel { Orientation = Orientation.Vertical };
        var up = MakeBtn("▲", (_, _) => Value += Increment);
        var down = MakeBtn("▼", (_, _) => Value -= Increment);
        up.Height = 14;
        down.Height = 14;
        rightPanel.Children.Add(up);
        rightPanel.Children.Add(down);

        Grid.SetColumn(_textBox, 0);
        Grid.SetColumn(rightPanel, 1);
        g.Children.Add(_textBox);
        g.Children.Add(rightPanel);
        Content = g;
        Loaded += (_, _) => UpdateTextBox();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (double.TryParse(_textBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            var old = _value;
            _value = Math.Clamp(v, MinValue, MaxValue);
            if (Math.Abs(old - _value) > 1e-12) ValueChanged?.Invoke(this, _value);
        }
    }

    private void UpdateTextBox()
    {
        _updating = true;
        string format = Math.Abs(Increment - Math.Truncate(Increment)) < 1e-12 ? "F0" : "0.0##";
        _textBox.Text = _value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        _updating = false;
    }

    private static Button MakeBtn(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Foreground = Brushes.White,
            FontSize = 9,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Width = 18
        };
        b.Click += click;
        return b;
    }
}