using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Rendering;
using WC4MapEditor.Rendering.Helpers;

namespace WC4MapEditor.Cli;

public class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Main(string[] args)
    {
        // modify 的帮助自己打印：框架的 help 会额外追加一节"附加参数"，
        // 那一节对"控制台命令体"这种透传用法没有意义。
        if ((args.Length == 1 && args[0] == "modify") ||
            (args.Length == 2 && args[0] == "modify" && args[1] is "-h" or "--help" or "-?"))
        {
            PrintModifyHelp();
            return 0;
        }

        var rootCommand = BuildRootCommand();
        int exitCode = rootCommand.Parse(args).Invoke();

        // 各命令通过 Environment.ExitCode 上报失败（Invoke 的返回值不会带上它），
        // 这里合并两者，保证 PowerShell 的 $LASTEXITCODE / cmd 的 %ERRORLEVEL% 可靠。
        return exitCode != 0 ? exitCode : Environment.ExitCode;
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("WC4 Map Editor - CLI");

        root.Subcommands.Add(BuildStageCommand());
        root.Subcommands.Add(BuildConquestCommand());
        root.Subcommands.Add(BuildWorldCommand());
        root.Subcommands.Add(BuildScreenshotCommand());
        root.Subcommands.Add(BuildAssetCommand());
        root.Subcommands.Add(BuildModifyCommand());
        root.Subcommands.Add(BuildRunCommand());

        return root;
    }

    private static Command BuildStageCommand()
    {
        var cmd = new Command("stage", "战役文件解析");

        var fileArg = new Argument<string>("file") { Description = "战役BTL文件路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "导出JSON文件路径" };
        var detailedOpt = new Option<bool>("--detailed", "-d") { Description = "导出详细数据（包含所有子模块）" };
        var analyzeOpt = new Option<bool>("--analyze", "-a") { Description = "使用详细分析模式（展示所有字段）" };

        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(detailedOpt);
        cmd.Options.Add(analyzeOpt);

        cmd.SetAction(parseResult =>
        {
            if (parseResult.GetValue(analyzeOpt))
                AnalyzeStage(parseResult.GetValue(fileArg)!);
            else
                ParseStage(
                    parseResult.GetValue(fileArg)!,
                    parseResult.GetValue(outputOpt),
                    parseResult.GetValue(detailedOpt));
        });

        return cmd;
    }

    private static Command BuildConquestCommand()
    {
        var cmd = new Command("conquest", "征服文件解析");

        var fileArg = new Argument<string>("file") { Description = "征服BTL文件路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "导出JSON文件路径" };
        var detailedOpt = new Option<bool>("--detailed", "-d") { Description = "导出详细数据（包含所有子模块）" };
        var analyzeOpt = new Option<bool>("--analyze", "-a") { Description = "使用详细分析模式（展示所有字段）" };

        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(detailedOpt);
        cmd.Options.Add(analyzeOpt);

        cmd.SetAction(parseResult =>
        {
            if (parseResult.GetValue(analyzeOpt))
                AnalyzeConquest(parseResult.GetValue(fileArg)!);
            else
                ParseConquest(
                    parseResult.GetValue(fileArg)!,
                    parseResult.GetValue(outputOpt),
                    parseResult.GetValue(detailedOpt));
        });

        return cmd;
    }

    private static Command BuildWorldCommand()
    {
        var cmd = new Command("world", "世界地图文件解析");

        var fileArg = new Argument<string>("file") { Description = "世界地图文件路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "导出JSON文件路径" };
        var detailedOpt = new Option<bool>("--detailed", "-d") { Description = "导出详细地形数据" };
        var analyzeOpt = new Option<bool>("--analyze", "-a") { Description = "使用详细分析模式（展示所有字段）" };

        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(detailedOpt);
        cmd.Options.Add(analyzeOpt);

        cmd.SetAction(parseResult =>
        {
            if (parseResult.GetValue(analyzeOpt))
                AnalyzeWorld(parseResult.GetValue(fileArg)!);
            else
                ParseWorld(
                    parseResult.GetValue(fileArg)!,
                    parseResult.GetValue(outputOpt),
                    parseResult.GetValue(detailedOpt));
        });

        return cmd;
    }

    private static Command BuildScreenshotCommand()
    {
        var inputArg = new Argument<string>("input") { Description = "BTL文件路径" };
        var outputArg = new Argument<string>("output") { Description = "输出PNG路径" };
        var zoomOpt = new Option<int>("--zoom") { Description = "缩放级别", DefaultValueFactory = _ => 1 };

        var cmd = new Command("screenshot", "离屏渲染截图");
        cmd.Arguments.Add(inputArg);
        cmd.Arguments.Add(outputArg);
        cmd.Options.Add(zoomOpt);
        cmd.SetAction(parseResult =>
        {
            Screenshot(
                parseResult.GetValue(inputArg)!,
                parseResult.GetValue(outputArg)!,
                parseResult.GetValue(zoomOpt));
        });
        return cmd;
    }

    private static Command BuildAssetCommand()
    {
        var cmd = new Command("asset", "资源管理操作");

        var scanPathArg = new Argument<string?>("path") { Description = "assets 根目录（默认自动探测）" };
        scanPathArg.Arity = ArgumentArity.ZeroOrOne;
        var forceOpt = new Option<bool>("--force", "-f", "强制重新扫描");
        var scanCmd = new Command("scan", "扫描 assets 目录并构建缓存");
        scanCmd.Arguments.Add(scanPathArg);
        scanCmd.Options.Add(forceOpt);
        scanCmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(scanPathArg);
            bool force = parseResult.GetValue(forceOpt);
            AssetScan(path, force);
        });
        cmd.Subcommands.Add(scanCmd);

        var summaryCmd = new Command("summary", "显示资源概览（按目录和类别统计）");
        summaryCmd.SetAction(_ => AssetSummary());
        cmd.Subcommands.Add(summaryCmd);

        var listKindOpt = new Option<AssetKind?>("--kind", "-k", "按语义类别过滤");
        var listExtOpt = new Option<string?>("--ext", "-e", "按扩展名过滤（可带或不带点）");
        var listDirOpt = new Option<string?>("--dir", "-d", "按顶层目录过滤");
        var listNameOpt = new Option<string?>("--name", "-n", "文件名包含子串（不区分大小写）");
        var listLimitOpt = new Option<int>("--limit", "-l") { Description = "最多显示条数", DefaultValueFactory = _ => 50 };
        var listCmd = new Command("list", "列出资源文件");
        listCmd.Options.Add(listKindOpt);
        listCmd.Options.Add(listExtOpt);
        listCmd.Options.Add(listDirOpt);
        listCmd.Options.Add(listNameOpt);
        listCmd.Options.Add(listLimitOpt);
        listCmd.SetAction(parseResult =>
        {
            AssetList(
                parseResult.GetValue(listKindOpt),
                parseResult.GetValue(listExtOpt),
                parseResult.GetValue(listDirOpt),
                parseResult.GetValue(listNameOpt),
                parseResult.GetValue(listLimitOpt));
        });
        cmd.Subcommands.Add(listCmd);

        var queryKindOpt = new Option<AssetKind?>("--kind", "-k", "语义类别");
        var queryExtOpt = new Option<string?>("--ext", "-e", "扩展名");
        var queryDirOpt = new Option<string?>("--dir", "-d", "顶层目录");
        var queryNameOpt = new Option<string?>("--name", "-n", "文件名子串");
        var queryFormatOpt = new Option<string>("--format") { Description = "输出格式: table, paths, json", DefaultValueFactory = _ => "table" };
        var queryCmd = new Command("query", "组合条件查询资源");
        queryCmd.Options.Add(queryKindOpt);
        queryCmd.Options.Add(queryExtOpt);
        queryCmd.Options.Add(queryDirOpt);
        queryCmd.Options.Add(queryNameOpt);
        queryCmd.Options.Add(queryFormatOpt);
        queryCmd.SetAction(parseResult =>
        {
            AssetQuery(
                parseResult.GetValue(queryKindOpt),
                parseResult.GetValue(queryExtOpt),
                parseResult.GetValue(queryDirOpt),
                parseResult.GetValue(queryNameOpt),
                parseResult.GetValue(queryFormatOpt)!);
        });
        cmd.Subcommands.Add(queryCmd);

        return cmd;
    }

    private static void AnalyzeStage(string file)
    {
        try
        {
            Console.WriteLine($"[Stage] 加载战役文件: {file}");
            var parser = new StageParser(file);
            
            parser.GetHeaderData();
            parser.GetLegionData();
            parser.GetTerrainData();
            parser.GetProvinceData();
            parser.GetBelongData();
            parser.GetBuildingData();
            parser.GetArmyData();
            parser.GetTrapData();
            parser.GetCaseData();
            parser.GetWeatherData();
            parser.GetEventData();
            parser.GetReinforcementData();
            parser.GetAirForceData();
            parser.GetUnitPlaceData();
            parser.GetCapitalData();
            parser.GetStrategyConstructionData();
            parser.GetAirSupportData();
            
            var analyzer = new BTLAnalyzer();
            string analysis = analyzer.AnalyzeStageParser(parser);
            Console.WriteLine(analysis);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void AnalyzeConquest(string file)
    {
        try
        {
            Console.WriteLine($"[Conquest] 加载征服文件: {file}");
            var parser = new ConquestParser(file);
            
            parser.GetHeaderData();
            parser.GetLegionData();
            parser.GetProvinceData();
            parser.GetBelongData();
            parser.GetBuildingData();
            parser.GetTroopData();
            parser.GetTrapData();
            parser.GetCaseData();
            parser.GetWeatherData();
            parser.GetEventData();
            parser.GetReinforcementData();
            parser.GetAirForceData();
            parser.GetUnitPlaceData();
            parser.GetCapitalData();
            parser.GetStrategyConstructionData();
            parser.GetAirSupportData();
            
            var analyzer = new BTLAnalyzer();
            string analysis = analyzer.AnalyzeConquestParser(parser);
            Console.WriteLine(analysis);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void AnalyzeWorld(string file)
    {
        try
        {
            Console.WriteLine($"[World] 加载世界地图文件: {file}");
            var mapData = WorldParser.LoadFromFile(file);
            
            var analyzer = new BTLAnalyzer();
            string analysis = analyzer.AnalyzeWorldParser(mapData);
            Console.WriteLine(analysis);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ParseStage(string file, string? output, bool detailed)
    {
        try
        {
            Console.WriteLine($"[Stage] 加载战役文件: {file}");
            var parser = new StageParser(file);

            var header = parser.GetHeaderData();
            
            // 简略模式：输出详细统计信息
            Console.WriteLine("\n========== 文件头信息 (BTLHeader) ==========");
            Console.WriteLine($"  BTL版本:           {header.BtlVersion}");
            Console.WriteLine($"  地图编号:          {header.MapNumber}");
            Console.WriteLine($"  地图裁剪X:         {header.MapClipX}");
            Console.WriteLine($"  地图裁剪Y:         {header.MapClipY}");
            Console.WriteLine($"  地图尺寸:          {header.MapWidth} x {header.MapLength} (宽 x 长)");
            Console.WriteLine($"  总格子数:          {header.TotalTiles}");
            Console.WriteLine();
            Console.WriteLine("  === 实体数量 ===");
            Console.WriteLine($"  军团数:            {header.ArmyCount}");
            Console.WriteLine($"  建筑数:            {header.BuildingCount}");
            Console.WriteLine($"  部队数:            {header.TroopCount}");
            Console.WriteLine($"  省份数:            {header.SelectableTileCount}");
            Console.WriteLine();
            Console.WriteLine("  === 游戏设置 ===");
            Console.WriteLine($"  胜利条件:          {header.VictoryCondition}");
            Console.WriteLine($"  最小回合:          {header.MinTurns}");
            Console.WriteLine($"  最大回合:          {header.MaxTurns}");
            Console.WriteLine($"  放置点A:           {header.PlacementA}");
            Console.WriteLine($"  放置点B:           {header.PlacementB}");
            Console.WriteLine($"  征服旗帜位置:      {header.ConqueredFlagPosition}");
            Console.WriteLine();
            Console.WriteLine("  === 特殊模块数量 ===");
            Console.WriteLine($"  陷阱数:            {header.TrapCount}");
            Console.WriteLine($"  计策数:            {header.PlanCount}");
            Console.WriteLine($"  事件数:            {header.EventCount}");
            Console.WriteLine($"  天气数:            {header.WeatherCount}");
            Console.WriteLine($"  增援数:            {header.ReinforcementCount}");
            Console.WriteLine($"  空袭数:            {header.AirRaidCount}");
            Console.WriteLine($"  战略建筑数:        {header.StrategyCount}");
            Console.WriteLine($"  空中支援数:        {header.AirSupportCount}");
            Console.WriteLine();
            Console.WriteLine("  === 累积资源 ===");
            Console.WriteLine($"  累积经济:          {header.AccumulatedEconomy}");
            Console.WriteLine($"  累积工业:          {header.AccumulatedIndustry}");
            Console.WriteLine($"  累积科技:          {header.AccumulatedTech}");
            Console.WriteLine();
            Console.WriteLine("  === 未知字段 ===");
            Console.WriteLine($"  Unknown3:          {header.Unknown3}");
            Console.WriteLine($"  Unknown4:          {header.Unknown4}");
            Console.WriteLine($"  Unknown5:          {header.Unknown5}");
            Console.WriteLine($"  Unknown6:          {header.Unknown6}");
            Console.WriteLine($"  Unknown7:          {header.Unknown7}");
            Console.WriteLine("==========================================\n");

            // 加载基础数据并统计
            var legions = parser.GetLegionData();
            var buildings = parser.GetBuildingData();
            var armies = parser.GetArmyData();

            Console.WriteLine("========== 实体统计 ==========");
            Console.WriteLine($"  军团:    {legions.Count} 个");
            if (legions.Count > 0)
            {
                var playerLegions = legions.Count(l => l.IsPlayer);
                var aiLegions = legions.Count - playerLegions;
                Console.WriteLine($"    - 玩家控制: {playerLegions} 个");
                Console.WriteLine($"    - AI控制:   {aiLegions} 个");
            }

            Console.WriteLine($"  建筑:    {buildings.Count} 个");
            if (buildings.Count > 0)
            {
                var buildingGroups = buildings.GroupBy(b => b.BuildingType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in buildingGroups.Take(5))
                {
                    string typeName = g.First().GetBuildingTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (buildingGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {buildingGroups.Count() - 5} 种类型");
            }

            Console.WriteLine($"  部队:    {armies.Count} 个");
            if (armies.Count > 0)
            {
                var unitGroups = armies.GroupBy(a => a.UnitType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in unitGroups.Take(5))
                {
                    string typeName = g.First().GetUnitTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (unitGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {unitGroups.Count() - 5} 种类型");
            }

            if (parser.Header.UsesV3Records && parser.ArmiesV3.Count > 0)
            {
                Console.WriteLine($"  部队V3:  {parser.ArmiesV3.Count} 个");
            }
            Console.WriteLine("================================\n");

            // 单位的归属按所在格子的归属值算，需要完整的 MapData（裁剪过的征服地图归属序号 ≠ 格子序号）
            var mapData = new MapData();
            StageParser.PopulateMapData(parser, mapData);

            var result = new StageExportData
            {
                FileType = "Stage",
                Header = ExportHeader(header),
                Legions = legions.Select(ExportLegionSummary).ToList(),
                Buildings = buildings.Select(ExportBuildingSummary).ToList(),
                Armies = armies.Select(a => ExportArmySummary(a, mapData)).ToList()
            };

            if (detailed)
            {
                Console.WriteLine("  [详细模式] 加载所有子模块数据...");
                result.Terrains = parser.GetTerrainData().Select(ExportTerrain).ToList();
                result.Provinces = parser.GetProvinceData().Select(ExportProvince).ToList();
                result.Belongs = parser.GetBelongData();
                result.Traps = parser.GetTrapData().Select(ExportTrap).ToList();
                result.Cases = parser.GetCaseData().Select(ExportMapCase).ToList();
                result.Weathers = parser.GetWeatherData().Select(ExportWeather).ToList();
                result.Events = parser.GetEventData().Select(ExportMapEvent).ToList();
                result.Reinforcements = parser.GetReinforcementData().Select(ExportReinforcement).ToList();
                result.AirForces = parser.GetAirForceData().Select(ExportAirForce).ToList();
                result.UnitPlacements = parser.GetUnitPlaceData().Select(ExportUnitPlacement).ToList();
                result.Capitals = parser.GetCapitalData().Select(ExportCapital).ToList();
                result.StrategyConstructions = parser.GetStrategyConstructionData().Select(ExportStrategicConstruction).ToList();
                result.AirSupports = parser.GetAirSupportData().Select(ExportAirSupport).ToList();

                if (parser.Header.UsesV3Records)
                {
                    result.ArmiesV3 = parser.ArmiesV3.Select(a => ExportArmyV3(a, mapData)).ToList();
                    result.ReinforcementsV3 = parser.ReinforcementsV3.Select(ExportReinforcementV3).ToList();
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                File.WriteAllText(output, json);
                Console.WriteLine($"\n已导出到: {output}");
            }
            else
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                Console.WriteLine("\n--- JSON 输出 ---");
                Console.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ParseConquest(string file, string? output, bool detailed)
    {
        try
        {
            Console.WriteLine($"[Conquest] 加载征服文件: {file}");
            var parser = new ConquestParser(file);

            var header = parser.GetHeaderData();
            
            // 简略模式：输出详细统计信息
            Console.WriteLine("\n========== 文件头信息 (BTLHeader) ==========");
            Console.WriteLine($"  BTL版本:           {header.BtlVersion}");
            Console.WriteLine($"  地图编号:          {header.MapNumber}");
            Console.WriteLine($"  地图裁剪X:         {header.MapClipX}");
            Console.WriteLine($"  地图裁剪Y:         {header.MapClipY}");
            Console.WriteLine($"  地图尺寸:          {header.MapWidth} x {header.MapLength} (宽 x 长)");
            Console.WriteLine($"  总格子数:          {header.TotalTiles}");
            Console.WriteLine();
            Console.WriteLine("  === 实体数量 ===");
            Console.WriteLine($"  军团数:            {header.ArmyCount}");
            Console.WriteLine($"  建筑数:            {header.BuildingCount}");
            Console.WriteLine($"  部队数:            {header.TroopCount}");
            Console.WriteLine($"  省份数:            {header.SelectableTileCount}");
            Console.WriteLine();
            Console.WriteLine("  === 游戏设置 ===");
            Console.WriteLine($"  胜利条件:          {header.VictoryCondition}");
            Console.WriteLine($"  最小回合:          {header.MinTurns}");
            Console.WriteLine($"  最大回合:          {header.MaxTurns}");
            Console.WriteLine($"  放置点A:           {header.PlacementA}");
            Console.WriteLine($"  放置点B:           {header.PlacementB}");
            Console.WriteLine($"  征服旗帜位置:      {header.ConqueredFlagPosition}");
            Console.WriteLine();
            Console.WriteLine("  === 特殊模块数量 ===");
            Console.WriteLine($"  陷阱数:            {header.TrapCount}");
            Console.WriteLine($"  计策数:            {header.PlanCount}");
            Console.WriteLine($"  事件数:            {header.EventCount}");
            Console.WriteLine($"  天气数:            {header.WeatherCount}");
            Console.WriteLine($"  增援数:            {header.ReinforcementCount}");
            Console.WriteLine($"  空袭数:            {header.AirRaidCount}");
            Console.WriteLine($"  战略建筑数:        {header.StrategyCount}");
            Console.WriteLine($"  空中支援数:        {header.AirSupportCount}");
            Console.WriteLine();
            Console.WriteLine("  === 累积资源 ===");
            Console.WriteLine($"  累积经济:          {header.AccumulatedEconomy}");
            Console.WriteLine($"  累积工业:          {header.AccumulatedIndustry}");
            Console.WriteLine($"  累积科技:          {header.AccumulatedTech}");
            Console.WriteLine();
            Console.WriteLine("  === 未知字段 ===");
            Console.WriteLine($"  Unknown3:          {header.Unknown3}");
            Console.WriteLine($"  Unknown4:          {header.Unknown4}");
            Console.WriteLine($"  Unknown5:          {header.Unknown5}");
            Console.WriteLine($"  Unknown6:          {header.Unknown6}");
            Console.WriteLine($"  Unknown7:          {header.Unknown7}");
            Console.WriteLine("==========================================\n");

            // 加载基础数据并统计
            var legions = parser.GetLegionData();
            var buildings = parser.GetBuildingData();
            var armies = parser.GetTroopData();

            Console.WriteLine("========== 实体统计 ==========");
            Console.WriteLine($"  军团:    {legions.Count} 个");
            if (legions.Count > 0)
            {
                var playerLegions = legions.Count(l => l.IsPlayer);
                var aiLegions = legions.Count - playerLegions;
                Console.WriteLine($"    - 玩家控制: {playerLegions} 个");
                Console.WriteLine($"    - AI控制:   {aiLegions} 个");
            }

            Console.WriteLine($"  建筑:    {buildings.Count} 个");
            if (buildings.Count > 0)
            {
                var buildingGroups = buildings.GroupBy(b => b.BuildingType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in buildingGroups.Take(5))
                {
                    string typeName = g.First().GetBuildingTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (buildingGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {buildingGroups.Count() - 5} 种类型");
            }

            Console.WriteLine($"  部队:    {armies.Count} 个");
            if (armies.Count > 0)
            {
                var unitGroups = armies.GroupBy(a => a.UnitType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in unitGroups.Take(5))
                {
                    string typeName = g.First().GetUnitTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (unitGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {unitGroups.Count() - 5} 种类型");
            }

            if (parser.Header.UsesV3Records && parser.ArmiesV3.Count > 0)
            {
                Console.WriteLine($"  部队V3:  {parser.ArmiesV3.Count} 个");
            }
            Console.WriteLine("================================\n");

            // 单位的归属按所在格子的归属值算，需要完整的 MapData（裁剪过的征服地图归属序号 ≠ 格子序号）
            var mapData = new MapData();
            ConquestParser.PopulateMapData(parser, mapData);

            var result = new ConquestExportData
            {
                FileType = "Conquest",
                Header = ExportHeader(header),
                Legions = legions.Select(ExportLegionSummary).ToList(),
                Buildings = buildings.Select(ExportBuildingSummary).ToList(),
                Armies = armies.Select(a => ExportArmySummary(a, mapData)).ToList()
            };

            if (detailed)
            {
                Console.WriteLine("  [详细模式] 加载所有子模块数据...");
                result.Provinces = parser.GetProvinceData().Select(ExportProvince).ToList();
                result.Belongs = parser.GetBelongData();
                result.Traps = parser.GetTrapData().Select(ExportTrap).ToList();
                result.Cases = parser.GetCaseData().Select(ExportMapCase).ToList();
                result.Weathers = parser.GetWeatherData().Select(ExportWeather).ToList();
                result.Events = parser.GetEventData().Select(ExportMapEvent).ToList();
                result.Reinforcements = parser.GetReinforcementData().Select(ExportReinforcement).ToList();
                result.AirForces = parser.GetAirForceData().Select(ExportAirForce).ToList();
                result.UnitPlacements = parser.GetUnitPlaceData().Select(ExportUnitPlacement).ToList();
                result.Capitals = parser.GetCapitalData().Select(ExportCapital).ToList();
                result.StrategyConstructions = parser.GetStrategyConstructionData().Select(ExportStrategicConstruction).ToList();
                result.AirSupports = parser.GetAirSupportData().Select(ExportAirSupport).ToList();

                if (parser.Header.UsesV3Records)
                {
                    result.ArmiesV3 = parser.ArmiesV3.Select(a => ExportArmyV3(a, mapData)).ToList();
                    result.ReinforcementsV3 = parser.ReinforcementsV3.Select(ExportReinforcementV3).ToList();
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                File.WriteAllText(output, json);
                Console.WriteLine($"\n已导出到: {output}");
            }
            else
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                Console.WriteLine("\n--- JSON 输出 ---");
                Console.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ParseWorld(string file, string? output, bool detailed)
    {
        try
        {
            Console.WriteLine($"[World] 加载世界地图文件: {file}");
            var mapData = WorldParser.LoadFromFile(file);

            var header = mapData.Header;
            
            // 简略模式：输出详细统计信息
            Console.WriteLine("\n========== 文件头信息 (BTLHeader) ==========");
            Console.WriteLine($"  BTL版本:           {header.BtlVersion}");
            Console.WriteLine($"  地图编号:          {header.MapNumber}");
            Console.WriteLine($"  地图裁剪X:         {header.MapClipX}");
            Console.WriteLine($"  地图裁剪Y:         {header.MapClipY}");
            Console.WriteLine($"  地图尺寸:          {header.MapWidth} x {header.MapLength} (宽 x 长)");
            Console.WriteLine($"  总格子数:          {header.TotalTiles}");
            Console.WriteLine();
            Console.WriteLine("  === 实体数量 ===");
            Console.WriteLine($"  军团数:            {header.ArmyCount}");
            Console.WriteLine($"  建筑数:            {header.BuildingCount}");
            Console.WriteLine($"  部队数:            {header.TroopCount}");
            Console.WriteLine($"  省份数:            {header.SelectableTileCount}");
            Console.WriteLine();
            Console.WriteLine("  === 游戏设置 ===");
            Console.WriteLine($"  胜利条件:          {header.VictoryCondition}");
            Console.WriteLine($"  最小回合:          {header.MinTurns}");
            Console.WriteLine($"  最大回合:          {header.MaxTurns}");
            Console.WriteLine($"  放置点A:           {header.PlacementA}");
            Console.WriteLine($"  放置点B:           {header.PlacementB}");
            Console.WriteLine($"  征服旗帜位置:      {header.ConqueredFlagPosition}");
            Console.WriteLine();
            Console.WriteLine("  === 特殊模块数量 ===");
            Console.WriteLine($"  陷阱数:            {header.TrapCount}");
            Console.WriteLine($"  计策数:            {header.PlanCount}");
            Console.WriteLine($"  事件数:            {header.EventCount}");
            Console.WriteLine($"  天气数:            {header.WeatherCount}");
            Console.WriteLine($"  增援数:            {header.ReinforcementCount}");
            Console.WriteLine($"  空袭数:            {header.AirRaidCount}");
            Console.WriteLine($"  战略建筑数:        {header.StrategyCount}");
            Console.WriteLine($"  空中支援数:        {header.AirSupportCount}");
            Console.WriteLine();
            Console.WriteLine("  === 累积资源 ===");
            Console.WriteLine($"  累积经济:          {header.AccumulatedEconomy}");
            Console.WriteLine($"  累积工业:          {header.AccumulatedIndustry}");
            Console.WriteLine($"  累积科技:          {header.AccumulatedTech}");
            Console.WriteLine();
            Console.WriteLine("  === 未知字段 ===");
            Console.WriteLine($"  Unknown3:          {header.Unknown3}");
            Console.WriteLine($"  Unknown4:          {header.Unknown4}");
            Console.WriteLine($"  Unknown5:          {header.Unknown5}");
            Console.WriteLine($"  Unknown6:          {header.Unknown6}");
            Console.WriteLine($"  Unknown7:          {header.Unknown7}");
            Console.WriteLine("==========================================\n");

            Console.WriteLine($"  内存占用: {mapData.GetMemoryUsageMB():F2} MB");

            // 实体统计
            var legions = mapData.Legions;
            var buildings = mapData.Buildings;
            var armies = mapData.Armies;

            Console.WriteLine("========== 实体统计 ==========");
            Console.WriteLine($"  军团:    {legions.Count} 个");
            if (legions.Count > 0)
            {
                var playerLegions = legions.Count(l => l.IsPlayer);
                var aiLegions = legions.Count - playerLegions;
                Console.WriteLine($"    - 玩家控制: {playerLegions} 个");
                Console.WriteLine($"    - AI控制:   {aiLegions} 个");
            }

            Console.WriteLine($"  建筑:    {buildings.Count} 个");
            if (buildings.Count > 0)
            {
                var buildingGroups = buildings.GroupBy(b => b.BuildingType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in buildingGroups.Take(5))
                {
                    string typeName = g.First().GetBuildingTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (buildingGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {buildingGroups.Count() - 5} 种类型");
            }

            Console.WriteLine($"  部队:    {armies.Count} 个");
            if (armies.Count > 0)
            {
                var unitGroups = armies.GroupBy(a => a.UnitType).OrderByDescending(g => g.Count());
                Console.WriteLine("    类型分布:");
                foreach (var g in unitGroups.Take(5))
                {
                    string typeName = g.First().GetUnitTypeName();
                    Console.WriteLine($"      - {typeName} (类型{g.Key}): {g.Count()} 个");
                }
                if (unitGroups.Count() > 5)
                    Console.WriteLine($"      ... 及其他 {unitGroups.Count() - 5} 种类型");
            }

            Console.WriteLine($"  地形:    {mapData.TerrainCount} 个");
            Console.WriteLine("================================\n");

            var result = new WorldExportData
            {
                FileType = "World",
                FilePath = mapData.FilePath,
                MapWidth = mapData.MapWidth,
                MapHeight = mapData.MapHeight,
                Header = ExportHeader(header),
                Buildings = buildings.Select(ExportBuildingSummary).ToList(),
                Armies = armies.Select(a => ExportArmySummary(a, mapData)).ToList(),
                Legions = legions.Select(ExportLegionSummary).ToList()
            };

            if (detailed)
            {
                Console.WriteLine("  [详细模式] 加载地形数据...");
                int terrainCount = mapData.TerrainCount;
                var terrains = new List<TerrainExport>(Math.Min(terrainCount, 10000));

                for (int i = 0; i < terrainCount && i < 10000; i++)
                {
                    var terrainData = mapData.GetTerrain(i);
                    var province = mapData.GetProvince(i);
                    int row = i / mapData.MapWidth;
                    int col = i % mapData.MapWidth;

                    terrains.Add(new TerrainExport
                    {
                        Index = i,
                        Col = col,
                        Row = row,
                        TileType1 = terrainData.TileType1,
                        DecorationType1 = terrainData.DecorationType1,
                        TextureOffsetX1 = terrainData.TextureOffsetX1,
                        TextureOffsetY1 = terrainData.TextureOffsetY1,
                        TileType2 = terrainData.TileType2,
                        DecorationType2 = terrainData.DecorationType2,
                        TextureOffsetX2 = terrainData.TextureOffsetX2,
                        TextureOffsetY2 = terrainData.TextureOffsetY2,
                        TileType3 = terrainData.TileType3,
                        DecorationType3 = terrainData.DecorationType3,
                        TextureOffsetX3 = terrainData.TextureOffsetX3,
                        TextureOffsetY3 = terrainData.TextureOffsetY3,
                        RiverValue = terrainData.RiverValue,
                        ProvinceValue = province.ProvinceValue
                    });
                }

                result.Terrains = terrains;

                if (terrainCount > 10000)
                {
                    Console.WriteLine($"  警告: 地形数量({terrainCount})超过10000，仅导出前10000个");
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                File.WriteAllText(output, json);
                Console.WriteLine($"\n已导出到: {output}");
            }
            else
            {
                string json = JsonSerializer.Serialize(result, JsonOptions);
                Console.WriteLine("\n--- JSON 输出 ---");
                Console.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    #region Export Helper Methods

    private static HeaderExport ExportHeader(BTLHeader header)
    {
        return new HeaderExport
        {
            BtlVersion = header.BtlVersion,
            MapNumber = header.MapNumber,
            MapClipX = header.MapClipX,
            MapClipY = header.MapClipY,
            MapWidth = header.MapWidth,
            MapLength = header.MapLength,
            ArmyCount = header.ArmyCount,
            BuildingCount = header.BuildingCount,
            TroopCount = header.TroopCount,
            PlanCount = header.PlanCount,
            EventCount = header.EventCount,
            WeatherCount = header.WeatherCount,
            VictoryCondition = header.VictoryCondition,
            MinTurns = header.MinTurns,
            MaxTurns = header.MaxTurns,
            ReinforcementCount = header.ReinforcementCount,
            AirRaidCount = header.AirRaidCount,
            PlacementA = header.PlacementA,
            PlacementB = header.PlacementB,
            ConqueredFlagPosition = header.ConqueredFlagPosition,
            SelectableTileCount = header.SelectableTileCount,
            TrapCount = header.TrapCount,
            StrategyCount = header.StrategyCount,
            AirSupportCount = header.AirSupportCount
        };
    }

    private static LegionSummaryExport ExportLegionSummary(Legion legion)
    {
        return new LegionSummaryExport
        {
            ActionId = legion.ActionId,
            CountryId = legion.CountryId,
            IsPlayerControlled = legion.IsPlayerControlled,
            Camp = legion.Camp,
            ColorR = legion.ColorR,
            ColorG = legion.ColorG,
            ColorB = legion.ColorB
        };
    }

    private static BuildingSummaryExport ExportBuildingSummary(Building building)
    {
        return new BuildingSummaryExport
        {
            Coordinate = building.Coordinate,
            BuildingType = building.BuildingType,
            BuildingTypeName = building.GetBuildingTypeName(),
            KeyPoint = building.KeyPoint,
            FactoryLevel = building.FactoryLevel,
            ResearchLevel = building.ResearchLevel,
            MedicalLevel = building.MedicalLevel
        };
    }

    /// <summary>格子上的归属值：单位的归属一律按所在格子的归属算（单位自带的 LegionId 只是编辑器运行期字段）。</summary>
    private static int BelongAtCell(MapData map, int cellIndex)
    {
        if (map.MapWidth <= 0 || cellIndex < 0 || cellIndex >= map.MapWidth * map.MapHeight) return 0xFF;
        return map.GetBelongValue(cellIndex % map.MapWidth, cellIndex / map.MapWidth);
    }

    private static ArmySummaryExport ExportArmySummary(Army army, MapData map)
    {
        return new ArmySummaryExport
        {
            Coordinate = army.CoordinateIndex,
            UnitType = army.UnitType,
            UnitTypeName = army.GetUnitTypeName(),
            Level = army.Level,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            Belong = BelongAtCell(map, army.CoordinateIndex)
        };
    }

    private static ArmyV3Export ExportArmyV3(Army_3 army, MapData map)
    {
        return new ArmyV3Export
        {
            Coordinate = army.CoordinateIndex,
            UnitType = army.UnitType,
            Level = army.Level,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            Belong = BelongAtCell(map, army.CoordinateIndex)
        };
    }

    private static TerrainExport ExportTerrain(Terrain terrain)
    {
        return new TerrainExport
        {
            TileType1 = terrain.TileType1,
            DecorationType1 = terrain.DecorationType1,
            TextureOffsetX1 = terrain.TextureOffsetX1,
            TextureOffsetY1 = terrain.TextureOffsetY1,
            TileType2 = terrain.TileType2,
            DecorationType2 = terrain.DecorationType2,
            TextureOffsetX2 = terrain.TextureOffsetX2,
            TextureOffsetY2 = terrain.TextureOffsetY2,
            TileType3 = terrain.TileType3,
            DecorationType3 = terrain.DecorationType3,
            TextureOffsetX3 = terrain.TextureOffsetX3,
            TextureOffsetY3 = terrain.TextureOffsetY3,
            RiverValue = terrain.RiverValue
        };
    }

    private static ProvinceExport ExportProvince(Province province)
    {
        return new ProvinceExport { ProvinceValue = province.ProvinceValue };
    }

    private static TrapExport ExportTrap(Trap trap)
    {
        return new TrapExport
        {
            Coordinate = trap.CellIndex,
            LegionId = trap.LegionId,
            Organization = trap.Organization,
            Health = trap.Health,
            Extra1 = trap.Extra1,
            Extra2 = trap.Extra2
        };
    }

    private static MapCaseExport ExportMapCase(MapCase mapCase)
    {
        return new MapCaseExport
        {
            PolicyNumber = mapCase.PolicyNumber,
            CaseType = mapCase.CaseType,
            TriggerRound = mapCase.TriggerRound,
            TargetValue = mapCase.TargetTile
        };
    }

    private static WeatherExport ExportWeather(Weather weather)
    {
        return new WeatherExport
        {
            WeatherType = weather.WeatherType,
            Reserved1 = weather.Reserved1,
            TriggerRound = weather.TriggerRound,
            Duration = weather.Duration
        };
    }

    private static MapEventExport ExportMapEvent(MapEvent e)
    {
        return new MapEventExport
        {
            Sequence = e.Sequence,
            RelatedEvent = e.RelatedEvent,
            TriggerCondition = e.TriggerCondition,
            TriggerEvent = e.TriggerEvent,
            TriggerLegion = e.TriggerLegion,
            AffectedLegion = e.AffectedLegion,
            CampChange = e.CampChange,
            Reserved = e.Reserved,
            TriggerRound = e.TriggerRound,
            DialogueCode = e.DialogueCode,
            EndSegment = e.EndSegment
        };
    }

    private static ReinforcementExport ExportReinforcement(Reinforcement r)
    {
        return new ReinforcementExport
        {
            Coordinate = r.Coordinate,
            UnitType = r.UnitType,
            Level = r.Level,
            Organization = r.Organization,
            Direction = r.Direction,
            Reserved14 = r.Reserved14,
            Reserved18 = r.Reserved18,
            General = r.General,
            Rank = r.Rank,
            Quality = r.Quality,
            Skill1Level = r.Skill1Level,
            Skill2Level = r.Skill2Level,
            Skill3Level = r.Skill3Level,
            Skill4Level = r.Skill4Level,
            Skill5Level = r.Skill5Level,
            Badge1 = r.Badge1,
            Badge2 = r.Badge2,
            Badge3 = r.Badge3,
            OwnerCountry = r.OwnerCountry,
            SpawnRound = r.SpawnRound
        };
    }

    private static ReinforcementV3Export ExportReinforcementV3(Reinforcement_3 r)
    {
        return new ReinforcementV3Export
        {
            Coordinate = r.Coordinate,
            UnitType = r.UnitType,
            Level = r.Level,
            Organization = r.Organization,
            BehaviorMode = r.BehaviorMode,
            Direction = r.Direction,
            Unknown = r.Unknown,
            General = r.General,
            Rank = r.Rank,
            HpLevel = r.HpLevel,
            Skill1Level = r.Skill1Level,
            Skill2Level = r.Skill2Level,
            Skill3Level = r.Skill3Level,
            Skill4Level = r.Skill4Level,
            Skill5Level = r.Skill5Level,
            Waste1 = r.Waste1,
            Waste2 = r.Waste2,
            Waste3 = r.Waste3,
            OwnerLegion = r.OwnerLegion,
            SpawnRound = r.SpawnRound,
            Medal1 = r.Medal1,
            Medal2 = r.Medal2,
            Medal3 = r.Medal3,
            Ribbon1 = r.Ribbon1,
            Ribbon2 = r.Ribbon2,
            Ribbon3 = r.Ribbon3
        };
    }

    private static AirForceExport ExportAirForce(AirForce af)
    {
        return new AirForceExport
        {
            Coordinate = af.Coordinate,
            UnitType = af.UnitType,
            AmmoType = af.AmmoType,
            OwnerLegion = af.OwnerLegion,
            TriggerRound = af.TriggerRound
        };
    }

    private static UnitPlacementExport ExportUnitPlacement(UnitPlacement up)
    {
        return new UnitPlacementExport
        {
            Coordinate = up.Coordinate,
            Direction = up.Direction,
            Sequence = up.Sequence,
            Unknown = up.Unknown,
            TransportShip = up.TransportShip
        };
    }

    private static CapitalExport ExportCapital(Capital c)
    {
        return new CapitalExport { Coordinate = c.Coordinate };
    }

    private static StrategicConstructionExport ExportStrategicConstruction(StrategicConstruction sc)
    {
        return new StrategicConstructionExport
        {
            LegionId = sc.LegionId,
            Reserved1 = sc.Reserved1,
            ExecuteRound = sc.ExecuteRound,
            ConstructionCode = sc.ConstructionCode
        };
    }

    private static AirSupportExport ExportAirSupport(AirSupport support)
    {
        return new AirSupportExport
        {
            AirForceSequence = support.AirForceSequence,
            AmmoType = support.AmmoType,
            OwnerLegion = support.OwnerLegion,
            TriggerRound = support.TriggerRound
        };
    }

    #endregion

    #region Export Data Models

    private class StageExportData
    {
        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = "";

        [JsonPropertyName("header")]
        public HeaderExport Header { get; set; } = null!;

        [JsonPropertyName("legions")]
        public List<LegionSummaryExport> Legions { get; set; } = new();

        [JsonPropertyName("buildings")]
        public List<BuildingSummaryExport> Buildings { get; set; } = new();

        [JsonPropertyName("armies")]
        public List<ArmySummaryExport> Armies { get; set; } = new();

        [JsonPropertyName("armies_v3")]
        public List<ArmyV3Export>? ArmiesV3 { get; set; }

        [JsonPropertyName("terrains")]
        public List<TerrainExport>? Terrains { get; set; }

        [JsonPropertyName("provinces")]
        public List<ProvinceExport>? Provinces { get; set; }

        [JsonPropertyName("belongs")]
        public List<string>? Belongs { get; set; }

        [JsonPropertyName("traps")]
        public List<TrapExport>? Traps { get; set; }

        [JsonPropertyName("cases")]
        public List<MapCaseExport>? Cases { get; set; }

        [JsonPropertyName("weathers")]
        public List<WeatherExport>? Weathers { get; set; }

        [JsonPropertyName("events")]
        public List<MapEventExport>? Events { get; set; }

        [JsonPropertyName("reinforcements")]
        public List<ReinforcementExport>? Reinforcements { get; set; }

        [JsonPropertyName("reinforcements_v3")]
        public List<ReinforcementV3Export>? ReinforcementsV3 { get; set; }

        [JsonPropertyName("air_forces")]
        public List<AirForceExport>? AirForces { get; set; }

        [JsonPropertyName("unit_placements")]
        public List<UnitPlacementExport>? UnitPlacements { get; set; }

        [JsonPropertyName("capitals")]
        public List<CapitalExport>? Capitals { get; set; }

        [JsonPropertyName("strategy_constructions")]
        public List<StrategicConstructionExport>? StrategyConstructions { get; set; }

        [JsonPropertyName("air_supports")]
        public List<AirSupportExport>? AirSupports { get; set; }
    }

    private class ConquestExportData
    {
        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = "";

        [JsonPropertyName("header")]
        public HeaderExport Header { get; set; } = null!;

        [JsonPropertyName("legions")]
        public List<LegionSummaryExport> Legions { get; set; } = new();

        [JsonPropertyName("buildings")]
        public List<BuildingSummaryExport> Buildings { get; set; } = new();

        [JsonPropertyName("armies")]
        public List<ArmySummaryExport> Armies { get; set; } = new();

        [JsonPropertyName("armies_v3")]
        public List<ArmyV3Export>? ArmiesV3 { get; set; }

        [JsonPropertyName("provinces")]
        public List<ProvinceExport>? Provinces { get; set; }

        [JsonPropertyName("belongs")]
        public List<string>? Belongs { get; set; }

        [JsonPropertyName("traps")]
        public List<TrapExport>? Traps { get; set; }

        [JsonPropertyName("cases")]
        public List<MapCaseExport>? Cases { get; set; }

        [JsonPropertyName("weathers")]
        public List<WeatherExport>? Weathers { get; set; }

        [JsonPropertyName("events")]
        public List<MapEventExport>? Events { get; set; }

        [JsonPropertyName("reinforcements")]
        public List<ReinforcementExport>? Reinforcements { get; set; }

        [JsonPropertyName("reinforcements_v3")]
        public List<ReinforcementV3Export>? ReinforcementsV3 { get; set; }

        [JsonPropertyName("air_forces")]
        public List<AirForceExport>? AirForces { get; set; }

        [JsonPropertyName("unit_placements")]
        public List<UnitPlacementExport>? UnitPlacements { get; set; }

        [JsonPropertyName("capitals")]
        public List<CapitalExport>? Capitals { get; set; }

        [JsonPropertyName("strategy_constructions")]
        public List<StrategicConstructionExport>? StrategyConstructions { get; set; }

        [JsonPropertyName("air_supports")]
        public List<AirSupportExport>? AirSupports { get; set; }
    }

    private class WorldExportData
    {
        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = "";

        [JsonPropertyName("file_path")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("map_width")]
        public int MapWidth { get; set; }

        [JsonPropertyName("map_height")]
        public int MapHeight { get; set; }

        [JsonPropertyName("header")]
        public HeaderExport Header { get; set; } = null!;

        [JsonPropertyName("buildings")]
        public List<BuildingSummaryExport> Buildings { get; set; } = new();

        [JsonPropertyName("armies")]
        public List<ArmySummaryExport> Armies { get; set; } = new();

        [JsonPropertyName("legions")]
        public List<LegionSummaryExport> Legions { get; set; } = new();

        [JsonPropertyName("terrains")]
        public List<TerrainExport>? Terrains { get; set; }
    }

    private class HeaderExport
    {
        [JsonPropertyName("btl_version")]
        public int BtlVersion { get; set; }

        [JsonPropertyName("map_number")]
        public int MapNumber { get; set; }

        [JsonPropertyName("map_clip_x")]
        public int MapClipX { get; set; }

        [JsonPropertyName("map_clip_y")]
        public int MapClipY { get; set; }

        [JsonPropertyName("map_width")]
        public int MapWidth { get; set; }

        [JsonPropertyName("map_length")]
        public int MapLength { get; set; }

        [JsonPropertyName("army_count")]
        public int ArmyCount { get; set; }

        [JsonPropertyName("building_count")]
        public int BuildingCount { get; set; }

        [JsonPropertyName("troop_count")]
        public int TroopCount { get; set; }

        [JsonPropertyName("plan_count")]
        public int PlanCount { get; set; }

        [JsonPropertyName("event_count")]
        public int EventCount { get; set; }

        [JsonPropertyName("weather_count")]
        public int WeatherCount { get; set; }

        [JsonPropertyName("victory_condition")]
        public int VictoryCondition { get; set; }

        [JsonPropertyName("min_turns")]
        public int MinTurns { get; set; }

        [JsonPropertyName("max_turns")]
        public int MaxTurns { get; set; }

        [JsonPropertyName("reinforcement_count")]
        public int ReinforcementCount { get; set; }

        [JsonPropertyName("air_raid_count")]
        public int AirRaidCount { get; set; }

        [JsonPropertyName("placement_a")]
        public int PlacementA { get; set; }

        [JsonPropertyName("placement_b")]
        public int PlacementB { get; set; }

        [JsonPropertyName("conquered_flag_position")]
        public int ConqueredFlagPosition { get; set; }

        [JsonPropertyName("selectable_tile_count")]
        public int SelectableTileCount { get; set; }

        [JsonPropertyName("trap_count")]
        public int TrapCount { get; set; }

        [JsonPropertyName("strategy_count")]
        public int StrategyCount { get; set; }

        [JsonPropertyName("air_support_count")]
        public int AirSupportCount { get; set; }
    }

    private class LegionSummaryExport
    {
        [JsonPropertyName("action_id")]
        public int ActionId { get; set; }

        [JsonPropertyName("country_id")]
        public int CountryId { get; set; }

        [JsonPropertyName("is_player_controlled")]
        public int IsPlayerControlled { get; set; }

        [JsonPropertyName("camp")]
        public int Camp { get; set; }

        [JsonPropertyName("color_r")]
        public byte ColorR { get; set; }

        [JsonPropertyName("color_g")]
        public byte ColorG { get; set; }

        [JsonPropertyName("color_b")]
        public byte ColorB { get; set; }
    }

    private class BuildingSummaryExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("building_type")]
        public byte BuildingType { get; set; }

        [JsonPropertyName("building_type_name")]
        public string BuildingTypeName { get; set; } = "";

        [JsonPropertyName("key_point")]
        public byte KeyPoint { get; set; }

        [JsonPropertyName("factory_level")]
        public byte FactoryLevel { get; set; }

        [JsonPropertyName("research_level")]
        public byte ResearchLevel { get; set; }

        [JsonPropertyName("medical_level")]
        public byte MedicalLevel { get; set; }
    }

    private class ArmySummaryExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public byte UnitType { get; set; }

        [JsonPropertyName("unit_type_name")]
        public string UnitTypeName { get; set; } = "";

        [JsonPropertyName("level")]
        public byte Level { get; set; }

        [JsonPropertyName("current_health")]
        public short CurrentHealth { get; set; }

        [JsonPropertyName("max_health")]
        public short MaxHealth { get; set; }

        [JsonPropertyName("general")]
        public short General { get; set; }

        /// <summary>所在格子的归属值（军团列表下标，0xFF = 无归属）。</summary>
        [JsonPropertyName("belong")]
        public int Belong { get; set; }
    }

    private class ArmyV3Export
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public byte UnitType { get; set; }

        [JsonPropertyName("level")]
        public byte Level { get; set; }

        [JsonPropertyName("current_health")]
        public short CurrentHealth { get; set; }

        [JsonPropertyName("max_health")]
        public short MaxHealth { get; set; }

        [JsonPropertyName("general")]
        public short General { get; set; }

        /// <summary>所在格子的归属值（军团列表下标，0xFF = 无归属）。</summary>
        [JsonPropertyName("belong")]
        public int Belong { get; set; }
    }

    private class TerrainExport
    {
        [JsonPropertyName("index")]
        public int? Index { get; set; }

        [JsonPropertyName("col")]
        public int? Col { get; set; }

        [JsonPropertyName("row")]
        public int? Row { get; set; }

        [JsonPropertyName("tile_type_1")]
        public byte TileType1 { get; set; }

        [JsonPropertyName("decoration_type_1")]
        public byte DecorationType1 { get; set; }

        [JsonPropertyName("texture_offset_x1")]
        public byte TextureOffsetX1 { get; set; }

        [JsonPropertyName("texture_offset_y1")]
        public byte TextureOffsetY1 { get; set; }

        [JsonPropertyName("tile_type_2")]
        public byte TileType2 { get; set; }

        [JsonPropertyName("decoration_type_2")]
        public byte DecorationType2 { get; set; }

        [JsonPropertyName("texture_offset_x2")]
        public byte TextureOffsetX2 { get; set; }

        [JsonPropertyName("texture_offset_y2")]
        public byte TextureOffsetY2 { get; set; }

        [JsonPropertyName("tile_type_3")]
        public byte TileType3 { get; set; }

        [JsonPropertyName("decoration_type_3")]
        public byte DecorationType3 { get; set; }

        [JsonPropertyName("texture_offset_x3")]
        public byte TextureOffsetX3 { get; set; }

        [JsonPropertyName("texture_offset_y3")]
        public byte TextureOffsetY3 { get; set; }

        [JsonPropertyName("river_value")]
        public byte RiverValue { get; set; }

        [JsonPropertyName("province_value")]
        public int? ProvinceValue { get; set; }
    }

    /// <summary>省区值（16 位，= 省会格子的索引，0xFFFF = 无省区）。</summary>
    private class ProvinceExport
    {
        [JsonPropertyName("province_value")]
        public int ProvinceValue { get; set; }
    }

    /// <summary>陷阱：LegionId 存的是军团的 ActionId（不是归属值），Organization = 陷阱等级。</summary>
    private class TrapExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }

        [JsonPropertyName("organization")]
        public int Organization { get; set; }

        [JsonPropertyName("health")]
        public int Health { get; set; }

        [JsonPropertyName("extra1")]
        public int Extra1 { get; set; }

        [JsonPropertyName("extra2")]
        public int Extra2 { get; set; }
    }

    /// <summary>方案（16 字节，四个 int32 原样导出）：方针编号 / 方针类型 / 触发回合 / 目标值（类型 2 时为地块序号）。</summary>
    private class MapCaseExport
    {
        [JsonPropertyName("policy_number")]
        public int PolicyNumber { get; set; }

        [JsonPropertyName("case_type")]
        public int CaseType { get; set; }

        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }

        [JsonPropertyName("target_value")]
        public int TargetValue { get; set; }
    }

    /// <summary>天气：从触发回合起持续 Duration 回合。</summary>
    private class WeatherExport
    {
        [JsonPropertyName("weather_type")]
        public int WeatherType { get; set; }

        [JsonPropertyName("reserved1")]
        public int Reserved1 { get; set; }

        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }
    }

    /// <summary>事件（44 字节）：字段名与 MapEvent 一致，触发 / 受影响军团是军团 ActionId。</summary>
    private class MapEventExport
    {
        [JsonPropertyName("sequence")]
        public int Sequence { get; set; }

        [JsonPropertyName("related_event")]
        public int RelatedEvent { get; set; }

        [JsonPropertyName("trigger_condition")]
        public int TriggerCondition { get; set; }

        [JsonPropertyName("trigger_event")]
        public int TriggerEvent { get; set; }

        [JsonPropertyName("trigger_legion")]
        public int TriggerLegion { get; set; }

        [JsonPropertyName("affected_legion")]
        public int AffectedLegion { get; set; }

        [JsonPropertyName("camp_change")]
        public int CampChange { get; set; }

        [JsonPropertyName("reserved")]
        public int Reserved { get; set; }

        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }

        [JsonPropertyName("dialogue_code")]
        public int DialogueCode { get; set; }

        [JsonPropertyName("end_segment")]
        public int EndSegment { get; set; }
    }

    /// <summary>援军 v1/v2（80 字节）：OwnerCountry 存的是军团 ActionId。</summary>
    private class ReinforcementExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public int UnitType { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("organization")]
        public int Organization { get; set; }

        [JsonPropertyName("direction")]
        public int Direction { get; set; }

        [JsonPropertyName("reserved14")]
        public int Reserved14 { get; set; }

        [JsonPropertyName("reserved18")]
        public int Reserved18 { get; set; }

        [JsonPropertyName("general")]
        public int General { get; set; }

        [JsonPropertyName("rank")]
        public int Rank { get; set; }

        [JsonPropertyName("quality")]
        public int Quality { get; set; }

        [JsonPropertyName("skill1_level")]
        public int Skill1Level { get; set; }

        [JsonPropertyName("skill2_level")]
        public int Skill2Level { get; set; }

        [JsonPropertyName("skill3_level")]
        public int Skill3Level { get; set; }

        [JsonPropertyName("skill4_level")]
        public int Skill4Level { get; set; }

        [JsonPropertyName("skill5_level")]
        public int Skill5Level { get; set; }

        [JsonPropertyName("badge1")]
        public int Badge1 { get; set; }

        [JsonPropertyName("badge2")]
        public int Badge2 { get; set; }

        [JsonPropertyName("badge3")]
        public int Badge3 { get; set; }

        [JsonPropertyName("owner_country")]
        public int OwnerCountry { get; set; }

        [JsonPropertyName("spawn_round")]
        public int SpawnRound { get; set; }
    }

    /// <summary>援军 v3（104 字节）：OwnerLegion 存的是军团 ActionId。</summary>
    private class ReinforcementV3Export
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public int UnitType { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("organization")]
        public int Organization { get; set; }

        [JsonPropertyName("behavior_mode")]
        public int BehaviorMode { get; set; }

        [JsonPropertyName("direction")]
        public int Direction { get; set; }

        [JsonPropertyName("unknown")]
        public int Unknown { get; set; }

        [JsonPropertyName("general")]
        public int General { get; set; }

        [JsonPropertyName("rank")]
        public int Rank { get; set; }

        [JsonPropertyName("hp_level")]
        public int HpLevel { get; set; }

        [JsonPropertyName("skill1_level")]
        public int Skill1Level { get; set; }

        [JsonPropertyName("skill2_level")]
        public int Skill2Level { get; set; }

        [JsonPropertyName("skill3_level")]
        public int Skill3Level { get; set; }

        [JsonPropertyName("skill4_level")]
        public int Skill4Level { get; set; }

        [JsonPropertyName("skill5_level")]
        public int Skill5Level { get; set; }

        [JsonPropertyName("waste1")]
        public int Waste1 { get; set; }

        [JsonPropertyName("waste2")]
        public int Waste2 { get; set; }

        [JsonPropertyName("waste3")]
        public int Waste3 { get; set; }

        [JsonPropertyName("owner_legion")]
        public int OwnerLegion { get; set; }

        [JsonPropertyName("spawn_round")]
        public int SpawnRound { get; set; }

        [JsonPropertyName("medal1")]
        public int Medal1 { get; set; }

        [JsonPropertyName("medal2")]
        public int Medal2 { get; set; }

        [JsonPropertyName("medal3")]
        public int Medal3 { get; set; }

        [JsonPropertyName("ribbon1")]
        public int Ribbon1 { get; set; }

        [JsonPropertyName("ribbon2")]
        public int Ribbon2 { get; set; }

        [JsonPropertyName("ribbon3")]
        public int Ribbon3 { get; set; }
    }

    /// <summary>空袭：Coordinate 是机场格索引，OwnerLegion 是军团 ActionId。</summary>
    private class AirForceExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public int UnitType { get; set; }

        [JsonPropertyName("ammo_type")]
        public int AmmoType { get; set; }

        [JsonPropertyName("owner_legion")]
        public int OwnerLegion { get; set; }

        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }
    }

    /// <summary>单位部署（8 字节）。</summary>
    private class UnitPlacementExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }

        [JsonPropertyName("direction")]
        public byte Direction { get; set; }

        [JsonPropertyName("sequence")]
        public byte Sequence { get; set; }

        [JsonPropertyName("unknown")]
        public byte Unknown { get; set; }

        [JsonPropertyName("transport_ship")]
        public byte TransportShip { get; set; }
    }

    private class CapitalExport
    {
        [JsonPropertyName("coordinate")]
        public int Coordinate { get; set; }
    }

    /// <summary>战略建设（16 字节）：LegionId 是军团 ActionId，记录里没有坐标。</summary>
    private class StrategicConstructionExport
    {
        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }

        [JsonPropertyName("reserved1")]
        public int Reserved1 { get; set; }

        [JsonPropertyName("execute_round")]
        public int ExecuteRound { get; set; }

        [JsonPropertyName("construction_code")]
        public int ConstructionCode { get; set; }
    }

    /// <summary>空中支援：AirForceSequence 是兵种编号，OwnerLegion 是军团 ActionId，记录里没有坐标。</summary>
    private class AirSupportExport
    {
        [JsonPropertyName("air_force_sequence")]
        public int AirForceSequence { get; set; }

        [JsonPropertyName("ammo_type")]
        public int AmmoType { get; set; }

        [JsonPropertyName("owner_legion")]
        public int OwnerLegion { get; set; }

        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }
    }

    #endregion

    private static void AssetScan(string? path, bool force)
    {
        try
        {
            var manager = AssetManager.Default;
            int count;
            if (string.IsNullOrWhiteSpace(path))
                count = manager.ScanDefault(force);
            else
                count = manager.Scan(path, force);

            Console.WriteLine($"扫描完成: {count} 个文件");
            Console.WriteLine($"根目录: {manager.AssetsRoot}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void AssetSummary()
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
                manager.ScanDefault();

            manager.PrintSummary();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void AssetList(AssetKind? kind, string? ext, string? dir, string? name, int limit)
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
                manager.ScanDefault();

            var entries = manager.Query(kind, ext, dir, name);
            Console.WriteLine($"匹配: {entries.Count} 条");
            Console.WriteLine();

            int shown = 0;
            foreach (var entry in entries)
            {
                if (shown >= limit) break;
                Console.WriteLine($"  {entry.RelativePath,-60} {entry.Size,8} bytes  [{entry.Kind}]");
                shown++;
            }
            if (entries.Count > limit)
                Console.WriteLine($"  ... 还有 {entries.Count - limit} 条（使用 --limit 调整）");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void AssetQuery(AssetKind? kind, string? ext, string? dir, string? name, string format)
    {
        try
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
                manager.ScanDefault();

            var entries = manager.Query(kind, ext, dir, name);

            switch (format.ToLowerInvariant())
            {
                case "paths":
                    foreach (var e in entries)
                        Console.WriteLine(e.RelativePath);
                    break;
                case "json":
                    var jsonEntries = entries.Select(e => new
                    {
                        path = e.RelativePath,
                        name = e.FileName,
                        ext = e.Extension,
                        size = e.Size,
                        kind = e.Kind.ToString(),
                        dir = e.TopDirectory
                    });
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonEntries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    break;
                default:
                    Console.WriteLine($"匹配: {entries.Count} 条");
                    Console.WriteLine();
                    Console.WriteLine($"{"Relative Path",-55} {"Size",10} {"Kind"}");
                    Console.WriteLine(new string('-', 80));
                    foreach (var e in entries)
                        Console.WriteLine($"{e.RelativePath,-55} {e.Size,10} {e.Kind}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void Screenshot(string input, string output, int zoom)
    {
        try
        {
            var mapData = WorldParser.LoadFromFile(input);
            if (mapData == null)
            {
                Console.Error.WriteLine($"无法加载地图文件: {input}");
                Environment.ExitCode = 1;
                return;
            }

            using var helper = new ScreenshotHelper();
            bool success = helper.CaptureMap(mapData, output, zoom);
            if (success)
                Console.WriteLine($"截图已保存: {output}");
            else
            {
                Console.Error.WriteLine("截图生成失败");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static Command BuildRunCommand()
    {
        var cmd = new Command("run", "按 .zme 指令脚本修改地图（复用 GUI 控制台的同一套命令树）");

        var fileArg = new Argument<string>("file") { Description = "地图文件（.btl 战役/征服）" };
        var scriptArg = new Argument<string>("script") { Description = ".zme 指令脚本路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "输出文件路径（默认覆盖原文件）" };
        var typeOpt = new Option<string?>("--type") { Description = "地图类型：stage / conquest（默认按文件名推断）" };

        cmd.Arguments.Add(fileArg);
        cmd.Arguments.Add(scriptArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(typeOpt);

        cmd.SetAction(parseResult => RunScript(
            parseResult.GetValue(fileArg)!,
            parseResult.GetValue(scriptArg)!,
            parseResult.GetValue(outputOpt),
            parseResult.GetValue(typeOpt)));

        return cmd;
    }

    /// <summary>
    /// 无界面执行 .zme 指令脚本。
    /// <para>
    /// 命令解析与 GUI 控制台是同一套（<see cref="CliCommandHost"/>），
    /// 这里只负责准备状态：<see cref="CommandManager"/> 的上下文（命令从这里取 MapData）
    /// 与 <see cref="EditModeManager"/> 的初始化（修改器依赖它）。
    /// </para>
    /// </summary>
    private static void RunScript(string filePath, string scriptPath, string? outputPath, string? type)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"文件不存在: {filePath}");
                Environment.ExitCode = 1;
                return;
            }

            if (!File.Exists(scriptPath))
            {
                Console.Error.WriteLine($"脚本不存在: {scriptPath}");
                Environment.ExitCode = 1;
                return;
            }

            string fileType = string.IsNullOrWhiteSpace(type) ? InferMapType(filePath) : type.Trim().ToLowerInvariant();
            if (fileType is not ("stage" or "conquest"))
            {
                Console.Error.WriteLine($"不支持的地图类型: {fileType}（仅支持 stage / conquest）");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"加载文件: {filePath}");
            var mapData = PrepareMap(filePath, fileType);
            if (mapData == null)
            {
                Console.Error.WriteLine("文件加载失败");
                Environment.ExitCode = 1;
                return;
            }

            var host = CliCommandHost.Instance;
            host.SetOutput(Console.Out);
            host.BeginBatchMode();

            int executed = 0, skipped = 0, failed = 0;
            try
            {
                var lines = File.ReadAllLines(scriptPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = CommandLineText.StripComment(lines[i].Trim());
                    if (string.IsNullOrEmpty(line) || CommandLineText.IsCommentLine(line))
                    {
                        skipped++;
                        continue;
                    }

                    Console.WriteLine($"[{i + 1}] {line}");
                    if (host.Execute(line) != 0) failed++;
                    executed++;
                }
            }
            finally
            {
                host.EndBatchMode();
            }

            string targetPath = string.IsNullOrWhiteSpace(outputPath) ? filePath : outputPath;
            bool success = SaveMapData(mapData, fileType, targetPath);

            if (!success)
            {
                Console.Error.WriteLine("保存失败");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"已保存: {targetPath}（执行 {executed} 条，跳过 {skipped} 条，失败 {failed} 条）");
            if (failed > 0) Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    /// <summary>按文件名推断地图类型（conquest 开头视为征服，其余按战役处理）</summary>
    private static string InferMapType(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        return name.StartsWith("conquest", StringComparison.Ordinal) ? "conquest" : "stage";
    }

    #region Modify Commands

    /// <summary>
    /// modify：把「地图文件 + 一条控制台命令」组合成可批量执行的入口。
    /// <para>
    /// 命令体原样透传给 <see cref="CliCommandHost"/>，这里**不重复定义命令树**，
    /// 因此 CLI 与 GUI 控制台的命令规则天然是同一套（改一处两边同时生效）。
    /// 两者的关系只是"共用 Core 里的实现"：GUI 不引用 Cli 项目，Cli 也不引用 GUI。
    /// </para>
    /// </summary>
    private static Command BuildModifyCommand()
    {
        var cmd = new Command("modify", """
            修改地图数据（命令体与 GUI 控制台完全同一套规则）
            用法: modify <stage|conquest> <文件> [-o 输出] [--no-save] <控制台命令...>
            示例: modify stage map.btl terrain greening -p 60
            """)
        {
            // 地图类型、文件和控制台命令整体作为未匹配 token 交给我们自己拆，
            // 这样命令体（含它自己的选项，如 -p 60）能原样保留顺序。
            TreatUnmatchedTokensAsErrors = false
        };

        cmd.SetAction(parseResult => RunModify(parseResult.UnmatchedTokens.ToArray()));
        return cmd;
    }

    /// <summary>拆 modify 的参数：类型、文件、-o / --no-save，其余原样作为命令体</summary>
    private static void RunModify(string[] args)
    {
        if (args.Length == 0)
        {
            PrintModifyHelp();
            Environment.ExitCode = 1;
            return;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            PrintModifyHelp();
            return;
        }

        string fileType = args[0].ToLowerInvariant();
        if (fileType is not ("stage" or "conquest"))
        {
            Console.Error.WriteLine($"未知地图类型: {args[0]}（仅支持 stage / conquest）");
            Environment.ExitCode = 1;
            return;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("缺少地图文件路径");
            Environment.ExitCode = 1;
            return;
        }

        string filePath = args[1];
        string? outputPath = null;
        bool noSave = false;

        int i = 2;
        while (i < args.Length && args[i].StartsWith('-'))
        {
            string a = args[i];

            if (a is "-o" or "--output")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("缺少输出文件路径");
                    Environment.ExitCode = 1;
                    return;
                }

                outputPath = args[i + 1];
                i += 2;
                continue;
            }

            if (a.StartsWith("--output=", StringComparison.Ordinal))
            {
                outputPath = a["--output=".Length..];
                i++;
                continue;
            }

            if (a == "--no-save")
            {
                noSave = true;
                i++;
                continue;
            }

            if (a is "-h" or "--help")
            {
                PrintModifyHelp();
                return;
            }

            // 其它以 - 开头的是控制台命令自己的选项（如 -p 60），命令体从这里开始
            break;
        }

        if (i >= args.Length)
        {
            Console.Error.WriteLine("缺少要执行的命令，例如: modify stage map.btl terrain greening -p 60");
            Environment.ExitCode = 1;
            return;
        }

        string commandLine = JoinCommand(args[i..]);
        Console.WriteLine($"命令: {commandLine}");

        ExecuteWithHost(filePath, fileType, commandLine, outputPath, noSave);
    }

    /// <summary>把参数拼回一行命令；含空格的参数补引号，避免被再次拆分</summary>
    private static string JoinCommand(IEnumerable<string> tokens)
        => string.Join(" ", tokens.Select(t => t.Contains(' ') || t.Contains('\t') ? $"\"{t}\"" : t));

    private static void PrintModifyHelp()
    {
        Console.WriteLine("""
            用法:
              wc4me modify <stage|conquest> <文件> [选项] <控制台命令...>

            选项:
              -o, --output <路径>  输出文件（默认覆盖原文件）
              --no-save            只执行不写回（info / check / inspect / compare / convert 默认就不写回）
              -h, --help           显示本帮助

            说明:
              控制台命令与 GUI 的 F3 调试控制台完全同一套规则：terrain / province /
              building / belong / legion / army / trap / info / check / fix 等。
              命令体内的选项（如 -p 60）保持原始顺序，直接透传给命令解析器。

            示例:
              wc4me modify stage map.btl terrain greening -p 60
              wc4me modify stage map.btl -o out.btl terrain change-type -t 5
              wc4me modify conquest c.btl info
              wc4me modify stage map.btl --no-save check
            """);
    }

    /// <summary>加载地图并准备命令执行所需的上下文（Modifier + CommandManager）</summary>
    private static MapData? PrepareMap(string filePath, string fileType)
    {
        var mapData = LoadMapData(filePath, fileType);
        if (mapData == null) return null;

        // 控制台命令从上下文取 MapData，修改器依赖 EditModeManager 初始化
        EditModeManager.Instance.Initialize(mapData);
        CommandManager.Instance.SetContext(new CommandContext { MapData = mapData });
        return mapData;
    }

    private static MapData? LoadMapData(string filePath, string fileType)
        => fileType == "stage" ? StageParser.LoadToMapData(filePath) : ConquestParser.LoadToMapData(filePath);

    private static bool SaveMapData(MapData mapData, string fileType, string targetPath)
        => fileType == "stage"
            ? StageParser.SaveFromMapData(mapData, targetPath)
            : ConquestParser.SaveFromMapData(mapData, targetPath);

    /// <summary>只读命令：执行完不需要写回文件（inspect / compare / convert 只读文件，convert 只写 -o 指定的新文件）</summary>
    private static bool IsReadOnlyCommand(string commandLine)
    {
        string first = commandLine.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first is "info" or "check" or "help" or "inspect" or "compare" or "convert";
    }

    /// <summary>加载 → 执行一条控制台命令 → 按需写回</summary>
    private static void ExecuteWithHost(string filePath, string fileType, string commandLine, string? outputPath, bool noSave)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"文件不存在: {filePath}");
                Environment.ExitCode = 1;
                return;
            }

            var mapData = PrepareMap(filePath, fileType);
            if (mapData == null)
            {
                Console.Error.WriteLine("文件加载失败");
                Environment.ExitCode = 1;
                return;
            }

            var host = CliCommandHost.Instance;
            host.SetOutput(Console.Out);
            host.BeginBatchMode();

            int exitCode;
            try
            {
                exitCode = host.Execute(commandLine);
            }
            finally
            {
                host.EndBatchMode();
            }

            if (exitCode != 0) Environment.ExitCode = 1;

            // info / check / inspect 这类只读命令不写回，避免无意义的往返覆盖
            if (noSave || IsReadOnlyCommand(commandLine)) return;

            string targetPath = string.IsNullOrWhiteSpace(outputPath) ? filePath : outputPath;
            if (SaveMapData(mapData, fileType, targetPath))
                Console.WriteLine($"已保存: {targetPath}");
            else
            {
                Console.Error.WriteLine("保存失败");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    #endregion
}