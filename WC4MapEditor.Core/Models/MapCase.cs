using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 方案数据结构 (16字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapCase
{
    public int PolicyNumber;
    public int EndEvent;
    public int TriggerRound;
    public int TargetTile;

    /// <summary>
    /// 从字节数组解析方案数据
    /// </summary>
    public static MapCase FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<MapCase>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认方案
    /// </summary>
    public static MapCase CreateDefault() => new MapCase();
}
