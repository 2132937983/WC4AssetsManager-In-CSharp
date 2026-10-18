using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class ArmyRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 64;
    private const int MAX_ATLAS_DRAW_COUNT = 20000;
    private const double UNIT_IMAGE_SIZE_RATIO = 0.9;
    private const float GENERAL_HEAD_SIZE_RATIO = 0.6f;
    private const double KEY_POINT_SIZE_RATIO = 1.5;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);
    private static readonly SKColor KEY_POINT_COLOR_1 = new(255, 0, 0, 128);
    private static readonly SKColor KEY_POINT_COLOR_2 = new(0, 255, 0, 128);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showUnits = true;
    private bool _atlasInitialized;

    /// <summary>共享单位图集，由 <see cref="UnitAtlasCache"/> 持有所有权，本类不得 Dispose。</summary>
    private SKImage? _unitAtlas;
    /// <summary>共享切片表，指向 <see cref="UnitAtlasCache"/> 中的实例，只读使用。</summary>
    private Dictionary<int, SKRect> _unitAtlasMap = new();

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<SKRect> _flippedSpriteRects = new();
    private readonly List<SKPoint> _flippedPositions = new();
    private readonly List<float> _flippedSizes = new();
    private readonly List<(SKPoint Position, float Size, SKColor Color)> _keyPointPositions = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image, bool IsDefault)> _generalHeadPositions = new();
    private readonly List<(SKPoint Position, float Size, int Level)> _levelIconPositions = new();
    private readonly List<(SKPoint BoardPos, SKSize BoardSize, int FormationCount, int Plan)> _formationInfos = new();

    private readonly Dictionary<int, SKImage> _levelIconCache = new();
    private SKImage? _formationBoardCache;
    private SKImage? _formationIconCache;
    private readonly Dictionary<int, SKImage> _actionPlanCache = new();

    private readonly SKFont _levelFont;
    private readonly SKPaint _levelTextPaint;
    private readonly SKPaint _generalDefaultPaint;
    private readonly SKPaint _generalBorderPaint;

    public bool ShowUnits
    {
        get => _showUnits;
        set => _showUnits = value;
    }

    public bool IsV3 { get; set; }

    public ArmyRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _levelFont = new SKFont(SKTypeface.Default, 10);
        _levelTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _generalDefaultPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill };
        _generalBorderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(218, 165, 32), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

        InitializeUnitAtlas();
        InitializeLevelAndFormationImages();
    }

    #region 纹理图集管理

    private void InitializeUnitAtlas()
    {
        if (_atlasInitialized) return;

        lock (_atlasLock)
        {
            if (_atlasInitialized) return;

            // 图集由 UnitAtlasCache 在全进程内构建一次，ArmyRender / ReinforceRender /
            // ReinforceRenderNew 共享同一份实例，避免各自重复扫描目录与解码 PNG。
            // 本类只持有引用，不负责释放。
            if (UnitAtlasCache.TryGet(out var atlas, out var tileMap))
            {
                _unitAtlas = atlas;
                _unitAtlasMap = tileMap;
                _atlasInitialized = true;
            }
        }
    }

    private void InitializeLevelAndFormationImages()
    {
        try
        {
            string infoMarkPath = ConfigManager.Instance.GetInformationMarkPath();

            for (int level = 1; level <= 5; level++)
            {
                string levelPath = Path.Combine(infoMarkPath, $"level_{level}.png");
                if (File.Exists(levelPath))
                {
                    using var stream = File.OpenRead(levelPath);
                    _levelIconCache[level] = SKImage.FromEncodedData(stream);
                }
            }

            string boardPath = Path.Combine(infoMarkPath, "formation_board1.png");
            if (File.Exists(boardPath))
            {
                using var stream = File.OpenRead(boardPath);
                _formationBoardCache = SKImage.FromEncodedData(stream);
            }

            string formationPath = Path.Combine(infoMarkPath, "formation.png");
            if (File.Exists(formationPath))
            {
                using var stream = File.OpenRead(formationPath);
                _formationIconCache = SKImage.FromEncodedData(stream);
            }

            for (int plan = 0; plan <= 4; plan++)
            {
                string planPath = Path.Combine(infoMarkPath, $"act{plan}.png");
                if (File.Exists(planPath))
                {
                    using var stream = File.OpenRead(planPath);
                    _actionPlanCache[plan] = SKImage.FromEncodedData(stream);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 初始化等级/编制图片失败: {ex.Message}");
        }
    }

    #endregion

    #region 主渲染方法

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showUnits) return;

        if (IsV3)
        {
            if (_mapData.ArmiesV3 == null || _mapData.ArmiesV3.Count == 0) return;
            RenderV3(canvas);
        }
        else
        {
            if (_mapData.Armies == null || _mapData.Armies.Count == 0) return;
            RenderV1(canvas);
        }
    }

    public void RenderGeneralHeadsOnly(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null || !_showUnits) return;
        if (_generalHeadPositions.Count == 0) return;

        try
        {
            DrawGeneralHeads(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 绘制将领头像失败: {ex.Message}");
        }
    }

    private void RenderV1(SKCanvas canvas)
    {
        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            float unitSize = CalculateUnitSize();
            float halfSize = unitSize / 2;

            foreach (var army in _mapData.Armies)
            {
                var screenPos = CalculateScreenPosition(army.Coordinate);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                int unitType = army.UnitType;
                if (!_unitAtlasMap.ContainsKey(unitType))
                {
                    if (_unitAtlasMap.ContainsKey(0)) unitType = 0;
                    else continue;
                }

                float scale = unitSize / ATLAS_TILE_SIZE;
                float tx = screenPos.Value.X - halfSize;
                float ty = screenPos.Value.Y - halfSize;

                if (army.Direction == 1)
                {
                    _flippedSpriteRects.Add(_unitAtlasMap[unitType]);
                    _flippedPositions.Add(new SKPoint(tx, ty));
                    _flippedSizes.Add(unitSize);
                }
                else
                {
                    _spriteRects.Add(_unitAtlasMap[unitType]);
                    _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));
                }

                CollectKeyPointIfNeeded(army.KeyPoint, screenPos.Value, unitSize);
                CollectGeneralHeadIfNeeded(army.General, screenPos.Value, unitSize);
                CollectLevelIconIfNeeded(army.Level, screenPos.Value, unitSize);
                CollectFormationInfoIfNeeded(army.Organization, army.Plan, screenPos.Value, unitSize);
            }

            DrawKeyPoints(canvas);
            DrawAtlasBatch(canvas);
            DrawLevelIcons(canvas);
            DrawFormationInfo(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 绘制v1单位失败: {ex.Message}");
        }
    }

    private void RenderV3(SKCanvas canvas)
    {
        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            float unitSize = CalculateUnitSize();
            float halfSize = unitSize / 2;

            foreach (var army in _mapData.ArmiesV3)
            {
                var screenPos = CalculateScreenPosition(army.Coordinate);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                int unitType = army.UnitType;
                if (!_unitAtlasMap.ContainsKey(unitType))
                {
                    if (_unitAtlasMap.ContainsKey(0)) unitType = 0;
                    else continue;
                }

                float scale = unitSize / ATLAS_TILE_SIZE;
                float tx = screenPos.Value.X - halfSize;
                float ty = screenPos.Value.Y - halfSize;

                if (army.Direction == 1)
                {
                    _flippedSpriteRects.Add(_unitAtlasMap[unitType]);
                    _flippedPositions.Add(new SKPoint(tx, ty));
                    _flippedSizes.Add(unitSize);
                }
                else
                {
                    _spriteRects.Add(_unitAtlasMap[unitType]);
                    _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));
                }

                CollectKeyPointIfNeeded(army.KeyPoint, screenPos.Value, unitSize);
                CollectGeneralHeadIfNeeded(army.General, screenPos.Value, unitSize);
                CollectLevelIconIfNeeded(army.Level, screenPos.Value, unitSize);
                CollectFormationInfoIfNeeded(army.Organization, army.Plan, screenPos.Value, unitSize);
            }

            DrawKeyPoints(canvas);
            DrawAtlasBatch(canvas);
            DrawLevelIcons(canvas);
            DrawFormationInfo(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 绘制v3单位失败: {ex.Message}");
        }
    }

    #endregion

    #region 数据收集

    private void ClearBuffers()
    {
        _spriteRects.Clear();
        _transforms.Clear();
        _flippedSpriteRects.Clear();
        _flippedPositions.Clear();
        _flippedSizes.Clear();
        _keyPointPositions.Clear();
        _generalHeadPositions.Clear();
        _levelIconPositions.Clear();
        _formationInfos.Clear();
    }

    private void CollectKeyPointIfNeeded(byte keyPoint, SKPoint screenPos, float unitSize)
    {
        if (keyPoint != 1 && keyPoint != 2) return;
        var color = keyPoint == 1 ? KEY_POINT_COLOR_1 : KEY_POINT_COLOR_2;
        float ellipseSize = unitSize * (float)KEY_POINT_SIZE_RATIO;
        _keyPointPositions.Add((screenPos, ellipseSize, color));
    }

    private void CollectGeneralHeadIfNeeded(short general, SKPoint screenPos, float unitSize)
    {
        if (general <= 0) return;

        float headSize = unitSize * GENERAL_HEAD_SIZE_RATIO;
        var headPos = new SKPoint(screenPos.X, screenPos.Y - unitSize * 0.6f);

        SKImage? headImage = null;
        bool isDefault = true;

        var tacticalCache = TacticalMapImageCache.Instance;
        if (tacticalCache.IsInitialized)
        {
            var generalSetting = GeneralSettingParser.Instance;
            if (generalSetting != null)
            {
                var generalData = generalSetting.GetById(general);
                string? ename = generalData?.EName;
                if (!string.IsNullOrEmpty(ename))
                {
                    headImage = tacticalCache.GetImage($"head_{ename}.png");
                    if (headImage != null) isDefault = false;
                }
            }
        }

        _generalHeadPositions.Add((headPos, headSize, headImage, isDefault));
    }

    private void CollectLevelIconIfNeeded(byte level, SKPoint screenPos, float unitSize)
    {
        if (level <= 0) return;
        int displayLevel = Math.Min((int)level, 5);
        float iconSize = unitSize * 0.3f;
        float iconX = screenPos.X + unitSize / 2 - iconSize / 2;
        float iconY = screenPos.Y + unitSize / 2 - iconSize / 2;
        _levelIconPositions.Add((new SKPoint(iconX, iconY), iconSize, displayLevel));
    }

    private void CollectFormationInfoIfNeeded(byte organization, short plan, SKPoint screenPos, float unitSize)
    {
        if (organization <= 0 || _formationBoardCache == null) return;

        float boardWidth = unitSize * 0.7f;
        float aspectRatio = (float)_formationBoardCache.Height / _formationBoardCache.Width;
        float boardHeight = boardWidth * aspectRatio;

        float boardX = screenPos.X + unitSize * 0.6f;
        float boardY = screenPos.Y;

        _formationInfos.Add((new SKPoint(boardX, boardY), new SKSize(boardWidth, boardHeight), organization, plan));
    }

    #endregion

    #region 绘制方法

    private void DrawKeyPoints(SKCanvas canvas)
    {
        if (_keyPointPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var kp in _keyPointPositions)
        {
            paint.Color = kp.Color;
            float halfSize = kp.Size / 2;
            var rect = new SKRect(kp.Position.X - halfSize, kp.Position.Y - halfSize,
                                  kp.Position.X + halfSize, kp.Position.Y + halfSize);
            canvas.DrawOval(rect, paint);
        }
    }

    private void DrawAtlasBatch(SKCanvas canvas)
    {
        lock (_atlasLock)
        {
            if (_unitAtlas == null) return;

            using var paint = new SKPaint { IsAntialias = true };

            if (_transforms.Count > 0)
            {
                canvas.DrawAtlas(_unitAtlas, _spriteRects.ToArray(), _transforms.ToArray(), paint);
            }

            if (_flippedPositions.Count > 0)
            {
                for (int i = 0; i < _flippedPositions.Count; i++)
                {
                    var spriteRect = _flippedSpriteRects[i];
                    var pos = _flippedPositions[i];
                    var size = _flippedSizes[i];

                    var dstRect = new SKRect(pos.X, pos.Y, pos.X + size, pos.Y + size);

                    canvas.Save();
                    canvas.Translate(dstRect.MidX, dstRect.MidY);
                    canvas.Scale(-1, 1);
                    canvas.Translate(-dstRect.MidX, -dstRect.MidY);
                    canvas.DrawImage(_unitAtlas, spriteRect, dstRect, paint);
                    canvas.Restore();
                }
            }
        }
    }

    private void DrawGeneralHeads(SKCanvas canvas)
    {
        if (_generalHeadPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var headInfo in _generalHeadPositions)
        {
            float halfSize = headInfo.Size / 2;

            if (headInfo.IsDefault)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(255, 215, 0);
                canvas.DrawCircle(headInfo.Position.X, headInfo.Position.Y, halfSize, paint);

                paint.Style = SKPaintStyle.Stroke;
                paint.Color = new SKColor(218, 165, 32);
                paint.StrokeWidth = halfSize * 0.1f;
                canvas.DrawCircle(headInfo.Position.X, headInfo.Position.Y, halfSize, paint);
            }
            else if (headInfo.Image != null)
            {
                var dstRect = new SKRect(
                    headInfo.Position.X - halfSize,
                    headInfo.Position.Y - halfSize,
                    headInfo.Position.X + halfSize,
                    headInfo.Position.Y + halfSize);
                canvas.DrawImage(headInfo.Image, dstRect, paint);
            }
        }
    }

    private void DrawLevelIcons(SKCanvas canvas)
    {
        if (_levelIconPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var levelInfo in _levelIconPositions)
        {
            if (!_levelIconCache.TryGetValue(levelInfo.Level, out var levelImage)) continue;
            if (levelImage == null) continue;

            float halfSize = levelInfo.Size / 2;
            var dstRect = new SKRect(
                levelInfo.Position.X - halfSize,
                levelInfo.Position.Y - halfSize,
                levelInfo.Position.X + halfSize,
                levelInfo.Position.Y + halfSize);

            canvas.DrawImage(levelImage, dstRect, paint);
        }
    }

    private void DrawFormationInfo(SKCanvas canvas)
    {
        if (_formationInfos.Count == 0 || _formationBoardCache == null || _formationIconCache == null) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var info in _formationInfos)
        {
            var boardRect = new SKRect(
                info.BoardPos.X - info.BoardSize.Width / 2,
                info.BoardPos.Y - info.BoardSize.Height / 2,
                info.BoardPos.X + info.BoardSize.Width / 2,
                info.BoardPos.Y + info.BoardSize.Height / 2);
            canvas.DrawImage(_formationBoardCache, boardRect, paint);

            float formationSize = info.BoardSize.Width * 0.3f;
            int displayCount = Math.Min(info.FormationCount, (int)4);

            for (int i = 0; i < displayCount; i++)
            {
                int row = i / 2;
                int col = i % 2;
                float offsetX = (col - 0.5f) * info.BoardSize.Width * 0.4f;
                float offsetY = (row - 0.5f) * info.BoardSize.Height * 0.4f;

                float formationX = info.BoardPos.X + offsetX;
                float formationY = info.BoardPos.Y + offsetY;

                var formationRect = new SKRect(
                    formationX - formationSize / 2,
                    formationY - formationSize / 2,
                    formationX + formationSize / 2,
                    formationY + formationSize / 2);

                canvas.DrawImage(_formationIconCache, formationRect, paint);
            }

            if (_actionPlanCache.TryGetValue(info.Plan, out var planImage) || _actionPlanCache.TryGetValue(0, out planImage))
            {
                if (planImage != null)
                {
                    float planAspect = (float)planImage.Height / planImage.Width;
                    float planWidth = info.BoardSize.Width * 0.5f;
                    float planHeight = planWidth * planAspect;
                    float planX = info.BoardPos.X;
                    float planY = info.BoardPos.Y - info.BoardSize.Height * 1.0f;

                    var planRect = new SKRect(
                        planX - planWidth / 2,
                        planY - planHeight / 2,
                        planX + planWidth / 2,
                        planY + planHeight / 2);

                    canvas.DrawImage(planImage, planRect, paint);
                }
            }
        }
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

    private float CalculateUnitSize()
    {
        double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3);
        return (float)(hexHeight * _camera.ZoomLevel * UNIT_IMAGE_SIZE_RATIO);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_atlasLock)
        {
            // _unitAtlas / _unitAtlasMap 归 UnitAtlasCache 持有，这里只解除引用，不能 Dispose。
            _unitAtlas = null;
        }

        _levelIconCache.Values.DisposeAll();
        _levelIconCache.Clear();
        _formationBoardCache?.Dispose();
        _formationIconCache?.Dispose();
        _actionPlanCache.Values.DisposeAll();
        _actionPlanCache.Clear();

        _levelFont.Dispose();
        _levelTextPaint.Dispose();
        _generalDefaultPaint.Dispose();
        _generalBorderPaint.Dispose();
    }
}

file static class DisposableExtensions
{
    public static void DisposeAll(this IEnumerable<IDisposable> disposables)
    {
        foreach (var d in disposables) d.Dispose();
    }
}