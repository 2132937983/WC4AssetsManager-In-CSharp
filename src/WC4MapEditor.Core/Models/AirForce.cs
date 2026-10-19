using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 空袭数据结构 (20字节) - 使用结构体减少内存开销。
/// <para>
/// 空袭挂在机场上：<see cref="Coordinate"/> 是机场所在格的索引（row * MapWidth + col）；
/// <see cref="OwnerLegion"/> 是军团 ActionId（与内存里的归属值同一套编号）。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AirForce
{
    public int Coordinate;
    public int UnitType;
    public int AmmoType;
    public int OwnerLegion;
    public int TriggerRound;

    /// <summary>
    /// 从字节数组解析空军数据
    /// </summary>
    public static AirForce FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<AirForce>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 新建空袭的默认值：轰炸机、默认弹药、第 1 回合（与 VB 列表窗口的"新增"一致）。
    /// 原先返回全 0：兵种 0 在游戏里不存在，地图标记也会跳过它，新增的空袭看不见。
    /// </summary>
    public static AirForce CreateDefault(int coordinate = 0, int ownerLegion = 0) => new()
    {
        Coordinate = coordinate,
        UnitType = AirForceCatalog.Bomber,
        AmmoType = AirForceCatalog.DefaultAmmo,
        OwnerLegion = ownerLegion,
        TriggerRound = 1
    };

    /// <summary>兵种名称（setting.txt [空袭修改] 的 AirForceList，缺省时用内置名录）。</summary>
    public readonly string GetUnitTypeName() => AirForceCatalog.TypeName(UnitType);

    /// <summary>弹药名称（setting.txt [空袭修改] 的 AirForcePowerList，缺省时用内置名录）。</summary>
    public readonly string GetAmmoTypeName() => AirForceCatalog.AmmoName(AmmoType);
}
