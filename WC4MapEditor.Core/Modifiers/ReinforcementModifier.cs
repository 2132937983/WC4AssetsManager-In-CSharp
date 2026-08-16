using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ReinforcementModifier : ModifierBase
{
    public override string Name => "reinforcement";
    public override string DisplayName => "援军修改器";
    public override string HelpText =>
        "援军修改器快捷键:\n" +
        "左键 - 选择援军\n" +
        "右键 - 放置/编辑援军\n" +
        "Delete - 删除选中援军\n" +
        "C - 复制援军\n" +
        "V - 粘贴援军\n" +
        "Q - 选择军团";

    private Reinforcement? _copiedReinforcement;
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

        int coordIndex = row * _mapData.MapWidth + col;

        Reinforcement reinforcement;
        if (parameter is Reinforcement r)
        {
            reinforcement = r;
            reinforcement.Coordinate = coordIndex;
        }
        else
        {
            reinforcement = Reinforcement.CreateDefault();
            reinforcement.Coordinate = coordIndex;
            reinforcement.OwnerCountry = _selectedLegionId;
        }

        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok($"已放置援军 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有援军");

        _mapData.RemoveReinforcementAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除援军 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindReinforcementIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindReinforcementIndex(col, row);
        return idx >= 0 ? _mapData.Reinforcements[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Reinforcement reinforcement) return false;

        reinforcement.Coordinate = row * _mapData.MapWidth + col;
        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return true;
    }

    public ModifierResult CopyReinforcement(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindReinforcementIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有援军");
        _copiedReinforcement = _mapData.Reinforcements[idx];
        return ModifierResult.Ok("已复制援军数据");
    }

    public ModifierResult PasteReinforcement(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedReinforcement == null) return ModifierResult.Fail("没有已复制的援军数据");
        return Apply(col, row, _copiedReinforcement);
    }

    public ModifierResult UpdateReinforcement(Reinforcement reinforcement)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(reinforcement.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindReinforcementIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok("已更新援军数据");
    }
}