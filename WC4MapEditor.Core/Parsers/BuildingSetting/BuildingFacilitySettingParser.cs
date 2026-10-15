using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.Core.Parsers.BuildingSetting;

/// <summary>
/// 建筑与设施解析器（双表合一：BuildingSettings.json + FacilitySettings.json）。
/// 采用与 CountryTechSettingParser 相同的单例与路径解析约定。
/// 两张表的名称都直接保存在 JSON 中，不需要 stringtable。
/// </summary>
public class BuildingFacilitySettingParser
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

    private readonly AssetManager _manager = AssetManager.Default;
    private List<BuildingSettingData> _buildings = new();
    private List<FacilitySettingData> _facilities = new();

    public IReadOnlyList<BuildingSettingData> Buildings => _buildings;
    public List<BuildingSettingData> BuildingItems => _buildings;
    public IReadOnlyList<FacilitySettingData> Facilities => _facilities;
    public List<FacilitySettingData> FacilityItems => _facilities;

    public string BuildingConfigPath { get; private set; } = "";
    public string FacilityConfigPath { get; private set; } = "";

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

    private BuildingFacilitySettingParser()
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

        BuildingConfigPath = Path.Combine(root, "json", "BuildingSettings.json");
        FacilityConfigPath = Path.Combine(root, "json", "FacilitySettings.json");
        Debug.WriteLine($"[BuildingFacilitySettingParser] Building={BuildingConfigPath} Facility={FacilityConfigPath}");
    }

    public void LoadAll()
    {
        LoadBuildings();
        LoadFacilities();
    }

    private void LoadBuildings()
    {
        try
        {
            if (!File.Exists(BuildingConfigPath))
            {
                Debug.WriteLine($"[BuildingFacilitySettingParser] 文件不存在: {BuildingConfigPath}");
                return;
            }
            var json = File.ReadAllText(BuildingConfigPath);
            var data = JsonSerializer.Deserialize<List<BuildingSettingData>>(json, JsonOpts);
            if (data != null) _buildings = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingFacilitySettingParser] 加载 BuildingSettings.json 失败: {ex.Message}");
        }
    }

    private void LoadFacilities()
    {
        try
        {
            if (!File.Exists(FacilityConfigPath))
            {
                Debug.WriteLine($"[BuildingFacilitySettingParser] 文件不存在: {FacilityConfigPath}");
                return;
            }
            var json = File.ReadAllText(FacilityConfigPath);
            var data = JsonSerializer.Deserialize<List<FacilitySettingData>>(json, JsonOpts);
            if (data != null) _facilities = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingFacilitySettingParser] 加载 FacilitySettings.json 失败: {ex.Message}");
        }
    }

    // ---------- 建筑 ----------

    public BuildingSettingData? GetBuildingById(int id)
    {
        foreach (var b in _buildings) if (b.Id == id) return b;
        return null;
    }

    public int GetNextBuildingId()
    {
        int max = 0;
        foreach (var b in _buildings) if (b.Id > max) max = b.Id;
        return max + 1;
    }

    // ---------- 设施 ----------

    public FacilitySettingData? GetFacilityById(int id)
    {
        foreach (var f in _facilities) if (f.Id == id) return f;
        return null;
    }

    public int GetNextFacilityId()
    {
        int max = 0;
        foreach (var f in _facilities) if (f.Id > max) max = f.Id;
        return max + 1;
    }

    public bool SaveAll()
    {
        try
        {
            File.WriteAllText(BuildingConfigPath, JsonSerializer.Serialize(_buildings, JsonOpts));
            File.WriteAllText(FacilityConfigPath, JsonSerializer.Serialize(_facilities, JsonOpts));
            Debug.WriteLine($"[BuildingFacilitySettingParser] 保存成功: {BuildingConfigPath} / {FacilityConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingFacilitySettingParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    public void Reload() => LoadAll();

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
