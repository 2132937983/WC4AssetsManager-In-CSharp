using System.Runtime.InteropServices;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 天气数据结构 (16字节) - 使用结构体减少内存开销
/// <para>
/// 0x00 天气类型（setting.txt [天气修改] WeatherList，游戏里 0 无 / 1 下雨 / 2 暴雨 / 3 下雪），
/// 0x04 保留（游戏数据里出现过非 0 值，必须原样保留；VB 版读写时跳过这 4 字节，保存后被清零），
/// 0x08 触发回合，0x0C 持续回合：天气覆盖第 <see cref="TriggerRound"/> 到 <see cref="EndRound"/> 回合。
/// </para>
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

    /// <summary>最后一个生效回合（持续回合不足 1 时按只在触发回合生效计）。</summary>
    public readonly int EndRound => TriggerRound + Math.Max(Duration, 1) - 1;

    /// <summary>生效回合是否与 [<paramref name="startRound"/>, <paramref name="endRound"/>] 有交集。</summary>
    public readonly bool Overlaps(int startRound, int endRound) => TriggerRound <= endRound && EndRound >= startRound;

    /// <summary>生效回合的显示文本，例如 "第 5–9 回合"（只有一回合时为 "第 5 回合"）。</summary>
    public readonly string FormatRounds()
        => EndRound > TriggerRound
            ? Loc.F("Module.Weather.Rounds", TriggerRound, EndRound)
            : Loc.F("Module.Weather.RoundSingle", TriggerRound);

    /// <summary>
    /// 标出生效回合与其它天气重叠的条目（结果下标与 <paramref name="weathers"/> 对应）。
    /// 按触发回合扫描一遍：当前条目开始时，之前出现过的最晚结束回合还没过，就与那一条重叠。
    /// </summary>
    public static bool[] FindOverlaps(IReadOnlyList<Weather> weathers)
    {
        var flags = new bool[weathers.Count];
        var order = Enumerable.Range(0, weathers.Count).OrderBy(i => weathers[i].TriggerRound).ToArray();
        int latestEnd = int.MinValue, latestIndex = -1;
        foreach (int i in order)
        {
            var w = weathers[i];
            if (latestIndex >= 0 && w.TriggerRound <= latestEnd)
            {
                flags[i] = true;
                flags[latestIndex] = true;
            }
            if (w.EndRound > latestEnd)
            {
                latestEnd = w.EndRound;
                latestIndex = i;
            }
        }
        return flags;
    }

    /// <summary>
    /// 获取天气类型名称
    /// </summary>
    public readonly string GetWeatherTypeName() => GetTypeName(WeatherType);

    /// <summary>
    /// 可选的天气类型：setting.txt [天气修改] WeatherList；没有配置时退回游戏内置的 0–3。
    /// </summary>
    public static IReadOnlyList<ConfigKeyValue> GetTypeOptions()
    {
        var configured = ConfigManager.Instance.GetWeatherEditConfig().WeatherList;
        if (configured.Count > 0) return configured;

        return new[]
        {
            new ConfigKeyValue { Value = 0, Name = Loc.T("Module.Weather.Type.0", "无") },
            new ConfigKeyValue { Value = 1, Name = Loc.T("Module.Weather.Type.1", "下雨") },
            new ConfigKeyValue { Value = 2, Name = Loc.T("Module.Weather.Type.2", "暴雨") },
            new ConfigKeyValue { Value = 3, Name = Loc.T("Module.Weather.Type.3", "下雪") },
        };
    }

    /// <summary>天气类型名称；不在类型表里时返回 "未知(值)"。</summary>
    public static string GetTypeName(int weatherType)
        => ConfigKeyValue.NameOf(GetTypeOptions(), weatherType) ?? Loc.F("Module.Weather.TypeUnknown", weatherType);

    /// <summary>类型是否在类型表里（不在表里的值游戏未必认识）。</summary>
    public static bool IsKnownType(int weatherType) => ConfigKeyValue.NameOf(GetTypeOptions(), weatherType) != null;
}
