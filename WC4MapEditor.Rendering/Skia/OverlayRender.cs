using SkiaSharp;

namespace WC4MapEditor.Rendering.Skia;

public sealed class OverlayRender
{
    private string _helpText = string.Empty;
    private string _modeName = string.Empty;
    private bool _showHelp = true;
    private bool _showModeName = true;
    private float _helpOpacity = 1.0f;
    private float _helpTargetOpacity = 1.0f;
    private const float HelpFadeSpeed = 0.12f;

    private SKPaint? _helpBgPaint;
    private SKPaint? _helpTextPaint;
    private SKPaint? _helpTitlePaint;
    private SKPaint? _modeBgPaint;
    private SKPaint? _modeTextPaint;
    private SKFont? _helpTextFont;
    private SKFont? _helpTitleFont;
    private SKFont? _modeTextFont;
    private bool _paintsInitialized;

    private bool _brushPreviewVisible;
    private int _brushPreviewCenterCol = -1;
    private int _brushPreviewCenterRow = -1;
    private int _brushPreviewSize;
    private string _brushPreviewShape = "圆形";
    private double _brushPreviewZoomLevel = -1;
    private double _brushPreviewOffsetX;
    private double _brushPreviewOffsetY;
    private List<(int col, int row)>? _cachedHexesInBrush;
    private int _cachedBrushCenterCol = -1;
    private int _cachedBrushCenterRow = -1;
    private int _cachedBrushSize = -1;
    private double _cachedBrushZoomLevel = -1;
    private SKPath? _brushPreviewHexPath;
    private SKPaint? _brushPreviewPaint;

    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    public string HelpText
    {
        get => _helpText;
        set => _helpText = value ?? string.Empty;
    }

    public string ModeName
    {
        get => _modeName;
        set => _modeName = value ?? string.Empty;
    }

    public bool ShowHelp
    {
        get => _showHelp;
        set
        {
            _showHelp = value;
            _helpTargetOpacity = value ? 1.0f : 0.0f;
        }
    }

    public bool ShowModeName
    {
        get => _showModeName;
        set => _showModeName = value;
    }

    public float HelpOpacity
    {
        get => _helpOpacity;
        set => _helpOpacity = Math.Max(0.0f, Math.Min(1.0f, value));
    }

    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

    public void UpdateFadeAnimation()
    {
        float diff = _helpTargetOpacity - _helpOpacity;
        if (Math.Abs(diff) < 0.01f)
            _helpOpacity = _helpTargetOpacity;
        else
            _helpOpacity = Math.Max(0.0f, Math.Min(1.0f, _helpOpacity + diff * HelpFadeSpeed));
    }

    public void SetBrushPreview(int centerCol, int centerRow, int brushSize, string brushShape,
        double zoomLevel, double offsetX, double offsetY, bool visible)
    {
        _brushPreviewVisible = visible;
        _brushPreviewCenterCol = centerCol;
        _brushPreviewCenterRow = centerRow;
        _brushPreviewSize = brushSize;
        _brushPreviewShape = brushShape;
        _brushPreviewZoomLevel = zoomLevel;
        _brushPreviewOffsetX = offsetX;
        _brushPreviewOffsetY = offsetY;

        if (visible && (_cachedBrushCenterCol != centerCol || _cachedBrushCenterRow != centerRow ||
            _cachedBrushSize != brushSize || Math.Abs(_cachedBrushZoomLevel - zoomLevel) > 0.001))
        {
            _cachedHexesInBrush = null;
        }
    }

    public void HideBrushPreview()
    {
        _brushPreviewVisible = false;
        _brushPreviewCenterCol = -1;
        _brushPreviewCenterRow = -1;
        _cachedBrushCenterCol = -1;
        _cachedBrushCenterRow = -1;
        _cachedHexesInBrush = null;
    }

