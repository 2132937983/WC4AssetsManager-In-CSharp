namespace WC4MapEditor.Core.Helpers;

public readonly struct WorldBounds
{
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public WorldBounds(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }
}

public class Camera
{
    private double _offsetX;
    private double _offsetY;
    private double _zoomLevel = 1.0;

    public const double BaseHexSize = 20.0;
    public const double HexHorizontalSpacing = 30.0;
    public const double HexVerticalSpacing = 34.6410161513775;

    public event EventHandler? ViewChanged;

    public double OffsetX
    {
        get => _offsetX;
        set
        {
            if (_offsetX != value)
            {
                _offsetX = value;
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double OffsetY
    {
        get => _offsetY;
        set
        {
            if (_offsetY != value)
            {
                _offsetY = value;
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            double newZoom = Math.Max(0.1, Math.Min(5.0, value));
            if (_zoomLevel != newZoom)
            {
                _zoomLevel = newZoom;
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    public (double, double) HexToWorld(int col, int row)
    {
        double worldX = col * HexHorizontalSpacing * _zoomLevel;
        double worldY = row * HexVerticalSpacing * _zoomLevel;
        if (col % 2 == 1)
            worldY += (HexVerticalSpacing / 2) * _zoomLevel;
        return (worldX, worldY);
    }

    public (double, double) WorldToScreen(double worldX, double worldY)
    {
        return (worldX + _offsetX, worldY + _offsetY);
    }

    public (double, double) ScreenToWorld(double screenX, double screenY)
    {
        return (screenX - _offsetX, screenY - _offsetY);
    }

    public (double, double) ScreenToWorldScaled(double screenX, double screenY)
    {
        return ((screenX - _offsetX) / _zoomLevel, (screenY - _offsetY) / _zoomLevel);
    }

    public (double, double) WorldToScreenScaled(double worldX, double worldY)
    {
        return (worldX * _zoomLevel + _offsetX, worldY * _zoomLevel + _offsetY);
    }

    public (double, double) HexToScreen(int col, int row)
    {
        var world = HexToWorld(col, row);
        return WorldToScreen(world.Item1, world.Item2);
    }

    public (int, int) ScreenToHex(double screenX, double screenY)
    {
        var world = ScreenToWorld(screenX, screenY);
        double scaledHexWidth = HexHorizontalSpacing * _zoomLevel;
        double scaledHexHeight = HexVerticalSpacing * _zoomLevel;
        int col = (int)Math.Round(world.Item1 / scaledHexWidth);
        double rowOffset = (col % 2 == 1) ? (HexVerticalSpacing / 2) * _zoomLevel : 0;
        int row = (int)Math.Round((world.Item2 - rowOffset) / scaledHexHeight);
        return (col, row);
    }

    public void CenterOnMap()
    {
        if (MapWidth <= 0 || MapHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
            return;

        double centerCol = MapWidth / 2.0;
        double centerRow = MapHeight / 2.0;
        double mapCenterX = centerCol * HexHorizontalSpacing * _zoomLevel;
        double mapCenterY = centerRow * HexVerticalSpacing * _zoomLevel;
        if ((int)centerCol % 2 == 1)
            mapCenterY += (HexVerticalSpacing / 2) * _zoomLevel;

        _offsetX = ViewportWidth / 2 - mapCenterX;
        _offsetY = ViewportHeight / 2 - mapCenterY;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CenterOnHex(int col, int row)
    {
        var screen = HexToScreen(col, row);
        _offsetX = ViewportWidth / 2 - (screen.Item1 - _offsetX);
        _offsetY = ViewportHeight / 2 - (screen.Item2 - _offsetY);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pan(double deltaX, double deltaY)
    {
        _offsetX += deltaX;
        _offsetY += deltaY;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomAt(double screenX, double screenY, double newZoom)
    {
        var worldBefore = ScreenToWorldScaled(screenX, screenY);
        double oldZoom = _zoomLevel;
        _zoomLevel = Math.Max(0.1, Math.Min(5.0, newZoom));
        var screenAfter = WorldToScreenScaled(worldBefore.Item1, worldBefore.Item2);
        _offsetX += screenX - screenAfter.Item1;
        _offsetY += screenY - screenAfter.Item2;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _offsetX = 0;
        _offsetY = 0;
        _zoomLevel = 1.0;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClampView()
    {
        if (MapWidth <= 0 || MapHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
            return;

        double mapWorldWidth = MapWidth * HexHorizontalSpacing * _zoomLevel;
        double mapWorldHeight = MapHeight * HexVerticalSpacing * _zoomLevel;
        double minOffsetX = ViewportWidth - mapWorldWidth * 0.5;
        double maxOffsetX = mapWorldWidth * 0.5;
        double minOffsetY = ViewportHeight - mapWorldHeight * 0.5;
        double maxOffsetY = mapWorldHeight * 0.5;

        _offsetX = Math.Max(minOffsetX, Math.Min(maxOffsetX, _offsetX));
        _offsetY = Math.Max(minOffsetY, Math.Min(maxOffsetY, _offsetY));
    }

    public bool IsHexInViewport(int col, int row, double margin = 0)
    {
        var screen = HexToScreen(col, row);
        return screen.Item1 >= -margin &&
               screen.Item1 <= ViewportWidth + margin &&
               screen.Item2 >= -margin &&
               screen.Item2 <= ViewportHeight + margin;
    }

    public (int minCol, int minRow, int maxCol, int maxRow) GetVisibleHexRange()
    {
        var topLeft = ScreenToHex(0, 0);
        var bottomRight = ScreenToHex(ViewportWidth, ViewportHeight);
        return (
            Math.Max(0, topLeft.Item1 - 1),
            Math.Max(0, topLeft.Item2 - 1),
            Math.Min(MapWidth - 1, bottomRight.Item1 + 1),
            Math.Min(MapHeight - 1, bottomRight.Item2 + 1)
        );
    }

    public WorldBounds GetVisibleWorldBounds()
    {
        var topLeft = ScreenToWorld(0, 0);
        var topRight = ScreenToWorld(ViewportWidth, 0);
        var bottomLeft = ScreenToWorld(0, ViewportHeight);
        var bottomRight = ScreenToWorld(ViewportWidth, ViewportHeight);

        double minX = Math.Min(Math.Min(topLeft.Item1, topRight.Item1), Math.Min(bottomLeft.Item1, bottomRight.Item1));
        double maxX = Math.Max(Math.Max(topLeft.Item1, topRight.Item1), Math.Max(bottomLeft.Item1, bottomRight.Item1));
        double minY = Math.Min(Math.Min(topLeft.Item2, topRight.Item2), Math.Min(bottomLeft.Item2, bottomRight.Item2));
        double maxY = Math.Max(Math.Max(topLeft.Item2, topRight.Item2), Math.Max(bottomLeft.Item2, bottomRight.Item2));

        return new WorldBounds(minX, minY, maxX, maxY);
    }
}
