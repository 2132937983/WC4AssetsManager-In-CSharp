using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Config;

public sealed class ConfigManager
{
    private static readonly object _lock = new();
    private static ConfigManager? _instance;

    public static event Action? StringTableChanged;

    public static ConfigManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConfigManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, string> _textConfig = new();
    private string _resourcePath = "";
    private bool _isInitialized;

    private MusicConfig? _musicConfig;
    private SettingTxtData? _settingData;
    private Dictionary<int, string> _terrainTypes = new();
    private Dictionary<string, string> _terrainMapping = new();
    private Dictionary<string, int> _terrainImageCounts = new();

    private List<CountryGeneralsConfig>? _generalInCountryData;
    private List<LegionLevelConfig>? _legionLvSettingData;
    private List<MaxFormationConfig>? _maxFormationData;
    private List<GeneralRandomTemplate>? _generalRandomTemplates;
    private List<GeneralSpecialtyTemplate>? _generalSpecialtyTemplates;
    private TacticalMapParser? _tacticalMapParser;
    private StringTableParser? _stringTableParser;

    private ConfigManager() { }

    public void Initialize()
    {
        if (_isInitialized) return;

        _resourcePath = DetectResourcePath();
        Debug.WriteLine($"[ConfigManager] Resource path: {_resourcePath}");

        LoadSettingFile();
        LoadMusicConfig();
        LoadConfigFiles();
        LoadDataFiles();
        LoadTerrainTypes();
        LoadTacticalMap();

        _isInitialized = true;
        Debug.WriteLine("[ConfigManager] Configuration initialized");
    }

    private string DetectResourcePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string localPath = IOPath.Combine(baseDir, "Resource");
        if (Directory.Exists(localPath)) return localPath;

        string parentPath = IOPath.GetFullPath(IOPath.Combine(baseDir, "..", "..", "..", "Resource"));
        if (Directory.Exists(parentPath)) return parentPath;

