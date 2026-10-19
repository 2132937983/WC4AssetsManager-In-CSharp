using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 援军配置模式（对齐 VB ReinforceModifier）：左键选格子，Q 打开援军编辑窗口编辑该格叠放的整组援军，
/// C / V 整组复制粘贴，X / Delete 删除所选格子的援军，Shift+Delete 删除全部，G 按援军设置格子归属。
/// <para>
/// 数据操作都在援军模块里（按地图版本取 v1 <see cref="ReinforcementModifier"/> 或 v3 <see cref="ReinforcementV3Modifier"/>），
/// 自动归属（所属未设置时取所在省份省会的军团、格子归属随第一个援军）也由模块完成；每个改数据的动作都可撤销。
/// </para>
/// </summary>
public sealed class ReinforcementDeployMode : ModeBase
{
    public override EditMode Mode => EditMode.ReinforcementDeploy;
    protected override string LocKey => "Reinforcement";
    protected override string DefaultDisplayName => "援军配置";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Reinforcement;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Reinforcement, ModifierKind.ReinforcementV3, ModifierKind.Legion, ModifierKind.Belong };
    public override bool RequiresSelection => true;

    /// <summary>与 VB 援军模式一致：国家领域、建筑、陷阱、援军（格子中心 2×6 网格）、归属国旗；不显示部队层。</summary>
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.Building | ModeRenderLayers.Trap
                                                   | ModeRenderLayers.Reinforcement | ModeRenderLayers.BelongFlag;

    protected override void DefineActions(ModeActionTable t)
    {
        t.HelpLine("select", ModeGesture.LeftClick, "选择目标格子（Shift 加选 / Ctrl 减选）")
         .HelpLine("box_select", ModeGesture.RightDrag, "框选多格")
         .GestureEdit(ModeGesture.RightClick, "apply", "放置援军", Apply)
         .Command("RD_Q", KeyCodes.Q, "open_editor", "打开援军编辑窗口",
             a => a.Context.OpenEditor(EditorKinds.ReinforcementSetting, a.Col, a.Row))
         .Command("RD_C", KeyCodes.C, "copy", "复制选中格子的援军数据和归属", Copy)
         .Edit("RD_V", KeyCodes.V, "paste", "粘贴援军数据和归属", Paste)
         .Edit("RD_X", KeyCodes.X, "remove", "删除选中格子的援军", Remove)
         .Alias("RD_Delete", KeyCodes.Delete, "remove")
         .Edit("RD_ShiftDelete", KeyChord.Shift(KeyCodes.Delete), "delete_all", "删除所有援军数据", DeleteAllAsync)
         .Edit("RD_G", KeyCodes.G, "sync_belong", "为援军格子添加归属值", SyncBelong)
         .Command("RD_BracketOpen", KeyCodes.OemOpenBrackets, "prev_legion", "上一个军团", a => CycleOwner(a, -1))
         .Command("RD_BracketClose", KeyCodes.OemCloseBrackets, "next_legion", "下一个军团", a => CycleOwner(a, +1))
         .GestureCommand(ModeGesture.Escape, "cancel", "取消选区", CancelSelection)
         .Note("auto_owner", "放置 / 粘贴 / 在编辑窗口应用时，未设置所属的援军自动归入所在省份省会的军团（[ / ] 可改为指定军团），格子归属随第一个援军")
         .Note("layer", "援军层：格子中心 2 列 6 行，绿色数字为爆兵回合，超过 12 个显示 X总数");
    }

    /// <summary>按地图版本取援军模块（v3 = BtlVersion ≥ 3）。</summary>
    private static ReinforcementModifierBase ModuleFor(ModeActionArgs a)
        => a.Map!.Header.UsesV3Records ? a.Module<ReinforcementV3Modifier>() : a.Module<ReinforcementModifier>();

    /// <summary>右键：在点中的格子上追加一个新援军（兵种按地形随机，所属自动），并同步格子归属。</summary>
    private static bool Apply(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        bool ownerResolved = module.ResolveNewOwner(a.Col, a.Row) > 0;
        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Reinforcement.Msg.UndoPlace", a.Col, a.Row),
            () => result = module.Apply(a.Col, a.Row));
        string message = result.Message ?? "";
        if (result.Success && !ownerResolved)
            message += Loc.T("Mode.Reinforcement.Msg.OwnerUnresolved", "（未能确定所属军团：省会与本格都没有归属，请按 Q 在编辑窗口里设置）");
        a.Status(message);
        return result.Success;
    }

    /// <summary>C：复制焦点格子上的整组援军。</summary>
    private static bool Copy(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var result = ModuleFor(a).CopyGroup(a.Col, a.Row);
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>V：把复制的援军组粘贴到所选的每个格子（替换原有的整组），所属与格子归属自动设置。</summary>
    private static bool Paste(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        if (module.ClipboardCount == 0)
        {
            a.Status(Loc.T("Module.Reinforcement.ClipboardEmpty", "没有可粘贴的援军组，请先按 C 复制"));
            return false;
        }

        var cells = a.TargetCells();
        int pasted = 0;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Reinforcement.Msg.UndoPaste", cells.Count), () =>
        {
            foreach (var c in cells)
                if (module.PasteGroup(c.Col, c.Row).Success) pasted++;
        });
        a.Status(Loc.F("Mode.Reinforcement.Msg.Pasted", module.ClipboardCount, pasted));
        return pasted > 0;
    }

    /// <summary>X / Delete：删除所选格子上的全部援军（格子归属不变）。</summary>
    private static bool Remove(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        var cells = a.TargetCells();
        int removed = 0, hexes = 0;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Reinforcement.Msg.UndoRemove", cells.Count), () =>
        {
            foreach (var c in cells)
            {
                int n = module.RemoveAllAt(c.Col, c.Row);
                if (n == 0) continue;
                removed += n;
                hexes++;
            }
        });
        a.Status(removed > 0
            ? Loc.F("Mode.Reinforcement.Msg.Removed", removed, hexes)
            : Loc.T("Mode.Reinforcement.Msg.NoneSelected", "所选格子没有援军"));
        return removed > 0;
    }

    /// <summary>Shift+Delete：确认后删除地图上的所有援军（可撤销）。</summary>
    private static async Task<bool> DeleteAllAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        int count = module.GetReinforcementCount();
        if (count == 0)
        {
            a.Status(Loc.T("Mode.Reinforcement.Msg.NoReinforcements", "地图上没有援军"));
            return false;
        }

        if (a.Dialogs != null && !await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Reinforcement.Msg.ConfirmTitle", "确认操作"),
                Loc.F("Mode.Reinforcement.Msg.ConfirmDeleteAll", count),
                Loc.T("Common.Ok", "确定"), Loc.T("Common.Cancel", "取消")))
            return false;

        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.T("Mode.Reinforcement.Msg.UndoDeleteAll", "删除所有援军"),
            () => result = module.DeleteAllReinforcements());
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>G：每个援军格子的归属设为第一个援军所属军团（所属无效时取所在省份省会的归属）。</summary>
    private static bool SyncBelong(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        if (module.GetReinforcementCount() == 0)
        {
            a.Status(Loc.T("Mode.Reinforcement.Msg.NoReinforcements", "地图上没有援军"));
            return false;
        }

        int changed = 0;
        a.Context.RecordMultiCellBelongChange(Loc.T("Mode.Reinforcement.Msg.UndoSyncBelong", "为援军格子添加归属值"),
            () => changed = module.SyncBelongsFromReinforcements());
        a.Status(changed > 0
            ? Loc.F("Mode.Reinforcement.Msg.BelongSynced", changed)
            : Loc.T("Mode.Reinforcement.Msg.BelongAlreadySynced", "援军格子的归属都已与援军一致"));
        return changed > 0;
    }

    /// <summary>[ / ]：在"自动"与本图各军团之间切换新援军的所属军团。</summary>
    private static bool CycleOwner(ModeActionArgs a, int step)
    {
        if (a.Map == null) return false;
        var module = ModuleFor(a);
        var legions = a.Map.Legions;
        int states = legions.Count + 1;   // -1（自动）+ 每个军团
        int current = module.DefaultOwnerLegionIndex;
        if (current < -1 || current >= legions.Count) current = -1;
        int next = ((current + 1 + step) % states + states) % states - 1;
        module.DefaultOwnerLegionIndex = next;

        a.Status(next < 0
            ? Loc.T("Mode.Reinforcement.Msg.OwnerAuto", "新援军所属军团：自动（取所在省份省会的军团）")
            : Loc.F("Mode.Reinforcement.Msg.OwnerLegion", next + 1, legions[next].ActionId, CountryNameProvider.Get(legions[next].CountryId)));
        return true;
    }

    /// <summary>Esc：有选区时清空选区并返回 true，没有选区时交给场景弹出返回确认。</summary>
    private static bool CancelSelection(ModeActionArgs a)
    {
        var selector = HexSelector.Instance;
        if (!selector.HasSelection) return false;
        selector.ClearSelection();
        return true;
    }
}
