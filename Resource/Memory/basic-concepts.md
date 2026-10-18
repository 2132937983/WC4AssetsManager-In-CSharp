# basic-concepts

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 这个编辑器在编辑什么、三种地图与文件、场景与编辑模式的区别、MapData 实体清单、几条关键值语义、BTL 段结构、两个校验器、配置与资源。**第一次接触本项目的任务先读这条。**

---

## 1. 这是什么

一个游戏地图编辑器（对齐一份 Java 老实现）。三大能力：

- **地图数据编辑**：地形 / 省区 / 归属 / 建筑 / 军团 / 单位 / 陷阱 / 首都等，按"编辑模式"切换；
- **可脚本化**：控制台命令（CLI）、`.zme` 命令脚本、内置 Lua（MoonSharp）三种方式批量改图，都走同一套接口；
- **带 AI 助手**：能调命令 / 跑 Lua / 切场景 / 截图看图，能力入口是 `AiToolRegistry` + `AiBridge` 静态桥。

## 2. 三种地图类型与文件

| 场景类型（`SceneType`） | 是什么 | 文件 | 加载器 | 允许的编辑模式 |
|---|---|---|---|---|
| `stage` | **战役 / 单关**，数据最全 | `.btl` | `StageParser` | 地形、省区、建筑、单位、归属、军团、援军（7 种） |
| `conquest` | **征服**，按文件名以 `conquest` 开头识别 | `.btl` | `ConquestParser` | 省区、建筑、单位、归属、军团、援军（**没有地形绘制**） |
| `world` | **世界地图**（战略层） | `.bin` | `WorldParser`（magic `0x59 0x53 0x41 0x45` = "YSAE"） | **只有地形绘制** |

- 文件名判定见 `RenderSceneBase.LoadBtlByFileName`；世界地图还有同目录的 `{同名}_map_hd.bin` 高清层。
- 另有**战术地图编辑器**（`TacticalMapEditScene`）：编辑图片 + XML 对象，不操作 MapData，与上面三套是平行功能。

## 3. 场景（Scene）vs 编辑模式（Mode）

- **场景** = 主窗口里的一个 `UserControl`（战役/征服/战术图/军团/国家/建筑/将领/技能/旗帜/资产浏览器/开始界面…），由 `MainWindow.SetCurrentScene()` 挂进 `_mainGrid`。**只有渲染场景才有地图。**
- **编辑模式** = 地图场景内部的工作模式，由 `EditModeManager` 统一管理，数字键切换（见 `BuildSceneModeMap`）：

| 键 | `EditMode` | 名称 |
|---|---|---|
| 1 | `TerrainPaint`(1) | 地形绘制 |
| 2 | `ProvinceEdit`(2) | 省份编辑 |
| 3 | `BuildingDeploy`(3) | 建筑部署 |
| 4 | `ArmyDeploy`(4) | 单位部署 |
| 5 | `BelongEdit`(5) | 归属编辑 |
| 6 | `LegionEdit`(6) | 军团编辑 |
| 8 | `ReinforcementDeploy`(8) | 援军部署 |

注意两点：**7 没有对应模式**（枚举里就缺 7）；**陷阱（Trap）有 `ModifierKind.Trap`、但没有专属编辑模式** —— 它只通过命令 / 随机生成来动。

一个模式背后通常挂 1~N 个 Modifier（`ModifierKind`：Terrain / Province / Building / Army / Legion / Belong / Trap / Reinforcement / Capital）。**能力在 Modifier 里，交互在 ModeHandler 里**，加功能常要先看清是哪一半。

## 4. `MapData` 里的实体

`Terrain`、`Province`、`Belong`、`Building`、`Legion`、`Army`(v1) / `Army_3`(v3)、`Trap`、`Reinforcement`/`Reinforcement_3`、`Capital`、`Case`（关卡计划）、`Weather`、`MapEvent`、`AirForce`（空袭）、`UnitPlacement`、`StrategicConstruction`、`AirSupport`。

**字段名与含义不一致的两个坑**（写文件时按代码来）：

- **军团数量**存在 `Header.ArmyCount`（不是"军队数"）；
- **首都数量**存在 `Header.ConqueredFlagPosition`。

## 5. 关键"值语义"速查（最常被搞错）

| 概念 | 语义 | 空值 |
|---|---|---|
| 归属 Belong | **军团在 `Legions` 列表里的索引**（0~N-1），**不是国家 ID** | `0xFF`(255) |
| 省区 Province | **省会格的索引**（生成器刻意保留省会原值） | `0xFFFF`(65535) |
| 地形判海 | **`TileType1 == 1`** 就是海洋（`0` 是空地，其余是陆地地形） | — |
| 建筑类型 | **> 30 海上建筑，< 30 陆上建筑**；等级城 15–19 才能设首都 | — |
| 单位版本 | `Header.BtlVersion >= 3` → v3 表，否则 v1 表 | — |

