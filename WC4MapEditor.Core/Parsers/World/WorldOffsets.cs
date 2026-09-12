using System.Diagnostics;
using System.IO;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.World;

/// <summary>
/// 世界地图偏移量计算器。
/// </summary>
public static class WorldOffsets
{
    public const int HeaderSize = 16;
    public const int TerrainSize = 16;
    public const int ProvinceSize = 2;

    public static long GetTerrainStartOffset() => HeaderSize;

    public static long GetTerrainEndOffset(int terrainCount)
        => GetTerrainStartOffset() + ((long)terrainCount * TerrainSize);

    public static long GetProvinceStartOffset(int terrainCount)
        => GetTerrainEndOffset(terrainCount);
}
