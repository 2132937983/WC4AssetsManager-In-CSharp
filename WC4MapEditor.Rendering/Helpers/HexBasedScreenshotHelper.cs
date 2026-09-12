using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Rendering.Skia;

namespace WC4MapEditor.Rendering.Helpers;

public class HexBasedScreenshotHelper : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_WIDTH = BASE_HEX_SIZE * 2;
    private static readonly double HEX_HEIGHT = BASE_HEX_SIZE * Math.Sqrt(3);
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private static string GetScreenshotResolutionFromSetting()
    {
        try
        {
            var resolution = ConfigManager.Instance.GetText("screenshot_resolution", "1x").Trim();
            string[] validResolutions = ["1x", "2x", "4x", "8x", "16x", "32x", "64x"];
            if (validResolutions.Contains(resolution))
                return resolution;
            return "1x";
        }
        catch { return "1x"; }
    }

    private static double GetScaleFactorFromResolution(string resolution) => resolution switch
    {
        "1x" => 1.0, "2x" => 0.5, "4x" => 0.25, "8x" => 0.125,
        "16x" => 0.0625, "32x" => 0.03125, "64x" => 0.015625, _ => 1.0
    };

    public bool CaptureMap(MapData mapData, string outputPath, double zoomLevel = 1.0,
        bool showGridLines = false, bool showHexLabels = false, bool showLayer2 = false,
        IProgress<(int current, int total, int row, int col, int percent)>? progress = null)
    {
        try
        {
            if (mapData == null)
            {
                Debug.WriteLine("[HexBasedScreenshotHelper] MapData is null");
                return false;
            }

            int mapWidth = mapData.MapWidth;
            int mapHeight = mapData.MapHeight;
            int totalHexes = mapWidth * mapHeight;

            var resolution = GetScreenshotResolutionFromSetting();
            var resolutionScale = GetScaleFactorFromResolution(resolution);
            double renderZoom = Math.Max(0.1, zoomLevel * resolutionScale);

            int targetWidth = (int)(mapWidth * HEX_HORIZONTAL_SPACING * renderZoom + HEX_WIDTH * renderZoom);
            int targetHeight = (int)(mapHeight * HEX_VERTICAL_SPACING * renderZoom + HEX_HEIGHT * renderZoom);

            const int maxDimension = 16384;
            if (targetWidth > maxDimension || targetHeight > maxDimension)
            {
                double scale = Math.Min(maxDimension / (double)targetWidth, maxDimension / (double)targetHeight);
                targetWidth = (int)(targetWidth * scale);
                targetHeight = (int)(targetHeight * scale);
                renderZoom *= scale;
            }

            Debug.WriteLine("[HexBasedScreenshotHelper] Map: " + mapWidth + "x" + mapHeight + ", Target: " + targetWidth + "x" + targetHeight);

            progress?.Report((0, totalHexes, 0, 0, 0));

            using var render = new BackGroundRender();
            if (render.GetLandTerrainsRender() == null)
                render.SetLandTerrainsRender(new LandTerrainsRender());

            render.ShowHexLabels = showHexLabels;

            double offsetX = BASE_HEX_SIZE * renderZoom;
            double offsetY = HEX_VERTICAL_SPACING * renderZoom / 2;

            using var bitmap = new SKBitmap(new SKImageInfo(targetWidth, targetHeight));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                render.RenderFullMap(canvas, mapData, offsetX, offsetY, renderZoom,
                    targetWidth, targetHeight, showLayer2, showGridLines);
            }

            progress?.Report((totalHexes, totalHexes, mapHeight - 1, mapWidth - 1, 92));

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(outputPath))
            {
                data.SaveTo(stream);
            }

            progress?.Report((totalHexes, totalHexes, mapHeight - 1, mapWidth - 1, 100));
            Debug.WriteLine("[HexBasedScreenshotHelper] Screenshot saved: " + outputPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[HexBasedScreenshotHelper] Capture failed: " + ex.Message);
            return false;
        }
    }

    public static string GenerateDefaultFileName() => "HexScreenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

    public void Dispose()
    {
    }
}
