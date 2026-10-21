using System.Diagnostics;
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
    private ClippedTileRender? _clippedTileRender;
    private SelectionRender? _selectionRender;
    private OverlayRender? _overlayRender;
    private GeoRulerRender? _geoRulerRender;
    private LegionDomainRender? _legionDomainRender;
    private BelongFlagRender? _belongFlagRender;
    private CapitalFlagRender? _capitalFlagRender;

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
    public bool EnableCapitalFlagRender { get; set; }
    public bool EnableReinforceNewRender { get; set; }
    public bool EnableStrategicConstructionRender { get; set; }
    public bool EnableAirForceRender { get; set; }
    public bool EnableWeatherRender { get; set; }
    public bool EnableMapCaseRender { get; set; }
    public bool EnableClippedTileRender { get; set; }

    /// <summary>
    /// 建筑名称开关。值由引擎保存：建筑渲染器是按需创建的（首次打开建筑层的那一帧），
    /// 换图后也会重建，新建的渲染器要沿用这里的值，否则地形模式"不显示名称"之类的设置会丢。
    /// </summary>
    public bool ShowBuildingNames
    {
        get => _showBuildingNames;
        set
        {
            _showBuildingNames = value;
            if (_buildingRender != null)
                _buildingRender.ShowBuildingNames = value;
        }
    }

    private bool _showBuildingNames = true;

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

    // ---------- 手绘边界覆盖层（省份编辑模式 U 键） ----------
    //
    // 掩码按"地图逻辑像素"组织（与 Camera 的世界坐标同一套），所以
    // 掩码像素 (x,y) 在屏幕上就是 (x * zoom + offsetX, y * zoom + offsetY)。
    //
    // 画法用点阵而不是位图缩放：位图在有缩放时靠采样，缩小时细线会被采样漏掉，
    // 表现为"线一段一段断开"。点阵按每个边界像素画一个点、点径随缩放调整，
    // 无论放大缩小都保证连成一线。
    //
    // 点坐标只在掩码变动时重建一次，变换交给 canvas 的 Translate/Scale，
    // 所以每帧只是提交一次 DrawPoints，不需要重新遍历掩码。
    private bool[]? _boundaryMask;
    private int _boundaryMaskWidth;
    private int _boundaryMaskHeight;
    private SKPoint[] _boundaryPoints = [];
    private bool _boundaryPointsDirty;

    // 源图在磁盘上的真实像素尺寸。
    // 注意与上面两个区分：_viewLayerOriginalWidth/Height 是"加载后位图"的尺寸，
    // 图片超过 maxDimension 时会被缩小，两者在大图上并不相等。
    // 需要按图片比例做坐标映射的功能（如 OCR 生成建筑）必须用源图尺寸。
    private int _viewLayerSourceWidth;
    private int _viewLayerSourceHeight;

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

    public bool IsHelpFading => _overlayRender?.IsFading ?? false;

    public int HelpBottomMargin
    {
        get => _helpBottomMargin;
        set
        {
            _helpBottomMargin = Math.Max(0, value);
            if (_overlayRender != null) _overlayRender.HelpBottomMargin = _helpBottomMargin;
        }
    }

    private int _helpBottomMargin;

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

    /// <summary>构造计时辅助：用于定位「进入渲染场景」的耗时分布</summary>
    private static T Timed<T>(string name, Func<T> factory)
    {
        var sw = Stopwatch.StartNew();
        var result = factory();
        Debug.WriteLine($"[Timing]   {name}: {sw.ElapsedMilliseconds}ms");
        return result;
    }

    private void InitializeTerrainRenderers()
    {
        if (_backgroundRender != null) return;

        var total = Stopwatch.StartNew();
        Debug.WriteLine("[Timing] InitializeTerrainRenderers 开始");

        _landTerrainsRender = Timed("LandTerrainsRender", () => new LandTerrainsRender());
        _backgroundRender = Timed("BackGroundRender", () => new BackGroundRender());
        _backgroundRender.SetLandTerrainsRender(_landTerrainsRender);
        _backgroundRender.ShowLayer2 = _showLayer2;
        _backgroundRender.ShowGridLines = _showHexBorders;
        _backgroundRender.CoastCacheUpdated += OnCoastCacheUpdated;
        _provinceRender = Timed("ProvinceRender", () => new ProvinceRender());
        _selectionRender = Timed("SelectionRender", () => new SelectionRender());
        _overlayRender = Timed("OverlayRender", () => new OverlayRender
        {
            HelpText = _helpText,
            ModeName = _modeName,
            ShowHelp = _showHelp,
            ShowModeName = _showModeName,
            HelpBottomMargin = _helpBottomMargin
        });
        _geoRulerRender = Timed("GeoRulerRender", () => new GeoRulerRender());
        _legionDomainRender = Timed("LegionDomainRender", () => new LegionDomainRender());
        _belongFlagRender = Timed("BelongFlagRender", () => new BelongFlagRender());
        _capitalFlagRender = Timed("CapitalFlagRender", () => new CapitalFlagRender());

        Debug.WriteLine($"[Timing] InitializeTerrainRenderers 总计: {total.ElapsedMilliseconds}ms");
    }

    private void InitializeEntityRenderers(Camera camera, MapData mapData)
    {
        // 只创建当前开关启用（或可能被渲染路径用到）的渲染器，避免为未启用的图层
        // 白付构造成本。Render 每帧都会调用本方法，开关之后再打开时会自动补建。
        var total = Stopwatch.StartNew();
        bool anyCreated = false;

        if (_buildingRender == null && EnableBuildingRender)
        {
            anyCreated = true;
            _buildingRender = Timed("BuildingRender", () => new BuildingRender(camera, mapData) { ShowBuildingNames = _showBuildingNames });
        }
        if (_armyRender == null && EnableArmyRender)
        {
            anyCreated = true;
            _armyRender = Timed("ArmyRender", () => new ArmyRender(camera, mapData));
        }
        if (_trapRender == null && EnableTrapRender)
        {
            anyCreated = true;
            _trapRender = Timed("TrapRender", () => new TrapRender(camera, mapData));
        }
        // 旧版增援渲染器仅在「未启用新版增援、且启用部队渲染」时才会被用到（见 Render）。
        if (_reinforceRender == null && !EnableReinforceNewRender && EnableArmyRender)
        {
            anyCreated = true;
            _reinforceRender = Timed("ReinforceRender", () => new ReinforceRender(camera, mapData));
        }
        if (_reinforceRenderNew == null && EnableReinforceNewRender)
        {
            anyCreated = true;
            _reinforceRenderNew = Timed("ReinforceRenderNew", () => new ReinforceRenderNew(camera, mapData));
        }
        if (_strategicConstructionRender == null && EnableStrategicConstructionRender)
        {
            anyCreated = true;
            _strategicConstructionRender = Timed("StrategicConstructionRender", () => new StrategicConstructionRender(camera, mapData));
        }
        if (_airForceRender == null && EnableAirForceRender)
        {
            anyCreated = true;
            _airForceRender = Timed("AirForceRender", () => new AirForceRender(camera, mapData));
        }
        if (_weatherRender == null && EnableWeatherRender)
        {
            anyCreated = true;
            _weatherRender = Timed("WeatherRender", () => new WeatherRender(camera, mapData));
        }
        if (_mapCaseRender == null && EnableMapCaseRender)
        {
            anyCreated = true;
            _mapCaseRender = Timed("MapCaseRender", () => new MapCaseRender(camera, mapData));
        }

        if (anyCreated)
            Debug.WriteLine($"[Timing] InitializeEntityRenderers 总计: {total.ElapsedMilliseconds}ms");
    }

    // 实体渲染器构造时就绑定了 Camera 与 MapData（存在只读字段里）。打开 / 新建 / 切换场景会换掉这两样，
    // 引擎却还是同一个：不重建的话，这些层会一直画上一张地图，而且不再跟随平移缩放。
    private MapData? _entityMapData;
    private Camera? _entityCamera;

    /// <summary>
    /// 本帧的地图或相机与实体渲染器绑定的不是同一个对象时，释放实体渲染器（下面按需重建）；
    /// 换了地图时还要让按地图缓存的数据（地形 / 海岸、省份、领域配色、国旗图集）失效。
    /// </summary>
    private void RebindEntityRenderersIfNeeded(Camera camera, MapData mapData)
    {
        if (ReferenceEquals(_entityMapData, mapData) && ReferenceEquals(_entityCamera, camera)) return;

        bool mapReplaced = _entityMapData != null && !ReferenceEquals(_entityMapData, mapData);
        _entityMapData = mapData;
        _entityCamera = camera;

        DisposeEntityRenderers();
        if (mapReplaced) InvalidateMapCaches(mapData);
    }

    private void DisposeEntityRenderers()
    {
        _buildingRender?.Dispose(); _buildingRender = null;
        _armyRender?.Dispose(); _armyRender = null;
        _trapRender?.Dispose(); _trapRender = null;
        _reinforceRender?.Dispose(); _reinforceRender = null;
        _reinforceRenderNew?.Dispose(); _reinforceRenderNew = null;
        _strategicConstructionRender?.Dispose(); _strategicConstructionRender = null;
        _airForceRender?.Dispose(); _airForceRender = null;
        _weatherRender?.Dispose(); _weatherRender = null;
        _mapCaseRender?.Dispose(); _mapCaseRender = null;
    }

    private void InvalidateMapCaches(MapData mapData)
    {
        _backgroundRender?.InvalidateCache();
        // 海岸装饰数组按地图尺寸分配：尺寸不同的新地图必须整张重算，否则海岸线错位
        _backgroundRender?.InvalidateCoastCacheFull(mapData);
        _provinceRender?.InvalidateCache();
        _legionDomainRender?.InvalidateCache();
        _belongFlagRender?.InvalidateCache();
        _capitalFlagRender?.InvalidateProvinceLegionCache();
        _viewLayerCacheValid = false;
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
        // 战术图集刚就绪：之前按"没有头像"缓存下来的将领头像要作废，否则一直显示不出来
        _armyRender?.ClearGeneralHeadCache();
        _reinforceRender?.ClearGeneralHeadCache();
        _reinforceRenderNew?.ClearGeneralHeadCache();
    }

    public void Render(SKCanvas canvas, MapData mapData, Camera camera)
    {
        if (canvas == null || mapData == null) return;

        RenderProfiler.BeginFrame(
            $"bg={EnableBackgroundRender} prov={EnableProvinceRender} dom={EnableLegionDomainRender} " +
            $"bld={EnableBuildingRender} army={EnableArmyRender} flag={EnableBelongFlagRender} " +
            $"clip={EnableClippedTileRender} sel={EnableSelectionRender} names={ShowBuildingNames} " +
            $"zoom={camera.ZoomLevel:0.00}");

        canvas.Clear(SKColors.Black);

        InitializeTerrainRenderers();
        RebindEntityRenderersIfNeeded(camera, mapData);
        InitializeEntityRenderers(camera, mapData);

        long t0 = Stopwatch.GetTimestamp();
        if (EnableBackgroundRender && _backgroundRender != null)
        {
            _backgroundRender.SetCameraState(camera.OffsetX, camera.OffsetY, camera.ZoomLevel,
                (int)camera.ViewportWidth, (int)camera.ViewportHeight,
                EnableBackgroundRender, EnableTerrainsRender);
            _backgroundRender.Render(canvas, mapData);
        }
        RenderProfiler.Add("background", t0);

        t0 = Stopwatch.GetTimestamp();
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
        RenderProfiler.Add("province", t0);

        t0 = Stopwatch.GetTimestamp();
        // 手绘边界：压在省区配色之上，方便对着省区看边界描得对不对
        RenderProvinceBoundary(canvas, camera);
        RenderProfiler.Add("province_boundary", t0);

        t0 = Stopwatch.GetTimestamp();
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
        RenderProfiler.Add("legion_domain", t0);

        t0 = Stopwatch.GetTimestamp();
        if (EnableBuildingRender && _buildingRender != null)
        {
            _buildingRender.Render(canvas, true);
        }
        RenderProfiler.Add("building", t0);

        t0 = Stopwatch.GetTimestamp();
        if (EnableArmyRender && _armyRender != null)
        {
            _armyRender.Render(canvas);
        }
        RenderProfiler.Add("army", t0);

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

        // 归属国旗：压在建筑、单位、陷阱、援军、战略建筑、空军等**实体层之上**，
        // 但仍在 天气 / 地图框 / 选区 / 首都国旗 之下。
        // 想再往上抬（例如压过天气和选区），把这一段整体往下移即可。
        t0 = Stopwatch.GetTimestamp();
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
        RenderProfiler.Add("belong_flag", t0);

        // 将领头像压在归属国旗之上（对齐 VB MainRender：国旗之后再画头像，免得被国旗挡住）。
        // 头像位置由本帧前面的 _armyRender.Render 收集。
        if (EnableArmyRender && _armyRender != null)
        {
            _armyRender.RenderGeneralHeadsOnly(canvas);
        }

        if (EnableWeatherRender && _weatherRender != null)
        {
            _weatherRender.Render(canvas);
        }

        if (EnableMapCaseRender && _mapCaseRender != null)
        {
            _mapCaseRender.Render(canvas);
        }

        // 数据范围外的格子：红叉画在实体层之上、选区之下（对齐 VB 版各模式 DrawSkia 里的裁剪区标记）
        t0 = Stopwatch.GetTimestamp();
        if (EnableClippedTileRender)
        {
            _clippedTileRender ??= new ClippedTileRender();
            _clippedTileRender.Render(canvas, mapData, camera.OffsetX, camera.OffsetY, camera.ZoomLevel,
                (int)camera.ViewportWidth, (int)camera.ViewportHeight);
        }
        RenderProfiler.Add("clipped", t0);

        t0 = Stopwatch.GetTimestamp();
        // 参考底图（视图层）叠在全部地图层之上、选区之下（对齐 VB MainRender 的绘制顺序）：
        // 半透明底图要能盖在省份配色与实体上，描图时才看得清两者是否对齐。
        RenderViewLayer(canvas, camera);
        RenderProfiler.Add("view_layer", t0);

        t0 = Stopwatch.GetTimestamp();
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
        RenderProfiler.Add("selection", t0);

        // 首都国旗：画在实体与选区之上（对齐 VB 版 Modifier.DrawSkia 的最后绘制），
        // 但仍位于帮助文字/模式名与经纬度标尺之下。
        t0 = Stopwatch.GetTimestamp();
        if (_capitalFlagRender != null)
        {
            _capitalFlagRender.EnableCapitalFlagRender = EnableCapitalFlagRender;
            _capitalFlagRender.OffsetX = camera.OffsetX;
            _capitalFlagRender.OffsetY = camera.OffsetY;
            _capitalFlagRender.ZoomLevel = camera.ZoomLevel;
            _capitalFlagRender.ViewportWidth = (int)camera.ViewportWidth;
            _capitalFlagRender.ViewportHeight = (int)camera.ViewportHeight;
            _capitalFlagRender.Render(canvas, mapData);
        }
        RenderProfiler.Add("capital_flag", t0);

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

        RenderProfiler.EndFrame();
    }

    public void Invalidate()
    {
        InvalidateCallback?.Invoke();
    }

    /// <summary>已加载的视图层背景图路径（未加载时为空串）</summary>
    public string ViewLayerImagePath => _viewLayerImagePath;

    /// <summary>
    /// 背景图在磁盘上的原始像素宽（未加载时为 0）。
    /// <para>
    /// 这是 <c>SKCodec</c> 读出的源图尺寸；引擎内的 <c>_viewLayerOriginalWidth</c> 是加载后的
    /// 位图尺寸（超过 4096 会被缩放），两者在大图上不同。需要按图片比例做坐标映射的功能要用这个。
    /// </para>
    /// </summary>
    public int ViewLayerSourceWidth => _viewLayerSourceWidth;

    /// <summary>背景图在磁盘上的原始像素高（未加载时为 0）</summary>
    public int ViewLayerSourceHeight => _viewLayerSourceHeight;

    /// <summary>
    /// 设置手绘边界覆盖层（红色）。传 null 关闭。
    /// <para>
    /// <paramref name="mask"/> 按"地图逻辑像素"组织，长度须为
    /// <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>。
    /// 掩码是原地修改的，所以每次画完都要再调一次本方法让位图缓存失效。
    /// </para>
    /// </summary>
    public void SetProvinceBoundaryOverlay(bool[]? mask, int pixelWidth, int pixelHeight)
    {
        _boundaryMask = mask;
        _boundaryMaskWidth = pixelWidth;
        _boundaryMaskHeight = pixelHeight;
        _boundaryPointsDirty = true;
        Invalidate();
    }

    /// <summary>把掩码里为 true 的像素收集成点阵（只在掩码变动后调用）</summary>
    private void RebuildBoundaryPoints()
    {
        _boundaryPointsDirty = false;

        if (_boundaryMask == null || _boundaryMaskWidth <= 0 || _boundaryMaskHeight <= 0)
        {
            _boundaryPoints = [];
            return;
        }

        long expected = (long)_boundaryMaskWidth * _boundaryMaskHeight;
        if (_boundaryMask.Length < expected)
        {
            _boundaryPoints = [];
            return;
        }

        var points = new List<SKPoint>(1024);

        for (int y = 0; y < _boundaryMaskHeight; y++)
        {
            int rowBase = y * _boundaryMaskWidth;

            for (int x = 0; x < _boundaryMaskWidth; x++)
            {
                if (!_boundaryMask[rowBase + x]) continue;

                // 加 0.5 取像素中心，否则点会压在像素格的角上、相邻点看着错位
                points.Add(new SKPoint(x + 0.5f, y + 0.5f));
            }
        }

        _boundaryPoints = points.Count > 0 ? points.ToArray() : [];
    }

    /// <summary>把手绘边界叠加到画布上（在省区层之上、UI 层之下）</summary>
    private void RenderProvinceBoundary(SKCanvas canvas, Camera camera)
    {
        if (_boundaryMask == null) return;

        if (_boundaryPointsDirty)
            RebuildBoundaryPoints();

        if (_boundaryPoints.Length == 0) return;

        double zoom = camera.ZoomLevel;
        if (zoom <= 0) return;

        // 点径要满足两件事：
        //   1) 缩小时不能被采掉 —— 屏幕上至少 1.5 像素；
        //   2) 放大时相邻点必须重叠 —— 屏幕点径要超过相邻掩码像素的屏幕间距（= zoom）。
        // 取两者的较大值，再除以 zoom 换算回"变换前的坐标系"。
        float screenDiameter = (float)Math.Max(1.5, zoom * 1.45);
        float strokeWidth = (float)(screenDiameter / zoom);

        canvas.Save();
        canvas.Translate((float)camera.OffsetX, (float)camera.OffsetY);
        canvas.Scale((float)zoom);

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 40, 40, 190),
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        canvas.DrawPoints(SKPointMode.Points, _boundaryPoints, paint);
        canvas.Restore();
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
            _viewLayerSourceWidth = originalWidth;
            _viewLayerSourceHeight = originalHeight;
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
        _viewLayerSourceWidth = 0;
        _viewLayerSourceHeight = 0;
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
        // 首都国旗的"省区→归属"表依赖省区数据
        _capitalFlagRender?.InvalidateProvinceLegionCache();
    }

    public void InvalidateLegionDomainCache()
    {
        _legionDomainRender?.InvalidateCache();
        // 宿主在每次 DataModified 都会调到这里，顺带让首都国旗的省区→归属表失效
        _capitalFlagRender?.InvalidateProvinceLegionCache();
    }

    public void InvalidateBelongFlagCache()
    {
        _belongFlagRender?.InvalidateCache();
        _capitalFlagRender?.InvalidateProvinceLegionCache();
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
        _clippedTileRender?.Dispose();
        _selectionRender?.Dispose();
        _overlayRender?.Dispose();
        _geoRulerRender?.Dispose();
        _legionDomainRender?.Dispose();
        _belongFlagRender?.Dispose();
        _capitalFlagRender?.Dispose();
    }
}