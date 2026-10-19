using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 首都国旗渲染层 - 对齐 VB 版 LegionModifier.DrawCapitalFlagsOnMap。
/// <para>
/// 遍历 <see cref="MapData.Capitals"/>，在每个首都格上绘制该格所属军团国家 ID 的国旗：
/// 优先取战术地图解析出的 flag_{countryId}.png，其次回退到 StageMark/CountryFlag 下的文件。
/// </para>
/// <para>
/// 归属值按本项目约定解释为 <see cref="MapData.Legions"/> 的索引；
/// 首都格本身没有归属(0xFF)时，到同省区内寻找一个有效归属
/// （对齐 VB DrawCapitalFlagsOnMap 的 FindProvinceLegionIndex 回退）。
/// </para>
/// <para>
/// 取不到国旗时（该国没有 flag_{countryId}.png，或该首都格及其省区都解析不出归属）
/// 退化为红色圆形占位，保证每个首都都有可见标记。
/// </para>
/// <para>只在军团编辑模式启用（由 RenderSceneBase.UpdateRenderLayersByMode 控制）。</para>
/// <para>
/// 国旗边长随缩放变化（缩放 1 时为 VB 的 60 像素，放大不超过 60、缩小到 16 为止）：
/// VB 固定 60 像素，地图缩小后相邻首都的国旗会叠成一片。
/// </para>
/// </summary>
public sealed class CapitalFlagRender : IDisposable
{
    /// <summary>缩放为 1 时的国旗边长（屏幕像素，对齐 VB 版的 60 像素），也是放大后的上限。</summary>
    private const int FLAG_SIZE = 60;

    /// <summary>地图缩得很小时国旗的最小边长（屏幕像素），保证仍然看得见。</summary>
    private const int MIN_FLAG_SIZE = 16;

    /// <summary>取不到国旗时的兜底标记颜色（红色圆形）</summary>
    private static readonly SKColor FALLBACK_CIRCLE_COLOR = SKColors.Red;

    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly ReaderWriterLockSlim _stateLock = new();
    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _enableCapitalFlagRender;
    private bool _disposed;

    private readonly Dictionary<int, SKImage?> _flagCache = new();

    /// <summary>
    /// 省区值 → 该省区内第一个有效归属（军团索引）。
    /// 无归属的首都格每帧都要在同省区里找归属；以前是每个首都扫一遍整张地图（O(首都数 × 格子数) / 帧），
    /// 现在整图扫一遍建表，归属 / 省区改动时由引擎调 <see cref="InvalidateProvinceLegionCache"/> 作废。
    /// </summary>
    private Dictionary<ushort, int>? _provinceLegion;
    private MapData? _provinceLegionSource;

    /// <summary>自己解码出来的图片（战术地图缓存返回的共享对象不在此列），Dispose 时需要释放</summary>
    private readonly HashSet<int> _ownedImages = new();

    private readonly object _flagCacheLock = new();

