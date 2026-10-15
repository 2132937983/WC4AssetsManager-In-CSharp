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
/// 事件 Buff 解析器（EventBuffSettings.json + stringtable_tw.ini 的 event_buff_desc_{Id}）。
/// 与 CountryTechSettingParser 采用相同的单例与路径解析约定。
/// </summary>
public class EventBuffSettingParser
{
    private static readonly object _lock = new();
    private static EventBuffSettingParser? _instance;
    public static EventBuffSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EventBuffSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;
    private List<EventBuffData> _buffs = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<EventBuffData> All => _buffs;
    public IReadOnlyList<EventBuffData> Items => _buffs;
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

    private EventBuffSettingParser()
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
            ConfigPath = Path.Combine(root, "json", "EventBuffSettings.json");
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

            ConfigPath = Path.Combine(candidate, "json", "EventBuffSettings.json");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[EventBuffSettingParser] ConfigPath={ConfigPath} StringTablePath={StringTablePath}");
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
                Debug.WriteLine($"[EventBuffSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<List<EventBuffData>>(json, JsonOpts);
            if (data != null) _buffs = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventBuffSettingParser] 加载失败: {ex.Message}");
        }
    }

    public EventBuffData? GetById(int id)
    {
        foreach (var b in _buffs) if (b.Id == id) return b;
        return null;
    }

    /// <summary>获取 Buff 描述：优先 event_buff_desc_{Id}，其次 event_buff_desc_{Type}。</summary>
    public string GetBuffDesc(int id, int type = -1)
    {
        var v = _stringTable?.GetValue($"event_buff_desc_{id}", "");
        if (!string.IsNullOrEmpty(v)) return v!;
        if (type >= 0)
        {
            var v2 = _stringTable?.GetValue($"event_buff_desc_{type}", "");
            if (!string.IsNullOrEmpty(v2)) return v2!;
        }
        return "";
    }

    /// <summary>设置 Buff 描述（写入 event_buff_desc_{Id}）。</summary>
    public void SetBuffDesc(int id, string desc) => _stringTable?.SetValue($"event_buff_desc_{id}", desc);

    public int GetNextId()
    {
        int max = 0;
        foreach (var b in _buffs) if (b.Id > max) max = b.Id;
        return max + 1;
    }

    public bool AddBuff(EventBuffData buff)
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
        _stringTable?.Remove($"event_buff_desc_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            var json = JsonSerializer.Serialize(_buffs, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            _stringTable?.Save();
            Debug.WriteLine($"[EventBuffSettingParser] 保存成功: {ConfigPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventBuffSettingParser] 保存失败: {ex.Message}");
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
/// 事件 Buff（EventBuffSettings.json）。
/// 描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的 event_buff_desc_{Id}（Type 回退）。
/// </summary>
public class EventBuffData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Value")] public int Value { get; set; }
    [JsonPropertyName("Round")] public int Round { get; set; }
    [JsonPropertyName("ArmyBuffs")] public List<int> ArmyBuffs { get; set; } = new();
}
