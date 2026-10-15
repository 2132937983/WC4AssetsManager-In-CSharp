namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给脚本的建筑快照（只读值对象）。
/// <para>
/// 刻意用纯数据 + 基础类型，让脚本层不必引用 Core 的程序集，
/// 从而保持 "Core 引用 Scripting" 的依赖方向不出现循环。
/// </para>
/// </summary>
public sealed class ScriptBuildingInfo
{
    /// <summary>格子列号</summary>
    public int Col { get; init; }

    /// <summary>格子行号</summary>
    public int Row { get; init; }

    /// <summary>格子线性索引（row * MapWidth + col）</summary>
    public int Index { get; init; }

    /// <summary>建筑类型（11~15 为一级~五级城等）</summary>
    public int BuildingType { get; init; }

    /// <summary>建筑类型的中文名</summary>
    public string BuildingTypeName { get; init; } = "";

    /// <summary>名称 ID（无符号 16 位，0xFFFF 表示无名称）</summary>
    public int NameId { get; init; }

    /// <summary>名称（查不到时为空字符串）</summary>
    public string Name { get; init; } = "";

    /// <summary>是否有名称（NameId 不为 0 且不为 0xFFFF）</summary>
    public bool HasName { get; init; }

    /// <summary>建筑所在格子的归属值（0xFF 表示无归属）</summary>
    public int Belong { get; init; }

    // ---------- 以下为建筑对象自身的字段（可用 SetBuildingField 按名改写） ----------

    /// <summary>外观值（0-255）</summary>
    public int Appearance { get; init; }

    /// <summary>地标建筑</summary>
    public int LandmarkBuilding { get; init; }

    /// <summary>装饰建筑</summary>
    public int DecorativeBuilding { get; init; }

    /// <summary>解锁技能</summary>
    public int SkillUnlock { get; init; }

    /// <summary>奖励数量</summary>
    public int RewardCount { get; init; }

    /// <summary>仇恨值</summary>
    public int HatredValue { get; init; }

    /// <summary>关键据点：0=否，1=红圈，2=绿圈</summary>
    public int KeyPoint { get; init; }

    /// <summary>关键据点的中文名</summary>
    public string KeyPointName { get; init; } = "";

    /// <summary>占领事件 ID</summary>
    public int OccupationEvent { get; init; }

    /// <summary>起火</summary>
    public int FireIgnition { get; init; }

    /// <summary>燃烧持续</summary>
    public int FireDuration { get; init; }

    /// <summary>防空武器</summary>
    public int AirDefenseWeapon { get; init; }

    /// <summary>防空雷达：0=否，2=是</summary>
    public int AirDefenseRadar { get; init; }

    /// <summary>防空雷达的中文名</summary>
    public string AirDefenseRadarName { get; init; } = "";

    /// <summary>工厂等级（0-4；校验器里叫"工业"）</summary>
    public int FactoryLevel { get; init; }

    /// <summary>研究所等级（0-4；校验器里叫"科技"）</summary>
    public int ResearchLevel { get; init; }

    /// <summary>补给站等级（0-4；信息面板里写"医疗等级"，校验器里叫"补给站"）</summary>
    public int MedicalLevel { get; init; }

    /// <summary>机场等级（0-4；信息面板里写"航空等级"，校验器里叫"机场"）</summary>
    public int AviationLevel { get; init; }

    /// <summary>导弹等级</summary>
    public int MissileLevel { get; init; }

    /// <summary>核武等级</summary>
    public int NuclearLevel { get; init; }
}
