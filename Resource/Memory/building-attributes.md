# building-attributes

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 建筑（32 字节）每个字段的含义、取值范围与校验规则。改建筑前读这条：字段名 → 中文含义 → 合法值。

---

## 怎么读写

- Lua：`map.GetBuilding(col,row)` 读快照；`map.SetBuildingField(col,row,字段名,值)` 写；`map.GetBuildingFields()` 查字段清单
- CLI：`building show <col> <row>` 看全字段；`building set-field <col> <row> <field> <v>` 改
- AI 工具：`get_object` / `set_object_field` / `set_object_field_batch`

字段名就是下面表格里的**英文名**（大小写不敏感），取值超范围会被**自动钳位**（不是报错）。

## 字段表

| 字段 | 中文名（含别名） | 含义与取值范围 |
|---|---|---|
| `Coordinate` | 坐标 | **线性索引** `row * MapWidth + col`（0xXXXX）。**不能直接写** —— 移动建筑用 `map.MoveBuilding` / `building move`，它还要同步归属与坐标索引 |
| `Name` | 名称 ID | 城市名在 stringtable 的 ID（键 `battle_cityname_{id:D3}`）。**`0xFFFF` 与 `0` 都表示"无名称"**；中文名会自动分配新 ID 并落盘 |
| `BuildingType` | 建筑类型 | 0-255，见下方类型表。**> 30 视为海上建筑、< 30 视为陆上建筑**（校验器按这个界纠正放错位置的建筑） |
| `Appearance` | 外观 / 方向参数 | 港口等建筑的朝向/外观。**非海上建筑（类型 < 30）校验器要求 ≤ 1**，否则会被警告并改为 1 |
| `LandmarkBuilding` | 地标建筑 | 地标标记 |
| `DecorativeBuilding` | 装饰建筑 | 装饰标记 |
| `SkillUnlock` | 技能解锁 | 解锁的技能编号 |
| `RewardCount` | 奖励数量 | 占领/触发时给的奖励数 |
| `HatredValue` | 仇恨值 | AI 仇恨相关 |
| `KeyPoint` | 关键据点 / 红圈 | **0 = 否，1 = 红圈，2 = 绿圈**，上限 2（超出会被清零） |
| `OccupationEvent` | 占领事件 | 占领时触发的事件 ID |
| `FireIgnition` | 火焰燃起 | 初始起火状态 |
| `FireDuration` | 火焰持续 | 燃烧持续回合/时长 |
| `AirDefenseWeapon` | 防空武器 | 合法值集合：**`0` 或 `11..22`**（其它值会被警告并清零） |
| `AirDefenseRadar` | 防空雷达 | **只能是 `0` 或 `2`**（`0`=无，`2`=有） |
| `FactoryLevel` | 工厂等级（校验器/游戏里叫**工业**） | **0-4** |
| `ResearchLevel` | 科研等级（校验器叫**科技**） | **0-4** |
| `MedicalLevel` | 医疗等级（校验器叫**补给站**） | **0-4** —— 同一个字段，面板与校验器标签不同，指的是补给/医疗设施等级 |
| `AviationLevel` | 航空等级（校验器叫**机场**） | **0-4** |
| `MissileLevel` | 导弹等级 | **0-2** |
| `NuclearLevel` | 核弹等级 | **0-2** |

> 取值范围出处：`BTLRuleChecker` 的常量 `MaxFacilityLevel = 4`（工业/科技/补给站/机场）、
> `MaxWeaponLevel = 2`（导弹/核弹）、`MaxKeyPoint = 2`、`ValidAirDefenseWeapon = {0,11..22}`、`ValidRadar = {0,2}`，
> 这些规则与 Java 版 `check()` 一致。

## 建筑类型表（`BuildingType`）

| 值 | 含义 | 值 | 含义 |
|---|---|---|---|
| 0 | 无 | 8 | 研究所 |
| 1 | 城市 | 9 | 补给站 |
| 2 | 港口 | 10 | 机场 |
| 3 | 关口 | 11-15 | **一级城 ~ 五级城** |
| 4 | 要塞 | 21 / 22 / 23 | 灯塔 / 哨塔 / 要塞2 |
| 5 | 小镇 | 31-34 | **海上建筑 1~4** |
| 6 | 村庄 | | |
| 7 | 工厂 | | |

- **可设首都的范围是 15-19**（`BTLRuleChecker.MinCapitalBuildingType/MaxCapitalBuildingType`）；低于 15 的普通城市不能当首都。
- 类型 7 是工厂（脚本里最常用来铺工业）；类型 11 是"一级城"，也是新建/IP 导入时的默认类型。

## 关键语义与坑

- **"有设施"的判定**（`HasFacility`）：`FactoryLevel`/`ResearchLevel`/`MedicalLevel`/`AviationLevel`/`MissileLevel`/`NuclearLevel`/`AirDefenseRadar`/`AirDefenseWeapon`/`KeyPoint`/`Appearance`/`RewardCount` **任一非 0** 就算"有设施"。
  校验器在把建筑改成"普通城市"时会调用 `ClearFacilities` **一次性清掉这 11 个字段** —— 所以"改个类型"可能连带清空设施，别指望它们自动保留。
- **归属不属于建筑字段**：建筑格子上的归属是**格子属性**（`Belong`），用 `map.SetBelong(col,row,v)` / `belong set` 改，不在 `SetBuildingField` 的白名单里。
- **名称是 ID 不是文本**：`SetBuildingField(...,'Name',123)` 是"名称 ID = 123"；要按**中文名**设置请用 `SetBuildingName` / `building set-name`（会查表并在缺条目时新建）。
- 快照 `ScriptBuildingInfo` 的这些字段都是**只读副本**；给它赋值不报错也不生效，必须走 `SetBuildingField`。
