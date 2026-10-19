using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 天气模块：天气是与格子无关的全局列表（第几回合开始、持续几回合、什么天气）。
/// 提供增删改、复制粘贴、新条目默认值、校验、重叠检查与 VB 版的随机天气序列生成。
/// </summary>
public sealed class WeatherModifier : ModifierBase
{
    /// <summary>触发回合 / 持续回合的上限（格式里是 int，这里只拦明显的误输入）。</summary>
    public const int MaxRound = 9999;

    /// <summary>随机生成的条数上限（对齐 VB 的防死循环保护）。</summary>
    private const int MaxGenerated = 1000;

    // VB GenerateWeatherSequence 的正态分布参数：平均持续 5 回合，标准差 2 回合
    private const double MeanDuration = 5.0;
    private const double StdDevDuration = 2.0;

    public override string Name => "weather";
    public override string DisplayName => Loc.T("Module.Weather.Name", "天气修改器");

    private Weather? _copiedWeather;
    private readonly Random _random = new();

    #region 基础CRUD（天气与格子无关）

    public override ModifierResult Apply(int col, int row, object? parameter = null) => NotCellBased();
    public override ModifierResult Remove(int col, int row) => NotCellBased();
    public override bool CanApply(int col, int row) => false;
    public override bool CanRemove(int col, int row) => false;
    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    // 早先的 Apply 无视坐标、每次追加一条默认天气：任何"点击即 apply"的路由都会悄悄往列表里加数据
    private static ModifierResult NotCellBased()
        => ModifierResult.Fail(Loc.T("Module.Weather.NotCellBased", "天气是全局列表，不能按格子添加或删除，请在天气列表中编辑"));

    #endregion

    #region 天气CRUD

    public ModifierResult AddWeather(Weather weather)
    {
        if (_mapData == null) return NotReady();
        if (Validate(weather) is { } error) return ModifierResult.Fail(error);

        _mapData.Weathers.Add(weather);
        MarkModified();
        return ModifierResult.Ok(WithTypeWarning(Loc.F("Module.Weather.Added", weather.GetWeatherTypeName(), weather.TriggerRound), weather.WeatherType));
    }

