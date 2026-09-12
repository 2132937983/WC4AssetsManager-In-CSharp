using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class TerrainPaintMode : IModeHandler
{
    private bool _isMovingSelection;
    private int _moveSelectionOffsetCol;
    private int _moveSelectionOffsetRow;
    private readonly HashSet<(int col, int row)> _originalSelectedHexes = [];

    public EditMode Mode => EditMode.TerrainPaint;
    public string DisplayName => "地形绘制";
    public ModifierKind PrimaryModifierKind => ModifierKind.Terrain;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Terrain };
    public bool RequiresSelection => true;

    public string HelpText =>
        "H - 使用画笔\n" +
        "C - 复制选中格子\n" +
        "V - 粘贴到选中格子\n" +
        "Y - 在该格绘制河流\n" +
        "P - 识别地形\n" +
        "U - 绿化平地\n" +
        "R - 随机平地变体\n" +
        "Shift+R - 随机当前层变体\n" +
        "F - 洪水填充\n" +
        "F4 - 创建海岸线\n" +
        "F5 - 处理海洋第二层\n" +
        "F6 - 导出HD文件\n" +
        "F7 - 经纬度换算并导出\n" +
        "F8 - 导出参考点配置\n" +
        "F9 - 导入参考点配置\n" +
        "Q - 添加经纬度参考点\n" +
        "T - 连接建筑（平地连接11-15类型建筑）\n" +
        "G - 按比例缩放地图（0.1-10.0）\n" +
        "[, ] - 调整画笔大小\n" +
        ",, . - 修改地形类型\n" +
        "Shift+, . - 修改变体\n" +
        "Z - 切换编辑层\n" +
        "B - 显示/隐藏网格\n" +
        "N - 显示/隐藏标签\n" +
        "F1 - 显示/隐藏第二层\n" +
        "Ctrl+F1 - 显示/隐藏帮助文本\n" +
        "I,J,K,L - 调整地图大小（单选或无选区时）\n" +
        "框选操作：\n" +
        "  右键拖动 - 框选区域\n" +
        "  Shift+右键拖动 - 新增选区\n" +
        "  Ctrl+右键拖动 - 剔除选区\n" +
        "  I,J,K,L - 移动选区\n" +
        "  Enter - 确认移动\n" +
        "  O - 剔除海洋格子";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("TP_BracketOpen", KeyCodes.OemOpenBrackets, KeyModifiers.None, "decrease_brush_size", "减小画笔大小"),
            new ModeKeyBinding("TP_BracketClose", KeyCodes.OemCloseBrackets, KeyModifiers.None, "increase_brush_size", "增大画笔大小"),
            new ModeKeyBinding("TP_Comma", KeyCodes.OemComma, KeyModifiers.None, "decrease_type", "上一个地形类型"),
            new ModeKeyBinding("TP_Period", KeyCodes.OemPeriod, KeyModifiers.None, "increase_type", "下一个地形类型"),
            new ModeKeyBinding("TP_ShiftComma", KeyCodes.OemComma, KeyModifiers.Shift, "decrease_decoration", "上一个变体"),
            new ModeKeyBinding("TP_ShiftPeriod", KeyCodes.OemPeriod, KeyModifiers.Shift, "increase_decoration", "下一个变体"),
            new ModeKeyBinding("TP_H", KeyCodes.H, KeyModifiers.None, "toggle_brush", "切换画笔"),
            new ModeKeyBinding("TP_C", KeyCodes.C, KeyModifiers.None, "copy", "复制"),
            new ModeKeyBinding("TP_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴"),
            new ModeKeyBinding("TP_O", KeyCodes.O, KeyModifiers.None, "remove_ocean_from_selection", "剔除海洋格子"),
            new ModeKeyBinding("TP_Y", KeyCodes.Y, KeyModifiers.None, "set_river", "绘制河流"),
            new ModeKeyBinding("TP_P", KeyCodes.P, KeyModifiers.None, "recognize_terrain", "识别地形"),
            new ModeKeyBinding("TP_U", KeyCodes.U, KeyModifiers.None, "greening", "绿化平地"),
            new ModeKeyBinding("TP_R", KeyCodes.R, KeyModifiers.None, "randomize_flat", "随机平地变体"),
            new ModeKeyBinding("TP_ShiftR", KeyCodes.R, KeyModifiers.Shift, "randomize_variant", "随机当前层变体"),
            new ModeKeyBinding("TP_F", KeyCodes.F, KeyModifiers.None, "flood_fill", "洪水填充"),
            new ModeKeyBinding("TP_F4", KeyCodes.F4, KeyModifiers.None, "create_coast", "创建海岸线"),
            new ModeKeyBinding("TP_F5", KeyCodes.F5, KeyModifiers.None, "process_ocean_layer2", "处理海洋第二层"),
            new ModeKeyBinding("TP_F6", KeyCodes.F6, KeyModifiers.None, "export_hd", "导出HD文件"),
            new ModeKeyBinding("TP_T", KeyCodes.T, KeyModifiers.None, "connect_buildings", "连接建筑"),
            new ModeKeyBinding("TP_G", KeyCodes.G, KeyModifiers.None, "scale_map", "按比例缩放地图"),
            new ModeKeyBinding("TP_I", KeyCodes.I, KeyModifiers.None, "ijkl_action", "I键-调整地图/移动选区"),
            new ModeKeyBinding("TP_J", KeyCodes.J, KeyModifiers.None, "jkl_action", "J键-调整地图/移动选区"),
            new ModeKeyBinding("TP_K", KeyCodes.K, KeyModifiers.None, "kl_action", "K键-调整地图/移动选区"),
            new ModeKeyBinding("TP_L", KeyCodes.L, KeyModifiers.None, "l_action", "L键-调整地图/移动选区"),
            new ModeKeyBinding("TP_Enter", KeyCodes.Enter, KeyModifiers.None, "confirm_selection_move", "确认选区移动"),
            new ModeKeyBinding("TP_Escape", KeyCodes.Escape, KeyModifiers.None, "cancel_selection_move", "取消选区移动"),
            new ModeKeyBinding("TP_Z", KeyCodes.Z, KeyModifiers.None, "toggle_layer", "切换编辑层"),
            new ModeKeyBinding("TP_B", KeyCodes.B, KeyModifiers.None, "toggle_hex_borders", "显示/隐藏网格"),
            new ModeKeyBinding("TP_N", KeyCodes.N, KeyModifiers.None, "toggle_labels", "显示/隐藏标签"),
            new ModeKeyBinding("TP_F1", KeyCodes.F1, KeyModifiers.None, "toggle_show_layer2", "切换第二层地形显示"),
            new ModeKeyBinding("TP_CtrlF1", KeyCodes.F1, KeyModifiers.Ctrl, "toggle_help_text", "显示/隐藏帮助文本"),
            new ModeKeyBinding("TP_CtrlZ", KeyCodes.Z, KeyModifiers.Ctrl, "undo", "撤销"),
            new ModeKeyBinding("TP_CtrlY", KeyCodes.Y, KeyModifiers.Ctrl, "redo", "重做"),
            new ModeKeyBinding("TP_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "删除"),
            new ModeKeyBinding("TP_Q", KeyCodes.Q, KeyModifiers.None, "add_geo_ref", "添加经纬度参考点"),
            new ModeKeyBinding("TP_F7", KeyCodes.F7, KeyModifiers.None, "geo_calculate", "经纬度换算并导出"),
            new ModeKeyBinding("TP_F8", KeyCodes.F8, KeyModifiers.None, "geo_export_ref", "导出参考点配置"),
            new ModeKeyBinding("TP_F9", KeyCodes.F9, KeyModifiers.None, "geo_import_ref", "导入参考点配置"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var terrain = context.GetModifier<TerrainModifier>()!;
        bool modified = false;

        // I/J/K/L 键根据是否有选区映射为不同动作
        if (action is "ijkl_action" or "jkl_action" or "kl_action" or "l_action")
        {
            return await HandleIjklAction(action, col, row, context, terrain);
        }

        switch (action)
        {
            case "decrease_brush_size":
                terrain.BrushSize = Math.Max(0, terrain.BrushSize - 1);
                context.RaiseStatusMessage?.Invoke($"画笔大小: {terrain.BrushSize}");
                context.NotifyBrushSizeChanged?.Invoke();
                return true;
            case "increase_brush_size":
                terrain.BrushSize = Math.Min(20, terrain.BrushSize + 1);
                context.RaiseStatusMessage?.Invoke($"画笔大小: {terrain.BrushSize}");
                context.NotifyBrushSizeChanged?.Invoke();
                return true;
            case "increase_type":
                context.RecordMultiCellChange($"增加地形类型 ({col},{row})", () => terrain.ChangeTerrainType(col, row, 1));
                modified = true; break;
            case "decrease_type":
                context.RecordMultiCellChange($"减少地形类型 ({col},{row})", () => terrain.ChangeTerrainType(col, row, -1));
                modified = true; break;
            case "increase_decoration":
                context.RecordMultiCellChange($"增加变体 ({col},{row})", () => terrain.ChangeDecoration(col, row, 1));
                modified = true; break;
            case "decrease_decoration":
                context.RecordMultiCellChange($"减少变体 ({col},{row})", () => terrain.ChangeDecoration(col, row, -1));
                modified = true; break;
            case "copy":
                {
                    var selected = HexSelector.Instance.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        var coords = selected.Select(h => (h.Col, h.Row));
                        terrain.CopyTerrainGroup(coords);
                    }
                    else
                    {
                        terrain.CopyTerrain(col, row);
                    }
                    SyncCopiedTerrainToGlobal(terrain);
                    return true;
                }
            case "paste":
                SyncGlobalCopiedTerrainToLocal(terrain);
                context.RecordMultiCellChange($"粘贴地形 ({col},{row})", () => terrain.PasteTerrain(col, row));
                modified = true; break;
            case "flood_fill":
                context.RecordMultiCellChange($"洪水填充 ({col},{row})", () => terrain.FloodFill(col, row, (byte)terrain.BrushTerrainType));
                modified = true; break;
            case "toggle_layer":
                terrain.EditLayer = (terrain.EditLayer % 3) + 1;
                context.RaiseStatusMessage?.Invoke($"编辑层: {terrain.EditLayer}");
                return true;
            case "set_river":
                {
                    if (context.MapData == null) return true;
                    var cell = context.MapData.GetTerrain(col, row);

                    byte currentRiverValue = cell.RiverValue;

                    if (context.DialogService != null)
                    {
                        var (confirmed, newRiverValue) = await context.DialogService.ShowRiverSettingDialogAsync(currentRiverValue);
                        if (confirmed)
                        {
                            context.RecordMultiCellChange($"设置河流 ({col},{row})", () => terrain.SetRiverValue(col, row, newRiverValue));
                            modified = true;
                        }
                    }
                    else
                    {
                        // 回退：直接设置河流值为1
                        context.RecordMultiCellChange($"绘制河流 ({col},{row})", () => terrain.SetRiverValue(col, row, 1));
                        modified = true;
                    }
                }
                break;
            case "greening":
                return await ShowInputDialogAndExecuteCli(context, "设置绿化值", "请输入绿化值（0-100）：", 50, "greening");
            case "randomize_flat":
                return await ShowInputDialogAndExecuteCli(context, "随机化平地地形", "请输入随机化概率（0-100）：", 50, "random_flat");
            case "randomize_variant":
                return await ShowInputDialogAndExecuteCli(context, "随机化地形变体", $"请输入第{terrain.EditLayer}层地形变体随机化概率（0-100）：", 50, "random_variant");
            case "recognize_terrain":
                {
                    if (context.RecognizeTerrainCallback != null)
                    {
                        var (success, modifiedCount) = await context.RecognizeTerrainCallback();
                        if (success)
                            context.RaiseStatusMessage?.Invoke($"地形识别完成，共修改 {modifiedCount} 个格子");
                        else
                            context.RaiseStatusMessage?.Invoke("地形识别未执行或失败，请检查日志或确保已加载视图层图片");
                        return true;
                    }
                    context.RaiseStatusMessage?.Invoke("识别地形功能需要视图层支持");
                    return true;
                }
            case "create_coast":
                {
                    var result = terrain.CreateCoast(null);
                    context.RecordMultiCellChange("创建海岸线", () => { });
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(result.Message);
                }
                return true;
            case "process_ocean_layer2":
                {
                    var result = terrain.ProcessOceanSecondLayer(null);
                    context.RecordMultiCellChange("处理海洋第二层", () => { });
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(result.Message);
                }
                return true;
            case "export_hd":
                {
                    var result = terrain.ExportHdFile();
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    if (result.Success && context.DialogService != null)
                    {
                        await context.DialogService.ShowNotificationDialogAsync("HD文件导出成功", result.Message ?? "导出完成", "确定");
                    }
                }
                return true;
            case "connect_buildings":
                {
                    var result = terrain.ConnectBuildings();
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    if (result.Success) context.NotifyDataModified?.Invoke();
                }
                return true;
            case "scale_map":
                return await ShowInputDialogAndExecuteCliDouble(context, "按比例缩放地图", "请输入缩放比例（0.1-10.0）：", 1.0, "scale_map");
            case "resize_map_up":
                return await ShowResizeMapDialog(context, "up");
            case "resize_map_down":
                return await ShowResizeMapDialog(context, "down");
            case "resize_map_left":
                return await ShowResizeMapDialog(context, "left");
            case "resize_map_right":
                return await ShowResizeMapDialog(context, "right");
            case "move_selection_up":
                StartOrUpdateSelectionMove(context, 0, -1);
                return true;
            case "move_selection_down":
                StartOrUpdateSelectionMove(context, 0, 1);
                return true;
            case "move_selection_left":
                StartOrUpdateSelectionMove(context, -1, 0);
                return true;
            case "move_selection_right":
                StartOrUpdateSelectionMove(context, 1, 0);
                return true;
            case "confirm_selection_move":
                ConfirmSelectionMove(context, terrain);
                return true;
            case "cancel_selection_move":
                CancelSelectionMove(context);
                return true;
            case "remove_ocean_from_selection":
                RemoveOceanHexesFromSelection(context);
                return true;
            case "toggle_brush":
                context.NotifyBrushToggled?.Invoke();
                return true;
            case "geo_calculate":
                if (context.GeoCalculateCallback != null)
                    await context.GeoCalculateCallback();
                return true;
            case "geo_export_ref":
                if (context.GeoExportRefCallback != null)
                    await context.GeoExportRefCallback();
                return true;
            case "geo_import_ref":
                if (context.GeoImportRefCallback != null)
                    await context.GeoImportRefCallback();
                return true;
            case "add_geo_ref":
                if (context.AddGeoRefCallback != null)
                    await context.AddGeoRefCallback();
                return true;
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    private static async Task<bool> ShowInputDialogAndExecuteCli(ModeContext context,
        string title, string description, int defaultValue, string cliCommand)
    {
        if (context.DialogService == null)
        {
            context.CliCommandExecutor?.Execute($"{cliCommand} {defaultValue}");
            return true;
        }

        var result = await context.DialogService.ShowInputDialogAsync(title, description, defaultValue.ToString(), 0, 100);
        if (result == null) return false;

        if (int.TryParse(result, out int value))
        {
            value = Math.Clamp(value, 0, 100);
            context.CliCommandExecutor?.Execute($"{cliCommand} {value}");
            return true;
        }

        return false;
    }

    private static void SyncCopiedTerrainToGlobal(TerrainModifier terrain)
    {
        var sceneManager = RenderSceneManager.Instance;
        var copiedData = terrain.GetCopiedTerrainData();
        var copiedGroup = terrain.GetCopiedTerrainGroup();
        var anchor = terrain.GetCopyAnchor();

        if (copiedData.HasValue)
        {
            sceneManager.GlobalCopiedTerrain = copiedData.Value;
            sceneManager.GlobalCopiedFromCol = anchor.col;
            sceneManager.GlobalCopiedFromRow = anchor.row;
            sceneManager.GlobalCopiedFromSceneId = sceneManager.CurrentSceneId;
        }

        sceneManager.GlobalCopiedHexes.Clear();
        if (copiedGroup != null && copiedGroup.Count > 0)
        {
            foreach (var kv in copiedGroup)
                sceneManager.GlobalCopiedHexes[kv.Key] = kv.Value;
            sceneManager.GlobalCopiedRegionMinCol = anchor.col;
            sceneManager.GlobalCopiedRegionMinRow = anchor.row;
        }
    }

    private static void SyncGlobalCopiedTerrainToLocal(TerrainModifier terrain)
    {
        var sceneManager = RenderSceneManager.Instance;
        if (sceneManager.GlobalCopiedTerrain == null) return;

        terrain.SetCopiedTerrainData(sceneManager.GlobalCopiedTerrain.Value,
            sceneManager.GlobalCopiedFromCol, sceneManager.GlobalCopiedFromRow);

        if (sceneManager.GlobalCopiedHexes.Count > 0)
        {
            terrain.SetCopiedTerrainGroup(sceneManager.GlobalCopiedHexes,
                sceneManager.GlobalCopiedRegionMinCol, sceneManager.GlobalCopiedRegionMinRow);
        }
    }

    private static async Task<bool> ShowInputDialogAndExecuteCliDouble(ModeContext context,
        string title, string description, double defaultValue, string cliCommand)
    {
        if (context.DialogService == null)
        {
            context.CliCommandExecutor?.Execute($"{cliCommand} {defaultValue}");
            return true;
        }

        var result = await context.DialogService.ShowInputDialogAsync(title, description, defaultValue.ToString("F1"), 0.1, 10.0);
        if (result == null) return false;

        if (double.TryParse(result, out double value))
        {
            value = Math.Clamp(value, 0.1, 10.0);
            context.CliCommandExecutor?.Execute($"{cliCommand} {value:F2}");
            return true;
        }

        return false;
    }

    private static async Task<bool> ShowResizeMapDialog(ModeContext context, string direction)
    {
        string dirText = direction switch { "up" => "向上", "down" => "向下", "left" => "向左", "right" => "向右", _ => direction };

        if (context.DialogService == null)
        {
            context.CliCommandExecutor?.Execute($"resize_map {direction} 0");
            return true;
        }

        var result = await context.DialogService.ShowInputDialogAsync($"调整地图大小 - {dirText}", "请输入程度参数（正数扩展，负数收缩）：", "0", -1000, 1000);
        if (result == null) return false;

        if (int.TryParse(result, out int amount) && amount != 0)
        {
            bool useOcean = false;
            if (amount > 0)
            {
                if (context.DialogService != null)
                {
                    useOcean = await context.DialogService.ShowConfirmDialogAsync(
                        "扩展地图 - 填充类型",
                        "新增格子使用海洋填充吗？\n（确认=海洋，取消=平地）",
                        "海洋",
                        "平地");
                }
                else
                {
                    useOcean = true;
                }
            }
            context.CliCommandExecutor?.Execute($"resize_map {direction} {amount}{(useOcean ? " ocean" : "")}");
            return true;
        }

        return false;
    }

    private void StartOrUpdateSelectionMove(ModeContext context, int deltaCol, int deltaRow)
    {
        var selector = HexSelector.Instance;

        // 如果 HexSelector 不在移动状态但 TerrainPaintMode 认为在移动，同步状态
        if (_isMovingSelection && !selector.IsMovingSelection)
        {
            _isMovingSelection = false;
            _moveSelectionOffsetCol = 0;
            _moveSelectionOffsetRow = 0;
            _originalSelectedHexes.Clear();
        }

        if (!_isMovingSelection)
        {
            _isMovingSelection = true;
            _moveSelectionOffsetCol = 0;
            _moveSelectionOffsetRow = 0;
            _originalSelectedHexes.Clear();
            foreach (var hex in selector.SelectedHexes)
                _originalSelectedHexes.Add((hex.Col, hex.Row));
            selector.SetSelectionMoving(true, 0, 0, _originalSelectedHexes);
        }

        _moveSelectionOffsetCol += deltaCol;
        _moveSelectionOffsetRow += deltaRow;

        if (context.MapData != null)
        {
            var newSelectedHexes = new List<HexCoord>();
            foreach (var (c, r) in _originalSelectedHexes)
            {
                int newCol = c + _moveSelectionOffsetCol;
                int newRow = r + _moveSelectionOffsetRow;
                if (newCol >= 0 && newCol < context.MapData.MapWidth &&
                    newRow >= 0 && newRow < context.MapData.MapHeight)
                {
                    newSelectedHexes.Add(new HexCoord(newCol, newRow));
                }
            }

            selector.SetSelection(newSelectedHexes);
            selector.SetSelectionMoving(true, _moveSelectionOffsetCol, _moveSelectionOffsetRow, _originalSelectedHexes);
        }

        context.RaiseStatusMessage?.Invoke($"移动预览: 偏移({_moveSelectionOffsetCol}, {_moveSelectionOffsetRow})，按Enter确认，Esc取消");
    }

    private void ConfirmSelectionMove(ModeContext context, TerrainModifier terrain)
    {
        if (!_isMovingSelection) return;

        var selector = HexSelector.Instance;
        var result = terrain.ConfirmSelectionMove(_originalSelectedHexes, _moveSelectionOffsetCol, _moveSelectionOffsetRow);
        context.RecordMultiCellChange("确认选区移动", () => { });
        context.NotifyDataModified?.Invoke();
        context.RaiseStatusMessage?.Invoke(result.Message);

        _isMovingSelection = false;
        _moveSelectionOffsetCol = 0;
        _moveSelectionOffsetRow = 0;
        _originalSelectedHexes.Clear();
        selector.SetSelectionMoving(false, 0, 0, null);
    }

    private void CancelSelectionMove(ModeContext context)
    {
        if (!_isMovingSelection) return;

        var selector = HexSelector.Instance;
        var originalHexes = _originalSelectedHexes.Select(h => new HexCoord(h.col, h.row)).ToList();
        selector.SetSelection(originalHexes);

        _isMovingSelection = false;
        _moveSelectionOffsetCol = 0;
        _moveSelectionOffsetRow = 0;
        _originalSelectedHexes.Clear();
        selector.SetSelectionMoving(false, 0, 0, null);

        context.RaiseStatusMessage?.Invoke("已取消框选区域移动");
    }

    private static void RemoveOceanHexesFromSelection(ModeContext context)
    {
        if (context.MapData == null) return;

        var selector = HexSelector.Instance;
        var hexesToRemove = new List<HexCoord>();

        foreach (var hex in selector.SelectedHexes)
        {
            ref var terrain = ref context.MapData.GetTerrainRef(hex.Col, hex.Row);
            if (terrain.TileType1 == 1)
                hexesToRemove.Add(hex);
        }

        foreach (var hex in hexesToRemove)
            selector.RemoveFromSelection(hex);

        context.RaiseStatusMessage?.Invoke($"已剔除 {hexesToRemove.Count} 个海洋格子，剩余 {selector.SelectedHexes.Count} 个格子");
    }

    private async Task<bool> HandleIjklAction(string action, int col, int row, ModeContext context, TerrainModifier terrain)
    {
        var selector = HexSelector.Instance;
        bool hasMultiSelection = selector.SelectedHexes.Count > 1;

        if (hasMultiSelection)
        {
            // 多选时，I/J/K/L 移动选区
            string moveAction = action switch
            {
                "ijkl_action" => "move_selection_up",
                "jkl_action" => "move_selection_left",
                "kl_action" => "move_selection_down",
                "l_action" => "move_selection_right",
                _ => action
            };
            return await HandleKeyAction(moveAction, col, row, context);
        }
        else
        {
            // 单选或无选区时，I/J/K/L 调整地图大小
            string resizeAction = action switch
            {
                "ijkl_action" => "resize_map_up",
                "jkl_action" => "resize_map_left",
                "kl_action" => "resize_map_down",
                "l_action" => "resize_map_right",
                _ => action
            };
            return await HandleKeyAction(resizeAction, col, row, context);
        }
    }
}