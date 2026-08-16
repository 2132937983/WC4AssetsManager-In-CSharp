using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Services;

public sealed class HexInfoService : IHexInfoProvider
{
    private static readonly object _lock = new();
    private static HexInfoService? _instance;

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

    private MapData? _mapData;

    private HexInfoService() { }

    public void SetMapData(MapData? mapData) => _mapData = mapData;

    public HexCellInfo? GetHexCellInfo(int col, int row, HexInfoDisplayMode mode = HexInfoDisplayMode.Default)
    {
        if (_mapData == null) return null;
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return null;

        var terrain = _mapData.GetTerrainAt(col, row);
        var province = _mapData.GetProvinceAt(col, row);
        var building = _mapData.GetBuildingAt(col, row);
        var army = _mapData.GetArmyAt(col, row);
        var trap = _mapData.GetTrapAt(col, row);
        var reinforcement = _mapData.GetReinforcementAt(col, row);

        Legion? legion = null;
        if (army.HasValue)
            legion = _mapData.GetLegionByCountryId(army.Value.LegionId);

        return new HexCellInfo
        {
            Col = col,
            Row = row,
            MapWidth = _mapData.MapWidth,
            DisplayMode = mode,
            CoordinateSection = BuildCoordinateSection(col, row, _mapData.MapWidth),
            TerrainSection = BuildTerrainSection(terrain),
            ProvinceSection = BuildProvinceSection(province),
            BuildingSection = BuildBuildingSection(building),
            ArmySection = BuildArmySection(army),
            LegionSection = BuildLegionSection(legion),
            ReinforcementSection = BuildReinforcementSection(reinforcement),
            TrapSection = BuildTrapSection(trap),
            BelongSection = BuildBelongSection(province)
        };
    }

    private static HexInfoSection BuildCoordinateSection(int col, int row, int mapWidth)
    {
        int coordIndex = row * mapWidth + col;
        return new HexInfoSection
        {
            Title = "坐标",
            Items = new List<(string, string)>
            {
                ("坐标", $"({col}, {row}) 0x{coordIndex:X4}")
            }
        };
    }

    private static HexInfoSection BuildTerrainSection(Terrain? terrain)
    {
        if (!terrain.HasValue)
        {
            return new HexInfoSection
            {
                Title = "地形信息",
                Items = new List<(string, string)>
                {
                    ("第一层地形", "-"), ("装饰 1", "-"), ("贴图偏移 X1", "-"), ("贴图偏移 Y1", "-"),
                    ("第二层地形", "-"), ("装饰 2", "-"), ("贴图偏移 X2", "-"), ("贴图偏移 Y2", "-"),
                    ("第三层地形", "-"), ("装饰 3", "-"), ("贴图偏移 X3", "-"), ("贴图偏移 Y3", "-"),
                    ("河流值", "-")
                }
            };
        }

        var t = terrain.Value;
        var cfg = ConfigManager.Instance;
        return new HexInfoSection
        {
            Title = "地形信息",
            Items = new List<(string, string)>
            {
                ("第一层地形", $"{cfg.GetTerrainTypeName(t.TileType1)} ({t.TileType1})"),
                ("装饰 1", $"{t.DecorationType1}"),
                ("贴图偏移 X1", $"{t.TextureOffsetX1}"),
                ("贴图偏移 Y1", $"{t.TextureOffsetY1}"),
                ("第二层地形", $"{cfg.GetTerrainTypeName(t.TileType2)} ({t.TileType2})"),
                ("装饰 2", $"{t.DecorationType2}"),
                ("贴图偏移 X2", $"{t.TextureOffsetX2}"),
                ("贴图偏移 Y2", $"{t.TextureOffsetY2}"),
                ("第三层地形", $"{cfg.GetTerrainTypeName(t.TileType3)} ({t.TileType3})"),
                ("装饰 3", $"{t.DecorationType3}"),
                ("贴图偏移 X3", $"{t.TextureOffsetX3}"),
                ("贴图偏移 Y3", $"{t.TextureOffsetY3}"),
                ("河流值", $"{t.RiverValue}")
            }
        };
    }

    private static HexInfoSection BuildProvinceSection(Province province)
    {
        return new HexInfoSection
        {
            Title = "省份信息",
            Items = new List<(string, string)>
            {
                ("省份", province.CountryId > 0 ? $"{province.CountryId}" : "-"),
                ("归属", province.CountryId > 0 ? $"0x{province.CountryId:X2}" : "-")
            }
        };
    }

