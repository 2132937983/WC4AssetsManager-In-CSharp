using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Models;

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

    private SKImage? _unitAtlas;
    private readonly Dictionary<int, SKRect> _unitAtlasMap = new();

    private readonly List<SKRect> _spriteRects = new();
    private readonly List<SKRotationScaleMatrix> _transforms = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _reinforceGroupTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _spawnRoundTexts = new();
    private readonly List<(SKPoint Position, float Size, SKImage? Image)> _generalHeadPositions = new();
    private readonly List<(SKPoint Position, float Size, int Level)> _levelIconPositions = new();

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
    }

    #region 纹理图集管理

    private void InitializeUnitAtlas()
    {
        if (_atlasInitialized) return;

        lock (_atlasLock)
        {
            try
            {
                _unitAtlas?.Dispose();
                _unitAtlas = null;
                _unitAtlasMap.Clear();

                string armyMarkPath = ConfigManager.Instance.GetArmyMarkPath();
                if (!Directory.Exists(armyMarkPath)) return;

                var availableUnits = CollectAvailableUnits(armyMarkPath);
                if (availableUnits.Count == 0) return;

                int atlasTiles = CalculateAtlasSize(availableUnits.Count);
                int atlasWidth = atlasTiles * ATLAS_TILE_SIZE;
                int atlasHeight = atlasTiles * ATLAS_TILE_SIZE;

                using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                int tileIndex = 0;
                foreach (int unitType in availableUnits)
                {
                    if (tileIndex >= atlasTiles * atlasTiles) break;

                    using var unitImage = LoadUnitImage(armyMarkPath, unitType);
                    if (unitImage != null)
                    {
                        int atlasX = (tileIndex % atlasTiles) * ATLAS_TILE_SIZE;
                        int atlasY = (tileIndex / atlasTiles) * ATLAS_TILE_SIZE;

                        var srcRect = new SKRect(0, 0, unitImage.Width, unitImage.Height);
                        var dstRect = new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE);
                        canvas.DrawImage(unitImage, srcRect, dstRect);

                        _unitAtlasMap[unitType] = new SKRect(atlasX, atlasY, atlasX + ATLAS_TILE_SIZE, atlasY + ATLAS_TILE_SIZE);
                        tileIndex++;
                    }
                }

                _unitAtlas = surface.Snapshot();
                _atlasInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ReinforceRender] 初始化单位图集失败: {ex.Message}");
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

            var groups = _mapData.Reinforcements
                .Where(r => r.Coordinate >= 0)
                .GroupBy(r => r.Coordinate);

            foreach (var group in groups)
            {
                var screenPos = CalculateScreenPosition(group.Key);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                var groupList = group.ToList();
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

            var groups = _mapData.ReinforcementsV3
                .Where(r => r.Coordinate >= 0)
                .GroupBy(r => r.Coordinate);

            foreach (var group in groups)
            {
                var screenPos = CalculateScreenPosition(group.Key);
                if (screenPos == null) continue;
                if (!IsInVisibleBounds(screenPos.Value, visibleBounds)) continue;

                var groupList = group.ToList();
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

        lock (_atlasLock)
        {
            _unitAtlas?.Dispose();
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