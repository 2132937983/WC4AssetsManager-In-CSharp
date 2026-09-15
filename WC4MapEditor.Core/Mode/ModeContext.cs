using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;

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
    /// 切换国家领域（归属着色 + 归属国旗）显示 - 由 GUI 层实现并注入
    /// </summary>
    public Action? NotifyToggleDomain { get; init; }

    /// <summary>
    /// 切换建筑名称显示（建筑编辑模式 U 键）- 由 GUI 层实现并注入。
    /// 实际显示状态由渲染层持有，模式切换时会按模式重置。
    /// </summary>
    public Action? NotifyToggleBuildingNames { get; init; }

    // ------------------------- 军团编辑模式（对齐 VB LegionModifier） -------------------------

    /// <summary>军团范围截图（P 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyCaptureLegionScreenshot { get; init; }

    /// <summary>打开军团设置窗口（Q 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyOpenLegionSetting { get; init; }

    /// <summary>
    /// 打开军团列表窗口 - 由 GUI 层实现并注入。
    /// 军团编辑模式原来的 F 键已让给首都编辑模块，该窗口改由归属编辑模式使用
    /// （对齐 VB 中 LegionBelongList「用于编辑归属模式的 Q 键」的定位）。
    /// </summary>
    public Action? NotifyOpenLegionList { get; init; }

    /// <summary>打开头部数据编辑窗口（E 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyOpenHeaderSetting { get; init; }

    /// <summary>更新征服国家设置（F6 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyUpdateConquerSettings { get; init; }

    /// <summary>把相机中心移动到指定格子（建筑编辑模式 Enter 键）- 由 GUI 层实现并注入</summary>
    public Action<int, int>? NotifyMoveCameraToHex { get; init; }

    /// <summary>建筑移动工具开/关切换（建筑编辑模式 K 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyToggleBuildingMoveTool { get; init; }

    /// <summary>
    /// 文字识别生成建筑（建筑编辑模式 P 键）- 由 GUI 层实现并注入。
    /// 完整流程：选图 → OCR 识别地名 → 导出地图网格 → 生成 zme → 批量放置建筑。
    /// </summary>
    public Action? NotifyRecognizeTextBuildings { get; init; }

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

    /// <summary>
    /// 记录单格归属变更 - 归属编辑模式的单格操作必须用它，否则无法撤销
    /// </summary>
    public void RecordBelongChange(int col, int row, string description, Action applyChange)
    {
        EditModeManager.Instance.RecordBelongChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 记录多格归属变更 - 归属编辑模式的批量操作（清理所有、随机化、批量更改）用它记录撤销
    /// </summary>
    public void RecordMultiCellBelongChange(string description, Action applyChange)
    {
        EditModeManager.Instance.RecordMultiCellBelongChange(description, applyChange);
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
    /// 导出格子数据（不含经纬度）回调 - 由 GUI 层实现并注入。
    /// 与经纬度换算导出同源，但跳过参考点与经纬度计算。
    /// </summary>
    public Func<Task>? GeoExportGridCallback { get; init; }

    /// <summary>
    /// 添加地理参考点回调 - 由 GUI 层实现并注入
    /// </summary>
    public Func<Task>? AddGeoRefCallback { get; init; }
}