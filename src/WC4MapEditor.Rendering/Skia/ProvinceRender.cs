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

    // 按六边形尺寸缓存的路径（同 BackGroundRender.GetHexPath）。原先 GetHexPath 每帧 new 一个 SKPath 且
    // Render 里从不释放，是一处原生内存泄漏；尺寸只随缩放变化，条目很少，Dispose 时统一释放。
    private readonly Dictionary<float, SKPath> _hexPathCache = new();

    // 每帧复用的画笔与省会位置列表（只在渲染线程使用），原先每帧重新分配。
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _borderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _capitalHexPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 215, 0, 200) };
    private readonly List<(float X, float Y)> _capitalDrawCalls = new();

    // 省界颜色按省区值缓存，每帧开头清空：同一省区的格子共用同一个省会归属，不必每格重新查归属与军团。
    private readonly Dictionary<int, SKColor> _borderColorCache = new();

    private static readonly double[] EdgeAngles = { -60, 0, 60, 120, 180, 240 };

    // 六边形 6 条边的端点单位向量与外法线方向。这些值只取决于 edgeIndex，
    // 与格子位置、缩放无关，预先算好以避免每格重复做 三角函数 + 开方。
    private static readonly float[] EdgeStartCos = new float[6];
    private static readonly float[] EdgeStartSin = new float[6];
    private static readonly float[] EdgeEndCos = new float[6];
    private static readonly float[] EdgeEndSin = new float[6];
    private static readonly float[] EdgeNormalX = new float[6];
    private static readonly float[] EdgeNormalY = new float[6];

    static ProvinceRender()
    {
        for (int i = 0; i < 6; i++)
        {
            double a0 = Math.PI / 180 * EdgeAngles[i];
            double a1 = Math.PI / 180 * ((EdgeAngles[i] + 60) % 360);

            float c0 = (float)Math.Cos(a0);
            float s0 = (float)Math.Sin(a0);
            float c1 = (float)Math.Cos(a1);
            float s1 = (float)Math.Sin(a1);

            EdgeStartCos[i] = c0;
            EdgeStartSin[i] = s0;
            EdgeEndCos[i] = c1;
            EdgeEndSin[i] = s1;

            // 边中点相对格心的方向（未归一化）→ 归一化后即为边界线的外偏移方向。
            // 原实现中的 screenX/Y 与 hexSize 在归一化时相互抵消，故可预先计算。
            float mx = (c0 + c1) / 2f;
            float my = (s0 + s1) / 2f;
            float len = (float)Math.Sqrt(mx * mx + my * my);
            if (len > 0) { mx /= len; my /= len; }

            EdgeNormalX[i] = mx;
            EdgeNormalY[i] = my;
        }
    }

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
        var fillPaint = _fillPaint;
        var borderPaint = _borderPaint;

        float borderWidth = Math.Max(1.0f, hexSize * 0.09f);
        borderPaint.StrokeWidth = borderWidth;

        _capitalDrawCalls.Clear();
        _borderColorCache.Clear();
        var capitalDrawCalls = enableCapital ? _capitalDrawCalls : null;

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

                // 用画布平移代替「每格复制一份 SKPath 再 Transform」，避免可视范围内逐格分配路径对象。
                canvas.Save();
                canvas.Translate((float)centerX, (float)centerY);
                canvas.DrawPath(hexPath, fillPaint);
                canvas.Restore();

                DrawProvinceBorders(canvas, col, row, provinceId, (float)centerX, (float)centerY, hexSize, borderWidth, borderPaint, mapData);

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

    private void DrawCapitalBatchWithHexagon(SKCanvas canvas, List<(float X, float Y)> drawCalls, float hexSize)
    {
        var paint = _capitalHexPaint;

        float markSize = hexSize * 0.3f;
        // 路径来自缓存，不能 using 释放。
        SKPath hexPath = GetHexPath(markSize);
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
        float screenX, float screenY, float hexSize, float borderWidth, SKPaint paint, MapData mapData)
    {
        // paint 由调用方复用（Style 已为 Stroke、StrokeWidth 已设好），这里只更新颜色，
        // 避免可视范围内逐格 new SKPaint。
        paint.Color = GetBorderColor(provinceValue, mapData);

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
                    // 端点与偏移方向全部查表，省掉原先每格 6 组 三角函数 + 开方。
                    float startX = screenX + hexSize * EdgeStartCos[edgeIndex];
                    float startY = screenY + hexSize * EdgeStartSin[edgeIndex];
                    float endX = screenX + hexSize * EdgeEndCos[edgeIndex];
                    float endY = screenY + hexSize * EdgeEndSin[edgeIndex];

                    float offset = 0.5f * borderWidth;
                    float ox = EdgeNormalX[edgeIndex] * offset;
                    float oy = EdgeNormalY[edgeIndex] * offset;

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

    private SKColor GetBorderColor(int provinceValue, MapData mapData)
    {
        if (_borderColorCache.TryGetValue(provinceValue, out var cached)) return cached;

        var color = ResolveBorderColor(provinceValue, mapData);
        _borderColorCache[provinceValue] = color;
        return color;
    }

    /// <summary>
    /// 省界颜色 = 省会格子所属军团的颜色（对齐 VB 版 ProvinceRender.GetBorderColor）。
    /// <para>
    /// 省会的归属必须用 <see cref="MapData.GetBelongValue"/> 按格子坐标取：征服地图的归属数组只覆盖 MapClip 裁剪区，
    /// 存的是裁剪区内的相对索引，直接拿省会索引去下标归属数组会取到别的格子（VB 同样做了这层换算）。
    /// 省区值没有指向真正的省会（那一格的省区值不等于自身索引）、省会无归属（0xFF）或军团不存在时画黑色。
    /// </para>
    /// </summary>
    private static SKColor ResolveBorderColor(int provinceValue, MapData mapData)
    {
        int width = mapData.MapWidth;
        if (width <= 0 || provinceValue < 0 || provinceValue >= width * mapData.MapHeight) return SKColors.Black;

        int capitalCol = provinceValue % width;
        int capitalRow = provinceValue / width;
        if (mapData.GetProvinceRef(capitalCol, capitalRow).ProvinceValue != provinceValue) return SKColors.Black;

        int belong = mapData.GetBelongValue(capitalCol, capitalRow);
        var legions = mapData.Legions;
        if (belong == 0xFF || belong < 0 || belong >= legions.Count) return SKColors.Black;

        var legion = legions[belong];
        return new SKColor(legion.ColorR, legion.ColorG, legion.ColorB);
    }

    private SKPath GetHexPath(float hexSize)
    {
        if (_hexPathCache.TryGetValue(hexSize, out var cached)) return cached;

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
        _hexPathCache[hexSize] = path;
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

        foreach (var path in _hexPathCache.Values)
            path.Dispose();
        _hexPathCache.Clear();

        _fillPaint.Dispose();
        _borderPaint.Dispose();
        _capitalHexPaint.Dispose();
    }
}