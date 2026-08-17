using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WC4MapEditor.Core.Config;

public sealed class ConfigManager
{
    private static readonly object _lock = new();
    private static ConfigManager? _instance;

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
            string path = IOPath.Combine(_resourcePath, "Config", "WindowMusic.json");
            if (File.Exists(path))
                _musicConfig = JsonSerializer.Deserialize<MusicConfig>(File.ReadAllText(path));
        }
        catch { }
    }

    private void LoadConfigFiles() { }
    private void LoadDataFiles() { }

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
    public string GetBuildMarkPath() => IOPath.Combine(GetStageMarkPath(), "BuildMark");
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

    public ColorToTerrainConfig GetColorToTerrainConfig() => _settingData?.ColorToTerrainConfig ?? new ColorToTerrainConfig();

    public BuildingConfig GetBuildingConfig() => _settingData?.BuildingConfig ?? new BuildingConfig();

    public ArmyEditConfig GetArmyEditConfig() => _settingData?.ArmyEditConfig ?? new ArmyEditConfig();

    public LegionEditConfig GetLegionEditConfig() => _settingData?.LegionEditConfig ?? new LegionEditConfig();

    public EventEditConfig GetEventEditConfig() => _settingData?.EventEditConfig ?? new EventEditConfig();

    public AirForceEditConfig GetAirForceEditConfig() => _settingData?.AirForceEditConfig ?? new AirForceEditConfig();

    public CaseEditConfig GetCaseEditConfig() => _settingData?.CaseEditConfig ?? new CaseEditConfig();

    public WeatherEditConfig GetWeatherEditConfig() => _settingData?.WeatherEditConfig ?? new WeatherEditConfig();

    public void Reload()
    {
        _isInitialized = false;
        _textConfig.Clear();
        _settingData = null;
        Initialize();
    }

    public class MusicConfig { public List<MusicTrack>? Tracks { get; set; } }
    public class MusicTrack { public string? Name { get; set; } public string? Path { get; set; } public int Volume { get; set; } }
}