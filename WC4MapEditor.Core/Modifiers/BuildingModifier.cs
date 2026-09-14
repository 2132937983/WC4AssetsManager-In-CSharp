using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Modifiers;

public sealed class BuildingModifier : ModifierBase
{
    public override string Name => "building";
    public override string DisplayName => "建筑修改器";

    private Building? _copiedBuilding;
    private bool _copiedLevelsOnly;
    private readonly Random _random = new();

    private Dictionary<int, Building>? _copiedBuildingGroup;
    private int _copiedGroupMinCol;
    private int _copiedGroupMinRow;

    private static readonly int[] DefaultRandomTypes = { 11, 12, 13, 14, 15 };

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int coordIndex = row * _mapData.MapWidth + col;

        Building building;
        if (parameter is Building b)
        {
            building = b;
            building.Coordinate = coordIndex;
        }
        else
        {
            building = Building.CreateDefault(coordIndex);
        }

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
        {
            _mapData.Buildings.Add(building);
            _mapData.AddToBuildingCoordIndex(coordIndex, _mapData.Buildings.Count - 1);
        }

        MarkModified();
        if (coordIndex > 65535)
            return ModifierResult.Ok($"已放置建筑 ({col}, {row}) [警告:坐标序号{coordIndex}超出65535,保存时将截断]");
        return ModifierResult.Ok($"已放置建筑 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有建筑");

        _mapData.RemoveBuildingAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除建筑 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindBuildingIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindBuildingIndex(col, row);
        return idx >= 0 ? _mapData.Buildings[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Building building) return false;

        building.Coordinate = row * _mapData.MapWidth + col;
        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
        {
            _mapData.Buildings.Add(building);
            _mapData.AddToBuildingCoordIndex(building.Coordinate, _mapData.Buildings.Count - 1);
        }

