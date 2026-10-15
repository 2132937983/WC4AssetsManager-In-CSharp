using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Views.Assist;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

/// <summary>
/// 布局编辑器场景（还原 source/layout_布局编辑器.html）：
/// 宽松解析布局 XML → 复刻 WC4 布局引擎求解 → Skia 画布可视化；
/// 左侧表单列表、右侧控件树 + 属性面板 + 精灵图库，支持拖拽移动/手柄缩放/属性编辑/导入导出。
/// </summary>
public partial class LayoutEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly LayoutDocument _doc = new();
    private readonly LayoutEngine.Context _ctx = new();

    // ---------- 画布状态 ----------
    private double _worldW = 1280, _worldH = 720;
    private double _zoom = 1;
    private double _panX, _panY;
    private bool _autoFit = true;

    // ---------- 显示开关 ----------
    private bool _showBox = true;
    private bool _ghost = true;
    private bool _showMissing = true;
    private bool _showGrid;
    private bool _showMargin = true;

    // ---------- 选择 ----------
    private LayoutNode? _form;
    private LayoutNode? _selected;
    private LayoutNode? _hover;
    private readonly List<(LayoutNode Node, double X, double Y, double W, double H)> _rects = new();

    private sealed class DragState
    {
        public string Type = "move";      // move / resize
        public string Handle = "";
        public double Sx, Sy, Sw, Sh;
        public LayoutNode? Node;
    }

    private DragState? _drag;
    private Point _mouseScreen;
    private bool _mouseInCanvas;
    private bool _dragOver;

    // ---------- 控件 ----------
    private SKElement _canvas = null!;
    private ListBox _formList = null!;
    private TextBox _formSearch = null!;
    private TextBlock _formCount = null!;
    private TextBlock _statusText = null!;
    private TextBlock _assetStat = null!;
    private StackPanel _treePanel = null!;
    private ScrollViewer _propsScroll = null!;
    private StackPanel _propsPanel = null!;
    private UIElement _spritesPane = null!;
    private Border _propsHost = null!;
    private TextBox _canvasWBox = null!;
    private TextBox _canvasHBox = null!;
    private ComboBox _layoutCombo = null!;
    private Button _btnAddChild = null!;
    private Button _btnDup = null!;
    private Button _btnDel = null!;

    private readonly List<(string Name, string Text)> _layouts = new();

    private DispatcherTimer? _formSearchDebounce;
    private DispatcherTimer? _spSearchDebounce;

    /// <summary>网格吸附步长（与可视网格的次格一致）；拖动时默认吸附，按住 Alt 取消。</summary>
    private const double SnapStep = 20;

    private static readonly HashSet<string> ImageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "texture", "res", "normal", "back", "normalimage", "backimage",
        "pressedimage", "disabledimage", "checkedimage", "uncheckedimage", "barimage", "thumbimage"
    };

    public LayoutEditScene(MainWindow window)
    {
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _ctx.ImageSize = r =>
        {
            var rec = LayoutImageProvider.Resolve(r);
            return rec is { Ready: true } ? (rec.W, rec.H) : null;
        };
        _ctx.MeasureText = MeasureTextSkia;
        _ctx.StringLookup = key =>
        {
            var table = AssetManager.Default.GetStringTable("tw");
            return table.TryGetValue(key, out var v) ? v : null;
        };
        _ctx.FontSizeLookup = name => LayoutFontProvider.GetFontMeta(name).Size;

        BuildUI();
        DiscoverLayouts();
        LoadPreferredLayout();

        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _assetStat.Text = $"图集 {LayoutImageProvider.AtlasNames.Count}｜字体 {LayoutFontProvider.BmFontCount}";
            FitZoom();
            Repaint();
        }, DispatcherPriority.Loaded);
    }

    // ============================================================= UI =============================================================
    private void BuildUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        root.Children.Add(BuildToolbar());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        Grid.SetRow(body, 1);

        // 左：表单列表
        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new TextBlock
        {
            Text = "表单列表",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xB9)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(9, 6, 9, 4)
        };
        var headRow = new StackPanel { Orientation = Orientation.Horizontal };
        headRow.Children.Add(head);
        _formCount = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(4, 6, 0, 4) };
        headRow.Children.Add(_formCount);
        Grid.SetRow(headRow, 0);
        leftGrid.Children.Add(headRow);

        var listStack = new StackPanel();
        _formSearch = MakeTextBox("搜索 id…");
        _formSearch.Margin = new Thickness(6, 2, 6, 4);
        _formSearch.TextChanged += (_, _) =>
        {
            _formSearchDebounce?.Stop();
            _formSearchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => RefreshFormList(), Dispatcher.CurrentDispatcher);
            _formSearchDebounce.Start();
        };
        listStack.Children.Add(_formSearch);
        _formList = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12
        };
        _formList.SelectionChanged += (_, _) =>
        {
            if (_formList.SelectedItem is FormEntry fe) SelectForm(fe.Node);
        };
        listStack.Children.Add(_formList);
        Grid.SetRow(listStack, 1);
        leftGrid.Children.Add(listStack);
        left.Child = leftGrid;
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var splitLeft = MakeSplitter(1);
        Grid.SetColumn(splitLeft, 1);
        body.Children.Add(splitLeft);

        // 中：画布
        var center = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
            ClipToBounds = true
        };
        _canvas = new SKElement { IgnorePixelScaling = false };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseLeftButtonDown += Canvas_MouseLeftDown;
        _canvas.MouseLeftButtonUp += Canvas_MouseLeftUp;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeave += (_, _) => { _hover = null; _mouseInCanvas = false; Repaint(); };
        _canvas.MouseRightButtonDown += Canvas_MouseRightDown;
        _canvas.MouseRightButtonUp += Canvas_MouseRightUp;
        _canvas.MouseWheel += Canvas_MouseWheel;
        _canvas.AllowDrop = true;
        _canvas.DragEnter += (_, e) => { _dragOver = true; e.Effects = DragDropEffects.Copy; e.Handled = true; Repaint(); };
        _canvas.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        _canvas.DragLeave += (_, _) => { _dragOver = false; Repaint(); };
        _canvas.Drop += Canvas_Drop;
        _canvas.SizeChanged += (_, _) => { if (_autoFit) FitZoom(); };
        center.Child = _canvas;
        Grid.SetColumn(center, 2);
        body.Children.Add(center);

        var splitRight = MakeSplitter(3);
        Grid.SetColumn(splitRight, 3);
        body.Children.Add(splitRight);

        // 右：控件树 + 属性/精灵图库
        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)) };
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var treeHead = new TextBlock
        {
            Text = "控件树",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xB9)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(9, 6, 9, 4)
        };
        Grid.SetRow(treeHead, 0);
        var treeGrid = new Grid();
        treeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        treeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        treeGrid.Children.Add(treeHead);
        _treePanel = new StackPanel();
        var treeScroll = new ScrollViewer { Content = _treePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(treeScroll, 1);
        treeGrid.Children.Add(treeScroll);
        Grid.SetRow(treeGrid, 0);
        rightGrid.Children.Add(treeGrid);

        // 页签
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)) };
        var tabProps = MakeTab("属性", true);
        var tabSprites = MakeTab("精灵图库", false);
        tabs.Children.Add(tabProps);
        tabs.Children.Add(tabSprites);
        Grid.SetRow(tabs, 1);
        rightGrid.Children.Add(tabs);

        // 属性页
        _propsPanel = new StackPanel { Margin = new Thickness(6, 4, 6, 20) };
        _propsScroll = new ScrollViewer
        {
            Content = _propsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _propsHost = new Border { Child = _propsScroll };
        Grid.SetRow(_propsHost, 2);
        rightGrid.Children.Add(_propsHost);

        // 节点操作
        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
        };
        _btnAddChild = MakeSmallBtn("添加子控件", (_, _) => OnAddChild());
        _btnDup = MakeSmallBtn("复制", (_, _) => OnDuplicate());
        _btnDel = MakeSmallBtn("删除", (_, _) => OnDeleteNode());
        ops.Children.Add(_btnAddChild);
        ops.Children.Add(_btnDup);
        ops.Children.Add(_btnDel);
        Grid.SetRow(ops, 3);
        rightGrid.Children.Add(ops);

        _spritesPane = BuildSpritesPane();

        right.Child = rightGrid;
        Grid.SetColumn(right, 4);
        body.Children.Add(right);

        tabProps.Click += (_, _) =>
        {
            _propsHost.Child = _propsScroll;
            tabProps.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x28));
            tabSprites.Background = Brushes.Transparent;
        };
        tabSprites.Click += (_, _) =>
        {
            _propsHost.Child = _spritesPane;
            tabSprites.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x28));
            tabProps.Background = Brushes.Transparent;
            RefreshSprites();
        };

        root.Children.Add(body);

        _statusText = new TextBlock
        {
            Foreground = Brushes.LightGray,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var statusBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Child = _statusText
        };
        Grid.SetRow(statusBorder, 2);
        root.Children.Add(statusBorder);

        Content = root;
    }

    private UIElement BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(8, 5, 8, 5), Orientation = Orientation.Horizontal };
        bar.Children.Add(MakeTitle("WC4 布局编辑器"));
        bar.Children.Add(MakeSep());

        bar.Children.Add(MakeBtn("返回", (_, _) => _window.ReturnToMainScene()));
        bar.Children.Add(MakeBtn("资源文件夹", OnImportResourceFolder));
        bar.Children.Add(MakeBtn("＋ 文件", OnImportResourceFiles));
        _assetStat = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xE0, 0xC2)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        bar.Children.Add(_assetStat);
        bar.Children.Add(MakeBtn("导入布局 XML", OnImportXml));
        _layoutCombo = new ComboBox
        {
            Width = 160,
            FontSize = 12,
            Margin = new Thickness(3, 0, 3, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White
        };
        _layoutCombo.SelectionChanged += (_, _) =>
        {
            int i = _layoutCombo.SelectedIndex;
            if (i >= 0 && i < _layouts.Count) LoadLayoutText(_layouts[i].Text, _layouts[i].Name);
        };
        bar.Children.Add(_layoutCombo);
        bar.Children.Add(MakeBtn("导出 XML", OnExportXml));
        bar.Children.Add(MakeBtn("导出 PNG", OnExportPng));

        bar.Children.Add(MakeSep());
        bar.Children.Add(MakeSmallLabel("画布"));
        _canvasWBox = MakeNumberBox("1280", 70);
        _canvasWBox.LostFocus += (_, _) => CommitCanvasSize();
        _canvasHBox = MakeNumberBox("720", 70);
        _canvasHBox.LostFocus += (_, _) => CommitCanvasSize();
        bar.Children.Add(_canvasWBox);
        bar.Children.Add(MakeSmallLabel("×"));
        bar.Children.Add(_canvasHBox);
        var preset = new ComboBox { Width = 130, FontSize = 12, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)), Foreground = Brushes.White };
        preset.Items.Add("预设…");
        preset.Items.Add("HD 1280×720");
        preset.Items.Add("SD 1136×640");
        preset.Items.Add("Pad 2048×1536");
        preset.Items.Add("真机 2712×1220");
        preset.SelectedIndex = 0;
        preset.SelectionChanged += (_, _) =>
        {
            int idx = preset.SelectedIndex;
            if (idx <= 0) return;
            (int w, int h) = idx switch { 1 => (1280, 720), 2 => (1136, 640), 3 => (2048, 1536), _ => (2712, 1220) };
            _canvasWBox.Text = w.ToString();
            _canvasHBox.Text = h.ToString();
            CommitCanvasSize();
            preset.SelectedIndex = 0;
        };
        bar.Children.Add(preset);

        bar.Children.Add(MakeSep());
        bar.Children.Add(MakeSmallLabel("缩放"));
        var zoomCombo = new ComboBox { Width = 88, FontSize = 12, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)), Foreground = Brushes.White };
        foreach (var s in new[] { "适应", "25%", "50%", "75%", "100%", "150%", "200%" }) zoomCombo.Items.Add(s);
        zoomCombo.SelectedIndex = 0;
        zoomCombo.SelectionChanged += (_, _) =>
        {
            int i = zoomCombo.SelectedIndex;
            if (i <= 0) { _autoFit = true; FitZoom(); }
            else { _autoFit = false; _zoom = i switch { 1 => 0.25, 2 => 0.5, 3 => 0.75, 4 => 1.0, 5 => 1.5, _ => 2.0 }; Repaint(); }
        };
        bar.Children.Add(zoomCombo);

        bar.Children.Add(MakeSep());
        bar.Children.Add(MakeCheck("边框", _showBox, v => { _showBox = v; Repaint(); }));
        bar.Children.Add(MakeCheck("隐藏控件", _ghost, v => { _ghost = v; Repaint(); }));
        bar.Children.Add(MakeCheck("缺图", _showMissing, v => { _showMissing = v; Repaint(); }));
        bar.Children.Add(MakeCheck("网格", _showGrid, v => { _showGrid = v; Repaint(); }));
        bar.Children.Add(MakeCheck("边距", _showMargin, v => { _showMargin = v; Repaint(); }));
        bar.Children.Add(MakeSmallLabel("吸附 20px（按住 Alt 取消）"));

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar
        };
        Grid.SetRow(border, 0);
        return border;
    }

    private static GridSplitter MakeSplitter(int column) => new()
    {
        Width = 4,
        Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Tag = column
    };

    private static Button MakeTab(string text, bool on)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 5, 14, 5),
            Cursor = Cursors.Hand,
            Background = on ? new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x28)) : Brushes.Transparent
        };
        return b;
    }

    private static TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xB9)),
        FontWeight = FontWeights.Bold,
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 6, 0)
    };

    private static TextBlock MakeSmallLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(3, 0, 2, 0)
    };

    private static Border MakeSep() => new()
    {
        Width = 1,
        Height = 22,
        Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button MakeBtn(string text, RoutedEventHandler handler)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(3, 0, 3, 0),
            Cursor = Cursors.Hand
        };
        b.Click += handler;
        return b;
    }

    private static Button MakeSmallBtn(string text, RoutedEventHandler handler)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand
        };
        b.Click += handler;
        return b;
    }

    private static TextBox MakeTextBox(string placeholder)
    {
        var tb = new TextBox
        {
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            CaretBrush = Brushes.White,
            Tag = placeholder
        };
        return tb;
    }

    private static TextBox MakeNumberBox(string def, double width) => new()
    {
        Text = def,
        Width = width,
        FontSize = 12,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(4, 2, 4, 2),
        Margin = new Thickness(2, 0, 2, 0)
    };

    private static CheckBox MakeCheck(string label, bool initial, Action<bool> changed)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = initial,
            Foreground = Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0)
        };
        cb.Checked += (_, _) => changed(true);
        cb.Unchecked += (_, _) => changed(false);
        return cb;
    }

    // ============================================================= 文档加载 =============================================================
    private void DiscoverLayouts()
    {
        _layouts.Clear();
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
            foreach (var entry in manager.ListByExtension(".xml"))
            {
                var name = entry.RelativePath.Replace('\\', '/');
                if (name.Contains('/')) continue;
                if (!name.StartsWith("layout", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var text = manager.ReadText(entry);
                    if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                        _layouts.Add((name, text));
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayoutEditScene] 扫描布局失败: {ex.Message}");
        }

        _layoutCombo.Items.Clear();
        foreach (var l in _layouts) _layoutCombo.Items.Add(l.Name);
    }

    private void LoadPreferredLayout()
    {
        var preferred = _layouts.FirstOrDefault(l => l.Name.Equals("layout_x.xml", StringComparison.OrdinalIgnoreCase));
        if (preferred.Name == null) preferred = _layouts.FirstOrDefault(l => l.Name.Equals("layout.xml", StringComparison.OrdinalIgnoreCase));
        if (preferred.Name == null && _layouts.Count > 0) preferred = _layouts[0];

        if (preferred.Name != null)
        {
            LoadLayoutText(preferred.Text, preferred.Name);
            _layoutCombo.SelectedIndex = _layouts.IndexOf(preferred);
        }
        else
        {
            SetStatus("未在 assets 中发现布局文件（可手动导入 layout XML）");
            RefreshFormList();
            RefreshTree();
            RenderProps();
        }
    }

    private void LoadLayoutText(string text, string name)
    {
        try
        {
            _doc.Load(text, name);
            _doc.BuildExpansion();
            _form = _doc.Forms.FirstOrDefault(f => f.Get("id") == "form_main") ?? _doc.Forms.FirstOrDefault();
            _selected = null;
            _hover = null;
            RefreshFormList();
            RefreshTree();
            RenderProps();
            Repaint();
            SetStatus($"{name} ｜ 布局已加载，{_doc.Forms.Count} 个表单");
        }
        catch (Exception ex)
        {
            SetStatus($"布局解析失败：{ex.Message}");
        }
    }

    private void OnImportXml(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "布局 XML|*.xml|所有文件|*.*", Title = "导入布局 XML" };
        if (dlg.ShowDialog() != true) return;
        var text = File.ReadAllText(dlg.FileName);
        var name = IOPath.GetFileName(dlg.FileName);
        if (!_layouts.Any(l => l.Name == name)) _layouts.Add((name, text));
        _layoutCombo.Items.Clear();
        foreach (var l in _layouts) _layoutCombo.Items.Add(l.Name);
        LoadLayoutText(text, name);
    }

    private void OnImportResourceFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择资源文件夹（含图集 xml / 图片 / 字体）" };
        if (dlg.ShowDialog() != true) return;
        IngestResources([dlg.FolderName]);
    }

    private void OnImportResourceFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "资源文件|*.xml;*.png;*.webp;*.jpg;*.jpeg;*.fnt|所有文件|*.*",
            Title = "选择资源文件（可多选）"
        };
        if (dlg.ShowDialog() != true) return;
        IngestResources(dlg.FileNames);
    }

    private void IngestResources(string[] paths)
    {
        var layouts = paths.Length == 1 && Directory.Exists(paths[0])
            ? LayoutImageProvider.IngestFolder(paths[0])
            : LayoutImageProvider.IngestFiles(paths);

        foreach (var (name, text) in layouts)
        {
            if (!_layouts.Any(l => l.Name == name)) _layouts.Add((name, text));
        }
        _layoutCombo.Items.Clear();
        foreach (var l in _layouts) _layoutCombo.Items.Add(l.Name);

        var nAtlas = LayoutImageProvider.ExtraAtlasCount;
        var nImg = LayoutImageProvider.ExtraImageCount;
        var nFont = LayoutFontProvider.BmFontCount;
        _assetStat.Text = $"图集 {nAtlas}｜图片 {nImg}｜字体 {nFont}";
        SetStatus($"资源已载入：额外 {nAtlas} 个图集 / {nImg} 个图片文件" +
                  (layouts.Count > 0 ? $" / {layouts.Count} 个布局文件" : ""));

        if (_doc.Forms.Count == 0 && _layouts.Count > 0)
        {
            var pref = _layouts.FirstOrDefault(l => l.Name.Equals("layout_x.xml", StringComparison.OrdinalIgnoreCase));
            if (pref.Name == null) pref = _layouts.FirstOrDefault(l => l.Name.Equals("layout.xml", StringComparison.OrdinalIgnoreCase));
            if (pref.Name == null) pref = _layouts[0];
            LoadLayoutText(pref.Text, pref.Name);
            _layoutCombo.SelectedIndex = _layouts.IndexOf(pref);
        }
        else
            Repaint();
    }

    private void OnExportXml(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "布局 XML|*.xml",
            FileName = _doc.SourceName ?? "layout.xml",
            Title = "导出布局 XML"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _doc.Dump(), new System.Text.UTF8Encoding(false));
            SetStatus($"已导出 {IOPath.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            FileName = (_form?.Get("id") ?? "layout") + ".png",
            Title = "导出画布 PNG"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int w = (int)Math.Max(1, Math.Round(_worldW));
            int h = (int)Math.Max(1, Math.Round(_worldH));
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0F, 0x14, 0x19));
            DrawScene(canvas, overlay: true);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data != null) File.WriteAllBytes(dlg.FileName, data.ToArray());
            SetStatus($"已导出 {IOPath.GetFileName(dlg.FileName)}（{w}×{h}）");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommitCanvasSize()
    {
        if (int.TryParse(_canvasWBox.Text, out var w) && w > 0) _worldW = w;
        if (int.TryParse(_canvasHBox.Text, out var h) && h > 0) _worldH = h;
        if (_autoFit) FitZoom();
        Repaint();
    }

    // ============================================================= 表单列表 =============================================================
    private sealed class FormEntry
    {
        public LayoutNode Node { get; init; } = null!;
        public override string ToString()
            => $"[{TagCn(Node.Tag)}] {Node.Get("id")}";
    }

    private void RefreshFormList()
    {
        var q = (_formSearch.Text ?? "").Trim().ToLowerInvariant();
        _formList.Items.Clear();
        foreach (var f in _doc.Forms)
        {
            var id = f.Get("id") ?? "";
            if (q.Length > 0 && !id.ToLowerInvariant().Contains(q)) continue;
            var item = new FormEntry { Node = f };
            _formList.Items.Add(item);
            if (ReferenceEquals(f, _form)) _formList.SelectedItem = item;
        }
        _formCount.Text = _doc.Forms.Count.ToString();
    }

    private void SelectForm(LayoutNode form)
    {
        _form = form;
        _selected = null;
        _hover = null;
        _doc.BuildExpansion(form);
        _autoFit = true;
        FitZoom();
        RefreshTree();
        RenderProps();
        Repaint();
    }

    // ============================================================= 选择 =============================================================
    private void SelectNode(LayoutNode? node)
    {
        _selected = node;
        RefreshTree();
        RenderProps();
        Repaint();
    }

    private static LayoutNode? OwnerOf(LayoutNode? n)
    {
        while (n is { IsVirtual: true }) n = n.Owner;
        return n;
    }

    private void Repaint() => _canvas?.InvalidateVisual();

    // ============================================================= 画布变换 =============================================================
    private void FitZoom()
    {
        double w = Math.Max(50, _canvas.ActualWidth - 40);
        double h = Math.Max(50, _canvas.ActualHeight - 40);
        _zoom = Math.Min(w / _worldW, h / _worldH);
        UpdatePanToCenter();
    }

    private void UpdatePanToCenter()
    {
        double cw = _canvas.ActualWidth, chh = _canvas.ActualHeight;
        _panX = Math.Max(24, (cw - _worldW * _zoom) / 2);
        _panY = Math.Max(24, (chh - _worldH * _zoom) / 2);
    }

    private (double x, double y) ScreenToWorld(double sx, double sy) => ((sx - _panX) / _zoom, (sy - _panY) / _zoom);

    /// <summary>把画布平移到以节点中心为画布中心（B12 centerOnNode）。</summary>
    private void CenterOnNode(LayoutNode n)
    {
        if (!n.HasRect) return;
        _autoFit = false;
        double cx = n.X + n.W / 2, cy = n.Y + n.H / 2;
        _panX = _canvas.ActualWidth / 2 - cx * _zoom;
        _panY = _canvas.ActualHeight / 2 - cy * _zoom;
        Repaint();
    }

    // ============================================================= 渲染 =============================================================
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x12, 0x12, 0x12));
        canvas.Save();
        canvas.Translate((float)_panX, (float)_panY);
        canvas.Scale((float)_zoom);
        DrawScene(canvas, overlay: true);
        canvas.Restore();
    }

    /// <summary>绘制网格 + 布局内容 + 覆盖层（overlay=false 时用于导出纯内容）。</summary>
    private void DrawScene(SKCanvas canvas, bool overlay)
    {
        if (_showGrid) DrawGrid(canvas);

        _rects.Clear();
        if (_form == null) return;

        double rw = _form.Num("width");
        double rh = _form.Num("height");
        if (double.IsNaN(rw)) rw = _worldW;
        if (double.IsNaN(rh)) rh = _worldH;

        int ha = AlignOf(AlignHMap, _form.Get("halign"), _form.Tag == "TmpWindow");
        int va = AlignOf(AlignVMap, _form.Get("valign"), _form.Tag == "TmpWindow");
        double rx = ha == 0 ? 0 : ha == 2 ? _worldW - rw : (_worldW - rw) / 2;
        double ry = va == 0 ? 0 : va == 2 ? _worldH - rh : (_worldH - rh) / 2;

        _form.X = rx; _form.Y = ry; _form.W = rw; _form.H = rh; _form.HasRect = true;

        foreach (var c in _form.Children) LayoutEngine.Layout(c, rx, ry, rw, rh, null, _ctx);

        DrawNode(canvas, _form, 0, 1.0, overlay);
        if (overlay) DrawOverlay(canvas);

        UpdateGeoCard();
        SetStatus($"{(_form.Get("id") ?? "")}  {_worldW:0}×{_worldH:0}  控件数:{_rects.Count}");
    }

    private static readonly Dictionary<string, int> AlignHMap = new(StringComparer.OrdinalIgnoreCase)
    { ["left"] = 0, ["center"] = 1, ["right"] = 2 };
    private static readonly Dictionary<string, int> AlignVMap = new(StringComparer.OrdinalIgnoreCase)
    { ["top"] = 0, ["center"] = 1, ["bottom"] = 2 };

    private static int AlignOf(Dictionary<string, int> map, string? v, bool defaultCenter)
    {
        if (v != null && map.TryGetValue(v, out var i)) return i;
        return defaultCenter ? 1 : 0;
    }

    private void DrawGrid(SKCanvas canvas)
    {
        const int minor = 20, major = 100;
        using var thin = new SKPaint { Color = new SKColor(120, 150, 180, 33), StrokeWidth = 1 };
        using var thick = new SKPaint { Color = new SKColor(120, 170, 220, 76), StrokeWidth = 1 };
        for (int x = minor; x < _worldW; x += minor)
            canvas.DrawLine(x, 0, x, (float)_worldH, x % major == 0 ? thick : thin);
        for (int y = minor; y < _worldH; y += minor)
            canvas.DrawLine(0, y, (float)_worldW, y, y % major == 0 ? thick : thin);

        using var lblFont = new SKFont(Typeface, 9);
        using var lblPaint = new SKPaint { Color = new SKColor(150, 190, 230, 115), IsAntialias = true };
        for (int x = major; x < _worldW; x += major)
            canvas.DrawText(x.ToString(), x + 2, 10, SKTextAlign.Left, lblFont, lblPaint);
        for (int y = major; y < _worldH; y += major)
            canvas.DrawText(y.ToString(), 2, y + 10, SKTextAlign.Left, lblFont, lblPaint);
    }

    private static byte A(double alpha) => (byte)Math.Clamp(alpha * 255, 0, 255);

    private void DrawNode(SKCanvas canvas, LayoutNode n, int depth, double parentAlpha, bool overlay)
    {
        if (!n.HasRect) return;
        double alpha = parentAlpha;
        var av = n.Num("alpha");
        if (!double.IsNaN(av)) alpha *= Math.Clamp(av, 0, 1);
        bool invisible = LayoutDocument.IsFalseValue(n.Get("visible") ?? "true") && n.Get("visible") != null;
        if (invisible) alpha = _ghost ? parentAlpha * 0.18 : 0;

        _rects.Add((n, n.X, n.Y, n.W, n.H));

        if (n.IsCell)
        {
            foreach (var c in n.Children) DrawNode(canvas, c, depth + 1, alpha, overlay);
            return;
        }

        bool clipOn = (n.Get("clipped") == "true" || n.Tag == "ScrollViewer") && n.W > 0 && n.H > 0;
        if (clipOn)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W), (float)(n.Y + n.H)));
        }

        bool drawn = false;
        var ir = LayoutDocument.ImageRefOf(n);
        if (ir != null)
        {
            var rec = LayoutImageProvider.Resolve(ir.Value.Ref);
            if (rec is { Ready: true })
            {
                int dm = MapDrawMode(n.Get("drawmode"));
                var tint = ParseColor(n.Get("color"));
                canvas.Save();
                canvas.Translate((float)n.X, (float)n.Y);
                if (tint is { } tc)
                {
                    using var layerPaint = new SKPaint();
                    canvas.SaveLayer(layerPaint);
                    DrawImage9(canvas, rec, dm, n.W, n.H, A(alpha));
                    using var tintPaint = new SKPaint { Color = tc.WithAlpha(A(alpha)), BlendMode = SKBlendMode.Multiply };
                    canvas.DrawRect(new SKRect(0, 0, (float)n.W, (float)n.H), tintPaint);
                    canvas.Restore();
                }
                else
                {
                    DrawImage9(canvas, rec, dm, n.W, n.H, A(alpha));
                }
                canvas.Restore();
                drawn = true;
            }
        }

        var ex = LayoutDocument.ExpandedKids(n);
        bool runtimeContainer = ir == null && n.Children.Count > 0;
        if (!drawn && !ReferenceEquals(n, _form) && (ex == null || ex.Count == 0) && !runtimeContainer
            && (LayoutDocument.IsContainer(n) || ir == null))
        {
            bool isPh = n.Tag is "SlideList" or "ScrollViewer" or "Template";
            if (isPh || (_showBox && n.W > 1 && n.H > 1))
            {
                using var paint = new SKPaint
                {
                    IsStroke = true,
                    StrokeWidth = 1,
                    Color = isPh ? new SKColor(126, 209, 185, A(alpha * 0.9)) : new SKColor(126, 209, 185, A(alpha * 0.35)),
                    PathEffect = SKPathEffect.CreateDash(isPh ? new[] { 6f, 4f } : new[] { 3f, 3f }, 0)
                };
                canvas.DrawRect(new SKRect((float)(n.X + 0.5), (float)(n.Y + 0.5), (float)(n.X + n.W - 0.5), (float)(n.Y + n.H - 0.5)), paint);
                if (isPh)
                {
                    string label = n.Tag + (n.Get("id") != null ? " #" + n.Get("id") : "");
                    DrawPill(canvas, label, (float)n.X, (float)n.Y, new SKColor(126, 209, 185, A(alpha)));
                }
            }
        }

        if (!drawn && ir != null && _showMissing)
        {
            using var paint = new SKPaint { Color = new SKColor(231, 76, 60, A(alpha * 0.75)), IsAntialias = true };
            using var font = new SKFont(Typeface, 9);
            canvas.DrawText("? " + ir.Value.Ref, (float)(n.X + 2), (float)(n.Y + 10), SKTextAlign.Left, font, paint);
        }

        if (n.Tag == "PlaceHolder" && n.Get("templateid") is { } tplId)
        {
            using var paint = new SKPaint { Color = new SKColor(126, 209, 185, A(alpha * 0.8)), IsAntialias = true };
            using var font = new SKFont(Typeface, 9);
            canvas.DrawText("\u2302 " + tplId, (float)(n.X + 2), (float)(n.Y + 10), SKTextAlign.Left, font, paint);
        }

        if (n.Tag is "Label" or "TextBox" or "EditBox" || (n.Tag == "Button" && !LayoutDocument.RealKids(n).Any(c => c.Tag == "Label")))
        {
            if (LayoutEngine.TextOf(n, _ctx).Length > 0) DrawLabelText(canvas, n, alpha);
        }

        if (_showBox && !LayoutDocument.IsContainer(n) && n.W > 1 && n.H > 1)
        {
            using var paint = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(90, 170, 255, A(alpha * 0.25)) };
            canvas.DrawRect(new SKRect((float)(n.X + 0.5), (float)(n.Y + 0.5), (float)(n.X + n.W - 0.5), (float)(n.Y + n.H - 0.5)), paint);
        }

        var list = ex ?? n.Children;
        foreach (var c in list) DrawNode(canvas, c, depth + 1, alpha, overlay);

        if (clipOn) canvas.Restore();
    }

    private static int MapDrawMode(string? dm) => dm switch
    {
        "hextend" => 1,
        "vextend" => 2,
        "extend" => 3,
        "stretch" => 4,
        "tile" => 5,
        _ => 0
    };

    /// <summary>对应 HTML 的 drawImage9：drawmode 决定贴图如何填满控件矩形。</summary>
    private static void DrawImage9(SKCanvas canvas, LayoutImageRec rec, int drawmode, double w, double h, byte alpha)
    {
        if (!rec.Ready || rec.Page == null) return;
        var src = rec.SrcRect;
        double sw = rec.W, sh = rec.H;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };

        switch (drawmode)
        {
            case 5: // tile
                for (double yy = 0; yy < h; yy += sh)
                    for (double xx = 0; xx < w; xx += sw)
                        canvas.DrawBitmap(rec.Page, src, new SKRect((float)xx, (float)yy, (float)(xx + sw), (float)(yy + sh)), paint);
                return;
            case 0: // natural：纹理自然像素 1:1 居中
                {
                    double dx = (w - sw) / 2, dy = (h - sh) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect((float)dx, (float)dy, (float)(dx + sw), (float)(dy + sh)), paint);
                    return;
                }
            case 1: // hextend
                {
                    double dh = sh * Math.Min(1, h / Math.Max(1, sh));
                    double dy = (h - dh) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect(0, (float)dy, (float)w, (float)(dy + dh)), paint);
                    return;
                }
            case 2: // vextend
                {
                    double dw = sw * Math.Min(1, w / Math.Max(1, sw));
                    double dx = (w - dw) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect((float)dx, 0, (float)(dx + dw), (float)h), paint);
                    return;
                }
            default: // extend / stretch
                canvas.DrawBitmap(rec.Page, src, new SKRect(0, 0, (float)w, (float)h), paint);
                return;
        }
    }

    private void DrawLabelText(SKCanvas canvas, LayoutNode n, double alpha)
    {
        var text = LayoutEngine.TextOf(n, _ctx);
        if (text.Length == 0) return;
        var fontName = n.Get("font") ?? "font_text_4";
        double fontSize = LayoutEngine.FontSizeOf(n, _ctx);
        var color = ParseColor(n.Get("color")) ?? new SKColor(255, 255, 255);
        var ha = n.Get("htextalign") ?? "left";
        var va = n.Get("vtextalign") ?? "center";

        canvas.Save();
        canvas.ClipRect(new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W), (float)(n.Y + n.H)));

        var bm = LayoutFontProvider.GetBmFont(fontName);
        if (bm is { Ready: true } bf)
        {
            double mW = 0;
            foreach (var ch in text) mW += bf.Glyphs.TryGetValue(ch, out var g) ? g.Advance : bf.LineHeight / 2.0;
            double px = ha switch { "center" => n.X + (n.W - mW) / 2, "right" => n.X + n.W - mW - 2, _ => n.X + 2 };
            double baseY = va switch { "top" => n.Y, "bottom" => n.Y + n.H - bf.LineHeight, _ => n.Y + (n.H - bf.LineHeight) / 2 };
            using var paint = new SKPaint { Color = color.WithAlpha(A(alpha)), IsAntialias = true };
            foreach (var ch in text)
            {
                if (!bf.Glyphs.TryGetValue(ch, out var g)) { px += bf.LineHeight / 2.0; continue; }
                if (bf.Page is { } page)
                {
                    var src = new SKRect(g.X, g.Y, g.X + g.W, g.Y + g.H);
                    var dst = new SKRect((float)(px + g.BearingX), (float)(baseY + g.BearingY), (float)(px + g.BearingX + g.W), (float)(baseY + g.BearingY + g.H));
                    canvas.DrawBitmap(page, src, dst, paint);
                }
                px += g.Advance;
            }
        }
        else
        {
            var meta = LayoutFontProvider.GetFontMeta(fontName);
            using var font = new SKFont(Typeface, (float)fontSize);
            using var paint = new SKPaint { Color = color.WithAlpha(A(alpha)), IsAntialias = true };
            float tw = font.MeasureText(text);
            float tx = ha switch { "center" => (float)n.X + (float)n.W / 2, "right" => (float)(n.X + n.W - 2), _ => (float)(n.X + 2) };
            float ty = va switch { "top" => (float)n.Y + (float)fontSize / 2 + 1, "bottom" => (float)(n.Y + n.H) - (float)fontSize / 2 - 1, _ => (float)(n.Y + n.H / 2) };
            var align = ha switch { "center" => SKTextAlign.Center, "right" => SKTextAlign.Right, _ => SKTextAlign.Left };
            if (meta.Outline > 0)
            {
                using var stroke = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, A(alpha * 0.85)),
                    IsAntialias = true,
                    StrokeWidth = (float)(meta.Outline * 2),
                    Style = SKPaintStyle.Stroke
                };
                canvas.DrawText(text, tx, ty, align, font, stroke);
            }
            canvas.DrawText(text, tx, ty, align, font, paint);
        }

        canvas.Restore();
    }

    private void DrawPill(SKCanvas canvas, string text, float x, float y, SKColor color)
    {
        using var font = new SKFont(Typeface, 11);
        float w = font.MeasureText(text) + 12;
        using var bg = new SKPaint { Color = color };
        canvas.DrawRect(x, y, w, 15, bg);
        using var fg = new SKPaint { Color = new SKColor(0x8B, 0xE0, 0xC2), IsAntialias = true };
        canvas.DrawText(text, x + 6, y + 11, SKTextAlign.Left, font, fg);
    }

    // ============================================================= 覆盖层 =============================================================
    private void DrawOverlay(SKCanvas canvas)
    {
        if (_hover is { } hv && !ReferenceEquals(hv, _selected) && hv.HasRect)
        {
            using var paint = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(255, 255, 255, 128) };
            canvas.DrawRect(new SKRect((float)(hv.X + 0.5), (float)(hv.Y + 0.5), (float)(hv.X + hv.W - 0.5), (float)(hv.Y + hv.H - 0.5)), paint);
            DrawPill(canvas, hv.ToString(), (float)hv.X, (float)(hv.Y - 17), new SKColor(70, 84, 98, 242));
        }

        if (_selected is { } sel && sel.HasRect)
        {
            if (_showMargin) DrawMarginGuides(canvas, sel);

            using var selPaint = new SKPaint { IsStroke = true, StrokeWidth = 1.5f, Color = new SKColor(56, 189, 248) };
            canvas.DrawRect(new SKRect((float)(sel.X + 0.5), (float)(sel.Y + 0.5), (float)(sel.X + sel.W - 0.5), (float)(sel.Y + sel.H - 0.5)), selPaint);
            DrawPill(canvas, sel.ToString(), (float)sel.X, (float)(sel.Y - 17), new SKColor(14, 116, 144, 242));

            // B2: W×H pill at bottom-right corner
            DrawPill(canvas, $"{Math.Round(sel.W)} × {Math.Round(sel.H)}", (float)(sel.X + sel.W), (float)(sel.Y + sel.H), new SKColor(14, 116, 144, 242));

            // B3: 四角延长线
            using var corner = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(56, 189, 248, 140) };
            const float cl = 10;
            float sx = (float)sel.X, sy = (float)sel.Y, ex = (float)(sel.X + sel.W), ey = (float)(sel.Y + sel.H);
            canvas.DrawLine(sx + cl, sy, sx, sy, corner); canvas.DrawLine(sx, sy, sx, sy + cl, corner);
            canvas.DrawLine(ex - cl, sy, ex, sy, corner); canvas.DrawLine(ex, sy, ex, sy + cl, corner);
            canvas.DrawLine(sx + cl, ey, sx, ey, corner); canvas.DrawLine(sx, ey, sx, ey - cl, corner);
            canvas.DrawLine(ex - cl, ey, ex, ey, corner); canvas.DrawLine(ex, ey, ex, ey - cl, corner);

            if (!sel.IsVirtual)
            {
                foreach (var (hx, hy) in Handles(sel))
                {
                    using var fill = new SKPaint { Color = new SKColor(0x2B, 0x8F, 0x70) };
                    using var edge = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = SKColors.White };
                    var rect = new SKRect((float)hx, (float)hy, (float)(hx + 9), (float)(hy + 9));
                    canvas.DrawRect(rect, fill);
                    canvas.DrawRect(rect, edge);
                }
            }
        }

        DrawCoordTip(canvas);
        DrawEmptyHint(canvas);
        DrawDropHint(canvas);
    }

    private void DrawEmptyHint(SKCanvas canvas)
    {
        if (_doc.Forms.Count > 0) return;
        using var font = new SKFont(Typeface, 16);
        using var small = new SKFont(Typeface, 12);
        using var paint = new SKPaint { Color = new SKColor(0x7F, 0xD1, 0xB9), IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(0x9A, 0x9A, 0x9A), IsAntialias = true };
        float cx = (float)(_worldW / 2), cy = (float)(_worldH / 2);
        canvas.DrawText("WC4 布局编辑器", cx, cy - 40, SKTextAlign.Center, font, paint);
        canvas.DrawText("1. 点工具栏「资源文件夹」选择 assets 文件夹（或拖到画布）", cx, cy - 8, SKTextAlign.Center, small, dim);
        canvas.DrawText("2. 点「导入布局 XML」选择 layout.xml", cx, cy + 14, SKTextAlign.Center, small, dim);
        canvas.DrawText("支持图集 xml / 散图 png·webp / 位图字体 fnt", cx, cy + 40, SKTextAlign.Center, small, dim);
    }

    private void DrawDropHint(SKCanvas canvas)
    {
        if (!_dragOver) return;
        using var paint = new SKPaint
        {
            IsStroke = true,
            StrokeWidth = 2,
            Color = new SKColor(0x7F, 0xD1, 0xB9, 200),
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 6f }, 0)
        };
        var rect = new SKRect(18, 18, (float)(_worldW - 18), (float)(_worldH - 18));
        canvas.DrawRect(rect, paint);
        using var font = new SKFont(Typeface, 22);
        using var fg = new SKPaint { Color = new SKColor(0x9F, 0xE6, 0xCF), IsAntialias = true };
        canvas.DrawText("松开以载入资源文件夹", (float)(_worldW / 2), (float)(_worldH / 2), SKTextAlign.Center, font, fg);
    }

    private void DrawCoordTip(SKCanvas canvas)
    {
        if (!_mouseInCanvas || _drag != null) return;
        var (wx, wy) = ScreenToWorld(_mouseScreen.X, _mouseScreen.Y);
        var q = HitTest(wx, wy);
        var parts = $"({Math.Round(wx)}, {Math.Round(wy)})";
        if (q.node is { } n)
        {
            parts += $"  {n}  {Math.Round(n.W)}×{Math.Round(n.H)}";
            if (n.IsVirtual) parts += " 〔模板展开〕";
        }
        using var font = new SKFont(Typeface, 11);
        float tw = font.MeasureText(parts);
        float tx = (float)(_mouseScreen.X + 14);
        float ty = (float)(_mouseScreen.Y + 14);
        var bgRect = new SKRect(tx - 4, ty - 2, tx + tw + 4, ty + 14);
        using var bg = new SKPaint { Color = new SKColor(13, 20, 27, 235) };
        canvas.DrawRect(bgRect, bg);
        using var border = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(56, 189, 248) };
        canvas.DrawRect(bgRect, border);
        using var fg = new SKPaint { Color = new SKColor(0xCF, 0xE8, 0xFF), IsAntialias = true };
        canvas.DrawText(parts, tx, ty + 11, SKTextAlign.Left, font, fg);
    }

    private static IEnumerable<(double x, double y)> Handles(LayoutNode n)
    {
        double r = n.X, t = n.Y, w = n.W, h = n.H;
        yield return (r - 4, t - 4);
        yield return (r + w / 2 - 4, t - 4);
        yield return (r + w - 4, t - 4);
        yield return (r + w - 4, t + h / 2 - 4);
        yield return (r + w - 4, t + h - 4);
        yield return (r + w / 2 - 4, t + h - 4);
        yield return (r - 4, t + h - 4);
        yield return (r - 4, t + h / 2 - 4);
    }

    private void DrawMarginGuides(SKCanvas canvas, LayoutNode sel)
    {
        var parent = sel.Parent;
        if (parent == null || !parent.HasRect) return;

        using var parentPaint = new SKPaint
        {
            IsStroke = true,
            StrokeWidth = 1,
            Color = new SKColor(255, 212, 121, 191),
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0)
        };
        canvas.DrawRect(new SKRect((float)parent.X, (float)parent.Y, (float)(parent.X + parent.W), (float)(parent.Y + parent.H)), parentPaint);

        using var seg = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(255, 212, 121, 191) };
        float midY = (float)(sel.Y + sel.H / 2);
        float midX = (float)(sel.X + sel.W / 2);
        DrawMarginSeg(canvas, seg, (float)parent.X, midY, (float)sel.X, midY, sel.X - parent.X);
        DrawMarginSeg(canvas, seg, (float)(sel.X + sel.W), midY, (float)(parent.X + parent.W), midY, parent.X + parent.W - (sel.X + sel.W));
        DrawMarginSeg(canvas, seg, midX, (float)parent.Y, midX, (float)sel.Y, sel.Y - parent.Y);
        DrawMarginSeg(canvas, seg, midX, (float)(sel.Y + sel.H), midX, (float)(parent.Y + parent.H), parent.Y + parent.H - (sel.Y + sel.H));
    }

    private void DrawMarginSeg(SKCanvas canvas, SKPaint seg, float x1, float y1, float x2, float y2, double val)
    {
        if (Math.Abs(val) < 0.5) return;
        canvas.DrawLine(x1, y1, x2, y2, seg);
        float cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
        var text = Math.Round(val).ToString();
        using var font = new SKFont(Typeface, 10);
        float tw = font.MeasureText(text);
        using var bg = new SKPaint { Color = new SKColor(10, 16, 22, 209) };
        canvas.DrawRect(cx - tw / 2 - 3, cy - 6.5f, tw + 6, 13, bg);
        using var fg = new SKPaint { Color = new SKColor(0xFF, 0xD4, 0x79), IsAntialias = true };
        canvas.DrawText(text, cx, cy + 3.5f, SKTextAlign.Center, font, fg);
    }

    private static SKTypeface Typeface => LayoutFontProvider.Typeface;

    private double MeasureTextSkia(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var font = new SKFont(Typeface, (float)fontSize);
        return font.MeasureText(text);
    }

    // ============================================================= 颜色 =============================================================
    private static SKColor? ParseColor(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();

        if (v.StartsWith('#'))
        {
            var h = v[1..];
            if (h.Length == 6 && TryHex(h[..2], out var r1) && TryHex(h[2..4], out var g1) && TryHex(h[4..6], out var b1))
                return new SKColor(r1, g1, b1);
            if (h.Length == 8 && TryHex(h[..2], out var r2) && TryHex(h[2..4], out var g2) && TryHex(h[4..6], out var b2) && TryHex(h[6..8], out var a2))
                return new SKColor(r2, g2, b2, a2);
            return null;
        }

        var parts = v.Split(',');
        var nums = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out nums[i])) return null;
        }
        if (nums.Length < 3) return null;

        double m = Math.Max(Math.Max(nums[0], nums[1]), nums[2]) > 1 ? 255 : 1;
        byte R = (byte)Math.Clamp(nums[0] / m * 255, 0, 255);
        byte G = (byte)Math.Clamp(nums[1] / m * 255, 0, 255);
        byte B = (byte)Math.Clamp(nums[2] / m * 255, 0, 255);
        byte Aa = nums.Length > 3 ? (byte)Math.Clamp(nums[3] * 255, 0, 255) : (byte)255;
        return new SKColor(R, G, B, Aa);
    }

    private static bool TryHex(string s, out byte value)
        => byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    // ============================================================= 鼠标交互 =============================================================
    private (LayoutNode? node, double x, double y, double w, double h) HitTest(double wx, double wy)
    {
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            var q = _rects[i];
            if (LayoutDocument.IsFalseValue(q.Node.Get("visible") ?? "true") && q.Node.Get("visible") != null) continue;
            if (wx >= q.X && wx <= q.X + q.W && wy >= q.Y && wy <= q.Y + q.H) return q;
        }
        return (null, 0, 0, 0, 0);
    }

    private void Canvas_MouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        var p = e.GetPosition(_canvas);
        var (wx, wy) = ScreenToWorld(p.X, p.Y);

        if (_selected is { } sel && sel.HasRect && !sel.IsVirtual)
        {
            foreach (var (hx, hy) in Handles(sel))
            {
                if (wx >= hx && wx <= hx + 9 && wy >= hy && wy <= hy + 9)
                {
                    _drag = new DragState { Type = "resize", Handle = HandleName(hx, hy, sel), Sx = wx, Sy = wy, Sw = sel.W, Sh = sel.H, Node = sel };
                    _canvas.CaptureMouse();
                    return;
                }
            }
        }

        var q = HitTest(wx, wy);
        if (q.node != null)
        {
            var on = OwnerOf(q.node) ?? q.node;
            SelectNode(on);
            _drag = new DragState { Type = "move", Sx = wx, Sy = wy, Node = on };
            _canvas.CaptureMouse();
        }
        else
        {
            SelectNode(null);
        }
    }

    private static string HandleName(double hx, double hy, LayoutNode n)
    {
        bool left = Math.Abs(hx - (n.X - 4)) < 0.6, right = Math.Abs(hx - (n.X + n.W - 4)) < 0.6;
        bool top = Math.Abs(hy - (n.Y - 4)) < 0.6, bottom = Math.Abs(hy - (n.Y + n.H - 4)) < 0.6;
        string h = top ? "n" : bottom ? "s" : "";
        h += left ? "w" : right ? "e" : "";
        return h.Length == 0 ? "se" : h;
    }

    private void Canvas_MouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        _drag = null;
        _canvas.ReleaseMouseCapture();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        _mouseScreen = p;
        _mouseInCanvas = true;
        var (wx, wy) = ScreenToWorld(p.X, p.Y);

        if (_drag is { } d && d.Node != null)
        {
            // 默认网格吸附；按住 Alt 可临时取消
            bool snap = (Keyboard.Modifiers & ModifierKeys.Alt) == 0;

            if (d.Type == "move")
            {
                double dx = wx - d.Sx, dy = wy - d.Sy;
                if (snap)
                {
                    // 让节点最终左上角落到网格上
                    dx = Math.Round((d.Node.X + dx) / SnapStep) * SnapStep - d.Node.X;
                    dy = Math.Round((d.Node.Y + dy) / SnapStep) * SnapStep - d.Node.Y;
                }
                dx = Math.Round(dx);
                dy = Math.Round(dy);
                if (dx != 0 || dy != 0)
                {
                    var m = d.Node.Margins();
                    bool rightAligned = d.Node.Get("halign") == "right";
                    bool bottomAligned = d.Node.Get("valign") == "bottom";
                    if (rightAligned) m.R = Math.Max(0, m.R - dx); else m.L = Math.Max(0, m.L + dx);
                    if (bottomAligned) m.B = Math.Max(0, m.B - dy); else m.T = Math.Max(0, m.T + dy);
                    SetAttr(d.Node, "margin", $"{m.L},{m.T},{m.R},{m.B}");
                    d.Sx = wx; d.Sy = wy;
                    if (ReferenceEquals(_selected, d.Node)) RenderProps();
                }
            }
            else
            {
                double dx = Math.Round(wx - d.Sx), dy = Math.Round(wy - d.Sy);
                double nw = d.Sw, nh = d.Sh;
                if (d.Handle.Contains('e')) nw = d.Sw + dx;
                if (d.Handle.Contains('s')) nh = d.Sh + dy;
                if (d.Handle.Contains('w')) nw = d.Sw - dx;
                if (d.Handle.Contains('n')) nh = d.Sh - dy;
                if (snap)
                {
                    nw = Math.Round(nw / SnapStep) * SnapStep;
                    nh = Math.Round(nh / SnapStep) * SnapStep;
                }
                nw = Math.Max(4, nw);
                nh = Math.Max(4, nh);
                SetAttr(d.Node, "width", ((int)Math.Round(nw)).ToString());
                SetAttr(d.Node, "height", ((int)Math.Round(nh)).ToString());
                if (ReferenceEquals(_selected, d.Node)) RenderProps();
            }
            Repaint();
            return;
        }

        var q = HitTest(wx, wy);
        _hover = q.node;
        _canvas.Cursor = q.node != null ? Cursors.Hand : Cursors.Arrow;
        Repaint();
    }

    private void Canvas_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        _drag = new DragState { Type = "pan", Sx = e.GetPosition(_canvas).X, Sy = e.GetPosition(_canvas).Y };
        _canvas.CaptureMouse();
    }

    private void Canvas_MouseRightUp(object sender, MouseButtonEventArgs e)
    {
        _drag = null;
        _canvas.ReleaseMouseCapture();
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        _autoFit = false;
        var p = e.GetPosition(_canvas);
        var (wx, wy) = ScreenToWorld(p.X, p.Y);
        _zoom = Math.Clamp(_zoom * factor, 0.05, 8);
        _panX = p.X - wx * _zoom;
        _panY = p.Y - wy * _zoom;
        Repaint();
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        _dragOver = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length > 0) IngestResources(files);
        }
        e.Handled = true;
    }

    // ============================================================= 属性写入 =============================================================
    private void SetAttr(LayoutNode n, string key, string? value)
    {
        n.Set(key, value);
        if (key is "templateid" or "itemcount" or "itemwidth" or "itemheight")
        {
            if (_form != null) _doc.BuildExpansion(_form);
        }
        if (ImageKeys.Contains(key)) InvalidateUsage();
        Repaint();
    }

    // ============================================================= 节点操作 =============================================================
    private async void OnAddChild()
    {
        var n = _selected;
        if (n == null) return;
        var owner = Window.GetWindow(this) ?? _window;
        using var dlg = new SingleInputDialog(owner)
        {
            Title = "添加子控件",
            Description = "控件类型（Button / Image / Label / Group / HGroup / VGroup …）",
            DefaultValue = "Button"
        };
        var tag = await dlg.ShowAsync();
        if (string.IsNullOrWhiteSpace(tag)) return;

        var node = new LayoutNode { Tag = tag.Trim(), Parent = n };
        node.Set("id", "");
        node.Set("margin", "0,0,0,0");
        n.Children.Add(node);
        n.Collapsed = false;
        if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        SelectNode(node);
    }

    private void OnDuplicate()
    {
        var n = _selected;
        if (n?.Parent == null || n.IsVirtual) return;
        var clone = n.DeepClone();
        clone.Parent = n.Parent;
        int i = n.Parent.Children.IndexOf(n);
        n.Parent.Children.Insert(i + 1, clone);
        if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        SelectNode(clone);
    }

    private void OnDeleteNode()
    {
        var n = _selected;
        if (n?.Parent == null || n.IsVirtual) return;
        var parent = n.Parent;
        parent.Children.Remove(n);
        if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        SelectNode(parent);
    }

    private void SetStatus(string msg)
    {
        if (_statusText != null) _statusText.Text = msg;
        Debug.WriteLine($"[LayoutEditScene] {msg}");
    }

    /// <summary>标签中文名（对应工具 TAG_CN）。</summary>
    private static string TagCn(string tag) => tag switch
    {
        "Form" => "表单",
        "TmpWindow" => "弹窗",
        "Template" => "模板",
        "Group" => "容器",
        "GroupBox" => "组框",
        "HGroup" => "横向组",
        "VGroup" => "纵向组",
        "Button" => "按钮",
        "CheckButton" => "勾选按钮",
        "Image" => "图片",
        "Animation" => "动画",
        "Label" => "文本",
        "TextBox" => "文本框",
        "EditBox" => "输入框",
        "Repeater" => "重复列表",
        "PlaceHolder" => "模板占位",
        "SlideList" => "滑动列表",
        "ScrollViewer" => "滚动视图",
        "ProgressBar" => "进度条",
        "Slider" => "滑块",
        _ => tag
    };
}
