using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 空中支援数据结构 (16字节) - 使用结构体减少内存开销。
/// <para>
/// 空中支援没有坐标；<see cref="AirForceSequence"/> 是兵种编号（与空袭的 UnitType 同一张名录），
/// <see cref="OwnerLegion"/> 是军团 ActionId。
/// </para>
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
    /// 新建空中支援的默认值：轰炸机、默认弹药、第 1 回合（与 VB 列表窗口的"新增"一致；原先返回全 0）。
    /// </summary>
    public static AirSupport CreateDefault(int ownerLegion = 0) => new()
    {
        AirForceSequence = AirForceCatalog.Bomber,
        AmmoType = AirForceCatalog.DefaultAmmo,
        OwnerLegion = ownerLegion,
        TriggerRound = 1
    };

    /// <summary>兵种名称（与空袭共用名录）。</summary>
    public readonly string GetAirForceTypeName() => AirForceCatalog.TypeName(AirForceSequence);

    /// <summary>弹药名称。</summary>
    public readonly string GetAmmoTypeName() => AirForceCatalog.AmmoName(AmmoType);
}
