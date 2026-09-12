using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Skia;

public class WeatherRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private const double PANEL_WIDTH_RATIO = 0.28;
    private const double PANEL_HEIGHT_RATIO = 0.06;
    private const float PANEL_MARGIN = 10f;
    private const float PANEL_PADDING = 8f;
    private const float ITEM_SPACING = 4f;

    private static readonly Dictionary<int, string> WeatherTypeNames = new()
    {
        { 1, "下雨" }, { 2, "暴雨" }, { 3, "下雪" }
    };

    private static readonly Dictionary<int, SKColor> WeatherTypeColors = new()
    {
        { 1, new SKColor(100, 149, 237) }, { 2, new SKColor(65, 105, 225) }, { 3, new SKColor(176, 224, 230) }
    };

    private static readonly Dictionary<int, string> WeatherTypeIcons = new()
    {
        { 1, "weather_rain" }, { 2, "weather_storm" }, { 3, "weather_snow" }
    };

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _lock = new();
    private bool _disposed;
    private bool _showWeather = true;
    private bool _initialized;

    private readonly Dictionary<int, SKImage> _weatherIconCache = new();

    private readonly SKPaint _panelBgPaint;
    private readonly SKPaint _panelBorderPaint;
    private readonly SKFont _titleFont;
    private readonly SKPaint _titlePaint;
    private readonly SKFont _itemFont;
    private readonly SKPaint _itemPaint;
    private readonly SKPaint _itemShadowPaint;
    private readonly SKPaint _roundPaint;
    private readonly SKPaint _durationPaint;

    public bool ShowWeather
    {
        get => _showWeather;
        set => _showWeather = value;
    }

    public WeatherRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _panelBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };
        _panelBorderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(100, 149, 237, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _titleFont = new SKFont(SKTypeface.FromFamilyName("Microsoft YaHei"), 14);
        _titlePaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _itemFont = new SKFont(SKTypeface.Default, 11);
        _itemPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _itemShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        _roundPaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _durationPaint = new SKPaint { IsAntialias = true, Color = SKColors.LightGreen };

        InitializeImages();
    }

    private void InitializeImages()
    {
        if (_initialized) return;

        lock (_lock)
        {
            try
            {
                string infoMarkPath = ConfigManager.Instance.GetInformationMarkPath();

                foreach (var kvp in WeatherTypeIcons)
                {
                    string iconPath = Path.Combine(infoMarkPath, $"{kvp.Value}.png");
                    if (File.Exists(iconPath))
                    {
                        using var stream = File.OpenRead(iconPath);
                        _weatherIconCache[kvp.Key] = SKImage.FromEncodedData(stream);
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WeatherRender] 初始化纹理失败: {ex.Message}");
            }
        }
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showWeather) return;
        if (_mapData.Weathers == null || _mapData.Weathers.Count == 0) return;
        if (!_initialized) return;

        try
        {
            var validWeathers = _mapData.Weathers
                .Where(w => w.WeatherType > 0)
                .OrderBy(w => w.TriggerRound)
                .ToList();

            if (validWeathers.Count == 0) return;

            DrawWeatherPanel(canvas, validWeathers);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeatherRender] 绘制天气失败: {ex.Message}");
        }
    }

    private void DrawWeatherPanel(SKCanvas canvas, List<Weather> weathers)
    {
        float viewportWidth = (float)_camera.ViewportWidth;
        float viewportHeight = (float)_camera.ViewportHeight;

        float panelWidth = (float)(viewportWidth * PANEL_WIDTH_RATIO);
        float lineHeight = 20f;
        float panelHeight = PANEL_PADDING * 2 + lineHeight + weathers.Count * (lineHeight + ITEM_SPACING);

        float panelX = viewportWidth - panelWidth - PANEL_MARGIN;
        float panelY = PANEL_MARGIN + 40f;

        var panelRect = new SKRect(panelX, panelY, panelX + panelWidth, panelY + panelHeight);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBgPaint);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBorderPaint);

        float titleY = panelY + PANEL_PADDING + lineHeight * 0.8f;
        canvas.DrawText("天气预报", panelX + PANEL_PADDING, titleY, SKTextAlign.Left, _titleFont, _titlePaint);

        float itemY = titleY + lineHeight * 0.4f;

        foreach (var weather in weathers)
        {
            itemY += lineHeight + ITEM_SPACING;

            string weatherName = GetWeatherTypeName(weather.WeatherType);
            var weatherColor = WeatherTypeColors.TryGetValue(weather.WeatherType, out var c) ? c : SKColors.White;

            if (_weatherIconCache.TryGetValue(weather.WeatherType, out var icon))
            {
                float iconSize = lineHeight * 0.8f;
                var iconRect = new SKRect(panelX + PANEL_PADDING, itemY - iconSize * 0.7f,
                                          panelX + PANEL_PADDING + iconSize, itemY + iconSize * 0.3f);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(icon, iconRect, paint);
            }

            float textX = panelX + PANEL_PADDING + lineHeight + 4f;

            _itemPaint.Color = weatherColor;
            canvas.DrawText(weatherName, textX + 1, itemY + 1, SKTextAlign.Left, _itemFont, _itemShadowPaint);
            canvas.DrawText(weatherName, textX, itemY, SKTextAlign.Left, _itemFont, _itemPaint);

            if (weather.TriggerRound > 0)
            {
                string roundText = $"R{weather.TriggerRound}";
                float roundX = textX + 60f;
                canvas.DrawText(roundText, roundX + 1, itemY + 1, SKTextAlign.Left, _itemFont, _itemShadowPaint);
                canvas.DrawText(roundText, roundX, itemY, SKTextAlign.Left, _itemFont, _roundPaint);
            }

            if (weather.Duration > 0)
            {
                string durationText = $"D{weather.Duration}";
                float durationX = textX + 110f;
                canvas.DrawText(durationText, durationX + 1, itemY + 1, SKTextAlign.Left, _itemFont, _itemShadowPaint);
                canvas.DrawText(durationText, durationX, itemY, SKTextAlign.Left, _itemFont, _durationPaint);
            }
        }
    }

    private static string GetWeatherTypeName(int weatherType) =>
        WeatherTypeNames.TryGetValue(weatherType, out var name) ? name : $"未知({weatherType})";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var image in _weatherIconCache.Values)
                image?.Dispose();
            _weatherIconCache.Clear();
        }

        _panelBgPaint.Dispose();
        _panelBorderPaint.Dispose();
        _titleFont.Dispose();
        _titlePaint.Dispose();
        _itemFont.Dispose();
        _itemPaint.Dispose();
        _itemShadowPaint.Dispose();
        _roundPaint.Dispose();
        _durationPaint.Dispose();
    }
}