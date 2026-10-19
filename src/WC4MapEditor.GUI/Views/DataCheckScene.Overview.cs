using System.Globalization;
using System.Windows.Documents;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.GUI.Views;

// 数据检查场景：概况页（文件概况、检查提示、头部字段、数据块地址表）。
public sealed partial class DataCheckScene
{
    private StackPanel _summaryPanel = null!;
    private StackPanel _issuePanel = null!;
    private ListView _headerList = null!;
    private ListView _segmentList = null!;

    /// <summary>地址表的一行。</summary>
    private sealed record SegmentRow(BtlSegment Segment, Brush Brush, string Name, string Start, string End, string RecordSize,
                                     string Count, string Length, string Note)
    {
        public override string ToString() => string.Join('\t', Name, Start, End, RecordSize, Count, Length, Note);
    }

    private UIElement BuildOverviewTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 320 });

        // 左：概况 + 检查提示 + 头部字段
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddRow(left, 0, EditorTheme.SectionHeader(Loc.T("DataCheck.Overview.Summary")));
        _summaryPanel = new StackPanel();
        AddRow(left, 1, _summaryPanel);

        AddRow(left, 2, EditorTheme.SectionHeader(Loc.T("DataCheck.Overview.Issues")));
        _issuePanel = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
        AddRow(left, 3, new ScrollViewer { Content = _issuePanel, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        AddRow(left, 4, EditorTheme.SectionHeader(Loc.T("DataCheck.Overview.Header")));
        _headerList = CreateTable(
            (Loc.T("DataCheck.Column.Offset"), nameof(FieldRow.Relative), 70),
            (Loc.T("DataCheck.Column.Field"), nameof(FieldRow.Name), 170),
            (Loc.T("DataCheck.Column.Value"), nameof(FieldRow.Value), 90),
            (Loc.T("DataCheck.Column.Hex"), nameof(FieldRow.Hex), 100),
            (Loc.T("DataCheck.Column.Meaning"), nameof(FieldRow.Meaning), 150),
            (Loc.T("DataCheck.Column.Note"), nameof(FieldRow.Note), 320));
        _headerList.MouseDoubleClick += (_, _) => ShowFieldInHex(_headerList.SelectedItem as FieldRow);
        AddRow(left, 5, _headerList);

        var leftPanel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        leftPanel.Child = left;
        grid.Children.Add(leftPanel);

        var splitter = EditorTheme.VSplitter();
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        // 右：数据块地址表
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var segmentHint = EditorTheme.Muted(Loc.T("DataCheck.Overview.SegmentsHint"));
        segmentHint.FontSize = 11;
        AddRow(right, 0, EditorTheme.SectionHeader(Loc.T("DataCheck.Overview.Segments"), segmentHint));

        var segmentView = new GridView();
        segmentView.Columns.Add(SwatchColumn(Loc.T("DataCheck.Column.Block"), nameof(SegmentRow.Brush), nameof(SegmentRow.Name), 140));
        foreach (var (header, path, width) in new[]
                 {
                     (Loc.T("DataCheck.Column.Start"), nameof(SegmentRow.Start), 96.0),
                     (Loc.T("DataCheck.Column.End"), nameof(SegmentRow.End), 96.0),
                     (Loc.T("DataCheck.Column.RecordSize"), nameof(SegmentRow.RecordSize), 92.0),
                     (Loc.T("DataCheck.Column.Count"), nameof(SegmentRow.Count), 70.0),
                     (Loc.T("DataCheck.Column.Length"), nameof(SegmentRow.Length), 86.0),
                     (Loc.T("DataCheck.Column.Note"), nameof(SegmentRow.Note), 220.0)
                 })
            segmentView.Columns.Add(new GridViewColumn { Header = header, DisplayMemberBinding = new Binding(path), Width = width });
        _segmentList = CreateTable(segmentView);
        _segmentList.MouseDoubleClick += (_, _) =>
        {
            if (_segmentList.SelectedItem is SegmentRow row && row.Segment.Length > 0)
                ShowInHex(row.Segment.Start, (int)Math.Min(row.Segment.RecordSize, int.MaxValue));
        };
        AddRow(right, 1, _segmentList);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return grid;
    }

    private static void AddRow(Grid grid, int row, UIElement element)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private void RefreshOverview()
    {
        _summaryPanel.Children.Clear();
        _issuePanel.Children.Clear();
        _headerList.ItemsSource = null;
        _segmentList.ItemsSource = null;

        var inspector = _inspector;
        if (inspector == null || _filePath == null) return;

        void Summary(string labelKey, string value, Brush? brush = null)
        {
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = brush ?? EditorTheme.Text };
            _summaryPanel.Children.Add(EditorTheme.FormRow(Loc.T(labelKey), text, 120));
        }

        Summary("DataCheck.Overview.Path", _filePath);
        Summary("DataCheck.Overview.Kind", BtlInspector.KindDisplayName(inspector.Kind));
        if (inspector.World != null)
            Summary("DataCheck.Overview.Version", Loc.F("DataCheck.Scene.WorldVersion", inspector.World.Version));
        else if (inspector.Header != null)
            Summary("DataCheck.Overview.Version", Loc.F("DataCheck.Scene.BtlVersion", inspector.BtlVersion));
        Summary("DataCheck.Overview.Size", Loc.F("DataCheck.Overview.SizeValue", inspector.FileLength));
        if (inspector.MapArea > 0)
            Summary("DataCheck.Overview.MapSize", Loc.F("DataCheck.Overview.MapSizeValue", inspector.MapColumns, inspector.MapRows));
        if (inspector.IsBtl)
            Summary("DataCheck.Overview.Family", BtlInspector.FamilyDisplayName(inspector.Layout.Family));
        Summary("DataCheck.Overview.Layout", inspector.LayoutStatusText, LayoutBrush(inspector));

        foreach (var issue in inspector.Issues)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            line.Inlines.Add(new Run("[" + Loc.T("DataCheck.IssueLevel." + issue.Level) + "]  ")
            {
                Foreground = issue.Level switch
                {
                    BtlInspectIssueLevel.Error => EditorTheme.Danger,
                    BtlInspectIssueLevel.Warning => EditorTheme.Warning,
                    _ => EditorTheme.Info
                },
                FontWeight = FontWeights.SemiBold
            });
            line.Inlines.Add(new Run(issue.Message) { Foreground = EditorTheme.Text });
            _issuePanel.Children.Add(line);
        }

        var header = inspector.GetRecord(BtlBlock.Header, 0);
        _headerList.ItemsSource = header?.Fields.Select(FieldRow.From).ToList();

        _segmentList.ItemsSource = inspector.Layout.Segments.Select(s => new SegmentRow(
            s,
            BlockBrush(s.Block),
            BtlInspector.BlockDisplayName(s.Block),
            Hex8(s.Start),
            s.Length > 0 ? Hex8(s.End - 1) : "—",
            s.RecordSize.ToString("N0", CultureInfo.CurrentCulture),
            s.Count.ToString("N0", CultureInfo.CurrentCulture),
            s.Length.ToString("N0", CultureInfo.CurrentCulture),
            s.IsTruncated ? Loc.F("DataCheck.Segment.Truncated", s.ReadableCount) : "")).ToList();
    }
}
