using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>字段的存储类型（小端）。</summary>
public enum BtlFieldType
{
    U8,
    I8,
    U16,
    I16,
    U32,
    I32,
    F32,
    /// <summary>R、G、B、保留各 1 字节。</summary>
    Rgb,
    /// <summary>ASCII 文本（定长）。</summary>
    Text,
    /// <summary>原样字节（定长）。</summary>
    Blob,
    /// <summary>protobuf 变长整数（只出现在扩展数据块的解码结果里）。</summary>
    Varint
}

/// <summary>字段值的解释方式（决定"含义"一栏显示什么）。</summary>
public enum BtlValueKind
{
    None,
    /// <summary>格子序号 → (列,行)；0xFFFF / -1 表示无。</summary>
    Coordinate,
    /// <summary>省份值：0xFFFF = 无省份，否则为省会格子序号。</summary>
    ProvinceValue,
    /// <summary>归属值：0xFF = 无归属，否则为军团序号。</summary>
    BelongValue,
    /// <summary>城市名称 ID：0 / 0xFFFF = 无名称。</summary>
    CityName,
    VictoryCondition,
    ArmyDirection,
    KeyPoint,
    Policy,
    Morale,
    WeatherType,
    TriggerCondition,
    TriggerEvent,
    AirUnitType,
    AmmoType,
    AirDefenseRadar
}

/// <summary>
/// 记录里的一个字段：记录内偏移、类型、显示名（翻译键 <c>DataCheck.Field.&lt;Name&gt;</c>）、可选说明。
/// 只保存翻译键，显示名在取用时按当前语言生成。
/// </summary>
public sealed class BtlFieldDef
{
    internal BtlFieldDef(int offset, BtlFieldType type, string name, int size,
                         int ordinal = 0, string? note = null, BtlValueKind meaning = BtlValueKind.None,
                         object[]? nameArgs = null)
    {
        Offset = offset;
        Type = type;
        Name = name;
        Size = size;
        Ordinal = ordinal;
        NoteName = note;
        Meaning = meaning;
        NameArgs = nameArgs;
    }

    /// <summary>记录内偏移。</summary>
    public int Offset { get; }

    public int Size { get; }
    public BtlFieldType Type { get; }

    /// <summary>翻译键后缀：完整键为 <c>DataCheck.Field.&lt;Name&gt;</c>。</summary>
    public string Name { get; }

    /// <summary>大于 0 时显示名带序号（如"技能等级3"）。</summary>
    public int Ordinal { get; }

    /// <summary>说明的翻译键后缀（<c>DataCheck.Note.&lt;NoteName&gt;</c>），没有说明为 null。</summary>
    public string? NoteName { get; }

    public BtlValueKind Meaning { get; }

    /// <summary>显示名的格式参数（扩展块解码出的动态字段用）。</summary>
    public object[]? NameArgs { get; }

    public string Key => "DataCheck.Field." + Name;

    /// <summary>当前语言的显示名。保留 / 未知字段带上偏移，便于区分。</summary>
    public string DisplayName
    {
        get
        {
            if (NameArgs != null) return Loc.F(Key, NameArgs);
            if (Ordinal > 0) return Loc.F(Key, Ordinal);
            return Name is "Reserved" or "Unknown" ? Loc.F(Key, "0x" + Offset.ToString("X2")) : Loc.T(Key);
        }
    }

    /// <summary>当前语言的说明（没有为 null）。</summary>
    public string? Note => NoteName == null ? null : Loc.T("DataCheck.Note." + NoteName);

    /// <summary>与另一字段的字节区间是否重叠。</summary>
    public bool Overlaps(int offset, int length) => offset < Offset + Size && Offset < offset + length;
}

