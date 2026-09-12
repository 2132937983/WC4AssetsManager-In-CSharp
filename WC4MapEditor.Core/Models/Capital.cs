using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 首都数据结构 (4字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Capital
{
    public int Coordinate;

    /// <summary>
    /// 从字节数组解析首都数据
    /// </summary>
    public static Capital FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Capital>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认首都
    /// </summary>
    public static Capital CreateDefault() => new Capital();
}
