using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 建筑 / 设施 / 城市等级图集的共享缓存。
///
/// BuildingRender 每次实例化都会走一遍 InitializeTextureAtlases：
///   - 31 类型 × 10 外观 = 310 次 File.Exists，外加 Directory.GetFiles("capital_*.png")
///   - 逐个 File.OpenRead + SKBitmap.Decode 解码上百张 PNG，再拼成三张图集
///
/// 但这三张图集只取决于资源目录，与打开的地图无关。这里改为全进程只构建一次，
/// 后续每次创建 BuildingRender 直接复用（实测该构造耗时 325~392ms，为主要单项）。
///
/// 共享的 SKImage 由本缓存持有所有权，调用方只能读取、不可 Dispose，
/// 也不得修改其中的映射表。
/// </summary>
internal static class BuildingAtlasCache
{
    /// <summary>图集中每个纹理的格子边长，与原 BuildingRender.ATLAS_TILE_SIZE 一致。</summary>
    internal const int AtlasTileSize = 128;

    /// <summary>三张图集及其映射表的容器。</summary>
    internal sealed class AtlasSet
    {
        internal SKImage? BuildingAtlas { get; set; }
        internal SKImage? FacilityAtlas { get; set; }
        internal SKImage? CityLevelAtlas { get; set; }

        internal Dictionary<string, SKRect> BuildingAtlasMap { get; } = new();
        internal Dictionary<string, SKRect> FacilityAtlasMap { get; } = new();
        internal Dictionary<string, SKRect> CityLevelAtlasMap { get; } = new();

        internal Dictionary<string, SKSize> BuildingOriginalSize { get; } = new();
        internal Dictionary<string, SKSize> FacilityOriginalSize { get; } = new();
        internal Dictionary<string, SKSize> CityLevelOriginalSize { get; } = new();
    }

    private static readonly object _lock = new();
    private static AtlasSet? _set;

    /// <summary>取共享图集。返回对象归缓存所有，调用方不得 Dispose 或修改。</summary>
    internal static AtlasSet Get()
    {
        var cached = _set;
        if (cached != null) return cached;

        lock (_lock)
        {
            if (_set != null) return _set;

            var sw = Stopwatch.StartNew();
            var set = new AtlasSet();

            BuildBuildingAtlas(set);
            BuildFacilityAtlas(set);
            BuildCityLevelAtlas(set);

            _set = set;
            Debug.WriteLine($"[BuildingAtlasCache] 图集构建完成: 建筑 {set.BuildingAtlasMap.Count} 个, 设施 {set.FacilityAtlasMap.Count} 个, 城市等级 {set.CityLevelAtlasMap.Count} 个, 耗时 {sw.ElapsedMilliseconds}ms");
            return set;
        }
    }

