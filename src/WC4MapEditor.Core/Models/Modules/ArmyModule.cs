namespace WC4MapEditor.Core.Models.Modules;

/// <summary>
/// 部队数据修改器。
/// </summary>
public static class ArmyModule
{
    public static void SetUnitType(ref Army army, byte unitType) => army.UnitType = unitType;

    public static void SetLevel(ref Army army, byte level) => army.Level = level;

    public static void SetHealth(ref Army army, short current, short max)
    {
        army.CurrentHealth = current;
        army.MaxHealth = max;
    }

    public static void SetGeneral(ref Army army, short general) => army.General = general;

    public static void SetSkillLevels(ref Army army, byte s1, byte s2, byte s3, byte s4, byte s5)
    {
        army.SkillLevel1 = s1;
        army.SkillLevel2 = s2;
        army.SkillLevel3 = s3;
        army.SkillLevel4 = s4;
        army.SkillLevel5 = s5;
    }

    public static void SetPolicy(ref Army army, byte policy) => army.Policy = policy;

    public static void SetMorale(ref Army army, byte morale) => army.Morale = morale;

    public static void CopyArmy(ref Army dest, in Army src)
    {
        dest = src;
    }

    public static int FindByCoordinate(ReadOnlySpan<Army> armies, short coord)
    {
        for (int i = 0; i < armies.Length; i++)
        {
            if (armies[i].Coordinate == coord) return i;
        }
        return -1;
    }

    public static void SetLegionIdBatch(Span<Army> armies, int legionId)
    {
        for (int i = 0; i < armies.Length; i++)
        {
            armies[i].LegionId = legionId;
        }
    }
}
