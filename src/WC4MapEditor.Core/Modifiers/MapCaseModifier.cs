using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 方案（AI 方针）模块：方案列表的增删改，以及单位方针值（<see cref="Army.Policy"/>）的批量设置。
/// <para>
/// 方针值等于 <see cref="MapCase.PolicyNumber"/> 的单位执行该方案。单位列表按地图版本区分：
/// v1/v2 用 <see cref="MapData.Armies"/>，v3（BtlVersion ≥ 3）用 <see cref="MapData.ArmiesV3"/>，
/// 本模块只读写当前版本实际存盘的那一个（与解析 / 保存逻辑一致）。
/// </para>
/// </summary>
public sealed class MapCaseModifier : ModifierBase
{
    /// <summary>方针值上限：单位的方针值只有 1 字节，编号超过 255 的方案没有单位能引用。</summary>
    public const int MaxPolicy = 255;

    private MapCase? _copiedCase;
    private readonly Random _random = new();

    public override string Name => "mapcase";
    public override string DisplayName => Loc.T("Module.MapCase.Name", "方案修改器");

    /// <summary>该地图的单位是否使用 v3 结构（<see cref="MapData.ArmiesV3"/>）。</summary>
    public static bool UsesV3Units(MapData map) => map.Header.UsesV3Records;

    #region 基础接口（按格子）

    /// <summary>
    /// 在 (col,row) 新增一个"使单位走向该地块"的方案：目标值 = 该格的地块序号，方针编号默认取下一个空闲编号。
    /// <paramref name="parameter"/> 为 <see cref="MapCase"/> 时以它的方针编号与触发回合为准（同样经过 <see cref="ValidateCase"/> 校验）。
    /// </summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.F("Module.MapCase.CellOutOfRange", col, row));

        var mapCase = parameter is MapCase template ? template : CreateCase();
        mapCase.CaseType = MapCase.MoveToTileType;
        mapCase.TargetTile = row * _mapData.MapWidth + col;
        var errors = ValidateCase(mapCase);
        if (errors.Count > 0) return Invalid(errors);

