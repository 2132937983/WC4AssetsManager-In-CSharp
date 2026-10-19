using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 空袭 / 空中支援编辑模式（对应 VB 的 AirForce 模式）。
/// <para>
/// 空袭是挂在机场上的全局列表，空中支援是不带坐标的全局列表，逐条增删改在两个列表窗口里做（Q / E）；
/// 地图上负责选机场：Ctrl+Q 在选中的机场批量生成空袭，Delete 删除选中格上的空袭。
/// 所有修改都经 <see cref="AirForceModifier"/>，并用 RecordCollectionChange 记录撤销。
/// </para>
/// </summary>
public sealed class AirForceEditMode : ModeBase
{
    public override EditMode Mode => EditMode.AirForceEdit;
    protected override string LocKey => "AirForce";
    protected override string DefaultDisplayName => "空袭编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.AirForce;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.AirForce, ModifierKind.Building };

    /// <summary>需要选区：Ctrl+Q / Delete 的目标机场要高亮出来（VB 里是绿色的剧本选中框）。</summary>
    public override bool RequiresSelection => true;

    /// <summary>VB 的空袭模式显示国家领域 + 建筑；空袭标记层（机场上的空袭条数、回合与军团）是 C# 新增的。</summary>
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.Building | ModeRenderLayers.AirForce;

    /// <summary>框选只命中带机场的建筑格与已有空袭的格子。</summary>
    public override Func<HexCoord, bool>? CreateSelectionFilter(MapData map)
    {
        int width = map.MapWidth;
        var hits = map.AirForces.Select(a => a.Coordinate).ToHashSet();
        foreach (var building in map.Buildings)
            if (building.AviationLevel > 0) hits.Add(building.Coordinate);
        return c => hits.Contains(c.Row * width + c.Col);
    }

    protected override void DefineActions(ModeActionTable t)
    {
        t.HelpLine("select", ModeGesture.LeftClick, "选择格子（Shift 加选、Ctrl 减选）")
         .HelpLine("select_box", ModeGesture.RightDrag, "框选机场")
         .Command("AF_Q", KeyCodes.Q, "open_list", "打开空袭列表",
             a => a.Context.OpenEditor(EditorKinds.AirForceList, a.Col, a.Row))
         .Command("AF_E", KeyCodes.E, "open_support_list", "打开空中支援列表",
             a => a.Context.OpenEditor(EditorKinds.AirSupportList, a.Col, a.Row))
         .Edit("AF_R", KeyCodes.R, "random_air_support", "随机生成空中支援", RandomAirSupportAsync)
         .Edit("AF_CQ", KeyChord.Ctrl(KeyCodes.Q), "airforce_from_building", "从建筑生成空袭", AirForceFromBuildingAsync)
         .Edit("AF_SQ", KeyChord.Shift(KeyCodes.Q), "airforce_from_all_airports", "给没有空袭的机场各补一条空袭", FillAirports)
         .Edit("AF_Delete", KeyCodes.Delete, "delete_at_cells", "删除选中格子上的空袭", DeleteAtCells)
         .Edit("AF_ShiftDelete", KeyChord.Shift(KeyCodes.Delete), "clear_air_forces", "清空所有空袭", ClearAirForcesAsync)
         .Edit("AF_CtrlDelete", KeyChord.Ctrl(KeyCodes.Delete), "clear_air_supports", "清空所有空中支援", ClearAirSupportsAsync)
         .GestureCommand(ModeGesture.Escape, "cancel", "取消选区", CancelSelection)
         .Note("markers", "地图标记：牌子上的数字 = 该机场的空袭条数，R = 触发回合，旗帜 = 所属军团");
    }

    /// <summary>撤销记录的说明 = 动作说明（随界面语言）。</summary>
    private string UndoText(ModeActionArgs a) => Actions.Find(a.Action)?.Text ?? a.Action;

    // ------------------------------------------------------------------ 生成

