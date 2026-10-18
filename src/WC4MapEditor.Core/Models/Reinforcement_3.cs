using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 增援数据结构 v3 (104字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reinforcement_3
{
    public int Coordinate;
    public int UnitType;
    public int Level;
    public int Organization;
    public int BehaviorMode;
    public int Direction;
    public int Unknown;
    public int General;
    public int Rank;
    public int HpLevel;
    public int Skill1Level;
    public int Skill2Level;
    public int Skill3Level;
    public int Skill4Level;
    public int Skill5Level;
    public int Waste1;
    public int Waste2;
    public int Waste3;
    public int OwnerLegion;
    public int SpawnRound;
    public int Medal1;
    public int Medal2;
    public int Medal3;
    public int Ribbon1;
    public int Ribbon2;
    public int Ribbon3;

    /// <summary>
    /// 从字节数组解析增援数据 v3
    /// </summary>
    public static Reinforcement_3 FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Reinforcement_3>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认增援 v3
    /// </summary>
    public static Reinforcement_3 CreateDefault() => new Reinforcement_3
    {
        Coordinate = 0,
        UnitType = 0,
        Level = 1,
        Organization = 1,
        BehaviorMode = 0,
        Direction = 0,
        Unknown = 0,
        General = 0,
        Rank = 0,
        HpLevel = 0,
        Skill1Level = 0,
        Skill2Level = 0,
        Skill3Level = 0,
        Skill4Level = 0,
        Skill5Level = 0,
        Waste1 = 0,
        Waste2 = 0,
        Waste3 = 0,
        OwnerLegion = 0,
        SpawnRound = 0,
        Medal1 = 0,
        Medal2 = 0,
        Medal3 = 0,
        Ribbon1 = 0,
        Ribbon2 = 0,
        Ribbon3 = 0
    };
}
