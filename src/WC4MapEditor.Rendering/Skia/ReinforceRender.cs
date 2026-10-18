using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class ReinforceRender : IDisposable
{
    private const int ATLAS_TILE_SIZE = 64;
    private const double UNIT_IMAGE_SIZE_RATIO = 0.9;
    private const float GENERAL_HEAD_SIZE_RATIO = 0.35f;
    private const double REINFORCE_GROUP_SIZE_RATIO = 0.5;
    private const int MAX_REINFORCE_PER_HEX = 6;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_HORIZONTAL_SPACING = BASE_HEX_SIZE * 1.5;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private readonly object _atlasLock = new();
    private bool _disposed;
    private bool _showReinforcements = true;
    private bool _atlasInitialized;

    /// <summary>共享单位图集，由 <see cref="UnitAtlasCache"/> 持有所有权，本类不得 Dispose。</summary>
    private SKImage? _unitAtlas;
    /// <summary>共享切片表，指向 <see cref="UnitAtlasCache"/> 中的实例，只读使用。</summary>
    private Dictionary<int, SKRect> _unitAtlasMap = new();

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _reinforceGroupTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _spawnRoundTexts = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _generalHeadPositions = new();
    private readonly List<(SKPoint Position, float Size, int Level)> _levelIconPositions = new();

    /// <summary>
    /// 按格子坐标分组的增援单位缓存。原实现每帧执行 .Where().GroupBy() 并为每个分组
    /// ToList()，分组数与部队数成正比，会产生大量临时对象；这里改为缓存，集合内容
    /// 变化时再重建。
    /// </summary>
    private Dictionary<int, List<Reinforcement>>? _groupCacheV1;
    private Dictionary<int, List<Reinforcement_3>>? _groupCacheV3;

    private readonly Dictionary<int, SKImage> _levelIconCache = new();
    private SKImage? _formationBoardCache;
    private SKImage? _formationIconCache;
    private readonly Dictionary<int, SKImage> _actionPlanCache = new();

    private readonly SKFont _spawnRoundFont;
    private readonly SKPaint _spawnRoundPaint;
    private readonly SKPaint _spawnRoundShadowPaint;
    private readonly SKPaint _groupTextPaint;
    private readonly SKFont _groupTextFont;

    public bool ShowReinforcements
    {
        get => _showReinforcements;
        set => _showReinforcements = value;
    }

    public bool IsV3 { get; set; }

    public ReinforceRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _spawnRoundFont = new SKFont(SKTypeface.Default, 10);
        _spawnRoundPaint = new SKPaint { IsAntialias = true, Color = SKColors.Green };
        _spawnRoundShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        _groupTextFont = new SKFont(SKTypeface.Default, 10);
        _groupTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.Yellow };

        InitializeUnitAtlas();
        InitializeLevelAndFormationImages();

        // 增援集合内容变化时丢弃分组缓存。
        if (_mapData.Reinforcements != null)
            _mapData.Reinforcements.CollectionChanged += OnReinforcementCollectionChanged;
        if (_mapData.ReinforcementsV3 != null)
            _mapData.ReinforcementsV3.CollectionChanged += OnReinforcementCollectionChanged;
    }

    private void OnReinforcementCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _groupCacheV1 = null;
        _groupCacheV3 = null;
    }

    /// <summary>按 Coordinate 分组的 v1 增援，惰性构建并缓存。</summary>
    private Dictionary<int, List<Reinforcement>> GetGroupsV1()
    {
        var cached = _groupCacheV1;
        if (cached != null) return cached;

        var map = new Dictionary<int, List<Reinforcement>>();
        var source = _mapData.Reinforcements;
        if (source != null)
        {
            foreach (var r in source)
            {
                if (r.Coordinate < 0) continue;
                if (!map.TryGetValue(r.Coordinate, out var list))
                {
                    list = new List<Reinforcement>(4);
                    map[r.Coordinate] = list;
                }
                list.Add(r);
            }
        }

        _groupCacheV1 = map;
        return map;
    }

    /// <summary>按 Coordinate 分组的 v3 增援，惰性构建并缓存。</summary>
    private Dictionary<int, List<Reinforcement_3>> GetGroupsV3()
    {
        var cached = _groupCacheV3;
        if (cached != null) return cached;

        var map = new Dictionary<int, List<Reinforcement_3>>();
        var source = _mapData.ReinforcementsV3;
        if (source != null)
        {
            foreach (var r in source)
            {
                if (r.Coordinate < 0) continue;
                if (!map.TryGetValue(r.Coordinate, out var list))
                {
                    list = new List<Reinforcement_3>(4);
                    map[r.Coordinate] = list;
                }
                list.Add(r);
            }
        }

        _groupCacheV3 = map;
        return map;
    }

    #region 纹理图集管理

    private void InitializeUnitAtlas()
    {
        if (_atlasInitialized) return;

        lock (_atlasLock)
        {
            if (_atlasInitialized) return;

            // 与 ArmyRender / ReinforceRenderNew 共享 UnitAtlasCache 中的同一份图集，
            // 避免各自重复扫描目录与解码 PNG。
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
            Debug.WriteLine($"[ReinforceRender] 初始化等级/编制图片失败: {ex.Message}");
        }
    }

    #endregion

    #region 主渲染方法

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showReinforcements) return;
        if (!_atlasInitialized || _unitAtlas == null) return;

        if (IsV3)
        {
            if (_mapData.ReinforcementsV3 == null || _mapData.ReinforcementsV3.Count == 0) return;
            RenderV3(canvas);
        }
        else
        {
            if (_mapData.Reinforcements == null || _mapData.Reinforcements.Count == 0) return;
            RenderV1(canvas);
        }
    }

    private void RenderV1(SKCanvas canvas)
    {
        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float groupIconSize = (float)(hexHeight * REINFORCE_GROUP_SIZE_RATIO);
            float halfIconSize = groupIconSize / 2;
            double hexWidth = BASE_HEX_SIZE * 1.5 * _camera.ZoomLevel;
            float hexHalfWidth = (float)hexWidth / 2;

            foreach (var kvp in GetGroupsV1())
            {
                var groupList = kvp.Value;

                var screenPos = CalculateScreenPosition(kvp.Key);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                int visibleCount = Math.Min(groupList.Count, MAX_REINFORCE_PER_HEX);

                for (int i = 0; i < visibleCount; i++)
                {
                    var reinforce = groupList[i];
                    int unitType = reinforce.UnitType;

                    if (!_unitAtlasMap.ContainsKey(unitType))
                    {
                        if (_unitAtlasMap.ContainsKey(0)) unitType = 0;
                        else continue;
                    }

                    float iconX = screenPos.Value.X - hexHalfWidth * 0.8f;
                    float iconY = screenPos.Value.Y - (float)hexHeight * 0.3f + i * groupIconSize * 0.4f;

                    float scale = groupIconSize / ATLAS_TILE_SIZE;
                    float tx = iconX - halfIconSize;
                    float ty = iconY - halfIconSize;

                    _spriteRects.Add(_unitAtlasMap[unitType]);
                    _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));

                    CollectLevelIconIfNeeded(reinforce.Level, new SKPoint(iconX, iconY), groupIconSize);
                    CollectSpawnRoundText(reinforce.SpawnRound, new SKPoint(iconX, iconY), groupIconSize);
                    CollectGeneralHeadIfNeeded(reinforce.General, new SKPoint(iconX, iconY), groupIconSize);
                }

                if (groupList.Count > MAX_REINFORCE_PER_HEX)
                {
                    float textX = screenPos.Value.X - hexHalfWidth * 0.8f;
                    float textY = screenPos.Value.Y - (float)hexHeight * 0.3f + visibleCount * groupIconSize * 0.4f + groupIconSize * 0.5f;
                    _reinforceGroupTexts.Add((new SKPoint(textX, textY), $"X{groupList.Count}", groupIconSize));
                }
            }

            DrawAtlasBatch(canvas);
            DrawSpawnRoundTexts(canvas);
            DrawLevelIcons(canvas);
            DrawGeneralHeads(canvas);
            DrawReinforceGroupTexts(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReinforceRender] 绘制v1援军失败: {ex.Message}");
        }
    }

    private void RenderV3(SKCanvas canvas)
    {
        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float groupIconSize = (float)(hexHeight * REINFORCE_GROUP_SIZE_RATIO);
            float halfIconSize = groupIconSize / 2;
            double hexWidth = BASE_HEX_SIZE * 1.5 * _camera.ZoomLevel;
            float hexHalfWidth = (float)hexWidth / 2;

            foreach (var kvp in GetGroupsV3())
            {
                var groupList = kvp.Value;

                var screenPos = CalculateScreenPosition(kvp.Key);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                int visibleCount = Math.Min(groupList.Count, MAX_REINFORCE_PER_HEX);

                for (int i = 0; i < visibleCount; i++)
                {
                    var reinforce = groupList[i];
                    int unitType = reinforce.UnitType;

                    if (!_unitAtlasMap.ContainsKey(unitType))
                    {
                        if (_unitAtlasMap.ContainsKey(0)) unitType = 0;
                        else continue;
                    }

                    float iconX = screenPos.Value.X - hexHalfWidth * 0.8f;
                    float iconY = screenPos.Value.Y - (float)hexHeight * 0.3f + i * groupIconSize * 0.4f;

                    float scale = groupIconSize / ATLAS_TILE_SIZE;
                    float tx = iconX - halfIconSize;
                    float ty = iconY - halfIconSize;

                    _spriteRects.Add(_unitAtlasMap[unitType]);
                    _transforms.Add(new SKRotationScaleMatrix(scale, 0, tx, ty));

                    CollectLevelIconIfNeeded(reinforce.Level, new SKPoint(iconX, iconY), groupIconSize);
                    CollectSpawnRoundText(reinforce.SpawnRound, new SKPoint(iconX, iconY), groupIconSize);
                    CollectGeneralHeadIfNeeded(reinforce.General, new SKPoint(iconX, iconY), groupIconSize);
                }

                if (groupList.Count > MAX_REINFORCE_PER_HEX)
                {
                    float textX = screenPos.Value.X - hexHalfWidth * 0.8f;
                    float textY = screenPos.Value.Y - (float)hexHeight * 0.3f + visibleCount * groupIconSize * 0.4f + groupIconSize * 0.5f;
                    _reinforceGroupTexts.Add((new SKPoint(textX, textY), $"X{groupList.Count}", groupIconSize));
                }
            }

            DrawAtlasBatch(canvas);
            DrawSpawnRoundTexts(canvas);
            DrawLevelIcons(canvas);
            DrawGeneralHeads(canvas);
            DrawReinforceGroupTexts(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReinforceRender] 绘制v3援军失败: {ex.Message}");
        }
    }

    #endregion

    #region 数据收集

    private void ClearBuffers()
    {
        _spriteRects.Clear();
        _transforms.Clear();
        _reinforceGroupTexts.Clear();
        _spawnRoundTexts.Clear();
        _generalHeadPositions.Clear();
        _levelIconPositions.Clear();
    }

    private void CollectLevelIconIfNeeded(int level, SKPoint screenPos, float unitSize)
    {
        if (level <= 0) return;
        int displayLevel = Math.Min(level, 5);
        float iconSize = unitSize * 0.3f;
        float iconX = screenPos.X + unitSize / 2 - iconSize / 2;
        float iconY = screenPos.Y + unitSize / 2 - iconSize / 2;
        _levelIconPositions.Add((new SKPoint(iconX, iconY), iconSize, displayLevel));
    }

    private void CollectSpawnRoundText(int spawnRound, SKPoint screenPos, float unitSize)
    {
        if (spawnRound <= 0) return;
        float textX = screenPos.X - unitSize * 0.7f;
        float textY = screenPos.Y + 4;
        _spawnRoundTexts.Add((new SKPoint(textX, textY), spawnRound.ToString(), unitSize * 0.4f));
    }

    private void CollectGeneralHeadIfNeeded(int general, SKPoint screenPos, float unitSize)
    {
        if (general <= 0) return;

        float headSize = unitSize * GENERAL_HEAD_SIZE_RATIO;
        var headPos = new SKPoint(screenPos.X, screenPos.Y - unitSize * 0.3f);

        SKImage? headImage = null;

        var tacticalCache = TacticalMapImageCache.Instance;
        if (tacticalCache.IsInitialized)
        {
            var generalSetting = GeneralSettingParser.Instance;
            if (generalSetting != null)
            {
                var generalData = generalSetting.GetById((int)general);
                string? ename = generalData?.EName;
                if (!string.IsNullOrEmpty(ename))
                    headImage = tacticalCache.GetImage($"head_{ename}.png");
            }
        }

        _generalHeadPositions.Add((headPos, headSize, headImage));
    }

    #endregion

    #region 绘制方法

    private void DrawAtlasBatch(SKCanvas canvas)
    {
        lock (_atlasLock)
        {
            if (_unitAtlas == null || _transforms.Count == 0) return;

            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawAtlas(_unitAtlas, _spriteRects.ToArray(), _transforms.ToArray(), paint);
        }
    }

    private void DrawSpawnRoundTexts(SKCanvas canvas)
    {
        if (_spawnRoundTexts.Count == 0) return;

        foreach (var textInfo in _spawnRoundTexts)
        {
            float fontSize = Math.Max(8, textInfo.Size);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            canvas.DrawText(textInfo.Text, textInfo.Position.X + 1, textInfo.Position.Y + 1,
                SKTextAlign.Center, font, _spawnRoundShadowPaint);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y,
                SKTextAlign.Center, font, _spawnRoundPaint);
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

    private void DrawGeneralHeads(SKCanvas canvas)
    {
        if (_generalHeadPositions.Count == 0) return;

        using var paint = new SKPaint { IsAntialias = true };

        foreach (var headInfo in _generalHeadPositions)
        {
            float halfSize = headInfo.Size / 2;

            if (headInfo.Image != null)
            {
                var dstRect = new SKRect(
                    headInfo.Position.X - halfSize,
                    headInfo.Position.Y - halfSize,
                    headInfo.Position.X + halfSize,
                    headInfo.Position.Y + halfSize);
                canvas.DrawImage(headInfo.Image, dstRect, paint);
            }
            else
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(255, 215, 0);
                canvas.DrawCircle(headInfo.Position.X, headInfo.Position.Y, halfSize, paint);

                paint.Style = SKPaintStyle.Stroke;
                paint.Color = new SKColor(218, 165, 32);
                paint.StrokeWidth = halfSize * 0.1f;
                canvas.DrawCircle(headInfo.Position.X, headInfo.Position.Y, halfSize, paint);
            }
        }
    }

    private void DrawReinforceGroupTexts(SKCanvas canvas)
    {
        if (_reinforceGroupTexts.Count == 0) return;

        foreach (var textInfo in _reinforceGroupTexts)
        {
            float fontSize = Math.Max(8, textInfo.Size * 0.5f);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            canvas.DrawText(textInfo.Text, textInfo.Position.X, textInfo.Position.Y,
                SKTextAlign.Center, font, _groupTextPaint);
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

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 解除对地图集合的订阅，避免渲染器释放后仍被集合事件引用。
        if (_mapData.Reinforcements != null)
            _mapData.Reinforcements.CollectionChanged -= OnReinforcementCollectionChanged;
        if (_mapData.ReinforcementsV3 != null)
            _mapData.ReinforcementsV3.CollectionChanged -= OnReinforcementCollectionChanged;

        lock (_atlasLock)
        {
            // _unitAtlas / _unitAtlasMap 归 UnitAtlasCache 持有，这里只解除引用，不能 Dispose。
            _unitAtlas = null;
        }

        foreach (var image in _levelIconCache.Values)
            image?.Dispose();
        _levelIconCache.Clear();
        _formationBoardCache?.Dispose();
        _formationIconCache?.Dispose();
        foreach (var image in _actionPlanCache.Values)
            image?.Dispose();
        _actionPlanCache.Clear();

        _spawnRoundFont.Dispose();
        _spawnRoundPaint.Dispose();
        _spawnRoundShadowPaint.Dispose();
        _groupTextPaint.Dispose();
        _groupTextFont.Dispose();
    }
}