    private void EnsurePaints()
    {
        if (_paintsInitialized) return;
        _paintsInitialized = true;

        _helpBgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 180),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var helpTypeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var titleTypeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        _helpTextPaint = new SKPaint { Color = new SKColor(255, 255, 255), IsAntialias = true };
        _helpTitlePaint = new SKPaint { Color = new SKColor(255, 220, 100), IsAntialias = true };
        _modeTextPaint = new SKPaint { Color = new SKColor(255, 255, 255), IsAntialias = true };

        _helpTextFont = new SKFont { Size = 12, Typeface = helpTypeface };
        _helpTitleFont = new SKFont { Size = 13, Typeface = titleTypeface };
        _modeTextFont = new SKFont { Size = 14, Typeface = titleTypeface };

        _modeBgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 160),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _brushPreviewPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 255, 0, 100)
        };

        _brushPreviewHexPath = CreateHexPath(1.0f);
    }

    private static SKPath CreateHexPath(float size)
    {
        var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI / 3 * i;
            float x = size * (float)Math.Cos(angle);
            float y = size * (float)Math.Sin(angle);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null) return;
        EnsurePaints();

        if (_brushPreviewVisible)
            RenderBrushPreview(canvas);

        if (_helpOpacity > 0.01f)
        {
            if (_showModeName && !string.IsNullOrEmpty(_modeName))
                RenderModeName(canvas);

            if (_showHelp && !string.IsNullOrEmpty(_helpText))
                RenderHelpText(canvas);
        }
    }

    private void RenderBrushPreview(SKCanvas canvas)
    {
        if (_brushPreviewCenterCol < 0 || _brushPreviewCenterRow < 0) return;

        double zoomLevel = _brushPreviewZoomLevel;
        if (zoomLevel <= 0) return;

        if (_cachedHexesInBrush == null)
        {
            _cachedHexesInBrush = GetHexesInBrush(_brushPreviewCenterCol, _brushPreviewCenterRow, _brushPreviewSize, _brushPreviewShape);
            _cachedBrushCenterCol = _brushPreviewCenterCol;
            _cachedBrushCenterRow = _brushPreviewCenterRow;
            _cachedBrushSize = _brushPreviewSize;
            _cachedBrushZoomLevel = zoomLevel;
        }

        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);
        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;
        double offsetX = _brushPreviewOffsetX;
        double offsetY = _brushPreviewOffsetY;

        foreach (var (col, row) in _cachedHexesInBrush)
        {
            double centerX = offsetX + col * hexSpacingX;
            double rowOffsetY = offsetY + (col & 1) * (hexSpacingY / 2);
            double centerY = rowOffsetY + row * hexSpacingY;

            canvas.Save();
            canvas.Translate((float)centerX, (float)centerY);
            canvas.Scale(hexSize);
            canvas.DrawPath(_brushPreviewHexPath!, _brushPreviewPaint!);
            canvas.Restore();
        }
    }

    private static List<(int col, int row)> GetHexesInBrush(int centerCol, int centerRow, int brushSize, string brushShape)
    {
        var hexes = new List<(int, int)>();
        if (brushShape == "方形")
        {
            for (int row = centerRow - brushSize; row <= centerRow + brushSize; row++)
            {
                for (int col = centerCol - brushSize; col <= centerCol + brushSize; col++)
                    hexes.Add((col, row));
            }
        }
        else
        {
            for (int row = centerRow - brushSize; row <= centerRow + brushSize; row++)
            {
                for (int col = centerCol - brushSize; col <= centerCol + brushSize; col++)
                {
                    if (CalculateHexDistance(centerCol, centerRow, col, row) <= brushSize)
                        hexes.Add((col, row));
                }
            }
        }
        return hexes;
    }

    private static int CalculateHexDistance(int col1, int row1, int col2, int row2)
    {
        int x1 = col1;
        int z1 = row1 - (col1 >> 1);
        int y1 = -x1 - z1;

        int x2 = col2;
        int z2 = row2 - (col2 >> 1);
        int y2 = -x2 - z2;

        return Math.Max(Math.Abs(x1 - x2), Math.Max(Math.Abs(y1 - y2), Math.Abs(z1 - z2)));
    }

    private void RenderModeName(SKCanvas canvas)
    {
        float marginRight = 20;
        float marginTop = 30;
        float textX = ViewportWidth - marginRight;
        float textY = marginTop;

        byte opacityByte = (byte)(_helpOpacity * 255);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, (byte)(200 * _helpOpacity)),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawText(_modeName, textX + 2, textY + 2, SKTextAlign.Right, _modeTextFont!, shadowPaint);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 215, 0, opacityByte),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawText(_modeName, textX, textY, SKTextAlign.Right, _modeTextFont!, textPaint);
    }

    private void RenderHelpText(SKCanvas canvas)
    {
        float marginLeft = 15;
        float marginTop = 15;
        float startX = marginLeft;
        float startY = marginTop + _helpTextFont!.Size;

        var lines = _helpText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return;

        int contentLines = lines.Length - 1;
        float maxWidth = 0;
        float lineHeight = _helpTextFont.Size + 4;
        for (int i = 1; i < lines.Length; i++)
        {
            float w = _helpTextFont.MeasureText(lines[i]);
            if (w > maxWidth) maxWidth = w;
        }

        float textWidth = maxWidth;
        float textHeight = contentLines * lineHeight;

        float shadowPadding = 8;
        var shadowRect = new SKRect(
            marginLeft - shadowPadding,
            marginTop - shadowPadding,
            marginLeft + textWidth + shadowPadding,
            marginTop + textHeight + shadowPadding
        );

        using (var shadowPaint = new SKPaint())
        {
            shadowPaint.IsAntialias = true;
            shadowPaint.Style = SKPaintStyle.Fill;

            var colors = new SKColor[]
            {
                new SKColor(0, 0, 0, (byte)(160 * _helpOpacity)),
                new SKColor(0, 0, 0, (byte)(100 * _helpOpacity)),
                new SKColor(0, 0, 0, (byte)(40 * _helpOpacity)),
                new SKColor(0, 0, 0, 0)
            };
            var positions = new float[] { 0.0f, 0.7f, 0.9f, 1.0f };

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(shadowRect.MidX, shadowRect.MidY),
                Math.Max(shadowRect.Width, shadowRect.Height) / 2,
                colors,
                positions,
                SKShaderTileMode.Clamp);
            shadowPaint.Shader = shader;
            canvas.DrawRoundRect(shadowRect, 10, 10, shadowPaint);
        }

        using (var shadowTextPaint = new SKPaint())
        {
            shadowTextPaint.IsAntialias = true;
            shadowTextPaint.Color = new SKColor(0, 0, 0, (byte)(180 * _helpOpacity));
            shadowTextPaint.Style = SKPaintStyle.Fill;

            for (int i = 1; i < lines.Length; i++)
            {
                float lineY = startY + ((i - 1) * lineHeight);
                canvas.DrawText(lines[i], startX + 1, lineY + 1, SKTextAlign.Left, _helpTextFont, shadowTextPaint);
            }
        }

        byte opacityByte = (byte)(_helpOpacity * 255);
        using (var textPaint = new SKPaint())
        {
            textPaint.IsAntialias = true;
            textPaint.Color = new SKColor(255, 255, 255, opacityByte);
            textPaint.Style = SKPaintStyle.Fill;

            for (int i = 1; i < lines.Length; i++)
            {
                float lineY = startY + ((i - 1) * lineHeight);
                canvas.DrawText(lines[i], startX, lineY, SKTextAlign.Left, _helpTextFont, textPaint);
            }
        }
    }

    public void Dispose()
    {
        _helpBgPaint?.Dispose();
        _helpTextPaint?.Dispose();
        _helpTitlePaint?.Dispose();
        _modeBgPaint?.Dispose();
        _modeTextPaint?.Dispose();
        _helpTextFont?.Dispose();
        _helpTitleFont?.Dispose();
        _modeTextFont?.Dispose();
        _brushPreviewPaint?.Dispose();
        _brushPreviewHexPath?.Dispose();
        _paintsInitialized = false;
    }
}