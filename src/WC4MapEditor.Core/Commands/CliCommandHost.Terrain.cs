using System.CommandLine;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Terrain 命令组。改地形的命令在非脚本模式下记录撤销（GUI 控制台里可以 Ctrl+Z）；
// 尺寸调整（scale-map / resize-map）在征服 / 世界底图地图上由地形模块拒绝执行。
public sealed partial class CliCommandHost
{
    private Command BuildTerrainCommand()
    {
        var cmd = new Command("terrain", "地形修改操作");

        var greeningCmd = new Command("greening", "绿化平地");
        var greeningProbOpt = new Option<int>("--probability", "-p") { Description = "绿化概率(0-100)", DefaultValueFactory = _ => 50 };
        greeningCmd.Options.Add(greeningProbOpt);
        greeningCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(greeningProbOpt), 0, 100);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"绿化平地 (概率={prob}%)", () => terrain.ApplyGreening(prob)));
            });
        });

        var randomFlatCmd = new Command("random-flat", "随机平地变体");
        var randomFlatProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        randomFlatCmd.Options.Add(randomFlatProbOpt);
        randomFlatCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(randomFlatProbOpt), 0, 100);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"随机平地变体 (概率={prob}%)", () => terrain.RandomizeFlatTerrain(prob)));
            });
        });

        var randomVariantCmd = new Command("random-variant", "随机当前层变体");
        var randomVariantProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        var randomVariantLayerOpt = new Option<int>("--layer", "-l") { Description = "编辑层(1-3)", DefaultValueFactory = _ => 1 };
        randomVariantCmd.Options.Add(randomVariantProbOpt);
        randomVariantCmd.Options.Add(randomVariantLayerOpt);
        randomVariantCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(randomVariantLayerOpt), 1, 3);
                int prob = Math.Clamp(parseResult.GetValue(randomVariantProbOpt), 0, 100);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"随机当前层变体 (概率={prob}%)", () => terrain.RandomizeVariant(prob)));
            });
        });

        var createCoastCmd = new Command("create-coast", "创建海岸线");
        createCoastCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                PrintTerrainResult(RecordTerrainCliChange(mgr, "创建海岸线", () => terrain.CreateCoast()));
            });
        });

        var processOceanCmd = new Command("process-ocean-layer2", "处理海洋第二层");
        processOceanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                PrintTerrainResult(RecordTerrainCliChange(mgr, "处理海洋第二层", () => terrain.ProcessOceanSecondLayer()));
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充地形（不给类型时：海洋↔平地互转，或用已复制的地形）");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        var fillTypeArg = new Argument<int?>("type") { Description = "填充成的地形类型（可选）", Arity = ArgumentArity.ZeroOrOne };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.Arguments.Add(fillTypeArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(fillColArg);
                int row = parseResult.GetValue(fillRowArg);
                byte? type = parseResult.GetValue(fillTypeArg) is int t ? (byte)Math.Clamp(t, 0, 255) : null;
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"洪水填充 ({col},{row})", () => terrain.FloodFill(col, row, type)));
            });
        });

        cmd.Subcommands.Add(greeningCmd);
        cmd.Subcommands.Add(randomFlatCmd);
        cmd.Subcommands.Add(randomVariantCmd);
        cmd.Subcommands.Add(createCoastCmd);
        cmd.Subcommands.Add(processOceanCmd);
        cmd.Subcommands.Add(floodFillCmd);

        var changeTypeCmd = new Command("change-type", "切换地形类型");
        var ctCol = new Argument<int>("col") { Description = "列" };
        var ctRow = new Argument<int>("row") { Description = "行" };
        var ctDeltaOpt = new Option<int>("--delta", "-d") { Description = "变化量(正数递增/负数递减)", DefaultValueFactory = _ => 1 };
        changeTypeCmd.Arguments.Add(ctCol);
        changeTypeCmd.Arguments.Add(ctRow);
        changeTypeCmd.Options.Add(ctDeltaOpt);
        changeTypeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(ctCol);
                int row = parseResult.GetValue(ctRow);
                int delta = parseResult.GetValue(ctDeltaOpt);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"切换地形类型 ({col},{row})", () => terrain.ChangeTerrainType(col, row, delta)));
            });
        });

        var changeDecoCmd = new Command("change-decoration", "切换地形变体");
        var cdCol = new Argument<int>("col") { Description = "列" };
        var cdRow = new Argument<int>("row") { Description = "行" };
        var cdDeltaOpt = new Option<int>("--delta", "-d") { Description = "变化量", DefaultValueFactory = _ => 1 };
        changeDecoCmd.Arguments.Add(cdCol);
        changeDecoCmd.Arguments.Add(cdRow);
        changeDecoCmd.Options.Add(cdDeltaOpt);
        changeDecoCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(cdCol);
                int row = parseResult.GetValue(cdRow);
                int delta = parseResult.GetValue(cdDeltaOpt);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"切换变体 ({col},{row})", () => terrain.ChangeDecoration(col, row, delta)));
            });
        });

        var setRiverCmd = new Command("set-river", "设置河流值");
        var srCol = new Argument<int>("col") { Description = "列" };
        var srRow = new Argument<int>("row") { Description = "行" };
        var srValOpt = new Option<int>("--value", "-v") { Description = "河流值(0-63)", DefaultValueFactory = _ => 1 };
        setRiverCmd.Arguments.Add(srCol);
        setRiverCmd.Arguments.Add(srRow);
        setRiverCmd.Options.Add(srValOpt);
        setRiverCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(srCol);
                int row = parseResult.GetValue(srRow);
                byte val = (byte)Math.Clamp(parseResult.GetValue(srValOpt), 0, 63);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"设置河流 ({col},{row})", () => terrain.SetRiverValue(col, row, val)));
            });
        });

        var copyCmd = new Command("copy", "复制地形");
        var cpCol = new Argument<int>("col") { Description = "列" };
        var cpRow = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(cpCol);
        copyCmd.Arguments.Add(cpRow);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                PrintTerrainResult(terrain.CopyTerrain(parseResult.GetValue(cpCol), parseResult.GetValue(cpRow)));
            });
        });

        var pasteCmd = new Command("paste", "粘贴地形");
        var psCol = new Argument<int>("col") { Description = "列" };
        var psRow = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(psCol);
        pasteCmd.Arguments.Add(psRow);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(psCol);
                int row = parseResult.GetValue(psRow);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"粘贴地形 ({col},{row})", () => terrain.PasteTerrain(col, row)));
            });
        });

        var removeCmd = new Command("remove", "重置编辑层地形");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(rmCol);
                int row = parseResult.GetValue(rmRow);
                PrintTerrainResult(RecordTerrainCliChange(mgr, $"重置编辑层地形 ({col},{row})", () => terrain.Remove(col, row)));
            });
        });

        var exportHdCmd = new Command("export-hd", "导出HD文件");
        exportHdCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                PrintTerrainResult(terrain.ExportHdFile());
            });
        });

        var connectBldCmd = new Command("connect-buildings", "连接建筑(A*寻路，最小生成树路网)");
        connectBldCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                PrintTerrainResult(RecordTerrainCliChange(mgr, "连接建筑", terrain.ConnectBuildings));
            });
        });

        var setLayerCmd = new Command("set-edit-layer", "设置编辑层");
        var slLayer = new Argument<int>("layer") { Description = "编辑层(1-3)" };
        setLayerCmd.Arguments.Add(slLayer);
        setLayerCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(slLayer), 1, 3);
                _output.WriteLine($"已设置编辑层={terrain.EditLayer}");
            });
        });

        var scaleMapCmd = new Command("scale-map", "按比例缩放地图（征服 / 世界底图地图不可用）");
        var smScale = new Argument<double>("scale") { Description = "缩放比例(0.1-10.0)" };
        scaleMapCmd.Arguments.Add(smScale);
        scaleMapCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                double scale = parseResult.GetValue(smScale);
                PrintTerrainResult(RecordResizeCliChange(mgr, mapData, $"缩放地图 (比例={scale:F2})", () => terrain.ScaleMap(scale)));
            });
        });

        var resizeMapCmd = new Command("resize-map", "调整地图大小（征服 / 世界底图地图不可用）");
        var rmDir = new Argument<string>("direction") { Description = "方向(up/down/left/right)" };
        var rmAmount = new Argument<int>("amount") { Description = "程度(正数扩展/负数收缩)" };
        var rmOceanOpt = new Option<bool>("--ocean", "-o") { Description = "扩展填充海洋" };
        resizeMapCmd.Arguments.Add(rmDir);
        resizeMapCmd.Arguments.Add(rmAmount);
        resizeMapCmd.Options.Add(rmOceanOpt);
        resizeMapCmd.SetAction(parseResult =>
        {
            string? dirText = parseResult.GetValue(rmDir);
            if (!TerrainModifier.TryParseDirection(dirText, out var direction))
            {
                _output.WriteLine($"无效的方向: {dirText}（可用 up/down/left/right）");
                return;
            }

            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int amount = parseResult.GetValue(rmAmount);
                bool ocean = parseResult.GetValue(rmOceanOpt);
                PrintTerrainResult(RecordResizeCliChange(mgr, mapData, $"调整地图大小 ({dirText}, {amount})",
                    () => terrain.ResizeMap(direction, amount, ocean)));
            });
        });

        cmd.Subcommands.Add(changeTypeCmd);
        cmd.Subcommands.Add(changeDecoCmd);
        cmd.Subcommands.Add(setRiverCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(exportHdCmd);
        cmd.Subcommands.Add(connectBldCmd);
        cmd.Subcommands.Add(setLayerCmd);
        cmd.Subcommands.Add(scaleMapCmd);
        cmd.Subcommands.Add(resizeMapCmd);

        var getCmd = new Command("get", "查询指定格子的地形数据");
        var getColArg = new Argument<int>("col") { Description = "列坐标" };
        var getRowArg = new Argument<int>("row") { Description = "行坐标" };
        getCmd.Arguments.Add(getColArg);
        getCmd.Arguments.Add(getRowArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(getColArg);
                int row = parseResult.GetValue(getRowArg);
                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }
                ref var t = ref mapData.GetTerrainRef(col, row);
                _output.WriteLine($"({col},{row}) 地形数据:");
                _output.WriteLine($"  第一层: 类型={t.TileType1} 装饰={t.DecorationType1}");
                _output.WriteLine($"  第二层: 类型={t.TileType2} 装饰={t.DecorationType2}");
                _output.WriteLine($"  第三层: 类型={t.TileType3} 装饰={t.DecorationType3}");
                _output.WriteLine($"  河流值={t.RiverValue}");
            }, readOnly: true);
        });

        var statsCmd = new Command("stats", "统计全图地形类型分布");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var typeCount = new Dictionary<byte, int>();
                for (int i = 0; i < mapData.MapWidth * mapData.MapHeight; i++)
                {
                    byte t = mapData.GetTerrainRef(i).TileType1;
                    if (!typeCount.ContainsKey(t)) typeCount[t] = 0;
                    typeCount[t]++;
                }
                _output.WriteLine($"地形类型分布 (共 {mapData.MapWidth * mapData.MapHeight} 格):");
                _output.WriteLine();
                foreach (var kvp in typeCount.OrderBy(k => k.Key))
                {
                    double pct = kvp.Value * 100.0 / (mapData.MapWidth * mapData.MapHeight);
                    _output.WriteLine($"  类型 {kvp.Key,3}: {kvp.Value,6} 格 ({pct,5:F1}%)");
                }
            }, readOnly: true);
        });

        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(statsCmd);

        return cmd;
    }

    /// <summary>
    /// 地形批量变更的撤销入口：非脚本模式下按地形快照记录撤销。
    /// 脚本批量执行（<c>run xxx.zme</c>）时直接执行、不记撤销：一份脚本动辄几百条命令，每条都存一份全图快照会撑爆撤销栈。
    /// </summary>
    private ModifierResult RecordTerrainCliChange(EditModeManager mgr, string description, Func<ModifierResult> operation)
    {
        if (_batchMode) return operation();

        ModifierResult result = default;
        mgr.RecordMultiCellChange(description, () => result = operation());
        return result;
    }

    /// <summary>
    /// 尺寸调整（缩放 / 单边扩缩）的撤销入口，规则同 <see cref="RecordTerrainCliChange"/>。
    /// 命令宿主拿不到撤销栈本身，只能经 <see cref="EditModeManager.RecordEntityChange"/> 记录：它会先执行一次"重做"
    /// （把地图恢复成刚调整完的同一状态，结果不变），再把命令压栈；没有撤销栈时（纯命令行）只是这一次重做。
    /// </summary>
    private ModifierResult RecordResizeCliChange(EditModeManager mgr, MapData mapData, string description, Func<ModifierResult> resize)
    {
        Action<IUndoableCommand>? record = _batchMode ? null : command => mgr.RecordEntityChange(command.Description, command.Execute, command.Undo);
        return TerrainModifier.RecordResize(mapData, record, description, resize);
    }

    /// <summary>输出地形模块的结果：成功给消息，失败也必须给出原因。</summary>
    private void PrintTerrainResult(ModifierResult result)
        => _output.WriteLine(result.Success ? result.Message ?? "" : $"失败：{result.Message}");
}
