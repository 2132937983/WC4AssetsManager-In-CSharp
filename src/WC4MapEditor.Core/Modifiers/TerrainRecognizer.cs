using System.Diagnostics;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 地形识别的结果：是否成功 / 是否被取消、给用户看的原因（失败或取消时），
/// 以及每格识别出的第一层地形（格子序号 → 地形类型）。
/// </summary>
public sealed record TerrainRecognitionResult(bool Success, bool Cancelled, string Message, IReadOnlyList<(int Index, byte TileType)> TileTypes)
{
    public static TerrainRecognitionResult Fail(string message) => new(false, false, message, Array.Empty<(int, byte)>());
}

/// <summary>
/// 按背景图（视图层图片）的颜色识别每格的第一层地形（颜色 → 地形的对应关系来自 setting.txt 的 ColorToTerrain）。
/// <para>
/// 只计算、不写地图：识别在后台线程进行，写入由调用方在 UI 线程里放进撤销记录
/// （<see cref="TerrainModifier.ApplyRecognition"/>）。这样中途取消时地图保持原样，识别期间也不会有后台线程改地图。
/// </para>
/// </summary>
public sealed class TerrainRecognizer
{
    private MapData? _mapData;
    private IViewLayerImageProvider? _imageProvider;

    public void UpdateMapData(MapData mapData) => _mapData = mapData;

    public void SetImageProvider(IViewLayerImageProvider provider) => _imageProvider = provider;

    /// <param name="progress">进度（已处理格数、总格数、当前行列），每前进 1% 报告一次。</param>
    public async Task<TerrainRecognitionResult> RecognizeTerrainAsync(
        IProgress<(int current, int total, int row, int col)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_mapData == null)
            return TerrainRecognitionResult.Fail(Loc.T("Module.Terrain.NoMap"));

        if (_imageProvider == null)
            return TerrainRecognitionResult.Fail(Loc.T("Module.Terrain.Recognize.NoProvider"));

        if (!_imageProvider.HasImage)
            return TerrainRecognitionResult.Fail(Loc.T("Module.Terrain.Recognize.NoImage"));

        var config = ConfigManager.Instance.GetColorToTerrainConfig();
        if (config.ColorMappings.Count == 0)
            return TerrainRecognitionResult.Fail(Loc.T("Module.Terrain.Recognize.NoConfig"));

        var imageProvider = _imageProvider;
        int bgWidth = imageProvider.Width;
        int bgHeight = imageProvider.Height;
        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        double hexWidth = Camera.HexHorizontalSpacing;
        double hexHeight = Camera.HexVerticalSpacing;
        double mapPixelWidth = mapWidth * hexWidth * 0.75 + hexWidth * 0.25;
        double mapPixelHeight = mapHeight * hexHeight + hexHeight * 0.5;
        double scaleX = mapPixelWidth / bgWidth;
        double scaleY = mapPixelHeight / bgHeight;

        Debug.WriteLine($"[TerrainRecognizer] 图片 {bgWidth}x{bgHeight}，地图 {mapWidth}x{mapHeight}，缩放 X={scaleX:F3} Y={scaleY:F3}，颜色映射 {config.ColorMappings.Count} 条");

        int totalHexes = mapWidth * mapHeight;
        var stopwatch = Stopwatch.StartNew();

