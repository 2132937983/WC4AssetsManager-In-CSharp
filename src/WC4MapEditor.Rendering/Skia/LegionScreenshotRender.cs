using System.Globalization;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 军团范围截图（军团编辑模式 F4）—— 对齐 VB 版 LegionModifier.CaptureLegionScreenshot：
/// 平顶六边形按省区着色（取省会格所属军团的颜色），省会格画国旗，数据范围外的格子画红叉，上下各留一行空白格。
/// <para>
/// 截图范围取归属数据覆盖的矩形（征服地图即 MapClip 裁剪区，行数按归属数据算，而不是地图高度减裁剪行），
/// 列从截图左边缘开始排（以前按地图绝对列号排，裁剪地图左侧空出一截、右侧被截掉）。
/// 国旗与地图上的首都国旗层同源：战术地图图集优先，文件兜底。
/// 截图尺寸按 setting.txt 的 screenshot_resolution 缩放（1x / 2x … 64x）。
/// </para>
/// </summary>
public static class LegionScreenshotRender
{
    private static readonly SKColor EmptyColor = new(240, 240, 240);
    private static readonly SKColor HexStrokeColor = new(200, 200, 200);

    /// <summary>每格的基准宽高（1x 时的像素，对齐 VB 版的 54 × 62）。</summary>
    private const double BaseCellWidth = 54;
    private const double BaseCellHeight = 62;

    /// <summary>渲染并保存为 PNG，返回文件路径（目录不存在时创建）。</summary>
    public static string SavePng(MapData map, string directory)
    {
        using var image = Render(map, ReadScaleFactor());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"legion_map_screenshot_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.png");

        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    /// <summary>按指定缩放系数（1 = 每格 54 × 62 像素）渲染军团范围图。</summary>
    public static SKImage Render(MapData map, double scaleFactor)
    {
        var (startCol, startRow, cols, rows) = GetBounds(map);

        int imageWidth = Math.Max(1, (int)(cols * BaseCellWidth * scaleFactor));
        int imageHeight = Math.Max(1, (int)((rows + 2) * BaseCellHeight * scaleFactor));
        double cellWidth = (double)imageWidth / cols;
        double cellHeight = (double)imageHeight / (rows + 2);
        float hexSize = (float)(cellHeight / Math.Sqrt(3));

        var provinceColors = BuildProvinceColors(map);
        var flags = new Dictionary<int, SKImage?>();
        var ownedFlags = new List<SKImage>();

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(EmptyColor);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = HexStrokeColor };
        using var cross = new SKPaint { IsAntialias = true, Color = SKColors.Red, StrokeWidth = 4 };
        using var flagPaint = new SKPaint { IsAntialias = true };

