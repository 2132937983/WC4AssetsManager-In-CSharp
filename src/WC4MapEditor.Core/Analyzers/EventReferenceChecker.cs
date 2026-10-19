using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>事件引用问题的种类。</summary>
public enum EventIssueKind
{
    /// <summary>多个事件使用同一序号（按序号的引用有歧义）</summary>
    DuplicateSequence,

    /// <summary>触发军团的行动序号没有对应的军团</summary>
    MissingTriggerLegion,

    /// <summary>受影响军团的行动序号没有对应的军团</summary>
    MissingAffectedLegion,

    /// <summary>关联事件（低 16 位）指向不存在的序号</summary>
    MissingRelatedEvent,

    /// <summary>建筑的占领事件指向不存在的序号</summary>
    BuildingMissingEvent,

    /// <summary>单位（v1/v2）的占领事件指向不存在的序号</summary>
    ArmyMissingEvent,

    /// <summary>占地触发的事件没有任何建筑的占领事件指向它（提示）</summary>
    UnreferencedOccupationEvent,

    /// <summary>"军团向一个方向进攻"的目标格（阵营变换字段里的格子下标）在地图外</summary>
    TargetOutsideMap,

    /// <summary>结束段不是原版固定的 00 CC CC CC：多半是事件块读错了位置，其余字段也不可信</summary>
    UnexpectedEndSegment,
}

/// <summary>
/// 一条事件引用问题。
/// </summary>
/// <param name="Kind">种类</param>
/// <param name="Level">严重程度（缺失引用 / 重复序号为警告，无人引用为提示）</param>
/// <param name="EventIndex">相关事件的列表下标；重复序号与建筑 / 单位类问题为 -1</param>
/// <param name="Sequence">相关事件的序号（建筑 / 单位类问题为它们指向的序号）</param>
/// <param name="Value">出问题的字段值（军团行动序号、关联序号、重复次数……）</param>
/// <param name="Cell">相关建筑 / 单位所在格（其余为 null）</param>
/// <param name="Message">可直接显示的说明</param>
public sealed record EventReferenceIssue(
    EventIssueKind Kind,
    BTLIssueLevel Level,
    int EventIndex,
    int Sequence,
    int Value,
    HexCoord? Cell,
    string Message);

