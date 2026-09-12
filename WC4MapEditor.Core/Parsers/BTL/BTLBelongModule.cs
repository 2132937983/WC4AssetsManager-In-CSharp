using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 归属数据解析模块。
/// </summary>
public static class BTLBelongModule
{
    private const int BelongSize = 1;

    /// <summary>
    /// 0-255 的十六进制字符串查表。原实现逐格调用 ToString("X2")，
    /// 16000~23520 个格子就会分配等量的临时字符串对象；改为查表后只剩数组索引。
    /// </summary>
    private static readonly string[] HexLookup = CreateHexLookup();

    private static string[] CreateHexLookup()
    {
        var table = new string[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = i.ToString("X2");
        return table;
    }

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

            belongs.Add(HexLookup[belongByte]);
        }

        Debug.WriteLine($"[BTLBelongModule] 解析 {belongs.Count} 个归属值，偏移={applyOffset}");
        return belongs;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * BelongSize);
}
