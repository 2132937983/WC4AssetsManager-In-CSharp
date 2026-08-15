namespace WC4MapEditor.Parsers.Conquest;

/// <summary>
/// 征服模式偏移量计算器。
/// 征服文件结构：Legion → Province → Belong → Building → [Army, Trap, Case, ...]
/// 注意：征服模式没有 Terrain 数据。
/// </summary>
public static class ConquestOffsets
{
    private const int LegionStart = 0x80;
    private const int LegionSize = 300;
    private const int ProvinceSize = 2;
    private const int BelongSize = 1;
    private const int BuildingSize = 32;

    /// <summary>
    /// 计算征服文件中各地块的起始偏移量。
    /// </summary>
    public static (int province, int belong, int building, int dataEnd) Calculate(
        int legionCount, int selectableTileCount, int buildingCount)
    {
        int provinceStart = LegionStart + (legionCount * LegionSize);
        int belongStart = provinceStart + (selectableTileCount * ProvinceSize);
        int buildingStart = belongStart + (selectableTileCount * BelongSize);
        int dataEnd = buildingStart + (buildingCount * BuildingSize);

        return (provinceStart, belongStart, buildingStart, dataEnd);
    }
}
