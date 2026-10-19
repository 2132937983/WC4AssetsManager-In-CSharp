using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 数据检查场景（对应 VB ParserScene"检查数据"）：只读打开 BTL / BIN 文件，按内容识别文件种类与布局，
/// 提供概况（头部字段 + 检查提示 + 数据块地址表）、按数据块浏览记录、十六进制查看（跳转 / 字节查找）、
/// 两个文件的二进制对比和 BTL v1 ↔ v3 转换。
/// <para>
/// 解析、定位、对比、转换都在 Core（<see cref="BtlInspector"/> / <see cref="BinaryCompare"/> /
/// <see cref="Core.Services.BtlVersionConverter"/>）里完成，场景只负责展示。
/// 打开的文件只读；转换只写用户在保存对话框里选定的新文件，不能覆盖源文件。
/// </para>
/// </summary>
public sealed partial class DataCheckScene : UserControl
{
    /// <summary>开始界面 / AI open_scene / --scene 使用的场景名。</summary>
    public const string SceneName = "datacheck";

    private readonly MainWindow _window;
    private readonly List<(UIElement Content, UIElement Hint)> _fileViews = new();
    private BtlInspector? _inspector;
    private InspectLayoutOverride _inspectorLayout;
    private string? _filePath;
    private int _loadGeneration;

    private ComboBox _layoutBox = null!;
    private TextBlock _fileInfo = null!;
    private TabControl _tabs = null!;
    private TabItem _hexTab = null!;
    private TextBlock _statusText = null!;

    public DataCheckScene(MainWindow window, string? filePath = null)
    {
        EditorTheme.Apply(this);
        _window = window;
        BuildUI();
        ShowInspector(null);

        Focusable = true;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
        PreviewKeyDown += OnPreviewKeyDown;

        if (!string.IsNullOrWhiteSpace(filePath)) LoadFile(filePath);
    }

    /// <summary>当前选择的布局解释方式（自动 / 强制战役 / 强制征服），对本场景里所有读取都生效。</summary>
    private InspectLayoutOverride SelectedLayout
        => (_layoutBox.SelectedItem as ComboBoxItem)?.Tag is InspectLayoutOverride layout ? layout : InspectLayoutOverride.Auto;

    // ============================================================= 布局 =============================================================

    private void BuildUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        _layoutBox = EditorTheme.ComboBox(150);
        foreach (var layout in Enum.GetValues<InspectLayoutOverride>())
            _layoutBox.Items.Add(new ComboBoxItem { Content = Loc.T("DataCheck.Scene.Layout." + layout), Tag = layout });
        _layoutBox.SelectedIndex = 0;
        _layoutBox.ToolTip = Loc.T("DataCheck.Scene.LayoutTip");
        _layoutBox.SelectionChanged += (_, _) =>
        {
            ReloadCurrent();
            InspectConvertSource();
        };

        var layoutLabel = EditorTheme.Muted(Loc.T("DataCheck.Scene.LayoutLabel"));
        layoutLabel.VerticalAlignment = VerticalAlignment.Center;
        layoutLabel.Margin = new Thickness(14, 0, 6, 0);

        var topBar = EditorTheme.TopBar(Loc.T("DataCheck.Title"), (_, _) => _window.ReturnToMainScene(),
            EditorTheme.PrimaryButton(Loc.T("DataCheck.Scene.OpenFile"), (_, _) => PickAndLoad()),
            layoutLabel,
            _layoutBox,
            EditorTheme.Button(Loc.T("Common.Reload"), (_, _) => ReloadCurrent()));
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        _fileInfo = new TextBlock { Margin = new Thickness(14, 7, 14, 7), TextTrimming = TextTrimming.CharacterEllipsis };
        var infoBar = new Border
        {
            Background = EditorTheme.Bg1,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _fileInfo
        };
        Grid.SetRow(infoBar, 1);
        root.Children.Add(infoBar);

