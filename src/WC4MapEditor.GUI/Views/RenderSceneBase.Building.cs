using WC4MapEditor.Core.ErrorHandling;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 建筑部署模式的场景部分：建筑移动工具（K 键开启后，右键按住建筑拖到目标格松开）。
/// <para>
/// 场景只管鼠标与拖拽虚影：按下时分发 move_begin（模式记下源格），松开时分发 move_end（模式记录撤销并移动建筑）。
/// 开关状态只在 <see cref="BuildingDeployMode.MoveToolActive"/> 里，场景不再自己维护一份
/// （以前两边各存一个开关、各自取反，场景重建后会对不上）。
/// </para>
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>右键拖拽移动建筑时的源格（null 表示当前没有在拖拽）。</summary>
    private HexCoord? _buildingDragSource;

    /// <summary>当前模式是开着移动工具的建筑部署模式时返回它。</summary>
    private BuildingDeployMode? ActiveBuildingMoveTool
        => _editModeManager.CurrentHandler is BuildingDeployMode { MoveToolActive: true } mode ? mode : null;

    /// <summary>建筑移动工具：右键按在建筑上开始拖拽（源格取按下右键的位置，而不是越过拖动阈值时的位置）。</summary>
    private bool TryStartBuildingDrag(MouseActionEventArgs e)
    {
        if (ActiveBuildingMoveTool == null || _mapData == null || _camera == null) return false;

        var (col, row) = _camera.ScreenToHex(e.Position.X - e.DeltaPosition.X, e.Position.Y - e.DeltaPosition.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        var building = _mapData.GetBuildingAt(col, row);
        if (building == null || !DispatchBuildingMoveAction("move_begin", col, row)) return false;

        _buildingDragSource = new HexCoord(col, row);
        BuildingEditOverlay.For(_mapData).BeginDrag(building.Value, e.Position.X, e.Position.Y);
        _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>拖动中：半透明建筑跟随鼠标，鼠标下的目标格用选区高亮。</summary>
    private bool TryContinueBuildingDrag(MouseActionEventArgs e)
    {
        if (_buildingDragSource == null) return false;
        if (_mapData == null || _camera == null) return true;

        BuildingEditOverlay.For(_mapData).MoveDrag(e.Position.X, e.Position.Y);
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight)
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);

        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>松开右键：交给模式移动建筑（剪切归属、可撤销）；移动成功后选中建筑的新位置。</summary>
    private bool TryEndBuildingDrag(MouseActionEventArgs e)
    {
        if (_buildingDragSource == null) return false;
        _buildingDragSource = null;
        if (_mapData == null || _camera == null) return true;

        BuildingEditOverlay.For(_mapData).EndDrag();
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (DispatchBuildingMoveAction("move_end", col, row))
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        else
            _hexSelector.ClearSelection();

        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>移动工具开关切换（建筑部署模式 K 键）：关闭时结束进行中的拖拽。</summary>
    private void OnBuildingMoveToolToggled(object? sender, EventArgs e)
    {
        if (ActiveBuildingMoveTool == null && _buildingDragSource != null)
        {
            _buildingDragSource = null;
            if (_mapData != null) BuildingEditOverlay.For(_mapData).EndDrag();
            _hexSelector.ClearSelection();
        }

        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 分发移动工具动作并取结果。move_begin / move_end 的处理函数是同步的，返回的任务此时已经完成；
    /// 处理函数抛出的异常记入错误收集器并在状态栏提示，按"未处理"返回。
    /// </summary>
    private bool DispatchBuildingMoveAction(string action, int col, int row)
    {
        var task = _editModeManager.HandleKeyAction(action, col, row);
        if (task.IsCompletedSuccessfully) return task.Result;

        if (task.IsFaulted)
        {
            var ex = task.Exception!.GetBaseException();
            ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                $"建筑移动工具动作失败: {action}", nameof(DispatchBuildingMoveAction), ex);
            _editModeManager.RaiseStatusMessage(Loc.F("Mode.Status.ActionFailed", _editModeManager.GetModeStatusText(), action, ex.Message));
        }
        return false;
    }
}
