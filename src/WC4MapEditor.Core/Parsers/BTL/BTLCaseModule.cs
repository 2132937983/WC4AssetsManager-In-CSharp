using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 方案数据解析模块。
/// </summary>
public static class BTLCaseModule
{
    private const int CaseSize = 16;

    public static List<MapCase> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<MapCase>();

        var cases = new List<MapCase>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * CaseSize);
            if (startPos + CaseSize > data.Length) break;

            var mapCase = MapCase.FromBytes(data, startPos);
            cases.Add(mapCase);
        }

        Debug.WriteLine($"[BTLCaseModule] 解析 {cases.Count} 个方案");
        return cases;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * CaseSize);
}
