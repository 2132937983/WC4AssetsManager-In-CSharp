using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Rendering.Helpers;
using IOPath = System.IO.Path;

namespace WC4MapEditor.Rendering.Skia;

public class BackGroundRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private static readonly SKColor DEFAULT_SEA_COLOR = new(0, 100, 150);
    private static readonly SKColor DEFAULT_LAND_COLOR = new(139, 119, 101);
    private static readonly SKColor GRID_LINE_COLOR = new(255, 50, 50, 180);

    private const byte OCEAN_TILE_TYPE = 1;
    private const byte COAST_TILE_TYPE = 31;

    private readonly ReaderWriterLockSlim _stateLock = new();

    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _showGridLines;
    private bool _enableBackgroundRender = true;
    private bool _enableTerrainsRender = true;
    private bool _enableCoastRender = true;
    private bool _showLayer2;

    private SKPaint? _seaPaint;
    private SKPaint? _landPaint;
    private SKPaint? _gridPaint;
    private SKPaint? _textPaint;
    private SKPaint? _riverPaint;
    private SKFont? _textFont;
    private SKBitmap? _seaTexture;
    private SKBitmap? _landTexture;
    private SKShader? _seaShader;
    private SKShader? _landShader;
    private SKImage? _landImage;

    private readonly Dictionary<float, SKPath> _hexPathCache = new();

    private LandTerrainsRender? _landTerrainsRender;

    private CoastHelper? _coastHelper;
    private CoastMaskProcessor? _coastMaskProcessor;

    // Ping-pong cache surfaces. While the camera pans, the previous frame's
    // surface still holds valid content for the overlapping region, so only
    // the newly exposed strip needs to be rendered instead of everything.
    private SKSurface? _bgCacheSurfaceA;
    private SKSurface? _bgCacheSurfaceB;
    private SKImage? _bgCacheImage;
    // World-space center (camera offset) that the cache was rendered for.
    private double _cachedBgCenterX = double.NaN;
    private double _cachedBgCenterY = double.NaN;
    private double _cachedBgZoomLevel;
    private int _cachedBgWidth;
    private int _cachedBgHeight;
    private bool _cachedBgShowLayer2;
    private bool _cachedBgShowGridLines;
    private int _cacheW;
    private int _cacheH;

    private byte[,]? _coastDecorationArray;
    private readonly object _arraySwapLock = new();
    private readonly object _coastTaskLock = new();

    private SKPicture? _hexLabelsPicture;
    private double _cachedLabelsOffsetX = double.NaN;
    private double _cachedLabelsOffsetY = double.NaN;
    private double _cachedLabelsZoomLevel;
    private int _cachedLabelsViewportWidth;
    private int _cachedLabelsViewportHeight;
    private HexLabelMode _cachedLabelMode = HexLabelMode.Hidden;

    public enum HexLabelMode { Hidden = 0, Index = 1, Coordinate = 2 }

    private HexLabelMode _hexLabelMode = HexLabelMode.Hidden;

    // Batch camera/state update: one write lock instead of ten.
    public void SetCameraState(double offsetX, double offsetY, double zoomLevel, int viewportWidth, int viewportHeight,
        bool enableBackgroundRender, bool enableTerrainsRender)
    {
        _stateLock.EnterWriteLock();
        try
        {
            _offsetX = offsetX;
            _offsetY = offsetY;
            double z = Math.Max(0.1, Math.Min(5.0, zoomLevel));
            _zoomLevel = z;
            _viewportWidth = Math.Max(1, viewportWidth);
            _viewportHeight = Math.Max(1, viewportHeight);
            _enableBackgroundRender = enableBackgroundRender;
            _enableTerrainsRender = enableTerrainsRender;
        }
        finally { _stateLock.ExitWriteLock(); }
    }

    public double OffsetX
    {
        get { _stateLock.EnterReadLock(); try { return _offsetX; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { if (Math.Abs(_offsetX - value) > 0.001) _offsetX = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetY
    {
        get { _stateLock.EnterReadLock(); try { return _offsetY; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { if (Math.Abs(_offsetY - value) > 0.001) _offsetY = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double ZoomLevel
    {
        get { _stateLock.EnterReadLock(); try { return _zoomLevel; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { double v = Math.Max(0.1, Math.Min(5.0, value)); if (Math.Abs(_zoomLevel - v) > 0.001) _zoomLevel = v; } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportWidth
    {
        get { _stateLock.EnterReadLock(); try { return _viewportWidth; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportWidth = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportHeight
    {
        get { _stateLock.EnterReadLock(); try { return _viewportHeight; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportHeight = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool ShowGridLines
    {
        get { _stateLock.EnterReadLock(); try { return _showGridLines; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _showGridLines = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool EnableBackgroundRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableBackgroundRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableBackgroundRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool EnableTerrainsRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableTerrainsRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableTerrainsRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool EnableCoastRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableCoastRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableCoastRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool ShowLayer2
    {
        get { _stateLock.EnterReadLock(); try { return _showLayer2; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _showLayer2 = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public HexLabelMode LabelMode
    {
        get { _stateLock.EnterReadLock(); try { return _hexLabelMode; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _hexLabelMode = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool ShowHexLabels
    {
        get => LabelMode != HexLabelMode.Hidden;
        set => LabelMode = value ? HexLabelMode.Index : HexLabelMode.Hidden;
    }

    public int MapWidth { get; private set; }
    public int MapHeight { get; private set; }

    public double MapPixelWidth => MapWidth * HEX_HORIZONTAL_SPACING + HEX_HORIZONTAL_SPACING / 2;
    public double MapPixelHeight => MapHeight * HEX_VERTICAL_SPACING + HEX_VERTICAL_SPACING / 2;

    public BackGroundRender()
    {
        InitializePaintObjects();
        InitializeCoastRender();
    }

    private void InitializePaintObjects()
    {
        LoadTextures();

        _seaPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        if (_seaShader != null) _seaPaint.Shader = _seaShader;
        else _seaPaint.Color = DEFAULT_SEA_COLOR;

        _landPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        if (_landShader != null) _landPaint.Shader = _landShader;
        else _landPaint.Color = DEFAULT_LAND_COLOR;

        _gridPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, Color = GRID_LINE_COLOR, StrokeWidth = 1 };
        _textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _textFont = new SKFont(SKTypeface.Default, 10);
        _riverPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(0, 0, 255, 255), StrokeWidth = 1 };
    }

    private void LoadTextures()
    {
        try
        {
            var configManager = ConfigManager.Instance;
            configManager.Initialize();
            string texturePath = configManager.GetTexturePath("MapTerrian");
            string seaPath = IOPath.Combine(texturePath, "MapSea.png");
            string landPath = IOPath.Combine(texturePath, "MapLand.png");

            if (File.Exists(seaPath))
            {
                _seaTexture = SKBitmap.Decode(seaPath);
                if (_seaTexture != null)
                    _seaShader = SKShader.CreateBitmap(_seaTexture, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            }
            if (File.Exists(landPath))
            {
                _landTexture = SKBitmap.Decode(landPath);
                if (_landTexture != null)
                    _landShader = SKShader.CreateBitmap(_landTexture, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                try
                {
                    using var fs = File.OpenRead(landPath);
                    _landImage = SKImage.FromEncodedData(fs);
                }
                catch { _landImage = null; }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BackGroundRender] Texture load failed: {ex.Message}");
        }
    }

    private void InitializeCoastRender()
    {
        try
        {
            _coastHelper = CoastHelper.Instance;
            _coastMaskProcessor = CoastMaskProcessor.Instance;

            if (!_coastMaskProcessor.IsInitialized)
                _coastMaskProcessor.Initialize(_coastHelper);

            // 缓存命中时无需预生成六边形图块：
            // 六边形图块（_hexagonCoastCache）仅被「最终图集构建」使用，而缓存已提供等价产物
            if (_coastMaskProcessor.LoadedFromDiskCache || _coastMaskProcessor.HasFinalAtlas) return;

            Task.Run(() => _coastHelper.PreGenerateAllHexagonCoasts(null));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BackGroundRender] 海岸线组件初始化失败: {ex.Message}");
        }
    }

    public void Render(SKCanvas canvas, MapData? mapData)
    {
        if (canvas == null || ViewportWidth <= 0 || ViewportHeight <= 0) return;

        if (mapData != null)
        {
            MapWidth = mapData.MapWidth;
            MapHeight = mapData.MapHeight;
            if (_coastDecorationArray == null && MapWidth > 0 && MapHeight > 0)
                InitializeCoastCacheArray(mapData);
        }

        if (mapData == null || MapWidth <= 0 || MapHeight <= 0)
        {
            RenderFallbackBackground(canvas);
            return;
        }

        RenderBackground(canvas, mapData);
        RenderHexLabels(canvas);
    }

    // Renders the complete map (base textures, coastlines, rivers, terrain
    // decorations, layer 2) to the canvas without any viewport caching.
    // offsetX/offsetY are camera offsets: hex (0,0) center maps to (offsetX, offsetY).
    public void RenderFullMap(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        double zoomLevel, int width, int height, bool showLayer2 = false, bool showGridLines = false)
    {
        if (canvas == null || mapData == null || width <= 0 || height <= 0) return;

        MapWidth = mapData.MapWidth;
        MapHeight = mapData.MapHeight;

        SetCameraState(offsetX, offsetY, zoomLevel, width, height, true, true);
        ShowGridLines = showGridLines;
        ShowLayer2 = showLayer2;

        PrepareCoastComposition();
        PrecomputeCoastDecorations(mapData);

        RenderCacheContent(canvas, mapData, offsetX, offsetY, zoomLevel, showGridLines, showLayer2,
            true, true, width, height);

        if (_hexLabelMode != HexLabelMode.Hidden)
            RenderHexLabels(canvas);
    }

    // Ensures the coast atlas/mask/composition pipeline is ready synchronously,
    // so off-screen renders (screenshots) are never missing coastline decorations.
    public void PrepareCoastComposition()
    {
        if (_coastHelper == null || _coastMaskProcessor == null) return;

        if (!_coastMaskProcessor.IsInitialized)
            _coastMaskProcessor.Initialize(_coastHelper);

        if (_landImage != null)
            _coastMaskProcessor.SetLandTexture(_landImage);

        if (!_coastMaskProcessor.HasFinalAtlas)
            _coastMaskProcessor.BuildFinalCoastAtlas(_coastHelper);

        _coastHelper.PreGenerateAllHexagonCoasts(null);
    }

    // Synchronously fills the coast decoration array for the whole map.
    // Used by screenshot rendering so coastlines do not depend on the
    // background precomputation task completing first.
    public void PrecomputeCoastDecorations(MapData mapData)
    {
        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return;

        lock (_coastTaskLock)
            _coastCalculationCts?.Cancel();

        var newArray = new byte[mapWidth, mapHeight];
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                if (mapData.GetTerrainAt(col, row).TileType1 == OCEAN_TILE_TYPE)
                    newArray[col, row] = CoastMaskProcessor.CalculateCoastDecorationType(mapData, col, row);
                else
                    newArray[col, row] = 255;
            }
        }

        lock (_arraySwapLock)
            _coastDecorationArray = newArray;
    }

    private void RenderFallbackBackground(SKCanvas canvas)
    {
        double offsetX, offsetY, zoomLevel;
        int vpW, vpH;
        _stateLock.EnterReadLock();
        try { offsetX = _offsetX; offsetY = _offsetY; zoomLevel = _zoomLevel; vpW = _viewportWidth; vpH = _viewportHeight; }
        finally { _stateLock.ExitReadLock(); }

        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

        int padding = 1;
        int visibleCols = (int)(vpW / hexSpacingX) + padding * 2;
        int visibleRows = (int)(vpH / hexSpacingY) + padding * 2;
        int startCol = Math.Max(0, (int)((-offsetX) / hexSpacingX) - padding);
        int startRow = Math.Max(0, (int)((-offsetY) / hexSpacingY) - padding);
        int endCol = startCol + visibleCols;
        int endRow = startRow + visibleRows;

        SKPath hexPath = GetHexPath(hexSize);
        using var seaPath = new SKPath();
        using var landPath = new SKPath();

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);
            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = rowOffsetY + row * hexSpacingY;
                if (centerX + hexSize < 0 || centerX - hexSize > vpW || centerY + hexSize < 0 || centerY - hexSize > vpH) continue;
                bool isLand = col % 3 != 0 || row % 3 != 0;
                SKPath targetPath = isLand ? landPath : seaPath;
                var matrix = SKMatrix.CreateTranslation((float)centerX, (float)centerY);
                targetPath.AddPath(hexPath, matrix);
            }
        }

        if (!seaPath.IsEmpty && _seaPaint != null) canvas.DrawPath(seaPath, _seaPaint);
        if (!landPath.IsEmpty && _landPaint != null) canvas.DrawPath(landPath, _landPaint);
    }

    private void RenderBackground(SKCanvas canvas, MapData mapData)
    {
        double offsetX, offsetY, zoomLevel;
        bool showGridLines, showLayer2;
        int vpW, vpH;
        bool enableBackgroundRender, enableTerrainsRender;

        _stateLock.EnterReadLock();
        try
        {
            offsetX = _offsetX; offsetY = _offsetY; zoomLevel = _zoomLevel;
            showGridLines = _showGridLines; showLayer2 = _showLayer2; vpW = _viewportWidth; vpH = _viewportHeight;
            enableBackgroundRender = _enableBackgroundRender; enableTerrainsRender = _enableTerrainsRender;
        }
        finally { _stateLock.ExitReadLock(); }

        if (vpW <= 0 || vpH <= 0) return;

        // Cache covers a 2x viewport area centered on the camera. The camera
        // can pan half a viewport in any direction before a rebuild is needed.
        int cacheW = vpW * 2;
        int cacheH = vpH * 2;

        bool haveCache = _bgCacheSurfaceA != null && _cacheW == cacheW && _cacheH == cacheH;
        bool stateMatches = _cachedBgWidth == vpW && _cachedBgHeight == vpH &&
            Math.Abs(_cachedBgZoomLevel - zoomLevel) <= 0.001 &&
            _cachedBgShowLayer2 == showLayer2 &&
            _cachedBgShowGridLines == showGridLines;

        if (haveCache && stateMatches)
        {
            // Camera offset bounds still inside the cached area: incremental
            // strip rendering reuses the previous frame's valid pixels.
            double cacheOriginX = _cachedBgCenterX - vpW * 0.5;
            double cacheOriginY = _cachedBgCenterY - vpH * 0.5;
            bool insideX = offsetX >= cacheOriginX && offsetX <= cacheOriginX + vpW;
            bool insideY = offsetY >= cacheOriginY && offsetY <= cacheOriginY + vpH;

            if (insideX && insideY)
            {
                bool panOnly = Math.Abs(offsetX - _cachedBgCenterX) > 0.001 || Math.Abs(offsetY - _cachedBgCenterY) > 0.001;
                if (panOnly)
                    RenderPanStrips(mapData, offsetX, offsetY, zoomLevel, showGridLines, showLayer2,
                        enableBackgroundRender, enableTerrainsRender, vpW, vpH);
                // Blit from cache to screen. Cache center sits at (cacheW/2,
                // cacheH/2); the camera pan delta shifts the source rect.
                float srcX = (float)(cacheW * 0.5 - vpW * 0.5 - (offsetX - _cachedBgCenterX));
                float srcY = (float)(cacheH * 0.5 - vpH * 0.5 - (offsetY - _cachedBgCenterY));
                canvas.DrawImage(_bgCacheImage!, new SKRect(srcX, srcY, srcX + vpW, srcY + vpH), new SKRect(0, 0, vpW, vpH));
                return;
            }
        }

        // Full rebuild needed (first frame, zoom/toggle change, size change,
        // or the camera left the cached area).
        if (!haveCache)
            RecreateCacheSurfaces(cacheW, cacheH);
        else
            (_bgCacheSurfaceA, _bgCacheSurfaceB) = (_bgCacheSurfaceB, _bgCacheSurfaceA);

        _cachedBgCenterX = offsetX;
        _cachedBgCenterY = offsetY;
        _cachedBgWidth = vpW;
        _cachedBgHeight = vpH;
        _cachedBgZoomLevel = zoomLevel;
        _cachedBgShowLayer2 = showLayer2;
        _cachedBgShowGridLines = showGridLines;

        var cacheCanvas = _bgCacheSurfaceA!.Canvas;
        cacheCanvas.Clear(SKColors.Transparent);
        RenderCacheContent(cacheCanvas, mapData, offsetX + vpW * 0.5, offsetY + vpH * 0.5,
            zoomLevel, showGridLines, showLayer2, enableBackgroundRender, enableTerrainsRender,
            _cacheW, _cacheH);
        RefreshCacheSnapshot();

        float srcX2 = (float)(cacheW * 0.5 - vpW * 0.5);
        float srcY2 = (float)(cacheH * 0.5 - vpH * 0.5);
        canvas.DrawImage(_bgCacheImage!, new SKRect(srcX2, srcY2, srcX2 + vpW, srcY2 + vpH), new SKRect(0, 0, vpW, vpH));
    }

    private void RefreshCacheSnapshot()
    {
        _bgCacheImage?.Dispose();
        _bgCacheImage = _bgCacheSurfaceA!.Snapshot();
    }

    private void RecreateCacheSurfaces(int cacheW, int cacheH)
    {
        _bgCacheSurfaceA?.Dispose();
        _bgCacheSurfaceB?.Dispose();
        _bgCacheSurfaceA = SKSurface.Create(new SKImageInfo(cacheW, cacheH));
        _bgCacheSurfaceB = SKSurface.Create(new SKImageInfo(cacheW, cacheH));
        _cacheW = cacheW;
        _cacheH = cacheH;
    }

    // Renders only the newly exposed strips after a camera pan by copying the
    // still-valid region of the front surface into the back surface shifted,
    // then filling the exposed strips.
    private void RenderPanStrips(MapData mapData, double offsetX, double offsetY, double zoomLevel, bool showGridLines,
        bool showLayer2, bool enableBackgroundRender, bool enableTerrainsRender, int vpW, int vpH)
    {
        var back = _bgCacheSurfaceB!;
        var backCanvas = back.Canvas;

        int shiftX = (int)Math.Round(offsetX - _cachedBgCenterX);
        int shiftY = (int)Math.Round(offsetY - _cachedBgCenterY);
        if (shiftX == 0 && shiftY == 0) return;

        // New cache content = old content shifted by (+shiftX, +shiftY): a map
        // point at cache pixel x moves to x + D when the camera pans by D.
        backCanvas.Clear(SKColors.Transparent);
        backCanvas.Save();
        backCanvas.Translate(shiftX, shiftY);
        _bgCacheSurfaceA!.Draw(backCanvas, 0, 0, _panCopyPaint);
        backCanvas.Restore();

        // Newly exposed regions in back-surface coordinates (opposite edge of
        // the pan direction).
        var strips = new List<SKRect>(4);
        if (shiftX > 0) strips.Add(new SKRect(0, 0, shiftX, _cacheH));
        else if (shiftX < 0) strips.Add(new SKRect(_cacheW + shiftX, 0, _cacheW, _cacheH));
        if (shiftY > 0) strips.Add(new SKRect(0, 0, _cacheW, shiftY));
        else if (shiftY < 0) strips.Add(new SKRect(0, _cacheH + shiftY, _cacheW, _cacheH));

        double newCacheOffsetX = offsetX + vpW * 0.5;
        double newCacheOffsetY = offsetY + vpH * 0.5;

        foreach (var strip in strips)
        {
            backCanvas.Save();
            backCanvas.ClipRect(strip);
            RenderCacheContent(backCanvas, mapData, newCacheOffsetX, newCacheOffsetY,
                zoomLevel, showGridLines, showLayer2, enableBackgroundRender, enableTerrainsRender,
                _cacheW, _cacheH,
                (int)strip.Left, (int)strip.Top, (int)strip.Width, (int)strip.Height);
            backCanvas.Restore();
        }

        (_bgCacheSurfaceA, _bgCacheSurfaceB) = (_bgCacheSurfaceB, _bgCacheSurfaceA);
        // Keep the cached center pixel-aligned with what was actually drawn
        // (integer shift); the sub-pixel remainder is applied in the blit.
        _cachedBgCenterX += shiftX;
        _cachedBgCenterY += shiftY;
        _cachedBgZoomLevel = zoomLevel;
        RefreshCacheSnapshot();
    }

    private readonly SKPaint _panCopyPaint = new() { BlendMode = SKBlendMode.Src };

    private void RenderCacheContent(SKCanvas canvas, MapData mapData, double cacheOffsetX, double cacheOffsetY,
        double zoomLevel, bool showGridLines, bool showLayer2, bool enableBackgroundRender, bool enableTerrainsRender,
        int areaWidth, int areaHeight, int clipX = 0, int clipY = 0, int clipW = 0, int clipH = 0)
    {
        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

        if (clipW <= 0 || clipH <= 0) { clipX = 0; clipY = 0; clipW = areaWidth; clipH = areaHeight; }

        if (enableBackgroundRender && _seaPaint != null && _landPaint != null)
            RenderHexGridToCanvas(canvas, mapData, cacheOffsetX, cacheOffsetY, hexSize, hexSpacingX, hexSpacingY,
                showGridLines, areaWidth, areaHeight, zoomLevel, clipX, clipY, clipW, clipH);

        if (enableTerrainsRender && _landTerrainsRender != null)
        {
            _landTerrainsRender.MapWidth = mapData.MapWidth;
            _landTerrainsRender.MapHeight = mapData.MapHeight;
            _landTerrainsRender.SetCameraState(cacheOffsetX, cacheOffsetY, zoomLevel, areaWidth, areaHeight, showLayer2);
            _landTerrainsRender.SetClipRect(clipX, clipY, clipW, clipH);
            _landTerrainsRender.Render(canvas, mapData);
            _landTerrainsRender.ClearClipRect();
        }
    }

    private void RenderHexGridToCanvas(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        float hexSize, double hexSpacingX, double hexSpacingY, bool showGridLines, int vpW, int vpH, double zoomLevel,
        int clipX = 0, int clipY = 0, int clipW = 0, int clipH = 0)
    {
        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return;

        if (clipW <= 0 || clipH <= 0) { clipX = 0; clipY = 0; clipW = vpW; clipH = vpH; }

        // Visible range derived from the clip rect (strip) instead of always
        // iterating the whole cache area.
        int startCol = Math.Max(0, (int)((clipX - offsetX) / hexSpacingX) - 1);
        int startRow = Math.Max(0, (int)((clipY - offsetY - hexSpacingY) / hexSpacingY) - 1);
        int endCol = Math.Min(mapWidth - 1, (int)((clipX + clipW - offsetX) / hexSpacingX) + 1);
        int endRow = Math.Min(mapHeight - 1, (int)((clipY + clipH - offsetY + hexSpacingY) / hexSpacingY) + 1);

        SKPath hexPath = GetHexPath(hexSize);
        using var seaPath = new SKPath();
        using var landPath = new SKPath();

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);
            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = rowOffsetY + row * hexSpacingY;
                if (centerX + hexSize < clipX || centerX - hexSize > clipX + clipW ||
                    centerY + hexSize < clipY || centerY - hexSize > clipY + clipH) continue;

                bool isLand = IsLandAt(mapData, col, row);
                SKPath targetPath = isLand ? landPath : seaPath;
                var matrix = SKMatrix.CreateTranslation((float)centerX, (float)centerY);
                targetPath.AddPath(hexPath, matrix);
            }
        }

        if (!seaPath.IsEmpty) DrawTexturedRegion(canvas, seaPath, false, offsetX, offsetY, vpW, vpH, zoomLevel);
        if (!landPath.IsEmpty) DrawTexturedRegion(canvas, landPath, true, offsetX, offsetY, vpW, vpH, zoomLevel);

        if (showGridLines && _gridPaint != null)
        {
            _gridPaint.StrokeWidth = Math.Max(0.5f, Math.Min(3f, (float)(zoomLevel * 0.8)));
            canvas.DrawPath(seaPath, _gridPaint);
            canvas.DrawPath(landPath, _gridPaint);
        }

        if (_enableCoastRender)
        {
            if (_coastMaskProcessor != null && _landImage != null)
                _coastMaskProcessor.SetLandTexture(_landImage);
            RenderCoastlines(canvas, mapData, offsetX, offsetY, hexSpacingX, hexSpacingY, startCol, endCol, startRow, endRow, zoomLevel);
        }

        RenderRivers(canvas, mapData, offsetX, offsetY, hexSize, hexSpacingX, hexSpacingY, startRow, endRow, startCol, endCol, zoomLevel);
    }

    private void DrawTexturedRegion(SKCanvas canvas, SKPath path, bool isLand, double offsetX, double offsetY, int vpW, int vpH, double zoomLevel)
    {
        SKBitmap? bitmap = isLand ? _landTexture : _seaTexture;
        SKPaint? fallbackPaint = isLand ? _landPaint : _seaPaint;

        if (bitmap != null)
        {
            // World-anchored texture that scales with zoom, so it expands/
            // contracts around the cursor during zoom-to-cursor (the sampled
            // point under the cursor stays fixed). Tile screen size = texSize*zoom,
            // so it stays crisp when zoomed out to the full map.
            // 采样: 纹理坐标 = (屏幕坐标 - 偏移) / 缩放；着色器局部矩阵是其逆变换:
            // 屏幕坐标 = 纹理坐标 * 缩放 + 偏移
            var matrix = SKMatrix.CreateScale((float)zoomLevel, (float)zoomLevel);
            matrix.PostConcat(SKMatrix.CreateTranslation((float)offsetX, (float)offsetY));
            using var shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix);
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader };
            canvas.DrawPath(path, paint);
        }
        else if (fallbackPaint != null)
        {
            canvas.DrawPath(path, fallbackPaint);
        }
    }

    private void RenderRivers(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        float hexSize, double hexSpacingX, double hexSpacingY, int startRow, int endRow, int startCol, int endCol, double zoomLevel)
    {
        if (_riverPaint == null) return;

        _riverPaint.StrokeWidth = Math.Max(1f, Math.Min(5f, hexSize / 15));
        double halfSize = hexSize / 2;
        double height = hexSize * Math.Sqrt(3) / 2;

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);
            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = rowOffsetY + row * hexSpacingY;
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.RiverValue == 0) continue;

                // 六边形六个顶点用局部变量（SKPoint 是 struct，栈上分配），
                // 替代原先每个河流格子每帧 new SKPoint[6]。
                var p0 = new SKPoint((float)(centerX - halfSize), (float)(centerY - height));
                var p1 = new SKPoint((float)(centerX + halfSize), (float)(centerY - height));
                var p2 = new SKPoint((float)(centerX + hexSize), (float)centerY);
                var p3 = new SKPoint((float)(centerX + halfSize), (float)(centerY + height));
                var p4 = new SKPoint((float)(centerX - halfSize), (float)(centerY + height));
                var p5 = new SKPoint((float)(centerX - hexSize), (float)centerY);

                int river = terrain.RiverValue;
                if ((river & 0x01) > 0) canvas.DrawLine(p0, p1, _riverPaint);
                if ((river & 0x02) > 0) canvas.DrawLine(p1, p2, _riverPaint);
                if ((river & 0x04) > 0) canvas.DrawLine(p2, p3, _riverPaint);
                if ((river & 0x08) > 0) canvas.DrawLine(p3, p4, _riverPaint);
                if ((river & 0x10) > 0) canvas.DrawLine(p4, p5, _riverPaint);
                if ((river & 0x20) > 0) canvas.DrawLine(p5, p0, _riverPaint);
            }
        }
    }

    private void RenderCoastlines(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        double hexSpacingX, double hexSpacingY, int startCol, int endCol, int startRow, int endRow, double zoomLevel)
    {
        if (mapData == null || _coastHelper == null) return;
        if (_coastDecorationArray == null) return;

        var canUseMask = _coastHelper.IsAtlasLoaded && _coastHelper.IsGrayLevelAtlasLoaded;

        using var paint = new SKPaint { IsAntialias = true };

        if (_coastHelper.IsAtlasLoaded)
        {
            var atlasImage = _coastHelper.GetCoastAtlasImage();
            if (atlasImage != null)
            {
                RenderCoastFromAtlas(canvas, mapData, atlasImage, offsetX, offsetY, hexSpacingX, hexSpacingY,
                    startCol, endCol, startRow, endRow, zoomLevel, canUseMask, paint);
                return;
            }
        }

        RenderCoastFromImages(canvas, mapData, offsetX, offsetY, hexSpacingX, hexSpacingY,
            startCol, endCol, startRow, endRow, zoomLevel, canUseMask, paint);
    }

    private void RenderCoastFromAtlas(SKCanvas canvas, MapData mapData, SKImage atlasImage,
        double offsetX, double offsetY, double hexSpacingX, double hexSpacingY,
        int startCol, int endCol, int startRow, int endRow, double zoomLevel, bool canUseMask, SKPaint paint)
    {
        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 != OCEAN_TILE_TYPE) continue;

                byte decorationType = GetCachedCoastDecorationType(col, row);
                if (decorationType < 11 || decorationType > 73) continue;

                double centerX = offsetX + col * hexSpacingX;
                double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);
                double centerY = rowOffsetY + row * hexSpacingY;

                var spriteName = _coastHelper?.GetCoastSpriteName(COAST_TILE_TYPE, decorationType);
                if (spriteName == null) continue;

                var srcRect = _coastHelper?.GetCoastAtlasSpriteRect(spriteName);
                if (!srcRect.HasValue) continue;

                var origin = _coastHelper?.GetCoastAtlasSpriteOrigin(spriteName);
                float refOffsetX = 0, refOffsetY = 0;
                if (origin.HasValue)
                {
                    refOffsetX = origin.Value.X - srcRect.Value.Width / 2;
                    refOffsetY = origin.Value.Y - srcRect.Value.Height / 2;
                }

                float scale = Math.Max(hexSize * 2 / srcRect.Value.Width, hexSize * 2 / srcRect.Value.Height);
                float drawWidth = srcRect.Value.Width * scale;
                float drawHeight = srcRect.Value.Height * scale;

                var dstRect = new SKRect(
                    (float)(centerX - drawWidth / 2 + refOffsetX * scale),
                    (float)(centerY - drawHeight / 2 + refOffsetY * scale - 0.5),
                    (float)(centerX + drawWidth / 2 + refOffsetX * scale),
                    (float)(centerY + drawHeight / 2 + refOffsetY * scale + 0.5));

                if (_coastMaskProcessor != null && _coastMaskProcessor.HasFinalAtlas)
                    _coastMaskProcessor.DrawFinalCoast(canvas, spriteName, dstRect, paint, zoomLevel);
                else if (canUseMask && _coastMaskProcessor != null && _coastMaskProcessor.HasMask(spriteName))
                    _coastMaskProcessor.DrawMaskedCoast(canvas, atlasImage, srcRect.Value, spriteName, dstRect, paint, zoomLevel);
                else
                    canvas.DrawImage(atlasImage, srcRect.Value, dstRect, paint);
            }
        }
    }

    private void RenderCoastFromImages(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        double hexSpacingX, double hexSpacingY, int startCol, int endCol, int startRow, int endRow,
        double zoomLevel, bool canUseMask, SKPaint paint)
    {
        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 != OCEAN_TILE_TYPE) continue;

                byte decorationType = GetCachedCoastDecorationType(col, row);
                if (decorationType < 11 || decorationType > 73) continue;

                double centerX = offsetX + col * hexSpacingX;
                double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);
                double centerY = rowOffsetY + row * hexSpacingY;

                var coastImage = _coastHelper?.GetCoastImageByDoodad(COAST_TILE_TYPE, decorationType);
                if (coastImage == null) continue;

                var spriteName = _coastHelper?.GetCoastSpriteName(COAST_TILE_TYPE, decorationType);

                float scale = Math.Max(hexSize * 2 / coastImage.Width, hexSize * 2 / coastImage.Height);
                float drawWidth = coastImage.Width * scale;
                float drawHeight = coastImage.Height * scale;

                var dstRect = new SKRect(
                    (float)(centerX - drawWidth / 2), (float)(centerY - drawHeight / 2 - 0.5),
                    (float)(centerX + drawWidth / 2), (float)(centerY + drawHeight / 2 + 0.5));

                if (canUseMask && _coastMaskProcessor != null && spriteName != null && _coastMaskProcessor.HasMask(spriteName))
                    _coastMaskProcessor.DrawMaskedCoast(canvas, coastImage,
                        new SKRect(0, 0, coastImage.Width, coastImage.Height), spriteName, dstRect, paint, zoomLevel);
                else
                    canvas.DrawImage(coastImage, dstRect, paint);
            }
        }
    }

    private byte GetCachedCoastDecorationType(int col, int row)
    {
        // 数组引用由后台预计算任务整体替换（见 StartCoastPrecomputation），内容则只在
        // UI 线程原地更新，而渲染也在 UI 线程，因此这里只需用 Volatile.Read 取一次引用，
        // 不必为可视范围内上万次调用逐个加锁（原实现每格 lock 一次）。
        var array = System.Threading.Volatile.Read(ref _coastDecorationArray);
        if (array == null) return 10;

        if (col >= 0 && col < array.GetLength(0) && row >= 0 && row < array.GetLength(1))
            return array[col, row];

        return 10;
    }

    private static bool IsLandAt(MapData mapData, int col, int row)
    {
        if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight) return false;
        var terrain = mapData.GetTerrainAt(col, row);
        return terrain.TileType1 != OCEAN_TILE_TYPE;
    }

    private void InitializeCoastCacheArray(MapData mapData)
    {
        var mapWidth = mapData.MapWidth;
        var mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return;

        lock (_arraySwapLock)
            _coastDecorationArray = new byte[mapWidth, mapHeight];

        StartCoastPrecomputation(mapData);
    }

    private void StartCoastPrecomputation(MapData mapData)
    {
        lock (_coastTaskLock)
        {
            _coastCalculationCts?.Cancel();
            _coastCalculationCts = new CancellationTokenSource();
            var token = _coastCalculationCts.Token;
            Task.Run(() => PreCalculateAllCoastlines(mapData, token), token);
        }
    }

    private CancellationTokenSource? _coastCalculationCts;

    private void PreCalculateAllCoastlines(MapData mapData, CancellationToken token)
    {
        var mapWidth = mapData.MapWidth;
        var mapHeight = mapData.MapHeight;
        var newArray = new byte[mapWidth, mapHeight];

        var calculatedCount = 0;
        Debug.WriteLine("[Coast] 开始预计算整张地图海岸线...");

        for (int row = 0; row < mapHeight; row++)
        {
            if (token.IsCancellationRequested) return;
            if (row % 100 == 0) Thread.Sleep(1);

            for (int col = 0; col < mapWidth; col++)
            {
                if (token.IsCancellationRequested) return;
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 == OCEAN_TILE_TYPE)
                {
                    var decorationType = CoastMaskProcessor.CalculateCoastDecorationType(mapData, col, row);
                    newArray[col, row] = decorationType;
                    if (decorationType >= 11 && decorationType <= 73) calculatedCount++;
                }
                else
                {
                    newArray[col, row] = 255;
                }
            }
        }

        lock (_arraySwapLock)
            _coastDecorationArray = newArray;

        Debug.WriteLine($"[Coast] 预计算完成！海岸线格子 {calculatedCount}");
        CoastCacheUpdated?.Invoke();
    }

    private SKPath GetHexPath(float hexSize)
    {
        if (_hexPathCache.TryGetValue(hexSize, out var cached)) return cached;
        var path = new SKPath();
        double sqrt3 = Math.Sqrt(3);
        double h = hexSize * sqrt3 / 2;
        float hs = hexSize / 2;
        path.MoveTo(-hs, (float)-h);
        path.LineTo(hs, (float)-h);
        path.LineTo(hexSize, 0);
        path.LineTo(hs, (float)h);
        path.LineTo(-hs, (float)h);
        path.LineTo(-hexSize, 0);
        path.Close();
        _hexPathCache[hexSize] = path;
        return path;
    }

    public void SetLandTerrainsRender(LandTerrainsRender render) => _landTerrainsRender = render;
    public LandTerrainsRender? GetLandTerrainsRender() => _landTerrainsRender;

    public void InvalidateCache()
    {
        _cachedBgCenterX = double.NaN;
        _cachedBgCenterY = double.NaN;
        _cachedBgZoomLevel = double.NaN;
        _cachedBgWidth = -1;
        _cachedBgHeight = -1;
    }

    public event Action? CoastCacheUpdated;

    public void InvalidateCoastCache(MapData mapData)
    {
        InitializeCoastCacheArray(mapData);
    }

    public void InvalidateCoastCacheRegion(MapData mapData, int centerCol, int centerRow, int radius)
    {
        lock (_coastTaskLock)
        {
            _coastCalculationCts?.Cancel();
        }

        if (_coastDecorationArray == null) return;
        var mapWidth = mapData.MapWidth;
        var mapHeight = mapData.MapHeight;

        for (int row = Math.Max(0, centerRow - radius); row <= Math.Min(mapHeight - 1, centerRow + radius); row++)
        {
            for (int col = Math.Max(0, centerCol - radius); col <= Math.Min(mapWidth - 1, centerCol + radius); col++)
            {
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 == OCEAN_TILE_TYPE)
                {
                    byte newDecoration = CoastMaskProcessor.CalculateCoastDecorationType(mapData, col, row);
                    _coastDecorationArray[col, row] = newDecoration;
                }
                else
                {
                    _coastDecorationArray[col, row] = 255;
                }
            }
        }
    }

    public void InvalidateCoastCacheFull(MapData mapData)
    {
        lock (_coastTaskLock)
        {
            _coastCalculationCts?.Cancel();
        }

        var mapWidth = mapData.MapWidth;
        var mapHeight = mapData.MapHeight;

        lock (_arraySwapLock)
        {
            if (_coastDecorationArray == null ||
                _coastDecorationArray.GetLength(0) != mapWidth ||
                _coastDecorationArray.GetLength(1) != mapHeight)
            {
                _coastDecorationArray = new byte[mapWidth, mapHeight];
            }
        }

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                var terrain = mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 == OCEAN_TILE_TYPE)
                {
                    byte newDecoration = CoastMaskProcessor.CalculateCoastDecorationType(mapData, col, row);
                    _coastDecorationArray[col, row] = newDecoration;
                }
                else
                {
                    _coastDecorationArray[col, row] = 255;
                }
            }
        }
    }

    public void RenderHexLabels(SKCanvas canvas)
    {
        if (_hexLabelMode == HexLabelMode.Hidden) return;

        double offsetX, offsetY, zoomLevel;
        int viewportWidth, viewportHeight;

        _stateLock.EnterReadLock();
        try
        {
            offsetX = _offsetX;
            offsetY = _offsetY;
            zoomLevel = _zoomLevel;
            viewportWidth = _viewportWidth;
            viewportHeight = _viewportHeight;
        }
        finally
        {
            _stateLock.ExitReadLock();
        }

        RenderHexLabelsCached(canvas, offsetX, offsetY, zoomLevel, viewportWidth, viewportHeight);
    }

    private void RenderHexLabelsCached(SKCanvas canvas, double offsetX, double offsetY,
        double zoomLevel, int viewportWidth, int viewportHeight)
    {
        bool cacheValid =
            _hexLabelsPicture != null &&
            Math.Abs(_cachedLabelsOffsetX - offsetX) < 0.5 &&
            Math.Abs(_cachedLabelsOffsetY - offsetY) < 0.5 &&
            Math.Abs(_cachedLabelsZoomLevel - zoomLevel) < 0.001 &&
            _cachedLabelsViewportWidth == viewportWidth &&
            _cachedLabelsViewportHeight == viewportHeight &&
            _cachedLabelMode == _hexLabelMode;

        if (!cacheValid)
        {
            _hexLabelsPicture?.Dispose();
            _hexLabelsPicture = null;

            using var recorder = new SKPictureRecorder();
            var rect = new SKRect(0, 0, viewportWidth, viewportHeight);
            using var pictureCanvas = recorder.BeginRecording(rect);

            double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
            double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

            int padding = 2;
            int visibleCols = (int)(viewportWidth / hexSpacingX) + padding * 2;
            int visibleRows = (int)(viewportHeight / hexSpacingY) + padding * 2;

            int startCol = Math.Max(0, (int)((-offsetX) / hexSpacingX) - padding);
            int startRow = Math.Max(0, (int)((-offsetY) / hexSpacingY) - padding);
            int endCol = Math.Min(MapWidth - 1, startCol + visibleCols);
            int endRow = Math.Min(MapHeight - 1, startRow + visibleRows);

            float labelFontSize = Math.Max(8.0f, (float)(12 * zoomLevel * 0.5));
            using var labelFont = new SKFont(SKTypeface.Default, labelFontSize);
            using var labelPaint = new SKPaint { IsAntialias = true, Color = SKColors.LimeGreen };
            using var shadowFont = new SKFont(SKTypeface.Default, labelFontSize);
            using var shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 180) };

            float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);

            for (int col = startCol; col <= endCol; col++)
            {
                double centerX = offsetX + col * hexSpacingX;
                double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);

                for (int row = startRow; row <= endRow; row++)
                {
                    double centerY = rowOffsetY + row * hexSpacingY;

                    if (centerX + hexSize < 0 || centerX - hexSize > viewportWidth ||
                        centerY + hexSize < 0 || centerY - hexSize > viewportHeight)
                        continue;

                    string text;
                    switch (_hexLabelMode)
                    {
                        case HexLabelMode.Index:
                            text = (row * MapWidth + col).ToString();
                            break;
                        case HexLabelMode.Coordinate:
                            text = $"({col},{row})";
                            break;
                        default:
                            continue;
                    }

                    float textWidth = labelFont.MeasureText(text);
                    float textX = (float)(centerX - textWidth / 2);
                    float textY = (float)(centerY + labelFont.Size / 3);

                    pictureCanvas.DrawText(text, textX + 1, textY + 1, SKTextAlign.Left, shadowFont, shadowPaint);
                    pictureCanvas.DrawText(text, textX, textY, SKTextAlign.Left, labelFont, labelPaint);
                }
            }

            _hexLabelsPicture = recorder.EndRecording();

            _cachedLabelsOffsetX = offsetX;
            _cachedLabelsOffsetY = offsetY;
            _cachedLabelsZoomLevel = zoomLevel;
            _cachedLabelsViewportWidth = viewportWidth;
            _cachedLabelsViewportHeight = viewportHeight;
            _cachedLabelMode = _hexLabelMode;
        }

        if (_hexLabelsPicture != null)
            canvas.DrawPicture(_hexLabelsPicture);
    }

    public void Dispose()
    {
        _seaPaint?.Dispose(); _landPaint?.Dispose(); _gridPaint?.Dispose();
        _textPaint?.Dispose(); _riverPaint?.Dispose(); _seaTexture?.Dispose();
        _landTexture?.Dispose(); _seaShader?.Dispose(); _landShader?.Dispose();
        _bgCacheImage?.Dispose();
        _hexLabelsPicture?.Dispose();
        _bgCacheSurfaceA?.Dispose();
        _bgCacheSurfaceB?.Dispose();
        _panCopyPaint.Dispose();
        foreach (var p in _hexPathCache.Values) p.Dispose();
        _stateLock.Dispose();
    }
}