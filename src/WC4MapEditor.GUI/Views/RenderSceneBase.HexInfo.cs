using WC4MapEditor.Core.Services;
using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// Tab 格子信息窗口：内容随编辑模式切换视图（省份 / 建筑 / 单位 / 归属 / 援军……），
/// 数据修改、撤销重做后自动刷新，读的永远是本场景当前的地图（打开 / 新建 / 切换场景后不会停在旧地图上）。
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>数据连续变化时（画笔拖动、脚本批量执行）合并刷新，对齐 VB 的 100ms 节流</summary>
    private static readonly TimeSpan HexInfoRefreshDelay = TimeSpan.FromMilliseconds(100);

    private HexInfoService? _sceneHexInfo;
    private DispatcherTimer? _hexInfoRefreshTimer;

    /// <summary>非选区模式下左键查看的格子（选区模式下以选区主格为准）</summary>
    private (int Col, int Row)? _inspectedHex;

    /// <summary>绑定到本场景当前地图的格子信息服务：每次查询都取 _mapData，换图后无需同步。</summary>
    private HexInfoService SceneHexInfo => _sceneHexInfo ??= new HexInfoService(() => _mapData);

    private HexInfoDisplayMode CurrentHexInfoMode => HexInfoService.DisplayModeFor(_editModeManager.CurrentMode, SceneType);

    private void OnToggleHexInfo()
    {
        if (_hexInfoWindow != null)
        {
            _hexInfoWindow.PlayFadeOutAndClose();
            _hexInfoWindow = null;
            RestoreFocus();
            return;
        }

        var window = new HexInfoWindow(SceneHexInfo)
        {
            Left = Window.Left + 10,
            Top = Window.Top + 10
        };
        // 窗口被别的途径关掉（场景销毁、系统关闭）时同步清掉引用，之后按 Tab 重新打开
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_hexInfoWindow, window)) _hexInfoWindow = null;
        };
        _hexInfoWindow = window;
        ShowHexInfoTarget();
        window.Show();
        RestoreFocus();
    }

    /// <summary>窗口显示哪一格：选区主格优先，其次是左键查看过的格子，再其次是鼠标所在格。</summary>
    private (int Col, int Row)? HexInfoTarget()
    {
        if (PrimaryHexInMap() is { } primary) return primary;
        if (_inspectedHex is { } inspected && IsHexInMap(inspected.Col, inspected.Row)) return inspected;
        return HoveredHex();
    }

    /// <summary>按当前模式的视图把目标格显示到信息窗口（窗口没开时什么也不做）。</summary>
    private void ShowHexInfoTarget()
    {
        if (_hexInfoWindow == null) return;

        if (HexInfoTarget() is { } target)
            _hexInfoWindow.UpdateHexInfo(target.Col, target.Row, CurrentHexInfoMode);
        else
            _hexInfoWindow.ClearHexInfo();
    }

    /// <summary>数据变了：稍后（合并连续修改）重新查询窗口里显示的那一格。</summary>
    private void QueueHexInfoRefresh()
    {
        if (_hexInfoWindow == null) return;
        if (_hexInfoRefreshTimer == null)
        {
            _hexInfoRefreshTimer = new DispatcherTimer { Interval = HexInfoRefreshDelay };
            _hexInfoRefreshTimer.Tick += (_, _) =>
            {
                _hexInfoRefreshTimer.Stop();
                _hexInfoWindow?.Refresh();
            };
        }
        _hexInfoRefreshTimer.Stop();
        _hexInfoRefreshTimer.Start();
    }

    private void StopHexInfoRefresh() => _hexInfoRefreshTimer?.Stop();

    /// <summary>
    /// 换了地图（打开 / 新建 / 切换场景）：选区、之前查看的格子与要带过模式切换的选中格作废，
    /// 信息窗口改为显示新地图上的目标格。由重绘时发现地图对象变了触发。
    /// </summary>
    private void OnSceneMapReplaced()
    {
        _inspectedHex = null;
        _carriedPrimary = null;
        _shownPositionValid = false;
        // 选区里是旧地图的坐标，在新地图上没有意义
        _hexSelector.ClearSelection();
        ShowHexInfoTarget();
        QueueSceneReadoutRefresh();
    }
}
