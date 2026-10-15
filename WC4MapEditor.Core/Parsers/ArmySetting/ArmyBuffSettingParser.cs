using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.ArmySetting;

/// <summary>
/// 军队 Buff 解析器（ArmyBuffSettings.json + stringtable_tw.ini 中的 army_buff_desc_{Id}）。
/// 采用与 CountryTechSettingParser 相同的单例与路径解析约定。
/// </summary>
public class ArmyBuffSettingParser
{
    private static readonly object _lock = new();
    private static ArmyBuffSettingParser? _instance;
    public static ArmyBuffSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ArmyBuffSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<ArmyBuffSettingData> _buffs = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<ArmyBuffSettingData> All => _buffs;
    public List<ArmyBuffSettingData> Items => _buffs;
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

    private ArmyBuffSettingParser()
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
            ConfigPath = Path.Combine(root, "json", "ArmyBuffSettings.json");
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

            ConfigPath = Path.Combine(candidate, "json", "ArmyBuffSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[ArmyBuffSettingParser] ConfigPath={ConfigPath} StringTablePath={StringTablePath}");
    }

    public void LoadAll()
    {
        LoadBuffs();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadBuffs()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[ArmyBuffSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<List<ArmyBuffSettingData>>(json, JsonOpts);
            if (data != null) _buffs = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyBuffSettingParser] 加载失败: {ex.Message}");
        }
    }

    public ArmyBuffSettingData? GetById(int id)
    {
        foreach (var b in _buffs) if (b.Id == id) return b;
        return null;
    }

    /// <summary>获取 Buff 描述（stringtable 的 army_buff_desc_{Id}）。</summary>
    public string GetBuffDesc(int id) => _stringTable?.GetValue($"army_buff_desc_{id}", "") ?? "";

    /// <summary>设置 Buff 描述（写入 stringtable 的 army_buff_desc_{Id}）。</summary>
    public void SetBuffDesc(int id, string desc) => _stringTable?.SetValue($"army_buff_desc_{id}", desc);

    public int GetNextId()
    {
        int max = 1000;
        foreach (var b in _buffs) if (b.Id > max) max = b.Id;
        return max + 1;
    }

    public bool AddBuff(ArmyBuffSettingData buff)
    {
        if (GetById(buff.Id) != null) return false;
        _buffs.Add(buff);
        return true;
    }

    public bool RemoveBuff(int id)
    {
        var b = GetById(id);
        if (b == null) return false;
        _buffs.Remove(b);
        _stringTable?.Remove($"army_buff_desc_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_buffs, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[ArmyBuffSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyBuffSettingParser] 保存失败: {ex.Message}");
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
/// 军队 Buff 节点（ArmyBuffSettings.json）。
/// 描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的 army_buff_desc_{Id} 键。
/// </summary>
public class ArmyBuffSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("Value")] public int Value { get; set; }
    [JsonPropertyName("Value2")] public int Value2 { get; set; }
    [JsonPropertyName("Debuff")] public bool Debuff { get; set; }
    [JsonPropertyName("Trigger")] public bool Trigger { get; set; }
    [JsonPropertyName("Hide")] public bool Hide { get; set; }
    [JsonPropertyName("ShowRounds")] public bool ShowRounds { get; set; }
}
