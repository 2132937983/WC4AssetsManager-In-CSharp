using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 归属数据解析模块。
/// </summary>
public static class BTLBelongModule
{
    private const int BelongSize = 1;

    public static List<string> Parse(byte[] data, int startOffset, int count, bool applyOffset = false)
    {
        if (data == null) return new List<string>();

        var belongs = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * BelongSize);
            if (startPos + BelongSize > data.Length) break;

            byte belongByte = data[startPos];
            if (applyOffset)
                belongByte = (byte)((belongByte + 1) & 0xFF);

            belongs.Add(belongByte.ToString("X2"));
        }

        Debug.WriteLine($"[BTLBelongModule] 解析 {belongs.Count} 个归属值，偏移={applyOffset}");
        return belongs;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * BelongSize);
}
