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

    public void InvalidateCache()
    {
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
            if (_flagAtlas == null)
            {
                _stateLock.ExitReadLock();
                InitializeFlagAtlas(mapData);
                _stateLock.EnterReadLock();
                if (_flagAtlas == null) return;
            }

            if (mapData.Belongs == null || mapData.Belongs.Count == 0) return;

            float flagSize = CalculateFlagSize();
            var visibleHexes = GetVisibleHexes(mapData);
            if (visibleHexes.Count == 0) return;

            PrepareDrawAtlasData(visibleHexes, flagSize, mapData);
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

    private static int GetHexIndex(int col, int row, MapData mapData)
    {
        int mapClipX = mapData.Header?.MapClipX ?? 0;
        int mapClipY = mapData.Header?.MapClipY ?? 0;

        if (mapClipY > 0 && row < mapClipY) return -1;
        if (mapClipX > 0 && col < mapClipX) return -1;

        int conquestWidth = mapData.Header?.MapLength ?? 0;
        if (conquestWidth <= 0) conquestWidth = mapData.MapWidth - mapClipX;

        int actualCol = col - mapClipX;
        int actualRow = row - mapClipY;

        if (actualCol >= conquestWidth)
        {
            actualCol -= conquestWidth;
            actualRow++;
        }

        return actualRow * conquestWidth + actualCol;
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

    private List<(int col, int row)> GetVisibleHexes(MapData mapData)
    {
        var result = new List<(int, int)>();

        double hexSpacingX = HEX_HORIZONTAL_SPACING * _zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * _zoomLevel;

        int startCol = Math.Max(0, (int)((-_offsetX) / hexSpacingX) - 2);
        int endCol = Math.Min(mapData.MapWidth - 1, (int)((_viewportWidth - _offsetX) / hexSpacingX) + 2);
        int startRow = Math.Max(0, (int)((-_offsetY) / hexSpacingY) - 2);
        int endRow = Math.Min(mapData.MapHeight - 1, (int)((_viewportHeight - _offsetY) / hexSpacingY) + 2);

        int mapClipX = mapData.Header?.MapClipX ?? 0;
        int mapClipY = mapData.Header?.MapClipY ?? 0;

        for (int col = startCol; col <= endCol; col++)
        {
            for (int row = startRow; row <= endRow; row++)
            {
                if (mapClipY > 0 && row < mapClipY) continue;
                if (mapClipX > 0 && col < mapClipX) continue;
                result.Add((col, row));
            }
        }

        return result;
    }

    private void PrepareDrawAtlasData(List<(int col, int row)> visibleHexes, float flagSize, MapData mapData)
    {
        _spriteRects.Clear();
        _transforms.Clear();
        _belongTextPositions.Clear();

        float halfSize = flagSize / 2;
        float hexHeight = (float)(BASE_HEX_SIZE * Math.Sqrt(3) * _zoomLevel);

        for (int i = 0; i < visibleHexes.Count; i++)
        {
            var (col, row) = visibleHexes[i];
            int hexIndex = GetHexIndex(col, row, mapData);
            if (hexIndex < 0 || hexIndex >= mapData.Belongs.Count) continue;

            int countryId = GetCountryId(hexIndex, mapData);
            if (countryId < 0) continue;
            if (!_flagAtlasMap.ContainsKey(countryId)) continue;

            double centerX = _offsetX + col * HEX_HORIZONTAL_SPACING * _zoomLevel;
            double centerY = _offsetY + (col % 2) * (hexHeight / 2) + row * hexHeight;

            _spriteRects.Add(_flagAtlasMap[countryId]);

            float scale = flagSize / ATLAS_TILE_SIZE;
            float tx = (float)centerX - halfSize;
            float ty = (float)centerY + hexHeight / 2 - halfSize;

            _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));

            int belongValue = GetBelongValue(hexIndex, mapData);
            float spacing = Math.Max(2.0f, flagSize * 0.05f);
            float textX = tx + flagSize + spacing;
            float textY = ty + halfSize;
            _belongTextPositions.Add((new SKPoint(textX, textY), belongValue.ToString()));

            if (_transforms.Count >= MAX_ATLAS_DRAW_COUNT) break;
        }
    }

    private void DrawAtlasBatch(SKCanvas canvas)
    {
        if (_transforms.Count == 0) return;

        lock (_atlasLock)
        {
            if (_flagAtlas == null) return;

            using var paint = new SKPaint { IsAntialias = true };

            var sprites = _spriteRects.ToArray();
            var transforms = _transforms.ToArray();

            canvas.DrawAtlas(_flagAtlas, sprites, transforms, paint);
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

        using var skFont = new SKFont(SKTypeface.Default, fontSize);
        using var textPaint = new SKPaint { IsAntialias = true };

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

        _stateLock.Dispose();
    }
}