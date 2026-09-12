using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Skia;

public class StrategicConstructionRender : IDisposable
{
    private const double ICON_SIZE_RATIO = 0.45;
    private const double FLAG_SIZE_RATIO = 0.35;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showConstructions = true;
    private bool _initialized;

    private SKImage? _constructionIcon;
    private readonly Dictionary<string, SKImage> _flagImageCache = new();

    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _constructionPositions = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _codeTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _roundTexts = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _flagPositions = new();

    private readonly SKFont _codeFont;
    private readonly SKFont _roundFont;
    private readonly SKPaint _codePaint;
    private readonly SKPaint _codeShadowPaint;
    private readonly SKPaint _roundPaint;
    private readonly SKPaint _roundShadowPaint;

    public bool ShowConstructions
    {
        get => _showConstructions;
        set => _showConstructions = value;
    }

    public StrategicConstructionRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _codeFont = new SKFont(SKTypeface.Default, 10);
        _roundFont = new SKFont(SKTypeface.Default, 10);
        _codePaint = new SKPaint { IsAntialias = true, Color = SKColors.Cyan };
        _codeShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        _roundPaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _roundShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        InitializeImages();
    }

    private void InitializeImages()
    {
        if (_initialized) return;

        lock (_atlasLock)
        {
            try
            {
                string infoMarkPath = ConfigManager.Instance.GetInformationMarkPath();

                string constructionPath = Path.Combine(infoMarkPath, "strategic_construction.png");
                if (File.Exists(constructionPath))
                {
                    using var stream = File.OpenRead(constructionPath);
                    _constructionIcon = SKImage.FromEncodedData(stream);
                }
                else
                {
                    _constructionIcon = CreateDefaultConstructionIcon();
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StrategicConstructionRender] 初始化纹理失败: {ex.Message}");
            }
        }
    }

    private static SKImage CreateDefaultConstructionIcon()
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { Color = new SKColor(0, 200, 200, 200), IsAntialias = true };
        using var borderPaint = new SKPaint { Color = new SKColor(0, 150, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        var rect = new SKRect(4, 4, 28, 28);
        canvas.DrawRect(rect, paint);
        canvas.DrawRect(rect, borderPaint);

        using var linePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, StrokeWidth = 2 };
        canvas.DrawLine(10, 16, 22, 16, linePaint);
        canvas.DrawLine(16, 10, 16, 22, linePaint);

        return surface.Snapshot();
    }

    private SKImage GetFlagImage(int countryId, int targetSize)
    {
        string cacheKey = $"{countryId}_{targetSize}";
        if (_flagImageCache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            SKImage? flagImage = null;

            var tacticalCache = TacticalMapImageCache.Instance;
            if (tacticalCache.IsInitialized)
            {
                var tacticalImage = tacticalCache.GetImage($"flag_{countryId}.png");
                if (tacticalImage != null)
                    flagImage = ResizeImage(tacticalImage, targetSize, targetSize);
            }

            if (flagImage == null)
            {
                string basePath = Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
                string flagPath = Path.Combine(basePath, $"flag_{countryId}.png");
                if (!File.Exists(flagPath) && (countryId == 0 || countryId == 255))
                    flagPath = Path.Combine(basePath, "flag_1.png");

                if (File.Exists(flagPath))
                {
                    using var stream = File.OpenRead(flagPath);
                    using var original = SKImage.FromEncodedData(stream);
                    if (original != null)
                        flagImage = ResizeImage(original, targetSize, targetSize);
                }
            }

            flagImage ??= CreateDefaultFlagImage(targetSize);

            _flagImageCache[cacheKey] = flagImage;
            return flagImage;
        }
        catch
        {
            return CreateDefaultFlagImage(targetSize);
        }
    }

    private static SKImage ResizeImage(SKImage source, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        var srcRect = new SKRect(0, 0, source.Width, source.Height);
        var dstRect = new SKRect(0, 0, width, height);
        canvas.DrawImage(source, srcRect, dstRect, paint);
        return surface.Snapshot();
    }

    private static SKImage CreateDefaultFlagImage(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(128, 128, 128), IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, size, size), paint);
        return surface.Snapshot();
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showConstructions) return;
        if (_mapData.StrategyConstructions == null || _mapData.StrategyConstructions.Count == 0) return;
        if (!_initialized) return;

        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float iconSize = (float)(hexHeight * ICON_SIZE_RATIO);
            float flagSize = (float)(hexHeight * FLAG_SIZE_RATIO);

            foreach (var construction in _mapData.StrategyConstructions)
            {
                if (construction.ConstructionCode <= 0) continue;

                var screenPos = CalculateScreenPosition(construction.LegionId);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                _constructionPositions.Add((screenPos.Value, iconSize, _constructionIcon));

                if (construction.ConstructionCode > 0)
                {
                    float codeFontSize = iconSize * 0.4f;
                    _codeTexts.Add((new SKPoint(screenPos.Value.X, screenPos.Value.Y + iconSize * 0.3f),
                        construction.ConstructionCode.ToString(), codeFontSize));
                }

                if (construction.Reserved2 > 0)
                {
                    float roundFontSize = iconSize * 0.35f;
                    _roundTexts.Add((new SKPoint(screenPos.Value.X - iconSize * 0.5f, screenPos.Value.Y),
                        $"R{construction.Reserved2}", roundFontSize));
                }

                int countryId = GetCountryIdFromLegionId(construction.LegionId);
                var flagImage = GetFlagImage(countryId, (int)flagSize);
                float flagOffsetY = -(float)hexHeight * 0.4f;
                _flagPositions.Add((new SKPoint(screenPos.Value.X, screenPos.Value.Y + flagOffsetY), flagSize, flagImage));
            }

            DrawConstructions(canvas);
            DrawFlags(canvas);
            DrawCodeTexts(canvas);
            DrawRoundTexts(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StrategicConstructionRender] 绘制战略建设失败: {ex.Message}");
        }
    }

    private void ClearBuffers()
    {
        _constructionPositions.Clear();
        _codeTexts.Clear();
        _roundTexts.Clear();
        _flagPositions.Clear();
    }

    private void DrawConstructions(SKCanvas canvas)
    {
        if (_constructionPositions.Count == 0 || _constructionIcon == null) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var item in _constructionPositions)
        {
            float halfSize = item.Size / 2;
            var dstRect = new SKRect(item.Position.X - halfSize, item.Position.Y - halfSize,
                                     item.Position.X + halfSize, item.Position.Y + halfSize);
            canvas.DrawImage(item.Image ?? _constructionIcon, dstRect, paint);
        }
    }

    private void DrawFlags(SKCanvas canvas)
    {
        if (_flagPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var flag in _flagPositions)
        {
            if (flag.Image == null) continue;
            float halfSize = flag.Size / 2;
            var dstRect = new SKRect(flag.Position.X - halfSize, flag.Position.Y - halfSize,
                                     flag.Position.X + halfSize, flag.Position.Y + halfSize);
            canvas.DrawImage(flag.Image, dstRect, paint);
        }
    }

    private void DrawCodeTexts(SKCanvas canvas)
    {
        if (_codeTexts.Count == 0) return;

        foreach (var textInfo in _codeTexts)
        {
            float fontSize = Math.Max(8, textInfo.Size);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1, SKTextAlign.Center, font, _codeShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y, SKTextAlign.Center, font, _codePaint);
        }
    }

    private void DrawRoundTexts(SKCanvas canvas)
    {
        if (_roundTexts.Count == 0) return;

        foreach (var textInfo in _roundTexts)
        {
            float fontSize = Math.Max(8, textInfo.Size);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1, SKTextAlign.Center, font, _roundShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y, SKTextAlign.Center, font, _roundPaint);
        }
    }

    private SKRect CalculateVisibleBounds()
    {
        float viewWidth = (float)_camera.ViewportWidth;
        float viewHeight = (float)_camera.ViewportHeight;
        float margin = 200f * (float)_camera.ZoomLevel;
        return new SKRect(-margin, -margin, viewWidth + margin, viewHeight + margin);
    }

    private static bool IsInVisibleBounds(SKPoint point, SKRect bounds)
    {
        return bounds.Contains(point.X, point.Y);
    }

    private SKPoint? CalculateScreenPosition(int coordinate)
    {
        if (_mapData == null || _mapData.MapWidth <= 0 || _mapData.MapHeight <= 0) return null;

        int row = coordinate / _mapData.MapWidth;
        int col = coordinate % _mapData.MapWidth;

        if (row < 0 || row >= _mapData.MapHeight || col < 0 || col >= _mapData.MapWidth) return null;

        var (screenX, screenY) = _camera.HexToScreen(col, row);
        return new SKPoint((float)screenX, (float)screenY);
    }

    private int GetCountryIdFromLegionId(int legionId)
    {
        if (_mapData?.Legions != null)
        {
            foreach (var legion in _mapData.Legions)
            {
                if (legion.ActionId == legionId)
                    return legion.CountryId;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            _constructionIcon?.Dispose();
            _constructionIcon = null;

            foreach (var image in _flagImageCache.Values)
                image?.Dispose();
            _flagImageCache.Clear();
        }

        _codeFont.Dispose();
        _roundFont.Dispose();
        _codePaint.Dispose();
        _codeShadowPaint.Dispose();
        _roundPaint.Dispose();
        _roundShadowPaint.Dispose();
    }
}