using SkiaSharp;

namespace WC4MapEditor.Rendering.Skia;

public sealed class GeoRulerMarker
{
    public int Id { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class GeoRulerRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private const float MARKER_SIZE = 14f;
    private const float LABEL_FONT_SIZE = 10f;

    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double ZoomLevel { get; set; } = 1.0;
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

    public List<GeoRulerMarker> Markers { get; set; } = [];
    public int? DraggingMarkerId { get; set; }

    private SKFont? _labelFont;
    private SKPaint? _markerPaint;
    private SKPaint? _markerDragPaint;
    private SKPaint? _labelPaint;
    private SKPaint? _labelBgPaint;
    private SKPaint? _linePaint;
    private bool _paintsInitialized;

    public float MarkerSize => MARKER_SIZE * (float)ZoomLevel;
    public float LabelFontSize => Math.Max(6f, LABEL_FONT_SIZE * (float)ZoomLevel);

    public int AddMarker(int row, int col, double lat = 0, double lon = 0)
    {
        int id = Markers.Count > 0 ? Markers.Max(m => m.Id) + 1 : 1;
        Markers.Add(new GeoRulerMarker
        {
            Id = id,
            Row = row,
            Col = col,
            Latitude = lat,
            Longitude = lon
        });
        return id;
    }

    public void RemoveMarker(int id)
    {
        Markers.RemoveAll(m => m.Id == id);
    }

    public void ClearMarkers()
    {
        Markers.Clear();
    }

    // 获取上侧标记的屏幕位置（经度标尺，按列定位）
    private (float mx, float my) GetTopMarkerScreenPos(GeoRulerMarker marker, double hexSpacingX, float size)
    {
        float mx = (float)(OffsetX + marker.Col * hexSpacingX);
        float my = (float)(OffsetY - size);
        return (mx, my);
    }

    // 获取左侧标记的屏幕位置（纬度标尺，按行定位）
    private (float mx, float my) GetLeftMarkerScreenPos(GeoRulerMarker marker, double hexSpacingY, float size)
    {
        float mx = (float)(OffsetX - size);
        float my = (float)(OffsetY + marker.Row * hexSpacingY);
        return (mx, my);
    }

    public bool HitTestMarker(double screenX, double screenY, out int markerId)
    {
        markerId = -1;
        float size = MarkerSize;
        float half = size / 2;

        double hexSpacingX = HEX_HORIZONTAL_SPACING * ZoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * ZoomLevel;

        foreach (var marker in Markers)
        {
            // 检测上侧标记
            var (topMx, topMy) = GetTopMarkerScreenPos(marker, hexSpacingX, size);
            if (screenX >= topMx - half && screenX <= topMx + half &&
                screenY >= topMy - half && screenY <= topMy + half)
            {
                markerId = marker.Id;
                return true;
            }

            // 检测左侧标记
            var (leftMx, leftMy) = GetLeftMarkerScreenPos(marker, hexSpacingY, size);
            if (screenX >= leftMx - half && screenX <= leftMx + half &&
                screenY >= leftMy - half && screenY <= leftMy + half)
            {
                markerId = marker.Id;
                return true;
            }
        }

        return false;
    }

    public void SnapDraggingMarkerToNearest(double screenX, double screenY)
    {
        if (!DraggingMarkerId.HasValue) return;

        var marker = Markers.FirstOrDefault(m => m.Id == DraggingMarkerId.Value);
        if (marker == null) return;

        double hexSpacingX = HEX_HORIZONTAL_SPACING * ZoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * ZoomLevel;

        marker.Col = Math.Clamp((int)Math.Round((screenX - OffsetX) / hexSpacingX), 0, MapWidth - 1);
        marker.Row = Math.Clamp((int)Math.Round((screenY - OffsetY) / hexSpacingY), 0, MapHeight - 1);
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null) return;
        EnsurePaints();
        UpdateFontSize();

        double hexSpacingX = HEX_HORIZONTAL_SPACING * ZoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * ZoomLevel;
        float size = MarkerSize;
        float half = size / 2;

        foreach (var marker in Markers)
        {
            bool isDragging = DraggingMarkerId == marker.Id;
            var paint = isDragging ? _markerDragPaint! : _markerPaint!;

            // ========== 上侧标记（经度标尺）==========
            var (topMx, topMy) = GetTopMarkerScreenPos(marker, hexSpacingX, size);

            // 绘制虚线连接到对应格子上边缘
            float cellTopX = (float)(OffsetX + marker.Col * hexSpacingX);
            float cellTopY = (float)OffsetY;
            DrawDashedLine(canvas, topMx, topMy + half, cellTopX, cellTopY);

            // 绘制标记（菱形）
            DrawDiamond(canvas, topMx, topMy, half, paint);

            // 绘制列标签（在标记上方）
            string colLabel = $"C{marker.Col}";
            DrawLabel(canvas, colLabel, topMx, topMy - half - 4, true);

            // 绘制经度值（在标记下方）
            string lonText = $"{marker.Longitude:F4}";
            DrawLabel(canvas, lonText, topMx, topMy + half + _labelFont!.Size + 4, false);

            // ========== 左侧标记（纬度标尺）==========
            var (leftMx, leftMy) = GetLeftMarkerScreenPos(marker, hexSpacingY, size);

            // 绘制虚线连接到对应格子左边缘
            float cellLeftX = (float)OffsetX;
            float cellLeftY = (float)(OffsetY + marker.Row * hexSpacingY);
            DrawDashedLine(canvas, leftMx + half, leftMy, cellLeftX, cellLeftY);

            // 绘制标记（菱形）
            DrawDiamond(canvas, leftMx, leftMy, half, paint);

            // 绘制行标签（在标记左侧）
            string rowLabel = $"R{marker.Row}";
            float rowLabelWidth = _labelFont!.MeasureText(rowLabel);
            DrawLabel(canvas, rowLabel, leftMx - half - 4, leftMy, true, true);

            // 绘制纬度值（在标记右侧）
            string latText = $"{marker.Latitude:F4}";
            DrawLabel(canvas, latText, leftMx + half + 4, leftMy, false, false);
        }
    }

