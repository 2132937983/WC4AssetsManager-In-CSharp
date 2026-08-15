using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;
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

    private SKImage? _bgCacheImage;
    private double _cachedBgCenterX = double.NaN;
    private double _cachedBgCenterY = double.NaN;
    private double _cachedBgZoomLevel;
    private int _cachedBgWidth;
    private int _cachedBgHeight;
    private bool _cachedBgShowLayer2;
    private bool _cachedBgShowGridLines;

    private byte[,]? _coastDecorationArray;
    private readonly object _arraySwapLock = new();
    private readonly object _coastTaskLock = new();

    private DateTime _lastCameraMoveTime = DateTime.Now;
    private bool _isCameraMoving;
    private double _lastCameraX = double.NaN;
    private double _lastCameraY = double.NaN;

    public enum HexLabelMode { Hidden = 0, Index = 1, Coordinate = 2 }

    private HexLabelMode _hexLabelMode = HexLabelMode.Hidden;

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
        bool showGridLines;
        int vpW, vpH;
        bool enableBackgroundRender, enableTerrainsRender;

        _stateLock.EnterReadLock();
        try
        {
            offsetX = _offsetX; offsetY = _offsetY; zoomLevel = _zoomLevel;
            showGridLines = _showGridLines; vpW = _viewportWidth; vpH = _viewportHeight;
            enableBackgroundRender = _enableBackgroundRender; enableTerrainsRender = _enableTerrainsRender;
        }
        finally { _stateLock.ExitReadLock(); }

        if (vpW <= 0 || vpH <= 0) return;

        bool cacheInvalid = _bgCacheImage == null ||
            Math.Abs(_cachedBgZoomLevel - zoomLevel) > 0.001 ||
            _cachedBgWidth != vpW || _cachedBgHeight != vpH ||
            _cachedBgShowLayer2 != _showLayer2 ||
            _cachedBgShowGridLines != showGridLines ||
            Math.Abs(offsetX - _cachedBgCenterX) > vpW * 0.5 ||
            Math.Abs(offsetY - _cachedBgCenterY) > vpH * 0.5;

        if (!cacheInvalid)
        {
            float srcX = (float)(vpW * 0.5 - (offsetX - _cachedBgCenterX));
            float srcY = (float)(vpH * 0.5 - (offsetY - _cachedBgCenterY));
            canvas.DrawImage(_bgCacheImage, new SKRect(srcX, srcY, srcX + vpW, srcY + vpH), new SKRect(0, 0, vpW, vpH));
            return;
        }

        _bgCacheImage?.Dispose();
        _bgCacheImage = null;

        int cacheW = vpW * 2;
        int cacheH = vpH * 2;
        double cacheOffsetX = offsetX + vpW * 0.5;
        double cacheOffsetY = offsetY + vpH * 0.5;

        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

        using var surface = SKSurface.Create(new SKImageInfo(cacheW, cacheH));
        var cacheCanvas = surface.Canvas;

        if (enableBackgroundRender && _seaPaint != null && _landPaint != null)
            RenderHexGridToCanvas(cacheCanvas, mapData, cacheOffsetX, cacheOffsetY, hexSize, hexSpacingX, hexSpacingY, showGridLines, cacheW, cacheH, zoomLevel);

        if (enableTerrainsRender && _landTerrainsRender != null)
        {
            _landTerrainsRender.MapWidth = mapData.MapWidth;
            _landTerrainsRender.MapHeight = mapData.MapHeight;
            _landTerrainsRender.OffsetX = cacheOffsetX;
            _landTerrainsRender.OffsetY = cacheOffsetY;
            _landTerrainsRender.ZoomLevel = zoomLevel;
            _landTerrainsRender.ViewportWidth = cacheW;
            _landTerrainsRender.ViewportHeight = cacheH;
            _landTerrainsRender.ShowLayer2 = _showLayer2;
            _landTerrainsRender.Render(cacheCanvas, mapData);
        }

        _bgCacheImage = surface.Snapshot();
        _cachedBgCenterX = offsetX;
        _cachedBgCenterY = offsetY;
        _cachedBgZoomLevel = zoomLevel;
        _cachedBgWidth = vpW;
        _cachedBgHeight = vpH;
        _cachedBgShowLayer2 = _showLayer2;
        _cachedBgShowGridLines = showGridLines;

        float srcX2 = (float)(vpW * 0.5 - (offsetX - _cachedBgCenterX));
        float srcY2 = (float)(vpH * 0.5 - (offsetY - _cachedBgCenterY));
        canvas.DrawImage(_bgCacheImage, new SKRect(srcX2, srcY2, srcX2 + vpW, srcY2 + vpH), new SKRect(0, 0, vpW, vpH));
    }

    private void RenderHexGridToCanvas(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        float hexSize, double hexSpacingX, double hexSpacingY, bool showGridLines, int vpW, int vpH, double zoomLevel)
    {
        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return;

        int visibleCols = (int)(vpW / hexSpacingX) + 1;
        int visibleRows = (int)(vpH / hexSpacingY) + 1;
        int startCol = Math.Max(0, (int)((-offsetX) / hexSpacingX));
        int startRow = Math.Max(0, (int)((-offsetY) / hexSpacingY));
        int endCol = Math.Min(mapWidth - 1, startCol + visibleCols);
        int endRow = Math.Min(mapHeight - 1, startRow + visibleRows);

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
        if (isLand && _landPaint != null) canvas.DrawPath(path, _landPaint);
        else if (!isLand && _seaPaint != null) canvas.DrawPath(path, _seaPaint);
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

                var v = new SKPoint[6];
                v[0] = new SKPoint((float)(centerX - halfSize), (float)(centerY - height));
                v[1] = new SKPoint((float)(centerX + halfSize), (float)(centerY - height));
                v[2] = new SKPoint((float)(centerX + hexSize), (float)centerY);
                v[3] = new SKPoint((float)(centerX + halfSize), (float)(centerY + height));
                v[4] = new SKPoint((float)(centerX - halfSize), (float)(centerY + height));
                v[5] = new SKPoint((float)(centerX - hexSize), (float)centerY);

                if ((terrain.RiverValue & 0x01) > 0) canvas.DrawLine(v[0], v[1], _riverPaint);
                if ((terrain.RiverValue & 0x02) > 0) canvas.DrawLine(v[1], v[2], _riverPaint);
                if ((terrain.RiverValue & 0x04) > 0) canvas.DrawLine(v[2], v[3], _riverPaint);
                if ((terrain.RiverValue & 0x08) > 0) canvas.DrawLine(v[3], v[4], _riverPaint);
                if ((terrain.RiverValue & 0x10) > 0) canvas.DrawLine(v[4], v[5], _riverPaint);
                if ((terrain.RiverValue & 0x20) > 0) canvas.DrawLine(v[5], v[0], _riverPaint);
            }
        }
    }

    private void RenderCoastlines(SKCanvas canvas, MapData mapData, double offsetX, double offsetY,
        double hexSpacingX, double hexSpacingY, int startCol, int endCol, int startRow, int endRow, double zoomLevel)
    {
        if (mapData == null || _coastHelper == null) return;
        if (_coastDecorationArray == null) return;

        var cameraMoved = Math.Abs(offsetX - _lastCameraX) > 1 || Math.Abs(offsetY - _lastCameraY) > 1;
        if (cameraMoved)
        {
            _isCameraMoving = true;
            _lastCameraMoveTime = DateTime.Now;
            _lastCameraX = offsetX;
            _lastCameraY = offsetY;
        }
        else if (_isCameraMoving && (DateTime.Now - _lastCameraMoveTime).TotalMilliseconds > 100)
        {
            _isCameraMoving = false;
        }

        if (_isCameraMoving && zoomLevel > 0.5) return;

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

                var srcRect = _coastHelper.GetCoastAtlasSpriteRect(spriteName);
                if (!srcRect.HasValue) continue;

                var origin = _coastHelper.GetCoastAtlasSpriteOrigin(spriteName);
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
                    (float)(centerY - drawHeight / 2 + refOffsetY * scale),
                    (float)(centerX + drawWidth / 2 + refOffsetX * scale),
                    (float)(centerY + drawHeight / 2 + refOffsetY * scale));

                if (zoomLevel >= 0.25)
                {
                    if (_coastMaskProcessor != null && _coastMaskProcessor.HasFinalAtlas)
                        _coastMaskProcessor.DrawFinalCoast(canvas, spriteName, dstRect, paint, zoomLevel);
                    else if (canUseMask && _coastMaskProcessor != null && _coastMaskProcessor.HasMask(spriteName))
                        _coastMaskProcessor.DrawMaskedCoast(canvas, atlasImage, srcRect.Value, spriteName, dstRect, paint, zoomLevel);
                    else
                        canvas.DrawImage(atlasImage, srcRect.Value, dstRect, paint);
                }
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

                var coastImage = _coastHelper!.GetCoastImageByDoodad(COAST_TILE_TYPE, decorationType);
                if (coastImage == null) continue;

                var spriteName = _coastHelper?.GetCoastSpriteName(COAST_TILE_TYPE, decorationType);

                float scale = Math.Max(hexSize * 2 / coastImage.Width, hexSize * 2 / coastImage.Height);
                float drawWidth = coastImage.Width * scale;
                float drawHeight = coastImage.Height * scale;

                var dstRect = new SKRect(
                    (float)(centerX - drawWidth / 2), (float)(centerY - drawHeight / 2),
                    (float)(centerX + drawWidth / 2), (float)(centerY + drawHeight / 2));

                if (zoomLevel >= 0.25)
                {
                    if (canUseMask && _coastMaskProcessor != null && spriteName != null && _coastMaskProcessor.HasMask(spriteName))
                        _coastMaskProcessor.DrawMaskedCoast(canvas, coastImage,
                            new SKRect(0, 0, coastImage.Width, coastImage.Height), spriteName, dstRect, paint, zoomLevel);
                    else
                        canvas.DrawImage(coastImage, dstRect, paint);
                }
            }
        }
    }

    private byte GetCachedCoastDecorationType(int col, int row)
    {
        if (_coastDecorationArray == null) return 10;
        lock (_arraySwapLock)
        {
            if (col >= 0 && col < _coastDecorationArray.GetLength(0) && row >= 0 && row < _coastDecorationArray.GetLength(1))
                return _coastDecorationArray[col, row];
        }
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

    public void Dispose()
    {
        _seaPaint?.Dispose(); _landPaint?.Dispose(); _gridPaint?.Dispose();
        _textPaint?.Dispose(); _riverPaint?.Dispose(); _seaTexture?.Dispose();
        _landTexture?.Dispose(); _seaShader?.Dispose(); _landShader?.Dispose();
        _bgCacheImage?.Dispose();
        foreach (var p in _hexPathCache.Values) p.Dispose();
        _stateLock.Dispose();
    }
}