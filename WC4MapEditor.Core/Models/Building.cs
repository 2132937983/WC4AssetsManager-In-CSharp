using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 建筑数据结构 (32字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Building
{
    public int Coordinate;
    public short Name;
    public byte BuildingType;
    public byte Appearance;
    public byte LandmarkBuilding;
    public byte DecorativeBuilding;
    public byte SkillUnlock;
    public byte RewardCount;
    public short Reserved1;
    public byte HatredValue;
    public byte KeyPoint;
    public byte OccupationEvent;
    public byte Reserved2;
    public int Reserved3;
    public byte FireIgnition;
    public byte FireDuration;
    public byte AirDefenseWeapon;
    public byte AirDefenseRadar;
    public byte FactoryLevel;
    public byte ResearchLevel;
    public byte MedicalLevel;
    public byte AviationLevel;
    public byte MissileLevel;
    public byte NuclearLevel;
    public int Reserved4;

    /// <summary>
    /// 从字节数组解析建筑数据
    /// </summary>
    public static Building FromBytes(ReadOnlySpan<byte> data, int offset)
    {
        if (data.Length < offset + 32) return default;

        return new Building
        {
            Coordinate = BitConverter.ToUInt16(data[offset..]),
            Name = BitConverter.ToInt16(data[(offset + 2)..]),
            BuildingType = data[offset + 4],
            Appearance = data[offset + 5],
            LandmarkBuilding = data[offset + 6],
            DecorativeBuilding = data[offset + 7],
            SkillUnlock = data[offset + 8],
            RewardCount = data[offset + 9],
            HatredValue = data[offset + 12],
            KeyPoint = data[offset + 13],
            OccupationEvent = data[offset + 14],
            FireIgnition = data[offset + 20],
            FireDuration = data[offset + 21],
            AirDefenseWeapon = data[offset + 22],
            AirDefenseRadar = data[offset + 23],
            FactoryLevel = data[offset + 24],
            ResearchLevel = data[offset + 25],
            MedicalLevel = data[offset + 26],
            AviationLevel = data[offset + 27],
            MissileLevel = data[offset + 28],
            NuclearLevel = data[offset + 29]
        };
    }

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset)
    {
        BitConverter.TryWriteBytes(data[offset..], (ushort)(Coordinate & 0xFFFF));
        BitConverter.TryWriteBytes(data[(offset + 2)..], Name);
        data[offset + 4] = BuildingType;
        data[offset + 5] = Appearance;
        data[offset + 6] = LandmarkBuilding;
        data[offset + 7] = DecorativeBuilding;
        data[offset + 8] = SkillUnlock;
        data[offset + 9] = RewardCount;
        data[offset + 12] = HatredValue;
        data[offset + 13] = KeyPoint;
        data[offset + 14] = OccupationEvent;
        data[offset + 20] = FireIgnition;
        data[offset + 21] = FireDuration;
        data[offset + 22] = AirDefenseWeapon;
        data[offset + 23] = AirDefenseRadar;
        data[offset + 24] = FactoryLevel;
        data[offset + 25] = ResearchLevel;
        data[offset + 26] = MedicalLevel;
        data[offset + 27] = AviationLevel;
        data[offset + 28] = MissileLevel;
        data[offset + 29] = NuclearLevel;
    }

    /// <summary>
    /// 创建默认建筑
    /// </summary>
    public static Building CreateDefault(int coord) => new Building
    {
        Coordinate = coord,
        Name = -1,
        BuildingType = 0,
        Appearance = 0,
        LandmarkBuilding = 0,
        DecorativeBuilding = 0,
        SkillUnlock = 0,
        RewardCount = 0,
        HatredValue = 0,
        KeyPoint = 0,
        OccupationEvent = 0,
        FireIgnition = 0,
        FireDuration = 0,
        AirDefenseWeapon = 0,
        AirDefenseRadar = 0,
        FactoryLevel = 0,
        ResearchLevel = 0,
        MedicalLevel = 0,
        AviationLevel = 0,
        MissileLevel = 0,
        NuclearLevel = 0
    };

    /// <summary>
    /// 获取建筑类型名称
    /// </summary>
    public readonly string GetBuildingTypeName() => BuildingType switch
    {
        0 => "无",
        1 => "城市",
        2 => "港口",
        3 => "关口",
        4 => "要塞",
        5 => "小镇",
        6 => "村庄",
        7 => "工厂",
        8 => "研究所",
        9 => "补给站",
        10 => "机场",
        11 => "一级城",
        12 => "二级城",
        13 => "三级城",
        14 => "四级城",
        15 => "五级城",
        21 => "灯塔",
        22 => "哨塔",
        23 => "要塞2",
        31 => "海上建筑",
        32 => "海上建筑2",
        33 => "海上建筑3",
        34 => "海上建筑4",
        _ => $"未知({BuildingType})"
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
    /// 获取防空武器雷达状态
    /// </summary>
    public readonly string GetAirDefenseRadarName() => AirDefenseRadar switch
    {
        0 => "否",
        2 => "是",
        _ => $"未知({AirDefenseRadar})"
    };
}