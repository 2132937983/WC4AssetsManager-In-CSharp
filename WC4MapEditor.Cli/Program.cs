using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers;
using WC4MapEditor.Parsers.BTL;
using WC4MapEditor.Parsers.Conquest;
using WC4MapEditor.Parsers.Stage;
using WC4MapEditor.Parsers.World;
using WC4MapEditor.Rendering;

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
        var rootCommand = BuildRootCommand();
        return rootCommand.Parse(args).Invoke();
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("WC4 Map Editor - CLI");

        root.Subcommands.Add(BuildStageCommand());
        root.Subcommands.Add(BuildConquestCommand());
        root.Subcommands.Add(BuildWorldCommand());
        root.Subcommands.Add(BuildScreenshotCommand());
        root.Subcommands.Add(BuildAssetCommand());

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
                var playerLegions = legions.Count(l => l.IsPlayerControlled == 1);
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

            if (parser.BtlVersion >= 3 && parser.ArmiesV3.Count > 0)
            {
                Console.WriteLine($"  部队V3:  {parser.ArmiesV3.Count} 个");
            }
            Console.WriteLine("================================\n");

            var result = new StageExportData
            {
                FileType = "Stage",
                Header = ExportHeader(header),
                Legions = legions.Select(ExportLegionSummary).ToList(),
                Buildings = buildings.Select(ExportBuildingSummary).ToList(),
                Armies = armies.Select(ExportArmySummary).ToList()
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

                if (parser.BtlVersion >= 3)
                {
                    result.ArmiesV3 = parser.ArmiesV3.Select(ExportArmyV3).ToList();
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
                var playerLegions = legions.Count(l => l.IsPlayerControlled == 1);
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

            if (parser.BtlVersion >= 3 && parser.ArmiesV3.Count > 0)
            {
                Console.WriteLine($"  部队V3:  {parser.ArmiesV3.Count} 个");
            }
            Console.WriteLine("================================\n");

            var result = new ConquestExportData
            {
                FileType = "Conquest",
                Header = ExportHeader(header),
                Legions = legions.Select(ExportLegionSummary).ToList(),
                Buildings = buildings.Select(ExportBuildingSummary).ToList(),
                Armies = armies.Select(ExportArmySummary).ToList()
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

                if (parser.BtlVersion >= 3)
                {
                    result.ArmiesV3 = parser.ArmiesV3.Select(ExportArmyV3).ToList();
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
                var playerLegions = legions.Count(l => l.IsPlayerControlled == 1);
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
                Armies = armies.Select(ExportArmySummary).ToList(),
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
                        CountryId = province.CountryId
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

    private static ArmySummaryExport ExportArmySummary(Army army)
    {
        return new ArmySummaryExport
        {
            Coordinate = army.Coordinate,
            UnitType = army.UnitType,
            UnitTypeName = army.GetUnitTypeName(),
            Level = army.Level,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            LegionId = army.LegionId
        };
    }

    private static ArmyV3Export ExportArmyV3(Army_3 army)
    {
        return new ArmyV3Export
        {
            Coordinate = army.Coordinate,
            UnitType = army.UnitType,
            Level = army.Level,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            LegionId = army.LegionId
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
        return new ProvinceExport { CountryId = province.CountryId };
    }

    private static TrapExport ExportTrap(Trap trap)
    {
        return new TrapExport
        {
            Coordinate = trap.Coordinate,
            TrapType = (byte)trap.LegionId,
            Round = (byte)trap.Organization
        };
    }

    private static MapCaseExport ExportMapCase(MapCase mapCase)
    {
        return new MapCaseExport
        {
            Coordinate = (short)mapCase.TargetTile,
            CaseType = (byte)mapCase.PolicyNumber,
            TriggerRound = (byte)mapCase.TriggerRound
        };
    }

    private static WeatherExport ExportWeather(Weather weather)
    {
        return new WeatherExport
        {
            WeatherType = weather.WeatherType,
            StartRound = weather.TriggerRound,
            EndRound = weather.Duration
        };
    }

    private static MapEventExport ExportMapEvent(MapEvent mapEvent)
    {
        return new MapEventExport
        {
            EventId = mapEvent.Sequence,
            TriggerType = mapEvent.TriggerCondition,
            TriggerValue = mapEvent.TriggerRound
        };
    }

    private static ReinforcementExport ExportReinforcement(Reinforcement r)
    {
        return new ReinforcementExport
        {
            TriggerRound = r.SpawnRound,
            LegionId = r.OwnerCountry,
            Coordinate = (short)r.Coordinate
        };
    }

    private static ReinforcementV3Export ExportReinforcementV3(Reinforcement_3 r)
    {
        return new ReinforcementV3Export
        {
            TriggerRound = r.SpawnRound,
            LegionId = r.OwnerLegion,
            Coordinate = (short)r.Coordinate
        };
    }

    private static AirForceExport ExportAirForce(AirForce af)
    {
        return new AirForceExport
        {
            Coordinate = (short)af.Coordinate,
            AirForceType = af.UnitType
        };
    }

    private static UnitPlacementExport ExportUnitPlacement(UnitPlacement up)
    {
        return new UnitPlacementExport
        {
            Coordinate = (short)up.Coordinate,
            UnitType = up.Direction
        };
    }

    private static CapitalExport ExportCapital(Capital c)
    {
        return new CapitalExport { Coordinate = (short)c.Coordinate };
    }

    private static StrategicConstructionExport ExportStrategicConstruction(StrategicConstruction sc)
    {
        return new StrategicConstructionExport
        {
            Coordinate = (short)sc.LegionId,
            ConstructionType = (byte)sc.ConstructionCode
        };
    }

    private static AirSupportExport ExportAirSupport(AirSupport as_)
    {
        return new AirSupportExport
        {
            Coordinate = (short)as_.AirForceSequence,
            SupportType = (byte)as_.TriggerRound
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
        public short Coordinate { get; set; }

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
        public short Coordinate { get; set; }

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

        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }
    }

    private class ArmyV3Export
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

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

        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }
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

        [JsonPropertyName("country_id")]
        public byte? CountryId { get; set; }
    }

    private class ProvinceExport
    {
        [JsonPropertyName("country_id")]
        public byte CountryId { get; set; }
    }

    private class TrapExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("trap_type")]
        public byte TrapType { get; set; }

        [JsonPropertyName("round")]
        public byte Round { get; set; }
    }

    private class MapCaseExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("case_type")]
        public byte CaseType { get; set; }

        [JsonPropertyName("trigger_round")]
        public byte TriggerRound { get; set; }
    }

    private class WeatherExport
    {
        [JsonPropertyName("weather_type")]
        public int WeatherType { get; set; }

        [JsonPropertyName("start_round")]
        public int StartRound { get; set; }

        [JsonPropertyName("end_round")]
        public int EndRound { get; set; }
    }

    private class MapEventExport
    {
        [JsonPropertyName("event_id")]
        public int EventId { get; set; }

        [JsonPropertyName("trigger_type")]
        public int TriggerType { get; set; }

        [JsonPropertyName("trigger_value")]
        public int TriggerValue { get; set; }
    }

    private class ReinforcementExport
    {
        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }

        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }

        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }
    }

    private class ReinforcementV3Export
    {
        [JsonPropertyName("trigger_round")]
        public int TriggerRound { get; set; }

        [JsonPropertyName("legion_id")]
        public int LegionId { get; set; }

        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }
    }

    private class AirForceExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("air_force_type")]
        public int AirForceType { get; set; }
    }

    private class UnitPlacementExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("unit_type")]
        public byte UnitType { get; set; }
    }

    private class CapitalExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }
    }

    private class StrategicConstructionExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("construction_type")]
        public byte ConstructionType { get; set; }
    }

    private class AirSupportExport
    {
        [JsonPropertyName("coordinate")]
        public short Coordinate { get; set; }

        [JsonPropertyName("support_type")]
        public byte SupportType { get; set; }
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
        Console.WriteLine($"截图功能尚未实现");
        Console.WriteLine($"输入: {input}");
        Console.WriteLine($"输出: {output}");
        Console.WriteLine($"缩放: {zoom}x");
    }
}