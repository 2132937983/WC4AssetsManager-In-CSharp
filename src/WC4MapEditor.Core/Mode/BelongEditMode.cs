using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 归属编辑模式 - 对齐 VB 版 Builder/BelongModifier。
/// <para>
/// 只改格子归属（主模块 <see cref="BelongModifier"/>），同时引入建筑、单位、陷阱模块：
/// 这些实体在本模式下可见，用于人工核对归属是否与驻地实体匹配（对齐 VB 归属模式的图层组合）。
/// Q / V / X 作用于整个选区（框选或 Shift 加选的多个格子），每次操作都可撤销。
/// </para>
/// </summary>
public sealed class BelongEditMode : ModeBase
{
    private readonly Random _random = new();

    public override EditMode Mode => EditMode.BelongEdit;
    protected override string LocKey => "Belong";
    protected override string DefaultDisplayName => "归属编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Belong;

    public override ModifierKind[] ModifierKinds => new[]
    {
        ModifierKind.Building, ModifierKind.Army, ModifierKind.Belong, ModifierKind.Trap
    };

    public override bool RequiresSelection => true;

    public override ModeRenderLayers RenderLayers =>
        ModeRenderLayers.Building | ModeRenderLayers.Army | ModeRenderLayers.Trap
        | ModeRenderLayers.LegionDomain | ModeRenderLayers.BelongFlag | ModeRenderLayers.ClippedTiles;

