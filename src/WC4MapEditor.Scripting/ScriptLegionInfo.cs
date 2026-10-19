namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给脚本的军团快照（只读值对象）。
/// <para>
/// <c>Index</c> 就是归属值（Belong 存的就是军团列表索引），用它把"军团"和"地图上的归属"对起来。
/// 改属性请用 <c>map.SetLegionField(index, 字段名, 值)</c>；改国家 ID 请走
/// <c>map.SetLegionCountryIdAt</c>（它会校验唯一性），因此 <c>CountryId</c> 不在通用字段白名单里。
/// </para>
/// </summary>
public sealed class ScriptLegionInfo
{
    /// <summary>军团列表索引 —— 同时就是归属值（0 ~ LegionCount-1）</summary>
    public int Index { get; init; }

    /// <summary>行动顺序</summary>
    public int ActionId { get; init; }

    /// <summary>国家 ID</summary>
    public int CountryId { get; init; }

    /// <summary>初始经济</summary>
    public int InitialEconomy { get; init; }

    /// <summary>初始工业</summary>
    public int InitialIndustry { get; init; }

    /// <summary>初始科技</summary>
    public int InitialTech { get; init; }

    /// <summary>控制值：0 = 玩家控制，1 = AI（征服地图全部为 1）</summary>
    public int IsPlayerControlled { get; init; }

    /// <summary>是否玩家控制（布尔便捷值）</summary>
    public bool IsPlayer { get; init; }

    /// <summary>阵营</summary>
    public int Camp { get; init; }

    /// <summary>战败条件</summary>
    public int DefeatCondition { get; init; }

    /// <summary>国家血率（float）</summary>
    public double CountryHpRate { get; init; }

    /// <summary>国家税率（float）</summary>
    public double CountryTaxRate { get; init; }

    /// <summary>颜色 R</summary>
    public int ColorR { get; init; }

    /// <summary>颜色 G</summary>
    public int ColorG { get; init; }

    /// <summary>颜色 B</summary>
    public int ColorB { get; init; }

    /// <summary>原子弹数量</summary>
    public int AtomicBombCount { get; init; }

    /// <summary>氢弹数量</summary>
    public int HydrogenBombCount { get; init; }

    /// <summary>三相弹数量（信息面板里的叫法；不是"中子弹"）</summary>
    public int NeutronBombCount { get; init; }

    /// <summary>反物质弹数量</summary>
    public int AntimatterBombCount { get; init; }

    /// <summary>初始科技等级</summary>
    public int InitialTechLevel { get; init; }

    /// <summary>国家 AI 行为</summary>
    public int CountryAiBehavior { get; init; }

    /// <summary>陆军平均等级（只读派生值）</summary>
    public double AverageArmyLevel { get; init; }

    /// <summary>海军平均等级（只读派生值）</summary>
    public double AverageNavyLevel { get; init; }

    /// <summary>空军平均等级（只读派生值）</summary>
    public double AverageAirForceLevel { get; init; }

    /// <summary>防御平均等级（只读派生值）</summary>
    public double AverageDefenseLevel { get; init; }

    // ---------- 科技等级（改名用 map.SetLegionField，字段名与本属性同名） ----------

    // 陆军
    public int MobilityLevel { get; init; }
    public int RifleLevel { get; init; }
    public int CamouflageLevel { get; init; }
    public int EngineerLevel { get; init; }
    public int GrenadeLevel { get; init; }
    public int MortarLevel { get; init; }
    public int MarchLevel { get; init; }
    public int BulletproofVestLevel { get; init; }
    public int ArmorLevel { get; init; }
    public int MainGunLevel { get; init; }
    public int VehicleBodyLevel { get; init; }
    public int EngineLevel { get; init; }
    public int MachineGunLevel { get; init; }
    public int RaidLevel { get; init; }
    public int VehicleAirDefenseLevel { get; init; }
    public int ReinforcedBodyLevel { get; init; }
    public int ArtilleryLevel { get; init; }
    public int RocketLevel { get; init; }
    public int TowingLevel { get; init; }
    public int ArtilleryArmorLevel { get; init; }
    public int FirepowerLevel { get; init; }

    // 海军
    public int NavyRocketLevel { get; init; }
    public int DisguiseLevel { get; init; }
    public int HullLevel { get; init; }
    public int PropulsionLevel { get; init; }
    public int NavyArmorLevel { get; init; }
    public int WeaponLevel { get; init; }
    public int NavalGunLevel { get; init; }
    public int TorpedoLevel { get; init; }
    public int MinesweepingLevel { get; init; }
    public int ShipAirDefenseLevel { get; init; }
    public int ModernHullLevel { get; init; }

    // 空军
    public int AviationFuelLevel { get; init; }
    public int AviationEngineLevel { get; init; }
    public int AviationBombLevel { get; init; }
    public int AirRaidLevel { get; init; }
    public int BombingLevel { get; init; }
    public int StrategicBombingLevel { get; init; }
    public int AirdropLevel { get; init; }
    public int JetEngineLevel { get; init; }

    // 防御 / 导弹
    public int BunkerLevel { get; init; }
    public int FortressGunLevel { get; init; }
    public int CoastalGunLevel { get; init; }
    public int RocketLauncherLevel { get; init; }
    public int FortificationLevel { get; init; }
    public int AntiAircraftGunLevel { get; init; }
    public int AntiAircraftCannonLevel { get; init; }
    public int AntiAircraftMissileLevel { get; init; }
    public int RadarLevel { get; init; }
    public int MissileWarheadLevel { get; init; }
    public int SolidRocketEngineLevel { get; init; }
    public int NuclearBombBreakthroughLevel { get; init; }
    public int NuclearFusionLevel { get; init; }
    public int InitialLaserGunLevel { get; init; }
}
