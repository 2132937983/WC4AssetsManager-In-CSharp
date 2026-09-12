using System.IO;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class TerrainModifier : ModifierBase, IBrushTarget
{
    public override string Name => "terrain";
    public override string DisplayName => "地形修改器";

    private TerrainData _copiedTerrain;
    private bool _hasCopiedTerrain;
    private Dictionary<(int col, int row), TerrainData>? _copiedTerrainGroup;
    private (int col, int row) _copyAnchor;
    private int _editLayer = 1;
    private int _brushTerrainType;
    private int _brushDecoration;
    private int _brushSize;
    private bool _brushActive;
    private string _brushShape = "圆形";

    private readonly TerrainRecognizer _terrainRecognizer = new();

    public TerrainRecognizer TerrainRecognizer => _terrainRecognizer;

    public int EditLayer
    {
        get => _editLayer;
        set => _editLayer = Math.Clamp(value, 1, 3);
    }

    public int BrushTerrainType
    {
        get => _brushTerrainType;
        set => _brushTerrainType = value;
    }

    public int BrushDecoration
    {
        get => _brushDecoration;
        set => _brushDecoration = value;
    }

    public int BrushSize
    {
        get => _brushSize;
        set => _brushSize = Math.Max(0, value);
    }

    public bool BrushActive
    {
        get => _brushActive;
        set => _brushActive = value;
    }

    public string BrushShape
    {
        get => _brushShape;
        set => _brushShape = value;
    }

    public TerrainData? GetCopiedTerrainData() => _hasCopiedTerrain ? _copiedTerrain : null;
    public Dictionary<(int col, int row), TerrainData>? GetCopiedTerrainGroup() => _copiedTerrainGroup;
    public (int col, int row) GetCopyAnchor() => _copyAnchor;

    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        _terrainRecognizer.UpdateMapData(mapData);
    }

    public override void Deinitialize()
    {
        base.Deinitialize();
    }

    public void SetCopiedTerrainData(TerrainData data, int anchorCol, int anchorRow)
    {
        _copiedTerrain = data;
        _hasCopiedTerrain = true;
        _copyAnchor = (anchorCol, anchorRow);
    }

    public void SetCopiedTerrainGroup(Dictionary<(int col, int row), TerrainData> group, int minCol, int minRow)
    {
        _copiedTerrainGroup = new Dictionary<(int, int), TerrainData>(group);
        _copyAnchor = (minCol, minRow);
        _hasCopiedTerrain = true;
        if (group.TryGetValue((minCol, minRow), out var terrain))
            _copiedTerrain = terrain;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);

        switch (parameter)
        {
            case TerrainData td:
                terrain = td;
                MarkModified();
                return ModifierResult.Ok($"已设置地形 ({col}, {row})");

            case Terrain t:
                terrain = t.ToTerrainData();
                MarkModified();
                return ModifierResult.Ok($"已设置地形 ({col}, {row})");

            case TerrainBrushInfo brushInfo:
                ApplyBrush(ref terrain, brushInfo);
                MarkModified();
                return ModifierResult.Ok($"已画笔地形 ({col}, {row})");

            default:
                return ModifierResult.Fail("未知的参数类型");
        }
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.GetTerrainRef(col, row) = TerrainData.CreateDefault();
        MarkModified();
        return ModifierResult.Ok($"已重置地形 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetTerrainRef(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);

        switch (data)
        {
            case TerrainData td:
                terrain = td;
                break;
            case Terrain t:
                terrain = t.ToTerrainData();
                break;
            default:
                return false;
        }

        MarkModified();
        return true;
    }

    public ModifierResult CopyTerrain(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedTerrain = _mapData!.GetTerrainRef(col, row);
        _hasCopiedTerrain = true;
        _copiedTerrainGroup = null;
        _copyAnchor = (col, row);
        return ModifierResult.Ok($"已复制地形 ({col}, {row})");
    }

    public ModifierResult CopyTerrainGroup(IEnumerable<(int col, int row)> coords)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var group = new Dictionary<(int, int), TerrainData>();
        int minCol = int.MaxValue, minRow = int.MaxValue;
        int count = 0;
        foreach (var (col, row) in coords)
        {
            if (!IsValidCoord(col, row)) continue;
            group[(col, row)] = _mapData.GetTerrainRef(col, row);
            if (col < minCol) minCol = col;
            if (row < minRow) minRow = row;
            count++;
        }

        if (count == 0) return ModifierResult.Fail("没有有效的格子可复制");

        _copiedTerrainGroup = group;
        _copyAnchor = (minCol, minRow);
        _hasCopiedTerrain = true;
        _copiedTerrain = _mapData.GetTerrainRef(minCol, minRow);
        return ModifierResult.Ok($"已复制{count}个格子的地形，锚点({minCol},{minRow})");
    }

    public ModifierResult PasteTerrain(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (!_hasCopiedTerrain) return ModifierResult.Fail("没有已复制的地形数据");

        if (_copiedTerrainGroup != null && _copiedTerrainGroup.Count > 1)
        {
            int dCol = col - _copyAnchor.col;
            int dRow = row - _copyAnchor.row;
            int count = 0;
            foreach (var kv in _copiedTerrainGroup)
            {
                int targetCol = kv.Key.col + dCol;
                int targetRow = kv.Key.row + dRow;
                if (!IsValidCoord(targetCol, targetRow)) continue;
                _mapData!.GetTerrainRef(targetCol, targetRow) = kv.Value;
                count++;
            }
            MarkModified();
            return ModifierResult.Ok($"已粘贴{count}个格子的地形");
        }

        _mapData!.GetTerrainRef(col, row) = _copiedTerrain;
        MarkModified();
        return ModifierResult.Ok($"已粘贴地形 ({col}, {row})");
    }

    public ModifierResult FloodFill(int startCol, int startRow, byte replacementType)
    {
        if (!IsValidCoord(startCol, startRow)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte startTerrainType = GetTileTypeByLayer(_mapData.GetTerrainRef(startCol, startRow), _editLayer);

        byte targetTerrainType;
        string conversionDirection;

        if (_hasCopiedTerrain)
        {
            targetTerrainType = GetTileTypeByLayer(_copiedTerrain, _editLayer);
            conversionDirection = $"第{_editLayer}层使用复制地形填充";
        }
        else if (startTerrainType == 1)
        {
            targetTerrainType = 0;
            conversionDirection = $"第{_editLayer}层海洋到平地";
        }
        else
        {
            targetTerrainType = 1;
            conversionDirection = $"第{_editLayer}层相同地形到海洋";
        }

        int filledCount = FloodFillBidirectional(startCol, startRow, startTerrainType, targetTerrainType);

        MarkModified();
        if (filledCount > 0)
            return ModifierResult.Ok($"洪水填充完成，共转换了{filledCount}个{conversionDirection}的格子", filledCount);
        else
            return ModifierResult.Ok("洪水填充：没有可转换的格子", 0);
    }

    private int FloodFillBidirectional(int startCol, int startRow, byte startTerrainType, byte targetTerrainType)
    {
        if (_mapData == null) return 0;

        var visited = new HashSet<(int, int)>();
        var queue = new Queue<(int col, int row)>();
        queue.Enqueue((startCol, startRow));
        visited.Add((startCol, startRow));

        int filledCount = 0;

        while (queue.Count > 0)
        {
            var (col, row) = queue.Dequeue();

            ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);
            byte currentTerrainType = GetTileTypeByLayer(terrain, _editLayer);

            bool shouldConvert;
            if (startTerrainType == 1)
            {
                shouldConvert = (currentTerrainType == 1);
            }
            else
            {
                shouldConvert = (currentTerrainType == startTerrainType);
            }

            if (!shouldConvert) continue;

            if (_hasCopiedTerrain)
            {
                SetTileTypeByLayer(ref terrain, _editLayer, GetTileTypeByLayer(_copiedTerrain, _editLayer));
                SetDecorationTypeByLayer(ref terrain, _editLayer, GetDecorationTypeByLayer(_copiedTerrain, _editLayer));
            }
            else
            {
                SetTileTypeByLayer(ref terrain, _editLayer, targetTerrainType);
                SetDecorationTypeByLayer(ref terrain, _editLayer, 0);
            }

            filledCount++;

            foreach (var (nc, nr) in GetHexNeighbors(col, row))
            {
                if (!visited.Contains((nc, nr)) && IsValidCoord(nc, nr))
                {
                    visited.Add((nc, nr));
                    queue.Enqueue((nc, nr));
                }
            }
        }

        return filledCount;
    }

    private static List<(int col, int row)> GetHexNeighbors(int col, int row)
    {
        if (col % 2 == 0)
        {
            return new List<(int, int)>
            {
                (col - 1, row), (col + 1, row),
                (col, row - 1), (col, row + 1),
                (col - 1, row - 1), (col + 1, row - 1)
            };
        }
        else
        {
            return new List<(int, int)>
            {
                (col - 1, row), (col + 1, row),
                (col, row - 1), (col, row + 1),
                (col - 1, row + 1), (col + 1, row + 1)
            };
        }
    }

    private static byte GetTileTypeByLayer(TerrainData terrain, int layer) => layer switch
    {
        2 => terrain.TileType2,
        3 => terrain.TileType3,
        _ => terrain.TileType1
    };

    private static byte GetDecorationTypeByLayer(TerrainData terrain, int layer) => layer switch
    {
        2 => terrain.DecorationType2,
        3 => terrain.DecorationType3,
        _ => terrain.DecorationType1
    };

    private static void SetTileTypeByLayer(ref TerrainData terrain, int layer, byte value)
    {
        switch (layer)
        {
            case 1: terrain.TileType1 = value; break;
            case 2: terrain.TileType2 = value; break;
            case 3: terrain.TileType3 = value; break;
        }
    }

    private static void SetDecorationTypeByLayer(ref TerrainData terrain, int layer, byte value)
    {
        switch (layer)
        {
            case 1: terrain.DecorationType1 = value; break;
            case 2: terrain.DecorationType2 = value; break;
            case 3: terrain.DecorationType3 = value; break;
        }
    }

    public ModifierResult ChangeTerrainType(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var terrainTypes = ConfigManager.Instance.GetTerrainTypes();
        int[] availableTypes = terrainTypes.Keys.Order().ToArray();

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);
        byte currentType = GetTileTypeByLayer(terrain, _editLayer);

        int newType;
        if (availableTypes.Length > 0)
        {
            int currentIndex = Array.IndexOf(availableTypes, (int)currentType);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = currentIndex + delta;
            if (newIndex < 0)
                newIndex = availableTypes.Length - 1;
            else if (newIndex >= availableTypes.Length)
                newIndex = 0;

            newType = availableTypes[newIndex];
        }
        else
        {
            newType = currentType + delta;
            if (newType < 0) newType = byte.MaxValue;
            if (newType > byte.MaxValue) newType = 0;
        }

        SetTileTypeByLayer(ref terrain, _editLayer, (byte)newType);
        SetDecorationTypeByLayer(ref terrain, _editLayer, 0);

        MarkModified();

        string typeName = ConfigManager.Instance.GetTerrainTypeName(newType);
        return ModifierResult.Ok($"第{_editLayer}层地形改为: {typeName}, 变体重置为0");
    }

    public ModifierResult ChangeDecoration(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);
        byte currentType = GetTileTypeByLayer(terrain, _editLayer);
        byte currentDeco = GetDecorationTypeByLayer(terrain, _editLayer);

        int maxDeco = ConfigManager.Instance.GetTerrainVariantCount(currentType) - 1;
        if (maxDeco < 0) maxDeco = 0;

        int newDeco = currentDeco + delta;
        if (newDeco < 0) newDeco = maxDeco;
        if (newDeco > maxDeco) newDeco = 0;

        SetDecorationTypeByLayer(ref terrain, _editLayer, (byte)newDeco);
        MarkModified();
        return ModifierResult.Ok($"第{_editLayer}层变体改为 {newDeco}");
    }

    public ModifierResult SetRiverValue(int col, int row, byte value)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte oldValue = _mapData.GetTerrainRef(col, row).RiverValue;
        _mapData.GetTerrainRef(col, row).RiverValue = value;

        // 同步河流值到邻居格子
        SyncRiverValueToNeighbors(col, row, value, oldValue);

        MarkModified();
        return ModifierResult.Ok($"河流值设为 {value}");
    }

    /// <summary>
    /// 同步河流值到邻居格子 - 当某条边有河流时，邻居格子的对边也应该有河流
    /// </summary>
    private void SyncRiverValueToNeighbors(int col, int row, byte newRiverValue, byte oldRiverValue)
    {
        if (_mapData == null) return;

        // 检查每条边的河流状态是否有变化
        for (int edgeIndex = 0; edgeIndex < 6; edgeIndex++)
        {
            bool oldHasRiver = (oldRiverValue & (1 << edgeIndex)) != 0;
            bool newHasRiver = (newRiverValue & (1 << edgeIndex)) != 0;

            // 如果状态没有变化，跳过
            if (oldHasRiver == newHasRiver) continue;

            // 状态有变化，需要同步到邻居格子
            SyncSingleRiverEdgeToNeighbor(col, row, edgeIndex, newHasRiver);
        }
    }

    /// <summary>
    /// 同步单个河流边到相邻格子
    /// </summary>
    private void SyncSingleRiverEdgeToNeighbor(int col, int row, int edgeIndex, bool hasRiver)
    {
        if (_mapData == null) return;

        // 获取邻居格子坐标
        GetNeighborPosition(col, row, edgeIndex, out int neighborCol, out int neighborRow);

        // 如果邻居格子不存在（边界），返回
        if (neighborCol < 0 || neighborCol >= _mapData.MapWidth ||
            neighborRow < 0 || neighborRow >= _mapData.MapHeight)
            return;

        // 获取邻居格子的地形
        ref var neighborTerrain = ref _mapData.GetTerrainRef(neighborCol, neighborRow);

        // 获取对边的索引（当前边的对边在邻居格子中的索引）
        int oppositeEdge = GetOppositeEdge(edgeIndex);

        // 获取邻居格子当前的河流值
        byte neighborRiverValue = neighborTerrain.RiverValue;

        // 检查邻居格子的对边状态是否与当前边状态一致
        bool neighborHasRiver = (neighborRiverValue & (1 << oppositeEdge)) != 0;

        if (neighborHasRiver != hasRiver)
        {
            // 更新邻居格子的对边状态
            if (hasRiver)
                neighborTerrain.RiverValue = (byte)(neighborRiverValue | (1 << oppositeEdge));
            else
                neighborTerrain.RiverValue = (byte)(neighborRiverValue & ~(1 << oppositeEdge));
        }
    }

    /// <summary>
    /// 获取指定边的邻居格子位置
    /// </summary>
    private void GetNeighborPosition(int col, int row, int edgeIndex, out int neighborCol, out int neighborRow)
    {
        // 根据格子编号的奇偶性计算邻居坐标（基于索引的奇偶性）
        int index = row * _mapData!.MapWidth + col;
        bool isEven = (index % 2 == 0);

        neighborCol = col;
        neighborRow = row;

        if (isEven)
        {
            // 偶数编号格子
            switch (edgeIndex)
            {
                case 0: neighborRow = row - 1; break;           // 上边
                case 1: neighborCol = col + 1; neighborRow = row - 1; break; // 右上边
                case 2: neighborCol = col + 1; break;           // 右下边
                case 3: neighborRow = row + 1; break;           // 下边
                case 4: neighborCol = col - 1; break;           // 左下边
                case 5: neighborCol = col - 1; neighborRow = row - 1; break; // 左上边
            }
        }
        else
        {
            // 奇数编号格子
            switch (edgeIndex)
            {
                case 0: neighborRow = row - 1; break;           // 上边
                case 1: neighborCol = col + 1; break;           // 右上边
                case 2: neighborCol = col + 1; neighborRow = row + 1; break; // 右下边
                case 3: neighborRow = row + 1; break;           // 下边
                case 4: neighborCol = col - 1; neighborRow = row + 1; break; // 左下边
                case 5: neighborCol = col - 1; break;           // 左上边
            }
        }
    }

    /// <summary>
    /// 获取对边的索引
    /// </summary>
    private static int GetOppositeEdge(int edgeIndex)
    {
        // 对边关系：0<->3, 1<->4, 2<->5
        return edgeIndex switch
        {
            0 => 3,
            1 => 4,
            2 => 5,
            3 => 0,
            4 => 1,
            5 => 2,
            _ => edgeIndex
        };
    }

    public ModifierResult ApplyGreening(int greeningValue, IEnumerable<(int col, int row)>? targetHexes = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        greeningValue = Math.Clamp(greeningValue, 0, 100);

        int[] greenTerrainTypes = [16, 20, 9];
        double probability = greeningValue / 100.0;
        var rand = new Random();
        int flatCount = 0, convertedCount = 0;

        var targets = GetTargetHexes(targetHexes);
        foreach (var (col, row) in targets)
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            if (terrain.TileType1 == 0)
            {
                flatCount++;
                if (rand.NextDouble() < probability)
                {
                    int newType = greenTerrainTypes[rand.Next(greenTerrainTypes.Length)];
                    terrain.TileType1 = (byte)newType;
                    terrain.TileType2 = 0x3F;
                    terrain.DecorationType2 = 0xFF;

                    int variantCount = ConfigManager.Instance.GetTerrainVariantCount(newType);
                    terrain.DecorationType1 = variantCount > 0 ? (byte)rand.Next(variantCount) : (byte)0;
                    convertedCount++;
                }
            }
        }

        if (convertedCount > 0) MarkModified();
        double rate = flatCount > 0 ? (convertedCount * 100.0 / flatCount) : 0;
        return ModifierResult.Ok($"绿化: {convertedCount}/{flatCount} 个平地 (转换率: {rate:F1}%)");
    }

    public ModifierResult RandomizeFlatTerrain(int probability, IEnumerable<(int col, int row)>? targetHexes = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        probability = Math.Clamp(probability, 0, 100);

        var terrainTypes = ConfigManager.Instance.GetTerrainTypes();
        var availableTypes = terrainTypes.Keys.Where(k => k > 1).Select(k => (byte)k).ToArray();
        if (availableTypes.Length == 0) return ModifierResult.Fail("没有可用的地形类型");

        double prob = probability / 100.0;
        var rand = new Random();
        int totalCount = 0, modifiedCount = 0;

        var targets = GetTargetHexes(targetHexes);
        foreach (var (col, row) in targets)
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            byte currentType = GetTileTypeByLayer(terrain, _editLayer);
            if (currentType == 0)
            {
                totalCount++;
                if (rand.NextDouble() < prob)
                {
                    byte newType = availableTypes[rand.Next(availableTypes.Length)];
                    SetTileTypeByLayer(ref terrain, _editLayer, newType);
                    int variantCount = ConfigManager.Instance.GetTerrainVariantCount(newType);
                    SetDecorationTypeByLayer(ref terrain, _editLayer, variantCount > 0 ? (byte)rand.Next(variantCount) : (byte)0);
                    modifiedCount++;
                }
            }
        }

        if (modifiedCount > 0) MarkModified();
        double rate = totalCount > 0 ? (modifiedCount * 100.0 / totalCount) : 0;
        return ModifierResult.Ok($"随机平地: {modifiedCount}/{totalCount} (修改率: {rate:F1}%)");
    }

    public ModifierResult RandomizeVariant(int probability, IEnumerable<(int col, int row)>? targetHexes = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        probability = Math.Clamp(probability, 0, 100);

        double prob = probability / 100.0;
        var rand = new Random();
        int totalCount = 0, modifiedCount = 0;

        var targets = GetTargetHexes(targetHexes);
        foreach (var (col, row) in targets)
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            byte currentType = GetTileTypeByLayer(terrain, _editLayer);
            if (currentType == 0 || currentType == 1) continue;

            totalCount++;
            if (rand.NextDouble() < prob)
            {
                int variantCount = ConfigManager.Instance.GetTerrainVariantCount(currentType);
                if (variantCount > 0)
                {
                    SetDecorationTypeByLayer(ref terrain, _editLayer, (byte)rand.Next(variantCount));
                    modifiedCount++;
                }
            }
        }

        if (modifiedCount > 0) MarkModified();
        double rate = totalCount > 0 ? (modifiedCount * 100.0 / totalCount) : 0;
        return ModifierResult.Ok($"随机变体第{_editLayer}层: {modifiedCount}/{totalCount} (修改率: {rate:F1}%)");
    }

    private List<(int col, int row)> GetTargetHexes(IEnumerable<(int col, int row)>? targetHexes)
    {
        if (targetHexes != null) return targetHexes.ToList();
        var result = new List<(int col, int row)>();
        if (_mapData == null) return result;
        for (int row = 0; row < _mapData.MapHeight; row++)
            for (int col = 0; col < _mapData.MapWidth; col++)
                result.Add((col, row));
        return result;
    }

    private void ApplyBrush(ref TerrainData terrain, TerrainBrushInfo brushInfo)
    {
        switch (brushInfo.Layer)
        {
            case 1:
                terrain.TileType1 = (byte)brushInfo.TerrainType;
                terrain.DecorationType1 = (byte)brushInfo.Decoration;
                break;
            case 2:
                terrain.TileType2 = (byte)brushInfo.TerrainType;
                terrain.DecorationType2 = (byte)brushInfo.Decoration;
                break;
            case 3:
                terrain.TileType3 = (byte)brushInfo.TerrainType;
                terrain.DecorationType3 = (byte)brushInfo.Decoration;
                break;
        }
    }

    public void PaintWithBrush(int centerCol, int centerRow)
    {
        if (_mapData == null || !_brushActive) return;

        var brushInfo = new TerrainBrushInfo
        {
            Layer = _editLayer,
            TerrainType = _brushTerrainType,
            Decoration = _brushDecoration
        };

        var hexes = GetHexesInBrush(centerCol, centerRow);
        foreach (var (c, r) in hexes)
            ApplyBrush(ref _mapData.GetTerrainRef(c, r), brushInfo);
        MarkModified();
    }

    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int>? maskedTerrainIds, bool maskIncludeMode)
    {
        if (_mapData == null || !_brushActive) return;

        var brushInfo = new TerrainBrushInfo
        {
            Layer = _editLayer,
            TerrainType = _brushTerrainType,
            Decoration = _brushDecoration
        };

        var hexes = GetHexesInBrush(centerCol, centerRow);
        foreach (var (c, r) in hexes)
        {
            if (PassesMask(c, r, maskedTerrainIds, maskIncludeMode))
                ApplyBrush(ref _mapData.GetTerrainRef(c, r), brushInfo);
        }
        MarkModified();
    }

    private List<(int col, int row)> GetHexesInBrush(int centerCol, int centerRow)
    {
        if (_brushShape == "方形")
            return GetHexesInSquare(centerCol, centerRow, _brushSize);
        return GetHexesInRadius(centerCol, centerRow, _brushSize);
    }

    private List<(int col, int row)> GetHexesInRadius(int centerCol, int centerRow, int radius)
    {
        var hexes = new List<(int, int)>();
        if (_mapData == null) return hexes;

        for (int row = Math.Max(0, centerRow - radius); row <= Math.Min(_mapData.MapHeight - 1, centerRow + radius); row++)
        {
            for (int col = Math.Max(0, centerCol - radius); col <= Math.Min(_mapData.MapWidth - 1, centerCol + radius); col++)
            {
                if (CalculateHexDistance(centerCol, centerRow, col, row) <= radius)
                    hexes.Add((col, row));
            }
        }
        return hexes;
    }

    private List<(int col, int row)> GetHexesInSquare(int centerCol, int centerRow, int size)
    {
        var hexes = new List<(int, int)>();
        if (_mapData == null) return hexes;

        for (int row = Math.Max(0, centerRow - size); row <= Math.Min(_mapData.MapHeight - 1, centerRow + size); row++)
        {
            for (int col = Math.Max(0, centerCol - size); col <= Math.Min(_mapData.MapWidth - 1, centerCol + size); col++)
                hexes.Add((col, row));
        }
        return hexes;
    }

    private static int CalculateHexDistance(int col1, int row1, int col2, int row2)
    {
        int x1 = col1;
        int z1 = row1 - (col1 >> 1);
        int y1 = -x1 - z1;

        int x2 = col2;
        int z2 = row2 - (col2 >> 1);
        int y2 = -x2 - z2;

        return (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) + Math.Abs(z1 - z2)) >> 1;
    }

    private bool PassesMask(int col, int row, HashSet<int>? maskedTerrainIds, bool maskIncludeMode)
    {
        if (maskedTerrainIds == null || maskedTerrainIds.Count == 0) return true;

        ref var terrain = ref _mapData!.GetTerrainRef(col, row);
        int currentType = _editLayer switch
        {
            1 => terrain.TileType1,
            2 => terrain.TileType2,
            3 => terrain.TileType3,
            _ => terrain.TileType1
        };
        bool inMask = maskedTerrainIds.Contains(currentType);
        return maskIncludeMode ? inMask : !inMask;
    }

    #region F4 - 创建海岸线

    public ModifierResult CreateCoast(IEnumerable<(int col, int row)>? targetHexes = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        int oceanModifiedCount = 0;
        var modifiedHexes = new List<(int, int)>();

        var targets = GetTargetHexes(targetHexes);

        foreach (var (col, row) in targets)
        {
            if (!IsValidCoord(col, row)) continue;

            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            bool isModified = false;

            if (terrain.TileType1 != 1 && terrain.TileType2 == 0)
            {
                terrain.TileType2 = 63;
                terrain.DecorationType2 = 255;
                modifiedCount++;
                isModified = true;
            }

            if (terrain.TileType1 == 1)
            {
                var nonOceanNeighbors = GetNonOceanNeighbors(col, row);

                switch (nonOceanNeighbors.Count)
                {
                    case 0:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = 10;
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 1:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = GetDecorationForSingleNeighbor(nonOceanNeighbors[0].Direction);
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 2:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = GetDecorationForTwoNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction);
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 3:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = GetDecorationForThreeNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction);
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 4:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = GetDecorationForFourNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction);
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 5:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = GetDecorationForFiveNeighbors(nonOceanNeighbors[0].Direction, nonOceanNeighbors[1].Direction, nonOceanNeighbors[2].Direction, nonOceanNeighbors[3].Direction, nonOceanNeighbors[4].Direction);
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                    case 6:
                        terrain.TileType2 = 31;
                        terrain.DecorationType2 = 11;
                        oceanModifiedCount++;
                        isModified = true;
                        break;
                }
            }

            if (isModified)
                modifiedHexes.Add((col, row));
        }

        if (modifiedHexes.Count > 0) MarkModified();
        string scopeInfo = targetHexes != null ? "（选中区域）" : "（全图）";
        return ModifierResult.Ok($"创建海岸线完成{scopeInfo}：修改了 {modifiedCount} 个陆地格子和 {oceanModifiedCount} 个海洋格子", modifiedHexes.Count);
    }

    private readonly struct NeighborInfo
    {
        public readonly int Col;
        public readonly int Row;
        public readonly string Direction;

        public NeighborInfo(int col, int row, string direction)
        {
            Col = col;
            Row = row;
            Direction = direction;
        }
    }

    private List<NeighborInfo> GetNonOceanNeighbors(int col, int row)
    {
        var result = new List<NeighborInfo>();
        bool isEven = (col % 2 == 0);

        int[,] offsets = isEven
            ? new int[,] { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 }, { -1, -1 } }
            : new int[,] { { 0, -1 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 } };

        string[] directions = ["上方", "右上方", "右下方", "下方", "左下方", "左上方"];

        for (int i = 0; i < 6; i++)
        {
            int neighborCol = col + offsets[i, 0];
            int neighborRow = row + offsets[i, 1];

            if (neighborCol < 0)
                neighborCol = _mapData!.MapWidth - 1;
            else if (neighborCol >= _mapData!.MapWidth)
                neighborCol = 0;

            if (neighborRow < 0 || neighborRow >= _mapData.MapHeight)
                continue;

            ref var neighborTerrain = ref _mapData.GetTerrainRef(neighborCol, neighborRow);
            if (neighborTerrain.TileType1 != 1)
                result.Add(new NeighborInfo(neighborCol, neighborRow, directions[i]));
        }

        return result;
    }

    private static byte GetDecorationForSingleNeighbor(string direction) => direction switch
    {
        "上方" => 73,
        "右上方" => 72,
        "右下方" => 70,
        "下方" => 66,
        "左下方" => 58,
        "左上方" => 42,
        _ => 10
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

    #endregion

    #region F5 - 处理海洋第二层

    public ModifierResult ProcessOceanSecondLayer(IEnumerable<(int col, int row)>? targetHexes = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        var modifiedHexes = new List<(int, int)>();

        var targets = GetTargetHexes(targetHexes);

        foreach (var (col, row) in targets)
        {
            if (!IsValidCoord(col, row)) continue;

            ref var terrain = ref _mapData.GetTerrainRef(col, row);

            if (terrain.TileType1 == 1 && terrain.TileType2 != 0)
            {
                terrain.TileType2 = 63;
                terrain.DecorationType2 = 255;
                modifiedCount++;
                modifiedHexes.Add((col, row));
            }
        }

        if (modifiedCount > 0) MarkModified();
        string scopeInfo = targetHexes != null ? "（选中区域）" : "（全图）";
        return ModifierResult.Ok($"处理完成{scopeInfo}：共修改了 {modifiedCount} 个海洋格子的第二层地形", modifiedCount);
    }

    #endregion

    #region F6 - 导出HD文件

    public ModifierResult ExportHdFile()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        try
        {
            string currentFilePath = _mapData.FilePath;
            if (string.IsNullOrEmpty(currentFilePath))
                return ModifierResult.Fail("当前文件路径为空，请先保存地图");

            int mapWidth = _mapData.MapWidth;
            int mapHeight = _mapData.MapHeight;

            string fileDir = Path.GetDirectoryName(currentFilePath)!;
            string fileName = Path.GetFileNameWithoutExtension(currentFilePath);
            string hdFilePath = Path.Combine(fileDir, $"{fileName}_map_hd.bin");

            int value1 = mapWidth * 108;
            int value2 = (int)(mapHeight * 62.5 * 2.0683076);

            int count1 = value1 / 125 + 1;
            int count2 = value2 / 125 + 1;
            int repeatCount = count1 * count2;

            using var fs = new FileStream(hdFilePath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            writer.Write((uint)value1);
            writer.Write((uint)value2);

            uint pattern = 0xFFFFFFFE;
            for (int i = 0; i < repeatCount; i++)
                writer.Write(pattern);

            long fileSize = 8 + repeatCount * 4;

            string detailMessage = $"HD文件导出成功！\n\n" +
                                   $"文件名: {fileName}_map_hd.bin\n" +
                                   $"文件大小: {fileSize} 字节\n" +
                                   $"地图尺寸: {mapWidth}x{mapHeight}\n" +
                                   $"Value1: {value1} (0x{value1:X8})\n" +
                                   $"Value2: {value2} (0x{value2:X8})\n" +
                                   $"重复次数: {repeatCount}";

            return ModifierResult.Ok(detailMessage, (int)fileSize);
        }
        catch (Exception ex)
        {
            return ModifierResult.Fail($"导出HD文件时出错：{ex.Message}");
        }
    }

    #endregion

    #region G键 - 按比例缩放地图

    public ModifierResult ScaleMap(double scale)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        scale = Math.Clamp(scale, 0.1, 10.0);
        if (Math.Abs(scale - 1.0) < 0.001) return ModifierResult.Ok("缩放比例为1.0，无需调整");

        int oldWidth = _mapData.MapWidth;
        int oldHeight = _mapData.MapHeight;

        int newWidth = Math.Max(1, (int)(oldWidth * scale));
        int newHeight = Math.Max(1, (int)(oldHeight * scale));

        double stepX = (double)oldWidth / newWidth;
        double stepY = (double)oldHeight / newHeight;

        var tempTerrains = new Dictionary<(int, int), TerrainData>();
        var tempProvinces = new Dictionary<(int, int), Province>();

        for (int newRow = 0; newRow < newHeight; newRow++)
        {
            for (int newCol = 0; newCol < newWidth; newCol++)
            {
                int oldCol = Math.Min((int)(newCol * stepX), oldWidth - 1);
                int oldRow = Math.Min((int)(newRow * stepY), oldHeight - 1);

                tempTerrains[(newCol, newRow)] = _mapData.GetTerrainRef(oldCol, oldRow);
                tempProvinces[(newCol, newRow)] = _mapData.GetProvinceRef(oldCol, oldRow);
            }
        }

        _mapData.Resize(newWidth, newHeight);

        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                ref var terrain = ref _mapData.GetTerrainRef(col, row);
                terrain = TerrainData.CreateDefault();
                terrain.TileType1 = 0;
                terrain.DecorationType1 = 0;
                _mapData.GetProvinceRef(col, row) = Province.CreateDefault();
            }
        }

        foreach (var kvp in tempTerrains)
            _mapData.GetTerrainRef(kvp.Key.Item1, kvp.Key.Item2) = kvp.Value;
        foreach (var kvp in tempProvinces)
            _mapData.GetProvinceRef(kvp.Key.Item1, kvp.Key.Item2) = kvp.Value;

        var buildingsToUpdate = _mapData.Buildings.ToList();
        _mapData.Buildings.Clear();
        for (int bi = 0; bi < buildingsToUpdate.Count; bi++)
        {
            var building = buildingsToUpdate[bi];
            var coord = HexCoord.FromIndex(building.Coordinate, oldWidth);
            int newCol = Math.Min((int)(coord.Col * scale), newWidth - 1);
            int newRow = Math.Min((int)(coord.Row * scale), newHeight - 1);
            building.Coordinate = newRow * newWidth + newCol;
            _mapData.Buildings.Add(building);
        }

        var trapsToUpdate = _mapData.Traps.ToList();
        _mapData.Traps.Clear();
        for (int ti = 0; ti < trapsToUpdate.Count; ti++)
        {
            var trap = trapsToUpdate[ti];
            var coord = HexCoord.FromIndex(trap.Coordinate, oldWidth);
            int newCol = Math.Min((int)(coord.Col * scale), newWidth - 1);
            int newRow = Math.Min((int)(coord.Row * scale), newHeight - 1);
            trap.Coordinate = (short)(newRow * newWidth + newCol);
            _mapData.Traps.Add(trap);
        }

        for (int i = _mapData.Armies.Count - 1; i >= 0; i--)
        {
            var army = _mapData.Armies[i];
            var coord = HexCoord.FromIndex(army.Coordinate, oldWidth);
            int newCol = Math.Min((int)(coord.Col * scale), newWidth - 1);
            int newRow = Math.Min((int)(coord.Row * scale), newHeight - 1);
            army.Coordinate = (short)(newRow * newWidth + newCol);
        }

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        MarkModified();
        return ModifierResult.Ok($"地图已缩放：{oldWidth}x{oldHeight} -> {newWidth}x{newHeight} (比例 {scale:F2})");
    }

    #endregion

    #region I/J/K/L键 - 调整地图大小

    public ModifierResult ResizeMap(string direction, int amount, bool useOcean)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int oldWidth = _mapData.MapWidth;
        int oldHeight = _mapData.MapHeight;
        int newWidth = oldWidth;
        int newHeight = oldHeight;
        int offsetCol = 0;
        int offsetRow = 0;

        switch (direction)
        {
            case "up":
                newHeight = oldHeight + amount;
                offsetRow = amount;
                break;
            case "down":
                newHeight = oldHeight + amount;
                break;
            case "left":
                newWidth = oldWidth + amount;
                offsetCol = amount;
                break;
            case "right":
                newWidth = oldWidth + amount;
                break;
        }

        if (newWidth <= 0 || newHeight <= 0)
            return ModifierResult.Fail("地图尺寸不能小于等于0");

        var tempTerrains = new Dictionary<(int, int), TerrainData>();
        var tempProvinces = new Dictionary<(int, int), Province>();

        for (int oldRow = 0; oldRow < oldHeight; oldRow++)
        {
            for (int oldCol = 0; oldCol < oldWidth; oldCol++)
            {
                int newCol = oldCol + offsetCol;
                int newRow = oldRow + offsetRow;

                if (newCol >= 0 && newCol < newWidth && newRow >= 0 && newRow < newHeight)
                {
                    tempTerrains[(newCol, newRow)] = _mapData.GetTerrainRef(oldCol, oldRow);
                    tempProvinces[(newCol, newRow)] = _mapData.GetProvinceRef(oldCol, oldRow);
                }
            }
        }

        _mapData.Resize(newWidth, newHeight);

        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                ref var terrain = ref _mapData.GetTerrainRef(col, row);
                terrain = TerrainData.CreateDefault();
                terrain.TileType1 = useOcean ? (byte)1 : (byte)0;
                terrain.DecorationType1 = 0;
                _mapData.GetProvinceRef(col, row) = Province.CreateDefault();
            }
        }

        foreach (var kvp in tempTerrains)
            _mapData.GetTerrainRef(kvp.Key.Item1, kvp.Key.Item2) = kvp.Value;
        foreach (var kvp in tempProvinces)
            _mapData.GetProvinceRef(kvp.Key.Item1, kvp.Key.Item2) = kvp.Value;

        if (offsetCol != 0 || offsetRow != 0)
        {
            var buildingsToUpdate = _mapData.Buildings.ToList();
            _mapData.Buildings.Clear();
            for (int bi = 0; bi < buildingsToUpdate.Count; bi++)
            {
                var building = buildingsToUpdate[bi];
                var coord = HexCoord.FromIndex(building.Coordinate, oldWidth);
                int newCol = coord.Col + offsetCol;
                int newRow = coord.Row + offsetRow;
                if (newCol >= 0 && newCol < newWidth && newRow >= 0 && newRow < newHeight)
                {
                    building.Coordinate = newRow * newWidth + newCol;
                    _mapData.Buildings.Add(building);
                }
            }
        }
        else
        {
            for (int i = _mapData.Buildings.Count - 1; i >= 0; i--)
            {
                var building = _mapData.Buildings[i];
                var coord = HexCoord.FromIndex(building.Coordinate, oldWidth);
                if (coord.Col >= newWidth || coord.Row >= newHeight)
                    _mapData.Buildings.RemoveAt(i);
                else
                    building.Coordinate = coord.Row * newWidth + coord.Col;
            }
        }

        var trapsToUpdate = _mapData.Traps.ToList();
        _mapData.Traps.Clear();
        for (int ti = 0; ti < trapsToUpdate.Count; ti++)
        {
            var trap = trapsToUpdate[ti];
            var coord = HexCoord.FromIndex(trap.Coordinate, oldWidth);
            int newCol = coord.Col + offsetCol;
            int newRow = coord.Row + offsetRow;
            if (newCol >= 0 && newCol < newWidth && newRow >= 0 && newRow < newHeight)
            {
                trap.Coordinate = (short)(newRow * newWidth + newCol);
                _mapData.Traps.Add(trap);
            }
        }

        for (int i = _mapData.Armies.Count - 1; i >= 0; i--)
        {
            var army = _mapData.Armies[i];
            var coord = HexCoord.FromIndex(army.Coordinate, oldWidth);
            int newCol = coord.Col + offsetCol;
            int newRow = coord.Row + offsetRow;
            if (newCol < 0 || newCol >= newWidth || newRow < 0 || newRow >= newHeight)
                _mapData.Armies.RemoveAt(i);
            else
                army.Coordinate = (short)(newRow * newWidth + newCol);
        }

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        MarkModified();
        string dirText = direction switch { "up" => "向上", "down" => "向下", "left" => "向左", "right" => "向右", _ => direction };
        string actionText = amount > 0 ? "扩展" : "收缩";
        return ModifierResult.Ok($"地图已调整：{dirText} 方向 {actionText} {Math.Abs(amount)} 格，新尺寸 {newWidth}x{newHeight}");
    }

    #endregion

    #region T键 - 连接建筑

    public ModifierResult ConnectBuildings()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var buildingCoords = new List<(int col, int row)>();
        foreach (var building in _mapData.Buildings)
        {
            if (building.BuildingType >= 11 && building.BuildingType <= 15)
            {
                int col = building.Coordinate % _mapData.MapWidth;
                int row = building.Coordinate / _mapData.MapWidth;
                if (col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight)
                    buildingCoords.Add((col, row));
            }
        }

        if (buildingCoords.Count < 2)
            return ModifierResult.Ok($"找到 {buildingCoords.Count} 个目标建筑，需要至少2个才能连接");

        int totalModified = 0;
        var modifiedHexes = new HashSet<(int, int)>();

        for (int i = 0; i < buildingCoords.Count - 1; i++)
        {
            var start = buildingCoords[i];
            var target = buildingCoords[i + 1];
            var path = FindPathWithAStar(start.col, start.row, target.col, target.row);

            if (path == null || path.Count == 0) continue;

            for (int pathIndex = 1; pathIndex < path.Count - 1; pathIndex++)
            {
                var (pc, pr) = path[pathIndex];
                if (pc < 0 || pc >= _mapData.MapWidth || pr < 0 || pr >= _mapData.MapHeight)
                    continue;

                ref var terrain = ref _mapData.GetTerrainRef(pc, pr);
                if (terrain.TileType1 != 0 && terrain.TileType1 != 1)
                {
                    terrain.TileType1 = 0;
                    terrain.DecorationType1 = 0;
                    totalModified++;
                    modifiedHexes.Add((pc, pr));
                }
            }
        }

        if (modifiedHexes.Count > 0) MarkModified();
        return ModifierResult.Ok($"建筑连接完成：共修改了 {totalModified} 个格子");
    }

    private List<(int col, int row)>? FindPathWithAStar(int startCol, int startRow, int targetCol, int targetRow)
    {
        if (_mapData == null) return null;
        if (startCol == targetCol && startRow == targetRow)
            return new List<(int, int)> { (startCol, startRow) };

        var openSet = new PriorityQueue<(int col, int row), double>();
        var closedSet = new HashSet<(int, int)>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), double>();

        var start = (startCol, startRow);
        gScore[start] = 0;
        openSet.Enqueue(start, HexHeuristic(startCol, startRow, targetCol, targetRow));

        int maxIterations = _mapData.MapWidth * _mapData.MapHeight * 2;
        int iterations = 0;

        while (openSet.Count > 0)
        {
            iterations++;
            if (iterations > maxIterations) return null;

            var current = openSet.Dequeue();
            if (current == (targetCol, targetRow))
                return ReconstructPath(cameFrom, current);

            if (closedSet.Contains(current)) continue;
            closedSet.Add(current);

            foreach (var (nc, nr) in GetHexNeighbors(current.col, current.row))
            {
                if (nc < 0 || nc >= _mapData.MapWidth || nr < 0 || nr >= _mapData.MapHeight)
                    continue;
                var neighbor = (nc, nr);
                if (closedSet.Contains(neighbor)) continue;

                double moveCost = 1.0;
                ref var terrain = ref _mapData.GetTerrainRef(nc, nr);
                if (terrain.TileType1 != 0 && terrain.TileType1 != 1)
                    moveCost = 5.0;

                double tentativeG = gScore[current] + moveCost;
                if (!gScore.TryGetValue(neighbor, out double existingG) || tentativeG < existingG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    openSet.Enqueue(neighbor, tentativeG + HexHeuristic(nc, nr, targetCol, targetRow));
                }
            }
        }

        return null;
    }

    private static double HexHeuristic(int col1, int row1, int col2, int row2)
    {
        return Math.Abs(col1 - col2) + Math.Abs(row1 - row2);
    }

    private static List<(int col, int row)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current };
        while (cameFrom.TryGetValue(current, out current))
            path.Add(current);
        path.Reverse();
        return path;
    }

    #endregion

    #region 选区移动

    public ModifierResult ConfirmSelectionMove(HashSet<(int col, int row)> originalHexes, int offsetCol, int offsetRow)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (originalHexes.Count == 0) return ModifierResult.Fail("没有选中的格子");
        if (offsetCol == 0 && offsetRow == 0) return ModifierResult.Ok("偏移为零，无需移动");

        var modifiedHexes = new List<(int, int)>();

        var moveOperations = new List<(int sourceCol, int sourceRow, int destCol, int destRow, TerrainData terrainData)>();
        foreach (var (sourceCol, sourceRow) in originalHexes)
        {
            int destCol = sourceCol + offsetCol;
            int destRow = sourceRow + offsetRow;

            if (!IsValidCoord(destCol, destRow)) continue;
            if (sourceCol == destCol && sourceRow == destRow) continue;

            TerrainData sourceTerrain = _mapData.GetTerrainRef(sourceCol, sourceRow);
            moveOperations.Add((sourceCol, sourceRow, destCol, destRow, sourceTerrain));
        }

        foreach (var op in moveOperations)
        {
            ref var terrain = ref _mapData.GetTerrainRef(op.sourceCol, op.sourceRow);
            terrain.TileType1 = 1;
            terrain.DecorationType1 = 0;
            modifiedHexes.Add((op.sourceCol, op.sourceRow));
        }

        foreach (var op in moveOperations)
        {
            if (!IsValidCoord(op.destCol, op.destRow)) continue;
            _mapData.GetTerrainRef(op.destCol, op.destRow) = op.terrainData;
            modifiedHexes.Add((op.destCol, op.destRow));
        }

        if (modifiedHexes.Count > 0) MarkModified();
        return ModifierResult.Ok($"已移动框选区域，共修改 {modifiedHexes.Count} 个格子", modifiedHexes.Count);
    }

    #endregion

    public async Task<(bool success, int modifiedCount)> RecognizeTerrainAsync(
        IProgress<(int current, int total, int row, int col)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (success, modifiedCount) = await _terrainRecognizer.RecognizeTerrainAsync(progress, cancellationToken);
        if (success && modifiedCount > 0)
            MarkModified();
        return (success, modifiedCount);
    }
}

public sealed class TerrainBrushInfo
{
    public int Layer { get; init; } = 1;
    public int TerrainType { get; init; }
    public int Decoration { get; init; }
}