        return localPath;
    }

    private void LoadSettingFile()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = IOPath.Combine(baseDir, "setting.txt");
            if (!File.Exists(configPath))
                configPath = IOPath.GetFullPath(IOPath.Combine(baseDir, "..", "..", "..", "setting.txt"));

            if (!File.Exists(configPath))
            {
                Debug.WriteLine($"[ConfigManager] setting.txt not found: {configPath}");
                return;
            }

            var lines = File.ReadAllLines(configPath);
            _settingData = SettingTxtParser.Parse(lines);

            // 将解析后的文本配置合并到 ConfigManager 的字典中
            foreach (var kv in _settingData.TextConfig)
            {
                if (_textConfig.ContainsKey(kv.Key))
                    _textConfig[kv.Key] = kv.Value;
                else
                    _textConfig.Add(kv.Key, kv.Value);
            }

            Debug.WriteLine($"[ConfigManager] Loaded {_textConfig.Count} config entries from setting.txt");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] Failed to load setting.txt: {ex.Message}");
        }
    }

    private void LoadMusicConfig()
    {
        try
        {
            string path = IOPath.Combine(_resourcePath, "Music", "WindowMusic.json");
            if (File.Exists(path))
                _musicConfig = JsonSerializer.Deserialize<MusicConfig>(File.ReadAllText(path));
        }
        catch { }
    }

    private void LoadConfigFiles()
    {
        try
        {
            string configPath = IOPath.Combine(_resourcePath, "Config");
            if (!Directory.Exists(configPath))
            {
                Debug.WriteLine($"[ConfigManager] Config 目录不存在: {configPath}");
                return;
            }

            LoadGeneralInCountryConfig(configPath);
            LoadLegionLvSettingConfig(configPath);
            LoadMaxFormationConfig(configPath);
            LoadGeneralRandomTemplates(configPath);
            LoadGeneralSpecialtyTemplates(configPath);

            Debug.WriteLine("[ConfigManager] Config 文件加载完成");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 Config 文件失败: {ex.Message}");
        }
    }

    private void LoadGeneralInCountryConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralInCountry.json");
            if (!File.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralInCountry.json 不存在");
                return;
            }

            var json = File.ReadAllText(filePath);
            _generalInCountryData = JsonSerializer.Deserialize<List<CountryGeneralsConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalInCountryData?.Count ?? 0} 个国家将领配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralInCountry.json 失败: {ex.Message}");
        }
    }

    private void LoadLegionLvSettingConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "LegionLvSetting.json");
            if (!File.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] LegionLvSetting.json 不存在");
                return;
            }

            var json = File.ReadAllText(filePath);
            _legionLvSettingData = JsonSerializer.Deserialize<List<LegionLevelConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_legionLvSettingData?.Count ?? 0} 个军团等级配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 LegionLvSetting.json 失败: {ex.Message}");
        }
    }

    private void LoadMaxFormationConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "MaxFormation.json");
            if (!File.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] MaxFormation.json 不存在");
                return;
            }

            var json = File.ReadAllText(filePath);
            _maxFormationData = JsonSerializer.Deserialize<List<MaxFormationConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_maxFormationData?.Count ?? 0} 个最大编队配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 MaxFormation.json 失败: {ex.Message}");
        }
    }

    private void LoadGeneralRandomTemplates(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralRandomTemplates.json");
            if (!File.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralRandomTemplates.json 不存在");
                return;
            }

            var json = File.ReadAllText(filePath);
            _generalRandomTemplates = JsonSerializer.Deserialize<List<GeneralRandomTemplate>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalRandomTemplates?.Count ?? 0} 个将领随机参数模板");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralRandomTemplates.json 失败: {ex.Message}");
        }
    }

    private void LoadGeneralSpecialtyTemplates(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralSpecialtyTemplates.json");
            if (!File.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralSpecialtyTemplates.json 不存在");
                return;
            }

            var json = File.ReadAllText(filePath);
            _generalSpecialtyTemplates = JsonSerializer.Deserialize<List<GeneralSpecialtyTemplate>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalSpecialtyTemplates?.Count ?? 0} 个兵种专长模板");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralSpecialtyTemplates.json 失败: {ex.Message}");
        }
    }

    private void LoadDataFiles()
    {
        try
        {
            var am = Assets.AssetManager.Default;
            if (!am.IsLoaded)
            {
                string assetsPath = Assets.AssetManager.GetDefaultAssetsPath();
                if (Directory.Exists(assetsPath))
                    am.Scan(assetsPath);
            }

            if (am.IsLoaded)
                Debug.WriteLine("[ConfigManager] Data 文件将通过 AssetManager 按需加载");
            else
                Debug.WriteLine("[ConfigManager] AssetManager 未扫描，Data 文件不可用");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 初始化 AssetManager 失败: {ex.Message}");
        }
    }

    private void LoadTerrainTypes()
    {
        try
        {
            string path = IOPath.Combine(_resourcePath, "Texture", "MapTerrian", "manager.json");
            if (File.Exists(path))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(path));

                if (doc.RootElement.TryGetProperty("terrain_types", out var types))
                {
                    foreach (var prop in types.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out int id))
                            _terrainTypes[id] = prop.Value.GetString() ?? "";
                    }
                }

                if (doc.RootElement.TryGetProperty("terrain_mapping", out var mapping))
                {
                    foreach (var prop in mapping.EnumerateObject())
                        _terrainMapping[prop.Name] = prop.Value.GetString() ?? "";
                }

                if (doc.RootElement.TryGetProperty("terrain_image_counts", out var counts))
                {
                    foreach (var prop in counts.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            _terrainImageCounts[prop.Name] = prop.Value.GetInt32();
                    }
                }
            }
        }
        catch { }
    }

    public string GetTerrainTypeName(int terrainId)
    {
        if (!_isInitialized) Initialize();
        return _terrainTypes.TryGetValue(terrainId, out var name) ? name : $"未知({terrainId})";
    }

    public Dictionary<int, string> GetTerrainTypes()
    {
        if (!_isInitialized) Initialize();
        return new Dictionary<int, string>(_terrainTypes);
    }

    public int GetTerrainVariantCount(int terrainId)
    {
        if (!_isInitialized) Initialize();

        if (!_terrainTypes.TryGetValue(terrainId, out var name))
            return 1;

        if (!_terrainMapping.TryGetValue(name, out var mappingKey))
            return 1;

        if (!_terrainImageCounts.TryGetValue(mappingKey, out int count))
            return 1;

        return Math.Max(1, count);
    }

    public string ResourcePath => _resourcePath;

    public string GetText(string key, string defaultValue = "")
    {
        if (!_isInitialized) Initialize();
        return _textConfig.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public int GetThemeColor(int defaultColor = 0x002fa7)
    {
        string hex = GetText("ThemeColor", "");
        if (string.IsNullOrEmpty(hex))
            return defaultColor;
        try
        {
            int val = hex.StartsWith("0x") ? Convert.ToInt32(hex, 16) : int.Parse(hex);
            return val;
        }
        catch { return defaultColor; }
    }

    public enum OpeningAnimationType { Logo, Token, None }

    public OpeningAnimationType GetOpeningAnimationType(OpeningAnimationType defaultType = OpeningAnimationType.Logo)
    {
        string v = GetText("OpeningAnimation", "Logo").Trim().ToLower();
        return v switch { "token" => OpeningAnimationType.Token, "none" => OpeningAnimationType.None, _ => defaultType };
    }

    public string GetMainToken() => GetText("Main_Token", "");
    public WordCloudConfig GetWordCloudConfig() => _settingData?.WordCloudConfig ?? new WordCloudConfig();

    public string GetTexturePath(string folder) => IOPath.Combine(ResourcePath, "Texture", folder);
    public string GetBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_BackGround");
    public string GetLoadingBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Loading");
    public string GetLogoPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Logo");
    public string GetConsoleBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Console", "Console.jpg");
    public string GetRenderBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_MEBackGround", "MEBackGround.jpg");
    public string GetStageMarkPath() => IOPath.Combine(ResourcePath, "Texture", "StageMark");
    public string GetProvinceMarkPath() => IOPath.Combine(GetStageMarkPath(), "ProvinceMark");
    public string GetProvinceCapitalImagePath() => IOPath.Combine(GetProvinceMarkPath(), "ProvinceCapital.png");
    public string GetBuildMarkPath() => IOPath.Combine(GetStageMarkPath(), "BuildMark");
    public string GetInformationMarkPath() => IOPath.Combine(GetStageMarkPath(), "InformationMark");
    public string GetArmyMarkPath() => IOPath.Combine(GetStageMarkPath(), "ArmyMark");

    public string GetLoadingBackgroundImagePath()
    {
        string folder = GetLoadingBackgroundPath();
        if (!Directory.Exists(folder)) return "";
        foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" })
        {
            var files = Directory.GetFiles(folder, "*" + ext);
            if (files.Length > 0) return files[0];
        }
        return "";
    }

    public Dictionary<int, int> GetCountryColors() => _settingData?.CountryColors ?? new Dictionary<int, int>();

    public int GetCountryColor(int countryId, int defaultValue = -1)
    {
        var colors = GetCountryColors();
        return colors.TryGetValue(countryId, out int color) ? color : defaultValue;
    }

    public ColorToTerrainConfig GetColorToTerrainConfig() => _settingData?.ColorToTerrainConfig ?? new ColorToTerrainConfig();

    public BuildingConfig GetBuildingConfig() => _settingData?.BuildingConfig ?? new BuildingConfig();

    public ArmyEditConfig GetArmyEditConfig() => _settingData?.ArmyEditConfig ?? new ArmyEditConfig();

    public LegionEditConfig GetLegionEditConfig() => _settingData?.LegionEditConfig ?? new LegionEditConfig();

    public EventEditConfig GetEventEditConfig() => _settingData?.EventEditConfig ?? new EventEditConfig();

    public AirForceEditConfig GetAirForceEditConfig() => _settingData?.AirForceEditConfig ?? new AirForceEditConfig();

    public CaseEditConfig GetCaseEditConfig() => _settingData?.CaseEditConfig ?? new CaseEditConfig();

    public WeatherEditConfig GetWeatherEditConfig() => _settingData?.WeatherEditConfig ?? new WeatherEditConfig();

    public List<CountryGeneralsConfig> GetGeneralInCountryData()
    {
        if (!_isInitialized) Initialize();
        return _generalInCountryData ?? [];
    }

    public List<LegionLevelConfig> GetLegionLvSettingData()
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData ?? [];
    }

    public List<MaxFormationConfig> GetMaxFormationData()
    {
        if (!_isInitialized) Initialize();
        return _maxFormationData ?? [];
    }

    public List<GeneralRandomTemplate> GetGeneralRandomTemplates()
    {
        if (!_isInitialized) Initialize();
        return _generalRandomTemplates ?? [];
    }

    public List<GeneralSpecialtyTemplate> GetGeneralSpecialtyTemplates()
    {
        if (!_isInitialized) Initialize();
        return _generalSpecialtyTemplates ?? [];
    }

    public LegionLevelConfig? GetLegionLevelConfig(int id)
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData?.FirstOrDefault(c => c.Id == id);
    }

    public List<LegionLevelConfig> GetAllLegionLevelConfigs()
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData ?? [];
    }

    public int GetMaxFormation(int unitType)
    {
        if (!_isInitialized) Initialize();
        var config = _maxFormationData?.FirstOrDefault(m => m.Id == unitType);
        return config?.MaxFormation ?? 0;
    }

    public List<int> GetGeneralsByCountryId(int countryId)
    {
        if (!_isInitialized) Initialize();
        var config = _generalInCountryData?.FirstOrDefault(c => c.CountryId == countryId);
        return config?.Generals ?? [];
    }

    public GeneralSettings? GetGeneralSettingsById(int generalId)
    {
        if (!_isInitialized) Initialize();
        return GetGeneralSettingsData().FirstOrDefault(g => g.Id == generalId);
    }

    public string? GetGeneralSpecialty(int generalId)
    {
        if (!_isInitialized) Initialize();
        var settings = GetGeneralSettingsById(generalId);
        if (settings == null) return null;

        var templates = GetGeneralSpecialtyTemplates();
        if (templates.Count == 0) return null;

        var maxStat = new List<(string Specialty, int Value)>
        {
            ("Infantry", settings.Infantry),
            ("Armor", settings.Armor),
            ("Artillery", settings.Artillery),
            ("Navy", settings.Navy),
            ("AirForce", settings.AirForce)
        }.OrderByDescending(s => s.Value).First();

        return maxStat.Value > 0 ? maxStat.Specialty : null;
    }

    public string? GetUnitSpecialtyType(int unitType)
    {
        if (!_isInitialized) Initialize();
        var armyConfig = GetArmyEditConfig();

        if (armyConfig.Infantry.Contains(unitType)) return "Infantry";
        if (armyConfig.Armor.Contains(unitType)) return "Armor";
        if (armyConfig.Artillery.Contains(unitType)) return "Artillery";
        if (armyConfig.Navy.Contains(unitType)) return "Navy";
        if (armyConfig.AirForce.Contains(unitType)) return "AirForce";

        return null;
    }

    private readonly HashSet<int> _assignedGenerals = new();

    public void AddAssignedGeneral(int generalId) => _assignedGenerals.Add(generalId);

    public void RemoveAssignedGeneral(int generalId) => _assignedGenerals.Remove(generalId);

    public bool IsGeneralAssigned(int generalId) => _assignedGenerals.Contains(generalId);

    public void ClearAssignedGenerals() => _assignedGenerals.Clear();

    public List<int> GetAvailableGenerals(List<int> generalIds)
    {
        return generalIds.Where(id => !IsGeneralAssigned(id)).ToList();
    }

    public List<ConquerCountryConfig> GetConquerCountrySettingsData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetConquerCountrySettings();
    }

    public List<GeneralSettings> GetGeneralSettingsData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetGeneralSettings();
    }

    public Dictionary<string, string> GetStringTableData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetStringTable();
    }

    public string GetStringTableValue(string key, string defaultValue = "")
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetStringTableValue(key, defaultValue);
    }

    /// <summary>
    /// 获取字符串表解析器（支持读写）
    /// </summary>
    public StringTableParser GetStringTableParser()
    {
        if (!_isInitialized) Initialize();
        _stringTableParser ??= new StringTableParser(GetStringTableFilePath());
        return _stringTableParser;
    }

    /// <summary>
    /// 获取stringtable文件路径（优先返回AssetManager使用的路径）
    /// </summary>
    public string GetStringTableFilePath()
    {
        // 优先使用AssetManager的路径（WC4DATA/assets），确保与游戏运行时一致
        string assetsPath = IOPath.Combine(ResourcePath, "WC4DATA", "assets", "stringtable_tw.ini");
        if (File.Exists(assetsPath)) return assetsPath;

        // 回退到Data目录
        string dataPath = IOPath.Combine(ResourcePath, "Data", "stringtable_tw.ini");
        if (File.Exists(dataPath)) return dataPath;

        // 如果都不存在，默认返回assets路径（会创建新文件）
        return assetsPath;
    }

    /// <summary>
    /// 重新加载字符串表
    /// </summary>
    public void ReloadStringTable()
    {
        _stringTableParser?.Reload();
        StringTableChanged?.Invoke();
    }

    public TacticalMapParser? TacticalMapParser
    {
        get
        {
            if (!_isInitialized) Initialize();
            return _tacticalMapParser;
        }
    }

    public void Reload()
    {
        _isInitialized = false;
        _textConfig.Clear();
        _settingData = null;
        _stringTableParser = null;
        _tacticalMapParser = null;
        Core.Parsers.TacticalMapParser.ClearCache();
        Initialize();
    }

    private void LoadTacticalMap()
    {
        try
        {
            _tacticalMapParser = new TacticalMapParser();
            _tacticalMapParser.EnsureLoadedFromAssets();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载战术地图失败: {ex.Message}");
        }
    }

    public class MusicConfig { public List<MusicTrack>? Tracks { get; set; } }
    public class MusicTrack { public string? Name { get; set; } public string? Path { get; set; } public int Volume { get; set; } }
}