namespace WC4MapEditor.Core.Models.Modules;

/// <summary>
/// 建筑修改模块 - 直接操作Building结构体数组
/// </summary>
public static class BuildingModule
{
    /// <summary>
    /// 设置建筑类型
    /// </summary>
    public static void SetBuildingType(ref Building building, byte type)
    {
        building.BuildingType = type;
    }

    /// <summary>
    /// 设置建筑外观
    /// </summary>
    public static void SetAppearance(ref Building building, byte appearance)
    {
        building.Appearance = appearance;
    }

    /// <summary>
    /// 设置建筑等级
    /// </summary>
    public static void SetLevel(ref Building building, byte factory, byte research, byte medical,
        byte aviation, byte missile, byte nuclear)
    {
        building.FactoryLevel = factory;
        building.ResearchLevel = research;
        building.MedicalLevel = medical;
        building.AviationLevel = aviation;
        building.MissileLevel = missile;
        building.NuclearLevel = nuclear;
    }

    /// <summary>
    /// 设置关键据点
    /// </summary>
    public static void SetKeyPoint(ref Building building, byte value)
    {
        building.KeyPoint = value;
    }

    /// <summary>
    /// 设置占领触发事件
    /// </summary>
    public static void SetOccupationEvent(ref Building building, byte value)
    {
        building.OccupationEvent = value;
    }

    /// <summary>
    /// 设置防空武器
    /// </summary>
    public static void SetAirDefense(ref Building building, byte weapon, byte radar)
    {
        building.AirDefenseWeapon = weapon;
        building.AirDefenseRadar = radar;
    }

    /// <summary>
    /// 设置火焰属性
    /// </summary>
    public static void SetFireProperties(ref Building building, byte ignition, byte duration)
    {
        building.FireIgnition = ignition;
        building.FireDuration = duration;
    }

    /// <summary>
    /// 复制建筑数据
    /// </summary>
    public static void CopyBuilding(ref Building source, ref Building target)
    {
        target = source;
    }

    /// <summary>
    /// 批量设置建筑等级
    /// </summary>
    public static void SetLevelBatch(Span<Building> buildings, byte factory, byte research, byte medical)
    {
        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].BuildingType == 0) continue;
            var b = buildings[i];
            b.FactoryLevel = factory;
            b.ResearchLevel = research;
            b.MedicalLevel = medical;
            buildings[i] = b;
        }
    }

    /// <summary>
    /// 查找指定类型的建筑
    /// </summary>
    public static int FindByType(ReadOnlySpan<Building> buildings, byte type)
    {
        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].BuildingType == type) return i;
        }
        return -1;
    }

    /// <summary>
    /// 统计建筑类型数量
    /// </summary>
    public static int CountByType(ReadOnlySpan<Building> buildings, byte type)
    {
        int count = 0;
        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].BuildingType == type) count++;
        }
        return count;
    }
}
