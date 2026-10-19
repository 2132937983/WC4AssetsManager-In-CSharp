using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 单位层：单位图标（按朝向镜像）、关键据点光圈、等级徽章、编制板与方案图标；
/// 将领头像单独由 <see cref="RenderGeneralHeadsOnly"/> 在归属国旗之后绘制（对齐 VB MainRender 的层序）。
/// v1 / v3 按所绘地图的 <see cref="BTLHeader.BtlVersion"/> 自动选择单位表。
/// </summary>
public class ArmyRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 64;
    private const double UNIT_IMAGE_SIZE_RATIO = 0.9;
    private const float GENERAL_HEAD_SIZE_RATIO = 0.6f;
    private const double KEY_POINT_SIZE_RATIO = 1.5;
    private const double BASE_HEX_SIZE = 20.0;
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
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _generalHeadPositions = new();
    private readonly List<(SKPoint Position, float Size, int Level)> _levelIconPositions = new();
    private readonly List<(SKPoint BoardPos, SKSize BoardSize, int FormationCount, int Plan)> _formationInfos = new();

    private readonly Dictionary<int, SKImage> _levelIconCache = new();
    private SKImage? _formationBoardCache;
    private SKImage? _formationIconCache;
    private readonly Dictionary<int, SKImage> _actionPlanCache = new();

    /// <summary>
    /// 将领 Id → 头像（null 表示该将领没有头像）。原先每个带将领的单位每帧都要查将领表并拼接文件名。
    /// 只在 TacticalMapImageCache 就绪时写入；图块缓存状态翻转或 <see cref="ClearGeneralHeadCache"/> 时清空。
    /// 头像归 TacticalMapImageCache 所有，本类不得 Dispose。
    /// </summary>
    private readonly Dictionary<int, SKImage?> _generalHeadCache = new();

    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private readonly SKPaint _generalDefaultPaint = new() { IsAntialias = true, Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill };
    private readonly SKPaint _generalBorderPaint = new() { IsAntialias = true, Color = new SKColor(218, 165, 32), Style = SKPaintStyle.Stroke };

    public bool ShowUnits
    {
        get => _showUnits;
        set => _showUnits = value;
    }

    /// <summary>所绘地图是否为 v3（BtlVersion &gt;= 3）：v3 地图只填 ArmiesV3，v1 / v2 地图只填 Armies。</summary>
    public bool IsV3 => _mapData.Header.UsesV3Records;

    public ArmyRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

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

    /// <summary>
    /// 绘制单位层（不含将领头像）。每帧先清空缓冲：即使地图上已没有单位，
    /// 随后的 <see cref="RenderGeneralHeadsOnly"/> 也不会画出上一帧残留的头像。
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        ClearBuffers();
        if (canvas == null || _mapData == null || !_showUnits) return;

        try
        {
            var visibleBounds = CalculateVisibleBounds();
            float unitSize = CalculateUnitSize();

            if (IsV3)
            {
                foreach (var a in _mapData.ArmiesV3)
                    Collect(new UnitMark((ushort)a.Coordinate, a.UnitType, a.Direction, a.KeyPoint, a.General, a.Level, a.Organization, a.Plan), visibleBounds, unitSize);
            }
            else
            {
                foreach (var a in _mapData.Armies)
                    Collect(new UnitMark((ushort)a.Coordinate, a.UnitType, a.Direction, a.KeyPoint, a.General, a.Level, a.Organization, a.Plan), visibleBounds, unitSize);
            }

            DrawKeyPoints(canvas);
            DrawAtlasBatch(canvas);
            DrawLevelIcons(canvas);
            DrawFormationInfo(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 绘制单位失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将领头像层：画在归属国旗之后，保证头像压在国旗之上（VB DrawGeneralHeadsOnly）。
    /// 使用同一帧 <see cref="Render"/> 收集的位置；没有头像的将领画金色圆形占位。
    /// </summary>
    public void RenderGeneralHeadsOnly(SKCanvas canvas)
    {
        if (canvas == null || !_showUnits || _generalHeadPositions.Count == 0) return;

        try
        {
            DrawGeneralHeads(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmyRender] 绘制将领头像失败: {ex.Message}");
        }
    }

    #endregion

    #region 数据收集

    /// <summary>v1 / v3 单位共有的绘制字段。</summary>
    private readonly record struct UnitMark(int Cell, byte UnitType, byte Direction, byte KeyPoint, short General, byte Level, byte Organization, short Plan);

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

    /// <summary>
    /// 收集一个单位。图集里既没有该兵种也没有 0 号兵种时不画图标，
    /// 但关键据点、将领、等级、编制照常收集（与 VB 一致，以前会整个跳过）。
    /// </summary>
    private void Collect(in UnitMark unit, SKRect visibleBounds, float unitSize)
    {
        var screenPos = CalculateScreenPosition(unit.Cell);
        if (screenPos == null || !visibleBounds.Contains(screenPos.Value.X, screenPos.Value.Y)) return;
        var pos = screenPos.Value;

        if (TryGetUnitTile(unit.UnitType, out var tile))
        {
            float halfSize = unitSize / 2;
            float tx = pos.X - halfSize;
            float ty = pos.Y - halfSize;

            // SKRotationScaleMatrix 不支持负缩放（会转成旋转 180 度），朝右的单位单独镜像绘制
            if (unit.Direction == 1)
            {
                _flippedSpriteRects.Add(tile);
                _flippedPositions.Add(new SKPoint(tx, ty));
                _flippedSizes.Add(unitSize);
            }
            else
            {
                _spriteRects.Add(tile);
                _transforms.Add(new SKRotationScaleMatrix(unitSize / ATLAS_TILE_SIZE, 0, tx, ty));
            }
        }

        CollectKeyPointIfNeeded(unit.KeyPoint, pos, unitSize);
        CollectGeneralHeadIfNeeded(unit.General, pos, unitSize);
        CollectLevelIconIfNeeded(unit.Level, pos, unitSize);
        CollectFormationInfoIfNeeded(unit.Organization, unit.Plan, pos, unitSize);
    }

    private bool TryGetUnitTile(int unitType, out SKRect tile)
        => _unitAtlasMap.TryGetValue(unitType, out tile) || _unitAtlasMap.TryGetValue(0, out tile);

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
        _generalHeadPositions.Add((headPos, headSize, ResolveGeneralHead(general)));
    }

    /// <summary>按将领 Id 取头像，结果（含"无头像"的 null）缓存在 <see cref="_generalHeadCache"/>。</summary>
    private SKImage? ResolveGeneralHead(int general)
    {
        var tacticalCache = TacticalMapImageCache.Instance;
        if (!tacticalCache.IsInitialized)
        {
            // 图块缓存未就绪时不写缓存，否则会把"暂时没有"永久记成"没有"；
            // 若之前就绪过而现在被清空，缓存里的图像已失效，一并丢弃。
            if (_generalHeadCache.Count > 0) _generalHeadCache.Clear();
            return null;
        }

        if (_generalHeadCache.TryGetValue(general, out var cached))
            return cached;

        SKImage? headImage = null;
        string? ename = GeneralSettingParser.Instance.GetEname(general);
        if (!string.IsNullOrEmpty(ename))
            headImage = tacticalCache.GetImage($"head_{ename}.png");

        _generalHeadCache[general] = headImage;
        return headImage;
    }

    /// <summary>将领表（GeneralSettings）重载后调用，丢弃已缓存的 将领 Id → 头像 映射。</summary>
    public void ClearGeneralHeadCache() => _generalHeadCache.Clear();

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

            if (_transforms.Count > 0)
                canvas.DrawAtlas(_unitAtlas, _spriteRects.ToArray(), _transforms.ToArray(), _imagePaint);

            for (int i = 0; i < _flippedPositions.Count; i++)
            {
                var pos = _flippedPositions[i];
                var size = _flippedSizes[i];
                var dstRect = new SKRect(pos.X, pos.Y, pos.X + size, pos.Y + size);

                canvas.Save();
                canvas.Translate(dstRect.MidX, dstRect.MidY);
                canvas.Scale(-1, 1);
                canvas.Translate(-dstRect.MidX, -dstRect.MidY);
                canvas.DrawImage(_unitAtlas, _flippedSpriteRects[i], dstRect, _imagePaint);
                canvas.Restore();
            }
        }
    }

    private void DrawGeneralHeads(SKCanvas canvas)
    {
        foreach (var (position, size, image) in _generalHeadPositions)
        {
            float halfSize = size / 2;
            if (image != null)
            {
                var dstRect = new SKRect(position.X - halfSize, position.Y - halfSize, position.X + halfSize, position.Y + halfSize);
                canvas.DrawImage(image, dstRect, _imagePaint);
            }
            else
            {
                canvas.DrawCircle(position.X, position.Y, halfSize, _generalDefaultPaint);
                _generalBorderPaint.StrokeWidth = halfSize * 0.1f;
                canvas.DrawCircle(position.X, position.Y, halfSize, _generalBorderPaint);
            }
        }
    }

    private void DrawLevelIcons(SKCanvas canvas)
    {
        foreach (var levelInfo in _levelIconPositions)
        {
            if (!_levelIconCache.TryGetValue(levelInfo.Level, out var levelImage) || levelImage == null) continue;

            float halfSize = levelInfo.Size / 2;
            var dstRect = new SKRect(
                levelInfo.Position.X - halfSize,
                levelInfo.Position.Y - halfSize,
                levelInfo.Position.X + halfSize,
                levelInfo.Position.Y + halfSize);

            canvas.DrawImage(levelImage, dstRect, _imagePaint);
        }
    }

    private void DrawFormationInfo(SKCanvas canvas)
    {
        if (_formationInfos.Count == 0 || _formationBoardCache == null || _formationIconCache == null) return;

        foreach (var info in _formationInfos)
        {
            var boardRect = new SKRect(
                info.BoardPos.X - info.BoardSize.Width / 2,
                info.BoardPos.Y - info.BoardSize.Height / 2,
                info.BoardPos.X + info.BoardSize.Width / 2,
                info.BoardPos.Y + info.BoardSize.Height / 2);
            canvas.DrawImage(_formationBoardCache, boardRect, _imagePaint);

            float formationSize = info.BoardSize.Width * 0.3f;
            int displayCount = Math.Min(info.FormationCount, 4);

            for (int i = 0; i < displayCount; i++)
            {
                int row = i / 2;
                int col = i % 2;
                float formationX = info.BoardPos.X + (col - 0.5f) * info.BoardSize.Width * 0.4f;
                float formationY = info.BoardPos.Y + (row - 0.5f) * info.BoardSize.Height * 0.4f;

                var formationRect = new SKRect(
                    formationX - formationSize / 2,
                    formationY - formationSize / 2,
                    formationX + formationSize / 2,
                    formationY + formationSize / 2);

                canvas.DrawImage(_formationIconCache, formationRect, _imagePaint);
            }

            if ((_actionPlanCache.TryGetValue(info.Plan, out var planImage) || _actionPlanCache.TryGetValue(0, out planImage))
                && planImage != null)
            {
                float planWidth = info.BoardSize.Width * 0.5f;
                float planHeight = planWidth * planImage.Height / planImage.Width;
                float planX = info.BoardPos.X;
                float planY = info.BoardPos.Y - info.BoardSize.Height;

                var planRect = new SKRect(
                    planX - planWidth / 2,
                    planY - planHeight / 2,
                    planX + planWidth / 2,
                    planY + planHeight / 2);

                canvas.DrawImage(planImage, planRect, _imagePaint);
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

    private SKPoint? CalculateScreenPosition(int cell)
    {
        if (_mapData.MapWidth <= 0 || _mapData.MapHeight <= 0) return null;

        int row = cell / _mapData.MapWidth;
        int col = cell % _mapData.MapWidth;
        if (row >= _mapData.MapHeight) return null;

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
        _generalHeadCache.Clear(); // 头像归 TacticalMapImageCache 所有，只清引用

        _imagePaint.Dispose();
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
