using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 单位部署数据结构 (8字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UnitPlacement
{
    public int Coordinate;
    public byte Direction;
    public byte Sequence;
    public byte Unknown;
    public byte TransportShip;

    /// <summary>
    /// 从字节数组解析单位部署数据
    /// </summary>
    public static UnitPlacement FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<UnitPlacement>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认单位部署
    /// </summary>
    public static UnitPlacement CreateDefault() => new UnitPlacement();
}
