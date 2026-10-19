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

    /// <summary>
    /// 创建本上下文的编辑模式管理器。
    /// <para>
    /// 各模式内部的撤销记录必须回调它，而不是走 <see cref="EditModeManager.Instance"/>：
    /// 单例只是过渡期的默认入口，换成别的实例（DI 新实例 / 单元测试）时，
    /// 走单例会静默记录到错误的对象上。
    /// </para>
    /// </summary>
    public required EditModeManager Owner { get; init; }
    public Action<string>? RaiseStatusMessage { get; init; }
    public Action? NotifyDataModified { get; init; }
    public Action? NotifyBrushToggled { get; init; }
    public Action? NotifyBrushSizeChanged { get; init; }

    /// <summary>
    /// 手绘边界状态变化（省份编辑模式 U 键开关、U 画笔半径、Ctrl+U 清除）- 由 GUI 层实现并注入。
    /// <para>
    /// 与 <see cref="NotifyBrushToggled"/> 分开，因为要重绘的东西不同：
    /// 那个刷的是画笔预览圈，这个要刷的是红色的边界覆盖层。
    /// </para>
    /// </summary>
    public Action? NotifyBoundaryDrawToggled { get; init; }

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

    /// <summary>军团范围截图（F4 键）- 由 GUI 层实现并注入</summary>
    public Action? NotifyCaptureLegionScreenshot { get; init; }

    /// <summary>
    /// 当前背景图（视图层图片）的像素入口 - 由 GUI 层注入。
    /// 省份编辑模式的"从背景图识别省区"（P 键）靠它读取像素做灰度与边界分析。
    /// </summary>
    public IViewLayerImageProvider? ViewLayerImage { get; init; }

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
        return Owner.GetModifier<T>();
    }

    public void RecordMultiCellChange(string description, Action applyChange)
    {
        Owner.RecordMultiCellChange(description, applyChange);
    }

    public void RecordMultiCellProvinceChange(string description, Action applyChange)
    {
        Owner.RecordMultiCellProvinceChange(description, applyChange);
    }

    public void RecordProvinceChange(int col, int row, string description, Action applyChange)
    {
        Owner.RecordProvinceChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 记录单格归属变更 - 归属编辑模式的单格操作必须用它，否则无法撤销
    /// </summary>
    public void RecordBelongChange(int col, int row, string description, Action applyChange)
    {
        Owner.RecordBelongChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 记录多格归属变更 - 归属编辑模式的批量操作（清理所有、随机化、批量更改）用它记录撤销
    /// </summary>
    public void RecordMultiCellBelongChange(string description, Action applyChange)
    {
        Owner.RecordMultiCellBelongChange(description, applyChange);
    }

    public void RecordEntityChange(string description, Action execute, Action undo)
    {
        Owner.RecordEntityChange(description, execute, undo);
    }

    /// <summary>
    /// 记录建筑相关的批量变更 - 同时快照 建筑列表 / 省份 / 归属。
    /// 建筑编辑模式里的随机化、批量生成、生成省会等操作必须用它记录撤销。
    /// </summary>
    public void RecordBuildingsSnapshot(string description, Action applyChange)
    {
        Owner.RecordBuildingsSnapshot(description, applyChange);
    }

    /// <summary>
    /// 记录范围不确定的批量变更 - 快照所有实体列表（建筑 / 单位 / 陷阱 / 援军 / 事件 / 天气……）+ 省份 + 归属。
    /// 一次操作同时改到多种实体时用它，比 <see cref="RecordBuildingsSnapshot"/> 覆盖面全。
    /// </summary>
    public void RecordEntitiesSnapshot(string description, Action applyChange)
    {
        Owner.RecordEntitiesSnapshot(description, applyChange);
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

    // ------------------------- 通用扩展：新功能统一走这些，不再为每个窗口 / 集合加专用回调 -------------------------

    /// <summary>
    /// 请求 GUI 打开某种编辑窗口（种类见 <see cref="EditorKinds"/>）。
    /// 没有 GUI（命令行 / 脚本）或该种窗口尚未实现时返回 false，并在状态栏说明原因。
    /// </summary>
    public bool OpenEditor(string kind, int col = -1, int row = -1, object? argument = null)
        => Owner.OpenEditor(new EditorRequest(kind, col, row, argument));

    /// <summary>
    /// 记录一次实体列表（事件 / 天气 / 方案 / 空袭 / 援军 / 军团……）修改的撤销：执行前后各拍一份列表快照。
    /// 元素是含引用类型字段的结构体时传入 <paramref name="clone"/> 做深拷贝。
    /// </summary>
    public void RecordCollectionChange<T>(string description, IList<T> list, Action applyChange, Func<T, T>? clone = null)
        => Owner.RecordCollectionChange(description, list, applyChange, clone);

    /// <summary>当前选区（只读）。批量操作的目标格请用 <see cref="ModeActionArgs.TargetCells"/>。</summary>
    public IReadOnlySet<HexCoord> Selection => HexSelector.Instance.SelectedHexes;
}