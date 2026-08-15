using SkiaSharp;


using WC4MapEditor.Core.Helpers;


using WC4MapEditor.Models;


namespace WC4MapEditor.Rendering.Skia;


public class BuildingRender : IDisposable
{
    private const double BUILDING_IMAGE_SCALE = 3.3;

    private const double BASE_HEX_SIZE = 20.0;

    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;

    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;

    private readonly MapData _mapData;

    private bool _showBuildingNames = true;

    private bool _disposed;

    public bool ShowBuildingNames    
{
        get => _showBuildingNames;
        set => _showBuildingNames = value;    
}
    public BuildingRender(Camera camera, MapData mapData)    
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


        using var font = new SKFont(SKTypeface.Default, 10);


        using var textPaint = new SKPaint 
{
 IsAntialias = true, Color = SKColors.White 
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
                bool hasBuilding = (col + row) % 5 == 0;

                if (hasBuilding)                
{
                    float drawSize = hexSize * (float)BUILDING_IMAGE_SCALE;
                    var rect = new SKRect(                        (float)centerX - drawSize / 2,                        (float)centerY - drawSize / 2,                        (float)centerX + drawSize / 2,                        (float)centerY + drawSize / 2);
                    paint.Color = new SKColor(200, 150, 50, 200);
                    canvas.DrawRect(rect, paint);

                    if (_showBuildingNames)                    
{
                        string name = $"B{col}-{row}";
                        canvas.DrawText(name, (float)centerX, (float)centerY + 4,                            SKTextAlign.Center, font, textPaint);                    
}
                
}
            
}
        
}
    
}
    public void Dispose()    
{
        if (_disposed) return;        _disposed = true;    
}

}