**归属索引不能自己算**：`MapData.GetBelongIndex(col,row)` 会处理征服地图的 `MapClipX/Y` 裁剪偏移与 `MapLength` 行回绕；自己写 `row * MapWidth + col` 在有裁剪的地图上会整体错位、甚至读成"无归属"。归属数组本身是**两位十六进制字符串**（`List<string>`，如 `"0A"`），读写一律走 `GetBelongValueByIndex` / `SetBelongValueByIndex`。

## 6. BTL 文件段结构（改解析/写盘必看）

`StageParser.SaveFromMapData` 的写出顺序（各段字节数）：

```
Header → Legion(300) → Terrain(16) → Province(2) → Belong(1) → Building(32)
→ Army(v1 48 / v3 64) → Trap(12) → Case(16) → Weather(16) → Event(44)
→ Reinforcement(v1 80 / v3 104) → AirForce(20) → UnitPlacement
→ Capital(4) → StrategicConstruction(16) → AirSupport(16)
```

v1/v3 差异不只是字段数：**表本身不同**（`Armies` vs `ArmiesV3`、`Reinforcements` vs `ReinforcementsV3`），`Header.TroopCount` 也要按版本取对应表的条数。

## 7. 两个校验器（别搞混）

- `BTLFormatChecker` —— **格式层**：文件能不能解析、各段是否完整；
- `BTLRuleChecker` —— **玩法规则层**：解析出来的数据合不合游戏逻辑（建筑设施等级、单位参数、军团科技、首都必须在 15–19 等级城上…），**对齐 Java 版 `Wc4StageDAO.check()`**。

## 8. 配置与资源

- `setting.txt` —— 程序配置（`ConfigManager`）；它的存在也是"项目根在哪"的判定依据之一；
- `Resource/` —— 图集、`Resource/Memory/`（AI 记忆）；
- 文本表：**`stringtable_{locale}.ini`**（默认 locale `tw`），键值 `key=value`。常用键名约定：
  - 国家名 `country_{id}`（`CountrySettings.json` 里有名字时优先用它，`CountryNameProvider` 做缓存并在文本表重载时失效）
  - 城市名 `battle_cityname_{id:D3}`（**新名字由 `StringTableParser.AddOrUpdateCityName` 自动分配 ID 并保存**）
  - 单位名 `unit_name_{id}`、技能 `skill_name_{id}`、事件 `event_{id}` / `conquest_event_{id}` / `army_buff_desc_{id}` 等；
- JSON 设置：`CountrySettings.json`、`ArmySettings.json`、`BuildingSettings.json`、`FacilitySettings.json`、`GeneralSettings.json` 等（改这些走对应编辑场景，改完记得让相关缓存失效）。

## 9. 对象属性读写（三处入口，同一实现）

建筑 / 单位 / 军团都能按**字段名**读写属性；三条入口底层都是 `MapScriptApi`（同一份字段白名单 + 按类型钳位 + 转发 Modifier）：

| 入口 | 写法 |
|---|---|
| Lua | 读：`map.GetBuilding(col,row)` / `map.GetArmy(col,row)` / `map.GetLegionAt(index)`；写：`map.SetBuildingField` / `SetArmyField` / `SetLegionField`；遍历：`ForEachBuilding` / `ForEachArmy` / `ForEachLegion`；字段清单：`GetBuildingFields` / `GetArmyFields` / `GetLegionFields` |
| CLI | `<组> fields` 查字段、`<组> show` 看对象、`<组> set-field` 改属性（building 另有 `set-type` / `set-name` / `move`；army `show`；legion `show-at` / `set-country-at` / `set-action-at`；province `set`） |
| AI 工具 | `list_object_fields` / `get_object` / `set_object_field`（内部转成上面的 CLI 命令执行）；批量用 `set_object_field_batch`（可用区域/类型/归属/等级/索引区间过滤，内部**生成 Lua 再走 Lua 通道**，所以整批可一次 Ctrl+Z 撤销） |

两条硬约定：

1. **军团一律按"列表索引"定位，索引 == 归属值**（不是国家 ID）；
2. **`CountryId` / `ActionId` 不在通用字段白名单里** —— 它们是军团定位键，改它们要走带唯一性校验的专用命令（`legion set-country-at` / `map.SetLegionCountryIdAt`），否则会出现"按国家找不到军团"或重复国家 ID（BTL 校验器会报）。
