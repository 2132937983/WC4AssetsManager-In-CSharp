using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 事件编辑模式（对应 VB 的 EventEdit 模式 / Builder/EventModifier.vb）。
/// <para>
/// VB 版只有 Q（打开事件设置窗口）。事件模块里的新建、删除、复制粘贴、循环选择、排序在 VB 里都写好了却没有按键，
/// 这里补上按键；排序会打乱文件里的事件顺序、又不影响窗口里按序号的显示，只做成隐藏动作（脚本 / AI 调用，命令行用 event sort）。
/// 这些键都作用于"选中的事件"：事件设置窗口里选中的那一项，或用 [ / ] 选到的那一项。
/// </para>
/// <para>
/// 地图上只显示军团领域与归属国旗（VB 的军团领域层自带国旗，C# 拆成了两层），事件不在格子上，模式不需要选择器。
/// 所有改动都记录撤销（快照 <c>MapData.Events</c>）。
/// </para>
/// </summary>
public sealed class EventEditMode : ModeBase
{
    public override EditMode Mode => EditMode.EventEdit;
    protected override string LocKey => "Event";
    protected override string DefaultDisplayName => "事件编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Event;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Event };
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.BelongFlag;

    protected override void DefineActions(ModeActionTable t)
    {
        t.Command("EV_Q", KeyCodes.Q, "open_list", "打开事件设置窗口",
                a => a.Context.OpenEditor(EditorKinds.EventSetting, a.Col, a.Row))
         .Edit("EV_E", KeyCodes.E, "create_event", "新建事件（序号 = 最大序号 + 1）",
                a => Run(a, Loc.T("Mode.Event.Undo.Create", "新建事件"), m => m.CreateEvent()))
         .Edit("EV_Delete", KeyCodes.Delete, "delete_event", "删除选中的事件", DeleteSelectedAsync)
         .Command("EV_CtrlC", KeyChord.Ctrl(KeyCodes.C), "copy_event", "复制选中的事件", CopySelected)
         .Edit("EV_CtrlV", KeyChord.Ctrl(KeyCodes.V), "paste_event", "把复制的事件粘贴为新事件", Paste)
         .Command("EV_BracketOpen", KeyCodes.OemOpenBrackets, "prev_event", "选择上一个事件（按序号）",
                a => Report(a, a.Module<EventModifier>().CyclePreviousEvent()))
         .Command("EV_BracketClose", KeyCodes.OemCloseBrackets, "next_event", "选择下一个事件（按序号）",
                a => Report(a, a.Module<EventModifier>().CycleSelectEvent()))
         .HiddenEdit("sort_by_sequence", "按序号重排事件列表",
                a => Run(a, Loc.T("Mode.Event.Undo.SortBySequence", "事件按序号排序"), m => m.SortEventsBySequence()))
         .HiddenEdit("sort_by_round", "按触发回合重排事件列表",
                a => Run(a, Loc.T("Mode.Event.Undo.SortByRound", "事件按触发回合排序"), m => m.SortEventsByTriggerRound()))
         .HiddenCommand("check_references", "检查事件引用（军团 / 关联事件 / 建筑与单位的占领事件、重复序号）", CheckReferences)
         .Note("selected", "Delete / Ctrl+C 作用于选中的事件（事件设置窗口里选中的项，或用 [ / ] 选到的项）；E / Ctrl+V 新建的事件随即成为选中的事件");
    }

    /// <summary>在一次可撤销的事件列表修改里执行模块操作，结果消息显示到状态栏。返回 true 表示数据确实变了。</summary>
    private static bool Run(ModeActionArgs a, string undoDescription, Func<EventModifier, ModifierResult> operation)
    {
        if (a.Map == null) return false;
        var events = a.Module<EventModifier>();
        ModifierResult result = default;
        a.Context.RecordCollectionChange(undoDescription, a.Map.Events, () => result = operation(events));
        return Report(a, result) && result.AffectedCount > 0;
    }

    private static bool Report(ModeActionArgs a, ModifierResult result)
    {
        if (result.Message != null) a.Status(result.Message);
        return result.Success;
    }

    private static async Task<bool> DeleteSelectedAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var events = a.Module<EventModifier>();
        int index = events.SelectedEventIndex;
        if (index < 0)
        {
            a.Status(Loc.T("Module.Event.Msg.NoSelection", "没有选中的事件"));
            return false;
        }

        var target = a.Map.Events[index];
        if (a.Dialogs != null)
        {
            bool confirmed = await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Event.Msg.DeleteTitle", "删除事件"),
                Loc.F("Mode.Event.Msg.DeleteConfirm", events.Describe(target)),
                Loc.T("Common.Confirm", "确认"), Loc.T("Common.Cancel", "取消"));
            // 对话框期间列表可能被别处改过：确认的必须还是同一个事件
            if (!confirmed || index >= a.Map.Events.Count || !a.Map.Events[index].Equals(target)) return false;
        }

        return Run(a, Loc.F("Mode.Event.Undo.Delete", target.Sequence), m => m.DeleteEvent(index));
    }

    private static bool CopySelected(ModeActionArgs a)
    {
        var events = a.Module<EventModifier>();
        int index = events.SelectedEventIndex;
        return Report(a, index < 0
            ? ModifierResult.Fail(Loc.T("Module.Event.Msg.NoSelection", "没有选中的事件"))
            : events.CopyEvent(index));
    }

    private static bool Paste(ModeActionArgs a)
    {
        if (!a.Module<EventModifier>().HasCopiedEvent)
            return Report(a, ModifierResult.Fail(Loc.T("Module.Event.Msg.NothingCopied", "没有已复制的事件数据")));
        return Run(a, Loc.T("Mode.Event.Undo.Paste", "粘贴事件"), m => m.PasteEvent());
    }

    private static bool CheckReferences(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var issues = EventReferenceChecker.Check(a.Map);
        a.Status(issues.Count == 0
            ? Loc.T("Mode.Event.Msg.CheckPassed", "事件引用检查通过")
            : Loc.F("Mode.Event.Msg.CheckFound", issues.Count) + "\n" + string.Join("\n", issues.Select(i => "  " + i.Message)));
        return true;
    }
}
