using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 单位部署模式：单位（v1 / v3 按地图版本自动分派）与陷阱。
/// <para>
/// 单位的数据操作都在 <see cref="IArmyModule"/>（<see cref="ArmyModifier"/> / <see cref="ArmyV3Modifier"/>）里，
/// 陷阱在 <see cref="TrapModifier"/>，占领事件联动用 <see cref="EventModifier"/>；本模式只决定按键 → 动作 → 模块函数，
/// 并负责撤销记录：单格 / 选区操作用 <see cref="ArmyHexState"/> 前后快照，整图批量操作用实体快照。
/// </para>
/// </summary>
public sealed class ArmyDeployMode : ModeBase
{
    /// <summary>Ctrl+F 上一次查找的等级与命中下标：再次查找同一等级时跳到下一个。</summary>
    private int _findLevel = 1;
    private int _findIndex = -1;

    public override EditMode Mode => EditMode.ArmyDeploy;
    protected override string LocKey => "Army";
    protected override string DefaultDisplayName => "单位部署";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Army;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Army, ModifierKind.ArmyV3, ModifierKind.Trap, ModifierKind.Event };
    public override bool RequiresSelection => true;

    // VB 单位模式：国家领域、建筑、单位、陷阱、归属国旗，以及数据范围外格子的红叉（基类 Draw → DrawClippedTiles）
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.Building | ModeRenderLayers.Army | ModeRenderLayers.Trap
                                                   | ModeRenderLayers.LegionDomain | ModeRenderLayers.BelongFlag | ModeRenderLayers.ClippedTiles;

    /// <summary>框选只命中有单位（v1 或 v3）的格子。</summary>
    public override Func<HexCoord, bool>? CreateSelectionFilter(MapData map)
    {
        var cells = new HashSet<int>();
        foreach (var a in map.Armies) cells.Add((ushort)a.Coordinate);
        foreach (var a in map.ArmiesV3) cells.Add((ushort)a.Coordinate);
        return c => cells.Contains(c.ToIndex(map.MapWidth));
    }

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureCommand(ModeGesture.LeftClick, "select", "选择单位（Shift 加选 / Ctrl 减选），状态栏显示单位与陷阱信息", ShowHexInfo)
         .HelpLine("box_select", ModeGesture.RightDrag, "框选单位（Shift 并入 / Ctrl 剔除）")
         .HelpLine("right_click", ModeGesture.RightClick, "选择单位")
         .GestureCommand(ModeGesture.Escape, "cancel", "取消多选", CancelMultiSelection)
         .Command("AD_C", KeyCodes.C, "copy", "复制单位（多选选中时批量复制）", CopyUnits)
         .Edit("AD_V", KeyCodes.V, "paste", "粘贴单位（有批量复制时按原布局粘贴，以当前格为左上角）", PasteUnits)
         .Edit("AD_X", KeyCodes.X, "remove", "删除单位（多选选中时批量删除）", RemoveUnits)
         .Alias("AD_Delete", KeyCodes.Delete, "remove")
         .Edit("AD_Q", KeyCodes.Q, "open_create", "创建/修改单位", CreateOrEditUnitAsync)
         .Edit("AD_L", KeyCodes.L, "set_legion", "按归属设置军团", SetLegionFromBelong)
         .Edit("AD_CQ", KeyChord.Ctrl(KeyCodes.Q), "set_plan_or_event", "多选时设置方案/单选时设置占领事件", SetPlanOrEventAsync)
         .Edit("AD_E", KeyCodes.E, "legion_strength", "按配置修改军团强度(可指定归属与强度等级)", ModifyStrengthAsync)
         .Edit("AD_R", KeyCodes.R, "randomize_legion", "随机化军团单位(可指定归属)", RandomizeUnitsAsync)
         .Edit("AD_I", KeyCodes.I, "generate_by_probability", "按概率生成单位(可指定归属，-1为所有军团)", GenerateUnitsAsync)
         .Edit("AD_G", KeyCodes.G, "auto_general", "自动分配将领(可指定归属与将领数量)", AssignGeneralsAsync)
         .Edit("AD_T", KeyCodes.T, "cycle_plan", "循环切换单位方案(0-4)", CyclePlan)
         .Edit("AD_Y", KeyCodes.Y, "batch_plan_by_legion", "批量修改单位方案(可指定归属与方案值)", BatchPlanAsync)
         .Edit("AD_SDEL", KeyChord.Shift(KeyCodes.Delete), "remove_by_belong", "删除指定归属的所有单位(可输入归属ID)", RemoveByBelongAsync)
         .Edit("AD_CSDEL", KeyChord.CtrlShift(KeyCodes.Delete), "remove_all", "删除所有单位（需确认）", RemoveAllUnitsAsync)
         .Command("AD_CC", KeyChord.Ctrl(KeyCodes.C), "trap_copy", "复制陷阱（格子上没有陷阱时复制单位）", CopyTrapOrUnit)
         .Edit("AD_CV", KeyChord.Ctrl(KeyCodes.V), "trap_paste", "粘贴陷阱（没有复制的陷阱时粘贴单位）", PasteTrapOrUnit)
         .Edit("AD_CX", KeyChord.Ctrl(KeyCodes.X), "trap_remove", "删除陷阱（格子上没有陷阱时删除单位）", RemoveTrapOrUnit)
         .Edit("AD_F", KeyCodes.F, "trap_place", "创建/修改陷阱", PlaceOrEditTrapAsync)
         .Edit("AD_SF", KeyChord.Shift(KeyCodes.F), "trap_cycle_level", "循环切换陷阱等级(1-4)", CycleTrapLevel)
         .Edit("AD_SR", KeyChord.Shift(KeyCodes.R), "trap_randomize", "随机化陷阱属性（等级、所属军团、血量）", RandomizeTrap)
         .Edit("AD_CG", KeyChord.Ctrl(KeyCodes.G), "trap_generate", "按归属与概率批量生成陷阱（跳过建筑格，可选是否包含海洋）", GenerateTrapsAsync)
         .Edit("AD_CI", KeyChord.Ctrl(KeyCodes.I), "trap_generate_by_province", "每个省份按概率生成一个陷阱", GenerateTrapsByProvinceAsync)
         .Edit("AD_CR", KeyChord.Ctrl(KeyCodes.R), "trap_random_levels", "随机化陷阱等级(可指定军团值)", RandomizeTrapLevelsAsync)
         .Command("AD_CF", KeyChord.Ctrl(KeyCodes.F), "trap_find", "按等级查找陷阱（重复查找同一等级时跳到下一个）", FindTrapAsync)
         .Edit("AD_CDEL", KeyChord.Ctrl(KeyCodes.Delete), "trap_remove_all", "删除所有陷阱（需确认）", RemoveAllTrapsAsync)
         .Command("AD_H", KeyCodes.H, "toggle_brush", "切换单位橡皮擦画笔（右键拖动批量删除单位）", ToggleEraser)
         .Command("AD_BrushDec", KeyCodes.OemOpenBrackets, "brush_decrease", "减小橡皮擦半径", a => ResizeEraser(a, -1))
         .Command("AD_BrushInc", KeyCodes.OemCloseBrackets, "brush_increase", "增大橡皮擦半径", a => ResizeEraser(a, 1));
    }

    #region 选择 / 信息

    /// <summary>左键选中格子后在状态栏显示该格的单位与陷阱（VB SelectArmyAt）。</summary>
    private static bool ShowHexInfo(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var parts = new[] { Units(a).DescribeUnitAt(a.Col, a.Row), a.Module<TrapModifier>().DescribeTrapAt(a.Col, a.Row) }
            .Where(s => s != null);
        string text = string.Join(Loc.T("Mode.Army.Msg.InfoSeparator", "；"), parts);
        a.Status(text.Length > 0 ? text : Loc.F("Mode.Army.Msg.EmptyHex", a.Col, a.Row));
        return true;
    }

    /// <summary>Esc：有多选时先清空多选（VB 单位模式同样先取消框选）；否则交给场景弹出返回确认。</summary>
    private static bool CancelMultiSelection(ModeActionArgs a)
    {
        var selector = HexSelector.Instance;
        if (selector.SelectedCount <= 1) return false;
        selector.ClearSelection();
        a.Status(Loc.T("Mode.Army.Msg.SelectionCleared", "已取消多选"));
        return true;
    }

    #endregion

    #region 单位：复制 / 粘贴 / 删除 / 军团 / 方案

    private static bool CopyUnits(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var units = Units(a);
        var cells = a.TargetCells();
        if (cells.Count > 1)
        {
            int count = units.CopyArmyGroup(cells);
            if (count > 0)
            {
                a.Status(Loc.F("Mode.Army.Msg.GroupCopied", count));
                return true;
            }
        }
        return Report(a, units.CopyArmy(a.Col, a.Row));
    }

    /// <summary>粘贴：有多选复制时按原布局粘贴，否则粘贴单格复制；目标格归属与 LegionId 取省会归属（省会无归属时不改）。</summary>
    private static bool PasteUnits(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var units = Units(a);
        if (units.HasCopiedArmyGroup)
            return ChangeMap(a, Loc.F("Mode.Army.Undo.PasteGroup", a.Col, a.Row), () => units.PasteArmyGroup(a.Col, a.Row, autoBelong: true));
        return ChangeCells(a, Loc.F("Mode.Army.Undo.Paste", a.Col, a.Row), [a.Cell], () => units.PasteArmy(a.Col, a.Row, autoBelong: true));
    }

    /// <summary>删除目标格（多选时为整个选区）上的单位。不再顺带把格子归属清成 0xFF（VB 从不这样做）。</summary>
    private static bool RemoveUnits(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var cells = a.TargetCells();
        return ChangeCells(a, Loc.F("Mode.Army.Undo.Remove", a.Col, a.Row), cells, () => Units(a).RemoveUnits(cells));
    }

    private static bool SetLegionFromBelong(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var cells = a.TargetCells();
        return ChangeCells(a, Loc.F("Mode.Army.Undo.SetLegion", a.Col, a.Row), cells, () => Units(a).SetLegionIdFromBelong(cells));
    }

    private static bool CyclePlan(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var cells = a.TargetCells();
        return ChangeCells(a, Loc.F("Mode.Army.Undo.CyclePlan", a.Col, a.Row), cells, () => Units(a).CyclePlan(cells));
    }

    #endregion

    #region 单位：Q 创建 / 修改

    /// <summary>
    /// Q：格子上有单位时用设置窗口编辑它（取消不改任何东西，撤销恢复原单位），没有时以默认值新建
    /// （新单位的 LegionId 与格子归属取省会归属）。窗口里改了坐标且目标格为空时，单位移动过去。
    /// </summary>
    private static async Task<bool> CreateOrEditUnitAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        return map.Header.UsesV3Records
            ? await EditUnitAsync(a, a.Module<ArmyV3Modifier>(), dialogs.ShowArmySettingV3DialogAsync)
            : await EditUnitAsync(a, a.Module<ArmyModifier>(), dialogs.ShowArmySettingDialogAsync);
    }

    private static async Task<bool> EditUnitAsync<TUnit>(ModeActionArgs a, ArmyModifierBase<TUnit> module,
        Func<TUnit, bool, Task<(bool confirmed, TUnit unit)>> showDialog) where TUnit : struct
    {
        var existing = module.GetUnitAt(a.Col, a.Row);
        bool isNew = existing == null;
        var (confirmed, edited) = await showDialog(existing ?? module.CreateUnitTemplate(a.Col, a.Row), isNew);
        if (!confirmed) return false;

        var cell = a.Cell;
        var dest = module.CoordOf(edited);
        string? moveNote = null;
        if (dest != cell && (!InMap(a.Map!, dest) || module.HasUnitAt(dest.Col, dest.Row)))
        {
            if (InMap(a.Map!, dest)) moveNote = Loc.F("Mode.Army.Msg.MoveBlocked", dest.Col, dest.Row);
            dest = cell;
        }

        string message = isNew ? Loc.F("Mode.Army.Msg.UnitCreated", dest.Col, dest.Row)
                       : dest != cell ? Loc.F("Mode.Army.Msg.UnitMoved", dest.Col, dest.Row)
                       : Loc.F("Mode.Army.Msg.UnitUpdated", dest.Col, dest.Row);
        if (moveNote != null) message += Loc.T("Mode.Army.Msg.InfoSeparator", "；") + moveNote;

        string undo = isNew ? Loc.F("Mode.Army.Undo.CreateUnit", dest.Col, dest.Row) : Loc.F("Mode.Army.Undo.EditUnit", a.Col, a.Row);
        HexCoord[] cells = dest == cell ? [cell] : [cell, dest];
        return ChangeCells(a, undo, cells, () =>
        {
            if (dest != cell) module.Remove(cell.Col, cell.Row);
            return module.PlaceUnit(dest.Col, dest.Row, edited, autoBelong: isNew);
        }, message);
    }

    #endregion

    #region 单位：Ctrl+Q 方案 / 占领事件

    /// <summary>
    /// Ctrl+Q：多选时为选中单位输入方案值；单选 v1 单位时输入占领触发事件序号，
    /// 并把该序号对应事件的触发条件改为"部队触发"（VB SetArmyOccupationEventById）。
    /// v3 单位没有占领事件字段，单选时也走方案输入。
    /// </summary>
    private static async Task<bool> SetPlanOrEventAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs == null) return NoDialog(a);

        var cells = a.TargetCells();
        return cells.Count == 1 && !map.Header.UsesV3Records
            ? await SetOccupationEventAsync(a)
            : await SetPlanAsync(a, cells);
    }

    private static async Task<bool> SetPlanAsync(ModeActionArgs a, IReadOnlyList<HexCoord> cells)
    {
        var units = Units(a);
        var first = cells.Select(c => units.GetFieldsAt(c.Col, c.Row)).FirstOrDefault(f => f.HasValue);
        if (first == null)
        {
            a.Status(Loc.T("Module.Army.NoUnit", "该位置没有单位"));
            return false;
        }

        string? input = await a.Dialogs!.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.PlanTitle", "设置单位方案"),
            Loc.F("Mode.Army.Dialog.PlanPrompt", cells.Count),
            first.Value.Plan.ToString(), 0, 4);
        if (input == null) return false;
        if (!TryParse(input, 0, 4, out int plan)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidPlan", "方案值需在 0-4 之间"));

        return ChangeCells(a, Loc.F("Mode.Army.Undo.SetPlan", a.Col, a.Row), cells, () => units.SetPlan(cells, plan));
    }

    private static async Task<bool> SetOccupationEventAsync(ModeActionArgs a)
    {
        var map = a.Map!;
        var army = a.Module<ArmyModifier>();
        byte? current = army.GetOccupationEvent(a.Col, a.Row);
        if (current == null)
        {
            a.Status(Loc.T("Module.Army.NoUnit", "该位置没有单位"));
            return false;
        }

        string? input = await a.Dialogs!.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.EventTitle", "设置占领触发事件"),
            Loc.T("Mode.Army.Dialog.EventPrompt", "事件序号（0-255，0 表示不触发事件）："),
            current.Value.ToString(), 0, 255);
        if (input == null) return false;
        if (!TryParse(input, 0, 255, out int eventId)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidEvent", "事件序号需在 0-255 之间"));

        var events = a.Module<EventModifier>();
        bool linked = false;
        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Army.Undo.SetEvent", a.Col, a.Row), () =>
        {
            result = army.SetOccupationEvent(a.Col, a.Row, (byte)eventId);
            int index = result.Success && eventId != 0 ? events.GetEventIndex(eventId) : -1;
            if (index < 0) return;
            linked = true;
            var mapEvent = map.Events[index];
            mapEvent.TriggerCondition = 1;
            events.UpdateEvent(index, mapEvent);
        });

        a.Status(!result.Success ? result.Message ?? ""
               : eventId == 0 ? Loc.T("Mode.Army.Msg.EventCleared", "已清除单位的占领触发事件")
               : linked ? Loc.F("Mode.Army.Msg.EventLinked", eventId)
               : Loc.F("Mode.Army.Msg.EventMissing", eventId));
        return result.Success;
    }

    #endregion

    #region 单位：整图批量（E / R / I / G / Y / Shift+Delete / Ctrl+Shift+Delete）

    private static async Task<bool> ModifyStrengthAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        // 强度等级范围从配置动态获取，对齐 VB 的 configs.Min(c => c.Id) / Max(c => c.Id)
        var levelConfigs = ConfigManager.Instance.GetAllLegionLevelConfigs();
        int minLevel = levelConfigs.Count > 0 ? levelConfigs.Min(c => c.Id) : 1;
        int maxLevel = levelConfigs.Count > 0 ? levelConfigs.Max(c => c.Id) : 1;

        var (confirmed, belongText, levelText) = await a.Dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.StrengthTitle", "修改军团强度"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            Loc.F("Mode.Army.Dialog.StrengthLevelPrompt", minLevel, maxLevel),
            SuggestedBelong(a).ToString(), minLevel.ToString());
        if (!confirmed) return false;

        if (!TryParse(belongText, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));
        if (!int.TryParse(levelText?.Trim(), out int level) || (level != -1 && (level < minLevel || level > maxLevel)))
        {
            // 对齐 VB：校验失败时列出全部可用等级，方便选择
            string available = string.Join(", ", levelConfigs.Select(c => $"{c.Id}={c.Name}"));
            return Invalid(a, Loc.F("Mode.Army.Msg.InvalidStrengthLevel", minLevel, maxLevel, available));
        }

        return ChangeMapForBelong(a, Loc.T("Mode.Army.Undo.Strength", "修改军团强度"), belong, () => Units(a).ModifyLegionStrength(belong, level));
    }

    private static async Task<bool> RandomizeUnitsAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        // 对齐 VB RandomizeLegionArmies：只有一个归属参数，用单输入框（-1 为全部军团）
        string? input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.RandomizeTitle", "随机化军团单位"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            SuggestedBelong(a).ToString());
        if (input == null) return false;
        if (!TryParse(input, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));

        return ChangeMapForBelong(a, Loc.T("Mode.Army.Undo.Randomize", "随机化军团单位"), belong, () => Units(a).RandomizeLegionArmies(belong));
    }

    private static async Task<bool> GenerateUnitsAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        var (confirmed, belongText, probText) = await a.Dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.GenerateTitle", "按概率生成单位"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            Loc.T("Mode.Army.Dialog.ProbabilityPrompt", "生成概率（0-100）："),
            SuggestedBelong(a).ToString(), "50");
        if (!confirmed) return false;

        if (!TryParse(belongText, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));
        if (!TryParse(probText, 0, 100, out int probability)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidProbability", "概率需在 0-100 之间"));

        return ChangeMapForBelong(a, Loc.T("Mode.Army.Undo.Generate", "按概率生成单位"), belong, () => Units(a).GenerateArmiesByProbability(belong, probability));
    }

    /// <summary>G：归属 + 将领数量，再依次确认"是否清除现有将领""是否让将领适配单位"（VB AutoAssignGeneralsToArmies）。</summary>
    private static async Task<bool> AssignGeneralsAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        var (confirmed, belongText, countText) = await dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.GeneralsTitle", "自动分配将领"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            Loc.T("Mode.Army.Dialog.GeneralCountPrompt", "将领数量（-1 为最大数量）："),
            SuggestedBelong(a).ToString(), "-1");
        if (!confirmed) return false;

        if (!TryParse(belongText, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));
        if (!TryParse(countText, -1, int.MaxValue, out int count)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidGeneralCount", "将领数量需为 -1 或非负整数"));

        bool clearExisting = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.ClearGeneralsTitle", "清除现有将领"),
            Loc.T("Mode.Army.Dialog.ClearGeneralsMessage", "是否清除现有单位上的将领？\n\n选择【是】：清除现有将领，拥有将领的单位也会重新分配新将领\n选择【否】：保留现有将领，跳过已有将领的单位"),
            Loc.T("Common.Yes", "是"), Loc.T("Common.No", "否"));
        bool adaptToUnits = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.AdaptGeneralsTitle", "将领适配单位"),
            Loc.T("Mode.Army.Dialog.AdaptGeneralsMessage", "是否让将领适配单位？\n\n选择【是】：将领按专长匹配对应类型的单位（步兵将领->步兵单位）\n选择【否】：尽可能分配所有将领，不匹配时随机分配单位类型"),
            Loc.T("Common.Yes", "是"), Loc.T("Common.No", "否"));

        return ChangeMapForBelong(a, Loc.T("Mode.Army.Undo.Generals", "自动分配将领"), belong,
            () => Units(a).AssignGenerals(belong, count, clearExisting, adaptToUnits));
    }

    private static async Task<bool> BatchPlanAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        // 对齐 VB BatchModifyArmyPlanByLegion 的双输入框：归属值 -1 表示所有军团，方案值 -1 表示随机
        var (confirmed, belongText, planText) = await a.Dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.BatchPlanTitle", "批量修改单位方案"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            Loc.T("Mode.Army.Dialog.BatchPlanPrompt", "方案值（0-4，-1 为随机）："),
            SuggestedBelong(a).ToString(), "-1");
        if (!confirmed) return false;

        if (!TryParse(belongText, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));
        if (!TryParse(planText, -1, 4, out int plan)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBatchPlan", "方案值需在 0-4 之间，或输入 -1（随机）"));

        return ChangeMapForBelong(a, Loc.T("Mode.Army.Undo.BatchPlan", "批量修改单位方案"), belong, () => Units(a).BatchModifyArmyPlanByLegion(belong, plan));
    }

    /// <summary>Shift+Delete：删除归属为输入值的所有单位（单位的归属 = 所在格子的归属值）。</summary>
    private static async Task<bool> RemoveByBelongAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        string? input = await dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.RemoveByBelongTitle", "删除指定归属的所有单位"),
            Loc.T("Mode.Army.Dialog.RemoveByBelongPrompt", "归属值（按单位所在格子的归属匹配）："),
            Math.Max(0, SuggestedBelong(a)).ToString(), 0, 255);
        if (input == null) return false;
        if (!TryParse(input, 0, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidOwner", "归属值需在 0-255 之间"));

        bool sure = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.RemoveByBelongTitle", "删除指定归属的所有单位"),
            Loc.F("Mode.Army.Dialog.RemoveByBelongConfirm", belong),
            Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消"));
        if (!sure) return false;

        return ChangeMap(a, Loc.F("Mode.Army.Undo.RemoveByBelong", belong), () => Units(a).DeleteArmiesByBelong(belong));
    }

    /// <summary>Ctrl+Shift+Delete：确认后删除全部单位（v1 与 v3 两张表；VB 里是无选区时按 Delete）。</summary>
    private static async Task<bool> RemoveAllUnitsAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        int total = map.Armies.Count + map.ArmiesV3.Count;
        if (total == 0)
        {
            a.Status(Loc.T("Mode.Army.Msg.NoUnits", "地图上没有单位"));
            return false;
        }

        bool sure = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.RemoveAllTitle", "删除所有单位"),
            Loc.F("Mode.Army.Dialog.RemoveAllConfirm", total),
            Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消"));
        if (!sure) return false;

        return ChangeMap(a, Loc.T("Mode.Army.Undo.RemoveAll", "删除所有单位"), () =>
        {
            a.Module<ArmyModifier>().DeleteAllArmies();
            a.Module<ArmyV3Modifier>().DeleteAllArmies();
            return ModifierResult.Ok(Loc.F("Module.Army.AllDeleted", total), total);
        });
    }

    #endregion

    #region 陷阱

    /// <summary>
    /// Ctrl+C：格子上有陷阱时复制陷阱，否则复制单位。复制单位成功时清空陷阱剪贴板，
    /// 这样 Ctrl+V 粘贴的总是最近一次 Ctrl+C 复制的东西（VB 里取决于陷阱剪贴板是否残留旧内容）。
    /// </summary>
    private static bool CopyTrapOrUnit(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var trap = a.Module<TrapModifier>();
        if (trap.GetTrapAt(a.Col, a.Row).HasValue) return Report(a, trap.CopyTrap(a.Col, a.Row));
        if (!CopyUnits(a)) return false;
        trap.ClearCopiedTrap();
        return true;
    }

    private static bool PasteTrapOrUnit(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var trap = a.Module<TrapModifier>();
        if (!trap.HasCopiedTrap) return PasteUnits(a);
        return ChangeCells(a, Loc.F("Mode.Army.Undo.PasteTrap", a.Col, a.Row), [a.Cell], () => trap.PasteTrap(a.Col, a.Row));
    }

    private static bool RemoveTrapOrUnit(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var trap = a.Module<TrapModifier>();
        var cells = a.TargetCells();
        if (!cells.Any(c => trap.GetTrapAt(c.Col, c.Row).HasValue)) return RemoveUnits(a);
        return ChangeCells(a, Loc.F("Mode.Army.Undo.RemoveTrap", a.Col, a.Row), cells, () => trap.RemoveTraps(cells));
    }

    /// <summary>F：空格放一个 1 级陷阱（所属军团按本格归属推断，建筑格拒绝）；已有陷阱时打开陷阱设置窗口修改。</summary>
    private static async Task<bool> PlaceOrEditTrapAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var trap = a.Module<TrapModifier>();
        var existing = trap.GetTrapAt(a.Col, a.Row);
        if (existing == null)
            return ChangeCells(a, Loc.F("Mode.Army.Undo.PlaceTrap", a.Col, a.Row), [a.Cell], () => trap.CreateTrapAt(a.Col, a.Row));

        if (a.Dialogs == null) return NoDialog(a);
        var (confirmed, edited) = await a.Dialogs.ShowTrapSettingDialogAsync(existing.Value, isNew: false);
        if (!confirmed) return false;

        return ChangeCells(a, Loc.F("Mode.Army.Undo.EditTrap", a.Col, a.Row), [a.Cell], () => trap.Apply(a.Col, a.Row, edited),
            Loc.F("Mode.Army.Msg.TrapEdited", a.Col, a.Row, edited.Organization, edited.LegionId, edited.Health));
    }

    private static bool CycleTrapLevel(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        return ChangeCells(a, Loc.F("Mode.Army.Undo.TrapLevel", a.Col, a.Row), [a.Cell],
            () => a.Module<TrapModifier>().CycleTrapLevel(a.Col, a.Row));
    }

    private static bool RandomizeTrap(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        return ChangeCells(a, Loc.F("Mode.Army.Undo.RandomizeTrap", a.Col, a.Row), [a.Cell],
            () => a.Module<TrapModifier>().RandomizeTrapAttributes(a.Col, a.Row));
    }

    /// <summary>Ctrl+G：归属值（-1 = 所有归属）+ 概率，再确认是否包含海洋格（VB 按归属生成，C# 早先改成了不分归属的自由生成）。</summary>
    private static async Task<bool> GenerateTrapsAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        var (confirmed, belongText, probText) = await dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.TrapGenerateTitle", "批量生成陷阱"),
            Loc.T("Mode.Army.Dialog.BelongAllPrompt", "归属值（-1 为所有军团）："),
            Loc.T("Mode.Army.Dialog.ProbabilityPrompt", "生成概率（0-100）："),
            SuggestedBelong(a).ToString(), "50");
        if (!confirmed) return false;

        if (!TryParse(belongText, -1, 255, out int belong)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidBelong", "归属值需在 -1 到 255 之间"));
        if (!TryParse(probText, 0, 100, out int probability)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidProbability", "概率需在 0-100 之间"));

        bool includeSea = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.TrapGenerateTitle", "批量生成陷阱"),
            Loc.T("Mode.Army.Dialog.TrapIncludeSea", "是否也在海洋格上生成陷阱（水雷）？"),
            Loc.T("Common.Yes", "是"), Loc.T("Common.No", "否"));

        return ChangeTraps(a, Loc.T("Mode.Army.Undo.TrapGenerate", "批量生成陷阱"),
            () => a.Module<TrapModifier>().GenerateTraps(belong, probability, includeSea));
    }

    private static async Task<bool> GenerateTrapsByProvinceAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        string? input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.TrapByProvinceTitle", "按省份生成陷阱"),
            Loc.T("Mode.Army.Dialog.TrapByProvincePrompt", "每个省份生成一个陷阱的概率（0-100）："),
            "10", 0, 100);
        if (input == null) return false;
        if (!TryParse(input, 0, 100, out int probability)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidProbability", "概率需在 0-100 之间"));

        return ChangeTraps(a, Loc.T("Mode.Army.Undo.TrapByProvince", "按省份生成陷阱"),
            () => a.Module<TrapModifier>().GenerateTrapsByProvince(probability));
    }

    /// <summary>Ctrl+R：军团值（陷阱的所属军团 ActionId，-1 = 所有陷阱）+ 每个陷阱的重抽概率。</summary>
    private static async Task<bool> RandomizeTrapLevelsAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs == null) return NoDialog(a);

        var trap = a.Module<TrapModifier>();
        int suggested = ArmyPlacementRules.ResolveOwner(map, a.Col, a.Row) >= 0 ? trap.ResolveTrapLegion(a.Col, a.Row) : -1;
        var (confirmed, legionText, probText) = await a.Dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Army.Dialog.TrapLevelsTitle", "随机化陷阱等级"),
            Loc.T("Mode.Army.Dialog.TrapLegionPrompt", "军团值（-1 为所有军团）："),
            Loc.T("Mode.Army.Dialog.TrapLevelsProbability", "随机概率（1-100）："),
            suggested.ToString(), "50");
        if (!confirmed) return false;

        if (!TryParse(legionText, -1, short.MaxValue, out int legion)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidLegion", "输入的军团值无效"));
        if (!TryParse(probText, 1, 100, out int probability)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidProbability1", "随机概率需在 1-100 之间"));

        return ChangeTraps(a, Loc.T("Mode.Army.Undo.TrapLevels", "随机化陷阱等级"), () => trap.RandomizeTrapLevels(legion, probability));
    }

    /// <summary>Ctrl+F：输入等级，选中并把视野移到下一个该等级的陷阱。</summary>
    private async Task<bool> FindTrapAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null) return NoDialog(a);

        string? input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Army.Dialog.TrapFindTitle", "查找陷阱"),
            Loc.T("Mode.Army.Dialog.TrapFindPrompt", "陷阱等级（编制）："),
            _findLevel.ToString(), 0, short.MaxValue);
        if (input == null) return false;
        if (!TryParse(input, 0, short.MaxValue, out int level)) return Invalid(a, Loc.T("Mode.Army.Msg.InvalidTrapLevel", "输入的陷阱等级无效"));

        var matches = a.Module<TrapModifier>().FindTrapsByLevel(level);
        if (matches.Count == 0)
        {
            a.Status(Loc.F("Mode.Army.Msg.TrapNotFound", level));
            return false;
        }

        _findIndex = level == _findLevel ? (_findIndex + 1) % matches.Count : 0;
        _findLevel = level;
        var hit = matches[_findIndex];
        HexSelector.Instance.Select(hit);
        a.Context.NotifyMoveCameraToHex?.Invoke(hit.Col, hit.Row);
        a.Status(Loc.F("Mode.Army.Msg.TrapFound", level, _findIndex + 1, matches.Count, hit.Col, hit.Row));
        return true;
    }

    private static async Task<bool> RemoveAllTrapsAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs is not { } dialogs) return NoDialog(a);

        if (map.Traps.Count == 0)
        {
            a.Status(Loc.T("Mode.Army.Msg.NoTraps", "地图上没有陷阱"));
            return false;
        }

        bool sure = await dialogs.ShowConfirmDialogAsync(
            Loc.T("Mode.Army.Dialog.RemoveAllTrapsTitle", "删除所有陷阱"),
            Loc.F("Mode.Army.Dialog.RemoveAllTrapsConfirm", map.Traps.Count),
            Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消"));
        if (!sure) return false;

        return ChangeTraps(a, Loc.T("Mode.Army.Undo.RemoveAllTraps", "删除所有陷阱"), () => a.Module<TrapModifier>().DeleteAllTraps());
    }

    #endregion

    #region 橡皮擦

    /// <summary>
    /// H：开关单位橡皮擦。开关、画笔设置窗口与预览都由场景处理（与地形画笔一致），这里只发通知。
    /// 场景开启时的提示（共享翻译 Render.Status.EraserOn）写的是"左键拖动"，实际擦除用右键，开启后用本模式的提示覆盖。
    /// </summary>
    private static bool ToggleEraser(ModeActionArgs a)
    {
        a.Context.NotifyBrushToggled?.Invoke();
        var brush = a.Module<ArmyModifier>().Brush;
        if (brush.Active) a.Status(Loc.F("Mode.Army.Msg.EraserOn", brush.Radius));
        return true;
    }

    private static bool ResizeEraser(ModeActionArgs a, int delta)
    {
        var brush = a.Module<ArmyModifier>().Brush;
        if (delta < 0) brush.DecreaseRadius();
        else brush.IncreaseRadius();
        a.Status(Loc.F("Mode.Army.Msg.EraserRadius", brush.Radius));
        a.Context.NotifyBrushSizeChanged?.Invoke();
        return true;
    }

    #endregion

    #region 辅助

    /// <summary>按地图版本取单位模块：BtlVersion &gt;= 3 用 v3 单位表。</summary>
    private static IArmyModule Units(ModeActionArgs a)
        => a.Map!.Header.UsesV3Records ? a.Module<ArmyV3Modifier>() : a.Module<ArmyModifier>();

    /// <summary>批量对话框里的建议归属：省会归属优先，其次当前格归属，都没有时 -1（全部）。</summary>
    private static int SuggestedBelong(ModeActionArgs a) => ArmyPlacementRules.ResolveOwner(a.Map!, a.Col, a.Row);

    private static bool InMap(MapData map, HexCoord c) => c.Col >= 0 && c.Row >= 0 && c.Col < map.MapWidth && c.Row < map.MapHeight;

    private static bool Report(ModeActionArgs a, ModifierResult result)
    {
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private static bool NoDialog(ModeActionArgs a)
    {
        a.Status(Loc.T("Mode.Army.Msg.NoDialog", "对话框服务未初始化"));
        return false;
    }

    private static bool Invalid(ModeActionArgs a, string message)
    {
        a.Status(message);
        return false;
    }

    private static bool TryParse(string? text, int min, int max, out int value)
        => int.TryParse(text?.Trim(), out value) && value >= min && value <= max;

    /// <summary>
    /// 修改若干格子并记录撤销：前后各拍一份 <see cref="ArmyHexState"/>（单位、陷阱、归属，含列表位置）。
    /// 模块返回失败或数据没有变化时不记录；状态栏显示 <paramref name="successMessage"/>（缺省为模块消息）。
    /// </summary>
    private static bool ChangeCells(ModeActionArgs a, string undoText, IReadOnlyCollection<HexCoord> cells,
        Func<ModifierResult> change, string? successMessage = null)
    {
        var map = a.Map!;
        var before = ArmyHexState.Capture(map, cells);
        var result = change();
        if (!result.Success) return Report(a, result);

        a.Status(successMessage ?? result.Message ?? "");
        var after = ArmyHexState.Capture(map, cells);
        if (after.SameAs(before)) return false;
        a.Context.RecordEntityChange(undoText, () => after.Restore(map), () => before.Restore(map));
        return true;
    }

    /// <summary>整图批量修改单位（同时可能改到格子归属、军团系数）：用全部实体 + 省份 + 归属快照记录撤销。</summary>
    private static bool ChangeMap(ModeActionArgs a, string undoText, Func<ModifierResult> change)
    {
        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(undoText, () => result = change());
        return Report(a, result);
    }

    /// <summary>同 <see cref="ChangeMap"/>，状态栏消息前加上"所有军团 / 归属 [N]"。</summary>
    private static bool ChangeMapForBelong(ModeActionArgs a, string undoText, int belong, Func<ModifierResult> change)
    {
        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(undoText, () => result = change());
        string message = result.Message ?? "";
        a.Status(belong < 0 ? Loc.F("Mode.Army.Msg.AllLegions", message) : Loc.F("Mode.Army.Msg.ForBelong", belong, message));
        return result.Success;
    }

    /// <summary>批量修改陷阱列表：只快照陷阱表记录撤销。</summary>
    private static bool ChangeTraps(ModeActionArgs a, string undoText, Func<ModifierResult> change)
    {
        ModifierResult result = default;
        a.Context.RecordCollectionChange(undoText, a.Map!.Traps, () => result = change());
        return Report(a, result);
    }

    #endregion
}
