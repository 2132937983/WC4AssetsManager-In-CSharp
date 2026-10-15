using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 国家科技树解析器（CountryTechSettings.json + stringtable_tw.ini 中的 skill_name_{Id}）。
/// 采用与 CountrySettingParser 相同的单例与路径解析约定。
/// </summary>
public class CountryTechSettingParser
{
    private static readonly object _lock = new();
    private static CountryTechSettingParser? _instance;
    public static CountryTechSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CountryTechSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<CountryTechData> _techs = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<CountryTechData> All => _techs;
    public IReadOnlyList<CountryTechData> Items => _techs;
    public string ConfigPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private CountryTechSettingParser()
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
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            ConfigPath = Path.Combine(root, "json", "CountryTechSettings.json");
            StringTablePath = Path.Combine(root, "stringtable_tw.ini");
        }
        else
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

            ConfigPath = Path.Combine(candidate, "json", "CountryTechSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[CountryTechSettingParser] ConfigPath={ConfigPath} StringTablePath={StringTablePath}");
    }

    public void LoadAll()
    {
        LoadTechs();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadTechs()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[CountryTechSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<List<CountryTechData>>(json, JsonOpts);
            if (data != null) _techs = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountryTechSettingParser] 加载失败: {ex.Message}");
        }
    }

    public CountryTechData? GetById(int id)
    {
        foreach (var t in _techs) if (t.Id == id) return t;
        return null;
    }

    /// <summary>获取科技名称（来自 stringtable 的 skill_name_{Id}）。</summary>
    public string GetTechName(int id) => _stringTable?.GetValue($"skill_name_{id}", "") ?? "";

    /// <summary>设置科技名称（写入 stringtable 的 skill_name_{Id}）。</summary>
    public void SetTechName(int id, string name) => _stringTable?.SetValue($"skill_name_{id}", name);

    // ---- 基于 Type 的 stringtable 文本（countrytech_{type} / countrytech_desc_{type}）----
    public string GetTechNameByType(int type) => _stringTable?.GetValue($"countrytech_{type}", "") ?? "";
    public void SetTechNameByType(int type, string v) => _stringTable?.SetValue($"countrytech_{type}", v);
    public string GetTechDescByType(int type) => _stringTable?.GetValue($"countrytech_desc_{type}", "") ?? "";
    public void SetTechDescByType(int type, string v) => _stringTable?.SetValue($"countrytech_desc_{type}", v);

    public int GetNextId()
    {
        int max = 0;
        foreach (var t in _techs) if (t.Id > max) max = t.Id;
        return max + 1;
    }

    /// <summary>取指定分类下最大 Id + 1（按分类自增，更易维护）。</summary>
    public int GetNextIdInCategory(int categoryId)
    {
        int max = 0;
        foreach (var t in _techs) if (t.CategoryId == categoryId && t.Id > max) max = t.Id;
        return max + 1;
    }

    public bool AddTech(CountryTechData tech)
    {
        if (GetById(tech.Id) != null) return false;
        _techs.Add(tech);
        return true;
    }

    public bool RemoveTech(int id)
    {
        var t = GetById(id);
        if (t == null) return false;
        _techs.Remove(t);
        _stringTable?.Remove($"skill_name_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_techs, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[CountryTechSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountryTechSettingParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    public void Reload() => LoadAll();

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>
/// 国家科技树节点（CountryTechSettings.json）。
/// 名称不保存在 JSON 中，而是写入 stringtable_tw.ini 的 skill_name_{Id} 键。
/// </summary>
public class CountryTechData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("NeedId")] public List<int> NeedId { get; set; } = new();
    [JsonPropertyName("CategoryId")] public int CategoryId { get; set; }
    [JsonPropertyName("Categorys")] public int Categorys { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("ResearchLv")] public int ResearchLv { get; set; }
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("Position")] public int Position { get; set; }
    [JsonPropertyName("Chance")] public int Chance { get; set; }
    [JsonPropertyName("Value")] public int Value { get; set; }
    [JsonPropertyName("Lines")] public List<double> Lines { get; set; } = new();
    [JsonPropertyName("Lines2")] public List<double> Lines2 { get; set; } = new();
    [JsonPropertyName("CostMerit")] public int CostMerit { get; set; }
}
