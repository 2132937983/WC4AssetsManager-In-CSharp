using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 空袭 / 空中支援模块：两张全局列表（<see cref="MapData.AirForces"/>、<see cref="MapData.AirSupports"/>）的增删改，
/// 在机场上批量生成空袭、按军团随机生成空中支援。
/// <para>
/// 空袭挂在机场（建筑所在格）上，空中支援没有坐标；两者的军团字段都是军团 ActionId。
/// 本模块只改数据、不记撤销：调用方（模式 / 列表窗口 / 命令行）用 RecordCollectionChange 包住对应列表。
/// </para>
/// </summary>
public sealed class AirForceModifier : ModifierBase
{
    /// <summary>回合字段的上限（列表窗口、生成对话框与命令行共用的校验口径）。</summary>
    public const int MaxRound = 9999;

    /// <summary>批量生成时每个军团最多生成的条数（VB 不设上限，一次输入就能生成"数量 × 军团数"条）。</summary>
    public const int MaxCountPerLegion = 100;

    /// <summary>军团值 -1 = 为每个军团各生成一份。</summary>
    public const int AllLegions = -1;

    public override string Name => "airforce";
    public override string DisplayName => Loc.T("Module.AirForce.Name", "空军修改器");

    private AirForce? _copiedAirForce;
    private AirSupport? _copiedAirSupport;
    private readonly Random _random = new();

    #region IModifier

    /// <summary>在格子上新增一条空袭：传入 <see cref="AirForce"/> 时按原样（坐标改为该格），否则用默认值、军团取该格归属。</summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.F("Module.AirForce.Msg.CellOutOfMap", col, row));

