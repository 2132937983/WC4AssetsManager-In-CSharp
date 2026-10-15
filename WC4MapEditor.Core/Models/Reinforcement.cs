using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 增援数据结构 (80字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reinforcement
{
    public int Coordinate;
    public int UnitType;
    public int Level;
    public int Organization;
    public int Direction;
    public int General;
    public int Rank;
    public int Quality;
    public int Skill1Level;
    public int Skill2Level;
    public int Skill3Level;
    public int Skill4Level;
    public int Skill5Level;
    public int Badge1;
    public int Badge2;
    public int Badge3;
    public int OwnerCountry;
    public int SpawnRound;

    /// <summary>
    /// 从字节数组解析增援数据
    /// </summary>
    public static Reinforcement FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Reinforcement>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认增援
    /// </summary>
    public static Reinforcement CreateDefault() => new Reinforcement
    {
        Coordinate = 0,
        UnitType = 0,
        Level = 1,
        Organization = 1,
        Direction = 0,
        General = 0,
        Rank = 0,
        Quality = 0,
        Skill1Level = 0,
        Skill2Level = 0,
        Skill3Level = 0,
        Skill4Level = 0,
        Skill5Level = 0,
        Badge1 = 0,
        Badge2 = 0,
        Badge3 = 0,
        OwnerCountry = 0,
        SpawnRound = 0
    };
}
