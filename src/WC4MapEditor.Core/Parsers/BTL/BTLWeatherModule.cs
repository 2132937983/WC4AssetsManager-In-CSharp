using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 天气数据解析模块。
/// </summary>
public static class BTLWeatherModule
{
    private const int WeatherSize = 16;

    public static List<Weather> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Weather>();

        var weathers = new List<Weather>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * WeatherSize);
            if (startPos + WeatherSize > data.Length) break;

            var weather = Weather.FromBytes(data, startPos);
            weathers.Add(weather);
        }

        Debug.WriteLine($"[BTLWeatherModule] 解析 {weathers.Count} 个天气");
        return weathers;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * WeatherSize);
}
