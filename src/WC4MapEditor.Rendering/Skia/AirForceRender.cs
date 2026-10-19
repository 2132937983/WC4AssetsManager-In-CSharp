using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 空袭标记层（空袭编辑模式，C# 新增；VB 的空袭模式只显示领域与建筑）。
/// <para>
/// 每个有空袭的格子（机场）画一枚标记：圆角牌子里是飞机图标与空袭条数，牌子下方是触发回合（范围），右边是所属军团的国旗。
/// 同一机场常挂几十条空袭（游戏地图里 10–30 条很常见），逐条画会完全叠在一起，所以按格子合并成一枚；
/// 只有一条时在回合后面写出兵种名称。逐条的详情在空袭列表窗口（Q）里看。
/// </para>
/// <para>
/// 标记放在格子中心偏右下，避开建筑图标顶部的设施图标和下方的城市名称。
/// 图标是矢量绘制的，不依赖贴图文件（原先查找的 airforce_XX.png 并不存在，读取失败后整层就再也不画了）。
/// </para>
/// </summary>
public class AirForceRender : IDisposable
{
    /// <summary>一个格子最多画几面军团国旗，多出来的写成 "+N"。</summary>
    private const int MaxFlags = 3;
    private const int DEFAULT_FLAG_SIZE = 32;

    /// <summary>弹药 → 标记角上的小圆点颜色（默认弹药不画点）。</summary>
    private static readonly Dictionary<int, SKColor> AmmoColors = new()
    {
        { 0x1D, new SKColor(255, 165, 0) },
        { 0x1E, new SKColor(255, 69, 0) },
        { 0x1F, new SKColor(255, 0, 0) },
        { 0x20, new SKColor(170, 60, 255) }
    };

    /// <summary>标签字体：能显示中文的字体（SKTypeface.Default 没有中文字形，兵种名会画成方框）。</summary>
    private static readonly SKTypeface LabelTypeface =
        SKFontManager.Default.MatchCharacter('中') ?? SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private bool _disposed;

    /// <summary>
    /// 国家 Id → 国旗（源尺寸），绘制时按目标矩形缩放。Owned 标记该图是否归本类所有
    /// （来自 TacticalMapImageCache 的图块不归本类，不能 Dispose）。
    /// 失效点：TacticalMapImageCache 就绪状态翻转时整体清空（见 GetFlagImage），以及 Dispose。
    /// </summary>
    private readonly Dictionary<int, (SKImage Image, bool Owned)> _flagImageCache = new();
    private bool _flagCacheTacticalReady;

    /// <summary>
    /// 军团 ActionId → CountryId 的惰性映射。
    /// 失效点：订阅的 Legions.CollectionChanged（增删 / 写回）触发时置空；
    /// 地图重载会整体替换 Legions 实例，GetLegionCountryMap 发现实例变化时换订阅并重建。
    /// </summary>
    private Dictionary<int, int>? _legionCountryMap;
    private ObservableCollection<Legion>? _subscribedLegions;

    /// <summary>按格子合并后的标记（每帧重建，对象复用）。</summary>
    private readonly Dictionary<int, Marker> _markers = new();
    private readonly Stack<Marker> _markerPool = new();

