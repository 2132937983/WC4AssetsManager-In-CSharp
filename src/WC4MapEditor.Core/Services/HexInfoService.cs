using System.Runtime.CompilerServices;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 格子信息：把一格上的地形 / 省份 / 归属 / 建筑 / 单位 / 军团 / 陷阱 / 援军整理成可显示的分区（文字随当前语言）。
/// <para>
/// v1/v2 地图读 <see cref="MapData.Armies"/> / <see cref="MapData.Reinforcements"/>，
/// v3 地图（BtlVersion ≥ 3）读 <see cref="MapData.ArmiesV3"/> / <see cref="MapData.ReinforcementsV3"/>。
/// 归属一律按 <see cref="MapData.GetBelongValue"/> 换算（裁剪过的征服地图上归属序号 ≠ 格子序号）。
/// </para>
/// </summary>
public sealed class HexInfoService : IHexInfoProvider
{
    private static readonly object _lock = new();
    private static HexInfoService? _instance;

    /// <summary>
    /// 全局默认实例。
    /// <para>
    /// 【迁移中】本服务实现 <see cref="IHexInfoProvider"/> 接口，新代码应依赖该接口并经由
    /// 依赖注入获取实例（见 <c>App.xaml.cs</c> 的注册）。
    /// </para>
    /// </summary>
    public static HexInfoService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new HexInfoService();
                }
            }
            return _instance;
        }
    }

    private const int NoBelong = 0xFF;

    private MapData? _mapData;
    private readonly Func<MapData?>? _mapSource;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public HexInfoService() { }

    /// <summary>
    /// 绑定到"当前地图"的取值函数：每次查询都取它返回的地图。
    /// 渲染场景打开 / 新建 / 切换地图后不必再同步，信息窗口不会继续显示上一张地图。
    /// </summary>
    public HexInfoService(Func<MapData?> mapSource)
    {
        _mapSource = mapSource;
    }

    public void SetMapData(MapData? mapData) => _mapData = mapData;

    private MapData? CurrentMap => _mapSource != null ? _mapSource() : _mapData;

    /// <summary>编辑模式 → 格子信息的显示视图（世界地图场景只有地形可看）。</summary>
    public static HexInfoDisplayMode DisplayModeFor(EditMode mode, string sceneType)
    {
        if (sceneType == "world") return HexInfoDisplayMode.Terrain;
        return mode switch
        {
            EditMode.TerrainPaint => HexInfoDisplayMode.Terrain,
            EditMode.ProvinceEdit => HexInfoDisplayMode.Province,
            EditMode.BuildingDeploy => HexInfoDisplayMode.Building,
            EditMode.ArmyDeploy => HexInfoDisplayMode.Army,
            EditMode.LegionEdit => HexInfoDisplayMode.Legion,
            EditMode.BelongEdit => HexInfoDisplayMode.Belong,
            EditMode.ReinforcementDeploy => HexInfoDisplayMode.Reinforcement,
            _ => HexInfoDisplayMode.Default
        };
    }

    public HexCellInfo? GetHexCellInfo(int col, int row, HexInfoDisplayMode mode = HexInfoDisplayMode.Default)
    {
        var map = CurrentMap;
        if (map == null || !InMap(map, col, row)) return null;

        int index = row * map.MapWidth + col;
        int belong = map.GetBelongValue(col, row);
        bool v3 = IsV3(map);

        return new HexCellInfo
        {
            Col = col,
            Row = row,
            MapWidth = map.MapWidth,
            DisplayMode = mode,
            CoordinateSection = BuildCoordinateSection(col, row, index),
            TerrainSection = BuildTerrainSection(map.GetTerrainAt(col, row)),
            ProvinceSection = BuildProvinceSection(map, col, row, index),
            BuildingSection = BuildBuildingSection(map.GetBuildingAt(col, row)),
            ArmySection = v3 ? BuildArmyV3Section(map, map.GetArmyV3At(col, row)) : BuildArmySection(map, map.GetArmyAt(col, row)),
            LegionSection = BuildLegionSection(belong == NoBelong ? null : map.ResolveLegionByBelong(belong)),
            TrapSection = BuildTrapSection(map, map.GetTrapAt(col, row)),
            BelongSection = BuildBelongSection(map, col, row, belong),
            ReinforcementSections = v3 ? BuildReinforcementV3Sections(map, index) : BuildReinforcementSections(map, index)
        };
    }

    /// <summary>
    /// 一行格子摘要（状态栏用）：坐标、序号、地形，再按有无追加省份 / 归属 / 建筑 / 单位 / 陷阱 / 援军。
    /// </summary>
    public string? DescribeHex(int col, int row)
    {
        var map = CurrentMap;
        if (map == null || !InMap(map, col, row)) return null;

        int index = row * map.MapWidth + col;
        var terrain = map.GetTerrainAt(col, row);
        var parts = new List<string>
        {
            Loc.F("Assist.HexInfo.Brief.Head", col, row, index, ConfigManager.Instance.GetTerrainTypeName(terrain.TileType1))
        };

        int provinceValue = map.GetProvinceAt(col, row).ProvinceValue;
        if (IsProvinceValue(provinceValue))
            parts.Add(provinceValue == index
                ? Loc.T("Assist.HexInfo.Brief.Capital", "省会")
                : Loc.F("Assist.HexInfo.Brief.Province", provinceValue));

        int belong = map.GetBelongValue(col, row);
        if (belong != NoBelong)
            parts.Add(Loc.F("Assist.HexInfo.Brief.Belong", belong));

        if (map.GetBuildingAt(col, row) is { } building)
            parts.Add(Loc.F("Assist.HexInfo.Brief.Building", building.GetBuildingTypeName()));

        if (IsV3(map))
        {
            if (map.GetArmyV3At(col, row) is { } a3)
                parts.Add(Loc.F("Assist.HexInfo.Brief.Army", a3.UnitType, a3.Level));
        }
        else if (map.GetArmyAt(col, row) is { } a1)
        {
            parts.Add(Loc.F("Assist.HexInfo.Brief.Army", a1.UnitType, a1.Level));
        }

        if (map.GetTrapAt(col, row).HasValue)
            parts.Add(Loc.T("Assist.HexInfo.Brief.Trap", "陷阱"));

        int reinforcements = IsV3(map)
            ? map.ReinforcementsV3.Count(r => r.Coordinate == index)
            : map.Reinforcements.Count(r => r.Coordinate == index);
        if (reinforcements > 0)
            parts.Add(Loc.F("Assist.HexInfo.Brief.Reinforcements", reinforcements));

        return string.Join(" · ", parts);
    }

    private static bool InMap(MapData map, int col, int row)
        => col >= 0 && col < map.MapWidth && row >= 0 && row < map.MapHeight;

    private static bool IsV3(MapData map) => (map.Header?.BtlVersion ?? 0) >= 3;

    /// <summary>省份值是否表示"有省份"：0 与 0xFFFF 都是无省份（与 Province.IsValid、省份渲染一致）。</summary>
    private static bool IsProvinceValue(int value) => value > 0 && value != 0xFFFF;

    // ------------------------------------------------------------------ 文本工具

    private static string L(string key, string fallback) => Loc.T(key, fallback);

    private static string None => Loc.T("Assist.HexInfo.Value.None", "无");

    private static string YesNo(bool value)
        => value ? Loc.T("Assist.HexInfo.Value.Yes", "是") : Loc.T("Assist.HexInfo.Value.No", "否");

    private static (string, string) Item(string key, string fallback, object? value) => (L(key, fallback), $"{value}");

    private static (string, string) Indexed(string key, int n, object? value) => (Loc.F(key, n), $"{value}");

    private static HexInfoSection Empty(string titleKey, string titleFallback, string labelKey, string labelFallback) => new()
    {
        Title = L(titleKey, titleFallback),
        Items = new List<(string, string)> { (L(labelKey, labelFallback), None) },
        IsEmpty = true
    };

    /// <summary>单位所在格子的归属：单位的归属就是它所在格子的归属值（单位自带的 LegionId 只是编辑器运行期字段，不在文件里）。</summary>
    private static string CellOwnerRef(MapData map, int cellIndex)
    {
        if (map.MapWidth <= 0 || cellIndex < 0 || cellIndex >= map.MapWidth * map.MapHeight) return None;
        return LegionRef(map, map.GetBelongValue(cellIndex % map.MapWidth, cellIndex / map.MapWidth));
    }

    /// <summary>陷阱里存的"军团"是行动 ID（ActionId，不是归属值）：原始值 + 对应的军团（归属值 / 国家 ID）。</summary>
    private static string ActionRef(MapData map, int actionId)
    {
        for (int i = 0; i < map.Legions.Count; i++)
        {
            if (map.Legions[i].ActionId == actionId)
                return Loc.F("Assist.HexInfo.Value.ActionRef", actionId, i, map.Legions[i].CountryId);
        }
        return Loc.F("Assist.HexInfo.Value.LegionUnresolved", actionId);
    }

    /// <summary>军团引用：原始值 + 解析出的军团（行动 ID / 国家 ID）。值为归属值，即军团在列表中的序号。</summary>
    private static string LegionRef(MapData map, int legionValue)
    {
        if (legionValue < 0 || legionValue == NoBelong) return $"{legionValue}";
        var legion = map.ResolveLegionByBelong(legionValue);
        return legion is { } l
            ? Loc.F("Assist.HexInfo.Value.LegionRef", legionValue, l.ActionId, l.CountryId)
            : Loc.F("Assist.HexInfo.Value.LegionUnresolved", legionValue);
    }

    private static byte[] Raw<T>(T value, Action<T, byte[]> write, int size)
    {
        var bytes = new byte[size];
        write(value, bytes);
        return bytes;
    }

    // ------------------------------------------------------------------ 分区

    private static HexInfoSection BuildCoordinateSection(int col, int row, int index) => new()
    {
        Title = L("Assist.HexInfo.Section.Coordinate", "坐标"),
        Items = new List<(string, string)>
        {
            (L("Assist.HexInfo.Coordinate", "坐标"), $"({col}, {row}) 0x{index:X4}")
        }
    };

    private static HexInfoSection BuildTerrainSection(Terrain t)
    {
        var cfg = ConfigManager.Instance;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Terrain", "地形信息"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Field.Layer1", "第一层地形"), $"{cfg.GetTerrainTypeName(t.TileType1)} ({t.TileType1})"),
                Indexed("Assist.HexInfo.Field.Decoration", 1, t.DecorationType1),
                Indexed("Assist.HexInfo.Field.OffsetX", 1, t.TextureOffsetX1),
                Indexed("Assist.HexInfo.Field.OffsetY", 1, t.TextureOffsetY1),
                (L("Assist.HexInfo.Field.Layer2", "第二层地形"), $"{cfg.GetTerrainTypeName(t.TileType2)} ({t.TileType2})"),
                Indexed("Assist.HexInfo.Field.Decoration", 2, t.DecorationType2),
                Indexed("Assist.HexInfo.Field.OffsetX", 2, t.TextureOffsetX2),
                Indexed("Assist.HexInfo.Field.OffsetY", 2, t.TextureOffsetY2),
                (L("Assist.HexInfo.Field.Layer3", "第三层地形"), $"{cfg.GetTerrainTypeName(t.TileType3)} ({t.TileType3})"),
                Indexed("Assist.HexInfo.Field.Decoration", 3, t.DecorationType3),
                Indexed("Assist.HexInfo.Field.OffsetX", 3, t.TextureOffsetX3),
                Indexed("Assist.HexInfo.Field.OffsetY", 3, t.TextureOffsetY3),
                Item("Assist.HexInfo.Field.River", "河流值", t.RiverValue)
            }
        };
    }

    /// <summary>省份：省份值（十进制 + 十六进制）、省会坐标、本格是否省会、省会归属（决定省份颜色）。</summary>
    private static HexInfoSection BuildProvinceSection(MapData map, int col, int row, int index)
    {
        int value = map.GetProvinceAt(col, row).ProvinceValue;
        if (!IsProvinceValue(value))
            return Empty("Assist.HexInfo.Section.Province", "省份信息", "Assist.HexInfo.Field.ProvinceValue", "省份值");

        int total = map.MapWidth * map.MapHeight;
        string capital = value < total ? $"({value % map.MapWidth}, {value / map.MapWidth})" : None;
        int capitalBelong = map.GetProvinceCapitalBelong(col, row);

        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Province", "省份信息"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Field.ProvinceValue", "省份值"), $"{value} (0x{value:X4})"),
                (L("Assist.HexInfo.Field.ProvinceCapital", "省会"), capital),
                (L("Assist.HexInfo.Field.IsCapital", "是否省会"), YesNo(value == index)),
                (L("Assist.HexInfo.Field.CapitalBelong", "省会归属"),
                    capitalBelong >= 0 && capitalBelong != NoBelong ? $"{capitalBelong} (0x{capitalBelong:X2})" : None)
            }
        };
    }

    private static HexInfoSection BuildBelongSection(MapData map, int col, int row, int belong)
    {
        string value = !map.IsInBelongRange(col, row)
            ? Loc.T("Assist.HexInfo.Value.OutOfRange", "数据范围外")
            : belong == NoBelong ? None : $"{belong} (0x{belong:X2})";

        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Belong", "归属信息"),
            Items = new List<(string, string)> { (L("Assist.HexInfo.Field.Belong", "归属"), value) }
        };
    }

    private static HexInfoSection BuildBuildingSection(Building? building)
    {
        if (!building.HasValue)
            return Empty("Assist.HexInfo.Section.Building", "建筑信息", "Assist.HexInfo.Field.Building", "建筑");

        var b = building.Value;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Building", "建筑信息"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{b.Coordinate:X4}"),
                (L("Assist.HexInfo.Field.Name", "名称"), BuildingName(b.Name)),
                (L("Assist.HexInfo.Field.BuildingType", "建筑类型"), $"{b.GetBuildingTypeName()} ({b.BuildingType})"),
                Item("Assist.HexInfo.Field.Appearance", "外观", b.Appearance),
                Item("Assist.HexInfo.Field.Landmark", "地标建筑", b.LandmarkBuilding),
                Item("Assist.HexInfo.Field.Decorative", "装饰建筑", b.DecorativeBuilding),
                Item("Assist.HexInfo.Field.SkillUnlock", "技能解锁", b.SkillUnlock),
                Item("Assist.HexInfo.Field.RewardCount", "奖励数量", b.RewardCount),
                Item("Assist.HexInfo.Field.Hatred", "仇恨值", b.HatredValue),
                (L("Assist.HexInfo.Field.KeyPoint", "关键据点"), HexCellInfo.GetKeyPointText(b.KeyPoint)),
                (L("Assist.HexInfo.Field.OccupationEvent", "占领事件"), b.OccupationEvent == 0 ? None : $"{b.OccupationEvent}"),
                Item("Assist.HexInfo.Field.FireIgnition", "火焰燃起", b.FireIgnition),
                Item("Assist.HexInfo.Field.FireDuration", "火焰持续", b.FireDuration),
                Item("Assist.HexInfo.Field.AirDefenseWeapon", "防空武器", b.AirDefenseWeapon),
                Item("Assist.HexInfo.Field.AirDefenseRadar", "防空雷达", b.AirDefenseRadar),
                Item("Assist.HexInfo.Field.FactoryLevel", "工厂等级", b.FactoryLevel),
                Item("Assist.HexInfo.Field.ResearchLevel", "科研等级", b.ResearchLevel),
                Item("Assist.HexInfo.Field.MedicalLevel", "医疗等级", b.MedicalLevel),
                Item("Assist.HexInfo.Field.AviationLevel", "航空等级", b.AviationLevel),
                Item("Assist.HexInfo.Field.MissileLevel", "导弹等级", b.MissileLevel),
                Item("Assist.HexInfo.Field.NuclearLevel", "核弹等级", b.NuclearLevel)
            },
            RawData = Raw(b, (v, buf) => v.ToBytes(buf, 0), 32)
        };
    }

    /// <summary>建筑名：字符串表 battle_cityname_{id} 的城市名 + 名称 ID；0 与 0xFFFF 表示无名称。</summary>
    private static string BuildingName(ushort nameId)
    {
        if (nameId == 0 || nameId == 0xFFFF) return None;
        string name = ConfigManager.Instance.GetStringTableValue($"battle_cityname_{nameId}");
        return string.IsNullOrEmpty(name) ? $"#{nameId}" : $"{name} (#{nameId})";
    }

    private static HexInfoSection BuildArmySection(MapData map, Army? army)
    {
        if (!army.HasValue)
            return Empty("Assist.HexInfo.Section.Army", "军队信息", "Assist.HexInfo.Field.Army", "单位");

        var a = army.Value;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Army", "军队信息"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{(ushort)a.Coordinate:X4}"),
                Item("Assist.HexInfo.Field.UnitType", "兵种", a.UnitType),
                Item("Assist.HexInfo.Field.Level", "等级", a.Level),
                Item("Assist.HexInfo.Field.Organization", "编制", a.Organization),
                Item("Assist.HexInfo.Field.Direction", "方向", a.Direction),
                Item("Assist.HexInfo.Field.Mobility", "移动力", a.Mobility),
                Item("Assist.HexInfo.Field.BuiltRound", "建造回合", a.BuiltRound),
                Item("Assist.HexInfo.Field.Experience", "经验", a.Experience),
                Item("Assist.HexInfo.Field.HealthBonus", "血量加成", a.HealthBonus),
                Item("Assist.HexInfo.Field.CurrentHealth", "当前血量", a.CurrentHealth),
                Item("Assist.HexInfo.Field.MaxHealth", "血量上限", a.MaxHealth),
                Item("Assist.HexInfo.Field.General", "将领", a.General),
                Item("Assist.HexInfo.Field.Rank", "军衔", a.Rank),
                Item("Assist.HexInfo.Field.Nobility", "爵位", a.Nobility),
                Indexed("Assist.HexInfo.Field.Badge", 1, a.Badge1),
                Indexed("Assist.HexInfo.Field.Badge", 2, a.Badge2),
                Indexed("Assist.HexInfo.Field.Badge", 3, a.Badge3),
                Indexed("Assist.HexInfo.Field.Skill", 1, a.SkillLevel1),
                Indexed("Assist.HexInfo.Field.Skill", 2, a.SkillLevel2),
                Indexed("Assist.HexInfo.Field.Skill", 3, a.SkillLevel3),
                Indexed("Assist.HexInfo.Field.Skill", 4, a.SkillLevel4),
                Indexed("Assist.HexInfo.Field.Skill", 5, a.SkillLevel5),
                (L("Assist.HexInfo.Field.KeyPoint", "关键据点"), HexCellInfo.GetKeyPointText(a.KeyPoint)),
                Item("Assist.HexInfo.Field.Policy", "方针", a.Policy),
                Item("Assist.HexInfo.Field.Plan", "方案", a.Plan),
                Item("Assist.HexInfo.Field.ChangeRound", "改变回合", a.ChangeRound),
                Item("Assist.HexInfo.Field.Morale", "士气", a.Morale),
                Item("Assist.HexInfo.Field.Duration", "持续回合", a.Duration),
                Item("Assist.HexInfo.Field.Dialogue", "关联对话", a.Dialogue),
                Item("Assist.HexInfo.Field.CanAttack", "能否攻击", a.CanAttack),
                (L("Assist.HexInfo.Field.Legion", "军团"), CellOwnerRef(map, a.CoordinateIndex))
            },
            RawData = Raw(a, (v, buf) => v.ToBytes(buf, 0), 48)
        };
    }

    /// <summary>v3 单位（64 字节）：比 v1 多运输船 / 仇恨 / 进攻目标 / 勋章勋带，爵位→HP等级、关联对话→死亡触发事件、能否攻击→等级标志。</summary>
    private static HexInfoSection BuildArmyV3Section(MapData map, Army_3? army)
    {
        if (!army.HasValue)
            return Empty("Assist.HexInfo.Section.Army", "军队信息", "Assist.HexInfo.Field.Army", "单位");

        var a = army.Value;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.ArmyV3", "军队信息 (v3)"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{(ushort)a.Coordinate:X4}"),
                Item("Assist.HexInfo.Field.UnitType", "兵种", a.UnitType),
                Item("Assist.HexInfo.Field.Level", "等级", a.Level),
                Item("Assist.HexInfo.Field.Organization", "编制", a.Organization),
                Item("Assist.HexInfo.Field.Direction", "方向", a.Direction),
                Item("Assist.HexInfo.Field.Mobility", "移动力", a.Mobility),
                Item("Assist.HexInfo.Field.BuiltRound", "建造回合", a.BuiltRound),
                Item("Assist.HexInfo.Field.Experience", "经验", a.Experience),
                Item("Assist.HexInfo.Field.HealthBonus", "血量加成", a.HealthBonus),
                Item("Assist.HexInfo.Field.CurrentHealth", "当前血量", a.CurrentHealth),
                Item("Assist.HexInfo.Field.MaxHealth", "血量上限", a.MaxHealth),
                Item("Assist.HexInfo.Field.General", "将领", a.General),
                Item("Assist.HexInfo.Field.Rank", "军衔", a.Rank),
                Item("Assist.HexInfo.Field.HpLevel", "HP等级", a.HpLevel),
                Indexed("Assist.HexInfo.Field.Skill", 1, a.SkillLevel1),
                Indexed("Assist.HexInfo.Field.Skill", 2, a.SkillLevel2),
                Indexed("Assist.HexInfo.Field.Skill", 3, a.SkillLevel3),
                Indexed("Assist.HexInfo.Field.Skill", 4, a.SkillLevel4),
                Indexed("Assist.HexInfo.Field.Skill", 5, a.SkillLevel5),
                (L("Assist.HexInfo.Field.KeyPoint", "关键据点"), HexCellInfo.GetKeyPointText(a.KeyPoint)),
                Item("Assist.HexInfo.Field.Policy", "方针", a.Policy),
                Item("Assist.HexInfo.Field.TransportShip", "运输船", a.TransportShip),
                Item("Assist.HexInfo.Field.Hatred", "仇恨值", a.Hatred),
                Item("Assist.HexInfo.Field.AttackTarget", "进攻目标", a.AttackTarget),
                Item("Assist.HexInfo.Field.Plan", "方案", a.Plan),
                Item("Assist.HexInfo.Field.ChangeRound", "改变回合", a.ChangeRound),
                Item("Assist.HexInfo.Field.Morale", "士气", a.Morale),
                Item("Assist.HexInfo.Field.Duration", "持续回合", a.Duration),
                Item("Assist.HexInfo.Field.DeathDialogue", "死亡触发事件", a.DeathDialogue),
                Item("Assist.HexInfo.Field.LevelMark", "等级标志显示", a.LevelMarkDisplay),
                Item("Assist.HexInfo.Field.AntiDefense", "坚守触发距离", a.AntiDefenseMode),
                Indexed("Assist.HexInfo.Field.Medal", 1, a.Medal1),
                Indexed("Assist.HexInfo.Field.Medal", 2, a.Medal2),
                Indexed("Assist.HexInfo.Field.Medal", 3, a.Medal3),
                Indexed("Assist.HexInfo.Field.Ribbon", 1, a.Ribbon1),
                Indexed("Assist.HexInfo.Field.Ribbon", 2, a.Ribbon2),
                Indexed("Assist.HexInfo.Field.Ribbon", 3, a.Ribbon3),
                (L("Assist.HexInfo.Field.Legion", "军团"), CellOwnerRef(map, a.CoordinateIndex))
            },
            RawData = Raw(a, (v, buf) => v.ToBytes(buf, 0), 64)
        };
    }

    /// <summary>军团：取该格归属值对应的军团（归属值 = 军团在列表中的序号）。</summary>
    private static HexInfoSection BuildLegionSection(Legion? legion)
    {
        if (!legion.HasValue)
            return Empty("Assist.HexInfo.Section.Legion", "军团信息", "Assist.HexInfo.Field.Legion", "军团");

        var l = legion.Value;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Legion", "军团信息"),
            Items = new List<(string, string)>
            {
                Item("Assist.HexInfo.Field.ActionId", "行动ID", l.ActionId),
                Item("Assist.HexInfo.Field.CountryId", "国家ID", l.CountryId),
                Item("Assist.HexInfo.Field.InitialEconomy", "初始经济", l.InitialEconomy),
                Item("Assist.HexInfo.Field.InitialIndustry", "初始工业", l.InitialIndustry),
                Item("Assist.HexInfo.Field.InitialTech", "初始科技", l.InitialTech),
                (L("Assist.HexInfo.Field.PlayerControlled", "玩家控制"), $"{YesNo(l.IsPlayer)} ({l.IsPlayerControlled})"),
                Item("Assist.HexInfo.Field.Camp", "阵营", l.Camp),
                Item("Assist.HexInfo.Field.DefeatCondition", "战败条件", l.DefeatCondition),
                Item("Assist.HexInfo.Field.HpRate", "国家血率", l.CountryHpRate),
                Item("Assist.HexInfo.Field.TaxRate", "国家税率", l.CountryTaxRate),
                (L("Assist.HexInfo.Field.Color", "国家颜色"), $"0x{l.ColorR:X2}{l.ColorG:X2}{l.ColorB:X2}"),
                Item("Assist.HexInfo.Field.AtomicBomb", "原子弹", l.AtomicBombCount),
                Item("Assist.HexInfo.Field.HydrogenBomb", "氢弹", l.HydrogenBombCount),
                Item("Assist.HexInfo.Field.NeutronBomb", "三相弹", l.NeutronBombCount),
                Item("Assist.HexInfo.Field.AntimatterBomb", "反物质弹", l.AntimatterBombCount)
            },
            RawData = Raw(l, (v, buf) => v.ToBytes(buf, 0), 300)
        };
    }

    private static HexInfoSection BuildTrapSection(MapData map, Trap? trap)
    {
        if (!trap.HasValue)
            return Empty("Assist.HexInfo.Section.Trap", "陷阱信息", "Assist.HexInfo.Field.Trap", "陷阱");

        var t = trap.Value;
        return new HexInfoSection
        {
            Title = L("Assist.HexInfo.Section.Trap", "陷阱信息"),
            Items = new List<(string, string)>
            {
                (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{(ushort)t.Coordinate:X4}"),
                (L("Assist.HexInfo.Field.Legion", "军团"), ActionRef(map, t.LegionId)),
                Item("Assist.HexInfo.Field.Organization", "编制", t.Organization),
                Item("Assist.HexInfo.Field.Health", "血量", t.Health)
            },
            RawData = Raw(t, (v, buf) => v.ToBytes(buf, 0), Unsafe.SizeOf<Trap>())
        };
    }

    private static IReadOnlyList<HexInfoSection> BuildReinforcementSections(MapData map, int index)
    {
        var list = map.Reinforcements.Where(r => r.Coordinate == index).ToList();
        if (list.Count == 0)
            return new[] { Empty("Assist.HexInfo.Section.Reinforcement", "援军信息", "Assist.HexInfo.Field.Reinforcement", "援军") };

        var sections = new List<HexInfoSection>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            sections.Add(new HexInfoSection
            {
                Title = ReinforcementTitle(i, list.Count, v3: false),
                Items = new List<(string, string)>
                {
                    (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{r.Coordinate:X4}"),
                    Item("Assist.HexInfo.Field.UnitType", "兵种", r.UnitType),
                    Item("Assist.HexInfo.Field.Level", "等级", r.Level),
                    Item("Assist.HexInfo.Field.Organization", "编制", r.Organization),
                    Item("Assist.HexInfo.Field.Direction", "方向", r.Direction),
                    Item("Assist.HexInfo.Field.General", "将领", r.General),
                    Item("Assist.HexInfo.Field.Rank", "军衔", r.Rank),
                    Item("Assist.HexInfo.Field.Quality", "品质", r.Quality),
                    Indexed("Assist.HexInfo.Field.Skill", 1, r.Skill1Level),
                    Indexed("Assist.HexInfo.Field.Skill", 2, r.Skill2Level),
                    Indexed("Assist.HexInfo.Field.Skill", 3, r.Skill3Level),
                    Indexed("Assist.HexInfo.Field.Skill", 4, r.Skill4Level),
                    Indexed("Assist.HexInfo.Field.Skill", 5, r.Skill5Level),
                    Indexed("Assist.HexInfo.Field.Badge", 1, r.Badge1),
                    Indexed("Assist.HexInfo.Field.Badge", 2, r.Badge2),
                    Indexed("Assist.HexInfo.Field.Badge", 3, r.Badge3),
                    Item("Assist.HexInfo.Field.OwnerCountry", "所属国家", r.OwnerCountry),
                    Item("Assist.HexInfo.Field.SpawnRound", "爆兵回合", r.SpawnRound)
                },
                RawData = Raw(r, (v, buf) => v.ToBytes(buf, 0), Unsafe.SizeOf<Reinforcement>())
            });
        }
        return sections;
    }

    /// <summary>v3 援军（104 字节）：行为模式、HP等级、勋章 / 勋带、所属军团。</summary>
    private static IReadOnlyList<HexInfoSection> BuildReinforcementV3Sections(MapData map, int index)
    {
        var list = map.ReinforcementsV3.Where(r => r.Coordinate == index).ToList();
        if (list.Count == 0)
            return new[] { Empty("Assist.HexInfo.Section.Reinforcement", "援军信息", "Assist.HexInfo.Field.Reinforcement", "援军") };

        var sections = new List<HexInfoSection>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            sections.Add(new HexInfoSection
            {
                Title = ReinforcementTitle(i, list.Count, v3: true),
                Items = new List<(string, string)>
                {
                    (L("Assist.HexInfo.Coordinate", "坐标"), $"0x{r.Coordinate:X4}"),
                    Item("Assist.HexInfo.Field.UnitType", "兵种", r.UnitType),
                    Item("Assist.HexInfo.Field.Level", "等级", r.Level),
                    Item("Assist.HexInfo.Field.Organization", "编制", r.Organization),
                    Item("Assist.HexInfo.Field.BehaviorMode", "行为模式", r.BehaviorMode),
                    Item("Assist.HexInfo.Field.Direction", "方向", r.Direction),
                    Item("Assist.HexInfo.Field.General", "将领", r.General),
                    Item("Assist.HexInfo.Field.Rank", "军衔", r.Rank),
                    Item("Assist.HexInfo.Field.HpLevel", "HP等级", r.HpLevel),
                    Indexed("Assist.HexInfo.Field.Skill", 1, r.Skill1Level),
                    Indexed("Assist.HexInfo.Field.Skill", 2, r.Skill2Level),
                    Indexed("Assist.HexInfo.Field.Skill", 3, r.Skill3Level),
                    Indexed("Assist.HexInfo.Field.Skill", 4, r.Skill4Level),
                    Indexed("Assist.HexInfo.Field.Skill", 5, r.Skill5Level),
                    Item("Assist.HexInfo.Field.OwnerLegion", "所属军团", r.OwnerLegion),
                    Item("Assist.HexInfo.Field.SpawnRound", "爆兵回合", r.SpawnRound),
                    Indexed("Assist.HexInfo.Field.Medal", 1, r.Medal1),
                    Indexed("Assist.HexInfo.Field.Medal", 2, r.Medal2),
                    Indexed("Assist.HexInfo.Field.Medal", 3, r.Medal3),
                    Indexed("Assist.HexInfo.Field.Ribbon", 1, r.Ribbon1),
                    Indexed("Assist.HexInfo.Field.Ribbon", 2, r.Ribbon2),
                    Indexed("Assist.HexInfo.Field.Ribbon", 3, r.Ribbon3)
                },
                RawData = Raw(r, (v, buf) => v.ToBytes(buf, 0), Unsafe.SizeOf<Reinforcement_3>())
            });
        }
        return sections;
    }

    private static string ReinforcementTitle(int i, int count, bool v3)
    {
        string title = v3 ? L("Assist.HexInfo.Section.ReinforcementV3", "援军信息 (v3)") : L("Assist.HexInfo.Section.Reinforcement", "援军信息");
        return count > 1 ? Loc.F("Assist.HexInfo.Section.Numbered", title, i + 1, count) : title;
    }
}