    private static HexInfoSection BuildBuildingSection(Building? building)
    {
        if (!building.HasValue)
        {
            return new HexInfoSection
            {
                Title = "建筑信息",
                Items = new List<(string, string)>
                {
                    ("坐标", "-"), ("名称", "-"), ("建筑类型", "-"), ("外观", "-"),
                    ("地标建筑", "-"), ("装饰建筑", "-"), ("技能解锁", "-"), ("奖励数量", "-"),
                    ("仇恨值", "-"), ("关键据点", "-"), ("占领事件", "-"),
                    ("火焰燃起", "-"), ("火焰持续", "-"),
                    ("防空武器", "-"), ("防空雷达", "-"),
                    ("工厂等级", "-"), ("科研等级", "-"), ("医疗等级", "-"),
                    ("航空等级", "-"), ("导弹等级", "-"), ("核弹等级", "-")
                }
            };
        }

        var b = building.Value;
        var bytes = new byte[32];
        b.ToBytes(bytes, 0);

        return new HexInfoSection
        {
            Title = "建筑信息",
            Items = new List<(string, string)>
            {
                ("坐标", $"0x{b.Coordinate:X4}"),
                ("名称", $"{b.Name}"),
                ("建筑类型", $"{b.BuildingType}"),
                ("外观", $"{b.Appearance}"),
                ("地标建筑", $"{b.LandmarkBuilding}"),
                ("装饰建筑", $"{b.DecorativeBuilding}"),
                ("技能解锁", $"{b.SkillUnlock}"),
                ("奖励数量", $"{b.RewardCount}"),
                ("仇恨值", $"{b.HatredValue}"),
                ("关键据点", HexCellInfo.GetKeyPointText(b.KeyPoint)),
                ("占领事件", $"{b.OccupationEvent}"),
                ("火焰燃起", $"{b.FireIgnition}"),
                ("火焰持续", $"{b.FireDuration}"),
                ("防空武器", $"{b.AirDefenseWeapon}"),
                ("防空雷达", $"{b.AirDefenseRadar}"),
                ("工厂等级", $"{b.FactoryLevel}"),
                ("科研等级", $"{b.ResearchLevel}"),
                ("医疗等级", $"{b.MedicalLevel}"),
                ("航空等级", $"{b.AviationLevel}"),
                ("导弹等级", $"{b.MissileLevel}"),
                ("核弹等级", $"{b.NuclearLevel}")
            },
            RawData = bytes
        };
    }

    private static HexInfoSection BuildArmySection(Army? army)
    {
        if (!army.HasValue)
        {
            return new HexInfoSection
            {
                Title = "军队信息",
                Items = new List<(string, string)>
                {
                    ("坐标", "-"), ("兵种", "-"), ("等级", "-"), ("编制", "-"),
                    ("方向", "-"), ("移动力", "-"), ("建造回合", "-"), ("经验", "-"),
                    ("血量加成", "-"), ("当前血量", "-"), ("血量上限", "-"),
                    ("将领", "-"), ("军衔", "-"), ("爵位", "-"),
                    ("胸章1", "-"), ("胸章2", "-"), ("胸章3", "-"),
                    ("技能1", "-"), ("技能2", "-"), ("技能3", "-"), ("技能4", "-"), ("技能5", "-"),
                    ("关键据点", "-"), ("方针", "-"), ("方案", "-"), ("改变回合", "-"),
                    ("士气", "-"), ("持续回合", "-"), ("关联对话", "-"), ("能否攻击", "-")
                }
            };
        }

        var a = army.Value;
        var bytes = new byte[48];
        a.ToBytes(bytes, 0);

        return new HexInfoSection
        {
            Title = "军队信息",
            Items = new List<(string, string)>
            {
                ("坐标", $"0x{a.Coordinate:X4}"),
                ("兵种", $"{a.UnitType}"),
                ("等级", $"{a.Level}"),
                ("编制", $"{a.Organization}"),
                ("方向", $"{a.Direction}"),
                ("移动力", $"{a.Mobility}"),
                ("建造回合", $"{a.BuiltRound}"),
                ("经验", $"{a.Experience}"),
                ("血量加成", $"{a.HealthBonus}"),
                ("当前血量", $"{a.CurrentHealth}"),
                ("血量上限", $"{a.MaxHealth}"),
                ("将领", $"{a.General}"),
                ("军衔", $"{a.Rank}"),
                ("爵位", $"{a.Nobility}"),
                ("胸章1", $"{a.Badge1}"),
                ("胸章2", $"{a.Badge2}"),
                ("胸章3", $"{a.Badge3}"),
                ("技能1", $"{a.SkillLevel1}"),
                ("技能2", $"{a.SkillLevel2}"),
                ("技能3", $"{a.SkillLevel3}"),
                ("技能4", $"{a.SkillLevel4}"),
                ("技能5", $"{a.SkillLevel5}"),
                ("关键据点", HexCellInfo.GetKeyPointText(a.KeyPoint)),
                ("方针", $"{a.Policy}"),
                ("方案", $"{a.Plan}"),
                ("改变回合", $"{a.ChangeRound}"),
                ("士气", $"{a.Morale}"),
                ("持续回合", $"{a.Duration}"),
                ("关联对话", $"{a.Dialogue}"),
                ("能否攻击", $"{a.CanAttack}")
            },
            RawData = bytes
        };
    }

