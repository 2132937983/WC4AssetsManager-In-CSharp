using System.CommandLine;
using WC4MapEditor.Core.Geo;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 geo 命令组：把 GeoJSON 栅格化成地图掩码。
// 它不读写 MapData，只产出文本文件，所以不需要 WithModifiers，也不要求先打开地图。
public sealed partial class CliCommandHost
{
    [CliSubcommand(Order = 110)]
    private Command BuildGeoCommand()
    {
        var cmd = new Command("geo", "地理数据工具（GeoJSON → 海陆/归属掩码）");

        var rasterize = new Command("rasterize", "把 GeoJSON 多边形栅格化成 build-map.lua 用的 ASCII 掩码");

        var geojsonOpt = new Option<string>("--geojson")
        {
            Description = "输入 GeoJSON（FeatureCollection / Feature / Polygon / MultiPolygon）",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "输出掩码文件（.txt）",
            Required = true,
        };
        var widthOpt = new Option<int>("--width") { Description = "地图宽度（格）", Required = true };
        var heightOpt = new Option<int>("--height") { Description = "地图高度（格）", Required = true };
        var bboxOpt = new Option<string?>("--bbox")
        {
            Description = "经纬度窗口 minLon,minLat,maxLon,maxLat；省略则用数据自身包围盒",
        };
        var ownersOpt = new Option<bool>("--owners")
        {
            Description = "输出归属掩码（每格一个势力字符）而不是 X/. 海陆掩码",
        };
        var propertyOpt = new Option<string>("--property")
        {
            Description = "归属模式取哪个属性作为势力键（默认自动尝试 id/ISO_A2/name）",
        };
        var idPropertyOpt = new Option<string>("--id-property")
        {
            Description = "势力键不是数字时，用这个属性取国家ID",
        };
        var smoothOpt = new Option<int>("--smooth")
        {
            Description = "海陆模式多数表决次数（0=不平滑，1=去孤立点）",
        };
        var minIslandOpt = new Option<int>("--min-island")
        {
            Description = "小于这个格数的陆块删掉（0=不删）",
        };
        var mapOutOpt = new Option<string?>("--map-out")
        {
            Description = "归属模式的字符映射输出（默认 <out>.map）",
        };

        rasterize.Options.Add(geojsonOpt);
        rasterize.Options.Add(outOpt);
        rasterize.Options.Add(widthOpt);
        rasterize.Options.Add(heightOpt);
        rasterize.Options.Add(bboxOpt);
        rasterize.Options.Add(ownersOpt);
        rasterize.Options.Add(propertyOpt);
        rasterize.Options.Add(idPropertyOpt);
        rasterize.Options.Add(smoothOpt);
        rasterize.Options.Add(minIslandOpt);
        rasterize.Options.Add(mapOutOpt);

        rasterize.SetAction(parseResult =>
        {
            try
            {
                string geojson = Path.GetFullPath(parseResult.GetValue(geojsonOpt)!);
                string outPath = Path.GetFullPath(parseResult.GetValue(outOpt)!);
                string? bboxText = parseResult.GetValue(bboxOpt);

                var options = new GeoRasterOptions
                {
                    GeoJsonPath = geojson,
                    Width = parseResult.GetValue(widthOpt),
                    Height = parseResult.GetValue(heightOpt),
                    Bounds = string.IsNullOrWhiteSpace(bboxText) ? null : GeoBounds.Parse(bboxText),
                    OwnersMode = parseResult.GetValue(ownersOpt),
                    Property = parseResult.GetValue(propertyOpt) ?? "",
                    IdProperty = parseResult.GetValue(idPropertyOpt) ?? "",
                    SmoothPasses = Math.Max(0, parseResult.GetValue(smoothOpt)),
                    MinIslandCells = Math.Max(0, parseResult.GetValue(minIslandOpt)),
                };

                var result = GeoRasterizer.Rasterize(options);

                EnsureDirectory(outPath);
                File.WriteAllText(outPath, result.Mask);

                _output.WriteLine($"GeoJSON: {geojson}");
                _output.WriteLine($"输出:    {outPath}");
                _output.WriteLine($"范围:    {result.Bounds}");
                _output.WriteLine($"尺寸:    {options.Width} x {options.Height}");
                _output.WriteLine($"陆地:    {result.LandCells} 格（{result.LandRatio:P1}），海: {result.SeaCells} 格");
                _output.WriteLine($"陆块:    {result.Components} 个，最大占陆地 {result.LargestComponentRatio:P1}");

                if (options.OwnersMode)
                {
                    string mapPath = parseResult.GetValue(mapOutOpt) is { Length: > 0 } custom
                        ? Path.GetFullPath(custom)
                        : outPath + ".map";

                    EnsureDirectory(mapPath);
                    File.WriteAllText(mapPath, string.IsNullOrEmpty(result.OwnerMap)
                        ? "# 没有可分配的势力键\n"
                        : result.OwnerMap);

                    _output.WriteLine($"字符映射: {mapPath}");
                    _output.WriteLine("下一步：在 build-map 的 [belong] 里指向这两个文件（mode=mask）。");
                }
                else
                {
                    _output.WriteLine("下一步：在 build-map 的 [terrain] 里设 enabled=true, mode=mask, mask_file=该文件。");
                }

                if (result.Warnings.Count > 0)
                {
                    _output.WriteLine("");
                    _output.WriteLine($"⚠ {result.Warnings.Count} 条提示：");
                    foreach (string warning in result.Warnings)
                        _output.WriteLine("  - " + warning);
                }

                _output.WriteLine("[OK] 掩码已生成（未改动地图数据）。");
            }
            catch (Exception ex)
            {
                _output.WriteLine("[FAIL] geo rasterize: " + ex.Message);
            }
        });

        cmd.Subcommands.Add(rasterize);
        return cmd;
    }

    private static void EnsureDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
