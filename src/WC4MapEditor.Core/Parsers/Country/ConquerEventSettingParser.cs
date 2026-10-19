using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 征服事件解析器（ConquerEventSettings.json + stringtable_tw.ini 的
/// conquest_event_{Id} / conquest_event_desc_{Id}）。
/// <para>
/// 描述键是 conquest_event_desc_{Id}（127 个征服事件全都有）。旧实现误用 event_intro_{Id}，
/// 那是活动介绍（例如曼纳海姆防线），与征服事件 101–109 撞号，编辑 / 删除会改坏活动文本。
/// </para>
/// </summary>
public sealed class ConquerEventSettingParser : IGameSettingsSource
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

    private ConquerEventSettingParser()
    {
        Table = new GameJsonTable<ConquerEventData>(GameJson.AssetPath("json", "ConquerEventSettings.json"));
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "conquest_event_{0}", "conquest_event_desc_{0}");
        LoadAll();
    }

    public GameJsonTable<ConquerEventData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>每个事件的文本键：0 = 标题，1 = 描述。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<ConquerEventData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public ConquerEventData? GetById(int id) => Items.FirstOrDefault(e => e.Id == id);

    public string GetEventName(int id) => Texts.Get(0, id);
    public void SetEventName(int id, string name) => Texts.Set(0, id, name);
    public string GetEventDesc(int id) => Texts.Get(1, id);
    public void SetEventDesc(int id, string desc) => Texts.Set(1, id, desc);

    public int GetNextId() => (Items.Count == 0 ? 0 : Math.Max(0, Items.Max(e => e.Id))) + 1;

    /// <summary>删除事件及其标题 / 描述文本。</summary>
    public void Remove(ConquerEventData ev)
    {
        Items.Remove(ev);
        Texts.Remove(ev.Id);
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false；标题 / 描述跟着移到新 Id。</summary>
    public bool ChangeId(ConquerEventData ev, int newId)
    {
        if (ev.Id == newId) return true;
        if (Items.Any(e => e.Id == newId)) return false;
        Texts.Move(ev.Id, newId);
        ev.Id = newId;
        return true;
    }

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>
/// 征服事件（ConquerEventSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// conquest_event_{Id} 与 conquest_event_desc_{Id} 键。
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