    public bool EnableCapitalFlagRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableCapitalFlagRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableCapitalFlagRender = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetX
    {
        get { _stateLock.EnterReadLock(); try { return _offsetX; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetX = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double OffsetY
    {
        get { _stateLock.EnterReadLock(); try { return _offsetY; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _offsetY = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public double ZoomLevel
    {
        get { _stateLock.EnterReadLock(); try { return _zoomLevel; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _zoomLevel = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportWidth
    {
        get { _stateLock.EnterReadLock(); try { return _viewportWidth; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportWidth = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    public int ViewportHeight
    {
        get { _stateLock.EnterReadLock(); try { return _viewportHeight; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _viewportHeight = Math.Max(1, value); } finally { _stateLock.ExitWriteLock(); } }
    }

    /// <summary>归属 / 省区数据变化后调用：下一帧重建省区→归属表。</summary>
    public void InvalidateProvinceLegionCache()
    {
        _provinceLegion = null;
        _provinceLegionSource = null;
    }

    /// <summary>丢弃国旗缓存（图集/国家配色热重载后调用）</summary>
    public void InvalidateCache()
    {
        InvalidateProvinceLegionCache();
        lock (_flagCacheLock)
        {
            foreach (int countryId in _ownedImages)
            {
                if (_flagCache.TryGetValue(countryId, out var image))
                    image?.Dispose();
            }

            _flagCache.Clear();
            _ownedImages.Clear();
        }
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (!EnableCapitalFlagRender || canvas == null || mapData == null) return;

        var capitals = mapData.Capitals;
        if (capitals == null || capitals.Count == 0) return;

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return;

        _stateLock.EnterReadLock();
        double offsetX, offsetY, zoomLevel;
        int viewportWidth, viewportHeight;
        try
        {
            offsetX = _offsetX;
            offsetY = _offsetY;
            zoomLevel = _zoomLevel;
            viewportWidth = _viewportWidth;
            viewportHeight = _viewportHeight;
        }
        finally
        {
            _stateLock.ExitReadLock();
        }

        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;
        int totalCells = mapWidth * mapHeight;
        float half = (float)Math.Clamp(FLAG_SIZE * zoomLevel, MIN_FLAG_SIZE, FLAG_SIZE) / 2.0f;

        using var imagePaint = new SKPaint { IsAntialias = true };
        using var fallbackPaint = new SKPaint { IsAntialias = true, Color = FALLBACK_CIRCLE_COLOR };

        for (int i = 0; i < capitals.Count; i++)
        {
            int hexIndex = capitals[i].Coordinate;
            if (hexIndex < 0 || hexIndex >= totalCells) continue;

            int col = hexIndex % mapWidth;
            int row = hexIndex / mapWidth;

            // 数据范围外的格子（红叉层）不画国旗：游戏不会读取那里的首都
            if (!mapData.IsInBelongRange(col, row)) continue;

            // 与地形/领域/归属国旗层共用同一套屏幕坐标公式
            float x = (float)(offsetX + col * hexSpacingX);
            float y = (float)(offsetY + (col % 2) * (hexSpacingY / 2) + row * hexSpacingY);

            // 视口外直接跳过
            if (x + half < 0 || x - half > viewportWidth || y + half < 0 || y - half > viewportHeight) continue;

            int countryId = ResolveCapitalCountryId(mapData, col, row, hexIndex);
            var flag = countryId >= 0 ? GetFlagImage(countryId) : null;

            if (flag != null)
            {
                var destRect = new SKRect(x - half, y - half, x + half, y + half);
                canvas.DrawImage(flag, destRect, imagePaint);
            }
            else
            {
                // 兜底：该国没有国旗资源，或该首都格及其省区都解析不出归属 → 红色圆形
                canvas.DrawCircle(x, y, half, fallbackPaint);
            }
        }
    }

    /// <summary>
    /// 解析首都格应显示的国家 ID：先取该格归属（= 军团索引），
    /// 无归属时到同省区内寻找（对齐 VB DrawCapitalFlagsOnMap + FindProvinceLegionIndex）。
    /// </summary>
    private int ResolveCapitalCountryId(MapData mapData, int col, int row, int hexIndex)
    {
        int legionIndex = mapData.GetBelongValue(col, row);

        if (!IsValidLegionIndex(legionIndex, mapData))
            legionIndex = FindProvinceLegionIndex(mapData, hexIndex);

        if (!IsValidLegionIndex(legionIndex, mapData)) return -1;

        return mapData.Legions[legionIndex].CountryId;
    }

    /// <summary>归属值 0xFF(255) 表示无归属；超出军团数量也视为无效</summary>
    private static bool IsValidLegionIndex(int legionIndex, MapData mapData)
        => legionIndex >= 0 && legionIndex != 0xFF && legionIndex < mapData.Legions.Count;

    /// <summary>
    /// 首都格没有归属时，取同省区（ProvinceValue 相同）内第一个有效归属。
    /// 对齐 VB LegionModifier.FindProvinceLegionIndex：原实现按格子下标顺序找第一个有效归属，
    /// 这里的表同样按下标顺序只记第一个，结果一致（首都格自己没有有效归属，天然不会被记进表）。
    /// </summary>
    private int FindProvinceLegionIndex(MapData mapData, int capitalHexIndex)
    {
        var capitalProvince = mapData.GetProvinceRef(capitalHexIndex);
        if (!capitalProvince.IsValid) return -1;

        var table = _provinceLegion;
        if (table == null || !ReferenceEquals(_provinceLegionSource, mapData))
        {
            table = BuildProvinceLegionTable(mapData);
            _provinceLegion = table;
            _provinceLegionSource = mapData;
        }

        return table.TryGetValue(capitalProvince.ProvinceValue, out int legion) ? legion : -1;
    }

    private static Dictionary<ushort, int> BuildProvinceLegionTable(MapData mapData)
    {
        var table = new Dictionary<ushort, int>();
        int mapWidth = mapData.MapWidth;
        int totalCells = mapWidth * mapData.MapHeight;
        for (int i = 0; i < totalCells; i++)
        {
            var province = mapData.GetProvinceRef(i);
            if (!province.IsValid || table.ContainsKey(province.ProvinceValue)) continue;

            int belongValue = mapData.GetBelongValue(i % mapWidth, i / mapWidth);
            if (IsValidLegionIndex(belongValue, mapData))
                table[province.ProvinceValue] = belongValue;
        }
        return table;
    }

    /// <summary>取国旗图片（按国家缓存），自己解码出来的记入 <see cref="_ownedImages"/>，Dispose 时释放。</summary>
    private SKImage? GetFlagImage(int countryId)
    {
        lock (_flagCacheLock)
        {
            if (_flagCache.TryGetValue(countryId, out var cached)) return cached;

            var image = LoadFlagImage(countryId, out bool owned);
            if (owned) _ownedImages.Add(countryId);
            _flagCache[countryId] = image;
            return image;
        }
    }

    /// <summary>
    /// 加载国旗：优先战术地图图集里的 flag_{countryId}.png（与 VB 相同），其次 StageMark/CountryFlag 目录下的文件。
    /// 图集返回的是共享对象（<paramref name="owned"/> = false，调用方不得释放）；
    /// 文件解码出来的归调用方所有（<paramref name="owned"/> = true）。军团截图也走这里。
    /// </summary>
    internal static SKImage? LoadFlagImage(int countryId, out bool owned)
    {
        owned = false;
        try
        {
            var cache = TacticalMapImageCache.Instance;
            if (cache.IsInitialized && cache.GetImage($"flag_{countryId}.png") is { } atlasImage)
                return atlasImage;

            var fileImage = LoadFlagImageFromFile(countryId);
            owned = fileImage != null;
            return fileImage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CapitalFlagRender] 加载国旗失败(国家{countryId}): {ex.Message}");
            return null;
        }
    }

    private static SKImage? LoadFlagImageFromFile(int countryId)
    {
        try
        {
            string flagPath = GetFlagPath(countryId);
            if (!File.Exists(flagPath)) return null;

            using var stream = File.OpenRead(flagPath);
            // 与归属国旗层保持一致：不 Dispose bitmap，确保 SKImage 背后的像素仍然有效
            var bitmap = SKBitmap.Decode(stream);
            return bitmap == null ? null : SKImage.FromBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static string GetFlagPath(int countryId)
    {
        string basePath = Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
        string flagPath = Path.Combine(basePath, $"flag_{countryId}.png");

        // 对齐归属国旗层：无国家(0/255)时回退到 flag_1
        if (!File.Exists(flagPath) && (countryId == 0 || countryId == 255))
            flagPath = Path.Combine(basePath, "flag_1.png");

        return flagPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        InvalidateCache();
        _stateLock.Dispose();
    }
}
