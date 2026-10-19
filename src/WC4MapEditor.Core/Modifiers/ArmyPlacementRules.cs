using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 放置单位 / 陷阱时确定归属的规则：单位模块（v1 / v3）、陷阱模块与单位部署模式共用一份，
/// 不再各写各的（以前放置单位、放置陷阱、批量生成陷阱三处的优先级互相矛盾）。
/// <para>
/// 归属值约定 = 军团在 <see cref="MapData.Legions"/> 里的下标（见 <see cref="MapData.ResolveLegionByBelong"/>），
/// 0xFF 表示无归属。这里统一把"无归属 / 越界 / 无省份"折算成 -1，调用方只需判断 &gt;= 0。
/// </para>
/// </summary>
public static class ArmyPlacementRules
{
    /// <summary>格子归属里的"无归属"值。</summary>
    public const int NoOwner = 0xFF;

    /// <summary>
    /// 格子所在省份的省会格子归属；无省份、省会无归属（0xFF）或越界时返回 -1。
    /// 以前直接用 <see cref="MapData.GetProvinceCapitalBelong"/> 的结果判断 &gt;= 0，
    /// 省会无归属时会把 0xFF 当成归属写进格子（把原有归属抹成"无"）。
    /// </summary>
    public static int CapitalBelong(MapData map, int col, int row)
        => Normalize(map.GetProvinceCapitalBelong(col, row));

    /// <summary>格子自身的归属；无归属或不在归属数据范围内（征服地图 MapClip 以外）时返回 -1。</summary>
    public static int CellBelong(MapData map, int col, int row)
        => Normalize(map.GetBelongValue(col, row));

    /// <summary>格子的推断归属：省会归属优先，其次格子自身归属，都没有时返回 -1。</summary>
    public static int ResolveOwner(MapData map, int col, int row)
    {
        int capital = CapitalBelong(map, col, row);
        return capital >= 0 ? capital : CellBelong(map, col, row);
    }

    /// <summary>
    /// 省份的归属：ProvinceValue 即省会格子的地图下标，取省会格子的归属；
    /// ProvinceValue 为 0 / 0xFFFF（无省份）、越界或省会无归属时返回 -1。
    /// </summary>
    public static int ProvinceBelong(MapData map, int provinceValue)
    {
        if (!IsProvince(provinceValue) || provinceValue >= map.MapWidth * map.MapHeight) return -1;
        var capital = HexCoord.FromIndex(provinceValue, map.MapWidth);
        return CellBelong(map, capital.Col, capital.Row);
    }

    /// <summary>省份值是否表示一个省份（0 与 0xFFFF 都表示"无省份"，与 <see cref="MapData.GetProvinceCapitalBelong"/> 一致）。</summary>
    public static bool IsProvince(int provinceValue) => provinceValue != 0 && provinceValue != 0xFFFF;

    /// <summary>第一层地形里表示海洋的地块类型（0 是普通陆地，游戏地图里的单位 / 陷阱 / 建筑大多在 0 上）。</summary>
    public const byte SeaTile = 1;

    /// <summary>
    /// 地图是否带地形数据：征服地图不存地形，所有格子都是默认的 1（海洋），这时无法区分海陆。
    /// 批量操作开始时取一次，再交给 <see cref="IsSea"/>。
    /// </summary>
    public static bool HasLandTiles(MapData map)
    {
        int total = Math.Min(map.TerrainCount, map.MapWidth * map.MapHeight);
        for (int i = 0; i < total; i++)
            if (map.GetTerrainRef(i).TileType1 != SeaTile) return true;
        return false;
    }

    /// <summary>格子是否是海洋。地图没有地形数据（<paramref name="mapHasLand"/> 为 false）时一律按陆地处理。</summary>
    public static bool IsSea(MapData map, int cell, bool mapHasLand)
        => mapHasLand && cell >= 0 && cell < map.TerrainCount && map.GetTerrainRef(cell).TileType1 == SeaTile;

    private static int Normalize(int belong) => belong is >= 0 and < NoOwner ? belong : -1;
}