    /// <summary>丢弃已有图集并允许下次重建（用于资源目录变更后的热重载）。</summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            _set?.BuildingAtlas?.Dispose();
            _set?.FacilityAtlas?.Dispose();
            _set?.CityLevelAtlas?.Dispose();
            _set = null;
        }
    }

    private static void BuildBuildingAtlas(AtlasSet set)
    {
        var basePath = ConfigManager.Instance.GetBuildMarkPath();
        if (!Directory.Exists(basePath)) return;

        // 先收集所有可用的建筑图片路径，再统一拼图集。
        var buildingImages = new Dictionary<string, string>();

        for (int buildingType = 1; buildingType <= 31; buildingType++)
        {
            for (int appearance = 0; appearance <= 9; appearance++)
            {
                string fileName = $"building_{buildingType}_{appearance}.png";
                string filePath = Path.Combine(basePath, fileName);
                if (File.Exists(filePath))
                {
                    string cacheKey = $"{buildingType}_{appearance}_0";
                    buildingImages[cacheKey] = filePath;
                }
            }

            string defaultFileName = $"building_{buildingType}.png";
            string defaultFilePath = Path.Combine(basePath, defaultFileName);
            if (File.Exists(defaultFilePath))
            {
                string cacheKey = $"{buildingType}_0_0";
                if (!buildingImages.ContainsKey(cacheKey))
                    buildingImages[cacheKey] = defaultFilePath;
            }
        }

        try
        {
            var capitalFiles = Directory.GetFiles(basePath, "capital_*.png");
            foreach (var capitalFilePath in capitalFiles)
            {
                string capitalFileName = Path.GetFileNameWithoutExtension(capitalFilePath);
                string capitalIdStr = capitalFileName.Replace("capital_", "");
                if (int.TryParse(capitalIdStr, out int capitalId) && capitalId > 0)
                {
                    string cacheKey = $"0_0_{capitalId}";
                    buildingImages[cacheKey] = capitalFilePath;
                }
            }
        }
        catch { }

        string defaultBuildingPath = Path.Combine(basePath, "building_1.png");
        if (File.Exists(defaultBuildingPath))
            buildingImages["default"] = defaultBuildingPath;

        if (buildingImages.Count == 0) return;

        set.BuildingAtlas = ComposeAtlas(buildingImages, set.BuildingAtlasMap, set.BuildingOriginalSize);
    }

    private static void BuildFacilityAtlas(AtlasSet set)
    {
        var basePath = ConfigManager.Instance.GetInformationMarkPath();
        if (!Directory.Exists(basePath)) return;

        string[] facilityTypes = { "factory", "lab", "depot", "airport", "launch", "nuclear" };
        var facilityImages = new Dictionary<string, string>();

        foreach (var facilityType in facilityTypes)
        {
            string filePath = Path.Combine(basePath, $"facility_{facilityType}.png");
            if (File.Exists(filePath))
                facilityImages[facilityType] = filePath;
        }

        if (facilityImages.Count == 0) return;

        set.FacilityAtlas = ComposeAtlas(facilityImages, set.FacilityAtlasMap, set.FacilityOriginalSize);
    }

    private static void BuildCityLevelAtlas(AtlasSet set)
    {
        var basePath = ConfigManager.Instance.GetInformationMarkPath();
        if (!Directory.Exists(basePath)) return;

        string[] cityLevels = { "city_lv1", "city_lv2", "city_lv3", "city_lv4", "city_lv5" };
        var cityLevelImages = new Dictionary<string, string>();

        foreach (var cityLevel in cityLevels)
        {
            string filePath = Path.Combine(basePath, $"{cityLevel}.png");
            if (File.Exists(filePath))
                cityLevelImages[cityLevel] = filePath;
        }

        if (cityLevelImages.Count == 0) return;

        set.CityLevelAtlas = ComposeAtlas(cityLevelImages, set.CityLevelAtlasMap, set.CityLevelOriginalSize);
    }

    /// <summary>
    /// 把若干图片按行优先顺序拼进一张方形图集，同时记录每张图的格子矩形与原始尺寸。
    /// 三个图集原本各自复制了一份相同的拼装逻辑，这里合并。
    /// </summary>
    private static SKImage? ComposeAtlas(
        Dictionary<string, string> images,
        Dictionary<string, SKRect> tileMap,
        Dictionary<string, SKSize> originalSizes)
    {
        int atlasTiles = CalculateAtlasSize(images.Count);
        int atlasWidth = atlasTiles * AtlasTileSize;
        int atlasHeight = atlasTiles * AtlasTileSize;

        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        int tileIndex = 0;
        foreach (var kvp in images)
        {
            if (tileIndex >= atlasTiles * atlasTiles) break;

            string cacheKey = kvp.Key;
            string imagePath = kvp.Value;

            try
            {
                using var stream = File.OpenRead(imagePath);
                var bitmap = SKBitmap.Decode(stream);
                if (bitmap == null) continue;

                originalSizes[cacheKey] = new SKSize(bitmap.Width, bitmap.Height);

                int atlasX = (tileIndex % atlasTiles) * AtlasTileSize;
                int atlasY = (tileIndex / atlasTiles) * AtlasTileSize;

                var srcRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                var dstRect = new SKRect(atlasX, atlasY, atlasX + AtlasTileSize, atlasY + AtlasTileSize);
                canvas.DrawBitmap(bitmap, srcRect, dstRect);

                tileMap[cacheKey] = new SKRect(atlasX, atlasY, atlasX + AtlasTileSize, atlasY + AtlasTileSize);
                tileIndex++;
            }
            catch { }
        }

        return surface.Snapshot();
    }

    private static int CalculateAtlasSize(int totalCount)
    {
        int size = 4;
        while (size * size < totalCount)
            size *= 2;
        return Math.Min(size, 32);
    }
}
