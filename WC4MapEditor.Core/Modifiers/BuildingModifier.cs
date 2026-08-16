using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class BuildingModifier : ModifierBase
{
    public override string Name => "building";
    public override string DisplayName => "建筑修改器";
    public override string HelpText =>
        "建筑修改器快捷键:\n" +
        "左键 - 选择建筑\n" +
        "右键 - 放置/编辑建筑\n" +
        "Delete - 删除选中建筑\n" +
        "C - 复制建筑\n" +
        "V - 粘贴建筑\n" +
        "N - 显示/隐藏建筑名称";

    private Building? _copiedBuilding;

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        short coordIndex = (short)(row * _mapData.MapWidth + col);

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
            _mapData.Buildings.Add(building);

        MarkModified();
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

        building.Coordinate = (short)(row * _mapData.MapWidth + col);
        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
            _mapData.Buildings.Add(building);

        MarkModified();
        return true;
    }

    public ModifierResult CopyBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有建筑");
        _copiedBuilding = _mapData.Buildings[idx];
        return ModifierResult.Ok("已复制建筑数据");
    }

    public ModifierResult PasteBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedBuilding == null) return ModifierResult.Fail("没有已复制的建筑数据");
        return Apply(col, row, _copiedBuilding);
    }

    public ModifierResult UpdateBuilding(Building building)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindBuildingIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
            _mapData.Buildings.Add(building);

        MarkModified();
        return ModifierResult.Ok("已更新建筑数据");
    }
}