    /// <summary>
    /// 框选 / 点选：在归属数据范围内的格子都能选中 —— 没有归属的空格子正是要分配归属的目标
    /// （以前只能选中已有归属或有实体的格子）；裁剪区外的格子没有归属数据，选了也改不了。
    /// </summary>
    public override Func<HexCoord, bool>? CreateSelectionFilter(MapData map)
        => c => map.IsInBelongRange(c.Col, c.Row);

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureCommand(ModeGesture.LeftClick, "show_belong", "选择格子并显示其归属", ShowBelong)
         .GestureEdit(ModeGesture.RightClick, "apply", "设置归属（画笔模式下按住右键绘制）", ApplyAtClick)
         .HelpLine("box_select", ModeGesture.RightDrag, "框选多格（Shift 加选，Ctrl 减选）")
         .Edit("BE_Q", KeyCodes.Q, "select_legion", "设置归属（从归属列表中选择）", SelectLegionAsync)
         .Command("BE_C", KeyCodes.C, "copy", "复制归属", Copy)
         .Edit("BE_V", KeyCodes.V, "paste", "粘贴归属", Paste)
         .Edit("BE_X", KeyCodes.X, "delete_belong", "删除归属", DeleteBelong)
         .Edit("BE_G", KeyCodes.G, "clean_orphan", "检查并清理归属（无归属时取最近归属）", CleanOrphan)
         .Edit("BE_R", KeyCodes.R, "randomize", "随机化归属", RandomizeAsync)
         .Edit("BE_T", KeyCodes.T, "batch_set", "批量更改归属", BatchSetAsync)
         .Edit("BE_Delete", KeyCodes.Delete, "remove", "清理所有归属", ClearAllAsync)
         .Edit("BE_E", KeyCodes.E, "remove_border_entities", "删除地图边缘的建筑、单位与陷阱", RemoveBorderEntitiesAsync)
         // VB 的 DeleteBorderUnitsOnly 从未绑键：保留能力给脚本 / AI，不占键位
         .HiddenEdit("remove_border_armies", "仅删除地图边缘的单位", RemoveBorderArmies)
         .Command("BE_L", KeyCodes.L, "toggle_domain", "切换国家领域显示", a =>
         {
             a.Context.NotifyToggleDomain?.Invoke();
             return true;
         })
         .Command("BE_H", KeyCodes.H, "toggle_brush", "切换画笔模式", a =>
         {
             a.Context.NotifyBrushToggled?.Invoke();
             return true;
         })
         .Command("BE_BracketL", KeyCodes.OemOpenBrackets, "brush_smaller", "减小画笔半径（仅画笔模式）", a => ResizeBrush(a, grow: false))
         .Command("BE_BracketR", KeyCodes.OemCloseBrackets, "brush_bigger", "增大画笔半径（仅画笔模式）", a => ResizeBrush(a, grow: true))
         .GestureCommand(ModeGesture.Escape, "cancel", "取消选区", BelongCellActions.CancelSelection)
         .Note("multi", "Q / V / X 作用于整个选区（框选或 Shift 加选的多个格子）")
         .Note("layers", "建筑/单位/陷阱层同时可见，便于核对归属")
         .Note("clipped", "红叉 = 数据范围外的格子，没有归属数据，无法设置归属");
    }

    // ------------------------------------------------------------------ 鼠标

    /// <summary>左键：场景已经选好格子，这里在状态栏报告该格归属（值与所属军团）。</summary>
    private static bool ShowBelong(ModeActionArgs a)
    {
        var belong = a.Module<BelongModifier>();
        a.Status(belong.IsWritable(a.Col, a.Row)
            ? Loc.F("Mode.Belong.Msg.CellBelong", a.Col, a.Row, belong.DescribeBelong(a.Map!.GetBelongValue(a.Col, a.Row)))
            : Loc.F("Module.Belong.OutsideRange", a.Col, a.Row));
        return true;
    }

    /// <summary>
    /// 右键：给点中的格子设置归属，复制的归属优先、否则用当前归属值（对齐 VB HandleMouseDown 右键分支）。
    /// 画笔开启时右键由场景用于绘制，不会走到这里；裁剪区外的格子如实报告失败。
    /// </summary>
    private static bool ApplyAtClick(ModeActionArgs a)
    {
        var belong = a.Module<BelongModifier>();
        int value = belong.ResolveBrushCountryId();
        ModifierResult result = default;
        a.Context.RecordBelongChange(a.Col, a.Row, Loc.F("Mode.Belong.Undo.Set", a.Col, a.Row),
            () => result = belong.SetBelongByCountryId(a.Col, a.Row, value));
        BelongCellActions.Report(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    // ------------------------------------------------------------------ 单格 / 选区

    private static string SelectFirst => Loc.T("Mode.Belong.Msg.SelectFirst", "请先选中一个格子");

    private static bool Copy(ModeActionArgs a)
    {
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out _)) return false;
        var result = a.Module<BelongModifier>().CopyBelongValue(a.Col, a.Row);
        BelongCellActions.Report(a, result);
        return result.Success;
    }

    private static bool Paste(ModeActionArgs a)
    {
        var belong = a.Module<BelongModifier>();
        if (!belong.HasCopiedBelong)
        {
            a.Status(Loc.T("Module.Belong.NothingCopied", "尚未复制归属（先按 C 复制一个格子的归属）"));
            return false;
        }
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out var cells)) return false;

        string value = belong.DescribeBelong(belong.CopiedBelongValue);
        return BelongCellActions.WriteCells(a, cells, UndoText(cells, "Paste"),
            c => belong.PasteBelongValue(c.Col, c.Row),
            changed => Loc.F("Mode.Belong.Msg.CellsPasted", changed, cells.Count, value));
    }

    private static bool DeleteBelong(ModeActionArgs a)
    {
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out var cells)) return false;
        var belong = a.Module<BelongModifier>();
        return BelongCellActions.WriteCells(a, cells, UndoText(cells, "Delete"),
            c => belong.Remove(c.Col, c.Row),
            changed => Loc.F("Mode.Belong.Msg.CellsCleared", changed, cells.Count));
    }

    /// <summary>
    /// Q：从归属列表选一个值，写到整个选区，并设为当前归属值。
    /// 选完会丢弃复制的归属，否则之后右键 / 画笔仍写复制的值，Q 的选择形同虚设（VB 的老问题）。
    /// 地图没有军团时也打开窗口：列表为空，但可以手动输入归属值。
    /// </summary>
    private static async Task<bool> SelectLegionAsync(ModeActionArgs a)
    {
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out var cells)) return false;
        if (a.Dialogs == null)
        {
            a.Status(NoDialog);
            return false;
        }

        var (confirmed, value) = await a.Dialogs.ShowBelongListDialogAsync(a.Map!.Legions.ToList());
        if (!confirmed) return false;

        var belong = a.Module<BelongModifier>();
        belong.SelectedCountryId = value;
        belong.ClearCopiedBelong();

        string desc = belong.DescribeBelong(value);
        return BelongCellActions.WriteCells(a, cells, UndoText(cells, "Set"),
            c => belong.SetBelongByCountryId(c.Col, c.Row, value),
            changed => Loc.F("Mode.Belong.Msg.CellsSet", changed, cells.Count, desc));
    }

    /// <summary>撤销说明：单格带坐标，多格带格数（键 Mode.Belong.Undo.{name} / {name}Many）。</summary>
    private static string UndoText(IReadOnlyList<HexCoord> cells, string name)
        => cells.Count == 1
            ? Loc.F($"Mode.Belong.Undo.{name}", cells[0].Col, cells[0].Row)
            : Loc.F($"Mode.Belong.Undo.{name}Many", cells.Count);

    // ------------------------------------------------------------------ 整图批量

    private static string NoDialog => Loc.T("Mode.Belong.Msg.NoDialog", "对话框服务未初始化");

    private static bool CleanOrphan(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var belong = a.Module<BelongModifier>();
        int cleaned = 0;
        BelongModifier.RecordBulkChange(a.Context.Owner, a.Map, Loc.T("Mode.Belong.Undo.CleanOrphan", "检查并清理归属"),
            () => cleaned = belong.CleanOrphanBelongs());

        var stats = belong.LastCleanStats;
        a.Status(Loc.F("Mode.Belong.Msg.Cleaned", cleaned, stats.ProvinceSynced, stats.NearestFilled, stats.BelongFixed));
        return cleaned + stats.ProvinceSynced + stats.NearestFilled + stats.BelongFixed > 0;
    }

    private async Task<bool> RandomizeAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null)
        {
            a.Status(NoDialog);
            return false;
        }

        var belong = a.Module<BelongModifier>();
        var input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Belong.Dialog.RandomizeTitle", "随机化归属"),
            Loc.T("Mode.Belong.Dialog.RandomizePrompt", "请输入要随机化的归属ID (0-255):"),
            belong.SelectedCountryId.ToString(CultureInfo.InvariantCulture), 0, 255);
        if (input == null) return false;
        if (!TryParseBelong(input, out int target))
        {
            a.Status(InvalidBelong);
            return false;
        }

        int count = 0;
        BelongModifier.RecordBulkChange(a.Context.Owner, a.Map, Loc.F("Mode.Belong.Undo.Randomize", target),
            () => count = belong.RandomizeBelongByTargetId(target, _random));
        a.Status(Loc.F("Mode.Belong.Msg.Randomized", count, target));
        return count > 0;
    }

    private static async Task<bool> BatchSetAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs == null)
        {
            a.Status(NoDialog);
            return false;
        }

        var belong = a.Module<BelongModifier>();
        var (confirmed, value1, value2) = await a.Dialogs.ShowRangedDoubleInputDialogAsync(
            Loc.T("Mode.Belong.Dialog.ReplaceTitle", "批量更改归属"),
            Loc.T("Mode.Belong.Dialog.ReplaceOld", "原归属ID (0-255):"),
            Loc.T("Mode.Belong.Dialog.ReplaceNew", "新归属ID (0-255):"),
            "0", belong.SelectedCountryId.ToString(CultureInfo.InvariantCulture),
            0, 255, 0, 255);
        if (!confirmed || value1 == null || value2 == null) return false;
        if (!TryParseBelong(value1, out int oldId) || !TryParseBelong(value2, out int newId))
        {
            a.Status(InvalidBelong);
            return false;
        }

        int count = 0;
        BelongModifier.RecordBulkChange(a.Context.Owner, a.Map, Loc.F("Mode.Belong.Undo.Replace", oldId, newId),
            () => count = belong.ReplaceBelong(oldId, newId));
        a.Status(Loc.F("Mode.Belong.Msg.Replaced", count, oldId, newId));
        return count > 0;
    }

    private static async Task<bool> ClearAllAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs != null && !await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Belong.Dialog.ConfirmTitle", "确认操作"),
                Loc.T("Mode.Belong.Dialog.ClearAllConfirm", "确定要清理所有归属吗？\n（可用 Ctrl+Z 撤销）"),
                Loc.T("Common.Ok", "确定"), Loc.T("Common.Cancel", "取消")))
            return false;

        int count = 0;
        BelongModifier.RecordBulkChange(a.Context.Owner, a.Map, Loc.T("Mode.Belong.Undo.ClearAll", "清理所有归属"),
            () => count = a.Module<BelongModifier>().ClearAllBelongs());
        a.Status(Loc.F("Mode.Belong.Msg.ClearedAll", count));
        return count > 0;
    }

    /// <summary>
    /// E：删除地图最外圈上的建筑、单位与陷阱。先确认（无界面时直接执行），
    /// 整体快照记录撤销（会动到建筑 / 单位 v1、v3 / 陷阱多个列表），状态栏分类报告数量。
    /// </summary>
    private static async Task<bool> RemoveBorderEntitiesAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (a.Dialogs != null && !await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Belong.Dialog.RemoveBorderTitle", "删除地图边缘实体"),
                Loc.T("Mode.Belong.Dialog.RemoveBorderConfirm", "删除地图最外圈格子上的建筑、单位与陷阱？\n（可用 Ctrl+Z 撤销）"),
                Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消")))
            return false;

        BelongModifier.BorderRemoval removal = default;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.T("Mode.Belong.Undo.RemoveBorder", "删除地图边缘的建筑、单位与陷阱"),
            () => removal = a.Module<BelongModifier>().RemoveBorderEntities());
        a.Status(Loc.F("Mode.Belong.Msg.BorderRemoved", removal.Buildings, removal.Armies + removal.ArmiesV3, removal.Traps));
        return removal.Total > 0;
    }

    private static bool RemoveBorderArmies(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        int count = 0;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.T("Mode.Belong.Undo.RemoveBorderArmies", "删除地图边缘的单位"),
            () => count = a.Module<BelongModifier>().RemoveBorderArmies());
        a.Status(Loc.F("Mode.Belong.Msg.BorderArmiesRemoved", count));
        return count > 0;
    }

    private static string InvalidBelong => Loc.T("Mode.Belong.Msg.InvalidBelong", "输入无效，请输入0-255的整数");

    private static bool TryParseBelong(string text, out int value)
        => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
           && value >= 0 && value <= 255;

    // ------------------------------------------------------------------ 画笔

    /// <summary>[ / ]：只在画笔模式下调整半径（对齐 VB；最小 0，无上限）。</summary>
    private static bool ResizeBrush(ModeActionArgs a, bool grow)
    {
        var brush = a.Module<BelongModifier>().Brush;
        if (!brush.Active) return false;

        if (grow) brush.IncreaseRadius();
        else brush.DecreaseRadius();
        a.Status(Loc.F(grow ? "Mode.Belong.Msg.BrushBigger" : "Mode.Belong.Msg.BrushSmaller", brush.Radius));
        a.Context.NotifyBrushSizeChanged?.Invoke();
        return true;
    }
}