// BtlInspector：各数据块的记录字段表。
public sealed partial class BtlInspector
{
    private static BtlFieldDef U8(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.U8, n, 1, ord, note, m);
    private static BtlFieldDef I8(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.I8, n, 1, ord, note, m);
    private static BtlFieldDef U16(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.U16, n, 2, ord, note, m);
    private static BtlFieldDef I16(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.I16, n, 2, ord, note, m);
    private static BtlFieldDef U32(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.U32, n, 4, ord, note, m);
    private static BtlFieldDef I32(int o, string n, BtlValueKind m = BtlValueKind.None, int ord = 0, string? note = null) => new(o, BtlFieldType.I32, n, 4, ord, note, m);
    private static BtlFieldDef F32(int o, string n) => new(o, BtlFieldType.F32, n, 4);
    private static BtlFieldDef Rgb(int o, string n) => new(o, BtlFieldType.Rgb, n, 4);
    private static BtlFieldDef Text(int o, int size, string n) => new(o, BtlFieldType.Text, n, size);
    private static BtlFieldDef Blob(int o, int size, string n) => new(o, BtlFieldType.Blob, n, size);

    /// <summary>同一名字、带序号的一组字段（技能等级1-5、徽章1-3……）。</summary>
    private static IEnumerable<BtlFieldDef> Series(Func<int, string, BtlValueKind, int, string?, BtlFieldDef> make,
                                                   int start, int stride, string name, int count, string? note = null)
        => Enumerable.Range(0, count).Select(i => make(start + i * stride, name, BtlValueKind.None, i + 1, note));

    // ---------------- 头部 ----------------

    private static readonly BtlFieldDef[] HeaderFields =
    {
        I32(0x00, "BtlVersion"), I32(0x04, "MapNumber"), I32(0x08, "MapClipX"), I32(0x0C, "MapClipY"),
        I32(0x10, "MapLength"), I32(0x14, "MapWidth"), I32(0x18, "LegionCount"), I32(0x1C, "BuildingCount"),
        I32(0x20, "TroopCount"), I32(0x24, "PlanCount"), I32(0x28, "EventCount"), I32(0x2C, "WeatherCount"),
        I32(0x30, "VictoryCondition", BtlValueKind.VictoryCondition), I32(0x34, "MinTurns"), I32(0x38, "MaxTurns"),
        I32(0x3C, "ReinforcementCount"), I32(0x40, "AirRaidCount"), I32(0x44, "PlacementA"), I32(0x48, "PlacementB"),
        I32(0x4C, "CapitalCount"), I32(0x50, "HeaderUnknown", ord: 3), I32(0x54, "ExtensionLength", note: "ExtensionLength"),
        I32(0x58, "SelectableTileCount", note: "SelectableTileCount"), I32(0x5C, "AccumulatedEconomy"),
        I32(0x60, "AccumulatedIndustry"), I32(0x64, "AccumulatedTech"), I32(0x68, "TrapCount"),
        I32(0x6C, "HeaderUnknown", ord: 5), I32(0x70, "StrategyCount"), I32(0x74, "HeaderUnknown", ord: 6),
        I32(0x78, "HeaderUnknown", ord: 7), I32(0x7C, "AirSupportCount")
    };

    private static readonly BtlFieldDef[] WorldHeaderFields =
    {
        Text(0x00, 4, "Magic"), I32(0x04, "WorldVersion"), I32(0x08, "WorldWidth"), I32(0x0C, "WorldHeight")
    };

    // ---------------- 军团（300 字节，命名与 VB Legion.vb 一致） ----------------

