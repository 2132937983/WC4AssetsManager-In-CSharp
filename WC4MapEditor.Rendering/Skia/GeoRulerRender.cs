using SkiaSharp;

namespace WC4MapEditor.Rendering.Skia;

public sealed class GeoRulerRender
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

    public int TopMarker1Col { get; set; } = 0;
    public int TopMarker2Col { get; set; } = 1;
    public int LeftMarker1Row { get; set; } = 0;
    public int LeftMarker2Row { get; set; } = 1;

    public double? TopMarker1Lon { get; set; }
    public double? TopMarker2Lon { get; set; }
    public double? LeftMarker1Lat { get; set; }
    public double? LeftMarker2Lat { get; set; }

    public int? DraggingMarker { get; set; }

    private SKFont? _labelFont;
    private SKPaint? _markerPaint;
    private SKPaint? _markerHoverPaint;
    private SKPaint? _markerDragPaint;
    private SKPaint? _labelPaint;
    private SKPaint? _labelBgPaint;
    private SKPaint? _linePaint;
    private bool _paintsInitialized;

    public float MarkerSize => MARKER_SIZE * (float)ZoomLevel;
    public float LabelFontSize => Math.Max(6f, LABEL_FONT_SIZE * (float)ZoomLevel);

    public bool HitTestMarker(double screenX, double screenY, out int markerId)
    {
        markerId = -1;
        float size = MarkerSize;
        float half = size / 2;

        double hexSpacingX = HEX_HORIZONTAL_SPACING * ZoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * ZoomLevel;

        var markers = new[]
        {
            (1, (float)(OffsetX + TopMarker1Col * hexSpacingX), (float)(OffsetY - size)),
            (2, (float)(OffsetX + TopMarker2Col * hexSpacingX), (float)(OffsetY - size)),
            (3, (float)(OffsetX - size), (float)(OffsetY + LeftMarker1Row * hexSpacingY)),
            (4, (float)(OffsetX - size), (float)(OffsetY + LeftMarker2Row * hexSpacingY)),
        };

        foreach (var (id, mx, my) in markers)
        {
            if (screenX >= mx - half && screenX <= mx + half &&
                screenY >= my - half && screenY <= my + half)
            {
                markerId = id;
                return true;
            }
        }

        return false;
    }

    public void SnapDraggingMarkerToNearest(double screenX, double screenY)
    {
        if (!DraggingMarker.HasValue) return;

        double hexSpacingX = HEX_HORIZONTAL_SPACING * ZoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * ZoomLevel;

        switch (DraggingMarker.Value)
        {
            case 1:
                TopMarker1Col = Math.Clamp((int)Math.Round((screenX - OffsetX) / hexSpacingX), 0, MapWidth - 1);
                break;
            case 2:
                TopMarker2Col = Math.Clamp((int)Math.Round((screenX - OffsetX) / hexSpacingX), 0, MapWidth - 1);
                break;
            case 3:
                LeftMarker1Row = Math.Clamp((int)Math.Round((screenY - OffsetY) / hexSpacingY), 0, MapHeight - 1);
                break;
            case 4:
                LeftMarker2Row = Math.Clamp((int)Math.Round((screenY - OffsetY) / hexSpacingY), 0, MapHeight - 1);
                break;
        }
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

        RenderMarker(canvas, 1, (float)(OffsetX + TopMarker1Col * hexSpacingX), (float)(OffsetY - size), size,
            TopMarker1Lon, $"C{TopMarker1Col}");
        RenderMarker(canvas, 2, (float)(OffsetX + TopMarker2Col * hexSpacingX), (float)(OffsetY - size), size,
            TopMarker2Lon, $"C{TopMarker2Col}");
        RenderMarker(canvas, 3, (float)(OffsetX - size), (float)(OffsetY + LeftMarker1Row * hexSpacingY), size,
            LeftMarker1Lat, $"R{LeftMarker1Row}");
        RenderMarker(canvas, 4, (float)(OffsetX - size), (float)(OffsetY + LeftMarker2Row * hexSpacingY), size,
            LeftMarker2Lat, $"R{LeftMarker2Row}");

        DrawDashedLine(canvas, (float)(OffsetX + TopMarker1Col * hexSpacingX), (float)(OffsetY - size) + half,
            (float)(OffsetX + TopMarker1Col * hexSpacingX), (float)OffsetY);
        DrawDashedLine(canvas, (float)(OffsetX + TopMarker2Col * hexSpacingX), (float)(OffsetY - size) + half,
            (float)(OffsetX + TopMarker2Col * hexSpacingX), (float)OffsetY);
        DrawDashedLine(canvas, (float)(OffsetX - size) + half, (float)(OffsetY + LeftMarker1Row * hexSpacingY),
            (float)OffsetX, (float)(OffsetY + LeftMarker1Row * hexSpacingY));
        DrawDashedLine(canvas, (float)(OffsetX - size) + half, (float)(OffsetY + LeftMarker2Row * hexSpacingY),
            (float)OffsetX, (float)(OffsetY + LeftMarker2Row * hexSpacingY));
    }

    private void RenderMarker(SKCanvas canvas, int markerId, float x, float y, float size, double? value, string label)
    {
        float half = size / 2;
        bool isDragging = DraggingMarker == markerId;

        var paint = isDragging ? _markerDragPaint! : _markerPaint!;

        canvas.DrawRect(x - half, y - half, size, size, paint);

        float textWidth = _labelFont!.MeasureText(label);
        float labelY = y + _labelFont.Size / 3;

        if (markerId <= 2)
        {
            float labelX = x - textWidth / 2;
            float bgY = y - half - _labelFont.Size - 4;
            var bgRect = new SKRect(labelX - 2, bgY - 1, labelX + textWidth + 2, bgY + _labelFont.Size + 2);
            canvas.DrawRect(bgRect, _labelBgPaint!);
            canvas.DrawText(label, labelX, bgY + _labelFont.Size, SKTextAlign.Left, _labelFont, _labelPaint!);
        }
        else
        {
            float labelX = x - half - textWidth - 4;
            float bgY = y - _labelFont.Size / 2;
            var bgRect = new SKRect(labelX - 2, bgY - 1, labelX + textWidth + 2, bgY + _labelFont.Size + 2);
            canvas.DrawRect(bgRect, _labelBgPaint!);
            canvas.DrawText(label, labelX, bgY + _labelFont.Size, SKTextAlign.Left, _labelFont, _labelPaint!);
        }

        if (value.HasValue)
        {
            string valText = $"{value.Value:F4}";
            float valWidth = _labelFont.MeasureText(valText);

            if (markerId <= 2)
            {
                float valX = x - valWidth / 2;
                float valY = y + half + _labelFont.Size + 2;
                var bgRect = new SKRect(valX - 2, valY - _labelFont.Size - 1, valX + valWidth + 2, valY + 2);
                canvas.DrawRect(bgRect, _labelBgPaint!);
                canvas.DrawText(valText, valX, valY, SKTextAlign.Left, _labelFont, _labelPaint!);
            }
            else
            {
                float valX = x + half + 4;
                float valY = y + _labelFont.Size / 3;
                var bgRect = new SKRect(valX - 2, valY - _labelFont.Size - 1, valX + valWidth + 2, valY + 2);
                canvas.DrawRect(bgRect, _labelBgPaint!);
                canvas.DrawText(valText, valX, valY, SKTextAlign.Left, _labelFont, _labelPaint!);
            }
        }
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

        _markerHoverPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 235, 100, 255),
            Style = SKPaintStyle.Fill
        };

        _markerDragPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 100, 100, 255),
            Style = SKPaintStyle.Fill
        };

        _linePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 215, 0, 180),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
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
            Color = new SKColor(0, 0, 0, 200),
            Style = SKPaintStyle.Fill
        };

        _labelFont = new SKFont(SKTypeface.Default, LabelFontSize);
    }

    public void Dispose()
    {
        _markerPaint?.Dispose();
        _markerHoverPaint?.Dispose();
        _markerDragPaint?.Dispose();
        _linePaint?.Dispose();
        _labelPaint?.Dispose();
        _labelBgPaint?.Dispose();
        _labelFont?.Dispose();
        _paintsInitialized = false;
    }
}