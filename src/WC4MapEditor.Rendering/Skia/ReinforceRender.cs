using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 援军渲染层，旧样式（对齐 VB ReinforceRender）：援军竖排在格子左侧，最多 6 个，单位编辑模式使用。
/// <para>
/// 同时是 <see cref="ReinforceRenderNew"/>（援军模式的格子中心 2×6 网格）的基类：两种样式共用分组缓存、
/// 叠加信息（等级、爆兵回合、将领头像）与绘制，只是图标排布不同（<see cref="MaxPerHex"/> / <see cref="IconCenter"/> /
/// <see cref="OverflowTextPosition"/>）。
/// </para>
/// <para>
/// 版本由地图决定：BtlVersion ≥ 3 读 ReinforcementsV3，否则读 Reinforcements。
/// 兵种没有图标（ArmyMark 里没有 legion_icon_{兵种}.png）时画一个带兵种编号的圆形占位，叠加信息照常画出，援军不会"隐身"。
/// </para>
/// </summary>
public class ReinforceRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 64;
    private const float GENERAL_HEAD_SIZE_RATIO = 0.35f;

    /// <summary>图标边长 = 六边形高 × 0.5（VB 两种样式相同）</summary>
    private const double ICON_SIZE_RATIO = 0.5;

    private const double BASE_HEX_SIZE = 20.0;

    /// <summary>VB MainRender.HexWidth（= BASE_HEX_SIZE × 2），旧样式按它计算左侧一列的位置</summary>
    private const double VB_HEX_WIDTH = BASE_HEX_SIZE * 2;

    private static readonly double HEX_HEIGHT = BASE_HEX_SIZE * Math.Sqrt(3);

    /// <summary>一个援军在渲染层上需要的信息（v1 / v3 记录统一成它，按格子分组缓存）。</summary>
    private readonly record struct Glyph(int UnitType, int Level, int SpawnRound, int General);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _atlasInitialized;

    /// <summary>共享单位图集，由 <see cref="UnitAtlasCache"/> 持有所有权，本类不得 Dispose。</summary>
    private SKImage? _unitAtlas;
    /// <summary>共享切片表，指向 <see cref="UnitAtlasCache"/> 中的实例，只读使用。</summary>
    private Dictionary<int, SKRect> _unitAtlasMap = new();

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<(SKPoint Position, float Size, int UnitType)> _placeholders = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _groupTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _spawnRoundTexts = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _generalHeads = new();
    private readonly List<(SKPoint Position, float Size, int Level)> _levelIcons = new();

    /// <summary>
    /// 按格子坐标分组的援军缓存（内容变化时重建，避免每帧分组）。
    /// 监听的是当前那份集合实例：撤销地图缩放等操作会整体换掉集合，渲染前发现实例变了就改订阅并丢弃缓存。
    /// </summary>
    private Dictionary<int, List<Glyph>>? _groups;
    private bool _groupsAreV3;
    private ObservableCollection<Reinforcement>? _watchedV1;
    private ObservableCollection<Reinforcement_3>? _watchedV3;

    private readonly Dictionary<int, SKImage> _levelIconCache = new();

    /// <summary>
    /// 将领 Id → 头像（null 表示该将领没有头像）。只在 TacticalMapImageCache 就绪时写入；
    /// 图块缓存状态翻转或 <see cref="ClearGeneralHeadCache"/> 时清空。头像归 TacticalMapImageCache 所有，本类不得 Dispose。
    /// </summary>
    private readonly Dictionary<int, SKImage?> _generalHeadCache = new();

    private readonly SKPaint _spritePaint = new() { IsAntialias = true };
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private readonly SKPaint _spawnRoundPaint = new() { IsAntialias = true, Color = SKColors.LimeGreen };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, Color = SKColors.Black };
    private readonly SKPaint _groupTextPaint = new() { IsAntialias = true, Color = SKColors.Yellow };
    private readonly SKPaint _placeholderFill = new() { IsAntialias = true, Color = new SKColor(40, 48, 64, 220) };
    private readonly SKPaint _placeholderStroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(230, 230, 230) };
    private readonly SKPaint _placeholderText = new() { IsAntialias = true, Color = SKColors.White };
    private readonly SKPaint _headFill = new() { IsAntialias = true, Color = new SKColor(255, 215, 0) };
    private readonly SKPaint _headStroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(218, 165, 32) };
    // 复用的文本字体，绘制时只改 Size
    private readonly SKFont _font = new(SKTypeface.Default, 10);

    public bool ShowReinforcements { get; set; } = true;

    /// <summary>当前地图是否为 v3（BtlVersion ≥ 3）：决定读哪份援军列表。</summary>
    public bool IsV3 => _mapData.Header.UsesV3Records;

    public ReinforceRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;
        InitializeUnitAtlas();
        InitializeLevelIcons();
        WatchCollections();
    }

    #region 布局（子类改写）

    /// <summary>每格最多画几个图标，超出时在下方显示 "X总数"。</summary>
    protected virtual int MaxPerHex => 6;

    /// <summary>
    /// 第 <paramref name="index"/> 个图标的中心。旧样式：格子中心左移 0.8 个六边形半宽（VB HexWidth = 40），
    /// 从中心上方 0.3 个六边形高开始往下排，每个下移 0.4 个图标（相邻图标重叠 60%）。
    /// </summary>
    protected virtual SKPoint IconCenter(SKPoint hexCenter, int index, float iconSize, float hexHeight)
        => new(ColumnX(hexCenter), hexCenter.Y - hexHeight * 0.3f + index * iconSize * 0.4f);

    /// <summary>超出 <see cref="MaxPerHex"/> 时 "X总数" 文本的位置（旧样式：紧接在这一列下方）。</summary>
    protected virtual SKPoint OverflowTextPosition(SKPoint hexCenter, int visibleCount, float iconSize, float hexHeight)
        => new(ColumnX(hexCenter), hexCenter.Y - hexHeight * 0.3f + visibleCount * iconSize * 0.4f + iconSize * 0.5f);

    private float ColumnX(SKPoint hexCenter) => hexCenter.X - (float)(VB_HEX_WIDTH * _camera.ZoomLevel / 2) * 0.8f;

    #endregion

    #region 数据

    private void WatchCollections()
    {
        if (!ReferenceEquals(_watchedV1, _mapData.Reinforcements))
        {
            if (_watchedV1 != null) _watchedV1.CollectionChanged -= OnReinforcementsChanged;
            _watchedV1 = _mapData.Reinforcements;
            _watchedV1.CollectionChanged += OnReinforcementsChanged;
            _groups = null;
        }
        if (!ReferenceEquals(_watchedV3, _mapData.ReinforcementsV3))
        {
            if (_watchedV3 != null) _watchedV3.CollectionChanged -= OnReinforcementsChanged;
            _watchedV3 = _mapData.ReinforcementsV3;
            _watchedV3.CollectionChanged += OnReinforcementsChanged;
            _groups = null;
        }
    }

    private void OnReinforcementsChanged(object? sender, NotifyCollectionChangedEventArgs e) => _groups = null;

    /// <summary>当前版本援军按坐标分组（组内保持列表顺序），惰性构建并缓存。</summary>
    private Dictionary<int, List<Glyph>> GetGroups()
    {
        bool v3 = IsV3;
        if (_groups != null && _groupsAreV3 == v3) return _groups;

        var map = new Dictionary<int, List<Glyph>>();
        if (v3) Collect(_mapData.ReinforcementsV3, map);
        else Collect(_mapData.Reinforcements, map);

        _groups = map;
        _groupsAreV3 = v3;
        return map;
    }

    private static void Collect<T>(IEnumerable<T> source, Dictionary<int, List<Glyph>> map) where T : struct, IReinforcementData
    {
        foreach (var r in source)
        {
            if (r.Coordinate < 0) continue;
            if (!map.TryGetValue(r.Coordinate, out var list))
            {
                list = new List<Glyph>(4);
                map[r.Coordinate] = list;
            }
            // 等级只取低字节：v3 地图里等级字段的高字节常是 0xCC 填充（0xCC000003 = 3 级）
            list.Add(new Glyph(r.UnitType, r.Level & 0xFF, r.SpawnRound, r.General));
        }
    }

    private void InitializeUnitAtlas()
    {
        if (_atlasInitialized) return;

        lock (_atlasLock)
        {
            if (_atlasInitialized) return;

            // 三个单位类渲染器共享 UnitAtlasCache 中的同一份图集
            if (UnitAtlasCache.TryGet(out var atlas, out var tileMap))
            {
                _unitAtlas = atlas;
                _unitAtlasMap = tileMap;
                _atlasInitialized = true;
            }
        }
    }

    private void InitializeLevelIcons()
    {
        try
        {
            string infoMarkPath = ConfigManager.Instance.GetInformationMarkPath();
            for (int level = 1; level <= 5; level++)
            {
                string levelPath = Path.Combine(infoMarkPath, $"level_{level}.png");
                if (!File.Exists(levelPath)) continue;
                using var stream = File.OpenRead(levelPath);
                var image = SKImage.FromEncodedData(stream);
                if (image != null) _levelIconCache[level] = image;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReinforceRender] 初始化等级图标失败: {ex.Message}");
        }
    }

    #endregion

    #region 渲染

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _disposed || !ShowReinforcements) return;
        if (_mapData.MapWidth <= 0 || _mapData.MapHeight <= 0) return;

        WatchCollections();
        var groups = GetGroups();
        if (groups.Count == 0) return;
        InitializeUnitAtlas();

        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            float hexHeight = (float)(HEX_HEIGHT * _camera.ZoomLevel);
            float iconSize = (float)(hexHeight * ICON_SIZE_RATIO);
            int maxPerHex = MaxPerHex;

            foreach (var (coordinate, group) in groups)
            {
                var center = CalculateScreenPosition(coordinate);
                if (center == null || !visibleBounds.Contains(center.Value)) continue;

                int visibleCount = Math.Min(group.Count, maxPerHex);
                for (int i = 0; i < visibleCount; i++)
                    CollectGlyph(group[i], IconCenter(center.Value, i, iconSize, hexHeight), iconSize);

                if (group.Count > maxPerHex)
                    _groupTexts.Add((OverflowTextPosition(center.Value, visibleCount, iconSize, hexHeight), $"X{group.Count}", iconSize));
            }

            DrawSprites(canvas);
            DrawSpawnRoundTexts(canvas);
            DrawLevelIcons(canvas);
            DrawGeneralHeads(canvas);
            DrawGroupTexts(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReinforceRender] 绘制援军失败: {ex.Message}");
        }
    }

    private void ClearBuffers()
    {
        _spriteRects.Clear();
        _transforms.Clear();
        _placeholders.Clear();
        _groupTexts.Clear();
        _spawnRoundTexts.Clear();
        _generalHeads.Clear();
        _levelIcons.Clear();
    }

    /// <summary>收集一个援军的图标（或占位）与叠加信息。</summary>
    private void CollectGlyph(Glyph glyph, SKPoint icon, float iconSize)
    {
        if (_unitAtlasMap.TryGetValue(glyph.UnitType, out var sprite))
        {
            float half = iconSize / 2;
            _spriteRects.Add(sprite);
            _transforms.Add(new SKRotationScaleMatrix(iconSize / ATLAS_TILE_SIZE, 0, icon.X - half, icon.Y - half));
        }
        else
        {
            _placeholders.Add((icon, iconSize, glyph.UnitType));
        }

        // 等级图标：右下角，边长 0.3 个图标，超过 5 级用 5 级图标
        if (glyph.Level > 0)
        {
            float size = iconSize * 0.3f;
            _levelIcons.Add((new SKPoint(icon.X + iconSize / 2 - size / 2, icon.Y + iconSize / 2 - size / 2), size, Math.Min(glyph.Level, 5)));
        }

        // 爆兵回合：绿色带阴影，在图标左侧（VB：字号 0.35 个图标，x − 0.55 个图标）
        if (glyph.SpawnRound > 0)
        {
            float fontSize = iconSize * 0.35f;
            _spawnRoundTexts.Add((new SKPoint(icon.X - iconSize * 0.55f, icon.Y + fontSize * 0.3f), glyph.SpawnRound.ToString(), fontSize));
        }

        // 将领头像：图标上方露出约 1/3；没有头像时画金色圆点，表示"有将领"
        if (glyph.General > 0)
            _generalHeads.Add((new SKPoint(icon.X, icon.Y - iconSize * 0.3f), iconSize * GENERAL_HEAD_SIZE_RATIO, ResolveGeneralHead(glyph.General)));
    }

    /// <summary>按将领 Id 取头像，结果（含"无头像"的 null）缓存在 <see cref="_generalHeadCache"/>。</summary>
    private SKImage? ResolveGeneralHead(int general)
    {
        var tacticalCache = TacticalMapImageCache.Instance;
        if (!tacticalCache.IsInitialized)
        {
            // 图块缓存未就绪时不写缓存，否则会把"暂时没有"永久记成"没有"；
            // 若之前就绪过而现在被清空，缓存里的图像已失效，一并丢弃。
            if (_generalHeadCache.Count > 0) _generalHeadCache.Clear();
            return null;
        }

        if (_generalHeadCache.TryGetValue(general, out var cached))
            return cached;

        SKImage? headImage = null;
        string? ename = GeneralSettingParser.Instance.GetById(general)?.EName;
        if (!string.IsNullOrEmpty(ename))
            headImage = tacticalCache.GetImage($"head_{ename}.png");

        _generalHeadCache[general] = headImage;
        return headImage;
    }

    /// <summary>将领表（GeneralSettings）重载后调用，丢弃已缓存的 将领 Id → 头像 映射。</summary>
    public void ClearGeneralHeadCache() => _generalHeadCache.Clear();

    private void DrawSprites(SKCanvas canvas)
    {
        if (_transforms.Count > 0)
        {
            lock (_atlasLock)
            {
                if (_unitAtlas != null)
                    canvas.DrawAtlas(_unitAtlas, _spriteRects.ToArray(), _transforms.ToArray(), _spritePaint);
            }
        }

        foreach (var (position, size, unitType) in _placeholders)
        {
            float radius = size * 0.42f;
            canvas.DrawCircle(position, radius, _placeholderFill);
            _placeholderStroke.StrokeWidth = Math.Max(1f, size * 0.05f);
            canvas.DrawCircle(position, radius, _placeholderStroke);
            _font.Size = Math.Max(7f, size * 0.4f);
            canvas.DrawText(unitType.ToString(), position.X, position.Y + _font.Size * 0.35f, SKTextAlign.Center, _font, _placeholderText);
        }
    }

    private void DrawSpawnRoundTexts(SKCanvas canvas)
    {
        foreach (var (position, text, size) in _spawnRoundTexts)
        {
            _font.Size = Math.Max(8, size);
            canvas.DrawText(text, position.X + 1, position.Y + 1, SKTextAlign.Center, _font, _shadowPaint);
            canvas.DrawText(text, position.X, position.Y, SKTextAlign.Center, _font, _spawnRoundPaint);
        }
    }

    private void DrawLevelIcons(SKCanvas canvas)
    {
        foreach (var (position, size, level) in _levelIcons)
        {
            if (!_levelIconCache.TryGetValue(level, out var image)) continue;
            float half = size / 2;
            canvas.DrawImage(image, new SKRect(position.X - half, position.Y - half, position.X + half, position.Y + half), _imagePaint);
        }
    }

    private void DrawGeneralHeads(SKCanvas canvas)
    {
        foreach (var (position, size, image) in _generalHeads)
        {
            float half = size / 2;
            if (image != null)
            {
                canvas.DrawImage(image, new SKRect(position.X - half, position.Y - half, position.X + half, position.Y + half), _imagePaint);
                continue;
            }
            canvas.DrawCircle(position, half, _headFill);
            _headStroke.StrokeWidth = half * 0.1f;
            canvas.DrawCircle(position, half, _headStroke);
        }
    }

    private void DrawGroupTexts(SKCanvas canvas)
    {
        foreach (var (position, text, size) in _groupTexts)
        {
            _font.Size = Math.Max(8, size * 0.8f);
            canvas.DrawText(text, position.X, position.Y, SKTextAlign.Center, _font, _groupTextPaint);
        }
    }

    private SKRect CalculateVisibleBounds()
    {
        float margin = 200f * (float)_camera.ZoomLevel;
        return new SKRect(-margin, -margin, (float)_camera.ViewportWidth + margin, (float)_camera.ViewportHeight + margin);
    }

    private SKPoint? CalculateScreenPosition(int coordinate)
    {
        int row = coordinate / _mapData.MapWidth;
        int col = coordinate % _mapData.MapWidth;
        if (row < 0 || row >= _mapData.MapHeight) return null;

        var (screenX, screenY) = _camera.HexToScreen(col, row);
        return new SKPoint((float)screenX, (float)screenY);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 解除对地图集合的订阅，避免渲染器释放后仍被集合事件引用
        if (_watchedV1 != null) _watchedV1.CollectionChanged -= OnReinforcementsChanged;
        if (_watchedV3 != null) _watchedV3.CollectionChanged -= OnReinforcementsChanged;

        lock (_atlasLock)
        {
            // _unitAtlas / _unitAtlasMap 归 UnitAtlasCache 持有，这里只解除引用，不能 Dispose
            _unitAtlas = null;
        }

        foreach (var image in _levelIconCache.Values)
            image.Dispose();
        _levelIconCache.Clear();
        _generalHeadCache.Clear(); // 头像归 TacticalMapImageCache 所有，只清引用

        _spritePaint.Dispose();
        _imagePaint.Dispose();
        _spawnRoundPaint.Dispose();
        _shadowPaint.Dispose();
        _groupTextPaint.Dispose();
        _placeholderFill.Dispose();
        _placeholderStroke.Dispose();
        _placeholderText.Dispose();
        _headFill.Dispose();
        _headStroke.Dispose();
        _font.Dispose();
    }
}
