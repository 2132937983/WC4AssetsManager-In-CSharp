using SkiaSharp;


using WC4MapEditor.Models;


namespace WC4MapEditor.Rendering.Skia;


public class ProvinceRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;

    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;

    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private const byte PROVINCE_ALPHA = 128;

    private readonly ReaderWriterLockSlim _stateLock = new();

    private double _offsetX = 50.0;

    private double _offsetY = 50.0;

    private double _zoomLevel = 1.0;

    private int _viewportWidth = 800;

    private int _viewportHeight = 600;

    private bool _enableProvinceRender;

    private bool _enableCapitalRender;

    private bool _disposed;

    public bool EnableProvinceRender    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _enableProvinceRender; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _enableProvinceRender = value; 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public bool EnableCapitalRender    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _enableCapitalRender; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _enableCapitalRender = value; 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public double OffsetX    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _offsetX; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _offsetX = value; 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public double OffsetY    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _offsetY; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _offsetY = value; 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public double ZoomLevel    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _zoomLevel; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _zoomLevel = Math.Max(0.1, Math.Min(5.0, value)); 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public int ViewportWidth    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _viewportWidth; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _viewportWidth = Math.Max(1, value); 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public int ViewportHeight    
{
        get 
{
 _stateLock.EnterReadLock();

 try 
{
 return _viewportHeight; 
}
 finally 
{
 _stateLock.ExitReadLock(); 
}
 
}
        set 
{
 _stateLock.EnterWriteLock();

 try 
{
 _viewportHeight = Math.Max(1, value); 
}
 finally 
{
 _stateLock.ExitWriteLock(); 
}
 
}
    
}
    public void Render(SKCanvas canvas, MapData mapData)    
{
        if (!EnableProvinceRender || canvas == null || mapData == null) return;        _stateLock.EnterReadLock();

        double offsetX = _offsetX, offsetY = _offsetY, zoomLevel = _zoomLevel;

        int vpW = _viewportWidth, vpH = _viewportHeight;        _stateLock.ExitReadLock();
        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);

        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;

        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;

        int padding = 1;

        int visibleCols = (int)(vpW / hexSpacingX) + padding * 2;

        int visibleRows = (int)(vpH / hexSpacingY) + padding * 2;

        int startCol = Math.Max(0, (int)((-offsetX) / hexSpacingX) - padding);

        int startRow = Math.Max(0, (int)((-offsetY) / hexSpacingY) - padding);

        int endCol = Math.Min(mapData.MapWidth - 1, startCol + visibleCols);

        int endRow = Math.Min(mapData.MapHeight - 1, startRow + visibleRows);
        SKPath hexPath = GetHexPath(hexSize);

        using var paint = new SKPaint 
{
 IsAntialias = true 
}
;


        for (int col = startCol;
 col <= endCol;
 col++)        
{
            double centerX = offsetX + col * hexSpacingX;

            double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);

            for (int row = startRow;
 row <= endRow;
 row++)            
{
                double centerY = rowOffsetY + row * hexSpacingY;

                int provinceId = (col + row * 7) % 12;
                var color = GetProvinceColor(provinceId);
                paint.Color = color;
                var matrix = SKMatrix.CreateTranslation((float)centerX, (float)centerY);

                using var translatedPath = new SKPath(hexPath);

                translatedPath.Transform(matrix);
                canvas.DrawPath(translatedPath, paint);            
}
        
}
    
}
    private static SKColor GetProvinceColor(int provinceId)    
{
        var colors = new[]        
{
            new SKColor(255, 0, 0, PROVINCE_ALPHA),            new SKColor(0, 255, 0, PROVINCE_ALPHA),            new SKColor(0, 0, 255, PROVINCE_ALPHA),            new SKColor(255, 255, 0, PROVINCE_ALPHA),            new SKColor(255, 0, 255, PROVINCE_ALPHA),            new SKColor(0, 255, 255, PROVINCE_ALPHA),            new SKColor(255, 128, 0, PROVINCE_ALPHA),            new SKColor(128, 0, 255, PROVINCE_ALPHA),            new SKColor(0, 128, 255, PROVINCE_ALPHA),            new SKColor(255, 0, 128, PROVINCE_ALPHA),            new SKColor(128, 255, 0, PROVINCE_ALPHA),            new SKColor(0, 255, 128, PROVINCE_ALPHA),        
}
;

        return colors[provinceId % colors.Length];    
}
    private static SKPath GetHexPath(float hexSize)    
{
        var path = new SKPath();

        double sqrt3 = Math.Sqrt(3);

        double h = hexSize * sqrt3 / 2;
        float hs = hexSize / 2;
        path.MoveTo(-hs, (float)-h);
        path.LineTo(hs, (float)-h);
        path.LineTo(hexSize, 0);
        path.LineTo(hs, (float)h);
        path.LineTo(-hs, (float)h);
        path.LineTo(-hexSize, 0);
        path.Close();

        return path;    
}
    public void Dispose()    
{
        if (_disposed) return;        _disposed = true;        _stateLock.Dispose();    
}

}
