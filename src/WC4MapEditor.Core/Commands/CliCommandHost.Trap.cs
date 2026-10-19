using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Trap 命令组：全部经 TrapModifier（与 GUI 单位部署模式的陷阱按键同一实现）。
public sealed partial class CliCommandHost
{
    private Command BuildTrapCommand()
    {
        var cmd = new Command("trap", "陷阱修改操作");

        var listCmd = new Command("list", "列出所有陷阱");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var traps = mapData.Traps;
                _output.WriteLine($"陷阱总数: {traps.Count}");
                if (traps.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"坐标",-10} {Loc.T("Cli.Trap.ColActionId", "行动ID"),-8} {Loc.T("Cli.Army.ColBelong", "归属"),-6} {"组织",-6} {"血量",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < traps.Count; i++)
                    {
                        var trap = traps[i];
                        _output.WriteLine($"{i,-6} {FormatCell(mapData, trap.CellIndex)}    {trap.LegionId,-8} {BelongTextAt(mapData, trap.CellIndex),-6} {trap.Organization,-6} {trap.Health,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有陷阱");
        removeAllCmd.SetAction(_ => RunTrapCommand("删除所有陷阱", trap => trap.DeleteAllTraps()));

        var placeCmd = new Command("place", "在空格放一个 1 级陷阱（所属军团按本格归属推断，建筑格拒绝）");
        var plCol = new Argument<int>("col") { Description = "列" };
        var plRow = new Argument<int>("row") { Description = "行" };
        placeCmd.Arguments.Add(plCol);
        placeCmd.Arguments.Add(plRow);
        placeCmd.SetAction(parseResult =>
        {
            int col = parseResult.GetValue(plCol), row = parseResult.GetValue(plRow);
            RunTrapCommand($"放置陷阱 ({col},{row})", trap => trap.CreateTrapAt(col, row));
        });

        var removeCmd = new Command("remove", "删除指定坐标的陷阱");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            int col = parseResult.GetValue(rmCol), row = parseResult.GetValue(rmRow);
            RunTrapCommand($"删除陷阱 ({col},{row})", trap => trap.Remove(col, row));
        });

        var cycleCmd = new Command("cycle-level", "陷阱等级 1→2→3→4→1 循环");
        var cyCol = new Argument<int>("col") { Description = "列" };
        var cyRow = new Argument<int>("row") { Description = "行" };
        cycleCmd.Arguments.Add(cyCol);
        cycleCmd.Arguments.Add(cyRow);
        cycleCmd.SetAction(parseResult =>
        {
            int col = parseResult.GetValue(cyCol), row = parseResult.GetValue(cyRow);
            RunTrapCommand($"切换陷阱等级 ({col},{row})", trap => trap.CycleTrapLevel(col, row));
        });

        var generateCmd = new Command("generate", "按归属与概率批量生成陷阱（跳过已有陷阱 / 建筑 / 单位的格子）");
        var genBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var genProb = new Argument<int>("probability") { Description = "概率(0-100)" };
        var genSeaOpt = new Option<bool>("--sea") { Description = "也在海洋格上生成（水雷）" };
        generateCmd.Arguments.Add(genBelong);
        generateCmd.Arguments.Add(genProb);
        generateCmd.Options.Add(genSeaOpt);
        generateCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(genBelong);
            int prob = parseResult.GetValue(genProb);
            bool sea = parseResult.GetValue(genSeaOpt);
            RunTrapCommand("批量生成陷阱", trap => trap.GenerateTraps(belong, prob, sea));
        });

        var byProvinceCmd = new Command("generate-by-province", "每个省份按概率在一个陆地格上生成陷阱");
        var bpProb = new Argument<int>("probability") { Description = "概率(0-100)" };
        byProvinceCmd.Arguments.Add(bpProb);
        byProvinceCmd.SetAction(parseResult =>
        {
            int prob = parseResult.GetValue(bpProb);
            RunTrapCommand("按省份生成陷阱", trap => trap.GenerateTrapsByProvince(prob));
        });

        var levelsCmd = new Command("random-levels", "随机化陷阱等级（每个陷阱按概率重抽 1-4 级）");
        var rlLegion = new Argument<int>("legion") { Description = "所属军团 ActionId(-1为所有陷阱)" };
        var rlProb = new Argument<int>("probability") { Description = "重抽概率(1-100)" };
        levelsCmd.Arguments.Add(rlLegion);
        levelsCmd.Arguments.Add(rlProb);
        levelsCmd.SetAction(parseResult =>
        {
            int legion = parseResult.GetValue(rlLegion);
            int prob = parseResult.GetValue(rlProb);
            RunTrapCommand("随机化陷阱等级", trap => trap.RandomizeTrapLevels(legion, prob));
        });

        var findCmd = new Command("find", "列出指定等级的陷阱坐标");
        var fdLevel = new Argument<int>("level") { Description = "陷阱等级（编制）" };
        findCmd.Arguments.Add(fdLevel);
        findCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int level = parseResult.GetValue(fdLevel);
                var found = BoundModifier<TrapModifier>(mapData, mgr).FindTrapsByLevel(level);
                _output.WriteLine($"等级 {level} 的陷阱: {found.Count} 个");
                foreach (var c in found)
                    _output.WriteLine($"  ({c.Col},{c.Row})");
            }, readOnly: true);
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(placeCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(cycleCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(byProvinceCmd);
        cmd.Subcommands.Add(levelsCmd);
        cmd.Subcommands.Add(findCmd);

        return cmd;
    }

    /// <summary>陷阱修改命令：可撤销（非脚本批量模式下），输出模块消息。</summary>
    private void RunTrapCommand(string undoText, Func<TrapModifier, ModifierResult> op)
    {
        WithModifiers((mapData, mgr) =>
        {
            var trap = BoundModifier<TrapModifier>(mapData, mgr);
            ModifierResult result = default;
            RecordEntityChange(undoText, () => result = op(trap));
            _output.WriteLine(result.Message);
        });
    }
}
