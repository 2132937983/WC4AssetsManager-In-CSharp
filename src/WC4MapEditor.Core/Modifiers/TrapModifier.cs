using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 陷阱（地雷）模块。由单位部署模式引入（VB 的独立陷阱模式进不去，用户实际用到的陷阱功能都在单位模式里）。
/// <para>
/// 数据约定（来自游戏自带地图）：<see cref="Trap.LegionId"/> 存的是军团的 ActionId（不是归属值），
/// 等级 = <see cref="Trap.Organization"/>（1-4），血量 = <see cref="Trap.StandardHealth"/>(等级)，
/// v3 地图的陷阱 Extra1 = 33。新建的陷阱都按这个约定生成；建筑所在格不放陷阱（BTLRuleChecker 会报警告）。
/// </para>
/// </summary>
public sealed class TrapModifier : ModifierBase
{
    public override string Name => "trap";
    public override string DisplayName => "陷阱修改器";

    /// <summary>陷阱最高等级：游戏数据只用 1-4，循环等级、随机等级与批量生成都在 1..MaxLevel 之间。</summary>
    public const int MaxLevel = 4;

    /// <summary>v3 地图规范陷阱的 Extra1（游戏自带 v3 地图里的规范陷阱全部是 33，v1 地图是 0）。</summary>
    private const short V3TrapExtra = 33;

    /// <summary>随机等级时 1-4 级的相对权重（与 VB 版一致：低等级更常见）。</summary>
    private static readonly double[] LevelWeights = [1.0, 0.8, 0.6, 0.4];

    private Trap? _copiedTrap;
    private readonly Random _random = new();

    #region 基础 CRUD

    /// <summary>放置陷阱：参数是 <see cref="Trap"/> 时原样放到该格，否则放一个按本格归属生成的 1 级陷阱。</summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);

