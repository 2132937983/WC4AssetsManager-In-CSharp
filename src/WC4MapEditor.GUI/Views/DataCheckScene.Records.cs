using System.Globalization;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.GUI.Views;

// 数据检查场景：记录页（左：数据块，中：记录列表 + 定位，右：逐字段解码）。
public sealed partial class DataCheckScene
{
    private ListBox _blockList = null!;
    private ListBox _recordList = null!;
    private TextBox _gotoBox = null!;
    private TextBlock _recordTitle = null!;
    private Button _recordHexButton = null!;
    private ListView _fieldList = null!;
    private BtlBlock _currentBlock = BtlBlock.Legion;
    private BtlRecord? _currentRecord;

    /// <summary>数据块列表的一项。</summary>
    private sealed record BlockItem(BtlBlock Block, string Text);

    private UIElement BuildRecordsTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210), MinWidth = 150 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320), MinWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        // 数据块
        var blockPanel = new Grid();
        blockPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        blockPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        AddRow(blockPanel, 0, EditorTheme.SectionHeader(Loc.T("DataCheck.Records.Blocks")));
        _blockList = new ListBox { DisplayMemberPath = nameof(BlockItem.Text) };
        _blockList.ItemContainerStyle = CompactItemStyle(typeof(ListBoxItem), new Thickness(10, 5, 10, 5));
        _blockList.SelectionChanged += (_, _) =>
        {
            if (_blockList.SelectedItem is BlockItem item) ShowBlock(item.Block);
        };
        AddRow(blockPanel, 1, _blockList);
        var blockBorder = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        blockBorder.Child = blockPanel;
        grid.Children.Add(blockBorder);

        AddSplitter(grid, 1);

        // 记录列表
        var recordPanel = new Grid();
        recordPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recordPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recordPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        AddRow(recordPanel, 0, EditorTheme.SectionHeader(Loc.T("DataCheck.Records.Records")));
        var gotoBox = EditorTheme.SearchBox(Loc.T("DataCheck.Records.GotoPlaceholder"), out _gotoBox);
        gotoBox.Margin = new Thickness(8);
        gotoBox.ToolTip = Loc.T("DataCheck.Records.GotoTip");
        _gotoBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            GotoRecord(_gotoBox.Text);
            e.Handled = true;
        };
        AddRow(recordPanel, 1, gotoBox);

        _recordList = new ListBox { FontFamily = EditorTheme.MonoFontFamily, FontSize = 12 };
        _recordList.ItemContainerStyle = CompactItemStyle(typeof(ListBoxItem), new Thickness(8, 3, 8, 3));
        VirtualizingPanel.SetScrollUnit(_recordList, ScrollUnit.Item);
        _recordList.SelectionChanged += (_, _) => ShowRecord(_recordList.SelectedIndex);
        AddRow(recordPanel, 2, _recordList);
        var recordBorder = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        recordBorder.Child = recordPanel;
        Grid.SetColumn(recordBorder, 2);
        grid.Children.Add(recordBorder);

        AddSplitter(grid, 3);

        // 字段
        var detail = new Grid();
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _recordTitle = new TextBlock { FontWeight = FontWeights.SemiBold, Foreground = EditorTheme.Accent, VerticalAlignment = VerticalAlignment.Center };
        _recordHexButton = EditorTheme.ToolButton(Loc.T("DataCheck.Records.ShowInHex"), (_, _) =>
        {
            if (_currentRecord != null) ShowInHex(_currentRecord.Offset, _currentRecord.Size);
        });
        var titleBar = new DockPanel { Margin = new Thickness(10, 6, 10, 6), LastChildFill = true };
        DockPanel.SetDock(_recordHexButton, Dock.Right);
        titleBar.Children.Add(_recordHexButton);
        titleBar.Children.Add(_recordTitle);
        AddRow(detail, 0, new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = titleBar
        });

        _fieldList = CreateTable(
            (Loc.T("DataCheck.Column.FileOffset"), nameof(FieldRow.Offset), 104),
            (Loc.T("DataCheck.Column.Relative"), nameof(FieldRow.Relative), 86),
            (Loc.T("DataCheck.Column.Field"), nameof(FieldRow.Name), 150),
            (Loc.T("DataCheck.Column.Value"), nameof(FieldRow.Value), 90),
            (Loc.T("DataCheck.Column.Hex"), nameof(FieldRow.Hex), 100),
            (Loc.T("DataCheck.Column.Meaning"), nameof(FieldRow.Meaning), 130),
            (Loc.T("DataCheck.Column.Note"), nameof(FieldRow.Note), 360),
            (Loc.T("DataCheck.Column.Binary"), nameof(FieldRow.Binary), 150));
        _fieldList.FontFamily = EditorTheme.MonoFontFamily;
        _fieldList.FontSize = 12;
        _fieldList.ToolTip = Loc.T("DataCheck.Records.FieldTip");
        _fieldList.MouseDoubleClick += (_, _) => ShowFieldInHex(_fieldList.SelectedItem as FieldRow);
        AddRow(detail, 1, _fieldList);
        Grid.SetColumn(detail, 4);
        grid.Children.Add(detail);

        return grid;
    }

    private static void AddSplitter(Grid grid, int column)
    {
        var splitter = EditorTheme.VSplitter();
        Grid.SetColumn(splitter, column);
        grid.Children.Add(splitter);
    }

    private void RefreshRecords()
    {
        var inspector = _inspector;
        _blockList.ItemsSource = null;
        if (inspector == null)
        {
            ShowBlock(BtlBlock.Legion);
            return;
        }

        var items = inspector.Layout.Segments
            .Where(s => s.Block != BtlBlock.Header)
            .Select(s => new BlockItem(s.Block, Loc.F("DataCheck.Records.BlockItem", BtlInspector.BlockDisplayName(s.Block),
                                                      s.ReadableCount.ToString("N0", CultureInfo.CurrentCulture))))
            .ToList();
        _blockList.ItemsSource = items;

        // 重新加载后尽量停在原来的数据块上
        var keep = items.FirstOrDefault(i => i.Block == _currentBlock) ?? items.FirstOrDefault(i => inspector.GetRecordCount(i.Block) > 0)
                   ?? items.FirstOrDefault();
        _blockList.SelectedItem = keep;
        if (keep == null) ShowBlock(BtlBlock.Legion);
    }

    private void ShowBlock(BtlBlock block)
    {
        _currentBlock = block;
        int count = _inspector?.GetRecordCount(block) ?? 0;
        _recordList.ItemsSource = _inspector == null ? null : new RecordRefList(_inspector, block);
        if (count > 0)
        {
            _recordList.SelectedIndex = 0;
            _recordList.ScrollIntoView(_recordList.SelectedItem);
        }
        else
        {
            ShowRecord(-1);
        }
    }

    private void ShowRecord(int index)
    {
        _currentRecord = index >= 0 ? _inspector?.GetRecord(_currentBlock, index) : null;
        _recordHexButton.IsEnabled = _currentRecord != null;
        if (_currentRecord == null)
        {
            _recordTitle.Text = _inspector == null ? "" : Loc.F("DataCheck.Records.EmptyBlock", BtlInspector.BlockDisplayName(_currentBlock));
            _fieldList.ItemsSource = null;
            return;
        }

        var record = _currentRecord;
        _recordTitle.Text = Loc.F("DataCheck.Record.Title", BtlInspector.BlockDisplayName(record.Block), record.Index,
                                  record.Offset, record.Offset + record.Size - 1, record.Size);
        _fieldList.ItemsSource = record.Fields.Select(FieldRow.From).ToList();
    }

    /// <summary>定位记录："#序号" / "序号"，或格子 "列,行"（地形 / 省份 / 归属按格子换算，其余块找坐标在该格的记录）。</summary>
    private void GotoRecord(string text)
    {
        var inspector = _inspector;
        if (inspector == null) return;

        int index;
        string trimmed = text.Trim().TrimStart('#');
        if (BtlInspectorHex.TryParseCell(trimmed, out int col, out int row))
        {
            index = inspector.FindRecordAt(_currentBlock, col, row);
            if (index < 0)
            {
                SetStatus(Loc.F("DataCheck.Cli.NoRecordAt", BtlInspector.BlockDisplayName(_currentBlock), col, row), true);
                return;
            }
        }
        else if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out index)
                 || index >= inspector.GetRecordCount(_currentBlock))
        {
            SetStatus(Loc.F("DataCheck.Records.BadGoto", text, inspector.GetRecordCount(_currentBlock)), true);
            return;
        }

        _recordList.SelectedIndex = index;
        _recordList.ScrollIntoView(_recordList.SelectedItem);
        SetStatus(Loc.F("DataCheck.Records.Located", BtlInspector.BlockDisplayName(_currentBlock), index));
    }
}
