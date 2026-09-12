using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 部队数据结构 (48字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Army
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
    public byte Reserved1;
    public short Reserved2;
    public short Plan;
    public short Reserved3;
    public short ChangeRound;
    public byte Morale;
    public byte Duration;
    public byte Dialogue;
    public byte CanAttack;
    public int LegionId;

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
            Plan = BitConverter.ToInt16(data[(offset + 34)..]),
            ChangeRound = BitConverter.ToInt16(data[(offset + 38)..]),
            Morale = data[offset + 40],
            Duration = data[offset + 41],
            Dialogue = data[offset + 42],
            CanAttack = data[offset + 43],
            LegionId = BitConverter.ToInt32(data[(offset + 44)..])
        };
    }

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset)
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
        data[offset + 31] = 0;
        BitConverter.TryWriteBytes(data[(offset + 32)..], (short)0);
        BitConverter.TryWriteBytes(data[(offset + 34)..], Plan);
        BitConverter.TryWriteBytes(data[(offset + 36)..], (short)0);
        BitConverter.TryWriteBytes(data[(offset + 38)..], ChangeRound);
        data[offset + 40] = Morale;
        data[offset + 41] = Duration;
        data[offset + 42] = Dialogue;
        data[offset + 43] = CanAttack;
        BitConverter.TryWriteBytes(data[(offset + 44)..], LegionId);
    }

    /// <summary>
    /// 创建默认部队
    /// </summary>
    public static Army CreateDefault(int coord) => new Army
    {
        Coordinate = (short)coord,
        UnitType = 0,
        Level = 1,
        Organization = 1,
        Direction = 0,
        Mobility = 0,
        BuiltRound = 0,
        Experience = 0,
        HealthBonus = 0,
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
        LegionId = 0
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
        253 => "混乱",
        254 => "士气双降",
        255 => "士气下降",
        _ => $"未知({Morale})"
    };

    /// <summary>
    /// 获取兵种名称
    /// </summary>
    public readonly string GetUnitTypeName() => UnitType switch
    {
        0 => "步兵",
        1 => "骑兵",
        2 => "弓箭手",
        4 => "海军",
        5 => "空军",
        _ => $"未知({UnitType})"
    };
}