    private static readonly string[] TechNames =
    {
        "Mobility", "Rifle", "Camouflage", "Engineer", "Grenade", "Mortar", "March", "BulletproofVest",
        "Armor", "MainGun", "VehicleBody", "Engine", "MachineGun", "Raid", "VehicleAirDefense", "ReinforcedBody",
        "Artillery", "Rocket", "Towing", "ArtilleryArmor", "Firepower", "NavyRocket", "Disguise", "Hull",
        "Propulsion", "NavyArmor", "Weapon", "NavalGun", "Torpedo", "Minesweeping", "ShipAirDefense", "ModernHull",
        "AviationFuel", "AviationEngine", "AviationBomb", "AirRaid", "Bombing", "StrategicBombing", "Airdrop", "JetEngine",
        "Bunker", "FortressGun", "CoastalGun", "RocketLauncher", "Fortification", "AntiAircraftGun", "AntiAircraftCannon",
        "AntiAircraftMissile", "Radar", "MissileWarhead", "SolidRocketEngine", "NuclearBreakthrough", "NuclearFusion"
    };

    private static readonly BtlFieldDef[] LegionFields = new[]
        {
            I32(0x00, "ActionId"), I32(0x04, "CountryId"), I32(0x08, "InitialEconomy"), I32(0x0C, "InitialIndustry"),
            I32(0x10, "InitialTech"), I32(0x14, "PlayerControlled"), I32(0x18, "Camp"), I32(0x1C, "DefeatCondition"),
            F32(0x20, "HpRate"), F32(0x24, "TaxRate"), Rgb(0x28, "CountryColor"),
            I32(0x2C, "AtomicBombs"), I32(0x30, "HydrogenBombs"), I32(0x34, "NeutronBombs"), I32(0x38, "AntimatterBombs")
        }
        .Concat(TechNames.Select((name, i) => I32(0x3C + i * 4, "Tech." + name)))
        .Concat(new[]
        {
            I32(0x110, "LegionUnknown", ord: 1), I32(0x114, "LegionUnknown", ord: 2), I32(0x118, "LegionUnknown", ord: 3),
            I32(0x11C, "CountryAiBehavior"), I32(0x120, "InitialTechLevel"), I32(0x124, "LegionUnknown", ord: 4),
            I32(0x128, "InitialLaserGunLevel")
        })
        .ToArray();

    // ---------------- 地形 / 省份 / 归属 ----------------

    private static readonly BtlFieldDef[] TerrainFields =
    {
        U8(0, "TileType", ord: 1), U8(1, "Decoration", ord: 1), U8(2, "TextureOffsetX", ord: 1), U8(3, "TextureOffsetY", ord: 1),
        U8(4, "TileType", ord: 2), U8(5, "Decoration", ord: 2), U8(6, "TextureOffsetX", ord: 2), U8(7, "TextureOffsetY", ord: 2),
        U8(8, "TileType", ord: 3), U8(9, "Decoration", ord: 3), U8(10, "TextureOffsetX", ord: 3), U8(11, "TextureOffsetY", ord: 3),
        U8(12, "Reserved"), U8(13, "Reserved"), U8(14, "RiverValue"), U8(15, "Reserved")
    };

    private static readonly BtlFieldDef[] ProvinceFields = { U16(0, "ProvinceValue", BtlValueKind.ProvinceValue) };

    private static readonly BtlFieldDef[] BelongFields = { U8(0, "BelongValue", BtlValueKind.BelongValue) };

    // ---------------- 建筑（32 字节） ----------------

    private static readonly BtlFieldDef[] BuildingFields =
    {
        U16(0x00, "Coordinate", BtlValueKind.Coordinate), U16(0x02, "CityName", BtlValueKind.CityName),
        U8(0x04, "BuildingType"), U8(0x05, "Appearance"), U8(0x06, "LandmarkBuilding"), U8(0x07, "DecorativeBuilding"),
        U8(0x08, "SkillUnlock"), U8(0x09, "RewardCount"), U16(0x0A, "Reserved"), I8(0x0C, "Hatred"),
        U8(0x0D, "KeyPoint", BtlValueKind.KeyPoint), U8(0x0E, "OccupationEvent"), U8(0x0F, "Reserved"), U32(0x10, "Reserved"),
        U8(0x14, "FireIgnition"), U8(0x15, "FireDuration"), U8(0x16, "AirDefenseWeapon"),
        U8(0x17, "AirDefenseRadar", BtlValueKind.AirDefenseRadar), U8(0x18, "FactoryLevel"), U8(0x19, "ResearchLevel"),
        U8(0x1A, "MedicalLevel"), U8(0x1B, "AviationLevel"), U8(0x1C, "MissileLevel"), U8(0x1D, "NuclearLevel"),
        U16(0x1E, "Reserved")
    };

