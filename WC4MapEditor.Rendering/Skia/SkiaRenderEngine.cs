using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Rendering.Helpers;

namespace WC4MapEditor.Rendering.Skia;

public class SkiaRenderEngine : IRenderEngine
{
    /// <summary>
    /// 请求重绘的回调。由宿主（WPF 层）注入，例如绑定到 SKElement.InvalidateVisual。
    /// 渲染库本身不依赖任何 UI 框架，未注入时为空操作。
    /// </summary>
    public Action? InvalidateCallback { get; set; }

    private bool _disposed;

    private BackGroundRender? _backgroundRender;
    private LandTerrainsRender? _landTerrainsRender;
    private ProvinceRender? _provinceRender;
    private BuildingRender? _buildingRender;
    private ArmyRender? _armyRender;
    private TrapRender? _trapRender;
    private ReinforceRender? _reinforceRender;
    private ReinforceRenderNew? _reinforceRenderNew;
    private StrategicConstructionRender? _strategicConstructionRender;
    private AirForceRender? _airForceRender;
    private WeatherRender? _weatherRender;
    private MapCaseRender? _mapCaseRender;
    private SelectionRender? _selectionRender;
    private OverlayRender? _overlayRender;
    private GeoRulerRender? _geoRulerRender;
    private LegionDomainRender? _legionDomainRender;
    private BelongFlagRender? _belongFlagRender;

    public bool IsAvailable => true;
    public string EngineName => "SkiaSharp";

    public bool EnableTerrainsRender { get; set; } = true;
    public bool EnableBackgroundRender { get; set; } = true;
    public bool EnableProvinceRender { get; set; }
    public bool EnableProvinceCapitalRender { get; set; } = true;
    public bool EnableBuildingRender { get; set; } = true;
    public bool EnableArmyRender { get; set; } = true;
    public bool EnableTrapRender { get; set; } = true;
    public bool EnableSelectionRender { get; set; } = true;
    public bool EnableLegionDomainRender { get; set; }
    public bool EnableBelongFlagRender { get; set; }
    public bool EnableReinforceNewRender { get; set; }
    public bool EnableStrategicConstructionRender { get; set; }
    public bool EnableAirForceRender { get; set; }
    public bool EnableWeatherRender { get; set; }
    public bool EnableMapCaseRender { get; set; }

    public bool ShowBuildingNames
    {
        get => _buildingRender?.ShowBuildingNames ?? true;
        set
        {
            if (_buildingRender != null)
                _buildingRender.ShowBuildingNames = value;
        }
    }

    public GeoRulerRender? GeoRuler
    {
        get => _geoRulerRender;
        set
        {
            _geoRulerRender?.Dispose();
            _geoRulerRender = value;
        }
    }

    private string _helpText = string.Empty;
    private string _modeName = string.Empty;
    private bool _showHelp = true;
    private bool _showModeName = true;
    private bool _showLayer2 = true;

    private SKBitmap? _viewLayerImage;
    private bool _viewLayerVisible;
    private float _viewLayerOpacity = 0.5f;
    private string _viewLayerImagePath = string.Empty;
    private int _viewLayerOriginalWidth;
    private int _viewLayerOriginalHeight;

    private SKBitmap? _viewLayerCache;
    private bool _viewLayerCacheValid;

    private readonly SkiaViewLayerImageProvider _viewLayerImageProvider = new();
    public SkiaViewLayerImageProvider ViewLayerImageProvider => _viewLayerImageProvider;
    private double _viewLayerCacheZoom;
    private double _viewLayerCacheOffsetX;
    private double _viewLayerCacheOffsetY;

    public string HelpText
    {
        get => _overlayRender?.HelpText ?? _helpText;
        set
        {
            _helpText = value ?? string.Empty;
            if (_overlayRender != null) _overlayRender.HelpText = _helpText;
        }
    }

    public string ModeName
    {
        get => _overlayRender?.ModeName ?? _modeName;
        set
        {
            _modeName = value ?? string.Empty;
            if (_overlayRender != null) _overlayRender.ModeName = _modeName;
        }
    }

    public bool ShowHelp
    {
        get => _overlayRender?.ShowHelp ?? _showHelp;
        set
        {
            _showHelp = value;
            if (_overlayRender != null) _overlayRender.ShowHelp = _showHelp;
        }
    }

    public float HelpOpacity
    {
        get => _overlayRender?.HelpOpacity ?? 1.0f;
        set { if (_overlayRender != null) _overlayRender.HelpOpacity = value; }
    }

    public void UpdateHelpFadeAnimation()
    {
        _overlayRender?.UpdateFadeAnimation();
    }

