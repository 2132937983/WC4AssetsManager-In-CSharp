using System.Globalization;
using System.Windows.Controls.Primitives;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.GUI.Views;

// 数据检查场景：十六进制页（转储 + 跳转 + 字节查找 + 选中字节解读 + 数据块图例）。
public sealed partial class DataCheckScene
{
    /// <summary>查找时最多记录的匹配数（超过只提示"+"，仍可在前这么多处之间跳）。</summary>
    private const int MaxFindMatches = 100_000;

    private DataCheckHexView _hexView = null!;
    private ScrollBar _hexScroll = null!;
    private TextBox _jumpBox = null!;
    private TextBox _findBox = null!;
    private TextBlock _findInfo = null!;
    private TextBlock _byteOffsetText = null!;
    private TextBlock _byteLocationText = null!;
    private TextBlock _byteFieldText = null!;
    private ListView _interpretList = null!;
    private ListBox _legendList = null!;
    private bool _syncingScroll;

    private string? _findPatternKey;
    private List<long> _findMatches = new();
    private bool _findTruncated;
    private int _findPatternLength;

    /// <summary>"按类型解读"表的一行。</summary>
    private sealed record InterpretRow(string Type, string Value)
    {
        public override string ToString() => Type + "\t" + Value;
    }

    /// <summary>数据块图例的一项。</summary>
    private sealed record LegendItem(BtlSegment Segment, Brush Brush, string Name, string Range);

