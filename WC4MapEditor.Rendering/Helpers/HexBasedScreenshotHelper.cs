using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Helpers;

public class HexBasedScreenshotHelper : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_WIDTH = BASE_HEX_SIZE * 2;
    private static readonly double HEX_HEIGHT = BASE_HEX_SIZE * Math.Sqrt(3);
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly TerrainHelper _terrainHelper;
    private bool _showLayer2;

    public HexBasedScreenshotHelper()
    {
        _terrainHelper = new TerrainHelper("MapTerrian");
    }

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

    private static SKColor GetTerrainColor(Terrain terrain)
    {
        try
        {
            return terrain.TileType1 switch
            {
                0 => SKColors.Blue,
                1 => SKColors.Green,
                2 => SKColors.DarkGreen,
                3 => SKColors.Gray,
                4 => SKColors.Yellow,
                5 => SKColors.DarkOliveGreen,
                6 => SKColors.White,
                _ => SKColors.LightGray
            };
        }
        catch { return SKColors.LightGray; }
    }

    private void DrawFlatTopHexagon(SKCanvas canvas, float centerX, float centerY, float hexSize, Terrain terrain)
    {
        try
        {
            float left = centerX - hexSize;
            float top = centerY - (float)(hexSize * Math.Sqrt(3) / 2);
            float right = centerX + hexSize;
            float bottom = centerY + (float)(hexSize * Math.Sqrt(3) / 2);
            var destRect = new SKRect(left, top, right, bottom);

            int terrainType = terrain.TileType1;
            var terrainTypeName = _terrainHelper.GetTerrainTypeName(terrainType);
            bool isSeaTerrain = terrainTypeName == "海洋";
            bool isLandTerrain = !isSeaTerrain;

            if (isLandTerrain)
            {
                int landTerrainId = GetLandTerrainId();
                var landBackgroundImage = _terrainHelper.GetTerrainSkImage(landTerrainId, 0);
                if (landBackgroundImage != null)
                {
                    using var paint = new SKPaint { IsAntialias = true };
                    canvas.DrawImage(landBackgroundImage, destRect, paint);
                }
            }

            SKImage? terrainImage = null;
            if (terrain.TileType1 != 0 || isLandTerrain)
            {
                int decorationType = terrain.DecorationType1;
                terrainImage = _terrainHelper.GetTerrainSkImage(terrainType, decorationType);
            }

            if (terrainImage != null)
            {
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(terrainImage, destRect, paint);
            }
            else if (!isLandTerrain)
            {
                var color = GetTerrainColor(terrain);
                var points = new SKPoint[6];
                points[0] = new SKPoint(centerX - hexSize, centerY);
                points[1] = new SKPoint(centerX - hexSize / 2, centerY - (float)(hexSize * Math.Sqrt(3) / 2));
                points[2] = new SKPoint(centerX + hexSize / 2, centerY - (float)(hexSize * Math.Sqrt(3) / 2));
                points[3] = new SKPoint(centerX + hexSize, centerY);
                points[4] = new SKPoint(centerX + hexSize / 2, centerY + (float)(hexSize * Math.Sqrt(3) / 2));
                points[5] = new SKPoint(centerX - hexSize / 2, centerY + (float)(hexSize * Math.Sqrt(3) / 2));

                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < 6; i++) path.LineTo(points[i]);
                path.Close();

                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawPath(path, paint);
            }

            DrawLayer2Terrain(canvas, destRect, terrain);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[HexBasedScreenshotHelper] DrawHex failed: " + ex.Message);
        }
    }

    private void DrawLayer2Terrain(SKCanvas canvas, SKRect destRect, Terrain terrain)
    {
        try
        {
            if (!_showLayer2) return;

            int layer2Type = terrain.TileType2;
            int layer2Decoration = terrain.DecorationType2;
            if (layer2Type == 0) return;

            var layer2TypeName = _terrainHelper.GetTerrainTypeName(layer2Type);
            if (string.IsNullOrEmpty(layer2TypeName)) return;

            var layer2Image = _terrainHelper.GetTerrainSkImage(layer2Type, layer2Decoration);
            if (layer2Image != null && layer2Image.Width > 64)
            {
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(layer2Image, destRect, paint);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[HexBasedScreenshotHelper] DrawLayer2 failed: " + ex.Message);
        }
    }

    private int GetLandTerrainId()
    {
        try
        {
            var allTypes = _terrainHelper.GetAllTerrainTypes();
            foreach (var kvp in allTypes)
            {
                if (kvp.Value == "平地") return kvp.Key;
            }
        }
        catch { }
        return 0;
    }

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

            _showLayer2 = showLayer2;

            int mapWidth = mapData.MapWidth;
            int mapHeight = mapData.MapHeight;
            int totalHexes = mapWidth * mapHeight;

            var resolution = GetScreenshotResolutionFromSetting();
            var resolutionScale = GetScaleFactorFromResolution(resolution);

            int targetWidth = (int)(mapWidth * HEX_HORIZONTAL_SPACING * zoomLevel * resolutionScale + HEX_WIDTH * zoomLevel * resolutionScale);
            int targetHeight = (int)(mapHeight * HEX_VERTICAL_SPACING * zoomLevel * resolutionScale + HEX_HEIGHT * zoomLevel * resolutionScale);

            const int maxDimension = 16384;
            if (targetWidth > maxDimension || targetHeight > maxDimension)
            {
                double scale = Math.Min(maxDimension / (double)targetWidth, maxDimension / (double)targetHeight);
                targetWidth = (int)(targetWidth * scale);
                targetHeight = (int)(targetHeight * scale);
                zoomLevel *= scale;
            }

            Debug.WriteLine("[HexBasedScreenshotHelper] Map: " + mapWidth + "x" + mapHeight + ", Target: " + targetWidth + "x" + targetHeight);

            progress?.Report((0, totalHexes, 0, 0, 0));

            using var bitmap = new SKBitmap(new SKImageInfo(targetWidth, targetHeight));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);

                float hexSize = (float)(BASE_HEX_SIZE * zoomLevel * resolutionScale);
                float cellWidth = (float)(HEX_HORIZONTAL_SPACING * zoomLevel * resolutionScale);
                float cellHeight = (float)(HEX_VERTICAL_SPACING * zoomLevel * resolutionScale);

                int processed = 0;
                int reportInterval = Math.Max(1, totalHexes / 100);

                for (int row = 0; row < mapHeight; row++)
                {
                    for (int col = 0; col < mapWidth; col++)
                    {
                        float x = col * cellWidth + cellWidth / 2;
                        float y = row * cellHeight + cellHeight / 2;

                        if (col % 2 == 1)
                            y += cellHeight / 2;

                        var terrain = mapData.GetTerrainAt(col, row);
                        DrawFlatTopHexagon(canvas, x, y, hexSize, terrain);

                        processed++;
                        if (processed % reportInterval == 0)
                        {
                            int percent = (int)(processed * 90.0 / totalHexes);
                            progress?.Report((processed, totalHexes, row, col, percent));
                        }
                    }
                }
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
        _terrainHelper.ClearAllCaches();
    }
}