using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 strategic 命令组：战略建设编辑模式（Q 列表窗口）的命令行版。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildStrategicCommand()
    {
        var cmd = new Command("strategic", "战略建设列表操作（某军团从第几回合起按哪条策略分配生产，与格子无关）");

        var listCmd = new Command("list", "列出所有战略建设（索引从 0 起）");
        listCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<StrategicConstructionModifier>()!;
            var constructions = mapData.StrategyConstructions;
            _output.WriteLine(Loc.F("Cli.Strategic.Total", constructions.Count));
            for (int i = 0; i < constructions.Count; i++)
            {
                var sc = constructions[i];
                _output.WriteLine(Loc.F("Cli.Strategic.Row", i, module.DescribeLegion(sc.LegionId), sc.ExecuteRound,
                    StrategicConstructionModifier.DescribeCode(sc.ConstructionCode), sc.Reserved1));
            }
        }, readOnly: true));

        var codesCmd = new Command("codes", "列出建设代码（游戏 config/def_strategy.xml 的策略）");
        var codesFilter = new Argument<string?>("filter") { Description = "只列名称或类别包含该文字的策略（如 navy、步兵）", Arity = ArgumentArity.ZeroOrOne };
        codesCmd.Arguments.Add(codesFilter);
        codesCmd.SetAction(parseResult => WithModifiers((_, _) =>
        {
            string filter = parseResult.GetValue(codesFilter)?.Trim() ?? "";
            int count = 0;
            foreach (var definition in StrategyCatalog.All)
            {
                if (filter.Length > 0
                    && !definition.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !definition.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !definition.CategoryName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _output.WriteLine($"{definition.Id,6}  {definition.DisplayName}");
                count++;
            }
            _output.WriteLine(Loc.F("Cli.Strategic.CodeCount", count));
        }, readOnly: true));

        var addCmd = new Command("add", "追加一条战略建设");
        var addLegion = new Argument<int>("legion") { Description = "军团序号（军团的 ActionId，见 legion list / legion get-by-action）" };
        var addRound = new Argument<int>("round") { Description = "执行回合（≥1）" };
        var addCode = new Argument<int>("code") { Description = "建设代码（见 strategic codes）" };
        var addReserved = new Option<int>("--reserved") { Description = "保留字段 0x04（默认 0）" };
        addCmd.Arguments.Add(addLegion);
        addCmd.Arguments.Add(addRound);
        addCmd.Arguments.Add(addCode);
        addCmd.Options.Add(addReserved);
        addCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<StrategicConstructionModifier>()!;
            var construction = new StrategicConstruction
            {
                LegionId = parseResult.GetValue(addLegion),
                Reserved1 = parseResult.GetValue(addReserved),
                ExecuteRound = parseResult.GetValue(addRound),
                ConstructionCode = parseResult.GetValue(addCode)
            };
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.T("Module.Strategic.UndoAdd", "新增战略建设"), mapData.StrategyConstructions,
                () => result = module.AddConstruction(construction));
            PrintStrategicResult(result);
        }));

        var setCmd = new Command("set", "修改一条战略建设（只改给出的字段）");
        var setIndex = new Argument<int>("index") { Description = "战略建设索引（0 起，见 strategic list）" };
        var setLegion = new Option<int?>("--legion", "-l") { Description = "军团序号（ActionId）" };
        var setRound = new Option<int?>("--round", "-r") { Description = "执行回合（≥1）" };
        var setCode = new Option<int?>("--code", "-c") { Description = "建设代码" };
        var setReserved = new Option<int?>("--reserved") { Description = "保留字段 0x04" };
        setCmd.Arguments.Add(setIndex);
        setCmd.Options.Add(setLegion);
        setCmd.Options.Add(setRound);
        setCmd.Options.Add(setCode);
        setCmd.Options.Add(setReserved);
        setCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<StrategicConstructionModifier>()!;
            int index = parseResult.GetValue(setIndex);
            if (module.GetConstruction(index) is not { } construction)
            {
                PrintStrategicResult(ModifierResult.Fail(Loc.F("Module.Strategic.IndexOutOfRange", index)));
                return;
            }
            construction.LegionId = parseResult.GetValue(setLegion) ?? construction.LegionId;
            construction.ExecuteRound = parseResult.GetValue(setRound) ?? construction.ExecuteRound;
            construction.ConstructionCode = parseResult.GetValue(setCode) ?? construction.ConstructionCode;
            construction.Reserved1 = parseResult.GetValue(setReserved) ?? construction.Reserved1;
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.F("Module.Strategic.UndoEdit", index + 1), mapData.StrategyConstructions,
                () => result = module.UpdateConstruction(index, construction));
            PrintStrategicResult(result);
        }));

        var removeCmd = new Command("remove", "删除一条战略建设");
        var removeIndex = new Argument<int>("index") { Description = "战略建设索引（0 起，见 strategic list）" };
        removeCmd.Arguments.Add(removeIndex);
        removeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<StrategicConstructionModifier>()!;
            int index = parseResult.GetValue(removeIndex);
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.F("Module.Strategic.UndoDelete", index + 1), mapData.StrategyConstructions,
                () => result = module.DeleteConstruction(index));
            PrintStrategicResult(result);
        }));

        var clearCmd = new Command("clear", "删除所有战略建设");
        clearCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
        {
            var module = mgr.GetModifier<StrategicConstructionModifier>()!;
            ModifierResult result = default;
            RecordCliListChange(mgr, Loc.T("Module.Strategic.UndoClear", "清空战略建设"), mapData.StrategyConstructions,
                () => result = module.DeleteAllConstructions());
            PrintStrategicResult(result);
        }));

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(codesCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(clearCmd);
        return cmd;
    }

    private void PrintStrategicResult(ModifierResult result)
        => _output.WriteLine(result.Success ? result.Message : Loc.F("Cli.Strategic.Failed", result.Message));
}
