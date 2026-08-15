using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Models;
using WC4MapEditor.Rendering.Helpers;

namespace WC4MapEditor.Rendering.Skia;

public class LandTerrainsRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);
    private const int ATLAS_TILE_SIZE = 64;

    private readonly TerrainHelper _terrainHelper;
    private readonly ReaderWriterLockSlim _stateLock = new();

    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _showLayer2;

    private SKImage? _textureAtlas;
    private readonly Dictionary<string, SKRect> _terrainAtlasMap = new();
    private readonly Dictionary<string, SKSize> _terrainOriginalSize = new();
    private readonly object _atlasLock = new();

    private struct TerrainDrawCall
    {
        public float CenterX;
        public float CenterY;
        public float Size;
        public string CacheKey;
        public TerrainDrawCall() { CacheKey = ""; }
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
        set { _stateLock.EnterWriteLock(); try { _zoomLevel = Math.Max(0.1, Math.Min(5.0, value)); } finally { _stateLock.ExitWriteLock(); } }
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

    public bool ShowLayer2
    {
        get { _stateLock.EnterReadLock(); try { return _showLayer2; } finally { _stateLock.ExitReadLock(); } }
        set { _stateLock.EnterWriteLock(); try { _showLayer2 = value; } finally { _stateLock.ExitWriteLock(); } }
    }

    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    public LandTerrainsRender()
    {
        _terrainHelper = new TerrainHelper("MapTerrian");
    }

    private void InitializeTextureAtlas()
    {
        if (_terrainHelper == null) return;

        lock (_atlasLock)
        {
            _textureAtlas?.Dispose();
            _textureAtlas = null;
            _terrainAtlasMap.Clear();
            _terrainOriginalSize.Clear();

            int totalTerrainCount = CalculateTotalTerrainCount();
            int atlasTiles = CalculateAtlasSize(totalTerrainCount);
            int atlasWidth = atlasTiles * ATLAS_TILE_SIZE;
            int atlasHeight = atlasTiles * ATLAS_TILE_SIZE;

            Debug.WriteLine($"[LandTerrainsRender] 图集尺寸: {atlasTiles}x{atlasTiles} = {atlasTiles * atlasTiles} 个格子");

            using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            int tileIndex = 0;
            int maxTerrainType = GetMaxTerrainType();

            for (int terrainType = 2; terrainType <= maxTerrainType; terrainType++)
            {
                int variantCount = _terrainHelper.GetTerrainVariantCount(terrainType);
                for (int decorationType = 0; decorationType < variantCount; decorationType++)
                {
                    if (tileIndex >= atlasTiles * atlasTiles) break;

                    var cacheKey = $"{terrainType}_{decorationType}";
                    var image = _terrainHelper.GetTerrainSkImage(terrainType, decorationType);
                    if (image != null)
                    {
                        _terrainOriginalSize[cacheKey] = new SKSize(image.Width, image.Height);
                        int atlasX = (tileIndex % atlasTiles) * ATLAS_TILE_SIZE;
                        int atlasY = (tileIndex / atlasTiles) * ATLAS_TILE_SIZE;
                        canvas.DrawImage(image, new SKRect(0, 0, image.Width, image.Height),
                            new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE));
                        _terrainAtlasMap[cacheKey] = new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE);
                        tileIndex++;
                    }
                }
            }

            _textureAtlas = surface.Snapshot();
            Debug.WriteLine($"[LandTerrainsRender] 图集初始化完成: {_terrainAtlasMap.Count} 个地形");
        }
    }

    private int CalculateTotalTerrainCount()
    {
        int count = 0;
        int maxType = GetMaxTerrainType();
        for (int i = 2; i <= maxType; i++)
            count += _terrainHelper.GetTerrainVariantCount(i);
        return count;
    }

    private static int CalculateAtlasSize(int totalCount)
    {
        int size = 8;
        while (size * size < totalCount) size *= 2;
        return Math.Min(size, 32);
    }

    private int GetMaxTerrainType()
    {
        int maxType = 0;
        for (int i = 0; i <= 255; i++)
        {
            if (!string.IsNullOrEmpty(_terrainHelper.GetTerrainTypeName(i)))
                maxType = i;
        }
        return maxType == 0 ? 40 : maxType;
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (mapData == null) return;

        if (_textureAtlas == null)
        {
            MapWidth = mapData.MapWidth;
            MapHeight = mapData.MapHeight;
            InitializeTextureAtlas();
            if (_textureAtlas == null) return;
        }

        double offsetX, offsetY, zoomLevel;
        int vpW, vpH;
        bool showLayer2;

        _stateLock.EnterReadLock();
        try
        {
            offsetX = _offsetX; offsetY = _offsetY; zoomLevel = _zoomLevel;
            vpW = _viewportWidth; vpH = _viewportHeight; showLayer2 = _showLayer2;
        }
        finally { _stateLock.ExitReadLock(); }

        double hexSpacingX = HEX_HORIZONTAL_SPACING * zoomLevel;
        double hexSpacingY = HEX_VERTICAL_SPACING * zoomLevel;
        float hexSize = (float)(BASE_HEX_SIZE * zoomLevel);

        int padding = 2;
        int visibleCols = (int)(vpW / hexSpacingX) + padding * 2;
        int visibleRows = (int)(vpH / hexSpacingY) + padding * 2;
        int startCol = Math.Max(0, (int)((-offsetX) / hexSpacingX) - padding);
        int startRow = Math.Max(0, (int)((-offsetY) / hexSpacingY) - padding);
        int endCol = Math.Min(mapData.MapWidth - 1, startCol + visibleCols);
        int endRow = Math.Min(mapData.MapHeight - 1, startRow + visibleRows);

        var batch = new List<TerrainDrawCall>();

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            double centerYBase = offsetY + (col % 2) * (hexSpacingY / 2);

            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = centerYBase + row * hexSpacingY;
                var terrain = mapData.GetTerrainAt(col, row);

                if (terrain.TileType1 != 0 && terrain.TileType1 != 1)
                {
                    int variantCount1 = _terrainHelper.GetTerrainVariantCount(terrain.TileType1);
                    int decorationIndex1 = terrain.DecorationType1 % variantCount1;
                    var cacheKey = $"{terrain.TileType1}_{decorationIndex1}";
                    if (_terrainAtlasMap.ContainsKey(cacheKey))
                        batch.Add(new TerrainDrawCall { CenterX = (float)centerX, CenterY = (float)centerY, Size = hexSize, CacheKey = cacheKey });
                }

                if (showLayer2 && terrain.TileType2 != 0 && terrain.TileType2 != 63 && terrain.TileType2 != 255)
                {
                    int variantCount2 = _terrainHelper.GetTerrainVariantCount(terrain.TileType2);
                    int decorationIndex2 = terrain.DecorationType2 % variantCount2;
                    var cacheKey = $"{terrain.TileType2}_{decorationIndex2}";
                    if (_terrainAtlasMap.ContainsKey(cacheKey))
                        batch.Add(new TerrainDrawCall { CenterX = (float)centerX, CenterY = (float)centerY, Size = hexSize, CacheKey = cacheKey });
                }
            }
        }

        if (batch.Count > 0)
            DrawTerrainFromAtlas(canvas, batch);
    }

    private void DrawTerrainFromAtlas(SKCanvas canvas, List<TerrainDrawCall> batch)
    {
        if (_textureAtlas == null || batch.Count == 0) return;

        var groups = new Dictionary<string, List<TerrainDrawCall>>();
        foreach (var dc in batch)
        {
            if (!groups.TryGetValue(dc.CacheKey, out var list))
            {
                list = new List<TerrainDrawCall>();
                groups[dc.CacheKey] = list;
            }
            list.Add(dc);
        }

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var kvp in groups)
        {
            if (!_terrainAtlasMap.TryGetValue(kvp.Key, out var srcRect)) continue;
            if (!_terrainOriginalSize.TryGetValue(kvp.Key, out var originalSize)) continue;

            float baseScaleX = 2.0f / originalSize.Width;
            float baseScaleY = 2.0f / originalSize.Height;

            foreach (var dc in kvp.Value)
            {
                float scale = Math.Max(dc.Size * baseScaleX, dc.Size * baseScaleY);
                float drawWidth = originalSize.Width * scale;
                float drawHeight = originalSize.Height * scale;

                var dstRect = new SKRect(
                    dc.CenterX - drawWidth * 0.5f, dc.CenterY - drawHeight * 0.5f,
                    dc.CenterX + drawWidth * 0.5f, dc.CenterY + drawHeight * 0.5f);

                canvas.DrawImage(_textureAtlas, srcRect, dstRect, paint);
            }
        }
    }

    public void Dispose()
    {
        lock (_atlasLock)
        {
            _textureAtlas?.Dispose();
            _textureAtlas = null;
            _terrainAtlasMap.Clear();
            _terrainOriginalSize.Clear();
        }
        _terrainHelper.ClearAllCaches();
        _stateLock.Dispose();
    }
}
