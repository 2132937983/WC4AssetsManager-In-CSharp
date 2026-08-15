using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 单位部署数据解析模块。
/// </summary>
public static class BTLUnitPlacementModule
{
    private const int UnitPlacementSize = 8;

    public static List<UnitPlacement> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<UnitPlacement>();

        var placements = new List<UnitPlacement>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * UnitPlacementSize);
            if (startPos + UnitPlacementSize > data.Length) break;

            var placement = UnitPlacement.FromBytes(data, startPos);
            placements.Add(placement);
        }

        Debug.WriteLine($"[BTLUnitPlacementModule] 解析 {placements.Count} 个单位部署");
        return placements;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * UnitPlacementSize);
}
