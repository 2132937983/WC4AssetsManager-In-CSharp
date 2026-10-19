using System.Collections.ObjectModel;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// v1（<see cref="Army"/>）与 v3（<see cref="Army_3"/>）单位共有的字段视图。
/// 两版单位结构体前 30 字节布局相同、字段语义相同，公共算法只在这个视图上写一次。
/// </summary>
public struct ArmyUnitFields
{
    /// <summary>格子下标（文件里是无符号 16 位，这里按无符号解释）。</summary>
    public int Cell;
    public byte UnitType;
    public byte Level;
    public byte Organization;
    public byte Direction;
    public byte Mobility;
    public short HealthBonus;
    public short CurrentHealth;
    public short MaxHealth;
    public short General;

    /// <summary>军衔（自动分配将领时写入将领的 MilitaryRank）。</summary>
    public byte Rank;

    /// <summary>爵位 = 将领血量等级（v1 的 Nobility、v3 的 HpLevel，同为偏移 0x13；强度档位的 GeneralHp 写这里）。</summary>
    public byte GeneralHpLevel;

    public byte SkillLevel1;
    public byte SkillLevel2;
    public byte SkillLevel3;
    public byte SkillLevel4;
    public byte SkillLevel5;
    public byte KeyPoint;
    public short Plan;

    /// <summary>编辑器记录的所属归属（放置 / 粘贴 / 生成时写入省会归属）。</summary>
    public int LegionId;
}

/// <summary>
/// 单位模块与版本无关的操作。模式 / 命令行按地图版本（<see cref="BTLHeader.BtlVersion"/> &gt;= 3 用 v3）
/// 取一个实现，之后的调用不再区分 v1 / v3。
/// <para>
/// "归属"一律指格子归属值（军团下标）：单位的归属 = 它所在格子的归属，
/// 不再读 <see cref="ArmyUnitFields.LegionId"/>（游戏自带地图里那 4 个字节多是未初始化的垃圾值）。
/// </para>
/// </summary>
public interface IArmyModule : IModifier
{
    /// <summary>本模块处理的单位表版本：1（Armies）或 3（ArmiesV3）。</summary>
    int Version { get; }

    bool HasUnitAt(int col, int row);

    /// <summary>格子上单位的公共字段；没有单位时返回 null。</summary>
    ArmyUnitFields? GetFieldsAt(int col, int row);

    /// <summary>格子上单位的一行说明（状态栏用）；没有单位时返回 null。</summary>
    string? DescribeUnitAt(int col, int row);

    bool HasCopiedArmy { get; }
    bool HasCopiedArmyGroup { get; }
    ModifierResult CopyArmy(int col, int row);

    /// <summary>多选复制：记录选区内所有单位的相对布局，返回复制到的数量（0 时不覆盖已有的复制内容）。</summary>
    int CopyArmyGroup(IEnumerable<HexCoord> cells);

    /// <summary>粘贴单格复制的单位。<paramref name="autoBelong"/> 为 true 时按省会归属设置格子归属与单位的 LegionId。</summary>
    ModifierResult PasteArmy(int col, int row, bool autoBelong = false);

    /// <summary>按多选复制时的相对布局粘贴，以 (col,row) 为左上角；<paramref name="autoBelong"/> 同 <see cref="PasteArmy"/>。</summary>
    ModifierResult PasteArmyGroup(int col, int row, bool autoBelong = false);

    ModifierResult RemoveUnits(IEnumerable<HexCoord> cells);
    ModifierResult SetLegionId(int col, int row, int legionId);

    /// <summary>把这些格子上单位的 LegionId 设为所在格子的归属值（L 键）。</summary>
    ModifierResult SetLegionIdFromBelong(IEnumerable<HexCoord> cells);

    ModifierResult QuickModifyArmyPlan(int col, int row);
    ModifierResult CyclePlan(IEnumerable<HexCoord> cells);
    ModifierResult SetPlan(IEnumerable<HexCoord> cells, int plan);
    ModifierResult BatchModifyArmyPlanByLegion(int belongValue, int planValue);
    ModifierResult ModifyLegionStrength(int belongValue, int levelId);
    ModifierResult RandomizeLegionArmies(int belongValue);
    ModifierResult GenerateArmiesByProbability(int belongValue, int probability);
    ModifierResult AssignGenerals(int belongValue, int requestedGeneralCount, bool clearExisting, bool adaptToUnits);
    ModifierResult DeleteArmiesByBelong(int belongValue);
    ModifierResult DeleteAllArmies();
    ModifierResult EraseArmyBrush(int centerCol, int centerRow, int radius);
}

