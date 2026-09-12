using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ProvinceModifier : ModifierBase, IBrushTarget
{
    public override string Name => "province";
    public override string DisplayName => "省份修改器";

    private ushort _copiedProvinceValue;
    private bool _hasCopiedProvince;
    private int _copiedFromCol = -1;
    private int _copiedFromRow = -1;
    private int _selectedCountryId;
    private bool _rectangleSelectMode;
    private bool _polygonSelectMode;
    private readonly BrushEngine _brushEngine = new();
    private readonly Dictionary<(int, int), Province> _copiedProvinceGroup = new();
    private int _copiedGroupMinCol;
    private int _copiedGroupMinRow;
    private readonly Random _random = new();

    public int SelectedCountryId
    {
        get => _selectedCountryId;
        set => _selectedCountryId = value;
    }

    public BrushEngine Brush => _brushEngine;
    public int BrushRadius => _brushEngine.Radius;
    public bool IsBrushMode => _brushEngine.Active;
    public bool IsRectangleSelectMode => _rectangleSelectMode;
    public bool IsPolygonSelectMode => _polygonSelectMode;

    bool IBrushTarget.BrushActive
    {
        get => _brushEngine.Active;
        set => _brushEngine.Active = value;
    }

    int IBrushTarget.BrushSize
    {
        get => _brushEngine.Radius;
        set => _brushEngine.Radius = value;
    }

    string IBrushTarget.BrushShape
    {
        get => _brushEngine.Shape;
        set => _brushEngine.Shape = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte countryId = parameter switch
        {
            byte b => b,
            int i => (byte)Math.Clamp(i, 0, 255),
            Province p => p.CountryId,
            _ => (byte)_selectedCountryId
        };

        _mapData.GetProvinceRef(col, row).CountryId = countryId;
        MarkModified();
        return ModifierResult.Ok($"已设置省份值 ({col}, {row}) = {countryId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.GetProvinceRef(col, row).ProvinceValue = 0xFFFF;
        MarkModified();
        return ModifierResult.Ok($"已清除省份值 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetProvinceRef(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        ref Province province = ref _mapData.GetProvinceRef(col, row);

        switch (data)
        {
            case Province p:
                province = p;
                break;
            case byte b:
                province.CountryId = b;
                break;
            case int id:
                province.CountryId = (byte)Math.Clamp(id, 0, 255);
                break;
            default:
                return false;
        }

        MarkModified();
        return true;
    }

    #region Copy/Paste

    public ModifierResult CopyProvinceValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedProvinceValue = _mapData!.GetProvinceRef(col, row).ProvinceValue;
        _hasCopiedProvince = true;
        _copiedFromCol = col;
        _copiedFromRow = row;
        _copiedProvinceGroup.Clear();
        return ModifierResult.Ok($"已复制省份值: 0x{_copiedProvinceValue:X4}");
    }

    public ModifierResult CopyProvinceGroup(IReadOnlyList<(int col, int row)> coords)
    {
        if (coords.Count == 0) return ModifierResult.Fail("没有选中的格子");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _copiedProvinceGroup.Clear();

        int minCol = int.MaxValue, minRow = int.MaxValue;
        foreach (var (c, r) in coords)
        {
            minCol = Math.Min(minCol, c);
            minRow = Math.Min(minRow, r);
        }
        _copiedGroupMinCol = minCol;
        _copiedGroupMinRow = minRow;

        foreach (var (c, r) in coords)
        {
            if (!IsValidCoord(c, r)) continue;
            int relCol = c - minCol;
            int relRow = r - minRow;
            _copiedProvinceGroup[(relCol, relRow)] = _mapData.GetProvinceRef(c, r);
        }

        var first = coords[0];
        _copiedProvinceValue = _mapData.GetProvinceRef(first.col, first.row).ProvinceValue;
        _hasCopiedProvince = true;
        _copiedFromCol = first.col;
        _copiedFromRow = first.row;

        return ModifierResult.Ok($"已复制区域 {coords.Count} 个格子的省份数据");
    }

    public ModifierResult PasteProvinceValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (!_hasCopiedProvince) return ModifierResult.Fail("请先复制省份值（按C键）");

        if (_copiedProvinceGroup.Count > 0)
        {
            int count = 0;
            foreach (var kv in _copiedProvinceGroup)
            {
                int destCol = col + kv.Key.Item1;
                int destRow = row + kv.Key.Item2;
                if (!IsValidCoord(destCol, destRow)) continue;
                _mapData!.GetProvinceRef(destCol, destRow) = kv.Value;
                count++;
            }
            MarkModified();
            return ModifierResult.Ok($"已粘贴区域 {count} 个格子的省份数据到 ({col}, {row})");
        }

        _mapData!.GetProvinceRef(col, row).ProvinceValue = _copiedProvinceValue;
        MarkModified();
        return ModifierResult.Ok($"已粘贴省份值: 0x{_copiedProvinceValue:X4} 到 ({col}, {row})");
    }

    public Province? GetCopiedProvinceData() => _hasCopiedProvince ? Province.Create(_copiedProvinceValue) : null;
    public (int col, int row) GetCopyAnchor() => (_copiedFromCol, _copiedFromRow);
    public IReadOnlyDictionary<(int, int), Province>? GetCopiedProvinceGroup() => _copiedProvinceGroup.Count > 0 ? _copiedProvinceGroup : null;

    public void SetCopiedProvinceData(Province data, int fromCol, int fromRow)
    {
        _copiedProvinceValue = data.ProvinceValue;
        _hasCopiedProvince = true;
        _copiedFromCol = fromCol;
        _copiedFromRow = fromRow;
    }

    public void SetCopiedProvinceGroup(IReadOnlyDictionary<(int, int), Province> group, int minCol, int minRow)
    {
        _copiedProvinceGroup.Clear();
        foreach (var kv in group)
            _copiedProvinceGroup[kv.Key] = kv.Value;
        _copiedGroupMinCol = minCol;
        _copiedGroupMinRow = minRow;
    }

    public ModifierResult SetProvinceByCountryId(int col, int row, int countryId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.SetBelongValue(col, row, countryId);
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = 国家ID {countryId}");
    }

    #endregion

    #region Mode Toggles

    public bool ToggleBrushMode()
    {
        _brushEngine.Toggle();
        if (_brushEngine.Active)
        {
            _rectangleSelectMode = false;
            _polygonSelectMode = false;
        }
        return _brushEngine.Active;
    }

    public bool ToggleRectangleSelectMode()
    {
        _rectangleSelectMode = !_rectangleSelectMode;
        if (_rectangleSelectMode)
        {
            _brushEngine.Active = false;
            _polygonSelectMode = false;
        }
        return _rectangleSelectMode;
    }

    public bool TogglePolygonSelectMode()
    {
        _polygonSelectMode = !_polygonSelectMode;
        if (_polygonSelectMode)
        {
            _brushEngine.Active = false;
            _rectangleSelectMode = false;
        }
        return _polygonSelectMode;
    }

    public void IncreaseBrushRadius()
    {
        _brushEngine.IncreaseRadius();
    }

    public void DecreaseBrushRadius()
    {
        _brushEngine.DecreaseRadius();
    }

    public void PaintWithBrush(int centerCol, int centerRow)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        SyncFromGlobalClipboard();
        if (!_hasCopiedProvince) return;

        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
            _mapData.GetProvinceRef(c, r).ProvinceValue = _copiedProvinceValue;
        MarkModified();
    }

    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int> maskTerrainIds, bool maskIncludeMode)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        SyncFromGlobalClipboard();
        if (!_hasCopiedProvince) return;

        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
        {
            byte terrainType = _mapData.GetTerrainRef(c, r).TileType1;
            bool isInMask = maskTerrainIds.Contains(terrainType);

            // maskIncludeMode=true: 只在选中地形上绘制
            // maskIncludeMode=false: 跳过选中地形
            if (maskIncludeMode && !isInMask) continue;
            if (!maskIncludeMode && isInMask) continue;

            _mapData.GetProvinceRef(c, r).ProvinceValue = _copiedProvinceValue;
        }
        MarkModified();
    }

    private void SyncFromGlobalClipboard()
    {
        var clipboard = Services.MapClipboard.Instance;
        if (clipboard.GlobalCopiedProvince.HasValue)
        {
            _copiedProvinceValue = clipboard.GlobalCopiedProvince.Value.ProvinceValue;
            _hasCopiedProvince = true;
        }
    }

    #endregion

    #region Flood Fill

    public ModifierResult FloodFill(int startCol, int startRow)
    {
        if (!IsValidCoord(startCol, startRow)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (!_hasCopiedProvince) return ModifierResult.Fail("请先按C键复制一个省份值，然后再按F键进行洪水填充");

        ushort startProvinceValue = _mapData.GetProvinceRef(startCol, startRow).ProvinceValue;
        byte startTerrainType = _mapData.GetTerrainRef(startCol, startRow).TileType1;
        ushort fillValue = _copiedProvinceValue;

        if (startProvinceValue == fillValue)
            return ModifierResult.Ok("起始格子省份值与填充值相同，无需填充", 0);

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        var queue = new Queue<(int col, int row)>();
        var visited = new HashSet<(int, int)>();
        int filledCount = 0;

        queue.Enqueue((startCol, startRow));
        visited.Add((startCol, startRow));

        while (queue.Count > 0)
        {
            var (col, row) = queue.Dequeue();

            ushort currentProvinceValue = _mapData.GetProvinceRef(col, row).ProvinceValue;
            byte currentTerrainType = _mapData.GetTerrainRef(col, row).TileType1;

            if (currentProvinceValue == startProvinceValue && IsSameTerrainCategory(startTerrainType, currentTerrainType))
            {
                _mapData.GetProvinceRef(col, row).ProvinceValue = fillValue;
                filledCount++;

                foreach (var (nc, nr) in GetHexNeighbors(col, row))
                {
                    if (nc >= 0 && nc < mapWidth && nr >= 0 && nr < mapHeight && !visited.Contains((nc, nr)))
                    {
                        visited.Add((nc, nr));
                        queue.Enqueue((nc, nr));
                    }
                }
            }
        }

        MarkModified();
        string terrainCategory = startTerrainType == 1 ? "海洋" : "非海洋";
        if (filledCount > 0)
            return ModifierResult.Ok($"洪水填充完成，共填充了 {filledCount} 个格子。条件：省份值相同且地形同为{terrainCategory}区域", filledCount);
        else
            return ModifierResult.Ok("洪水填充完成，没有填充任何格子", 0);
    }

    #endregion

    #region Province Generation

    public ModifierResult ClearAllProvinces()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;
        int clearedCount = 0;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                _mapData.GetProvinceRef(col, row).ProvinceValue = 0xFFFF;
                clearedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已清空所有省份为0xFFFF，共 {clearedCount} 个格子", clearedCount);
    }

    public ModifierResult GenerateProvincesForIsolatedCapitals()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        var isolatedCapitals = new List<(HexCoord hex, int provinceValue)>();
        var checkedProvinces = new HashSet<int>();

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ushort provinceValue = _mapData.GetProvinceRef(index).ProvinceValue;

                if (provinceValue == index && provinceValue != 0xFFFF && !checkedProvinces.Contains(provinceValue))
                {
                    checkedProvinces.Add(provinceValue);

                    bool hasSameProvince = false;
                    foreach (var (nc, nr) in GetHexNeighbors(col, row))
                    {
                        if (nc >= 0 && nc < mapWidth && nr >= 0 && nr < mapHeight)
                        {
                            int neighborIndex = nr * mapWidth + nc;
                            if (_mapData.GetProvinceRef(neighborIndex).ProvinceValue == provinceValue)
                            {
                                hasSameProvince = true;
                                break;
                            }
                        }
                    }

                    if (!hasSameProvince)
                    {
                        isolatedCapitals.Add((new HexCoord(col, row), provinceValue));
                    }
                }
            }
        }

        if (isolatedCapitals.Count == 0)
            return ModifierResult.Ok("没有找到孤立的省会", 0);

        GenerateProvincesSimultaneously(isolatedCapitals);
        MarkModified();
        return ModifierResult.Ok($"已为 {isolatedCapitals.Count} 个孤立省会生成省区", isolatedCapitals.Count);
    }

    private void GenerateProvincesSimultaneously(List<(HexCoord hex, int provinceValue)> isolatedCapitals)
    {
        if (_mapData == null) return;

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;
        int maxRadius = 15;

        var capitalRings = new Dictionary<int, HashSet<HexCoord>>();
        var capitalFilled = new Dictionary<int, HashSet<HexCoord>>();

        foreach (var (hex, provinceValue) in isolatedCapitals)
        {
            capitalRings[provinceValue] = new HashSet<HexCoord> { hex };
            capitalFilled[provinceValue] = new HashSet<HexCoord> { hex };
            _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)provinceValue;
        }

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            var expansionCandidates = new Dictionary<HexCoord, List<int>>();

            foreach (var (hex, provinceValue) in isolatedCapitals)
            {
                var currentRing = capitalRings[provinceValue];
                var filled = capitalFilled[provinceValue];

                if (currentRing.Count == 0) continue;

                foreach (var ringHex in currentRing)
                {
                    foreach (var (nc, nr) in GetHexNeighbors(ringHex.Col, ringHex.Row))
                    {
                        if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                        var neighbor = new HexCoord(nc, nr);
                        if (filled.Contains(neighbor)) continue;

                        int neighborIndex = nr * mapWidth + nc;
                        if (IsSeaTerrain(neighborIndex)) continue;
                        if (_mapData.GetProvinceRef(neighborIndex).ProvinceValue != 0xFFFF) continue;

                        if (!expansionCandidates.ContainsKey(neighbor))
                            expansionCandidates[neighbor] = new List<int>();

                        if (!expansionCandidates[neighbor].Contains(provinceValue))
                            expansionCandidates[neighbor].Add(provinceValue);
                    }
                }
            }

            if (expansionCandidates.Count == 0) break;

            foreach (var kvp in expansionCandidates)
            {
                var hex = kvp.Key;
                var competingProvinces = kvp.Value;

                int winnerProvince = competingProvinces.Count == 1
                    ? competingProvinces[0]
                    : competingProvinces[_random.Next(competingProvinces.Count)];

                double breakProbability = Math.Min(0.1 + (radius - 1) * 0.03, 0.5);
                double noiseValue = _random.NextDouble();
                double angleNoise = GetAngleNoise(
                    capitalFilled[winnerProvince].First().Col,
                    capitalFilled[winnerProvince].First().Row,
                    hex.Col, hex.Row, radius);
                double combinedNoise = noiseValue * 0.5 + angleNoise * 0.5;

                if (combinedNoise > breakProbability)
                {
                    _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)winnerProvince;
                    capitalFilled[winnerProvince].Add(hex);
                }
            }

            var newRings = new Dictionary<int, HashSet<HexCoord>>();
            foreach (var (_, provinceValue) in isolatedCapitals)
                newRings[provinceValue] = new HashSet<HexCoord>();

            foreach (var (_, provinceValue) in isolatedCapitals)
            {
                var filled = capitalFilled[provinceValue];

                foreach (var hex in filled)
                {
                    bool hasEmptyNeighbor = false;
                    foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
                    {
                        if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                        if (filled.Contains(new HexCoord(nc, nr))) continue;

                        int neighborIndex = nr * mapWidth + nc;
                        if (IsSeaTerrain(neighborIndex)) continue;
                        if (_mapData.GetProvinceRef(neighborIndex).ProvinceValue == 0xFFFF)
                        {
                            hasEmptyNeighbor = true;
                            break;
                        }
                    }

                    if (hasEmptyNeighbor)
                        newRings[provinceValue].Add(hex);
                }
            }

            capitalRings = newRings;
        }
    }

    public ModifierResult ExpandAllProvincesToFillMap()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        var assigned = new bool[mapHeight, mapWidth];
        for (int row = 0; row < mapHeight; row++)
            for (int col = 0; col < mapWidth; col++)
                assigned[row, col] = _mapData.GetProvinceRef(col, row).ProvinceValue != 0xFFFF;

        var provinceCapitals = new Dictionary<int, HexCoord>();
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ushort provinceValue = _mapData.GetProvinceRef(index).ProvinceValue;
                if (provinceValue == index && provinceValue != 0xFFFF)
                    provinceCapitals[provinceValue] = new HexCoord(col, row);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (var kvp in provinceCapitals)
            {
                int provinceValue = kvp.Key;
                var capital = kvp.Value;

                var provinceHexes = FindProvinceHexes(capital, provinceValue, mapWidth, mapHeight);

                foreach (var hex in provinceHexes)
                {
                    foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
                    {
                        if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                        if (assigned[nr, nc]) continue;

                        int neighborIndex = nr * mapWidth + nc;
                        if (IsSeaTerrain(neighborIndex)) continue;

                        double minDistance = double.MaxValue;
                        int closestProvince = -1;
                        foreach (var capKvp in provinceCapitals)
                        {
                            double distance = Math.Sqrt(
                                Math.Pow(nc - capKvp.Value.Col, 2) +
                                Math.Pow(nr - capKvp.Value.Row, 2));
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestProvince = capKvp.Key;
                            }
                        }

                        if (closestProvince == provinceValue)
                        {
                            _mapData.GetProvinceRef(nc, nr).ProvinceValue = (ushort)provinceValue;
                            assigned[nr, nc] = true;
                            changed = true;
                        }
                    }
                }
            }
        }

        int additionalAssigned = 0;
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                if (assigned[row, col]) continue;
                int index = row * mapWidth + col;
                if (IsSeaTerrain(index)) continue;

                double minDistance = double.MaxValue;
                int closestProvince = -1;
                foreach (var capKvp in provinceCapitals)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(col - capKvp.Value.Col, 2) +
                        Math.Pow(row - capKvp.Value.Row, 2));
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestProvince = capKvp.Key;
                    }
                }

                if (closestProvince >= 0)
                {
                    _mapData.GetProvinceRef(col, row).ProvinceValue = (ushort)closestProvince;
                    assigned[row, col] = true;
                    additionalAssigned++;
                }
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已扩展所有省区填满地图，额外分配了 {additionalAssigned} 个孤立空省区", additionalAssigned);
    }

    public ModifierResult ProcessIsolatedAndEmptyProvinces()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;
        int isolatedCount = 0;
        int emptyCount = 0;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ushort provinceValue = _mapData.GetProvinceRef(index).ProvinceValue;

                if (provinceValue == index || provinceValue == 0xFFFF) continue;

                bool hasSameProvince = false;
                var neighborProvinces = new List<ushort>();

                foreach (var (nc, nr) in GetHexNeighbors(col, row))
                {
                    if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                    int neighborIndex = nr * mapWidth + nc;
                    ushort neighborProvince = _mapData.GetProvinceRef(neighborIndex).ProvinceValue;

                    if (neighborProvince == provinceValue)
                    {
                        hasSameProvince = true;
                        break;
                    }
                    else if (neighborProvince != 0xFFFF)
                    {
                        neighborProvinces.Add(neighborProvince);
                    }
                }

                if (!hasSameProvince && neighborProvinces.Count > 0)
                {
                    ushort newProvince = neighborProvinces[_random.Next(neighborProvinces.Count)];
                    _mapData.GetProvinceRef(col, row).ProvinceValue = newProvince;
                    isolatedCount++;
                }
            }
        }

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ushort provinceValue = _mapData.GetProvinceRef(index).ProvinceValue;

                if (provinceValue != 0xFFFF) continue;
                if (IsSeaTerrain(index)) continue;

                var neighborProvinces = new List<ushort>();
                foreach (var (nc, nr) in GetHexNeighbors(col, row))
                {
                    if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                    int neighborIndex = nr * mapWidth + nc;
                    ushort neighborProvince = _mapData.GetProvinceRef(neighborIndex).ProvinceValue;
                    if (neighborProvince != 0xFFFF)
                        neighborProvinces.Add(neighborProvince);
                }

                if (neighborProvinces.Count > 0)
                {
                    ushort newProvince = neighborProvinces[_random.Next(neighborProvinces.Count)];
                    _mapData.GetProvinceRef(col, row).ProvinceValue = newProvince;
                    emptyCount++;
                }
            }
        }

        MarkModified();
        return ModifierResult.Ok($"处理了 {isolatedCount} 个孤立省区和 {emptyCount} 个空白格子", isolatedCount + emptyCount);
    }

    public ModifierResult SetProvinceValueToIndex(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int index = row * _mapData.MapWidth + col;
        ushort provinceValue = (ushort)(index <= 65535 ? index : index % 65536);
        _mapData.GetProvinceRef(col, row).ProvinceValue = provinceValue;
        MarkModified();
        return ModifierResult.Ok($"已将格子 ({col},{row}) 索引 {index} 的省区数据设置为 {provinceValue}");
    }

    #endregion

    #region Helpers

    private List<(int col, int row)> GetHexNeighbors(int col, int row)
    {
        int index = row * _mapData!.MapWidth + col;
        if (index % 2 == 0)
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

    private bool IsSeaTerrain(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.MapWidth * _mapData.MapHeight) return false;
        return _mapData.GetTerrainRef(index).TileType1 == 1;
    }

    private static bool IsSameTerrainCategory(byte type1, byte type2)
    {
        bool isSea1 = type1 == 1;
        bool isSea2 = type2 == 1;
        return isSea1 == isSea2;
    }

    private double GetAngleNoise(int centerCol, int centerRow, int x, int y, int seed)
    {
        double dx = x - centerCol;
        double dy = y - centerRow;
        double angle = Math.Atan2(dy, dx);
        if (angle < 0) angle += 2 * Math.PI;

        int angleSector = (int)(angle / (Math.PI / 3)) % 6;
        var rng = new Random(seed ^ (angleSector * 12345));
        return rng.NextDouble();
    }

    private List<HexCoord> FindProvinceHexes(HexCoord capital, int provinceValue, int mapWidth, int mapHeight)
    {
        var result = new List<HexCoord>();
        var queue = new Queue<HexCoord>();
        var visited = new HashSet<HexCoord>();
        queue.Enqueue(capital);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            if (current.Col < 0 || current.Col >= mapWidth || current.Row < 0 || current.Row >= mapHeight)
                continue;

            int index = current.Row * mapWidth + current.Col;
            if (_mapData!.GetProvinceRef(index).ProvinceValue == provinceValue)
            {
                result.Add(current);
                foreach (var (nc, nr) in GetHexNeighbors(current.Col, current.Row))
                {
                    var neighbor = new HexCoord(nc, nr);
                    if (!visited.Contains(neighbor))
                        queue.Enqueue(neighbor);
                }
            }
        }

        return result;
    }

    #endregion
}