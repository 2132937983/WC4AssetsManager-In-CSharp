using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 方案（AI 方针）数据结构 (16字节) - 使用结构体减少内存开销。
/// <para>
/// 方针值（<see cref="Army.Policy"/>）等于 <see cref="PolicyNumber"/> 的单位，在第 <see cref="TriggerRound"/> 回合
/// 执行 <see cref="CaseType"/> 指定的动作；类型名称来自 setting.txt [方针修改] 的 CaseList。
/// 同一个方针编号可以有多条方案（例如先改类型、下一回合再走向某个地块）。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapCase
{
    /// <summary>"使单位走向目标值序号地块"：只有这种类型的 <see cref="TargetTile"/> 是地块序号（行 × 地图宽 + 列）。</summary>
    public const int MoveToTileType = 2;

    /// <summary>0x00 方针编号：方针值等于它的单位执行本方案。</summary>
    public int PolicyNumber;

    /// <summary>
    /// 0x04 方针类型。VB 模型把这个字段叫 EndEvent（终结事件），但 VB 的方案列表窗口按方针类型编辑它，
    /// setting.txt 的 CaseList 也是这个字段的取值表，这里统一按方针类型理解。
    /// </summary>
    public int CaseType;

    /// <summary>0x08 触发回合。</summary>
    public int TriggerRound;

    /// <summary>
    /// 0x0C 目标值（沿用 VB 的字段名），含义随类型而定：<see cref="MoveToTileType"/> 时为地块序号，其他类型为数值。
    /// 调整地图尺寸等按地块重映射的操作只应改 <see cref="TargetsTile"/> 为 true 的方案。
    /// </summary>
    public int TargetTile;

    /// <summary>目标值是否是地块序号。</summary>
    public readonly bool TargetsTile => CaseType == MoveToTileType;

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
