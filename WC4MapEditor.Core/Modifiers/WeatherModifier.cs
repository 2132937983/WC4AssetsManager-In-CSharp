using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class WeatherModifier : ModifierBase
{
    public override string Name => "weather";
    public override string DisplayName => "天气修改器";

    private Weather? _copiedWeather;
    private readonly Random _random = new();

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        Weather weather;
        if (parameter is Weather w)
            weather = w;
        else
            weather = Weather.CreateDefault();

        _mapData.Weathers.Add(weather);
        MarkModified();
        return ModifierResult.Ok($"已添加天气数据");
    }

    public override ModifierResult Remove(int col, int row)
    {
        return ModifierResult.Fail("天气不支持按坐标删除，请使用DeleteWeather");
    }

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;

    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 天气CRUD

    public ModifierResult AddWeather(Weather weather)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.Weathers.Add(weather);
        MarkModified();
        return ModifierResult.Ok($"已添加天气: {weather.GetWeatherTypeName()}, 回合={weather.TriggerRound}");
    }

    public ModifierResult UpdateWeather(int index, Weather weather)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Weathers.Count) return ModifierResult.Fail("天气索引超出范围");

        _mapData.Weathers[index] = weather;
        MarkModified();
        return ModifierResult.Ok($"已更新天气: {weather.GetWeatherTypeName()}");
    }

    public ModifierResult DeleteWeather(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Weathers.Count) return ModifierResult.Fail("天气索引超出范围");

        _mapData.Weathers.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除天气数据");
    }

    public ModifierResult DeleteAllWeathers()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Weathers.Count;
        _mapData.Weathers.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有天气数据，共 {count} 个");
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyWeather(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Weathers.Count) return ModifierResult.Fail("天气索引超出范围");

        _copiedWeather = _mapData.Weathers[index];
        return ModifierResult.Ok($"已复制天气: {_copiedWeather.Value.GetWeatherTypeName()}");
    }

    public ModifierResult PasteWeather()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedWeather == null) return ModifierResult.Fail("没有已复制的天气数据");

        _mapData.Weathers.Add(_copiedWeather.Value);
        MarkModified();
        return ModifierResult.Ok("已粘贴天气数据");
    }

    #endregion

    #region 随机生成

    public ModifierResult GenerateRandomWeather(int startRound, int endRound)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (startRound < 1) return ModifierResult.Fail("初始回合必须大于等于1");
        if (endRound < startRound) return ModifierResult.Fail("最终回合必须大于等于初始回合");

        int generatedCount = 0;
        for (int round = startRound; round <= endRound; round++)
        {
            int weatherType = _random.Next(0, 4);
            int duration = _random.Next(1, 4);

            var weather = new Weather
            {
                WeatherType = weatherType,
                Reserved1 = 0,
                TriggerRound = round,
                Duration = duration
            };

            _mapData.Weathers.Add(weather);
            generatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个天气数据 (回合 {startRound}-{endRound})");
    }

    public ModifierResult GenerateRandomWeatherSequence(int startRound, int endRound, double meanDuration = 5.0, double stdDevDuration = 2.0)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (startRound < 1) return ModifierResult.Fail("初始回合必须大于等于1");
        if (endRound < startRound) return ModifierResult.Fail("最终回合必须大于等于初始回合");

        var weatherTypes = GetAvailableWeatherTypes();
        if (weatherTypes.Count == 0)
            return ModifierResult.Fail("没有可用的天气类型");

        int generatedCount = 0;
        int currentRound = startRound;

        while (currentRound <= endRound)
        {
            int weatherType = weatherTypes[_random.Next(weatherTypes.Count)];
            int duration = GenerateNormalRandom(meanDuration, stdDevDuration);

            if (duration < 1) duration = 1;
            if (currentRound + duration - 1 > endRound)
                duration = endRound - currentRound + 1;

            if (duration < 1) break;

            var weather = new Weather
            {
                WeatherType = weatherType,
                Reserved1 = 0,
                TriggerRound = currentRound,
                Duration = duration
            };

            _mapData.Weathers.Add(weather);
            generatedCount++;

            currentRound += duration;

            if (generatedCount > 1000) break;
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个连续天气数据 (回合 {startRound}-{endRound})");
    }

    private List<int> GetAvailableWeatherTypes()
    {
        var weatherTypes = new List<int>();

        try
        {
            var config = ConfigManager.Instance.GetWeatherEditConfig();
            if (config.WeatherList.Count > 0)
            {
                foreach (var item in config.WeatherList)
                {
                    if (item.Value > 0)
                        weatherTypes.Add(item.Value);
                }
            }
        }
        catch
        {
        }

        if (weatherTypes.Count == 0)
            weatherTypes.AddRange([1, 2, 3]);

        return weatherTypes;
    }

    private int GenerateNormalRandom(double mean, double stdDev)
    {
        double u1 = _random.NextDouble();
        double u2 = _random.NextDouble();

        if (u1 < 0.0001) u1 = 0.0001;

        double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        double result = mean + stdDev * z0;

        return (int)Math.Round(result);
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
}