        MarkModified();
        return true;
    }

    public ModifierResult CopyBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有建筑");
        _copiedBuilding = _mapData.Buildings[idx];
        _copiedLevelsOnly = false;
        _copiedBuildingGroup = null;
        return ModifierResult.Ok("已复制完整建筑数据");
    }

    public int CopyBuildingGroup(IReadOnlySet<HexCoord> selectedHexes)
    {
        if (_mapData == null) return 0;

        _copiedBuildingGroup = new Dictionary<int, Building>();
        _copiedGroupMinCol = int.MaxValue;
        _copiedGroupMinRow = int.MaxValue;

        foreach (var coord in selectedHexes)
        {
            int idx = _mapData.FindBuildingIndex(coord.Col, coord.Row);
            if (idx < 0) continue;

            var building = _mapData.Buildings[idx];
            int relCol = coord.Col;
            int relRow = coord.Row;
            _copiedGroupMinCol = Math.Min(_copiedGroupMinCol, relCol);
            _copiedGroupMinRow = Math.Min(_copiedGroupMinRow, relRow);
            int key = relCol * 10000 + relRow;
            _copiedBuildingGroup[key] = building;
        }

        _copiedBuilding = null;
        return _copiedBuildingGroup.Count;
    }

    public int PasteBuildingGroup(int targetCol, int targetRow)
    {
        if (_mapData == null) return 0;
        if (_copiedBuildingGroup == null || _copiedBuildingGroup.Count == 0) return 0;

        int count = 0;
        foreach (var (key, building) in _copiedBuildingGroup)
        {
            int relCol = key / 10000 - _copiedGroupMinCol;
            int relRow = key % 10000 - _copiedGroupMinRow;
            int destCol = targetCol + relCol;
            int destRow = targetRow + relRow;

            if (destCol < 0 || destCol >= _mapData.MapWidth || destRow < 0 || destRow >= _mapData.MapHeight)
                continue;

            Apply(destCol, destRow, building);
            count++;
        }

        return count;
    }

    public bool HasCopiedGroup => _copiedBuildingGroup != null && _copiedBuildingGroup.Count > 0;

    public ModifierResult CopyBuildingLevels(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有建筑");
        var src = _mapData.Buildings[idx];
        _copiedBuilding = new Building
        {
            FactoryLevel = src.FactoryLevel,
            ResearchLevel = src.ResearchLevel,
            MedicalLevel = src.MedicalLevel,
            AviationLevel = src.AviationLevel,
            MissileLevel = src.MissileLevel,
            NuclearLevel = src.NuclearLevel
        };
        _copiedLevelsOnly = true;
        return ModifierResult.Ok("已复制建筑等级数据");
    }

    public ModifierResult PasteBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedBuilding == null) return ModifierResult.Fail("没有已复制的建筑数据");
        return Apply(col, row, _copiedBuilding);
    }

    public ModifierResult PasteBuildingLevels(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedBuilding == null) return ModifierResult.Fail("没有已复制的建筑数据");

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("当前格子没有建筑，无法粘贴等级");

        var building = _mapData.Buildings[idx];
        building.FactoryLevel = _copiedBuilding.Value.FactoryLevel;
        building.ResearchLevel = _copiedBuilding.Value.ResearchLevel;
        building.MedicalLevel = _copiedBuilding.Value.MedicalLevel;
        building.AviationLevel = _copiedBuilding.Value.AviationLevel;
        building.MissileLevel = _copiedBuilding.Value.MissileLevel;
        building.NuclearLevel = _copiedBuilding.Value.NuclearLevel;
        _mapData.ReplaceBuilding(idx, building);

        MarkModified();
        return ModifierResult.Ok("已粘贴建筑等级数据");
    }

    public ModifierResult UpdateBuilding(Building building)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindBuildingIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
        {
            _mapData.Buildings.Add(building);
            _mapData.AddToBuildingCoordIndex(building.Coordinate, _mapData.Buildings.Count - 1);
        }

        MarkModified();
        return ModifierResult.Ok("已更新建筑数据");
    }

    public void RemoveAll()
    {
        if (_mapData == null) return;
        _mapData.Buildings.Clear();
        _mapData.InvalidateBuildingCoordIndex();
        MarkModified();
    }

    public int RemoveNonCapitalBuildings(MapData mapData)
    {
        if (_mapData == null) return 0;

        var toRemove = new List<Building>();
        foreach (var building in _mapData.Buildings)
        {
            var province = mapData.GetProvinceRef(building.Coordinate);
            if (building.Coordinate != province.ProvinceValue)
                toRemove.Add(building);
        }

        foreach (var building in toRemove)
            _mapData.Buildings.Remove(building);

        if (toRemove.Count > 0) MarkModified();
        return toRemove.Count;
    }

    public ModifierResult ToggleKeyPoint(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("当前格子没有建筑");

        var building = _mapData.Buildings[idx];
        string msg;
        switch (building.KeyPoint)
        {
            case 0:
                building.KeyPoint = 1;
                msg = "已设置红圈标记";
                break;
            case 1:
                building.KeyPoint = 2;
                msg = "已设置绿圈标记";
                break;
            default:
                building.KeyPoint = 0;
                msg = "已取消标记";
                break;
        }
        _mapData.ReplaceBuilding(idx, building);
        MarkModified();
        return ModifierResult.Ok(msg);
    }

    public ModifierResult SetOccupationEvent(int col, int row, int eventId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("当前格子没有建筑");

        var building = _mapData.Buildings[idx];
        building.OccupationEvent = (byte)Math.Clamp(eventId, 0, 255);
        _mapData.ReplaceBuilding(idx, building);
        MarkModified();
        return ModifierResult.Ok($"已设置占领触发事件为 {eventId}");
    }

    public int MarkAllEventBuildingsWithRedCircle()
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.OccupationEvent != 0)
            {
                building.KeyPoint = 1;
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();
        return count;
    }

    #region 建筑类型随机化（H 键，遵循 setting.txt 建筑配置）

    /// <summary>随机化「拥有名称」的建筑类型（对齐 VB RandomizeNamedBuildings）</summary>
    public ModifierResult RandomizeNamedBuildingTypes(bool useCondition)
        => RandomizeBuildingTypesByConfig(ownName: true, useCondition);

    /// <summary>随机化「没有名称」的建筑类型（对齐 VB RandomizeUnnamedBuildings）</summary>
    public ModifierResult RandomizeUnnamedBuildingTypes(bool useCondition)
        => RandomizeBuildingTypesByConfig(ownName: false, useCondition);

    private ModifierResult RandomizeBuildingTypesByConfig(bool ownName, bool useCondition)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        if (buildingConfig == null) return ModifierResult.Fail("建筑配置未加载");

        var config = ownName
            ? buildingConfig.OwnNameBuildingRandomConfig
            : buildingConfig.NotOwnNameBuildingRandomConfig;
        var configName = ownName ? "OwnNameBuildingRandomConfig" : "NotOwnNameBuildingRandomConfig";

        if (config == null || (config.Rules.Count == 0 && config.DefaultRange == default))
            return ModifierResult.Fail($"无法读取{configName}配置");

        // 可参与随机的建筑类型以 setting.txt 的 BuildingRandomCharacter 为准
        var randomizableTypes = buildingConfig.BuildingRandomCharacter is { Count: > 0 }
            ? buildingConfig.BuildingRandomCharacter
            : new List<int>(DefaultRandomTypes);

        // 预建坐标占用表，避免条件判断时逐格线性查找
        var occupied = new HashSet<int>();
        foreach (var b in _mapData.Buildings) occupied.Add(b.Coordinate);

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];

            bool hasName = building.Name != 0 && building.Name != -1;
            if (hasName != ownName) continue;
            if (!randomizableTypes.Contains(building.BuildingType)) continue;

            if (ApplyBuildingNameRandomConfig(ref building, config, buildingConfig, occupied, useCondition))
            {
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();

        var modeText = useCondition ? "条件判断" : "默认随机";
        var nameText = ownName ? "拥有名称" : "没有名称";
        return ModifierResult.Ok($"已使用【{modeText}】方式随机化 {count} 个{nameText}的建筑");
    }

    /// <summary>
    /// 应用建筑类型随机配置。
    /// DefaultRange 为默认随机范围；Rules 每项为 ((检测范围层数, 最少建筑数), 建筑类型)，
    /// 从最后一条规则往前检测，范围内建筑数达到下限时套用对应类型。
    /// </summary>
    private bool ApplyBuildingNameRandomConfig(
        ref Building building,
        BuildingNameRandomConfig config,
        BuildingConfig buildingConfig,
        HashSet<int> occupied,
        bool useCondition)
    {
        if (_mapData == null) return false;

        bool hasRules = config.Rules.Count > 0;
        bool hasDefault = config.DefaultRange.Max > config.DefaultRange.Min;
        if (!hasRules && !hasDefault) return false;

        int mapWidth = _mapData.MapWidth;

        int coord = building.Coordinate;
        int row = coord / mapWidth;
        int col = coord % mapWidth;

        int picked = 0;
        bool applied = false;

        if (useCondition && hasRules)
        {
            // 从最后一个条件开始检查（越靠后的条件要求越严格）
            for (int i = config.Rules.Count - 1; i >= 0; i--)
            {
                var (range, buildingType) = config.Rules[i];
                int rangeX = range.Min;      // 检测范围（六边形层数）
                int minCountY = range.Max;   // 范围内至少需要的建筑数

                // 六边形 BFS：统计 rangeX 层内（含自身）的建筑数
                int count = CountBuildingsInHexRange(col, row, rangeX, occupied);

                // 建筑数达到下限才套用该类型
                if (count >= minCountY)
                {
                    picked = buildingType;
                    applied = true;
                    break;
                }
            }
        }

        if (!applied)
        {
            if (!hasDefault) return false;
            picked = _random.Next(config.DefaultRange.Min, config.DefaultRange.Max + 1);
        }

        building.BuildingType = (byte)picked;
        RandomizeBuildingLevelsFromConfig(ref building, buildingConfig);
        return true;
    }

    /// <summary>
    /// 按 setting.txt 的 BuildingRandomCharacter / BuildingRandomParameters 随机化设施等级
    /// （对齐 VB RandomizeBuildingLevels，每组 7 个字段：范围、工厂、研究所、补给、机场、导弹、核设施）。
    /// </summary>
    private void RandomizeBuildingLevelsFromConfig(ref Building building, BuildingConfig buildingConfig)
    {
        const int FieldsPerGroup = 7;

        var randomTypes = buildingConfig.BuildingRandomCharacter is { Count: > 0 }
            ? buildingConfig.BuildingRandomCharacter
            : new List<int>(DefaultRandomTypes);

        var allParams = buildingConfig.BuildingRandomParameters;
        int typeIndex = randomTypes.IndexOf(building.BuildingType);

        if (allParams == null || typeIndex < 0 || typeIndex >= allParams.Count)
        {
            RandomizeBuildingLevels(ref building);
            return;
        }

        var flat = allParams[typeIndex];
        int groupCount = flat.Count / FieldsPerGroup;
        if (groupCount == 0)
        {
            RandomizeBuildingLevels(ref building);
            return;
        }

        int start = _random.Next(groupCount) * FieldsPerGroup;

        building.FactoryLevel = (byte)RollRange(flat[start + 1]);
        building.ResearchLevel = (byte)RollRange(flat[start + 2]);
        building.MedicalLevel = (byte)RollRange(flat[start + 3]);
        building.AviationLevel = (byte)RollRange(flat[start + 4]);
        building.MissileLevel = (byte)RollRange(flat[start + 5]);
        building.NuclearLevel = (byte)RollRange(flat[start + 6]);
        building.HatredValue = (byte)_random.Next(0, 256);
    }

    private int RollRange((int Min, int Max) range)
        => range.Max > range.Min ? _random.Next(range.Min, range.Max + 1) : range.Min;

    #endregion

    public int RandomizeBuildingsByBelong(int targetBelongId)
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];

            if (!DefaultRandomTypes.Contains(building.BuildingType))
                continue;

            if (targetBelongId >= 0)
            {
                var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
                var belongId = _mapData.GetBelongValue(coord.Col, coord.Row);
                if (belongId != targetBelongId) continue;
            }

            building.BuildingType = (byte)DefaultRandomTypes[_random.Next(DefaultRandomTypes.Length)];
            RandomizeBuildingLevels(ref building);
            _mapData.ReplaceBuilding(i, building);
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    public int GenerateCapitalsForAllBuildings(MapData mapData, bool overwriteExisting = false)
    {
        if (_mapData == null) return 0;

        int count = 0;
        foreach (var building in _mapData.Buildings)
        {
            var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
            ref var province = ref mapData.GetProvinceRef(coord.Col, coord.Row);
            if (province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF)
            {
                province.ProvinceValue = (ushort)building.Coordinate;
                count++;
            }
            else if (overwriteExisting)
            {
                province.ProvinceValue = (ushort)building.Coordinate;
                count++;
            }
        }

        if (count > 0) MarkModified();
        return count;
    }

    public int CountBuildingsWithExistingProvince(MapData mapData)
    {
        if (_mapData == null) return 0;

        int count = 0;
        foreach (var building in _mapData.Buildings)
        {
            var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
            ref var province = ref mapData.GetProvinceRef(coord.Col, coord.Row);
            if (province.ProvinceValue != 0 && province.ProvinceValue != 0xFFFF)
                count++;
        }
        return count;
    }

    public int RandomizeBuildingsOnCapitals(MapData mapData)
    {
        if (_mapData == null) return 0;

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var randomTypes = buildingConfig.BuildingRandomCharacter is { Count: > 0 }
            ? buildingConfig.BuildingRandomCharacter
            : new List<int>(DefaultRandomTypes);

        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            var province = _mapData.GetProvinceRef(i);

            // 省会：省区值等于格子索引，且不是空白省区
            if (province.ProvinceValue != i || province.ProvinceValue == 0xFFFF)
                continue;

            var coord = HexCoord.FromIndex(i, _mapData.MapWidth);
            if (_mapData.FindBuildingIndex(coord.Col, coord.Row) >= 0)
                continue;

            // 对齐 VB：只跳过无效归属（-1），0xFF 仍参与生成
            int belongId = _mapData.GetBelongValueByIndex(i);
            if (belongId < 0)
                continue;

            int buildingType = randomTypes[_random.Next(randomTypes.Count)];
            if (CreateBuildingAtIndex(i, buildingType, belongId))
                count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    /// <summary>
    /// 智能设置建筑外观值：按邻居海陆关系决定外观（对齐 VB SmartSetBuildingAppearance）
    /// </summary>
    public int SmartSetBuildingAppearance(MapData mapData)
    {
        if (_mapData == null) return 0;

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var needDirection = buildingConfig?.BuildingNeedDirection is { Count: > 0 }
            ? buildingConfig.BuildingNeedDirection
            : new List<int> { 31, 32, 33, 34 };

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];

            // 只处理需要方向的建筑类型
            if (!needDirection.Contains(building.BuildingType)) continue;

            int coord = building.Coordinate;
            int row = coord / _mapData.MapWidth;
            int col = coord % _mapData.MapWidth;

            var neighborTerrain = GetNeighborTerrain(row, col);
            building.Appearance = (byte)DetermineAppearanceByNeighbors(neighborTerrain);

            _mapData.ReplaceBuilding(i, building);
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    #region 海岸线港口生成（E 键）

    /// <summary>
    /// 在海岸线海洋格子上按概率生成港口（对齐 VB GenerateBuildingsOnCoastalHexes）
    /// </summary>
    public ModifierResult GenerateBuildingsOnCoastalHexes(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        probability = Math.Clamp(probability, 0, 100);

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var buildingOnSea = buildingConfig?.BuildingOnSea is { Count: > 0 }
            ? buildingConfig.BuildingOnSea
            : new List<int> { 31, 32, 33, 34 };

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        int coastalHexCount = 0;
        int buildingsGenerated = 0;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                // 跳过地图边界
                if (row == 0 || row == mapHeight - 1 || col == 0 || col == mapWidth - 1)
                    continue;

                int index = row * mapWidth + col;

                // 只处理海洋格子
                if (GetTerrainTypeByIndex(index) != 1) continue;

                var neighborTerrain = GetNeighborTerrain(row, col);

                // 邻居中同时存在海洋与非海洋 → 海岸线海洋格子
                bool hasOceanNeighbor = false;
                bool hasLandNeighbor = false;
                foreach (var t in neighborTerrain.Values)
                {
                    if (t == 1) hasOceanNeighbor = true;
                    else hasLandNeighbor = true;
                }

                if (!hasOceanNeighbor || !hasLandNeighbor) continue;

                coastalHexCount++;

                if (_mapData.FindBuildingIndex(col, row) >= 0) continue;
                if (_random.Next(100) >= probability) continue;

                int buildingType = buildingOnSea[_random.Next(buildingOnSea.Count)];
                if (CreateBuildingAtIndex(index, buildingType, null))
                {
                    SetBelongFromNeighborCapital(index, row, col);
                    buildingsGenerated++;
                }
            }
        }

        if (buildingsGenerated > 0) MarkModified();

        return ModifierResult.Ok($"找到 {coastalHexCount} 个海岸线海洋格子，生成了 {buildingsGenerated} 个建筑");
    }

    /// <summary>把当前格归属设为随机一个邻居省份的省会归属（对齐 VB SetBelongFromNeighborCapital）</summary>
    private void SetBelongFromNeighborCapital(int index, int row, int col)
    {
        if (_mapData == null) return;

        var neighborTerrain = GetNeighborTerrain(row, col);
        var neighborProvinces = new List<int>();

        foreach (var (name, dr, dc) in GetHexDirections(col))
        {
            // 只取非海洋邻居
            if (!neighborTerrain.TryGetValue(name, out int terrain) || terrain == 1) continue;

            int nr = row + dr;
            int nc = col + dc;
            if (nr < 0 || nr >= _mapData.MapHeight || nc < 0 || nc >= _mapData.MapWidth) continue;

            int neighborIndex = nr * _mapData.MapWidth + nc;
            int provinceValue = _mapData.GetProvinceRef(neighborIndex).ProvinceValue;
            if (provinceValue != 0xFFFF)
                neighborProvinces.Add(provinceValue);
        }

        if (neighborProvinces.Count == 0) return;

        int selectedProvinceValue = neighborProvinces[_random.Next(neighborProvinces.Count)];

        // 查找该省份的省会格子（省区值等于格子索引）
        int capitalIndex = -1;
        int totalCells = _mapData.MapWidth * _mapData.MapHeight;
        for (int i = 0; i < totalCells; i++)
        {
            int provinceValue = _mapData.GetProvinceRef(i).ProvinceValue;
            if (provinceValue == selectedProvinceValue && provinceValue == i)
            {
                capitalIndex = i;
                break;
            }
        }

        if (capitalIndex < 0) return;

        int capitalBelongId = _mapData.GetBelongValueByIndex(capitalIndex);
        if (capitalBelongId >= 0)
        {
            int c = index % _mapData.MapWidth;
            int r = index / _mapData.MapWidth;
            _mapData.SetBelongValue(c, r, capitalBelongId);
        }
    }

    #endregion

    #region 六边形邻居辅助

    /// <summary>偶数列的六边形邻居偏移</summary>
    private static readonly (string Name, int DR, int DC)[] HexDirectionsEven =
    {
        ("up", -1, 0), ("up_right", -1, 1), ("down_right", 0, 1),
        ("down", 1, 0), ("down_left", 0, -1), ("up_left", -1, -1)
    };

    /// <summary>奇数列的六边形邻居偏移</summary>
    private static readonly (string Name, int DR, int DC)[] HexDirectionsOdd =
    {
        ("up", -1, 0), ("up_right", 0, 1), ("down_right", 1, 1),
        ("down", 1, 0), ("down_left", 1, -1), ("up_left", 0, -1)
    };

    /// <summary>六边形邻居方向偏移（奇偶列不同）</summary>
    private static (string Name, int DR, int DC)[] GetHexDirections(int col)
        => col % 2 == 0 ? HexDirectionsEven : HexDirectionsOdd;

    /// <summary>
    /// 六边形 BFS：统计以 (col,row) 为中心、range 层六边形邻域内的建筑数量（含中心格自身）。
    /// 每层的邻居偏移按该格自身所在列的奇偶性选取。
    /// </summary>
    private int CountBuildingsInHexRange(int col, int row, int range, HashSet<int> occupied)
    {
        if (_mapData == null) return 0;

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        int centerIndex = row * mapWidth + col;
        int count = occupied.Contains(centerIndex) ? 1 : 0;

        if (range <= 0) return count;

        var visited = new HashSet<int> { centerIndex };
        var frontier = new List<(int Col, int Row)> { (col, row) };

        for (int step = 1; step <= range; step++)
        {
            var next = new List<(int Col, int Row)>();

            foreach (var (c, r) in frontier)
            {
                foreach (var (_, dr, dc) in GetHexDirections(c))
                {
                    int nc = c + dc;
                    int nr = r + dr;
                    if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;

                    int idx = nr * mapWidth + nc;
                    if (!visited.Add(idx)) continue;

                    if (occupied.Contains(idx)) count++;
                    next.Add((nc, nr));
                }
            }

            if (next.Count == 0) break;
            frontier = next;
        }

        return count;
    }

    /// <summary>获取邻居格子的地形类型（越界视为海洋=1）</summary>
    private Dictionary<string, int> GetNeighborTerrain(int row, int col)
    {
        var neighborTerrain = new Dictionary<string, int>();
        if (_mapData == null) return neighborTerrain;

        foreach (var (name, dr, dc) in GetHexDirections(col))
        {
            int nr = row + dr;
            int nc = col + dc;

            if (nr >= 0 && nr < _mapData.MapHeight && nc >= 0 && nc < _mapData.MapWidth)
                neighborTerrain[name] = GetTerrainTypeByIndex(nr * _mapData.MapWidth + nc);
            else
                neighborTerrain[name] = 1;
        }

        return neighborTerrain;
    }

    /// <summary>根据邻居海陆关系确定建筑外观值（对齐 VB DetermineAppearanceByNeighbors）</summary>
    private int DetermineAppearanceByNeighbors(Dictionary<string, int> neighborTerrain)
    {
        bool Land(string key) => neighborTerrain.TryGetValue(key, out int t) && t != 1;

        var validAppearances = new List<int>();

        // 第一轮：严格的 and 条件
        if (Land("up") && Land("up_right")) validAppearances.Add(1);
        if (Land("up") && Land("up_left")) validAppearances.Add(2);
        if (Land("down") && Land("down_left")) validAppearances.Add(3);
        if (Land("down") && Land("down_right")) validAppearances.Add(4);

        if (validAppearances.Count > 0)
            return validAppearances[_random.Next(validAppearances.Count)];

        // 第二轮：宽松的 or 条件
        validAppearances.Clear();
        if (Land("up") || Land("up_right")) validAppearances.Add(1);
        if (Land("up") || Land("up_left")) validAppearances.Add(2);
        if (Land("down") || Land("down_left")) validAppearances.Add(3);
        if (Land("down") || Land("down_right")) validAppearances.Add(4);

        if (validAppearances.Count > 0)
            return validAppearances[_random.Next(validAppearances.Count)];

        return _random.Next(1, 5);
    }

    #endregion

    /// <summary>
    /// 按归属概率在省区格子上生成建筑（对齐 VB ExecuteBuildingProbabilityGeneration）。
    /// targetBelongId = -1 表示对全部归属生效。
    /// </summary>
    public ModifierResult GenerateBuildingsByBelongProbability(int targetBelongId, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        probability = Math.Clamp(probability, 0, 100);

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();

        // 建筑类型以 setting.txt 的 CommonBuilding 为准，空则回退默认
        var commonBuildingTypes = buildingConfig?.CommonBuilding is { Count: > 0 }
            ? buildingConfig.CommonBuilding
            : new List<int> { 2, 3, 21, 22, 31 };
        var seaBuildingTypes = buildingConfig?.BuildingOnSea ?? new List<int>();

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        // 省区值 -> 省会归属ID
        var provinceBelongMap = new Dictionary<int, int>();
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                var province = _mapData.GetProvinceRef(index);

                // 省会：省区值等于格子索引
                if (province.ProvinceValue == index)
                {
                    int belongId = _mapData.GetBelongValueByIndex(index);
                    if (belongId >= 0 && belongId != 0xFF)
                        provinceBelongMap[province.ProvinceValue] = belongId;
                }
            }
        }

        int generatedCount = 0;
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;

                if (_mapData.FindBuildingIndex(col, row) >= 0) continue;

                var province = _mapData.GetProvinceRef(index);
                int provinceValue = province.ProvinceValue;

                // 跳过空白省区
                if (provinceValue == 0 || provinceValue == 0xFFFF) continue;

                if (!provinceBelongMap.TryGetValue(provinceValue, out int provinceBelongId)) continue;
                if (targetBelongId != -1 && provinceBelongId != targetBelongId) continue;

                if (_random.Next(1, 101) > probability) continue;

                // 海陆地形决定可用的建筑类型
                bool isOcean = GetTerrainTypeByIndex(index) == 1;
                var available = new List<int>();
                foreach (var bt in commonBuildingTypes)
                {
                    if (isOcean == seaBuildingTypes.Contains(bt)) available.Add(bt);
                }
                if (available.Count == 0) continue;

                int buildingType = available[_random.Next(available.Count)];
                if (CreateBuildingAtIndex(index, buildingType, provinceBelongId))
                    generatedCount++;
            }
        }

        if (generatedCount > 0) MarkModified();

        return ModifierResult.Ok(targetBelongId == -1
            ? $"已按概率 {probability}% 在所有归属的省区格子上生成 {generatedCount} 个建筑"
            : $"已按概率 {probability}% 在归属为 {targetBelongId} 的省区格子上生成 {generatedCount} 个建筑");
    }

    /// <summary>
    /// 在指定格子索引创建建筑（对齐 VB CreateBuildingAtIndex）。
    /// belongId 为 null 时按"省会格子归属"自动设置。
    /// </summary>
    private bool CreateBuildingAtIndex(int index, int buildingType, int? belongId)
    {
        if (_mapData == null) return false;

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var needDirection = buildingConfig?.BuildingNeedDirection is { Count: > 0 }
            ? buildingConfig.BuildingNeedDirection
            : new List<int> { 31, 32, 33, 34 };

        // Coordinate 是 int，不能转 short：地图格子索引超过 32767 会被截断成错误坐标
        var building = Building.CreateDefault(index);
        building.BuildingType = (byte)buildingType;
        building.Name = 0;
        building.FactoryLevel = 0;
        building.ResearchLevel = 0;
        building.MedicalLevel = 0;
        building.AviationLevel = 0;
        building.MissileLevel = 0;
        building.NuclearLevel = 0;

        // 仅对需要方向的建筑类型随机外观值（1-4）
        building.Appearance = needDirection.Contains(buildingType)
            ? (byte)_random.Next(1, 5)
            : (byte)0;

        _mapData.Buildings.Add(building);

        int col = index % _mapData.MapWidth;
        int row = index / _mapData.MapWidth;

        if (belongId.HasValue)
        {
            _mapData.SetBelongValue(col, row, belongId.Value);
        }
        else
        {
            // 自动检测：格子有省区时使用省会格子的归属
            int provinceBelong = _mapData.GetProvinceCapitalBelong(col, row);
            if (provinceBelong >= 0)
                _mapData.SetBelongValue(col, row, provinceBelong);
        }

        return true;
    }

    private int GetTerrainTypeByIndex(int hexIndex)
    {
        if (_mapData == null) return 1;
        if (hexIndex < 0 || hexIndex >= _mapData.TerrainCount) return 1;
        return _mapData.GetTerrainRef(hexIndex).TileType1;
    }

    /// <summary>
    /// 把建筑从源格子移动到目标格子（对齐 VB 的建筑移动工具）。
    /// 目标格已有建筑时先删除它；同时剪切归属值（源格归属移到目标格，源格置 0xFF 无归属）。
    /// </summary>
    public ModifierResult MoveBuilding(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        if (fromCol < 0 || fromCol >= mapWidth || fromRow < 0 || fromRow >= mapHeight)
            return ModifierResult.Fail("源坐标超出范围");
        if (toCol < 0 || toCol >= mapWidth || toRow < 0 || toRow >= mapHeight)
            return ModifierResult.Fail("目标坐标超出范围");
        if (fromCol == toCol && fromRow == toRow)
            return ModifierResult.Fail("源与目标为同一格");

        int sourceIdx = _mapData.FindBuildingIndex(fromCol, fromRow);
        if (sourceIdx < 0) return ModifierResult.Fail("源格子没有建筑");

        // Building 是 struct，先取副本；后续删除会改变集合索引
        var movingBuilding = _mapData.Buildings[sourceIdx];

        // 目标格已有建筑 → 先删除
        int existingIdx = _mapData.FindBuildingIndex(toCol, toRow);
        if (existingIdx >= 0) _mapData.RemoveBuildingAt(existingIdx);

        // 归属剪切：源格归属移到目标格，源格清空为无归属
        int sourceBelong = _mapData.GetBelongValue(fromCol, fromRow);
        if (sourceBelong >= 0)
        {
            _mapData.SetBelongValue(toCol, toRow, sourceBelong);
            _mapData.SetBelongValue(fromCol, fromRow, 0xFF);
        }

        // 移动建筑本体（删除后索引可能变化，重新查源格）
        movingBuilding.Coordinate = toRow * mapWidth + toCol;
        int sourceIdxNow = _mapData.FindBuildingIndex(fromCol, fromRow);
        if (sourceIdxNow >= 0)
            _mapData.ReplaceBuilding(sourceIdxNow, movingBuilding);
        else
            _mapData.Buildings.Add(movingBuilding);

        MarkModified();
        return ModifierResult.Ok($"建筑已移动到 ({toCol}, {toRow})，归属已剪切");
    }

    private void RandomizeBuildingLevels(ref Building building)
    {
        building.FactoryLevel = (byte)_random.Next(0, 5);
        building.ResearchLevel = (byte)_random.Next(0, 5);
        building.MedicalLevel = (byte)_random.Next(0, 5);
        building.AviationLevel = (byte)_random.Next(0, 5);
        building.MissileLevel = (byte)_random.Next(0, 5);
        building.NuclearLevel = (byte)_random.Next(0, 5);
        building.HatredValue = (byte)_random.Next(0, 256);
    }
}