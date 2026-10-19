using System.CommandLine;
using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 case 命令组：方案（AI 方针）列表的增删改查，以及单位方针值的设置。
// 方案列表的修改记录到方案列表的撤销快照，单位方针值的修改按地图版本记录 v1/v2 或 v3 单位列表；脚本批量执行时不记撤销。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildCaseCommand()
    {
        var cmd = new Command("case", "方案（AI 方针）操作：方针值等于方针编号的单位执行该方案");

        cmd.Subcommands.Add(BuildCaseListCommand());
        cmd.Subcommands.Add(BuildCaseAddCommand());
        cmd.Subcommands.Add(BuildCaseAddTargetCommand());
        cmd.Subcommands.Add(BuildCaseSetCommand());
        cmd.Subcommands.Add(BuildCaseRemoveCommand());
        cmd.Subcommands.Add(BuildCaseRemoveTargetCommand());
        cmd.Subcommands.Add(BuildCaseClearCommand());
        cmd.Subcommands.Add(BuildCaseSetPolicyCommand());
        cmd.Subcommands.Add(BuildCasePolicyByBelongCommand());
        cmd.Subcommands.Add(BuildCaseRandomizePolicyCommand());
        cmd.Subcommands.Add(BuildCaseUnitsCommand());
        return cmd;
    }

    private Command BuildCaseListCommand()
    {
        var listCmd = new Command("list", "列出所有方案（序号从 0 开始）");
        listCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var cases = mapData.Cases;
            _output.WriteLine(Loc.F("Cli.MapCase.Count", cases.Count));
            if (cases.Count == 0) return;

            var types = ConfigManager.Instance.GetCaseEditConfig().CaseList;
            _output.WriteLine();
            _output.WriteLine($"{Loc.T("Cli.MapCase.Col.Index", "序号"),-6} {Loc.T("Cli.MapCase.Col.Policy", "方针编号"),-8} "
                + $"{Loc.T("Cli.MapCase.Col.Round", "触发回合"),-8} {Loc.T("Cli.MapCase.Col.Target", "目标值"),-14} {Loc.T("Cli.MapCase.Col.Type", "方针类型")}");
            _output.WriteLine(new string('-', 64));
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                string type = ConfigKeyValue.NameOf(types, c.CaseType) is { } name ? $"{c.CaseType} {name}" : CaseNumber(c.CaseType);
                _output.WriteLine($"{i,-6} {c.PolicyNumber,-8} {c.TriggerRound,-8} {DescribeCaseTarget(mapData, c),-14} {type}");
            }
        }, readOnly: true));
        return listCmd;
    }

    private Command BuildCaseAddCommand()
    {
        var addCmd = new Command("add", "新增方案（方针编号默认取下一个空闲编号，其余字段默认 0）");
        var policyOpt = new Option<int?>("--policy", "-p") { Description = "方针编号（0-255）" };
        var typeOpt = new Option<int?>("--type", "-t") { Description = "方针类型（见 setting.txt [方针修改] CaseList）" };
        var roundOpt = new Option<int?>("--round", "-r") { Description = "触发回合" };
        var targetOpt = new Option<int?>("--target") { Description = "目标值（类型 2 时为地块序号 = 行×地图宽+列）" };
        addCmd.Options.Add(policyOpt);
        addCmd.Options.Add(typeOpt);
        addCmd.Options.Add(roundOpt);
        addCmd.Options.Add(targetOpt);
        addCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            var mapCase = module.CreateCase();
            mapCase.PolicyNumber = parseResult.GetValue(policyOpt) ?? mapCase.PolicyNumber;
            mapCase.CaseType = parseResult.GetValue(typeOpt) ?? mapCase.CaseType;
            mapCase.TriggerRound = parseResult.GetValue(roundOpt) ?? mapCase.TriggerRound;
            mapCase.TargetTile = parseResult.GetValue(targetOpt) ?? mapCase.TargetTile;

            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.T("Cli.MapCase.Undo.Add", "命令行：新增方案"), () => result = module.AddCase(mapCase));
            WriteCaseResult(result);
        }));
        return addCmd;
    }

    private Command BuildCaseAddTargetCommand()
    {
        var addTargetCmd = new Command("add-target", "新增\"使单位走向目标值序号地块\"的方案，目标为 (col,row)");
        var colArg = new Argument<int>("col") { Description = "列" };
        var rowArg = new Argument<int>("row") { Description = "行" };
        var policyOpt = new Option<int?>("--policy", "-p") { Description = "方针编号（0-255，默认取下一个空闲编号）" };
        var roundOpt = new Option<int?>("--round", "-r") { Description = "触发回合（默认 0）" };
        addTargetCmd.Arguments.Add(colArg);
        addTargetCmd.Arguments.Add(rowArg);
        addTargetCmd.Options.Add(policyOpt);
        addTargetCmd.Options.Add(roundOpt);
        addTargetCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            var template = module.CreateCase();
            template.PolicyNumber = parseResult.GetValue(policyOpt) ?? template.PolicyNumber;
            template.TriggerRound = parseResult.GetValue(roundOpt) ?? template.TriggerRound;

            int col = parseResult.GetValue(colArg), row = parseResult.GetValue(rowArg);
            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.AddTarget", col, row), () => result = module.Apply(col, row, template));
            WriteCaseResult(result);
        }));
        return addTargetCmd;
    }

    private Command BuildCaseSetCommand()
    {
        var setCmd = new Command("set", "修改方案（只改给出的字段）");
        var indexArg = new Argument<int>("index") { Description = "方案序号（case list 里的序号，从 0 开始）" };
        var policyOpt = new Option<int?>("--policy", "-p") { Description = "方针编号（0-255）" };
        var typeOpt = new Option<int?>("--type", "-t") { Description = "方针类型" };
        var roundOpt = new Option<int?>("--round", "-r") { Description = "触发回合" };
        var targetOpt = new Option<int?>("--target") { Description = "目标值（类型 2 时为地块序号）" };
        setCmd.Arguments.Add(indexArg);
        setCmd.Options.Add(policyOpt);
        setCmd.Options.Add(typeOpt);
        setCmd.Options.Add(roundOpt);
        setCmd.Options.Add(targetOpt);
        setCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int index = parseResult.GetValue(indexArg);
            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.Set", index), () => result = module.UpdateCaseFields(index,
                parseResult.GetValue(policyOpt), parseResult.GetValue(typeOpt), parseResult.GetValue(roundOpt), parseResult.GetValue(targetOpt)));
            WriteCaseResult(result);
        }));
        return setCmd;
    }

    private Command BuildCaseRemoveCommand()
    {
        var removeCmd = new Command("remove", "删除方案");
        var indexArg = new Argument<int>("index") { Description = "方案序号（从 0 开始）" };
        removeCmd.Arguments.Add(indexArg);
        removeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int index = parseResult.GetValue(indexArg);
            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.Remove", index), () => result = module.DeleteCase(index));
            WriteCaseResult(result);
        }));
        return removeCmd;
    }

    private Command BuildCaseRemoveTargetCommand()
    {
        var removeTargetCmd = new Command("remove-target", "删除以 (col,row) 为目标地块的方案");
        var colArg = new Argument<int>("col") { Description = "列" };
        var rowArg = new Argument<int>("row") { Description = "行" };
        removeTargetCmd.Arguments.Add(colArg);
        removeTargetCmd.Arguments.Add(rowArg);
        removeTargetCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int col = parseResult.GetValue(colArg), row = parseResult.GetValue(rowArg);
            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.RemoveTarget", col, row), () => result = module.Remove(col, row));
            WriteCaseResult(result);
        }));
        return removeTargetCmd;
    }

    private Command BuildCaseClearCommand()
    {
        var clearCmd = new Command("clear", "删除全部方案");
        clearCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            var result = default(ModifierResult);
            RecordCaseListChange(mgr, mapData, Loc.T("Cli.MapCase.Undo.Clear", "命令行：删除全部方案"), () => result = module.DeleteAllCases());
            WriteCaseResult(result);
        }));
        return clearCmd;
    }

    private Command BuildCaseSetPolicyCommand()
    {
        var setPolicyCmd = new Command("set-policy", "设置 (col,row) 上单位的方针值");
        var colArg = new Argument<int>("col") { Description = "列" };
        var rowArg = new Argument<int>("row") { Description = "行" };
        var policyArg = new Argument<int>("policy") { Description = "方针值（0-255）" };
        setPolicyCmd.Arguments.Add(colArg);
        setPolicyCmd.Arguments.Add(rowArg);
        setPolicyCmd.Arguments.Add(policyArg);
        setPolicyCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int col = parseResult.GetValue(colArg), row = parseResult.GetValue(rowArg), policy = parseResult.GetValue(policyArg);
            var result = default(ModifierResult);
            RecordUnitPolicyChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.SetPolicy", col, row, policy),
                () => result = module.SetPolicyForUnitsAt(new[] { new HexCoord(col, row) }, policy));
            WriteCaseResult(result);
        }));
        return setPolicyCmd;
    }

    private Command BuildCasePolicyByBelongCommand()
    {
        var byBelongCmd = new Command("policy-by-belong", "按归属批量设置单位的方针值");
        var belongArg = new Argument<int>("belong") { Description = "归属值（-1 = 全部单位）" };
        var policyArg = new Argument<int>("policy") { Description = "方针值（0-255）" };
        byBelongCmd.Arguments.Add(belongArg);
        byBelongCmd.Arguments.Add(policyArg);
        byBelongCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int belong = parseResult.GetValue(belongArg), policy = parseResult.GetValue(policyArg);
            var result = default(ModifierResult);
            RecordUnitPolicyChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.PolicyByBelong", belong, policy),
                () => result = module.SetPolicyByBelong(belong, policy));
            WriteCaseResult(result);
        }));
        return byBelongCmd;
    }

    private Command BuildCaseRandomizePolicyCommand()
    {
        var randomizeCmd = new Command("randomize-policy", "按归属把单位的方针值随机设为 0-255");
        var belongArg = new Argument<int>("belong") { Description = "归属值（-1 = 全部单位）" };
        randomizeCmd.Arguments.Add(belongArg);
        randomizeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var module = CaseModule(mapData, mgr);
            int belong = parseResult.GetValue(belongArg);
            var result = default(ModifierResult);
            RecordUnitPolicyChange(mgr, mapData, Loc.F("Cli.MapCase.Undo.RandomizePolicy", belong),
                () => result = module.RandomizePolicyByBelong(belong));
            WriteCaseResult(result);
        }));
        return randomizeCmd;
    }

    private Command BuildCaseUnitsCommand()
    {
        var unitsCmd = new Command("units", "列出方针值为 policy 的单位");
        var policyArg = new Argument<int>("policy") { Description = "方针值（0-255）" };
        unitsCmd.Arguments.Add(policyArg);
        unitsCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int policy = parseResult.GetValue(policyArg);
            var cells = CaseModule(mapData, mgr).GetUnitCellsWithPolicy(policy);
            _output.WriteLine(Loc.F("Cli.MapCase.UnitCount", policy, cells.Count));
            if (cells.Count > 0)
                _output.WriteLine(string.Join(" ", cells.Select(c => $"({c.Col},{c.Row})")));
        }, readOnly: true));
        return unitsCmd;
    }

    // ------------------------------------------------------------------ 辅助

    /// <summary>优先复用 EditModeManager 里注册的实例（保留复制的方案等状态）；没对这份地图初始化时临时建一个。</summary>
    private static MapCaseModifier CaseModule(MapData mapData, EditModeManager mgr)
    {
        var module = mgr.GetModifier<MapCaseModifier>();
        if (module != null && module.IsInitializedFor(mapData)) return module;
        module = new MapCaseModifier();
        module.Initialize(mapData);
        return module;
    }

    private void RecordCaseListChange(EditModeManager mgr, MapData mapData, string description, Action apply)
    {
        if (_batchMode)
        {
            apply();
            return;
        }
        mgr.RecordCollectionChange(description, mapData.Cases, apply);
    }

    private void RecordUnitPolicyChange(EditModeManager mgr, MapData mapData, string description, Action apply)
    {
        if (_batchMode)
        {
            apply();
            return;
        }
        if (MapCaseModifier.UsesV3Units(mapData))
            mgr.RecordCollectionChange(description, mapData.ArmiesV3, apply);
        else
            mgr.RecordCollectionChange(description, mapData.Armies, apply);
    }

    private void WriteCaseResult(ModifierResult result) => _output.WriteLine(result.Message ?? "");

    /// <summary>目标值的显示：走向地块类型且在地图内时写成 "序号(列,行)"，否则写数值。</summary>
    private static string DescribeCaseTarget(MapData mapData, MapCase c)
    {
        int width = mapData.MapWidth;
        if (c.TargetsTile && width > 0 && c.TargetTile >= 0 && c.TargetTile < width * mapData.MapHeight)
            return $"{c.TargetTile}({c.TargetTile % width},{c.TargetTile / width})";
        return CaseNumber(c.TargetTile);
    }

    private static string CaseNumber(int value) => value.ToString(CultureInfo.InvariantCulture);
}
