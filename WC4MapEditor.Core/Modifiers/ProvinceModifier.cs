using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ProvinceModifier : ModifierBase
{
    public override string Name => "province";
    public override string DisplayName => "省份修改器";
    public override string HelpText =>
        "省份修改器快捷键:\n" +
        "左键 - 选择格子\n" +
        "右键 - 设置省份值\n" +
        "H - 使用画笔\n" +
        "C - 复制省份值\n" +
        "V - 粘贴省份值\n" +
        "Q - 选择军团/国家ID\n" +
        "B - 画笔模式开关";

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

        _mapData.GetProvinceRef(col, row).CountryId = 0xFF;
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

    public ModifierResult CopyProvinceValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedCountryId = _mapData!.GetProvinceRef(col, row).CountryId;
        return ModifierResult.Ok($"已复制省份值: {_copiedCountryId}");
    }

    public ModifierResult PasteProvinceValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.GetProvinceRef(col, row).CountryId = _copiedCountryId;
        MarkModified();
        return ModifierResult.Ok($"已粘贴省份值: {_copiedCountryId}");
    }

    public ModifierResult SetProvinceByCountryId(int col, int row, int countryId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.GetProvinceRef(col, row).CountryId = (byte)Math.Clamp(countryId, 0, 255);
        MarkModified();
        return ModifierResult.Ok($"已设置省份归属 ({col}, {row}) = 国家ID {countryId}");
    }
}