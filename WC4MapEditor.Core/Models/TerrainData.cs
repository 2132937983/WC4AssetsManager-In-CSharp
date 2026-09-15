using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainData
{
    public byte TileType1;
    public byte DecorationType1;
    public byte TextureOffsetX1;
    public byte TextureOffsetY1;
    public byte TileType2;
    public byte DecorationType2;
    public byte TextureOffsetX2;
    public byte TextureOffsetY2;
    public byte TileType3;
    public byte DecorationType3;
    public byte TextureOffsetX3;
    public byte TextureOffsetY3;
    public byte Reserved1;
    public byte Reserved2;
    public byte RiverValue;
    public byte Reserved3;

    public static TerrainData FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<TerrainData>(data[offset..]);

    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    public static TerrainData CreateDefault() => new TerrainData { TileType1 = 1 };
}

/// <summary>
/// 省份数据结构 (2字节) - 使用小端序存储省份值，表示该省份的省会序号
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Province
{
    private byte _lowByte;     // 低字节 (1字节)
    private byte _highByte;    // 高字节 (1字节)

    /// <summary>
    /// 获取或设置省份值 (0-65535)，对应省会序号
    /// </summary>
    public ushort ProvinceValue
    {
        get => (ushort)(_lowByte | (_highByte << 8));
        set
        {
            _lowByte = (byte)(value & 0xFF);
            _highByte = (byte)((value >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// 获取或设置国家ID（低字节，兼容旧代码）
    /// </summary>
    public byte CountryId
    {
        get => _lowByte;
        set => _lowByte = value;
    }

    /// <summary>
    /// 获取或设置保留字节（高字节，兼容旧代码）
    /// </summary>
    public byte Reserved
    {
        get => _highByte;
        set => _highByte = value;
    }

    /// <summary>
    /// 是否有效省份（值大于0且不等于0xFFFF）
    /// </summary>
    public bool IsValid => ProvinceValue > 0 && ProvinceValue != 0xFFFF;

    public static Province FromBytes(ReadOnlySpan<byte> data, int offset) =>
        new Province { _lowByte = data[offset], _highByte = data[offset + 1] };

    public void ToBytes(Span<byte> data, int offset)
    {
        data[offset] = _lowByte;
        data[offset + 1] = _highByte;
    }

    /// <summary>
    /// 创建「不属于任何省区」的默认省区值。
    /// <para>
    /// 取 0xFFFF 而不是 0：地图扩展/缩放产生的新格子用的就是这个值，
    /// 而代码中多处判定（ExpandAllProvincesToFillMap、GenerateProvincesForIsolatedCapitals、
    /// 渲染层等）都以 0xFFFF 作为“空白省区”的标志。若返回 0，新格子会被误判为已分配省区。
    /// </para>
    /// </summary>
    public static Province CreateDefault() => Create(0xFFFF);

    /// <summary>
    /// 创建指定省份值的省份
    /// </summary>
    public static Province Create(ushort value)
    {
        return new Province
        {
            _lowByte = (byte)(value & 0xFF),
            _highByte = (byte)((value >> 8) & 0xFF)
        };
    }
}