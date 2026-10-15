using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.Core.Parsers.ArmyGroupSetting;

/// <summary>
/// 集团军模式解析器（五表联动：ArmyGroupSettings + ArmyGroupReinforcementSettings +
/// EventSettings + EventStageSettings + EventCalendarSettings，外加 stringtable 的 event_{EventId}）。
/// 采用与 CountryTechSettingParser 相同的单例与路径解析约定。
/// </summary>
public class ArmyGroupSettingParser
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

    private readonly AssetManager _manager = AssetManager.Default;
    private List<ArmyGroupSettingData> _armyGroups = new();
    private List<ArmyGroupReinforcementData> _reinforcements = new();
    private List<EventSettingData> _events = new();
    private List<EventStageSettingData> _stages = new();
    private List<EventCalendarSettingData> _calendars = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<ArmyGroupSettingData> All => _armyGroups;
    public List<ArmyGroupSettingData> Items => _armyGroups;
    public List<ArmyGroupReinforcementData> Reinforcements => _reinforcements;
    public List<EventSettingData> Events => _events;
    public List<EventStageSettingData> Stages => _stages;
    public List<EventCalendarSettingData> Calendars => _calendars;

    public string ConfigPath { get; private set; } = "";
    public string ReinforcementPath { get; private set; } = "";
    public string EventPath { get; private set; } = "";
    public string EventStagePath { get; private set; } = "";
    public string EventCalendarPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new SafeInt32Converter(), new SafeIntListConverter() }
    };

    private ArmyGroupSettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        if (!_manager.IsLoaded)
        {
            try { _manager.ScanDefault(); } catch { }
        }

        var root = _manager.AssetsRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            var baseDir = AppContext.BaseDirectory;
            string? candidate = null;
            string? current = baseDir;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
            {
                var test = Path.Combine(current, "Resource", "WC4DATA", "assets");
                if (Directory.Exists(test)) { candidate = test; break; }
                current = Path.GetDirectoryName(current);
            }
            candidate ??= Path.GetFullPath(Path.Combine(baseDir ?? "", "..", "..", "..", "..", "Resource", "WC4DATA", "assets"));
            root = candidate;
        }

        ConfigPath = Path.Combine(root, "json", "ArmyGroupSettings.json");
        ReinforcementPath = Path.Combine(root, "json", "ArmyGroupReinforcementSettings.json");
        EventPath = Path.Combine(root, "json", "EventSettings.json");
        EventStagePath = Path.Combine(root, "json", "EventStageSettings.json");
        EventCalendarPath = Path.Combine(root, "json", "EventCalendarSettings.json");
        StringTablePath = Path.Combine(root, "stringtable_tw.ini");
        Debug.WriteLine($"[ArmyGroupSettingParser] ConfigPath={ConfigPath}");
    }

    public void LoadAll()
    {
        _armyGroups = LoadTable<ArmyGroupSettingData>(ConfigPath, _armyGroups);
        _reinforcements = LoadTable<ArmyGroupReinforcementData>(ReinforcementPath, _reinforcements);
        _events = LoadTable<EventSettingData>(EventPath, _events);
        _stages = LoadTable<EventStageSettingData>(EventStagePath, _stages);
        _calendars = LoadTable<EventCalendarSettingData>(EventCalendarPath, _calendars);
        _stringTable = new StringTableParser(StringTablePath);
    }

    private List<T> LoadTable<T>(string path, List<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[ArmyGroupSettingParser] 文件不存在: {path}");
                return new List<T>();
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyGroupSettingParser] 加载失败 {path}: {ex.Message}");
            return fallback;
        }
    }

    // ---------- 查询 ----------

    public ArmyGroupSettingData? GetById(int id)
    {
        foreach (var a in _armyGroups) if (a.Id == id) return a;
        return null;
    }

    public List<ArmyGroupReinforcementData> GetReinforcements(int armyGroupId)
    {
        var list = new List<ArmyGroupReinforcementData>();
        foreach (var r in _reinforcements) if (r.ArmyGroupId == armyGroupId) list.Add(r);
        return list;
    }

    public EventSettingData? GetEventByEventId(int eventId)
    {
        foreach (var e in _events) if (e.Id == eventId) return e;
        return null;
    }

    public EventStageSettingData? GetStageByEventId(int eventId)
    {
        foreach (var s in _stages) if (s.EventId == eventId) return s;
        return null;
    }

    public EventCalendarSettingData? GetCalendarByEventId(int eventId)
    {
        foreach (var c in _calendars) if (c.EventId == eventId) return c;
        return null;
    }

    // ---------- 文本 ----------

    public string GetEventDesc(int eventId) => _stringTable?.GetValue($"event_{eventId}", "") ?? "";
    public void SetEventDesc(int eventId, string desc) => _stringTable?.SetValue($"event_{eventId}", desc);

    // ---------- 主键分配 ----------

    private static int NextId<T>(List<T> list, Func<T, int> getId, int floor)
    {
        int max = floor;
        foreach (var item in list)
        {
            int id = getId(item);
            if (id > max) max = id;
        }
        return max + 1;
    }

    public int GetNextArmyGroupId() => NextId(_armyGroups, x => x.Id, 100);
    public int GetNextEventId() => NextId(_events, x => x.Id, 800);
    public int GetNextStageId() => NextId(_stages, x => x.Id, 0);
    public int GetNextCalendarId() => NextId(_calendars, x => x.Id, 0);
    public int GetNextReinforcementId() => NextId(_reinforcements, x => x.Id, 90000);

    public bool SaveAll()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_armyGroups, JsonOpts));
            File.WriteAllText(ReinforcementPath, JsonSerializer.Serialize(_reinforcements, JsonOpts));
            File.WriteAllText(EventPath, JsonSerializer.Serialize(_events, JsonOpts));
            File.WriteAllText(EventStagePath, JsonSerializer.Serialize(_stages, JsonOpts));
            File.WriteAllText(EventCalendarPath, JsonSerializer.Serialize(_calendars, JsonOpts));
            _stringTable?.Save();
            Debug.WriteLine("[ArmyGroupSettingParser] 五表保存成功");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyGroupSettingParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    public void Reload() => LoadAll();

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
    [JsonPropertyName("Photo")] public string Photo { get; set; } = "";
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
