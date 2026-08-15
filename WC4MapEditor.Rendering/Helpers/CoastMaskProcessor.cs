using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using WC4MapEditor.Models;

namespace WC4MapEditor.Rendering.Helpers;

public sealed class CoastMaskProcessor
{
    private static CoastMaskProcessor? _instance;
    private static readonly object _instanceLock = new();

    private readonly Dictionary<string, (SKBitmap CoastMask, SKBitmap LandMask)> _maskCache = new();
    private readonly object _cacheLock = new();

    private SKImage? _landTexture;
    private SKImage? _hexagonLandTexture;

    private SKImage? _finalCoastAtlas;
    private readonly Dictionary<string, SKRect> _finalAtlasSpriteRects = new();
    private readonly Dictionary<string, SKSize> _finalAtlasSpriteSizes = new();
    private readonly Dictionary<string, SKPoint> _finalAtlasSpriteOrigins = new();
    private bool _finalAtlasBuilt;
    private SKImage? _finalAtlasLandTextureRef;

    private bool _initialized;

    public bool IsInitialized => _initialized;
    public bool HasFinalAtlas => _finalAtlasBuilt && _finalCoastAtlas != null;

    public static CoastMaskProcessor Instance
    {
        get
        {
            lock (_instanceLock)
            {
                _instance ??= new CoastMaskProcessor();
            }
            return _instance;
        }
    }

    private CoastMaskProcessor() { }

    public void Initialize(CoastHelper coastHelper)
    {
        Initialize(coastHelper, null);
    }

    public void Initialize(CoastHelper coastHelper, Action<int, string>? progressCallback)
    {
        if (coastHelper == null) return;
        if (_initialized) { progressCallback?.Invoke(100, "海岸线遮罩已缓存"); return; }

        Debug.WriteLine("[CoastMaskProcessor] 开始初始化遮罩缓存...");
        var sw = Stopwatch.StartNew();
        var processedCount = 0;

        var spriteNames = coastHelper.GetGrayLevelSpriteNames();
        var totalCount = spriteNames.Count;

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"加工海岸线遮罩... ({i + 1}/{totalCount})");

                var maskImage = coastHelper.GetGrayLevelCoastImage(spriteName);
                if (maskImage != null)
                {
                    var masks = CreateMasks(maskImage);
                    if (masks.HasValue)
                    {
                        lock (_cacheLock)
                            _maskCache[spriteName] = masks.Value;
                        processedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 处理遮罩 {spriteName} 失败: {ex.Message}");
            }
        }

