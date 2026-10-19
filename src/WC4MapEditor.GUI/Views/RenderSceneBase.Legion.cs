namespace WC4MapEditor.GUI.Views;

// 军团编辑模式里需要场景配合的部分：军团范围截图（F4）。
// 军团编辑器、军团列表、头部数据窗口都是 AssistEditor，由模式按 EditorKinds 打开，场景不再持有它们。
public abstract partial class RenderSceneBase
{
    /// <summary>军团范围截图（军团编辑模式 F4 键）：后台渲染并保存到程序目录的 screenshots 下，完成后提示文件位置。</summary>
    private async void OnCaptureLegionScreenshotRequested(object? sender, EventArgs e)
    {
        var map = _mapData;
        if (map == null) return;

        try
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.CapturingLegion"));
            string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "screenshots");
            string path = await Task.Run(() => LegionScreenshotRender.SavePng(map, directory));
            MessageBox.Show(Loc.F("Render.Legion.ScreenshotSaved", path), Loc.T("Render.Legion.ScreenshotTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Legion.ScreenshotFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
