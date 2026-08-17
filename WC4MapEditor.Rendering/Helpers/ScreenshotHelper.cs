using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;
using WC4MapEditor.Rendering.Skia;

namespace WC4MapEditor.Rendering.Helpers;

public class ScreenshotHelper : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_WIDTH = BASE_HEX_SIZE * 2;
    private static readonly double HEX_HEIGHT = BASE_HEX_SIZE * Math.Sqrt(3);
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private BackGroundRender? _screenshotRender;

    public ScreenshotHelper() { }

    private static string GetScreenshotResolutionFromSetting()
    {
        try
        {
            var resolution = ConfigManager.Instance.GetText("screenshot_resolution", "1x").Trim();
            string[] validResolutions = ["1x", "2x", "4x", "8x", "16x", "32x", "64x"];
            if (validResolutions.Contains(resolution))
            {
                Debug.WriteLine("[ScreenshotHelper] ConfigManager resolution: " + resolution);
                return resolution;
            }
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
                Debug.WriteLine("[ScreenshotHelper] MapData is null");
                return false;
            }

            int mapWidth = mapData.MapWidth;
            int mapHeight = mapData.MapHeight;
            int totalHexes = mapWidth * mapHeight;

            var resolution = GetScreenshotResolutionFromSetting();
            var resolutionScale = GetScaleFactorFromResolution(resolution);
            double renderZoomLevel = Math.Max(0.1, zoomLevel);

            int targetWidth = (int)(mapWidth * HEX_HORIZONTAL_SPACING * zoomLevel * resolutionScale + HEX_WIDTH * zoomLevel * resolutionScale);
            int targetHeight = (int)(mapHeight * HEX_VERTICAL_SPACING * zoomLevel * resolutionScale + HEX_HEIGHT * zoomLevel * resolutionScale);
            int renderWidth = (int)(mapWidth * HEX_HORIZONTAL_SPACING * renderZoomLevel + HEX_WIDTH * renderZoomLevel);
            int renderHeight = (int)(mapHeight * HEX_VERTICAL_SPACING * renderZoomLevel + HEX_HEIGHT * renderZoomLevel);

            const int maxDimension = 16384;
            if (renderWidth > maxDimension || renderHeight > maxDimension)
            {
                double scale = Math.Min(maxDimension / (double)renderWidth, maxDimension / (double)renderHeight);
                renderWidth = (int)(renderWidth * scale);
                renderHeight = (int)(renderHeight * scale);
                targetWidth = (int)(targetWidth * scale);
                targetHeight = (int)(targetHeight * scale);
                renderZoomLevel *= scale;
            }

            progress?.Report((0, totalHexes, 0, 0, 0));

            Debug.WriteLine("[ScreenshotHelper] Target: " + targetWidth + "x" + targetHeight + ", Render: " + renderWidth + "x" + renderHeight);

            _screenshotRender ??= new BackGroundRender();

            using var bitmap = new SKBitmap(new SKImageInfo(targetWidth, targetHeight));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);

                float scaleX = (float)(targetWidth / (double)renderWidth);
                float scaleY = (float)(targetHeight / (double)renderHeight);
                canvas.Scale(scaleX, scaleY);

                _screenshotRender.ZoomLevel = renderZoomLevel;
                _screenshotRender.OffsetX = HEX_WIDTH * renderZoomLevel / 2;
                _screenshotRender.OffsetY = HEX_HEIGHT * renderZoomLevel / 2;
                _screenshotRender.ViewportWidth = renderWidth;
                _screenshotRender.ViewportHeight = renderHeight;
                _screenshotRender.ShowGridLines = showGridLines;
                _screenshotRender.ShowHexLabels = showHexLabels;
                _screenshotRender.EnableBackgroundRender = true;
                _screenshotRender.EnableTerrainsRender = true;
                _screenshotRender.ShowLayer2 = showLayer2;

                _screenshotRender.Render(canvas, mapData);
            }

            progress?.Report((totalHexes, totalHexes, mapHeight - 1, mapWidth - 1, 50));

            _screenshotRender.Dispose();
            _screenshotRender = null;

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
            Debug.WriteLine("[ScreenshotHelper] Screenshot saved: " + outputPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[ScreenshotHelper] Capture failed: " + ex.Message);
            return false;
        }
    }

    public static string GenerateDefaultFileName() => "MapScreenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

    public void Dispose()
    {
        _screenshotRender?.Dispose();
        _screenshotRender = null;
    }
}