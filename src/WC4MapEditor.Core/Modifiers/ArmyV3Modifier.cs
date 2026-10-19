using System.Collections.ObjectModel;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// v3 地图（BtlVersion &gt;= 3，<see cref="MapData.ArmiesV3"/>）的单位模块。公共操作见 <see cref="ArmyModifierBase{TUnit}"/>。
/// </summary>
public sealed class ArmyV3Modifier : ArmyModifierBase<Army_3>
{
    public override string Name => "army_v3";
    public override string DisplayName => "v3单位修改器";
    public override int Version => 3;

    /// <summary>自动分配将领（v3 单位表）。参数含义见 <see cref="ArmyModifierBase{TUnit}.AssignGenerals"/>。</summary>
    public ModifierResult AutoAssignGeneralsToArmies3(int targetBelongValue, int requestedGeneralCount, bool clearExisting, bool adaptToUnits)
        => AssignGenerals(targetBelongValue, requestedGeneralCount, clearExisting, adaptToUnits);

    protected override ObservableCollection<Army_3> UnitsOf(MapData map) => map.ArmiesV3;

    protected override int CellOf(in Army_3 unit) => (ushort)unit.Coordinate;

    /// <summary>
    /// v3 新单位默认显示等级标记（LevelMarkDisplay = 1）：游戏自带 v3 地图里约 87% 的单位是 1，
    /// 结构体默认的 0 会让新单位不显示等级标记。
    /// </summary>
    protected override Army_3 CreateDefault(int cell)
    {
        var army = Army_3.CreateDefault(cell);
        army.LevelMarkDisplay = 1;
        return army;
    }

    protected override ArmyUnitFields Read(in Army_3 a) => new()
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
        GeneralHpLevel = a.HpLevel,
        SkillLevel1 = a.SkillLevel1,
        SkillLevel2 = a.SkillLevel2,
        SkillLevel3 = a.SkillLevel3,
        SkillLevel4 = a.SkillLevel4,
        SkillLevel5 = a.SkillLevel5,
        KeyPoint = a.KeyPoint,
        Plan = a.Plan,
        LegionId = a.LegionId,
    };

    protected override Army_3 Write(Army_3 a, in ArmyUnitFields f)
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
        a.HpLevel = f.GeneralHpLevel;
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
}
