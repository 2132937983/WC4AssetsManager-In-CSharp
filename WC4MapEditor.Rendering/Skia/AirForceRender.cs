using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Skia;

public class AirForceRender : IDisposable
{
    private const double ICON_SIZE_RATIO = 0.55;
    private const double FLAG_SIZE_RATIO = 0.35;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private static readonly Dictionary<int, string> UnitTypeNames = new()
    {
        { 0x14, "战斗机" }, { 0x15, "轰炸机" }, { 0x16, "空降伞兵" },
        { 0x17, "战略轰炸机" }, { 0x18, "特色轰炸机" }, { 0x19, "近程导弹" },
        { 0x1A, "中程导弹" }, { 0x1B, "远程导弹" }, { 0x1E, "洲际导弹" }
    };

    private static readonly Dictionary<int, SKColor> AmmoTypeColors = new()
    {
        { 0x0, SKColors.White }, { 0x1D, new SKColor(255, 165, 0) },
        { 0x1E, new SKColor(255, 69, 0) }, { 0x1F, new SKColor(255, 0, 0) },
        { 0x20, new SKColor(148, 0, 211) }
    };

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showAirForces = true;
    private bool _initialized;

    private readonly Dictionary<int, SKImage> _unitTypeIconCache = new();
    private readonly Dictionary<string, SKImage> _flagImageCache = new();

    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _iconPositions = new();
    private readonly List<(SKPoint Position, string Text, float Size, SKColor Color)> _typeTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _roundTexts = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _flagPositions = new();
    private readonly List<(SKPoint Position, float Size, SKColor Color)> _ammoIndicators = new();

    private readonly SKFont _typeFont;
    private readonly SKFont _roundFont;
    private readonly SKPaint _typePaint;
    private readonly SKPaint _typeShadowPaint;
    private readonly SKPaint _roundPaint;
    private readonly SKPaint _roundShadowPaint;

    public bool ShowAirForces
    {
        get => _showAirForces;
        set => _showAirForces = value;
    }

    public AirForceRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _typeFont = new SKFont(SKTypeface.Default, 10);
        _roundFont = new SKFont(SKTypeface.Default, 10);
        _typePaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _typeShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        _roundPaint = new SKPaint { IsAntialias = true, Color = SKColors.Magenta };
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

                foreach (var kvp in UnitTypeNames)
                {
                    string iconPath = Path.Combine(infoMarkPath, $"airforce_{kvp.Key:X2}.png");
                    if (File.Exists(iconPath))
                    {
                        using var stream = File.OpenRead(iconPath);
                        _unitTypeIconCache[kvp.Key] = SKImage.FromEncodedData(stream);
                    }
                }

