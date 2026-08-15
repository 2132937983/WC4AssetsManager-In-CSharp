using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 陷阱数据结构 (12字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Trap
{
    public short Coordinate;
    public short LegionId;
    public short Organization;
    public short Health;
    public short Extra1;
    public short Extra2;

    /// <summary>
    /// 从字节数组解析陷阱数据
    /// </summary>
    public static Trap FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Trap>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认陷阱
    /// </summary>
    public static Trap CreateDefault(int coord) => new Trap
    {
        Coordinate = (short)coord,
        LegionId = 0,
        Organization = 1,
        Health = 100
    };
}