    // ---------------- 单位（v1 48 字节 / v3 64 字节） ----------------
    // 两版前 0x30 字节逐字节同位同义（按全部资源关卡的取值分布核实）；名称沿用各自版本在编辑器里的叫法，
    // 说明一栏给出另一版本同位字段的名字。

    private static readonly BtlFieldDef[] ArmyCommonHead =
    {
        U16(0x00, "Coordinate", BtlValueKind.Coordinate), U8(0x02, "UnitType"), U8(0x03, "Level"), U8(0x04, "Organization"),
        U8(0x05, "Direction", BtlValueKind.ArmyDirection), U8(0x06, "Mobility"), U8(0x07, "BuiltRound"), I16(0x08, "Experience"),
        I16(0x0A, "HealthBonus"), I16(0x0C, "CurrentHealth"), I16(0x0E, "MaxHealth"), U16(0x10, "General"), U8(0x12, "Rank")
    };

    private static readonly BtlFieldDef[] ArmyV1Fields = ArmyCommonHead
        .Append(U8(0x13, "Nobility", note: "NobilityHpLevel"))
        .Concat(Series(U8, 0x14, 1, "Badge", 3, "BadgeMedal"))
        .Concat(Series(U8, 0x17, 1, "SkillLevel", 5))
        .Concat(new[]
        {
            U8(0x1C, "KeyPoint", BtlValueKind.KeyPoint), U8(0x1D, "Policy", BtlValueKind.Policy),
            U8(0x1E, "OccupationEvent", note: "ArmyByte1E"), U8(0x1F, "Field1F"), I16(0x20, "Reserved", note: "ArmyHatredV1"),
            I16(0x22, "Plan", note: "ArmyPlanV1"), I16(0x24, "Reserved", note: "ArmyPlanV3"), I16(0x26, "ChangeRound"),
            U8(0x28, "Morale", BtlValueKind.Morale), U8(0x29, "MoraleDuration"), U8(0x2A, "Dialogue"), U8(0x2B, "CanAttack"),
            I32(0x2C, "EditorLegionId", note: "ArmyLegionV1")
        })
        .ToArray();

    private static readonly BtlFieldDef[] ArmyV3Fields = ArmyCommonHead
        .Append(U8(0x13, "HpLevel", note: "NobilityHpLevel"))
        .Concat(Series(U8, 0x14, 1, "Unused", 3, "UnusedBadge"))
        .Concat(Series(U8, 0x17, 1, "SkillLevel", 5))
        .Concat(new[]
        {
            U8(0x1C, "KeyPoint", BtlValueKind.KeyPoint), U8(0x1D, "Policy", BtlValueKind.Policy),
            U8(0x1E, "TransportShip", note: "ArmyByte1E"), U8(0x1F, "Field1F"), I16(0x20, "Hatred"),
            I16(0x22, "AttackTarget", note: "ArmyPlanV1"), I16(0x24, "Plan"), I16(0x26, "ChangeRound"),
            U8(0x28, "Morale", BtlValueKind.Morale), U8(0x29, "MoraleDuration"), U8(0x2A, "DeathDialogue"),
            U8(0x2B, "LevelMarkDisplay"), I32(0x2C, "AntiDefenseMode", note: "ArmyLegionV1")
        })
        .Concat(Series(U8, 0x30, 1, "Medal", 3, "BadgeMedal"))
        .Concat(Series(U8, 0x33, 1, "Ribbon", 3))
        .Concat(new[]
        {
            // 0x3C / 0x3E 在资源里有取值（0x3C：5/8/12/15/16，0x3E：2），含义未知；其余字节恒为 0
            I32(0x36, "EditorLegionId", note: "ArmyLegionV3"), U16(0x3A, "Reserved"), U8(0x3C, "Unknown"),
            U8(0x3D, "Reserved"), U8(0x3E, "Unknown"), U8(0x3F, "Reserved")
        })
        .ToArray();

