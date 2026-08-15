using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 空军数据解析模块。
/// </summary>
public static class BTLAirForceModule
{
    private const int AirForceSize = 20;

    public static List<AirForce> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<AirForce>();

        var airForces = new List<AirForce>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * AirForceSize);
            if (startPos + AirForceSize > data.Length) break;

            var airForce = AirForce.FromBytes(data, startPos);
            airForces.Add(airForce);
        }

        Debug.WriteLine($"[BTLAirForceModule] 解析 {airForces.Count} 个空军");
        return airForces;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * AirForceSize);
}
