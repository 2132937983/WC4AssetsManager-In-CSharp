using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Province 命令组。改数据的命令整体记一步撤销（脚本批量执行时不记，与归属命令组一致）。
public sealed partial class CliCommandHost
{
    private Command BuildProvinceCommand()
    {
        var cmd = new Command("province", "省份修改操作");

        cmd.Subcommands.Add(ProvinceMapCommand("clear", "清空所有省份", p => p.ClearAllProvinces()));
        cmd.Subcommands.Add(ProvinceMapCommand("generate", "生成孤立省会省区", p => p.GenerateProvincesForIsolatedCapitals()));
        cmd.Subcommands.Add(ProvinceMapCommand("expand", "扩展省区填满地图", p => p.ExpandAllProvincesToFillMap()));
        cmd.Subcommands.Add(ProvinceMapCommand("process", "处理孤立和空白省区", p => p.ProcessIsolatedAndEmptyProvinces()));

        var floodFillCmd = new Command("flood-fill", "洪水填充省份（省略省区值时用省份编辑模式 C 键复制的值）");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        var fillValueArg = new Argument<int?>("value")
        {
            Description = "填充的省区值(0-65535，65535=无省区；省略=用复制的值)",
            Arity = ArgumentArity.ZeroOrOne
        };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.Arguments.Add(fillValueArg);
        floodFillCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var province = mgr.GetModifier<ProvinceModifier>()!;
            int col = parseResult.GetValue(fillColArg);
            int row = parseResult.GetValue(fillRowArg);
            int? value = parseResult.GetValue(fillValueArg);
            if (value is < 0 or > Province.EmptyValue)
            {
                _output.WriteLine(Loc.F("Module.Province.Msg.ValueOutOfRange", value));
                return;
            }

            ModifierResult result = default;
            RecordProvinceCliChange($"洪水填充 ({col},{row})",
                () => result = value is int fill ? province.FloodFill(col, row, (ushort)fill) : province.FloodFill(col, row));
            _output.WriteLine(result.Message);
        }));

        // province set：精确写单格省区值（与 Lua 的 map.SetProvince 同一实现）
        var setValueCmd = new Command("set", "设置指定格子的省区值(0-65535，65535=无省区)");
        var svColArg = new Argument<int>("col") { Description = "列" };
        var svRowArg = new Argument<int>("row") { Description = "行" };
        var svValueArg = new Argument<int>("value") { Description = "省区值(0-65535；65535=无省区)" };
        setValueCmd.Arguments.Add(svColArg);
        setValueCmd.Arguments.Add(svRowArg);
        setValueCmd.Arguments.Add(svValueArg);
        setValueCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(svColArg);
            int row = parseResult.GetValue(svRowArg);
            ScriptOperationResult result = default;
            RecordProvinceCliChange($"设置省区 ({col},{row})",
                () => result = ObjectApi(mapData).SetProvince(col, row, parseResult.GetValue(svValueArg)));
            PrintOp(result);
        }));

        cmd.Subcommands.Add(floodFillCmd);
        cmd.Subcommands.Add(setValueCmd);

        return cmd;
    }

    /// <summary>整图省区操作（clear / generate / expand / process）：执行、记一步撤销并输出结果。</summary>
    private Command ProvinceMapCommand(string name, string description, Func<ProvinceModifier, ModifierResult> operation)
    {
        var command = new Command(name, description);
        command.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var province = mgr.GetModifier<ProvinceModifier>()!;
            ModifierResult result = default;
            RecordProvinceCliChange(description, () => result = operation(province));
            _output.WriteLine(result.Message);
        }));
        return command;
    }

    /// <summary>
    /// 省区变更的撤销入口：非脚本模式下记一步撤销；脚本批量执行（<c>run xxx.zme</c>）时直接执行，
    /// 理由同 <see cref="RecordBelongChange"/>（每条命令一份全图快照会撑爆撤销栈）。
    /// </summary>
    private void RecordProvinceCliChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordMultiCellProvinceChange(description, applyChange);
    }
}
