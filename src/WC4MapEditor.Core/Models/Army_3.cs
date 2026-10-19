using System.Runtime.InteropServices;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 部队数据结构 v3 (64字节) - 使用结构体减少内存开销。
/// <para>
/// 记录里的每个字节都原样读写：规范里标为填充的 0x36-0x3F 在原版地图里也常有非零值，
/// 用 <see cref="Padding36"/> / <see cref="Padding3E"/> 保留，"读入 → 保存"不丢数据。
/// </para>
/// <para>
/// <see cref="LegionId"/> 是编辑器运行期字段，不在文件里：读入时为 <see cref="Army.UnassignedLegion"/>，保存时不写。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Army_3
{
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
    public byte HpLevel;
    public byte Waste1;
    public byte Waste2;
    public byte Waste3;
    public byte SkillLevel1;
    public byte SkillLevel2;
    public byte SkillLevel3;
    public byte SkillLevel4;
    public byte SkillLevel5;
    public byte KeyPoint;
    public byte Policy;
    public byte TransportShip;
    public byte Field1F;
    public short Hatred;
    public short AttackTarget;
    public short Plan;
    public short ChangeRound;
    public byte Morale;
    public byte Duration;
    public byte DeathDialogue;
    public byte LevelMarkDisplay;
    public int AntiDefenseMode;
    public byte Medal1;
    public byte Medal2;
    public byte Medal3;
    public byte Ribbon1;
    public byte Ribbon2;
    public byte Ribbon3;
    /// <summary>0x36-0x3D：规范记为填充，原版数据里常有非零值，原样保留。</summary>
    public long Padding36;
    /// <summary>0x3E-0x3F：规范记为填充，原样保留。</summary>
    public short Padding3E;

    /// <summary>
    /// 编辑器运行期的所属军团（归属值），由 L 键、按概率生成等操作设置。不读写文件：
    /// 文件里 0x36-0x39 是 <see cref="Padding36"/> 的一部分。读入的单位与新建的默认单位为
    /// <see cref="Army.UnassignedLegion"/>（按所在格子的归属算），不会被当成归属 0 的单位。
    /// </summary>
    public int LegionId;

    /// <summary>格子序号（按无符号读取：大地图上的序号可能超过 32767）。</summary>
    public readonly int CoordinateIndex => (ushort)Coordinate;

    /// <summary>
    /// 从字节数组解析部队数据 v3
    /// </summary>
    public static Army_3 FromBytes(ReadOnlySpan<byte> data, int offset)
    {
        if (data.Length < offset + 64) return default;

        return new Army_3
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
            HpLevel = data[offset + 19],
            Waste1 = data[offset + 20],
            Waste2 = data[offset + 21],
            Waste3 = data[offset + 22],
            SkillLevel1 = data[offset + 23],
            SkillLevel2 = data[offset + 24],
            SkillLevel3 = data[offset + 25],
            SkillLevel4 = data[offset + 26],
            SkillLevel5 = data[offset + 27],
            KeyPoint = data[offset + 28],
            Policy = data[offset + 29],
            TransportShip = data[offset + 30],
            Field1F = data[offset + 31],
            Hatred = BitConverter.ToInt16(data[(offset + 32)..]),
            AttackTarget = BitConverter.ToInt16(data[(offset + 34)..]),
            Plan = BitConverter.ToInt16(data[(offset + 36)..]),
            ChangeRound = BitConverter.ToInt16(data[(offset + 38)..]),
            Morale = data[offset + 40],
            Duration = data[offset + 41],
            DeathDialogue = data[offset + 42],
            LevelMarkDisplay = data[offset + 43],
            AntiDefenseMode = BitConverter.ToInt32(data[(offset + 44)..]),
            Medal1 = data[offset + 48],
            Medal2 = data[offset + 49],
            Medal3 = data[offset + 50],
            Ribbon1 = data[offset + 51],
            Ribbon2 = data[offset + 52],
            Ribbon3 = data[offset + 53],
            Padding36 = BitConverter.ToInt64(data[(offset + 54)..]),
            Padding3E = BitConverter.ToInt16(data[(offset + 62)..]),
            LegionId = Army.UnassignedLegion
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
        data[offset + 19] = HpLevel;
        data[offset + 20] = Waste1;
        data[offset + 21] = Waste2;
        data[offset + 22] = Waste3;
        data[offset + 23] = SkillLevel1;
        data[offset + 24] = SkillLevel2;
        data[offset + 25] = SkillLevel3;
        data[offset + 26] = SkillLevel4;
        data[offset + 27] = SkillLevel5;
        data[offset + 28] = KeyPoint;
        data[offset + 29] = Policy;
        data[offset + 30] = TransportShip;
        data[offset + 31] = Field1F;
        BitConverter.TryWriteBytes(data[(offset + 32)..], Hatred);
        BitConverter.TryWriteBytes(data[(offset + 34)..], AttackTarget);
        BitConverter.TryWriteBytes(data[(offset + 36)..], Plan);
        BitConverter.TryWriteBytes(data[(offset + 38)..], ChangeRound);
        data[offset + 40] = Morale;
        data[offset + 41] = Duration;
        data[offset + 42] = DeathDialogue;
        data[offset + 43] = LevelMarkDisplay;
        BitConverter.TryWriteBytes(data[(offset + 44)..], AntiDefenseMode);
        data[offset + 48] = Medal1;
        data[offset + 49] = Medal2;
        data[offset + 50] = Medal3;
        data[offset + 51] = Ribbon1;
        data[offset + 52] = Ribbon2;
        data[offset + 53] = Ribbon3;
        BitConverter.TryWriteBytes(data[(offset + 54)..], Padding36);
        BitConverter.TryWriteBytes(data[(offset + 62)..], Padding3E);
    }

    /// <summary>
    /// 创建默认部队 v3
    /// </summary>
    public static Army_3 CreateDefault(int coord) => new Army_3
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
        HealthBonus = 100,
        CurrentHealth = 100,
        MaxHealth = 100,
        General = 0,
        Rank = 0,
        HpLevel = 0,
        Waste1 = 0,
        Waste2 = 0,
        Waste3 = 0,
        SkillLevel1 = 0,
        SkillLevel2 = 0,
        SkillLevel3 = 0,
        SkillLevel4 = 0,
        SkillLevel5 = 0,
        KeyPoint = 0,
        Policy = 0,
        TransportShip = 0,
        Field1F = 0,
        Hatred = 0,
        AttackTarget = 0,
        Plan = 0,
        ChangeRound = 0,
        Morale = 0,
        Duration = 0,
        DeathDialogue = 0,
        LevelMarkDisplay = 0,
        AntiDefenseMode = 0,
        Medal1 = 0,
        Medal2 = 0,
        Medal3 = 0,
        Ribbon1 = 0,
        Ribbon2 = 0,
        Ribbon3 = 0,
        LegionId = Army.UnassignedLegion
    };

    public readonly string GetDirectionName() => Direction switch
    {
        0 => "左",
        1 => "右",
        _ => $"未知({Direction})"
    };

    public readonly string GetKeyPointName() => KeyPoint switch
    {
        0 => "否",
        1 => "红圈",
        2 => "绿圈",
        _ => $"未知({KeyPoint})"
    };

    public readonly string GetPolicyName() => Policy switch
    {
        0 => "自由行动",
        1 => "猛攻",
        2 => "缓慢进攻",
        3 => "坚守",
        4 => "撤退",
        _ => $"未知({Policy})"
    };

    public readonly string GetMoraleName() => Morale switch
    {
        0 => "正常",
        1 => "士气上升",
        253 => "混乱",
        254 => "士气双降",
        255 => "士气下降",
        _ => $"未知({Morale})"
    };
}
