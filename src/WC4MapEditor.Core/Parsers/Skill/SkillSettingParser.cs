using System.Globalization;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Skill;

/// <summary>
/// 技能解析器（SkillSettings.json + stringtable_tw.ini 中的 skill_name_{Type} / skill_info_{Type}）。
/// 技能名 / 描述按 Type（技能效果）索引，同一 Type 的各级技能共用文本。
/// </summary>
public sealed class SkillSettingParser : IGameSettingsSource
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

    private SkillSettingParser()
    {
        Table = new GameJsonTable<SkillSettingData>(GameJson.AssetPath("json", "SkillSettings.json"));
        Strings = StringTableStore.Default;
        TypeTexts = new RecordTextKeys(Strings, this, "skill_name_{0}", "skill_info_{0}");
        LoadAll();
    }

    public GameJsonTable<SkillSettingData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>按技能 Type 的文本键：0 = 名称，1 = 描述。</summary>
    public RecordTextKeys TypeTexts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<SkillSettingData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public SkillSettingData? GetById(int id) => Items.FirstOrDefault(s => s.Id == id);

    /// <summary>获取技能名（stringtable 的 skill_name_{Type}）。</summary>
    public string GetSkillName(int type) => TypeTexts.Get(0, type);

    /// <summary>设置技能名（写入 stringtable 的 skill_name_{Type}）。</summary>
    public void SetSkillName(int type, string name) => TypeTexts.Set(0, type, name);

    /// <summary>获取技能描述（stringtable 的 skill_info_{Type}）。</summary>
    public string GetSkillDesc(int type) => TypeTexts.Get(1, type);

    /// <summary>设置技能描述（写入 stringtable 的 skill_info_{Type}）。</summary>
    public void SetSkillDesc(int type, string desc) => TypeTexts.Set(1, type, desc);

    /// <summary>技能描述（skill_info_{Type}）：%d 换成该技能的 SkillEffect，%% 还原成 %，去掉 [#RRGGBB#文字] 颜色标记。</summary>
    public string GetSkillEffectText(SkillSettingData skill)
    {
        var text = GetSkillDesc(skill.Type).Replace("%d", skill.SkillEffect.ToString(CultureInfo.InvariantCulture));
        return GameText.StripColorTags(text).Replace("%%", "%");
    }

    /// <summary>技能图标 general_skill_{Type}（.webp 或 .png）；没有时返回 null。</summary>
    public string? GetIconPath(int type)
    {
        foreach (var extension in new[] { "webp", "png" })
        {
            var path = GameJson.AssetPath("image", "general_skill", $"general_skill_{type}.{extension}");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public int GetNextId() => Math.Max(1000, Items.Count == 0 ? 0 : Items.Max(s => s.Id)) + 1;

    /// <summary>改 Id：新 Id 已被占用时返回 false。名称 / 描述按 Type 共享，不随 Id 移动。</summary>
    public bool ChangeId(SkillSettingData skill, int newId)
    {
        if (skill.Id == newId) return true;
        if (Items.Any(s => s.Id == newId)) return false;
        skill.Id = newId;
        return true;
    }

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