/// <summary>
/// 归属 / 军团两个模式共用的"按选区写归属"动作片段：取目标格、逐格写入并记录撤销、取消选区、报告结果。
/// </summary>
internal static class BelongCellActions
{
    /// <summary>
    /// 键盘动作的目标格（<see cref="ModeActionArgs.TargetCells"/>）。选区为空说明焦点格只是兜底的 (0,0)
    /// （没有选中、鼠标也不在地图上），拒绝执行并提示，而不是去改左上角的格子（对齐 VB"请先左键选中一个格子"）。
    /// </summary>
    public static bool TryGetTargets(ModeActionArgs a, string selectFirstMessage, out IReadOnlyList<HexCoord> cells)
    {
        if (a.Map == null || a.Context.Selection.Count == 0)
        {
            a.Status(selectFirstMessage);
            cells = Array.Empty<HexCoord>();
            return false;
        }
        cells = a.TargetCells();
        return true;
    }

    /// <summary>
    /// 对目标格逐个写归属并记录撤销：单格逐格记录，多格按归属数组快照（一次撤销还原整批）。
    /// 单格时状态栏给模块的结果消息（含失败原因，如裁剪区外），多格时给汇总。返回是否有格子真的变了。
    /// </summary>
    public static bool WriteCells(ModeActionArgs a, IReadOnlyList<HexCoord> cells, string undoText,
        Func<HexCoord, ModifierResult> write, Func<int, string> summary)
    {
        int changed = 0;
        ModifierResult last = default;
        void Run()
        {
            foreach (var cell in cells)
            {
                last = write(cell);
                if (last.Success) changed += last.AffectedCount;
            }
        }

        if (cells.Count == 1)
            a.Context.RecordBelongChange(cells[0].Col, cells[0].Row, undoText, Run);
        else
            BelongModifier.RecordBulkChange(a.Context.Owner, a.Map!, undoText, Run);

        if (cells.Count == 1) Report(a, last);
        else a.Status(summary(changed));
        return changed > 0;
    }

    /// <summary>Esc：有选区就清空并返回 true（场景不再弹返回主界面确认），没有选区返回 false。</summary>
    public static bool CancelSelection(ModeActionArgs a)
    {
        var selector = HexSelector.Instance;
        if (!selector.HasSelection) return false;
        selector.ClearSelection();
        return true;
    }

    /// <summary>把模块的结果消息显示到状态栏（没有消息时不显示）。</summary>
    public static void Report(ModeActionArgs a, ModifierResult result)
    {
        if (!string.IsNullOrEmpty(result.Message)) a.Status(result.Message);
    }
}