    private async Task<bool> RandomAirSupportAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        var input = await AskGenerateParamsAsync(a, module, module.DefaultLegion(),
            Loc.T("Mode.AirForce.Dialog.RandomSupportStep1", "随机生成空中支援 - 步骤1/2"),
            Loc.T("Mode.AirForce.Dialog.RandomSupportStep2", "随机生成空中支援 - 步骤2/2"));
        if (input is not { } p) return false;

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirSupports,
            () => result = module.GenerateRandomAirSupportByLegion(p.Legion, p.Count, p.MinRound, p.MaxRound));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private async Task<bool> AirForceFromBuildingAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        // 先确认目标格里有机场再弹参数对话框（与 VB 一致）；多选时在每个选中的机场上各生成一份
        var found = module.FindAirports(a.TargetCells(), out var airports);
        if (!found.Success)
        {
            a.Status(found.Message ?? "");
            return false;
        }

        var input = await AskGenerateParamsAsync(a, module, module.DefaultLegionAt(airports[0].Col, airports[0].Row),
            Loc.T("Mode.AirForce.Dialog.FromBuildingStep1", "从建筑生成空袭 - 步骤1/2"),
            Loc.T("Mode.AirForce.Dialog.FromBuildingStep2", "从建筑生成空袭 - 步骤2/2"));
        if (input is not { } p) return false;

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirForces,
            () => result = module.GenerateAirForcesAtAirports(airports, p.Legion, p.Count, p.MinRound, p.MaxRound));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>
    /// 两步参数输入（与 VB 相同的标题、说明与默认值）：第 1 步所属军团（-1 = 每个军团）+ 每个军团的生成数量，
    /// 第 2 步回合范围。取消或输入无效时返回 null（无效时在状态栏说明原因）。
    /// </summary>
    private static async Task<(int Legion, int Count, int MinRound, int MaxRound)?> AskGenerateParamsAsync(
        ModeActionArgs a, AirForceModifier module, int defaultLegion, string step1Title, string step2Title)
    {
        if (a.Dialogs is not { } dialogs)
        {
            a.Status(Loc.T("Mode.AirForce.Msg.NeedGui", "该操作需要图形界面（命令行请用 air 命令组）"));
            return null;
        }

        string cancel = Loc.T("Common.Cancel", "取消");
        var (ok1, legionText, countText) = await dialogs.ShowAirGenerateStepAsync(new AirGenerateStep(step1Title,
            Loc.T("Mode.AirForce.Dialog.Legion", "所属军团 (-1=每个军团):"), defaultLegion.ToString(CultureInfo.InvariantCulture), AirForceModifier.AllLegions, 255,
            Loc.T("Mode.AirForce.Dialog.Count", "生成数量 (>0):"), "1", 1, AirForceModifier.MaxCountPerLegion,
            Loc.T("Mode.AirForce.Dialog.Next", "下一步"), cancel));
        if (!ok1) return null;

        if (!TryParseInt(legionText, out int legion))
            return Invalid(a, Loc.T("Mode.AirForce.Msg.BadLegion", "军团值无效"));
        if (!TryParseInt(countText, out int count))
            return Invalid(a, Loc.T("Mode.AirForce.Msg.BadCount", "数量值无效"));
        if (legion != AirForceModifier.AllLegions && !module.IsValidLegion(legion))
            return Invalid(a, Loc.F("Module.AirForce.Msg.LegionNotFoundOrAll", legion));

        var (ok2, minText, maxText) = await dialogs.ShowAirGenerateStepAsync(new AirGenerateStep(step2Title,
            Loc.T("Mode.AirForce.Dialog.MinRound", "回合最小值 (>0):"), "1", 1, AirForceModifier.MaxRound,
            Loc.T("Mode.AirForce.Dialog.MaxRound", "回合最大值 (>0):"), "50", 1, AirForceModifier.MaxRound,
            Loc.T("Mode.AirForce.Dialog.Generate", "生成"), cancel));
        if (!ok2) return null;

        if (!TryParseInt(minText, out int minRound))
            return Invalid(a, Loc.T("Mode.AirForce.Msg.BadMinRound", "回合最小值无效"));
        if (!TryParseInt(maxText, out int maxRound))
            return Invalid(a, Loc.T("Mode.AirForce.Msg.BadMaxRound", "回合最大值无效"));

        return (legion, count, minRound, maxRound);
    }

    private static bool TryParseInt(string? text, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static (int, int, int, int)? Invalid(ModeActionArgs a, string message)
    {
        a.Status(message);
        return null;
    }

    private bool FillAirports(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirForces, () => result = module.GenerateAirForceFromBuildings());
        a.Status(result.Message ?? "");
        return result.Success;
    }

    // ------------------------------------------------------------------ 删除

    private bool DeleteAtCells(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirForces, () => result = module.DeleteAirForcesAt(a.TargetCells()));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private async Task<bool> ClearAirForcesAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        if (map.AirForces.Count > 0 && !await ConfirmAsync(a, Loc.F("Mode.AirForce.Msg.ConfirmClearAirForces", map.AirForces.Count)))
            return false;

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirForces, () => result = module.DeleteAllAirForces());
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private async Task<bool> ClearAirSupportsAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var module = a.Module<AirForceModifier>();

        if (map.AirSupports.Count > 0 && !await ConfirmAsync(a, Loc.F("Mode.AirForce.Msg.ConfirmClearAirSupports", map.AirSupports.Count)))
            return false;

        var result = default(ModifierResult);
        a.Context.RecordCollectionChange(UndoText(a), map.AirSupports, () => result = module.DeleteAllAirSupports());
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>有界面时弹确认框；没有界面（脚本 / AI 分发）时直接执行，和其它模式的批量删除一致。</summary>
    private static async Task<bool> ConfirmAsync(ModeActionArgs a, string message)
    {
        if (a.Dialogs is not { } dialogs) return true;
        return await dialogs.ShowConfirmDialogAsync(Loc.T("Mode.AirForce.Dialog.ConfirmTitle", "确认删除"), message,
            Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消"));
    }

    // ------------------------------------------------------------------ 选区

    /// <summary>Esc：有选区时清空选区并返回 true；没有选区时返回 false，由场景弹出"返回主界面"。</summary>
    private static bool CancelSelection(ModeActionArgs a)
    {
        if (a.Context.Selection.Count == 0) return false;
        HexSelector.Instance.ClearSelection();
        return true;
    }
}
