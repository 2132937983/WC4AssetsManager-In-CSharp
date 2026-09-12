using WC4MapEditor.Models;
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

    public int RandomizeNamedBuildingTypes()
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.Name != 0 && building.Name != -1)
            {
                building.BuildingType = (byte)DefaultRandomTypes[_random.Next(DefaultRandomTypes.Length)];
                RandomizeBuildingLevels(ref building);
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();
        return count;
    }

    public int RandomizeUnnamedBuildingTypes()
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.Name == 0 || building.Name == -1)
            {
                building.BuildingType = (byte)DefaultRandomTypes[_random.Next(DefaultRandomTypes.Length)];
                RandomizeBuildingLevels(ref building);
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();
        return count;
    }

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
        var randomTypes = buildingConfig.BuildingRandomCharacter;
        if (randomTypes == null || randomTypes.Count == 0)
            randomTypes = new List<int>(DefaultRandomTypes);

        var buildingNeedDirection = buildingConfig.BuildingNeedDirection;
        if (buildingNeedDirection == null || buildingNeedDirection.Count == 0)
            buildingNeedDirection = new List<int> { 31, 32, 33, 34 };

        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            var province = mapData.GetProvinceRef(i);
            if (province.ProvinceValue != i || province.ProvinceValue == 0xFFFF)
                continue;

            var coord = HexCoord.FromIndex(i, _mapData.MapWidth);
            if (_mapData.FindBuildingIndex(coord.Col, coord.Row) >= 0)
                continue;

            int belongId = _mapData.GetBelongValueByIndex(i);
            if (belongId < 0 || belongId == 0xFF)
                continue;

            var building = Building.CreateDefault((short)i);
            building.BuildingType = (byte)randomTypes[_random.Next(randomTypes.Count)];
            if (buildingNeedDirection.Contains(building.BuildingType))
                building.Appearance = (byte)_random.Next(1, 5);
            _mapData.Buildings.Add(building);

            int provinceBelong = _mapData.GetBelongValueByIndex(province.ProvinceValue);
            if (provinceBelong >= 0 && provinceBelong != 0xFF)
                _mapData.SetBelongValueByIndex(i, provinceBelong);

            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    public int SmartSetBuildingAppearance(MapData mapData)
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.BuildingType > 0 && building.Appearance == 0)
            {
                building.Appearance = (byte)(_random.Next(0, 4) + 1);
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();
        return count;
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