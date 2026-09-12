using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class TrapRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 32;
    private const double TRAP_IMAGE_SIZE_RATIO = 0.6;
    private const double FLAG_SIZE_RATIO = 0.5;
    private const double BASE_RADIUS = 15.0;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showTraps = true;
    private bool _initialized;

    private SKImage? _landTrapImage;
    private SKImage? _seaTrapImage;
    private readonly Dictionary<string, SKImage> _flagImageCache = new();

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<(SKRect SpriteRect, SKRotationScaleMatrix Transform, SKImage Image)> _flagDrawList = new();

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

    private SKImage GetFlagImage(int countryId, int targetSize)
    {
        string cacheKey = $"{countryId}_{targetSize}";
        if (_flagImageCache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            SKImage? flagImage = null;

            var tacticalCache = TacticalMapImageCache.Instance;
            if (tacticalCache.IsInitialized)
            {
                var tacticalImage = tacticalCache.GetImage($"flag_{countryId}.png");
                if (tacticalImage != null)
                    flagImage = ResizeImage(tacticalImage, targetSize, targetSize);
            }

            if (flagImage == null)
            {
                string basePath = Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
                string flagPath = Path.Combine(basePath, $"flag_{countryId}.png");
                if (!File.Exists(flagPath) && (countryId == 0 || countryId == 255))
                    flagPath = Path.Combine(basePath, "flag_1.png");

                if (File.Exists(flagPath))
                {
                    using var stream = File.OpenRead(flagPath);
                    using var original = SKImage.FromEncodedData(stream);
                    if (original != null)
                        flagImage = ResizeImage(original, targetSize, targetSize);
                }
            }

            flagImage ??= CreateDefaultFlagImage(targetSize);

            _flagImageCache[cacheKey] = flagImage;
            return flagImage;
        }
        catch
        {
            return CreateDefaultFlagImage(targetSize);
        }
    }

    private static SKImage ResizeImage(SKImage source, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        var srcRect = new SKRect(0, 0, source.Width, source.Height);
        var dstRect = new SKRect(0, 0, width, height);
        canvas.DrawImage(source, srcRect, dstRect, paint);
        return surface.Snapshot();
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

    private static List<SKPoint> GetTrapPositions(int level, float centerX, float centerY, float zoom)
    {
        var positions = new List<SKPoint>();
        float radius = (float)(BASE_RADIUS * zoom);
        int normalizedLevel = Math.Max(1, Math.Min(5, level));

        switch (normalizedLevel)
        {
            case 1:
                positions.Add(new SKPoint(centerX, centerY));
                break;
            case 2:
                positions.Add(new SKPoint(centerX - radius, centerY));
                positions.Add(new SKPoint(centerX + radius, centerY));
                break;
            case 3:
                positions.Add(new SKPoint(centerX, centerY - radius));
                positions.Add(new SKPoint(centerX - radius * 0.866f, centerY + radius * 0.5f));
                positions.Add(new SKPoint(centerX + radius * 0.866f, centerY + radius * 0.5f));
                break;
            case 4:
                positions.Add(new SKPoint(centerX - radius * 0.707f, centerY - radius * 0.707f));
                positions.Add(new SKPoint(centerX + radius * 0.707f, centerY - radius * 0.707f));
                positions.Add(new SKPoint(centerX - radius * 0.707f, centerY + radius * 0.707f));
                positions.Add(new SKPoint(centerX + radius * 0.707f, centerY + radius * 0.707f));
                break;
            case 5:
                positions.Add(new SKPoint(centerX, centerY));
                positions.Add(new SKPoint(centerX - radius * 0.707f, centerY - radius * 0.707f));
                positions.Add(new SKPoint(centerX + radius * 0.707f, centerY - radius * 0.707f));
                positions.Add(new SKPoint(centerX - radius * 0.707f, centerY + radius * 0.707f));
                positions.Add(new SKPoint(centerX + radius * 0.707f, centerY + radius * 0.707f));
                break;
        }

        return positions;
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
            _spriteRects.Clear();
            _transforms.Clear();
            _flagDrawList.Clear();

            float zoom = (float)_camera.ZoomLevel;
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float trapSize = (float)(hexHeight * TRAP_IMAGE_SIZE_RATIO);
            float flagSize = (float)(hexHeight * FLAG_SIZE_RATIO);

            var visibleBounds = CalculateVisibleBounds();

            foreach (var trap in _mapData.Traps)
            {
                if (trap.Coordinate < 0 || trap.Coordinate >= _mapData.MapWidth * _mapData.MapHeight)
                    continue;

                var screenPos = CalculateScreenPosition(trap.Coordinate);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                bool isSea = IsSeaHex(trap.Coordinate);
                var trapImage = (isSea && _seaTrapImage != null) ? _seaTrapImage : _landTrapImage;
                if (trapImage == null) continue;

                var positions = GetTrapPositions(trap.Organization, screenPos.Value.X, screenPos.Value.Y, zoom);

                foreach (var pos in positions)
                {
                    _spriteRects.Add(new SKRect(0, 0, trapImage.Width, trapImage.Height));
                    float scaleX = trapSize / trapImage.Width;
                    float scaleY = trapSize / trapImage.Height;
                    float tx = pos.X - trapSize / 2;
                    float ty = pos.Y - trapSize / 2;
                    _transforms.Add(new SKRotationScaleMatrix(scaleX, 0, tx, ty));
                }

                float flagOffsetY = -(float)hexHeight * 0.4f;
                float flagX = screenPos.Value.X;
                float flagY = screenPos.Value.Y + flagOffsetY;

                int countryId = GetCountryIdFromLegionId(trap.LegionId);
                var flagImage = GetFlagImage(countryId, (int)flagSize);

                if (flagImage != null)
                {
                    float flagScale = flagSize / flagImage.Width;
                    float flagTx = flagX - flagSize / 2;
                    float flagTy = flagY - flagSize / 2;
                    _flagDrawList.Add((
                        new SKRect(0, 0, flagImage.Width, flagImage.Height),
                        new SKRotationScaleMatrix(flagScale, 0, flagTx, flagTy),
                        flagImage
                    ));
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
        if (_transforms.Count == 0) return;

        lock (_atlasLock)
        {
            var trapImage = _landTrapImage ?? _seaTrapImage;
            if (trapImage == null) return;

            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawAtlas(trapImage, _spriteRects.ToArray(), _transforms.ToArray(), paint);
            _spriteRects.Clear();
            _transforms.Clear();
        }
    }

    private void DrawFlagsBatch(SKCanvas canvas)
    {
        if (_flagDrawList.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var flag in _flagDrawList)
        {
            canvas.DrawAtlas(flag.Image, new[] { flag.SpriteRect }, new[] { flag.Transform }, paint);
        }

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

    private bool IsSeaHex(int hexIndex)
    {
        try
        {
            var terrain = _mapData.GetTerrain(hexIndex);
            return terrain.TileType1 == 1;
        }
        catch { return false; }
    }

    private int GetCountryIdFromLegionId(int legionId)
    {
        if (_mapData?.Legions != null)
        {
            foreach (var legion in _mapData.Legions)
            {
                if (legion.ActionId == legionId)
                    return legion.CountryId;
            }
        }
        return 0;
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

            foreach (var image in _flagImageCache.Values)
                image?.Dispose();
            _flagImageCache.Clear();
        }
    }
}