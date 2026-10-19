using System.IO;
using System.Text;
using Microsoft.Win32;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.GUI.Views;

// 数据检查场景：文件对比页（选两个文件 → 按数据块 / 记录 / 字节区间对比 → 保存报告）。
public sealed partial class DataCheckScene
{
    private TextBox _comparePathA = null!;
    private TextBox _comparePathB = null!;
    private TextBlock _compareSummary = null!;
    private Button _compareButton = null!;
    private Button _saveReportButton = null!;
    private ListView _compareBlocks = null!;
    private TextBox _compareDetail = null!;
    private BinaryCompareResult? _compareResult;

    /// <summary>数据块对比表的一行。</summary>
    private sealed record BlockDiffRow(BlockDiff Diff, Brush Brush, string Name, int CountA, int CountB, int Changed, int OnlyA,
                                       int OnlyB, string Status)
    {
        public override string ToString() => string.Join('\t', Name, CountA, CountB, Changed, OnlyA, OnlyB, Status);
    }

    private UIElement BuildCompareTab()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Margin = new Thickness(12, 10, 12, 6) };
        head.Children.Add(FilePickerRow(Loc.T("DataCheck.Compare.FileA"), out _comparePathA));
        head.Children.Add(FilePickerRow(Loc.T("DataCheck.Compare.FileB"), out _comparePathB));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _compareButton = EditorTheme.PrimaryButton(Loc.T("DataCheck.Compare.Start"), (_, _) => RunCompare());
        _saveReportButton = EditorTheme.Button(Loc.T("DataCheck.Compare.SaveReport"), (_, _) => SaveCompareReport());
        _saveReportButton.IsEnabled = false;
        var fullReport = EditorTheme.GhostButton(Loc.T("DataCheck.Compare.FullReport"), (_, _) => ShowFullCompareReport());
        actions.Children.Add(_compareButton);
        actions.Children.Add(Spaced(_saveReportButton));
        actions.Children.Add(Spaced(fullReport));
        _compareSummary = new TextBlock { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        actions.Children.Add(_compareSummary);
        head.Children.Add(actions);
        AddRow(grid, 0, new Border
        {
            Background = EditorTheme.Bg1,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = head
        });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(670), MinWidth = 300 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        var view = new GridView();
        view.Columns.Add(SwatchColumn(Loc.T("DataCheck.Column.Block"), nameof(BlockDiffRow.Brush), nameof(BlockDiffRow.Name), 140));
        foreach (var (header, path, width) in new[]
                 {
                     (Loc.T("DataCheck.Compare.Column.CountA"), nameof(BlockDiffRow.CountA), 64.0),
                     (Loc.T("DataCheck.Compare.Column.CountB"), nameof(BlockDiffRow.CountB), 64.0),
                     (Loc.T("DataCheck.Compare.Column.Changed"), nameof(BlockDiffRow.Changed), 72.0),
                     (Loc.T("DataCheck.Compare.Column.OnlyA"), nameof(BlockDiffRow.OnlyA), 56.0),
                     (Loc.T("DataCheck.Compare.Column.OnlyB"), nameof(BlockDiffRow.OnlyB), 56.0),
                     (Loc.T("DataCheck.Column.Note"), nameof(BlockDiffRow.Status), 210.0)
                 })
            view.Columns.Add(new GridViewColumn { Header = header, DisplayMemberBinding = new Binding(path), Width = width });
        _compareBlocks = CreateTable(view);
        _compareBlocks.SelectionMode = SelectionMode.Single;
        _compareBlocks.SelectionChanged += (_, _) =>
        {
            if (_compareResult != null && _compareBlocks.SelectedItem is BlockDiffRow row)
                _compareDetail.Text = BinaryCompare.BuildBlockDetail(_compareResult, row.Diff);
        };
        var listBorder = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        listBorder.Child = _compareBlocks;
        body.Children.Add(listBorder);

        AddSplitter(body, 1);

        _compareDetail = ReportBox();
        _compareDetail.Text = Loc.T("DataCheck.Compare.Intro");
        Grid.SetColumn(_compareDetail, 2);
        body.Children.Add(_compareDetail);
        AddRow(grid, 1, body);
        return grid;
    }

    /// <summary>"标签 + 只读路径框 + 选择… + 当前文件"一行。</summary>
    private Grid FilePickerRow(string label, out TextBox pathBox)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = EditorTheme.FieldLabel(label);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);

        var box = new TextBox { IsReadOnly = true };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        pathBox = box;

        var pick = Spaced(EditorTheme.Button(Loc.T("DataCheck.Scene.Browse"), (_, _) =>
        {
            string? path = PickFile(Loc.F("DataCheck.Scene.PickTitle", label), box.Text.Length > 0 ? box.Text : _filePath);
            if (path != null) box.Text = path;
        }));
        Grid.SetColumn(pick, 2);
        row.Children.Add(pick);

        var current = Spaced(EditorTheme.GhostButton(Loc.T("DataCheck.Scene.UseCurrent"), (_, _) =>
        {
            if (_filePath != null) box.Text = _filePath;
        }));
        Grid.SetColumn(current, 3);
        row.Children.Add(current);
        return row;
    }

    /// <summary>等宽、不换行、可滚动的只读报告框。</summary>
    private static TextBox ReportBox()
    {
        var box = EditorTheme.MonoTextBox();
        box.Height = double.NaN;
        box.TextWrapping = TextWrapping.NoWrap;
        box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.BorderThickness = new Thickness(0);
        return box;
    }

    /// <summary>打开文件后，把它填进对比 A / 转换源这些还空着的位置；转换源就是它时重新识别一次。</summary>
    private void OfferCurrentFileToTools()
    {
        if (_filePath == null) return;
        if (_comparePathA.Text.Length == 0) _comparePathA.Text = _filePath;
        if (_convertPath.Text.Length == 0) SetConvertSource(_filePath);
        else if (_convertPath.Text == _filePath) InspectConvertSource();
    }

    private async void RunCompare()
    {
        string pathA = _comparePathA.Text, pathB = _comparePathB.Text;
        if (!File.Exists(pathA) || !File.Exists(pathB))
        {
            SetStatus(Loc.T("DataCheck.Compare.NeedFiles"), true);
            return;
        }

        var layout = SelectedLayout;
        _compareButton.IsEnabled = false;
        SetStatus(Loc.T("DataCheck.Compare.Running"));
        try
        {
            var result = await Task.Run(() => BinaryCompare.Compare(BtlInspector.Load(pathA, layout), BtlInspector.Load(pathB, layout)));
            ShowCompareResult(result);
            SetStatus(Loc.F("DataCheck.Compare.Done", BinaryCompare.SummaryText(result)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(Loc.F("DataCheck.LoadFailed", ex.Message), true);
        }
        finally
        {
            _compareButton.IsEnabled = true;
        }
    }

    private void ShowCompareResult(BinaryCompareResult result)
    {
        _compareResult = result;
        _saveReportButton.IsEnabled = true;
        _compareSummary.Text = BinaryCompare.SummaryText(result);
        _compareSummary.Foreground = result.Identical ? EditorTheme.Success : EditorTheme.Warning;

        _compareBlocks.ItemsSource = result.Blocks.Select(b => new BlockDiffRow(
            b, BlockBrush(b.Block), BtlInspector.BlockDisplayName(b.Block), b.CountA, b.CountB, b.ChangedRecords, b.OnlyInA, b.OnlyInB,
            (b.IsIdentical, b.AlignedByConversion) switch
            {
                (true, false) => Loc.T("DataCheck.Compare.Same"),
                (true, true) => Loc.T("DataCheck.Compare.SameAligned"),
                (false, true) => Loc.T("DataCheck.Compare.DifferentAligned"),
                _ => Loc.T("DataCheck.Compare.Different")
            })).ToList();
        ShowFullCompareReport();
    }

    private void ShowFullCompareReport()
    {
        if (_compareResult == null) return;
        _compareBlocks.SelectedItem = null;
        _compareDetail.Text = BinaryCompare.BuildReport(_compareResult);
    }

    /// <summary>把完整报告存成 UTF-8 文本；取消保存不算失败，也不改对比结果。</summary>
    private void SaveCompareReport()
    {
        var result = _compareResult;
        if (result == null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("DataCheck.Compare.SaveReport"),
            Filter = $"{Loc.T("DataCheck.Compare.ReportFilter")} (*.txt)|*.txt",
            FileName = $"compare_{IOPath.GetFileNameWithoutExtension(result.A.SourceName)}_{IOPath.GetFileNameWithoutExtension(result.B.SourceName)}.txt"
        };
        string? directory = IOPath.GetDirectoryName(result.B.SourceName);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog() != true)
        {
            SetStatus(Loc.T("DataCheck.Compare.SaveCancelled"));
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, BinaryCompare.BuildReport(result), new UTF8Encoding(true));
            SetStatus(Loc.F("DataCheck.Compare.Saved", dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(Loc.F("DataCheck.Compare.SaveFailed", ex.Message), true);
        }
    }
}