        _hexTab = new TabItem { Header = Loc.T("DataCheck.Tab.Hex"), Content = FileView(BuildHexTab()) };
        _tabs = new TabControl();
        _tabs.Items.Add(new TabItem { Header = Loc.T("DataCheck.Tab.Overview"), Content = FileView(BuildOverviewTab()) });
        _tabs.Items.Add(new TabItem { Header = Loc.T("DataCheck.Tab.Records"), Content = FileView(BuildRecordsTab()) });
        _tabs.Items.Add(_hexTab);
        _tabs.Items.Add(new TabItem { Header = Loc.T("DataCheck.Tab.Compare"), Content = BuildCompareTab() });
        _tabs.Items.Add(new TabItem { Header = Loc.T("DataCheck.Tab.Convert"), Content = BuildConvertTab() });
        Grid.SetRow(_tabs, 2);
        root.Children.Add(_tabs);

        var status = EditorTheme.StatusBar(out _statusText, Loc.T("DataCheck.Scene.Ready"));
        Grid.SetRow(status, 3);
        root.Children.Add(status);

        Content = root;
    }

    /// <summary>依赖已打开文件的页：没有文件时盖一层"请先打开文件"的提示。</summary>
    private Grid FileView(UIElement content)
    {
        var hint = EditorTheme.EmptyHint(Loc.T("DataCheck.Scene.NoFileHint"));
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(hint);
        _fileViews.Add((content, hint));
        return grid;
    }

    // ============================================================= 加载 =============================================================

    private void PickAndLoad()
    {
        string? path = PickFile(Loc.T("DataCheck.Scene.OpenTitle"), _filePath);
        if (path != null) LoadFile(path);
    }

    private void ReloadCurrent()
    {
        if (_filePath != null) LoadFile(_filePath);
    }

    /// <summary>只读加载文件（后台线程）；失败时保留当前文件，只在状态栏报错。</summary>
    private async void LoadFile(string path)
    {
        int generation = ++_loadGeneration;
        var layout = SelectedLayout;
        SetStatus(Loc.F("DataCheck.Scene.Loading", IOPath.GetFileName(path)));
        try
        {
            var inspector = await Task.Run(() => BtlInspector.Load(path, layout));
            if (generation != _loadGeneration) return;

            _filePath = path;
            _inspectorLayout = layout;
            ShowInspector(inspector);
            SetStatus(Loc.F("DataCheck.Scene.Loaded", IOPath.GetFileName(path), BtlInspector.KindDisplayName(inspector.Kind),
                            inspector.LayoutStatusText));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (generation == _loadGeneration) SetStatus(Loc.F("DataCheck.LoadFailed", ex.Message), true);
        }
    }

    private void ShowInspector(BtlInspector? inspector)
    {
        _inspector = inspector;
        foreach (var (content, hint) in _fileViews)
        {
            content.Visibility = inspector != null ? Visibility.Visible : Visibility.Collapsed;
            hint.Visibility = inspector != null ? Visibility.Collapsed : Visibility.Visible;
        }

        UpdateFileInfo();
        RefreshOverview();
        RefreshRecords();
        RefreshHex();
        OfferCurrentFileToTools();
    }

    private void UpdateFileInfo()
    {
        var inspector = _inspector;
        if (inspector == null || _filePath == null)
        {
            _fileInfo.Text = Loc.T("DataCheck.Scene.NoFile");
            _fileInfo.Foreground = EditorTheme.TextMuted;
            _fileInfo.ToolTip = null;
            return;
        }

        string version = inspector.World != null
            ? Loc.F("DataCheck.Scene.WorldVersion", inspector.World.Version)
            : Loc.F("DataCheck.Scene.BtlVersion", inspector.BtlVersion);
        _fileInfo.Text = Loc.F("DataCheck.Scene.FileInfo", IOPath.GetFileName(_filePath), BtlInspector.KindDisplayName(inspector.Kind),
                               version, inspector.FileLength, inspector.LayoutStatusText);
        _fileInfo.Foreground = LayoutBrush(inspector);
        _fileInfo.ToolTip = _filePath;
    }

    /// <summary>布局吻合情况的颜色：精确 = 成功色，有未解析字节 = 警告色，截断 / 无法识别 = 危险色。</summary>
    private static Brush LayoutBrush(BtlInspector inspector)
    {
        if (inspector.Kind == InspectedFileKind.Unknown || inspector.Layout.MissingBytes > 0) return EditorTheme.Danger;
        return inspector.Layout.IsExact ? EditorTheme.Success : EditorTheme.Warning;
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusText.Text = text;
        _statusText.Foreground = error ? EditorTheme.Danger : EditorTheme.TextMuted;
    }

    /// <summary>打开文件对话框（.btl / .bin），默认目录 = 参考文件所在目录，否则游戏关卡目录；取消返回 null。</summary>
    private static string? PickFile(string title, string? near)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{Loc.T("DataCheck.Scene.FileFilter")} (*.btl;*.bin)|*.btl;*.bin|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            CheckFileExists = true
        };
        string? directory = near != null ? IOPath.GetDirectoryName(near) : IOPath.Combine(AssetManager.GetDefaultAssetsPath(), "stage");
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) dialog.InitialDirectory = directory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    // ============================================================= 输入 =============================================================

    private static string? DroppedFile(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && File.Exists(files[0]) ? files[0] : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        string? file = DroppedFile(e);
        if (file != null) LoadFile(file);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.O && modifiers == ModifierKeys.Control)
        {
            PickAndLoad();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && modifiers == ModifierKeys.None)
        {
            ReloadCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && ReferenceEquals(_tabs.SelectedItem, _hexTab))
        {
            FindNext(modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    // ============================================================= 共用控件 =============================================================

    /// <summary>
    /// 各数据块的标识色（十六进制着色、地址表色块、图例共用）。这是数据颜色（区分数据块），不属于界面主题。
    /// </summary>
    private static readonly Dictionary<BtlBlock, Brush> BlockBrushes = new()
    {
        [BtlBlock.Header] = Frozen(0x9A, 0xA8, 0xBE),
        [BtlBlock.Legion] = Frozen(0xE6, 0xB4, 0x50),
        [BtlBlock.Terrain] = Frozen(0x7C, 0xC4, 0x6E),
        [BtlBlock.Province] = Frozen(0x58, 0xB4, 0xD9),
        [BtlBlock.Belong] = Frozen(0x7A, 0x9C, 0xFF),
        [BtlBlock.Building] = Frozen(0xE5, 0x8B, 0x4B),
        [BtlBlock.Army] = Frozen(0xF0, 0x6A, 0x62),
        [BtlBlock.Trap] = Frozen(0xC6, 0x78, 0xDD),
        [BtlBlock.Case] = Frozen(0xA9, 0xDC, 0x76),
        [BtlBlock.Weather] = Frozen(0x56, 0xC8, 0xC2),
        [BtlBlock.Event] = Frozen(0xD1, 0x9A, 0x66),
        [BtlBlock.Reinforcement] = Frozen(0xFF, 0x8F, 0xB0),
        [BtlBlock.AirRaid] = Frozen(0x61, 0xAF, 0xEF),
        [BtlBlock.UnitPlacement] = Frozen(0xB4, 0xA0, 0xFF),
        [BtlBlock.Capital] = Frozen(0xFF, 0xD8, 0x66),
        [BtlBlock.Strategy] = Frozen(0x8E, 0xE0, 0xA8),
        [BtlBlock.AirSupport] = Frozen(0x78, 0xDC, 0xE8),
        [BtlBlock.Extension] = Frozen(0xFF, 0xA8, 0x6E),
        [BtlBlock.Unparsed] = Frozen(0xFF, 0x53, 0x70)
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush BlockBrush(BtlBlock block) => BlockBrushes.TryGetValue(block, out var brush) ? brush : EditorTheme.TextDim;

    private static string Hex8(long value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>带列的只读表格（GridView）；Ctrl+C 复制选中行（每行是该行对象的 ToString，制表符分隔）。</summary>
    private static ListView CreateTable(params (string Header, string Path, double Width)[] columns)
    {
        var view = new GridView();
        foreach (var (header, path, width) in columns)
            view.Columns.Add(new GridViewColumn { Header = header, DisplayMemberBinding = new Binding(path), Width = width });
        return CreateTable(view);
    }

    private static ListView CreateTable(GridView view)
    {
        var list = new ListView { View = view, SelectionMode = SelectionMode.Extended };
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);
        list.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopyRows(list),
                                                    (_, e) => e.CanExecute = list.SelectedItems.Count > 0));
        return list;
    }

    private static void CopyRows(ListView list)
    {
        var rows = list.SelectedItems.Cast<object>().OrderBy(list.Items.IndexOf).Select(o => o.ToString());
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, rows));
        }
        catch (ExternalException)
        {
            // 剪贴板被其它程序占用：放弃这次复制
        }
    }

    /// <summary>"色块 + 文本"的表格列（数据块名一栏）。</summary>
    private static GridViewColumn SwatchColumn(string header, string brushPath, string textPath, double width)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(WidthProperty, 10.0);
        swatch.SetValue(HeightProperty, 10.0);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        swatch.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        swatch.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        swatch.SetBinding(Border.BackgroundProperty, new Binding(brushPath));
        panel.AppendChild(swatch);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        panel.AppendChild(text);

        return new GridViewColumn { Header = header, Width = width, CellTemplate = new DataTemplate { VisualTree = panel } };
    }

    /// <summary>行距更紧凑的列表项样式（基于主题的隐式样式）。</summary>
    private Style CompactItemStyle(Type itemType, Thickness padding)
    {
        var style = new Style(itemType, (Style)FindResource(itemType));
        style.Setters.Add(new Setter(PaddingProperty, padding));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        return style;
    }

    /// <summary>
    /// 虚拟记录列表的一项：哪个检查器、哪个数据块、第几条。检查器按引用比较，
    /// 换文件或换数据块后旧容器不会被当成"同一项"复用而显示旧文本。
    /// </summary>
    private sealed record RecordRef(BtlInspector Inspector, BtlBlock Block, int Index)
    {
        public override string ToString() => Inspector.DescribeRecord(Block, Index);
    }

    /// <summary>
    /// 一个数据块全部记录的只读虚拟列表：列表框只为可见行取项，世界文件上百万格也不必预先建对象；
    /// IndexOf 直接取序号（选中时不必遍历）。
    /// </summary>
    private sealed class RecordRefList : IList
    {
        private readonly BtlInspector _inspector;
        private readonly BtlBlock _block;

        public RecordRefList(BtlInspector inspector, BtlBlock block)
        {
            _inspector = inspector;
            _block = block;
            Count = inspector.GetRecordCount(block);
        }

        public int Count { get; }
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public object? this[int index]
        {
            get => (uint)index < (uint)Count ? new RecordRef(_inspector, _block, index) : throw new ArgumentOutOfRangeException(nameof(index));
            set => throw new NotSupportedException();
        }

        public int IndexOf(object? value)
            => value is RecordRef r && ReferenceEquals(r.Inspector, _inspector) && r.Block == _block && (uint)r.Index < (uint)Count ? r.Index : -1;

        public bool Contains(object? value) => IndexOf(value) >= 0;

        public IEnumerator GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return new RecordRef(_inspector, _block, i);
        }

        public void CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++) array.SetValue(new RecordRef(_inspector, _block, i), index + i);
        }

        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    /// <summary>字段表格的一行（记录详情、头部字段共用）。</summary>
    private sealed record FieldRow(string Offset, string Relative, string Name, string Value, string Hex, string Binary,
                                   string Meaning, string Note, long FileOffset, int Size)
    {
        public static FieldRow From(BtlFieldValue value)
            => new(Hex8(value.Offset), "+0x" + value.Field.Offset.ToString("X2", CultureInfo.InvariantCulture), value.Field.DisplayName,
                   value.Value, value.Hex, value.Binary, value.Meaning, value.Field.Note ?? "", value.Offset, Math.Max(1, value.Field.Size));

        public override string ToString() => string.Join('\t', Offset, Relative, Name, Value, Hex, Binary, Meaning, Note);
    }
}
