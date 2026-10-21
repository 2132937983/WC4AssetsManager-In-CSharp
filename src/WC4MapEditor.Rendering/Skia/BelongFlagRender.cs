using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class BelongFlagRender : IDisposable
{
    private const double FLAG_SIZE_RATIO = 0.4;
    private const int ATLAS_TILE_SIZE = 64;
    private const int MAX_ATLAS_DRAW_COUNT = 20000;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly ReaderWriterLockSlim _stateLock = new();
    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _enableBelongFlagRender;
    private bool _disposed;

    private SKImage? _flagAtlas;
    private readonly Dictionary<int, SKRect> _flagAtlasMap = new();
    private readonly object _atlasLock = new();
    private bool _atlasInitialized;

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<(SKPoint Position, string Text)> _belongTextPositions = new();

    // 归属值 0..255 的标签字符串表，避免每格每帧 belongValue.ToString()。
    private static readonly string[] BelongLabels = BuildBelongLabels();

    // 复用的图集画笔与标签字体 / 画笔（只在渲染线程使用），每帧只改字号与颜色。
    private readonly SKPaint _atlasPaint = new() { IsAntialias = true };
    private readonly SKFont _belongTextFont = new(SKTypeface.Default, 10);
    private readonly SKPaint _belongTextPaint = new() { IsAntialias = true };

    public bool EnableBelongFlagRender
    {
        get { _stateLock.EnterReadLock(); try { return _enableBelongFlagRender; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _enableBelongFlagRender = value; } finally { _stateLock.ExitWriteLock(); } }
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

    /// <summary>
    /// 丢弃国旗图集，下一帧渲染时按当前军团 / 归属数据重建。
    /// <para>
    /// 军团的增删改会让国家 ID 集合发生变化（新增军团拿到新的 CountryId），
    /// 而图集只在首次渲染时构建一次，不失效的话新国家的国旗永远不在图集里。
    /// </para>
    /// </summary>
    public void InvalidateCache()
    {
        // 渲染时 Render() 持有读锁，这里拿写锁，避免 Dispose 掉正在绘制的图集
        _stateLock.EnterWriteLock();
        try
        {
            lock (_atlasLock)
            {
                _flagAtlas?.Dispose();
                _flagAtlas = null;
                _flagAtlasMap.Clear();
                _atlasInitialized = false;
            }
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
    }

    public void PreloadAtlas(MapData mapData)
    {
        if (_atlasInitialized) return;
        InitializeFlagAtlas(mapData);
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (!EnableBelongFlagRender || canvas == null || mapData == null) return;

        _stateLock.EnterReadLock();
        try
        {
            float flagSize = CalculateFlagSize();
            // 旗子太小时既看不清，又要为整屏每个格子准备 sprite / DrawAtlas 数据。
            // 必须在初始化图集之前判断，否则每帧重建 64×64 国旗图集，比绘制本身还慢。
            if (flagSize < 16f) return;

            if (_flagAtlas == null)
            {
                _stateLock.ExitReadLock();
                InitializeFlagAtlas(mapData);
                _stateLock.EnterReadLock();
                if (_flagAtlas == null) return;
            }

            if (mapData.Belongs == null || mapData.Belongs.Count == 0) return;

            PrepareDrawAtlasData(flagSize, mapData);
            DrawAtlasBatch(canvas);
        }
        finally
        {
            _stateLock.ExitReadLock();
        }
    }

    private void InitializeFlagAtlas(MapData mapData)
    {
        if (_atlasInitialized) return;

        lock (_atlasLock)
        {
            if (_atlasInitialized) return;

            try
            {
                _flagAtlas?.Dispose();
                _flagAtlas = null;
                _flagAtlasMap.Clear();

                var availableFlags = CollectAvailableFlags(mapData);
                if (availableFlags.Count == 0) return;

                int atlasTiles = CalculateAtlasSize(availableFlags.Count);
                int atlasWidth = atlasTiles * ATLAS_TILE_SIZE;
                int atlasHeight = atlasTiles * ATLAS_TILE_SIZE;

                using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                int tileIndex = 0;
                foreach (int countryId in availableFlags)
                {
                    if (tileIndex >= atlasTiles * atlasTiles) break;

                    bool fromCache;
                    SKImage? flagImage;
                    var cachedImage = TacticalMapImageCache.Instance.GetImage($"flag_{countryId}.png");
                    if (cachedImage != null)
                    {
                        flagImage = cachedImage;
                        fromCache = true;
                    }
                    else
                    {
                        flagImage = LoadFlagImageFromFile(countryId);
                        fromCache = false;
                    }

                    if (flagImage != null)
                    {
                        int atlasX = (tileIndex % atlasTiles) * ATLAS_TILE_SIZE;
                        int atlasY = (tileIndex / atlasTiles) * ATLAS_TILE_SIZE;

                        var srcRect = new SKRect(0, 0, flagImage.Width, flagImage.Height);
                        var dstRect = new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE);
                        canvas.DrawImage(flagImage, srcRect, dstRect);

                        _flagAtlasMap[countryId] = new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE);
                        if (!fromCache) flagImage.Dispose();
                        tileIndex++;
                    }
                }

                _flagAtlas = surface.Snapshot();
                _atlasInitialized = true;
            }
            catch
            {
            }
        }
    }

    private List<int> CollectAvailableFlags(MapData mapData)
    {
        var result = new List<int>();

        if (mapData.Belongs != null)
        {
            foreach (string belongStr in mapData.Belongs)
            {
                int countryId = GetCountryIdFromBelongString(belongStr, mapData);
                if (countryId >= 0 && !result.Contains(countryId) && FlagExists(countryId))
                    result.Add(countryId);
            }
        }

        foreach (var legion in mapData.Legions)
        {
            if (!result.Contains(legion.CountryId) && FlagExists(legion.CountryId))
                result.Add(legion.CountryId);
        }

        if (result.Count == 0)
        {
            for (int i = 0; i <= 20; i++)
                if (FlagExists(i)) result.Add(i);
            if (FlagExists(255)) result.Add(255);
        }

        return result;
    }

    private static int GetCountryIdFromBelongString(string belongStr, MapData mapData)
    {
        if (string.IsNullOrEmpty(belongStr)) return -1;

        try
        {
            int belongValue;
            if (belongStr.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                belongValue = Convert.ToInt32(belongStr.Substring(2), 16);
            else if (int.TryParse(belongStr, System.Globalization.NumberStyles.HexNumber, null, out int hexResult))
                belongValue = hexResult;
            else if (int.TryParse(belongStr, out int result))
                belongValue = result;
            else
                belongValue = Convert.ToInt32(belongStr, 16);

            if (belongValue < 0 || belongValue == 255) return -1;

            if (belongValue >= 0 && belongValue < mapData.Legions.Count)
                return mapData.Legions[belongValue].CountryId;

            return belongValue;
        }
        catch
        {
            return -1;
        }
    }

    private static int GetCountryId(int hexIndex, MapData mapData)
    {
        try
        {
            int belongValue = GetBelongValue(hexIndex, mapData);
            if (belongValue < 0 || belongValue == 255) return -1;

            if (belongValue >= 0 && belongValue < mapData.Legions.Count)
                return mapData.Legions[belongValue].CountryId;

            return belongValue;
        }
        catch
        {
            return -1;
        }
    }

    private static int GetBelongValue(int hexIndex, MapData mapData)
    {
        if (mapData.Belongs == null || hexIndex < 0 || hexIndex >= mapData.Belongs.Count) return -1;

        string belongStr = mapData.Belongs[hexIndex];
        if (string.IsNullOrEmpty(belongStr)) return -1;

        try
        {
            if (belongStr.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(belongStr.Substring(2), 16);

            if (int.TryParse(belongStr, System.Globalization.NumberStyles.HexNumber, null, out int hexResult))
                return hexResult;

            if (int.TryParse(belongStr, out int result))
                return result;

            return Convert.ToInt32(belongStr, 16);
        }
        catch
        {
            return -1;
        }
    }

    private static bool FlagExists(int countryId)
    {
        var cache = TacticalMapImageCache.Instance;
        if (cache.IsInitialized && cache.HasImage($"flag_{countryId}.png"))
            return true;

        var tacticalMapParser = ConfigManager.Instance.TacticalMapParser;
        if (tacticalMapParser != null && tacticalMapParser.HasImage($"flag_{countryId}.png"))
            return true;

        return File.Exists(GetFlagPath(countryId));
    }

    private static string GetFlagPath(int countryId)
    {
        string basePath = Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
        string flagPath = Path.Combine(basePath, $"flag_{countryId}.png");

        if (!File.Exists(flagPath) && (countryId == 0 || countryId == 255))
            flagPath = Path.Combine(basePath, "flag_1.png");

        return flagPath;
    }

    private static SKImage? LoadFlagImage(int countryId)
    {
        var cache = TacticalMapImageCache.Instance;
        if (cache.IsInitialized)
        {
            var flagImage = cache.GetImage($"flag_{countryId}.png");
            if (flagImage != null) return flagImage;
        }

        return LoadFlagImageFromFile(countryId);
    }

    private static SKImage? LoadFlagImageFromFile(int countryId)
    {
        string flagPath = GetFlagPath(countryId);
        try
        {
            if (File.Exists(flagPath))
            {
                using var stream = File.OpenRead(flagPath);
                var bitmap = SKBitmap.Decode(stream);
                if (bitmap != null) return SKImage.FromBitmap(bitmap);
            }
        }
        catch
        {
        }
        return null;
    }

    private static int CalculateAtlasSize(int totalCount)
    {
        int size = 4;
        while (size * size < totalCount)
            size *= 2;
        return Math.Min(size, 16);
    }

    private float CalculateFlagSize()
    {
        double hexSize = BASE_HEX_SIZE * Math.Sqrt(3) * _zoomLevel;
        return (float)(hexSize * FLAG_SIZE_RATIO);
    }

    private static string[] BuildBelongLabels()
    {
        var labels = new string[256];
        for (int i = 0; i < labels.Length; i++) labels[i] = i.ToString();
        return labels;
    }

    private static string GetBelongLabel(int belongValue)
        => (uint)belongValue < (uint)BelongLabels.Length ? BelongLabels[belongValue] : belongValue.ToString();

    private void PrepareDrawAtlasData(float flagSize, MapData mapData)
    {
        _spriteRects.Clear();
        _transforms.Clear();
        _belongTextPositions.Clear();

        // 直接按 列→行 遍历可视范围，不再先收集成 List<(col,row)> 再二次遍历；遍历顺序与原先一致。
        double hexSpacingX = HEX_HORIZONTAL_SPACING * _zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * _zoomLevel;

        int startCol = Math.Max(0, (int)((-_offsetX) / hexSpacingX) - 2);
        int endCol = Math.Min(mapData.MapWidth - 1, (int)((_viewportWidth - _offsetX) / hexSpacingX) + 2);
        int startRow = Math.Max(0, (int)((-_offsetY) / hexSpacingY) - 2);
        int endRow = Math.Min(mapData.MapHeight - 1, (int)((_viewportHeight - _offsetY) / hexSpacingY) + 2);

        float halfSize = flagSize / 2;
        float hexHeight = (float)(BASE_HEX_SIZE * Math.Sqrt(3) * _zoomLevel);
        float scale = flagSize / ATLAS_TILE_SIZE;
        float spacing = Math.Max(2.0f, flagSize * 0.05f);

        for (int col = startCol; col <= endCol; col++)
        {
            for (int row = startRow; row <= endRow; row++)
            {
                int hexIndex = mapData.GetBelongIndex(col, row);
                if (hexIndex < 0 || hexIndex >= mapData.Belongs.Count) continue;

                int countryId = GetCountryId(hexIndex, mapData);
                if (countryId < 0) continue;
                if (!_flagAtlasMap.TryGetValue(countryId, out var spriteRect)) continue;

                double centerX = _offsetX + col * HEX_HORIZONTAL_SPACING * _zoomLevel;
                double centerY = _offsetY + (col % 2) * (hexHeight / 2) + row * hexHeight;

                _spriteRects.Add(spriteRect);

                float tx = (float)centerX - halfSize;
                float ty = (float)centerY + hexHeight / 2 - halfSize;

                _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));

                int belongValue = GetBelongValue(hexIndex, mapData);
                float textX = tx + flagSize + spacing;
                float textY = ty + halfSize;
                _belongTextPositions.Add((new SKPoint(textX, textY), GetBelongLabel(belongValue)));

                if (_transforms.Count >= MAX_ATLAS_DRAW_COUNT) return;
            }
        }
    }

    private void DrawAtlasBatch(SKCanvas canvas)
    {
        if (_transforms.Count == 0) return;

        lock (_atlasLock)
        {
            if (_flagAtlas == null) return;

            var sprites = _spriteRects.ToArray();
            var transforms = _transforms.ToArray();

            canvas.DrawAtlas(_flagAtlas, sprites, transforms, _atlasPaint);
        }

        DrawBelongTexts(canvas);
    }

    private void DrawBelongTexts(SKCanvas canvas)
    {
        if (_belongTextPositions.Count == 0) return;

        float flagSize = CalculateFlagSize();
        float fontSize = flagSize * 0.8f;

        if (fontSize < 6.0f) return;

        float shadowOffset = Math.Max(1.0f, fontSize * 0.1f);

        // 复用字段字体 / 画笔，只改字号与颜色；原先每帧各 new 一个。
        var skFont = _belongTextFont;
        skFont.Size = fontSize;
        var textPaint = _belongTextPaint;

        float textOffsetY = fontSize * 0.35f;

        textPaint.Color = SKColors.Black;
        for (int i = 0; i < _belongTextPositions.Count; i++)
        {
            var pos = _belongTextPositions[i].Position;
            canvas.DrawText(_belongTextPositions[i].Text, pos.X + shadowOffset, pos.Y + textOffsetY + shadowOffset, SKTextAlign.Left, skFont, textPaint);
        }

        textPaint.Color = SKColors.White;
        for (int i = 0; i < _belongTextPositions.Count; i++)
        {
            var pos = _belongTextPositions[i].Position;
            canvas.DrawText(_belongTextPositions[i].Text, pos.X, pos.Y + textOffsetY, SKTextAlign.Left, skFont, textPaint);
        }
    }

    public void ReinitializeAtlas()
    {
        lock (_atlasLock)
        {
            _flagAtlas?.Dispose();
            _flagAtlas = null;
            _flagAtlasMap.Clear();
            _atlasInitialized = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            _flagAtlas?.Dispose();
            _flagAtlas = null;
            _flagAtlasMap.Clear();
        }

        _atlasPaint.Dispose();
        _belongTextFont.Dispose();
        _belongTextPaint.Dispose();
        _stateLock.Dispose();
    }
}