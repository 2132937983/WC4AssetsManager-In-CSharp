using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering;

public class MainRender : IRenderEngine
{
    private readonly IRenderEngine _activeEngine;
    private bool _disposed;

    public bool IsAvailable => _activeEngine.IsAvailable;
    public string EngineName => _activeEngine.EngineName;

    public bool EnableTerrainsRender
    {
        get => _activeEngine.EnableTerrainsRender;
        set => _activeEngine.EnableTerrainsRender = value;
    }

    public bool EnableBackgroundRender
    {
        get => _activeEngine.EnableBackgroundRender;
        set => _activeEngine.EnableBackgroundRender = value;
    }

    public bool EnableProvinceRender
    {
        get => _activeEngine.EnableProvinceRender;
        set => _activeEngine.EnableProvinceRender = value;
    }

    public bool EnableBuildingRender
    {
        get => _activeEngine.EnableBuildingRender;
        set => _activeEngine.EnableBuildingRender = value;
    }

    public bool EnableArmyRender
    {
        get => _activeEngine.EnableArmyRender;
        set => _activeEngine.EnableArmyRender = value;
    }

    public bool EnableTrapRender
    {
        get => _activeEngine.EnableTrapRender;
        set => _activeEngine.EnableTrapRender = value;
    }

    public MainRender(IRenderEngine activeEngine)
    {
        _activeEngine = activeEngine;
    }

    public void Initialize(IntPtr hwnd, int width, int height)
    {
        _activeEngine.Initialize(hwnd, width, height);
    }

    public void Resize(int width, int height)
    {
        _activeEngine.Resize(width, height);
    }

    public void Render(MapData mapData, Camera camera)
    {
        _activeEngine.Render(mapData, camera);
    }

    public void Render(SKCanvas canvas, MapData mapData, Camera camera)
    {
        _activeEngine.Render(canvas, mapData, camera);
    }

    public void Invalidate()
    {
        _activeEngine.Invalidate();
    }

    public (int col, int row) ScreenToHex(double screenX, double screenY)
    {
        return _activeEngine.ScreenToHex(screenX, screenY);
    }

    public (double x, double y) HexToScreen(int col, int row)
    {
        return _activeEngine.HexToScreen(col, row);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeEngine.Dispose();
    }
}

public static class RenderEngineFactory
{
    public static IRenderEngine Create()
    {
        return new Skia.SkiaRenderEngine();
    }
}
