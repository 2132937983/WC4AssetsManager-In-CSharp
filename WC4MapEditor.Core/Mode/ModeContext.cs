using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class ModeContext
{
    public MapData? MapData { get; init; }
    public UndoManager? UndoManager { get; init; }
    public Action<string>? RaiseStatusMessage { get; init; }
    public Action? NotifyDataModified { get; init; }
    public Action? NotifyBrushToggled { get; init; }
    public Action? NotifyBrushSizeChanged { get; init; }

    /// <summary>
    /// 对话框服务 - 由 GUI 层实现并注入
    /// </summary>
    public IDialogService? DialogService { get; init; }

    /// <summary>
    /// CLI 命令执行器 - 由 GUI 层实现并注入
    /// </summary>
    public ICliCommandExecutor? CliCommandExecutor { get; init; }

    /// <summary>
    /// 地形识别回调 - 由 GUI 层实现并注入，负责执行识别并返回结果
    /// </summary>
    public Func<Task<(bool success, int modifiedCount)>>? RecognizeTerrainCallback { get; init; }

    public T? GetModifier<T>() where T : class, IModifier
    {
        return EditModeManager.Instance.GetModifier<T>();
    }

    public void RecordMultiCellChange(string description, Action applyChange)
    {
        EditModeManager.Instance.RecordMultiCellChange(description, applyChange);
    }

    public void RecordMultiCellProvinceChange(string description, Action applyChange)
    {
        EditModeManager.Instance.RecordMultiCellProvinceChange(description, applyChange);
    }

    public void RecordProvinceChange(int col, int row, string description, Action applyChange)
    {
        EditModeManager.Instance.RecordProvinceChange(col, row, description, applyChange);
    }

    public void RecordEntityChange(string description, Action execute, Action undo)
    {
        EditModeManager.Instance.RecordEntityChange(description, execute, undo);
    }

    /// <summary>
    /// 地理计算回调 - 由 GUI 层实现并注入，负责执行经纬度换算并导出
    /// </summary>
    public Func<Task>? GeoCalculateCallback { get; init; }

    /// <summary>
    /// 导出参考点回调 - 由 GUI 层实现并注入
    /// </summary>
    public Func<Task>? GeoExportRefCallback { get; init; }

    /// <summary>
    /// 导入参考点回调 - 由 GUI 层实现并注入
    /// </summary>
    public Func<Task>? GeoImportRefCallback { get; init; }

    /// <summary>
    /// 添加地理参考点回调 - 由 GUI 层实现并注入
    /// </summary>
    public Func<Task>? AddGeoRefCallback { get; init; }
}