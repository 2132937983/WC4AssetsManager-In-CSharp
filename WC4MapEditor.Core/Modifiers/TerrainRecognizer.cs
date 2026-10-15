using System.Diagnostics;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class TerrainRecognizer
{
    private MapData? _mapData;
    private IViewLayerImageProvider? _imageProvider;

    public event Action<string>? StatusMessageChanged;
    public event Action? RecognitionCompleted;

    public void UpdateMapData(MapData mapData) => _mapData = mapData;

    public void SetImageProvider(IViewLayerImageProvider provider) => _imageProvider = provider;

    public async Task<(bool success, int modifiedCount)> RecognizeTerrainAsync(
        IProgress<(int current, int total, int row, int col)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_mapData == null)
            return (false, 0);

        if (_imageProvider == null)
        {
            StatusMessageChanged?.Invoke("未找到视图层图片提供器");
            Debug.WriteLine("[TerrainRecognizer] 失败: _imageProvider 为 null");
            return (false, 0);
        }

        if (!_imageProvider.HasImage)
        {
            StatusMessageChanged?.Invoke("未找到视图层图片，请先按O键加载图片");
            Debug.WriteLine("[TerrainRecognizer] 失败: _imageProvider.HasImage 为 false");
            return (false, 0);
        }

        var config = ConfigManager.Instance.GetColorToTerrainConfig();
        if (config.ColorMappings.Count == 0)
        {
            StatusMessageChanged?.Invoke("未找到ColorToTerrain配置，请检查setting.txt文件");
            Debug.WriteLine("[TerrainRecognizer] 失败: ColorMappings 为空");
            return (false, 0);
        }

        int bgWidth = _imageProvider.Width;
        int bgHeight = _imageProvider.Height;
        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        double hexWidth = Camera.HexHorizontalSpacing;
        double hexHeight = Camera.HexVerticalSpacing;

        double mapPixelWidth = mapWidth * hexWidth * 0.75 + hexWidth * 0.25;
        double mapPixelHeight = mapHeight * hexHeight + hexHeight * 0.5;

        double scaleX = mapPixelWidth / bgWidth;
        double scaleY = mapPixelHeight / bgHeight;

        Debug.WriteLine($"[TerrainRecognizer] 原始图片尺寸: {bgWidth} x {bgHeight}");
        Debug.WriteLine($"[TerrainRecognizer] 地图尺寸: {mapWidth} x {mapHeight}");
        Debug.WriteLine($"[TerrainRecognizer] 六边形尺寸: {hexWidth} x {hexHeight}");
        Debug.WriteLine($"[TerrainRecognizer] 地图世界尺寸: {mapPixelWidth:F1} x {mapPixelHeight:F1}");
        Debug.WriteLine($"[TerrainRecognizer] 缩放比例: X={scaleX:F3}, Y={scaleY:F3}");
        Debug.WriteLine($"[TerrainRecognizer] 颜色映射数量: {config.ColorMappings.Count}");

        int totalHexes = mapWidth * mapHeight;
        int modifiedCount = 0;
        int processedCount = 0;

        var stopwatch = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            var localRandom = new Random();

            for (int row = 0; row < mapHeight; row++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                for (int col = 0; col < mapWidth; col++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    var regionCoords = GetHexRegionOnOriginalImage(
                        col, row, bgWidth, bgHeight,
                        hexWidth, hexHeight, mapWidth, mapHeight,
                        scaleX, scaleY);

                    if (regionCoords == null)
                        continue;

                    int x = regionCoords.Value.X;
                    int y = regionCoords.Value.Y;
                    int w = regionCoords.Value.Width;
                    int h = regionCoords.Value.Height;

                    byte[]? regionData = _imageProvider.ExtractRegionData(x, y, w, h);
                    if (regionData == null || regionData.Length == 0)
                        continue;

                    int? bestTerrainType = null;
                    double bestRatio = 0;

                    foreach (var mapping in config.ColorMappings)
                    {
                        double ratio = CalculateColorRatio(regionData, mapping.ColorValue);
                        if (ratio >= config.MinRatio && ratio > bestRatio)
                        {
                            if (mapping.TerrainTypes.Count > 0)
                            {
                                bestTerrainType = mapping.TerrainTypes[localRandom.Next(mapping.TerrainTypes.Count)];
                                bestRatio = ratio;
                            }
                        }
                    }

                    bool wasModified = false;

                    if (bestTerrainType.HasValue)
                    {
                        SetTerrainType(col, row, bestTerrainType.Value);
                        modifiedCount++;
                        wasModified = true;
                    }
                    else
                    {
                        var validMappings = new List<(ColorTerrainMapping Mapping, double Ratio)>();
                        foreach (var mapping in config.ColorMappings)
                        {
                            double ratio = CalculateColorRatio(regionData, mapping.ColorValue);
                            if (ratio > 0)
                                validMappings.Add((mapping, ratio));
                        }

                        if (validMappings.Count > 0)
                        {
                            var selectedMapping = SelectMappingByWeight(validMappings, localRandom);
                            if (selectedMapping != null && selectedMapping.TerrainTypes.Count > 0)
                            {
                                int randomTerrainType = selectedMapping.TerrainTypes[localRandom.Next(selectedMapping.TerrainTypes.Count)];
                                SetTerrainType(col, row, randomTerrainType);
                                modifiedCount++;
                                wasModified = true;
                            }
                        }
                        else if (config.ColorMappings.Count > 0)
                        {
                            var randomMapping = config.ColorMappings[localRandom.Next(config.ColorMappings.Count)];
                            if (randomMapping.TerrainTypes.Count > 0)
                            {
                                int randomTerrainType = randomMapping.TerrainTypes[localRandom.Next(randomMapping.TerrainTypes.Count)];
                                SetTerrainType(col, row, randomTerrainType);
                                modifiedCount++;
                                wasModified = true;
                            }
                        }
                    }

                    if (wasModified && config.LandNoiseProbability > 0)
                    {
                        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);
                        int currentType = terrain.TileType1;
                        if (currentType != 1 && currentType != 0)
                        {
                            if (localRandom.Next(100) < config.LandNoiseProbability)
                            {
                                terrain.TileType1 = 0;
                                terrain.DecorationType1 = 0;
                            }
                        }
                    }

                    processedCount++;
                    progress?.Report((processedCount, totalHexes, row, col));
                }
            }
        });

        stopwatch.Stop();
        Debug.WriteLine($"[TerrainRecognizer] 处理完成，耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒，修改了 {modifiedCount} 个格子");

        string message = $"地形识别完成，共修改 {modifiedCount} 个格子";
        StatusMessageChanged?.Invoke(message);
        RecognitionCompleted?.Invoke();

        return (true, modifiedCount);
    }

    private (int X, int Y, int Width, int Height)? GetHexRegionOnOriginalImage(
        int col, int row, int bgWidth, int bgHeight,
        double hexWidth, double hexHeight,
        int mapWidth, int mapHeight,
        double scaleX, double scaleY)
    {
        try
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

            int bgX = bgCenterX - sampleWidth / 2;
            int bgY = bgCenterY - sampleHeight / 2;

            bgX = Math.Max(0, Math.Min(bgX, bgWidth - sampleWidth));
            bgY = Math.Max(0, Math.Min(bgY, bgHeight - sampleHeight));

            if (sampleWidth > 0 && sampleHeight > 0)
                return (bgX, bgY, sampleWidth, sampleHeight);

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerrainRecognizer] 计算六边形区域时出错: {ex.Message}");
            return null;
        }
    }

    private static double CalculateColorRatio(byte[] regionData, int targetColorValue, int tolerance = 30)
    {
        if (regionData == null || regionData.Length == 0)
            return 0;

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
            double distance = Math.Sqrt(dr * dr + dg * dg + db * db);

            if (distance <= tolerance)
                matchingPixels++;
        }

        return totalPixels == 0 ? 0 : (double)matchingPixels / totalPixels;
    }

    private static ColorTerrainMapping? SelectMappingByWeight(
        List<(ColorTerrainMapping Mapping, double Ratio)> mappings, Random random)
    {
        if (mappings.Count == 0)
            return null;

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

        return mappings[mappings.Count - 1].Mapping;
    }

    private void SetTerrainType(int col, int row, int terrainType)
    {
        if (_mapData == null) return;
        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);
        terrain.TileType1 = (byte)terrainType;
        terrain.DecorationType1 = 0;
    }
}