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

        // 只重置当前编辑层。原实现整格替换为 CreateDefault()，
        // 会让 Delete 把三层地形一起清掉，与"按层编辑"的预期不符。
        ref var terrain = ref _mapData.GetTerrainRef(col, row);
        SetTileTypeByLayer(ref terrain, _editLayer, 0);
        SetDecorationTypeByLayer(ref terrain, _editLayer, 0);

        MarkModified();
        return ModifierResult.Ok($"已重置第{_editLayer}层地形 ({col}, {row})");
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

            // 按当前编辑层判断与写入（原实现写死第一层，切到第 2/3 层后绿化仍然改第一层）
            if (GetTileTypeByLayer(terrain, _editLayer) == 0)
            {
                flatCount++;
                if (rand.NextDouble() < probability)
                {
                    int newType = greenTerrainTypes[rand.Next(greenTerrainTypes.Length)];
                    SetTileTypeByLayer(ref terrain, _editLayer, (byte)newType);

                    // 原实现在第一层变陆地时会把第二层重置为默认值（0x3F / 0xFF），
                    // 这里保留该行为，但只在编辑第一层时执行，避免动到别的层。
                    if (_editLayer == 1)
                    {
                        terrain.TileType2 = 0x3F;
                        terrain.DecorationType2 = 0xFF;
                    }

                    int variantCount = ConfigManager.Instance.GetTerrainVariantCount(newType);
                    SetDecorationTypeByLayer(ref terrain, _editLayer, variantCount > 0 ? (byte)rand.Next(variantCount) : (byte)0);
                    convertedCount++;
                }
            }
        }

        if (convertedCount > 0) MarkModified();
        double rate = flatCount > 0 ? (convertedCount * 100.0 / flatCount) : 0;
        return ModifierResult.Ok($"绿化第{_editLayer}层: {convertedCount}/{flatCount} 个平地 (转换率: {rate:F1}%)");
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

        // ---- 六边形感知的缩放映射 ----
        //
        // 地图是 flat-top 六边形，且奇数列整体下移半格（与渲染层的
        // (col % 2) * hexSpacingY / 2 一致）。因此几何坐标是
        //     x = col * 0.75            （单位 hexW）
        //     y = row + (col % 2) * 0.5 （单位 hexH）
        // 只按 row * scale 缩放属于"方形矩阵"重采样，忽略了这半格偏移，
        // 放大后奇偶列交界处的地块就会错开半格。

        // 旧格 → 新格（实体搬运、归属生成、省会序号重算共用）
        int MapCol(int oldCol) => Math.Clamp((int)(oldCol * scale), 0, newWidth - 1);

        int MapRow(int oldCol, int oldRow)
        {
            double y = (oldRow + (oldCol & 1) * 0.5) * scale;
            int nr = (int)Math.Floor(y - (MapCol(oldCol) & 1) * 0.5 + 0.5);
            return Math.Clamp(nr, 0, newHeight - 1);
        }

        // 新格 → 旧格（地形/省份重采样）
        (int Col, int Row) FindSource(int newCol, int newRow)
        {
            int c = Math.Clamp((int)Math.Floor(newCol / scale), 0, oldWidth - 1);
            double y = (newRow + (newCol & 1) * 0.5) / scale;
            int r = (int)Math.Floor(y - (c & 1) * 0.5 + 0.5);
            return (c, Math.Clamp(r, 0, oldHeight - 1));
        }

        // Belongs 是紧凑索引（征服地图只覆盖 MapClip 裁剪区），且 MapData.Resize
        // 不会重建它。必须按"地图坐标 → 归属索引"先把每个格子的归属抽出来，
        // 稍后按同样的步长重采样；直接拿旧数组按下标取会越界，表现为归属不全。
        var oldBelongByMapIndex = new string[oldWidth * oldHeight];
        for (int row = 0; row < oldHeight; row++)
        {
            for (int col = 0; col < oldWidth; col++)
            {
                int bIdx = _mapData.GetBelongIndex(col, row);
                oldBelongByMapIndex[row * oldWidth + col] =
                    (bIdx >= 0 && bIdx < _mapData.Belongs.Count) ? _mapData.Belongs[bIdx] : "FF";
            }
        }

        var tempTerrains = new Dictionary<(int, int), TerrainData>();
        var tempProvinces = new Dictionary<(int, int), Province>();

        for (int newRow = 0; newRow < newHeight; newRow++)
        {
            for (int newCol = 0; newCol < newWidth; newCol++)
            {
                var (oldCol, oldRow) = FindSource(newCol, newRow);

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

        // 同 ResizeMap：ProvinceValue 是省会格子的线性序号，缩放后必须按同一比例重算，
        // 否则省区会指向错误格子。换算公式与下方建筑/陷阱/单位保持一致。
        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                ref var province = ref _mapData.GetProvinceRef(col, row);
                ushort capitalIndex = province.ProvinceValue;

                if (capitalIndex == 0 || capitalIndex == 0xFFFF) continue;

                int capitalCol = MapCol(capitalIndex % oldWidth);
                int capitalRow = MapRow(capitalIndex % oldWidth, capitalIndex / oldWidth);

                province.ProvinceValue = (ushort)(capitalRow * newWidth + capitalCol);
            }
        }

        // 缩放必须只有"一个"映射函数：所有实体（建筑/陷阱/单位/…）都按 旧格 → 新格
        // 落位，归属也必须按同一方向生成，两者才严格一致。
        //
        // 原来归属走的是反方向 floor(newCol * stepX)（新 → 旧），建筑走
        // floor(oldCol * scale)（旧 → 新）。floor 取整不可逆，两个方向并不互逆：
        // 例如 scale=0.7、旧宽 10（新宽 7）时，旧列 3 的建筑落到新列 floor(2.1)=2，
        // 而新列 2 的归属却取自旧列 floor(2 * 1.4286)=2 —— 不是 3。
        // 于是建筑落到了"归属来自另一个旧格"的位置；旧列 2 若恰好没有归属，
        // 这个原本有归属的建筑看起来就丢了归属。
        var newBelongs = new string[newWidth * newHeight];
        Array.Fill(newBelongs, "FF");
        for (int oldRow = 0; oldRow < oldHeight; oldRow++)
        {
            for (int oldCol = 0; oldCol < oldWidth; oldCol++)
            {
                int target = MapRow(oldCol, oldRow) * newWidth + MapCol(oldCol);
                string value = oldBelongByMapIndex[oldRow * oldWidth + oldCol];

                // 缩小/放大时会出现多个旧格映射到同一新格：优先保留有归属的值
                if (newBelongs[target] == "FF" || value != "FF")
                    newBelongs[target] = value;
            }
        }

        _mapData.Belongs.Clear();
        _mapData.Belongs.AddRange(newBelongs);

        var buildingsToUpdate = _mapData.Buildings.ToList();
        _mapData.Buildings.Clear();
        for (int bi = 0; bi < buildingsToUpdate.Count; bi++)
        {
            var building = buildingsToUpdate[bi];
            var coord = HexCoord.FromIndex(building.Coordinate, oldWidth);
            building.Coordinate = MapRow(coord.Col, coord.Row) * newWidth + MapCol(coord.Col);
            _mapData.Buildings.Add(building);
        }

        var trapsToUpdate = _mapData.Traps.ToList();
        _mapData.Traps.Clear();
        for (int ti = 0; ti < trapsToUpdate.Count; ti++)
        {
            var trap = trapsToUpdate[ti];
            var coord = HexCoord.FromIndex(trap.Coordinate, oldWidth);
            trap.Coordinate = (short)(MapRow(coord.Col, coord.Row) * newWidth + MapCol(coord.Col));
            _mapData.Traps.Add(trap);
        }

        for (int i = _mapData.Armies.Count - 1; i >= 0; i--)
        {
            var army = _mapData.Armies[i];
            var coord = HexCoord.FromIndex(army.Coordinate, oldWidth);
            army.Coordinate = (short)(MapRow(coord.Col, coord.Row) * newWidth + MapCol(coord.Col));
            _mapData.Armies[i] = army;   // Army 是结构体，必须写回
        }

        // 其余带格子坐标的实体：单位(v3)、援军(v1/v3)、空军、首都、单位部署
        RemapRemainingEntityCoordinates(oldWidth, newWidth, newHeight, scale);

        // 以建筑位置为省会修补省区数据（对齐 VB UpdateProvincesForBuildings）
        UpdateProvincesFromBuildings(newWidth, newHeight);

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        // 同 ResizeMap：尺寸变了，原裁剪区不再适用。清零让索引规则退化为
        // row * MapWidth + col，与上面按全尺寸顺序重建的 Belongs 对齐。
        _mapData.Header.MapClipX = 0;
        _mapData.Header.MapClipY = 0;

        MarkModified();
        return ModifierResult.Ok($"地图已缩放：{oldWidth}x{oldHeight} -> {newWidth}x{newHeight} (比例 {scale:F2})");
    }

    /// <summary>
    /// 缩放后重映射其余带格子坐标的实体：单位(v3)、援军(v1/v3)、空军、首都、单位部署。
    /// <para>
    /// 这些集合在原实现里被漏掉了 —— 缩放后它们的坐标仍指向旧地图的格子编号，
    /// 会落到完全错误的位置。此处与建筑/陷阱/单位用同一套换算。
    /// </para>
    /// </summary>
    private void RemapRemainingEntityCoordinates(int oldWidth, int newWidth, int newHeight, double scale)
    {
        if (_mapData == null) return;

        // 单位 v3
        var armiesV3 = _mapData.ArmiesV3.ToList();
        _mapData.ArmiesV3.Clear();
        foreach (var army in armiesV3)
        {
            var updated = army;
            updated.Coordinate = (short)ScaleIndex(army.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.ArmiesV3.Add(updated);
        }

        // 援军 v1
        var reinforcements = _mapData.Reinforcements.ToList();
        _mapData.Reinforcements.Clear();
        foreach (var item in reinforcements)
        {
            var updated = item;
            updated.Coordinate = ScaleIndex(item.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.Reinforcements.Add(updated);
        }

        // 援军 v3
        var reinforcementsV3 = _mapData.ReinforcementsV3.ToList();
        _mapData.ReinforcementsV3.Clear();
        foreach (var item in reinforcementsV3)
        {
            var updated = item;
            updated.Coordinate = ScaleIndex(item.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.ReinforcementsV3.Add(updated);
        }

        // 空军
        var airForces = _mapData.AirForces.ToList();
        _mapData.AirForces.Clear();
        foreach (var air in airForces)
        {
            var updated = air;
            updated.Coordinate = ScaleIndex(air.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.AirForces.Add(updated);
        }

        // 首都
        var capitals = _mapData.Capitals.ToList();
        _mapData.Capitals.Clear();
        foreach (var capital in capitals)
        {
            var updated = capital;
            updated.Coordinate = ScaleIndex(capital.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.Capitals.Add(updated);
        }

        // 单位部署
        var unitPlaces = _mapData.UnitPlaces.ToList();
        _mapData.UnitPlaces.Clear();
        foreach (var place in unitPlaces)
        {
            var updated = place;
            updated.Coordinate = ScaleIndex(place.Coordinate, oldWidth, newWidth, newHeight, scale);
            _mapData.UnitPlaces.Add(updated);
        }
    }

    /// <summary>把旧地图上的格子索引换算为新地图索引（结果夹进新地图范围）。</summary>
    /// <remarks>
    /// 六边形感知：地图是 flat-top 六边形且奇数列下移半格，几何纵坐标是
    /// <c>row + (col % 2) * 0.5</c>。所以行号不能按 <c>row * scale</c> 直接换算，
    /// 必须先把几何纵坐标缩放、再按新列的奇偶偏移还原，否则奇偶列交界处会错开半格。
    /// </remarks>
    private static int ScaleIndex(int oldIndex, int oldWidth, int newWidth, int newHeight, double scale)
    {
        if (oldWidth <= 0 || newWidth <= 0) return 0;

        var coord = HexCoord.FromIndex(oldIndex, oldWidth);
        int newCol = Math.Clamp((int)(coord.Col * scale), 0, newWidth - 1);

        double y = (coord.Row + (coord.Col & 1) * 0.5) * scale;
        int newRow = Math.Clamp((int)Math.Floor(y - (newCol & 1) * 0.5 + 0.5), 0, newHeight - 1);

        return newRow * newWidth + newCol;
    }

    /// <summary>
    /// 尺寸调整（整体平移）后重映射其余带格子坐标的实体：
    /// 单位 v3、援军 v1/v3、空军、首都、单位部署。
    /// <para>
    /// 与 <see cref="RemapRemainingEntityCoordinates"/>（按比例缩放）不同，这里的坐标变化是
    /// "加偏移"而不是"乘比例"，所以不能共用那个函数 —— 那也正是 ResizeMap 当初漏掉它们的原因。
    /// </para>
    /// <para>平移后落到新地图范围外的实体直接丢弃，与建筑/陷阱/军队的处理保持一致。</para>
    /// </summary>
    private void TranslateRemainingEntityCoordinates(
        int oldWidth, int newWidth, int newHeight, int offsetCol, int offsetRow)
    {
        if (_mapData == null) return;

        // 单位 v3
        var armiesV3 = _mapData.ArmiesV3.ToList();
        _mapData.ArmiesV3.Clear();
        foreach (var army in armiesV3)
        {
            if (!TryTranslateIndex(army.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = army;
            updated.Coordinate = (short)index;
            _mapData.ArmiesV3.Add(updated);
        }

        // 援军 v1
        var reinforcements = _mapData.Reinforcements.ToList();
        _mapData.Reinforcements.Clear();
        foreach (var item in reinforcements)
        {
            if (!TryTranslateIndex(item.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = item;
            updated.Coordinate = index;
            _mapData.Reinforcements.Add(updated);
        }

        // 援军 v3
        var reinforcementsV3 = _mapData.ReinforcementsV3.ToList();
        _mapData.ReinforcementsV3.Clear();
        foreach (var item in reinforcementsV3)
        {
            if (!TryTranslateIndex(item.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = item;
            updated.Coordinate = index;
            _mapData.ReinforcementsV3.Add(updated);
        }

        // 空军
        var airForces = _mapData.AirForces.ToList();
        _mapData.AirForces.Clear();
        foreach (var air in airForces)
        {
            if (!TryTranslateIndex(air.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = air;
            updated.Coordinate = index;
            _mapData.AirForces.Add(updated);
        }

        // 首都
        var capitals = _mapData.Capitals.ToList();
        _mapData.Capitals.Clear();
        foreach (var capital in capitals)
        {
            if (!TryTranslateIndex(capital.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = capital;
            updated.Coordinate = index;
            _mapData.Capitals.Add(updated);
        }

        // 单位部署
        var unitPlaces = _mapData.UnitPlaces.ToList();
        _mapData.UnitPlaces.Clear();
        foreach (var place in unitPlaces)
        {
            if (!TryTranslateIndex(place.Coordinate, oldWidth, newWidth, newHeight, offsetCol, offsetRow, out int index))
                continue;

            var updated = place;
            updated.Coordinate = index;
            _mapData.UnitPlaces.Add(updated);
        }
    }

    /// <summary>
    /// 把旧格索引按 (offsetCol, offsetRow) 平移到新地图；落在新范围外返回 false。
    /// </summary>
    private static bool TryTranslateIndex(
        int oldIndex, int oldWidth, int newWidth, int newHeight,
        int offsetCol, int offsetRow, out int newIndex)
    {
        newIndex = 0;
        if (oldWidth <= 0) return false;

        int newCol = oldIndex % oldWidth + offsetCol;
        int newRow = oldIndex / oldWidth + offsetRow;

        if (newCol < 0 || newCol >= newWidth || newRow < 0 || newRow >= newHeight)
            return false;

        newIndex = newRow * newWidth + newCol;
        return true;
    }

    /// <summary>
    /// 缩放后以建筑位置为省会修补省区数据（对齐 VB UpdateProvincesForBuildings）：
    /// 建筑所在格若省区为空（0 或 0xFFFF），就把它设成以该建筑索引为省会值的省区；
    /// 再从这些省会用 BFS 向四周扩散，填补同样为空的相邻陆地格子（跳过海洋）。
    /// </summary>
    private void UpdateProvincesFromBuildings(int mapWidth, int mapHeight)
    {
        if (_mapData == null) return;

        int total = mapWidth * mapHeight;

        // 第一步：把"建筑所在且省区为空"的格子设成省会
        var capitalIndices = new List<int>();
        foreach (var building in _mapData.Buildings)
        {
            int index = building.Coordinate;
            if (index < 0 || index >= total) continue;

            ref var province = ref _mapData.GetProvinceRef(index);
            if (province.ProvinceValue != 0 && province.ProvinceValue != 0xFFFF) continue;

            province.ProvinceValue = (ushort)(index <= 0xFFFF ? index : index % 0x10000);
            capitalIndices.Add(index);
        }

        if (capitalIndices.Count == 0) return;

        // 第二步：多源 BFS 向相邻空白陆地格扩散
        var visited = new bool[total];
        var queue = new Queue<int>();
        var neighbors = new List<int>(6);

        foreach (int capitalIndex in capitalIndices)
        {
            ushort fillValue = (ushort)(capitalIndex <= 0xFFFF ? capitalIndex : capitalIndex % 0x10000);

            queue.Clear();
            queue.Enqueue(capitalIndex);
            visited[capitalIndex] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int col = current % mapWidth;
                int row = current / mapWidth;

                CollectHexNeighbors(col, row, mapWidth, mapHeight, neighbors);
                foreach (int neighbor in neighbors)
                {
                    if (visited[neighbor]) continue;
                    visited[neighbor] = true;

                    ref var province = ref _mapData.GetProvinceRef(neighbor);
                    if (province.ProvinceValue != 0 && province.ProvinceValue != 0xFFFF) continue;

                    // 海洋不参与省区填充
                    if (_mapData.GetTerrainRef(neighbor).TileType1 == 1) continue;

                    province.ProvinceValue = fillValue;
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// 收集六边形邻居（奇数列下移，与渲染层的 <c>(col % 2) * hexSpacingY / 2</c> 偏移一致）。
    /// </summary>
    private static void CollectHexNeighbors(int col, int row, int mapWidth, int mapHeight, List<int> output)
    {
        output.Clear();

        void Add(int c, int r)
        {
            if (c < 0 || c >= mapWidth || r < 0 || r >= mapHeight) return;
            output.Add(r * mapWidth + c);
        }

        Add(col - 1, row);
        Add(col + 1, row);
        Add(col, row - 1);
        Add(col, row + 1);

        if ((col & 1) == 1)
        {
            Add(col - 1, row + 1);
            Add(col + 1, row + 1);
        }
        else
        {
            Add(col - 1, row - 1);
            Add(col + 1, row - 1);
        }
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

        // 归属数组是紧凑索引（征服地图只覆盖 MapClip 裁剪区），必须在 Resize 之前
        // 按"地图坐标 → 归属索引"把每个格子的归属先抽出来。直接拿旧数组按下标取
        // 会越界，结果被当成"无归属"，表现就是归属不全。
        var oldBelongByMapIndex = new string[oldWidth * oldHeight];
        for (int row = 0; row < oldHeight; row++)
        {
            for (int col = 0; col < oldWidth; col++)
            {
                int bIdx = _mapData.GetBelongIndex(col, row);
                oldBelongByMapIndex[row * oldWidth + col] =
                    (bIdx >= 0 && bIdx < _mapData.Belongs.Count) ? _mapData.Belongs[bIdx] : "FF";
            }
        }

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

        // ProvinceValue 存的是「省会格子的线性序号」(row * 宽度 + col)，地图尺寸一变就整体错位。
        // 上面只搬移了 Province 本身，这里必须按 旧索引 -> 新索引 重算，
        // 否则省区绘制、以及按省会归属放置建筑/单位都会指向错误的格子。
        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                ref var province = ref _mapData.GetProvinceRef(col, row);
                ushort capitalIndex = province.ProvinceValue;

                // 0 与 0xFFFF 都表示该格不隶属于任何省区
                if (capitalIndex == 0 || capitalIndex == 0xFFFF) continue;

                int capitalCol = capitalIndex % oldWidth + offsetCol;
                int capitalRow = capitalIndex / oldWidth + offsetRow;

                if (capitalCol < 0 || capitalCol >= newWidth || capitalRow < 0 || capitalRow >= newHeight)
                {
                    // 省会格被裁到地图之外，该省区随之失效
                    province.ProvinceValue = 0xFFFF;
                    continue;
                }

                province.ProvinceValue = (ushort)(capitalRow * newWidth + capitalCol);
            }
        }

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
            {
                army.Coordinate = (short)(newRow * newWidth + newCol);
                _mapData.Armies[i] = army;   // Army 是结构体，必须写回
            }
        }

        // 其余带格子坐标的实体：单位(v3)、援军(v1/v3)、空军、首都、单位部署。
        // 与 ScaleMap 同理，这里漏掉的话它们仍按旧地图编号解释，会落到完全错误的位置。
        TranslateRemainingEntityCoordinates(oldWidth, newWidth, newHeight, offsetCol, offsetRow);

        // Belongs 是逐格索引的字符串表，MapData.Resize 不会重建它。
        // 不重建就会整体错位：地图收缩、或向上/向左扩展时内容被搬移，
        // 归属却停在原处，偏差正好等于偏移量。
        // 这里按 旧索引 -> 新索引 重采样（与 ScaleMap 的做法一致）。
        _mapData.Belongs.Clear();
        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                int oldCol = col - offsetCol;
                int oldRow = row - offsetRow;

                if (oldCol >= 0 && oldCol < oldWidth && oldRow >= 0 && oldRow < oldHeight)
                    _mapData.Belongs.Add(oldBelongByMapIndex[oldRow * oldWidth + oldCol]);
                else
                    _mapData.Belongs.Add("FF");   // 新扩展出来的格子：无归属
            }
        }

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        // 尺寸变了之后原来的裁剪区不再适用。清零可让"地图坐标 → 归属索引"
        // 退化成 row * MapWidth + col，与上面按全尺寸顺序重建的 Belongs 对齐。
        _mapData.Header.MapClipX = 0;
        _mapData.Header.MapClipY = 0;

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