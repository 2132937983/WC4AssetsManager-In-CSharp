using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

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
    /// <summary>
    /// 国家 Id → 国旗（源尺寸）。原先按 "{countryId}_{targetSize}" 缓存，每个缩放级别都重新缩放一张新图，
    /// 既不淘汰也随缩放膨胀；现在每国只存一张源图，绘制时按目标矩形缩放。
    /// Owned 标记该图是否归本类所有（来自 TacticalMapImageCache 的图块不归本类，不能 Dispose）。
    /// 失效点：TacticalMapImageCache 就绪状态翻转时整体清空（见 GetFlagImage），以及 Dispose。
    /// </summary>
    private readonly Dictionary<int, (SKImage Image, bool Owned)> _flagImageCache = new();
    private bool _flagCacheTacticalReady;
    private const int DEFAULT_FLAG_SIZE = 32;

    /// <summary>
    /// 军团 ActionId → CountryId 的惰性映射。原先每个实体每帧线性扫描 Legions。
    /// 失效点：订阅的 Legions.CollectionChanged（增删 / ReplaceLegion 写回）触发时置空；
    /// 地图重载会整体替换 Legions 实例，GetLegionCountryMap 发现实例变化时换订阅并重建。
    /// </summary>
    private Dictionary<int, int>? _legionCountryMap;
    private ObservableCollection<Legion>? _subscribedLegions;

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
            if (owned) flagImage?.Dispose();
            flagImage = null;
            owned = false;
        }

        if (flagImage == null)
        {
            // 占位图是纯色方块，任意尺寸缩放后外观一致。
            flagImage = CreateDefaultFlagImage(DEFAULT_FLAG_SIZE);
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
                var flagImage = GetFlagImage(countryId);
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
            // 复用字段字体、只改字号；原先每条文本每帧 new 一个 SKFont。
            _codeFont.Size = Math.Max(8, textInfo.Size);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1, SKTextAlign.Center, _codeFont, _codeShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y, SKTextAlign.Center, _codeFont, _codePaint);
        }
    }

    private void DrawRoundTexts(SKCanvas canvas)
    {
        if (_roundTexts.Count == 0) return;

        foreach (var textInfo in _roundTexts)
        {
            _roundFont.Size = Math.Max(8, textInfo.Size);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1, SKTextAlign.Center, _roundFont, _roundShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y, SKTextAlign.Center, _roundFont, _roundPaint);
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
        var map = GetLegionCountryMap();
        return map != null && map.TryGetValue(legionId, out int countryId) ? countryId : 0;
    }

    private Dictionary<int, int>? GetLegionCountryMap()
    {
        var legions = _mapData?.Legions;
        if (!ReferenceEquals(legions, _subscribedLegions))
        {
            // Legions 实例被整体替换（地图重载）：旧订阅不再有效，换到新实例并丢弃映射。
            if (_subscribedLegions != null)
                _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = legions;
            if (legions != null)
                legions.CollectionChanged += OnLegionsCollectionChanged;
            _legionCountryMap = null;
        }

        if (legions == null) return null;

        var map = _legionCountryMap;
        if (map != null) return map;

        map = new Dictionary<int, int>(legions.Count);
        foreach (var legion in legions)
            map.TryAdd(legion.ActionId, legion.CountryId); // 重复 ActionId 保留首个，与原线性查找一致
        _legionCountryMap = map;
        return map;
    }

    private void OnLegionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _legionCountryMap = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            _constructionIcon?.Dispose();
            _constructionIcon = null;

            ClearFlagCache();
        }

        if (_subscribedLegions != null)
        {
            _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = null;
        }

        _codeFont.Dispose();
        _roundFont.Dispose();
        _codePaint.Dispose();
        _codeShadowPaint.Dispose();
        _roundPaint.Dispose();
        _roundShadowPaint.Dispose();
    }
}