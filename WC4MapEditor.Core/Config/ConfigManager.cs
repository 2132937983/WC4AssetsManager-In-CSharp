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
    private ColorToTerrainConfig? _colorToTerrainConfig;
    private BuildingConfig? _buildingConfig;
    private ArmyEditConfig? _armyEditConfig;
    private LegionEditConfig? _legionEditConfig;
    private WordCloudConfig? _wordCloudConfig;
    private EventEditConfig? _eventEditConfig;
    private AirForceEditConfig? _airForceEditConfig;
    private CaseEditConfig? _caseEditConfig;
    private WeatherEditConfig? _weatherEditConfig;
    private Dictionary<int, int> _countryColors = new();
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

            bool inTerrainSection = false;
            bool inBuildingSection = false;
            bool inArmySection = false;
            bool inLegionSection = false;
            bool inWordCloudSection = false;
            bool inEventSection = false;
            bool inAirForceSection = false;
            bool inCaseSection = false;
            bool inWeatherSection = false;

            foreach (string rawLine in File.ReadAllLines(configPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line == "[地形修改]") { inTerrainSection = true; ContinueReset(); continue; }
                if (line == "[建筑修改]") { inBuildingSection = true; _buildingConfig = new BuildingConfig(); ContinueReset(); continue; }
                if (line == "[编辑单位]") { inArmySection = true; _armyEditConfig = new ArmyEditConfig(); ContinueReset(); continue; }
                if (line == "[编辑军团]") { inLegionSection = true; _legionEditConfig = new LegionEditConfig(); ContinueReset(); continue; }
                if (line == "[词云一言]") { inWordCloudSection = true; _wordCloudConfig = new WordCloudConfig(); ContinueReset(); continue; }
                if (line == "[事件修改]") { inEventSection = true; _eventEditConfig = new EventEditConfig(); ContinueReset(); continue; }
                if (line == "[空袭修改]") { inAirForceSection = true; _airForceEditConfig = new AirForceEditConfig(); ContinueReset(); continue; }
                if (line == "[方针修改]") { inCaseSection = true; _caseEditConfig = new CaseEditConfig(); ContinueReset(); continue; }
                if (line == "[天气修改]") { inWeatherSection = true; _weatherEditConfig = new WeatherEditConfig(); ContinueReset(); continue; }
                if (line.StartsWith("[") && line.EndsWith("]")) { ContinueReset(); continue; }

                void ContinueReset()
                {
                    inTerrainSection = false; inBuildingSection = false; inArmySection = false;
                    inLegionSection = false; inWordCloudSection = false; inEventSection = false;
                    inAirForceSection = false; inCaseSection = false; inWeatherSection = false;
                }

                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0) continue;

                string key = line.Substring(0, eqIdx).Trim();
                string value = line.Substring(eqIdx + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                if (_textConfig.ContainsKey(key))
                    _textConfig[key] = value;
                else
                    _textConfig.Add(key, value);

                if (inTerrainSection && key.StartsWith("ColorToTerrain"))
                    ParseColorToTerrainConfig(value);
                if (inBuildingSection) ParseBuildingConfig(key, value);
                if (inArmySection && _armyEditConfig != null) ParseArmyEditConfig(key, value);
                if (inLegionSection && _legionEditConfig != null) ParseLegionEditConfig(key, value);
                if (inWordCloudSection && _wordCloudConfig != null) ParseWordCloudConfig(key, value);
                if (inEventSection && _eventEditConfig != null) ParseEventEditConfig(key, value);
                if (inAirForceSection && _airForceEditConfig != null) ParseAirForceEditConfig(key, value);
                if (inCaseSection && _caseEditConfig != null) ParseCaseEditConfig(key, value);
                if (inWeatherSection && _weatherEditConfig != null) ParseWeatherEditConfig(key, value);
                if (key.StartsWith("country_color_")) ParseCountryColor(key, value);
            }

            Debug.WriteLine($"[ConfigManager] Loaded {_textConfig.Count} config entries");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] Failed to load setting.txt: {ex.Message}");
        }
    }

    private void ParseColorToTerrainConfig(string value)
    {
        try
        {
            _colorToTerrainConfig = new ColorToTerrainConfig();
            if (!value.StartsWith('[')) return;
            value = value[1..^1];

            var elements = new List<string>();
            int depth = 0;
            string current = "";
            foreach (char c in value)
            {
                if (c == '[') { depth++; current += c; }
                else if (c == ']') { depth--; current += c; }
                else if (c == ',' && depth == 0) { elements.Add(current.Trim()); current = ""; }
                else current += c;
            }
            if (!string.IsNullOrWhiteSpace(current)) elements.Add(current.Trim());

            if (elements.Count > 0)
            {
                if (int.TryParse(elements[0], out int prob))
                    _colorToTerrainConfig.LandNoiseProbability = prob;
            }

            for (int i = 1; i < elements.Count; i++)
            {
                string m = elements[i].Trim();
                if (!m.StartsWith('[')) continue;
                m = m[1..^1];
                var parts = m.Split(',');
                if (parts.Length < 2) continue;
                var mapping = new ColorTerrainMapping();
                string cs = parts[0].Trim();
                mapping.ColorValue = cs.StartsWith("0x") ? Convert.ToInt32(cs, 16) : int.Parse(cs);
                for (int j = 1; j < parts.Length; j++)
                    if (int.TryParse(parts[j].Trim(), out int t)) mapping.TerrainTypes.Add(t);
                if (mapping.TerrainTypes.Count > 0) _colorToTerrainConfig.ColorMappings.Add(mapping);
            }
        }
        catch { _colorToTerrainConfig = new ColorToTerrainConfig(); }
    }

    private void ParseBuildingConfig(string key, string value)
    {
        if (_buildingConfig == null) return;
        switch (key)
        {
            case "BuildingRandomCharacter": _buildingConfig.BuildingRandomCharacter = ParseIntList(value); break;
            case "LowStrengthBuilding": _buildingConfig.LowStrengthBuilding = ParseIntList(value); break;
            case "MediumStrengthBuilding": _buildingConfig.MediumStrengthBuilding = ParseIntList(value); break;
            case "HighStrengthBuilding": _buildingConfig.HighStrengthBuilding = ParseIntList(value); break;
            case "CommonBuilding": _buildingConfig.CommonBuilding = ParseIntList(value); break;
            case "BuidingOnSea": case "BuildingOnSea": _buildingConfig.BuildingOnSea = ParseIntList(value); break;
            case "BuildingNeedDirection": _buildingConfig.BuildingNeedDirection = ParseIntList(value); break;
        }
    }

    private void ParseArmyEditConfig(string key, string value)
    {
        if (_armyEditConfig == null) return;
        switch (key)
        {
            case "LowStrengthArmy": _armyEditConfig.LowStrengthArmy = ParseIntList(value); break;
            case "MediumStrengthArmy": _armyEditConfig.MediumStrengthArmy = ParseIntList(value); break;
            case "HighStrengthArmy": _armyEditConfig.HighStrengthArmy = ParseIntList(value); break;
            case "ArmyOnLand": _armyEditConfig.ArmyOnLand = ParseIntList(value); break;
            case "ArmyOnSea": _armyEditConfig.ArmyOnSea = ParseIntList(value); break;
            case "Infantry": _armyEditConfig.Infantry = ParseIntList(value); break;
            case "Armor": _armyEditConfig.Armor = ParseIntList(value); break;
            case "Artillery": _armyEditConfig.Artillery = ParseIntList(value); break;
            case "Navy": _armyEditConfig.Navy = ParseIntList(value); break;
            case "AirForce": _armyEditConfig.AirForce = ParseIntList(value); break;
        }
    }

    private void ParseLegionEditConfig(string key, string value)
    {
        if (_legionEditConfig == null) return;
        switch (key)
        {
            case "LowStrengthGift": _legionEditConfig.LowStrengthGift = ParseGiftList(value); break;
            case "MediumStrengthGift": _legionEditConfig.MediumStrengthGift = ParseGiftList(value); break;
            case "HighStrengthGift": _legionEditConfig.HighStrengthGift = ParseGiftList(value); break;
            case "LowStrengthCountry": _legionEditConfig.LowStrengthCountry = ParseIntList(value); break;
            case "MediumStrengthCountry": _legionEditConfig.MediumStrengthCountry = ParseIntList(value); break;
            case "HighStrengthCountry": _legionEditConfig.HighStrengthCountry = ParseIntList(value); break;
        }
    }

    private void ParseWordCloudConfig(string key, string value)
    {
        if (_wordCloudConfig == null) return;
        switch (key)
        {
            case "Main_Token": _wordCloudConfig.MainToken = value; break;
            case "tokens_num": int.TryParse(value, out int n); _wordCloudConfig.TokensNum = n; break;
            case "animation_keep": int.TryParse(value, out int k); _wordCloudConfig.AnimationKeep = k; break;
            default:
                if (key.StartsWith("token_") && int.TryParse(key["token_".Length..], out int idx) && idx >= 1 && idx <= 30)
                    _wordCloudConfig.Tokens[idx - 1] = value;
                break;
        }
    }

    private void ParseEventEditConfig(string key, string value) { }
    private void ParseAirForceEditConfig(string key, string value) { }
    private void ParseCaseEditConfig(string key, string value) { }
    private void ParseWeatherEditConfig(string key, string value) { }

    private void ParseCountryColor(string key, string value)
    {
        try
        {
            string numPart = key["country_color_".Length..];
            if (int.TryParse(numPart, out int countryId))
            {
                string cs = value.Trim();
                int colorVal = cs.StartsWith("0x") ? Convert.ToInt32(cs, 16) : int.Parse(cs);
                _countryColors[countryId] = colorVal;
            }
        }
        catch { }
    }

    private List<int> ParseIntList(string value)
    {
        var result = new List<int>();
        try
        {
            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                value = value[1..^1];
                foreach (var part in value.Split(','))
                    if (int.TryParse(part.Trim(), out int n)) result.Add(n);
            }
        }
        catch { }
        return result;
    }

    private List<List<(int Min, int Max)>> ParseGiftList(string value)
    {
        var result = new List<List<(int, int)>>();
        try
        {
            if (!value.StartsWith('[')) return result;
            value = value[1..^1];
            var tuples = new List<string>();
            int depth = 0;
            string current = "";
            foreach (char c in value)
            {
                if (c == '(') { depth++; current += c; }
                else if (c == ')') { depth--; current += c; if (depth == 0) { tuples.Add(current.Trim()); current = ""; } }
                else if (depth > 0) current += c;
            }
            foreach (var tuple in tuples)
            {
                if (!tuple.StartsWith('(')) continue;
                var nums = tuple[1..^1].Split(',');
                if (nums.Length >= 2)
                    result.Add(nums.Select(n => (Min: int.Parse(n.Trim()), Max: 0)).ToList());
            }
        }
        catch { }
        return result;
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
    public WordCloudConfig GetWordCloudConfig() => _wordCloudConfig ?? new WordCloudConfig();

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

    public Dictionary<int, int> GetCountryColors() => _countryColors;

    public void Reload()
    {
        _isInitialized = false;
        _textConfig.Clear();
        Initialize();
    }

    public class MusicConfig { public List<MusicTrack>? Tracks { get; set; } }
    public class MusicTrack { public string? Name { get; set; } public string? Path { get; set; } public int Volume { get; set; } }

    public class ColorToTerrainConfig
    {
        public int LandNoiseProbability { get; set; } = 30;
        public List<ColorTerrainMapping> ColorMappings { get; set; } = [];
        public double MinRatio { get; set; } = 0.3;
    }

    public class ColorTerrainMapping
    {
        public int ColorValue { get; set; }
        public List<int> TerrainTypes { get; set; } = [];
    }

    public class BuildingConfig
    {
        public List<int> BuildingRandomCharacter { get; set; } = [];
        public List<int> LowStrengthBuilding { get; set; } = [];
        public List<int> MediumStrengthBuilding { get; set; } = [];
        public List<int> HighStrengthBuilding { get; set; } = [];
        public List<int> CommonBuilding { get; set; } = [];
        public List<int> BuildingOnSea { get; set; } = [];
        public List<int> BuildingNeedDirection { get; set; } = [];
    }

    public class ArmyEditConfig
    {
        public List<int> LowStrengthArmy { get; set; } = [];
        public List<int> MediumStrengthArmy { get; set; } = [];
        public List<int> HighStrengthArmy { get; set; } = [];
        public List<int> ArmyOnLand { get; set; } = [];
        public List<int> ArmyOnSea { get; set; } = [];
        public List<int> Infantry { get; set; } = [];
        public List<int> Armor { get; set; } = [];
        public List<int> Artillery { get; set; } = [];
        public List<int> Navy { get; set; } = [];
        public List<int> AirForce { get; set; } = [];
    }

    public class LegionEditConfig
    {
        public List<List<(int, int)>> LowStrengthGift { get; set; } = [];
        public List<List<(int, int)>> MediumStrengthGift { get; set; } = [];
        public List<List<(int, int)>> HighStrengthGift { get; set; } = [];
        public List<int> LowStrengthCountry { get; set; } = [];
        public List<int> MediumStrengthCountry { get; set; } = [];
        public List<int> HighStrengthCountry { get; set; } = [];
    }

    public class WordCloudConfig
    {
        public string MainToken { get; set; } = "";
        public int TokensNum { get; set; } = 30;
        public int AnimationKeep { get; set; } = 5;
        public string[] Tokens { get; set; } = new string[30];
    }

    public class EventEditConfig { }
    public class AirForceEditConfig { }
    public class CaseEditConfig { }
    public class WeatherEditConfig { }
}