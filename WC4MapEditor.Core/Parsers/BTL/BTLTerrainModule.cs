using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 地形数据解析模块。
/// </summary>
public static class BTLTerrainModule
{
    private const int TerrainSize = 16;

    public static List<Terrain> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Terrain>();

        var terrains = new List<Terrain>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * 16);
            if (startPos + 16 > data.Length) break;

            terrains.Add(Terrain.FromBytes(data, startPos));
        }

        Debug.WriteLine($"[BTLTerrainModule] 解析 {terrains.Count} 个地形");
        return terrains;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * TerrainSize);
}
