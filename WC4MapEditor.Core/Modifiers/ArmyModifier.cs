using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ArmyModifier : ModifierBase
{
    public override string Name => "army";
    public override string DisplayName => "单位修改器";
    public override string HelpText =>
        "单位修改器快捷键:\n" +
        "左键 - 选择单位\n" +
        "右键 - 放置/编辑单位\n" +
        "Delete - 删除选中单位\n" +
        "C - 复制单位\n" +
        "V - 粘贴单位\n" +
        "E - 橡皮擦模式";

    private Army? _copiedArmy;

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        short coordIndex = (short)(row * _mapData.MapWidth + col);

        Army army;
        if (parameter is Army a)
        {
            army = a;
            army.Coordinate = coordIndex;
        }
        else
        {
            army = Army.CreateDefault(coordIndex);
        }

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return ModifierResult.Ok($"已放置单位 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");

        _mapData.RemoveArmyAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除单位 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindArmyIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindArmyIndex(col, row);
        return idx >= 0 ? _mapData.Armies[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Army army) return false;

        army.Coordinate = (short)(row * _mapData.MapWidth + col);
        int idx = _mapData.FindArmyIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return true;
    }

    public ModifierResult CopyArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");
        _copiedArmy = _mapData.Armies[idx];
        return ModifierResult.Ok("已复制单位数据");
    }

    public ModifierResult PasteArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedArmy == null) return ModifierResult.Fail("没有已复制的单位数据");
        return Apply(col, row, _copiedArmy);
    }

    public ModifierResult UpdateArmy(Army army)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(army.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindArmyIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return ModifierResult.Ok("已更新单位数据");
    }

    public ModifierResult SetLegionId(int col, int row, int legionId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");

        var army = _mapData.Armies[idx];
        army.LegionId = legionId;
        _mapData.ReplaceArmy(idx, army);
        MarkModified();
        return ModifierResult.Ok($"已设置军团ID: {legionId}");
    }
}