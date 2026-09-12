using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering;

public interface IRenderEngine : IDisposable
{
    bool IsAvailable { get; }
    string EngineName { get; }
    void Initialize(IntPtr hwnd, int width, int height);
    void Resize(int width, int height);
    void Render(MapData mapData, Camera camera);
    void Render(SKCanvas canvas, MapData mapData, Camera camera);
    void Invalidate();
    void InvalidateTerrainCache();
    void InvalidateProvinceCache();
    void InvalidateCoastCache(MapData mapData);
    void InvalidateCoastCacheRegion(MapData mapData, int centerCol, int centerRow, int radius);
    void InvalidateCoastCacheFull(MapData mapData);
    bool EnableTerrainsRender { get; set; }
    bool EnableBackgroundRender { get; set; }
    bool EnableProvinceRender { get; set; }
    bool EnableProvinceCapitalRender { get; set; }
    bool EnableBuildingRender { get; set; }
    bool EnableArmyRender { get; set; }
    bool EnableTrapRender { get; set; }
    bool EnableSelectionRender { get; set; }
    string HelpText { get; set; }
    string ModeName { get; set; }
    bool ShowHelp { get; set; }
    bool ShowModeName { get; set; }
    float HelpOpacity { get; set; }
    void UpdateHelpFadeAnimation();
    void SetBrushPreview(int centerCol, int centerRow, int brushSize, string brushShape,
        double zoomLevel, double offsetX, double offsetY, bool visible);
    void HideBrushPreview();
    (int col, int row) ScreenToHex(double screenX, double screenY);
    (double x, double y) HexToScreen(int col, int row);
    bool LoadViewLayerImage(string imagePath);
    void ClearViewLayerImage();
    bool ViewLayerVisible { get; set; }
    float ViewLayerOpacity { get; set; }
    void InvalidateViewLayerCache();
    bool EnableLegionDomainRender { get; set; }
    bool EnableBelongFlagRender { get; set; }
    bool EnableReinforceNewRender { get; set; }
    bool EnableStrategicConstructionRender { get; set; }
    bool EnableAirForceRender { get; set; }
    bool EnableWeatherRender { get; set; }
    bool EnableMapCaseRender { get; set; }
    bool ShowBuildingNames { get; set; }
    void PreloadBelongFlagAtlas(MapData mapData);
    void InitializeTacticalMapImageCache();
    bool ShowLayer2 { get; set; }
    bool ShowHexBorders { get; set; }
    void CycleLabelMode();
}