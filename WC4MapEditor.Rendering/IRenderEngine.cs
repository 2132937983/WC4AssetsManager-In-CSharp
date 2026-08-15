using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;

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
    bool EnableTerrainsRender { get; set; }
    bool EnableBackgroundRender { get; set; }
    bool EnableProvinceRender { get; set; }
    bool EnableBuildingRender { get; set; }
    bool EnableArmyRender { get; set; }
    bool EnableTrapRender { get; set; }
    (int col, int row) ScreenToHex(double screenX, double screenY);
    (double x, double y) HexToScreen(int col, int row);
}