using SkiaSharp;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Skia;

public sealed class SelectionRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private static readonly SKColor HIGHLIGHT_FILL_COLOR = new(255, 255, 0, 80);
    private static readonly SKColor HIGHLIGHT_BORDER_COLOR = new(255, 255, 0, 200);
    private static readonly SKColor OCEAN_FILL_COLOR = new(0, 100, 255, 80);
    private static readonly SKColor OCEAN_BORDER_COLOR = new(0, 100, 255, 200);
    private static readonly SKColor LAND_FILL_COLOR = new(0, 255, 100, 80);
    private static readonly SKColor LAND_BORDER_COLOR = new(0, 255, 100, 200);
    private static readonly SKColor SELECTION_RECT_FILL_COLOR = new(100, 149, 237, 40);
    private static readonly SKColor SELECTION_RECT_BORDER_COLOR = new(100, 149, 237, 180);

    private readonly ReaderWriterLockSlim _stateLock = new();
    private readonly Dictionary<float, SKPath> _hexPathCache = new();

    private double _offsetX;
    private double _offsetY;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _disposed;

    private SKPaint _fillPaint = null!;
    private SKPaint _borderPaint = null!;
    private SKPaint _oceanFillPaint = null!;
    private SKPaint _oceanBorderPaint = null!;
    private SKPaint _landFillPaint = null!;
    private SKPaint _landBorderPaint = null!;
    private SKPaint _rectFillPaint = null!;
    private SKPaint _rectBorderPaint = null!;

    public MapData? MapData { get; set; }

    public double OffsetX
    {
        get { _stateLock.EnterReadLock(); try { return _offsetX; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetX = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetY
    {
        get { _stateLock.EnterReadLock(); try { return _offsetY; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetY = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double ZoomLevel
    {
        get { _stateLock.EnterReadLock(); try { return _zoomLevel; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _zoomLevel = Math.Max(0.1, Math.Min(5.0, value)); } finally { _stateLock.ExitWriteLock(); } }
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

    public SelectionRender()
    {
        InitializePaints();
    }

    private void InitializePaints()
    {
        _fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = HIGHLIGHT_FILL_COLOR
        };

        _borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = HIGHLIGHT_BORDER_COLOR,
            StrokeWidth = 2
        };

        _oceanFillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = OCEAN_FILL_COLOR
        };

        _oceanBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = OCEAN_BORDER_COLOR,
            StrokeWidth = 2
        };

        _landFillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = LAND_FILL_COLOR
        };

        _landBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = LAND_BORDER_COLOR,
            StrokeWidth = 2
        };

        _rectFillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SELECTION_RECT_FILL_COLOR
        };

        _rectBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = SELECTION_RECT_BORDER_COLOR,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0)
        };
    }

    public void Render(SKCanvas canvas, int mapWidth, int mapHeight)
    {
        if (canvas == null || _disposed) return;

        RenderSelectionRect(canvas);
        RenderSelectedHexes(canvas, mapWidth, mapHeight);
    }

    private void RenderSelectionRect(SKCanvas canvas)
    {
        var selector = HexSelector.Instance;
        if (!selector.IsSelectionRectActive) return;

        var (startX, startY, endX, endY) = selector.SelectionRect;
        float left = (float)Math.Min(startX, endX);
        float top = (float)Math.Min(startY, endY);
        float right = (float)Math.Max(startX, endX);
        float bottom = (float)Math.Max(startY, endY);

        var rect = new SKRect(left, top, right, bottom);
        canvas.DrawRect(rect, _rectFillPaint);
        canvas.DrawRect(rect, _rectBorderPaint);
    }

    private void RenderSelectedHexes(SKCanvas canvas, int mapWidth, int mapHeight)
    {
        var selector = HexSelector.Instance;
        if (!selector.HasSelection) return;

        double offsetX, offsetY, zoomLevel;
        int vpW, vpH;
        _stateLock.EnterReadLock();
        try
        {
            offsetX = _offsetX; offsetY = _offsetY; zoomLevel = _zoomLevel;
            vpW = _viewportWidth; vpH = _viewportHeight;
        }
        finally { _stateLock.ExitReadLock(); }

        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

        _borderPaint.StrokeWidth = Math.Max(1f, Math.Min(3f, (float)(zoomLevel * 1.5)));
        _oceanBorderPaint.StrokeWidth = _borderPaint.StrokeWidth;
        _landBorderPaint.StrokeWidth = _borderPaint.StrokeWidth;

        SKPath hexPath = GetHexPath(hexSize);
        var mapData = MapData;
        bool isMovingSelection = selector.IsMovingSelection;
        int moveOffsetCol = selector.MoveOffsetCol;
        int moveOffsetRow = selector.MoveOffsetRow;
        var originalSelectedHexes = selector.OriginalSelectedHexes;

        foreach (var coord in selector.SelectedHexes)
        {
            if (coord.Col < 0 || coord.Col >= mapWidth || coord.Row < 0 || coord.Row >= mapHeight) continue;

            double centerX = offsetX + coord.Col * hexSpacingX;
            double centerY = offsetY + (coord.Col % 2) * (hexSpacingY / 2) + coord.Row * hexSpacingY;

            if (centerX + hexSize < 0 || centerX - hexSize > vpW || centerY + hexSize < 0 || centerY - hexSize > vpH) continue;

            var matrix = SKMatrix.CreateTranslation((float)centerX, (float)centerY);
            using var translatedPath = new SKPath(hexPath);
            translatedPath.Transform(matrix);

            SKPaint fillPaint, borderPaint;

            if (isMovingSelection && mapData != null)
            {
                int originalCol = coord.Col - moveOffsetCol;
                int originalRow = coord.Row - moveOffsetRow;
                bool isOriginalHex = originalSelectedHexes?.Contains((originalCol, originalRow)) ?? false;

                if (isOriginalHex)
                {
                    bool isOcean = originalCol >= 0 && originalCol < mapData.MapWidth
                        && originalRow >= 0 && originalRow < mapData.MapHeight
                        && mapData.GetTerrainRef(originalCol, originalRow).TileType1 == 1;

                    if (isOcean)
                    {
                        fillPaint = _oceanFillPaint;
                        borderPaint = _oceanBorderPaint;
                    }
                    else
                    {
                        fillPaint = _landFillPaint;
                        borderPaint = _landBorderPaint;
                    }
                }
                else
                {
                    fillPaint = _landFillPaint;
                    borderPaint = _landBorderPaint;
                }
            }
            else
            {
                fillPaint = _fillPaint;
                borderPaint = _borderPaint;
            }

            canvas.DrawPath(translatedPath, fillPaint);
            canvas.DrawPath(translatedPath, borderPaint);
        }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fillPaint?.Dispose();
        _borderPaint?.Dispose();
        _oceanFillPaint?.Dispose();
        _oceanBorderPaint?.Dispose();
        _landFillPaint?.Dispose();
        _landBorderPaint?.Dispose();
        _rectFillPaint?.Dispose();
        _rectBorderPaint?.Dispose();
        foreach (var p in _hexPathCache.Values) p.Dispose();
        _hexPathCache.Clear();
        _stateLock.Dispose();
    }
}