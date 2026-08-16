using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class LegionModifier : ModifierBase
{
    public override string Name => "legion";
    public override string DisplayName => "军团修改器";
    public override string HelpText =>
        "军团修改器快捷键:\n" +
        "Q - 选择军团ID\n" +
        "左键 - 选择格子查看军团\n" +
        "右键 - 设置军团领域\n" +
        "S - 打开军团设置\n" +
        "R - 随机化军团等级与经济";

    private int _selectedLegionId = 1;

    public int SelectedLegionId
    {
        get => _selectedLegionId;
        set => _selectedLegionId = Math.Clamp(value, 1, 8);
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int legionId = parameter switch
        {
            int id => id,
            Legion l => l.CountryId,
            _ => _selectedLegionId
        };

        _mapData.GetProvinceRef(col, row).CountryId = (byte)Math.Clamp(legionId, 0, 255);
        MarkModified();
        return ModifierResult.Ok($"已设置军团领域 ({col}, {row}) = 军团 {legionId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.GetProvinceRef(col, row).CountryId = 0xFF;
        MarkModified();
        return ModifierResult.Ok($"已清除军团领域 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        byte countryId = _mapData!.GetProvinceRef(col, row).CountryId;
        int idx = _mapData.FindLegionIndex(countryId);
        return idx >= 0 ? _mapData.Legions[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (data is Legion legion)
        {
            return UpdateLegion(legion).Success;
        }
        return false;
    }

    public ModifierResult UpdateLegion(Legion legion)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindLegionIndex(legion.CountryId);
        if (idx >= 0)
            _mapData.ReplaceLegion(idx, legion);
        else
            _mapData.Legions.Add(legion);

        MarkModified();
        return ModifierResult.Ok($"已更新军团 {legion.CountryId}");
    }

    public Legion? GetLegion(int legionId)
    {
        int idx = _mapData?.FindLegionIndex(legionId) ?? -1;
        return idx >= 0 ? _mapData!.Legions[idx] : null;
    }

    public ModifierResult SetLegionColor(int legionId, byte r, byte g, byte b)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail($"未找到军团 {legionId}");

        var legion = _mapData.Legions[idx];
        legion.ColorR = r;
        legion.ColorG = g;
        legion.ColorB = b;
        _mapData.ReplaceLegion(idx, legion);
        MarkModified();
        return ModifierResult.Ok($"已设置军团 {legionId} 颜色");
    }

    public ModifierResult SetLegionActionId(int legionId, int actionId)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail($"未找到军团 {legionId}");

        var legion = _mapData.Legions[idx];
        legion.ActionId = actionId;
        _mapData.ReplaceLegion(idx, legion);
        MarkModified();
        return ModifierResult.Ok($"已设置军团 {legionId} ActionId");
    }
}