        _mapData.Cases.Add(mapCase);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.TargetAdded", mapCase.PolicyNumber, col, row));
    }

    /// <summary>删除以 (col,row) 为目标地块的方案（只看"走向地块"类型）。</summary>
    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.F("Module.MapCase.CellOutOfRange", col, row));

        int tile = row * _mapData.MapWidth + col;
        int removed = 0;
        for (int i = _mapData.Cases.Count - 1; i >= 0; i--)
        {
            var c = _mapData.Cases[i];
            if (!c.TargetsTile || c.TargetTile != tile) continue;
            _mapData.Cases.RemoveAt(i);
            removed++;
        }

        if (removed == 0) return ModifierResult.Fail(Loc.F("Module.MapCase.NoTargetAt", col, row));
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.TargetRemoved", removed, col, row), removed);
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => FindTargetCaseIndex(col, row) >= 0;

    /// <summary>以该格为目标地块的第一个方案；没有时为 null。</summary>
    public override object? GetDataAt(int col, int row)
    {
        int index = FindTargetCaseIndex(col, row);
        return index >= 0 ? _mapData!.Cases[index] : null;
    }

    /// <summary>方案不属于某个格子，不支持按格写入（用 <see cref="UpdateCase"/>）。</summary>
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 方案列表

    /// <summary>新方案的默认值：方针编号取下一个空闲编号，其余字段为 0（对齐 VB 新增按钮）。</summary>
    public MapCase CreateCase() => new() { PolicyNumber = NextPolicyNumber() };

    /// <summary>
    /// 下一个空闲的方针编号：现有最大编号 + 1（空列表为 1：0 是单位的默认方针值，不作为新方案的默认编号）；
    /// 超过 <see cref="MaxPolicy"/> 时取 1..255 里第一个未使用的编号。
    /// </summary>
    public int NextPolicyNumber()
    {
        if (_mapData == null) return 1;

        var used = new bool[MaxPolicy + 1];
        int max = 0;
        foreach (var c in _mapData.Cases)
        {
            if (c.PolicyNumber >= 0 && c.PolicyNumber <= MaxPolicy) used[c.PolicyNumber] = true;
            if (c.PolicyNumber > max) max = c.PolicyNumber;
        }

        if (max < MaxPolicy) return max + 1;
        for (int p = 1; p <= MaxPolicy; p++)
        {
            if (!used[p]) return p;
        }
        return MaxPolicy;
    }

    /// <summary>
    /// 校验方案字段（窗口与命令行共用同一套规则），返回不合格字段的说明，空列表表示通过。
    /// 与 <paramref name="original"/> 相同的字段不检查：游戏自带地图里有超出编辑范围的旧数据
    /// （方针编号 &gt; 255、负回合等），修改别的字段时不应被它们卡住。
    /// </summary>
    public List<string> ValidateCase(MapCase mapCase, MapCase? original = null)
    {
        var errors = new List<string>();
        if (original?.PolicyNumber != mapCase.PolicyNumber && (mapCase.PolicyNumber < 0 || mapCase.PolicyNumber > MaxPolicy))
            errors.Add(Loc.F("Module.MapCase.Invalid.Policy", MaxPolicy));
        if (original?.CaseType != mapCase.CaseType && mapCase.CaseType < 0)
            errors.Add(Loc.T("Module.MapCase.Invalid.Type", "方针类型不能为负数"));
        if (original?.TriggerRound != mapCase.TriggerRound && mapCase.TriggerRound < 0)
            errors.Add(Loc.T("Module.MapCase.Invalid.Round", "触发回合不能为负数"));

        bool targetUnchanged = original?.TargetTile == mapCase.TargetTile && original?.CaseType == mapCase.CaseType;
        int tileCount = (_mapData?.MapWidth ?? 0) * (_mapData?.MapHeight ?? 0);
        if (mapCase.TargetsTile && !targetUnchanged && (mapCase.TargetTile < 0 || mapCase.TargetTile >= tileCount))
            errors.Add(Loc.F("Module.MapCase.Invalid.Target", tileCount - 1));
        return errors;
    }

    public ModifierResult AddCase(MapCase mapCase)
    {
        if (_mapData == null) return NotInitialized();
        var errors = ValidateCase(mapCase);
        if (errors.Count > 0) return Invalid(errors);

        _mapData.Cases.Add(mapCase);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.Added", _mapData.Cases.Count - 1, mapCase.PolicyNumber));
    }

    public ModifierResult UpdateCase(int index, MapCase mapCase)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Cases.Count) return IndexOutOfRange(index);

        var current = _mapData.Cases[index];
        if (current.Equals(mapCase))
            return ModifierResult.Ok(Loc.F("Module.MapCase.Unchanged", index), 0);
        var errors = ValidateCase(mapCase, current);
        if (errors.Count > 0) return Invalid(errors);

        _mapData.Cases[index] = mapCase;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.Updated", index, mapCase.PolicyNumber));
    }

    /// <summary>只修改给出的字段（null 表示保持原值），校验规则同 <see cref="UpdateCase"/>。</summary>
    public ModifierResult UpdateCaseFields(int index, int? policyNumber = null, int? caseType = null, int? triggerRound = null, int? targetTile = null)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Cases.Count) return IndexOutOfRange(index);

        var mapCase = _mapData.Cases[index];
        mapCase.PolicyNumber = policyNumber ?? mapCase.PolicyNumber;
        mapCase.CaseType = caseType ?? mapCase.CaseType;
        mapCase.TriggerRound = triggerRound ?? mapCase.TriggerRound;
        mapCase.TargetTile = targetTile ?? mapCase.TargetTile;
        return UpdateCase(index, mapCase);
    }

    public ModifierResult DeleteCase(int index)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Cases.Count) return IndexOutOfRange(index);

        _mapData.Cases.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.Deleted", index));
    }

    public ModifierResult DeleteAllCases()
    {
        if (_mapData == null) return NotInitialized();

        int count = _mapData.Cases.Count;
        if (count == 0) return ModifierResult.Ok(Loc.T("Module.MapCase.NothingToDelete", "没有方案可删除"), 0);

        _mapData.Cases.Clear();
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.AllDeleted", count), count);
    }

    #endregion

    #region 复制 / 粘贴

    public ModifierResult CopyCase(int index)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Cases.Count) return IndexOutOfRange(index);

        _copiedCase = _mapData.Cases[index];
        return ModifierResult.Ok(Loc.F("Module.MapCase.Copied", index, _copiedCase.Value.PolicyNumber));
    }

    /// <summary>粘贴复制的方案：插到 <paramref name="insertAt"/>（越界或省略时追加到末尾）。</summary>
    public ModifierResult PasteCase(int insertAt = -1)
    {
        if (_mapData == null) return NotInitialized();
        if (_copiedCase == null) return ModifierResult.Fail(Loc.T("Module.MapCase.NothingCopied", "没有已复制的方案数据"));

        if (insertAt < 0 || insertAt > _mapData.Cases.Count) insertAt = _mapData.Cases.Count;
        _mapData.Cases.Insert(insertAt, _copiedCase.Value);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.Pasted", insertAt));
    }

    /// <summary>复制第 <paramref name="index"/> 个方案并插在它后面（同一方针的后续步骤通常紧挨着）；返回新方案的下标，失败为 -1。</summary>
    public int DuplicateCase(int index)
    {
        if (!CopyCase(index).Success) return -1;
        return PasteCase(index + 1).Success ? index + 1 : -1;
    }

    #endregion

    #region 单位方针值

    /// <summary>全部单位所在的地块序号（框选过滤用：一次建表，免得逐格扫描单位列表）。</summary>
    public static HashSet<int> UnitTiles(MapData map)
    {
        var tiles = new HashSet<int>();
        if (UsesV3Units(map))
        {
            foreach (var army in map.ArmiesV3) tiles.Add(army.Coordinate);
        }
        else
        {
            foreach (var army in map.Armies) tiles.Add(army.Coordinate);
        }
        return tiles;
    }

    /// <summary>这些格子上全部单位的方针值（没有单位的格子跳过；同一格叠放的多个单位各算一个）。</summary>
    public List<int> GetPoliciesAt(IEnumerable<HexCoord> cells)
    {
        var result = new List<int>();
        if (_mapData == null) return result;

        var tiles = ToTileSet(cells);
        if (UsesV3Units(_mapData))
        {
            foreach (var army in _mapData.ArmiesV3)
                if (tiles.Contains(army.Coordinate)) result.Add(army.Policy);
        }
        else
        {
            foreach (var army in _mapData.Armies)
                if (tiles.Contains(army.Coordinate)) result.Add(army.Policy);
        }
        return result;
    }

    /// <summary>这些格子上单位的共同方针值；没有单位或方针值不一致时为 null（VB：共同值作为输入框的默认值）。</summary>
    public int? GetCommonPolicy(IEnumerable<HexCoord> cells)
    {
        int? common = null;
        foreach (int policy in GetPoliciesAt(cells))
        {
            if (common == null) common = policy;
            else if (common.Value != policy) return null;
        }
        return common;
    }

    /// <summary>把这些格子上全部单位（含叠放的单位）的方针值设为 <paramref name="policy"/>（0-255）。AffectedCount = 实际改变的单位数。</summary>
    public ModifierResult SetPolicyForUnitsAt(IEnumerable<HexCoord> cells, int policy)
    {
        if (_mapData == null) return NotInitialized();
        if (policy < 0 || policy > MaxPolicy) return PolicyOutOfRange(policy);

        var tiles = ToTileSet(cells);
        int units = 0, changed = 0;
        byte value = (byte)policy;
        if (UsesV3Units(_mapData))
        {
            for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
            {
                var army = _mapData.ArmiesV3[i];
                if (!tiles.Contains(army.Coordinate)) continue;
                units++;
                if (army.Policy == value) continue;
                army.Policy = value;
                _mapData.ReplaceArmyV3(i, army);
                changed++;
            }
        }
        else
        {
            for (int i = 0; i < _mapData.Armies.Count; i++)
            {
                var army = _mapData.Armies[i];
                if (!tiles.Contains(army.Coordinate)) continue;
                units++;
                if (army.Policy == value) continue;
                army.Policy = value;
                _mapData.ReplaceArmy(i, army);
                changed++;
            }
        }

        if (units == 0) return ModifierResult.Fail(Loc.T("Module.MapCase.NoUnits", "所选格子上没有单位"));
        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.PolicySet", units, policy), changed);
    }

    /// <summary>按归属批量设置方针值：<paramref name="belongValue"/> = -1 表示全部单位。</summary>
    public ModifierResult SetPolicyByBelong(int belongValue, int policyValue)
    {
        if (_mapData == null) return NotInitialized();
        if (policyValue < 0 || policyValue > MaxPolicy) return PolicyOutOfRange(policyValue);

        byte value = (byte)policyValue;
        int changed = ForEachUnitOfBelong(belongValue, () => value);
        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.PolicySetByBelong", changed, policyValue, belongValue), changed);
    }

    /// <summary>按归属把单位的方针值随机设为 0-255：<paramref name="belongValue"/> = -1 表示全部单位。</summary>
    public ModifierResult RandomizePolicyByBelong(int belongValue)
    {
        if (_mapData == null) return NotInitialized();

        int changed = ForEachUnitOfBelong(belongValue, () => (byte)_random.Next(0, MaxPolicy + 1));
        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.MapCase.PolicyRandomized", changed, belongValue), changed);
    }

    /// <summary>方针值为 <paramref name="policy"/> 的单位所在的格子（方案列表"选中使用该方针的单位"用）。</summary>
    public List<HexCoord> GetUnitCellsWithPolicy(int policy)
    {
        var cells = new List<HexCoord>();
        if (_mapData == null || policy < 0 || policy > MaxPolicy) return cells;

        int width = _mapData.MapWidth;
        if (UsesV3Units(_mapData))
        {
            foreach (var army in _mapData.ArmiesV3)
                if (army.Policy == policy) cells.Add(HexCoord.FromIndex(army.Coordinate, width));
        }
        else
        {
            foreach (var army in _mapData.Armies)
                if (army.Policy == policy) cells.Add(HexCoord.FromIndex(army.Coordinate, width));
        }
        return cells;
    }

    /// <summary>
    /// 找与 (col,row) 相关的方案：该格有单位时取方针编号等于单位方针值的第一个方案，
    /// 否则取以该格为目标地块的第一个方案；都没有时为 -1。
    /// </summary>
    public int FindCaseIndexFor(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return -1;

        int? policy = GetCommonPolicy(new[] { new HexCoord(col, row) });
        if (policy != null)
        {
            for (int i = 0; i < _mapData.Cases.Count; i++)
                if (_mapData.Cases[i].PolicyNumber == policy.Value) return i;
        }
        return FindTargetCaseIndex(col, row);
    }

    #endregion

    #region 查询

    public IReadOnlyList<MapCase> GetAllCases()
    {
        return _mapData?.Cases ?? [];
    }

    public MapCase? GetCase(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.Cases.Count) return null;
        return _mapData.Cases[index];
    }

    #endregion

    #region 内部

    private int FindTargetCaseIndex(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return -1;

        int tile = row * _mapData.MapWidth + col;
        for (int i = 0; i < _mapData.Cases.Count; i++)
        {
            var c = _mapData.Cases[i];
            if (c.TargetsTile && c.TargetTile == tile) return i;
        }
        return -1;
    }

    private HashSet<int> ToTileSet(IEnumerable<HexCoord> cells)
    {
        var tiles = new HashSet<int>();
        foreach (var cell in cells)
        {
            if (IsValidCoord(cell.Col, cell.Row)) tiles.Add(cell.ToIndex(_mapData!.MapWidth));
        }
        return tiles;
    }

    /// <summary>
    /// 把归属为 <paramref name="belongValue"/>（-1 = 全部）的单位的方针值改成 <paramref name="nextPolicy"/> 给出的值，返回实际改变的单位数。
    /// 单位归属按它所在的格子查（<see cref="MapData.GetBelongValue"/>）：征服地图有裁剪偏移时，归属下标并不等于地块序号。
    /// </summary>
    private int ForEachUnitOfBelong(int belongValue, Func<byte> nextPolicy)
    {
        var map = _mapData!;
        int width = map.MapWidth;
        int changed = 0;
        if (UsesV3Units(map))
        {
            for (int i = 0; i < map.ArmiesV3.Count; i++)
            {
                var army = map.ArmiesV3[i];
                if (!MatchesBelong(map, army.Coordinate, width, belongValue)) continue;
                byte value = nextPolicy();
                if (army.Policy == value) continue;
                army.Policy = value;
                map.ReplaceArmyV3(i, army);
                changed++;
            }
        }
        else
        {
            for (int i = 0; i < map.Armies.Count; i++)
            {
                var army = map.Armies[i];
                if (!MatchesBelong(map, army.Coordinate, width, belongValue)) continue;
                byte value = nextPolicy();
                if (army.Policy == value) continue;
                army.Policy = value;
                map.ReplaceArmy(i, army);
                changed++;
            }
        }
        return changed;
    }

    private static bool MatchesBelong(MapData map, int coordinate, int width, int belongValue)
    {
        if (belongValue == -1) return true;
        var cell = HexCoord.FromIndex(coordinate, width);
        return map.GetBelongValue(cell.Col, cell.Row) == belongValue;
    }

    private static ModifierResult Invalid(List<string> errors) => ModifierResult.Fail(string.Join(Loc.T("Module.MapCase.ErrorSeparator", "；"), errors));

    private static ModifierResult NotInitialized() => ModifierResult.Fail(Loc.T("Module.MapCase.NotInitialized", "地图数据未初始化"));

    private ModifierResult IndexOutOfRange(int index)
    {
        int count = _mapData?.Cases.Count ?? 0;
        return ModifierResult.Fail(count == 0
            ? Loc.T("Module.MapCase.NoCases", "当前没有方案")
            : Loc.F("Module.MapCase.IndexOutOfRange", index, count - 1));
    }

    private static ModifierResult PolicyOutOfRange(int policy)
        => ModifierResult.Fail(Loc.F("Module.MapCase.PolicyOutOfRange", policy, MaxPolicy));

    #endregion
}