/// <summary>
/// v1 / v3 单位模块的共同实现。子类只提供"单位表在哪、两版结构体与 <see cref="ArmyUnitFields"/> 怎么互转、
/// 新单位的版本默认值"，强度 / 随机化 / 生成 / 分配将领 / 方案 / 复制粘贴删除都只写这一份。
/// </summary>
public abstract class ArmyModifierBase<TUnit> : ModifierBase, IArmyModule where TUnit : struct
{
    private static readonly string[] SpecialtyOrder = ["Infantry", "Armor", "Artillery", "Navy", "AirForce"];

    private TUnit? _copiedUnit;
    private List<(int RelCol, int RelRow, TUnit Unit)>? _copiedGroup;

    private readonly Random _random = new();

    public abstract int Version { get; }

    /// <summary>本版本的单位表（<see cref="MapData.Armies"/> / <see cref="MapData.ArmiesV3"/>）。</summary>
    protected abstract ObservableCollection<TUnit> UnitsOf(MapData map);

    /// <summary>单位所在格子下标（按无符号解释坐标字段）。</summary>
    protected abstract int CellOf(in TUnit unit);

    /// <summary>新单位（Q 键创建、无参数放置、按概率生成）的版本默认值。</summary>
    protected abstract TUnit CreateDefault(int cell);

    protected abstract ArmyUnitFields Read(in TUnit unit);

    /// <summary>把视图里的公共字段写回单位，版本独有的字段保持不变。</summary>
    protected abstract TUnit Write(TUnit unit, in ArmyUnitFields fields);

    private ObservableCollection<TUnit> Units => UnitsOf(_mapData!);

    #region 基础 CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        TUnit unit = parameter is TUnit given ? WithCell(given, col, row) : CreatePlacedDefault(col, row);
        Put(col, row, unit);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Placed", col, row));
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int idx = IndexOf(col, row);
        if (idx < 0) return ModifierResult.Fail(NoUnitText);

        Units.RemoveAt(idx);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Removed", col, row));
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => HasUnitAt(col, row);

