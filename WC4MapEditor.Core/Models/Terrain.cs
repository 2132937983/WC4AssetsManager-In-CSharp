using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 地形数据结构 (16字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Terrain
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

    /// <summary>
    /// 从字节数组解析地形数据（零拷贝）
    /// </summary>
    public static Terrain FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Terrain>(data[offset..]);

    /// <summary>
    /// 写入字节数组（零拷贝）
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认地形（平原）
    /// </summary>
    public static Terrain CreateDefault() => new Terrain { TileType1 = 1 };

    /// <summary>
    /// 从TerrainData转换
    /// </summary>
    public static Terrain FromTerrainData(TerrainData data) => new Terrain
    {
        TileType1 = data.TileType1,
        DecorationType1 = data.DecorationType1,
        TextureOffsetX1 = data.TextureOffsetX1,
        TextureOffsetY1 = data.TextureOffsetY1,
        TileType2 = data.TileType2,
        DecorationType2 = data.DecorationType2,
        TextureOffsetX2 = data.TextureOffsetX2,
        TextureOffsetY2 = data.TextureOffsetY2,
        TileType3 = data.TileType3,
        DecorationType3 = data.DecorationType3,
        TextureOffsetX3 = data.TextureOffsetX3,
        TextureOffsetY3 = data.TextureOffsetY3,
        RiverValue = data.RiverValue
    };

    /// <summary>
    /// 转换为TerrainData
    /// </summary>
    public TerrainData ToTerrainData() => new TerrainData
    {
        TileType1 = TileType1,
        DecorationType1 = DecorationType1,
        TextureOffsetX1 = TextureOffsetX1,
        TextureOffsetY1 = TextureOffsetY1,
        TileType2 = TileType2,
        DecorationType2 = DecorationType2,
        TextureOffsetX2 = TextureOffsetX2,
        TextureOffsetY2 = TextureOffsetY2,
        TileType3 = TileType3,
        DecorationType3 = DecorationType3,
        TextureOffsetX3 = TextureOffsetX3,
        TextureOffsetY3 = TextureOffsetY3,
        RiverValue = RiverValue
    };

    /// <summary>
    /// 获取地形类型名称
    /// </summary>
    public static string GetTerrainName(int terrainId) => terrainId switch
    {
        0 => "海洋",
        1 => "平原",
        2 => "森林",
        3 => "山地",
        4 => "丘陵",
        5 => "沙漠",
        6 => "沼泽",
        7 => "雪地",
        _ => $"未知({terrainId})"
    };

    /// <summary>
    /// 获取第一层地形类型名称
    /// </summary>
    public readonly string GetTerrainTypeName() => GetTerrainName(TileType1);

    /// <summary>
    /// 检查是否可以通过（陆地单位）
    /// </summary>
    public readonly bool IsPassable() => TileType1 != 0;

    /// <summary>
    /// 检查是否可以通过（海军单位）
    /// </summary>
    public readonly bool IsNavigable() => TileType1 == 0;
}
