using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 事件 Buff 解析器（EventBuffSettings.json + stringtable_tw.ini 的 event_buff_desc_{Id}，
/// 没有按 Id 的描述时回退到 event_buff_desc_{Type}）。
/// </summary>
public sealed class EventBuffSettingParser : IGameSettingsSource
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

    private EventBuffSettingParser()
    {
        Table = new GameJsonTable<EventBuffData>(GameJson.AssetPath("json", "EventBuffSettings.json"));
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "event_buff_desc_{0}");
        LoadAll();
    }

    public GameJsonTable<EventBuffData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>每个事件 Buff 的文本键：0 = 描述（按 Id）。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<EventBuffData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public EventBuffData? GetById(int id) => Items.FirstOrDefault(b => b.Id == id);

    /// <summary>获取 Buff 描述：优先 event_buff_desc_{Id}，其次 event_buff_desc_{Type}。</summary>
    public string GetBuffDesc(int id, int type = -1)
    {
        var v = Texts.Get(0, id);
        if (!string.IsNullOrEmpty(v)) return v;
        return type >= 0 ? Texts.Get(0, type) : "";
    }

    /// <summary>按 Id 的描述（不回退），编辑框里显示 / 修改的就是它。</summary>
    public string GetOwnBuffDesc(int id) => Texts.Get(0, id);

    /// <summary>设置 Buff 描述（写入 event_buff_desc_{Id}）。</summary>
    public void SetBuffDesc(int id, string desc) => Texts.Set(0, id, desc);

    public int GetNextId() => (Items.Count == 0 ? 0 : Math.Max(0, Items.Max(b => b.Id))) + 1;

    /// <summary>删除 Buff 及其按 Id 的描述。</summary>
    public void Remove(EventBuffData buff)
    {
        Items.Remove(buff);
        Texts.Remove(buff.Id);
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false；按 Id 的描述跟着移动。</summary>
    public bool ChangeId(EventBuffData buff, int newId)
    {
        if (buff.Id == newId) return true;
        if (Items.Any(b => b.Id == newId)) return false;
        Texts.Move(buff.Id, newId);
        buff.Id = newId;
        return true;
    }

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