    public void SetBrushPreview(int centerCol, int centerRow, int brushSize, string brushShape,
        double zoomLevel, double offsetX, double offsetY, bool visible)
    {
        _overlayRender?.SetBrushPreview(centerCol, centerRow, brushSize, brushShape, zoomLevel, offsetX, offsetY, visible);
    }

    public void HideBrushPreview()
    {
        _overlayRender?.HideBrushPreview();
    }

    public bool ShowModeName
    {
        get => _overlayRender?.ShowModeName ?? _showModeName;
        set
        {
            _showModeName = value;
            if (_overlayRender != null) _overlayRender.ShowModeName = _showModeName;
        }
    }

    public bool ShowLayer2
    {
        get => _showLayer2;
        set
        {
            if (_showLayer2 == value) return;
            _showLayer2 = value;
            if (_backgroundRender != null) _backgroundRender.ShowLayer2 = value;
            Invalidate();
        }
    }

    private bool _showHexBorders;
    public bool ShowHexBorders
    {
        get => _showHexBorders;
        set
        {
            if (_showHexBorders == value) return;
            _showHexBorders = value;
            if (_backgroundRender != null) _backgroundRender.ShowGridLines = value;
            Invalidate();
        }
    }

    public void CycleLabelMode()
    {
        if (_backgroundRender == null) return;
        var current = _backgroundRender.LabelMode;
        var next = (BackGroundRender.HexLabelMode)(((int)current + 1) % 3);
        _backgroundRender.LabelMode = next;
        Invalidate();
    }

    public void Initialize(IntPtr hwnd, int width, int height)
    {
    }

    private void InitializeTerrainRenderers()
    {
        if (_backgroundRender != null) return;

        _landTerrainsRender = new LandTerrainsRender();
        _backgroundRender = new BackGroundRender();
        _backgroundRender.SetLandTerrainsRender(_landTerrainsRender);
        _backgroundRender.ShowLayer2 = _showLayer2;
        _backgroundRender.ShowGridLines = _showHexBorders;
        _backgroundRender.CoastCacheUpdated += OnCoastCacheUpdated;
        _provinceRender = new ProvinceRender();
        _selectionRender = new SelectionRender();
        _overlayRender = new OverlayRender
        {
            HelpText = _helpText,
            ModeName = _modeName,
            ShowHelp = _showHelp,
            ShowModeName = _showModeName
        };
        _geoRulerRender = new GeoRulerRender();
        _legionDomainRender = new LegionDomainRender();
        _belongFlagRender = new BelongFlagRender();
    }

    private void InitializeEntityRenderers(Camera camera, MapData mapData)
    {
        if (_buildingRender == null)
            _buildingRender = new BuildingRender(camera, mapData);
        if (_armyRender == null)
            _armyRender = new ArmyRender(camera, mapData);
        if (_trapRender == null)
            _trapRender = new TrapRender(camera, mapData);
        if (_reinforceRender == null)
            _reinforceRender = new ReinforceRender(camera, mapData);
        if (_reinforceRenderNew == null)
            _reinforceRenderNew = new ReinforceRenderNew(camera, mapData);
        if (_strategicConstructionRender == null)
            _strategicConstructionRender = new StrategicConstructionRender(camera, mapData);
        if (_airForceRender == null)
            _airForceRender = new AirForceRender(camera, mapData);
        if (_weatherRender == null)
            _weatherRender = new WeatherRender(camera, mapData);
        if (_mapCaseRender == null)
            _mapCaseRender = new MapCaseRender(camera, mapData);
    }

    public void Resize(int width, int height)
    {
        if (_backgroundRender != null)
        {
            _backgroundRender.ViewportWidth = width;
            _backgroundRender.ViewportHeight = height;
        }
    }

    public void Render(MapData mapData, Camera camera)
    {
        Invalidate();
    }

    public void PreloadBelongFlagAtlas(MapData mapData)
    {
        _belongFlagRender?.PreloadAtlas(mapData);
    }

    /// <summary>
    /// 重新加载建筑城市名称（用于热重载）
    /// </summary>
    public void ReloadBuildingCityNames()
    {
        _buildingRender?.ReloadCityNames();
    }

    public void InitializeTacticalMapImageCache()
    {
        TacticalMapImageCache.Instance.Initialize();
    }

