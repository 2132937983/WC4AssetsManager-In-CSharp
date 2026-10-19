using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 战略建设面板（战略建设编辑模式）：屏幕右上角按军团分组列出战略建设 ——
/// 军团行（军团颜色色块 + 国旗 + 军团序号与国家名），其下每条一行（执行回合、建设代码与 def_strategy.xml 里的策略名）。
/// 放不下时最后一行显示"还有 N 条"。
/// <para>
/// 战略建设记录里没有坐标（军团序号 / 保留 / 执行回合 / 建设代码），所以画成屏幕空间的面板。
/// 早先的版本把军团序号当格子下标去算屏幕位置，图标落在地图第一行毫无意义的格子上。
/// VB 版战略建设模式只显示国家领域，面板是 C# 新增的。
/// </para>
/// </summary>
public class StrategicConstructionRender : IDisposable
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
    private const float FlagWidth = 18f;
    private const float FlagHeight = 12f;
    private const float Gap = 5f;
    private const float EntryIndent = 14f;
    private const float ColumnGap = 10f;
    private const float MinPanelWidth = 170f;
    private const int DefaultFlagSize = 32;

    private static readonly SKTypeface UiTypeface = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
    private static readonly SKSamplingOptions FlagSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly SKColor RoundColor = new(255, 190, 90);
    private static readonly SKColor CodeColor = new(150, 230, 230);
    private static readonly SKColor WarnColor = new(255, 110, 110);
    private static readonly SKColor MutedColor = new(170, 170, 170);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private bool _disposed;

    /// <summary>
    /// 国家 Id → 国旗（源尺寸，绘制时按目标矩形缩放）。
    /// Owned 标记该图是否归本类所有（来自 TacticalMapImageCache 的图块不归本类，不能 Dispose）。
    /// 失效点：TacticalMapImageCache 就绪状态翻转时整体清空（见 GetFlagImage），以及 Dispose。
    /// </summary>
    private readonly Dictionary<int, (SKImage Image, bool Owned)> _flagImageCache = new();
    private bool _flagCacheTacticalReady;

    /// <summary>
    /// 军团 ActionId → 列表位置 / 国家 Id / 颜色 的惰性映射。
    /// 失效点：订阅的 Legions.CollectionChanged（增删 / ReplaceLegion 写回）触发时置空；
    /// 地图重载会整体替换 Legions 实例，GetLegionInfo 发现实例变化时换订阅并重建。
    /// </summary>
    private Dictionary<int, LegionInfo>? _legionInfo;
    private ObservableCollection<Legion>? _subscribedLegions;

    private readonly SKPaint _panelBgPaint = new() { IsAntialias = true, Color = new SKColor(0, 0, 0, 170) };
    private readonly SKPaint _panelBorderPaint = new() { IsAntialias = true, Color = new SKColor(0, 200, 200, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _textPaint = new() { IsAntialias = true, Color = SKColors.White };
    private readonly SKPaint _swatchPaint = new() { IsAntialias = true };
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private readonly SKFont _titleFont = new(UiTypeface, 14);
    private readonly SKFont _itemFont = new(UiTypeface, 12);

    private readonly record struct LegionInfo(int Order, int CountryId, SKColor Color);

    /// <summary>面板一行：军团行（IsHeader）或战略建设行（Left = 执行回合，Right = 建设代码）。</summary>
    private readonly record struct Row(bool IsHeader, string Left, string Right, SKColor LeftColor, SKColor RightColor, SKColor Swatch, int FlagCountryId);

    public bool ShowConstructions { get; set; } = true;

    public StrategicConstructionRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;
    }

    public void Render(SKCanvas canvas)
    {
        if (_disposed || !ShowConstructions || canvas == null) return;

        float viewportWidth = (float)_camera.ViewportWidth;
        float viewportHeight = (float)_camera.ViewportHeight;
        if (viewportWidth < MinPanelWidth + PanelMargin * 2 || viewportHeight < PanelTop + BottomReserve + 60f) return;

        var rows = BuildRows();
        int capacity = Math.Max(1, (int)((viewportHeight - PanelTop - BottomReserve - Padding * 2 - TitleHeight) / RowHeight));
        int shown = rows.Count <= capacity ? rows.Count : capacity - 1;
        int hiddenEntries = 0;
        for (int i = shown; i < rows.Count; i++)
            if (!rows[i].IsHeader) hiddenEntries++;
        string? footer = rows.Count == 0 ? Loc.T("Render.Strategic.Empty", "无战略建设数据")
                       : hiddenEntries > 0 ? Loc.F("Render.Strategic.More", hiddenEntries)
                       : null;

        string title = Loc.T("Render.Strategic.Title", "战略建设");
        float roundColumn = 0;
        for (int i = 0; i < shown; i++)
            if (!rows[i].IsHeader) roundColumn = Math.Max(roundColumn, _itemFont.MeasureText(rows[i].Left));
        float contentWidth = _titleFont.MeasureText(title);
        for (int i = 0; i < shown; i++)
        {
            var row = rows[i];
            float width = row.IsHeader
                ? SwatchSize + Gap + FlagWidth + Gap + _itemFont.MeasureText(row.Left)
                : EntryIndent + roundColumn + ColumnGap + _itemFont.MeasureText(row.Right);
            contentWidth = Math.Max(contentWidth, width);
        }
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
        float baseline = y + Padding + TitleHeight + RowHeight - 5f;
        for (int i = 0; i < shown; i++, baseline += RowHeight)
        {
            var row = rows[i];
            if (row.IsHeader) DrawHeader(canvas, row, left, baseline);
            else DrawEntry(canvas, row, left + EntryIndent, left + EntryIndent + roundColumn + ColumnGap, baseline);
        }

        if (footer != null)
        {
            _textPaint.Color = MutedColor;
            canvas.DrawText(footer, left, baseline, SKTextAlign.Left, _itemFont, _textPaint);
        }
    }

    private void DrawHeader(SKCanvas canvas, Row row, float left, float baseline)
    {
        _swatchPaint.Color = row.Swatch;
        canvas.DrawRoundRect(new SKRect(left, baseline - SwatchSize, left + SwatchSize, baseline), 2, 2, _swatchPaint);

        float textX = left + SwatchSize + Gap;
        if (row.FlagCountryId >= 0)
        {
            float top = baseline - FlagHeight + 1f;
            canvas.DrawImage(GetFlagImage(row.FlagCountryId), new SKRect(textX, top, textX + FlagWidth, top + FlagHeight), FlagSampling, _imagePaint);
            textX += FlagWidth + Gap;
        }

        _textPaint.Color = row.LeftColor;
        canvas.DrawText(row.Left, textX, baseline, SKTextAlign.Left, _itemFont, _textPaint);
    }

    private void DrawEntry(SKCanvas canvas, Row row, float roundX, float codeX, float baseline)
    {
        _textPaint.Color = row.LeftColor;
        canvas.DrawText(row.Left, roundX, baseline, SKTextAlign.Left, _itemFont, _textPaint);
        _textPaint.Color = row.RightColor;
        canvas.DrawText(row.Right, codeX, baseline, SKTextAlign.Left, _itemFont, _textPaint);
    }

    /// <summary>
    /// 面板行：按军团在军团列表里的顺序分组（军团列表里没有的序号排在最后并标红），
    /// 组内按执行回合、再按列表顺序排列。
    /// </summary>
    private List<Row> BuildRows()
    {
        var constructions = _mapData.StrategyConstructions;
        var legions = GetLegionInfo();
        var order = Enumerable.Range(0, constructions.Count)
            .OrderBy(i => legions.TryGetValue(constructions[i].LegionId, out var info) ? info.Order : int.MaxValue)
            .ThenBy(i => constructions[i].LegionId)
            .ThenBy(i => constructions[i].ExecuteRound)
            .ThenBy(i => i);

        var rows = new List<Row>(constructions.Count + legions.Count);
        int? currentLegion = null;
        foreach (int i in order)
        {
            var sc = constructions[i];
            if (sc.LegionId != currentLegion)
            {
                currentLegion = sc.LegionId;
                rows.Add(legions.TryGetValue(sc.LegionId, out var info)
                    ? new Row(true, Loc.F("Render.Strategic.Legion", sc.LegionId, CountryNameProvider.Get(info.CountryId)), "",
                        SKColors.White, SKColors.White, info.Color, info.CountryId)
                    : new Row(true, Loc.F("Render.Strategic.LegionMissing", sc.LegionId), "",
                        WarnColor, WarnColor, MutedColor, -1));
            }

            var definition = StrategyCatalog.Find(sc.ConstructionCode);
            string code = definition != null
                ? Loc.F("Render.Strategic.Code", sc.ConstructionCode, definition.Name)
                : Loc.F("Render.Strategic.CodeUnknown", sc.ConstructionCode);
            rows.Add(new Row(false, Loc.F("Render.Strategic.Round", sc.ExecuteRound), code,
                RoundColor, definition != null ? CodeColor : WarnColor, default, -1));
        }
        return rows;
    }

    private SKImage GetFlagImage(int countryId)
    {
        var tacticalCache = TacticalMapImageCache.Instance;
        bool tacticalReady = tacticalCache.IsInitialized;
        // 图块缓存在本缓存建立之后才就绪（或之后被清空）：旧条目要么是占位/文件图、要么已失效，整体丢弃重建。
        if (tacticalReady != _flagCacheTacticalReady)
        {
            ClearFlagCache();
            _flagCacheTacticalReady = tacticalReady;
        }

        if (_flagImageCache.TryGetValue(countryId, out var cached)) return cached.Image;

        SKImage? flagImage = null;
        bool owned = false;

        try
        {
            if (tacticalReady)
                flagImage = tacticalCache.GetImage($"flag_{countryId}.png");

            if (flagImage == null)
            {
                string basePath = Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
                string flagPath = Path.Combine(basePath, $"flag_{countryId}.png");
                if (!File.Exists(flagPath) && (countryId == 0 || countryId == 255))
                    flagPath = Path.Combine(basePath, "flag_1.png");

                if (File.Exists(flagPath))
                {
                    using var stream = File.OpenRead(flagPath);
                    flagImage = SKImage.FromEncodedData(stream);
                    owned = flagImage != null;
                }
            }
        }
        catch
        {
            // 图块缓存 / 文件解码失败都退回占位图，不让一面国旗拖垮整帧绘制
            if (owned) flagImage?.Dispose();
            flagImage = null;
            owned = false;
        }

        if (flagImage == null)
        {
            // 占位图是纯色方块，任意尺寸缩放后外观一致。
            flagImage = CreateDefaultFlagImage(DefaultFlagSize);
            owned = true;
        }

        _flagImageCache[countryId] = (flagImage, owned);
        return flagImage;
    }

    private void ClearFlagCache()
    {
        foreach (var entry in _flagImageCache.Values)
            if (entry.Owned) entry.Image.Dispose();
        _flagImageCache.Clear();
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

    private Dictionary<int, LegionInfo> GetLegionInfo()
    {
        var legions = _mapData.Legions;
        if (!ReferenceEquals(legions, _subscribedLegions))
        {
            // Legions 实例被整体替换（地图重载）：旧订阅不再有效，换到新实例并丢弃映射。
            if (_subscribedLegions != null)
                _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = legions;
            legions.CollectionChanged += OnLegionsCollectionChanged;
            _legionInfo = null;
        }

        if (_legionInfo != null) return _legionInfo;

        var map = new Dictionary<int, LegionInfo>(legions.Count);
        for (int i = 0; i < legions.Count; i++)
        {
            var legion = legions[i];
            // 重复 ActionId 保留首个（与按 ActionId 线性查找的结果一致）
            map.TryAdd(legion.ActionId, new LegionInfo(i, legion.CountryId, new SKColor(legion.ColorR, legion.ColorG, legion.ColorB)));
        }
        _legionInfo = map;
        return map;
    }

    private void OnLegionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _legionInfo = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearFlagCache();

        if (_subscribedLegions != null)
        {
            _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = null;
        }

        _panelBgPaint.Dispose();
        _panelBorderPaint.Dispose();
        _textPaint.Dispose();
        _swatchPaint.Dispose();
        _imagePaint.Dispose();
        _titleFont.Dispose();
        _itemFont.Dispose();
    }
}
