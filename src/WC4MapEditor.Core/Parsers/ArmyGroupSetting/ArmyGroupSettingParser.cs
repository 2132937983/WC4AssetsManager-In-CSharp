using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.ArmyGroupSetting;

/// <summary>
/// 集团军模式解析器（五表联动：ArmyGroupSettings + ArmyGroupReinforcementSettings +
/// EventSettings + EventStageSettings + EventCalendarSettings，外加 stringtable 的 event_{EventId}）。
/// 五张表一起加载、一起保存；任意一张加载失败时整组禁止保存，避免联动数据只写了一半。
/// </summary>
public sealed class ArmyGroupSettingParser : IGameSettingsSource
{
    private static readonly object _lock = new();
    private static ArmyGroupSettingParser? _instance;
    public static ArmyGroupSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ArmyGroupSettingParser();
                }
            }
            return _instance;
        }
    }

    private ArmyGroupSettingParser()
    {
        GroupTable = new GameJsonTable<ArmyGroupSettingData>(GameJson.AssetPath("json", "ArmyGroupSettings.json"));
        ReinforcementTable = new GameJsonTable<ArmyGroupReinforcementData>(GameJson.AssetPath("json", "ArmyGroupReinforcementSettings.json"));
        EventTable = new GameJsonTable<EventSettingData>(GameJson.AssetPath("json", "EventSettings.json"));
        StageTable = new GameJsonTable<EventStageSettingData>(GameJson.AssetPath("json", "EventStageSettings.json"));
        CalendarTable = new GameJsonTable<EventCalendarSettingData>(GameJson.AssetPath("json", "EventCalendarSettings.json"));
        Strings = StringTableStore.Default;
        EventTexts = new RecordTextKeys(Strings, this, "event_{0}");
        LoadAll();
    }

    public GameJsonTable<ArmyGroupSettingData> GroupTable { get; }
    public GameJsonTable<ArmyGroupReinforcementData> ReinforcementTable { get; }
    public GameJsonTable<EventSettingData> EventTable { get; }
    public GameJsonTable<EventStageSettingData> StageTable { get; }
    public GameJsonTable<EventCalendarSettingData> CalendarTable { get; }
    public StringTableParser Strings { get; }

    /// <summary>活动（事件组）的文本键，按 EventId：0 = 介绍。多个集团军可能共用同一个活动。</summary>
    public RecordTextKeys EventTexts { get; }

    public IReadOnlyList<IEditableTable> Tables => [GroupTable, ReinforcementTable, EventTable, StageTable, CalendarTable];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<ArmyGroupSettingData> Items => GroupTable.Items;
    public List<ArmyGroupReinforcementData> Reinforcements => ReinforcementTable.Items;
    public List<EventSettingData> Events => EventTable.Items;
    public List<EventStageSettingData> Stages => StageTable.Items;
    public List<EventCalendarSettingData> Calendars => CalendarTable.Items;

    public string ConfigPath => GroupTable.FilePath;
    public string ReinforcementPath => ReinforcementTable.FilePath;
    public string EventPath => EventTable.FilePath;
    public string EventStagePath => StageTable.FilePath;
    public string EventCalendarPath => CalendarTable.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll()
    {
        foreach (var table in Tables) table.Reload();
    }

    // ---------- 查询 ----------

    public ArmyGroupSettingData? GetById(int id) => Items.FirstOrDefault(a => a.Id == id);

    public List<ArmyGroupReinforcementData> GetReinforcements(int armyGroupId) => Reinforcements.Where(r => r.ArmyGroupId == armyGroupId).ToList();

    public EventSettingData? GetEventByEventId(int eventId) => Events.FirstOrDefault(e => e.Id == eventId);

    public EventStageSettingData? GetStageByEventId(int eventId) => Stages.FirstOrDefault(s => s.EventId == eventId);

    public EventCalendarSettingData? GetCalendarByEventId(int eventId) => Calendars.FirstOrDefault(c => c.EventId == eventId);

    /// <summary>没有任何援军的集团军（游戏会闪退），保存前必须拦下。</summary>
    public List<int> GetGroupsWithoutReinforcement()
    {
        var withRe = Reinforcements.Select(r => r.ArmyGroupId).ToHashSet();
        return Items.Where(a => !withRe.Contains(a.Id)).Select(a => a.Id).ToList();
    }

    // ---------- 文本 ----------

    public string GetEventDesc(int eventId) => EventTexts.Get(0, eventId);
    public void SetEventDesc(int eventId, string desc) => EventTexts.Set(0, eventId, desc);

    // ---------- 主键分配 ----------

    private static int NextId<T>(List<T> list, Func<T, int> getId, int floor)
        => Math.Max(floor, list.Count == 0 ? 0 : list.Max(getId)) + 1;

    public int GetNextArmyGroupId() => NextId(Items, x => x.Id, 100);
    public int GetNextEventId() => NextId(Events, x => x.Id, 800);
    public int GetNextStageId() => NextId(Stages, x => x.Id, 0);
    public int GetNextCalendarId() => NextId(Calendars, x => x.Id, 0);
    public int GetNextReinforcementId() => NextId(Reinforcements, x => x.Id, 90000);

    // ---------- 记录操作 ----------

    /// <summary>删除集团军及其专属援军（活动数据可能被别的集团军共用，不删除）。</summary>
    public void RemoveGroup(ArmyGroupSettingData group)
    {
        Items.Remove(group);
        Reinforcements.RemoveAll(r => r.ArmyGroupId == group.Id);
    }

    /// <summary>改集团军 Id：新 Id 已被占用时返回 false；专属援军的 ArmyGroupId 跟着改。</summary>
    public bool ChangeGroupId(ArmyGroupSettingData group, int newId)
    {
        if (group.Id == newId) return true;
        if (Items.Any(a => a.Id == newId)) return false;
        foreach (var r in Reinforcements.Where(r => r.ArmyGroupId == group.Id)) r.ArmyGroupId = newId;
        group.Id = newId;
        return true;
    }

    /// <summary>复制集团军时复制它的援军（新援军 Id，归属新集团军），副本保存时不会因缺少援军被拦截。</summary>
    public void CopyReinforcements(int fromGroupId, int toGroupId)
    {
        foreach (var source in GetReinforcements(fromGroupId))
        {
            var copy = ReinforcementTable.Clone(source);
            copy.Id = GetNextReinforcementId();
            copy.ArmyGroupId = toGroupId;
            Reinforcements.Add(copy);
        }
    }

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>集团军（ArmyGroupSettings.json）。</summary>
public class ArmyGroupSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("EventId")] public int EventId { get; set; }
    [JsonPropertyName("StageId")] public int StageId { get; set; }
    [JsonPropertyName("Seat")] public int Seat { get; set; }
    [JsonPropertyName("Star")] public int Star { get; set; }
    [JsonPropertyName("Camp")] public int Camp { get; set; }
    [JsonPropertyName("Difficulty")] public int Difficulty { get; set; }
    [JsonPropertyName("MeritPoint")] public int MeritPoint { get; set; }
    [JsonPropertyName("TechAcquire")] public int TechAcquire { get; set; }
    [JsonPropertyName("CountryId")] public int CountryId { get; set; }
    [JsonPropertyName("GroupId")] public int GroupId { get; set; }
    [JsonPropertyName("PrizeGold")] public int PrizeGold { get; set; }
    [JsonPropertyName("PrizeIndustry")] public int PrizeIndustry { get; set; }
    [JsonPropertyName("PrizeEnergy")] public int PrizeEnergy { get; set; }
    [JsonPropertyName("PrizeTech")] public int PrizeTech { get; set; }
    [JsonPropertyName("Photo")] public string? Photo { get; set; } = "";
    [JsonPropertyName("TechCategoryIds")] public List<int> TechCategoryIds { get; set; } = new();
    [JsonPropertyName("CloseTechTypes")] public List<int> CloseTechTypes { get; set; } = new();
    [JsonPropertyName("CloseCardTypes")] public List<int> CloseCardTypes { get; set; } = new();
    [JsonPropertyName("GeneralId")] public List<int> GeneralId { get; set; } = new();
    [JsonPropertyName("ShortName")] public string ShortName { get; set; } = "";
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("Coefficient")] public int Coefficient { get; set; }
    [JsonPropertyName("CityNum")] public int CityNum { get; set; }
}

