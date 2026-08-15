using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 空中支援数据结构 (16字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AirSupport
{
    public int AirForceSequence;
    public int AmmoType;
    public int OwnerLegion;
    public int TriggerRound;

    /// <summary>
    /// 从字节数组解析空中支援数据
    /// </summary>
    public static AirSupport FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<AirSupport>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认空中支援
    /// </summary>
    public static AirSupport CreateDefault() => new AirSupport();

    /// <summary>
    /// 获取空军类型名称
    /// </summary>
    public readonly string GetAirForceTypeName() => AirForceSequence switch
    {
        0x14 => "战斗机",
        0x15 => "轰炸机",
        0x16 => "空降伞兵",
        0x17 => "战略轰炸机",
        0x18 => "特色轰炸机",
        0x19 => "近程导弹",
        0x1A => "中程导弹",
        0x1B => "远程导弹",
        0x1E => "洲际导弹",
        _ => $"未知({AirForceSequence})"
    };

    /// <summary>
    /// 获取弹药类型名称
    /// </summary>
    public readonly string GetAmmoTypeName() => AmmoType switch
    {
        0x0 => "默认弹药",
        0x1D => "原子弹",
        0x1E => "氢弹",
        0x1F => "三相弹",
        0x20 => "反物质弹",
        _ => $"未知({AmmoType})"
    };
}