        int coordinate = row * _mapData.MapWidth + col;
        var airForce = parameter is AirForce af ? af : AirForce.CreateDefault(coordinate, DefaultLegionAt(col, row));
        airForce.Coordinate = coordinate;
        return AddAirForce(airForce);
    }

    /// <summary>删除该格上的全部空袭。</summary>
    public override ModifierResult Remove(int col, int row) => DeleteAirForcesAt(new[] { new HexCoord(col, row) });

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => CountAirForcesAt(col, row) > 0;

    /// <summary>该格上的空袭（没有时为 null）。</summary>
    public override object? GetDataAt(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return null;
        int coordinate = row * _mapData.MapWidth + col;
        var list = _mapData.AirForces.Where(a => a.Coordinate == coordinate).ToList();
        return list.Count > 0 ? list : null;
    }

    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 校验与查询

    /// <summary>格子索引（row * MapWidth + col）是否在地图内。</summary>
    public bool IsValidCoordinate(int coordinate)
        => _mapData != null && coordinate >= 0 && coordinate < _mapData.MapWidth * _mapData.MapHeight;

    /// <summary>
    /// 军团值是否可用：地图上有该 ActionId 的军团。地图没有任何军团时只接受 0（与"每个军团"在无军团时退回 0 一致）。
    /// </summary>
    public bool IsValidLegion(int actionId)
    {
        if (_mapData == null) return false;
        if (_mapData.Legions.Count == 0) return actionId == 0;
        return _mapData.Legions.Any(l => l.ActionId == actionId);
    }

    /// <summary>
    /// 新建条目的默认军团：0（与 VB 一致）；0 不是现有军团时（征服地图的 ActionId 常从 1 开始）用第一个军团。
    /// </summary>
    public int DefaultLegion()
    {
        if (_mapData == null || _mapData.Legions.Count == 0 || IsValidLegion(0)) return 0;
        return _mapData.Legions[0].ActionId;
    }

    /// <summary>
    /// 在某格新建条目时的默认军团：该格的归属值是现有军团时用它（内存里的归属值与 ActionId 同一套编号），
    /// 否则同 <see cref="DefaultLegion"/>。
    /// </summary>
    public int DefaultLegionAt(int col, int row)
    {
        if (_mapData != null && IsValidCoord(col, row))
        {
            int belong = _mapData.GetBelongValue(col, row);
            if (belong != 0xFF && IsValidLegion(belong)) return belong;
        }
        return DefaultLegion();
    }

    /// <summary>该格上的空袭条数。</summary>
    public int CountAirForcesAt(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return 0;
        int coordinate = row * _mapData.MapWidth + col;
        return _mapData.AirForces.Count(a => a.Coordinate == coordinate);
    }

    /// <summary>该格的建筑带机场（航空等级 &gt; 0）时返回该建筑，否则 null。</summary>
    public Building? GetAirport(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return null;
        int index = _mapData.FindBuildingIndex(col, row);
        if (index < 0) return null;
        var building = _mapData.Buildings[index];
        return building.AviationLevel > 0 ? building : null;
    }

    /// <summary>
    /// 从目标格里挑出带机场的建筑格（保持原顺序）。一个都没有时返回失败：只有一格时说明原因
    /// （没有建筑 / 建筑没有机场，与 VB 的提示一致），多格时统一提示。
    /// 生成前先调它，界面就能在弹出参数对话框之前提示用户。
    /// </summary>
    public ModifierResult FindAirports(IReadOnlyList<HexCoord> cells, out IReadOnlyList<HexCoord> airports)
    {
        airports = cells.Where(c => GetAirport(c.Col, c.Row) != null).ToList();
        if (_mapData == null) return NotReady();
        if (airports.Count > 0) return ModifierResult.Ok(count: airports.Count);
        if (cells.Count != 1) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirportInCells", "选中的格子里没有带机场的建筑"));

        var (col, row) = (cells[0].Col, cells[0].Row);
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.F("Module.AirForce.Msg.CellOutOfMap", col, row));
        return _mapData.FindBuildingIndex(col, row) < 0
            ? ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoBuilding", "选中的格子没有建筑"))
            : ModifierResult.Fail(Loc.F("Module.AirForce.Msg.NoAirport", col, row));
    }

    /// <summary>校验一条空袭的字段；合法时返回 null，否则返回原因。</summary>
    public string? ValidateAirForce(in AirForce airForce)
    {
        if (_mapData == null) return NotReady().Message;
        if (!IsValidCoordinate(airForce.Coordinate))
            return Loc.F("Module.AirForce.Msg.CoordinateOutOfMap", airForce.Coordinate, _mapData.MapWidth, _mapData.MapHeight);
        return ValidateCommon(airForce.UnitType, airForce.AmmoType, airForce.OwnerLegion, airForce.TriggerRound);
    }

    /// <summary>校验一条空中支援的字段；合法时返回 null，否则返回原因。</summary>
    public string? ValidateAirSupport(in AirSupport airSupport)
    {
        if (_mapData == null) return NotReady().Message;
        return ValidateCommon(airSupport.AirForceSequence, airSupport.AmmoType, airSupport.OwnerLegion, airSupport.TriggerRound);
    }

    private string? ValidateCommon(int type, int ammo, int legion, int round)
    {
        if (type < 0 || type > AirForceCatalog.MaxCode)
            return Loc.F("Module.AirForce.Msg.TypeOutOfRange", type, AirForceCatalog.MaxCode);
        if (ammo < 0 || ammo > AirForceCatalog.MaxCode)
            return Loc.F("Module.AirForce.Msg.AmmoOutOfRange", ammo, AirForceCatalog.MaxCode);
        if (round < 0 || round > MaxRound)
            return Loc.F("Module.AirForce.Msg.RoundOutOfRange", round, MaxRound);
        if (!IsValidLegion(legion))
            return Loc.F("Module.AirForce.Msg.LegionNotFound", legion);
        return null;
    }

    #endregion

    #region 空袭增删改

    public ModifierResult AddAirForce(AirForce airForce)
    {
        if (_mapData == null) return NotReady();
        if (ValidateAirForce(airForce) is { } error) return ModifierResult.Fail(error);

        _mapData.AirForces.Add(airForce);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirForceAdded", airForce.GetUnitTypeName()));
    }

    public ModifierResult UpdateAirForce(int index, AirForce airForce)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirForces.Count) return AirForceIndexOutOfRange(index);
        if (ValidateAirForce(airForce) is { } error) return ModifierResult.Fail(error);

        string message = Loc.F("Module.AirForce.Msg.AirForceUpdated", airForce.GetUnitTypeName());
        // 没有变化时不写回：不触发列表替换通知，也不把地图标成已修改
        if (EqualityComparer<AirForce>.Default.Equals(_mapData.AirForces[index], airForce)) return ModifierResult.Ok(message, 0);

        _mapData.AirForces[index] = airForce;
        MarkModified();
        return ModifierResult.Ok(message);
    }

    /// <summary>复制第 <paramref name="index"/> 条空袭，副本插在它后面。</summary>
    public ModifierResult DuplicateAirForce(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirForces.Count) return AirForceIndexOutOfRange(index);

        _mapData.AirForces.Insert(index + 1, _mapData.AirForces[index]);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirForceDuplicated", index + 1));
    }

    public ModifierResult DeleteAirForce(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirForces.Count) return AirForceIndexOutOfRange(index);

        _mapData.AirForces.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.AirForce.Msg.AirForceDeleted", "已删除空袭数据"));
    }

    /// <summary>删除这些格子上的全部空袭。</summary>
    public ModifierResult DeleteAirForcesAt(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return NotReady();

        int width = _mapData.MapWidth;
        var coordinates = cells.Where(c => IsValidCoord(c.Col, c.Row)).Select(c => c.Row * width + c.Col).ToHashSet();
        int removed = 0;
        for (int i = _mapData.AirForces.Count - 1; i >= 0; i--)
        {
            if (!coordinates.Contains(_mapData.AirForces[i].Coordinate)) continue;
            _mapData.AirForces.RemoveAt(i);
            removed++;
        }

        if (removed == 0) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirForceAtCells", "选中的格子上没有空袭"));
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirForcesDeletedAt", removed), removed);
    }

    public ModifierResult DeleteAllAirForces()
    {
        if (_mapData == null) return NotReady();

        int count = _mapData.AirForces.Count;
        if (count == 0) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirForces", "地图上没有空袭"));
        _mapData.AirForces.Clear();
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AllAirForcesDeleted", count), count);
    }

    #endregion

    #region 空中支援增删改

    public ModifierResult AddAirSupport(AirSupport airSupport)
    {
        if (_mapData == null) return NotReady();
        if (ValidateAirSupport(airSupport) is { } error) return ModifierResult.Fail(error);

        _mapData.AirSupports.Add(airSupport);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirSupportAdded", airSupport.GetAirForceTypeName()));
    }

    public ModifierResult UpdateAirSupport(int index, AirSupport airSupport)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirSupports.Count) return AirSupportIndexOutOfRange(index);
        if (ValidateAirSupport(airSupport) is { } error) return ModifierResult.Fail(error);

        string message = Loc.F("Module.AirForce.Msg.AirSupportUpdated", airSupport.GetAirForceTypeName());
        if (EqualityComparer<AirSupport>.Default.Equals(_mapData.AirSupports[index], airSupport)) return ModifierResult.Ok(message, 0);

        _mapData.AirSupports[index] = airSupport;
        MarkModified();
        return ModifierResult.Ok(message);
    }

    /// <summary>复制第 <paramref name="index"/> 条空中支援，副本插在它后面。</summary>
    public ModifierResult DuplicateAirSupport(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirSupports.Count) return AirSupportIndexOutOfRange(index);

        _mapData.AirSupports.Insert(index + 1, _mapData.AirSupports[index]);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirSupportDuplicated", index + 1));
    }

    public ModifierResult DeleteAirSupport(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirSupports.Count) return AirSupportIndexOutOfRange(index);

        _mapData.AirSupports.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.AirForce.Msg.AirSupportDeleted", "已删除空中支援数据"));
    }

    public ModifierResult DeleteAllAirSupports()
    {
        if (_mapData == null) return NotReady();

        int count = _mapData.AirSupports.Count;
        if (count == 0) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirSupports", "地图上没有空中支援"));
        _mapData.AirSupports.Clear();
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AllAirSupportsDeleted", count), count);
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyAirForce(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirForces.Count) return AirForceIndexOutOfRange(index);

        _copiedAirForce = _mapData.AirForces[index];
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirForceCopied", _copiedAirForce.Value.GetUnitTypeName()));
    }

    public ModifierResult PasteAirForce()
    {
        if (_mapData == null) return NotReady();
        if (_copiedAirForce == null) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NothingCopiedAirForce", "没有已复制的空袭数据"));

        return AddAirForce(_copiedAirForce.Value);
    }

    public ModifierResult CopyAirSupport(int index)
    {
        if (_mapData == null) return NotReady();
        if (index < 0 || index >= _mapData.AirSupports.Count) return AirSupportIndexOutOfRange(index);

        _copiedAirSupport = _mapData.AirSupports[index];
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirSupportCopied", _copiedAirSupport.Value.GetAirForceTypeName()));
    }

    public ModifierResult PasteAirSupport()
    {
        if (_mapData == null) return NotReady();
        if (_copiedAirSupport == null) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NothingCopiedAirSupport", "没有已复制的空中支援数据"));

        return AddAirSupport(_copiedAirSupport.Value);
    }

    #endregion

    #region 批量生成

    /// <summary>
    /// 在一组格子里带机场的建筑上随机生成空袭（Ctrl+Q）：每个机场 × 每个目标军团生成 <paramref name="countPerLegion"/> 条，
    /// 兵种 / 弹药从名录里随机取，回合在 [<paramref name="minRound"/>, <paramref name="maxRound"/>] 里随机。
    /// <para>军团值 -1 = 每个军团；数量限制在 1–<see cref="MaxCountPerLegion"/>；回合限制在 1–<see cref="MaxRound"/>，上下限填反时自动交换。</para>
    /// </summary>
    public ModifierResult GenerateAirForcesAtAirports(IReadOnlyList<HexCoord> cells, int legionValue, int countPerLegion, int minRound, int maxRound)
    {
        if (_mapData == null) return NotReady();

        var found = FindAirports(cells, out var airports);
        if (!found.Success) return found;
        if (!TryResolveLegions(legionValue, out var legions, out var legionError)) return legionError;

        int count = ClampCount(countPerLegion);
        (minRound, maxRound) = NormalizeRounds(minRound, maxRound);
        var types = PoolOf(AirForceCatalog.Types);
        var ammos = PoolOf(AirForceCatalog.Ammos);
        int width = _mapData.MapWidth;

        int generated = 0;
        foreach (var airport in airports)
        {
            foreach (int legion in legions)
            {
                for (int i = 0; i < count; i++)
                {
                    _mapData.AirForces.Add(new AirForce
                    {
                        Coordinate = airport.Row * width + airport.Col,
                        UnitType = types[_random.Next(types.Count)],
                        AmmoType = ammos[_random.Next(ammos.Count)],
                        OwnerLegion = legion,
                        TriggerRound = _random.Next(minRound, maxRound + 1)
                    });
                    generated++;
                }
            }
        }

        MarkModified();
        string where = airports.Count == 1
            ? Loc.F("Module.AirForce.Msg.AtAirport", airports[0].Col, airports[0].Row)
            : Loc.F("Module.AirForce.Msg.AtAirports", airports.Count);
        string message = legionValue == AllLegions
            ? Loc.F("Module.AirForce.Msg.AirForcesGeneratedAll", generated, where, count, legions.Count, minRound, maxRound)
            : Loc.F("Module.AirForce.Msg.AirForcesGenerated", generated, where, legionValue, minRound, maxRound);
        return ModifierResult.Ok(message, generated);
    }

    /// <summary>
    /// 给每个还没有空袭的机场补一条空袭（Shift+Q）：战斗机、默认弹药、第 1 回合，军团 = 机场所在格的归属军团；
    /// 归属为空或归属值不是现有军团的机场跳过。
    /// </summary>
    public ModifierResult GenerateAirForceFromBuildings()
    {
        if (_mapData == null) return NotReady();

        int width = _mapData.MapWidth;
        var existing = _mapData.AirForces.Select(a => a.Coordinate).ToHashSet();
        var added = new List<AirForce>();
        foreach (var building in _mapData.Buildings)
        {
            if (building.AviationLevel <= 0 || existing.Contains(building.Coordinate)) continue;

            int col = building.Coordinate % width, row = building.Coordinate / width;
            if (!IsValidCoord(col, row)) continue;
            // 归属按格子坐标取：征服地图的归属数组是裁剪区内的相对索引，不能拿建筑的格子索引直接去查
            int belong = _mapData.GetBelongValue(col, row);
            if (belong == 0xFF || !IsValidLegion(belong)) continue;

            var airForce = AirForce.CreateDefault(building.Coordinate, belong);
            airForce.UnitType = AirForceCatalog.Fighter;
            added.Add(airForce);
            existing.Add(building.Coordinate);
        }

        if (added.Count == 0) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirportToFill", "没有需要补空袭的机场（机场都已有空袭，或机场所在格没有归属军团）"));
        foreach (var airForce in added) _mapData.AirForces.Add(airForce);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirForcesFromBuildings", added.Count), added.Count);
    }

    /// <summary>
    /// 按军团随机生成空中支援（R）：每个目标军团生成 <paramref name="countPerLegion"/> 条，兵种 / 弹药从名录里随机取，
    /// 回合在 [<paramref name="minRound"/>, <paramref name="maxRound"/>] 里随机。限制与 <see cref="GenerateAirForcesAtAirports"/> 相同。
    /// </summary>
    public ModifierResult GenerateRandomAirSupportByLegion(int legionValue, int countPerLegion, int minRound, int maxRound)
    {
        if (_mapData == null) return NotReady();
        if (!TryResolveLegions(legionValue, out var legions, out var legionError)) return legionError;

        int count = ClampCount(countPerLegion);
        (minRound, maxRound) = NormalizeRounds(minRound, maxRound);
        var types = PoolOf(AirForceCatalog.Types);
        var ammos = PoolOf(AirForceCatalog.Ammos);

        int generated = 0;
        foreach (int legion in legions)
        {
            for (int i = 0; i < count; i++)
            {
                _mapData.AirSupports.Add(new AirSupport
                {
                    AirForceSequence = types[_random.Next(types.Count)],
                    AmmoType = ammos[_random.Next(ammos.Count)],
                    OwnerLegion = legion,
                    TriggerRound = _random.Next(minRound, maxRound + 1)
                });
                generated++;
            }
        }

        MarkModified();
        string message = legionValue == AllLegions
            ? Loc.F("Module.AirForce.Msg.AirSupportsGeneratedAll", generated, count, legions.Count, minRound, maxRound)
            : Loc.F("Module.AirForce.Msg.AirSupportsGenerated", generated, legionValue, minRound, maxRound);
        return ModifierResult.Ok(message, generated);
    }

    /// <summary>
    /// 为现有空袭随机配空中支援：每条取一个现有空袭的军团，兵种 / 弹药从名录里随机取，回合在 1–<paramref name="maxRound"/> 里随机。
    /// </summary>
    public ModifierResult GenerateRandomAirSupport(int count, int maxRound = 50)
    {
        if (_mapData == null) return NotReady();
        if (_mapData.AirForces.Count == 0) return ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NoAirForceForSupport", "没有空袭数据，无法生成空中支援"));

        count = ClampCount(count);
        (_, maxRound) = NormalizeRounds(1, maxRound);
        var types = PoolOf(AirForceCatalog.Types);
        var ammos = PoolOf(AirForceCatalog.Ammos);

        for (int i = 0; i < count; i++)
        {
            var source = _mapData.AirForces[_random.Next(_mapData.AirForces.Count)];
            _mapData.AirSupports.Add(new AirSupport
            {
                AirForceSequence = types[_random.Next(types.Count)],
                AmmoType = ammos[_random.Next(ammos.Count)],
                OwnerLegion = source.OwnerLegion,
                TriggerRound = _random.Next(1, maxRound + 1)
            });
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.AirForce.Msg.AirSupportsRandomized", count), count);
    }

    /// <summary>
    /// 军团值 → 目标军团列表：-1 = 地图上全部军团（ActionId 去重，没有军团时退回 0），否则必须是现有军团。
    /// </summary>
    private bool TryResolveLegions(int legionValue, out List<int> legions, out ModifierResult error)
    {
        error = default;
        if (legionValue == AllLegions)
        {
            legions = _mapData!.Legions.Select(l => l.ActionId).Distinct().ToList();
            if (legions.Count == 0) legions.Add(0);
            return true;
        }

        legions = new List<int> { legionValue };
        if (IsValidLegion(legionValue)) return true;
        error = ModifierResult.Fail(Loc.F("Module.AirForce.Msg.LegionNotFoundOrAll", legionValue));
        return false;
    }

    private static int ClampCount(int count) => Math.Clamp(count, 1, MaxCountPerLegion);

    /// <summary>回合限制在 1–<see cref="MaxRound"/>，上下限填反时交换（上限封顶后 Random.Next(min, max + 1) 不会溢出）。</summary>
    private static (int Min, int Max) NormalizeRounds(int minRound, int maxRound)
    {
        minRound = Math.Clamp(minRound, 1, MaxRound);
        maxRound = Math.Clamp(maxRound, 1, MaxRound);
        return minRound <= maxRound ? (minRound, maxRound) : (maxRound, minRound);
    }

    private static List<int> PoolOf(IReadOnlyList<ConfigKeyValue> catalog) => catalog.Select(x => x.Value).ToList();

    #endregion

    #region 查询

    public IReadOnlyList<AirForce> GetAllAirForces()
    {
        return _mapData?.AirForces ?? [];
    }

    public IReadOnlyList<AirSupport> GetAllAirSupports()
    {
        return _mapData?.AirSupports ?? [];
    }

    public AirForce? GetAirForce(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.AirForces.Count) return null;
        return _mapData.AirForces[index];
    }

    public AirSupport? GetAirSupport(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.AirSupports.Count) return null;
        return _mapData.AirSupports[index];
    }

    #endregion

    private static ModifierResult NotReady() => ModifierResult.Fail(Loc.T("Module.AirForce.Msg.NotReady", "地图数据未初始化"));

    private static ModifierResult AirForceIndexOutOfRange(int index)
        => ModifierResult.Fail(Loc.F("Module.AirForce.Msg.AirForceIndexOutOfRange", index));

    private static ModifierResult AirSupportIndexOutOfRange(int index)
        => ModifierResult.Fail(Loc.F("Module.AirForce.Msg.AirSupportIndexOutOfRange", index));
}
