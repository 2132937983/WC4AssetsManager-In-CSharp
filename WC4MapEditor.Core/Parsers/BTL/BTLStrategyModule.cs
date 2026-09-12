using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 战略建筑数据解析模块。
/// </summary>
public static class BTLStrategyModule
{
    private const int StrategySize = 16;

    public static List<StrategicConstruction> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<StrategicConstruction>();

        var constructions = new List<StrategicConstruction>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * StrategySize);
            if (startPos + StrategySize > data.Length) break;

            var sc = StrategicConstruction.FromBytes(data, startPos);
            constructions.Add(sc);
        }

        Debug.WriteLine($"[BTLStrategyModule] 解析 {constructions.Count} 个战略建筑");
        return constructions;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * StrategySize);
}
