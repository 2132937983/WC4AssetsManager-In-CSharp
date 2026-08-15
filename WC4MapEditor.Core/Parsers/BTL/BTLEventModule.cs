using System.Diagnostics;
using WC4MapEditor.Models;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 事件数据解析模块。
/// </summary>
public static class BTLEventModule
{
    private const int EventSize = 44;

    public static List<MapEvent> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<MapEvent>();

        var events = new List<MapEvent>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * EventSize);
            if (startPos + EventSize > data.Length) break;

            var mapEvent = MapEvent.FromBytes(data, startPos);
            events.Add(mapEvent);
        }

        Debug.WriteLine($"[BTLEventModule] 解析 {events.Count} 个事件");
        return events;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * EventSize);
}
