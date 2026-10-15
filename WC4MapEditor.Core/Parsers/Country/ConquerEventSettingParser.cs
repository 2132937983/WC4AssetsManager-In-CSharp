using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 征服事件解析器（ConquerEventSettings.json + stringtable_tw.ini 的
/// conquest_event_{Id} / conquest_event_intro_{Id}）。
/// </summary>
public class ConquerEventSettingParser
{
    private static readonly object _lock = new();
    private static ConquerEventSettingParser? _instance;
    public static ConquerEventSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConquerEventSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<ConquerEventData> _events = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<ConquerEventData> All => _events;
    public IReadOnlyList<ConquerEventData> Items => _events;
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

    private ConquerEventSettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        Debug.WriteLine($"[ConquerEventSettingParser] ResolvePaths 开始: manager.IsLoaded={_manager.IsLoaded}, manager.AssetsRoot={_manager.AssetsRoot}");
        if (!_manager.IsLoaded)
        {
            try { _manager.ScanDefault(); } catch (Exception ex) { Debug.WriteLine($"[ConquerEventSettingParser] ScanDefault 失败: {ex.Message}"); }
        }

        Debug.WriteLine($"[ConquerEventSettingParser] ScanDefault 后: manager.IsLoaded={_manager.IsLoaded}, manager.AssetsRoot={_manager.AssetsRoot}");
        var root = _manager.AssetsRoot;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            ConfigPath = Path.Combine(root, "json", "ConquerEventSettings.json");
            StringTablePath = Path.Combine(root, "stringtable_tw.ini");
        }
        else
        {
            var baseDir = AppContext.BaseDirectory;
            Debug.WriteLine($"[ConquerEventSettingParser] AssetsRoot 不可用，回退搜索. baseDir={baseDir}");
            string? candidate = null;
            string? current = baseDir;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
            {
                var test = Path.Combine(current, "Resource", "WC4DATA", "assets");
                if (Directory.Exists(test)) { candidate = test; break; }
                current = Path.GetDirectoryName(current);
            }
            candidate ??= Path.GetFullPath(Path.Combine(baseDir ?? "", "..", "..", "..", "..", "Resource", "WC4DATA", "assets"));

            ConfigPath = Path.Combine(candidate, "json", "ConquerEventSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[ConquerEventSettingParser] ConfigPath={ConfigPath} (存在={File.Exists(ConfigPath)}), StringTablePath={StringTablePath} (存在={File.Exists(StringTablePath)})");
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
                Debug.WriteLine($"[ConquerEventSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            Debug.WriteLine($"[ConquerEventSettingParser] 文件已读取, 长度={json.Length} 字符");
            var data = JsonSerializer.Deserialize<List<ConquerEventData>>(json, JsonOpts);
            if (data != null) _events = data;
            Debug.WriteLine($"[ConquerEventSettingParser] 反序列化完成: {_events.Count} 条事件");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquerEventSettingParser] 加载失败: {ex.Message}");
        }
    }

    public ConquerEventData? GetById(int id)
    {
        foreach (var e in _events) if (e.Id == id) return e;
        return null;
    }

    public string GetEventName(int id) => _stringTable?.GetValue($"conquest_event_{id}", "") ?? "";
    public void SetEventName(int id, string name) => _stringTable?.SetValue($"conquest_event_{id}", name);
    public string GetEventDesc(int id) => _stringTable?.GetValue($"event_intro_{id}", "") ?? "";
    public void SetEventDesc(int id, string desc) => _stringTable?.SetValue($"event_intro_{id}", desc);

    public int GetNextId()
    {
        int max = 0;
        foreach (var e in _events) if (e.Id > max) max = e.Id;
        return max + 1;
    }

    public bool AddEvent(ConquerEventData ev)
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
        _stringTable?.Remove($"conquest_event_{id}");
        _stringTable?.Remove($"event_intro_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_events, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[ConquerEventSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquerEventSettingParser] 保存失败: {ex.Message}");
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
/// 征服事件（ConquerEventSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// conquest_event_{Id} 与 conquest_event_intro_{Id} 键。
/// </summary>
public class ConquerEventData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("ConquerId")] public int ConquerId { get; set; }
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
}
