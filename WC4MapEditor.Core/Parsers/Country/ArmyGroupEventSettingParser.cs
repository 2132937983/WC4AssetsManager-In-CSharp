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
/// 集团军事件解析器（ArmyGroupEventSettings.json + stringtable_tw.ini 的
/// armygroup_event_{Id} / armygroup_event_desc_{Id}）。
/// </summary>
public class ArmyGroupEventSettingParser
{
    private static readonly object _lock = new();
    private static ArmyGroupEventSettingParser? _instance;
    public static ArmyGroupEventSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ArmyGroupEventSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<ArmyGroupEventData> _events = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<ArmyGroupEventData> All => _events;
    public IReadOnlyList<ArmyGroupEventData> Items => _events;
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

    private ArmyGroupEventSettingParser()
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
            ConfigPath = Path.Combine(root, "json", "ArmyGroupEventSettings.json");
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

            ConfigPath = Path.Combine(candidate, "json", "ArmyGroupEventSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[ArmyGroupEventSettingParser] ConfigPath={ConfigPath} StringTablePath={StringTablePath}");
    }

    public void LoadAll()
    {
        LoadEvents();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadEvents()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[ArmyGroupEventSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<List<ArmyGroupEventData>>(json, JsonOpts);
            if (data != null) _events = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyGroupEventSettingParser] 加载失败: {ex.Message}");
        }
    }

    public ArmyGroupEventData? GetById(int id)
    {
        foreach (var e in _events) if (e.Id == id) return e;
        return null;
    }

    public string GetEventName(int id) => _stringTable?.GetValue($"armygroup_event_{id}", "") ?? "";
    public void SetEventName(int id, string name) => _stringTable?.SetValue($"armygroup_event_{id}", name);
    public string GetEventDesc(int id) => _stringTable?.GetValue($"armygroup_event_desc_{id}", "") ?? "";
    public void SetEventDesc(int id, string desc) => _stringTable?.SetValue($"armygroup_event_desc_{id}", desc);

    public int GetNextId()
    {
        int max = 0;
        foreach (var e in _events) if (e.Id > max) max = e.Id;
        return max + 1;
    }

    public bool AddEvent(ArmyGroupEventData ev)
    {
        if (GetById(ev.Id) != null) return false;
        _events.Add(ev);
        return true;
    }

    public bool RemoveEvent(int id)
    {
        var e = GetById(id);
        if (e == null) return false;
        _events.Remove(e);
        _stringTable?.Remove($"armygroup_event_{id}");
        _stringTable?.Remove($"armygroup_event_desc_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_events, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[ArmyGroupEventSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyGroupEventSettingParser] 保存失败: {ex.Message}");
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
/// 集团军事件（ArmyGroupEventSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// armygroup_event_{Id} 与 armygroup_event_desc_{Id} 键。
/// </summary>
public class ArmyGroupEventData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("EventId")] public int EventId { get; set; }
    [JsonPropertyName("EventBuffId1")] public int EventBuffId1 { get; set; }
    [JsonPropertyName("Round1")] public int Round1 { get; set; }
    [JsonPropertyName("CountryId1")] public List<int> CountryId1 { get; set; } = new();
    [JsonPropertyName("EventBuffId2")] public int EventBuffId2 { get; set; }
    [JsonPropertyName("Round2")] public int Round2 { get; set; }
    [JsonPropertyName("CountryId2")] public List<int> CountryId2 { get; set; } = new();
    [JsonPropertyName("Trigger")] public int Trigger { get; set; }
    [JsonPropertyName("TriggerValue")] public List<int> TriggerValue { get; set; } = new();
    [JsonPropertyName("Location")] public List<int> Location { get; set; } = new();
    [JsonPropertyName("Chance")] public int Chance { get; set; }
    [JsonPropertyName("PlayerCountry")] public List<int> PlayerCountry { get; set; } = new();
    [JsonPropertyName("DisableEvents")] public List<int> DisableEvents { get; set; } = new();
}
