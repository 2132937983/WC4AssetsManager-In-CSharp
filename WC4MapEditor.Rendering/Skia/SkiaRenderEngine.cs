using SkiaSharp;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;
using WC4MapEditor.Rendering.Helpers;

namespace WC4MapEditor.Rendering.Skia;

public interface ISkiaRenderer : IDisposable
{
    void Initialize(SKElement skElement);
    void Resize(int width, int height);
    void Render(SKCanvas canvas, MapData mapData, Camera camera);
}

public class SkiaRenderEngine : IRenderEngine, ISkiaRenderer
{
    private SKElement? _skElement;
    private bool _disposed;

    private BackGroundRender? _backgroundRender;
    private LandTerrainsRender? _landTerrainsRender;
    private ProvinceRender? _provinceRender;
    private BuildingRender? _buildingRender;
    private ArmyRender? _armyRender;
    private TrapRender? _trapRender;

    public bool IsAvailable => true;
    public string EngineName => "SkiaSharp";

    public bool EnableTerrainsRender { get; set; } = true;
    public bool EnableBackgroundRender { get; set; } = true;
    public bool EnableProvinceRender { get; set; }
    public bool EnableBuildingRender { get; set; } = true;
    public bool EnableArmyRender { get; set; } = true;
    public bool EnableTrapRender { get; set; } = true;

    public void Initialize(IntPtr hwnd, int width, int height)
    {
    }

    public void Initialize(SKElement skElement)
    {
        _skElement = skElement;
        InitializeTerrainRenderers();
    }

    private void InitializeTerrainRenderers()
    {
        if (_backgroundRender != null) return;

        _landTerrainsRender = new LandTerrainsRender();
        _backgroundRender = new BackGroundRender();
        _backgroundRender.SetLandTerrainsRender(_landTerrainsRender);
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
        if (_skElement == null) return;
        _skElement.InvalidateVisual();
    }

    public void Render(SKCanvas canvas, MapData mapData, Camera camera)
    {
        if (canvas == null || mapData == null) return;

        canvas.Clear(SKColors.Black);

        InitializeTerrainRenderers();

        if (EnableBackgroundRender && _backgroundRender != null)
        {
            _backgroundRender.OffsetX = camera.OffsetX;
            _backgroundRender.OffsetY = camera.OffsetY;
            _backgroundRender.ZoomLevel = camera.ZoomLevel;
            _backgroundRender.ViewportWidth = (int)camera.ViewportWidth;
            _backgroundRender.ViewportHeight = (int)camera.ViewportHeight;
            _backgroundRender.EnableBackgroundRender = EnableBackgroundRender;
            _backgroundRender.EnableTerrainsRender = EnableTerrainsRender;
            _backgroundRender.Render(canvas, mapData);
        }

        if (EnableProvinceRender && _provinceRender != null)
        {
            _provinceRender.OffsetX = camera.OffsetX;
            _provinceRender.OffsetY = camera.OffsetY;
            _provinceRender.ZoomLevel = camera.ZoomLevel;
            _provinceRender.ViewportWidth = (int)camera.ViewportWidth;
            _provinceRender.ViewportHeight = (int)camera.ViewportHeight;
            _provinceRender.Render(canvas, mapData);
        }
    }

    public void Invalidate()
    {
        _skElement?.InvalidateVisual();
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
        _landTerrainsRender?.Dispose();
        _backgroundRender?.Dispose();
        _provinceRender?.Dispose();
        _buildingRender?.Dispose();
        _armyRender?.Dispose();
        _trapRender?.Dispose();
    }
}
