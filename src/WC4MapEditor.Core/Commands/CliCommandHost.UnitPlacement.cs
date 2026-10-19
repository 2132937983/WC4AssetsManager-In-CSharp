using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 unitplace 命令组：单位位置数据的只读查看（对应 VB 解析场景的"单位位置数据"页，VB 也从不编辑它）。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildUnitPlacementCommand()
    {
        var cmd = new Command("unitplace", "单位位置数据（只读）：文件头 PlacementA / PlacementB 两组，每条 8 字节（坐标、方向、序号、未知、运输船）");

        var listCmd = new Command("list", "列出单位位置数据（索引从 0 起；坐标 -1 为空位）");
        listCmd.SetAction(_ => WithModifiers((mapData, _) => PrintUnitPlacements(mapData), readOnly: true));

        cmd.Subcommands.Add(listCmd);
        return cmd;
    }

    private void PrintUnitPlacements(MapData mapData)
    {
        var places = mapData.UnitPlaces;
        int groupA = mapData.Header.PlacementA;
        _output.WriteLine(Loc.F("Cli.UnitPlace.Total", places.Count, groupA, mapData.Header.PlacementB));

        int width = mapData.MapWidth;
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            string cell = p.Coordinate >= 0 && width > 0
                ? Loc.F("Cli.UnitPlace.Cell", p.Coordinate % width, p.Coordinate / width)
                : Loc.T("Cli.UnitPlace.Empty", "空位");
            _output.WriteLine(Loc.F("Cli.UnitPlace.Row", i, i < groupA ? "A" : "B", p.Coordinate, cell,
                p.Direction, p.Sequence, p.Unknown, p.TransportShip));
        }
    }
}
