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
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// WC4 布局编辑器：加载 assets 里的 layout*.xml → 用 <see cref="LayoutEngine"/> 按游戏规则求解 → Skia 画布所见即所得。
/// <para>
/// 左侧表单列表，中间画布（拖动移动 / 手柄缩放 / 右键平移 / 滚轮缩放），右侧控件树 + 属性面板（按
/// <see cref="LayoutSchema"/> 只列游戏真正支持的属性）+ 精灵图库。支持撤销 / 重做、直接保存回 assets（自动留 .bak）。
/// </para>
/// <para>
/// 文字按所选 stringtable 语言显示，字号来自 font_resource.xml 的设备配置（默认 PHONE/HD），
/// 图片按 image_resource.xml 的图集顺序解析 —— 这些都是旧版与游戏不一致的地方。
/// </para>
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
    private bool _snap = true;

    // ---------- 选择 ----------
    private LayoutNode? _form;
    private LayoutNode? _selected;
    private LayoutNode? _hover;
    private readonly List<(LayoutNode Node, double X, double Y, double W, double H)> _rects = new();

    private sealed class DragState
    {
        public string Type = "move";      // move / resize / pan
        public string Handle = "";
        public double Sx, Sy, Sw, Sh;
        public LayoutMargins StartMargins;
        public LayoutNode? Node;
        public bool Moved;
    }

    private DragState? _drag;
    private Point _mouseScreen;
    private bool _mouseInCanvas;
    private bool _dragOver;

    // ---------- 撤销 ----------
    private sealed record Snapshot(string Xml, List<int>? FormPath, List<int>? SelectedPath);
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private const int MaxUndo = 100;
    private bool _dirty;

    // ---------- 语言 / 字体配置 ----------
    private string _lang = "tw";
    private static readonly Dictionary<string, Dictionary<string, string>> _stringTables = new(StringComparer.OrdinalIgnoreCase);

    // ---------- 控件 ----------
    private SKElement _canvas = null!;
    private ListBox _formList = null!;
    private TextBox _formSearch = null!;
    private TextBlock _formCount = null!;
    private TextBlock _statusText = null!;
    private TextBlock _assetStat = null!;
    private TextBlock _titleText = null!;
    private StackPanel _treePanel = null!;
    private ScrollViewer _treeScroll = null!;
    private ScrollViewer _propsScroll = null!;
    private StackPanel _propsPanel = null!;
    private UIElement _spritesPane = null!;
    private Border _propsHost = null!;
    private Button _tabProps = null!;
    private Button _tabSprites = null!;
    private TextBox _canvasWBox = null!;
    private TextBox _canvasHBox = null!;
    private ComboBox _layoutCombo = null!;
    private ComboBox _langCombo = null!;
    private ComboBox _profileCombo = null!;
    private ComboBox _zoomCombo = null!;
    private Button _btnUndo = null!;
    private Button _btnRedo = null!;
    private Button _btnSave = null!;
    private Button _btnAddChild = null!;
    private Button _btnDup = null!;
    private Button _btnDel = null!;
    private Button _btnUp = null!;
    private Button _btnDown = null!;

    private readonly List<(string Name, string Text, string? Path)> _layouts = new();
    private bool _suppressLayoutCombo;

    private DispatcherTimer? _formSearchDebounce;
    private DispatcherTimer? _spSearchDebounce;

    /// <summary>网格吸附步长；按住 Alt 临时取消。</summary>
    private const double SnapStep = 10;

    private static readonly HashSet<string> ImageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "texture", "normalimage", "backimage", "barimage", "thumbimage", "res", "bg_tex",
        "vscrollbarimage", "hscrollbarimage"
    };

    public LayoutEditScene(MainWindow window)
    {
        _window = window;
        EditorTheme.Apply(this);
        Background = EditorTheme.Bg0;
        Focusable = true;

        _ctx.ImageSize = r =>
        {
            var rec = LayoutImageProvider.Resolve(r);
            return rec is { Ready: true } ? (rec.W, rec.H) : null;
        };
        _ctx.MeasureText = MeasureTextSkia;
        _ctx.StringLookup = key => StringTable(_lang).TryGetValue(key, out var v) ? v : null;
        _ctx.FontSizeLookup = name => LayoutFontProvider.GetFontMeta(name).Size;
        _ctx.Language = _lang;

        BuildUI();
        DiscoverLayouts();
        LoadPreferredLayout();

        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            UpdateAssetStat();
            FitZoom();
            Repaint();
            _canvas.Focus();
        }, DispatcherPriority.Loaded);
    }

    // ============================================================= UI =============================================================
    private void BuildUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        // 顶栏：返回 + 标题 + 文件操作
        _titleText = EditorTheme.Title(Loc.T("Layout.Title"));
        _btnSave = EditorTheme.PrimaryButton(Loc.T("Common.Save"), (_, _) => OnSave());
        _btnSave.ToolTip = "Ctrl+S";
        var top = EditorTheme.TopBar("", (_, _) => ConfirmLeave(),
            EditorTheme.Button(Loc.T("Layout.ImportResources"), OnImportResourceFolder),
            EditorTheme.Button(Loc.T("Layout.ImportFiles"), OnImportResourceFiles),
            EditorTheme.Button(Loc.T("Layout.ImportXml"), OnImportXml),
            EditorTheme.Button(Loc.T("Layout.ExportXml"), OnExportXml),
            EditorTheme.Button(Loc.T("Layout.ExportPng"), OnExportPng),
            _btnSave);
        // TopBar 自带的标题是空的，这里把带脏标记的标题塞进去
        if (top.Child is Grid topGrid && topGrid.Children[0] is StackPanel leftStack)
        {
            leftStack.Children.RemoveAt(leftStack.Children.Count - 1);
            leftStack.Children.Add(_titleText);
            _layoutCombo = EditorTheme.ComboBox(200);
            _layoutCombo.Margin = new Thickness(12, 0, 0, 0);
            _layoutCombo.SelectionChanged += (_, _) =>
            {
                if (_suppressLayoutCombo) return;
                int i = _layoutCombo.SelectedIndex;
                if (i >= 0 && i < _layouts.Count && !ConfirmDiscard()) { ReselectCurrentLayout(); return; }
                if (i >= 0 && i < _layouts.Count) LoadLayoutText(_layouts[i].Text, _layouts[i].Name, _layouts[i].Path);
            };
            leftStack.Children.Add(_layoutCombo);
        }
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        Grid.SetRow(BuildToolbar(), 1);
        root.Children.Add(BuildToolbarHost());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        Grid.SetRow(body, 2);

        // 左：表单列表
        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headRow = new Grid { Margin = new Thickness(10, 8, 8, 4) };
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var head = EditorTheme.Section(Loc.T("Layout.Forms"));
        head.Margin = new Thickness(0);
        head.VerticalAlignment = VerticalAlignment.Center;
        headRow.Children.Add(head);
        _formCount = EditorTheme.Badge("0").Child as TextBlock ?? new TextBlock();
        var badge = EditorTheme.Badge("0");
        _formCount = (TextBlock)badge.Child!;
        Grid.SetColumn(badge, 1);
        headRow.Children.Add(badge);
        var addForm = EditorTheme.ToolButton("＋", (_, _) => OnAddForm());
        addForm.ToolTip = Loc.T("Layout.AddForm");
        addForm.Margin = new Thickness(6, 0, 0, 0);
        addForm.Padding = new Thickness(7, 1, 7, 1);
        addForm.MinHeight = 22;
        Grid.SetColumn(addForm, 2);
        headRow.Children.Add(addForm);
        leftGrid.Children.Add(headRow);

        var search = EditorTheme.SearchBox(Loc.T("Layout.SearchForms"), out _formSearch);
        search.Margin = new Thickness(8, 2, 8, 6);
        _formSearch.TextChanged += (_, _) =>
        {
            _formSearchDebounce?.Stop();
            _formSearchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => { _formSearchDebounce?.Stop(); RefreshFormList(); }, Dispatcher.CurrentDispatcher);
            _formSearchDebounce.Start();
        };
        Grid.SetRow(search, 1);
        leftGrid.Children.Add(search);

        _formList = new ListBox { FontSize = 12 };
        _formList.SelectionChanged += (_, _) =>
        {
            if (_formList.SelectedItem is FormEntry fe && !ReferenceEquals(fe.Node, _form)) SelectForm(fe.Node);
        };
        Grid.SetRow(_formList, 2);
        leftGrid.Children.Add(_formList);
        left.Child = leftGrid;
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var splitLeft = EditorTheme.VSplitter();
        Grid.SetColumn(splitLeft, 1);
        body.Children.Add(splitLeft);

        // 中：画布
        var center = new Border { Background = EditorTheme.Bg0, ClipToBounds = true };
        _canvas = new SKElement { IgnorePixelScaling = false, Focusable = true };
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
        _canvas.PreviewKeyDown += Canvas_KeyDown;
        center.Child = _canvas;
        Grid.SetColumn(center, 2);
        body.Children.Add(center);

        var splitRight = EditorTheme.VSplitter();
        Grid.SetColumn(splitRight, 3);
        body.Children.Add(splitRight);

        // 右：控件树 + 属性 / 图库
        var right = EditorTheme.Panel(new Thickness(1, 0, 0, 0));
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(240) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var treeGrid = new Grid();
        treeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        treeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        treeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var treeHead = EditorTheme.Section(Loc.T("Layout.Tree"));
        treeHead.Margin = new Thickness(10, 8, 10, 4);
        treeGrid.Children.Add(treeHead);
        _treePanel = new StackPanel();
        _treeScroll = new ScrollViewer { Content = _treePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(_treeScroll, 1);
        treeGrid.Children.Add(_treeScroll);

        // 节点操作条
        var ops = new WrapPanel { Margin = new Thickness(6, 4, 6, 4) };
        _btnAddChild = EditorTheme.ToolButton(Loc.T("Layout.AddChild"), (_, _) => OnAddChild());
        _btnDup = EditorTheme.ToolButton(Loc.T("Common.Duplicate"), (_, _) => OnDuplicate());
        _btnDup.ToolTip = "Ctrl+D";
        _btnUp = EditorTheme.ToolButton("▲", (_, _) => OnMove(-1));
        _btnUp.ToolTip = Loc.T("Layout.MoveUp");
        _btnDown = EditorTheme.ToolButton("▼", (_, _) => OnMove(1));
        _btnDown.ToolTip = Loc.T("Layout.MoveDown");
        _btnDel = EditorTheme.DangerButton(Loc.T("Common.Delete"), (_, _) => OnDeleteNode());
        _btnDel.Style = EditorTheme.ToolButtonStyle;
        _btnDel.Background = EditorTheme.Danger;
        _btnDel.BorderBrush = EditorTheme.Danger;
        _btnDel.ToolTip = "Delete";
        foreach (var b in new[] { _btnAddChild, _btnDup, _btnUp, _btnDown, _btnDel }) ops.Children.Add(b);
        Grid.SetRow(ops, 2);
        treeGrid.Children.Add(ops);
        Grid.SetRow(treeGrid, 0);
        rightGrid.Children.Add(treeGrid);

        var hsplit = EditorTheme.HSplitter();
        Grid.SetRow(hsplit, 1);
        rightGrid.Children.Add(hsplit);

        // 页签
        var tabs = new Border { Background = EditorTheme.Bg2, BorderBrush = EditorTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        _tabProps = EditorTheme.TabButton(Loc.T("Layout.Props"), true);
        _tabSprites = EditorTheme.TabButton(Loc.T("Layout.Sprites"), false);
        tabRow.Children.Add(_tabProps);
        tabRow.Children.Add(_tabSprites);
        tabs.Child = tabRow;
        Grid.SetRow(tabs, 2);
        rightGrid.Children.Add(tabs);

        _propsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        _propsScroll = new ScrollViewer { Content = _propsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _propsHost = new Border { Child = _propsScroll };
        Grid.SetRow(_propsHost, 3);
        rightGrid.Children.Add(_propsHost);

        _spritesPane = BuildSpritesPane();

        _tabProps.Click += (_, _) => ShowPropsTab();
        _tabSprites.Click += (_, _) => ShowSpritesTab();

        right.Child = rightGrid;
        Grid.SetColumn(right, 4);
        body.Children.Add(right);
        root.Children.Add(body);

        var status = EditorTheme.StatusBar(out _statusText);
        // 资源统计放状态栏右侧：顶栏按钮多，再塞文字在 1250 宽度下会把"保存"挤出去
        _assetStat = EditorTheme.Muted("");
        _assetStat.VerticalAlignment = VerticalAlignment.Center;
        _assetStat.Margin = new Thickness(0, 0, 12, 0);
        _assetStat.HorizontalAlignment = HorizontalAlignment.Right;
        var statusGrid = new Grid();
        status.Child = null;   // 先从状态栏里摘下来，否则加进 Grid 会报"已是另一个元素的逻辑子元素"
        statusGrid.Children.Add(_statusText);
        statusGrid.Children.Add(_assetStat);
        status.Child = statusGrid;
        Grid.SetRow(status, 3);
        root.Children.Add(status);

        Content = root;
    }

    private Border _toolbarHost = null!;

    private Border BuildToolbarHost() => _toolbarHost;

    private UIElement BuildToolbar()
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };

        _btnUndo = EditorTheme.ToolButton("↶ " + Loc.T("Common.Undo"), (_, _) => Undo());
        _btnUndo.ToolTip = "Ctrl+Z";
        _btnRedo = EditorTheme.ToolButton("↷ " + Loc.T("Common.Redo"), (_, _) => Redo());
        _btnRedo.ToolTip = "Ctrl+Y";
        bar.Children.Add(_btnUndo);
        bar.Children.Add(_btnRedo);
        bar.Children.Add(EditorTheme.VSeparator());

        bar.Children.Add(SmallLabel(Loc.T("Layout.Canvas")));
        _canvasWBox = NumberBox("1280", 64);
        _canvasWBox.LostFocus += (_, _) => CommitCanvasSize();
        _canvasHBox = NumberBox("720", 64);
        _canvasHBox.LostFocus += (_, _) => CommitCanvasSize();
        bar.Children.Add(_canvasWBox);
        bar.Children.Add(SmallLabel("×"));
        bar.Children.Add(_canvasHBox);
        var preset = EditorTheme.ComboBox(150);
        preset.FontSize = 12;
        preset.Margin = new Thickness(4, 0, 6, 0);
        preset.Items.Add(Loc.T("Layout.Preset"));
        preset.Items.Add(Loc.T("Layout.Preset.Hd"));
        preset.Items.Add(Loc.T("Layout.Preset.Wide"));
        preset.Items.Add(Loc.T("Layout.Preset.Sd"));
        preset.Items.Add(Loc.T("Layout.Preset.Pad"));
        preset.SelectedIndex = 0;
        preset.SelectionChanged += (_, _) =>
        {
            int idx = preset.SelectedIndex;
            if (idx <= 0) return;
            (int w, int h) = idx switch { 1 => (1280, 720), 2 => (1560, 720), 3 => (1136, 640), _ => (1024, 768) };
            _canvasWBox.Text = w.ToString();
            _canvasHBox.Text = h.ToString();
            CommitCanvasSize();
            preset.SelectedIndex = 0;
        };
        bar.Children.Add(preset);
        bar.Children.Add(EditorTheme.VSeparator());

        bar.Children.Add(SmallLabel(Loc.T("Layout.Language")));
        _langCombo = EditorTheme.ComboBox(70);
        _langCombo.FontSize = 12;
        _langCombo.Margin = new Thickness(4, 0, 6, 0);
        foreach (var l in DiscoverStringTableLanguages()) _langCombo.Items.Add(l);
        _langCombo.SelectedItem = _langCombo.Items.Contains(_lang) ? _lang : (_langCombo.Items.Count > 0 ? _langCombo.Items[0] : null);
        if (_langCombo.SelectedItem is string sl) { _lang = sl; _ctx.Language = sl; }
        _langCombo.SelectionChanged += (_, _) =>
        {
            if (_langCombo.SelectedItem is string s) { _lang = s; _ctx.Language = s; Repaint(); RenderProps(); }
        };
        bar.Children.Add(_langCombo);

        bar.Children.Add(SmallLabel(Loc.T("Layout.FontProfile")));
        _profileCombo = EditorTheme.ComboBox(100);
        _profileCombo.FontSize = 12;
        _profileCombo.Margin = new Thickness(4, 0, 6, 0);
        foreach (var p in FontResourceParser.Profiles) _profileCombo.Items.Add(p);
        _profileCombo.SelectedItem = LayoutFontProvider.Profile;
        _profileCombo.SelectionChanged += (_, _) =>
        {
            if (_profileCombo.SelectedItem is string s) { LayoutFontProvider.Profile = s; Repaint(); }
        };
        bar.Children.Add(_profileCombo);
        bar.Children.Add(EditorTheme.VSeparator());

        bar.Children.Add(SmallLabel(Loc.T("Layout.Zoom")));
        _zoomCombo = EditorTheme.ComboBox(86);
        _zoomCombo.FontSize = 12;
        _zoomCombo.Margin = new Thickness(4, 0, 6, 0);
        foreach (var s in new[] { Loc.T("Layout.ZoomFit"), "25%", "50%", "75%", "100%", "150%", "200%" }) _zoomCombo.Items.Add(s);
        _zoomCombo.SelectedIndex = 0;
        _zoomCombo.SelectionChanged += (_, _) =>
        {
            int i = _zoomCombo.SelectedIndex;
            if (i <= 0) { _autoFit = true; FitZoom(); Repaint(); }
            else { _autoFit = false; _zoom = i switch { 1 => 0.25, 2 => 0.5, 3 => 0.75, 4 => 1.0, 5 => 1.5, _ => 2.0 }; UpdatePanToCenter(); Repaint(); }
        };
        bar.Children.Add(_zoomCombo);
        bar.Children.Add(EditorTheme.VSeparator());

        bar.Children.Add(Check(Loc.T("Layout.ShowBoxes"), _showBox, v => { _showBox = v; Repaint(); }));
        bar.Children.Add(Check(Loc.T("Layout.ShowHidden"), _ghost, v => { _ghost = v; Repaint(); }));
        bar.Children.Add(Check(Loc.T("Layout.ShowMissing"), _showMissing, v => { _showMissing = v; Repaint(); }));
        bar.Children.Add(Check(Loc.T("Layout.ShowGrid"), _showGrid, v => { _showGrid = v; Repaint(); }));
        bar.Children.Add(Check(Loc.T("Layout.ShowMargins"), _showMargin, v => { _showMargin = v; Repaint(); }));
        var snap = Check(Loc.F("Layout.Snap", SnapStep), _snap, v => _snap = v);
        snap.ToolTip = Loc.T("Layout.SnapHint");
        bar.Children.Add(snap);

        _toolbarHost = EditorTheme.ToolBar(bar);
        Grid.SetRow(_toolbarHost, 1);
        return _toolbarHost;
    }

    private static TextBlock SmallLabel(string text)
    {
        var tb = EditorTheme.Muted(text);
        tb.VerticalAlignment = VerticalAlignment.Center;
        tb.Margin = new Thickness(2, 0, 2, 0);
        return tb;
    }

    private static TextBox NumberBox(string def, double width) => new()
    {
        Text = def,
        Width = width,
        FontSize = 12,
        MinHeight = 26,
        Padding = new Thickness(4, 2, 4, 2),
        Margin = new Thickness(2, 0, 2, 0)
    };

    private static CheckBox Check(string label, bool initial, Action<bool> changed)
    {
        var cb = EditorTheme.CheckBox(label, initial, changed);
        cb.VerticalAlignment = VerticalAlignment.Center;
        cb.Margin = new Thickness(4, 0, 8, 0);
        return cb;
    }

    private void ShowPropsTab()
    {
        _propsHost.Child = _propsScroll;
        EditorTheme.SetTabActive(_tabProps, true);
        EditorTheme.SetTabActive(_tabSprites, false);
    }

    private void ShowSpritesTab()
    {
        _propsHost.Child = _spritesPane;
        EditorTheme.SetTabActive(_tabProps, false);
        EditorTheme.SetTabActive(_tabSprites, true);
        RefreshSprites();
    }

    private void UpdateAssetStat()
    {
        _assetStat.Text = Loc.F("Layout.Status.Assets", LayoutImageProvider.AtlasNames.Count, LayoutImageProvider.ImageCount, LayoutFontProvider.BmFontCount);
    }

    private void UpdateTitle()
    {
        _titleText.Text = Loc.T("Layout.Title") + (_dirty ? " *" : "");
        _btnUndo.IsEnabled = _undo.Count > 0;
        _btnRedo.IsEnabled = _redo.Count > 0;
    }

    // ============================================================= stringtable =============================================================

    private static IEnumerable<string> DiscoverStringTableLanguages()
    {
        var list = new List<string>();
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded) { try { manager.ScanDefault(); } catch { } }
            foreach (var e in manager.ListByExtension(".ini"))
            {
                var name = e.FileNameWithoutExtension;
                if (name.StartsWith("stringtable_", StringComparison.OrdinalIgnoreCase))
                    list.Add(name["stringtable_".Length..].ToLowerInvariant());
            }
        }
        catch { }
        if (list.Count == 0) list.Add("tw");
        // 游戏默认语言排前面
        list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        return list.Distinct();

        static int Rank(string l) => l switch { "tw" => 0, "cn" => 1, "en" => 2, "ja" => 3, "ko" => 4, _ => 9 };
    }

    /// <summary>按语言读 stringtable_xx.ini（自己解析，不走 AssetManager 的单语言缓存）。</summary>
    private static Dictionary<string, string> StringTable(string lang)
    {
        if (_stringTables.TryGetValue(lang, out var t)) return t;
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manager = AssetManager.Default;
            var entry = manager.Find($"stringtable_{lang}.ini");
            if (entry != null)
            {
                foreach (var raw in manager.ReadText(entry).Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] is ';' or '#' or '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    table[line[..eq].Trim()] = line[(eq + 1)..].Trim().Replace("\\n", "\n");
                }
            }
        }
        catch { }
        _stringTables[lang] = table;
        return table;
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
                        _layouts.Add((name, text, entry.FullPath));
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayoutEditScene] 扫描布局失败: {ex.Message}");
        }
        RefreshLayoutCombo();
    }

    private void RefreshLayoutCombo()
    {
        _suppressLayoutCombo = true;
        _layoutCombo.Items.Clear();
        foreach (var l in _layouts) _layoutCombo.Items.Add(l.Name);
        _suppressLayoutCombo = false;
    }

    private void ReselectCurrentLayout()
    {
        _suppressLayoutCombo = true;
        int idx = _layouts.FindIndex(l => l.Name == _doc.SourceName);
        _layoutCombo.SelectedIndex = idx;
        _suppressLayoutCombo = false;
    }

    private void LoadPreferredLayout()
    {
        int idx = _layouts.FindIndex(l => l.Name.Equals("layout_x.xml", StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = _layouts.FindIndex(l => l.Name.Equals("layout.xml", StringComparison.OrdinalIgnoreCase));
        if (idx < 0 && _layouts.Count > 0) idx = 0;

        if (idx >= 0)
        {
            LoadLayoutText(_layouts[idx].Text, _layouts[idx].Name, _layouts[idx].Path);
            ReselectCurrentLayout();
        }
        else
        {
            _doc.NewEmpty();
            SetStatus(Loc.T("Layout.Status.NoLayouts"));
            RefreshFormList();
            RefreshTree();
            RenderProps();
            UpdateTitle();
        }
    }

    private void LoadLayoutText(string text, string name, string? path)
    {
        try
        {
            _doc.Load(text, name, path);
            _doc.BuildExpansion();
            _form = _doc.Forms.FirstOrDefault(f => f.Get("id") == "form_main") ?? _doc.Forms.FirstOrDefault();
            _selected = null;
            _hover = null;
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            InvalidateUsage();
            RefreshFormList();
            RefreshTree();
            RenderProps();
            _autoFit = true;
            FitZoom();
            Repaint();
            UpdateTitle();
            SetStatus(Loc.F("Layout.Status.Loaded", name, _doc.Forms.Count));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Layout.Status.ParseFailed", ex.Message));
        }
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        return MessageBox.Show(Loc.T("Layout.Dlg.Discard"), Loc.T("Common.Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void ConfirmLeave()
    {
        if (ConfirmDiscard()) _window.ReturnToMainScene();
    }

    private void OnImportXml(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = $"{Loc.T("Layout.Dlg.XmlFilter")} (*.xml)|*.xml|{Loc.T("Common.AllFiles")} (*.*)|*.*", Title = Loc.T("Layout.ImportXml") };
        if (dlg.ShowDialog() != true) return;
        var text = File.ReadAllText(dlg.FileName);
        var name = IOPath.GetFileName(dlg.FileName);
        int existing = _layouts.FindIndex(l => l.Name == name);
        if (existing >= 0) _layouts[existing] = (name, text, dlg.FileName);
        else _layouts.Add((name, text, dlg.FileName));
        RefreshLayoutCombo();
        LoadLayoutText(text, name, dlg.FileName);
        ReselectCurrentLayout();
    }

    private void OnImportResourceFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Layout.Dlg.ResourceFolder") };
        if (dlg.ShowDialog() != true) return;
        IngestResources([dlg.FolderName]);
    }

    private void OnImportResourceFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = $"{Loc.T("Layout.Dlg.ResourceFilter")}|*.xml;*.png;*.webp;*.jpg;*.jpeg;*.fnt|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            Title = Loc.T("Layout.Dlg.ResourceFiles")
        };
        if (dlg.ShowDialog() != true) return;
        IngestResources(dlg.FileNames);
    }

    private void IngestResources(string[] paths)
    {
        var layouts = paths.Length == 1 && Directory.Exists(paths[0])
            ? LayoutImageProvider.IngestFolder(paths[0])
            : LayoutImageProvider.IngestFiles(paths);

        foreach (var (name, text, path) in layouts)
        {
            int existing = _layouts.FindIndex(l => l.Name == name);
            if (existing >= 0) _layouts[existing] = (name, text, path);
            else _layouts.Add((name, text, path));
        }
        RefreshLayoutCombo();
        ReselectCurrentLayout();

        UpdateAssetStat();
        SetStatus(Loc.F("Layout.Status.Resources", LayoutImageProvider.ExtraAtlasCount, LayoutImageProvider.ExtraImageCount, layouts.Count));

        if (_doc.Forms.Count == 0 && _layouts.Count > 0 && ConfirmDiscard())
        {
            int idx = _layouts.FindIndex(l => l.Name.Equals("layout_x.xml", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = _layouts.FindIndex(l => l.Name.Equals("layout.xml", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            LoadLayoutText(_layouts[idx].Text, _layouts[idx].Name, _layouts[idx].Path);
            ReselectCurrentLayout();
        }
        else
        {
            InvalidateUsage();
            Repaint();
        }
    }

    /// <summary>保存回来源文件（首次覆盖前留一份 .bak）；没有来源文件时走导出。</summary>
    private void OnSave()
    {
        var path = _doc.SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            OnExportXml(this, new RoutedEventArgs());
            return;
        }
        if (MessageBox.Show(Loc.F("Layout.Dlg.SaveConfirm", path), Loc.T("Common.Save"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var bak = path + ".bak";
            if (!File.Exists(bak)) File.Copy(path, bak);
            File.WriteAllText(path, _doc.Dump(), new System.Text.UTF8Encoding(false));
            _dirty = false;
            UpdateTitle();
            int idx = _layouts.FindIndex(l => l.Path == path);
            if (idx >= 0) _layouts[idx] = (_layouts[idx].Name, _doc.Dump(), path);
            SetStatus(Loc.F("Layout.Status.Saved", path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Common.OperationFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportXml(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = $"{Loc.T("Layout.Dlg.XmlFilter")} (*.xml)|*.xml",
            FileName = _doc.SourceName ?? "layout.xml",
            Title = Loc.T("Layout.ExportXml")
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _doc.Dump(), new System.Text.UTF8Encoding(false));
            _doc.SourcePath = dlg.FileName;
            _doc.SourceName = IOPath.GetFileName(dlg.FileName);
            _dirty = false;
            UpdateTitle();
            SetStatus(Loc.F("Common.Exported", IOPath.GetFileName(dlg.FileName)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Common.ExportFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = $"{Loc.T("Layout.Dlg.PngFilter")} (*.png)|*.png",
            FileName = (_form?.Get("id") ?? "layout") + ".png",
            Title = Loc.T("Layout.ExportPng")
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
            DrawScene(canvas, overlay: false);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data != null) File.WriteAllBytes(dlg.FileName, data.ToArray());
            SetStatus(Loc.F("Layout.Status.ExportedPng", IOPath.GetFileName(dlg.FileName), w, h));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Common.ExportFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CommitCanvasSize()
    {
        if (int.TryParse(_canvasWBox.Text, out var w) && w > 0) _worldW = w;
        if (int.TryParse(_canvasHBox.Text, out var h) && h > 0) _worldH = h;
        if (_autoFit) FitZoom();
        Repaint();
    }

    // ============================================================= 撤销 / 重做 =============================================================

    private Snapshot TakeSnapshot() => new(_doc.Dump(),
        _form != null ? _doc.PathOf(_form) : null,
        _selected is { IsVirtual: false } ? _doc.PathOf(_selected) : null);

    /// <summary>在改动文档之前调用：记一份快照进撤销栈。</summary>
    private void PushUndo()
    {
        _undo.Add(TakeSnapshot());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _dirty = true;
        UpdateTitle();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(TakeSnapshot());
        var snap = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreSnapshot(snap);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(TakeSnapshot());
        var snap = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreSnapshot(snap);
    }

    private void RestoreSnapshot(Snapshot snap)
    {
        var name = _doc.SourceName;
        var path = _doc.SourcePath;
        _doc.Load(snap.Xml, name, path);
        _doc.BuildExpansion();
        _form = snap.FormPath != null ? _doc.NodeAt(snap.FormPath) : null;
        _form ??= _doc.Forms.FirstOrDefault();
        _selected = snap.SelectedPath != null ? _doc.NodeAt(snap.SelectedPath) : null;
        _hover = null;
        _dirty = true;
        InvalidateUsage();
        RefreshFormList();
        RefreshTree();
        RenderProps();
        Repaint();
        UpdateTitle();
    }

    // ============================================================= 表单列表 =============================================================
    private sealed class FormEntry
    {
        public LayoutNode Node { get; init; } = null!;
        public override string ToString() => $"[{TagName(Node.Tag)}] {Node.Get("id")}";
    }

    private void RefreshFormList()
    {
        var q = (_formSearch.Text ?? "").Trim().ToLowerInvariant();
        _formList.Items.Clear();
        foreach (var f in _doc.Forms)
        {
            var id = f.Get("id") ?? "";
            if (q.Length > 0 && !id.ToLowerInvariant().Contains(q) && !f.Tag.ToLowerInvariant().Contains(q)) continue;
            var item = new FormEntry { Node = f };
            _formList.Items.Add(item);
            if (ReferenceEquals(f, _form)) _formList.SelectedItem = item;
        }
        _formCount.Text = _doc.Forms.Count.ToString();
        if (_formList.SelectedItem != null) _formList.ScrollIntoView(_formList.SelectedItem);
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
        if (node != null && node.IsCell) node = node.Owner;
        _selected = node;
        RefreshTree();
        RenderProps();
        Repaint();
    }

    /// <summary>虚拟节点（模板展开 / 重复单元）回到拥有者，编辑总是落在真实节点上。</summary>
    private static LayoutNode? OwnerOf(LayoutNode? n)
    {
        while (n is { IsVirtual: true }) n = n.Owner;
        return n;
    }

    private void Repaint() => _canvas?.InvalidateVisual();

    // ============================================================= 画布变换 =============================================================
    private void FitZoom()
    {
        if (_canvas == null) return;
        double w = Math.Max(50, _canvas.ActualWidth - 40);
        double h = Math.Max(50, _canvas.ActualHeight - 40);
        _zoom = Math.Max(0.02, Math.Min(w / _worldW, h / _worldH));
        UpdatePanToCenter();
    }

    private void UpdatePanToCenter()
    {
        double cw = _canvas.ActualWidth, chh = _canvas.ActualHeight;
        _panX = Math.Max(20, (cw - _worldW * _zoom) / 2);
        _panY = Math.Max(20, (chh - _worldH * _zoom) / 2);
    }

    private (double x, double y) ScreenToWorld(double sx, double sy) => ((sx - _panX) / _zoom, (sy - _panY) / _zoom);

    /// <summary>把画布平移到以节点中心为画布中心。</summary>
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
        canvas.Clear(new SKColor(0x14, 0x17, 0x1B));

        // 画布外框（屏幕范围）
        canvas.Save();
        canvas.Translate((float)_panX, (float)_panY);
        canvas.Scale((float)_zoom);
        using (var bg = new SKPaint { Color = new SKColor(0x0F, 0x14, 0x19) })
            canvas.DrawRect(0, 0, (float)_worldW, (float)_worldH, bg);
        DrawScene(canvas, overlay: true);
        using (var frame = new SKPaint { IsStroke = true, StrokeWidth = (float)(1 / _zoom), Color = new SKColor(0x46, 0x50, 0x5C) })
            canvas.DrawRect(0, 0, (float)_worldW, (float)_worldH, frame);
        canvas.Restore();

        DrawCoordTip(canvas);
    }

    /// <summary>求解当前表单并绘制（overlay=false 时用于导出纯内容）。</summary>
    private void DrawScene(SKCanvas canvas, bool overlay)
    {
        if (_showGrid && overlay) DrawGrid(canvas);

        _rects.Clear();
        if (_form == null)
        {
            if (overlay) DrawEmptyHint(canvas);
            if (overlay) DrawDropHint(canvas);
            return;
        }

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)_worldW, (float)_worldH));
        SolveForm(_form);
        DrawNode(canvas, _form, 0, 1.0, overlay);
        canvas.Restore();

        if (overlay)
        {
            DrawOverlay(canvas);
            DrawDropHint(canvas);
        }

        UpdateGeoCard();
        SetStatus(Loc.F("Layout.Status.Summary", _form.Get("id") ?? "", _worldW.ToString("0"), _worldH.ToString("0"), _rects.Count));
    }

    /// <summary>
    /// 顶层表单的摆放：没写尺寸的 Form 填满屏幕；写了尺寸的 Form / TmpWindow 默认居中（弹窗），
    /// 显式 halign / valign 则按其对齐；margin 照常生效。
    /// </summary>
    private void SolveForm(LayoutNode form)
    {
        var m = form.Margins();
        double cw = _worldW - m.L - m.R, ch = _worldH - m.T - m.B;
        double rw = form.Num("width"), rh = form.Num("height");
        bool sized = !double.IsNaN(rw) || !double.IsNaN(rh);
        if (double.IsNaN(rw)) rw = cw;
        if (double.IsNaN(rh)) rh = ch;

        bool dialogLike = form.Tag == "TmpWindow" || sized;
        int ha = form.Get("halign") is { } hv ? LayoutEngine.HAlignOf(hv) : (dialogLike ? 1 : 0);
        int va = form.Get("valign") is { } vv ? LayoutEngine.VAlignOf(vv) : (dialogLike ? 1 : 0);
        double rx = ha switch { 2 => _worldW - m.R - rw, 1 => m.L + (cw - rw) / 2, _ => m.L };
        double ry = va switch { 2 => _worldH - m.B - rh, 1 => m.T + (ch - rh) / 2, _ => m.T };

        form.X = rx; form.Y = ry; form.W = rw; form.H = rh; form.HasRect = true;
        foreach (var c in form.Children) LayoutEngine.Layout(c, rx, ry, rw, rh, _ctx);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        const int minor = 20, major = 100;
        using var thin = new SKPaint { Color = new SKColor(120, 150, 180, 28), StrokeWidth = 1 };
        using var thick = new SKPaint { Color = new SKColor(120, 170, 220, 70), StrokeWidth = 1 };
        for (int x = minor; x < _worldW; x += minor)
            canvas.DrawLine(x, 0, x, (float)_worldH, x % major == 0 ? thick : thin);
        for (int y = minor; y < _worldH; y += minor)
            canvas.DrawLine(0, y, (float)_worldW, y, y % major == 0 ? thick : thin);

        using var lblFont = new SKFont(UiTypeface, 9);
        using var lblPaint = new SKPaint { Color = new SKColor(150, 190, 230, 115), IsAntialias = true };
        for (int x = major; x < _worldW; x += major)
            canvas.DrawText(x.ToString(), x + 2, 10, SKTextAlign.Left, lblFont, lblPaint);
        for (int y = major; y < _worldH; y += major)
            canvas.DrawText(y.ToString(), 2, y + 10, SKTextAlign.Left, lblFont, lblPaint);
    }

    private static byte A(double alpha) => (byte)Math.Clamp(alpha * 255, 0, 255);

    private static readonly SKColor PlaceholderColor = new(126, 209, 185);
    private static readonly SKColor MissingColor = new(231, 76, 60);

    private void DrawNode(SKCanvas canvas, LayoutNode n, int depth, double parentAlpha, bool overlay)
    {
        if (!n.HasRect) return;

        double alpha = parentAlpha;
        var av = n.Num("alpha");
        if (!double.IsNaN(av)) alpha *= Math.Clamp(av, 0, 1);
        bool hidden = LayoutDocument.IsHidden(n);
        if (hidden)
        {
            if (!_ghost || !overlay) return;
            alpha = parentAlpha * 0.2;
        }

        _rects.Add((n, n.X, n.Y, n.W, n.H));

        if (n.IsCell)
        {
            foreach (var c in n.Children) DrawNode(canvas, c, depth + 1, alpha, overlay);
            return;
        }

        var def = LayoutSchema.Get(n.Tag);
        bool clipOn = (n.Get("clipped") is { } cl && !LayoutDocument.IsFalseValue(cl)) || n.Tag == "ScrollViewer";
        if (clipOn && n.W > 0 && n.H > 0)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W), (float)(n.Y + n.H)));
        }

        var rect = new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W), (float)(n.Y + n.H));

        // ---- 窗口底（TmpWindow / 带尺寸的 Form 是弹窗，游戏会画统一的窗框） ----
        if (n.Tag == "TmpWindow" || (n.Tag == "Form" && ReferenceEquals(n, _form) && (n.Has("width") || n.Has("height"))))
            DrawWindowFrame(canvas, n, alpha);

        // ---- 底图 / 底色 ----
        bool drawn = false;
        var ir = LayoutDocument.ImageRefOf(n);
        var tint = ParseColor(n.Get("color"));
        if (ir != null)
        {
            var rec = LayoutImageProvider.Resolve(ir.Value.Ref);
            if (rec is { Ready: true })
            {
                int dm = MapDrawMode(n.Get("drawmode"));
                var (sx, sy) = LayoutEngine.ScaleOf(n);
                double rot = n.Num("rotate");
                double flipX = n.Num("scalex");

                canvas.Save();
                canvas.Translate((float)n.X, (float)n.Y);
                if (!double.IsNaN(rot) && rot != 0)
                {
                    canvas.Translate((float)(n.W / 2), (float)(n.H / 2));
                    canvas.RotateDegrees((float)rot);
                    canvas.Translate((float)(-n.W / 2), (float)(-n.H / 2));
                }
                if (!double.IsNaN(flipX) && flipX < 0)
                {
                    canvas.Translate((float)n.W, 0);
                    canvas.Scale(-1, 1);
                }

                byte alphaByte = A(alpha * (tint?.Alpha ?? 255) / 255.0);
                if (tint is { } tc && (tc.Red != 255 || tc.Green != 255 || tc.Blue != 255))
                {
                    using var layerPaint = new SKPaint();
                    canvas.SaveLayer(layerPaint);
                    DrawImage(canvas, rec, dm, n.W, n.H, sx, sy, 255);
                    using var tintPaint = new SKPaint { Color = tc.WithAlpha(255), BlendMode = SKBlendMode.Modulate };
                    canvas.DrawRect(new SKRect(0, 0, (float)n.W, (float)n.H), tintPaint);
                    canvas.Restore();
                    if (alphaByte < 255)
                    {
                        // 叠一层整体透明度：Modulate 层无法直接带 alpha
                        using var fade = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 - alphaByte)), BlendMode = SKBlendMode.DstOut };
                        canvas.DrawRect(new SKRect(0, 0, (float)n.W, (float)n.H), fade);
                    }
                }
                else
                {
                    DrawImage(canvas, rec, dm, n.W, n.H, sx, sy, alphaByte);
                }
                canvas.Restore();
                drawn = true;
            }
        }
        else if (tint is { } fill && n.Tag is "GroupBox" or "Group" && fill.Alpha > 0)
        {
            // 没有底图但写了 color 的分组：实色面板
            using var p = new SKPaint { Color = fill.WithAlpha(A(alpha * fill.Alpha / 255.0)) };
            canvas.DrawRect(rect, p);
            drawn = true;
        }

        // ---- 占位（容器 / 游戏控件 / 缺图） ----
        if (!drawn && !ReferenceEquals(n, _form))
        {
            bool isGamePh = def.Category == "Game" || n.Tag is "SlideList" or "ScrollViewer" or "Repeater" or "PlaceHolder";
            if (overlay && (isGamePh || (_showBox && n.W > 1 && n.H > 1 && (def.IsContainer || ir == null))))
            {
                using var paint = new SKPaint
                {
                    IsStroke = true,
                    StrokeWidth = 1,
                    Color = PlaceholderColor.WithAlpha(A(alpha * (isGamePh ? 0.9 : 0.3))),
                    PathEffect = SKPathEffect.CreateDash(isGamePh ? new[] { 6f, 4f } : new[] { 3f, 3f }, 0)
                };
                canvas.DrawRect(Inset(rect), paint);
                if (isGamePh)
                    DrawPill(canvas, TagName(n.Tag) + (n.Get("id") != null ? " #" + n.Get("id") : ""), rect.Left, rect.Top, PlaceholderColor.WithAlpha(A(alpha)));
            }
        }

        if (!drawn && ir != null && _showMissing && overlay)
        {
            using var paint = new SKPaint { Color = MissingColor.WithAlpha(A(alpha * 0.8)), IsAntialias = true };
            using var font = new SKFont(UiTypeface, 9);
            canvas.DrawText("? " + ir.Value.Ref, (float)(n.X + 2), (float)(n.Y + 10), SKTextAlign.Left, font, paint);
            using var box = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = MissingColor.WithAlpha(A(alpha * 0.5)) };
            canvas.DrawRect(Inset(rect), box);
        }

        if (overlay && n.Tag == "PlaceHolder" && n.Get("templateid") is { } tplId)
        {
            using var paint = new SKPaint { Color = PlaceholderColor.WithAlpha(A(alpha * 0.8)), IsAntialias = true };
            using var font = new SKFont(UiTypeface, 9);
            canvas.DrawText("⌂ " + tplId, (float)(n.X + 2), (float)(n.Y + n.H - 3), SKTextAlign.Left, font, paint);
        }

        // ---- 进度条前景 ----
        if (n.Tag == "ProgressBar" && n.Get("barimage") is { Length: > 0 } barRef && n.W > 0)
        {
            var bar = LayoutImageProvider.Resolve(barRef);
            if (bar is { Ready: true })
            {
                double max = n.Num("max"); if (double.IsNaN(max) || max <= 0) max = 100;
                double val = n.Num("value"); if (double.IsNaN(val)) val = max * 0.6;
                double frac = Math.Clamp(val / max, 0, 1);
                canvas.Save();
                canvas.ClipRect(new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W * frac), (float)(n.Y + n.H)));
                canvas.Translate((float)n.X, (float)n.Y);
                DrawImage(canvas, bar, MapDrawMode(n.Get("drawmode") ?? "stretch"), n.W, n.H, 1, 1, A(alpha));
                canvas.Restore();
            }
        }

        // ---- 文字 ----
        if (n.Tag is "Label" or "TextBox" or "EditBox" || (n.Tag == "Button" && n.Has("text")))
        {
            if (LayoutEngine.TextOf(n, _ctx).Length > 0) DrawLabelText(canvas, n, alpha);
        }

        if (overlay && _showBox && !def.IsContainer && n.W > 1 && n.H > 1)
        {
            using var paint = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(90, 170, 255, A(alpha * 0.22)) };
            canvas.DrawRect(Inset(rect), paint);
        }

        var kids = LayoutDocument.ExpandedKids(n) ?? n.Children;
        foreach (var c in kids) DrawNode(canvas, c, depth + 1, alpha, overlay);

        if (clipOn && n.W > 0 && n.H > 0) canvas.Restore();
    }

    private static SKRect Inset(SKRect r) => new(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f);

    /// <summary>弹窗底：半透明深色面板 + 标题文字 + 关闭钮占位，模拟游戏给 TmpWindow 画的统一窗框。</summary>
    private void DrawWindowFrame(SKCanvas canvas, LayoutNode n, double alpha)
    {
        var rect = new SKRect((float)n.X, (float)n.Y, (float)(n.X + n.W), (float)(n.Y + n.H));
        using var fill = new SKPaint { Color = new SKColor(24, 36, 48, A(alpha * 0.92)), IsAntialias = true };
        canvas.DrawRoundRect(rect, 10, 10, fill);
        using var edge = new SKPaint { IsStroke = true, StrokeWidth = 2, Color = new SKColor(120, 160, 190, A(alpha * 0.7)), IsAntialias = true };
        canvas.DrawRoundRect(rect, 10, 10, edge);

        var titleKey = n.Get("text");
        if (!string.IsNullOrEmpty(titleKey))
        {
            string title = StringTable(_lang).TryGetValue(titleKey, out var t) ? t : titleKey;
            var meta = LayoutFontProvider.GetFontMeta(n.Get("font") ?? "font_text_4");
            using var font = new SKFont(LayoutFontProvider.GetTypeface(_lang, LayoutFontProvider.GetDef(n.Get("font") ?? "font_text_4")), (float)(meta.Size > 0 ? meta.Size : 28));
            using var paint = new SKPaint { Color = new SKColor(255, 240, 200, A(alpha)), IsAntialias = true };
            canvas.DrawText(title, (float)(n.X + n.W / 2), (float)(n.Y + 8 + font.Size), SKTextAlign.Center, font, paint);
        }

        if (n.Tag == "TmpWindow" && !(n.Get("close") is { } c && LayoutDocument.IsFalseValue(c)))
        {
            using var x = new SKPaint { IsStroke = true, StrokeWidth = 3, Color = new SKColor(220, 90, 80, A(alpha)), IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            float cx = rect.Right - 26, cy = rect.Top + 24;
            canvas.DrawLine(cx - 8, cy - 8, cx + 8, cy + 8, x);
            canvas.DrawLine(cx + 8, cy - 8, cx - 8, cy + 8, x);
        }
    }

    private static int MapDrawMode(string? dm) => (dm ?? "").ToLowerInvariant() switch
    {
        "hextend" => 1,
        "vextend" => 2,
        "extend" => 3,
        "stretch" => 4,
        "tile" => 5,
        _ => 0
    };

    /// <summary>
    /// 按 drawmode 把贴图画进 (0,0,w,h)：
    /// 默认 1:1 居中（乘 scale）；hextend/vextend 单向铺满；extend/stretch 双向铺满；tile 平铺。
    /// </summary>
    private static void DrawImage(SKCanvas canvas, LayoutImageRec rec, int drawmode, double w, double h, double sx, double sy, byte alpha)
    {
        if (!rec.Ready || rec.Page == null) return;
        var src = rec.SrcRect;
        double sw = rec.W * sx, sh = rec.H * sy;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha), FilterQuality = SKFilterQuality.Medium };

        switch (drawmode)
        {
            case 5: // tile
                for (double yy = 0; yy < h; yy += Math.Max(1, sh))
                    for (double xx = 0; xx < w; xx += Math.Max(1, sw))
                        canvas.DrawBitmap(rec.Page, src, new SKRect((float)xx, (float)yy, (float)(xx + sw), (float)(yy + sh)), paint);
                return;
            case 0: // natural：1:1 居中
                {
                    double dx = (w - sw) / 2, dy = (h - sh) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect((float)dx, (float)dy, (float)(dx + sw), (float)(dy + sh)), paint);
                    return;
                }
            case 1: // hextend：横向铺满，纵向自然居中
                {
                    double dy = (h - sh) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect(0, (float)dy, (float)w, (float)(dy + sh)), paint);
                    return;
                }
            case 2: // vextend
                {
                    double dx = (w - sw) / 2;
                    canvas.DrawBitmap(rec.Page, src, new SKRect((float)dx, 0, (float)(dx + sw), (float)h), paint);
                    return;
                }
            default: // extend / stretch
                canvas.DrawBitmap(rec.Page, src, new SKRect(0, 0, (float)w, (float)h), paint);
                return;
        }
    }

    /// <summary>Label / Button 文字：位图字体按 contentscale 缩放逐字贴；矢量字体用 NotoSans + 描边，支持换行 / 自适应缩小。</summary>
    private void DrawLabelText(SKCanvas canvas, LayoutNode n, double alpha)
    {
        var text = LayoutEngine.TextOf(n, _ctx);
        if (text.Length == 0) return;
        var fontName = LayoutEngine.FontNameOf(n, _ctx);
        var meta = LayoutFontProvider.GetFontMeta(fontName);
        var color = ParseColor(n.Get("color")) ?? new SKColor(255, 255, 255);
        int ha = n.Get("htextalign") is { } hv ? LayoutEngine.HAlignOf(hv) : 1;
        int va = n.Get("vtextalign") is { } vv ? LayoutEngine.VAlignOf(vv) : 1;
        double horblank = n.Num("horblank"); if (double.IsNaN(horblank)) horblank = 0;
        double verblank = n.Num("verblank"); if (double.IsNaN(verblank)) verblank = 0;
        bool wrap = n.Get("breakwords") is { } bw && !LayoutDocument.IsFalseValue(bw);
        bool adaptive = n.Get("adaptive") is { } ad && !LayoutDocument.IsFalseValue(ad);

        // 文字区域：EditBox 用 textpos 偏移，Label 用 bkmargin
        double tx0 = n.X, ty0 = n.Y, tw = n.W, th = n.H;
        if (n.Tag == "EditBox" && n.Get("textpos") is { } tp)
        {
            var p = tp.Split(',');
            if (p.Length >= 2 && double.TryParse(p[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var ox) && double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var oy))
            { tx0 += ox; ty0 += oy; tw -= ox; th -= oy; ha = 0; va = 0; }
        }

        canvas.Save();
        canvas.ClipRect(new SKRect((float)n.X - 2, (float)n.Y - 2, (float)(n.X + n.W) + 2, (float)(n.Y + n.H) + 2));

        double bmScale = 1;
        var bm = meta.IsBitmap ? LayoutFontProvider.GetBmFont(fontName, out bmScale) : null;
        if (bm is { Ready: true } bf)
        {
            double lineH = bf.LineHeight * bmScale;
            double mW = 0;
            foreach (var ch in text) mW += (bf.Glyphs.TryGetValue(ch, out var g) ? g.Advance * bmScale : lineH / 2) + horblank;
            double px = ha switch { 1 => tx0 + (tw - mW) / 2, 2 => tx0 + tw - mW, _ => tx0 };
            double baseY = va switch { 0 => ty0, 2 => ty0 + th - lineH, _ => ty0 + (th - lineH) / 2 };
            using var paint = new SKPaint { Color = color.WithAlpha(A(alpha * color.Alpha / 255.0)), IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            foreach (var ch in text)
            {
                if (!bf.Glyphs.TryGetValue(ch, out var g)) { px += lineH / 2 + horblank; continue; }
                if (bf.Page is { } page)
                {
                    var src = new SKRect(g.X, g.Y, g.X + g.W, g.Y + g.H);
                    var dst = new SKRect((float)(px + g.BearingX * bmScale), (float)(baseY + g.BearingY * bmScale),
                        (float)(px + (g.BearingX + g.W) * bmScale), (float)(baseY + (g.BearingY + g.H) * bmScale));
                    canvas.DrawBitmap(page, src, dst, paint);
                }
                px += g.Advance * bmScale + horblank;
            }
            canvas.Restore();
            return;
        }

        double fontSize = LayoutEngine.FontSizeOf(n, _ctx);
        var typeface = LayoutFontProvider.GetTypeface(_lang, LayoutFontProvider.GetDef(fontName));
        using var font = new SKFont(typeface, (float)fontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };

        // 自适应：文字比控件宽就缩小字号
        if (adaptive && tw > 0)
        {
            float full = font.MeasureText(text);
            if (full > tw) font.Size = (float)Math.Max(6, fontSize * tw / full);
        }

        var lines = wrap && tw > 0 ? WrapText(font, text, (float)tw) : text.Split('\n').ToList();
        float lh = (float)(font.Spacing + verblank);
        float blockH = lh * lines.Count;
        float startY = va switch { 0 => (float)ty0, 2 => (float)(ty0 + th) - blockH, _ => (float)(ty0 + (th - blockH) / 2) };
        float ascent = -font.Metrics.Ascent;
        float yPad = (lh - (font.Metrics.Descent - font.Metrics.Ascent)) / 2;

        using var fill = new SKPaint { Color = color.WithAlpha(A(alpha * color.Alpha / 255.0)), IsAntialias = true };
        SKPaint? stroke = null;
        if (meta.Outline > 0)
        {
            stroke = new SKPaint
            {
                Color = new SKColor(0, 0, 0, A(alpha * 0.9)),
                IsAntialias = true,
                StrokeWidth = (float)(meta.Outline * 2),
                Style = SKPaintStyle.Stroke,
                StrokeJoin = SKStrokeJoin.Round
            };
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            float lineW = font.MeasureText(line) + (float)(horblank * Math.Max(0, line.Length - 1));
            float x = ha switch { 1 => (float)(tx0 + (tw - lineW) / 2), 2 => (float)(tx0 + tw) - lineW, _ => (float)tx0 };
            float y = startY + i * lh + yPad + ascent + (float)meta.OffsetY;
            if (horblank != 0)
            {
                float cx = x;
                foreach (var ch in line)
                {
                    var s = ch.ToString();
                    if (stroke != null) canvas.DrawText(s, cx, y, SKTextAlign.Left, font, stroke);
                    canvas.DrawText(s, cx, y, SKTextAlign.Left, font, fill);
                    cx += font.MeasureText(s) + (float)horblank;
                }
            }
            else
            {
                if (stroke != null) canvas.DrawText(line, x, y, SKTextAlign.Left, font, stroke);
                canvas.DrawText(line, x, y, SKTextAlign.Left, font, fill);
            }
        }
        stroke?.Dispose();
        canvas.Restore();
    }

    /// <summary>按宽度折行：中日韩逐字可断，拉丁字按空格断。</summary>
    private static List<string> WrapText(SKFont font, string text, float maxW)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var cur = new System.Text.StringBuilder();
            int i = 0;
            while (i < para.Length)
            {
                // 取一个"词"：CJK 单字，或连续的非空白拉丁串
                int j = i;
                if (IsCjk(para[i])) j = i + 1;
                else { while (j < para.Length && !char.IsWhiteSpace(para[j]) && !IsCjk(para[j])) j++; if (j == i) j = i + 1; }
                string word = para[i..j];
                string candidate = cur.Length == 0 ? word.TrimStart() : cur + word;
                if (cur.Length > 0 && font.MeasureText(candidate) > maxW)
                {
                    lines.Add(cur.ToString().TrimEnd());
                    cur.Clear();
                    cur.Append(word.TrimStart());
                }
                else cur.Append(cur.Length == 0 ? word.TrimStart() : word);
                i = j;
            }
            lines.Add(cur.ToString().TrimEnd());
        }
        return lines;

        static bool IsCjk(char c) => c >= 0x2E80 && c <= 0x9FFF || c >= 0xAC00 && c <= 0xD7AF || c >= 0xF900 && c <= 0xFAFF || c >= 0xFF00 && c <= 0xFFEF;
    }

    private void DrawPill(SKCanvas canvas, string text, float x, float y, SKColor color)
    {
        using var font = new SKFont(UiTypeface, 11);
        float w = font.MeasureText(text) + 12;
        using var bg = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(x, y, x + w, y + 16), 3, 3, bg);
        using var fg = new SKPaint { Color = new SKColor(0x0F, 0x14, 0x19), IsAntialias = true };
        canvas.DrawText(text, x + 6, y + 12, SKTextAlign.Left, font, fg);
    }

    // ============================================================= 覆盖层 =============================================================
    private void DrawOverlay(SKCanvas canvas)
    {
        float hair = (float)(1 / _zoom);

        if (_hover is { } hv && !ReferenceEquals(hv, _selected) && hv.HasRect)
        {
            using var paint = new SKPaint { IsStroke = true, StrokeWidth = hair, Color = new SKColor(255, 255, 255, 140) };
            canvas.DrawRect(new SKRect((float)hv.X, (float)hv.Y, (float)(hv.X + hv.W), (float)(hv.Y + hv.H)), paint);
        }

        if (_selected is { } sel && sel.HasRect)
        {
            if (_showMargin) DrawMarginGuides(canvas, sel);

            using var selPaint = new SKPaint { IsStroke = true, StrokeWidth = hair * 1.5f, Color = new SKColor(79, 140, 255) };
            var r = new SKRect((float)sel.X, (float)sel.Y, (float)(sel.X + sel.W), (float)(sel.Y + sel.H));
            canvas.DrawRect(r, selPaint);

            canvas.Save();
            canvas.Scale(hair);  // 下面的标签按屏幕像素画，不随缩放变大
            float sx = (float)(sel.X / hair), sy = (float)(sel.Y / hair), ex = (float)((sel.X + sel.W) / hair), ey = (float)((sel.Y + sel.H) / hair);
            DrawPill(canvas, NodeLabel(sel), sx, sy - 18, new SKColor(79, 140, 255, 235));
            DrawPill(canvas, $"{Math.Round(sel.W)} × {Math.Round(sel.H)}", ex - 70, ey + 2, new SKColor(79, 140, 255, 235));

            if (!sel.IsVirtual)
            {
                foreach (var (hx, hy) in Handles(sel))
                {
                    using var fillP = new SKPaint { Color = new SKColor(79, 140, 255), IsAntialias = true };
                    using var edge = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = SKColors.White, IsAntialias = true };
                    var hr = new SKRect((float)(hx / hair) - 4, (float)(hy / hair) - 4, (float)(hx / hair) + 4, (float)(hy / hair) + 4);
                    canvas.DrawRect(hr, fillP);
                    canvas.DrawRect(hr, edge);
                }
            }
            canvas.Restore();
        }
    }

    private void DrawEmptyHint(SKCanvas canvas)
    {
        using var font = new SKFont(UiTypeface, 18);
        using var small = new SKFont(UiTypeface, 12);
        using var paint = new SKPaint { Color = new SKColor(0xE6, 0xE9, 0xEE), IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(0x9A, 0xA3, 0xAF), IsAntialias = true };
        float cx = (float)(_worldW / 2), cy = (float)(_worldH / 2);
        canvas.DrawText(Loc.T("Layout.Title"), cx, cy - 40, SKTextAlign.Center, font, paint);
        canvas.DrawText(Loc.T("Layout.Hint.Empty1"), cx, cy - 8, SKTextAlign.Center, small, dim);
        canvas.DrawText(Loc.T("Layout.Hint.Empty2"), cx, cy + 14, SKTextAlign.Center, small, dim);
        canvas.DrawText(Loc.T("Layout.Hint.Empty3"), cx, cy + 40, SKTextAlign.Center, small, dim);
    }

    private void DrawDropHint(SKCanvas canvas)
    {
        if (!_dragOver) return;
        using var paint = new SKPaint
        {
            IsStroke = true,
            StrokeWidth = 2,
            Color = PlaceholderColor.WithAlpha(200),
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 6f }, 0)
        };
        canvas.DrawRect(new SKRect(18, 18, (float)(_worldW - 18), (float)(_worldH - 18)), paint);
        using var font = new SKFont(UiTypeface, 22);
        using var fg = new SKPaint { Color = new SKColor(0x9F, 0xE6, 0xCF), IsAntialias = true };
        canvas.DrawText(Loc.T("Layout.Hint.Drop"), (float)(_worldW / 2), (float)(_worldH / 2), SKTextAlign.Center, font, fg);
    }

    /// <summary>鼠标旁的坐标 / 控件提示（屏幕坐标系）。</summary>
    private void DrawCoordTip(SKCanvas canvas)
    {
        if (!_mouseInCanvas || _drag != null) return;
        var (wx, wy) = ScreenToWorld(_mouseScreen.X, _mouseScreen.Y);
        var q = HitTest(wx, wy);
        var parts = $"({Math.Round(wx)}, {Math.Round(wy)})";
        if (q is { } n)
        {
            parts += $"  {NodeLabel(n)}  {Math.Round(n.W)}×{Math.Round(n.H)}";
            if (n.IsVirtual) parts += "  " + Loc.T("Layout.Hint.TemplateExpanded");
        }
        using var font = new SKFont(UiTypeface, 11);
        float tw = font.MeasureText(parts);
        float tx = (float)(_mouseScreen.X + 14);
        float ty = (float)(_mouseScreen.Y + 14);
        var bgRect = new SKRect(tx - 5, ty - 3, tx + tw + 5, ty + 14);
        using var bg = new SKPaint { Color = new SKColor(0x22, 0x27, 0x2E, 235), IsAntialias = true };
        canvas.DrawRoundRect(bgRect, 3, 3, bg);
        using var border = new SKPaint { IsStroke = true, StrokeWidth = 1, Color = new SKColor(0x46, 0x50, 0x5C), IsAntialias = true };
        canvas.DrawRoundRect(bgRect, 3, 3, border);
        using var fg = new SKPaint { Color = new SKColor(0xE6, 0xE9, 0xEE), IsAntialias = true };
        canvas.DrawText(parts, tx, ty + 10, SKTextAlign.Left, font, fg);
    }

    private static IEnumerable<(double x, double y)> Handles(LayoutNode n)
    {
        double l = n.X, t = n.Y, w = n.W, h = n.H;
        yield return (l, t);
        yield return (l + w / 2, t);
        yield return (l + w, t);
        yield return (l + w, t + h / 2);
        yield return (l + w, t + h);
        yield return (l + w / 2, t + h);
        yield return (l, t + h);
        yield return (l, t + h / 2);
    }

    private void DrawMarginGuides(SKCanvas canvas, LayoutNode sel)
    {
        var parent = sel.Parent;
        if (parent == null || !parent.HasRect) return;
        float hair = (float)(1 / _zoom);
        var guide = new SKColor(255, 212, 121, 191);

        using var parentPaint = new SKPaint { IsStroke = true, StrokeWidth = hair, Color = guide, PathEffect = SKPathEffect.CreateDash(new[] { 4f * hair, 3f * hair }, 0) };
        canvas.DrawRect(new SKRect((float)parent.X, (float)parent.Y, (float)(parent.X + parent.W), (float)(parent.Y + parent.H)), parentPaint);

        using var seg = new SKPaint { IsStroke = true, StrokeWidth = hair, Color = guide };
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
        float hair = (float)(1 / _zoom);
        float cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
        var text = Math.Round(val).ToString();
        canvas.Save();
        canvas.Scale(hair);
        using var font = new SKFont(UiTypeface, 10);
        float tw = font.MeasureText(text);
        using var bg = new SKPaint { Color = new SKColor(10, 16, 22, 209), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(cx / hair - tw / 2 - 3, cy / hair - 7, cx / hair + tw / 2 + 3, cy / hair + 7), 2, 2, bg);
        using var fg = new SKPaint { Color = new SKColor(0xFF, 0xD4, 0x79), IsAntialias = true };
        canvas.DrawText(text, cx / hair, cy / hair + 3.5f, SKTextAlign.Center, font, fg);
        canvas.Restore();
    }

    /// <summary>编辑器自己的提示文字字体（不是游戏字体）。</summary>
    private static SKTypeface UiTypeface => LayoutFontProvider.Typeface;

    private double MeasureTextSkia(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var font = new SKFont(LayoutFontProvider.GetTypeface(_lang), (float)fontSize);
        return font.MeasureText(text);
    }

    private static SKColor? ParseColor(string? v)
        => LayoutColor.TryParse(v, out var r, out var g, out var b, out var a) ? new SKColor(r, g, b, a) : null;

    // ============================================================= 鼠标交互 =============================================================

    /// <summary>
    /// 命中测试：从最上层往下找。与父级矩形完全重合的"填充型"节点（无贴图的 Image、填满的分组）
    /// 让位给更小的目标，否则大面积的背景会把下面所有控件都挡住点不到。
    /// </summary>
    private LayoutNode? HitTest(double wx, double wy)
    {
        LayoutNode? fallback = null;
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            var q = _rects[i];
            if (ReferenceEquals(q.Node, _form)) continue;
            if (!(wx >= q.X && wx <= q.X + q.W && wy >= q.Y && wy <= q.Y + q.H)) continue;
            if (!_ghost && LayoutDocument.IsHidden(q.Node)) continue;

            bool fillsParent = q.Node.Parent is { HasRect: true } p
                && Math.Abs(p.X - q.X) < 0.5 && Math.Abs(p.Y - q.Y) < 0.5 && Math.Abs(p.W - q.W) < 0.5 && Math.Abs(p.H - q.H) < 0.5;
            if (fillsParent && !q.Node.Has("width") && !q.Node.Has("height"))
            {
                fallback ??= q.Node;
                continue;
            }
            return q.Node;
        }
        return fallback;
    }

    private void Canvas_MouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        Keyboard.Focus(_canvas);
        var p = e.GetPosition(_canvas);
        var (wx, wy) = ScreenToWorld(p.X, p.Y);
        double grab = 6 / _zoom;

        if (_selected is { } sel && sel.HasRect && !sel.IsVirtual)
        {
            foreach (var (hx, hy) in Handles(sel))
            {
                if (Math.Abs(wx - hx) <= grab && Math.Abs(wy - hy) <= grab)
                {
                    _drag = new DragState { Type = "resize", Handle = HandleName(hx, hy, sel), Sx = wx, Sy = wy, Sw = sel.W, Sh = sel.H, StartMargins = sel.Margins(), Node = sel };
                    _canvas.CaptureMouse();
                    return;
                }
            }
        }

        var hit = HitTest(wx, wy);
        if (hit != null)
        {
            var on = OwnerOf(hit) ?? hit;
            if (!ReferenceEquals(on, _selected)) SelectNode(on);
            _drag = new DragState { Type = "move", Sx = wx, Sy = wy, StartMargins = on.Margins(), Node = on };
            _canvas.CaptureMouse();
        }
        else
        {
            SelectNode(null);
        }
    }

    private static string HandleName(double hx, double hy, LayoutNode n)
    {
        bool left = Math.Abs(hx - n.X) < 0.6, right = Math.Abs(hx - (n.X + n.W)) < 0.6;
        bool top = Math.Abs(hy - n.Y) < 0.6, bottom = Math.Abs(hy - (n.Y + n.H)) < 0.6;
        string h = top ? "n" : bottom ? "s" : "";
        h += left ? "w" : right ? "e" : "";
        return h.Length == 0 ? "se" : h;
    }

    private void Canvas_MouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is { Moved: true, Node: { } } d && d.Type != "pan")
        {
            RefreshTree();
            RenderProps();
        }
        _drag = null;
        _canvas.ReleaseMouseCapture();
        Repaint();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        _mouseScreen = p;
        _mouseInCanvas = true;

        if (_drag is { Type: "pan" } pan)
        {
            _autoFit = false;
            _panX += p.X - pan.Sx;
            _panY += p.Y - pan.Sy;
            pan.Sx = p.X; pan.Sy = p.Y;
            Repaint();
            return;
        }

        var (wx, wy) = ScreenToWorld(p.X, p.Y);

        if (_drag is { } d && d.Node != null)
        {
            bool snap = _snap && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
            double dx = wx - d.Sx, dy = wy - d.Sy;
            if (snap) { dx = Math.Round(dx / SnapStep) * SnapStep; dy = Math.Round(dy / SnapStep) * SnapStep; }
            dx = Math.Round(dx); dy = Math.Round(dy);
            if (dx == 0 && dy == 0 && !d.Moved) return;

            if (!d.Moved)
            {
                PushUndo();
                d.Moved = true;
            }

            if (d.Type == "move")
            {
                var m = d.StartMargins;
                ApplyOffset(d.Node, ref m, dx, dy);
                d.Node.Set("margin", m.ToString());
            }
            else
            {
                double nw = d.Sw, nh = d.Sh;
                var m = d.StartMargins;
                int ha = LayoutEngine.HAlignOf(d.Node.Get("halign"));
                int va = LayoutEngine.VAlignOf(d.Node.Get("valign"));
                if (d.Handle.Contains('e')) { nw = d.Sw + dx; if (ha == 2) m.R -= dx; }
                if (d.Handle.Contains('s')) { nh = d.Sh + dy; if (va == 2) m.B -= dy; }
                if (d.Handle.Contains('w')) { nw = d.Sw - dx; if (ha != 2) m.L += dx; }
                if (d.Handle.Contains('n')) { nh = d.Sh - dy; if (va != 2) m.T += dy; }
                nw = Math.Max(1, Math.Round(nw));
                nh = Math.Max(1, Math.Round(nh));
                d.Node.Set("width", nw.ToString(CultureInfo.InvariantCulture));
                d.Node.Set("height", nh.ToString(CultureInfo.InvariantCulture));
                d.Node.Set("margin", m.ToString());
            }
            Repaint();
            UpdateGeoCard();
            return;
        }

        var hit = HitTest(wx, wy);
        _hover = hit;
        bool onHandle = false;
        if (_selected is { HasRect: true, IsVirtual: false } s)
        {
            double grab = 6 / _zoom;
            foreach (var (hx, hy) in Handles(s))
                if (Math.Abs(wx - hx) <= grab && Math.Abs(wy - hy) <= grab) { onHandle = true; break; }
        }
        _canvas.Cursor = onHandle ? Cursors.SizeAll : hit != null ? Cursors.Hand : Cursors.Arrow;
        Repaint();
    }

    /// <summary>
    /// 把位移换成 margin 改动：左对齐改 L、右对齐改 R、居中则 L/R 对称移动（居中偏移量 = (L-R)/2，所以各移 dx）。
    /// 垂直同理。
    /// </summary>
    private static void ApplyOffset(LayoutNode node, ref LayoutMargins m, double dx, double dy)
    {
        int ha = LayoutEngine.HAlignOf(node.Get("halign"));
        int va = LayoutEngine.VAlignOf(node.Get("valign"));
        switch (ha)
        {
            case 2: m.R -= dx; break;
            case 1: m.L += dx; m.R -= dx; break;
            default: m.L += dx; break;
        }
        switch (va)
        {
            case 2: m.B -= dy; break;
            case 1: m.T += dy; m.B -= dy; break;
            default: m.T += dy; break;
        }
    }

    private void Canvas_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        _drag = new DragState { Type = "pan", Sx = p.X, Sy = p.Y };
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

    private void Canvas_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        double step = shift ? 10 : 1;

        switch (e.Key)
        {
            case Key.Z when ctrl: Undo(); e.Handled = true; return;
            case Key.Y when ctrl: Redo(); e.Handled = true; return;
            case Key.S when ctrl: OnSave(); e.Handled = true; return;
            case Key.D when ctrl: OnDuplicate(); e.Handled = true; return;
            case Key.Delete: OnDeleteNode(); e.Handled = true; return;
            case Key.Escape: SelectNode(null); e.Handled = true; return;
            case Key.Left: Nudge(-step, 0); e.Handled = true; return;
            case Key.Right: Nudge(step, 0); e.Handled = true; return;
            case Key.Up: Nudge(0, -step); e.Handled = true; return;
            case Key.Down: Nudge(0, step); e.Handled = true; return;
        }
    }

    private void Nudge(double dx, double dy)
    {
        if (_selected is not { IsVirtual: false } n || ReferenceEquals(n, _form)) return;
        PushUndo();
        var m = n.Margins();
        ApplyOffset(n, ref m, dx, dy);
        n.Set("margin", m.ToString());
        Repaint();
        RenderProps();
    }

    // ============================================================= 属性写入 =============================================================

    /// <summary>属性面板 / 拖动统一走这里改属性：记撤销、触发重算、刷新引用表。</summary>
    private void SetAttr(LayoutNode n, string key, string? value, bool recordUndo = true)
    {
        var old = n.Get(key);
        if (string.Equals(old, value, StringComparison.Ordinal)) return;
        if (string.IsNullOrEmpty(old) && string.IsNullOrEmpty(value)) return;
        if (recordUndo) PushUndo();
        n.Set(key, value);
        if (key is "templateid" or "itemcount" or "itemwidth" or "itemheight")
        {
            if (_form != null) _doc.BuildExpansion(_form);
        }
        if (n.Tag == "Template" && key == "id") _doc.RebuildIndex();
        if (ImageKeys.Contains(key)) InvalidateUsage();
        if (key == "id" && ReferenceEquals(n, _form)) RefreshFormList();
        Repaint();
    }

    // ============================================================= 节点操作 =============================================================
    private void OnAddChild()
    {
        var n = _selected ?? _form;
        if (n == null) return;
        var owner = OwnerOf(n) ?? n;

        var menu = new ContextMenu();
        EditorTheme.Apply(menu);
        foreach (var tag in LayoutSchema.CreatableTags)
        {
            var item = new MenuItem { Header = $"{TagName(tag)}  ({tag})" };
            string t = tag;
            item.Click += (_, _) => AddChild(owner, t);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = _btnAddChild;
        menu.IsOpen = true;
    }

    private void AddChild(LayoutNode parent, string tag)
    {
        PushUndo();
        var node = new LayoutNode { Tag = tag, Parent = parent };
        node.Set("id", $"{tag.ToLowerInvariant()}_{parent.Children.Count + 1}");
        switch (tag)
        {
            case "Label": node.Set("font", "font_text_2"); node.Set("string", "Text"); node.Set("width", "120"); node.Set("height", "30"); break;
            case "Button": node.Set("normalimage", "btn_rect.png"); node.Set("drawmode", "hextend"); node.Set("width", "120"); node.Set("height", "40"); break;
            case "Image": node.Set("image", ""); node.Set("width", "64"); node.Set("height", "64"); break;
            case "GroupBox": case "Group": node.Set("width", "200"); node.Set("height", "120"); break;
            case "Repeater": node.Set("itemwidth", "100"); node.Set("itemheight", "100"); node.Set("itemcount", "4"); break;
            case "PlaceHolder": node.Set("templateid", _doc.Templates.Keys.FirstOrDefault() ?? ""); break;
            case "HGroup": case "VGroup": node.Set("gap", "10"); break;
        }
        if (!node.Has("margin")) node.Set("margin", "0,0,0,0");
        parent.Children.Add(node);
        parent.Collapsed = false;
        if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        SelectNode(node);
    }

    private void OnAddForm()
    {
        var menu = new ContextMenu();
        EditorTheme.Apply(menu);
        foreach (var tag in LayoutSchema.TopLevelTags)
        {
            var item = new MenuItem { Header = $"{TagName(tag)}  ({tag})" };
            string t = tag;
            item.Click += (_, _) =>
            {
                PushUndo();
                var container = _doc.TopLevelContainer;
                var node = new LayoutNode { Tag = t, Parent = container };
                node.Set("id", $"{(t == "Template" ? "tmp" : "form")}_new_{_doc.Forms.Count + 1}");
                if (t == "TmpWindow") { node.Set("width", "600"); node.Set("height", "400"); node.Set("modal", "true"); node.Set("fade", "true"); node.Set("close", "true"); }
                container.Children.Add(node);
                _doc.RebuildIndex();
                _doc.BuildExpansion(node);
                _form = node;
                _selected = null;
                RefreshFormList();
                RefreshTree();
                RenderProps();
                Repaint();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnDuplicate()
    {
        var n = _selected;
        if (n?.Parent == null || n.IsVirtual) return;
        PushUndo();
        var clone = n.DeepClone();
        clone.Parent = n.Parent;
        int i = n.Parent.Children.IndexOf(n);
        n.Parent.Children.Insert(i + 1, clone);
        if (_doc.Forms.Contains(n)) _doc.RebuildIndex();
        if (_doc.Forms.Contains(clone)) _doc.BuildExpansion(clone);
        else if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        if (_doc.Forms.Contains(clone)) { _form = clone; RefreshFormList(); }
        SelectNode(clone);
    }

    private void OnDeleteNode()
    {
        var n = _selected;
        if (n?.Parent == null || n.IsVirtual) return;
        bool isForm = _doc.Forms.Contains(n);
        if (isForm && MessageBox.Show(Loc.F("Layout.Dlg.DeleteForm", n.Get("id") ?? n.Tag), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        PushUndo();
        var parent = n.Parent;
        parent.Children.Remove(n);
        if (isForm)
        {
            _doc.RebuildIndex();
            _form = _doc.Forms.FirstOrDefault();
            _selected = null;
            RefreshFormList();
            RefreshTree();
            RenderProps();
            Repaint();
            return;
        }
        if (_form != null) _doc.BuildExpansion(_form);
        InvalidateUsage();
        SelectNode(parent);
    }

    /// <summary>在同级里上移 / 下移（改变绘制顺序 = 改变层级）。</summary>
    private void OnMove(int delta)
    {
        var n = _selected;
        if (n?.Parent == null || n.IsVirtual) return;
        var list = n.Parent.Children;
        int i = list.IndexOf(n);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        PushUndo();
        list.RemoveAt(i);
        list.Insert(j, n);
        if (_doc.Forms.Contains(n)) { _doc.RebuildIndex(); RefreshFormList(); }
        else if (_form != null) _doc.BuildExpansion(_form);
        RefreshTree();
        Repaint();
    }

    private void SetStatus(string msg)
    {
        if (_statusText != null) _statusText.Text = msg;
    }

    /// <summary>节点的简短标签："标签 #id"。</summary>
    private static string NodeLabel(LayoutNode n) => TagName(n.Tag) + (n.Get("id") is { Length: > 0 } id ? " #" + id : "");

    /// <summary>标签的本地化显示名（未知标签原样显示）。</summary>
    private static string TagName(string tag)
    {
        string key = "Layout.Tag." + tag;
        return Loc.Has(key) ? Loc.T(key) : tag;
    }
}