        _initialized = true;
        sw.Stop();
        progressCallback?.Invoke(100, $"海岸线遮罩完成 ({processedCount}/{totalCount})");
        Debug.WriteLine($"[CoastMaskProcessor] 遮罩缓存初始化完成: {processedCount} 个，耗时 {sw.ElapsedMilliseconds}ms");
    }

    public void DrawMaskedCoast(SKCanvas canvas, SKImage coastAtlasImage, SKRect coastSrcRect,
        string spriteName, SKRect dstRect, SKPaint paint, double zoomLevel = 1.0)
    {
        if (canvas == null || coastAtlasImage == null || string.IsNullOrEmpty(spriteName)) return;
        if (zoomLevel < 0.25) return;

        SKBitmap? coastMask = null, landMask = null;
        lock (_cacheLock)
        {
            if (_maskCache.TryGetValue(spriteName, out var masks))
            {
                coastMask = masks.CoastMask;
                landMask = masks.LandMask;
            }
        }

        if (coastMask == null)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        RenderWithMasks(canvas, coastAtlasImage, coastSrcRect, coastMask, landMask, dstRect, paint);
    }

    public void DrawFinalCoast(SKCanvas canvas, string spriteName, SKRect dstRect, SKPaint paint, double zoomLevel = 1.0)
    {
        if (canvas == null || string.IsNullOrEmpty(spriteName)) return;
        if (!_finalAtlasBuilt || _finalCoastAtlas == null) return;
        if (zoomLevel < 0.25) return;

        if (!_finalAtlasSpriteRects.TryGetValue(spriteName, out var srcRect)) return;
        canvas.DrawImage(_finalCoastAtlas, srcRect, dstRect, paint);
    }

    private void RenderWithMasks(SKCanvas canvas, SKImage coastAtlasImage, SKRect coastSrcRect,
        SKBitmap coastMask, SKBitmap landMask, SKRect dstRect, SKPaint paint)
    {
        var width = (int)dstRect.Width;
        var height = (int)dstRect.Height;
        if (width <= 0 || height <= 0) return;
        if (width > 4096 || height > 4096)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        var info = new SKImageInfo(width, height);
        using var tempSurface = SKSurface.Create(info);
        if (tempSurface == null)
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        var tempCanvas = tempSurface.Canvas;
        tempCanvas.Clear(SKColors.Transparent);

        var landTextureToUse = _hexagonLandTexture ?? _landTexture;
        if (landMask != null && landTextureToUse != null)
        {
            try
            {
                using var landTempSurface = SKSurface.Create(info);
                if (landTempSurface != null)
                {
                    var landTempCanvas = landTempSurface.Canvas;
                    landTempCanvas.Clear(SKColors.Transparent);

                    using (var maskPaint = new SKPaint { IsAntialias = true })
                        landTempCanvas.DrawBitmap(landMask, new SKRect(0, 0, width, height), maskPaint);

                    using (var srcInPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.SrcIn })
                        landTempCanvas.DrawImage(landTextureToUse, new SKRect(0, 0, landTextureToUse.Width, landTextureToUse.Height),
                            new SKRect(0, 0, width, height), srcInPaint);

                    using var landSnapshot = landTempSurface.Snapshot();
                    if (landSnapshot != null)
                        using (var resultPaint = new SKPaint { IsAntialias = true })
                            tempCanvas.DrawImage(landSnapshot, 0, 0, resultPaint);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 平地纹理渲染失败: {ex.Message}");
            }
        }

        try
        {
            using var coastTempSurface = SKSurface.Create(info);
            if (coastTempSurface != null)
            {
                var coastTempCanvas = coastTempSurface.Canvas;
                coastTempCanvas.Clear(SKColors.Transparent);

                using (var maskPaint = new SKPaint { IsAntialias = true })
                    coastTempCanvas.DrawBitmap(coastMask, new SKRect(0, 0, width, height), maskPaint);

                using (var srcInPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.SrcIn })
                    coastTempCanvas.DrawImage(coastAtlasImage, coastSrcRect, new SKRect(0, 0, width, height), srcInPaint);

                using var coastSnapshot = coastTempSurface.Snapshot();
                if (coastSnapshot != null)
                    using (var plusPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
                        tempCanvas.DrawImage(coastSnapshot, 0, 0, plusPaint);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 海岸线渲染失败: {ex.Message}");
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
            return;
        }

        try
        {
            using var snapshot = tempSurface.Snapshot();
            if (snapshot != null)
                using (var finalPaint = new SKPaint { IsAntialias = true })
                    canvas.DrawImage(snapshot, dstRect, finalPaint);
        }
        catch
        {
            canvas.DrawImage(coastAtlasImage, coastSrcRect, dstRect, paint);
        }
    }

    private (SKBitmap CoastMask, SKBitmap LandMask)? CreateMasks(SKImage maskImage)
    {
        const int GREEN_THRESHOLD = 32;
        var width = maskImage.Width;
        var height = maskImage.Height;
        var coastMask = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var landMask = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        try
        {
            using var tempBitmap = SKBitmap.FromImage(maskImage);
            var ptr = tempBitmap.GetPixels();
            if (ptr != IntPtr.Zero)
            {
                var rowBytes = tempBitmap.RowBytes;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var offset = y * rowBytes + x * 4;
                        var b = Marshal.ReadByte(ptr, offset);
                        var g = Marshal.ReadByte(ptr, offset + 1);
                        var r = Marshal.ReadByte(ptr, offset + 2);

                        byte coastAlpha = (byte)(g <= GREEN_THRESHOLD ? r : 0);
                        coastMask.SetPixel(x, y, new SKColor(255, 255, 255, coastAlpha));

                        byte landAlpha = (byte)(g <= GREEN_THRESHOLD ? 255 - r : 0);
                        landMask.SetPixel(x, y, new SKColor(255, 255, 255, landAlpha));
                    }
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = tempBitmap.GetPixel(x, y);
                        var r = pixel.Red;
                        var g = pixel.Green;

                        byte coastAlpha = (byte)(g <= GREEN_THRESHOLD ? r : 0);
                        coastMask.SetPixel(x, y, new SKColor(255, 255, 255, coastAlpha));

                        byte landAlpha = (byte)(g <= GREEN_THRESHOLD ? 255 - r : 0);
                        landMask.SetPixel(x, y, new SKColor(255, 255, 255, landAlpha));
                    }
                }
            }
            return (coastMask, landMask);
        }
        catch
        {
            coastMask.Dispose();
            landMask.Dispose();
            return null;
        }
    }

    public void SetLandTexture(SKImage landTexture)
    {
        if (ReferenceEquals(_landTexture, landTexture)) return;

        if (_hexagonLandTexture != null && !ReferenceEquals(_hexagonLandTexture, _landTexture))
        {
            _hexagonLandTexture.Dispose();
            _hexagonLandTexture = null;
        }

        _landTexture = landTexture;
        _hexagonLandTexture = landTexture != null ? CreateHexagonLandTexture(landTexture) : null;
    }

    private static SKImage? CreateHexagonLandTexture(SKImage landTexture)
    {
        try
        {
            var width = landTexture.Width;
            var height = landTexture.Height;
            float hexSide = width / 2f;
            float hexHeight = hexSide * (float)Math.Sqrt(3);
            if (hexHeight > height) { hexHeight = height; hexSide = hexHeight / (float)Math.Sqrt(3); }
            float verticalMargin = (height - hexHeight) / 2;
            float centerX = width / 2f;
            float centerY = height / 2f;

            var hexPath = new SKPath();
            hexPath.MoveTo(centerX - hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide, centerY);
            hexPath.LineTo(centerX + hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide, centerY);
            hexPath.Close();

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.ClipPath(hexPath);
            surface.Canvas.DrawImage(landTexture, new SKRect(0, 0, width, height));
            return surface.Snapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 创建六边形平地纹理失败: {ex.Message}");
            return landTexture;
        }
    }

    public bool HasMask(string spriteName)
    {
        lock (_cacheLock)
            return _maskCache.ContainsKey(spriteName);
    }

    public void BuildFinalCoastAtlas(CoastHelper coastHelper, Action<int, string>? progressCallback = null)
    {
        if (coastHelper == null || !_initialized || _landTexture == null) return;
        if (_finalAtlasBuilt && ReferenceEquals(_finalAtlasLandTextureRef, _landTexture))
        {
            progressCallback?.Invoke(100, "海岸线最终图集已缓存");
            return;
        }

        Debug.WriteLine("[CoastMaskProcessor] 开始构建最终海岸线合成图集...");
        var sw = Stopwatch.StartNew();

        var spriteNames = coastHelper.GetGrayLevelSpriteNames();
        var totalCount = spriteNames.Count;
        if (totalCount == 0) { progressCallback?.Invoke(100, "没有海岸线精灵"); return; }

        var compositedBitmaps = new Dictionary<string, SKBitmap>();
        var compositedSizes = new Dictionary<string, SKSize>();
        var compositedOrigins = new Dictionary<string, SKPoint>();

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"合成海岸线纹理... ({i + 1}/{totalCount})");

                var result = BuildSingleFinalCoastTexture(coastHelper, spriteName);
                if (result.HasValue)
                {
                    compositedBitmaps[spriteName] = result.Value.Bitmap;
                    compositedSizes[spriteName] = new SKSize(result.Value.Width, result.Value.Height);
                    compositedOrigins[spriteName] = result.Value.Origin;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastMaskProcessor] 合成纹理失败 [{spriteName}]: {ex.Message}");
            }
        }

        if (compositedBitmaps.Count == 0) { progressCallback?.Invoke(100, "合成失败"); return; }

        progressCallback?.Invoke(95, "打包海岸线图集...");
        PackFinalCoastAtlas(compositedBitmaps, compositedSizes, compositedOrigins);

        _finalAtlasLandTextureRef = _landTexture;
        _finalAtlasBuilt = true;
        sw.Stop();
        progressCallback?.Invoke(100, $"海岸线图集完成 ({compositedBitmaps.Count} 个精灵)");
        Debug.WriteLine($"[CoastMaskProcessor] 最终图集构建完成: {compositedBitmaps.Count} 个精灵，耗时 {sw.ElapsedMilliseconds}ms");
    }

    private (SKBitmap Bitmap, int Width, int Height, SKPoint Origin)? BuildSingleFinalCoastTexture(CoastHelper coastHelper, string spriteName)
    {
        var hexCoastImage = coastHelper.GetHexagonCoastImage(spriteName);
        if (hexCoastImage == null) return null;

        lock (_cacheLock)
        {
            if (!_maskCache.TryGetValue(spriteName, out var masks)) return null;
        }

        var width = hexCoastImage.Width;
        var height = hexCoastImage.Height;
        if (width <= 0 || height <= 0) return null;

        var spriteInfo = coastHelper.GetGrayLevelCoastSpriteInfo(spriteName);
        var origin = spriteInfo != null ? new SKPoint(spriteInfo.RefX, spriteInfo.RefY) : new SKPoint(width / 2f, height / 2f);

        var info = new SKImageInfo(width, height);
        using var tempSurface = SKSurface.Create(info);
        if (tempSurface == null) return null;

        var tempCanvas = tempSurface.Canvas;
        tempCanvas.Clear(SKColors.Transparent);

        lock (_cacheLock)
        {
            if (_maskCache.TryGetValue(spriteName, out var masks))
            {
                var landTextureToUse = _hexagonLandTexture ?? _landTexture;
                if (masks.LandMask != null && landTextureToUse != null)
                {
                    try
                    {
                        using var landTempSurface = SKSurface.Create(info);
                        if (landTempSurface != null)
                        {
                            var landTempCanvas = landTempSurface.Canvas;
                            landTempCanvas.Clear(SKColors.Transparent);
                            using (var maskPaint = new SKPaint { IsAntialias = true })
                                landTempCanvas.DrawBitmap(masks.LandMask, new SKRect(0, 0, width, height), maskPaint);
                            using (var srcInPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.SrcIn })
                                landTempCanvas.DrawImage(landTextureToUse, new SKRect(0, 0, landTextureToUse.Width, landTextureToUse.Height),
                                    new SKRect(0, 0, width, height), srcInPaint);
                            using var landSnapshot = landTempSurface.Snapshot();
                            if (landSnapshot != null)
                                using (var resultPaint = new SKPaint { IsAntialias = true })
                                    tempCanvas.DrawImage(landSnapshot, 0, 0, resultPaint);
                        }
                    }
                    catch { }
                }

                if (masks.CoastMask != null)
                {
                    try
                    {
                        using var coastTempSurface = SKSurface.Create(info);
                        if (coastTempSurface != null)
                        {
                            var coastTempCanvas = coastTempSurface.Canvas;
                            coastTempCanvas.Clear(SKColors.Transparent);
                            using (var maskPaint = new SKPaint { IsAntialias = true })
                                coastTempCanvas.DrawBitmap(masks.CoastMask, new SKRect(0, 0, width, height), maskPaint);
                            using (var srcInPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.SrcIn })
                                coastTempCanvas.DrawImage(hexCoastImage, new SKRect(0, 0, width, height), srcInPaint);
                            using var coastSnapshot = coastTempSurface.Snapshot();
                            if (coastSnapshot != null)
                                using (var plusPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
                                    tempCanvas.DrawImage(coastSnapshot, 0, 0, plusPaint);
                        }
                    }
                    catch { }
                }
            }
        }

        using var snapshot = tempSurface.Snapshot();
        if (snapshot == null) return null;
        var bitmap = SKBitmap.FromImage(snapshot);
        return bitmap != null ? (bitmap, width, height, origin) : null;
    }

    private void PackFinalCoastAtlas(Dictionary<string, SKBitmap> compositedBitmaps,
        Dictionary<string, SKSize> compositedSizes, Dictionary<string, SKPoint> compositedOrigins)
    {
        if (compositedBitmaps.Count == 0) return;

        _finalCoastAtlas?.Dispose();
        _finalCoastAtlas = null;
        _finalAtlasSpriteRects.Clear();
        _finalAtlasSpriteSizes.Clear();
        _finalAtlasSpriteOrigins.Clear();

        var sortedSprites = compositedBitmaps.Keys.OrderByDescending(n => compositedSizes[n].Height).ToList();

        int totalArea = 0, maxWidth = 0;
        foreach (var kvp in compositedBitmaps)
        {
            totalArea += (int)(kvp.Value.Width * kvp.Value.Height);
            if (kvp.Value.Width > maxWidth) maxWidth = kvp.Value.Width;
        }

        int atlasSize = 1024;
        while (atlasSize * atlasSize < totalArea * 1.2) atlasSize *= 2;
        if (atlasSize > 8192) atlasSize = 8192;

        int currentX = 0, currentY = 0, rowHeight = 0;
        const int padding = 2;

        foreach (var spriteName in sortedSprites)
        {
            var bitmap = compositedBitmaps[spriteName];
            var spriteWidth = bitmap.Width;
            var spriteHeight = bitmap.Height;

            if (currentX + spriteWidth > atlasSize)
            {
                currentX = 0;
                currentY += rowHeight + padding;
                rowHeight = 0;
            }

            while (currentY + spriteHeight > atlasSize)
            {
                var newSize = atlasSize * 2;
                if (newSize > 8192) break;
                atlasSize = newSize;
            }

            _finalAtlasSpriteRects[spriteName] = new SKRect(currentX, currentY, currentX + spriteWidth, currentY + spriteHeight);
            _finalAtlasSpriteSizes[spriteName] = compositedSizes[spriteName];
            _finalAtlasSpriteOrigins[spriteName] = compositedOrigins[spriteName];

            if (spriteHeight > rowHeight) rowHeight = spriteHeight;
            currentX += spriteWidth + padding;
        }

        try
        {
            using var atlasSurface = SKSurface.Create(new SKImageInfo(atlasSize, atlasSize));
            if (atlasSurface == null) { _finalAtlasSpriteRects.Clear(); return; }

            atlasSurface.Canvas.Clear(SKColors.Transparent);
            foreach (var kvp in _finalAtlasSpriteRects)
            {
                var name = kvp.Key;
                var rect = kvp.Value;
                if (compositedBitmaps.TryGetValue(name, out var bmp))
                    atlasSurface.Canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height), rect);
            }
            _finalCoastAtlas = atlasSurface.Snapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastMaskProcessor] 图集打包失败: {ex.Message}");
            _finalAtlasSpriteRects.Clear();
            _finalCoastAtlas = null;
        }

        foreach (var kvp in compositedBitmaps)
            kvp.Value.Dispose();
        compositedBitmaps.Clear();
    }

    public static List<(int, int)> GetHexNeighbors(int col, int row, int mapWidth, int mapHeight)
    {
        var neighbors = new List<(int, int)>();
        bool isEven = col % 2 == 0;

        int[] dcol, drow;
        if (isEven) { dcol = [0, 1, 1, 0, -1, -1]; drow = [-1, -1, 0, 1, 0, -1]; }
        else { dcol = [0, 1, 1, 0, -1, -1]; drow = [-1, 0, 1, 1, 1, 0]; }

        for (int i = 0; i < 6; i++)
        {
            int nc = col + dcol[i];
            int nr = row + drow[i];
            if (nc < 0) nc = mapWidth - 1;
            else if (nc >= mapWidth) nc = 0;
            if (nr >= 0 && nr < mapHeight)
                neighbors.Add((nc, nr));
        }
        return neighbors;
    }

    public static byte CalculateCoastDecorationType(MapData mapData, int col, int row)
    {
        const int OCEAN_TILE_TYPE = 1;
        var nonOceanNeighbors = GetNonOceanNeighbors(mapData, col, row, OCEAN_TILE_TYPE);

        return nonOceanNeighbors.Count switch
        {
            0 => 10,
            1 => GetDecorationForSingleNeighbor(nonOceanNeighbors[0].Direction),
            2 => GetDecorationForTwoNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction),
            3 => GetDecorationForThreeNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction),
            4 => GetDecorationForFourNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction),
            5 => GetDecorationForFiveNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction, nonOceanNeighbors[4].Direction),
            6 => 11,
            _ => 10
        };
    }

    private struct NeighborInfo { public int Col; public int Row; public string Direction; }

    private static List<NeighborInfo> GetNonOceanNeighbors(MapData mapData, int col, int row, int oceanType)
    {
        var result = new List<NeighborInfo>();
        bool isEven = col % 2 == 0;

        int[,] evenOffsets = { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 }, { -1, -1 } };
        int[,] oddOffsets = { { 0, -1 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 } };
        var offsets = isEven ? evenOffsets : oddOffsets;
        string[] directions = ["上方", "右上方", "右下方", "下方", "左下方", "左上方"];

        for (int i = 0; i < 6; i++)
        {
            int nc = col + offsets[i, 0];
            int nr = row + offsets[i, 1];
            if (nc < 0) nc = mapData.MapWidth - 1;
            else if (nc >= mapData.MapWidth) nc = 0;
            if (nr < 0 || nr >= mapData.MapHeight) continue;

            var terrain = mapData.GetTerrainAt(nc, nr);
            if (terrain.TileType1 != oceanType)
                result.Add(new NeighborInfo { Col = nc, Row = nr, Direction = directions[i] });
        }
        return result;
    }

    private static byte GetDecorationForSingleNeighbor(string direction) => direction switch
    {
        "上方" => 73, "右上方" => 72, "右下方" => 70, "下方" => 66, "左下方" => 58, "左上方" => 42, _ => 10
    };

    private static byte GetDecorationForTwoNeighbors(string dir1, string dir2)
    {
        var dirs = new HashSet<string> { dir1, dir2 };
        if (dirs.Contains("上方") && dirs.Contains("右上方")) return 71;
        if (dirs.Contains("上方") && dirs.Contains("右下方")) return 69;
        if (dirs.Contains("上方") && dirs.Contains("下方")) return 65;
        if (dirs.Contains("上方") && dirs.Contains("左下方")) return 57;
        if (dirs.Contains("上方") && dirs.Contains("左上方")) return 41;
        if (dirs.Contains("右上方") && dirs.Contains("左上方")) return 40;
        if (dirs.Contains("右上方") && dirs.Contains("右下方")) return 68;
        if (dirs.Contains("右上方") && dirs.Contains("下方")) return 64;
        if (dirs.Contains("右上方") && dirs.Contains("左下方")) return 56;
        if (dirs.Contains("右下方") && dirs.Contains("下方")) return 62;
        if (dirs.Contains("右下方") && dirs.Contains("左下方")) return 54;
        if (dirs.Contains("右下方") && dirs.Contains("左上方")) return 38;
        if (dirs.Contains("下方") && dirs.Contains("左下方")) return 50;
        if (dirs.Contains("下方") && dirs.Contains("左上方")) return 34;
        if (dirs.Contains("左下方") && dirs.Contains("左上方")) return 26;
        return 10;
    }

    private static byte GetDecorationForThreeNeighbors(string dir1, string dir2, string dir3)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3 };
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方")) return 67;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方")) return 63;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左下方")) return 55;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左上方")) return 39;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 61;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 53;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 37;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 49;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 33;
        if (dirs.Contains("上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 25;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 60;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 52;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 36;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 48;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 32;
        if (dirs.Contains("右上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 24;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 46;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 30;
        if (dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 22;
        if (dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 18;
        return 10;
    }

    private static byte GetDecorationForFourNeighbors(string dir1, string dir2, string dir3, string dir4)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3, dir4 };
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方")) return 59;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方")) return 51;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左上方")) return 35;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 47;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 31;
        if (dirs.Contains("上方") && dirs.Contains("右上方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 23;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 45;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 29;
        if (dirs.Contains("上方") && dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 21;
        if (dirs.Contains("上方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 17;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方")) return 44;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左上方")) return 28;
        if (dirs.Contains("右上方") && dirs.Contains("右下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 20;
        if (dirs.Contains("右上方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 16;
        if (dirs.Contains("右下方") && dirs.Contains("下方") && dirs.Contains("左下方") && dirs.Contains("左上方")) return 14;
        return 10;
    }

    private static byte GetDecorationForFiveNeighbors(string dir1, string dir2, string dir3, string dir4, string dir5)
    {
        var dirs = new HashSet<string> { dir1, dir2, dir3, dir4, dir5 };
        if (!dirs.Contains("上方")) return 12;
        if (!dirs.Contains("右上方")) return 13;
        if (!dirs.Contains("右下方")) return 15;
        if (!dirs.Contains("下方")) return 19;
        if (!dirs.Contains("左下方")) return 27;
        if (!dirs.Contains("左上方")) return 43;
        return 10;
    }

    public void DisposeAll()
    {
        lock (_cacheLock)
        {
            foreach (var masks in _maskCache.Values)
            {
                masks.CoastMask?.Dispose();
                masks.LandMask?.Dispose();
            }
            _maskCache.Clear();
        }

        _finalCoastAtlas?.Dispose();
        _finalCoastAtlas = null;
        _finalAtlasSpriteRects.Clear();
        _finalAtlasSpriteSizes.Clear();
        _finalAtlasSpriteOrigins.Clear();
        _finalAtlasBuilt = false;
        _finalAtlasLandTextureRef = null;

        if (_hexagonLandTexture != null && !ReferenceEquals(_hexagonLandTexture, _landTexture))
            _hexagonLandTexture.Dispose();
        _hexagonLandTexture = null;
        _landTexture = null;
        _initialized = false;
    }
}
