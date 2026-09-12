using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;
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
        root.Subcommands.Add(BuildModifyCommand());

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

    #region Modify Commands

    private static Command BuildModifyCommand()
    {
        var cmd = new Command("modify", "修改地图数据");

        cmd.Subcommands.Add(BuildModifyStageCommand());
        cmd.Subcommands.Add(BuildModifyConquestCommand());

        return cmd;
    }

    private static Command BuildModifyStageCommand()
    {
        var cmd = new Command("stage", "修改战役文件");

        var fileArg = new Argument<string>("file") { Description = "战役BTL文件路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "输出文件路径（默认覆盖原文件）" };
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);

        cmd.Subcommands.Add(BuildModifyTerrainCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyProvinceCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyBuildingCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyBelongCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyLegionCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyArmyCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyTrapCommand(fileArg, "stage"));
        cmd.Subcommands.Add(BuildModifyInfoCommand(fileArg, "stage"));

        return cmd;
    }

    private static Command BuildModifyConquestCommand()
    {
        var cmd = new Command("conquest", "修改征服文件");

        var fileArg = new Argument<string>("file") { Description = "征服BTL文件路径" };
        var outputOpt = new Option<string?>("--output", "-o") { Description = "输出文件路径（默认覆盖原文件）" };
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);

        cmd.Subcommands.Add(BuildModifyTerrainCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyProvinceCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyBuildingCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyBelongCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyLegionCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyArmyCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyTrapCommand(fileArg, "conquest"));
        cmd.Subcommands.Add(BuildModifyInfoCommand(fileArg, "conquest"));

        return cmd;
    }

    private static Command BuildModifyTerrainCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("terrain", "地形修改操作");

        var greeningCmd = new Command("greening", "绿化平地");
        var greeningProbOpt = new Option<int>("--probability", "-p") { Description = "绿化概率(0-100)", DefaultValueFactory = _ => 50 };
        greeningCmd.Options.Add(greeningProbOpt);
        greeningCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(greeningProbOpt), 0, 100);
                var result = terrain.ApplyGreening(prob);
                Console.WriteLine(result.Message ?? $"绿化完成，概率={prob}%");
            });
        });

        var randomFlatCmd = new Command("random-flat", "随机平地变体");
        var randomFlatProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        randomFlatCmd.Options.Add(randomFlatProbOpt);
        randomFlatCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(randomFlatProbOpt), 0, 100);
                var result = terrain.RandomizeFlatTerrain(prob);
                Console.WriteLine(result.Message ?? $"随机平地变体完成，概率={prob}%");
            });
        });

        var randomVariantCmd = new Command("random-variant", "随机当前层变体");
        var randomVariantProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        var randomVariantLayerOpt = new Option<int>("--layer", "-l") { Description = "编辑层(1-3)", DefaultValueFactory = _ => 1 };
        randomVariantCmd.Options.Add(randomVariantProbOpt);
        randomVariantCmd.Options.Add(randomVariantLayerOpt);
        randomVariantCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(randomVariantLayerOpt), 1, 3);
                int prob = Math.Clamp(parseResult.GetValue(randomVariantProbOpt), 0, 100);
                var result = terrain.RandomizeVariant(prob);
                Console.WriteLine(result.Message ?? $"随机变体完成，编辑层={terrain.EditLayer}，概率={prob}%");
            });
        });

        var createCoastCmd = new Command("create-coast", "创建海岸线");
        createCoastCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                var result = terrain.CreateCoast();
                Console.WriteLine(result.Message ?? "海岸线创建完成");
            });
        });

        var processOceanCmd = new Command("process-ocean-layer2", "处理海洋第二层");
        processOceanCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                var result = terrain.ProcessOceanSecondLayer();
                Console.WriteLine(result.Message ?? "海洋第二层处理完成");
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
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                var result = terrain.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg), (byte)parseResult.GetValue(fillTypeArg));
                Console.WriteLine(result.Message ?? "洪水填充完成");
            });
        });

        var exportHdCmd = new Command("export-hd", "导出高清地形文件");
        exportHdCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var terrain = modifiers.GetModifier<TerrainModifier>()!;
                var result = terrain.ExportHdFile();
                Console.WriteLine(result.Message ?? "高清地形文件导出完成");
            });
        });

        var recognizeCmd = new Command("recognize", "识别地形（需图像提供者）");
        recognizeCmd.SetAction(parseResult =>
        {
            Console.WriteLine("地形识别需要图像提供者，在CLI模式下不可用");
        });

        cmd.Subcommands.Add(greeningCmd);
        cmd.Subcommands.Add(randomFlatCmd);
        cmd.Subcommands.Add(randomVariantCmd);
        cmd.Subcommands.Add(createCoastCmd);
        cmd.Subcommands.Add(processOceanCmd);
        cmd.Subcommands.Add(floodFillCmd);
        cmd.Subcommands.Add(exportHdCmd);
        cmd.Subcommands.Add(recognizeCmd);

        return cmd;
    }

    private static Command BuildModifyProvinceCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("province", "省份修改操作");

        var clearCmd = new Command("clear", "清空所有省份");
        clearCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var province = modifiers.GetModifier<ProvinceModifier>()!;
                var result = province.ClearAllProvinces();
                Console.WriteLine(result.Message ?? "所有省份已清空");
            });
        });

        var generateCmd = new Command("generate", "生成孤立省会省区");
        generateCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var province = modifiers.GetModifier<ProvinceModifier>()!;
                var result = province.GenerateProvincesForIsolatedCapitals();
                Console.WriteLine(result.Message ?? "孤立省会省区生成完成");
            });
        });

        var expandCmd = new Command("expand", "扩展省区填满地图");
        expandCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var province = modifiers.GetModifier<ProvinceModifier>()!;
                var result = province.ExpandAllProvincesToFillMap();
                Console.WriteLine(result.Message ?? "省区扩展完成");
            });
        });

        var processCmd = new Command("process", "处理孤立和空白省区");
        processCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var province = modifiers.GetModifier<ProvinceModifier>()!;
                var result = province.ProcessIsolatedAndEmptyProvinces();
                Console.WriteLine(result.Message ?? "孤立和空白省区处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充省份");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var province = modifiers.GetModifier<ProvinceModifier>()!;
                var result = province.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg));
                Console.WriteLine(result.Message ?? "省份洪水填充完成");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(expandCmd);
        cmd.Subcommands.Add(processCmd);
        cmd.Subcommands.Add(floodFillCmd);

        return cmd;
    }

    private static Command BuildModifyBuildingCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("building", "建筑修改操作");

        var removeAllCmd = new Command("remove-all", "删除所有建筑");
        removeAllCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                building.RemoveAll();
                Console.WriteLine("所有建筑已删除");
            });
        });

        var removeNonCapitalCmd = new Command("remove-non-capital", "删除非首都建筑");
        removeNonCapitalCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.RemoveNonCapitalBuildings(mapData);
                Console.WriteLine($"已删除 {count} 个非首都建筑");
            });
        });

        var randomizeNamedCmd = new Command("randomize-named", "随机有名称建筑类型");
        randomizeNamedCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeNamedBuildingTypes();
                Console.WriteLine($"已随机 {count} 个有名称建筑");
            });
        });

        var randomizeUnnamedCmd = new Command("randomize-unnamed", "随机无名称建筑类型");
        randomizeUnnamedCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeUnnamedBuildingTypes();
                Console.WriteLine($"已随机 {count} 个无名称建筑");
            });
        });

        var randomizeByBelongCmd = new Command("randomize-by-belong", "按归属随机建筑");
        var belongIdArg = new Argument<int>("belongId") { Description = "归属ID" };
        randomizeByBelongCmd.Arguments.Add(belongIdArg);
        randomizeByBelongCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int belongId = parseResult.GetValue(belongIdArg);
                int count = building.RandomizeBuildingsByBelong(belongId);
                Console.WriteLine($"已按归属{belongId}随机 {count} 个建筑");
            });
        });

        var generateCapitalsCmd = new Command("generate-capitals", "为所有建筑生成首都");
        generateCapitalsCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.GenerateCapitalsForAllBuildings(mapData);
                Console.WriteLine($"已生成 {count} 个首都");
            });
        });

        var randomizeOnCapitalsCmd = new Command("randomize-on-capitals", "在首都随机建筑");
        randomizeOnCapitalsCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeBuildingsOnCapitals(mapData);
                Console.WriteLine($"已在首都随机 {count} 个建筑");
            });
        });

        var smartAppearanceCmd = new Command("smart-appearance", "智能设置建筑外观");
        smartAppearanceCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var building = modifiers.GetModifier<BuildingModifier>()!;
                int count = building.SmartSetBuildingAppearance(mapData);
                Console.WriteLine($"已智能设置 {count} 个建筑外观");
            });
        });

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

    private static Command BuildModifyBelongCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("belong", "归属修改操作");

        var clearCmd = new Command("clear", "清空所有归属");
        clearCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
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
                Console.WriteLine($"已清空 {count} 个格子的归属");
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
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var belong = modifiers.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(setColArg);
                int row = parseResult.GetValue(setRowArg);
                int belongVal = Math.Clamp(parseResult.GetValue(setBelongArg), 0, 255);
                var result = belong.SetBelongByCountryId(col, row, belongVal);
                Console.WriteLine(result.Message ?? $"已设置归属 ({col},{row}) = {belongVal}");
            });
        });

        var fillCmd = new Command("fill-from-legion", "按军团ID填充归属到建筑格子");
        var fillLegionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        fillCmd.Arguments.Add(fillLegionIdArg);
        fillCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                int legionId = parseResult.GetValue(fillLegionIdArg);
                int count = 0;
                foreach (var building in mapData.Buildings)
                {
                    int col = building.Coordinate % mapData.MapWidth;
                    int row = building.Coordinate / mapData.MapWidth;
                    if (mapData.GetBelongValue(col, row) != legionId)
                    {
                        mapData.SetBelongValue(col, row, legionId);
                        count++;
                    }
                }
                Console.WriteLine($"已将 {count} 个建筑格子归属设为军团 {legionId}");
            });
        });

        var statsCmd = new Command("stats", "归属统计");
        statsCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
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
                Console.WriteLine("归属值分布:");
                foreach (var kv in belongCounts.OrderBy(x => x.Key))
                {
                    string name = "";
                    int legionIdx = mapData.FindLegionIndex(kv.Key);
                    if (legionIdx >= 0) name = $" ({mapData.Legions[legionIdx].DisplayName})";
                    Console.WriteLine($"  归属={kv.Key}{name}: {kv.Value} 格");
                }
                int emptyCount = mapData.Belongs.Count - belongCounts.Values.Sum();
                Console.WriteLine($"  无归属(0xFF): {emptyCount} 格");
            }, save: false);
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(statsCmd);

        return cmd;
    }

    private static Command BuildModifyLegionCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("legion", "军团修改操作");

        var listCmd = new Command("list", "列出所有军团");
        listCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var legions = mapData.Legions;
                Console.WriteLine($"军团总数: {legions.Count}");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-4} {"名称",-20} {"颜色",-12} {"玩家控制",-10}");
                Console.WriteLine(new string('-', 50));
                foreach (var legion in legions)
                {
                    Console.WriteLine($"{legion.CountryId,-4} {legion.DisplayName,-20} ({legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2})     {(legion.IsPlayerControlled == 1 ? "是" : "否"),-10}");
                }
            });
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
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var legion = modifiers.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setColorIdArg);
                byte r = (byte)Math.Clamp(parseResult.GetValue(setColorRArg), 0, 255);
                byte g = (byte)Math.Clamp(parseResult.GetValue(setColorGArg), 0, 255);
                byte b = (byte)Math.Clamp(parseResult.GetValue(setColorBArg), 0, 255);
                var result = legion.SetLegionColor(legionId, r, g, b);
                Console.WriteLine(result.Message ?? $"已设置军团 {legionId} 颜色为 ({r},{g},{b})");
            });
        });

        var setActionCmd = new Command("set-action", "设置军团ActionId");
        var setActionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setActionValArg = new Argument<int>("actionId") { Description = "ActionId" };
        setActionCmd.Arguments.Add(setActionIdArg);
        setActionCmd.Arguments.Add(setActionValArg);
        setActionCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var legion = modifiers.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setActionIdArg);
                int actionId = parseResult.GetValue(setActionValArg);
                var result = legion.SetLegionActionId(legionId, actionId);
                Console.WriteLine(result.Message ?? $"已设置军团 {legionId} ActionId={actionId}");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(setColorCmd);
        cmd.Subcommands.Add(setActionCmd);

        return cmd;
    }

    private static Command BuildModifyArmyCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("army", "部队修改操作");

        var listCmd = new Command("list", "列出所有部队");
        listCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var armies = mapData.Armies;
                Console.WriteLine($"部队总数: {armies.Count}");
                if (armies.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {"军团",-6}");
                    Console.WriteLine(new string('-', 30));
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var army = armies[i];
                        int col = army.Coordinate % mapData.MapWidth;
                        int row = army.Coordinate / mapData.MapWidth;
                        Console.WriteLine($"{i,-6} {army.UnitType,-6} ({col},{row})    {army.LegionId,-6}");
                    }
                }
            }, save: false);
        });

        var removeAllCmd = new Command("remove-all", "删除所有部队");
        removeAllCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                int count = mapData.Armies.Count;
                mapData.Armies.Clear();
                Console.WriteLine($"已删除 {count} 个部队");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

    private static Command BuildModifyTrapCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("trap", "陷阱修改操作");

        var listCmd = new Command("list", "列出所有陷阱");
        listCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var traps = mapData.Traps;
                Console.WriteLine($"陷阱总数: {traps.Count}");
                if (traps.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"{"序号",-6} {"坐标",-10} {"军团",-6} {"组织",-6} {"血量",-6}");
                    Console.WriteLine(new string('-', 40));
                    for (int i = 0; i < traps.Count; i++)
                    {
                        var trap = traps[i];
                        int col = trap.Coordinate % mapData.MapWidth;
                        int row = trap.Coordinate / mapData.MapWidth;
                        Console.WriteLine($"{i,-6} ({col},{row})    {trap.LegionId,-6} {trap.Organization,-6} {trap.Health,-6}");
                    }
                }
            }, save: false);
        });

        var removeAllCmd = new Command("remove-all", "删除所有陷阱");
        removeAllCmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                int count = mapData.Traps.Count;
                mapData.Traps.Clear();
                Console.WriteLine($"已删除 {count} 个陷阱");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

    private static Command BuildModifyInfoCommand(Argument<string> fileArg, string fileType)
    {
        var cmd = new Command("info", "显示地图信息");

        cmd.SetAction(parseResult =>
        {
            ExecuteModify(parseResult, fileArg, fileType, (mapData, modifiers) =>
            {
                var header = mapData.Header;
                Console.WriteLine("========== 地图信息 ==========");
                Console.WriteLine($"  地图尺寸:    {header.MapWidth} x {header.MapLength}");
                Console.WriteLine($"  总格子数:    {header.TotalTiles}");
                Console.WriteLine($"  军团数:      {mapData.Legions.Count}");
                Console.WriteLine($"  建筑数:      {mapData.Buildings.Count}");
                Console.WriteLine($"  省份数据:    {mapData.Provinces.Count} 格");
                Console.WriteLine($"  归属数据:    {mapData.Belongs.Count} 格");
                Console.WriteLine($"  地形数据:    {mapData.Terrains.Count} 格");

                int provinceCount = mapData.Provinces.Count(p => p.ProvinceValue != 0);
                int belongCount = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                    if (mapData.GetBelongValueByIndex(i) != 0xFF) belongCount++;
                Console.WriteLine($"  有效省份:    {provinceCount} 格");
                Console.WriteLine($"  有效归属:    {belongCount} 格");
                Console.WriteLine("==============================");
            }, save: false);
        });

        return cmd;
    }

    private static void ExecuteModify(ParseResult parseResult, Argument<string> fileArg, string fileType, Action<MapData, EditModeManager> action, bool save = true)
    {
        try
        {
            string filePath = parseResult.GetValue(fileArg)!;
            DoModify(filePath, fileType, action, save);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void DoModify(string filePath, string fileType, Action<MapData, EditModeManager> action, bool save)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"文件不存在: {filePath}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"加载文件: {filePath}");
        MapData mapData;
        if (fileType == "stage")
            mapData = StageParser.LoadToMapData(filePath);
        else
            mapData = ConquestParser.LoadToMapData(filePath);

        if (mapData == null)
        {
            Console.Error.WriteLine("文件加载失败");
            Environment.ExitCode = 1;
            return;
        }

        var editModeManager = EditModeManager.Instance;
        editModeManager.Initialize(mapData);

        action(mapData, editModeManager);

        if (save)
        {
            bool success;
            if (fileType == "stage")
                success = StageParser.SaveFromMapData(mapData, filePath);
            else
                success = ConquestParser.SaveFromMapData(mapData, filePath);

            if (success)
                Console.WriteLine($"已保存: {filePath}");
            else
            {
                Console.Error.WriteLine("保存失败");
                Environment.ExitCode = 1;
            }
        }
    }

    #endregion
}