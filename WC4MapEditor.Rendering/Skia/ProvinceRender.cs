using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

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

    private SKBitmap? _capitalImage;
    private readonly object _capitalImageLock = new();
    private readonly Dictionary<int, SKBitmap> _capitalTileCache = new();
    private readonly object _capitalTileCacheLock = new();

    private static readonly double[] EdgeAngles = { -60, 0, 60, 120, 180, 240 };

    public bool EnableProvinceRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableProvinceRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableProvinceRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public bool EnableCapitalRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableCapitalRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableCapitalRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetX
    {
        get { _stateLock.EnterReadLock(); try { return _offsetX; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetX = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetY
    {
        get { _stateLock.EnterReadLock(); try { return _offsetY; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetY = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double ZoomLevel
    {
        get { _stateLock.EnterReadLock(); try { return _zoomLevel; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _zoomLevel = Math.Max(0.1, Math.Min(5.0, value)); } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportWidth
    {
        get { _stateLock.EnterReadLock(); try { return _viewportWidth; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportWidth = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportHeight
    {
        get { _stateLock.EnterReadLock(); try { return _viewportHeight; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportHeight = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    public void InvalidateCache()
    {
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (!EnableProvinceRender || canvas == null || mapData == null) return;

        _stateLock.EnterReadLock();
        double offsetX = _offsetX, offsetY = _offsetY, zoomLevel = _zoomLevel;
        int vpW = _viewportWidth, vpH = _viewportHeight;
        bool enableCapital = _enableCapitalRender;
        _stateLock.ExitReadLock();

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
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        float borderWidth = Math.Max(1.0f, hexSize * 0.09f);
        borderPaint.StrokeWidth = borderWidth;

        var capitalDrawCalls = enableCapital ? new List<(float X, float Y)>() : null;

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            double rowOffsetY = offsetY + (col % 2) * (hexSpacingY / 2);

            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = rowOffsetY + row * hexSpacingY;
                var province = mapData.GetProvince(col, row);
                if (!province.IsValid) continue;

                int provinceId = province.ProvinceValue;
                var fillColor = GetProvinceColor(provinceId);
                fillPaint.Color = fillColor;

                var matrix = SKMatrix.CreateTranslation((float)centerX, (float)centerY);
                using var translatedPath = new SKPath(hexPath);
                translatedPath.Transform(matrix);
                canvas.DrawPath(translatedPath, fillPaint);

                DrawProvinceBorders(canvas, col, row, provinceId, (float)centerX, (float)centerY, hexSize, borderWidth, mapData, mapData.Belongs, mapData.Legions);

                if (enableCapital && capitalDrawCalls != null)
                {
                    int index = row * mapData.MapWidth + col;
                    if (provinceId == index)
                        capitalDrawCalls.Add(((float)centerX, (float)centerY));
                }
            }
        }

        if (enableCapital && capitalDrawCalls != null && capitalDrawCalls.Count > 0)
            DrawCapitalBatch(canvas, capitalDrawCalls, hexSize);
    }

    private void DrawCapitalBatch(SKCanvas canvas, List<(float X, float Y)> drawCalls, float hexSize)
    {
        var capitalImage = GetCapitalImage();
        if (capitalImage != null)
            DrawCapitalBatchWithImage(canvas, drawCalls, hexSize, capitalImage);
        else
            DrawCapitalBatchWithHexagon(canvas, drawCalls, hexSize);
    }

    private void DrawCapitalBatchWithImage(SKCanvas canvas, List<(float X, float Y)> drawCalls, float hexSize, SKBitmap capitalImage)
    {
        var tile = GetCapitalTile(hexSize, capitalImage);
        if (tile == null) return;

        using var paint = new SKPaint { IsAntialias = true };
        float markSize = hexSize * 1.5f;
        float halfMarkSize = markSize / 2.0f;

        foreach (var drawCall in drawCalls)
        {
            var destRect = new SKRect(
                drawCall.X - halfMarkSize,
                drawCall.Y - halfMarkSize,
                drawCall.X + halfMarkSize,
                drawCall.Y + halfMarkSize
            );
            canvas.DrawBitmap(tile, destRect, paint);
        }
    }

    private static void DrawCapitalBatchWithHexagon(SKCanvas canvas, List<(float X, float Y)> drawCalls, float hexSize)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 215, 0, 200)
        };

        float markSize = hexSize * 0.3f;
        using var hexPath = GetHexPath(markSize);
        foreach (var drawCall in drawCalls)
        {
            canvas.Save();
            canvas.Translate(drawCall.X, drawCall.Y);
            canvas.DrawPath(hexPath, paint);
            canvas.Restore();
        }
    }

    private SKBitmap? GetCapitalImage()
    {
        lock (_capitalImageLock)
        {
            if (_capitalImage != null) return _capitalImage;

            try
            {
                string imagePath = ConfigManager.Instance.GetProvinceCapitalImagePath();
                if (!File.Exists(imagePath))
                {
                    Debug.WriteLine($"[ProvinceRender] 省会标记图片不存在: {imagePath}");
                    return null;
                }

                using var stream = File.OpenRead(imagePath);
                _capitalImage = SKBitmap.Decode(stream);

                Debug.WriteLine($"[ProvinceRender] 加载省会标记图片: {imagePath}");
                return _capitalImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProvinceRender] 加载省会标记图片失败: {ex.Message}");
                return null;
            }
        }
    }

    private SKBitmap? GetCapitalTile(float hexSize, SKBitmap originalImage)
    {
        float targetSize = hexSize * 1.5f;
        int targetWidth = (int)Math.Ceiling(targetSize);
        int targetHeight = (int)Math.Ceiling(targetSize);
        int cacheKey = targetWidth;

        lock (_capitalTileCacheLock)
        {
            if (_capitalTileCache.TryGetValue(cacheKey, out var cached)) return cached;

            try
            {
                var scaledBitmap = new SKBitmap(targetWidth, targetHeight);
                using var tileCanvas = new SKCanvas(scaledBitmap);
                tileCanvas.Clear(SKColors.Transparent);

                var srcRect = new SKRect(0, 0, originalImage.Width, originalImage.Height);
                var destRect = new SKRect(0, 0, targetWidth, targetHeight);

                using var paint = new SKPaint { IsAntialias = true };
                tileCanvas.DrawBitmap(originalImage, srcRect, destRect, paint);

                _capitalTileCache[cacheKey] = scaledBitmap;

                Debug.WriteLine($"[ProvinceRender] 创建省会标记瓦片: {targetWidth}x{targetHeight}");
                return scaledBitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProvinceRender] 创建省会标记瓦片失败: {ex.Message}");
                return null;
            }
        }
    }

    private void DrawProvinceBorders(SKCanvas canvas, int col, int row, int provinceValue,
        float screenX, float screenY, float hexSize, float borderWidth, MapData mapData,
        List<string>? belongs, ObservableCollection<Legion>? legions)
    {
        var borderColor = GetBorderColor(provinceValue, belongs, legions);
        using var paint = new SKPaint
        {
            Color = borderColor,
            StrokeWidth = borderWidth,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        for (int edgeIndex = 0; edgeIndex < 6; edgeIndex++)
        {
            GetNeighbor(col, row, edgeIndex, out int neighborCol, out int neighborRow);

            if (neighborCol >= 0 && neighborCol < mapData.MapWidth &&
                neighborRow >= 0 && neighborRow < mapData.MapHeight)
            {
                var neighborProvince = mapData.GetProvince(neighborCol, neighborRow);
                int neighborProvinceValue = neighborProvince.ProvinceValue;

                if (!neighborProvince.IsValid) neighborProvinceValue = -1;

                if (neighborProvinceValue != provinceValue)
                {
                    double angleDeg = EdgeAngles[edgeIndex];
                    double angleRad = Math.PI / 180 * angleDeg;
                    float startX = screenX + hexSize * (float)Math.Cos(angleRad);
                    float startY = screenY + hexSize * (float)Math.Sin(angleRad);

                    double nextAngleDeg = (angleDeg + 60) % 360;
                    double nextAngleRad = Math.PI / 180 * nextAngleDeg;
                    float endX = screenX + hexSize * (float)Math.Cos(nextAngleRad);
                    float endY = screenY + hexSize * (float)Math.Sin(nextAngleRad);

                    float edgeMidX = (startX + endX) / 2;
                    float edgeMidY = (startY + endY) / 2;
                    float dirX = edgeMidX - screenX;
                    float dirY = edgeMidY - screenY;
                    float dirLength = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
                    if (dirLength > 0) { dirX /= dirLength; dirY /= dirLength; }

                    float offset = 0.5f * borderWidth;
                    float ox = dirX * offset;
                    float oy = dirY * offset;

                    canvas.DrawLine(startX - ox, startY - oy, endX - ox, endY - oy, paint);
                }
            }
        }
    }

    private static void GetNeighbor(int col, int row, int edgeIndex, out int neighborCol, out int neighborRow)
    {
        neighborCol = -1;
        neighborRow = -1;

        if (col % 2 == 0)
        {
            switch (edgeIndex)
            {
                case 0: neighborCol = col + 1; neighborRow = row - 1; break;
                case 1: neighborCol = col + 1; neighborRow = row; break;
                case 2: neighborCol = col; neighborRow = row + 1; break;
                case 3: neighborCol = col - 1; neighborRow = row; break;
                case 4: neighborCol = col - 1; neighborRow = row - 1; break;
                case 5: neighborCol = col; neighborRow = row - 1; break;
            }
        }
        else
        {
            switch (edgeIndex)
            {
                case 0: neighborCol = col + 1; neighborRow = row; break;
                case 1: neighborCol = col + 1; neighborRow = row + 1; break;
                case 2: neighborCol = col; neighborRow = row + 1; break;
                case 3: neighborCol = col - 1; neighborRow = row + 1; break;
                case 4: neighborCol = col - 1; neighborRow = row; break;
                case 5: neighborCol = col; neighborRow = row - 1; break;
            }
        }
    }

    private static SKColor GetProvinceColor(int provinceValue)
    {
        ushort colorValue = (ushort)(provinceValue & 0xFFFF);
        byte r = (byte)(((colorValue >> 11) & 0x1F) * 8);
        byte g = (byte)(((colorValue >> 5) & 0x3F) * 4);
        byte b = (byte)((colorValue & 0x1F) * 8);
        return new SKColor(r, g, b, PROVINCE_ALPHA);
    }

    private static SKColor GetBorderColor(int provinceValue, List<string>? belongs, ObservableCollection<Legion>? legions)
    {
        var borderColor = SKColors.Black;

        int? capitalBelongValue = GetCapitalBelongValue(provinceValue, belongs);
        if (capitalBelongValue.HasValue)
        {
            var legionColor = GetLegionColor(capitalBelongValue.Value, legions);
            if (legionColor.HasValue)
            {
                borderColor = legionColor.Value;
            }
        }

        return borderColor;
    }

    private static int? GetCapitalBelongValue(int provinceValue, List<string>? belongs)
    {
        if (provinceValue == 0xFFFF) return null;

        int capitalIndex = provinceValue;
        if (belongs == null || capitalIndex < 0 || capitalIndex >= belongs.Count) return null;

        return GetBelongValue(capitalIndex, belongs);
    }

    private static int? GetBelongValue(int index, List<string>? belongs)
    {
        if (belongs == null || index < 0 || index >= belongs.Count) return null;

        string belongHex = belongs[index];
        if (string.IsNullOrEmpty(belongHex)) return null;

        if (int.TryParse(belongHex, System.Globalization.NumberStyles.HexNumber, null, out int value))
            return value;

        return null;
    }

    private static SKColor? GetLegionColor(int belongValue, ObservableCollection<Legion>? legions)
    {
        if (legions == null || legions.Count == 0) return null;
        if (belongValue == 255) return null;
        if (belongValue < 0 || belongValue >= legions.Count) return null;

        var legion = legions[belongValue];
        return new SKColor(legion.ColorR, legion.ColorG, legion.ColorB);
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
        if (_disposed) return;
        _disposed = true;
        _stateLock.Dispose();

        lock (_capitalImageLock)
        {
            _capitalImage?.Dispose();
            _capitalImage = null;
        }

        lock (_capitalTileCacheLock)
        {
            foreach (var tile in _capitalTileCache.Values)
                tile?.Dispose();
            _capitalTileCache.Clear();
        }
    }
}