    public void Render(SKCanvas canvas, MapData mapData, Camera camera)
    {
        if (canvas == null || mapData == null) return;

        canvas.Clear(SKColors.Black);

        InitializeTerrainRenderers();
        InitializeEntityRenderers(camera, mapData);

        if (EnableBackgroundRender && _backgroundRender != null)
        {
            _backgroundRender.SetCameraState(camera.OffsetX, camera.OffsetY, camera.ZoomLevel,
                (int)camera.ViewportWidth, (int)camera.ViewportHeight,
                EnableBackgroundRender, EnableTerrainsRender);
            _backgroundRender.Render(canvas, mapData);
        }

        RenderViewLayer(canvas, camera);

        if (_provinceRender != null)
        {
            _provinceRender.EnableProvinceRender = EnableProvinceRender;
            _provinceRender.EnableCapitalRender = EnableProvinceCapitalRender;
            _provinceRender.OffsetX = camera.OffsetX;
            _provinceRender.OffsetY = camera.OffsetY;
            _provinceRender.ZoomLevel = camera.ZoomLevel;
            _provinceRender.ViewportWidth = (int)camera.ViewportWidth;
            _provinceRender.ViewportHeight = (int)camera.ViewportHeight;
            _provinceRender.Render(canvas, mapData);
        }

        if (_legionDomainRender != null)
        {
            _legionDomainRender.EnableLegionDomainRender = EnableLegionDomainRender;
            _legionDomainRender.OffsetX = camera.OffsetX;
            _legionDomainRender.OffsetY = camera.OffsetY;
            _legionDomainRender.ZoomLevel = camera.ZoomLevel;
            _legionDomainRender.ViewportWidth = (int)camera.ViewportWidth;
            _legionDomainRender.ViewportHeight = (int)camera.ViewportHeight;
            _legionDomainRender.Render(canvas, mapData);
        }

        if (_belongFlagRender != null)
        {
            _belongFlagRender.EnableBelongFlagRender = EnableBelongFlagRender;
            _belongFlagRender.OffsetX = camera.OffsetX;
            _belongFlagRender.OffsetY = camera.OffsetY;
            _belongFlagRender.ZoomLevel = camera.ZoomLevel;
            _belongFlagRender.ViewportWidth = (int)camera.ViewportWidth;
            _belongFlagRender.ViewportHeight = (int)camera.ViewportHeight;
            _belongFlagRender.Render(canvas, mapData);
        }

        if (EnableBuildingRender && _buildingRender != null)
        {
            _buildingRender.Render(canvas, true);
        }

        if (EnableArmyRender && _armyRender != null)
        {
            _armyRender.Render(canvas);
        }

        if (EnableTrapRender && _trapRender != null)
        {
            _trapRender.Render(canvas);
        }

        if (EnableReinforceNewRender && _reinforceRenderNew != null)
        {
            _reinforceRenderNew.Render(canvas);
        }
        else if (EnableArmyRender && _reinforceRender != null)
        {
            _reinforceRender.Render(canvas);
        }

        if (EnableStrategicConstructionRender && _strategicConstructionRender != null)
        {
            _strategicConstructionRender.Render(canvas);
        }

        if (EnableAirForceRender && _airForceRender != null)
        {
            _airForceRender.Render(canvas);
        }

        if (EnableWeatherRender && _weatherRender != null)
        {
            _weatherRender.Render(canvas);
        }

        if (EnableMapCaseRender && _mapCaseRender != null)
        {
            _mapCaseRender.Render(canvas);
        }

        if (EnableSelectionRender && _selectionRender != null)
        {
            _selectionRender.OffsetX = camera.OffsetX;
            _selectionRender.OffsetY = camera.OffsetY;
            _selectionRender.ZoomLevel = camera.ZoomLevel;
            _selectionRender.ViewportWidth = (int)camera.ViewportWidth;
            _selectionRender.ViewportHeight = (int)camera.ViewportHeight;
            _selectionRender.MapData = mapData;
            _selectionRender.Render(canvas, mapData.MapWidth, mapData.MapHeight);
        }

        if (_overlayRender != null)
        {
            _overlayRender.ViewportWidth = (int)camera.ViewportWidth;
            _overlayRender.ViewportHeight = (int)camera.ViewportHeight;
            _overlayRender.Render(canvas);
        }

        if (_geoRulerRender != null)
        {
            _geoRulerRender.OffsetX = camera.OffsetX;
            _geoRulerRender.OffsetY = camera.OffsetY;
            _geoRulerRender.ZoomLevel = camera.ZoomLevel;
            _geoRulerRender.ViewportWidth = (int)camera.ViewportWidth;
            _geoRulerRender.ViewportHeight = (int)camera.ViewportHeight;
            _geoRulerRender.MapWidth = mapData.MapWidth;
            _geoRulerRender.MapHeight = mapData.MapHeight;
            _geoRulerRender.Render(canvas);
        }
    }

    public void Invalidate()
    {
        InvalidateCallback?.Invoke();
    }

