using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class TrapModifier : ModifierBase
{
    public override string Name => "trap";
    public override string DisplayName => "陷阱修改器";
    public override string HelpText =>
        "陷阱修改器快捷键:\n" +
        "左键 - 选择陷阱\n" +
        "右键 - 放置/编辑陷阱\n" +
        "Delete - 删除选中陷阱\n" +
        "C - 复制陷阱\n" +
        "V - 粘贴陷阱\n" +
        "Q - 选择军团";

    private Trap? _copiedTrap;
    private int _selectedLegionId;

    public int SelectedLegionId
    {
        get => _selectedLegionId;
        set => _selectedLegionId = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        short coordIndex = (short)(row * _mapData.MapWidth + col);

        Trap trap;
        if (parameter is Trap t)
        {
            trap = t;
            trap.Coordinate = coordIndex;
        }
        else
        {
            trap = Trap.CreateDefault(coordIndex);
            trap.LegionId = (short)_selectedLegionId;
        }

        int idx = _mapData.FindTrapIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return ModifierResult.Ok($"已放置陷阱 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindTrapIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有陷阱");

        _mapData.RemoveTrapAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除陷阱 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindTrapIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindTrapIndex(col, row);
        return idx >= 0 ? _mapData.Traps[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Trap trap) return false;

        trap.Coordinate = (short)(row * _mapData.MapWidth + col);
        int idx = _mapData.FindTrapIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return true;
    }

    public ModifierResult CopyTrap(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindTrapIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有陷阱");
        _copiedTrap = _mapData.Traps[idx];
        return ModifierResult.Ok("已复制陷阱数据");
    }

    public ModifierResult PasteTrap(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedTrap == null) return ModifierResult.Fail("没有已复制的陷阱数据");
        return Apply(col, row, _copiedTrap);
    }

    public ModifierResult UpdateTrap(Trap trap)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(trap.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindTrapIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return ModifierResult.Ok("已更新陷阱数据");
    }
}