    // ---------------- 陷阱 / 方案 / 天气 / 事件 ----------------

    private static readonly BtlFieldDef[] TrapFields =
    {
        U16(0x00, "Coordinate", BtlValueKind.Coordinate), I16(0x02, "OwnerLegion"), I16(0x04, "Organization"),
        I16(0x06, "TrapHealth"), I16(0x08, "Extra", ord: 1), I16(0x0A, "Extra", ord: 2)
    };

    private static readonly BtlFieldDef[] CaseFields =
    {
        I32(0x00, "PolicyNumber"), I32(0x04, "EndEvent"), I32(0x08, "TriggerRound"), I32(0x0C, "TargetTile", BtlValueKind.Coordinate)
    };

    private static readonly BtlFieldDef[] WeatherFields =
    {
        I32(0x00, "WeatherType", BtlValueKind.WeatherType), I32(0x04, "Reserved"), I32(0x08, "TriggerRound"), I32(0x0C, "Duration")
    };

    private static readonly BtlFieldDef[] EventFields =
    {
        I32(0x00, "Sequence"), I32(0x04, "RelatedEvent"), I32(0x08, "TriggerCondition", BtlValueKind.TriggerCondition),
        I32(0x0C, "TriggerEvent", BtlValueKind.TriggerEvent), I32(0x10, "TriggerLegion"), I32(0x14, "AffectedLegion"),
        I32(0x18, "CampChange"), I32(0x1C, "Reserved"), I32(0x20, "TriggerRound"), I32(0x24, "DialogueCode"),
        U32(0x28, "EndSegment")
    };

    // ---------------- 援军（v1 80 字节 / v3 104 字节） ----------------
    // v1 记录 = v3 记录的前 80 字节（同位同义）；v3 在末尾追加勋章 / 勋带，并把 0x3C 起的徽章移到 0x50。

    private static readonly BtlFieldDef[] ReinforcementCommonHead =
    {
        I32(0x00, "Coordinate", BtlValueKind.Coordinate), I32(0x04, "UnitType"), I32(0x08, "Level"), I32(0x0C, "Organization")
    };

    private static readonly BtlFieldDef[] ReinforcementV1Fields = ReinforcementCommonHead
        .Concat(new[]
        {
            I32(0x10, "ReinforcementFacing", note: "ReinforcementField10"), I32(0x14, "Unknown", note: "ReinforcementField14"),
            I32(0x18, "Unknown", note: "ReinforcementField18"), I32(0x1C, "General"), I32(0x20, "Rank"),
            I32(0x24, "Quality", note: "QualityHpLevel")
        })
        .Concat(Series(I32, 0x28, 4, "SkillLevel", 5))
        .Concat(Series(I32, 0x3C, 4, "Badge", 3, "BadgeMedal"))
        .Concat(new[] { I32(0x48, "OwnerCountry", note: "OwnerLegion"), I32(0x4C, "SpawnRound") })
        .ToArray();