    public override object? GetDataAt(int col, int row) => GetUnitAt(col, row);

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || data is not TUnit unit) return false;
        Put(col, row, WithCell(unit, col, row));
        MarkModified();
        return true;
    }

    public bool HasUnitAt(int col, int row) => IsValidCoord(col, row) && IndexOf(col, row) >= 0;

    /// <summary>格子上的单位；没有时返回 null。</summary>
    public TUnit? GetUnitAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = IndexOf(col, row);
        return idx >= 0 ? Units[idx] : null;
    }

    public ArmyUnitFields? GetFieldsAt(int col, int row)
    {
        var unit = GetUnitAt(col, row);
        return unit.HasValue ? Read(unit.Value) : null;
    }

    /// <summary>单位所在格子（按无符号解释坐标字段）。</summary>
    public HexCoord CoordOf(TUnit unit) => HexCoord.FromIndex(CellOf(unit), _mapData!.MapWidth);

    /// <summary>
    /// Q 键新建单位时交给设置窗口的初始值：版本默认值，LegionId 预填为省会归属（省会无归属时保持 0）。
    /// </summary>
    public TUnit CreateUnitTemplate(int col, int row)
    {
        var unit = CreateDefault(row * _mapData!.MapWidth + col);
        int owner = ArmyPlacementRules.CapitalBelong(_mapData, col, row);
        if (owner < 0) return unit;

        var f = Read(unit);
        f.LegionId = owner;
        return Write(unit, f);
    }

    /// <summary>
    /// 把单位放到 (col,row)（坐标字段改写为该格）：已有单位则替换，否则追加。
    /// <paramref name="autoBelong"/> 为 true 时按省会归属设置格子归属（新建单位用）；
    /// 单位的 LegionId 保持传入的值（新建模板已预填省会归属，用户在窗口里改过的不再被覆盖）。
    /// </summary>
    public ModifierResult PlaceUnit(int col, int row, TUnit unit, bool autoBelong)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        PlaceAt(unit, col, row, autoBelong, setLegionId: false);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Placed", col, row));
    }

    public string? DescribeUnitAt(int col, int row)
    {
        var unit = GetUnitAt(col, row);
        if (unit == null) return null;
        var f = Read(unit.Value);
        return Loc.F("Module.Army.Describe", Version, col, row, f.UnitType, f.Level, f.Organization,
            f.CurrentHealth, f.MaxHealth, f.General, f.Plan, _mapData!.GetBelongValue(col, row));
    }

    /// <summary>按单位自带的坐标写回（Lua 的 SetArmyField 用）：该格已有单位则替换，否则追加。</summary>
    public ModifierResult UpdateArmy(TUnit unit)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        var coord = HexCoord.FromIndex(CellOf(unit), _mapData.MapWidth);
        Put(coord.Col, coord.Row, unit);
        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.Army.Updated", "已更新单位数据"));
    }

    #endregion

    #region 复制 / 粘贴 / 删除 / 军团

    public bool HasCopiedArmy => _copiedUnit.HasValue;

    public bool HasCopiedArmyGroup => _copiedGroup is { Count: > 0 };

    public ModifierResult CopyArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        var unit = GetUnitAt(col, row);
        if (unit == null) return ModifierResult.Fail(NoUnitText);

        _copiedUnit = unit;
        // 单格复制覆盖多选缓冲区，与建筑模式 CopyBuilding 一致
        _copiedGroup = null;
        var f = Read(unit.Value);
        return ModifierResult.Ok(Loc.F("Module.Army.Copied", f.UnitType, f.Level));
    }

    public int CopyArmyGroup(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return 0;

        var found = new List<(int Col, int Row, TUnit Unit)>();
        foreach (var cell in cells.Distinct())
        {
            var unit = GetUnitAt(cell.Col, cell.Row);
            if (unit.HasValue) found.Add((cell.Col, cell.Row, unit.Value));
        }
        if (found.Count == 0) return 0;

        int minCol = found.Min(u => u.Col);
        int minRow = found.Min(u => u.Row);
        _copiedGroup = found.Select(u => (u.Col - minCol, u.Row - minRow, u.Unit)).ToList();
        _copiedUnit = null;
        return found.Count;
    }

    public ModifierResult PasteArmy(int col, int row, bool autoBelong = false)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        if (_copiedUnit == null) return ModifierResult.Fail(Loc.T("Module.Army.NothingToPaste", "没有已复制的单位数据"));

        PlaceAt(_copiedUnit.Value, col, row, autoBelong, setLegionId: autoBelong);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Pasted", col, row));
    }

    public ModifierResult PasteArmyGroup(int col, int row, bool autoBelong = false)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (_copiedGroup is not { Count: > 0 }) return ModifierResult.Fail(Loc.T("Module.Army.NothingToPaste", "没有已复制的单位数据"));

        int count = 0;
        foreach (var (relCol, relRow, unit) in _copiedGroup)
        {
            int destCol = col + relCol, destRow = row + relRow;
            if (!IsValidCoord(destCol, destRow)) continue;
            PlaceAt(unit, destCol, destRow, autoBelong, setLegionId: autoBelong);
            count++;
        }

        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.GroupPasted", count, col, row), count);
    }

    public ModifierResult RemoveUnits(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var set = CellSet(cells);
        var units = Units;
        int removed = 0;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(CellOf(units[i]))) continue;
            units.RemoveAt(i);
            removed++;
        }

        if (removed == 0) return ModifierResult.Fail(NoUnitText);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.UnitsRemoved", removed), removed);
    }

    public ModifierResult SetLegionId(int col, int row, int legionId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int idx = IndexOf(col, row);
        if (idx < 0) return ModifierResult.Fail(NoUnitText);

        Modify(idx, (ref ArmyUnitFields f) => f.LegionId = legionId);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.LegionSet", legionId));
    }

    public ModifierResult SetLegionIdFromBelong(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int count = ForEachUnitAt(cells, (ref ArmyUnitFields f) =>
        {
            var coord = HexCoord.FromIndex(f.Cell, _mapData.MapWidth);
            f.LegionId = _mapData.GetBelongValue(coord.Col, coord.Row);
        });

        if (count == 0) return ModifierResult.Fail(NoUnitText);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.LegionSetBatch", count), count);
    }

    public ModifierResult DeleteAllArmies()
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int count = Units.Count;
        Units.Clear();
        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.AllDeleted", count), count);
    }

    /// <summary>删除归属（所在格子的归属值）为 <paramref name="belongValue"/> 的所有单位。</summary>
    public ModifierResult DeleteArmiesByBelong(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var units = Units;
        int deleted = 0;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (OwnerOf(units[i]) != belongValue) continue;
            units.RemoveAt(i);
            deleted++;
        }

        if (deleted > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.DeletedByBelong", deleted, belongValue), deleted);
    }

    /// <summary>删除以 (centerCol, centerRow) 为中心、半径 <paramref name="radius"/>（行列偏移的直线距离）内的单位。</summary>
    public ModifierResult EraseArmyBrush(int centerCol, int centerRow, int radius)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var cells = new List<HexCoord>();
        for (int dr = -radius; dr <= radius; dr++)
        {
            for (int dc = -radius; dc <= radius; dc++)
            {
                if (Math.Sqrt(dr * dr + dc * dc) > radius) continue;
                if (IsValidCoord(centerCol + dc, centerRow + dr)) cells.Add(new HexCoord(centerCol + dc, centerRow + dr));
            }
        }

        var result = RemoveUnits(cells);
        return ModifierResult.Ok(Loc.F("Module.Army.Erased", result.AffectedCount), result.AffectedCount);
    }

    #endregion

    #region 方案 (T / Y / Ctrl+Q)

    public ModifierResult QuickModifyArmyPlan(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Army.OutOfRange", "坐标超出范围"));
        return CyclePlan([new HexCoord(col, row)]);
    }

    /// <summary>方案在 0-4 之间循环 +1。只改到一个单位时消息里给出新旧值。</summary>
    public ModifierResult CyclePlan(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int oldPlan = 0, newPlan = 0;
        int count = ForEachUnitAt(cells, (ref ArmyUnitFields f) =>
        {
            oldPlan = f.Plan;
            f.Plan = (short)(((f.Plan + 1) % 5 + 5) % 5);
            newPlan = f.Plan;
        });

        if (count == 0) return ModifierResult.Fail(NoUnitText);
        MarkModified();
        return count == 1
            ? ModifierResult.Ok(Loc.F("Module.Army.PlanCycled", oldPlan, newPlan))
            : ModifierResult.Ok(Loc.F("Module.Army.PlanCycledBatch", count), count);
    }

    public ModifierResult SetPlan(IEnumerable<HexCoord> cells, int plan)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int count = ForEachUnitAt(cells, (ref ArmyUnitFields f) => f.Plan = (short)plan);
        if (count == 0) return ModifierResult.Fail(NoUnitText);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.PlanSet", count, plan), count);
    }

    /// <summary>按归属批量改方案：归属 -1 = 全部单位；方案 -1 = 每个单位随机 1-4。</summary>
    public ModifierResult BatchModifyArmyPlanByLegion(int belongValue, int planValue)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        int count = ForEachUnitOf(belongValue,
            (ref ArmyUnitFields f) => f.Plan = (short)(planValue == -1 ? _random.Next(1, 5) : planValue));

        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.PlanBatchByBelong", count, belongValue, planValue), count);
    }

    #endregion

    #region 按配置修改军团强度 (E)

    /// <summary>
    /// 按强度档位（LegionLvSetting.json）修改归属为 <paramref name="belongValue"/>（-1 = 全部）的单位，
    /// 并写回军团自身的血量 / 税率系数。<paramref name="levelId"/> = -1 时按国家所在的强弱分档自动选档。
    /// </summary>
    public ModifierResult ModifyLegionStrength(int belongValue, int levelId)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (levelId == -1) return AutoAdjustLegionStrengthByCountry(belongValue);

        var config = ConfigManager.Instance.GetLegionLevelConfig(levelId) ?? DefaultLevelConfig(levelId);
        int count = ForEachUnitOf(belongValue, (ref ArmyUnitFields f) => ApplyLegionLevelConfig(ref f, config));

        var legions = _mapData.Legions;
        for (int i = 0; i < legions.Count; i++)
        {
            if (belongValue != -1 && i != belongValue) continue;
            var legion = legions[i];
            legion.CountryHpRate = (float)config.Blood;
            legion.CountryTaxRate = (float)config.Rate;
            _mapData.ReplaceLegion(i, legion);
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.StrengthModified", count, config.Name), count);
    }

    /// <summary>每个军团按其国家在 setting.txt 强 / 中 / 弱分档里随机取一个档位，套用到该军团的单位与军团系数。</summary>
    public ModifierResult AutoAdjustLegionStrengthByCountry(int targetBelongValue)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var levelByCountry = PickCountryStrengthLevels();
        var legions = _mapData.Legions;
        int processed = 0, modified = 0;

        for (int i = 0; i < legions.Count; i++)
        {
            if (targetBelongValue != -1 && i != targetBelongValue) continue;
            var legion = legions[i];
            if (!levelByCountry.TryGetValue(legion.CountryId, out int levelId)) continue;
            var config = ConfigManager.Instance.GetLegionLevelConfig(levelId);
            if (config == null) continue;

            modified += ForEachUnitOf(i, (ref ArmyUnitFields f) => ApplyLegionLevelConfig(ref f, config));
            legion.CountryHpRate = (float)config.Blood;
            legion.CountryTaxRate = (float)config.Rate;
            _mapData.ReplaceLegion(i, legion);
            processed++;
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.StrengthAuto", processed, modified), modified);
    }

    private void ApplyLegionLevelConfig(ref ArmyUnitFields f, LegionLevelConfig config)
    {
        f.MaxHealth = (short)config.HpPercent;

        if (config.ArmyLvMax > 0)
            f.Level = (byte)_random.Next(1, config.ArmyLvMax + 1);

        f.HealthBonus = config.ArmyHpBonus is { Count: >= 2 }
            ? (short)(100 + _random.Next(config.ArmyHpBonus[0], config.ArmyHpBonus[1] + 1))
            : (short)100;

        // HealthBonus 抬高的是实际血量上限，CurrentHealth 是绝对值，必须同步缩放，否则血条显示不满
        f.CurrentHealth = (short)(f.MaxHealth * f.HealthBonus / 100);

        if (config.ArmyNumMax > 0)
        {
            int actualMax = Math.Min(config.ArmyNumMax, ConfigManager.Instance.GetMaxFormation(f.UnitType));
            f.Organization = actualMax > 0 ? (byte)_random.Next(1, actualMax + 1) : (byte)0;
        }

        if (f.General > 0 && config.GeneralHpMax >= config.GeneralHpMin)
            f.GeneralHpLevel = (byte)_random.Next(config.GeneralHpMin, config.GeneralHpMax + 1);
    }

    private static LegionLevelConfig DefaultLevelConfig(int levelId) => new()
    {
        Id = levelId,
        Name = Loc.F("Module.Army.DefaultLevelName", levelId),
        Rate = 1.0,
        Blood = 1.0,
        HpPercent = 100,
        GeneralHpMin = 1,
        GeneralHpMax = 1,
        ArmyNumMax = 0,
        ArmyLvMax = 1,
        ArmyHpBonus = [0, 0]
    };

    /// <summary>国家 Id → 随机选出的强度档位 Id（setting.txt 的 Low/Medium/HighStrengthCountry × *StrengthArmy）。</summary>
    private Dictionary<int, int> PickCountryStrengthLevels()
    {
        var cfg = ConfigManager.Instance;
        var army = cfg.GetArmyEditConfig();
        var legion = cfg.GetLegionEditConfig();
        var result = new Dictionary<int, int>();

        void Pick(List<int> countries, List<int> levels)
        {
            if (levels.Count == 0) return;
            foreach (int countryId in countries)
                result[countryId] = levels[_random.Next(levels.Count)];
        }

        Pick(legion.LowStrengthCountry, army.LowStrengthArmy);
        Pick(legion.MediumStrengthCountry, army.MediumStrengthArmy);
        Pick(legion.HighStrengthCountry, army.HighStrengthArmy);
        return result;
    }

    /// <summary>归属值（军团下标）→ 该军团国家对应的强度档位配置，生成 / 放置新单位时直接套用。</summary>
    private Dictionary<int, LegionLevelConfig> BuildStrengthConfigByOwner()
    {
        var result = new Dictionary<int, LegionLevelConfig>();
        var levelByCountry = PickCountryStrengthLevels();
        var legions = _mapData!.Legions;
        for (int i = 0; i < legions.Count; i++)
        {
            if (!levelByCountry.TryGetValue(legions[i].CountryId, out int levelId)) continue;
            var config = ConfigManager.Instance.GetLegionLevelConfig(levelId);
            if (config != null) result[i] = config;
        }
        return result;
    }

    #endregion

    #region 随机化军团单位 (R)

    public ModifierResult RandomizeLegionArmies(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);

        var armyConfig = ConfigManager.Instance.GetArmyEditConfig();
        bool hasLand = ArmyPlacementRules.HasLandTiles(_mapData);
        int count = ForEachUnitOf(belongValue, (ref ArmyUnitFields f) =>
        {
            f.CurrentHealth = (short)_random.Next(50, 101);
            f.MaxHealth = 100;
            f.Level = (byte)_random.Next(1, 6);
            f.HealthBonus = (short)(100 + _random.Next(-10, 21));

            var pool = ArmyPlacementRules.IsSea(_mapData, f.Cell, hasLand) ? armyConfig.ArmyOnSea : armyConfig.ArmyOnLand;
            if (pool.Count > 0) f.UnitType = (byte)pool[_random.Next(pool.Count)];

            int maxFormation = ConfigManager.Instance.GetMaxFormation(f.UnitType);
            f.Organization = maxFormation > 0 ? (byte)_random.Next(1, maxFormation + 1) : (byte)0;

            if (f.General > 0) f.GeneralHpLevel = (byte)_random.Next(1, 4);
        });

        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Randomized", count), count);
    }

    #endregion

    #region 按概率生成单位 (I)

    /// <summary>
    /// 在归属为 <paramref name="belongValue"/>（-1 = 全部）的省份里，按概率在没有单位、且在归属数据范围内的格子上生成单位：
    /// 海洋格从 ArmyOnSea、其它格从 ArmyOnLand 取兵种（没有地形数据的征服地图按陆地）；格子归属与单位 LegionId 设为省份的归属；
    /// 能找到该军团的强度档位时直接套用，否则满血。
    /// </summary>
    public ModifierResult GenerateArmiesByProbability(int belongValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        if (probability < 0 || probability > 100) return ModifierResult.Fail(Loc.T("Module.Army.BadProbability", "概率必须在0-100之间"));

        var cfg = ConfigManager.Instance;
        var armyConfig = cfg.GetArmyEditConfig();
        var occupied = new HashSet<int>();
        foreach (var a in _mapData.Armies) occupied.Add((ushort)a.Coordinate);
        foreach (var a in _mapData.ArmiesV3) occupied.Add((ushort)a.Coordinate);

        var strengthByOwner = BuildStrengthConfigByOwner();
        bool hasLand = ArmyPlacementRules.HasLandTiles(_mapData);
        int total = _mapData.MapWidth * _mapData.MapHeight;
        int created = 0;

        // 先取齐所有省份的归属：生成过程中会改写格子归属，若某省会格恰好落在别的省份里，
        // 边生成边查会让同一省份前后取到不同的归属
        var provinceOwner = new Dictionary<int, int>();
        for (int cell = 0; cell < total; cell++)
        {
            int provinceValue = _mapData.GetProvinceRef(cell).ProvinceValue;
            if (ArmyPlacementRules.IsProvince(provinceValue) && !provinceOwner.ContainsKey(provinceValue))
                provinceOwner[provinceValue] = ArmyPlacementRules.ProvinceBelong(_mapData, provinceValue);
        }

        for (int cell = 0; cell < total; cell++)
        {
            if (occupied.Contains(cell)) continue;
            var coord = HexCoord.FromIndex(cell, _mapData.MapWidth);
            // 征服地图 MapClip 以外的格子游戏不读（VB 同样跳过）
            if (!_mapData.IsInBelongRange(coord.Col, coord.Row)) continue;

            if (!provinceOwner.TryGetValue(_mapData.GetProvinceRef(cell).ProvinceValue, out int owner)) continue;
            if (belongValue != -1 && owner != belongValue) continue;

            if (_random.Next(1, 101) > probability) continue;

            var pool = ArmyPlacementRules.IsSea(_mapData, cell, hasLand) ? armyConfig.ArmyOnSea : armyConfig.ArmyOnLand;
            if (pool.Count == 0) continue;

            if (owner >= 0) _mapData.SetBelongValue(coord.Col, coord.Row, owner);

            var unit = CreateDefault(cell);
            var f = Read(unit);
            f.UnitType = (byte)pool[_random.Next(pool.Count)];
            f.Level = (byte)_random.Next(1, 6);
            int maxFormation = cfg.GetMaxFormation(f.UnitType);
            f.Organization = (byte)(maxFormation > 0 ? _random.Next(1, maxFormation + 1) : _random.Next(1, 5));
            f.Direction = (byte)_random.Next(0, 2);
            f.Mobility = (byte)_random.Next(3, 11);
            f.HealthBonus = 100;
            f.Plan = 1;
            f.SkillLevel1 = f.SkillLevel2 = f.SkillLevel3 = f.SkillLevel4 = f.SkillLevel5 = 1;
            if (owner >= 0) f.LegionId = owner;

            if (owner >= 0 && strengthByOwner.TryGetValue(owner, out var strength))
                ApplyLegionLevelConfig(ref f, strength);
            else
                f.CurrentHealth = f.MaxHealth;

            Units.Add(Write(unit, f));
            created++;
        }

        if (created > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.Generated", created), created);
    }

    #endregion

    #region 自动分配将领 (G)

    /// <summary>
    /// 给归属为 <paramref name="belongValue"/>（-1 = 全部）的单位按其军团国家（GeneralInCountry.json）分配将领。
    /// <paramref name="clearExisting"/>：先清除<b>这些</b>单位上已有的将领再重新分配；否则只给没有将领的单位分配。
    /// <paramref name="adaptToUnits"/>：将领只分配给与其专长同类的单位；否则尽量分配完，专长不匹配时随机挑单位。
    /// <paramref name="requestedGeneralCount"/> = -1 表示每个军团按可用将领数与单位数取较小值。
    /// 将领写入 General，军衔（MilitaryRank）写入 Rank，技能等级取自将领配置。
    /// </summary>
    public ModifierResult AssignGenerals(int belongValue, int requestedGeneralCount, bool clearExisting, bool adaptToUnits)
    {
        if (_mapData == null) return ModifierResult.Fail(NoMapText);
        var units = Units;
        if (units.Count == 0) return ModifierResult.Fail(Loc.T("Module.Army.NoUnitsForGenerals", "没有单位需要分配将领"));
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail(Loc.T("Module.Army.NoLegions", "军团数据未加载，无法分配将领"));

        var cfg = ConfigManager.Instance;
        var targets = new List<int>();
        for (int i = 0; i < units.Count; i++)
        {
            if (belongValue != -1 && OwnerOf(units[i]) != belongValue) continue;
            if (clearExisting || Read(units[i]).General == 0) targets.Add(i);
        }
        if (targets.Count == 0) return ModifierResult.Fail(Loc.F("Module.Army.NoUnitsOfBelong", belongValue));

        if (clearExisting)
        {
            foreach (int i in targets)
            {
                Modify(i, (ref ArmyUnitFields f) =>
                {
                    if (f.General <= 0) return;
                    cfg.RemoveAssignedGeneral(f.General);
                    f.General = 0;
                    f.Rank = 0;
                    f.GeneralHpLevel = 0;
                    f.SkillLevel1 = f.SkillLevel2 = f.SkillLevel3 = f.SkillLevel4 = f.SkillLevel5 = 1;
                });
            }
        }

        int assigned = 0;
        foreach (var group in targets.GroupBy(i => OwnerOf(units[i])))
        {
            int owner = group.Key;
            if (owner < 0 || owner >= _mapData.Legions.Count) continue;

            var generals = cfg.GetAvailableGenerals(cfg.GetGeneralsByCountryId(_mapData.Legions[owner].CountryId));
            if (generals.Count == 0) continue;

            var groupUnits = group.ToList();
            int count = requestedGeneralCount == -1
                ? Math.Min(groupUnits.Count, generals.Count)
                : Math.Min(requestedGeneralCount, Math.Min(generals.Count, groupUnits.Count));
            if (count <= 0) continue;

            assigned += adaptToUnits
                ? AssignBySpecialty(generals, groupUnits, count)
                : AssignPreferringSpecialty(generals, groupUnits, count);
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Army.GeneralsAssigned", assigned), assigned);
    }

    /// <summary>按专长分组：步兵 / 装甲 / 炮兵 / 海军 / 空军将领只分配给对应兵种的单位。</summary>
    private int AssignBySpecialty(List<int> generals, List<int> unitIndices, int maxCount)
    {
        var cfg = ConfigManager.Instance;
        var bySpecialty = unitIndices.ToLookup(i => cfg.GetUnitSpecialtyType(Read(Units[i]).UnitType));
        int assigned = 0;

        foreach (string specialty in SpecialtyOrder)
        {
            var candidates = bySpecialty[specialty].ToList();
            if (candidates.Count == 0) continue;

            foreach (int generalId in Shuffle(generals.Where(g => cfg.GetGeneralSpecialty(g) == specialty)))
            {
                if (candidates.Count == 0 || assigned >= maxCount) break;
                int pick = _random.Next(candidates.Count);
                AssignGeneral(candidates[pick], generalId);
                candidates.RemoveAt(pick);
                assigned++;
            }
        }
        return assigned;
    }

    /// <summary>尽量分配完：每个将领优先挑同专长的单位，没有时随机挑一个。</summary>
    private int AssignPreferringSpecialty(List<int> generals, List<int> unitIndices, int maxCount)
    {
        var cfg = ConfigManager.Instance;
        var remaining = unitIndices.ToList();
        int assigned = 0;

        foreach (int generalId in Shuffle(generals))
        {
            if (remaining.Count == 0 || assigned >= maxCount) break;

            string? specialty = cfg.GetGeneralSpecialty(generalId);
            var matching = remaining.Where(i => cfg.GetUnitSpecialtyType(Read(Units[i]).UnitType) == specialty).ToList();
            int unitIndex = matching.Count > 0 ? matching[_random.Next(matching.Count)] : remaining[_random.Next(remaining.Count)];

            remaining.Remove(unitIndex);
            AssignGeneral(unitIndex, generalId);
            assigned++;
        }
        return assigned;
    }

    private void AssignGeneral(int unitIndex, int generalId)
    {
        var cfg = ConfigManager.Instance;
        var settings = cfg.GetGeneralSettingsById(generalId);
        Modify(unitIndex, (ref ArmyUnitFields f) =>
        {
            f.General = (short)generalId;
            if (settings == null) return;
            f.Rank = (byte)settings.MilitaryRank;
            for (int s = 0; s < Math.Min(settings.Skills.Count, 5); s++)
            {
                int level = settings.Skills[s] % 10;
                byte value = (byte)(level == 0 ? 1 : level);
                switch (s)
                {
                    case 0: f.SkillLevel1 = value; break;
                    case 1: f.SkillLevel2 = value; break;
                    case 2: f.SkillLevel3 = value; break;
                    case 3: f.SkillLevel4 = value; break;
                    default: f.SkillLevel5 = value; break;
                }
            }
        });
        cfg.AddAssignedGeneral(generalId);
    }

    private List<int> Shuffle(IEnumerable<int> items) => items.OrderBy(_ => _random.Next()).ToList();

    #endregion

    #region 内部

    private delegate void FieldEdit(ref ArmyUnitFields fields);

    private static string NoMapText => Loc.T("Module.Army.NoMap", "地图数据未初始化");

    private static string NoUnitText => Loc.T("Module.Army.NoUnit", "该位置没有单位");

    /// <summary>单位的归属 = 所在格子的归属值（0xFF = 无归属，或不在归属数据范围内）。</summary>
    private int OwnerOf(in TUnit unit)
    {
        var coord = HexCoord.FromIndex(CellOf(unit), _mapData!.MapWidth);
        return _mapData.GetBelongValue(coord.Col, coord.Row);
    }

    private int IndexOf(int col, int row)
    {
        int cell = row * _mapData!.MapWidth + col;
        var units = Units;
        for (int i = 0; i < units.Count; i++)
            if (CellOf(units[i]) == cell) return i;
        return -1;
    }

    private HashSet<int> CellSet(IEnumerable<HexCoord> cells)
    {
        var set = new HashSet<int>();
        foreach (var c in cells)
            if (IsValidCoord(c.Col, c.Row)) set.Add(c.ToIndex(_mapData!.MapWidth));
        return set;
    }

    private TUnit WithCell(TUnit unit, int col, int row)
    {
        var f = Read(unit);
        f.Cell = row * _mapData!.MapWidth + col;
        return Write(unit, f);
    }

    private void Put(int col, int row, TUnit unit)
    {
        int idx = IndexOf(col, row);
        if (idx >= 0) Units[idx] = unit;
        else Units.Add(unit);
    }

    private void Modify(int index, FieldEdit edit)
    {
        var unit = Units[index];
        var f = Read(unit);
        edit(ref f);
        Units[index] = Write(unit, f);
    }

    private int ForEachUnitAt(IEnumerable<HexCoord> cells, FieldEdit edit)
    {
        var set = CellSet(cells);
        int count = 0;
        for (int i = 0; i < Units.Count; i++)
        {
            if (!set.Contains(CellOf(Units[i]))) continue;
            Modify(i, edit);
            count++;
        }
        return count;
    }

    private int ForEachUnitOf(int belongValue, FieldEdit edit)
    {
        int count = 0;
        for (int i = 0; i < Units.Count; i++)
        {
            if (belongValue != -1 && OwnerOf(Units[i]) != belongValue) continue;
            Modify(i, edit);
            count++;
        }
        return count;
    }

    /// <summary>无参数放置：版本默认单位，能确定所在军团的强度档位时直接套用（不再统一无加成满血）。</summary>
    private TUnit CreatePlacedDefault(int col, int row)
    {
        var unit = CreateDefault(row * _mapData!.MapWidth + col);
        int owner = ArmyPlacementRules.ResolveOwner(_mapData, col, row);
        if (owner < 0 || !BuildStrengthConfigByOwner().TryGetValue(owner, out var config)) return unit;

        var f = Read(unit);
        ApplyLegionLevelConfig(ref f, config);
        return Write(unit, f);
    }

    /// <summary>
    /// 把单位放到 (col,row)。自动归属时：省会有归属 → 格子归属设为省会归属，<paramref name="setLegionId"/> 为 true 时
    /// 单位 LegionId 也设为它（粘贴用：复制来的 LegionId 属于原位置）；省会无归属 → 都保持不变（不再把 0xFF 写成格子归属）。
    /// </summary>
    private void PlaceAt(TUnit source, int col, int row, bool autoBelong, bool setLegionId)
    {
        var unit = WithCell(source, col, row);
        int owner = autoBelong ? ArmyPlacementRules.CapitalBelong(_mapData!, col, row) : -1;
        if (owner >= 0)
        {
            _mapData!.SetBelongValue(col, row, owner);
            if (setLegionId)
            {
                var f = Read(unit);
                f.LegionId = owner;
                unit = Write(unit, f);
            }
        }
        Put(col, row, unit);
    }

    #endregion
}