    private void DrawDiamond(SKCanvas canvas, float cx, float cy, float half, SKPaint paint)
    {
        using var path = new SKPath();
        path.MoveTo(cx, cy - half);
        path.LineTo(cx + half, cy);
        path.LineTo(cx, cy + half);
        path.LineTo(cx - half, cy);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawLabel(SKCanvas canvas, string text, float x, float y, bool isAboveOrLeft, bool alignRight = false)
    {
        float textWidth = _labelFont!.MeasureText(text);
        float textHeight = _labelFont.Size;

        float drawX, drawY;
        SKRect bgRect;

        if (alignRight)
        {
            // 右对齐（用于左侧标记的标签，显示在标记左边）
            drawX = x - textWidth;
            drawY = y + textHeight / 3;
            bgRect = new SKRect(drawX - 2, drawY - textHeight - 1, drawX + textWidth + 2, drawY + 2);
        }
        else
        {
            // 居中
            drawX = x - textWidth / 2;
            drawY = isAboveOrLeft ? y : y + textHeight / 3;
            bgRect = new SKRect(drawX - 2, drawY - textHeight - 1, drawX + textWidth + 2, drawY + 2);
        }

        canvas.DrawRect(bgRect, _labelBgPaint!);
        canvas.DrawText(text, drawX, drawY, SKTextAlign.Left, _labelFont, _labelPaint!);
    }

    private void DrawDashedLine(SKCanvas canvas, float x1, float y1, float x2, float y2)
    {
        using var path = new SKPath();
        path.MoveTo(x1, y1);
        path.LineTo(x2, y2);

        using var effect = SKPathEffect.CreateDash(new[] { 4f * (float)ZoomLevel, 4f * (float)ZoomLevel }, 0);
        _linePaint!.PathEffect = effect;
        canvas.DrawPath(path, _linePaint);
        _linePaint.PathEffect = null;
    }

    private void UpdateFontSize()
    {
        if (_labelFont == null) return;
        float newSize = LabelFontSize;
        if (Math.Abs(_labelFont.Size - newSize) > 0.5f)
        {
            _labelFont?.Dispose();
            _labelFont = new SKFont(SKTypeface.Default, newSize);
        }
    }

    private void EnsurePaints()
    {
        if (_paintsInitialized) return;
        _paintsInitialized = true;

        _markerPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 215, 0, 230),
            Style = SKPaintStyle.Fill
        };

        _markerDragPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 100, 100, 255),
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill
        };

        _labelBgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 180),
            Style = SKPaintStyle.Fill
        };

        _linePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 215, 0, 180),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        _labelFont = new SKFont(SKTypeface.Default, LabelFontSize);
    }

    public void Dispose()
    {
        _markerPaint?.Dispose();
        _markerDragPaint?.Dispose();
        _labelPaint?.Dispose();
        _labelBgPaint?.Dispose();
        _linePaint?.Dispose();
        _labelFont?.Dispose();
    }
}