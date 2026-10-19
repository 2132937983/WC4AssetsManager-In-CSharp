using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 方案（AI 方针）编辑模式（对应 VB 的 MapCase 模式）。
/// <para>
/// 选择单位（单击 / 右键拖动框选，只命中有单位的格子），Ctrl+Q 批量设置它们的方针值（Army.Policy）——
/// 方针值等于方案方针编号的单位执行该方案；Q 打开方案列表窗口编辑方案本身。
/// </para>
/// </summary>
public sealed class MapCaseEditMode : ModeBase
{
    public override EditMode Mode => EditMode.MapCaseEdit;
    protected override string LocKey => "MapCase";
    protected override string DefaultDisplayName => "方案编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.MapCase;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.MapCase, ModifierKind.Army, ModifierKind.ArmyV3 };
    public override bool RequiresSelection => true;

    /// <summary>对齐 VB：国家领域 + 单位（MainRender 的 MapCaseEditMode 分支）；方案层画目标地块与方案面板。</summary>
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.Army | ModeRenderLayers.MapCase;

    /// <summary>框选只命中有单位的格子（v1/v2 与 v3 地图分别查各自的单位列表）。</summary>
    public override Func<HexCoord, bool>? CreateSelectionFilter(MapData map)
    {
        var unitTiles = MapCaseModifier.UnitTiles(map);
        int width = map.MapWidth;
        return c => unitTiles.Contains(c.Row * width + c.Col);
    }

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureCommand(ModeGesture.LeftClick, "click_select", "选择单位（点空白处取消选择）", SelectUnitAt)
         .GestureCommand(ModeGesture.RightClick, "select_unit", "选择单位（点空白处取消选择）", SelectUnitAt)
         .HelpLine("box_select", ModeGesture.RightDrag, "框选单位（Shift 加选，Ctrl 减选）")
         .Command("MC_Q", KeyCodes.Q, "open_list", "打开方案列表",
             a => a.Context.OpenEditor(EditorKinds.MapCaseList, a.Col, a.Row))
         .Edit("MC_CQ", KeyChord.Ctrl(KeyCodes.Q), "set_policy", "设置选中单位的方针值", SetPolicyAsync)
         .GestureCommand(ModeGesture.Escape, "cancel", "取消选择", CancelSelection)
         .Note("link", "方针值等于方案方针编号的单位执行该方案（Tab 查看单位的方针值）");
    }

    /// <summary>
    /// 单击（不带 Shift / Ctrl）：点到单位就只选中它并显示它的方针值，点到空白处取消选择（对齐 VB）。
    /// 带 Shift / Ctrl 的单击由场景按通用规则加选 / 减选。
    /// </summary>
    private static bool SelectUnitAt(ModeActionArgs a)
    {
        if (a.Map == null) return false;

        var selector = HexSelector.Instance;
        var policies = a.Module<MapCaseModifier>().GetPoliciesAt(new[] { a.Cell });
        if (policies.Count == 0)
        {
            selector.ClearSelection();
            return true;
        }

        selector.SetSelection(new[] { a.Cell });
        a.Status(Loc.F("Mode.MapCase.Msg.UnitSelected", a.Col, a.Row, string.Join(",", policies)));
        return true;
    }

    /// <summary>
    /// Ctrl+Q：为选中的单位设置方针值（0-255）。选中单位的方针值一致时以它为默认值；
    /// 没有多选时作用于焦点格上的单位。整批修改记一次撤销，结果显示在状态栏（VB 弹的是模态提示框）。
    /// </summary>
    private static async Task<bool> SetPolicyAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null) return false;

        var module = a.Module<MapCaseModifier>();
        var cells = a.TargetCells();
        var policies = module.GetPoliciesAt(cells);
        if (policies.Count == 0)
        {
            a.Status(Loc.T("Mode.MapCase.Msg.NoUnitSelected", "请先选择至少一个单位（单击或右键拖动框选）"));
            return false;
        }

        if (a.Dialogs == null)
        {
            a.Status(Loc.T("Mode.MapCase.Msg.NeedDialog", "设置方针值需要界面输入框"));
            return false;
        }

        int defaultPolicy = policies.TrueForAll(p => p == policies[0]) ? policies[0] : 0;
        string? input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.MapCase.Dialog.PolicyTitle", "设置方针值"),
            Loc.F("Mode.MapCase.Dialog.PolicyPrompt", policies.Count),
            defaultPolicy.ToString(CultureInfo.InvariantCulture), 0, MapCaseModifier.MaxPolicy);
        if (input == null) return false;

        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int policy)
            || policy < 0 || policy > MapCaseModifier.MaxPolicy)
        {
            a.Status(Loc.F("Mode.MapCase.Msg.InvalidPolicy", input, MapCaseModifier.MaxPolicy));
            return false;
        }

        var result = default(ModifierResult);
        RecordUnitsChange(a.Context, map, Loc.F("Mode.MapCase.Undo.SetPolicy", policies.Count, policy),
            () => result = module.SetPolicyForUnitsAt(cells, policy));
        if (result.Message != null) a.Status(result.Message);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>Esc：有选区时取消选择；没有可取消的东西时返回 false，交给场景弹出返回主界面确认。</summary>
    private static bool CancelSelection(ModeActionArgs a)
    {
        var selector = HexSelector.Instance;
        if (!selector.HasSelection) return false;

        selector.ClearSelection();
        a.Status(Loc.T("Mode.MapCase.Msg.SelectionCleared", "已取消选择"));
        return true;
    }

    /// <summary>单位方针值修改的撤销记录：按地图版本快照 v1/v2 或 v3 单位列表。</summary>
    private static void RecordUnitsChange(ModeContext context, MapData map, string description, Action apply)
    {
        if (MapCaseModifier.UsesV3Units(map))
            context.RecordCollectionChange(description, map.ArmiesV3, apply);
        else
            context.RecordCollectionChange(description, map.Armies, apply);
    }
}
