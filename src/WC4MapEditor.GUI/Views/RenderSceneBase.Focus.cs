using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using CoreKeyBinding = WC4MapEditor.Core.Input.KeyBinding;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 模式按键的目标格（焦点格）与选区的跨模式保持。
/// <para>
/// 焦点格规则：选区主格优先，其次是鼠标所在格；两者都没有（没选格子、鼠标也不在地图上）时不分发模式按键，
/// 只在状态栏提示 —— 以前这时会退回 (0,0)，Delete、F 之类的按键会悄悄改掉左上角那一格。
/// </para>
/// <para>
/// 非选区模式（<see cref="IModeHandler.RequiresSelection"/> 为 false）绝不往选择器里写格子：
/// 以前第一次按键会把鼠标所在格"隐形地"选中，之后的按键一直作用在那一格上，点别处也改不掉。
/// </para>
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>鼠标在地图区域内时的位置（离开地图区域、移到底栏上时为 null）</summary>
    private Point? _hoverPoint;

    /// <summary>被本轮模式切换清掉的选区主格（同一调度周期内有效，之后自动作废）</summary>
    private HexCoord? _primaryClearedThisTurn;

    /// <summary>要带到下一个选区模式里的主格（途经非选区模式时先存着）</summary>
    private HexCoord? _carriedPrimary;

    private void TrackHoverPoint(Point position)
    {
        _hoverPoint = position;
        QueueSceneReadoutRefresh();
    }

    private void ClearHoverPoint()
    {
        _hoverPoint = null;
        QueueSceneReadoutRefresh();
    }

    private bool IsHexInMap(int col, int row)
        => _mapData != null && col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight;

    /// <summary>鼠标所在格（鼠标不在地图区域内或指向地图外时为 null）。按当前相机现算，平移缩放后也准确。</summary>
    private (int Col, int Row)? HoveredHex()
    {
        if (_hoverPoint is not { } p || _camera == null) return null;
        var (col, row) = _camera.ScreenToHex(p.X, p.Y);
        return IsHexInMap(col, row) ? (col, row) : null;
    }

    private (int Col, int Row)? PrimaryHexInMap()
    {
        var primary = _hexSelector.PrimarySelected;
        return primary is { } h && IsHexInMap(h.Col, h.Row) ? (h.Col, h.Row) : null;
    }

    /// <summary>模式按键的目标格：选区主格优先，其次鼠标所在格；都没有时为 null。</summary>
    private (int Col, int Row)? ResolveFocusHex() => PrimaryHexInMap() ?? HoveredHex();

    /// <summary>
    /// <see cref="EditModeManager"/> 分发模式按键时取目标格的回调。
    /// 没有目标格时返回 (-1,-1)（正常情况下按键闸门已经拦下，走不到这里）。
    /// 选区模式下没有选中格时，把鼠标所在格选中，让按键作用的格子看得见；非选区模式不写选区。
    /// </summary>
    private (int col, int row) GetFocusHex()
    {
        var focus = ResolveFocusHex();
        if (focus is not { } f || _mapData == null) return (-1, -1);

        if (_hexSelector.PrimarySelected == null && _editModeManager.IsSelectionActive)
        {
            var filter = GetSelectionFilter();
            if (filter == null || filter(new HexCoord(f.Col, f.Row)))
                _hexSelector.Select(f.Col, f.Row, _mapData.MapWidth, _mapData.MapHeight);
        }
        return f;
    }

    /// <summary>
    /// 按键闸门（<see cref="Core.Input.KeyboardManager.BindingGate"/>）：当前模式的按键在没有目标格时不执行，改为提示。
    /// 全局键、数字键切换模式等其它绑定一律放行。
    /// </summary>
    private bool AllowSceneKeyBinding(CoreKeyBinding binding)
    {
        var handler = _editModeManager.CurrentHandler;
        if (handler == null || _mapData == null) return true;
        if (!handler.GetKeyBindings().Any(b => b.Id == binding.Id)) return true;
        if (ResolveFocusHex() != null) return true;

        PostSceneStatus(Loc.T("Scene.Status.NoTargetHex"));
        return false;
    }

    /// <summary>
    /// 非选区模式下左键单击：不改选区，只显示这一格的坐标与概况，信息窗口（Tab）也跟过来。
    /// 所有模式共用一个"当前格"（对齐 VB）：之后切到需要选区的模式时，这一格会被选中。
    /// </summary>
    private void InspectHex(int col, int row)
    {
        _inspectedHex = (col, row);
        _carriedPrimary = new HexCoord(col, row);
        var summary = SceneHexInfo.DescribeHex(col, row);
        if (summary != null) PostSceneStatus(summary);
        ShowHexInfoTarget();
        QueueSceneReadoutRefresh();
    }

    /// <summary>
    /// 选区变化：记下"被模式切换清掉"的主格。EditModeManager 离开选区模式时会先清空选区、
    /// 紧接着在同一调用里发出 ModeChanged；清空发生在本轮调度内的，才算切换造成的。
    /// 用户自己清空选区（Esc 等）之后不会有模式切换跟着发生，记录在本轮结束时作废。
    /// </summary>
    private void TrackPrimaryForModeSwitch(HexSelectionChangedEventArgs e)
    {
        if (e.CurrentPrimary.HasValue)
        {
            _carriedPrimary = null;
            return;
        }
        if (!e.PreviousPrimary.HasValue) return;

        _primaryClearedThisTurn = e.PreviousPrimary;
        // Send：当前调用栈一结束就作废，排在后续任何按键之前
        Dispatcher.BeginInvoke(DispatcherPriority.Send, () => _primaryClearedThisTurn = null);
    }

    /// <summary>
    /// 模式切换后把原来的选中格带过去（对齐 VB：所有模式共用一个选中格）。只带主格不带多选；
    /// 新模式不需要选区时先存着，等进入下一个选区模式再恢复；新模式的选区过滤不接受该格时不恢复。
    /// </summary>
    private void CarryPrimaryAcrossModeSwitch()
    {
        if (_primaryClearedThisTurn is { } cleared)
        {
            _carriedPrimary = cleared;
            _primaryClearedThisTurn = null;
        }

        if (_carriedPrimary is not { } carried || _mapData == null) return;
        if (!_editModeManager.IsSelectionActive || _hexSelector.HasSelection) return;
        if (!IsHexInMap(carried.Col, carried.Row))
        {
            _carriedPrimary = null;
            return;
        }

        var filter = GetSelectionFilter();
        if (filter != null && !filter(carried)) return;

        _hexSelector.Select(carried);
    }
}
