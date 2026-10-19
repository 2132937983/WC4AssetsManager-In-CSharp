using WC4MapEditor.Core.Parsers.ArmyGroupSetting;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Core.Services;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>
/// 一种被其它表引用的记录（国家、将领、兵种……）：怎么显示名称、选择器里列出哪些候选。
/// 表单字段（<see cref="ReferenceFields"/>）用它给数字 Id 旁边加上名称和选择按钮。
/// </summary>
public sealed class ReferenceKind
{
    /// <summary>选择对话框的标题。</summary>
    public string PickerTitle { get; init; } = "";

    /// <summary>候选项；为 null 的种类只显示名称，不提供选择按钮。</summary>
    public Func<IReadOnlyList<PickerEntry>>? Entries { get; init; }

    /// <summary>Id 对应的名称；记录不存在时返回 null（字段旁显示“找不到”的提醒），没有名称时返回空串。</summary>
    public required Func<int, string?> Describe { get; init; }

    /// <summary>字段允许 0 = 没有引用（选择对话框里出现“清除”）。</summary>
    public bool AllowClear { get; init; } = true;
}

/// <summary>各种引用的名称来源与候选列表（读的都是解析器里当前的内存数据，包括没有保存的修改）。</summary>
public static class ReferenceKinds
{
    public static ReferenceKind Country => new()
    {
        PickerTitle = Loc.T("Picker.Title.Country"),
        Entries = () =>
        {
            var parser = CountrySettingParser.Instance;
            return parser.Countries.OrderBy(c => c.Id).Select(c => new PickerEntry(c.Id, $"{c.Id} - {parser.GetCountryDisplayName(c.Id)}")).ToList();
        },
        Describe = id => CountrySettingParser.Instance.GetCountryById(id) == null ? null : CountrySettingParser.Instance.GetCountryDisplayName(id)
    };

    public static ReferenceKind General => new()
    {
        PickerTitle = Loc.T("Picker.Title.General"),
        Entries = () => GeneralSettingParser.Instance.Items.OrderBy(g => g.Id).Select(g => new PickerEntry(g.Id, $"{g.Id} - {g.Name} ({g.EName})")).ToList(),
        Describe = id => GeneralSettingParser.Instance.GetById(id) is { } g ? $"{g.Name} ({g.EName})" : null
    };

    public static ReferenceKind Unit => new()
    {
        PickerTitle = Loc.T("Picker.Title.Unit"),
        Entries = () => ArmySettingParser.Instance.Items.OrderBy(u => u.Id).Select(u => new PickerEntry(u.Id, $"{u.Id} - {UnitName(u)}")).ToList(),
        Describe = id => ArmySettingParser.Instance.GetById(id) is { } u ? UnitName(u) : null
    };

    public static ReferenceKind Skill => new()
    {
        PickerTitle = Loc.T("Picker.Title.Skill"),
        Entries = () => SkillSettingParser.Instance.Items.OrderBy(s => s.Id).Select(s => new PickerEntry(s.Id, $"{s.Id} - {SkillLabel(s)}")).ToList(),
        Describe = id => SkillSettingParser.Instance.GetById(id) is { } s ? SkillLabel(s) : null
    };

    public static ReferenceKind ArmyBuff => new()
    {
        PickerTitle = Loc.T("Picker.Title.ArmyBuff"),
        Entries = () => ArmyBuffSettingParser.Instance.Items.OrderBy(b => b.Id).Select(b => new PickerEntry(b.Id, $"{b.Id} - {ArmyBuffLabel(b)}")).ToList(),
        Describe = id => ArmyBuffSettingParser.Instance.GetById(id) is { } b ? ArmyBuffLabel(b) : null
    };

    /// <summary>事件 Buff：“[T 类型] 描述”，选择器里再带上数值、回合和附带的军队 Buff。</summary>
    public static ReferenceKind EventBuff => new()
    {
        PickerTitle = Loc.T("Picker.Title.EventBuff"),
        Entries = () => EventBuffSettingParser.Instance.Items.OrderBy(b => b.Id)
            .Select(b => new PickerEntry(b.Id, Loc.F("Picker.EventBuffRow", b.Id, EventBuffLabel(b), b.Value, b.Round, ListText.Format(b.ArmyBuffs)))).ToList(),
        Describe = id => EventBuffSettingParser.Instance.GetById(id) is { } b ? EventBuffLabel(b) : null
    };

    /// <summary>集团军事件组（活动）：名称来自 stringtable 的 event_{组 Id}，只显示名称。</summary>
    public static ReferenceKind EventGroup => new() { Describe = EventGroupName };

    /// <summary>事件组名称（活动介绍的第一行，过长时截断）；没有文本时返回空串。</summary>
    public static string EventGroupName(int groupId)
    {
        var line = ArmyGroupSettingParser.Instance.GetEventDesc(groupId).Split('\n', '\r')[0].Trim();
        return line.Length > 20 ? line[..20] + "…" : line;
    }

    private static string UnitName(ArmySettingData u)
    {
        var name = ArmySettingParser.Instance.GetUnitName(u.Id);
        return string.IsNullOrEmpty(name) ? u.Name : name;
    }

    private static string SkillLabel(SkillSettingData s)
    {
        var name = SkillSettingParser.Instance.GetSkillName(s.Type);
        return Loc.F("Picker.SkillLabel", string.IsNullOrEmpty(name) ? s.Name : name, s.Level);
    }

    private static string ArmyBuffLabel(ArmyBuffSettingData b)
    {
        var desc = ArmyBuffSettingParser.Instance.GetBuffDesc(b.Id);
        return string.IsNullOrEmpty(desc) ? b.Name : $"{b.Name}  {GameText.StripColorTags(desc)}";
    }

    private static string EventBuffLabel(EventBuffData b)
    {
        var desc = EventBuffSettingParser.Instance.GetBuffDesc(b.Id, b.Type);
        return Loc.F("Picker.EventBuffLabel", b.Type, GameText.StripColorTags(desc));
    }
}
