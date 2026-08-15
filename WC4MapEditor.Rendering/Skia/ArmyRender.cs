using SkiaSharp;


using WC4MapEditor.Core.Helpers;


using WC4MapEditor.Models;


namespace WC4MapEditor.Rendering.Skia;


public class ArmyRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 64;

    private const double UNIT_IMAGE_SIZE_RATIO = 0.9;

    private const double BASE_HEX_SIZE = 20.0;

    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;

    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;

    private readonly MapData _mapData;

    private bool _disposed;

    public ArmyRender(Camera camera, MapData mapData)    
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
 IsAntialias = true 
}
;


        using var font = new SKFont(SKTypeface.Default, 12);


        using var textPaint = new SKPaint 
{
 IsAntialias = true, Color = SKColors.Yellow 
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
                bool hasArmy = (col * 3 + row * 7) % 11 == 0;

                if (hasArmy)                
{
                    float unitSize = hexSize * (float)UNIT_IMAGE_SIZE_RATIO;
                    var rect = new SKRect(                        (float)centerX - unitSize / 2,                        (float)centerY - unitSize / 2,                        (float)centerX + unitSize / 2,                        (float)centerY + unitSize / 2);
                    paint.Color = new SKColor(255, 100, 100, 230);
                    canvas.DrawRect(rect, paint);
                    canvas.DrawText("A", (float)centerX, (float)centerY + 4,                        SKTextAlign.Center, font, textPaint);                
}
            
}
        
}
    
}
    public void Dispose()    
{
        if (_disposed) return;        _disposed = true;    
}

}
