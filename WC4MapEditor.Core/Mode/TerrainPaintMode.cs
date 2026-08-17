using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Models;

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
        "T - 连接建筑（平地连接11-15类型建筑）\n" +
        "G - 按比例缩放地图（0.1-10.0）\n" +
        "[, ] - 修改地形类型\n" +
        "Shift+[, ] - 修改变体\n" +
        "Z - 切换编辑层\n" +
        "B - 显示/隐藏网格\n" +
        "N - 显示/隐藏标签\n" +
        "F1 - 显示/隐藏第二层\n" +
        "Ctrl+F1 - 显示/隐藏帮助文本\n" +
        "I,J,K,L - 调整地图大小（无框选时）\n" +
        "框选操作：\n" +
        "  右键拖动 - 框选区域\n" +
        "  Shift+右键拖动 - 新增选区\n" +
        "  Ctrl+右键拖动 - 剔除选区\n" +
        "  I,J,K,L - 移动选区\n" +
        "  Enter - 确认移动\n" +
        "  O - 剔除海洋格子";

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var terrain = context.GetModifier<TerrainModifier>()!;
        bool modified = false;
        switch (action)
        {
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
                    var targetHexes = HexSelector.Instance.SelectedHexes.Count > 0
                        ? HexSelector.Instance.SelectedHexes.Select(h => (h.Col, h.Row)).ToList()
                        : null;
                    var result = terrain.CreateCoast(targetHexes);
                    context.RecordMultiCellChange("创建海岸线", () => { });
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(result.Message);
                }
                return true;
            case "process_ocean_layer2":
                {
                    var targetHexes = HexSelector.Instance.SelectedHexes.Count > 0
                        ? HexSelector.Instance.SelectedHexes.Select(h => (h.Col, h.Row)).ToList()
                        : null;
                    var result = terrain.ProcessOceanSecondLayer(targetHexes);
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
                context.RaiseStatusMessage?.Invoke("连接建筑功能待实现（需要建筑数据）");
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
}