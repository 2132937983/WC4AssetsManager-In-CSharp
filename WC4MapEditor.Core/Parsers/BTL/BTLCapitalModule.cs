using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 首都数据解析模块。
/// </summary>
public static class BTLCapitalModule
{
    private const int CapitalSize = 4;

    public static List<Capital> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Capital>();

        var capitals = new List<Capital>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * CapitalSize);
            if (startPos + CapitalSize > data.Length) break;

            var capital = Capital.FromBytes(data, startPos);
            capitals.Add(capital);
        }

        Debug.WriteLine($"[BTLCapitalModule] 解析 {capitals.Count} 个首都");
        return capitals;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * CapitalSize);
}