                string defaultIconPath = Path.Combine(infoMarkPath, "airforce_default.png");
                if (File.Exists(defaultIconPath))
                {
                    using var stream = File.OpenRead(defaultIconPath);
                    _unitTypeIconCache[0] = SKImage.FromEncodedData(stream);
                }
                else
                {
                    _unitTypeIconCache[0] = CreateDefaultAirForceIcon();
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AirForceRender] 初始化纹理失败: {ex.Message}");
            }
        }
    }

    private static SKImage CreateDefaultAirForceIcon()
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { Color = new SKColor(200, 100, 255, 200), IsAntialias = true };
        using var borderPaint = new SKPaint { Color = new SKColor(150, 50, 200), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        var path = new SKPath();
        path.MoveTo(16, 2);
        path.LineTo(28, 16);
        path.LineTo(22, 16);
        path.LineTo(22, 28);
        path.LineTo(10, 28);
        path.LineTo(10, 16);
        path.LineTo(4, 16);
        path.Close();

        canvas.DrawPath(path, paint);
        canvas.DrawPath(path, borderPaint);

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
        if (!_showAirForces) return;
        if (_mapData.AirForces == null || _mapData.AirForces.Count == 0) return;
        if (!_initialized) return;

        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float iconSize = (float)(hexHeight * ICON_SIZE_RATIO);
            float flagSize = (float)(hexHeight * FLAG_SIZE_RATIO);

            foreach (var airForce in _mapData.AirForces)
            {
                if (airForce.UnitType <= 0) continue;

                var screenPos = CalculateScreenPosition(airForce.Coordinate);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                var iconImage = GetUnitTypeIcon(airForce.UnitType);
                _iconPositions.Add((screenPos.Value, iconSize, iconImage));

                string typeName = GetUnitTypeName(airForce.UnitType);
                float typeFontSize = iconSize * 0.35f;
                _typeTexts.Add((new SKPoint(screenPos.Value.X, screenPos.Value.Y + iconSize * 0.6f),
                    typeName, typeFontSize, SKColors.White));

                if (airForce.TriggerRound > 0)
                {
                    float roundFontSize = iconSize * 0.35f;
                    _roundTexts.Add((new SKPoint(screenPos.Value.X - iconSize * 0.6f, screenPos.Value.Y),
                        $"R{airForce.TriggerRound}", roundFontSize));
                }

                if (airForce.AmmoType > 0)
                {
                    var ammoColor = AmmoTypeColors.TryGetValue(airForce.AmmoType, out var c) ? c : SKColors.Yellow;
                    float indicatorSize = iconSize * 0.25f;
                    _ammoIndicators.Add((new SKPoint(screenPos.Value.X + iconSize * 0.4f, screenPos.Value.Y - iconSize * 0.4f),
                        indicatorSize, ammoColor));
                }

                int countryId = GetCountryIdFromLegionId(airForce.OwnerLegion);
                var flagImage = GetFlagImage(countryId, (int)flagSize);
                float flagOffsetY = -(float)hexHeight * 0.4f;
                _flagPositions.Add((new SKPoint(screenPos.Value.X, screenPos.Value.Y + flagOffsetY), flagSize, flagImage));
            }

            DrawIcons(canvas);
            DrawFlags(canvas);
            DrawAmmoIndicators(canvas);
            DrawTypeTexts(canvas);
            DrawRoundTexts(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AirForceRender] 绘制空袭失败: {ex.Message}");
        }
    }

    private SKImage? GetUnitTypeIcon(int unitType)
    {
        if (_unitTypeIconCache.TryGetValue(unitType, out var icon))
            return icon;
        if (_unitTypeIconCache.TryGetValue(0, out var defaultIcon))
            return defaultIcon;
        return null;
    }

    private static string GetUnitTypeName(int unitType) =>
        UnitTypeNames.TryGetValue(unitType, out var name) ? name : $"未知({unitType:X2})";

    private void ClearBuffers()
    {
        _iconPositions.Clear();
        _typeTexts.Clear();
        _roundTexts.Clear();
        _flagPositions.Clear();
        _ammoIndicators.Clear();
    }

    private void DrawIcons(SKCanvas canvas)
    {
        if (_iconPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var item in _iconPositions)
        {
            if (item.Image == null) continue;
            float halfSize = item.Size / 2;
            var dstRect = new SKRect(item.Position.X - halfSize, item.Position.Y - halfSize,
                                     item.Position.X + halfSize, item.Position.Y + halfSize);
            canvas.DrawImage(item.Image, dstRect, paint);
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

    private void DrawAmmoIndicators(SKCanvas canvas)
    {
        if (_ammoIndicators.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var indicator in _ammoIndicators)
        {
            paint.Color = indicator.Color;
            canvas.DrawCircle(indicator.Position.X, indicator.Position.Y, indicator.Size, paint);
        }
    }

    private void DrawTypeTexts(SKCanvas canvas)
    {
        if (_typeTexts.Count == 0) return;

        foreach (var textInfo in _typeTexts)
        {
            float fontSize = Math.Max(8, textInfo.Size);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1, SKTextAlign.Center, font, _typeShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y, SKTextAlign.Center, font, _typePaint);
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
            foreach (var image in _unitTypeIconCache.Values)
                image?.Dispose();
            _unitTypeIconCache.Clear();

            foreach (var image in _flagImageCache.Values)
                image?.Dispose();
            _flagImageCache.Clear();
        }

        _typeFont.Dispose();
        _roundFont.Dispose();
        _typePaint.Dispose();
        _typeShadowPaint.Dispose();
        _roundPaint.Dispose();
        _roundShadowPaint.Dispose();
    }
}