    private static HexInfoSection BuildLegionSection(Legion? legion)
    {
        if (!legion.HasValue)
        {
            return new HexInfoSection
            {
                Title = "军团信息",
                Items = new List<(string, string)>
                {
                    ("行动ID", "-"), ("国家ID", "-"),
                    ("初始经济", "-"), ("初始工业", "-"), ("初始科技", "-"),
                    ("玩家控制", "-"), ("阵营", "-"), ("战败条件", "-"),
                    ("国家血率", "-"), ("国家税率", "-"), ("国家颜色", "-"),
                    ("原子弹", "-"), ("氢弹", "-"), ("三相弹", "-"), ("反物质弹", "-")
                }
            };
        }

        var l = legion.Value;
        var bytes = new byte[300];
        l.ToBytes(bytes, 0);

        return new HexInfoSection
        {
            Title = "军团信息",
            Items = new List<(string, string)>
            {
                ("行动ID", $"{l.ActionId}"),
                ("国家ID", $"{l.CountryId}"),
                ("初始经济", $"{l.InitialEconomy}"),
                ("初始工业", $"{l.InitialIndustry}"),
                ("初始科技", $"{l.InitialTech}"),
                ("玩家控制", l.IsPlayerControlled == 1 ? "是" : "否"),
                ("阵营", $"{l.Camp}"),
                ("战败条件", $"{l.DefeatCondition}"),
                ("国家血率", $"{l.CountryHpRate}"),
                ("国家税率", $"{l.CountryTaxRate}"),
                ("国家颜色", $"0x{l.ColorR:X2}{l.ColorG:X2}{l.ColorB:X2}"),
                ("原子弹", $"{l.AtomicBombCount}"),
                ("氢弹", $"{l.HydrogenBombCount}"),
                ("三相弹", $"{l.NeutronBombCount}"),
                ("反物质弹", $"{l.AntimatterBombCount}")
            },
            RawData = bytes
        };
    }

    private static HexInfoSection BuildReinforcementSection(Reinforcement? reinforcement)
    {
        if (!reinforcement.HasValue)
        {
            return new HexInfoSection
            {
                Title = "援军信息",
                Items = new List<(string, string)>
                {
                    ("坐标", "-"), ("兵种", "-"), ("等级", "-"), ("编制", "-"),
                    ("方向", "-"), ("将领", "-"), ("军衔", "-"), ("品质", "-"),
                    ("技能1", "-"), ("技能2", "-"), ("技能3", "-"), ("技能4", "-"), ("技能5", "-"),
                    ("胸章1", "-"), ("胸章2", "-"), ("胸章3", "-"),
                    ("所属国家", "-"), ("爆兵回合", "-")
                }
            };
        }

        var r = reinforcement.Value;
        var bytes = new byte[80];
        r.ToBytes(bytes, 0);

        return new HexInfoSection
        {
            Title = "援军信息",
            Items = new List<(string, string)>
            {
                ("坐标", $"0x{r.Coordinate:X4}"),
                ("兵种", $"{r.UnitType}"),
                ("等级", $"{r.Level}"),
                ("编制", $"{r.Organization}"),
                ("方向", $"{r.Direction}"),
                ("将领", $"{r.General}"),
                ("军衔", $"{r.Rank}"),
                ("品质", $"{r.Quality}"),
                ("技能1", $"{r.Skill1Level}"),
                ("技能2", $"{r.Skill2Level}"),
                ("技能3", $"{r.Skill3Level}"),
                ("技能4", $"{r.Skill4Level}"),
                ("技能5", $"{r.Skill5Level}"),
                ("胸章1", $"{r.Badge1}"),
                ("胸章2", $"{r.Badge2}"),
                ("胸章3", $"{r.Badge3}"),
                ("所属国家", $"{r.OwnerCountry}"),
                ("爆兵回合", $"{r.SpawnRound}")
            },
            RawData = bytes
        };
    }

    private static HexInfoSection BuildTrapSection(Trap? trap)
    {
        if (!trap.HasValue)
        {
            return new HexInfoSection
            {
                Title = "陷阱信息",
                Items = new List<(string, string)> { ("陷阱", "-") }
            };
        }

        var t = trap.Value;
        return new HexInfoSection
        {
            Title = "陷阱信息",
            Items = new List<(string, string)>
            {
                ("坐标", $"0x{t.Coordinate:X4}"),
                ("军团", $"{t.LegionId}"),
                ("编制", $"{t.Organization}"),
                ("血量", $"{t.Health}")
            }
        };
    }

    private static HexInfoSection BuildBelongSection(Province province)
    {
        return new HexInfoSection
        {
            Title = "归属信息",
            Items = new List<(string, string)>
            {
                ("归属", province.CountryId > 0 ? $"0x{province.CountryId:X2}" : "-")
            }
        };
    }
}