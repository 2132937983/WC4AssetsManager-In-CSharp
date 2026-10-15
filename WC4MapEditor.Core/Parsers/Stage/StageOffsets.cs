namespace WC4MapEditor.Core.Parsers.Stage;

/// <summary>
/// 战役模式偏移量计算器。
/// 战役文件结构：Legion → Terrain → Province → Belong → Building → [Army, Trap, Case, ...]
/// </summary>
public static class StageOffsets
{
    private const int LegionStart = 0x80;
    private const int LegionSize = 300;
    private const int TerrainSize = 16;
    private const int ProvinceSize = 2;
    private const int BelongSize = 1;
    private const int BuildingSize = 32;

    /// <summary>
    /// 计算战役文件中各地块的起始偏移量。
    /// </summary>
    public static (int terrain, int province, int belong, int building, int dataEnd) Calculate(
        int legionCount, int selectableTileCount, int buildingCount)
    {
        int terrainStart = LegionStart + (legionCount * LegionSize);
        int provinceStart = terrainStart + (selectableTileCount * TerrainSize);
        int belongStart = provinceStart + (selectableTileCount * ProvinceSize);
        int buildingStart = belongStart + (selectableTileCount * BelongSize);
        int dataEnd = buildingStart + (buildingCount * BuildingSize);

        return (terrainStart, provinceStart, belongStart, buildingStart, dataEnd);
    }
}
