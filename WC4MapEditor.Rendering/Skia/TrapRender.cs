using SkiaSharp;


using WC4MapEditor.Core.Helpers;


using WC4MapEditor.Models;


namespace WC4MapEditor.Rendering.Skia;


public class TrapRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;

    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;

    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;

    private readonly MapData _mapData;

    private bool _disposed;

    public TrapRender(Camera camera, MapData mapData)    
{
        _camera = camera;        _mapData = mapData;    
}
    public void Render(SKCanvas canvas)    
{
        if (canvas == null || _mapData == null) return;
        float hexSize = (float)(BASE_HEX_SIZE * _camera.ZoomLevel);

        double hexSpacingX = HEX_HORIZONTAL_SPACING * _camera.ZoomLevel;

        double hexSpacingY = HEX_VERTICAL_SPACING * _camera.ZoomLevel;
        var (startCol, startRow, endCol, endRow) = _camera.GetVisibleHexRange();

        using var paint = new SKPaint 
{
 IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 
}
;


        using var font = new SKFont(SKTypeface.Default, 10);


        using var textPaint = new SKPaint 
{
 IsAntialias = true, Color = SKColors.Orange 
}
;


        for (int col = startCol;
 col <= endCol;
 col++)        
{
            double centerX = _camera.OffsetX + col * hexSpacingX;

            double rowOffsetY = _camera.OffsetY + (col % 2) * (hexSpacingY / 2);

            for (int row = startRow;
 row <= endRow;
 row++)            
{
                double centerY = rowOffsetY + row * hexSpacingY;
                bool hasTrap = (col + row * 2) % 13 == 0;

                if (hasTrap)                
{
                    paint.Color = SKColors.Orange;
                    float r = hexSize * 0.6f;
                    canvas.DrawCircle((float)centerX, (float)centerY, r, paint);
                    canvas.DrawText("T", (float)centerX, (float)centerY + 4,                        SKTextAlign.Center, font, textPaint);                
}
            
}
        
}
    
}
    public void Dispose()    
{
        if (_disposed) return;        _disposed = true;    
}

}
