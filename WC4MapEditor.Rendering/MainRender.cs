using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Services;
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

    public bool EnableSelectionRender
    {
        get => _activeEngine.EnableSelectionRender;
        set => _activeEngine.EnableSelectionRender = value;
    }

    public string HelpText
    {
        get => _activeEngine.HelpText;
        set => _activeEngine.HelpText = value;
    }

    public string ModeName
    {
        get => _activeEngine.ModeName;
        set => _activeEngine.ModeName = value;
    }

    public bool ShowHelp
    {
        get => _activeEngine.ShowHelp;
        set => _activeEngine.ShowHelp = value;
    }

    public bool ShowModeName
    {
        get => _activeEngine.ShowModeName;
        set => _activeEngine.ShowModeName = value;
    }

    public float HelpOpacity
    {
        get => _activeEngine.HelpOpacity;
        set => _activeEngine.HelpOpacity = value;
    }

    public void UpdateHelpFadeAnimation()
    {
        _activeEngine.UpdateHelpFadeAnimation();
    }

    public void SetBrushPreview(int centerCol, int centerRow, int brushSize, string brushShape,
        double zoomLevel, double offsetX, double offsetY, bool visible)
    {
        _activeEngine.SetBrushPreview(centerCol, centerRow, brushSize, brushShape, zoomLevel, offsetX, offsetY, visible);
    }

    public void HideBrushPreview()
    {
        _activeEngine.HideBrushPreview();
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

    public void InvalidateTerrainCache()
    {
        _activeEngine.InvalidateTerrainCache();
    }

    public void InvalidateCoastCache(MapData mapData)
    {
        _activeEngine.InvalidateCoastCache(mapData);
    }

    public void InvalidateCoastCacheRegion(MapData mapData, int centerCol, int centerRow, int radius)
    {
        _activeEngine.InvalidateCoastCacheRegion(mapData, centerCol, centerRow, radius);
    }

    public void InvalidateCoastCacheFull(MapData mapData)
    {
        _activeEngine.InvalidateCoastCacheFull(mapData);
    }

    public (int col, int row) ScreenToHex(double screenX, double screenY)
    {
        return _activeEngine.ScreenToHex(screenX, screenY);
    }

    public (double x, double y) HexToScreen(int col, int row)
    {
        return _activeEngine.HexToScreen(col, row);
    }

    public bool LoadViewLayerImage(string imagePath)
    {
        return _activeEngine.LoadViewLayerImage(imagePath);
    }

    public void ClearViewLayerImage()
    {
        _activeEngine.ClearViewLayerImage();
    }

    public bool ViewLayerVisible
    {
        get => _activeEngine.ViewLayerVisible;
        set => _activeEngine.ViewLayerVisible = value;
    }

    public float ViewLayerOpacity
    {
        get => _activeEngine.ViewLayerOpacity;
        set => _activeEngine.ViewLayerOpacity = value;
    }

    public void InvalidateViewLayerCache()
    {
        _activeEngine.InvalidateViewLayerCache();
    }

    public bool ShowLayer2
    {
        get => _activeEngine.ShowLayer2;
        set => _activeEngine.ShowLayer2 = value;
    }

    public bool ShowHexBorders
    {
        get => _activeEngine.ShowHexBorders;
        set => _activeEngine.ShowHexBorders = value;
    }

    public void CycleLabelMode() => _activeEngine.CycleLabelMode();

    public Skia.GeoRulerRender? GeoRuler =>
        (_activeEngine as Skia.SkiaRenderEngine)?.GeoRuler;

    public IViewLayerImageProvider? ViewLayerImageProvider =>
        (_activeEngine as Skia.SkiaRenderEngine)?.ViewLayerImageProvider;

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