    private static readonly BtlFieldDef[] ReinforcementV3Fields = ReinforcementCommonHead
        .Concat(new[]
        {
            I32(0x10, "BehaviorMode", note: "ReinforcementField10"), I32(0x14, "Direction", note: "ReinforcementField14"),
            I32(0x18, "Unknown", note: "ReinforcementField18"), I32(0x1C, "General"), I32(0x20, "Rank"),
            I32(0x24, "HpLevel", note: "QualityHpLevel")
        })
        .Concat(Series(I32, 0x28, 4, "SkillLevel", 5))
        .Concat(Series(I32, 0x3C, 4, "Unused", 3, "UnusedBadge"))
        .Concat(new[] { I32(0x48, "OwnerLegion", note: "OwnerLegion"), I32(0x4C, "SpawnRound") })
        .Concat(Series(I32, 0x50, 4, "Medal", 3, "BadgeMedal"))
        .Concat(Series(I32, 0x5C, 4, "Ribbon", 3))
        .ToArray();

    // ---------------- 空袭 / 单位位置 / 首都 / 战略建设 / 空中支援 ----------------

    private static readonly BtlFieldDef[] AirRaidFields =
    {
        I32(0x00, "Coordinate", BtlValueKind.Coordinate), I32(0x04, "AirUnitType", BtlValueKind.AirUnitType),
        I32(0x08, "AmmoType", BtlValueKind.AmmoType), I32(0x0C, "OwnerLegion"), I32(0x10, "TriggerRound")
    };

    private static readonly BtlFieldDef[] UnitPlacementFields =
    {
        I32(0x00, "Coordinate", BtlValueKind.Coordinate), U8(0x04, "Direction"), U8(0x05, "PlacementSequence"),
        U8(0x06, "Unknown"), U8(0x07, "TransportShip")
    };

    private static readonly BtlFieldDef[] CapitalFields = { I32(0x00, "Coordinate", BtlValueKind.Coordinate) };

    private static readonly BtlFieldDef[] StrategyFields =
    {
        I32(0x00, "LegionId"), I32(0x04, "Reserved"), I32(0x08, "Reserved"), I32(0x0C, "ConstructionCode")
    };

    private static readonly BtlFieldDef[] AirSupportFields =
    {
        I32(0x00, "AirUnitType", BtlValueKind.AirUnitType), I32(0x04, "AmmoType", BtlValueKind.AmmoType),
        I32(0x08, "OwnerLegion"), I32(0x0C, "TriggerRound")
    };

    /// <summary>单位记录的字段表。</summary>
    public static IReadOnlyList<BtlFieldDef> ArmySchema(BtlRecordFamily family)
        => family == BtlRecordFamily.V3 ? ArmyV3Fields : ArmyV1Fields;

    /// <summary>援军记录的字段表。</summary>
    public static IReadOnlyList<BtlFieldDef> ReinforcementSchema(BtlRecordFamily family)
        => family == BtlRecordFamily.V3 ? ReinforcementV3Fields : ReinforcementV1Fields;

    private static IReadOnlyList<BtlFieldDef> SchemaFor(BtlBlock block, InspectedFileKind kind, BtlRecordFamily family) => block switch
    {
        BtlBlock.Header => kind switch
        {
            InspectedFileKind.World => WorldHeaderFields,
            InspectedFileKind.Stage or InspectedFileKind.Conquest => HeaderFields,
            _ => Array.Empty<BtlFieldDef>()
        },
        BtlBlock.Legion => LegionFields,
        BtlBlock.Terrain => TerrainFields,
        BtlBlock.Province => ProvinceFields,
        BtlBlock.Belong => BelongFields,
        BtlBlock.Building => BuildingFields,
        BtlBlock.Army => ArmySchema(family),
        BtlBlock.Trap => TrapFields,
        BtlBlock.Case => CaseFields,
        BtlBlock.Weather => WeatherFields,
        BtlBlock.Event => EventFields,
        BtlBlock.Reinforcement => ReinforcementSchema(family),
        BtlBlock.AirRaid => AirRaidFields,
        BtlBlock.UnitPlacement => UnitPlacementFields,
        BtlBlock.Capital => CapitalFields,
        BtlBlock.Strategy => StrategyFields,
        BtlBlock.AirSupport => AirSupportFields,
        _ => Array.Empty<BtlFieldDef>()
    };
}