    public bool LoadViewLayerImage(string imagePath)
    {
        try
        {
            if (!System.IO.File.Exists(imagePath))
                return false;

            int originalWidth = 0;
            int originalHeight = 0;
            using (var stream = System.IO.File.OpenRead(imagePath))
            {
                using var codec = SKCodec.Create(stream);
                if (codec == null) return false;
                originalWidth = codec.Info.Width;
                originalHeight = codec.Info.Height;
            }

            const int maxDimension = 4096;
            double scale = 1.0;
            if (originalWidth > maxDimension || originalHeight > maxDimension)
                scale = Math.Min(maxDimension / (double)originalWidth, maxDimension / (double)originalHeight);

            int sampleWidth = (int)(originalWidth * scale);
            int sampleHeight = (int)(originalHeight * scale);

            System.Diagnostics.Debug.WriteLine($"[视图层] 加载图片: {imagePath}, 原始尺寸: {originalWidth}x{originalHeight}, 缩放: {scale:F3}");

            _viewLayerImage?.Dispose();
            _viewLayerImage = null;

            using (var stream = System.IO.File.OpenRead(imagePath))
            {
                if (scale < 1.0)
                {
                    using var originalBitmap = SKBitmap.Decode(stream);
                    if (originalBitmap != null)
                        _viewLayerImage = originalBitmap.Resize(new SKImageInfo(sampleWidth, sampleHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                }
                else
                {
                    _viewLayerImage = SKBitmap.Decode(stream);
                }
            }

            if (_viewLayerImage == null) return false;

            _viewLayerOriginalWidth = _viewLayerImage.Width;
            _viewLayerOriginalHeight = _viewLayerImage.Height;
            _viewLayerImagePath = imagePath;
            _viewLayerVisible = true;
            _viewLayerCacheValid = false;

            _viewLayerImageProvider.UpdateBitmap(_viewLayerImage);

            System.Diagnostics.Debug.WriteLine($"[视图层] 加载成功, 实际尺寸: {_viewLayerOriginalWidth}x{_viewLayerOriginalHeight}");
            Invalidate();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[视图层] 加载失败: {ex.Message}");
            return false;
        }
    }

    public void ClearViewLayerImage()
    {
        _viewLayerImage?.Dispose();
        _viewLayerImage = null;
        _viewLayerCache?.Dispose();
        _viewLayerCache = null;
        _viewLayerImagePath = string.Empty;
        _viewLayerVisible = false;
        _viewLayerCacheValid = false;
        _viewLayerImageProvider.UpdateBitmap(null);
        Invalidate();
    }

    public bool ViewLayerVisible
    {
        get => _viewLayerVisible;
        set
        {
            if (_viewLayerVisible == value) return;
            _viewLayerVisible = value;
            _viewLayerCacheValid = false;
            Invalidate();
        }
    }

    public float ViewLayerOpacity
    {
        get => _viewLayerOpacity;
        set
        {
            float v = Math.Max(0f, Math.Min(1f, value));
            if (Math.Abs(_viewLayerOpacity - v) < 0.001f) return;
            _viewLayerOpacity = v;
            _viewLayerCacheValid = false;
            Invalidate();
        }
    }

    public void InvalidateViewLayerCache()
    {
        _viewLayerCacheValid = false;
    }

    private void RenderViewLayer(SKCanvas canvas, Camera camera)
    {
        if (!_viewLayerVisible || _viewLayerImage == null || _backgroundRender == null) return;

        int viewportWidth = canvas.DeviceClipBounds.Width;
        int viewportHeight = canvas.DeviceClipBounds.Height;
        double currentZoom = camera.ZoomLevel;
        double currentOffsetX = camera.OffsetX;
        double currentOffsetY = camera.OffsetY;

        bool cacheValid = _viewLayerCacheValid &&
                          _viewLayerCache != null &&
                          Math.Abs(_viewLayerCacheZoom - currentZoom) < 0.001 &&
                          Math.Abs(_viewLayerCacheOffsetX - currentOffsetX) < 0.5 &&
                          Math.Abs(_viewLayerCacheOffsetY - currentOffsetY) < 0.5 &&
                          _viewLayerCache.Width == viewportWidth &&
                          _viewLayerCache.Height == viewportHeight;

        if (cacheValid)
        {
            canvas.DrawBitmap(_viewLayerCache, 0, 0);
            return;
        }

        _viewLayerCache?.Dispose();
        _viewLayerCache = null;

        _viewLayerCache = new SKBitmap(viewportWidth, viewportHeight);
        using var cacheCanvas = new SKCanvas(_viewLayerCache);
        cacheCanvas.Clear(SKColors.Transparent);

        const double baseHexSize = 20.0;

        double mapPixelWidth = _backgroundRender.MapPixelWidth * currentZoom;
        double mapPixelHeight = _backgroundRender.MapPixelHeight * currentZoom;

        double mapOffsetX = currentOffsetX - 20 * currentZoom;
        double mapOffsetY = currentOffsetY - 20 * currentZoom * 0.866;

        float destLeft = (float)mapOffsetX;
        float destTop = (float)mapOffsetY;
        float destRight = destLeft + (float)mapPixelWidth;
        float destBottom = destTop + (float)mapPixelHeight;

        float visibleLeft = Math.Max(destLeft, 0);
        float visibleTop = Math.Max(destTop, 0);
        float visibleRight = Math.Min(destRight, viewportWidth);
        float visibleBottom = Math.Min(destBottom, viewportHeight);

        if (visibleLeft < visibleRight && visibleTop < visibleBottom)
        {
            float relLeft = (visibleLeft - destLeft) / (float)mapPixelWidth;
            float relTop = (visibleTop - destTop) / (float)mapPixelHeight;
            float relRight = (visibleRight - destLeft) / (float)mapPixelWidth;
            float relBottom = (visibleBottom - destTop) / (float)mapPixelHeight;

            float srcLeft = relLeft * _viewLayerOriginalWidth;
            float srcTop = relTop * _viewLayerOriginalHeight;
            float srcRight = relRight * _viewLayerOriginalWidth;
            float srcBottom = relBottom * _viewLayerOriginalHeight;

            var srcRect = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
            var destRect = new SKRect(visibleLeft, visibleTop, visibleRight, visibleBottom);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(255, 255, 255, (byte)(_viewLayerOpacity * 255))
            };
            cacheCanvas.DrawBitmap(_viewLayerImage, srcRect, destRect, paint);
        }

        _viewLayerCacheZoom = currentZoom;
        _viewLayerCacheOffsetX = currentOffsetX;
        _viewLayerCacheOffsetY = currentOffsetY;
        _viewLayerCacheValid = true;

        using var drawPaint = new SKPaint();
        canvas.DrawBitmap(_viewLayerCache, 0, 0, drawPaint);
    }

    public void InvalidateTerrainCache()
    {
        _backgroundRender?.InvalidateCache();
    }

    public void InvalidateProvinceCache()
    {
        _provinceRender?.InvalidateCache();
    }

    public void InvalidateCoastCache(MapData mapData)
    {
        _backgroundRender?.InvalidateCoastCache(mapData);
    }

    public void InvalidateCoastCacheRegion(MapData mapData, int centerCol, int centerRow, int radius)
    {
        _backgroundRender?.InvalidateCoastCacheRegion(mapData, centerCol, centerRow, radius);
    }

    public void InvalidateCoastCacheFull(MapData mapData)
    {
        _backgroundRender?.InvalidateCoastCacheFull(mapData);
    }

    private void OnCoastCacheUpdated()
    {
        InvalidateTerrainCache();
        Invalidate();
    }

    public (int col, int row) ScreenToHex(double screenX, double screenY)
    {
        double scaledHexWidth = Camera.HexHorizontalSpacing;
        double scaledHexHeight = Camera.HexVerticalSpacing;
        int col = (int)Math.Round(screenX / scaledHexWidth);
        double rowOffset = (col % 2 == 1) ? (scaledHexHeight / 2) : 0;
        int row = (int)Math.Round((screenY - rowOffset) / scaledHexHeight);
        return (col, row);
    }

    public (double x, double y) HexToScreen(int col, int row)
    {
        double x = col * Camera.HexHorizontalSpacing;
        double y = row * Camera.HexVerticalSpacing;
        if (col % 2 == 1)
            y += Camera.HexVerticalSpacing / 2;
        return (x, y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewLayerImage?.Dispose();
        _viewLayerCache?.Dispose();
        _landTerrainsRender?.Dispose();
        _backgroundRender?.Dispose();
        _provinceRender?.Dispose();
        _buildingRender?.Dispose();
        _armyRender?.Dispose();
        _trapRender?.Dispose();
        _reinforceRender?.Dispose();
        _reinforceRenderNew?.Dispose();
        _strategicConstructionRender?.Dispose();
        _airForceRender?.Dispose();
        _weatherRender?.Dispose();
        _mapCaseRender?.Dispose();
        _selectionRender?.Dispose();
        _overlayRender?.Dispose();
        _geoRulerRender?.Dispose();
        _legionDomainRender?.Dispose();
        _belongFlagRender?.Dispose();
    }
}