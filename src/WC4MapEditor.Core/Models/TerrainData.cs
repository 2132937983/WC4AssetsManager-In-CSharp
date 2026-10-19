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
/// <para>
/// 省区值 = 该省省会格子的地图索引（row * MapWidth + col）；省会格子的省区值等于它自己的索引。
/// <see cref="EmptyValue"/>（0xFFFF）表示不属于任何省区。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Province
{
    /// <summary>"不属于任何省区"的省区值</summary>
    public const ushort EmptyValue = 0xFFFF;

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
    /// 省区值的低字节（原始字节访问）。
    /// <para>
    /// 名字是历史遗留：省区值是省会格子的索引，不是"国家 ID"，拆成高低字节没有含义。
    /// 读写省区一律用 <see cref="ProvinceValue"/>；这里只给按字节导出的旧代码用。
    /// </para>
    /// </summary>
    public byte CountryId
    {
        get => _lowByte;
        set => _lowByte = value;
    }

    /// <summary>
    /// 省区值的高字节（原始字节访问，名字同样是历史遗留，见 <see cref="CountryId"/>）。
    /// </summary>
    public byte Reserved
    {
        get => _highByte;
        set => _highByte = value;
    }

    /// <summary>
    /// 是否有效省份（值大于0且不等于0xFFFF）。
    /// <para>
    /// 0 也按"无省区"处理，这是有意的选择：扫描游戏自带的 1358 张关卡 / 征服地图，没有裁剪偏移（MapClip）的地图里
    /// 没有一张以格子 0 为省会，省区值 0 只是数组末尾附近零星几格的填充值；<c>new MapData(w, h)</c> 建的新地图也全是 0。
    /// 若把 0 当成"省会在格子 0 的省区"，新地图会整片被画成一个省，孤立 / 空白处理也会把 0 扩散出去。
    /// 渲染层、首都国旗层与 <see cref="MapData.GetProvinceCapitalBelong"/> 用的都是同一规则。
    /// </para>
    /// </summary>
    public bool IsValid => ProvinceValue > 0 && ProvinceValue != EmptyValue;

    /// <summary>
    /// 格子索引能否当省会（省会的省区值 = 自身索引）：省区值只有 16 位，0 与 0xFFFF 又都表示无省区，
    /// 所以只有 1–65534 的索引能写成一个有效的、指向自己的省区值。
    /// </summary>
    public static bool CanBeCapitalIndex(int index) => index > 0 && index < EmptyValue;

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
    public static Province CreateDefault() => Create(EmptyValue);

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