    public ModifierResult UpdateWeather(int index, Weather weather)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);
        if (Validate(weather) is { } error) return ModifierResult.Fail(error);

        _mapData.Weathers[index] = weather;
        MarkModified();
        return ModifierResult.Ok(WithTypeWarning(Loc.F("Module.Weather.Updated", index + 1, weather.GetWeatherTypeName()), weather.WeatherType));
    }

    public ModifierResult DeleteWeather(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        _mapData.Weathers.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Weather.Deleted", index + 1));
    }

    public ModifierResult DeleteAllWeathers()
    {
        if (_mapData == null) return NotReady();

        int count = _mapData.Weathers.Count;
        _mapData.Weathers.Clear();
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Weather.DeletedAll", count), count);
    }

    /// <summary>
    /// 复制第 <paramref name="index"/> 条天气，副本插在它后面（下标 index + 1），
    /// 并从原天气结束后的下一回合开始，不与原天气重叠。
    /// </summary>
    public ModifierResult DuplicateWeather(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        var copy = _mapData.Weathers[index];
        copy.TriggerRound = Math.Min(copy.EndRound + 1, MaxRound);
        _mapData.Weathers.Insert(index + 1, copy);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Weather.Duplicated", index + 1, copy.TriggerRound));
    }

    /// <summary>
    /// 新增天气的默认值：类型表里第一种真实天气（不取 0 "无"），
    /// 从现有天气全部结束后的下一回合开始，持续 1 回合（VB 新增的是全 0，触发 / 持续回合都无效）。
    /// </summary>
    public Weather CreateNextWeather()
    {
        int start = 1;
        if (_mapData != null)
        {
            foreach (var w in _mapData.Weathers)
                start = Math.Max(start, w.EndRound + 1);
        }
        return new Weather { WeatherType = GetAvailableWeatherTypes()[0], TriggerRound = Math.Min(start, MaxRound), Duration = 1 };
    }

    #endregion

    #region 校验

    /// <summary>
    /// 校验一条天气（窗口、命令行共用）：类型 ≥ 0，触发回合与持续回合在 1–<see cref="MaxRound"/>。
    /// 类型不在类型表里不算错误（列表与面板上显示为"未知"）。通过时返回 null。
    /// </summary>
    public static string? Validate(in Weather weather)
    {
        if (weather.WeatherType < 0) return Loc.F("Module.Weather.InvalidType", weather.WeatherType);
        if (weather.TriggerRound < 1 || weather.TriggerRound > MaxRound) return Loc.F("Module.Weather.InvalidRound", 1, MaxRound);
        if (weather.Duration < 1 || weather.Duration > MaxRound) return Loc.F("Module.Weather.InvalidDuration", 1, MaxRound);
        return null;
    }

    /// <summary>
    /// 类型不在类型表里时的提醒（照样允许保存：可能是表里没列的天气）；在表里时返回 null。
    /// 新增 / 修改成功的消息里会带上它。
    /// </summary>
    public static string? UnknownTypeWarning(int weatherType)
        => Weather.IsKnownType(weatherType) ? null : Loc.F("Module.Weather.UnknownType", weatherType);

    private static string WithTypeWarning(string message, int weatherType)
        => UnknownTypeWarning(weatherType) is { } warning ? Loc.F("Module.Weather.WithWarning", message, warning) : message;

    /// <summary>随机生成的回合范围校验（前两条与 VB 的提示一致）。通过时返回 null。</summary>
    public static string? ValidateRoundRange(int startRound, int endRound)
    {
        if (startRound < 1) return Loc.T("Module.Weather.StartTooSmall", "初始回合必须大于等于1");
        if (endRound < startRound) return Loc.T("Module.Weather.EndBeforeStart", "最终回合必须大于等于初始回合");
        if (endRound > MaxRound) return Loc.F("Module.Weather.EndTooLarge", MaxRound);
        return null;
    }

    /// <summary>与回合区间 [start, end] 重叠的天气条数。</summary>
    public int CountOverlapping(int startRound, int endRound)
        => _mapData?.Weathers.Count(w => w.Overlaps(startRound, endRound)) ?? 0;

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyWeather(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        _copiedWeather = _mapData.Weathers[index];
        return ModifierResult.Ok(Loc.F("Module.Weather.Copied", _copiedWeather.Value.GetWeatherTypeName()));
    }

    public ModifierResult PasteWeather()
    {
        if (_mapData == null) return NotReady();
        if (_copiedWeather == null) return ModifierResult.Fail(Loc.T("Module.Weather.NothingCopied", "没有已复制的天气数据"));

        _mapData.Weathers.Add(_copiedWeather.Value);
        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.Weather.Pasted", "已粘贴天气数据"));
    }

    #endregion

    #region 随机生成

    /// <summary>
    /// 在第 <paramref name="startRound"/>–<paramref name="endRound"/> 回合随机生成一段首尾相接的天气序列（对齐 VB GenerateWeatherSequence）：
    /// 类型从类型表里随机取（不取 0 "无"），持续回合按正态分布取整，至少 1 回合、不越过最终回合。
    /// <paramref name="replaceOverlapping"/> 为 true 时先删除与该范围重叠的天气 —— VB 直接追加，连按两次会得到互相重叠的天气。
    /// 成功时 <see cref="ModifierResult.AffectedCount"/> 为生成条数。
    /// </summary>
    public ModifierResult GenerateRandomWeatherSequence(int startRound, int endRound, bool replaceOverlapping = true)
    {
        if (_mapData == null) return NotReady();
        if (ValidateRoundRange(startRound, endRound) is { } error) return ModifierResult.Fail(error);

        var weatherTypes = GetAvailableWeatherTypes();
        int removed = replaceOverlapping ? RemoveOverlapping(startRound, endRound) : 0;

        int generated = 0;
        int currentRound = startRound;
        while (currentRound <= endRound && generated < MaxGenerated)
        {
            int duration = Math.Clamp(GenerateNormalRandom(MeanDuration, StdDevDuration), 1, endRound - currentRound + 1);
            _mapData.Weathers.Add(new Weather
            {
                WeatherType = weatherTypes[_random.Next(weatherTypes.Count)],
                TriggerRound = currentRound,
                Duration = duration
            });
            generated++;
            currentRound += duration;
        }

        MarkModified();
        string message = Loc.F("Module.Weather.Generated", generated, startRound, endRound);
        if (removed > 0) message += Loc.F("Module.Weather.GeneratedReplaced", removed);
        return ModifierResult.Ok(message, generated);
    }

    /// <summary>删除与回合区间 [start, end] 重叠的天气，返回删除条数。</summary>
    private int RemoveOverlapping(int startRound, int endRound)
    {
        var weathers = _mapData!.Weathers;
        int removed = 0;
        for (int i = weathers.Count - 1; i >= 0; i--)
        {
            if (!weathers[i].Overlaps(startRound, endRound)) continue;
            weathers.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    /// <summary>可用于生成的天气类型：类型表里除 0（无）以外的值；一个都没有时用游戏的 1–3。</summary>
    private static List<int> GetAvailableWeatherTypes()
    {
        var weatherTypes = Weather.GetTypeOptions().Where(o => o.Value > 0).Select(o => o.Value).ToList();
        if (weatherTypes.Count == 0)
            weatherTypes.AddRange([1, 2, 3]);
        return weatherTypes;
    }

    /// <summary>Box-Muller 变换生成正态分布随机数并四舍五入（与 VB 相同）。</summary>
    private int GenerateNormalRandom(double mean, double stdDev)
    {
        double u1 = Math.Max(_random.NextDouble(), 0.0001);
        double u2 = _random.NextDouble();
        double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return (int)Math.Round(mean + stdDev * z0);
    }

    #endregion

    #region 查询

    public IReadOnlyList<Weather> GetAllWeathers()
    {
        return _mapData?.Weathers ?? [];
    }

    public Weather? GetWeather(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.Weathers.Count) return null;
        return _mapData.Weathers[index];
    }

    #endregion

    private bool IsValidIndex(int index) => _mapData != null && index >= 0 && index < _mapData.Weathers.Count;

    private static ModifierResult NotReady() => ModifierResult.Fail(Loc.T("Module.Weather.NotReady", "地图数据未初始化"));

    private static ModifierResult IndexOutOfRange(int index)
        => ModifierResult.Fail(Loc.F("Module.Weather.IndexOutOfRange", index));
}