    private readonly SKPath _planePath = CreatePlanePath();
    private readonly SKFont _countFont;
    private readonly SKFont _labelFont;
    private readonly SKPaint _pillPaint = new() { IsAntialias = true, Color = new SKColor(18, 22, 30, 215) };
    private readonly SKPaint _pillBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(140, 200, 255, 230) };
    private readonly SKPaint _planePaint = new() { IsAntialias = true, Color = new SKColor(140, 200, 255) };
    private readonly SKPaint _countPaint = new() { IsAntialias = true, Color = SKColors.White };
    private readonly SKPaint _roundPaint = new() { IsAntialias = true, Color = new SKColor(255, 225, 120) };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, Color = new SKColor(0, 0, 0, 200) };
    private readonly SKPaint _ammoPaint = new() { IsAntialias = true };
    private readonly SKPaint _flagBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0, 0, 0, 160) };
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };

    private bool _showAirForces = true;

    public bool ShowAirForces
    {
        get => _showAirForces;
        set => _showAirForces = value;
    }

    public AirForceRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;
        _countFont = new SKFont(LabelTypeface, 12) { Embolden = true };
        _labelFont = new SKFont(LabelTypeface, 11);
    }

    /// <summary>一个格子上全部空袭的汇总。</summary>
    private sealed class Marker
    {
        public int Coordinate;
        public int Count;
        public int MinRound;
        public int MaxRound;
        public int FirstType;
        public int SpecialAmmo;
        public readonly List<int> Legions = new();

        public void Reset(in AirForce first)
        {
            Coordinate = first.Coordinate;
            Count = 0;
            MinRound = int.MaxValue;
            MaxRound = int.MinValue;
            FirstType = first.UnitType;
            SpecialAmmo = 0;
            Legions.Clear();
        }

        public void Add(in AirForce airForce)
        {
            Count++;
            MinRound = Math.Min(MinRound, airForce.TriggerRound);
            MaxRound = Math.Max(MaxRound, airForce.TriggerRound);
            if (SpecialAmmo == 0 && airForce.AmmoType != 0) SpecialAmmo = airForce.AmmoType;
            if (!Legions.Contains(airForce.OwnerLegion)) Legions.Add(airForce.OwnerLegion);
        }
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null || !_showAirForces) return;
        if (_mapData.AirForces == null || _mapData.AirForces.Count == 0 || _mapData.MapWidth <= 0) return;

        try
        {
            CollectMarkers();
            if (_markers.Count == 0) return;

            float hexRadius = (float)(Camera.BaseHexSize * _camera.ZoomLevel);
            float pillHeight = Math.Clamp(hexRadius * 0.75f, 13f, 30f);
            float margin = pillHeight * 6;
            float right = (float)_camera.ViewportWidth + margin;
            float bottom = (float)_camera.ViewportHeight + margin;
            var typeNames = AirForceCatalog.Types;

            foreach (var marker in _markers.Values)
            {
                int col = marker.Coordinate % _mapData.MapWidth;
                int row = marker.Coordinate / _mapData.MapWidth;
                var (sx, sy) = _camera.HexToScreen(col, row);
                if (sx < -margin || sy < -margin || sx > right || sy > bottom) continue;

                DrawMarker(canvas, marker, (float)sx, (float)sy, hexRadius, pillHeight, typeNames);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AirForceRender] 绘制空袭失败: {ex.Message}");
        }
    }

    /// <summary>把空袭按格子合并；坐标不在地图内的跳过。</summary>
    private void CollectMarkers()
    {
        foreach (var m in _markers.Values) _markerPool.Push(m);
        _markers.Clear();

        int cellCount = _mapData.MapWidth * _mapData.MapHeight;
        foreach (var airForce in _mapData.AirForces)
        {
            if (airForce.Coordinate < 0 || airForce.Coordinate >= cellCount) continue;
            if (!_markers.TryGetValue(airForce.Coordinate, out var marker))
            {
                marker = _markerPool.Count > 0 ? _markerPool.Pop() : new Marker();
                marker.Reset(airForce);
                _markers[airForce.Coordinate] = marker;
            }
            marker.Add(airForce);
        }
    }

    private void DrawMarker(SKCanvas canvas, Marker marker, float cx, float cy, float hexRadius, float h,
        IReadOnlyList<ConfigKeyValue> typeNames)
    {
        // 牌子：左边缘在格子中心右侧一点，竖直方向在中心偏下
        _countFont.Size = h * 0.78f;
        string countText = marker.Count.ToString();
        float pad = h * 0.18f;
        float iconSize = h * 0.86f;
        float textWidth = _countFont.MeasureText(countText);
        float left = cx + hexRadius * 0.2f;
        float top = cy + hexRadius * 0.15f;
        var pill = new SKRect(left, top, left + pad + iconSize + pad * 0.6f + textWidth + pad * 1.4f, top + h);

        float radius = h * 0.45f;
        canvas.DrawRoundRect(pill, radius, radius, _pillPaint);
        _pillBorderPaint.StrokeWidth = Math.Max(1f, h * 0.07f);
        canvas.DrawRoundRect(pill, radius, radius, _pillBorderPaint);

        // 飞机图标（单位路径缩放到 iconSize，机头朝右上）
        float iconCx = left + pad + iconSize / 2;
        float iconCy = top + h / 2;
        canvas.Save();
        canvas.Translate(iconCx, iconCy);
        canvas.RotateDegrees(45);
        canvas.Scale(iconSize);
        canvas.Translate(-0.5f, -0.5f);
        canvas.DrawPath(_planePath, _planePaint);
        canvas.Restore();

        if (marker.SpecialAmmo != 0)
        {
            _ammoPaint.Color = AmmoColors.TryGetValue(marker.SpecialAmmo, out var c) ? c : SKColors.Yellow;
            canvas.DrawCircle(left + pad + iconSize * 0.95f, top + h * 0.22f, h * 0.17f, _ammoPaint);
        }

        float baseline = top + h / 2 + _countFont.Size * 0.36f;
        canvas.DrawText(countText, left + pad + iconSize + pad * 0.6f, baseline, SKTextAlign.Left, _countFont, _countPaint);

        // 回合（范围）；只有一条空袭时补上兵种名称
        _labelFont.Size = Math.Max(10f, h * 0.68f);
        string roundText = marker.MinRound == marker.MaxRound ? $"R{marker.MinRound}" : $"R{marker.MinRound}-{marker.MaxRound}";
        if (marker.Count == 1) roundText += " " + AirForceCatalog.NameIn(typeNames, marker.FirstType);
        float labelY = pill.Bottom + _labelFont.Size * 1.05f;
        DrawShadowedText(canvas, roundText, pill.Left + radius * 0.4f, labelY, _labelFont, _roundPaint);

        DrawFlags(canvas, marker, pill.Right + h * 0.25f, top, h);
    }

    private void DrawFlags(SKCanvas canvas, Marker marker, float x, float top, float h)
    {
        float flagHeight = h * 0.8f;
        float y = top + (h - flagHeight) / 2;
        int shown = Math.Min(MaxFlags, marker.Legions.Count);
        for (int i = 0; i < shown; i++)
        {
            var image = GetFlagImage(GetCountryIdFromLegionId(marker.Legions[i]));
            float width = image.Height > 0 ? flagHeight * image.Width / image.Height : flagHeight;
            var rect = new SKRect(x, y, x + width, y + flagHeight);
            canvas.DrawImage(image, rect, _imagePaint);
            canvas.DrawRect(rect, _flagBorderPaint);
            x += width + h * 0.15f;
        }

        if (marker.Legions.Count > shown)
        {
            _labelFont.Size = Math.Max(10f, h * 0.68f);
            DrawShadowedText(canvas, $"+{marker.Legions.Count - shown}", x, top + h / 2 + _labelFont.Size * 0.36f, _labelFont, _countPaint);
        }
    }

    private void DrawShadowedText(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        float offset = Math.Max(1f, font.Size * 0.08f);
        canvas.DrawText(text, x + offset, y + offset, SKTextAlign.Left, font, _shadowPaint);
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }

    /// <summary>飞机剪影（单位正方形内，机头朝上）：机身、主翼、尾翼。</summary>
    private static SKPath CreatePlanePath()
    {
        var path = new SKPath();
        path.MoveTo(0.50f, 0.00f);
        path.LineTo(0.57f, 0.10f);
        path.LineTo(0.57f, 0.36f);
        path.LineTo(0.98f, 0.58f);
        path.LineTo(0.98f, 0.67f);
        path.LineTo(0.57f, 0.56f);
        path.LineTo(0.57f, 0.78f);
        path.LineTo(0.74f, 0.90f);
        path.LineTo(0.74f, 0.97f);
        path.LineTo(0.50f, 0.91f);
        path.LineTo(0.26f, 0.97f);
        path.LineTo(0.26f, 0.90f);
        path.LineTo(0.43f, 0.78f);
        path.LineTo(0.43f, 0.56f);
        path.LineTo(0.02f, 0.67f);
        path.LineTo(0.02f, 0.58f);
        path.LineTo(0.43f, 0.36f);
        path.LineTo(0.43f, 0.10f);
        path.Close();
        return path;
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

        ClearFlagCache();

        if (_subscribedLegions != null)
        {
            _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = null;
        }

        _planePath.Dispose();
        _countFont.Dispose();
        _labelFont.Dispose();
        _pillPaint.Dispose();
        _pillBorderPaint.Dispose();
        _planePaint.Dispose();
        _countPaint.Dispose();
        _roundPaint.Dispose();
        _shadowPaint.Dispose();
        _ammoPaint.Dispose();
        _flagBorderPaint.Dispose();
        _imagePaint.Dispose();
    }
}
