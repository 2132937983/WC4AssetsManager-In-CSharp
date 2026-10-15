using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 陷阱数据解析模块。
/// </summary>
public static class BTLTrapModule
{
    private const int TrapSize = 12;

    public static List<Trap> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Trap>();

        var traps = new List<Trap>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * TrapSize);
            if (startPos + TrapSize > data.Length) break;

            var trap = Trap.FromBytes(data, startPos);
            traps.Add(trap);
        }

        Debug.WriteLine($"[BTLTrapModule] 解析 {traps.Count} 个陷阱");
        return traps;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * TrapSize);
}
