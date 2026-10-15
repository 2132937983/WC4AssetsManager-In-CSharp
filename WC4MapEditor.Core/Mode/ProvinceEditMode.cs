using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class ProvinceEditMode : IModeHandler
{
    public EditMode Mode => EditMode.ProvinceEdit;
    public string DisplayName => "省份编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Province;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Province };
    public bool RequiresSelection => true;

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

    private bool[]? _boundaryMask;
    private int _boundaryPixelWidth;
    private int _boundaryPixelHeight;

    /// <summary>是否处于边界绘制模式（U 键切换）</summary>
    public bool IsBoundaryDrawMode { get; private set; }

    /// <summary>笔宽（像素），用 [ ] 调整</summary>
    public int BoundaryBrushSize { get; private set; } = 3;

    /// <summary>已画的边界像素数</summary>
    public int DrawnBoundaryPixelCount { get; private set; }

    /// <summary>上一次落笔的逻辑像素坐标，用来把两次鼠标事件连成一条线</summary>
    private double _lastDrawX = double.NaN;
    private double _lastDrawY = double.NaN;

    /// <summary>是否已经画过边界（没画过时 P 键仍走图片识别）</summary>
    public bool HasDrawnBoundary => _boundaryMask != null && DrawnBoundaryPixelCount > 0;

    /// <summary>边界掩码（长度 = 画布宽 × 高），未初始化时为 null</summary>
    public bool[]? BoundaryMask => _boundaryMask;

    public int BoundaryPixelWidth => _boundaryPixelWidth;
    public int BoundaryPixelHeight => _boundaryPixelHeight;

    /// <summary>
    /// 按地图尺寸准备画布。尺寸变化说明换了地图，旧边界不再对得上，
    /// 直接重建丢弃（拉伸沿用只会得到错位的边界）。
    /// </summary>
    public void EnsureBoundaryCanvas(int mapWidth, int mapHeight)
    {
        // 画布要覆盖所有格子中心及其周边：列步进 hexW，行步进 hexH，
        // 奇数列整体下移半格，所以总高要多加半格。
        int w = (int)Math.Ceiling(mapWidth * Camera.HexHorizontalSpacing) + 1;
        int h = (int)Math.Ceiling(mapHeight * Camera.HexVerticalSpacing
                                  + Camera.HexVerticalSpacing / 2.0) + 1;

        if (_boundaryMask != null &&
            _boundaryPixelWidth == w && _boundaryPixelHeight == h)
        {
            return;
        }

        _boundaryPixelWidth = Math.Max(1, w);
        _boundaryPixelHeight = Math.Max(1, h);
        _boundaryMask = new bool[_boundaryPixelWidth * _boundaryPixelHeight];
        DrawnBoundaryPixelCount = 0;
        _lastDrawX = double.NaN;
        _lastDrawY = double.NaN;
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

        int added;
        if (double.IsNaN(_lastDrawX))
        {
            added = StampBrush((int)Math.Round(mapX), (int)Math.Round(mapY));
        }
        else
        {
            added = DrawLine(
                (int)Math.Round(_lastDrawX), (int)Math.Round(_lastDrawY),
                (int)Math.Round(mapX), (int)Math.Round(mapY));
        }

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

    /// <summary>清除所有手绘边界</summary>
    public void ClearDrawnBoundary()
    {
        if (_boundaryMask != null)
            Array.Clear(_boundaryMask);

        DrawnBoundaryPixelCount = 0;
        _lastDrawX = double.NaN;
        _lastDrawY = double.NaN;
    }

    public void IncreaseBoundaryBrushSize()
        => BoundaryBrushSize = Math.Min(64, BoundaryBrushSize + 1);

    public void DecreaseBoundaryBrushSize()
        => BoundaryBrushSize = Math.Max(1, BoundaryBrushSize - 1);

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

    public string HelpText =>
        "省份编辑模式快捷键:\n" +
        "左键点击 - 选择格子\n" +
        "C - 复制当前格子的省份值\n" +
        "V - 粘贴省份值到当前格子\n" +
        "Delete - 清除当前格子的省份值\n" +
        "H - 切换画笔模式\n" +
        "[ / ] - 调整笔宽（边界绘制模式下调边界笔宽，否则调画笔半径）\n" +
        "Q - 清空所有省份为0xFFFF\n" +
        "G - 为所有孤立省会生成省区\n" +
        "E - 扩展所有省区填满地图\n" +
        "I - 处理孤立和空白省区\n" +
        "X - 设置省区数据为格子索引\n" +
        "F - 洪水填充（需先复制省份值）\n" +
        "P - 生成省区（画过手绘边界就按它分，否则从背景图识别）\n" +
        "U - 切换边界绘制（开启后右键拖拽画边界线，像素级自由绘制）\n" +
        "Ctrl+U - 清除手绘边界\n" +
        "O - 切换多边形选择模式\n" +
        "Esc - 清除所有选择\n" +
        "\n" +
        "手绘边界以红色显示。适合图片界线识别不准的地图 —— 自己把省界描一遍，\n" +
        "再按 P，就按你描的线分省区（区域内按省会就近归属）。";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("PE_C", KeyCodes.C, KeyModifiers.None, "copy_province", "复制省份值"),
            new ModeKeyBinding("PE_V", KeyCodes.V, KeyModifiers.None, "paste_province", "粘贴省份值"),
            new ModeKeyBinding("PE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清除省份值"),
            new ModeKeyBinding("PE_R", KeyCodes.R, KeyModifiers.None, "toggle_rect_select", "切换矩形选择模式"),
            new ModeKeyBinding("PE_P", KeyCodes.P, KeyModifiers.None, "generate_provinces_from_background", "从背景图识别省区"),
            new ModeKeyBinding("PE_O", KeyCodes.O, KeyModifiers.None, "toggle_polygon_select", "切换多边形选择模式"),
            new ModeKeyBinding("PE_H", KeyCodes.H, KeyModifiers.None, "toggle_brush", "切换画笔模式"),
            new ModeKeyBinding("PE_BracketOpen", KeyCodes.OemOpenBrackets, KeyModifiers.None, "decrease_brush_radius", "减小画笔半径"),
            new ModeKeyBinding("PE_BracketClose", KeyCodes.OemCloseBrackets, KeyModifiers.None, "increase_brush_radius", "增大画笔半径"),
            new ModeKeyBinding("PE_Q", KeyCodes.Q, KeyModifiers.None, "clear_all_provinces", "清空所有省份为0xFFFF"),
            new ModeKeyBinding("PE_G", KeyCodes.G, KeyModifiers.None, "generate_isolated_capitals", "为孤立省会生成省区"),
            new ModeKeyBinding("PE_E", KeyCodes.E, KeyModifiers.None, "expand_all_provinces", "扩展所有省区填满地图"),
            new ModeKeyBinding("PE_I", KeyCodes.I, KeyModifiers.None, "process_isolated_empty", "处理孤立和空白省区"),
            new ModeKeyBinding("PE_T", KeyCodes.T, KeyModifiers.None, "rebalance_isolated_capitals", "孤立省会重新划分"),
            new ModeKeyBinding("PE_X", KeyCodes.X, KeyModifiers.None, "set_province_to_index", "设置省区数据为格子索引"),
            new ModeKeyBinding("PE_F", KeyCodes.F, KeyModifiers.None, "flood_fill", "洪水填充"),
            new ModeKeyBinding("PE_Escape", KeyCodes.Escape, KeyModifiers.None, "clear_selection", "清除所有选择"),
            new ModeKeyBinding("PE_S", KeyCodes.S, KeyModifiers.None, "apply", "设置省份值"),
            new ModeKeyBinding("PE_U", KeyCodes.U, KeyModifiers.None, "toggle_boundary_draw", "切换边界绘制模式"),
            new ModeKeyBinding("PE_CtrlU", KeyCodes.U, KeyModifiers.Ctrl, "clear_drawn_boundary", "清除手绘边界"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var province = context.GetModifier<ProvinceModifier>()!;
        var selector = HexSelector.Instance;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置省份 ({col},{row})", () => province.Apply(col, row));
                modified = true;
                break;

            case "remove":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量清除省份 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.Remove(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"清除省份 ({col},{row})", () => province.Remove(col, row));
                        modified = true;
                    }
                }
                break;

            case "copy_province":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        var coords = selected.Select(h => (h.Col, h.Row)).ToList();
                        var result = province.CopyProvinceGroup(coords);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    }
                    else
                    {
                        var result = province.CopyProvinceValue(col, row);
                        SyncCopiedProvinceToGlobal(province);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    }
                    return true;
                }

            case "paste_province":
                {
                    SyncGlobalCopiedProvinceToLocal(province);
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量粘贴省份 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.PasteProvinceValue(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"粘贴省份 ({col},{row})", () => province.PasteProvinceValue(col, row));
                        modified = true;
                    }
                }
                break;

            case "toggle_rect_select":
                {
                    bool newState = province.ToggleRectangleSelectMode();
                    context.RaiseStatusMessage?.Invoke(newState ? "已进入矩形选择模式" : "已退出矩形选择模式");
                }
                return true;

            case "toggle_polygon_select":
                {
                    bool newState = province.TogglePolygonSelectMode();
                    context.RaiseStatusMessage?.Invoke(newState ? "已进入多边形选择模式" : "已退出多边形选择模式");
                }
                return true;

            case "toggle_brush":
                context.NotifyBrushToggled?.Invoke();
                return true;

            case "toggle_boundary_draw":
                {
                    IsBoundaryDrawMode = !IsBoundaryDrawMode;
                    if (IsBoundaryDrawMode && context.MapData != null)
                        EnsureBoundaryCanvas(context.MapData.MapWidth, context.MapData.MapHeight);

                    context.RaiseStatusMessage?.Invoke(IsBoundaryDrawMode
                        ? $"边界绘制：开 —— 右键拖拽画线，笔宽 {BoundaryBrushSize}px（用 [ ] 调整）"
                        : "边界绘制：关");
                    context.NotifyBoundaryDrawToggled?.Invoke();
                }
                return true;

            case "clear_drawn_boundary":
                {
                    ClearDrawnBoundary();
                    context.RaiseStatusMessage?.Invoke("已清除手绘边界");
                    context.NotifyBoundaryDrawToggled?.Invoke();
                }
                return true;

            case "decrease_brush_radius":
                {
                    // 边界绘制模式下 [ ] 调的是边界画笔，否则调省区画笔 —— 两者是独立的笔刷，
                    // 边界线通常要求更细，共用一个半径会很别扭。
                    if (IsBoundaryDrawMode)
                    {
                        DecreaseBoundaryBrushSize();
                        context.RaiseStatusMessage?.Invoke($"边界笔宽: {BoundaryBrushSize} px");
                        context.NotifyBoundaryDrawToggled?.Invoke();
                    }
                    else if (province.IsBrushMode)
                    {
                        province.DecreaseBrushRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {province.BrushRadius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                }
                return true;

            case "increase_brush_radius":
                {
                    if (IsBoundaryDrawMode)
                    {
                        IncreaseBoundaryBrushSize();
                        context.RaiseStatusMessage?.Invoke($"边界笔宽: {BoundaryBrushSize} px");
                        context.NotifyBoundaryDrawToggled?.Invoke();
                    }
                    else if (province.IsBrushMode)
                    {
                        province.IncreaseBrushRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {province.BrushRadius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                }
                return true;

            case "clear_all_provinces":
                {
                    if (context.DialogService != null)
                    {
                        bool confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要清空所有省份为0xFFFF吗？\n\n此操作不可撤销！", "确定", "取消");
                        if (!confirmed) return false;
                    }
                    context.RecordMultiCellProvinceChange("清空所有省份为0xFFFF", () =>
                    {
                        var result = province.ClearAllProvinces();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "generate_isolated_capitals":
                {
                    context.RecordMultiCellProvinceChange("为孤立省会生成省区", () =>
                    {
                        var result = province.GenerateProvincesForIsolatedCapitals();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "expand_all_provinces":
                {
                    context.RecordMultiCellProvinceChange("扩展所有省区填满地图", () =>
                    {
                        var result = province.ExpandAllProvincesToFillMap();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "process_isolated_empty":
                {
                    context.RecordMultiCellProvinceChange("处理孤立和空白省区", () =>
                    {
                        var result = province.ProcessIsolatedAndEmptyProvinces();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "rebalance_isolated_capitals":
                {
                    context.RecordMultiCellProvinceChange("孤立省会重新划分", () =>
                    {
                        var result = province.RebalanceIsolatedCapitals();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "set_province_to_index":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量设置省区索引 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.SetProvinceValueToIndex(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"设置省区索引 ({col},{row})", () => province.SetProvinceValueToIndex(col, row));
                        modified = true;
                    }
                }
                break;

            case "generate_provinces_from_background":
                {
                    // P 键：手绘边界优先。
                    // 用户特意描过的界线，比从图片灰度里猜出来的可靠得多，
                    // 所以只要画过就用它；没画过才回退到背景图识别。
                    var drawnMask = BoundaryMask;
                    if (drawnMask != null && HasDrawnBoundary)
                    {
                        int maskWidth = BoundaryPixelWidth;
                        int maskHeight = BoundaryPixelHeight;

                        context.RecordMultiCellProvinceChange("按手绘边界分省区", () =>
                        {
                            var result = province.GenerateProvincesFromDrawnBoundary(
                                drawnMask, maskWidth, maskHeight);
                            context.RaiseStatusMessage?.Invoke(result.Message);
                        });
                        modified = true;
                        break;
                    }

                    // 没画过边界 → 走原来的图片识别。
                    // 省会既可能来自 Capitals 列表，也可能是"省区值 == 自身索引"的格子，
                    // 所以这里只检查图片，省会交给生成器统一识别。
                    if (context.ViewLayerImage == null)
                    {
                        context.RaiseStatusMessage?.Invoke(
                            "没有可用的边界：先用 U 键描出省界，或加载视图层图片以便从图片识别");
                        return false;
                    }

                    context.RecordMultiCellProvinceChange("从背景图识别省区", () =>
                    {
                        var result = province.GenerateProvincesFromBackground(context.ViewLayerImage);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "flood_fill":
                {
                    SyncGlobalCopiedProvinceToLocal(province);
                    context.RecordMultiCellProvinceChange($"洪水填充 ({col},{row})", () =>
                    {
                        var result = province.FloodFill(col, row);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "clear_selection":
                selector.ClearSelection();
                context.RaiseStatusMessage?.Invoke("已清除所有选择");
                return true;
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    private static void SyncCopiedProvinceToGlobal(ProvinceModifier province)
    {
        var clipboard = MapClipboard.Instance;
        var copiedData = province.GetCopiedProvinceData();
        var anchor = province.GetCopyAnchor();

        if (copiedData.HasValue)
        {
            clipboard.GlobalCopiedProvince = copiedData.Value;
            clipboard.GlobalCopiedProvinceFromCol = anchor.col;
            clipboard.GlobalCopiedProvinceFromRow = anchor.row;
        }

        var copiedGroup = province.GetCopiedProvinceGroup();
        clipboard.GlobalCopiedProvinceHexes.Clear();
        if (copiedGroup != null && copiedGroup.Count > 0)
        {
            foreach (var kv in copiedGroup)
                clipboard.GlobalCopiedProvinceHexes[kv.Key] = kv.Value;
            clipboard.GlobalCopiedProvinceRegionMinCol = anchor.col;
            clipboard.GlobalCopiedProvinceRegionMinRow = anchor.row;
        }
    }

    private static void SyncGlobalCopiedProvinceToLocal(ProvinceModifier province)
    {
        var clipboard = MapClipboard.Instance;
        if (clipboard.GlobalCopiedProvince == null) return;

        province.SetCopiedProvinceData(clipboard.GlobalCopiedProvince.Value,
            clipboard.GlobalCopiedProvinceFromCol, clipboard.GlobalCopiedProvinceFromRow);

        if (clipboard.GlobalCopiedProvinceHexes.Count > 0)
        {
            province.SetCopiedProvinceGroup(clipboard.GlobalCopiedProvinceHexes,
                clipboard.GlobalCopiedProvinceRegionMinCol, clipboard.GlobalCopiedProvinceRegionMinRow);
        }
    }
}