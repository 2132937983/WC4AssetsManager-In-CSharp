using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 部队数据结构 v1/v2（48 字节）- 使用结构体减少内存开销。
/// <para>
/// 记录里的每个字节都有对应字段并原样读写（含含义未确认的 Reserved*），"读入 → 保存"不丢失原版地图的数据。
/// 0x00-0x2F 的排布与 v3（<see cref="Army_3"/>）一致，Reserved* 的注释里标出了 v3 同位字段的名字。
/// </para>
/// <para>
/// <see cref="LegionId"/> 是编辑器运行期字段，不在文件里：读入时为 <see cref="UnassignedLegion"/>，保存时不写。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Army
{
    /// <summary><see cref="LegionId"/> 的"未指定"值：单位的归属按它所在格子的归属值算（v1 / v3 通用）。</summary>
    public const int UnassignedLegion = -1;

    public short Coordinate;
    public byte UnitType;
    public byte Level;
    public byte Organization;
    public byte Direction;
    public byte Mobility;
    public byte BuiltRound;
    public short Experience;
    public short HealthBonus;
    public short CurrentHealth;
    public short MaxHealth;
    public short General;
    public byte Rank;
    public byte Nobility;
    public byte Badge1;
    public byte Badge2;
    public byte Badge3;
    public byte SkillLevel1;
    public byte SkillLevel2;
    public byte SkillLevel3;
    public byte SkillLevel4;
    public byte SkillLevel5;
    public byte KeyPoint;
    public byte Policy;
    public byte OccupationEvent;
    /// <summary>0x1F：含义未确认（v3 同位为 Field1F），原样保留。</summary>
    public byte Reserved1;
    /// <summary>0x20-0x21：含义未确认（原版数据多为 -1；v3 同位为仇恨值 Hatred），原样保留。</summary>
    public short Reserved2;
    public short Plan;
    /// <summary>0x24-0x25：含义未确认（v3 同位为行为方案 Plan），原样保留。</summary>
    public short Reserved3;
    public short ChangeRound;
    public byte Morale;
    public byte Duration;
    public byte Dialogue;
    public byte CanAttack;
    /// <summary>0x2C-0x2F：含义未确认（原版数据多为 2~6；v3 同位为坚守模式 AntiDefenseMode），原样保留。</summary>
    public int Reserved4;

    /// <summary>
    /// 编辑器运行期的所属军团（归属值），由 L 键、按概率生成等操作设置。不读写文件：
    /// 文件里 0x2C-0x2F 是游戏数据（见 <see cref="Reserved4"/>）。读入的单位与新建的默认单位为
    /// <see cref="UnassignedLegion"/>（按所在格子的归属算），不会被当成归属 0 的单位。
    /// </summary>
    public int LegionId;

    /// <summary>格子序号（按无符号读取：大地图上的序号可能超过 32767）。</summary>
    public readonly int CoordinateIndex => (ushort)Coordinate;

    /// <summary>
    /// 从字节数组解析部队数据
    /// </summary>
    public static Army FromBytes(ReadOnlySpan<byte> data, int offset)
    {
        if (data.Length < offset + 48) return default;

        return new Army
        {
            Coordinate = BitConverter.ToInt16(data[offset..]),
            UnitType = data[offset + 2],
            Level = data[offset + 3],
            Organization = data[offset + 4],
            Direction = data[offset + 5],
            Mobility = data[offset + 6],
            BuiltRound = data[offset + 7],
            Experience = BitConverter.ToInt16(data[(offset + 8)..]),
            HealthBonus = BitConverter.ToInt16(data[(offset + 10)..]),
            CurrentHealth = BitConverter.ToInt16(data[(offset + 12)..]),
            MaxHealth = BitConverter.ToInt16(data[(offset + 14)..]),
            General = BitConverter.ToInt16(data[(offset + 16)..]),
            Rank = data[offset + 18],
            Nobility = data[offset + 19],
            Badge1 = data[offset + 20],
            Badge2 = data[offset + 21],
            Badge3 = data[offset + 22],
            SkillLevel1 = data[offset + 23],
            SkillLevel2 = data[offset + 24],
            SkillLevel3 = data[offset + 25],
            SkillLevel4 = data[offset + 26],
            SkillLevel5 = data[offset + 27],
            KeyPoint = data[offset + 28],
            Policy = data[offset + 29],
            OccupationEvent = data[offset + 30],
            Reserved1 = data[offset + 31],
            Reserved2 = BitConverter.ToInt16(data[(offset + 32)..]),
            Plan = BitConverter.ToInt16(data[(offset + 34)..]),
            Reserved3 = BitConverter.ToInt16(data[(offset + 36)..]),
            ChangeRound = BitConverter.ToInt16(data[(offset + 38)..]),
            Morale = data[offset + 40],
            Duration = data[offset + 41],
            Dialogue = data[offset + 42],
            CanAttack = data[offset + 43],
            Reserved4 = BitConverter.ToInt32(data[(offset + 44)..]),
            LegionId = UnassignedLegion
        };
    }

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public readonly void ToBytes(Span<byte> data, int offset)
    {
        BitConverter.TryWriteBytes(data[offset..], Coordinate);
        data[offset + 2] = UnitType;
        data[offset + 3] = Level;
        data[offset + 4] = Organization;
        data[offset + 5] = Direction;
        data[offset + 6] = Mobility;
        data[offset + 7] = BuiltRound;
        BitConverter.TryWriteBytes(data[(offset + 8)..], Experience);
        BitConverter.TryWriteBytes(data[(offset + 10)..], HealthBonus);
        BitConverter.TryWriteBytes(data[(offset + 12)..], CurrentHealth);
        BitConverter.TryWriteBytes(data[(offset + 14)..], MaxHealth);
        BitConverter.TryWriteBytes(data[(offset + 16)..], General);
        data[offset + 18] = Rank;
        data[offset + 19] = Nobility;
        data[offset + 20] = Badge1;
        data[offset + 21] = Badge2;
        data[offset + 22] = Badge3;
        data[offset + 23] = SkillLevel1;
        data[offset + 24] = SkillLevel2;
        data[offset + 25] = SkillLevel3;
        data[offset + 26] = SkillLevel4;
        data[offset + 27] = SkillLevel5;
        data[offset + 28] = KeyPoint;
        data[offset + 29] = Policy;
        data[offset + 30] = OccupationEvent;
        data[offset + 31] = Reserved1;
        BitConverter.TryWriteBytes(data[(offset + 32)..], Reserved2);
        BitConverter.TryWriteBytes(data[(offset + 34)..], Plan);
        BitConverter.TryWriteBytes(data[(offset + 36)..], Reserved3);
        BitConverter.TryWriteBytes(data[(offset + 38)..], ChangeRound);
        data[offset + 40] = Morale;
        data[offset + 41] = Duration;
        data[offset + 42] = Dialogue;
        data[offset + 43] = CanAttack;
        BitConverter.TryWriteBytes(data[(offset + 44)..], Reserved4);
    }

    /// <summary>
    /// 创建默认部队
    /// </summary>
    public static Army CreateDefault(int coord) => new Army
    {
        Coordinate = (short)coord,
        // 兵种 0 表示"无兵种"：等级/编制非 0 时规则检查会把它当残留参数清掉，所以默认给一个合法兵种
        UnitType = BTLRuleChecker.DefaultUnitType,
        Level = 1,
        Organization = 1,
        Direction = 0,
        Mobility = 0,
        BuiltRound = 0,
        Experience = 0,
        // HealthBonus 的基准值是 100（=100%）。填 0 会让血量上限失效、血条显示异常，
        // 这里与 Army_3.CreateDefault 保持一致。
        HealthBonus = 100,
        CurrentHealth = 100,
        MaxHealth = 100,
        General = 0,
        Rank = 0,
        Nobility = 0,
        Badge1 = 0,
        Badge2 = 0,
        Badge3 = 0,
        SkillLevel1 = 0,
        SkillLevel2 = 0,
        SkillLevel3 = 0,
        SkillLevel4 = 0,
        SkillLevel5 = 0,
        KeyPoint = 0,
        Policy = 0,
        OccupationEvent = 0,
        Plan = 0,
        ChangeRound = 0,
        Morale = 0,
        Duration = 0,
        Dialogue = 0,
        CanAttack = 1,
        LegionId = UnassignedLegion
    };

    /// <summary>
    /// 获取方向名称
    /// </summary>
    public readonly string GetDirectionName() => Direction switch
    {
        0 => "左",
        1 => "右",
        _ => $"未知({Direction})"
    };

    /// <summary>
    /// 获取关键据点名称
    /// </summary>
    public readonly string GetKeyPointName() => KeyPoint switch
    {
        0 => "否",
        1 => "红圈",
        2 => "绿圈",
        _ => $"未知({KeyPoint})"
    };

    /// <summary>
    /// 获取政策名称
    /// </summary>
    public readonly string GetPolicyName() => Policy switch
    {
        0 => "自由行动",
        1 => "猛攻",
        2 => "缓慢进攻",
        3 => "坚守",
        4 => "撤退",
        _ => $"未知({Policy})"
    };

    /// <summary>
    /// 获取士气名称
    /// </summary>
    public readonly string GetMoraleName() => Morale switch
    {
        0 => "正常",
        1 => "士气上升",
        253 => "混乱",
        254 => "士气双降",
        255 => "士气下降",
        _ => $"未知({Morale})"
    };

    /// <summary>
    /// 获取兵种名称（CLI / 脚本输出用）
    /// </summary>
    public readonly string GetUnitTypeName()
        => ResolveUnitTypeName(UnitType) ?? (UnitType == 0 ? "无兵种" : $"未知({UnitType})");

    /// <summary>精英兵种在 string table 里的名字带 "LvN" 后缀（如 "鹰眼部队Lv1"），单位等级另有字段，显示兵种时去掉。</summary>
    private static readonly Regex UnitLevelSuffix = new(@"\s*Lv\d+$", RegexOptions.Compiled);

    /// <summary>
    /// 兵种（BTL 的 UnitType = ArmySettings.json 的 Army 字段）的显示名，v1 / v3 通用。
    /// <para>
    /// 同一兵种在 ArmySettings.json 里可能有多个条目（各国特种兵、超级坦克等），取 Id 最小的那条，
    /// 名字用 string table 的 <c>unit_name_{Id}</c>（同兵种各条目在这里是通用名），没有时退回 JSON 的 Name。
    /// 兵种为 0（无兵种）或不在配置表里时返回 null。
    /// </para>
    /// </summary>
    public static string? ResolveUnitTypeName(int unitType)
    {
        if (unitType <= 0) return null;

        var parser = ArmySettingParser.Instance;
        ArmySettingData? first = null;
        foreach (var unit in parser.Items)
        {
            if (unit.Army == unitType && (first == null || unit.Id < first.Id))
                first = unit;
        }
        if (first == null) return null;

        string name = parser.GetUnitName(first.Id);
        if (!string.IsNullOrWhiteSpace(name)) return UnitLevelSuffix.Replace(name, "");
        return string.IsNullOrWhiteSpace(first.Name) ? null : first.Name;
    }
}

