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
/// </summary>
public sealed class CapitalFlagRender : IDisposable
{
    /// <summary>国旗绘制尺寸（屏幕像素）。对齐 VB 版固定 60 像素，不随缩放变化。</summary>
    private const int FLAG_SIZE = 60;

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

    /// <summary>丢弃国旗缓存（图集/国家配色热重载后调用）</summary>
    public void InvalidateCache()
    {
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
        float half = FLAG_SIZE / 2.0f;

        using var imagePaint = new SKPaint { IsAntialias = true };
        using var fallbackPaint = new SKPaint { IsAntialias = true, Color = FALLBACK_CIRCLE_COLOR };

        for (int i = 0; i < capitals.Count; i++)
        {
            int hexIndex = capitals[i].Coordinate;
            if (hexIndex < 0 || hexIndex >= totalCells) continue;

            int col = hexIndex % mapWidth;
            int row = hexIndex / mapWidth;

            if (IsTileClipped(mapData, col, row)) continue;

            // 与地形/领域/归属国旗层共用同一套屏幕坐标公式
            float x = (float)(offsetX + col * hexSpacingX);
            float y = (float)(offsetY + (col % 2) * (hexSpacingY / 2) + row * hexSpacingY);

            // 国旗尺寸固定，视口外直接跳过
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
    private static int ResolveCapitalCountryId(MapData mapData, int col, int row, int hexIndex)
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
    /// 首都格没有归属时，遍历同省区（ProvinceValue 相同）的其他格子找一个有效归属。
    /// 对齐 VB LegionModifier.FindProvinceLegionIndex。
    /// </summary>
    private static int FindProvinceLegionIndex(MapData mapData, int capitalHexIndex)
    {
        var capitalProvince = mapData.GetProvinceRef(capitalHexIndex);
        if (!capitalProvince.IsValid) return -1;

        ushort provinceValue = capitalProvince.ProvinceValue;
        int mapWidth = mapData.MapWidth;
        int totalCells = mapWidth * mapData.MapHeight;

        for (int i = 0; i < totalCells; i++)
        {
            if (i == capitalHexIndex) continue;

            var province = mapData.GetProvinceRef(i);
            if (!province.IsValid || province.ProvinceValue != provinceValue) continue;

            int belongValue = mapData.GetBelongValue(i % mapWidth, i / mapWidth);
            if (IsValidLegionIndex(belongValue, mapData))
                return belongValue;
        }

        return -1;
    }

    /// <summary>地图裁剪区之外的格子不绘制（与归属国旗层一致）</summary>
    private static bool IsTileClipped(MapData mapData, int col, int row)
    {
        int clipX = mapData.Header?.MapClipX ?? 0;
        int clipY = mapData.Header?.MapClipY ?? 0;
        if (clipY > 0 && row < clipY) return true;
        if (clipX > 0 && col < clipX) return true;
        return false;
    }

    /// <summary>
    /// 取国旗图片：优先战术地图解析出的 flag_{countryId}.png，其次 CountryFlag 目录下的文件。
    /// 战术地图缓存返回的是共享对象，不归本层所有，Dispose 时不能释放。
    /// </summary>
    private SKImage? GetFlagImage(int countryId)
    {
        lock (_flagCacheLock)
        {
            if (_flagCache.TryGetValue(countryId, out var cached)) return cached;

            SKImage? image = null;
            try
            {
                var cache = TacticalMapImageCache.Instance;
                if (cache.IsInitialized)
                    image = cache.GetImage($"flag_{countryId}.png");

                if (image == null)
                {
                    image = LoadFlagImageFromFile(countryId);
                    if (image != null) _ownedImages.Add(countryId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CapitalFlagRender] 加载国旗失败(国家{countryId}): {ex.Message}");
            }

            _flagCache[countryId] = image;
            return image;
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
