using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.BuildingSetting;

/// <summary>
/// 建筑与设施解析器（双表合一：BuildingSettings.json + FacilitySettings.json）。
/// 两张表的名称都直接保存在 JSON 中，不需要 stringtable。
/// </summary>
public sealed class BuildingFacilitySettingParser : IGameSettingsSource
{
    private static readonly object _lock = new();
    private static BuildingFacilitySettingParser? _instance;
    public static BuildingFacilitySettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new BuildingFacilitySettingParser();
                }
            }
            return _instance;
        }
    }

    private BuildingFacilitySettingParser()
    {
        BuildingTable = new GameJsonTable<BuildingSettingData>(GameJson.AssetPath("json", "BuildingSettings.json"));
        FacilityTable = new GameJsonTable<FacilitySettingData>(GameJson.AssetPath("json", "FacilitySettings.json"));
        LoadAll();
    }

    public GameJsonTable<BuildingSettingData> BuildingTable { get; }
    public GameJsonTable<FacilitySettingData> FacilityTable { get; }

    public IReadOnlyList<IEditableTable> Tables => [BuildingTable, FacilityTable];
    StringTableParser? IGameSettingsSource.Strings => null;

    public List<BuildingSettingData> BuildingItems => BuildingTable.Items;
    public List<FacilitySettingData> FacilityItems => FacilityTable.Items;

    public string BuildingConfigPath => BuildingTable.FilePath;
    public string FacilityConfigPath => FacilityTable.FilePath;

    public void LoadAll()
    {
        BuildingTable.Load();
        FacilityTable.Load();
    }

    public BuildingSettingData? GetBuildingById(int id) => BuildingItems.FirstOrDefault(b => b.Id == id);

    public int GetNextBuildingId() => (BuildingItems.Count == 0 ? 0 : Math.Max(0, BuildingItems.Max(b => b.Id))) + 1;

    public FacilitySettingData? GetFacilityById(int id) => FacilityItems.FirstOrDefault(f => f.Id == id);

    /// <summary>设施 Type → stringtable 的设施名称键（与 FacilitySettings.json 里各 Type 的设施对应：1 城市 … 8 军港）。</summary>
    private static readonly string[] FacilityTypeKeys =
    [
        "facility_city", "facility_factory", "facility_lab", "facility_depot",
        "facility_airport", "facility_missile", "facility_nuke", "facility_port"
    ];

    /// <summary>设施 Type 的游戏内名称（stringtable 的 facility_*）；Type 不在 1~8 或没有文本时返回空串。</summary>
    public string GetFacilityTypeName(int type)
        => type >= 1 && type <= FacilityTypeKeys.Length ? StringTableStore.Default.GetValue(FacilityTypeKeys[type - 1]) : "";

    /// <summary>所有设施 Type 的 (Type, 名称)，没有文本的 Type 不列出。</summary>
    public IEnumerable<(int Type, string Name)> FacilityTypeNames()
        => Enumerable.Range(1, FacilityTypeKeys.Length).Select(t => (t, GetFacilityTypeName(t))).Where(x => x.Item2.Length > 0);

    public int GetNextFacilityId() => (FacilityItems.Count == 0 ? 0 : Math.Max(0, FacilityItems.Max(f => f.Id))) + 1;

    /// <summary>改建筑 Id：新 Id 已被占用时返回 false。</summary>
    public bool ChangeBuildingId(BuildingSettingData building, int newId)
    {
        if (building.Id == newId) return true;
        if (BuildingItems.Any(b => b.Id == newId)) return false;
        building.Id = newId;
        return true;
    }

    /// <summary>改设施 Id：新 Id 已被占用时返回 false；引用它的建筑 FacilityId 跟着改。</summary>
    public bool ChangeFacilityId(FacilitySettingData facility, int newId)
    {
        if (facility.Id == newId) return true;
        if (FacilityItems.Any(f => f.Id == newId)) return false;
        foreach (var b in BuildingItems.Where(b => b.FacilityId == facility.Id)) b.FacilityId = newId;
        facility.Id = newId;
        return true;
    }

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>建筑节点（BuildingSettings.json）。ResName 允许为 null。</summary>
public class BuildingSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("ProduceMoney")] public int ProduceMoney { get; set; }
    [JsonPropertyName("ProduceGear")] public int ProduceGear { get; set; }
    [JsonPropertyName("ProduceAtomic")] public int ProduceAtomic { get; set; }
    [JsonPropertyName("ArmyId")] public int ArmyId { get; set; }
    [JsonPropertyName("FacilityId")] public int FacilityId { get; set; }
    [JsonPropertyName("ResName")] public string? ResName { get; set; }
    [JsonPropertyName("StyleCount")] public int StyleCount { get; set; }
    [JsonPropertyName("MeritExp")] public int MeritExp { get; set; }
}

/// <summary>设施节点（FacilitySettings.json）。</summary>
public class FacilitySettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("UnlockArmy")] public List<int> UnlockArmy { get; set; } = new();
    [JsonPropertyName("UnlockArmyType")] public List<int> UnlockArmyType { get; set; } = new();
    [JsonPropertyName("CityRecovery")] public int CityRecovery { get; set; }
    [JsonPropertyName("ArmyRecovery")] public int ArmyRecovery { get; set; }
    [JsonPropertyName("ProduceMoney")] public int ProduceMoney { get; set; }
    [JsonPropertyName("ProduceGear")] public int ProduceGear { get; set; }
    [JsonPropertyName("ProduceAtomic")] public int ProduceAtomic { get; set; }
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("Only")] public int Only { get; set; }
}