        var trap = parameter is Trap given ? WithCell(given, col, row) : NewTrap(col, row, 1, ResolveTrapLegion(col, row));
        Put(col, row, trap);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.Placed", col, row, trap.Organization, trap.LegionId, trap.Health));
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);

        int idx = IndexOf(col, row);
        if (idx < 0) return ModifierResult.Fail(NoTrapText);

        _mapData!.Traps.RemoveAt(idx);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.Removed", col, row));
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => GetTrapAt(col, row).HasValue;

    public override object? GetDataAt(int col, int row) => GetTrapAt(col, row);

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || data is not Trap trap) return false;
        Put(col, row, WithCell(trap, col, row));
        MarkModified();
        return true;
    }

    public Trap? GetTrapAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = IndexOf(col, row);
        return idx >= 0 ? _mapData!.Traps[idx] : null;
    }

    /// <summary>格子上陷阱的一行说明（状态栏用）；没有陷阱时返回 null。</summary>
    public string? DescribeTrapAt(int col, int row)
    {
        var trap = GetTrapAt(col, row);
        return trap == null ? null : Loc.F("Module.Trap.Describe", trap.Value.Organization, trap.Value.LegionId, trap.Value.Health);
    }

    /// <summary>
    /// 新陷阱的所属军团（ActionId）：按 <see cref="ArmyPlacementRules.ResolveOwner"/> 取本格归属（省会优先），
    /// 再换算成该军团的 ActionId；没有归属或找不到军团时为 0。
    /// </summary>
    public int ResolveTrapLegion(int col, int row)
    {
        if (_mapData == null) return 0;
        return LegionActionId(ArmyPlacementRules.ResolveOwner(_mapData, col, row));
    }

    #endregion

    #region 放置 / 等级 / 随机 (F / Shift+F / Shift+R)

    /// <summary>在空格上放一个 1 级陷阱（F）。已有陷阱或建筑所在格拒绝。</summary>
    public ModifierResult CreateTrapAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);
        if (IndexOf(col, row) >= 0) return ModifierResult.Fail(Loc.T("Module.Trap.Exists", "该位置已有陷阱"));
        if (HasBuildingAt(col, row)) return ModifierResult.Fail(OnBuildingText);
        return Apply(col, row);
    }

    /// <summary>陷阱等级 1→2→3→4→1 循环（Shift+F，VB 的 F 键行为）。血量为旧等级标准值时随等级更新。</summary>
    public ModifierResult CycleTrapLevel(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);
        int idx = IndexOf(col, row);
        if (idx < 0) return ModifierResult.Fail(NoTrapText);

        var trap = _mapData!.Traps[idx];
        int oldLevel = trap.Organization;
        int newLevel = oldLevel is >= 1 and < MaxLevel ? oldLevel + 1 : 1;
        _mapData.Traps[idx] = WithLevel(trap, newLevel);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.LevelCycled", col, row, oldLevel, newLevel));
    }

    /// <summary>
    /// 随机化格子上陷阱的属性（Shift+R）：等级随机 1..<see cref="MaxLevel"/>、血量取该等级标准值、
    /// 所属军团按本格归属重算。格子上没有陷阱时新建一个（建筑所在格除外）。
    /// </summary>
    public ModifierResult RandomizeTrapAttributes(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);
        int idx = IndexOf(col, row);
        if (idx < 0 && HasBuildingAt(col, row)) return ModifierResult.Fail(OnBuildingText);

        int level = _random.Next(1, MaxLevel + 1);
        var trap = idx >= 0 ? _mapData!.Traps[idx] : NewTrap(col, row, level, 0);
        trap.Organization = (short)level;
        trap.Health = Trap.StandardHealth(level);
        trap.LegionId = (short)ResolveTrapLegion(col, row);

        Put(col, row, trap);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.Randomized", col, row, trap.Organization, trap.LegionId, trap.Health));
    }

    #endregion

    #region 复制 / 粘贴 / 删除 (Ctrl+C / Ctrl+V / Ctrl+X / Ctrl+Delete)

    public bool HasCopiedTrap => _copiedTrap.HasValue;

    public ModifierResult CopyTrap(int col, int row)
    {
        var trap = GetTrapAt(col, row);
        if (trap == null) return ModifierResult.Fail(NoTrapText);
        _copiedTrap = trap;
        return ModifierResult.Ok(Loc.F("Module.Trap.Copied", trap.Value.Organization, trap.Value.LegionId, trap.Value.Health));
    }

    /// <summary>清空陷阱剪贴板（Ctrl+C 在没有陷阱的格子上改为复制单位时调用，之后 Ctrl+V 粘贴的就是这个单位）。</summary>
    public void ClearCopiedTrap() => _copiedTrap = null;

    public ModifierResult PasteTrap(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(OutOfRangeText);
        if (_copiedTrap == null) return ModifierResult.Fail(Loc.T("Module.Trap.NothingToPaste", "没有已复制的陷阱数据"));
        if (HasBuildingAt(col, row)) return ModifierResult.Fail(OnBuildingText);

        Put(col, row, WithCell(_copiedTrap.Value, col, row));
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.Pasted", col, row));
    }

    /// <summary>删除这些格子上的陷阱，返回删除数量；一个都没有时失败。</summary>
    public ModifierResult RemoveTraps(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var set = new HashSet<int>();
        foreach (var c in cells)
            if (IsValidCoord(c.Col, c.Row)) set.Add(c.ToIndex(_mapData.MapWidth));

        int removed = 0;
        for (int i = _mapData.Traps.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(_mapData.Traps[i].CellIndex)) continue;
            _mapData.Traps.RemoveAt(i);
            removed++;
        }

        if (removed == 0) return ModifierResult.Fail(NoTrapText);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.RemovedBatch", removed), removed);
    }

    public ModifierResult DeleteAllTraps()
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int count = _mapData.Traps.Count;
        _mapData.Traps.Clear();
        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.AllDeleted", count), count);
    }

    #endregion

    #region 批量 (Ctrl+R / Ctrl+G / Ctrl+I / Ctrl+F)

    /// <summary>
    /// 随机化陷阱等级（Ctrl+R）：所属军团为 <paramref name="legionValue"/>（ActionId，-1 = 全部陷阱）的每个陷阱
    /// 以 <paramref name="probability"/>% 的几率重新抽取等级（1-4 级权重 1 : 0.8 : 0.6 : 0.4）。
    /// VB 版把概率乘到四个权重上，归一化后毫无作用、且每个陷阱都会被改；这里改为"每个陷阱的重抽几率"。
    /// </summary>
    public ModifierResult RandomizeTrapLevels(int legionValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (probability < 1 || probability > 100)
            return ModifierResult.Fail(Loc.T("Module.Trap.BadProbability1", "概率必须在1-100之间"));

        int modified = 0;
        for (int i = 0; i < _mapData.Traps.Count; i++)
        {
            var trap = _mapData.Traps[i];
            if (legionValue != -1 && trap.LegionId != legionValue) continue;
            if (_random.Next(1, 101) > probability) continue;

            _mapData.Traps[i] = WithLevel(trap, RandomLevel());
            modified++;
        }

        if (modified > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.LevelsRandomized", modified), modified);
    }

    /// <summary>
    /// 按归属与概率批量生成陷阱（Ctrl+G）。只在归属数据范围内、没有陷阱 / 建筑 / 单位的格子上生成，
    /// <paramref name="includeSea"/> 为 false 时跳过海洋格（没有地形数据的征服地图不分海陆）。
    /// 格子的归属按 <see cref="ArmyPlacementRules.ResolveOwner"/>（省会优先）推断，
    /// 没有归属的格子不生成；<paramref name="belongFilter"/> = -1 表示所有归属，否则只在推断归属等于它的格子上生成。
    /// 陷阱等级随机 1..<see cref="MaxLevel"/>，血量取标准值，所属军团为该归属对应军团的 ActionId。
    /// </summary>
    public ModifierResult GenerateTraps(int belongFilter, int probability, bool includeSea)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (probability < 0 || probability > 100)
            return ModifierResult.Fail(Loc.T("Module.Trap.BadProbability", "概率必须在0-100之间"));

        var occupied = OccupiedCells();
        bool hasLand = ArmyPlacementRules.HasLandTiles(_mapData);
        int generated = 0;
        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int cell = row * _mapData.MapWidth + col;
                if (occupied.Contains(cell) || !_mapData.IsInBelongRange(col, row)) continue;
                if (!includeSea && ArmyPlacementRules.IsSea(_mapData, cell, hasLand)) continue;

                int owner = ArmyPlacementRules.ResolveOwner(_mapData, col, row);
                if (belongFilter != -1 && owner != belongFilter) continue;
                if (owner < 0 || _mapData.ResolveLegionByBelong(owner) is not { } legion) continue;
                if (_random.Next(0, 100) >= probability) continue;

                _mapData.Traps.Add(NewTrap(col, row, _random.Next(1, MaxLevel + 1), legion.ActionId));
                occupied.Add(cell);
                generated++;
            }
        }

        if (generated > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.Generated", generated), generated);
    }

    /// <summary>
    /// 每个省份以 <paramref name="probability"/>% 的几率在一个随机的陆地格上放一个陷阱（Ctrl+I）。
    /// 跳过无省份（0 / 0xFFFF）、海洋、归属数据范围外以及已有陷阱 / 建筑 / 单位的格子；所属军团取省份归属对应军团的 ActionId。
    /// </summary>
    public ModifierResult GenerateTrapsByProvince(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (probability < 0 || probability > 100)
            return ModifierResult.Fail(Loc.T("Module.Trap.BadProbability", "概率必须在0-100之间"));

        var occupied = OccupiedCells();
        bool hasLand = ArmyPlacementRules.HasLandTiles(_mapData);
        var candidates = new SortedDictionary<int, List<int>>();
        int total = _mapData.MapWidth * _mapData.MapHeight;
        for (int cell = 0; cell < total; cell++)
        {
            int provinceValue = _mapData.GetProvinceRef(cell).ProvinceValue;
            if (!ArmyPlacementRules.IsProvince(provinceValue) || occupied.Contains(cell)) continue;
            if (ArmyPlacementRules.IsSea(_mapData, cell, hasLand)) continue;
            if (!_mapData.IsInBelongRange(cell % _mapData.MapWidth, cell / _mapData.MapWidth)) continue;

            if (!candidates.TryGetValue(provinceValue, out var cells))
                candidates[provinceValue] = cells = new List<int>();
            cells.Add(cell);
        }

        int generated = 0;
        foreach (var (provinceValue, cells) in candidates)
        {
            if (_random.Next(0, 100) >= probability) continue;

            var coord = HexCoord.FromIndex(cells[_random.Next(cells.Count)], _mapData.MapWidth);
            int legion = LegionActionId(ArmyPlacementRules.ProvinceBelong(_mapData, provinceValue));
            _mapData.Traps.Add(NewTrap(coord.Col, coord.Row, _random.Next(1, MaxLevel + 1), legion));
            generated++;
        }

        if (generated > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Trap.GeneratedByProvince", generated), generated);
    }

    /// <summary>等级为 <paramref name="level"/> 的所有陷阱所在格（按格子下标排序，Ctrl+F 依次跳转）。</summary>
    public IReadOnlyList<HexCoord> FindTrapsByLevel(int level)
    {
        if (_mapData == null) return [];
        return _mapData.Traps
            .Where(t => t.Organization == level && t.CellIndex < _mapData.MapWidth * _mapData.MapHeight)
            .Select(t => t.CellIndex)
            .Distinct()
            .Order()
            .Select(cell => HexCoord.FromIndex(cell, _mapData.MapWidth))
            .ToList();
    }

    #endregion

    #region 内部

    private static string OutOfRangeText => Loc.T("Module.Trap.OutOfRange", "坐标超出范围");

    private static string NoMapText => Loc.T("Module.Trap.NoMap", "地图数据未初始化");

    private static string NoTrapText => Loc.T("Module.Trap.NoTrap", "该位置没有陷阱");

    private static string OnBuildingText => Loc.T("Module.Trap.OnBuilding", "建筑所在格不能放置陷阱");

    private int IndexOf(int col, int row)
    {
        int cell = row * _mapData!.MapWidth + col;
        var traps = _mapData.Traps;
        for (int i = 0; i < traps.Count; i++)
            if (traps[i].CellIndex == cell) return i;
        return -1;
    }

    private bool HasBuildingAt(int col, int row) => _mapData!.FindBuildingIndex(col, row) >= 0;

    private void Put(int col, int row, Trap trap)
    {
        int idx = IndexOf(col, row);
        if (idx >= 0) _mapData!.Traps[idx] = trap;
        else _mapData!.Traps.Add(trap);
    }

    private Trap WithCell(Trap trap, int col, int row)
    {
        trap.Coordinate = unchecked((short)(row * _mapData!.MapWidth + col));
        return trap;
    }

    /// <summary>按数据约定新建陷阱：指定等级、该等级标准血量、所属军团 ActionId，v3 地图 Extra1 = 33。</summary>
    private Trap NewTrap(int col, int row, int level, int legionActionId)
    {
        var trap = Trap.CreateDefault(row * _mapData!.MapWidth + col);
        trap.Organization = (short)level;
        trap.Health = Trap.StandardHealth(level);
        trap.LegionId = (short)legionActionId;
        if (_mapData.Header.BtlVersion >= 3) trap.Extra1 = V3TrapExtra;
        return trap;
    }

    /// <summary>改等级；血量是旧等级的标准值时同步成新等级的标准值（手工改过的血量保持不变）。</summary>
    private static Trap WithLevel(Trap trap, int level)
    {
        if (trap.Health == Trap.StandardHealth(trap.Organization))
            trap.Health = Trap.StandardHealth(level);
        trap.Organization = (short)level;
        return trap;
    }

    private int RandomLevel()
    {
        double roll = _random.NextDouble() * LevelWeights.Sum();
        for (int i = 0; i < LevelWeights.Length; i++)
        {
            roll -= LevelWeights[i];
            if (roll < 0) return i + 1;
        }
        return LevelWeights.Length;
    }

    /// <summary>归属值 → 该军团的 ActionId（按 <see cref="MapData.ResolveLegionByBelong"/> 解析）；无归属或找不到军团时为 0。</summary>
    private int LegionActionId(int owner)
        => owner >= 0 ? _mapData!.ResolveLegionByBelong(owner)?.ActionId ?? 0 : 0;

    /// <summary>已有陷阱、建筑、单位（v1 + v3）的格子：批量生成时都跳过。</summary>
    private HashSet<int> OccupiedCells()
    {
        var cells = new HashSet<int>();
        foreach (var t in _mapData!.Traps) cells.Add(t.CellIndex);
        foreach (var b in _mapData.Buildings) cells.Add(b.Coordinate);
        foreach (var a in _mapData.Armies) cells.Add((ushort)a.Coordinate);
        foreach (var a in _mapData.ArmiesV3) cells.Add((ushort)a.Coordinate);
        return cells;
    }

    #endregion
}
