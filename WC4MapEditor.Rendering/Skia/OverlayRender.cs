using SkiaSharp;

namespace WC4MapEditor.Rendering.Skia;

public sealed class OverlayRender
{
    private string _helpText = string.Empty;
    private string _modeName = string.Empty;
    private bool _showHelp = true;
    private bool _showModeName = true;

    private SKPaint? _helpBgPaint;
    private SKPaint? _helpTextPaint;
    private SKPaint? _helpTitlePaint;
    private SKPaint? _modeBgPaint;
    private SKPaint? _modeTextPaint;
    private SKFont? _helpTextFont;
    private SKFont? _helpTitleFont;
    private SKFont? _modeTextFont;
    private bool _paintsInitialized;

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
        set => _showHelp = value;
    }

    public bool ShowModeName
    {
        get => _showModeName;
        set => _showModeName = value;
    }

    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

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

        _helpTextPaint = new SKPaint { Color = new SKColor(200, 220, 255), IsAntialias = true };
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
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null) return;
        EnsurePaints();

        if (_showModeName && !string.IsNullOrEmpty(_modeName))
            RenderModeName(canvas);

        if (_showHelp && !string.IsNullOrEmpty(_helpText))
            RenderHelpText(canvas);
    }

    private void RenderModeName(SKCanvas canvas)
    {
        float padding = 8;
        float x = ViewportWidth - 10;
        float y = 10;

        float textWidth = _modeTextFont!.MeasureText(_modeName);
        float textHeight = _modeTextFont.Metrics.Descent - _modeTextFont.Metrics.Ascent;

        float bgLeft = x - textWidth - padding * 2;
        float bgTop = y;
        float bgRight = x;
        float bgBottom = y + textHeight + padding * 2;

        var bgRect = new SKRect(bgLeft, bgTop, bgRight, bgBottom);
        canvas.DrawRoundRect(bgRect, 4, 4, _modeBgPaint!);
        canvas.DrawText(_modeName, bgLeft + padding, bgBottom - padding, SKTextAlign.Left, _modeTextFont, _modeTextPaint!);
    }

    private void RenderHelpText(SKCanvas canvas)
    {
        float padding = 10;
        float lineHeight = 18;
        float x = 10;
        float y = 50;

        var lines = _helpText.Split('\n');
        if (lines.Length == 0) return;

        float maxWidth = 0;
        foreach (var line in lines)
        {
            float w = _helpTextFont!.MeasureText(line);
            if (w > maxWidth) maxWidth = w;
        }

        float bgWidth = maxWidth + padding * 2;
        float bgHeight = lines.Length * lineHeight + padding * 2;

        if (x + bgWidth > ViewportWidth)
            bgWidth = ViewportWidth - x - 5;
        if (y + bgHeight > ViewportHeight)
            bgHeight = ViewportHeight - y - 5;

        var bgRect = new SKRect(x, y, x + bgWidth, y + bgHeight);
        canvas.DrawRoundRect(bgRect, 6, 6, _helpBgPaint!);

        float textY = y + padding + lineHeight * 0.8f;
        for (int i = 0; i < lines.Length; i++)
        {
            if (textY > y + bgHeight - padding) break;

            var font = (i == 0) ? _helpTitleFont! : _helpTextFont!;
            var paint = (i == 0) ? _helpTitlePaint! : _helpTextPaint!;
            canvas.DrawText(lines[i], x + padding, textY, SKTextAlign.Left, font, paint);
            textY += lineHeight;
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
        _paintsInitialized = false;
    }
}