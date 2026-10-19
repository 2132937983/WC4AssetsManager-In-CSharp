using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.ArmySetting;

/// <summary>
/// 兵种解析器（三文件联动：ArmySettings.json + config/def_armypos.xml +
/// stringtable_tw.ini 的 unit_name_{Id}）。
/// def_armypos.xml 只记录少数兵种的立绘偏移：只有用户真正改了某个兵种的偏移才会新增一行。
/// </summary>
public sealed class ArmySettingParser : IGameSettingsSource
{
    private static readonly object _lock = new();
    private static ArmySettingParser? _instance;
    public static ArmySettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ArmySettingParser();
                }
            }
            return _instance;
        }
    }

    private ArmySettingParser()
    {
        Table = new GameJsonTable<ArmySettingData>(GameJson.AssetPath("json", "ArmySettings.json"));
        PosTable = new XmlRowTable<ArmyPosEntry>(GameJson.AssetPath("config", "def_armypos.xml"), "units", "unit", ReadPos, WritePos);
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "unit_name_{0}");
        LoadAll();
    }

    public GameJsonTable<ArmySettingData> Table { get; }
    public XmlRowTable<ArmyPosEntry> PosTable { get; }
    public StringTableParser Strings { get; }

    /// <summary>每个兵种的文本键：0 = 名称。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table, PosTable];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<ArmySettingData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string XmlPosPath => PosTable.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll()
    {
        Table.Load();
        PosTable.Load();
    }

    public ArmySettingData? GetById(int id) => Items.FirstOrDefault(u => u.Id == id);

    public int GetNextId() => Math.Max(1000, Items.Count == 0 ? 0 : Items.Max(u => u.Id)) + 1;

    /// <summary>获取兵种名称（stringtable 的 unit_name_{Id}）。</summary>
    public string GetUnitName(int id) => Texts.Get(0, id);

    /// <summary>设置兵种名称（写入 stringtable 的 unit_name_{Id}）。</summary>
    public void SetUnitName(int id, string name) => Texts.Set(0, id, name);

    // ============================== def_armypos.xml ==============================

    public ArmyPosEntry? GetPos(int id) => PosTable.Entries.FirstOrDefault(p => p.Id == id);

    /// <summary>取兵种偏移，没有时新增一行默认值（只在用户修改偏移时调用）。</summary>
    public ArmyPosEntry EnsurePos(int id)
    {
        var pos = GetPos(id);
        if (pos != null) return pos;
        pos = new ArmyPosEntry { Id = id };
        PosTable.Entries.Add(pos);
        return pos;
    }

    public void RemovePos(int id) => PosTable.Entries.RemoveAll(p => p.Id == id);

    /// <summary>兵种 Id 改变时偏移行跟着改 Id。</summary>
    public void MovePos(int oldId, int newId)
    {
        var pos = GetPos(oldId);
        if (pos != null && oldId != newId) pos.Id = newId;
    }

    /// <summary>复制兵种时复制偏移行（原兵种有偏移才复制）。</summary>
    public void CopyPos(int fromId, int toId)
    {
        var source = GetPos(fromId);
        if (source == null) return;
        var target = EnsurePos(toId);
        target.PosX = source.PosX;
        target.PosY = source.PosY;
        target.Scale = source.Scale;
    }

    private static ArmyPosEntry? ReadPos(XElement el)
    {
        var id = (int?)el.Attribute("id");
        if (id == null) return null;
        return new ArmyPosEntry
        {
            Id = id.Value,
            PosX = (int?)el.Attribute("x") ?? 0,
            PosY = (int?)el.Attribute("y") ?? 0,
            Scale = (double?)el.Attribute("scale") ?? 1.0
        };
    }

    private static IReadOnlyList<KeyValuePair<string, string>> WritePos(ArmyPosEntry p) =>
    [
        new("id", p.Id.ToString(CultureInfo.InvariantCulture)),
        new("x", p.PosX.ToString(CultureInfo.InvariantCulture)),
        new("y", p.PosY.ToString(CultureInfo.InvariantCulture)),
        new("scale", p.Scale.ToString("0.0######", CultureInfo.InvariantCulture))
    ];

    // ============================== 记录操作 ==============================

    /// <summary>删除兵种：同时删除名称与 def_armypos.xml 里的偏移行。</summary>
    public void Remove(ArmySettingData unit)
    {
        Items.Remove(unit);
        RemovePos(unit.Id);
        Texts.Remove(unit.Id);
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false；名称与偏移行跟着改到新 Id。</summary>
    public bool ChangeId(ArmySettingData unit, int newId)
    {
        if (unit.Id == newId) return true;
        if (Items.Any(u => u.Id == newId)) return false;
        Texts.Move(unit.Id, newId);
        MovePos(unit.Id, newId);
        unit.Id = newId;
        return true;
    }

    /// <summary>复制兵种时复制名称与偏移行。</summary>
    public void CopyExtras(int fromId, int toId)
    {
        Texts.Copy(fromId, toId);
        CopyPos(fromId, toId);
    }

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>
/// 兵种节点（ArmySettings.json）。
/// 名称不保存在 JSON 中，而是写入 stringtable_tw.ini 的 unit_name_{Id} 键。
/// </summary>
public class ArmySettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Anim")] public string Anim { get; set; } = "";
    [JsonPropertyName("Army")] public int Army { get; set; }
    [JsonPropertyName("SubType")] public int SubType { get; set; }
    [JsonPropertyName("Elite")] public int Elite { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Ranking")] public int Ranking { get; set; }
    [JsonPropertyName("Feature")] public List<int> Feature { get; set; } = new();
    [JsonPropertyName("FeatureLevel")] public List<int> FeatureLevel { get; set; } = new();
    [JsonPropertyName("MinAttack")] public int MinAttack { get; set; }
    [JsonPropertyName("MaxAttack")] public int MaxAttack { get; set; }
    [JsonPropertyName("MinRange")] public int MinRange { get; set; }
    [JsonPropertyName("MaxRange")] public int MaxRange { get; set; }
    [JsonPropertyName("HP")] public int HP { get; set; }
    [JsonPropertyName("Defence")] public int Defence { get; set; }
    [JsonPropertyName("Mobility")] public int Mobility { get; set; }
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("CostPoints")] public int CostPoints { get; set; }
    [JsonPropertyName("MaxElite")] public int MaxElite { get; set; }
    [JsonPropertyName("MaxFormation")] public int MaxFormation { get; set; }
    [JsonPropertyName("Carrier")] public int Carrier { get; set; }
    [JsonPropertyName("BuildTime")] public int BuildTime { get; set; }
    [JsonPropertyName("BuildCD")] public int BuildCD { get; set; }
    [JsonPropertyName("AOE1")] public int AOE1 { get; set; }
    [JsonPropertyName("AOE2")] public int AOE2 { get; set; }
    [JsonPropertyName("Country")] public List<int> Country { get; set; } = new();
    [JsonPropertyName("FormationChance")] public List<int> FormationChance { get; set; } = new();
    [JsonPropertyName("MeritExp")] public int MeritExp { get; set; }
    [JsonPropertyName("Formation")] public bool Formation { get; set; }
}

/// <summary>def_armypos.xml 中单个兵种的展示坐标与缩放。</summary>
public class ArmyPosEntry
{
    public int Id { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public double Scale { get; set; } = 1.0;
}
