using System.CommandLine;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 event 命令组：地图事件的查看、增删改、排序与引用检查（VB 版没有对应命令）。
public sealed partial class CliCommandHost
{
    /// <summary>
    /// event 命令组。事件默认按<b>序号</b>指定；序号重复时加 <c>-i</c> 改按列表下标（event list 第一列）指定。
    /// 所有修改都走 <see cref="EventModifier"/> 并记录撤销（脚本批量执行时不记，与其他命令组一致）。
    /// </summary>
    [CliSubcommand]
    private Command BuildEventCommand()
    {
        var cmd = new Command("event", "地图事件操作（事件按序号指定；序号重复时加 -i 按列表下标指定）");

        var listCmd = new Command("list", "列出所有事件（按序号排序；[下标] 是 -i 用的列表下标，! 表示有引用问题）");
        listCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            var issues = EventReferenceChecker.Check(mapData);
            _output.WriteLine($"事件总数: {mapData.Events.Count}");
            foreach (int i in events.GetDisplayOrder())
            {
                var e = mapData.Events[i];
                string mark = EventReferenceChecker.ForEvent(issues, i, e.Sequence).Any(x => x.Level == BTLIssueLevel.Warning) ? "!" : " ";
                string dialogue = e.DialogueCode != 0 ? $"  对话={e.DialogueCode}" : "";
                _output.WriteLine($"{mark} [{i}] {events.Describe(e)}{dialogue}");
            }
        }, readOnly: true));

        var showCmd = new Command("show", "显示一个事件的全部字段、引用它的地方与相关问题");
        var showArg = EventTargetArgument();
        var showIndexOpt = EventIndexOption();
        showCmd.Arguments.Add(showArg);
        showCmd.Options.Add(showIndexOpt);
        showCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int index = ResolveEventIndex(events, mapData, parseResult.GetValue(showArg), parseResult.GetValue(showIndexOpt));
            if (index < 0) return;

            var e = mapData.Events[index];
            _output.WriteLine($"{events.Describe(e)}  （列表下标 {index}）");
            foreach (var field in MapEventFields.All)
                _output.WriteLine($"  {field.Name,-17} {field.Format(field.Get(e)),-12} {field.Label}");
            if (e.TriggerLegion != 0 || e.AffectedLegion != 0)
                _output.WriteLine($"  军团: 触发 {events.DescribeLegion(e.TriggerLegion)}，受影响 {events.DescribeLegion(e.AffectedLegion)}");
            var refs = events.CountReferences(e.Sequence, index);
            _output.WriteLine($"  引用: 关联事件 {refs.RelatedEvents} 处，建筑 {refs.Buildings} 处，单位 {refs.Armies} 处");
            foreach (var issue in EventReferenceChecker.ForEvent(EventReferenceChecker.Check(mapData), index, e.Sequence))
                _output.WriteLine($"  [{issue.Level}] {issue.Message}");
        }, readOnly: true));

        var addCmd = new Command("add", "新增事件（默认序号 = 最大序号 + 1，结束段 00 CC CC CC）");
        var addSeqOpt = new Option<int?>("--seq", "-s") { Description = "指定序号（已被占用则失败）" };
        addCmd.Options.Add(addSeqOpt);
        addCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int? seq = parseResult.GetValue(addSeqOpt);
            WriteEventResult(RecordEventListChange(mgr, mapData, "新增事件",
                () => seq.HasValue ? events.CreateEventAtSequence(seq.Value) : events.CreateEvent()));
        }));

        var setCmd = new Command("set", "修改事件字段（字段名见 event fields；值可写十进制或 0x 十六进制）");
        var setArg = EventTargetArgument();
        var setFieldArg = new Argument<string>("field") { Description = "字段名，如 TriggerRound、AffectedLegion" };
        var setValueArg = new Argument<string>("value") { Description = "新值（十进制或 0x 十六进制）" };
        var setIndexOpt = EventIndexOption();
        setCmd.Arguments.Add(setArg);
        setCmd.Arguments.Add(setFieldArg);
        setCmd.Arguments.Add(setValueArg);
        setCmd.Options.Add(setIndexOpt);
        setCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int index = ResolveEventIndex(events, mapData, parseResult.GetValue(setArg), parseResult.GetValue(setIndexOpt));
            if (index < 0) return;
            string fieldName = parseResult.GetValue(setFieldArg) ?? "";
            var field = MapEventFields.Find(fieldName);
            if (field == null)
            {
                _output.WriteLine($"失败：没有字段 {fieldName}。可写字段：{string.Join(", ", MapEventFields.All.Select(f => f.Name))}");
                return;
            }
            string value = parseResult.GetValue(setValueArg) ?? "";
            WriteEventResult(RecordEventListChange(mgr, mapData, $"修改事件字段 {field.Name}",
                () => events.SetEventField(index, field, value)));
        }));

        var removeCmd = new Command("remove", "删除事件");
        var removeArg = EventTargetArgument();
        var removeIndexOpt = EventIndexOption();
        removeCmd.Arguments.Add(removeArg);
        removeCmd.Options.Add(removeIndexOpt);
        removeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int index = ResolveEventIndex(events, mapData, parseResult.GetValue(removeArg), parseResult.GetValue(removeIndexOpt));
            if (index < 0) return;
            WriteEventResult(RecordEventListChange(mgr, mapData, "删除事件", () => events.DeleteEvent(index)));
        }));

        var duplicateCmd = new Command("duplicate", "复制事件为新事件（新序号 = 最大序号 + 1）");
        var duplicateArg = EventTargetArgument();
        var duplicateIndexOpt = EventIndexOption();
        duplicateCmd.Arguments.Add(duplicateArg);
        duplicateCmd.Options.Add(duplicateIndexOpt);
        duplicateCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int index = ResolveEventIndex(events, mapData, parseResult.GetValue(duplicateArg), parseResult.GetValue(duplicateIndexOpt));
            if (index < 0) return;
            WriteEventResult(RecordEventListChange(mgr, mapData, "复制事件", () => events.DuplicateEvent(index)));
        }));

        var renumberCmd = new Command("renumber", "修改事件序号；加 --refs 同时把关联事件、建筑与单位的占领事件改到新序号");
        var renumberArg = EventTargetArgument();
        var renumberNewArg = new Argument<int>("newSeq") { Description = "新序号（0-65535；建筑 / 单位只能引用 1-255）" };
        var renumberRefsOpt = new Option<bool>("--refs", "-r") { Description = "同时更新引用旧序号的地方" };
        var renumberIndexOpt = EventIndexOption();
        renumberCmd.Arguments.Add(renumberArg);
        renumberCmd.Arguments.Add(renumberNewArg);
        renumberCmd.Options.Add(renumberRefsOpt);
        renumberCmd.Options.Add(renumberIndexOpt);
        renumberCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            int index = ResolveEventIndex(events, mapData, parseResult.GetValue(renumberArg), parseResult.GetValue(renumberIndexOpt));
            if (index < 0) return;
            int newSeq = parseResult.GetValue(renumberNewArg);
            bool refs = parseResult.GetValue(renumberRefsOpt);
            ModifierResult result = default;
            // 带 --refs 时会改到建筑 / 单位列表，用全实体快照记录撤销
            if (refs) RecordEventEntitiesChange(mgr, "修改事件序号并更新引用", () => result = events.RenumberEvent(index, newSeq, true));
            else result = RecordEventListChange(mgr, mapData, "修改事件序号", () => events.RenumberEvent(index, newSeq, false));
            WriteEventResult(result);
        }));

        var sortCmd = new Command("sort", "重排文件里的事件顺序（默认按序号；--by-round 按触发回合）");
        var sortRoundOpt = new Option<bool>("--by-round") { Description = "按触发回合排序" };
        sortCmd.Options.Add(sortRoundOpt);
        sortCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var events = mgr.GetModifier<EventModifier>()!;
            bool byRound = parseResult.GetValue(sortRoundOpt);
            WriteEventResult(RecordEventListChange(mgr, mapData, byRound ? "事件按触发回合排序" : "事件按序号排序",
                () => byRound ? events.SortEventsByTriggerRound() : events.SortEventsBySequence()));
        }));

        var checkCmd = new Command("check", "检查事件引用：缺失的军团 / 关联事件、地图外的进攻目标格、建筑与单位指向不存在的事件、重复序号");
        checkCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var issues = EventReferenceChecker.Check(mapData);
            if (issues.Count == 0)
            {
                _output.WriteLine("[事件] 引用检查全部通过");
                return;
            }
            _output.WriteLine($"[事件] 发现 {issues.Count} 处问题：");
            foreach (var issue in issues)
                _output.WriteLine($"  [{issue.Level}] {issue.Message}");
        }, readOnly: true));

        var fieldsCmd = new Command("fields", "列出事件字段名与取值范围");
        fieldsCmd.SetAction(_ =>
        {
            foreach (var field in MapEventFields.All)
                _output.WriteLine($"  {field.Name,-17} {field.Format(unchecked((int)field.Min))} ~ {field.Format(unchecked((int)field.Max)),-11} {field.Label}：{field.Tooltip}");
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(duplicateCmd);
        cmd.Subcommands.Add(renumberCmd);
        cmd.Subcommands.Add(sortCmd);
        cmd.Subcommands.Add(checkCmd);
        cmd.Subcommands.Add(fieldsCmd);
        return cmd;
    }

    private static Argument<int> EventTargetArgument()
        => new("event") { Description = "事件序号（加 -i 时为列表下标）" };

    private static Option<bool> EventIndexOption()
        => new("--index", "-i") { Description = "把事件参数当作列表下标（event list 第一列），用于序号重复的事件" };

    /// <summary>把命令里的事件参数换成列表下标；找不到时输出原因并返回 -1。序号重复时取第一个并提示改用 -i。</summary>
    private int ResolveEventIndex(EventModifier events, MapData mapData, int target, bool byIndex)
    {
        if (byIndex)
        {
            if (target >= 0 && target < mapData.Events.Count) return target;
            _output.WriteLine($"失败：列表下标 {target} 超出范围（共 {mapData.Events.Count} 个事件）");
            return -1;
        }

        int index = events.GetEventIndex(target);
        if (index < 0)
        {
            _output.WriteLine($"失败：没有序号为 {target} 的事件");
            return -1;
        }
        int count = events.CountBySequence(target);
        if (count > 1)
            _output.WriteLine($"注意：序号 {target} 有 {count} 个事件，操作的是列表下标 {index} 的那一个（其余用 -i 指定）");
        return index;
    }

    /// <summary>事件列表修改的撤销入口（快照 MapData.Events）；脚本批量执行时不记撤销。</summary>
    private ModifierResult RecordEventListChange(EditModeManager mgr, MapData mapData, string description, Func<ModifierResult> apply)
    {
        ModifierResult result = default;
        if (_batchMode) result = apply();
        else mgr.RecordCollectionChange(description, mapData.Events, () => result = apply());
        return result;
    }

    /// <summary>同时改到事件与建筑 / 单位时的撤销入口（全实体快照）；脚本批量执行时不记撤销。</summary>
    private void RecordEventEntitiesChange(EditModeManager mgr, string description, Action apply)
    {
        if (_batchMode) apply();
        else mgr.RecordEntitiesSnapshot(description, apply);
    }

    private void WriteEventResult(ModifierResult result)
        => _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");
}
