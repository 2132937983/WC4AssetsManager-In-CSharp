using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class BelongModifier : ModifierBase
{
    public override string Name => "belong";
    public override string DisplayName => "归属修改器";
    public override string HelpText =>
        "归属修改器快捷键:\n" +
        "左键 - 选择格子查看归属\n" +
        "右键 - 设置归属\n" +
        "Q - 选择军团\n" +
        "H - 画笔模式\n" +
        "C - 复制归属值\n" +
        "V - 粘贴归属值";

    private byte _copiedCountryId;
    private int _selectedCountryId;

    public int SelectedCountryId
    {
        get => _selectedCountryId;
        set => _selectedCountryId = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte countryId = parameter switch
        {
            byte b => b,
            int id => (byte)Math.Clamp(id, 0, 255),
            _ => (byte)Math.Clamp(_selectedCountryId, 0, 255)
        };

        _mapData.GetProvinceRef(col, row).CountryId = countryId;
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = {countryId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.GetProvinceRef(col, row).CountryId = 0xFF;
        MarkModified();
        return ModifierResult.Ok($"已清除归属 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return (int)_mapData!.GetProvinceRef(col, row).CountryId;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        ref Province province = ref _mapData.GetProvinceRef(col, row);

        switch (data)
        {
            case byte b:
                province.CountryId = b;
                break;
            case int i:
                province.CountryId = (byte)Math.Clamp(i, 0, 255);
                break;
            default:
                return false;
        }

        MarkModified();
        return true;
    }

    public ModifierResult CopyBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedCountryId = _mapData!.GetProvinceRef(col, row).CountryId;
        return ModifierResult.Ok($"已复制归属值: {_copiedCountryId}");
    }

    public ModifierResult PasteBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.GetProvinceRef(col, row).CountryId = _copiedCountryId;
        MarkModified();
        return ModifierResult.Ok($"已粘贴归属值: {_copiedCountryId}");
    }

    public int? GetBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetProvinceRef(col, row).CountryId;
    }
}