        var tileTypes = await Task.Run(() =>
        {
            var result = new List<(int Index, byte TileType)>(totalHexes);
            var random = new Random();
            int processed = 0;
            int lastPercent = -1;

            for (int row = 0; row < mapHeight && !cancellationToken.IsCancellationRequested; row++)
            {
                for (int col = 0; col < mapWidth && !cancellationToken.IsCancellationRequested; col++)
                {
                    var region = GetHexRegionOnOriginalImage(col, row, bgWidth, bgHeight, hexWidth, hexHeight, mapWidth, mapHeight, scaleX, scaleY);
                    byte[]? regionData = region == null ? null : imageProvider.ExtractRegionData(region.Value.X, region.Value.Y, region.Value.Width, region.Value.Height);

                    if (regionData is { Length: > 0 } && PickTerrainType(regionData, config, random) is int terrainType)
                    {
                        // 陆地噪点：按概率把识别出的陆地地形换回平地（海洋与平地不受影响）
                        if (terrainType != 0 && terrainType != 1 && config.LandNoiseProbability > 0
                            && random.Next(100) < config.LandNoiseProbability)
                            terrainType = 0;
                        result.Add((row * mapWidth + col, (byte)terrainType));
                    }

                    processed++;
                    int percent = (int)(processed * 100L / totalHexes);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report((processed, totalHexes, row, col));
                    }
                }
            }
            return result;
        }, CancellationToken.None);

        stopwatch.Stop();
        Debug.WriteLine($"[TerrainRecognizer] 耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒，识别 {tileTypes.Count} 格，取消={cancellationToken.IsCancellationRequested}");

        if (cancellationToken.IsCancellationRequested)
            return new TerrainRecognitionResult(false, true, Loc.T("Module.Terrain.Recognize.Cancelled"), Array.Empty<(int, byte)>());

        return new TerrainRecognitionResult(true, false, string.Empty, tileTypes);
    }

    /// <summary>
    /// 取一格采样区域对应的地形：颜色占比达到阈值的映射里取占比最高的；都没达到时按占比加权随机；
    /// 区域里完全没有映射颜色时随机取一条映射（与原实现一致）。
    /// </summary>
    private static int? PickTerrainType(byte[] regionData, ColorToTerrainConfig config, Random random)
    {
        ColorTerrainMapping? best = null;
        double bestRatio = 0;
        var weighted = new List<(ColorTerrainMapping Mapping, double Ratio)>();

        foreach (var mapping in config.ColorMappings)
        {
            double ratio = CalculateColorRatio(regionData, mapping.ColorValue);
            if (ratio > 0) weighted.Add((mapping, ratio));
            if (ratio >= config.MinRatio && ratio > bestRatio && mapping.TerrainTypes.Count > 0)
            {
                best = mapping;
                bestRatio = ratio;
            }
        }

        var chosen = best
            ?? (weighted.Count > 0 ? SelectMappingByWeight(weighted, random) : config.ColorMappings[random.Next(config.ColorMappings.Count)]);
        if (chosen == null || chosen.TerrainTypes.Count == 0) return null;
        return chosen.TerrainTypes[random.Next(chosen.TerrainTypes.Count)];
    }

    private static (int X, int Y, int Width, int Height)? GetHexRegionOnOriginalImage(
        int col, int row, int bgWidth, int bgHeight,
        double hexWidth, double hexHeight,
        int mapWidth, int mapHeight,
        double scaleX, double scaleY)
    {
        double hexSize = hexWidth / 2;

        double mapPixelWidth = mapWidth * hexWidth * 0.75 + hexWidth * 0.25;
        double mapPixelHeight = mapHeight * hexHeight + hexHeight * 0.5;

        double relX = (col * hexWidth * 0.75 + hexSize) / mapPixelWidth;
        double relY = (row * hexHeight + hexHeight / 2 + (col % 2) * hexHeight * 0.5) / mapPixelHeight;

        int bgCenterX = (int)(relX * bgWidth);
        int bgCenterY = (int)(relY * bgHeight);

        int sampleWidth = Math.Max(5, (int)(hexSize * 2 / scaleX));
        int sampleHeight = Math.Max(5, (int)(hexHeight / scaleY));

        int bgX = Math.Max(0, Math.Min(bgCenterX - sampleWidth / 2, bgWidth - sampleWidth));
        int bgY = Math.Max(0, Math.Min(bgCenterY - sampleHeight / 2, bgHeight - sampleHeight));

        return sampleWidth > 0 && sampleHeight > 0 ? (bgX, bgY, sampleWidth, sampleHeight) : null;
    }

    private static double CalculateColorRatio(byte[] regionData, int targetColorValue, int tolerance = 30)
    {
        byte targetR = (byte)((targetColorValue >> 16) & 0xFF);
        byte targetG = (byte)((targetColorValue >> 8) & 0xFF);
        byte targetB = (byte)(targetColorValue & 0xFF);

        int matchingPixels = 0;
        int totalPixels = 0;

        for (int i = 0; i < regionData.Length - 3; i += 4)
        {
            if (regionData[i + 3] < 128)
                continue;

            totalPixels++;

            int dr = regionData[i] - targetR;
            int dg = regionData[i + 1] - targetG;
            int db = regionData[i + 2] - targetB;
            if (Math.Sqrt(dr * dr + dg * dg + db * db) <= tolerance)
                matchingPixels++;
        }

        return totalPixels == 0 ? 0 : (double)matchingPixels / totalPixels;
    }

    private static ColorTerrainMapping? SelectMappingByWeight(List<(ColorTerrainMapping Mapping, double Ratio)> mappings, Random random)
    {
        double totalRatio = 0;
        foreach (var item in mappings)
            totalRatio += item.Ratio;

        double randomValue = random.NextDouble() * totalRatio;
        double currentRatio = 0;
        foreach (var item in mappings)
        {
            currentRatio += item.Ratio;
            if (randomValue <= currentRatio)
                return item.Mapping;
        }

        return mappings[^1].Mapping;
    }
}
