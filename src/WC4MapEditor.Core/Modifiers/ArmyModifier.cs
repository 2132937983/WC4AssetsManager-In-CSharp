using System.Collections.ObjectModel;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// v1 / v2 地图（<see cref="MapData.Armies"/>）的单位模块。公共操作见 <see cref="ArmyModifierBase{TUnit}"/>；
/// 这里只有 v1 独有的部分：占领事件字段，以及单位部署模式的橡皮擦画笔（画笔对 v1 / v3 单位都生效）。
/// </summary>
public sealed class ArmyModifier : ArmyModifierBase<Army>, IBrushTarget
{
    public override string Name => "army";
    public override string DisplayName => "单位修改器";
    public override int Version => 1;

    private readonly BrushEngine _brushEngine = new();

    /// <summary>单位橡皮擦（H 键开关，[ / ] 调半径，右键拖动擦除）。</summary>
    public BrushEngine Brush => _brushEngine;

    bool IBrushTarget.BrushActive
    {
        get => _brushEngine.Active;
        set => _brushEngine.Active = value;
    }

    int IBrushTarget.BrushSize
    {
        get => _brushEngine.Radius;
        set => _brushEngine.Radius = value;
    }

    string IBrushTarget.BrushShape
    {
        get => _brushEngine.Shape;
        set => _brushEngine.Shape = value;
    }

    /// <summary>橡皮擦：删除画笔范围内的 v1 与 v3 单位（撤销由场景在一笔开始 / 结束时拍实体快照）。</summary>
    public void PaintWithBrush(int centerCol, int centerRow)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        var cells = new HashSet<int>();
        foreach (var (c, r) in _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight))
            cells.Add(r * _mapData.MapWidth + c);

        bool removed = RemoveWhere(_mapData.Armies, a => cells.Contains((ushort)a.Coordinate))
                     | RemoveWhere(_mapData.ArmiesV3, a => cells.Contains((ushort)a.Coordinate));
        if (removed) MarkModified();
    }

    /// <summary>自动分配将领（v1 单位表）。参数含义见 <see cref="ArmyModifierBase{TUnit}.AssignGenerals"/>。</summary>
    public ModifierResult AutoAssignGeneralsToArmies(int targetBelongValue, int requestedGeneralCount, bool clearExisting, bool adaptToUnits)
        => AssignGenerals(targetBelongValue, requestedGeneralCount, clearExisting, adaptToUnits);

    /// <summary>格子上 v1 单位的占领触发事件序号；没有单位时返回 null（v3 单位没有这个字段）。</summary>
    public byte? GetOccupationEvent(int col, int row) => GetUnitAt(col, row)?.OccupationEvent;

    /// <summary>设置格子上 v1 单位的占领触发事件序号（Ctrl+Q 单选）。</summary>
    public ModifierResult SetOccupationEvent(int col, int row, byte eventId)
    {
        var unit = GetUnitAt(col, row);
        if (unit == null) return ModifierResult.Fail(Loc.T("Module.Army.NoUnit", "该位置没有单位"));

        var army = unit.Value;
        byte old = army.OccupationEvent;
        army.OccupationEvent = eventId;
        Apply(col, row, army);
        return ModifierResult.Ok(Loc.F("Module.Army.OccupationEventSet", col, row, old, eventId));
    }

    protected override ObservableCollection<Army> UnitsOf(MapData map) => map.Armies;

    protected override int CellOf(in Army unit) => (ushort)unit.Coordinate;

    protected override Army CreateDefault(int cell) => Army.CreateDefault(cell);

    protected override ArmyUnitFields Read(in Army a) => new()
    {
        Cell = (ushort)a.Coordinate,
        UnitType = a.UnitType,
        Level = a.Level,
        Organization = a.Organization,
        Direction = a.Direction,
        Mobility = a.Mobility,
        HealthBonus = a.HealthBonus,
        CurrentHealth = a.CurrentHealth,
        MaxHealth = a.MaxHealth,
        General = a.General,
        Rank = a.Rank,
        GeneralHpLevel = a.Nobility,
        SkillLevel1 = a.SkillLevel1,
        SkillLevel2 = a.SkillLevel2,
        SkillLevel3 = a.SkillLevel3,
        SkillLevel4 = a.SkillLevel4,
        SkillLevel5 = a.SkillLevel5,
        KeyPoint = a.KeyPoint,
        Plan = a.Plan,
        LegionId = a.LegionId,
    };

    protected override Army Write(Army a, in ArmyUnitFields f)
    {
        a.Coordinate = unchecked((short)f.Cell);
        a.UnitType = f.UnitType;
        a.Level = f.Level;
        a.Organization = f.Organization;
        a.Direction = f.Direction;
        a.Mobility = f.Mobility;
        a.HealthBonus = f.HealthBonus;
        a.CurrentHealth = f.CurrentHealth;
        a.MaxHealth = f.MaxHealth;
        a.General = f.General;
        a.Rank = f.Rank;
        a.Nobility = f.GeneralHpLevel;
        a.SkillLevel1 = f.SkillLevel1;
        a.SkillLevel2 = f.SkillLevel2;
        a.SkillLevel3 = f.SkillLevel3;
        a.SkillLevel4 = f.SkillLevel4;
        a.SkillLevel5 = f.SkillLevel5;
        a.KeyPoint = f.KeyPoint;
        a.Plan = f.Plan;
        a.LegionId = f.LegionId;
        return a;
    }

    private static bool RemoveWhere<T>(ObservableCollection<T> list, Func<T, bool> match)
    {
        bool removed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!match(list[i])) continue;
            list.RemoveAt(i);
            removed = true;
        }
        return removed;
    }
}
