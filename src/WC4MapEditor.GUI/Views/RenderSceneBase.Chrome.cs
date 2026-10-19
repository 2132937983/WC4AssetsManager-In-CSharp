using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 底栏工具按钮的反馈与窗口外壳：撤销 / 重做按钮的可用状态与提示、关闭前的未保存提醒、
/// 每次重绘后的界面同步，以及把脚本文件拖到地图上直接执行。
/// </summary>
public abstract partial class RenderSceneBase
{
    private const double UnavailableButtonOpacity = 0.45;

    private Button? _undoButton;
    private Button? _redoButton;

    /// <summary>最近一次重绘时的地图对象：与 _mapData 不同说明刚换了地图（打开 / 新建 / 切换场景）。</summary>
    private MapData? _sceneUxMap;

    private void AttachUndoRedoButtons(Button undoButton, Button redoButton)
    {
        _undoButton = undoButton;
        _redoButton = redoButton;
    }

    /// <summary>
    /// 撤销 / 重做按钮：没有可撤销的步骤时变暗，鼠标提示写出下一步要撤销 / 重做的操作。
    /// 按钮始终可点（点了会在状态栏说明"没有可撤销的操作"），不用 IsEnabled —— 默认禁用样式是一块浅灰底，压在深色底栏上很刺眼。
    /// </summary>
    private void UpdateUndoRedoButtons()
    {
        var undo = _fileStateManager.UndoManager;
        if (_undoButton != null)
        {
            _undoButton.Opacity = undo.CanUndo ? 1.0 : UnavailableButtonOpacity;
            _undoButton.ToolTip = undo.CanUndo
                ? Loc.F("Scene.Toolbar.UndoTip", undo.LastUndoDescription ?? "")
                : Loc.T("Scene.Status.NothingToUndo");
        }
        if (_redoButton != null)
        {
            _redoButton.Opacity = undo.CanRedo ? 1.0 : UnavailableButtonOpacity;
            _redoButton.ToolTip = undo.CanRedo
                ? Loc.F("Scene.Toolbar.RedoTip", undo.LastRedoDescription ?? "")
                : Loc.T("Scene.Status.NothingToRedo");
        }
    }

    /// <summary>✕：有未保存的修改时先确认，避免一键丢掉编辑结果。</summary>
    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fileStateManager.IsDirty)
        {
            if (_confirmDialogShowing) return;
            _confirmDialogShowing = true;
            bool confirmed;
            using (var dialog = new ConfirmDialog(Window)
            {
                Title = Loc.T("Scene.Close.Title"),
                Message = Loc.T("Scene.Close.DirtyMessage"),
                ConfirmText = Loc.T("Scene.Close.Confirm"),
                CancelText = Loc.T("Common.Cancel")
            })
            {
                confirmed = await dialog.ShowAsync();
            }
            _confirmDialogShowing = false;
            RestoreFocus();
            if (!confirmed) return;
        }
        Window.CloseApplication();
    }

    /// <summary>
    /// 每次重绘之后：发现换了地图就同步依赖地图的界面状态（信息窗口、查看格、读数），并刷新位置 / 缩放读数。
    /// 打开、新建、切换场景都会在换图后重绘，所以不必在每条换图路径里各写一遍。
    /// </summary>
    private void AfterScenePaint()
    {
        if (!ReferenceEquals(_mapData, _sceneUxMap))
        {
            bool replaced = _sceneUxMap != null;
            _sceneUxMap = _mapData;
            if (replaced) Dispatcher.BeginInvoke(OnSceneMapReplaced);
        }
        QueueSceneReadoutRefresh();
    }

    // ------------------------------------------------------------------ 拖放脚本

    private void AttachScriptDrop()
    {
        _rootGrid.AllowDrop = true;
        _rootGrid.DragOver += OnSceneScriptDragOver;
        _rootGrid.Drop += OnSceneScriptDrop;
    }

    private void DetachScriptDrop()
    {
        _rootGrid.DragOver -= OnSceneScriptDragOver;
        _rootGrid.Drop -= OnSceneScriptDrop;
    }

    private static string[] DroppedScripts(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
        return ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
            .Where(DebugConsoleWindow.IsSupportedScript)
            .ToArray();
    }

    private void OnSceneScriptDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedScripts(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// 把 .zme / .zsc / .lua 脚本拖到地图上：打开调试控制台并执行（对齐 VB 往渲染窗口拖 .zsc 即执行）。
    /// 执行过程与控制台里拖放、run 命令一致，输出、撤销都走控制台那一套。
    /// </summary>
    private async void OnSceneScriptDrop(object sender, DragEventArgs e)
    {
        var scripts = DroppedScripts(e);
        if (scripts.Length == 0) return;
        e.Handled = true;

        if (!_debugConsole.IsVisible) Window.ToggleDebugConsole();
        foreach (var file in scripts)
            await DebugConsoleWindow.RunScriptFileAsync(_debugConsole, file);

        _skElement.InvalidateVisual();
    }
}
