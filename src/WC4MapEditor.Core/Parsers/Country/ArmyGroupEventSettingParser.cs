using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 集团军事件解析器（ArmyGroupEventSettings.json + stringtable_tw.ini 的
/// armygroup_event_{Id} / armygroup_event_desc_{Id}）。
/// </summary>
public sealed class ArmyGroupEventSettingParser : IGameSettingsSource
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

    private ArmyGroupEventSettingParser()
    {
        Table = new GameJsonTable<ArmyGroupEventData>(GameJson.AssetPath("json", "ArmyGroupEventSettings.json"));
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "armygroup_event_{0}", "armygroup_event_desc_{0}");
        LoadAll();
    }

    public GameJsonTable<ArmyGroupEventData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>每个事件的文本键：0 = 标题，1 = 描述。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<ArmyGroupEventData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public ArmyGroupEventData? GetById(int id) => Items.FirstOrDefault(e => e.Id == id);

    public string GetEventName(int id) => Texts.Get(0, id);
    public void SetEventName(int id, string name) => Texts.Set(0, id, name);
    public string GetEventDesc(int id) => Texts.Get(1, id);
    public void SetEventDesc(int id, string desc) => Texts.Set(1, id, desc);

    public int GetNextId() => (Items.Count == 0 ? 0 : Math.Max(0, Items.Max(e => e.Id))) + 1;

    /// <summary>删除事件及其标题 / 描述文本。</summary>
    public void Remove(ArmyGroupEventData ev)
    {
        Items.Remove(ev);
        Texts.Remove(ev.Id);
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false；标题 / 描述跟着移到新 Id。</summary>
    public bool ChangeId(ArmyGroupEventData ev, int newId)
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
/// 集团军事件（ArmyGroupEventSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// armygroup_event_{Id} 与 armygroup_event_desc_{Id} 键。
/// </summary>
public class ArmyGroupEventData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("EventId")] public int EventId { get; set; }
    [JsonPropertyName("StageId")] public int StageId { get; set; }
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