    private UIElement BuildHexTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330), MinWidth = 240 });

        // 左：工具条 + 十六进制视图
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        AddRow(left, 0, EditorTheme.ToolBar(BuildHexToolbar()));

        var viewGrid = new Grid();
        viewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        viewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hexView = new DataCheckHexView(BlockBrush);
        _hexView.ViewChanged += SyncHexScroll;
        _hexView.SelectionChanged += UpdateByteInfo;
        viewGrid.Children.Add(_hexView);
        _hexScroll = new ScrollBar { Orientation = Orientation.Vertical, SmallChange = 1 };
        _hexScroll.ValueChanged += (_, e) =>
        {
            if (!_syncingScroll) _hexView.TopLine = (long)Math.Round(e.NewValue);
        };
        Grid.SetColumn(_hexScroll, 1);
        viewGrid.Children.Add(_hexScroll);
        AddRow(left, 1, viewGrid);
        grid.Children.Add(left);

        AddSplitter(grid, 1);

        var right = BuildHexSidePanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private Panel BuildHexToolbar()
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };

        var jump = EditorTheme.SearchBox(Loc.T("DataCheck.Hex.JumpPlaceholder"), out _jumpBox, 150);
        jump.ToolTip = Loc.T("DataCheck.Hex.JumpTip");
        _jumpBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            JumpToOffset();
            e.Handled = true;
        };
        bar.Children.Add(jump);
        bar.Children.Add(Spaced(EditorTheme.ToolButton(Loc.T("DataCheck.Hex.Jump"), (_, _) => JumpToOffset())));
        bar.Children.Add(EditorTheme.VSeparator());

        var find = EditorTheme.SearchBox(Loc.T("DataCheck.Hex.FindPlaceholder"), out _findBox, 220);
        find.ToolTip = Loc.T("DataCheck.Hex.FindTip");
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        };
        bar.Children.Add(find);
        bar.Children.Add(Spaced(EditorTheme.ToolButton(Loc.T("DataCheck.Hex.FindPrevious"), (_, _) => FindNext(-1))));
        bar.Children.Add(Spaced(EditorTheme.ToolButton(Loc.T("DataCheck.Hex.FindNext"), (_, _) => FindNext(1))));

        _findInfo = EditorTheme.Muted("");
        _findInfo.VerticalAlignment = VerticalAlignment.Center;
        _findInfo.Margin = new Thickness(10, 0, 0, 0);
        bar.Children.Add(_findInfo);
        return bar;
    }

    private static FrameworkElement Spaced(FrameworkElement element)
    {
        element.Margin = new Thickness(6, 0, 0, 0);
        return element;
    }

    private UIElement BuildHexSidePanel()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddRow(panel, 0, EditorTheme.SectionHeader(Loc.T("DataCheck.Hex.Selection")));
        var info = new StackPanel();
        _byteOffsetText = InfoText();
        _byteLocationText = InfoText();
        _byteFieldText = InfoText();
        info.Children.Add(EditorTheme.FormRow(Loc.T("DataCheck.Hex.Offset"), _byteOffsetText, 86));
        info.Children.Add(EditorTheme.FormRow(Loc.T("DataCheck.Hex.Location"), _byteLocationText, 86));
        info.Children.Add(EditorTheme.FormRow(Loc.T("DataCheck.Hex.FieldValue"), _byteFieldText, 86));
        AddRow(panel, 1, info);

        _interpretList = CreateTable(
            (Loc.T("DataCheck.Column.Type"), nameof(InterpretRow.Type), 60),
            (Loc.T("DataCheck.Column.Value"), nameof(InterpretRow.Value), 200));
        _interpretList.FontFamily = EditorTheme.MonoFontFamily;
        _interpretList.FontSize = 12;
        AddRow(panel, 2, _interpretList);

        AddRow(panel, 3, EditorTheme.SectionHeader(Loc.T("DataCheck.Hex.Legend")));
        _legendList = new ListBox { ToolTip = Loc.T("DataCheck.Hex.LegendTip") };
        _legendList.ItemContainerStyle = CompactItemStyle(typeof(ListBoxItem), new Thickness(8, 3, 8, 3));
        _legendList.ItemTemplate = LegendTemplate();
        _legendList.MouseLeftButtonUp += (_, _) =>
        {
            if (_legendList.SelectedItem is LegendItem item) ShowInHex(item.Segment.Start, item.Segment.RecordSize, switchTab: false);
        };
        AddRow(panel, 4, _legendList);

        var border = EditorTheme.Panel(new Thickness(1, 0, 0, 0));
        border.Child = panel;
        return border;
    }

    private static TextBlock InfoText() => new() { TextWrapping = TextWrapping.Wrap, Foreground = EditorTheme.Text };

    private static DataTemplate LegendTemplate()
    {
        var root = new FrameworkElementFactory(typeof(DockPanel));
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(WidthProperty, 10.0);
        swatch.SetValue(HeightProperty, 10.0);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        swatch.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        swatch.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(LegendItem.Brush)));
        root.AppendChild(swatch);

        var range = new FrameworkElementFactory(typeof(TextBlock));
        range.SetValue(DockPanel.DockProperty, Dock.Right);
        range.SetValue(TextBlock.FontFamilyProperty, EditorTheme.MonoFontFamily);
        range.SetValue(TextBlock.FontSizeProperty, 11.0);
        range.SetValue(TextBlock.ForegroundProperty, EditorTheme.TextMuted);
        range.SetValue(MarginProperty, new Thickness(8, 0, 0, 0));
        range.SetBinding(TextBlock.TextProperty, new Binding(nameof(LegendItem.Range)));
        root.AppendChild(range);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(LegendItem.Name)));
        root.AppendChild(name);
        return new DataTemplate { VisualTree = root };
    }

    // ============================================================= 刷新 =============================================================

    private void RefreshHex()
    {
        _findPatternKey = null;
        _findMatches = new List<long>();
        _findInfo.Text = "";
        _hexView.SetInspector(_inspector);
        _legendList.ItemsSource = _inspector?.NonEmptySegments
            .Select(s => new LegendItem(s, BlockBrush(s.Block), BtlInspector.BlockDisplayName(s.Block), Hex8(s.Start) + "–" + Hex8(s.End - 1)))
            .ToList();
        if (_inspector != null && _inspector.Data.LongLength > 0) _hexView.Select(0, 1, center: false);
    }

    private void SyncHexScroll()
    {
        _syncingScroll = true;
        try
        {
            int visible = _hexView.VisibleLines;
            _hexScroll.Maximum = _hexView.MaxTopLine;
            _hexScroll.ViewportSize = visible;
            _hexScroll.LargeChange = Math.Max(1, visible - 1);
            _hexScroll.Value = _hexView.TopLine;
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>选中字节变化：显示偏移、所属块 / 记录 / 字段、字段值，以及按常见类型的解读；并浅色标出所在字段。</summary>
    private void UpdateByteInfo(long offset)
    {
        var inspector = _inspector;
        if (inspector == null || offset < 0)
        {
            _byteOffsetText.Text = _byteLocationText.Text = _byteFieldText.Text = "—";
            _interpretList.ItemsSource = null;
            return;
        }

        _byteOffsetText.Text = Loc.F("DataCheck.Hex.OffsetValue", Hex8(offset), offset);
        _byteLocationText.Text = inspector.DescribeLocation(offset);

        var location = inspector.Locate(offset);
        if (location is { Field: { } field } loc)
        {
            long recordStart = loc.Segment.RecordOffset(loc.RecordIndex);
            var value = inspector.DecodeField(field, recordStart);
            _byteFieldText.Text = string.IsNullOrEmpty(value.Meaning) ? $"{value.Value}  ({value.Hex})" : $"{value.Value}  ({value.Hex})  {value.Meaning}";
            _hexView.SetHighlight(recordStart + field.Offset, field.Size);
        }
        else
        {
            _byteFieldText.Text = "—";
            _hexView.SetHighlight(-1, 0);
        }

        _interpretList.ItemsSource = BtlInspectorHex.Interpret(inspector.Data, offset).Select(t => new InterpretRow(t.Type, t.Value)).ToList();
    }

    // ============================================================= 跳转 / 查找 =============================================================

    /// <summary>切到十六进制页并选中一段字节（记录、字段、数据块起点）。</summary>
    private void ShowInHex(long offset, int length, bool switchTab = true)
    {
        if (_inspector == null) return;
        if (switchTab) _tabs.SelectedItem = _hexTab;
        _hexView.Select(offset, Math.Max(1, length), center: true);
    }

    private void ShowFieldInHex(FieldRow? row)
    {
        if (row != null) ShowInHex(row.FileOffset, row.Size);
    }

    private void JumpToOffset()
    {
        var inspector = _inspector;
        if (inspector == null) return;

        string text = _jumpBox.Text;
        if (!BtlInspectorHex.TryParseOffset(text, out long offset))
        {
            SetStatus(Loc.F("DataCheck.Hex.BadOffset", text), true);
            return;
        }
        if (offset >= inspector.Data.LongLength)
        {
            SetStatus(Loc.F("DataCheck.Hex.OutOfRange", Hex8(offset), Hex8(inspector.Data.LongLength - 1)), true);
            return;
        }

        _hexView.Select(offset, 1, center: true);
        _hexView.Focus();
        SetStatus(Loc.F("DataCheck.Hex.Jumped", Hex8(offset), inspector.DescribeLocation(offset)));
    }

    /// <summary>查找下一处（<paramref name="direction"/> = 1）或上一处（-1），到头后回绕。同一模式只搜一遍，结果缓存。</summary>
    private void FindNext(int direction)
    {
        var inspector = _inspector;
        if (inspector == null) return;

        string text = _findBox.Text;
        if (!BtlInspectorHex.TryParsePattern(text, out var pattern))
        {
            SetStatus(Loc.F("DataCheck.Hex.BadPattern", text), true);
            return;
        }

        string key = BtlInspectorHex.FormatPattern(pattern);
        if (key != _findPatternKey)
        {
            _findMatches = BtlInspectorHex.FindAll(inspector.Data, pattern, MaxFindMatches, out _findTruncated);
            _findPatternKey = key;
            _findPatternLength = pattern.Length;
        }

        if (_findMatches.Count == 0)
        {
            _findInfo.Text = Loc.T("DataCheck.Hex.NoMatch");
            SetStatus(Loc.F("DataCheck.Hex.NoMatchStatus", key), true);
            return;
        }

        long current = _hexView.SelectedOffset;
        int index = _findMatches.BinarySearch(current);
        if (direction > 0)
        {
            index = index >= 0 ? index + 1 : ~index;
            if (index >= _findMatches.Count) index = 0;
        }
        else
        {
            index = (index >= 0 ? index : ~index) - 1;
            if (index < 0) index = _findMatches.Count - 1;
        }

        long hit = _findMatches[index];
        _hexView.Select(hit, _findPatternLength, center: true);
        string total = _findMatches.Count.ToString("N0", CultureInfo.CurrentCulture) + (_findTruncated ? "+" : "");
        _findInfo.Text = Loc.F("DataCheck.Hex.MatchInfo", index + 1, total);
        SetStatus(Loc.F("DataCheck.Hex.Found", key, Hex8(hit), inspector.DescribeLocation(hit)));
    }
}
