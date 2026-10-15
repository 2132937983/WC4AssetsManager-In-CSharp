using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.Skill;

/// <summary>
/// 技能解析器（SkillSettings.json + stringtable_tw.ini 中的
/// skill_name_{Type} / skill_info_{Type}）。
/// 采用与 CountryTechSettingParser 相同的单例与路径解析约定。
/// 技能名/描述不保存在 JSON 中，而是写入 stringtable，索引取 Type。
/// </summary>
public class SkillSettingParser
{
    private static readonly object _lock = new();
    private static SkillSettingParser? _instance;
    public static SkillSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SkillSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<SkillSettingData> _skills = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<SkillSettingData> All => _skills;
    public List<SkillSettingData> Items => _skills;
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

    private SkillSettingParser()
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
            ConfigPath = Path.Combine(root, "json", "SkillSettings.json");
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

            ConfigPath = Path.Combine(candidate, "json", "SkillSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[SkillSettingParser] ConfigPath={ConfigPath} StringTablePath={StringTablePath}");
    }

    public void LoadAll()
    {
        LoadSkills();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadSkills()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[SkillSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<List<SkillSettingData>>(json, JsonOpts);
            if (data != null) _skills = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillSettingParser] 加载失败: {ex.Message}");
        }
    }

    public SkillSettingData? GetById(int id)
    {
        foreach (var s in _skills) if (s.Id == id) return s;
        return null;
    }

    /// <summary>获取技能名（stringtable 的 skill_name_{Type}）。</summary>
    public string GetSkillName(int type) => _stringTable?.GetValue($"skill_name_{type}", "") ?? "";

    /// <summary>设置技能名（写入 stringtable 的 skill_name_{Type}）。</summary>
    public void SetSkillName(int type, string name) => _stringTable?.SetValue($"skill_name_{type}", name);

    /// <summary>获取技能描述（stringtable 的 skill_info_{Type}）。</summary>
    public string GetSkillDesc(int type) => _stringTable?.GetValue($"skill_info_{type}", "") ?? "";

    /// <summary>设置技能描述（写入 stringtable 的 skill_info_{Type}）。</summary>
    public void SetSkillDesc(int type, string desc) => _stringTable?.SetValue($"skill_info_{type}", desc);

    public int GetNextId()
    {
        int max = 1000;
        foreach (var s in _skills) if (s.Id > max) max = s.Id;
        return max + 1;
    }

    public bool AddSkill(SkillSettingData skill)
    {
        if (GetById(skill.Id) != null) return false;
        _skills.Add(skill);
        return true;
    }

    public bool RemoveSkill(int id)
    {
        var s = GetById(id);
        if (s == null) return false;
        _skills.Remove(s);
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_skills, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[SkillSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillSettingParser] 保存失败: {ex.Message}");
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
/// 技能节点（SkillSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// skill_name_{Type} 与 skill_info_{Type} 键。
/// </summary>
public class SkillSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Series")] public int Series { get; set; }
    [JsonPropertyName("ActivatesChance")] public int ActivatesChance { get; set; }
    [JsonPropertyName("IfPercent")] public int IfPercent { get; set; }
    [JsonPropertyName("SkillEffect")] public int SkillEffect { get; set; }
    [JsonPropertyName("UpgradeId")] public int UpgradeId { get; set; }
    [JsonPropertyName("CostMedal")] public int CostMedal { get; set; }
    [JsonPropertyName("OpenDefault")] public int OpenDefault { get; set; }
    [JsonPropertyName("NeedStageId")] public int NeedStageId { get; set; }
    [JsonPropertyName("NeedScenarioId")] public int NeedScenarioId { get; set; }
    [JsonPropertyName("Score")] public int Score { get; set; }
    [JsonPropertyName("ArmyBuff")] public int ArmyBuff { get; set; }
    [JsonPropertyName("AurasRange")] public int AurasRange { get; set; }
}
