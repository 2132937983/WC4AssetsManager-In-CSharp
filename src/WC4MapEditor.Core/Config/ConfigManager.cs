using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Config;

public sealed class ConfigManager
{
    private static readonly object _lock = new();
    private static ConfigManager? _instance;

    /// <summary>
    /// 字符串表重新加载 / 保存 / 放弃修改之后触发。
    /// 事件源是共享的 <see cref="Services.StringTableStore"/>：任何编辑器保存 stringtable 后，国家名缓存、城市名渲染都会跟着刷新。
    /// </summary>
    public static event Action? StringTableChanged
    {
        add => Services.StringTableStore.Changed += value;
        remove => Services.StringTableStore.Changed -= value;
    }

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

    /// <summary>
    /// setting.txt 解析结果；首次访问时确保已初始化。
    /// 以前只有 <see cref="GetText"/> 会触发初始化，命令行 / 脚本里若先调用 GetCountryColors、GetBuildingConfig 等，
    /// 拿到的是空配置（例如 legion apply-settings-colors 一个军团的颜色都匹配不到）。
    /// </summary>
    private SettingTxtData? SettingData
    {
        get
        {
            if (!_isInitialized) Initialize();
            return _settingData;
        }
    }
    private Dictionary<int, string> _terrainTypes = new();
    private Dictionary<string, string> _terrainMapping = new();
    private Dictionary<string, int> _terrainImageCounts = new();
    private List<MapTerrainEntry>? _mapTerrainEntries;
    private HashSet<int>? _buildingTypeIds;
    private HashSet<int>? _armyTypeIds;
    private HashSet<int>? _countryIds;
    private HashSet<int>? _armyLevels;
    private HashSet<int>? _rankIds;
    private HashSet<int>? _qualityIds;

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

    /// <summary>
    /// setting.txt 的位置：从 exe 目录逐级向上找第一份（与 Resource 同一套规则），找不到时指向 exe 同级。
    /// 设置窗口的写回、背景音乐偏好的保存都用这个路径，保证读写的是同一份文件。
    /// </summary>
    public static string SettingFilePath =>
        ProjectPaths.FindFile("setting.txt") ?? IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.txt");

    private string DetectResourcePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string localPath = IOPath.Combine(baseDir, "Resource");
        if (Directory.Exists(localPath)) return localPath;

