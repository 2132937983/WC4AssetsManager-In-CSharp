using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class BuildingRender : IDisposable
{
    private const double BUILDING_IMAGE_SCALE = 3.3;
    private const double FACILITY_IMAGE_SCALE = 0.45;
    private const double KEY_POINT_ELLIPSE_SCALE = 1.8;
    private const byte KEY_POINT_ALPHA = 128;

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _showBuildingNames = true;
    private bool _disposed;

    /// <summary>城市名表，指向全进程共享的实例，本类只读。</summary>
    private Dictionary<int, string> _cityNames = new();
    private readonly Dictionary<int, string> _cityNameCache = new();

    /// <summary>三张图集由 <see cref="BuildingAtlasCache"/> 持有所有权，本类不得 Dispose。</summary>
    private SKImage? _buildingAtlas;
    private SKImage? _facilityAtlas;
    private SKImage? _cityLevelAtlas;

    /// <summary>以下映射表均指向 <see cref="BuildingAtlasCache"/> 中的实例，只读使用。</summary>
    private Dictionary<string, SKRect> _buildingAtlasMap = new();
    private Dictionary<string, SKRect> _facilityAtlasMap = new();
    private Dictionary<string, SKRect> _cityLevelAtlasMap = new();

    private Dictionary<string, SKSize> _buildingOriginalSize = new();
    private Dictionary<string, SKSize> _facilityOriginalSize = new();
    private Dictionary<string, SKSize> _cityLevelOriginalSize = new();

    // 每帧复用的绘制批处理容器。原实现每帧新建 5 个 List，并在 CollectFacilities 中
    // 为每个建筑再新建一个 List；这些容器只在渲染线程使用，Clear 后即可复用。
    private readonly List<BuildingDrawCall> _buildingBatch = new();
    private readonly List<FacilityDrawCall> _facilityBatch = new();
    private readonly List<KeyPointDrawCall> _keyPointList = new();
    private readonly List<NameDrawCall> _nameList = new();
    private readonly List<CityLevelDrawCall> _cityLevelList = new();
    private readonly List<(string Type, int Level)> _facilityScratch = new();

    private readonly SKFont _buildingNameFont;
    private readonly SKFont _facilityLevelFont;
    private readonly SKPaint _redKeyPointPaint;
    private readonly SKPaint _greenKeyPointPaint;
    private readonly SKPaint _facilityTextPaint;
    private readonly SKPaint _buildingNamePaint;
    private readonly SKPaint _buildingNameShadowPaint;
    private readonly SKPaint _fallbackTrianglePaint;

    public bool ShowBuildingNames
    {
        get => _showBuildingNames;
        set => _showBuildingNames = value;
    }

    public BuildingRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _buildingNameFont = new SKFont(SKTypeface.FromFamilyName("Microsoft YaHei"), 12);
        _facilityLevelFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 10);

        _redKeyPointPaint = new SKPaint { Color = new SKColor(255, 0, 0, KEY_POINT_ALPHA), IsAntialias = true };
        _greenKeyPointPaint = new SKPaint { Color = new SKColor(0, 255, 0, KEY_POINT_ALPHA), IsAntialias = true };
        _facilityTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _buildingNamePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _buildingNameShadowPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        _fallbackTrianglePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };

        LoadCityNames();
        InitializeTextureAtlases();
    }

    /// <summary>
    /// 重新加载城市名称（用于热重载）
    /// </summary>
    public void ReloadCityNames()
    {
        // 丢弃全进程共享的城市名表并重建。
        lock (_sharedCityNamesLock)
        {
            _sharedCityNames = null;
        }

        _cityNames = GetSharedCityNames();
        _cityNameCache.Clear();
        Debug.WriteLine("[BuildingRender] 城市名称已重新加载");
    }

    #region Texture Atlas Initialization

    private void InitializeTextureAtlases()
    {
        // 三张图集只取决于资源目录、与地图无关，由 BuildingAtlasCache 全进程构建一次。
        // 本类只持有引用，不拥有所有权。
        var set = BuildingAtlasCache.Get();

        _buildingAtlas = set.BuildingAtlas;
        _facilityAtlas = set.FacilityAtlas;
        _cityLevelAtlas = set.CityLevelAtlas;

        _buildingAtlasMap = set.BuildingAtlasMap;
        _facilityAtlasMap = set.FacilityAtlasMap;
        _cityLevelAtlasMap = set.CityLevelAtlasMap;

        _buildingOriginalSize = set.BuildingOriginalSize;
        _facilityOriginalSize = set.FacilityOriginalSize;
        _cityLevelOriginalSize = set.CityLevelOriginalSize;

        Debug.WriteLine($"[BuildingRender] 建筑图集: {_buildingAtlasMap.Count} 个纹理, 设施图集: {_facilityAtlasMap.Count} 个纹理, 城市等级图集: {_cityLevelAtlasMap.Count} 个纹理");
    }




    #endregion

    #region City Names

    private static readonly object _sharedCityNamesLock = new();
    private static Dictionary<int, string>? _sharedCityNames;

    /// <summary>
    /// 全进程共享的城市名表。原实现每次构造 BuildingRender 都会向 StringTableParser
    /// 取一次 9841 条城市名并逐条拷进实例字典，而它只取决于字符串表配置，与地图无关，
    /// 因此改为只构建一次。
    /// </summary>
    private static Dictionary<int, string> GetSharedCityNames()
    {
        var cached = _sharedCityNames;
        if (cached != null) return cached;

        lock (_sharedCityNamesLock)
        {
            if (_sharedCityNames != null) return _sharedCityNames;

            var names = new Dictionary<int, string>();
            try
            {
                var parser = ConfigManager.Instance.GetStringTableParser();
                var cityNames = parser.FindCityNames();

                foreach (var kvp in cityNames)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                        names[kvp.Key] = kvp.Value;
                }

                Debug.WriteLine($"[BuildingRender] 已加载 {names.Count} 个城市名称（共享）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BuildingRender] 加载城市名称失败: {ex.Message}");
            }

            _sharedCityNames = names;
            return names;
        }
    }

    private void LoadCityNames()
    {
        _cityNames = GetSharedCityNames();
    }

    private string GetCityName(int nameId)
    {
        if (nameId == 0 || nameId == -1)
            return string.Empty;

        if (_cityNameCache.TryGetValue(nameId, out string? cached))
            return cached;

        string cityName = _cityNames.TryGetValue(nameId, out string? name) ? name : $"城市{nameId}";
        _cityNameCache[nameId] = cityName;
        return cityName;
    }

    #endregion

    #region Cache Key

    private string GetBuildingCacheKey(Building building)
    {
        int buildingType = building.BuildingType;
        int appearance = building.Appearance;
        int decorativeBuilding = building.DecorativeBuilding;

        if (decorativeBuilding > 0)
        {
            string capitalKey = $"0_0_{decorativeBuilding}";
            if (_buildingAtlasMap.ContainsKey(capitalKey))
                return capitalKey;
        }

        if (buildingType > 31) buildingType = 31;
        if (buildingType < 1) buildingType = 1;

        string cacheKeyWithAppearance = $"{buildingType}_{appearance}_0";
        if (_buildingAtlasMap.ContainsKey(cacheKeyWithAppearance))
            return cacheKeyWithAppearance;

        string cacheKeyDefault = $"{buildingType}_0_0";
        if (_buildingAtlasMap.ContainsKey(cacheKeyDefault))
            return cacheKeyDefault;

        return cacheKeyWithAppearance;
    }

    #endregion

    #region Render

    public void Render(SKCanvas canvas, bool showFacilities = true)
    {
        if (canvas == null || _mapData?.Buildings == null || _mapData.Buildings.Count == 0)
            return;

        var visibleBounds = _camera.GetVisibleWorldBounds();
        double zoomLevel = _camera.ZoomLevel;
        double hexWidth = Camera.BaseHexSize * 2 * zoomLevel;
        double hexHeight = Camera.BaseHexSize * Math.Sqrt(3) * zoomLevel;

        int margin = 2;
        int startCol = Math.Max(0, (int)((visibleBounds.MinX - 50 * zoomLevel) / (hexWidth * 0.75)) - margin);
        int endCol = Math.Min(_mapData.MapWidth, (int)((visibleBounds.MaxX + 50 * zoomLevel) / (hexWidth * 0.75)) + margin + 1);
        int startRow = Math.Max(0, (int)((visibleBounds.MinY - 50 * zoomLevel) / hexHeight) - margin);
        int endRow = Math.Min(_mapData.MapHeight, (int)((visibleBounds.MaxY + 50 * zoomLevel) / hexHeight) + margin + 2);

        float scaledHexSize = (float)(Camera.BaseHexSize * _camera.ZoomLevel);
        int buildingImageSize = (int)(scaledHexSize * BUILDING_IMAGE_SCALE);
        int facilityImageSize = (int)(scaledHexSize * FACILITY_IMAGE_SCALE);

        _buildingBatch.Clear();
        _facilityBatch.Clear();
        _keyPointList.Clear();
        _nameList.Clear();
        _cityLevelList.Clear();

        foreach (var building in _mapData.Buildings)
        {
            int row = building.Coordinate / _mapData.MapWidth;
            int col = building.Coordinate % _mapData.MapWidth;

            if (col < startCol || col >= endCol || row < startRow || row >= endRow)
                continue;

            var (screenX, screenY) = _camera.HexToScreen(col, row);
            float sx = (float)screenX;
            float sy = (float)screenY;

            if (building.KeyPoint == 1 || building.KeyPoint == 2)
            {
                _keyPointList.Add(new KeyPointDrawCall(sx, sy, building.KeyPoint, scaledHexSize));
            }

            string cacheKey = GetBuildingCacheKey(building);
            if (_buildingAtlasMap.ContainsKey(cacheKey))
            {
                _buildingBatch.Add(new BuildingDrawCall(sx, sy, buildingImageSize, cacheKey, building, false));
            }
            else
            {
                _buildingBatch.Add(new BuildingDrawCall(sx, sy, buildingImageSize, "default", building, true));
            }

            if (showFacilities)
                CollectFacilities(building, sx, sy, scaledHexSize, facilityImageSize, _facilityBatch);

            if (_showBuildingNames && building.Name != 0 && building.Name != -1)
                _nameList.Add(new NameDrawCall(sx, sy, building, buildingImageSize));

            if (building.BuildingType >= 11 && building.BuildingType <= 15)
            {
                int cityLevel = building.BuildingType - 10;
                string cityLevelCacheKey = $"city_lv{cityLevel}";
                if (_cityLevelAtlasMap.ContainsKey(cityLevelCacheKey))
                {
                    int cityLevelImageSize = (int)(facilityImageSize * 1.2);
                    _cityLevelList.Add(new CityLevelDrawCall(sx, sy, cityLevelImageSize, cityLevelCacheKey));
                }
            }
        }

        DrawKeyPoints(canvas, _keyPointList);
        DrawBuildingsAtlas(canvas, _buildingBatch);
        DrawFacilitiesBatch(canvas, _facilityBatch);
        DrawCityLevelsBatch(canvas, _cityLevelList);
        DrawBuildingNames(canvas, _nameList);
    }

    private void CollectFacilities(Building building, float screenX, float screenY, float scaledHexSize, int imageSize, List<FacilityDrawCall> batch)
    {
        var facilities = _facilityScratch;
        facilities.Clear();

        if (building.FactoryLevel > 0) facilities.Add(("factory", building.FactoryLevel));
        if (building.ResearchLevel > 0) facilities.Add(("lab", building.ResearchLevel));
        if (building.MedicalLevel > 0) facilities.Add(("depot", building.MedicalLevel));
        if (building.AviationLevel > 0) facilities.Add(("airport", building.AviationLevel));
        if (building.MissileLevel > 0) facilities.Add(("launch", building.MissileLevel));
        if (building.NuclearLevel > 0) facilities.Add(("nuclear", building.NuclearLevel));

        int facilityCount = facilities.Count;
        if (facilityCount == 0) return;

        float spacing = imageSize;
        float totalWidth = (facilityCount - 1) * spacing;
        float startX = screenX - totalWidth / 2;
        float startY = screenY - scaledHexSize * 0.7f;

        for (int i = 0; i < facilityCount; i++)
        {
            float fx = startX + i * spacing;
            string facilityType = facilities[i].Type;
            int facilityLevel = facilities[i].Level;

            if (_facilityAtlasMap.ContainsKey(facilityType))
                batch.Add(new FacilityDrawCall(fx, startY, imageSize, facilityType, facilityLevel));
        }
    }

    private void DrawKeyPoints(SKCanvas canvas, List<KeyPointDrawCall> keyPoints)
    {
        foreach (var kp in keyPoints)
        {
            float ellipseSize = kp.ScaledHexSize * (float)KEY_POINT_ELLIPSE_SCALE;
            var rect = new SKRect(kp.ScreenX - ellipseSize / 2, kp.ScreenY - ellipseSize / 2, kp.ScreenX + ellipseSize / 2, kp.ScreenY + ellipseSize / 2);
            var paint = kp.KeyPointType == 1 ? _redKeyPointPaint : _greenKeyPointPaint;
            canvas.DrawOval(rect, paint);
        }
    }

    private void DrawBuildingsAtlas(SKCanvas canvas, List<BuildingDrawCall> batch)
    {
        if (batch.Count == 0 || _buildingAtlas == null) return;

        lock (_atlasLock)
        {
            using var paint = new SKPaint { IsAntialias = true };

            var grouped = batch.GroupBy(d => d.IsFallback ? "fallback" : d.CacheKey);

            foreach (var group in grouped)
            {
                if (group.Key == "fallback")
                {
                    foreach (var drawCall in group)
                        RenderFallbackTriangle(canvas, drawCall.ScreenX, drawCall.ScreenY, drawCall.ImageSize * 0.5f);
                    continue;
                }

                if (!_buildingAtlasMap.TryGetValue(group.Key, out var srcRect)) continue;
                if (!_buildingOriginalSize.TryGetValue(group.Key, out var originalSize)) continue;

                foreach (var drawCall in group)
                {
                    float scale = Math.Min(drawCall.ImageSize / originalSize.Width, drawCall.ImageSize / originalSize.Height);
                    float drawWidth = originalSize.Width * scale;
                    float drawHeight = originalSize.Height * scale;

                    var dstRect = new SKRect(drawCall.ScreenX - drawWidth / 2, drawCall.ScreenY - drawHeight / 2, drawCall.ScreenX + drawWidth / 2, drawCall.ScreenY + drawHeight / 2);
                    canvas.DrawImage(_buildingAtlas, srcRect, dstRect, paint);
                }
            }
        }
    }

    private void DrawFacilitiesBatch(SKCanvas canvas, List<FacilityDrawCall> batch)
    {
        if (batch.Count == 0 || _facilityAtlas == null) return;

        lock (_atlasLock)
        {
            using var paint = new SKPaint { IsAntialias = true };

            var grouped = batch.GroupBy(d => d.CacheKey);

            foreach (var group in grouped)
            {
                if (!_facilityAtlasMap.TryGetValue(group.Key, out var srcRect)) continue;
                if (!_facilityOriginalSize.TryGetValue(group.Key, out var originalSize)) continue;

                foreach (var drawCall in group)
                {
                    float scale = Math.Min(drawCall.ImageSize / originalSize.Width, drawCall.ImageSize / originalSize.Height);
                    float drawWidth = originalSize.Width * scale;
                    float drawHeight = originalSize.Height * scale;

                    var dstRect = new SKRect(drawCall.ScreenX - drawWidth / 2, drawCall.ScreenY - drawHeight / 2, drawCall.ScreenX + drawWidth / 2, drawCall.ScreenY + drawHeight / 2);
                    canvas.DrawImage(_facilityAtlas, srcRect, dstRect, paint);
                }
            }

            foreach (var drawCall in batch)
            {
                if (drawCall.Level > 1 && _facilityOriginalSize.TryGetValue(drawCall.CacheKey, out var originalSize))
                {
                    float scale = Math.Min(drawCall.ImageSize / originalSize.Width, drawCall.ImageSize / originalSize.Height);
                    float drawWidth = originalSize.Width * scale;
                    float drawHeight = originalSize.Height * scale;
                    DrawFacilityLevel(canvas, drawCall.ScreenX + drawWidth * 0.4f, drawCall.ScreenY + drawHeight * 0.4f, drawCall.Level);
                }
            }
        }
    }

    private void DrawFacilityLevel(SKCanvas canvas, float screenX, float screenY, int level)
    {
        string levelText = level.ToString();
        float textSize = (float)(8 * _camera.ZoomLevel);
        _facilityLevelFont.Size = textSize;

        float shadowOffset = (float)(1 * Math.Max(0.5f, _camera.ZoomLevel));

        canvas.DrawText(levelText, screenX + shadowOffset, screenY + shadowOffset + textSize * 0.3f, SKTextAlign.Center, _facilityLevelFont, _buildingNameShadowPaint);
        canvas.DrawText(levelText, screenX, screenY + textSize * 0.3f, SKTextAlign.Center, _facilityLevelFont, _facilityTextPaint);
    }

    private void DrawCityLevelsBatch(SKCanvas canvas, List<CityLevelDrawCall> batch)
    {
        if (batch.Count == 0 || _cityLevelAtlas == null) return;

        lock (_atlasLock)
        {
            using var paint = new SKPaint { IsAntialias = true };

            var grouped = batch.GroupBy(d => d.CacheKey);

            foreach (var group in grouped)
            {
                if (!_cityLevelAtlasMap.TryGetValue(group.Key, out var srcRect)) continue;
                if (!_cityLevelOriginalSize.TryGetValue(group.Key, out var originalSize)) continue;

                foreach (var drawCall in group)
                {
                    float scale = Math.Min(drawCall.ImageSize / originalSize.Width, drawCall.ImageSize / originalSize.Height);
                    float drawWidth = originalSize.Width * scale;
                    float drawHeight = originalSize.Height * scale;

                    var dstRect = new SKRect(drawCall.ScreenX - drawWidth / 2, drawCall.ScreenY - drawHeight / 2, drawCall.ScreenX + drawWidth / 2, drawCall.ScreenY + drawHeight / 2);
                    canvas.DrawImage(_cityLevelAtlas, srcRect, dstRect, paint);
                }
            }
        }
    }

    private void DrawBuildingNames(SKCanvas canvas, List<NameDrawCall> nameList)
    {
        if (nameList.Count == 0) return;

        const float MIN_FONT_SIZE = 18.0f;
        float textSize = (float)(16 * _camera.ZoomLevel);
        textSize = Math.Max(textSize, MIN_FONT_SIZE);
        _buildingNameFont.Size = textSize;

        float shadowOffset = (float)(1 * Math.Max(0.5f, _camera.ZoomLevel));

        foreach (var nameCall in nameList)
        {
            string cityName = GetCityName(nameCall.Building.Name);
            if (string.IsNullOrEmpty(cityName)) continue;

            float yOffset = nameCall.ImageSize * 0.45f + textSize * 0.8f;
            float x = nameCall.ScreenX;
            float y = nameCall.ScreenY + yOffset;

            canvas.DrawText(cityName, x + shadowOffset, y + shadowOffset, SKTextAlign.Center, _buildingNameFont, _buildingNameShadowPaint);
            canvas.DrawText(cityName, x, y, SKTextAlign.Center, _buildingNameFont, _buildingNamePaint);
        }
    }

    private void RenderFallbackTriangle(SKCanvas canvas, float screenX, float screenY, float size)
    {
        using var path = new SKPath();
        path.MoveTo(screenX, screenY - size);
        path.LineTo(screenX - size * 0.866f, screenY + size * 0.5f);
        path.LineTo(screenX + size * 0.866f, screenY + size * 0.5f);
        path.Close();
        canvas.DrawPath(path, _fallbackTrianglePaint);
    }

    #endregion

    #region Draw Call Structures

    private readonly struct BuildingDrawCall(float screenX, float screenY, int imageSize, string cacheKey, Building building, bool isFallback)
    {
        public readonly float ScreenX = screenX;
        public readonly float ScreenY = screenY;
        public readonly int ImageSize = imageSize;
        public readonly string CacheKey = cacheKey;
        public readonly Building Building = building;
        public readonly bool IsFallback = isFallback;
    }

    private readonly struct FacilityDrawCall(float screenX, float screenY, int imageSize, string cacheKey, int level)
    {
        public readonly float ScreenX = screenX;
        public readonly float ScreenY = screenY;
        public readonly int ImageSize = imageSize;
        public readonly string CacheKey = cacheKey;
        public readonly int Level = level;
    }

    private readonly struct KeyPointDrawCall(float screenX, float screenY, int keyPointType, float scaledHexSize)
    {
        public readonly float ScreenX = screenX;
        public readonly float ScreenY = screenY;
        public readonly int KeyPointType = keyPointType;
        public readonly float ScaledHexSize = scaledHexSize;
    }

    private readonly struct NameDrawCall(float screenX, float screenY, Building building, int imageSize)
    {
        public readonly float ScreenX = screenX;
        public readonly float ScreenY = screenY;
        public readonly Building Building = building;
        public readonly int ImageSize = imageSize;
    }

    private readonly struct CityLevelDrawCall(float screenX, float screenY, int imageSize, string cacheKey)
    {
        public readonly float ScreenX = screenX;
        public readonly float ScreenY = screenY;
        public readonly int ImageSize = imageSize;
        public readonly string CacheKey = cacheKey;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            // 图集与映射表归 BuildingAtlasCache 持有，这里只解除引用，不能 Dispose / Clear。
            _buildingAtlas = null;
            _facilityAtlas = null;
            _cityLevelAtlas = null;
        }

        _redKeyPointPaint?.Dispose();
        _greenKeyPointPaint?.Dispose();
        _facilityTextPaint?.Dispose();
        _buildingNamePaint?.Dispose();
        _buildingNameShadowPaint?.Dispose();
        _fallbackTrianglePaint?.Dispose();
        _buildingNameFont?.Dispose();
        _facilityLevelFont?.Dispose();

        Debug.WriteLine("[BuildingRender] 资源已释放");
    }
}