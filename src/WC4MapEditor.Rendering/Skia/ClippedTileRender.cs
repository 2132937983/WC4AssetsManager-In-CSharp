using SkiaSharp;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 给"数据范围外"的格子画红叉：征服地图只有 MapClip 裁剪区内的格子有归属数据，
/// 区外的格子即使放了建筑 / 单位、改了归属，游戏也不会读取。VB 版在建筑、军团、归属、省份等模式里都画这个标记。
/// <para>只遍历可见范围，地图没有裁剪时整层几乎零开销（每格一次整数比较）。</para>
/// </summary>
public sealed class ClippedTileRender : IDisposable
{
    private const double BaseHexSize = 20.0;
    private static readonly double SpacingX = BaseHexSize * 1.5;
    private static readonly double SpacingY = BaseHexSize * Math.Sqrt(3);

    private readonly SKPaint _paint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = new SKColor(230, 40, 40, 210),
        StrokeCap = SKStrokeCap.Round
    };

    private bool _disposed;

    public void Render(SKCanvas canvas, MapData? map, double offsetX, double offsetY, double zoom, int viewportWidth, int viewportHeight)
    {
        if (_disposed || canvas == null || map == null || map.MapWidth <= 0 || map.MapHeight <= 0) return;

        double sx = SpacingX * zoom;
        double sy = SpacingY * zoom;
        float hexSize = (float)(BaseHexSize * zoom);
        float arm = hexSize * 0.45f;
        _paint.StrokeWidth = Math.Max(1f, Math.Min(4f, (float)(zoom * 2)));

        int colMin = Math.Max(0, (int)Math.Floor((-offsetX - hexSize) / sx));
        int colMax = Math.Min(map.MapWidth - 1, (int)Math.Ceiling((viewportWidth - offsetX + hexSize) / sx));
        int rowMin = Math.Max(0, (int)Math.Floor((-offsetY - hexSize - sy / 2) / sy));
        int rowMax = Math.Min(map.MapHeight - 1, (int)Math.Ceiling((viewportHeight - offsetY + hexSize) / sy));
        if (colMin > colMax || rowMin > rowMax) return;

        for (int col = colMin; col <= colMax; col++)
        {
            double cx = offsetX + col * sx;
            double colShift = (col % 2) * (sy / 2);
            for (int row = rowMin; row <= rowMax; row++)
            {
                if (map.IsInBelongRange(col, row)) continue;
                float x = (float)cx;
                float y = (float)(offsetY + colShift + row * sy);
                canvas.DrawLine(x - arm, y - arm, x + arm, y + arm, _paint);
                canvas.DrawLine(x - arm, y + arm, x + arm, y - arm, _paint);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _paint.Dispose();
    }
}
