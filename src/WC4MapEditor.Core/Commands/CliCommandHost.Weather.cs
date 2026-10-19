using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 weather 命令组：天气编辑模式（Q 列表窗口 / R 随机生成）的命令行版。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildWeatherCommand()
    {
        var cmd = new Command("weather", "天气列表操作（天气与格子无关：第几回合开始、持续几回合、什么天气）");

        var listCmd = new Command("list", "列出所有天气（索引从 0 起，生效回合重叠的条目会标出）");
        listCmd.SetAction(_ => WithModifiers((mapData, _) => PrintWeathers(mapData), readOnly: true));

        var typesCmd = new Command("types", "列出天气类型（setting.txt [天气修改] WeatherList）");
        typesCmd.SetAction(_ => WithModifiers((_, _) =>
        {
            foreach (var option in Weather.GetTypeOptions())
                _output.WriteLine($"{option.Value,4}  {option.Name}");
        }, readOnly: true));

        var addCmd = new Command("add", "追加一条天气");
        var addType = new Argument<int>("type") { Description = "天气类型（见 weather types）" };
        var addRound = new Argument<int>("round") { Description = "触发回合（≥1）" };
        var addDuration = new Argument<int>("duration") { Description = "持续回合（≥1）" };
        addCmd.Arguments.Add(addType);
        addCmd.Arguments.Add(addRound);
        addCmd.Arguments.Add(addDuration);
        addCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<WeatherModifier>()!;
            var weather = new Weather
            {
                WeatherType = parseResult.GetValue(addType),
                TriggerRound = parseResult.GetValue(addRound),
                Duration = parseResult.GetValue(addDuration)
            };
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.T("Module.Weather.UndoAdd", "新增天气"), mapData.Weathers, () => result = module.AddWeather(weather));
            PrintWeatherResult(result);
        }));

        var setCmd = new Command("set", "修改一条天气（只改给出的字段，保留字段不变）");
        var setIndex = new Argument<int>("index") { Description = "天气索引（0 起，见 weather list）" };
        var setType = new Option<int?>("--type", "-t") { Description = "天气类型" };
        var setRound = new Option<int?>("--round", "-r") { Description = "触发回合（≥1）" };
        var setDuration = new Option<int?>("--duration", "-d") { Description = "持续回合（≥1）" };
        setCmd.Arguments.Add(setIndex);
        setCmd.Options.Add(setType);
        setCmd.Options.Add(setRound);
        setCmd.Options.Add(setDuration);
        setCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<WeatherModifier>()!;
            int index = parseResult.GetValue(setIndex);
            if (module.GetWeather(index) is not { } weather)
            {
                PrintWeatherResult(ModifierResult.Fail(Loc.F("Module.Weather.IndexOutOfRange", index)));
                return;
            }
            weather.WeatherType = parseResult.GetValue(setType) ?? weather.WeatherType;
            weather.TriggerRound = parseResult.GetValue(setRound) ?? weather.TriggerRound;
            weather.Duration = parseResult.GetValue(setDuration) ?? weather.Duration;
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.F("Module.Weather.UndoEdit", index + 1), mapData.Weathers, () => result = module.UpdateWeather(index, weather));
            PrintWeatherResult(result);
        }));

        var removeCmd = new Command("remove", "删除一条天气");
        var removeIndex = new Argument<int>("index") { Description = "天气索引（0 起，见 weather list）" };
        removeCmd.Arguments.Add(removeIndex);
        removeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<WeatherModifier>()!;
            int index = parseResult.GetValue(removeIndex);
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.F("Module.Weather.UndoDelete", index + 1), mapData.Weathers, () => result = module.DeleteWeather(index));
            PrintWeatherResult(result);
        }));

        var clearCmd = new Command("clear", "删除所有天气");
        clearCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<WeatherModifier>()!;
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.T("Module.Weather.UndoClear", "清空天气"), mapData.Weathers, () => result = module.DeleteAllWeathers());
            PrintWeatherResult(result);
        }));

        var randomCmd = new Command("random", "在一段回合内随机生成首尾相接的天气序列（与天气模式 R 键相同）");
        var randomStart = new Argument<int>("start") { Description = "初始回合（≥1）" };
        var randomEnd = new Argument<int>("end") { Description = "最终回合（≥初始回合）" };
        var randomKeep = new Option<bool>("--keep", "-k") { Description = "保留与该回合范围重叠的现有天气（默认先删除它们）" };
        randomCmd.Arguments.Add(randomStart);
        randomCmd.Arguments.Add(randomEnd);
        randomCmd.Options.Add(randomKeep);
        randomCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<WeatherModifier>()!;
            int start = parseResult.GetValue(randomStart);
            int end = parseResult.GetValue(randomEnd);
            bool keep = parseResult.GetValue(randomKeep);
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.F("Module.Weather.UndoRandom", start, end), mapData.Weathers,
                () => result = module.GenerateRandomWeatherSequence(start, end, replaceOverlapping: !keep));
            PrintWeatherResult(result);
        }));

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(typesCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(randomCmd);
        return cmd;
    }

    private void PrintWeathers(MapData mapData)
    {
        var weathers = mapData.Weathers;
        _output.WriteLine(Loc.F("Cli.Weather.Total", weathers.Count));
        bool[] overlaps = Weather.FindOverlaps(weathers);
        for (int i = 0; i < weathers.Count; i++)
        {
            var w = weathers[i];
            string mark = overlaps[i] ? Loc.T("Cli.Weather.OverlapMark", "  [重叠]") : "";
            _output.WriteLine(Loc.F("Cli.Weather.Row", i, w.GetWeatherTypeName(), w.WeatherType, w.FormatRounds(), w.TriggerRound, w.Duration, w.Reserved1) + mark);
        }
    }

    private void PrintWeatherResult(ModifierResult result)
        => _output.WriteLine(result.Success ? result.Message : Loc.F("Cli.Weather.Failed", result.Message));

    /// <summary>
    /// 列表类数据（天气 / 战略建设）修改的撤销入口：按整表前后快照记录一步撤销；
    /// 脚本批量执行（run xxx.zme）时直接执行、不记撤销，与其它命令组的约定一致。
    /// </summary>
    private void RecordCliListChange<T>(EditModeManager mgr, string description, IList<T> list, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        mgr.RecordCollectionChange(description, list, applyChange);
    }
}