/// <summary>规则检查涉及的单位字段。v1 的 Nobility（爵位）与 v3 的 HpLevel 是同一字节、同一含义（将领 HP 等级），都算 <see cref="HpLevel"/>。</summary>
public enum ArmyRuleField
{
    UnitType,
    Level,
    Organization,
    Direction,
    HealthBonus,
    General,
    Rank,
    HpLevel,
    Skill1,
    Skill2,
    Skill3,
    Skill4,
    Skill5,
    KeyPoint
}

/// <summary>一条规则提示：对应的字段 + 已本地化的说明。</summary>
public readonly record struct ArmyRuleIssue(ArmyRuleField Field, string Message);

/// <summary>规则检查的输入：v1 / v3 单位共有的玩法字段。</summary>
public readonly record struct ArmyRuleValues(
    int UnitType, int Level, int Organization, int Direction, int HealthBonus,
    int General, int Rank, int HpLevel,
    int Skill1, int Skill2, int Skill3, int Skill4, int Skill5,
    int KeyPoint);

/// <summary>
/// 单位的玩法规则（软约束）：与 <see cref="BTLRuleChecker"/> 的单位修复规则同源，只给出提示、不改数据。
/// 单位设置对话框用它显示警告；CLI / Lua 的字段写入也可以复用。
/// <para>"要塞类兵种不能放在建筑上" 需要地图上下文，仍由 <see cref="BTLRuleChecker"/> 检查。</para>
/// </summary>
public static class ArmyRules
{
    public static List<ArmyRuleIssue> Check(in ArmyRuleValues v)
    {
        var issues = new List<ArmyRuleIssue>();

        if (v.UnitType == 0)
        {
            if (v.Level != 0 || v.Organization != 0 || v.General != 0)
                issues.Add(new ArmyRuleIssue(ArmyRuleField.UnitType,
                    Loc.T("Assist.Army.Rule.NoUnitType", "无兵种时等级、编制、将领应为 0")));
            return issues;
        }

        var armyIds = ConfigManager.Instance.GetArmyTypeIds();
        if (armyIds.Count > 0 && !armyIds.Contains(v.UnitType))
            issues.Add(new ArmyRuleIssue(ArmyRuleField.UnitType, Loc.F("Assist.Army.Rule.UnknownUnitType", v.UnitType)));

        CheckRange(issues, ArmyRuleField.Level, v.Level, 1, BTLRuleChecker.MaxUnitLevel);
        CheckRange(issues, ArmyRuleField.Organization, v.Organization, 1, BTLRuleChecker.MaxOrganization);
        CheckRange(issues, ArmyRuleField.Direction, v.Direction, 0, 1);
        if (v.HealthBonus < 1)
            issues.Add(new ArmyRuleIssue(ArmyRuleField.HealthBonus, Loc.T("Assist.Army.Rule.HealthBonus", "血量加成应不小于 1（100 = 100%）")));

        if (v.General != 0)
        {
            var general = GeneralSettingParser.Instance.GetById(v.General);
            if (general == null)
                issues.Add(new ArmyRuleIssue(ArmyRuleField.General, Loc.F("Assist.Army.Rule.UnknownGeneral", v.General)));
            CheckRange(issues, ArmyRuleField.Rank, v.Rank, BTLRuleChecker.MinRank, BTLRuleChecker.MaxRank);
            CheckRange(issues, ArmyRuleField.HpLevel, v.HpLevel, BTLRuleChecker.MinNobility, BTLRuleChecker.MaxNobility);

            // 将领没有的技能槽在原版地图里是 0（统计：3252 个这样的槽里只有 2 个不是 0），所以只有将领确实有该技能时才要求 1~5；
            // 查不到将领时不知道技能数，按"可以为 0"处理（上面已经提示将领不存在）
            int knownSkills = general?.Skills?.Count ?? 0;
            int[] skills = [v.Skill1, v.Skill2, v.Skill3, v.Skill4, v.Skill5];
            for (int slot = 0; slot < skills.Length; slot++)
            {
                int min = slot < knownSkills ? BTLRuleChecker.MinSkill : 0;
                CheckRange(issues, ArmyRuleField.Skill1 + slot, skills[slot], min, BTLRuleChecker.MaxSkill);
            }
        }
        else
        {
            string noGeneral = Loc.T("Assist.Army.Rule.NoGeneral", "无将领时应为 0");
            if (v.Rank != 0) issues.Add(new ArmyRuleIssue(ArmyRuleField.Rank, noGeneral));
            if (v.HpLevel != 0) issues.Add(new ArmyRuleIssue(ArmyRuleField.HpLevel, noGeneral));
        }

        CheckRange(issues, ArmyRuleField.KeyPoint, v.KeyPoint, 0, BTLRuleChecker.MaxKeyPoint);
        return issues;
    }

    private static void CheckRange(List<ArmyRuleIssue> issues, ArmyRuleField field, int value, int min, int max)
    {
        if (value < min || value > max)
            issues.Add(new ArmyRuleIssue(field, Loc.F("Assist.Army.Rule.Range", min, max)));
    }
}
