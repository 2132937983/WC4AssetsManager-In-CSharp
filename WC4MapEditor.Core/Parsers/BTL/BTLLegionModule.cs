using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 军团数据解析模块。
/// </summary>
public static class BTLLegionModule
{
    private const int LegionStartOffset = 0x80;
    private const int LegionSize = 300;

    public static List<Legion> Parse(byte[] data, int count)
    {
        if (data == null) return new List<Legion>();

        var legions = new List<Legion>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = LegionStartOffset + (i * LegionSize);
            if (startPos + LegionSize > data.Length) break;

            var legion = Legion.FromBytes(data, startPos);
            legions.Add(legion);
        }

        Debug.WriteLine($"[BTLLegionModule] 解析 {legions.Count} 个军团");
        return legions;
    }

    public static int CalculateEndOffset(int count)
        => LegionStartOffset + (count * LegionSize);
}
