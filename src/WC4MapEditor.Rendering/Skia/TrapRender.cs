using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 陷阱层：每个陷阱按等级（编制，1-5）画 1-5 个地雷图标，海洋格用 sea_trap.png、其它格用 land_trap.png，
/// 上方画所属军团（Trap.LegionId = 军团 ActionId）的国旗。
/// </summary>
public class TrapRender : IDisposable
{
    private const double TRAP_IMAGE_SIZE_RATIO = 0.6;
    private const double FLAG_SIZE_RATIO = 0.5;
    private const double BASE_RADIUS = 15.0;
    private const double BASE_HEX_SIZE = 20.0;

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showTraps = true;
    private bool _initialized;

    private SKImage? _landTrapImage;
    private SKImage? _seaTrapImage;
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

    /// <summary>
    /// 陆地 / 海洋陷阱各一批：DrawAtlas 一次只能用一张图，以前整批都用陆地图绘制（海洋图的切片矩形套在陆地图上），
    /// 海洋陷阱从来显示不出 sea_trap.png（VB 同样的 bug）。
    /// </summary>
    private readonly SpriteBatch _landSprites = new();
    private readonly SpriteBatch _seaSprites = new();
    private readonly List<(SKRect DstRect, SKImage Image)> _flagDrawList = new();
    // 复用的绘制画笔；原先陷阱图集每帧、国旗每面各 new 一个 SKPaint。
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private const int MAX_TRAP_POSITIONS = 5;

    public bool ShowTraps
    {
        get => _showTraps;
        set => _showTraps = value;
    }

