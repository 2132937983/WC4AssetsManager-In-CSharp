using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 陷阱数据结构 (12字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Trap
{
    /// <summary>
    /// 格子下标的原始 16 位值。文件里是<b>无符号</b>数，这里按有符号读入：
    /// 超过 32767 的格子会变成负数，比较 / 换算坐标请用 <see cref="CellIndex"/>。
    /// </summary>
    public short Coordinate;

    /// <summary>所属军团：军团的 ActionId（不是归属值 / 军团下标），渲染层按它找国旗。</summary>
    public short LegionId;

    /// <summary>编制 = 陷阱等级（游戏数据里是 1-4，决定图标数量）。</summary>
    public short Organization;

    public short Health;
    public short Extra1;
    public short Extra2;

    /// <summary>格子下标（按无符号解释 <see cref="Coordinate"/>，大地图上超过 32767 的格子也正确）。</summary>
    public readonly int CellIndex => (ushort)Coordinate;

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
    /// 创建默认陷阱：1 级、该等级的标准血量（见 <see cref="StandardHealth"/>）。
    /// <paramref name="cellIndex"/> 为 0-65535 的格子下标。
    /// </summary>
    public static Trap CreateDefault(int cellIndex) => new Trap
    {
        Coordinate = unchecked((short)cellIndex),
        LegionId = 0,
        Organization = 1,
        Health = StandardHealth(1)
    };

    /// <summary>
    /// 某等级陷阱的标准血量。游戏自带地图里的规范陷阱都满足 血量 = 25 + 45 × 等级（70 / 115 / 160 / 205）。
    /// </summary>
    public static short StandardHealth(int level) => (short)(25 + 45 * level);
}
