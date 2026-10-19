using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.ArmySetting;

/// <summary>
/// 军队 Buff 解析器（ArmyBuffSettings.json + stringtable_tw.ini 中的 army_buff_desc_{Id}）。
/// JSON 的读写与字段保持交给 <see cref="GameJsonTable{T}"/>，文本写入共享字符串表（记在本解析器名下）。
/// </summary>
public sealed class ArmyBuffSettingParser : IGameSettingsSource
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

    private ArmyBuffSettingParser()
    {
        Table = new GameJsonTable<ArmyBuffSettingData>(GameJson.AssetPath("json", "ArmyBuffSettings.json"));
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "army_buff_desc_{0}");
        LoadAll();
    }

    public GameJsonTable<ArmyBuffSettingData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>每条 Buff 的文本键：0 = 描述。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<ArmyBuffSettingData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public ArmyBuffSettingData? GetById(int id) => Items.FirstOrDefault(b => b.Id == id);

    /// <summary>获取 Buff 描述（stringtable 的 army_buff_desc_{Id}）。</summary>
    public string GetBuffDesc(int id) => Texts.Get(0, id);

    /// <summary>设置 Buff 描述（写入 stringtable 的 army_buff_desc_{Id}）。</summary>
    public void SetBuffDesc(int id, string desc) => Texts.Set(0, id, desc);

    public int GetNextId() => Math.Max(1000, Items.Count == 0 ? 0 : Items.Max(b => b.Id)) + 1;

    /// <summary>删除 Buff 及其描述文本。</summary>
    public void Remove(ArmyBuffSettingData buff)
    {
        Items.Remove(buff);
        Texts.Remove(buff.Id);
    }

    /// <summary>改 Id：新 Id 已被别的 Buff 占用时返回 false；描述文本跟着移到新 Id。</summary>
    public bool ChangeId(ArmyBuffSettingData buff, int newId)
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
