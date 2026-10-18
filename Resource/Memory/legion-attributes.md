# legion-attributes

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 军团（300 字节）每个字段的含义、默认值、取值范围，以及"科技等级族"的对应关系。军团即势力：归属值就是军团列表索引。

---

## 关键前提

- **军团的"索引"就是归属值**：`Belong` 存的是军团在 `Legions` 列表里的位置。要"给某个势力铺归属"，先 `legion list` 把国家名 ↔ 索引对上。
- **`CountryId` / `ActionId` 是定位键**：`FindLegionIndex` 按 **CountryId** 匹配，所以改 CountryId 必须走按索引的专用接口（`map.SetLegionCountryIdAt` / `legion set-country-at`，它还会校验唯一性）；通用字段白名单刻意不含这两个。
- 读写入口：CLI `legion show-at <索引>` / `legion set-field <索引> <字段> <值>`；Lua `map.GetLegionAt(i)` / `map.SetLegionField(i,字段,值)`；批量 `set_object_field_batch`。

## 基础字段（中文名与军团设置窗口一致）

| 字段 | 中文名 | 含义与默认 |
|---|---|---|
| `ActionId` | 行动顺序 / 行动ID | 行动顺序（新建地图：ActionId = 序号，0 起；`CreateDefault` 里等于国家 ID） |
| `CountryId` | 国家ID | 势力对应 `CountrySettings.json` / stringtable `country_{id}` 的国家；**定位键** |
| `InitialEconomy` | 初始经济 | 默认 **1000**；随机化范围 100-5000 |
| `InitialIndustry` | 初始工业 | 默认 **100**；随机化范围 10-500 |
| `InitialTech` | 初始科技 | 默认 **50**；随机化范围 0-200 |
| `IsPlayerControlled` | 玩家控制 | `0`/`1`；`CreateDefault` 里第 1 个军团为 1 |
| `Camp` | 阵营 | 默认 = 国家 ID；**新增军团置 0（无阵营）**。用于"名义势力 → 实际阵营"的分组 |
| `DefeatCondition` | 战败条件 | 默认 0，含义随战役配置（界面只给数字） |
| `CountryHpRate` | 国家血率 | **float**，默认 **1.0** |
| `CountryTaxRate` | 国家税率 | **float**，默认 **0.1** |
| `ColorR` / `ColorG` / `ColorB` | 国家颜色 | 各 0-255，默认全 `0xFF`（白）。渲染归属国旗/着色用 |
| `AtomicBombCount` | 原子弹 | 初始库存 |
| `HydrogenBombCount` | 氢弹 | 初始库存 |
| `NeutronBombCount` | **三相弹**（信息面板这么叫，不是"中子弹"） | 初始库存 |
| `AntimatterBombCount` | 反物质弹 | 初始库存 |
| `InitialTechLevel` | 科技等级 / 初始科技等级 | VB 原版默认 0；**本项目新增军团给 1**（否则开局不可玩） |
| `CountryAiBehavior` | 国家 AI 行为 | AI 行为编号 |
| `Unknown1` / `Unknown2` / `Unknown3` / `Unknown4` | 未知字段 1-4 | 尚未破解语义，界面里可编辑；**改动前先备份** |

## 科技等级族（关键：它们对应"各类兵器的科技等级"）

所有等级字段都是 **0-5 的整数**（随机化用 `Next(0, 6)`），共 54 个，按兵种分组：

| 组 | 字段 | 对应装备 |
|---|---|---|
| 陆军（21 项） | `MobilityLevel` `RifleLevel` `CamouflageLevel` `EngineerLevel` `GrenadeLevel` `MortarLevel` `MarchLevel` `BulletproofVestLevel` `ArmorLevel` `MainGunLevel` `VehicleBodyLevel` `EngineLevel` `MachineGunLevel` `RaidLevel` `VehicleAirDefenseLevel` `ReinforcedBodyLevel` `ArtilleryLevel` `RocketLevel` `TowingLevel` `ArtilleryArmorLevel` `FirepowerLevel` | 机动/步枪/伪装/工兵/手雷/迫击炮/行军/防弹衣/装甲/主炮/车体/发动机/机枪/突击/车载防空/加固车体/火炮/火箭/牵引/炮塔装甲/火力 |
| 海军（11 项） | `NavyRocketLevel` `DisguiseLevel` `HullLevel` `PropulsionLevel` `NavyArmorLevel` `WeaponLevel` `NavalGunLevel` `TorpedoLevel` `MinesweepingLevel` `ShipAirDefenseLevel` `ModernHullLevel` | 舰用火箭/伪装/船体/推进/舰装甲/武器/舰炮/鱼雷/扫雷/舰载防空/现代化船体 |
| 空军（8 项） | `AviationFuelLevel` `AviationEngineLevel` `AviationBombLevel` `AirRaidLevel` `BombingLevel` `StrategicBombingLevel` `AirdropLevel` `JetEngineLevel` | 航油/航发/航弹/空袭/轰炸/战略轰炸/空投/喷气发动机 |
| 防御/导弹（14 项） | `BunkerLevel` `FortressGunLevel` `CoastalGunLevel` `RocketLauncherLevel` `FortificationLevel` `AntiAircraftGunLevel` `AntiAircraftCannonLevel` `AntiAircraftMissileLevel` `RadarLevel` `MissileWarheadLevel` `SolidRocketEngineLevel` `NuclearBombBreakthroughLevel` `NuclearFusionLevel` `InitialLaserGunLevel`（激光等级） | 地堡/要塞炮/岸防炮/火箭发射器/筑垒/高射机枪/高射炮/防空导弹/雷达/导弹弹头/固体火箭发动机/核弹突防/核聚变/**激光炮** |

**派生只读值**（快照里可直接读，不能写）：`AverageArmyLevel` / `AverageNavyLevel` / `AverageAirForceLevel` / `AverageDefenseLevel` —— 分别是上面四组的**平均等级**（`Legion.GetAverage*Level()`，用来一眼看出这个势力强在哪）。

## 常用改法

```lua
-- 让 0 号势力开局富裕、并把陆军科技整体拉到 4
map.SetLegionField(0, 'InitialEconomy', 3000)
map.ForEachLegion(function(l) map.SetLegionField(l.Index, 'ArmorLevel', 4) end)

-- 改名国家 ID 必须用专用接口（会校验唯一性）
map.SetLegionCountryIdAt(0, 7)
```

## 坑

- **"军团数量"存在 `Header.ArmyCount`**（名字里是 Army 但不是单位数），写盘时按代码来。
- **改 `CountryId` 用通用字段会失败**（不在白名单）；用 `set-country-at` / `SetLegionCountryIdAt`。
- **删除军团会让后面所有索引前移** → 归属值跟着变；要批量删请**倒序删**（`for i = LegionCount-1, 0, -1`）。
- `InitialEconomy/Industry/Tech` 是**开局初始值**，"随机化军团等级"（`map.RandomizeLegionLevels`）会同时重置这三项与全部科技等级。
