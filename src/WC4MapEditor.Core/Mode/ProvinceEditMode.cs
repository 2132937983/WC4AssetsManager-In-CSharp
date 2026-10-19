using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 省份编辑模式（对应 VB 的 ProvinceModifier）：编辑每格的 16 位省区值（= 省会格子的索引，0xFFFF = 无省区）。
/// <para>
/// 数据操作都在 <see cref="ProvinceModifier"/> 里，这里只决定按键 / 鼠标手势对应哪个操作。
/// 模式自己只持有一样不进地图文件的会话状态：手绘边界（U 键）。
/// </para>
/// </summary>
public sealed class ProvinceEditMode : ModeBase
{
    public override EditMode Mode => EditMode.ProvinceEdit;
    protected override string LocKey => "Province";
    protected override string DefaultDisplayName => "省份编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Province;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Province };
    public override bool RequiresSelection => true;

    /// <summary>省区配色与边界、省会标记，以及数据范围外格子的红叉（VB 的 ProvinceModifier.Draw 同样画 DrawClippedTiles）。</summary>
    public override ModeRenderLayers RenderLayers =>
        ModeRenderLayers.Province | ModeRenderLayers.ProvinceCapital | ModeRenderLayers.ClippedTiles;

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureCommand(ModeGesture.LeftClick, "select_tool_click", "选择格子（矩形选择模式下设起点，多边形选择模式下添加顶点）", OnSelectToolLeftClick)
         .GestureCommand(ModeGesture.RightClick, "select_tool_finish", "完成矩形选择/取消多边形/画笔绘制", OnSelectToolRightClick)
         .HelpLine("box_select", ModeGesture.RightDrag, "框选多格（Shift 加选，Ctrl 减选）")
         .Command("PE_C", KeyCodes.C, "copy_province", "复制省份值", CopyProvince)
         .Edit("PE_V", KeyCodes.V, "paste_province", "粘贴省份值", PasteProvince)
         .Edit("PE_Delete", KeyCodes.Delete, "remove", "清除省份值", a => ForEachTarget(a, a.Module<ProvinceModifier>().Remove))
         .Note("region", "多选时 C 复制整个区域，V 以选区左上角（单选时为当前格）为起点粘贴，区域内的省会随区域移动")
         .Command("PE_R", KeyCodes.R, "toggle_rect_select", "切换矩形选择模式", a => ToggleSelectTool(a, ProvinceSelectTool.Rectangle))
         .Command("PE_O", KeyCodes.O, "toggle_polygon_select", "切换多边形选择模式", a => ToggleSelectTool(a, ProvinceSelectTool.Polygon))
         .Command("PE_Enter", KeyCodes.Enter, "close_polygon", "闭合多边形（也可左键点回第一个顶点）", ClosePolygon)
         .Edit("PE_S", KeyCodes.S, "apply", "用选中的格子创建省区（省会取最靠近中心的陆地格）", CreateProvinceFromSelection)
         .Command("PE_H", KeyCodes.H, "toggle_brush", "切换画笔模式", ToggleBrush)
         .Command("PE_BracketOpen", KeyCodes.OemOpenBrackets, "decrease_brush_radius", "减小画笔半径", a => ChangePenSize(a, -1))
         .Command("PE_BracketClose", KeyCodes.OemCloseBrackets, "increase_brush_radius", "增大画笔半径", a => ChangePenSize(a, +1))
         .Note("brush", "画笔模式操作：先按C复制省份值，然后按住右键拖动绘制")
         .Edit("PE_Q", KeyCodes.Q, "clear_all_provinces", "清空所有省份为0xFFFF", ClearAllProvincesAsync)
         .Edit("PE_G", KeyCodes.G, "generate_isolated_capitals", "为所有孤立省会生成省区",
             a => RunWholeMap(a, p => p.GenerateProvincesForIsolatedCapitals()))
         .Edit("PE_E", KeyCodes.E, "expand_all_provinces", "扩展所有省区填满地图",
             a => RunWholeMap(a, p => p.ExpandAllProvincesToFillMap()))
         .Edit("PE_I", KeyCodes.I, "process_isolated_empty", "处理孤立和空白省区",
             a => RunWholeMap(a, p => p.ProcessIsolatedAndEmptyProvinces()))
         .Edit("PE_T", KeyCodes.T, "rebalance_isolated_capitals", "孤立省会重新划分",
             a => RunWholeMap(a, p => p.RebalanceIsolatedCapitals()))
         .Edit("PE_X", KeyCodes.X, "set_province_to_index", "设置省区数据为格子索引",
             a => ForEachTarget(a, a.Module<ProvinceModifier>().SetProvinceValueToIndex))
         .Edit("PE_F", KeyCodes.F, "flood_fill", "洪水填充（需先复制省份值）", FloodFill)
         .Edit("PE_P", KeyCodes.P, "generate_provinces_from_background", "生成省区（画过手绘边界就按它分，否则从背景图识别）", GenerateProvinces)
         .Command("PE_U", KeyCodes.U, "toggle_boundary_draw", "切换边界绘制（开启后右键拖拽画边界线，像素级自由绘制）", ToggleBoundaryDraw)
         .Command("PE_CtrlU", KeyChord.Ctrl(KeyCodes.U), "clear_drawn_boundary", "清除手绘边界", ClearDrawnBoundary)
         .Note("pen_width", "边界绘制模式下 [ / ] 调整的是边界笔宽")
         .GestureCommand(ModeGesture.Escape, "clear_selection", "清除所有选择", ClearSelection)
         .Note("boundary1", "手绘边界以红色显示。适合图片界线识别不准的地图 —— 自己把省界描一遍，")
         .Note("boundary2", "再按 P，就按你描的线分省区（区域内按省会就近归属）。");
    }

    // ==================== 选择工具（R 矩形 / O 多边形）与 Esc ====================

    /// <summary>
    /// 左键：场景已先把点到的格子设为选区。矩形工具记下起点；多边形工具加顶点 / 点回第一个顶点闭合，
    /// 进行中的折线与闭合后的多边形都直接显示成选区。没开工具时什么也不做。
    /// </summary>
    private static bool OnSelectToolLeftClick(ModeActionArgs a)
    {
        if (a.Map == null || !IsInMap(a.Map, a.Cell)) return false;

        var province = a.Module<ProvinceModifier>();
        switch (province.SelectTool)
        {
            case ProvinceSelectTool.Rectangle:
                province.SetRectangleAnchor(a.Cell);
                a.Status(Loc.F("Mode.Province.Msg.RectAnchor", a.Col, a.Row));
                return true;

            case ProvinceSelectTool.Polygon:
                ShowPolygonResult(a, province.AddPolygonVertex(a.Cell), province.PolygonVertices, a.Cell);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Enter：闭合进行中的多边形（与左键点回第一个顶点相同）。</summary>
    private static bool ClosePolygon(ModeActionArgs a)
    {
        var province = a.Module<ProvinceModifier>();
        var vertices = province.PolygonVertices;
        if (!province.IsPolygonSelectMode || vertices.Count == 0)
        {
            a.Status(Loc.T("Mode.Province.Msg.PolygonNone", "没有进行中的多边形：先按 O 进入多边形选择模式，再左键添加顶点"));
            return false;
        }

        var last = vertices[^1];
        var result = province.ClosePolygon();
        if (result is not { } closed) return false;

        ShowPolygonResult(a, closed, vertices, last);
        return closed.Kind == PolygonClickKind.Closed;
    }

    /// <summary>
    /// 把多边形工具的结果显示成选区：闭合后是多边形覆盖的格子，进行中是折线经过的格子。
    /// 主选格放在 <paramref name="focus"/>（刚点的格子 / 最后一个顶点）上，之后的 C / V / S 等以它为焦点格。
    /// </summary>
    private static void ShowPolygonResult(ModeActionArgs a, PolygonClick click, IReadOnlyList<HexCoord> vertices, HexCoord focus)
    {
        var selector = HexSelector.Instance;
        selector.SetSelection(click.Hexes);
        selector.AddToSelection(focus);

        switch (click.Kind)
        {
            case PolygonClickKind.Closed:
                a.Status(Loc.F("Mode.Province.Msg.PolygonClosed", selector.SelectedCount));
                break;
            case PolygonClickKind.TooFewVertices:
                a.Status(Loc.T("Mode.Province.Msg.PolygonTooFew", "多边形至少需要 3 个顶点"));
                break;
            default:
                var first = vertices[0];
                a.Status(Loc.F("Mode.Province.Msg.PolygonVertex", vertices.Count, focus.Col, focus.Row, first.Col, first.Row));
                break;
        }
    }

    /// <summary>
    /// 右键单击（画笔开着时场景先拿去画了，到不了这里）：矩形工具以点到的格子为终点完成选择；
    /// 多边形工具取消进行中的多边形；其余情况与场景默认的右键单击一样选中该格。
    /// </summary>
    private static bool OnSelectToolRightClick(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null || !IsInMap(map, a.Cell)) return false;

        var province = a.Module<ProvinceModifier>();
        var selector = HexSelector.Instance;

        if (province.IsRectangleSelectMode && province.RectangleAnchor is { } anchor)
        {
            selector.ClearSelection();
            selector.SelectRange(anchor, a.Cell, map.MapWidth, map.MapHeight);
            a.Status(Loc.F("Mode.Province.Msg.RectSelected", selector.SelectedCount));
            return true;
        }

        if (province.IsPolygonSelectMode && province.CancelShape())
        {
            selector.ClearSelection();
            a.Status(Loc.T("Mode.Province.Msg.PolygonCancelled", "已取消多边形选择"));
            return true;
        }

        selector.Select(a.Col, a.Row, map.MapWidth, map.MapHeight);
        if (province.IsRectangleSelectMode)
            a.Status(Loc.T("Mode.Province.Msg.RectNeedsAnchor", "请先左键点击矩形起点"));
        return true;
    }

    /// <summary>Esc：取消进行中的矩形 / 多边形并清空选区；什么都没有时返回 false，由场景弹出返回主界面确认。</summary>
    private static bool ClearSelection(ModeActionArgs a)
    {
        bool cancelled = a.Module<ProvinceModifier>().CancelShape();
        var selector = HexSelector.Instance;
        if (selector.HasSelection)
        {
            selector.ClearSelection();
            cancelled = true;
        }

        if (cancelled)
            a.Status(Loc.T("Mode.Province.Msg.SelectionCleared", "已清除所有选择"));
        return cancelled;
    }

    /// <summary>R / O：切换矩形 / 多边形选择工具；开启时关掉画笔与边界绘制。</summary>
    private bool ToggleSelectTool(ModeActionArgs a, ProvinceSelectTool tool)
    {
        var province = a.Module<ProvinceModifier>();
        bool rectangle = tool == ProvinceSelectTool.Rectangle;
        bool on = false;
        ChangeSelectTool(province, () => on = rectangle ? province.ToggleRectangleSelectMode() : province.TogglePolygonSelectMode());
        if (on)
        {
            TurnOffBrush(a, province);
            TurnOffBoundaryDraw(a);
        }

        a.Status((rectangle, on) switch
        {
            (true, true) => Loc.T("Mode.Province.Msg.RectOn", "已进入矩形选择模式"),
            (true, false) => Loc.T("Mode.Province.Msg.RectOff", "已退出矩形选择模式"),
            (false, true) => Loc.T("Mode.Province.Msg.PolygonOn", "已进入多边形选择模式"),
            _ => Loc.T("Mode.Province.Msg.PolygonOff", "已退出多边形选择模式"),
        });
        return true;
    }

    // 画笔、手绘边界、矩形 / 多边形工具都要用鼠标右键，同一时刻只开一个：开启其中一个时关掉其余的。

    /// <summary>做一次会丢弃进行中图形的工具切换。进行中的多边形显示在选区里，被丢弃时把选区一起清掉。</summary>
    private static void ChangeSelectTool(ProvinceModifier province, Action change)
    {
        bool hadPolygon = province.PolygonVertices.Count > 0;
        change();
        if (hadPolygon && province.PolygonVertices.Count == 0)
            HexSelector.Instance.ClearSelection();
    }

    private static void TurnOffBrush(ModeActionArgs a, ProvinceModifier province)
    {
        if (!province.IsBrushMode) return;

        // 走 GUI 的开关流程，画笔设置窗口与预览圈才会一起收起；没有 GUI（命令行 / 脚本）时直接关掉
        a.Context.NotifyBrushToggled?.Invoke();
        if (province.IsBrushMode) province.Brush.Active = false;
    }

    private void TurnOffBoundaryDraw(ModeActionArgs a)
    {
        if (!IsBoundaryDrawMode) return;

        IsBoundaryDrawMode = false;
        a.Context.NotifyBoundaryDrawToggled?.Invoke();
    }

    // ==================== 复制 / 粘贴 / 逐格操作 ====================

    /// <summary>C：多选时复制整个区域，否则复制当前格的省区值。</summary>
    private static bool CopyProvince(ModeActionArgs a)
    {
        if (!HasTarget(a)) return false;

        var province = a.Module<ProvinceModifier>();
        var cells = a.TargetCells();
        var result = cells.Count > 1
            ? province.CopyProvinceRegion(cells, a.Cell)
            : province.CopyProvinceValue(a.Col, a.Row);
        Report(a, result);
        return result.Success;
    }

    /// <summary>
    /// V：剪贴板里有区域数据时把整个区域粘贴<b>一次</b>（多选时对齐选区左上角，否则对齐当前格）；
    /// 否则把单个省区值粘贴到每个目标格。整次粘贴记为一步撤销。
    /// </summary>
    private bool PasteProvince(ModeActionArgs a)
    {
        if (!HasTarget(a)) return false;

        var province = a.Module<ProvinceModifier>();
        if (!province.HasCopiedRegion)
            return ForEachTarget(a, province.PasteProvinceValue);

        var cells = a.TargetCells();
        var anchor = cells.Count > 1 ? new HexCoord(cells.Min(c => c.Col), cells.Min(c => c.Row)) : a.Cell;

        ModifierResult result = default;
        a.Context.RecordMultiCellProvinceChange(Describe(a, anchor),
            () => result = province.PasteProvinceRegion(anchor.Col, anchor.Row));
        Report(a, result);
        return result.Success;
    }

    /// <summary>S：把目标格（多选 = 整个选区）做成一个省区，省会取最靠近中心的陆地格。</summary>
    private bool CreateProvinceFromSelection(ModeActionArgs a)
    {
        if (!HasTarget(a)) return false;

        var province = a.Module<ProvinceModifier>();
        var cells = a.TargetCells();
        ModifierResult result = default;
        a.Context.RecordMultiCellProvinceChange(Describe(a, a.Cell), () => result = province.CreateProvinceFromHexes(cells));
        Report(a, result);
        return result.Success;
    }

    /// <summary>F：用剪贴板里的省区值从当前格开始洪水填充。</summary>
    private bool FloodFill(ModeActionArgs a)
    {
        if (!HasTarget(a)) return false;

        var province = a.Module<ProvinceModifier>();
        ModifierResult result = default;
        a.Context.RecordMultiCellProvinceChange(Describe(a, a.Cell), () => result = province.FloodFill(a.Col, a.Row));
        Report(a, result);
        return result.Success;
    }

    /// <summary>
    /// 对目标格（多选 = 整个选区，否则当前格）逐格执行单格操作，整体记一步撤销。
    /// 单格时显示该操作自己的结果，多格时显示汇总。
    /// </summary>
    private bool ForEachTarget(ModeActionArgs a, Func<int, int, ModifierResult> operation)
    {
        if (!HasTarget(a)) return false;

        var cells = a.TargetCells();
        ModifierResult last = default;
        int done = 0;
        a.Context.RecordMultiCellProvinceChange(Describe(a, a.Cell), () =>
        {
            foreach (var cell in cells)
            {
                last = operation(cell.Col, cell.Row);
                if (last.Success) done++;
            }
        });

        if (cells.Count == 1)
            Report(a, last);
        else
            a.Status(Loc.F("Mode.Province.Msg.Batch", ActionText(a), done, cells.Count));
        return done > 0;
    }

    /// <summary>整图操作（Q / G / E / I / T）：执行并整体记一步撤销。</summary>
    private bool RunWholeMap(ModeActionArgs a, Func<ProvinceModifier, ModifierResult> operation)
    {
        var province = a.Module<ProvinceModifier>();
        ModifierResult result = default;
        a.Context.RecordMultiCellProvinceChange(ActionText(a), () => result = operation(province));
        Report(a, result);
        return result.Success;
    }

    private async Task<bool> ClearAllProvincesAsync(ModeActionArgs a)
    {
        if (a.Dialogs != null)
        {
            bool confirmed = await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Province.Msg.ConfirmClearTitle", "确认操作"),
                Loc.T("Mode.Province.Msg.ConfirmClearAll", "确定要清空所有省份为0xFFFF吗？\n\n可以用 Ctrl+Z 撤销。"),
                Loc.T("Common.Ok", "确定"),
                Loc.T("Common.Cancel", "取消"));
            if (!confirmed) return false;
        }

        return RunWholeMap(a, p => p.ClearAllProvinces());
    }

    // ==================== 画笔 ====================

    private bool ToggleBrush(ModeActionArgs a)
    {
        var province = a.Module<ProvinceModifier>();
        bool turningOn = !province.IsBrushMode;
        if (turningOn)
        {
            ChangeSelectTool(province, province.DeactivateSelectTool);
            TurnOffBoundaryDraw(a);
        }

        a.Context.NotifyBrushToggled?.Invoke();

        // 画笔画的是剪贴板里的省区值：没复制过时画下去什么也不会发生，开画笔时就提醒
        if (turningOn && province.IsBrushMode && !province.CopiedProvinceValue.HasValue)
            a.Status(Loc.T("Mode.Province.Msg.BrushNeedsCopy", "画笔画的是复制的省份值：请先在格子上按 C 复制"));
        return true;
    }

    /// <summary>
    /// [ / ]：边界绘制模式下调边界笔宽，否则调省区画笔半径 —— 两者是独立的笔，边界线通常要细得多，共用一个半径会很别扭。
    /// </summary>
    private bool ChangePenSize(ModeActionArgs a, int delta)
    {
        if (IsBoundaryDrawMode)
        {
            BoundaryBrushSize = Math.Clamp(BoundaryBrushSize + delta, MinBoundaryBrushSize, MaxBoundaryBrushSize);
            a.Status(Loc.F("Mode.Province.Msg.BoundaryPenWidth", BoundaryBrushSize));
            a.Context.NotifyBoundaryDrawToggled?.Invoke();
            return true;
        }

        var province = a.Module<ProvinceModifier>();
        if (province.IsBrushMode)
        {
            if (delta > 0) province.IncreaseBrushRadius();
            else province.DecreaseBrushRadius();
            a.Status(Loc.F("Mode.Province.Msg.BrushRadius", province.BrushRadius));
            a.Context.NotifyBrushSizeChanged?.Invoke();
        }
        return true;
    }

    // ==================== 生成省区（P） ====================

    /// <summary>
    /// P：手绘边界优先 —— 用户特意描过的界线比从图片灰度里猜出来的可靠得多，所以只要当前地图上画过就用它；
    /// 没画过才回退到背景图识别。省会既可能来自 Capitals 列表，也可能是"省区值 == 自身索引"的格子，交给生成器统一识别。
    /// </summary>
    private bool GenerateProvinces(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null) return false;

        var province = a.Module<ProvinceModifier>();
        ModifierResult result = default;

        var drawnMask = GetBoundaryMask(map);
        if (drawnMask != null && DrawnBoundaryPixelCount > 0)
        {
            int maskWidth = BoundaryPixelWidth;
            int maskHeight = BoundaryPixelHeight;
            a.Context.RecordMultiCellProvinceChange(ActionText(a),
                () => result = province.GenerateProvincesFromDrawnBoundary(drawnMask, maskWidth, maskHeight));
        }
        else
        {
            var image = a.Context.ViewLayerImage;
            if (image == null)
            {
                a.Status(Loc.T("Mode.Province.Msg.NoBoundarySource", "没有可用的边界：先用 U 键描出省界，或加载视图层图片以便从图片识别"));
                return false;
            }

            a.Context.RecordMultiCellProvinceChange(ActionText(a),
                () => result = province.GenerateProvincesFromBackground(image));
        }

        Report(a, result);
        return result.Success;
    }

    // ==================== 手绘边界（U 键） ====================
    //
    // 为什么放在模式层而不是 Modifier：Core 是纯数据驱动的模型，没有"边界线"这种字段，
    // 也不该为一个编辑辅助状态去扩展地图格式。它只是本次编辑会话里的一层临时标注。
    //
    // 为什么用【像素】而不是格子：边界线常常比一个格子细得多，格子级的点阵画出来
    // 是一条锯齿状粗带，既贴不上地图形状，也当不了界线用。
    //
    // 坐标系与渲染层完全一致（Camera 的世界坐标，未叠加缩放与平移）：
    //     格子 (col,row) → 逻辑像素 (col * hexW, row * hexH + (col % 2) * hexH / 2)
    // 所以鼠标落在屏幕哪个位置，就画在画布哪个位置，中间不需要任何换算。
    // 画布与屏幕的对应关系交给渲染层的 WorldToScreenScaled 处理。
    //
    // 画布记着它属于哪张地图：换了地图（哪怕尺寸相同）旧边界就对不上了，不能拿去给新地图分省区。

    private const int MinBoundaryBrushSize = 1;
    private const int MaxBoundaryBrushSize = 64;

    private bool[]? _boundaryMask;
    private int _boundaryPixelWidth;
    private int _boundaryPixelHeight;
    private WeakReference<MapData>? _boundaryMap;

    /// <summary>上一次落笔的逻辑像素坐标，用来把两次鼠标事件连成一条线</summary>
    private double _lastDrawX = double.NaN;
    private double _lastDrawY = double.NaN;

    /// <summary>是否处于边界绘制模式（U 键切换）</summary>
    public bool IsBoundaryDrawMode { get; private set; }

    /// <summary>笔宽（像素），用 [ ] 调整</summary>
    public int BoundaryBrushSize { get; private set; } = 3;

    /// <summary>已画的边界像素数</summary>
    public int DrawnBoundaryPixelCount { get; private set; }

    public int BoundaryPixelWidth => _boundaryPixelWidth;
    public int BoundaryPixelHeight => _boundaryPixelHeight;

    /// <summary>
    /// <paramref name="map"/> 上画的边界掩码（长度 = 画布宽 × 高）；没建过画布、或画布属于别的地图 / 地图尺寸已变时返回 null。
    /// </summary>
    public bool[]? GetBoundaryMask(MapData? map)
        => map != null && IsBoundaryCanvasFor(map) ? _boundaryMask : null;

    /// <summary>
    /// 按地图准备画布。画布属于别的地图、或地图尺寸变了，旧边界都不再对得上，直接重建丢弃（拉伸沿用只会得到错位的边界）。
    /// </summary>
    public void EnsureBoundaryCanvas(MapData map)
    {
        if (IsBoundaryCanvasFor(map)) return;

        var (width, height) = CanvasSizeFor(map);
        _boundaryPixelWidth = width;
        _boundaryPixelHeight = height;
        _boundaryMask = new bool[width * height];
        _boundaryMap = new WeakReference<MapData>(map);
        DrawnBoundaryPixelCount = 0;
        EndBoundaryStroke();
    }

    /// <summary>
    /// 画到逻辑像素坐标 (mapX, mapY)，返回新增像素数。
    /// <para>
    /// 会从上一次落点连一条线过来 —— 鼠标拖拽时事件是离散的，
    /// 不连线的话快速拖动会画成一串断点。
    /// </para>
    /// </summary>
    public int DrawBoundaryTo(double mapX, double mapY)
    {
        if (_boundaryMask == null) return 0;

        int added = double.IsNaN(_lastDrawX)
            ? StampBrush((int)Math.Round(mapX), (int)Math.Round(mapY))
            : DrawLine((int)Math.Round(_lastDrawX), (int)Math.Round(_lastDrawY), (int)Math.Round(mapX), (int)Math.Round(mapY));

        _lastDrawX = mapX;
        _lastDrawY = mapY;
        DrawnBoundaryPixelCount += added;
        return added;
    }

    /// <summary>结束一笔（鼠标抬起），下一次落笔重新起线</summary>
    public void EndBoundaryStroke()
    {
        _lastDrawX = double.NaN;
        _lastDrawY = double.NaN;
    }

    private bool ToggleBoundaryDraw(ModeActionArgs a)
    {
        bool turningOn = !IsBoundaryDrawMode;
        if (turningOn)
        {
            var province = a.Module<ProvinceModifier>();
            ChangeSelectTool(province, province.DeactivateSelectTool);
            TurnOffBrush(a, province);
            if (a.Map != null) EnsureBoundaryCanvas(a.Map);
        }

        IsBoundaryDrawMode = turningOn;
        a.Status(turningOn
            ? Loc.F("Mode.Province.Msg.BoundaryOn", BoundaryBrushSize)
            : Loc.T("Mode.Province.Msg.BoundaryOff", "边界绘制：关"));
        a.Context.NotifyBoundaryDrawToggled?.Invoke();
        return true;
    }

    private bool ClearDrawnBoundary(ModeActionArgs a)
    {
        if (_boundaryMask != null)
            Array.Clear(_boundaryMask);
        DrawnBoundaryPixelCount = 0;
        EndBoundaryStroke();

        a.Status(Loc.T("Mode.Province.Msg.BoundaryCleared", "已清除手绘边界"));
        a.Context.NotifyBoundaryDrawToggled?.Invoke();
        return true;
    }

    private bool IsBoundaryCanvasFor(MapData map)
        => _boundaryMask != null
           && _boundaryMap != null && _boundaryMap.TryGetTarget(out var owner) && ReferenceEquals(owner, map)
           && CanvasSizeFor(map) == (_boundaryPixelWidth, _boundaryPixelHeight);

    /// <summary>画布要覆盖所有格子中心及其周边：列步进 hexW，行步进 hexH，奇数列整体下移半格，所以总高要多加半格。</summary>
    private static (int width, int height) CanvasSizeFor(MapData map)
    {
        int width = (int)Math.Ceiling(map.MapWidth * Camera.HexHorizontalSpacing) + 1;
        int height = (int)Math.Ceiling(map.MapHeight * Camera.HexVerticalSpacing + Camera.HexVerticalSpacing / 2.0) + 1;
        return (Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>按当前笔宽在 (cx, cy) 上盖一个方章，返回新增像素数</summary>
    private int StampBrush(int cx, int cy)
    {
        if (_boundaryMask == null) return 0;

        int half = BoundaryBrushSize / 2;
        int added = 0;

        for (int y = cy - half; y <= cy + half; y++)
        {
            if (y < 0 || y >= _boundaryPixelHeight) continue;
            int rowBase = y * _boundaryPixelWidth;

            for (int x = cx - half; x <= cx + half; x++)
            {
                if (x < 0 || x >= _boundaryPixelWidth) continue;

                int index = rowBase + x;
                if (_boundaryMask[index]) continue;

                _boundaryMask[index] = true;
                added++;
            }
        }

        return added;
    }

    /// <summary>Bresenham 画线：沿线逐点盖章，保证笔画连续</summary>
    private int DrawLine(int x0, int y0, int x1, int y1)
    {
        int added = 0;
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            added += StampBrush(x0, y0);
            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }

        return added;
    }

    // ==================== 辅助 ====================

    /// <summary>
    /// 单格类操作（C / V / Delete / X / F / S）要有选中的格子，与 VB 一样没有时提示"请先选择一个格子"。
    /// 场景在没有选区、鼠标又不在地图上时会把 (0,0) 当成焦点格传进来，若不拦下就会悄悄改掉 (0,0)。
    /// </summary>
    private static bool HasTarget(ModeActionArgs a)
    {
        if (a.Map != null && IsInMap(a.Map, a.Cell) && a.Context.Selection.Count > 0) return true;
        a.Status(Loc.T("Mode.Province.Msg.NoTarget", "请先选择一个格子"));
        return false;
    }

    private static bool IsInMap(MapData map, HexCoord cell)
        => cell.Col >= 0 && cell.Row >= 0 && cell.Col < map.MapWidth && cell.Row < map.MapHeight;

    private static void Report(ModeActionArgs a, ModifierResult result)
    {
        if (!string.IsNullOrEmpty(result.Message))
            a.Status(result.Message);
    }

    /// <summary>动作在当前语言下的说明文本（也用作撤销记录的描述）。</summary>
    private string ActionText(ModeActionArgs a) => Actions.Find(a.Action)?.Text ?? a.Action;

    private string Describe(ModeActionArgs a, HexCoord cell) => $"{ActionText(a)} ({cell.Col},{cell.Row})";
}
