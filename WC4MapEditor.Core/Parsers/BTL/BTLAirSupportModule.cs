using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 空中支援数据解析模块。
/// </summary>
public static class BTLAirSupportModule
{
    private const int AirSupportSize = 16;

    public static List<AirSupport> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<AirSupport>();

        var supports = new List<AirSupport>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * AirSupportSize);
            if (startPos + AirSupportSize > data.Length) break;

            var support = AirSupport.FromBytes(data, startPos);
            supports.Add(support);
        }

        Debug.WriteLine($"[BTLAirSupportModule] 解析 {supports.Count} 个空中支援");
        return supports;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * AirSupportSize);
}
