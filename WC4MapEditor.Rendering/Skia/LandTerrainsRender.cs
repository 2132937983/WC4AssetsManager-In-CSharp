using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Rendering.Helpers;

namespace WC4MapEditor.Rendering.Skia;

public class LandTerrainsRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly TerrainHelper _terrainHelper;
    private readonly ReaderWriterLockSlim _stateLock = new();

    private double _offsetX = 50.0;
    private double _offsetY = 50.0;
    private double _zoomLevel = 1.0;
    private int _viewportWidth = 800;
    private int _viewportHeight = 600;
    private bool _showLayer2;

    private readonly Dictionary<long, SKBitmap> _terrainSource = new();
    private readonly Dictionary<long, SKSize> _terrainSourceSize = new();
    private readonly object _sourceLock = new();
    private readonly object _terrainHelperLock = new();

    private struct TerrainDrawCall
    {
        public float CenterX;
        public float CenterY;
        public float Size;
        public long CacheKey;
    }

    // Reused across frames to avoid per-frame allocations.
    private readonly List<TerrainDrawCall> _batch = new();
    private readonly Dictionary<long, List<TerrainDrawCall>> _groups = new();
    private readonly List<long> _groupKeys = new();
    private readonly Stack<List<TerrainDrawCall>> _groupPool = new();

    // Optional render clip (strip) in surface coordinates; empty = full area.
    private int _clipX, _clipY, _clipW, _clipH;
    private bool _hasClip;

    private static long MakeKey(int terrainType, int decorationIndex) => ((long)terrainType << 32) | (uint)decorationIndex;

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

    // Batch state update: one write lock instead of six.
    public void SetCameraState(double offsetX, double offsetY, double zoomLevel, int viewportWidth, int viewportHeight, bool showLayer2)
    {
        _stateLock.EnterWriteLock();
        try
        {
            _offsetX = offsetX;
            _offsetY = offsetY;
            _zoomLevel = Math.Max(0.1, Math.Min(5.0, zoomLevel));
            _viewportWidth = Math.Max(1, viewportWidth);
            _viewportHeight = Math.Max(1, viewportHeight);
            _showLayer2 = showLayer2;
        }
        finally { _stateLock.ExitWriteLock(); }
    }

    public void SetClipRect(int x, int y, int width, int height)
    {
        _clipX = x; _clipY = y; _clipW = width; _clipH = height;
        _hasClip = width > 0 && height > 0;
    }

    public void ClearClipRect() => _hasClip = false;

    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    public LandTerrainsRender()
    {
        _terrainHelper = new TerrainHelper("MapTerrian");
    }

    public void Render(SKCanvas canvas, MapData mapData)
    {
        if (mapData == null) return;

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

        // Iteration bounds: the clip (strip) rect when set, otherwise the
        // whole viewport area.
        double boundsX = _hasClip ? _clipX : 0;
        double boundsY = _hasClip ? _clipY : 0;
        double boundsW = _hasClip ? _clipW : vpW;
        double boundsH = _hasClip ? _clipH : vpH;

        int padding = 2;
        int startCol = Math.Max(0, (int)((boundsX - offsetX) / hexSpacingX) - padding);
        int startRow = Math.Max(0, (int)((boundsY - offsetY - hexSpacingY) / hexSpacingY) - padding);
        int endCol = Math.Min(mapData.MapWidth - 1, (int)((boundsX + boundsW - offsetX) / hexSpacingX) + padding);
        int endRow = Math.Min(mapData.MapHeight - 1, (int)((boundsY + boundsH - offsetY + hexSpacingY) / hexSpacingY) + padding);

        _batch.Clear();
        float fBoundsX = (float)boundsX, fBoundsY = (float)boundsY;
        float fBoundsR = (float)(boundsX + boundsW), fBoundsB = (float)(boundsY + boundsH);

        for (int col = startCol; col <= endCol; col++)
        {
            double centerX = offsetX + col * hexSpacingX;
            float fCenterX = (float)centerX;
            if (fCenterX + hexSize < fBoundsX || fCenterX - hexSize > fBoundsR) continue;
            double centerYBase = offsetY + (col % 2) * (hexSpacingY / 2);

            for (int row = startRow; row <= endRow; row++)
            {
                double centerY = centerYBase + row * hexSpacingY;
                float fCenterY = (float)centerY;
                if (fCenterY + hexSize < fBoundsY || fCenterY - hexSize > fBoundsB) continue;

                var terrain = mapData.GetTerrainAt(col, row);

                if (terrain.TileType1 != 0 && terrain.TileType1 != 1)
                {
                    int variantCount1 = _terrainHelper.GetTerrainVariantCount(terrain.TileType1);
                    int decorationIndex1 = terrain.DecorationType1 % variantCount1;
                    long cacheKey = MakeKey(terrain.TileType1, decorationIndex1);
                    if (EnsureTerrainSource(cacheKey, terrain.TileType1, decorationIndex1))
                        _batch.Add(new TerrainDrawCall { CenterX = fCenterX, CenterY = fCenterY, Size = hexSize, CacheKey = cacheKey });
                }

                if (showLayer2 && terrain.TileType2 != 0 && terrain.TileType2 != 63 && terrain.TileType2 != 255)
                {
                    int variantCount2 = _terrainHelper.GetTerrainVariantCount(terrain.TileType2);
                    int decorationIndex2 = terrain.DecorationType2 % variantCount2;
                    long cacheKey = MakeKey(terrain.TileType2, decorationIndex2);
                    if (EnsureTerrainSource(cacheKey, terrain.TileType2, decorationIndex2))
                        _batch.Add(new TerrainDrawCall { CenterX = fCenterX, CenterY = fCenterY, Size = hexSize, CacheKey = cacheKey });
                }
            }
        }

        if (_batch.Count > 0)
            DrawTerrainFromImages(canvas);
    }

    private bool EnsureTerrainSource(long cacheKey, int terrainType, int decorationIndex)
    {
        // 快速路径不加锁：条目一旦写入就不会被移除，且渲染与 Dispose 都发生在 UI 线程。
        // 原实现为可视范围内的每一格都取一次锁，在逐格循环里开销显著。
        if (_terrainSource.ContainsKey(cacheKey)) return true;

        SKBitmap? owned = null;
        lock (_terrainHelperLock)
        {
            var image = _terrainHelper.GetTerrainSkImage(terrainType, decorationIndex);
            if (image == null) return false;

            try
            {
                using var encoded = image.Encode();
                if (encoded == null) return false;
                owned = SKBitmap.Decode(encoded);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LandTerrainsRender] 复制纹理失败: {ex.Message}");
                return false;
            }
        }

        if (owned == null) return false;

        lock (_sourceLock)
        {
            if (!_terrainSource.ContainsKey(cacheKey))
            {
                _terrainSource[cacheKey] = owned;
                _terrainSourceSize[cacheKey] = new SKSize(owned.Width, owned.Height);
            }
            else
            {
                owned.Dispose();
            }
        }
        return true;
    }

    private void DrawTerrainFromImages(SKCanvas canvas)
    {
        if (_batch.Count == 0) return;

        _groupKeys.Clear();
        // 分组容器回收到池中复用，避免每帧为每种地形类型重新分配 List。
        foreach (var g in _groups.Values)
        {
            g.Clear();
            _groupPool.Push(g);
        }
        _groups.Clear();

        foreach (var dc in _batch)
        {
            if (!_groups.TryGetValue(dc.CacheKey, out var list))
            {
                list = _groupPool.Count > 0 ? _groupPool.Pop() : new List<TerrainDrawCall>();
                _groups[dc.CacheKey] = list;
                _groupKeys.Add(dc.CacheKey);
            }
            list.Add(dc);
        }

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var key in _groupKeys)
        {
            SKBitmap? image;
            SKSize size;
            lock (_sourceLock)
            {
                if (!_terrainSource.TryGetValue(key, out image) || image == null) continue;
                size = _terrainSourceSize[key];
            }

            float baseScaleX = 2.0f / size.Width;
            float baseScaleY = 2.0f / size.Height;
            var srcRect = new SKRect(0, 0, size.Width, size.Height);

            foreach (var dc in _groups[key])
            {
                float scale = Math.Max(dc.Size * baseScaleX, dc.Size * baseScaleY);
                float drawWidth = size.Width * scale;
                float drawHeight = size.Height * scale;

                var dstRect = new SKRect(
                    dc.CenterX - drawWidth * 0.5f, dc.CenterY - drawHeight * 0.5f,
                    dc.CenterX + drawWidth * 0.5f, dc.CenterY + drawHeight * 0.5f);

                canvas.DrawBitmap(image, srcRect, dstRect, paint);
            }
        }
    }

    public void Dispose()
    {
        lock (_sourceLock)
        {
            foreach (var img in _terrainSource.Values)
                img.Dispose();
            _terrainSource.Clear();
            _terrainSourceSize.Clear();
        }

        _terrainHelper.ClearAllCaches();
        _stateLock.Dispose();
    }
}