using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 天气数据结构 (16字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Weather
{
    public int WeatherType;
    public int Reserved1;
    public int TriggerRound;
    public int Duration;

    /// <summary>
    /// 从字节数组解析天气数据
    /// </summary>
    public static Weather FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Weather>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认天气
    /// </summary>
    public static Weather CreateDefault() => new Weather();

    /// <summary>
    /// 获取天气类型名称
    /// </summary>
    public readonly string GetWeatherTypeName() => WeatherType switch
    {
        0 => "无",
        1 => "下雨",
        2 => "暴雨",
        3 => "下雪",
        _ => $"未知({WeatherType})"
    };
}