        // 与 AssetManager 共用同一套规则：exe 同级 → 逐级向上（最多 8 层）
        return ProjectPaths.FindDirectory("Resource") ?? localPath;
    }

    private void LoadSettingFile()
    {
        try
        {
            string configPath = SettingFilePath;

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

    /// <summary>WindowMusic.json 的键是小写开头（tracks / filename / crossfadeDuration …），按不区分大小写反序列化。</summary>
    private static readonly JsonSerializerOptions MusicJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private void LoadMusicConfig()
    {
        try
        {
            string path = IOPath.Combine(_resourcePath, "Music", "WindowMusic.json");
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[ConfigManager] 音乐配置文件不存在: {path}");
                return;
            }

            var config = JsonSerializer.Deserialize<MusicConfig>(File.ReadAllText(path), MusicJsonOptions);
            if (config == null) return;
            // JSON 里写成 null 的列表 / 设置按缺省处理，文件名为空的曲目直接丢弃
            config.Tracks = (config.Tracks ?? []).Where(t => t != null && !string.IsNullOrWhiteSpace(t.Filename)).ToList();
            config.Settings ??= new MusicSettings();
            _musicConfig = config;
            Debug.WriteLine($"[ConfigManager] 加载了 {config.Tracks.Count} 首音乐");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载音乐配置失败: {ex.Message}");
        }
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

    /// <summary>
    /// 地形定义列表，来自 assets/config/def_mapterrain.xml —— 游戏对地形的<b>权威定义</b>，
    /// 与 Java 版 ResConfig.Config.TERRAINIDS 同源。
    /// <para>
    /// 职责划分：由 <c>AssetManager</c> 负责定位并读取 WC4DATA 内的文件，
    /// 由 <see cref="DefMapTerrainParser"/> 负责解析 XML 语义。
    /// 资源未扫描或文件缺失时返回空列表，让调用方<b>跳过</b>地形检查，
    /// 而不是把「取不到配置」误判成「所有 ID 都非法」。
    /// </para>
    /// </summary>
    public IReadOnlyList<MapTerrainEntry> GetMapTerrainEntries()
    {
        if (!_isInitialized) Initialize();
        if (_mapTerrainEntries != null) return _mapTerrainEntries;

        _mapTerrainEntries = new List<MapTerrainEntry>();

        try
        {
            var am = Assets.AssetManager.Default;
            if (!am.IsLoaded)
            {
                string assetsPath = Assets.AssetManager.GetDefaultAssetsPath();
                if (Directory.Exists(assetsPath)) am.Scan(assetsPath);
            }

            var entry = am.Find(DefMapTerrainParser.AssetPath);
            if (entry == null)
            {
                Debug.WriteLine($"[ConfigManager] AssetManager 中未找到 {DefMapTerrainParser.AssetPath}");
                return _mapTerrainEntries;
            }

            _mapTerrainEntries = DefMapTerrainParser.Parse(ReadConfigText(am, entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 {DefMapTerrainParser.AssetPath} 失败: {ex.Message}");
        }

        return _mapTerrainEntries;
    }

    /// <summary>
    /// 合法地形组 ID 集合（<see cref="GetMapTerrainEntries"/> 的便捷形式）。
    /// 哨兵值 0 与 63 由调用方另行放行 —— 它们未必出现在定义表中。
    /// </summary>
    public HashSet<int> GetTerrainGroupIds()
        => DefMapTerrainParser.ToGroupIdSet(GetMapTerrainEntries());

    /// <summary>
    /// 合法建筑类型集合（来自 assets/json/BuildingSettings.json 的 Id）。
    /// 对应 Java 版 ResConfig.Config.DEF_WC4BUILD。
    /// 取不到时返回空集合，调用方据此<b>跳过</b>建筑类型校验。
    /// </summary>
    public HashSet<int> GetBuildingTypeIds()
        => LoadIdSet(GameSettingsParser.BuildingAssetPath, GameSettingsParser.ParseBuildingIds, ref _buildingTypeIds);

    /// <summary>
    /// 合法兵种 ID 集合（来自 assets/json/ArmySettings.json 的 Army）。
    /// 对应 Java 版 ResConfig.Config.DEF_WC4ARMY。
    /// 取不到时返回空集合，调用方据此<b>跳过</b>兵种校验。
    /// </summary>
    public HashSet<int> GetArmyTypeIds()
        => LoadIdSet(GameSettingsParser.ArmyAssetPath, GameSettingsParser.ParseArmyIds, ref _armyTypeIds);

    /// <summary>合法国家 ID 集合（CountrySettings.json 的 Id）</summary>
    public HashSet<int> GetCountryIds()
        => LoadIdSet(GameSettingsParser.CountryAssetPath, GameSettingsParser.ParseCountryIds, ref _countryIds);

    /// <summary>合法兵种等级集合（ArmyLevelSettings.json 的 Level）</summary>
    public HashSet<int> GetArmyLevels()
        => LoadIdSet(GameSettingsParser.ArmyLevelAssetPath, GameSettingsParser.ParseArmyLevels, ref _armyLevels);

    /// <summary>合法军衔集合（GeneralLevelSettings.json 的 Id）</summary>
    public HashSet<int> GetRankIds()
        => LoadIdSet(GameSettingsParser.RankAssetPath, GameSettingsParser.ParseRankIds, ref _rankIds);

    /// <summary>合法将领品质（HP 等级）集合（GeneralQualitySettings.json 的 Id）</summary>
    public HashSet<int> GetQualityIds()
        => LoadIdSet(GameSettingsParser.QualityAssetPath, GameSettingsParser.ParseQualityIds, ref _qualityIds);

    /// <summary>
    /// 经 AssetManager 取文件、交给 Parser 解析，并缓存结果。
    /// </summary>
    private HashSet<int> LoadIdSet(string assetPath, Func<string, HashSet<int>> parse, ref HashSet<int>? cache)
    {
        if (!_isInitialized) Initialize();
        if (cache != null) return cache;

        cache = [];

        try
        {
            var am = Assets.AssetManager.Default;
            if (!am.IsLoaded)
            {
                string assetsPath = Assets.AssetManager.GetDefaultAssetsPath();
                if (Directory.Exists(assetsPath)) am.Scan(assetsPath);
            }

            var entry = am.Find(assetPath);
            if (entry == null)
            {
                Debug.WriteLine($"[ConfigManager] AssetManager 中未找到 {assetPath}");
                return cache;
            }

            cache = parse(am.ReadText(entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 {assetPath} 失败: {ex.Message}");
        }

        return cache;
    }

    /// <summary>
    /// 读取配置文件文本，自动判断编码。
    /// <para>
    /// WC4DATA/assets/config 下的 xml 目前是 <b>GBK</b> 编码 —— 例如「平原」的字节是
    /// <c>C6 BD D4 AD</c>，按 UTF-8 解会得到乱码（实测：解成 U+01BD U+052D）。
    /// 这里先按 UTF-8 解；若出现替换字符 U+FFFD 就说明不是 UTF-8，再尝试 GB18030。
    /// <para>
    /// 注意：GBK / GB18030 依赖 <c>CodePagesEncodingProvider</c>，本项目并未注册
    /// （也不含 System.Text.Encoding.CodePages 包），因此该尝试会抛异常并退回 UTF-8 结果。
    /// 结果是<b>中文地形名会乱码，但组 ID 与类型都是 ASCII 数字，不受任何影响</b> ——
    /// 校验只依赖组 ID，所以功能完全正确。
    /// 若将来要显示正确的中文名，需引入该包并注册提供程序。
    /// </para>
    /// </summary>
    private static string ReadConfigText(Assets.AssetManager am, Assets.AssetEntry entry)
    {
        string utf8 = am.ReadText(entry, Encoding.UTF8);
        if (!utf8.Contains('\uFFFD')) return utf8;

        // 不是 UTF-8：这批配置文件是 GBK。
        // 注意 GBK/GB18030 依赖 CodePagesEncodingProvider，未注册时会抛异常 ——
        // 此时必须退回 UTF-8 结果，绝不能让异常把整个解析打断：
        // 组 ID / 类型都是 ASCII 数字，任何单字节解码都拿得到，只有中文名会乱码。
        try
        {
            return am.ReadText(entry, Encoding.GetEncoding("GB18030"));
        }
        catch
        {
            return utf8;
        }
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

    /// <summary>
    /// 主题色（[全局编辑] ThemeColor，0xRRGGBB）。与 VB 版一致一律按十六进制解析，
    /// 可带 0x / 0X / # 前缀；为空或无法解析时返回 <paramref name="defaultColor"/>。
    /// </summary>
    public int GetThemeColor(int defaultColor = 0x002fa7)
    {
        string hex = GetText("ThemeColor", "").Trim().TrimStart('#');
        if (string.IsNullOrEmpty(hex))
            return defaultColor;
        try
        {
            return Convert.ToInt32(hex, 16) & 0xFFFFFF;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return defaultColor;
        }
    }

    public enum OpeningAnimationType { Logo, Token, None }

    public OpeningAnimationType GetOpeningAnimationType(OpeningAnimationType defaultType = OpeningAnimationType.Logo)
    {
        string v = GetText("OpeningAnimation", "Logo").Trim().ToLower();
        return v switch { "token" => OpeningAnimationType.Token, "none" => OpeningAnimationType.None, _ => defaultType };
    }

    public string GetMainToken() => GetText("Main_Token", "");
    public WordCloudConfig GetWordCloudConfig() => SettingData?.WordCloudConfig ?? new WordCloudConfig();

    public string GetTexturePath(string folder) => IOPath.Combine(ResourcePath, "Texture", folder);
    public string GetBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_BackGround");
    public string GetLoadingBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Loading");
    public string GetLogoPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Logo");

    /// <summary>
    /// 控制台背景图：ZSEditor_Console 目录里第一张支持格式的图片（对齐 VB 版 GetConsoleBackgroundImagePath 的自动识别）；
    /// 目录里没有图片时返回约定的 Console.jpg 路径，由调用方判断文件是否存在。
    /// </summary>
    public string GetConsoleBackgroundPath()
    {
        string folder = IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Console");
        string found = FindFirstImage(folder);
        return found.Length > 0 ? found : IOPath.Combine(folder, "Console.jpg");
    }

    public string GetRenderBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_MEBackGround", "MEBackGround.jpg");
    public string GetStageMarkPath() => IOPath.Combine(ResourcePath, "Texture", "StageMark");
    public string GetProvinceMarkPath() => IOPath.Combine(GetStageMarkPath(), "ProvinceMark");
    public string GetProvinceCapitalImagePath() => IOPath.Combine(GetProvinceMarkPath(), "ProvinceCapital.png");
    public string GetBuildMarkPath() => IOPath.Combine(GetStageMarkPath(), "BuildMark");
    public string GetInformationMarkPath() => IOPath.Combine(GetStageMarkPath(), "InformationMark");
    public string GetArmyMarkPath() => IOPath.Combine(GetStageMarkPath(), "ArmyMark");

    /// <summary>加载画面背景图：ZSEditor_Loading 目录里第一张支持格式的图片，没有则返回空串。</summary>
    public string GetLoadingBackgroundImagePath() => FindFirstImage(GetLoadingBackgroundPath());

    /// <summary>背景图自动识别支持的格式，按此顺序取第一个（与 VB 版一致）</summary>
    private static readonly string[] BackgroundImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif" };

    private static string FindFirstImage(string folder)
    {
        if (!Directory.Exists(folder)) return "";
        foreach (string ext in BackgroundImageExtensions)
        {
            var files = Directory.GetFiles(folder, "*" + ext);
            if (files.Length == 0) continue;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files[0];
        }
        return "";
    }

    /// <summary>背景音乐配置（Resource/Music/WindowMusic.json）；文件缺失或解析失败时为空列表。</summary>
    public MusicConfig GetMusicConfig()
    {
        if (!_isInitialized) Initialize();
        return _musicConfig ?? new MusicConfig();
    }

    /// <summary>背景音乐文件夹（Resource/Music），播放列表里的文件名相对于它。</summary>
    public string GetMusicPath() => IOPath.Combine(ResourcePath, "Music");

    public Dictionary<int, int> GetCountryColors() => SettingData?.CountryColors ?? new Dictionary<int, int>();

    public int GetCountryColor(int countryId, int defaultValue = -1)
    {
        var colors = GetCountryColors();
        return colors.TryGetValue(countryId, out int color) ? color : defaultValue;
    }

    public ColorToTerrainConfig GetColorToTerrainConfig() => SettingData?.ColorToTerrainConfig ?? new ColorToTerrainConfig();

    public BuildingConfig GetBuildingConfig() => SettingData?.BuildingConfig ?? new BuildingConfig();

    public ArmyEditConfig GetArmyEditConfig() => SettingData?.ArmyEditConfig ?? new ArmyEditConfig();

    public LegionEditConfig GetLegionEditConfig() => SettingData?.LegionEditConfig ?? new LegionEditConfig();

    public EventEditConfig GetEventEditConfig() => SettingData?.EventEditConfig ?? new EventEditConfig();

    public AirForceEditConfig GetAirForceEditConfig() => SettingData?.AirForceEditConfig ?? new AirForceEditConfig();

    public CaseEditConfig GetCaseEditConfig() => SettingData?.CaseEditConfig ?? new CaseEditConfig();

    public WeatherEditConfig GetWeatherEditConfig() => SettingData?.WeatherEditConfig ?? new WeatherEditConfig();

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

    /// <summary>字符串表全部条目的快照（与编辑器共用同一份数据，含未保存的修改）。</summary>
    public Dictionary<string, string> GetStringTableData()
        => new(GetStringTableParser().Entries, StringComparer.OrdinalIgnoreCase);

    /// <summary>读字符串表的值（与编辑器共用同一份数据，编辑器保存后立即可见）。</summary>
    public string GetStringTableValue(string key, string defaultValue = "")
        => GetStringTableParser().GetValue(key, defaultValue);

    /// <summary>
    /// 获取字符串表解析器（支持读写）。同一个文件全程只有一个实例，见 <see cref="Services.StringTableStore"/>。
    /// </summary>
    public StringTableParser GetStringTableParser()
    {
        if (!_isInitialized) Initialize();
        _stringTableParser ??= Services.StringTableStore.Get(GetStringTableFilePath());
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
    public void ReloadStringTable() => GetStringTableParser().Reload();

    public TacticalMapParser? TacticalMapParser
    {
        get
        {
            if (!_isInitialized) Initialize();
            return _tacticalMapParser;
        }
    }

    /// <summary>
    /// <see cref="Reload"/> 完成后触发（在调用线程上）：界面据此刷新窗口标题、主题色、开始界面文本、背景音乐等。
    /// </summary>
    public static event Action? ConfigReloaded;

    /// <summary>
    /// 热重载：清空 setting.txt 与 Resource 配置的全部缓存（文本、各节配置、音乐、地形表、ID 集合、
    /// Config 目录 JSON、字符串表与战术图解析器）后重新加载，再触发 <see cref="ConfigReloaded"/>。
    /// 设置窗口保存 / 刷新后调用，修改无需重启即可生效。
    /// </summary>
    public void Reload()
    {
        _isInitialized = false;
        _textConfig.Clear();
        _settingData = null;
        _musicConfig = null;
        _terrainTypes.Clear();
        _terrainMapping.Clear();
        _terrainImageCounts.Clear();
        _mapTerrainEntries = null;
        _buildingTypeIds = null;
        _armyTypeIds = null;
        _countryIds = null;
        _armyLevels = null;
        _rankIds = null;
        _qualityIds = null;
        _generalInCountryData = null;
        _legionLvSettingData = null;
        _maxFormationData = null;
        _generalRandomTemplates = null;
        _generalSpecialtyTemplates = null;
        _stringTableParser = null;
        _tacticalMapParser = null;
        Core.Parsers.TacticalMapParser.ClearCache();
        Initialize();
        ConfigReloaded?.Invoke();
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

    /// <summary>Resource/Music/WindowMusic.json：背景音乐播放列表与播放设置</summary>
    public sealed class MusicConfig
    {
        public List<MusicTrack> Tracks { get; set; } = new();
        public MusicSettings Settings { get; set; } = new();
    }

    /// <summary>播放列表中的一首（filename 相对于 Resource/Music）</summary>
    public sealed class MusicTrack
    {
        public string Filename { get; set; } = "";

        /// <summary>单曲循环：列表里只剩这一首能播时重复播放它（多首时由列表循环决定）</summary>
        public bool Loop { get; set; }

        /// <summary>该曲目的相对音量 0~1，与主音量相乘</summary>
        public double Volume { get; set; } = 1.0;
    }

    /// <summary>WindowMusic.json 的 settings 节</summary>
    public sealed class MusicSettings
    {
        /// <summary>随机播放</summary>
        public bool Random { get; set; }

        /// <summary>列表循环：播完最后一首回到第一首，否则停止</summary>
        public bool Loop { get; set; } = true;

        /// <summary>切歌时淡出 / 淡入的时长（毫秒）</summary>
        public int CrossfadeDuration { get; set; } = 1000;

        /// <summary>默认主音量 0~1（setting.txt 里没有保存过音量时使用）</summary>
        public double DefaultVolume { get; set; } = 0.8;
    }
}