using System.IO;
using System.Text;
using Microsoft.Win32;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views;

// 数据检查场景：版本转换页（BTL 单位 / 援军记录 v1 ↔ v3；往返检查）。
// 转换只替换单位块、援军块并改写头部版本号，其余字节原样复制；结果写到用户另选的新文件，源文件不动。
public sealed partial class DataCheckScene
{
    private TextBox _convertPath = null!;
    private TextBlock _convertInfo = null!;
    private Button _toV1Button = null!;
    private Button _toV3Button = null!;
    private Button _roundTripButton = null!;
    private TextBox _convertReport = null!;
    private BtlInspector? _convertSource;

    private UIElement BuildConvertTab()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        var picker = FilePickerRow(Loc.T("DataCheck.Convert.Source"), out _convertPath);
        _convertPath.TextChanged += (_, _) => InspectConvertSource();
        head.Children.Add(picker);

        _convertInfo = new TextBlock { Margin = new Thickness(80, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = EditorTheme.TextMuted };
        head.Children.Add(_convertInfo);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(80, 0, 0, 0) };
        _toV1Button = EditorTheme.PrimaryButton(Loc.T("DataCheck.Convert.ToV1"), (_, _) => ConvertTo(1));
        _toV3Button = EditorTheme.PrimaryButton(Loc.T("DataCheck.Convert.ToV3"), (_, _) => ConvertTo(3));
        _roundTripButton = EditorTheme.Button(Loc.T("DataCheck.Convert.RoundTrip"), (_, _) => RunRoundTrip());
        _roundTripButton.ToolTip = Loc.T("DataCheck.Convert.RoundTripTip");
        actions.Children.Add(_toV1Button);
        actions.Children.Add(Spaced(_toV3Button));
        actions.Children.Add(Spaced(_roundTripButton));
        var note = EditorTheme.Muted(Loc.T("DataCheck.Convert.SafetyNote"));
        note.VerticalAlignment = VerticalAlignment.Center;
        note.Margin = new Thickness(14, 0, 0, 0);
        actions.Children.Add(note);
        head.Children.Add(actions);

        AddRow(grid, 0, new Border
        {
            Background = EditorTheme.Bg1,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = head
        });

        _convertReport = ReportBox();
        _convertReport.Text = MappingDocumentation();
        AddRow(grid, 1, _convertReport);

        UpdateConvertButtons();
        return grid;
    }

    /// <summary>字段映射说明（两个方向），作为转换页的初始内容。</summary>
    private static string MappingDocumentation()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("DataCheck.Convert.MappingIntro"));
        BtlConversionResult.AppendMapping(sb, BtlRecordFamily.V3, BtlRecordFamily.V1);
        BtlConversionResult.AppendMapping(sb, BtlRecordFamily.V1, BtlRecordFamily.V3);
        return sb.ToString();
    }

    private void SetConvertSource(string path) => _convertPath.Text = path;

    /// <summary>源文件变化：只读识别一遍，显示种类 / 版本 / 记录数 / 能否转换。</summary>
    private void InspectConvertSource()
    {
        _convertSource = null;
        string path = _convertPath.Text;
        if (path.Length == 0 || !File.Exists(path))
        {
            _convertInfo.Text = "";
            UpdateConvertButtons();
            return;
        }

        try
        {
            // 源文件就是当前打开的文件、且布局设置相同时直接复用，不再读一遍
            var layout = SelectedLayout;
            var inspector = path == _filePath && _inspector != null && _inspectorLayout == layout ? _inspector : BtlInspector.Load(path, layout);
            string? error = BtlVersionConverter.ValidateSource(inspector);
            _convertSource = error == null ? inspector : null;
            _convertInfo.Text = error ?? Loc.F("DataCheck.Convert.SourceInfo", BtlInspector.KindDisplayName(inspector.Kind), inspector.BtlVersion,
                                               BtlInspector.FamilyDisplayName(inspector.Layout.Family),
                                               inspector.GetRecordCount(BtlBlock.Army), inspector.GetRecordCount(BtlBlock.Reinforcement));
            _convertInfo.Foreground = error == null ? EditorTheme.TextMuted : EditorTheme.Danger;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _convertInfo.Text = Loc.F("DataCheck.LoadFailed", ex.Message);
            _convertInfo.Foreground = EditorTheme.Danger;
        }
        UpdateConvertButtons();
    }

    private void UpdateConvertButtons()
    {
        var source = _convertSource;
        _toV1Button.IsEnabled = source != null && source.BtlVersion != 1;
        _toV3Button.IsEnabled = source != null && source.BtlVersion != 3;
        _roundTripButton.IsEnabled = source != null;
    }

    /// <summary>转换到目标版本并另存为新文件（默认名 原名_v1.btl / 原名_v3.btl，不能是源文件本身）。</summary>
    private async void ConvertTo(int targetVersion)
    {
        var source = _convertSource;
        string sourcePath = _convertPath.Text;
        if (source == null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.F("DataCheck.Convert.SaveTitle", targetVersion),
            Filter = $"{Loc.T("DataCheck.Convert.BtlFilter")} (*.btl)|*.btl",
            FileName = $"{IOPath.GetFileNameWithoutExtension(sourcePath)}_v{targetVersion}.btl",
            OverwritePrompt = true
        };
        string? directory = IOPath.GetDirectoryName(sourcePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog() != true)
        {
            SetStatus(Loc.T("DataCheck.Convert.Cancelled"));
            return;
        }

        string outputPath = dialog.FileName;
        if (string.Equals(IOPath.GetFullPath(outputPath), IOPath.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(Loc.T("DataCheck.Convert.Err.SameFile"), true);
            return;
        }

        SetConvertBusy(true);
        try
        {
            var result = await Task.Run(() => BtlVersionConverter.ConvertFile(source, targetVersion));
            if (result.Success) await File.WriteAllBytesAsync(outputPath, result.Output!);
            _convertReport.Text = result.ToReport(sourcePath, result.Success ? outputPath : null);
            SetStatus(result.Success ? Loc.F("DataCheck.Convert.Saved", outputPath) : result.Error ?? "", !result.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(Loc.F("DataCheck.Convert.WriteFailed", ex.Message), true);
        }
        finally
        {
            SetConvertBusy(false);
        }
    }

    /// <summary>只在内存里做 v3 → v1 → v3（或 v1 → v3 → v1），列出没能复原的字段，不写任何文件。</summary>
    private async void RunRoundTrip()
    {
        var source = _convertSource;
        if (source == null) return;

        string sourcePath = _convertPath.Text;
        SetConvertBusy(true);
        try
        {
            var result = await Task.Run(() => BtlVersionConverter.CheckRoundTrip(source));
            _convertReport.Text = result.ToReport(sourcePath);
            SetStatus(result.Success ? Loc.T("DataCheck.Convert.RoundTripDone") : result.Error ?? "", !result.Success);
        }
        finally
        {
            SetConvertBusy(false);
        }
    }

    private void SetConvertBusy(bool busy)
    {
        if (busy)
        {
            _toV1Button.IsEnabled = _toV3Button.IsEnabled = _roundTripButton.IsEnabled = false;
            SetStatus(Loc.T("DataCheck.Convert.Running"));
        }
        else
        {
            UpdateConvertButtons();
        }
    }
}
