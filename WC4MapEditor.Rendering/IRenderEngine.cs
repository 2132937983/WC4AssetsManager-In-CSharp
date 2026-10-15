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
    /// <summary>
    /// 清空军团领域层按归属值缓存的国家颜色。
    /// 军团配色（C 键 / U 键 / 军团设置窗口）改完后必须调用，否则领域着色仍是旧颜色。
    /// </summary>
    void InvalidateLegionDomainCache();
    /// <summary>
    /// 重建归属国旗图集。军团增删改后调用，否则新增军团的国旗不会出现在已构建的图集里。
    /// </summary>
    void InvalidateBelongFlagCache();
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

    /// <summary>已加载的视图层背景图路径（未加载时为空串）</summary>
    string ViewLayerImagePath { get; }

    /// <summary>背景图在磁盘上的原始像素尺寸（未加载时为 0）。用于按图片比例做坐标映射。</summary>
    int ViewLayerSourceWidth { get; }
    int ViewLayerSourceHeight { get; }

    /// <summary>
    /// 设置手绘边界覆盖层（红色），传 null 关闭。
    /// 掩码按"地图逻辑像素"组织，长度须为 pixelWidth × pixelHeight；原地修改后需再次调用以刷新缓存。
    /// </summary>
    void SetProvinceBoundaryOverlay(bool[]? mask, int pixelWidth, int pixelHeight);

    bool ViewLayerVisible { get; set; }
    float ViewLayerOpacity { get; set; }
    void InvalidateViewLayerCache();
    bool EnableLegionDomainRender { get; set; }
    bool EnableBelongFlagRender { get; set; }
    bool EnableCapitalFlagRender { get; set; }
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