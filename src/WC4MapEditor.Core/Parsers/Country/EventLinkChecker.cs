using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.ArmySetting;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>引用检查结果：断开的引用、没有被任何事件使用的事件 Buff，以及各表的条数。</summary>
public sealed class EventLinkReport
{
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>没有被集团军事件或征服事件引用的事件 Buff Id。</summary>
    public required IReadOnlyList<int> UnusedBuffs { get; init; }

    public required int ArmyGroupEvents { get; init; }
    public required int ConquerEvents { get; init; }
    public required int EventBuffs { get; init; }
}

/// <summary>
/// 事件引用检查：集团军事件 / 征服事件 → 事件 Buff → 军队 Buff，事件 → 国家，事件 → 事件（连带触发、禁用事件）。
/// 检查的是内存里当前的数据（包括没有保存的修改）；游戏读到断开的引用会闪退或让效果失效。
/// </summary>
public static class EventLinkChecker
{
    /// <summary>Trigger 5 是连带 / 延迟事件：TriggerValue[0] 是前置事件 Id。</summary>
    private const int ChainTrigger = 5;

    public static EventLinkReport Check()
    {
        var armyGroupEvents = ArmyGroupEventSettingParser.Instance.Items;
        var conquerEvents = ConquerEventSettingParser.Instance.Items;
        var buffs = EventBuffSettingParser.Instance.Items;
        var buffIds = buffs.Select(b => b.Id).ToHashSet();
        var countryIds = CountrySettingParser.Instance.Countries.Select(c => c.Id).ToHashSet();
        var armyBuffIds = ArmyBuffSettingParser.Instance.Items.Select(b => b.Id).ToHashSet();
        var problems = new List<string>();
        var usedBuffs = new HashSet<int>();

        void CheckEvent(string table, int id, int trigger, List<int> triggerValue, (int Buff, List<int> Countries)[] slots, HashSet<int> eventIds)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var (buff, countries) = slots[i];
                if (buff != 0)
                {
                    usedBuffs.Add(buff);
                    if (!buffIds.Contains(buff)) problems.Add(Loc.F("EventLink.Problem.Buff", table, id, $"EventBuffId{i + 1}", buff));
                }
                foreach (var country in countries.Where(c => !countryIds.Contains(c)))
                    problems.Add(Loc.F("EventLink.Problem.Country", table, id, $"CountryId{i + 1}", country));
            }
            if (trigger == ChainTrigger && triggerValue.Count > 0 && !eventIds.Contains(triggerValue[0]))
                problems.Add(Loc.F("EventLink.Problem.ChainEvent", table, id, triggerValue[0]));
        }

        string armyGroupTable = Loc.T("EventLink.Table.ArmyGroupEvent");
        var armyGroupIds = armyGroupEvents.Select(e => e.Id).ToHashSet();
        foreach (var e in armyGroupEvents)
        {
            CheckEvent(armyGroupTable, e.Id, e.Trigger, e.TriggerValue, [(e.EventBuffId1, e.CountryId1), (e.EventBuffId2, e.CountryId2)], armyGroupIds);
            foreach (var country in e.PlayerCountry.Where(c => !countryIds.Contains(c)))
                problems.Add(Loc.F("EventLink.Problem.Country", armyGroupTable, e.Id, "PlayerCountry", country));
            foreach (var disabled in e.DisableEvents.Where(d => !armyGroupIds.Contains(d)))
                problems.Add(Loc.F("EventLink.Problem.DisableEvent", armyGroupTable, e.Id, disabled));
        }

        string conquerTable = Loc.T("EventLink.Table.ConquerEvent");
        var conquerIds = conquerEvents.Select(e => e.Id).ToHashSet();
        foreach (var e in conquerEvents)
            CheckEvent(conquerTable, e.Id, e.Trigger, e.TriggerValue, [(e.EventBuffId1, e.CountryId1), (e.EventBuffId2, e.CountryId2)], conquerIds);

        foreach (var buff in buffs)
        {
            foreach (var armyBuff in buff.ArmyBuffs.Where(a => !armyBuffIds.Contains(a)))
                problems.Add(Loc.F("EventLink.Problem.ArmyBuff", buff.Id, armyBuff));
        }

        return new EventLinkReport
        {
            Problems = problems,
            UnusedBuffs = buffs.Select(b => b.Id).Where(id => !usedBuffs.Contains(id)).OrderBy(id => id).ToList(),
            ArmyGroupEvents = armyGroupEvents.Count,
            ConquerEvents = conquerEvents.Count,
            EventBuffs = buffs.Count
        };
    }
}
