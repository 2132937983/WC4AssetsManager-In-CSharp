using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class LegionDomainRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);
    private const byte DOMAIN_ALPHA = 120;

    private readonly ReaderWriterLockSlim _stateLock = new();
    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _enableLegionDomainRender;
    private bool _disposed;

    private readonly Dictionary<int, SKColor> _legionColorCache = new();
    private readonly object _colorCacheLock = new();

    private readonly Dictionary<float, SKPath> _hexPathCache = new();
    private readonly object _pathCacheLock = new();

    private SKPaint? _domainPaint;

    public bool EnableLegionDomainRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableLegionDomainRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableLegionDomainRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

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
        set { _stateLock.EnterWriteLock(); try { _zoomLevel = value; } finally { _stateLock.ExitWriteLock(); } }
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

    public LegionDomainRender()
    {
        _domainPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public void InvalidateCache()
    {
        ClearColorCache();
    }

    public void ClearColorCache()
    {
        lock (_colorCacheLock)
        {
            _legionColorCache.Clear();
        }
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (!EnableLegionDomainRender || canvas == null || mapData == null) return;

        _stateLock.EnterReadLock();
        try
        {
            if (mapData.Belongs == null || mapData.Belongs.Count == 0) return;
            if (mapData.Legions == null || mapData.Legions.Count == 0) return;

            RenderDomainsToCanvas(canvas, mapData);
        }
        finally
        {
            _stateLock.ExitReadLock();
        }
    }

    private void RenderDomainsToCanvas(SKCanvas canvas, MapData mapData)
    {
        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth == 0 || mapHeight == 0) return;

        float scaledHexSize = (float)(BASE_HEX_SIZE * _zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * _zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * _zoomLevel;

        int padding = 2;
        int startCol = Math.Max(0, (int)((-_offsetX) / hexSpacingX) - padding);
        int startRow = Math.Max(0, (int)((-_offsetY) / hexSpacingY) - padding);
        int endCol = Math.Min(mapWidth - 1, startCol + (int)(_viewportWidth / hexSpacingX) + padding * 2);
        int endRow = Math.Min(mapHeight - 1, startRow + (int)(_viewportHeight / hexSpacingY) + padding * 2);

        var colorGroups = new Dictionary<SKColor, List<(float x, float y)>>();

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int index = row * mapWidth + col;

                ushort provinceValue = mapData.GetProvinceRef(index).ProvinceValue;
                if (provinceValue == 0xFFFF) continue;

                int capitalIndex = provinceValue;
                if (capitalIndex < 0 || capitalIndex >= mapWidth * mapHeight) continue;

                int belongValue = GetBelongValue(capitalIndex, mapData);
                if (belongValue < 0 || belongValue == 255) continue;

                SKColor color = GetLegionColor(belongValue, mapData);
                if (color == default) continue;

                float x = (float)(_offsetX + col * hexSpacingX);
                float y = (float)(_offsetY + (col % 2) * (hexSpacingY / 2) + row * hexSpacingY);

                if (!colorGroups.ContainsKey(color))
                    colorGroups[color] = new List<(float, float)>();
                colorGroups[color].Add((x, y));
            }
        }

        if (_domainPaint == null) return;

        foreach (var kvp in colorGroups)
        {
            _domainPaint.Color = kvp.Key;
            foreach (var pos in kvp.Value)
                DrawHexagon(canvas, pos.x, pos.y, scaledHexSize);
        }
    }

    private static int GetBelongValue(int index, MapData mapData)
    {
        if (mapData.Belongs == null || mapData.Belongs.Count == 0) return -1;

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth == 0 || mapHeight == 0) return -1;

        int row = index / mapWidth;
        int col = index % mapWidth;

        int mapClipX = mapData.Header?.MapClipX ?? 0;
        int mapClipY = mapData.Header?.MapClipY ?? 0;

        if (mapClipY > 0 && row < mapClipY) return -1;
        if (mapClipX > 0 && col < mapClipX) return -1;

        int conquestWidth = mapData.Header?.MapLength ?? 0;
        if (conquestWidth <= 0) conquestWidth = mapWidth - mapClipX;

        int actualCol = col - mapClipX;
        int actualRow = row - mapClipY;

        if (actualCol >= conquestWidth)
        {
            actualCol -= conquestWidth;
            actualRow++;
        }

        int actualIndex = actualRow * conquestWidth + actualCol;

        if (actualIndex < 0 || actualIndex >= mapData.Belongs.Count) return -1;

        string belongStr = mapData.Belongs[actualIndex];
        if (string.IsNullOrEmpty(belongStr)) return -1;

        try
        {
            if (belongStr.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(belongStr.Substring(2), 16);

            if (int.TryParse(belongStr, System.Globalization.NumberStyles.HexNumber, null, out int hexResult))
                return hexResult;

            if (int.TryParse(belongStr, out int result))
                return result;

            return Convert.ToInt32(belongStr, 16);
        }
        catch
        {
            return -1;
        }
    }

    private SKColor GetLegionColor(int belongValue, MapData mapData)
    {
        lock (_colorCacheLock)
        {
            if (_legionColorCache.TryGetValue(belongValue, out SKColor cached))
                return cached;
        }

        SKColor color = default;

        if (belongValue >= 0 && belongValue < mapData.Legions.Count)
        {
            var legion = mapData.Legions[belongValue];
            color = new SKColor(legion.ColorR, legion.ColorG, legion.ColorB, DOMAIN_ALPHA);
        }

        if (color != default)
        {
            lock (_colorCacheLock)
            {
                if (!_legionColorCache.ContainsKey(belongValue))
                    _legionColorCache[belongValue] = color;
            }
        }

        return color;
    }

    private void DrawHexagon(SKCanvas canvas, float x, float y, float size)
    {
        var path = GetHexagonPath(size);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.DrawPath(path, _domainPaint!);
        canvas.Restore();
    }

    private SKPath GetHexagonPath(float size)
    {
        lock (_pathCacheLock)
        {
            if (_hexPathCache.TryGetValue(size, out SKPath? cached))
                return cached;
        }

        var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            double angleRad = Math.PI / 180 * (60 * i);
            float px = size * (float)Math.Cos(angleRad);
            float py = size * (float)Math.Sin(angleRad);

            if (i == 0) path.MoveTo(px, py);
            else path.LineTo(px, py);
        }
        path.Close();

        lock (_pathCacheLock)
        {
            if (!_hexPathCache.ContainsKey(size))
                _hexPathCache[size] = path;
        }

        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _domainPaint?.Dispose();
        _domainPaint = null;

        lock (_pathCacheLock)
        {
            foreach (var path in _hexPathCache.Values)
                path?.Dispose();
            _hexPathCache.Clear();
        }

        _stateLock.Dispose();
    }
}