    public TrapRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;
        InitializeTrapImages();
    }

    #region 纹理管理

    private void InitializeTrapImages()
    {
        if (_initialized) return;

        lock (_atlasLock)
        {
            try
            {
                _landTrapImage?.Dispose();
                _seaTrapImage?.Dispose();
                _landTrapImage = null;
                _seaTrapImage = null;
                _flagImageCache.Clear();

                string infoMarkPath = ConfigManager.Instance.GetInformationMarkPath();

                string landTrapPath = Path.Combine(infoMarkPath, "land_trap.png");
                if (File.Exists(landTrapPath))
                {
                    using var stream = File.OpenRead(landTrapPath);
                    _landTrapImage = SKImage.FromEncodedData(stream);
                }
                else
                {
                    _landTrapImage = CreateDefaultTrapImage(SKColors.Red);
                }

                string seaTrapPath = Path.Combine(infoMarkPath, "sea_trap.png");
                if (File.Exists(seaTrapPath))
                {
                    using var stream = File.OpenRead(seaTrapPath);
                    _seaTrapImage = SKImage.FromEncodedData(stream);
                }
                else
                {
                    _seaTrapImage = CreateDefaultTrapImage(SKColors.Blue);
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrapRender] 初始化陷阱纹理失败: {ex.Message}");
            }
        }
    }

    private static SKImage CreateDefaultTrapImage(SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(16, 16));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, 16, 16), paint);
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

    #endregion

    #region 陷阱位置计算

    /// <summary>
    /// 把 level 对应的陷阱图标位置写入 positions（至少 MAX_TRAP_POSITIONS 个槽位），返回实际数量。
    /// 原先每个陷阱每帧 new 一个 List&lt;SKPoint&gt;；数量上限是常数，改为调用方 stackalloc。
    /// </summary>
    private static int GetTrapPositions(int level, float centerX, float centerY, float zoom, Span<SKPoint> positions)
    {
        float radius = (float)(BASE_RADIUS * zoom);
        int normalizedLevel = Math.Max(1, Math.Min(5, level));

        switch (normalizedLevel)
        {
            case 1:
                positions[0] = new SKPoint(centerX, centerY);
                return 1;
            case 2:
                positions[0] = new SKPoint(centerX - radius, centerY);
                positions[1] = new SKPoint(centerX + radius, centerY);
                return 2;
            case 3:
                positions[0] = new SKPoint(centerX, centerY - radius);
                positions[1] = new SKPoint(centerX - radius * 0.866f, centerY + radius * 0.5f);
                positions[2] = new SKPoint(centerX + radius * 0.866f, centerY + radius * 0.5f);
                return 3;
            case 4:
                positions[0] = new SKPoint(centerX - radius * 0.707f, centerY - radius * 0.707f);
                positions[1] = new SKPoint(centerX + radius * 0.707f, centerY - radius * 0.707f);
                positions[2] = new SKPoint(centerX - radius * 0.707f, centerY + radius * 0.707f);
                positions[3] = new SKPoint(centerX + radius * 0.707f, centerY + radius * 0.707f);
                return 4;
            default:
                positions[0] = new SKPoint(centerX, centerY);
                positions[1] = new SKPoint(centerX - radius * 0.707f, centerY - radius * 0.707f);
                positions[2] = new SKPoint(centerX + radius * 0.707f, centerY - radius * 0.707f);
                positions[3] = new SKPoint(centerX - radius * 0.707f, centerY + radius * 0.707f);
                positions[4] = new SKPoint(centerX + radius * 0.707f, centerY + radius * 0.707f);
                return 5;
        }
    }

    #endregion

    #region 主渲染方法

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showTraps) return;
        if (_mapData.Traps == null || _mapData.Traps.Count == 0) return;
        if (!_initialized) return;

        try
        {
            _landSprites.Clear();
            _seaSprites.Clear();
            _flagDrawList.Clear();

            float zoom = (float)_camera.ZoomLevel;
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float trapSize = (float)(hexHeight * TRAP_IMAGE_SIZE_RATIO);
            float flagSize = (float)(hexHeight * FLAG_SIZE_RATIO);

            var visibleBounds = CalculateVisibleBounds();

            // 放在循环外分配一次：stackalloc 的栈空间直到方法返回才释放，放进循环会随陷阱数累积。
            Span<SKPoint> positions = stackalloc SKPoint[MAX_TRAP_POSITIONS];

            // 征服地图不存地形（全部是默认的海洋值），分不出海陆，这时一律用陆地图标
            bool mapHasLand = ArmyPlacementRules.HasLandTiles(_mapData);

            foreach (var trap in _mapData.Traps)
            {
                int cell = trap.CellIndex;
                if (cell >= _mapData.MapWidth * _mapData.MapHeight) continue;

                var screenPos = CalculateScreenPosition(cell);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                bool isSea = _seaTrapImage != null && ArmyPlacementRules.IsSea(_mapData, cell, mapHasLand);
                var batch = isSea ? _seaSprites : _landSprites;
                var trapImage = isSea ? _seaTrapImage : _landTrapImage;
                if (trapImage == null) continue;

                int positionCount = GetTrapPositions(trap.Organization, screenPos.Value.X, screenPos.Value.Y, zoom, positions);
                for (int p = 0; p < positionCount; p++)
                    batch.Add(trapImage, positions[p], trapSize);

                float flagOffsetY = -(float)hexHeight * 0.4f;
                float flagX = screenPos.Value.X;
                float flagY = screenPos.Value.Y + flagOffsetY;

                int countryId = GetCountryIdFromLegionId(trap.LegionId);
                var flagImage = GetFlagImage(countryId);

                if (flagImage != null)
                {
                    // 目标矩形为 flagSize 正方形，与原先「先缩放成正方形再按 1:1 贴图」的几何一致。
                    float flagTx = flagX - flagSize / 2;
                    float flagTy = flagY - flagSize / 2;
                    _flagDrawList.Add((new SKRect(flagTx, flagTy, flagTx + flagSize, flagTy + flagSize), flagImage));
                }
            }

            DrawTrapsBatch(canvas);
            DrawFlagsBatch(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrapRender] 绘制陷阱失败: {ex.Message}");
        }
    }

    private void DrawTrapsBatch(SKCanvas canvas)
    {
        lock (_atlasLock)
        {
            if (_landTrapImage != null) _landSprites.Draw(canvas, _landTrapImage, _imagePaint);
            if (_seaTrapImage != null) _seaSprites.Draw(canvas, _seaTrapImage, _imagePaint);
        }
    }

    /// <summary>同一张图的一批图标：图标按宽高中较大的一边缩放到 size（不再只按宽度缩放），中心对准目标点。</summary>
    private sealed class SpriteBatch
    {
        private readonly List<SKRect> _rects = new();
        private readonly List<SKRotationScaleMatrix> _transforms = new();

        public void Add(SKImage image, SKPoint center, float size)
        {
            float scale = Math.Min(size / image.Width, size / image.Height);
            _rects.Add(new SKRect(0, 0, image.Width, image.Height));
            _transforms.Add(new SKRotationScaleMatrix(scale, 0, center.X - image.Width * scale / 2, center.Y - image.Height * scale / 2));
        }

        public void Draw(SKCanvas canvas, SKImage image, SKPaint paint)
        {
            if (_transforms.Count > 0)
                canvas.DrawAtlas(image, _rects.ToArray(), _transforms.ToArray(), paint);
            Clear();
        }

        public void Clear()
        {
            _rects.Clear();
            _transforms.Clear();
        }
    }

    private void DrawFlagsBatch(SKCanvas canvas)
    {
        if (_flagDrawList.Count == 0) return;

        // 国旗源图块是 52x54 的非正方形，DrawAtlas 只能等比缩放，无法像原先那样拉成正方形；
        // 原先每面国旗单独一次 DrawAtlas 并分配两个数组，本就没有批量收益，这里直接按目标矩形 DrawImage，零分配。
        foreach (var flag in _flagDrawList)
            canvas.DrawImage(flag.Image, flag.DstRect, _imagePaint);

        _flagDrawList.Clear();
    }

    #endregion

    #region 辅助方法

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

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            _landTrapImage?.Dispose();
            _seaTrapImage?.Dispose();
            _landTrapImage = null;
            _seaTrapImage = null;

            ClearFlagCache();
        }

        if (_subscribedLegions != null)
        {
            _subscribedLegions.CollectionChanged -= OnLegionsCollectionChanged;
            _subscribedLegions = null;
        }

        _imagePaint.Dispose();
    }
}