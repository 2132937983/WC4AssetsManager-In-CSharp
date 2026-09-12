using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

public sealed class CliCommandHost
{
    private static readonly object _lock = new();
    private static CliCommandHost? _instance;

    public static CliCommandHost Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new CliCommandHost();
            }
            return _instance;
        }
    }

    private readonly RootCommand _rootCommand;
    private TextWriter _output = Console.Out;

    private CliCommandHost()
    {
        _rootCommand = BuildRootCommand();
    }

    public void SetOutput(TextWriter output) => _output = output;

    public int Execute(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);
        var parseResult = _rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    public (List<string> Errors, string CommandName, string CommandDescription) GetParseErrors(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);
        var parseResult = _rootCommand.Parse(args);
        var errors = new List<string>();
        foreach (var error in parseResult.Errors)
            errors.Add(error.Message);
        var cmd = parseResult.CommandResult.Command;
        return (errors, cmd?.Name ?? "", cmd?.Description ?? "");
    }

    public string GetHelp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CLI修改命令 ===");
        foreach (var sub in _rootCommand.Subcommands)
        {
            sb.AppendLine($"  {sub.Name,-12} - {sub.Description}");
            foreach (var sub2 in sub.Subcommands)
                sb.AppendLine($"    {sub.Name} {sub2.Name,-16} - {sub2.Description}");
        }
        sb.AppendLine();
        sb.AppendLine("示例: building add 0 12 -t 11");
        sb.AppendLine("      building add 87 25 -t 15 -n 北京");
        sb.AppendLine("      belong set 0 12 5");
        sb.AppendLine("      belong copy 0 12");
        sb.AppendLine("      belong paste 5 8");
        sb.AppendLine("      belong remove 3 4");
        sb.AppendLine("      belong clean-orphan");
        sb.AppendLine("      belong randomize");
        sb.AppendLine("      belong batch-set 3");
        sb.AppendLine("      belong remove-border-entities");
        sb.AppendLine("      belong remove-border-armies");
        sb.AppendLine("      terrain greening -p 50");
        return sb.ToString();
    }

    public Command GetRootCommand() => _rootCommand;

    private static string[] CommandLineToArgs(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in commandLine)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private RootCommand BuildRootCommand()
    {
        var root = new RootCommand("WC4 Map Editor");

        root.Subcommands.Add(BuildTerrainCommand());
        root.Subcommands.Add(BuildProvinceCommand());
        root.Subcommands.Add(BuildBuildingCommand());
        root.Subcommands.Add(BuildBelongCommand());
        root.Subcommands.Add(BuildLegionCommand());
        root.Subcommands.Add(BuildArmyCommand());
        root.Subcommands.Add(BuildTrapCommand());
        root.Subcommands.Add(BuildInfoCommand());
        root.Subcommands.Add(BuildRunCommand());

        return root;
    }

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
                var result = terrain.ApplyGreening(prob);
                _output.WriteLine(result.Message ?? $"绿化完成，概率={prob}%");
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
                var result = terrain.RandomizeFlatTerrain(prob);
                _output.WriteLine(result.Message ?? $"随机平地变体完成，概率={prob}%");
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
                var result = terrain.RandomizeVariant(prob);
                _output.WriteLine(result.Message ?? $"随机变体完成，编辑层={terrain.EditLayer}，概率={prob}%");
            });
        });

        var createCoastCmd = new Command("create-coast", "创建海岸线");
        createCoastCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.CreateCoast();
                _output.WriteLine(result.Message ?? "海岸线创建完成");
            });
        });

        var processOceanCmd = new Command("process-ocean-layer2", "处理海洋第二层");
        processOceanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.ProcessOceanSecondLayer();
                _output.WriteLine(result.Message ?? "海洋第二层处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充地形");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        var fillTypeArg = new Argument<int>("type") { Description = "替换地形类型" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.Arguments.Add(fillTypeArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg), (byte)parseResult.GetValue(fillTypeArg));
                _output.WriteLine(result.Message ?? "洪水填充完成");
            });
        });

        cmd.Subcommands.Add(greeningCmd);
        cmd.Subcommands.Add(randomFlatCmd);
        cmd.Subcommands.Add(randomVariantCmd);
        cmd.Subcommands.Add(createCoastCmd);
        cmd.Subcommands.Add(processOceanCmd);
        cmd.Subcommands.Add(floodFillCmd);

        return cmd;
    }

    private Command BuildProvinceCommand()
    {
        var cmd = new Command("province", "省份修改操作");

        var clearCmd = new Command("clear", "清空所有省份");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ClearAllProvinces();
                _output.WriteLine(result.Message ?? "所有省份已清空");
            });
        });

        var generateCmd = new Command("generate", "生成孤立省会省区");
        generateCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.GenerateProvincesForIsolatedCapitals();
                _output.WriteLine(result.Message ?? "孤立省会省区生成完成");
            });
        });

        var expandCmd = new Command("expand", "扩展省区填满地图");
        expandCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ExpandAllProvincesToFillMap();
                _output.WriteLine(result.Message ?? "省区扩展完成");
            });
        });

        var processCmd = new Command("process", "处理孤立和空白省区");
        processCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ProcessIsolatedAndEmptyProvinces();
                _output.WriteLine(result.Message ?? "孤立和空白省区处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充省份");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg));
                _output.WriteLine(result.Message ?? "省份洪水填充完成");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(expandCmd);
        cmd.Subcommands.Add(processCmd);
        cmd.Subcommands.Add(floodFillCmd);

        return cmd;
    }

    private Command BuildBuildingCommand()
    {
        var cmd = new Command("building", "建筑修改操作");

        var addCmd = new Command("add", "在指定坐标添加建筑");
        var addColArg = new Argument<int>("col") { Description = "列坐标" };
        var addRowArg = new Argument<int>("row") { Description = "行坐标" };
        var addTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型(11=一级城,12=二级城,...,15=五级城)", DefaultValueFactory = _ => 11 };
        var addNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称,默认0xFFFF=无名称)", DefaultValueFactory = _ => "-1" };
        var addIndexOpt = new Option<int>("--index", "-i") { Description = "格子索引(优先于坐标)" };
        addCmd.Arguments.Add(addColArg);
        addCmd.Arguments.Add(addRowArg);
        addCmd.Options.Add(addTypeOpt);
        addCmd.Options.Add(addNameOpt);
        addCmd.Options.Add(addIndexOpt);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(addColArg);
                int row = parseResult.GetValue(addRowArg);
                int buildingType = parseResult.GetValue(addTypeOpt);
                string nameInput = parseResult.GetValue(addNameOpt) ?? "-1";
                short name = ParseBuildingName(nameInput);

                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }

                int coordIndex = row * mapData.MapWidth + col;
                var b = Building.CreateDefault(coordIndex);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (coordIndex > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {coordIndex} 超出文件格式上限 65535，保存时将被截断为 {coordIndex & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在 ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var addByIdxCmd = new Command("add-by-idx", "按格子索引添加建筑");
        var addIdxArg = new Argument<int>("index") { Description = "格子索引" };
        var addIdxTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型", DefaultValueFactory = _ => 11 };
        var addIdxNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称)", DefaultValueFactory = _ => "-1" };
        addByIdxCmd.Arguments.Add(addIdxArg);
        addByIdxCmd.Options.Add(addIdxTypeOpt);
        addByIdxCmd.Options.Add(addIdxNameOpt);
        addByIdxCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int idx = parseResult.GetValue(addIdxArg);
                int buildingType = parseResult.GetValue(addIdxTypeOpt);
                string nameInput = parseResult.GetValue(addIdxNameOpt) ?? "-1";
                short name = ParseBuildingName(nameInput);

                if (idx < 0 || idx >= mapData.MapWidth * mapData.MapHeight)
                {
                    _output.WriteLine($"索引 {idx} 超出范围 (0-{mapData.MapWidth * mapData.MapHeight - 1})");
                    return;
                }

                int col = idx % mapData.MapWidth;
                int row = idx / mapData.MapWidth;
                var b = Building.CreateDefault(idx);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (idx > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {idx} 超出文件格式上限 65535，保存时将被截断为 {idx & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在索引{idx} ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var removeCmd = new Command("remove", "删除指定坐标的建筑");
        var rmColArg = new Argument<int>("col") { Description = "列坐标" };
        var rmRowArg = new Argument<int>("row") { Description = "行坐标" };
        removeCmd.Arguments.Add(rmColArg);
        removeCmd.Arguments.Add(rmRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(rmColArg);
                int row = parseResult.GetValue(rmRowArg);
                var result = building.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已删除 ({col},{row}) 的建筑");
            });
        });

        var removeAllCmd = new Command("remove-all", "删除所有建筑");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                building.RemoveAll();
                _output.WriteLine("所有建筑已删除");
            });
        });

        var removeNonCapitalCmd = new Command("remove-non-capital", "删除非首都建筑");
        removeNonCapitalCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RemoveNonCapitalBuildings(mapData);
                _output.WriteLine($"已删除 {count} 个非首都建筑");
            });
        });

        var randomizeNamedCmd = new Command("randomize-named", "随机有名称建筑类型");
        randomizeNamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeNamedBuildingTypes();
                _output.WriteLine($"已随机 {count} 个有名称建筑");
            });
        });

        var randomizeUnnamedCmd = new Command("randomize-unnamed", "随机无名称建筑类型");
        randomizeUnnamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeUnnamedBuildingTypes();
                _output.WriteLine($"已随机 {count} 个无名称建筑");
            });
        });

        var randomizeByBelongCmd = new Command("randomize-by-belong", "按归属随机建筑");
        var belongIdArg = new Argument<int>("belongId") { Description = "归属ID" };
        randomizeByBelongCmd.Arguments.Add(belongIdArg);
        randomizeByBelongCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int belongId = parseResult.GetValue(belongIdArg);
                int count = building.RandomizeBuildingsByBelong(belongId);
                _output.WriteLine($"已按归属{belongId}随机 {count} 个建筑");
            });
        });

        var generateCapitalsCmd = new Command("generate-capitals", "为所有建筑生成首都");
        generateCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.GenerateCapitalsForAllBuildings(mapData);
                _output.WriteLine($"已生成 {count} 个首都");
            });
        });

        var randomizeOnCapitalsCmd = new Command("randomize-on-capitals", "在首都随机建筑");
        randomizeOnCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeBuildingsOnCapitals(mapData);
                _output.WriteLine($"已在首都随机 {count} 个建筑");
            });
        });

        var smartAppearanceCmd = new Command("smart-appearance", "智能设置建筑外观");
        smartAppearanceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.SmartSetBuildingAppearance(mapData);
                _output.WriteLine($"已智能设置 {count} 个建筑外观");
            });
        });

        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(addByIdxCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(removeNonCapitalCmd);
        cmd.Subcommands.Add(randomizeNamedCmd);
        cmd.Subcommands.Add(randomizeUnnamedCmd);
        cmd.Subcommands.Add(randomizeByBelongCmd);
        cmd.Subcommands.Add(generateCapitalsCmd);
        cmd.Subcommands.Add(randomizeOnCapitalsCmd);
        cmd.Subcommands.Add(smartAppearanceCmd);

        return cmd;
    }

    private Command BuildBelongCommand()
    {
        var cmd = new Command("belong", "归属修改操作");

        var clearCmd = new Command("clear", "清空所有归属");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                {
                    if (mapData.GetBelongValueByIndex(i) != 0xFF)
                    {
                        mapData.SetBelongValueByIndex(i, 0xFF);
                        count++;
                    }
                }
                _output.WriteLine($"已清空 {count} 个格子的归属");
            });
        });

        var setCmd = new Command("set", "设置指定格子归属");
        var setColArg = new Argument<int>("col") { Description = "列" };
        var setRowArg = new Argument<int>("row") { Description = "行" };
        var setBelongArg = new Argument<int>("belong") { Description = "归属值(0-255, 255=无归属)" };
        setCmd.Arguments.Add(setColArg);
        setCmd.Arguments.Add(setRowArg);
        setCmd.Arguments.Add(setBelongArg);
        setCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(setColArg);
                int row = parseResult.GetValue(setRowArg);
                int belongVal = Math.Clamp(parseResult.GetValue(setBelongArg), 0, 255);
                var result = belong.SetBelongByCountryId(col, row, belongVal);
                _output.WriteLine(result.Message ?? $"已设置归属 ({col},{row}) = {belongVal}");
            });
        });

        var fillCmd = new Command("fill-from-legion", "按军团ID填充归属到建筑格子");
        var fillLegionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        fillCmd.Arguments.Add(fillLegionIdArg);
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int legionId = parseResult.GetValue(fillLegionIdArg);
                int count = 0;
                foreach (var b in mapData.Buildings)
                {
                    int col = b.Coordinate % mapData.MapWidth;
                    int row = b.Coordinate / mapData.MapWidth;
                    if (mapData.GetBelongValue(col, row) != legionId)
                    {
                        mapData.SetBelongValue(col, row, legionId);
                        count++;
                    }
                }
                _output.WriteLine($"已将 {count} 个建筑格子归属设为军团 {legionId}");
            });
        });

        var statsCmd = new Command("stats", "归属统计");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belongCounts = new Dictionary<int, int>();
                for (int i = 0; i < mapData.Belongs.Count; i++)
                {
                    int val = mapData.GetBelongValueByIndex(i);
                    if (val != 0xFF)
                    {
                        if (!belongCounts.ContainsKey(val)) belongCounts[val] = 0;
                        belongCounts[val]++;
                    }
                }
                _output.WriteLine("归属值分布:");
                foreach (var kv in belongCounts.OrderBy(x => x.Key))
                {
                    string name = "";
                    int legionIdx = mapData.FindLegionIndex(kv.Key);
                    if (legionIdx >= 0) name = $" ({mapData.Legions[legionIdx].DisplayName})";
                    _output.WriteLine($"  归属={kv.Key}{name}: {kv.Value} 格");
                }
                int emptyCount = mapData.Belongs.Count - belongCounts.Values.Sum();
                _output.WriteLine($"  无归属(0xFF): {emptyCount} 格");
            }, readOnly: true);
        });

        var removeCmd = new Command("remove", "删除指定格子归属");
        var removeColArg = new Argument<int>("col") { Description = "列" };
        var removeRowArg = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(removeColArg);
        removeCmd.Arguments.Add(removeRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(removeColArg);
                int row = parseResult.GetValue(removeRowArg);
                var result = belong.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已删除归属 ({col},{row})");
            });
        });

        var copyCmd = new Command("copy", "复制指定格子归属值");
        var copyColArg = new Argument<int>("col") { Description = "列" };
        var copyRowArg = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(copyColArg);
        copyCmd.Arguments.Add(copyRowArg);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(copyColArg);
                int row = parseResult.GetValue(copyRowArg);
                var result = belong.CopyBelongValue(col, row);
                _output.WriteLine(result.Message ?? $"已复制归属 ({col},{row})");
            }, readOnly: true);
        });

        var pasteCmd = new Command("paste", "粘贴归属值到指定格子");
        var pasteColArg = new Argument<int>("col") { Description = "列" };
        var pasteRowArg = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(pasteColArg);
        pasteCmd.Arguments.Add(pasteRowArg);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(pasteColArg);
                int row = parseResult.GetValue(pasteRowArg);
                var result = belong.PasteBelongValue(col, row);
                _output.WriteLine(result.Message ?? $"已粘贴归属 ({col},{row})");
            });
        });

        var cleanOrphanCmd = new Command("clean-orphan", "清理孤立归属(无建筑/单位的格子)");
        cleanOrphanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = belong.CleanOrphanBelongs();
                _output.WriteLine($"已清理 {count} 个孤立归属");
            });
        });

        var randomizeCmd = new Command("randomize", "随机化所有归属");
        randomizeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = belong.RandomizeBelongs(new Random());
                _output.WriteLine($"已随机化 {count} 个格子的归属");
            });
        });

        var batchSetCmd = new Command("batch-set", "批量设置所有归属为目标ID");
        var batchTargetArg = new Argument<int>("targetId") { Description = "目标国家ID(0-255)" };
        batchSetCmd.Arguments.Add(batchTargetArg);
        batchSetCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(batchTargetArg), 0, 255);
                int count = belong.BatchSetBelongByProvince(targetId);
                _output.WriteLine($"已批量设置 {count} 个格子归属为国家ID {targetId}");
            });
        });

        var removeBorderEntitiesCmd = new Command("remove-border-entities", "删除归属边界上的单位与建筑");
        removeBorderEntitiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = belong.RemoveBorderEntities();
                _output.WriteLine($"已删除 {count} 个边界上的单位与建筑");
            });
        });

        var removeBorderArmiesCmd = new Command("remove-border-armies", "删除归属边界上的单位");
        removeBorderArmiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = belong.RemoveBorderArmies();
                _output.WriteLine($"已删除 {count} 个边界上的单位");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(statsCmd);
        cmd.Subcommands.Add(cleanOrphanCmd);
        cmd.Subcommands.Add(randomizeCmd);
        cmd.Subcommands.Add(batchSetCmd);
        cmd.Subcommands.Add(removeBorderEntitiesCmd);
        cmd.Subcommands.Add(removeBorderArmiesCmd);

        return cmd;
    }

    private Command BuildLegionCommand()
    {
        var cmd = new Command("legion", "军团修改操作");

        var listCmd = new Command("list", "列出所有军团");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legions = mapData.Legions;
                _output.WriteLine($"军团总数: {legions.Count}");
                _output.WriteLine();
                _output.WriteLine($"{"ID",-4} {"名称",-20} {"颜色",-12} {"玩家控制",-10}");
                _output.WriteLine(new string('-', 50));
                foreach (var legion in legions)
                {
                    _output.WriteLine($"{legion.CountryId,-4} {legion.DisplayName,-20} ({legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2})     {(legion.IsPlayerControlled == 1 ? "是" : "否"),-10}");
                }
            }, readOnly: true);
        });

        var setColorCmd = new Command("set-color", "设置军团颜色");
        var setColorIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setColorRArg = new Argument<int>("r") { Description = "红色(0-255)" };
        var setColorGArg = new Argument<int>("g") { Description = "绿色(0-255)" };
        var setColorBArg = new Argument<int>("b") { Description = "蓝色(0-255)" };
        setColorCmd.Arguments.Add(setColorIdArg);
        setColorCmd.Arguments.Add(setColorRArg);
        setColorCmd.Arguments.Add(setColorGArg);
        setColorCmd.Arguments.Add(setColorBArg);
        setColorCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setColorIdArg);
                byte r = (byte)Math.Clamp(parseResult.GetValue(setColorRArg), 0, 255);
                byte g = (byte)Math.Clamp(parseResult.GetValue(setColorGArg), 0, 255);
                byte b = (byte)Math.Clamp(parseResult.GetValue(setColorBArg), 0, 255);
                var result = legion.SetLegionColor(legionId, r, g, b);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} 颜色为 ({r},{g},{b})");
            });
        });

        var setActionCmd = new Command("set-action", "设置军团ActionId");
        var setActionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setActionValArg = new Argument<int>("actionId") { Description = "ActionId" };
        setActionCmd.Arguments.Add(setActionIdArg);
        setActionCmd.Arguments.Add(setActionValArg);
        setActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setActionIdArg);
                int actionId = parseResult.GetValue(setActionValArg);
                var result = legion.SetLegionActionId(legionId, actionId);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} ActionId={actionId}");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(setColorCmd);
        cmd.Subcommands.Add(setActionCmd);

        return cmd;
    }

    private Command BuildArmyCommand()
    {
        var cmd = new Command("army", "部队修改操作");

        var listCmd = new Command("list", "列出所有部队");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var armies = mapData.Armies;
                _output.WriteLine($"部队总数: {armies.Count}");
                if (armies.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {"军团",-6}");
                    _output.WriteLine(new string('-', 30));
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var army = armies[i];
                        int col = army.Coordinate % mapData.MapWidth;
                        int row = army.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} ({col},{row})    {army.LegionId,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有部队");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Armies.Count;
                mapData.Armies.Clear();
                _output.WriteLine($"已删除 {count} 个部队");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

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
                    _output.WriteLine($"{"序号",-6} {"坐标",-10} {"军团",-6} {"组织",-6} {"血量",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < traps.Count; i++)
                    {
                        var trap = traps[i];
                        int col = trap.Coordinate % mapData.MapWidth;
                        int row = trap.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} ({col},{row})    {trap.LegionId,-6} {trap.Organization,-6} {trap.Health,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有陷阱");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Traps.Count;
                mapData.Traps.Clear();
                _output.WriteLine($"已删除 {count} 个陷阱");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

    private Command BuildInfoCommand()
    {
        var cmd = new Command("info", "显示地图信息");

        cmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var header = mapData.Header;
                _output.WriteLine("========== 地图信息 ==========");
                _output.WriteLine($"  地图尺寸:    {header.MapWidth} x {header.MapLength}");
                _output.WriteLine($"  总格子数:    {header.TotalTiles}");
                _output.WriteLine($"  军团数:      {mapData.Legions.Count}");
                _output.WriteLine($"  建筑数:      {mapData.Buildings.Count}");
                _output.WriteLine($"  省份数据:    {mapData.Provinces.Count} 格");
                _output.WriteLine($"  归属数据:    {mapData.Belongs.Count} 格");
                _output.WriteLine($"  地形数据:    {mapData.Terrains.Count} 格");

                int provinceCount = mapData.Provinces.Count(p => p.ProvinceValue != 0);
                int belongCount = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                    if (mapData.GetBelongValueByIndex(i) != 0xFF) belongCount++;
                _output.WriteLine($"  有效省份:    {provinceCount} 格");
                _output.WriteLine($"  有效归属:    {belongCount} 格");
                _output.WriteLine("==============================");
            }, readOnly: true);
        });

        return cmd;
    }

    private bool _batchMode;
    private int _batchCommandCount;

    public void BeginBatchMode()
    {
        _batchMode = true;
        _batchCommandCount = 0;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.RebuildBuildingCoordIndex();
    }

    public void EndBatchMode()
    {
        _batchMode = false;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.InvalidateBuildingCoordIndex();
        if (_batchCommandCount > 0)
            DataModified?.Invoke();
        _batchCommandCount = 0;
    }

    private void WithModifiers(Action<MapData, EditModeManager> action, bool readOnly = false)
    {
        var ctx = CommandManager.Instance.GetContext();
        if (ctx?.MapData == null)
        {
            _output.WriteLine("地图数据未加载");
            return;
        }

        var mapData = ctx.MapData;
        var mgr = EditModeManager.Instance;

        if (!mgr.IsInitialized)
            mgr.Initialize(mapData);

        action(mapData, mgr);

        if (!readOnly)
        {
            if (_batchMode)
                _batchCommandCount++;
            else
                DataModified?.Invoke();
        }
    }

    public event Action? DataModified;

    /// <summary>
    /// 解析建筑名称：如果是数字直接返回，如果是中文则查找或创建城市名称条目
    /// </summary>
    private static short ParseBuildingName(string nameInput)
    {
        if (string.IsNullOrWhiteSpace(nameInput)) return -1;

        // 尝试解析为数字
        if (short.TryParse(nameInput, out var numericValue))
            return numericValue;

        // 不是数字，视为中文城市名称
        var parser = ConfigManager.Instance.GetStringTableParser();

        // 查找是否已存在该名称
        var existingId = parser.FindCityIdByName(nameInput);
        if (existingId.HasValue)
            return (short)existingId.Value;

        // 不存在则创建新条目
        int newId = parser.AddOrUpdateCityName(nameInput);
        parser.Save();

        // 触发城市名称重载
        ConfigManager.Instance.ReloadStringTable();

        return (short)newId;
    }

    private Command BuildRunCommand()
    {
        var cmd = new Command("run", "执行脚本文件(.zme)");

        var fileArg = new Argument<string>("file") { Description = "脚本文件路径(.zme)，支持绝对路径或相对于程序目录的路径" };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            var filePath = parseResult.GetValue(fileArg)!;

            if (!Path.IsPathRooted(filePath))
                filePath = Path.Combine(AppContext.BaseDirectory, filePath);

            if (!filePath.EndsWith(".zme", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine("[脚本] 仅支持 .zme 脚本文件");
                return;
            }

            if (!File.Exists(filePath))
            {
                _output.WriteLine($"[脚本] 文件不存在: {filePath}");
                return;
            }

            _output.WriteLine($"[脚本] 加载脚本文件: {Path.GetFileName(filePath)}");

            var lines = File.ReadAllLines(filePath);
            int executedCount = 0;
            int skippedCount = 0;

            BeginBatchMode();
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//"))
                    {
                        skippedCount++;
                        continue;
                    }

                    var args = CommandLineToArgs(line);
                    var parse = _rootCommand.Parse(args);
                    parse.Invoke();

                    executedCount++;
                }
            }
            finally
            {
                EndBatchMode();
            }

            _output.WriteLine($"[脚本] 执行完成: {executedCount} 条命令, {skippedCount} 条注释跳过, 共 {lines.Length} 行");
        });

        return cmd;
    }
}