/// <summary>
/// 事件引用检查（VB 版没有）：事件指向的军团、关联事件、进攻目标格是否存在，建筑与单位的占领事件是否指向存在的事件，序号是否重复。
/// <para>
/// 判定口径按原版关卡核对过：两个军团字段存的是军团的行动序号（<see cref="Legion.ActionId"/>），0 表示不指定；
/// 关联事件取低 16 位作序号（高位是附加参数）；建筑与 v1/v2 单位的占领事件（1 字节）按序号引用事件，0 表示无；
/// "军团向一个方向进攻"的阵营变换字段是目标格下标。
/// 在事件块位置正确的 586 张原版关卡上，军团字段全部能对上行动序号，建筑的占领事件全部能对上事件序号，
/// 进攻目标格全部在地图内，所以这里报出来的基本都是真问题（例如删了军团或改了序号之后留下的悬空引用）。
/// </para>
/// <para>
/// 原版关卡每个事件的结束段都是 00 CC CC CC。读出别的值说明事件块的起点算错了（目前相当一部分带事件的原版关卡如此，
/// 偏差是 16 字节的整数倍，出在事件块之前的数据块上），这时整条事件都是错位的数据，单独报出来，免得把它当真问题去改。
/// </para>
/// </summary>
public static class EventReferenceChecker
{
    /// <summary>检查整张地图，按"重复序号 → 各事件 → 建筑 → 单位"的顺序返回问题。</summary>
    public static List<EventReferenceIssue> Check(MapData map)
    {
        var issues = new List<EventReferenceIssue>();
        var events = map.Events;
        var sequences = new HashSet<int>(events.Select(e => e.Sequence));
        var actionIds = new HashSet<int>(map.Legions.Select(l => l.ActionId));
        var buildingRefs = new HashSet<int>(map.Buildings.Select(b => (int)b.OccupationEvent));
        int tileCount = map.MapWidth * map.MapHeight;

        foreach (var group in events.GroupBy(e => e.Sequence).Where(g => g.Count() > 1).OrderBy(g => g.Key))
        {
            issues.Add(new EventReferenceIssue(EventIssueKind.DuplicateSequence, BTLIssueLevel.Warning, -1, group.Key, group.Count(),
                null, Loc.F("Module.Event.Check.DuplicateSequence", group.Key, group.Count())));
        }

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.EndSegment != MapEvent.DefaultEndSegment)
                issues.Add(new EventReferenceIssue(EventIssueKind.UnexpectedEndSegment, BTLIssueLevel.Warning, i, e.Sequence, e.EndSegment,
                    null, Loc.F("Module.Event.Check.UnexpectedEndSegment", e.Sequence, MapEventFields.EndSegment.Format(e.EndSegment))));
            if (e.TriggerLegion != 0 && !actionIds.Contains(e.TriggerLegion))
                issues.Add(new EventReferenceIssue(EventIssueKind.MissingTriggerLegion, BTLIssueLevel.Warning, i, e.Sequence, e.TriggerLegion,
                    null, Loc.F("Module.Event.Check.MissingTriggerLegion", e.Sequence, e.TriggerLegion)));
            if (e.AffectedLegion != 0 && !actionIds.Contains(e.AffectedLegion))
                issues.Add(new EventReferenceIssue(EventIssueKind.MissingAffectedLegion, BTLIssueLevel.Warning, i, e.Sequence, e.AffectedLegion,
                    null, Loc.F("Module.Event.Check.MissingAffectedLegion", e.Sequence, e.AffectedLegion)));
            if (e.RelatedSequence != 0 && !sequences.Contains(e.RelatedSequence))
                issues.Add(new EventReferenceIssue(EventIssueKind.MissingRelatedEvent, BTLIssueLevel.Warning, i, e.Sequence, e.RelatedSequence,
                    null, Loc.F("Module.Event.Check.MissingRelatedEvent", e.Sequence, e.RelatedSequence)));
            if (e.TriggerEvent == MapEvent.EffectAttackTowards && (e.CampChange < 0 || e.CampChange >= tileCount))
                issues.Add(new EventReferenceIssue(EventIssueKind.TargetOutsideMap, BTLIssueLevel.Warning, i, e.Sequence, e.CampChange,
                    null, Loc.F("Module.Event.Check.TargetOutsideMap", e.Sequence, e.CampChange, map.MapWidth, map.MapHeight)));
            if (e.TriggerCondition == MapEvent.ConditionOccupation && !buildingRefs.Contains(e.Sequence))
                issues.Add(new EventReferenceIssue(EventIssueKind.UnreferencedOccupationEvent, BTLIssueLevel.Info, i, e.Sequence, 0,
                    null, Loc.F("Module.Event.Check.UnreferencedOccupationEvent", e.Sequence)));
        }

        int width = Math.Max(1, map.MapWidth);
        foreach (var b in map.Buildings)
        {
            if (b.OccupationEvent == 0 || sequences.Contains(b.OccupationEvent)) continue;
            var cell = HexCoord.FromIndex(b.Coordinate, width);
            issues.Add(new EventReferenceIssue(EventIssueKind.BuildingMissingEvent, BTLIssueLevel.Warning, -1, b.OccupationEvent, b.OccupationEvent,
                cell, Loc.F("Module.Event.Check.BuildingMissingEvent", cell.Col, cell.Row, b.OccupationEvent)));
        }

        foreach (var a in map.Armies)
        {
            if (a.OccupationEvent == 0 || sequences.Contains(a.OccupationEvent)) continue;
            var cell = HexCoord.FromIndex(a.Coordinate, width);
            issues.Add(new EventReferenceIssue(EventIssueKind.ArmyMissingEvent, BTLIssueLevel.Warning, -1, a.OccupationEvent, a.OccupationEvent,
                cell, Loc.F("Module.Event.Check.ArmyMissingEvent", cell.Col, cell.Row, a.OccupationEvent)));
        }

        return issues;
    }

    /// <summary>与某个事件有关的问题：它自己的字段问题，加上它所在的重复序号组。</summary>
    public static IEnumerable<EventReferenceIssue> ForEvent(IEnumerable<EventReferenceIssue> issues, int eventIndex, int sequence)
        => issues.Where(i => i.EventIndex == eventIndex
                             || (i.Kind == EventIssueKind.DuplicateSequence && i.Sequence == sequence));
}
