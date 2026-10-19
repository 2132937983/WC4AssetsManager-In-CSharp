using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 地形绘制模式（对应 VB TerrainModifier 的按键处理）：只决定"哪个键调地形模块的哪个函数"，
/// 数据操作都在 <see cref="TerrainModifier"/> 里。
/// <para>
/// 约定：批量按键对多选生效（<see cref="ModeActionArgs.TargetCells"/>）；整图类操作（绿化 / 随机 / 海岸线 / 海洋第二层）
/// 有多格选区时只处理选区，否则处理全图（对齐 VB）。每个改数据的按键都记一条撤销，并把模块返回的消息显示到状态栏。
/// </para>
/// <para>
/// 区域移动的状态只放在 <see cref="HexSelector"/> 里（点选、切换模式都会清掉它）；确认前还会核对选区是否仍是移动预览，
/// 中途被框选 / 点选改掉就视为放弃，不会执行过期的移动。
/// </para>
/// </summary>
public sealed class TerrainPaintMode : ModeBase
{
    public override EditMode Mode => EditMode.TerrainPaint;
    protected override string LocKey => "Terrain";
    protected override string DefaultDisplayName => "地形绘制";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Terrain;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Terrain };
    public override bool RequiresSelection => true;
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.Building | ModeRenderLayers.HideBuildingNames;

    protected override void DefineActions(ModeActionTable t)
    {
        t.HelpLine("select", ModeGesture.LeftClick, "选择格子（Shift 加选 / Ctrl 减选）")
         .HelpLine("pan", ModeGesture.LeftDrag, "平移视图")
         .Command("TP_H", KeyCodes.H, "toggle_brush", "切换画笔", ToggleBrush)
         .Note("brush", "画笔开启时右键单击 / 拖动绘制地形，有框选时只画在框选范围内")
         .Command("TP_C", KeyCodes.C, "copy", "复制选中格子", Copy)
         .Edit("TP_V", KeyCodes.V, "paste", "粘贴到选中格子", Paste)
         .Edit("TP_Y", KeyCodes.Y, "set_river", "在该格绘制河流", SetRiverAsync)
         .Edit("TP_P", KeyCodes.P, "recognize_terrain", "识别地形", RecognizeAsync)
         .Edit("TP_U", KeyCodes.U, "greening", "绿化平地", GreeningAsync)
         .Edit("TP_R", KeyCodes.R, "randomize_flat", "随机平地变体", RandomizeFlatAsync)
         .Edit("TP_ShiftR", KeyChord.Shift(KeyCodes.R), "randomize_variant", "随机当前层变体", RandomizeVariantAsync)
         .Edit("TP_F", KeyCodes.F, "flood_fill", "洪水填充", FloodFill)
         .Edit("TP_F4", KeyCodes.F4, "create_coast", "创建海岸线", CreateCoast)
         .Edit("TP_F5", KeyCodes.F5, "process_ocean_layer2", "处理海洋第二层", ProcessOceanLayer2)
         .Command("TP_F6", KeyCodes.F6, "export_hd", "导出HD文件", ExportHdAsync)
         .Command("TP_F7", KeyCodes.F7, "geo_calculate", "经纬度换算并导出", a => RunGuiCallbackAsync(a, a.Context.GeoCalculateCallback))
         .Command("TP_F8", KeyCodes.F8, "geo_export_ref", "导出参考点配置", a => RunGuiCallbackAsync(a, a.Context.GeoExportRefCallback))
         .Command("TP_F9", KeyCodes.F9, "geo_import_ref", "导入参考点配置", a => RunGuiCallbackAsync(a, a.Context.GeoImportRefCallback))
         .Command("TP_F10", KeyCodes.F10, "geo_export_grid", "导出格子数据（不含经纬度）", a => RunGuiCallbackAsync(a, a.Context.GeoExportGridCallback))
         .Command("TP_Q", KeyCodes.Q, "add_geo_ref", "添加经纬度参考点", a => RunGuiCallbackAsync(a, a.Context.AddGeoRefCallback))
         .Edit("TP_T", KeyCodes.T, "connect_buildings", "连接建筑（平地连接11-15类型建筑）", ConnectBuildings)
         .Edit("TP_G", KeyCodes.G, "scale_map", "按比例缩放地图（0.1-10.0）", ScaleMapAsync)
         .Command("TP_BracketOpen", KeyCodes.OemOpenBrackets, "decrease_brush_size", "减小画笔大小", a => ChangeBrushSize(a, -1))
         .Command("TP_BracketClose", KeyCodes.OemCloseBrackets, "increase_brush_size", "增大画笔大小", a => ChangeBrushSize(a, 1))
         .Edit("TP_Comma", KeyCodes.OemComma, "decrease_type", "上一个地形类型", a => ChangeType(a, -1))
         .Edit("TP_Period", KeyCodes.OemPeriod, "increase_type", "下一个地形类型", a => ChangeType(a, 1))
         .Edit("TP_ShiftComma", KeyChord.Shift(KeyCodes.OemComma), "decrease_decoration", "上一个变体", a => ChangeVariant(a, -1))
         .Edit("TP_ShiftPeriod", KeyChord.Shift(KeyCodes.OemPeriod), "increase_decoration", "下一个变体", a => ChangeVariant(a, 1))
         .Command("TP_Z", KeyCodes.Z, "toggle_layer", "切换编辑层", ToggleLayer)
         .Edit("TP_Delete", KeyCodes.Delete, "remove", "重置编辑层地形", Remove)
         .Command("TP_I", KeyCodes.I, "ijkl_action", "向上调整地图大小（有框选时上移选区）", a => ResizeOrMoveAsync(a, MapResizeDirection.Up))
         .Command("TP_J", KeyCodes.J, "jkl_action", "向左调整地图大小（有框选时左移选区）", a => ResizeOrMoveAsync(a, MapResizeDirection.Left))
         .Command("TP_K", KeyCodes.K, "kl_action", "向下调整地图大小（有框选时下移选区）", a => ResizeOrMoveAsync(a, MapResizeDirection.Down))
         .Command("TP_L", KeyCodes.L, "l_action", "向右调整地图大小（有框选时右移选区）", a => ResizeOrMoveAsync(a, MapResizeDirection.Right))
         .HelpLine("region_select", ModeGesture.RightDrag, "框选区域（按住 Shift 新增选区 / Ctrl 剔除选区）")
         .Edit("TP_Enter", KeyCodes.Enter, "confirm_selection_move", "确认选区移动", ConfirmMove)
         .GestureCommand(ModeGesture.Escape, "cancel_selection_move", "取消选区移动", CancelMove)
         .Command("TP_O", KeyCodes.O, "remove_ocean_from_selection", "剔除海洋格子", RemoveOcean)
         // 不绑键、不进帮助：I/J/K/L 按有无框选分派到这里，脚本 / AI 也能按名字直接调用
         .HiddenEdit("resize_map_up", "向上调整地图大小", a => ResizeAsync(a, MapResizeDirection.Up))
         .HiddenEdit("resize_map_down", "向下调整地图大小", a => ResizeAsync(a, MapResizeDirection.Down))
         .HiddenEdit("resize_map_left", "向左调整地图大小", a => ResizeAsync(a, MapResizeDirection.Left))
         .HiddenEdit("resize_map_right", "向右调整地图大小", a => ResizeAsync(a, MapResizeDirection.Right))
         .HiddenCommand("move_selection_up", "上移选区", a => MoveSelection(a, MapResizeDirection.Up))
         .HiddenCommand("move_selection_down", "下移选区", a => MoveSelection(a, MapResizeDirection.Down))
         .HiddenCommand("move_selection_left", "左移选区", a => MoveSelection(a, MapResizeDirection.Left))
         .HiddenCommand("move_selection_right", "右移选区", a => MoveSelection(a, MapResizeDirection.Right));
    }

    #region 公共辅助

    /// <summary>在一条地形撤销记录里执行模块操作，并把结果消息显示到状态栏。返回是否真的改了格子。</summary>
    private static bool RecordTerrain(ModeActionArgs a, string description, Func<ModifierResult> operation)
    {
        ModifierResult result = default;
        a.Context.RecordMultiCellChange(description, () => result = operation());
        ShowResult(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    private static void ShowResult(ModeActionArgs a, ModifierResult result)
    {
        if (!string.IsNullOrEmpty(result.Message)) a.Status(result.Message);
    }

    /// <summary>整图类操作的范围：多格选区时只处理选区，否则 null（全图）。</summary>
    private static IReadOnlyList<HexCoord>? RegionOrWholeMap(ModeActionArgs a)
        => a.Context.Selection.Count > 1 ? a.TargetCells() : null;

    /// <summary>
    /// 输入 0-100 的百分比。没有界面（脚本 / 测试）时直接用默认值；取消或输入无效返回 null。
    /// </summary>
    private static async Task<int?> AskPercentAsync(ModeActionArgs a, string title, string prompt, int defaultValue)
    {
        if (a.Dialogs == null) return defaultValue;

        string? text = await a.Dialogs.ShowInputDialogAsync(title, prompt, defaultValue.ToString(CultureInfo.InvariantCulture), 0, 100);
        if (text == null) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            a.Status(Loc.F("Mode.Terrain.Msg.InvalidNumber", text));
            return null;
        }
        return Math.Clamp(value, 0, 100);
    }

    private static async Task<bool> RunGuiCallbackAsync(ModeActionArgs a, Func<Task>? callback)
    {
        if (callback == null)
        {
            a.Status(Loc.T("Mode.Terrain.Msg.NeedsGui"));
            return false;
        }
        await callback();
        return true;
    }

    #endregion

    #region 画笔 / 编辑层

    private static bool ToggleBrush(ModeActionArgs a)
    {
        a.Context.NotifyBrushToggled?.Invoke();
        return true;
    }

    private static bool ChangeBrushSize(ModeActionArgs a, int delta)
    {
        var terrain = a.Module<TerrainModifier>();
        terrain.BrushSize += delta;
        a.Status(Loc.F("Mode.Terrain.Msg.BrushSize", terrain.BrushSize));
        a.Context.NotifyBrushSizeChanged?.Invoke();
        return true;
    }

    private static bool ToggleLayer(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        terrain.EditLayer = terrain.EditLayer % 3 + 1;
        a.Status(Loc.F("Mode.Terrain.Msg.EditLayer", terrain.EditLayer));
        return true;
    }

    #endregion

    #region 单格 / 多选编辑

    private static bool ChangeType(ModeActionArgs a, int delta)
    {
        var terrain = a.Module<TerrainModifier>();
        var cells = a.TargetCells();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Type"), () => terrain.ChangeTerrainType(cells, delta));
    }

    private static bool ChangeVariant(ModeActionArgs a, int delta)
    {
        var terrain = a.Module<TerrainModifier>();
        var cells = a.TargetCells();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Variant"), () => terrain.ChangeDecoration(cells, delta));
    }

    private static bool Remove(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        var cells = a.TargetCells();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Remove"), () => terrain.RemoveRange(cells));
    }

    private static bool Copy(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        var result = a.Context.Selection.Count > 1
            ? terrain.CopyTerrainGroup(a.TargetCells())
            : terrain.CopyTerrain(a.Col, a.Row);
        if (result.Success) SyncCopiedTerrainToGlobal(terrain);
        ShowResult(a, result);
        return result.Success;
    }

    /// <summary>粘贴：复制的是区域时以焦点格为左上角整体粘贴；复制的是单格时粘到选区的每一格。</summary>
    private static bool Paste(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        SyncGlobalCopiedTerrainToLocal(terrain);
        var cells = a.TargetCells();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Paste"), () => terrain.PasteTerrain(a.Col, a.Row, cells));
    }

    private static async Task<bool> SetRiverAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null) return false;

        // 没有界面时沿用原有行为：直接把上边设为河流
        byte riverValue = 1;
        if (a.Dialogs != null)
        {
            var (confirmed, value) = await a.Dialogs.ShowRiverSettingDialogAsync(map.GetTerrain(a.Col, a.Row).RiverValue);
            if (!confirmed)
            {
                a.Status(Loc.T("Mode.Terrain.Msg.RiverCancelled"));
                return false;
            }
            riverValue = value;
        }

        var terrain = a.Module<TerrainModifier>();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.River"), () => terrain.SetRiverValue(a.Col, a.Row, riverValue));
    }

    private static bool FloodFill(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.FloodFill"), () => terrain.FloodFill(a.Col, a.Row));
    }

    #endregion

    #region 整图 / 选区批量

    private static async Task<bool> GreeningAsync(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        int? value = await AskPercentAsync(a, Loc.T("Mode.Terrain.Msg.GreeningTitle"), Loc.T("Mode.Terrain.Msg.GreeningPrompt"), terrain.GreeningValue);
        if (value is not int greening) return false;

        terrain.GreeningValue = greening;
        var region = RegionOrWholeMap(a);
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Greening"), () => terrain.ApplyGreening(greening, region));
    }

    private static async Task<bool> RandomizeFlatAsync(ModeActionArgs a)
    {
        int? value = await AskPercentAsync(a, Loc.T("Mode.Terrain.Msg.RandomFlatTitle"), Loc.T("Mode.Terrain.Msg.RandomFlatPrompt"), 50);
        if (value is not int probability) return false;

        var terrain = a.Module<TerrainModifier>();
        var region = RegionOrWholeMap(a);
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.RandomFlat"), () => terrain.RandomizeFlatTerrain(probability, region));
    }

    private static async Task<bool> RandomizeVariantAsync(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        int? value = await AskPercentAsync(a, Loc.T("Mode.Terrain.Msg.RandomVariantTitle"),
            Loc.F("Mode.Terrain.Msg.RandomVariantPrompt", terrain.EditLayer), 50);
        if (value is not int probability) return false;

        var region = RegionOrWholeMap(a);
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.RandomVariant"), () => terrain.RandomizeVariant(probability, region));
    }

    private static bool CreateCoast(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        var region = RegionOrWholeMap(a);
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Coast"), () => terrain.CreateCoast(region));
    }

    private static bool ProcessOceanLayer2(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        var region = RegionOrWholeMap(a);
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.OceanLayer2"), () => terrain.ProcessOceanSecondLayer(region));
    }

    private static bool ConnectBuildings(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        return RecordTerrain(a, Loc.T("Module.Terrain.Undo.Connect"), terrain.ConnectBuildings);
    }

    /// <summary>识别地形由 GUI 完成（进度窗口、撤销记录与结果消息都在 GUI 回调里），这里只负责分派。</summary>
    private static async Task<bool> RecognizeAsync(ModeActionArgs a)
    {
        var callback = a.Context.RecognizeTerrainCallback;
        if (callback == null)
        {
            a.Status(Loc.T("Mode.Terrain.Msg.RecognizeUnavailable"));
            return false;
        }

        var (success, modifiedCount) = await callback();
        return success && modifiedCount > 0;
    }

    private static async Task<bool> ExportHdAsync(ModeActionArgs a)
    {
        var result = a.Module<TerrainModifier>().ExportHdFile();
        ShowResult(a, result);
        if (result.Success && a.Dialogs != null)
            await a.Dialogs.ShowNotificationDialogAsync(Loc.T("Mode.Terrain.Msg.HdExportedTitle"), result.Message ?? "", Loc.T("Common.Ok"));
        return result.Success;
    }

    #endregion

    #region 地图尺寸

    private static async Task<bool> ScaleMapAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null) return false;

        var terrain = a.Module<TerrainModifier>();
        if (terrain.IsWorldBasedMap)
        {
            a.Status(Loc.T("Module.Terrain.WorldMapReadOnly"));
            return false;
        }

        double scale = 1.0;
        if (a.Dialogs != null)
        {
            string? text = await a.Dialogs.ShowInputDialogAsync(Loc.T("Mode.Terrain.Msg.ScaleTitle"), Loc.T("Mode.Terrain.Msg.ScalePrompt"),
                scale.ToString("F1", CultureInfo.CurrentCulture), 0.1, 10.0);
            if (text == null) return false;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out scale)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
            {
                a.Status(Loc.F("Mode.Terrain.Msg.InvalidNumber", text));
                return false;
            }
        }

        var result = TerrainModifier.RecordResize(map, a.Context.UndoManager is { } undo ? undo.Record : null,
            Loc.F("Module.Terrain.Undo.Scale", scale), () => terrain.ScaleMap(scale));
        ShowResult(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>
    /// I/J/K/L：有多格选区（或正在移动选区）时移动选区预览，否则在该方向扩缩地图。
    /// 扩缩会改数据，这里自己通知重绘（按键是命令动作，移动预览不改数据）。
    /// </summary>
    private static async Task<bool> ResizeOrMoveAsync(ModeActionArgs a, MapResizeDirection direction)
    {
        var selector = HexSelector.Instance;
        if (selector.SelectedHexes.Count > 1 || selector.IsMovingSelection)
            return MoveSelection(a, direction);

        bool changed = await ResizeAsync(a, direction);
        if (changed) a.Context.NotifyDataModified?.Invoke();
        return changed;
    }

    private static async Task<bool> ResizeAsync(ModeActionArgs a, MapResizeDirection direction)
    {
        var map = a.Map;
        if (map == null) return false;

        var terrain = a.Module<TerrainModifier>();
        if (terrain.IsWorldBasedMap)
        {
            a.Status(Loc.T("Module.Terrain.WorldMapReadOnly"));
            return false;
        }

        // 扩缩量只能由用户输入，没有界面时不执行
        if (a.Dialogs == null)
        {
            a.Status(Loc.T("Mode.Terrain.Msg.ResizeNeedsInput"));
            return false;
        }

        string directionName = TerrainModifier.DirectionName(direction);
        string? text = await a.Dialogs.ShowInputDialogAsync(Loc.F("Mode.Terrain.Msg.ResizeTitle", directionName),
            Loc.T("Mode.Terrain.Msg.ResizePrompt"), "0", -1000, 1000);
        if (text == null) return false;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
        {
            a.Status(Loc.F("Mode.Terrain.Msg.InvalidNumber", text));
            return false;
        }
        if (amount == 0) return false;

        bool useOcean = amount > 0 && await a.Dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Terrain.Msg.ResizeFillTitle"), Loc.T("Mode.Terrain.Msg.ResizeFillPrompt"),
            Loc.T("Mode.Terrain.Msg.ResizeFillOcean"), Loc.T("Mode.Terrain.Msg.ResizeFillFlat"));

        var result = TerrainModifier.RecordResize(map, a.Context.UndoManager is { } undo ? undo.Record : null,
            Loc.F("Module.Terrain.Undo.Resize", directionName, amount), () => terrain.ResizeMap(direction, amount, useOcean));
        ShowResult(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    #endregion

    #region 区域移动

    /// <summary>
    /// 当前仍有效的区域移动：选择器处于移动状态，且选区仍是按偏移平移后的预览。
    /// 移动途中选区被框选 / 点选改掉就算放弃（以前只看模式里的标志，会在之后的 Enter 上执行过期的移动）。
    /// </summary>
    private static bool TryGetActiveMove(TerrainModifier terrain, out HashSet<(int col, int row)> originals, out int offsetCol, out int offsetRow)
    {
        var selector = HexSelector.Instance;
        originals = selector.OriginalSelectedHexes is { } original ? new HashSet<(int col, int row)>(original) : new();
        offsetCol = selector.MoveOffsetCol;
        offsetRow = selector.MoveOffsetRow;
        if (!selector.IsMovingSelection || originals.Count == 0) return false;
        return selector.SelectedHexes.SetEquals(terrain.TranslateRegion(originals, offsetCol, offsetRow));
    }

    private static bool MoveSelection(ModeActionArgs a, MapResizeDirection direction)
    {
        var terrain = a.Module<TerrainModifier>();
        var selector = HexSelector.Instance;
        if (!TryGetActiveMove(terrain, out var originals, out int offsetCol, out int offsetRow))
        {
            originals = selector.SelectedHexes.Select(h => (h.Col, h.Row)).ToHashSet();
            offsetCol = offsetRow = 0;
            if (originals.Count == 0) return false;
        }

        switch (direction)
        {
            case MapResizeDirection.Up: offsetRow--; break;
            case MapResizeDirection.Down: offsetRow++; break;
            case MapResizeDirection.Left: offsetCol--; break;
            case MapResizeDirection.Right: offsetCol++; break;
        }

        selector.SetSelection(terrain.TranslateRegion(originals, offsetCol, offsetRow));
        selector.SetSelectionMoving(true, offsetCol, offsetRow, originals);
        a.Status(Loc.F("Mode.Terrain.Msg.MovePreview", offsetCol, offsetRow));
        return true;
    }

    private static bool ConfirmMove(ModeActionArgs a)
    {
        var terrain = a.Module<TerrainModifier>();
        var selector = HexSelector.Instance;
        if (!TryGetActiveMove(terrain, out var originals, out int offsetCol, out int offsetRow))
        {
            if (selector.IsMovingSelection)
            {
                selector.SetSelectionMoving(false, 0, 0, null);
                a.Status(Loc.T("Mode.Terrain.Msg.MoveAbandoned"));
            }
            return false;
        }

        bool changed = RecordTerrain(a, Loc.T("Module.Terrain.Undo.Move"), () => terrain.MoveRegion(originals, offsetCol, offsetRow));
        selector.SetSelectionMoving(false, 0, 0, null);
        return changed;
    }

    /// <summary>Esc：取消正在进行的区域移动、恢复原选区。没有移动时返回 false，让场景弹出返回主界面的确认。</summary>
    private static bool CancelMove(ModeActionArgs a)
    {
        var selector = HexSelector.Instance;
        if (!selector.IsMovingSelection) return false;

        bool active = TryGetActiveMove(a.Module<TerrainModifier>(), out var originals, out _, out _);
        selector.SetSelectionMoving(false, 0, 0, null);
        if (!active) return false;

        selector.SetSelection(originals.Select(h => new HexCoord(h.col, h.row)));
        a.Status(Loc.T("Mode.Terrain.Msg.MoveCancelled"));
        return true;
    }

    /// <summary>
    /// O：从选区剔除海洋格子。移动途中按原位置的地形判断、从待移动的原始格子里剔除并重建预览
    /// （VB 只剔除预览，确认时用的原始格子不变，等于没剔除）。
    /// </summary>
    private static bool RemoveOcean(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null) return false;

        var terrain = a.Module<TerrainModifier>();
        var selector = HexSelector.Instance;
        bool IsOcean(int col, int row) => map.GetTerrain(col, row).TileType1 == 1;

        if (TryGetActiveMove(terrain, out var originals, out int offsetCol, out int offsetRow))
        {
            int removedFromMove = originals.RemoveWhere(h => IsOcean(h.col, h.row));
            selector.SetSelection(terrain.TranslateRegion(originals, offsetCol, offsetRow));
            if (originals.Count > 0)
                selector.SetSelectionMoving(true, offsetCol, offsetRow, originals);
            else
                selector.SetSelectionMoving(false, 0, 0, null);
            a.Status(Loc.F("Mode.Terrain.Msg.OceanRemoved", removedFromMove, originals.Count));
            return true;
        }

        var oceanCells = selector.SelectedHexes.Where(h => IsOcean(h.Col, h.Row)).ToList();
        foreach (var cell in oceanCells)
            selector.RemoveFromSelection(cell);
        a.Status(Loc.F("Mode.Terrain.Msg.OceanRemoved", oceanCells.Count, selector.SelectedHexes.Count));
        return true;
    }

    #endregion

    #region 跨场景剪贴板

    private static void SyncCopiedTerrainToGlobal(TerrainModifier terrain)
    {
        var clipboard = MapClipboard.Instance;
        var copiedData = terrain.GetCopiedTerrainData();
        var copiedGroup = terrain.GetCopiedTerrainGroup();
        var anchor = terrain.GetCopyAnchor();

        if (copiedData.HasValue)
        {
            clipboard.GlobalCopiedTerrain = copiedData.Value;
            clipboard.GlobalCopiedFromCol = anchor.col;
            clipboard.GlobalCopiedFromRow = anchor.row;
        }

        clipboard.GlobalCopiedHexes.Clear();
        if (copiedGroup is { Count: > 0 })
        {
            foreach (var kv in copiedGroup)
                clipboard.GlobalCopiedHexes[kv.Key] = kv.Value;
            clipboard.GlobalCopiedRegionMinCol = anchor.col;
            clipboard.GlobalCopiedRegionMinRow = anchor.row;
        }
    }

    private static void SyncGlobalCopiedTerrainToLocal(TerrainModifier terrain)
    {
        var clipboard = MapClipboard.Instance;
        if (clipboard.GlobalCopiedTerrain == null) return;

        terrain.SetCopiedTerrainData(clipboard.GlobalCopiedTerrain.Value,
            clipboard.GlobalCopiedFromCol, clipboard.GlobalCopiedFromRow);

        if (clipboard.GlobalCopiedHexes.Count > 0)
        {
            terrain.SetCopiedTerrainGroup(clipboard.GlobalCopiedHexes,
                clipboard.GlobalCopiedRegionMinCol, clipboard.GlobalCopiedRegionMinRow);
        }
    }

    #endregion
}
