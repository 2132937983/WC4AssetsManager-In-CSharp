using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 建筑数据解析模块。
/// </summary>
public static class BTLBuildingModule
{
    private const int BuildingSize = 32;

    public static List<Building> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Building>();

        var buildings = new List<Building>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * 32);
            if (startPos + 32 > data.Length) break;

            buildings.Add(Building.FromBytes(data, startPos));
        }

        Debug.WriteLine($"[BTLBuildingModule] 解析 {buildings.Count} 个建筑");
        return buildings;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * BuildingSize);
}
