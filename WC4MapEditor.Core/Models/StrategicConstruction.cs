using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 战略建筑数据结构 (16字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct StrategicConstruction
{
    public int LegionId;
    public int Reserved1;
    public int Reserved2;
    public int ConstructionCode;

    /// <summary>
    /// 从字节数组解析战略建筑数据
    /// </summary>
    public static StrategicConstruction FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<StrategicConstruction>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认战略建筑
    /// </summary>
    public static StrategicConstruction CreateDefault() => new StrategicConstruction();
}
