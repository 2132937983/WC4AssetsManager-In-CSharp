using System.CommandLine;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 air 命令组：空袭（挂在机场上）与空中支援（无坐标）两张列表的查看、增删与批量生成。
// 与空袭编辑模式共用 AirForceModifier；非脚本模式下每条修改命令记一次撤销。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildAirForceCommand()
    {
        var cmd = new Command("air", "空袭 / 空中支援操作");

        // ------------------------------------------------------------------ 查看

        var typesCmd = new Command("types", "列出兵种与弹药名录（setting.txt [空袭修改]，缺省时为内置默认表）");
        typesCmd.SetAction(parseResult =>
        {
            _output.WriteLine("兵种:");
            foreach (var t in AirForceCatalog.Types) _output.WriteLine($"  {t.Value,-5} {t.Name}");
            _output.WriteLine("弹药:");
            foreach (var t in AirForceCatalog.Ammos) _output.WriteLine($"  {t.Value,-5} {t.Name}");
        });

        var listCmd = new Command("list", "列出所有空袭");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                var list = mapData.AirForces;
                _output.WriteLine($"空袭总数: {list.Count}");
                if (list.Count == 0) return;
                _output.WriteLine();
                _output.WriteLine($"{"序号",-6} {"坐标",-10} {"兵种",-16} {"弹药",-14} {"军团",-6} {"回合",-6}");
                _output.WriteLine(new string('-', 64));
                for (int i = 0; i < list.Count; i++)
                {
                    var af = list[i];
                    string cell = $"({af.Coordinate % mapData.MapWidth},{af.Coordinate / mapData.MapWidth})";
                    _output.WriteLine($"{i,-6} {cell,-10} {$"{af.UnitType} {af.GetUnitTypeName()}",-16} {$"{af.AmmoType} {af.GetAmmoTypeName()}",-14} {af.OwnerLegion,-6} {af.TriggerRound,-6}");
                }
            }, readOnly: true);
        });

        var supportListCmd = new Command("support-list", "列出所有空中支援");
        supportListCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                var list = mapData.AirSupports;
                _output.WriteLine($"空中支援总数: {list.Count}");
                if (list.Count == 0) return;
                _output.WriteLine();
                _output.WriteLine($"{"序号",-6} {"兵种",-16} {"弹药",-14} {"军团",-6} {"回合",-6}");
                _output.WriteLine(new string('-', 54));
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    _output.WriteLine($"{i,-6} {$"{s.AirForceSequence} {s.GetAirForceTypeName()}",-16} {$"{s.AmmoType} {s.GetAmmoTypeName()}",-14} {s.OwnerLegion,-6} {s.TriggerRound,-6}");
                }
            }, readOnly: true);
        });

        // ------------------------------------------------------------------ 空袭

        var addCol = new Argument<int>("col") { Description = "机场所在列" };
        var addRow = new Argument<int>("row") { Description = "机场所在行" };
        var addType = new Option<int>("--type", "-t") { Description = "兵种（见 air types）", DefaultValueFactory = _ => AirForceCatalog.Bomber };
        var addAmmo = new Option<int>("--ammo", "-a") { Description = "弹药（见 air types）", DefaultValueFactory = _ => AirForceCatalog.DefaultAmmo };
        var addLegion = new Option<int>("--legion", "-l") { Description = "所属军团 ActionId（默认取该格归属）", DefaultValueFactory = _ => -1 };
        var addRound = new Option<int>("--round", "-r") { Description = "触发回合", DefaultValueFactory = _ => 1 };
        var addCmd = new Command("add", "在指定格子新增一条空袭");
        addCmd.Arguments.Add(addCol);
        addCmd.Arguments.Add(addRow);
        addCmd.Options.Add(addType);
        addCmd.Options.Add(addAmmo);
        addCmd.Options.Add(addLegion);
        addCmd.Options.Add(addRound);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                int col = parseResult.GetValue(addCol), row = parseResult.GetValue(addRow);
                if (col < 0 || row < 0 || col >= mapData.MapWidth || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"失败：坐标 ({col},{row}) 超出地图范围");
                    return;
                }

                var module = AirModule(mapData);
                int legion = parseResult.GetValue(addLegion);
                var airForce = new AirForce
                {
                    Coordinate = row * mapData.MapWidth + col,
                    UnitType = parseResult.GetValue(addType),
                    AmmoType = parseResult.GetValue(addAmmo),
                    OwnerLegion = legion >= 0 ? legion : module.DefaultLegionAt(col, row),
                    TriggerRound = parseResult.GetValue(addRound)
                };
                PrintAirResult(RecordAirList($"新增空袭 ({col},{row})", mapData.AirForces, () => module.AddAirForce(airForce)));
            });
        });

        var removeIndex = new Argument<int>("index") { Description = "空袭序号（见 air list）" };
        var removeCmd = new Command("remove", "按序号删除一条空袭");
        removeCmd.Arguments.Add(removeIndex);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                int index = parseResult.GetValue(removeIndex);
                PrintAirResult(RecordAirList($"删除空袭 #{index}", mapData.AirForces, () => AirModule(mapData).DeleteAirForce(index)));
            });
        });

        var removeAtCol = new Argument<int>("col") { Description = "列" };
        var removeAtRow = new Argument<int>("row") { Description = "行" };
        var removeAtCmd = new Command("remove-at", "删除指定格子上的全部空袭");
        removeAtCmd.Arguments.Add(removeAtCol);
        removeAtCmd.Arguments.Add(removeAtRow);
        removeAtCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                var cell = new HexCoord(parseResult.GetValue(removeAtCol), parseResult.GetValue(removeAtRow));
                PrintAirResult(RecordAirList($"删除 ({cell.Col},{cell.Row}) 的空袭", mapData.AirForces,
                    () => AirModule(mapData).DeleteAirForcesAt(new[] { cell })));
            });
        });

        var clearCmd = new Command("clear", "删除所有空袭");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
                PrintAirResult(RecordAirList("清空所有空袭", mapData.AirForces, () => AirModule(mapData).DeleteAllAirForces())));
        });

        var genCol = new Argument<int>("col") { Description = "机场所在列" };
        var genRow = new Argument<int>("row") { Description = "机场所在行" };
        var genLegion = new Option<int>("--legion", "-l") { Description = "所属军团 ActionId（-1 = 每个军团）", DefaultValueFactory = _ => AirForceModifier.AllLegions };
        var genCount = new Option<int>("--count", "-c") { Description = $"每个军团生成数量（1-{AirForceModifier.MaxCountPerLegion}）", DefaultValueFactory = _ => 1 };
        var genMin = new Option<int>("--min") { Description = "回合最小值", DefaultValueFactory = _ => 1 };
        var genMax = new Option<int>("--max") { Description = "回合最大值", DefaultValueFactory = _ => 50 };
        var generateCmd = new Command("generate", "在指定机场随机生成空袭（兵种 / 弹药随机，回合在范围内随机）");
        generateCmd.Arguments.Add(genCol);
        generateCmd.Arguments.Add(genRow);
        generateCmd.Options.Add(genLegion);
        generateCmd.Options.Add(genCount);
        generateCmd.Options.Add(genMin);
        generateCmd.Options.Add(genMax);
        generateCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                var cell = new HexCoord(parseResult.GetValue(genCol), parseResult.GetValue(genRow));
                PrintAirResult(RecordAirList($"在机场 ({cell.Col},{cell.Row}) 生成空袭", mapData.AirForces,
                    () => AirModule(mapData).GenerateAirForcesAtAirports(new[] { cell }, parseResult.GetValue(genLegion),
                        parseResult.GetValue(genCount), parseResult.GetValue(genMin), parseResult.GetValue(genMax))));
            });
        });

        var fillCmd = new Command("fill-airports", "给每个还没有空袭的机场补一条空袭（战斗机、第 1 回合、军团 = 机场归属）");
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
                PrintAirResult(RecordAirList("给机场补空袭", mapData.AirForces, () => AirModule(mapData).GenerateAirForceFromBuildings())));
        });

        // ------------------------------------------------------------------ 空中支援

        var supAddType = new Option<int>("--type", "-t") { Description = "兵种（见 air types）", DefaultValueFactory = _ => AirForceCatalog.Bomber };
        var supAddAmmo = new Option<int>("--ammo", "-a") { Description = "弹药（见 air types）", DefaultValueFactory = _ => AirForceCatalog.DefaultAmmo };
        var supAddLegion = new Option<int>("--legion", "-l") { Description = "所属军团 ActionId（默认 0；0 不是现有军团时取第一个军团）", DefaultValueFactory = _ => -1 };
        var supAddRound = new Option<int>("--round", "-r") { Description = "触发回合", DefaultValueFactory = _ => 1 };
        var supportAddCmd = new Command("support-add", "新增一条空中支援");
        supportAddCmd.Options.Add(supAddType);
        supportAddCmd.Options.Add(supAddAmmo);
        supportAddCmd.Options.Add(supAddLegion);
        supportAddCmd.Options.Add(supAddRound);
        supportAddCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                var module = AirModule(mapData);
                int legion = parseResult.GetValue(supAddLegion);
                var support = new AirSupport
                {
                    AirForceSequence = parseResult.GetValue(supAddType),
                    AmmoType = parseResult.GetValue(supAddAmmo),
                    OwnerLegion = legion >= 0 ? legion : module.DefaultLegion(),
                    TriggerRound = parseResult.GetValue(supAddRound)
                };
                PrintAirResult(RecordAirList("新增空中支援", mapData.AirSupports, () => module.AddAirSupport(support)));
            });
        });

        var supRemoveIndex = new Argument<int>("index") { Description = "空中支援序号（见 air support-list）" };
        var supportRemoveCmd = new Command("support-remove", "按序号删除一条空中支援");
        supportRemoveCmd.Arguments.Add(supRemoveIndex);
        supportRemoveCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
            {
                int index = parseResult.GetValue(supRemoveIndex);
                PrintAirResult(RecordAirList($"删除空中支援 #{index}", mapData.AirSupports, () => AirModule(mapData).DeleteAirSupport(index)));
            });
        });

        var supportClearCmd = new Command("support-clear", "删除所有空中支援");
        supportClearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
                PrintAirResult(RecordAirList("清空所有空中支援", mapData.AirSupports, () => AirModule(mapData).DeleteAllAirSupports())));
        });

        var supLegion = new Option<int>("--legion", "-l") { Description = "所属军团 ActionId（-1 = 每个军团）", DefaultValueFactory = _ => AirForceModifier.AllLegions };
        var supCount = new Option<int>("--count", "-c") { Description = $"每个军团生成数量（1-{AirForceModifier.MaxCountPerLegion}）", DefaultValueFactory = _ => 1 };
        var supMin = new Option<int>("--min") { Description = "回合最小值", DefaultValueFactory = _ => 1 };
        var supMax = new Option<int>("--max") { Description = "回合最大值", DefaultValueFactory = _ => 50 };
        var supportRandomCmd = new Command("support-random", "按军团随机生成空中支援（兵种 / 弹药随机，回合在范围内随机）");
        supportRandomCmd.Options.Add(supLegion);
        supportRandomCmd.Options.Add(supCount);
        supportRandomCmd.Options.Add(supMin);
        supportRandomCmd.Options.Add(supMax);
        supportRandomCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, _) =>
                PrintAirResult(RecordAirList("随机生成空中支援", mapData.AirSupports,
                    () => AirModule(mapData).GenerateRandomAirSupportByLegion(parseResult.GetValue(supLegion),
                        parseResult.GetValue(supCount), parseResult.GetValue(supMin), parseResult.GetValue(supMax)))));
        });

        cmd.Subcommands.Add(typesCmd);
        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAtCmd);
        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(supportListCmd);
        cmd.Subcommands.Add(supportAddCmd);
        cmd.Subcommands.Add(supportRemoveCmd);
        cmd.Subcommands.Add(supportClearCmd);
        cmd.Subcommands.Add(supportRandomCmd);
        return cmd;
    }

    /// <summary>
    /// 取空袭模块：优先复用 EditModeManager 里为这份地图初始化过的实例（保留复制 / 粘贴状态），否则临时建一个。
    /// </summary>
    private static AirForceModifier AirModule(MapData mapData)
    {
        var module = EditModeManager.Instance.GetModifier<AirForceModifier>();
        if (module != null && module.IsInitializedFor(mapData)) return module;
        module = new AirForceModifier();
        module.Initialize(mapData);
        return module;
    }

    /// <summary>修改一张空袭 / 空中支援列表并记录撤销（脚本批量执行时不记，与其它命令组一致）。</summary>
    private ModifierResult RecordAirList<T>(string description, IList<T> list, Func<ModifierResult> change)
    {
        var result = default(ModifierResult);
        if (_batchMode) return change();
        EditModeManager.Instance.RecordCollectionChange(description, list, () => result = change());
        return result;
    }

    private void PrintAirResult(ModifierResult result)
        => _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");
}
