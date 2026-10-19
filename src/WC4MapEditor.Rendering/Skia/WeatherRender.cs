using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 天气面板（天气编辑模式）：屏幕右上角按触发回合列出全部天气 —— 类型色块、类型名（setting.txt 的类型表）、生效回合。
/// 生效回合与其它天气重叠的条目标红；放不下时最后一行显示"还有 N 条"。
/// <para>
/// 天气与格子无关，所以画成屏幕空间的面板（VB 版天气模式只显示国家领域，面板是 C# 新增的）。
/// 早先的版本条目字体用 SKTypeface.Default（中文类型名画成方框）、名称写死、面板高度随条目数无限增长。
/// </para>
/// </summary>
public class WeatherRender : IDisposable
{
    private const float PanelMargin = 10f;
    /// <summary>面板顶边：右上角模式名（基线 y≈30）的下方。</summary>
    private const float PanelTop = 50f;
    /// <summary>面板底边留白：场景底部的工具栏（高 40）压在画布上，最后一行不能画到它下面。</summary>
    private const float BottomReserve = 50f;
    private const float Padding = 8f;
    private const float TitleHeight = 22f;
    private const float RowHeight = 18f;
    private const float SwatchSize = 9f;
    private const float SwatchGap = 6f;
    private const float ColumnGap = 16f;
    private const float MinPanelWidth = 150f;

    private static readonly SKTypeface UiTypeface = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
    private static readonly SKColor OverlapColor = new(255, 110, 110);
    private static readonly SKColor UnknownTypeColor = new(255, 170, 60);
    private static readonly SKColor RangeColor = new(190, 215, 255);
    private static readonly SKColor MutedColor = new(170, 170, 170);

    /// <summary>
    /// 类型色块：0 无（灰）/ 1 下雨（天蓝）/ 2 暴雨（紫，与下雨在小色块上也要分得开）/ 3 下雪（白）；
    /// 不在这里的类型用橙色（游戏未必认识）。
    /// </summary>
    private static readonly Dictionary<int, SKColor> TypeColors = new()
    {
        { 0, new SKColor(150, 150, 150) },
        { 1, new SKColor(100, 170, 245) },
        { 2, new SKColor(160, 110, 245) },
        { 3, new SKColor(225, 240, 250) },
    };

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private bool _disposed;

    private readonly SKPaint _panelBgPaint = new() { IsAntialias = true, Color = new SKColor(0, 0, 0, 170) };
    private readonly SKPaint _panelBorderPaint = new() { IsAntialias = true, Color = new SKColor(100, 149, 237, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _textPaint = new() { IsAntialias = true, Color = SKColors.White };
    private readonly SKPaint _swatchPaint = new() { IsAntialias = true };
    private readonly SKFont _titleFont = new(UiTypeface, 14);
    private readonly SKFont _itemFont = new(UiTypeface, 12);

    /// <summary>一行：类型名、生效回合、色块、是否与其它天气重叠。</summary>
    private readonly record struct Row(string Name, string Range, SKColor Swatch, bool Known, bool Overlap);

    public bool ShowWeather { get; set; } = true;

    public WeatherRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;
    }

    public void Render(SKCanvas canvas)
    {
        if (_disposed || !ShowWeather || canvas == null) return;

        float viewportWidth = (float)_camera.ViewportWidth;
        float viewportHeight = (float)_camera.ViewportHeight;
        if (viewportWidth < MinPanelWidth + PanelMargin * 2 || viewportHeight < PanelTop + BottomReserve + 60f) return;

        var rows = BuildRows();
        int capacity = Math.Max(1, (int)((viewportHeight - PanelTop - BottomReserve - Padding * 2 - TitleHeight) / RowHeight));
        int shown = rows.Count <= capacity ? rows.Count : capacity - 1;
        string? footer = rows.Count == 0 ? Loc.T("Render.Weather.Empty", "无天气数据")
                       : shown < rows.Count ? Loc.F("Render.Weather.More", rows.Count - shown)
                       : null;

        string title = Loc.T("Render.Weather.Title", "天气预报");
        float nameWidth = 0, rangeWidth = 0;
        for (int i = 0; i < shown; i++)
        {
            nameWidth = Math.Max(nameWidth, _itemFont.MeasureText(rows[i].Name));
            rangeWidth = Math.Max(rangeWidth, _itemFont.MeasureText(rows[i].Range));
        }
        float contentWidth = Math.Max(_titleFont.MeasureText(title), SwatchSize + SwatchGap + nameWidth + ColumnGap + rangeWidth);
        if (footer != null) contentWidth = Math.Max(contentWidth, _itemFont.MeasureText(footer));

        float panelWidth = Math.Clamp(contentWidth + Padding * 2, MinPanelWidth, Math.Max(MinPanelWidth, viewportWidth * 0.45f));
        int lines = shown + (footer != null ? 1 : 0);
        float panelHeight = Padding * 2 + TitleHeight + lines * RowHeight;
        float x = viewportWidth - panelWidth - PanelMargin;
        float y = PanelTop;

        var panelRect = new SKRect(x, y, x + panelWidth, y + panelHeight);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBgPaint);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBorderPaint);

        _textPaint.Color = SKColors.White;
        canvas.DrawText(title, x + Padding, y + Padding + 15f, SKTextAlign.Left, _titleFont, _textPaint);

        float left = x + Padding;
        float right = x + panelWidth - Padding;
        float baseline = y + Padding + TitleHeight + RowHeight - 5f;
        for (int i = 0; i < shown; i++, baseline += RowHeight)
        {
            var row = rows[i];
            _swatchPaint.Color = row.Swatch;
            canvas.DrawRoundRect(new SKRect(left, baseline - SwatchSize, left + SwatchSize, baseline), 2, 2, _swatchPaint);

            _textPaint.Color = row.Overlap ? OverlapColor : row.Known ? SKColors.White : UnknownTypeColor;
            canvas.DrawText(row.Name, left + SwatchSize + SwatchGap, baseline, SKTextAlign.Left, _itemFont, _textPaint);

            _textPaint.Color = row.Overlap ? OverlapColor : RangeColor;
            canvas.DrawText(row.Range, right, baseline, SKTextAlign.Right, _itemFont, _textPaint);
        }

        if (footer != null)
        {
            _textPaint.Color = MutedColor;
            canvas.DrawText(footer, left, baseline, SKTextAlign.Left, _itemFont, _textPaint);
        }
    }

    /// <summary>按触发回合排序（同回合保持列表顺序）的面板行。</summary>
    private List<Row> BuildRows()
    {
        var weathers = _mapData.Weathers;
        bool[] overlaps = Weather.FindOverlaps(weathers);
        var rows = new List<Row>(weathers.Count);
        foreach (int i in Enumerable.Range(0, weathers.Count).OrderBy(i => weathers[i].TriggerRound).ThenBy(i => i))
        {
            var w = weathers[i];
            bool known = Weather.IsKnownType(w.WeatherType);
            var swatch = TypeColors.TryGetValue(w.WeatherType, out var c) ? c : UnknownTypeColor;
            rows.Add(new Row(Weather.GetTypeName(w.WeatherType), w.FormatRounds(), swatch, known, overlaps[i]));
        }
        return rows;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _panelBgPaint.Dispose();
        _panelBorderPaint.Dispose();
        _textPaint.Dispose();
        _swatchPaint.Dispose();
        _titleFont.Dispose();
        _itemFont.Dispose();
    }
}
