using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 单位图集共享缓存。
///
/// ArmyRender、ReinforceRender、ReinforceRenderNew 三个渲染器需要的是完全相同的一份
/// legion_icon_*.png 图集，但原实现各自扫描目录、逐个解码 PNG、再拼成图集，
/// 同一份资源被完整构建三次（冷启动时约 400ms，且各渲染器耗时波动很大）。
///
/// 这里改为全进程只构建一次，三个渲染器共享同一份 SKImage 与切片表。
/// 共享的 SKImage 由本缓存持有所有权，调用方只能读取、不可 Dispose，
/// 也不得修改切片表。
/// </summary>
internal static class UnitAtlasCache
{
    /// <summary>图集中每个单位的格子边长，三个渲染器原本都使用 64。</summary>
    internal const int TileSize = 64;

    private static readonly object _lock = new();
    private static bool _initialized;

    private static SKImage? _atlas;
    private static Dictionary<int, SKRect> _tileMap = new();

    /// <summary>
    /// 取共享单位图集。返回的 <paramref name="atlas"/> 归缓存所有，调用方不得 Dispose；
    /// <paramref name="tileMap"/> 为只读用途，调用方不得修改。
    /// </summary>
    internal static bool TryGet(
        [NotNullWhen(true)] out SKImage? atlas,
        [NotNullWhen(true)] out Dictionary<int, SKRect>? tileMap)
    {
        EnsureInitialized();

        atlas = _atlas;
        tileMap = _atlas != null ? _tileMap : null;
        return _atlas != null;
    }

    /// <summary>丢弃已有图集并允许下次调用重建（用于资源目录变更后的热重载）。</summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            _atlas?.Dispose();
            _atlas = null;
            _tileMap = new Dictionary<int, SKRect>();
            _initialized = false;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            try
            {
                string armyMarkPath = ConfigManager.Instance.GetArmyMarkPath();
                if (!Directory.Exists(armyMarkPath)) return;

                var availableUnits = CollectAvailableUnits(armyMarkPath);
                if (availableUnits.Count == 0) return;

                var sw = Stopwatch.StartNew();

                int atlasTiles = CalculateAtlasSize(availableUnits.Count);
                int atlasWidth = atlasTiles * TileSize;
                int atlasHeight = atlasTiles * TileSize;

                using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var map = new Dictionary<int, SKRect>(availableUnits.Count);
                int tileIndex = 0;

                foreach (int unitType in availableUnits)
                {
                    if (tileIndex >= atlasTiles * atlasTiles) break;

                    using var unitImage = LoadUnitImage(armyMarkPath, unitType);
                    if (unitImage == null) continue;

                    int atlasX = (tileIndex % atlasTiles) * TileSize;
                    int atlasY = (tileIndex / atlasTiles) * TileSize;

                    var srcRect = new SKRect(0, 0, unitImage.Width, unitImage.Height);
                    var dstRect = new SKRect(atlasX, atlasY, atlasX + TileSize, atlasY + TileSize);
                    canvas.DrawImage(unitImage, srcRect, dstRect);

                    map[unitType] = new SKRect(atlasX, atlasY, atlasX + TileSize, atlasY + TileSize);
                    tileIndex++;
                }

                _atlas = surface.Snapshot();
                _tileMap = map;
                _initialized = true;

                Debug.WriteLine($"[UnitAtlasCache] 单位图集构建完成: {atlasWidth}x{atlasHeight}, {map.Count} 个类型, 耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                // 保持 _initialized = false，下次调用可重试，行为与原先各渲染器内部实现一致。
                Debug.WriteLine($"[UnitAtlasCache] 构建单位图集失败: {ex.Message}");
            }
        }
    }

    private static List<int> CollectAvailableUnits(string armyMarkPath)
    {
        var units = new List<int>();
        try
        {
            foreach (string file in Directory.GetFiles(armyMarkPath, "legion_icon_*.png"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int type))
                    units.Add(type);
            }
            units.Sort();
        }
        catch { }
        return units;
    }

    private static int CalculateAtlasSize(int unitCount)
    {
        int tiles = 1;
        while (tiles * tiles < unitCount) tiles *= 2;
        return tiles;
    }

    private static SKImage? LoadUnitImage(string armyMarkPath, int unitType)
    {
        try
        {
            string imagePath = Path.Combine(armyMarkPath, $"legion_icon_{unitType}.png");
            if (File.Exists(imagePath))
            {
                using var stream = File.OpenRead(imagePath);
                return SKImage.FromEncodedData(stream);
            }
        }
        catch { }
        return null;
    }
}