/// <summary>集团军专属援军（ArmyGroupReinforcementSettings.json）。</summary>
public class ArmyGroupReinforcementData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("MapId")] public int MapId { get; set; }
    [JsonPropertyName("ArmyId")] public int ArmyId { get; set; }
    [JsonPropertyName("ArmyGroupId")] public int ArmyGroupId { get; set; }
    [JsonPropertyName("CostPoint")] public int CostPoint { get; set; }
    [JsonPropertyName("Num")] public int Num { get; set; }
}

/// <summary>活动（EventSettings.json）。</summary>
public class EventSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("NormalId")] public int NormalId { get; set; }
    [JsonPropertyName("NeedHQLv")] public int NeedHQLv { get; set; }
    [JsonPropertyName("Points")] public List<int> Points { get; set; } = new();
    [JsonPropertyName("GeneralId")] public int GeneralId { get; set; }
    [JsonPropertyName("DifficultStar")] public int DifficultStar { get; set; }
}

/// <summary>活动关卡（EventStageSettings.json）。RoundPhoto 允许为 null。</summary>
public class EventStageSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("EventId")] public int EventId { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("CountryId")] public int CountryId { get; set; }
    [JsonPropertyName("UnlockStageId")] public int UnlockStageId { get; set; }
    [JsonPropertyName("RefStageId")] public int RefStageId { get; set; }
    [JsonPropertyName("Difficulty")] public int Difficulty { get; set; }
    [JsonPropertyName("Seat")] public int Seat { get; set; }
    [JsonPropertyName("GeneralID")] public int GeneralID { get; set; }
    [JsonPropertyName("X")] public int X { get; set; }
    [JsonPropertyName("Y")] public int Y { get; set; }
    [JsonPropertyName("PrizeMedals")] public int PrizeMedals { get; set; }
    [JsonPropertyName("PrizeTicket")] public int PrizeTicket { get; set; }
    [JsonPropertyName("PrizeItem")] public List<int> PrizeItem { get; set; } = new();
    [JsonPropertyName("PrizeGold")] public List<int> PrizeGold { get; set; } = new();
    [JsonPropertyName("PrizeIndustry")] public List<int> PrizeIndustry { get; set; } = new();
    [JsonPropertyName("PrizeEnergy")] public List<int> PrizeEnergy { get; set; } = new();
    [JsonPropertyName("PrizeTech")] public List<int> PrizeTech { get; set; } = new();
    [JsonPropertyName("PrizeMerit")] public List<int> PrizeMerit { get; set; } = new();
    [JsonPropertyName("EnableRoundEvent")] public int EnableRoundEvent { get; set; }
    [JsonPropertyName("RoundPhoto")] public string? RoundPhoto { get; set; }
}

/// <summary>活动日历（EventCalendarSettings.json）。</summary>
public class EventCalendarSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("EventId")] public int EventId { get; set; }
    [JsonPropertyName("StartDate")] public List<int> StartDate { get; set; } = new();
    [JsonPropertyName("LastingDays")] public int LastingDays { get; set; }
    [JsonPropertyName("NoticeDays")] public int NoticeDays { get; set; }
    [JsonPropertyName("PeriodDays")] public int PeriodDays { get; set; }
    [JsonPropertyName("PrizeIds")] public List<int> PrizeIds { get; set; } = new();
    [JsonPropertyName("HardBuffs")] public List<int> HardBuffs { get; set; } = new();
    [JsonPropertyName("MyBuffPool")] public List<int> MyBuffPool { get; set; } = new();
    [JsonPropertyName("HardPrizeIds")] public List<int> HardPrizeIds { get; set; } = new();
    [JsonPropertyName("NeedHQLv")] public int NeedHQLv { get; set; }
    [JsonPropertyName("Version")] public int Version { get; set; }
}