        try
        {
            // 显示行 0 与 rows + 1 是上下两行空白格，中间是地图第 startRow 行起的数据行
            for (int displayRow = 0; displayRow < rows + 2; displayRow++)
            {
                int row = startRow + displayRow - 1;
                bool padding = displayRow == 0 || displayRow == rows + 1;

                for (int colIdx = 0; colIdx < cols; colIdx++)
                {
                    int col = startCol + colIdx;
                    float x = (float)(colIdx * cellWidth + cellWidth / 2);
                    float y = (float)(displayRow * cellHeight + cellHeight / 2);
                    if (col % 2 == 1) y += (float)(cellHeight / 2);

                    if (padding)
                    {
                        DrawHexagon(canvas, x, y, hexSize, EmptyColor, fill, stroke);
                        continue;
                    }

                    if (!map.IsInBelongRange(col, row))
                    {
                        DrawHexagon(canvas, x, y, hexSize, EmptyColor, fill, stroke);
                        float len = hexSize * 0.6f;
                        canvas.DrawLine(x - len / 2, y - len / 2, x + len / 2, y + len / 2, cross);
                        canvas.DrawLine(x - len / 2, y + len / 2, x + len / 2, y - len / 2, cross);
                        continue;
                    }

                    int index = row * map.MapWidth + col;
                    int province = map.GetProvinceRef(index).ProvinceValue;
                    var color = provinceColors.TryGetValue(province, out var c) ? c : EmptyColor;
                    DrawHexagon(canvas, x, y, hexSize, color, fill, stroke);

                    // 省会格（省区值等于自身索引）画所属军团的国旗
                    if (province == index && map.ResolveLegionByBelong(map.GetBelongValue(col, row)) is { } legion)
                    {
                        var flag = GetFlag(legion.CountryId, flags, ownedFlags);
                        if (flag != null)
                        {
                            float half = Math.Max(4, hexSize * 0.8f) / 2f;
                            canvas.DrawImage(flag, new SKRect(x - half, y - half, x + half, y + half), flagPaint);
                        }
                    }
                }
            }
            return surface.Snapshot();
        }
        finally
        {
            foreach (var image in ownedFlags) image.Dispose();
        }
    }

    /// <summary>
    /// 截图范围：归属数据覆盖的格子的外接矩形（列起点、行起点、列数、行数）；地图没有归属数据时取全图。
    /// </summary>
    private static (int StartCol, int StartRow, int Cols, int Rows) GetBounds(MapData map)
    {
        int minCol = int.MaxValue, minRow = int.MaxValue, maxCol = -1, maxRow = -1;
        for (int row = 0; row < map.MapHeight; row++)
        {
            for (int col = 0; col < map.MapWidth; col++)
            {
                if (!map.IsInBelongRange(col, row)) continue;
                minCol = Math.Min(minCol, col);
                maxCol = Math.Max(maxCol, col);
                minRow = Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
            }
        }

        if (maxCol < 0) return (0, 0, Math.Max(1, map.MapWidth), Math.Max(1, map.MapHeight));
        return (minCol, minRow, maxCol - minCol + 1, maxRow - minRow + 1);
    }

    /// <summary>省区值（省会格索引）→ 省会格所属军团的颜色；省会没有归属或解析不出军团的省区不着色。</summary>
    private static Dictionary<int, SKColor> BuildProvinceColors(MapData map)
    {
        var colors = new Dictionary<int, SKColor>();
        int total = map.MapWidth * map.MapHeight;
        for (int i = 0; i < total; i++)
        {
            int province = map.GetProvinceRef(i).ProvinceValue;
            if (province == 0 || province == 0xFFFF || province >= total || colors.ContainsKey(province)) continue;

            int belong = map.GetBelongValue(province % map.MapWidth, province / map.MapWidth);
            if (belong == 0xFF) continue;
            if (map.ResolveLegionByBelong(belong) is { } legion)
                colors[province] = new SKColor(legion.ColorR, legion.ColorG, legion.ColorB);
        }
        return colors;
    }

    private static SKImage? GetFlag(int countryId, Dictionary<int, SKImage?> cache, List<SKImage> owned)
    {
        if (cache.TryGetValue(countryId, out var cached)) return cached;
        var image = CapitalFlagRender.LoadFlagImage(countryId, out bool isOwned);
        if (isOwned && image != null) owned.Add(image);
        cache[countryId] = image;
        return image;
    }

    private static void DrawHexagon(SKCanvas canvas, float x, float y, float size, SKColor color, SKPaint fill, SKPaint stroke)
    {
        using var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI / 3 * i;
            float px = x + size * (float)Math.Cos(angle);
            float py = y + size * (float)Math.Sin(angle);
            if (i == 0) path.MoveTo(px, py);
            else path.LineTo(px, py);
        }
        path.Close();

        fill.Color = color;
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    /// <summary>setting.txt 的 screenshot_resolution（1x / 2x / … / 64x）换算成缩放系数，无效时取 1。</summary>
    private static double ReadScaleFactor()
    {
        string resolution;
        try
        {
            resolution = ConfigManager.Instance.GetText("screenshot_resolution", "1x").Trim();
        }
        catch (Exception)
        {
            resolution = "1x";
        }

        return resolution switch
        {
            "2x" => 0.5,
            "4x" => 0.25,
            "8x" => 0.125,
            "16x" => 0.0625,
            "32x" => 0.03125,
            "64x" => 0.015625,
            _ => 1.0
        };
    }
}
