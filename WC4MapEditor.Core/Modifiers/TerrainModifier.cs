using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class TerrainModifier : ModifierBase
{
    public override string Name => "terrain";
    public override string DisplayName => "地形修改器";
    public override string HelpText => "";

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

        _mapData.GetTerrainRef(col, row).RiverValue = value;
        MarkModified();
        return ModifierResult.Ok($"河流值设为 {value}");
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
}

public sealed class TerrainBrushInfo
{
    public int Layer { get; init; } = 1;
    public int TerrainType { get; init; }
    public int Decoration { get; init; }
}