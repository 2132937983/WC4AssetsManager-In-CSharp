# army-attributes

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 单位（v1 48 字节 / v3 64 字节）每个字段的含义、取值范围，以及 v1/v3 差异。改单位前读这条。

---

## 怎么读写

- Lua：`map.GetArmy(col,row)`（快照带 `a.Version`：1 或 3）、`map.SetArmyField(col,row,字段名,值)`、`map.GetArmyFields()`、批量遍历 `map.ForEachArmy`
- CLI：`army show <col> <row>` / `army set-field <col> <row> <field> <v>` / `army fields`
- **一格里只有一个单位**；v1/v3 由 `Header.BtlVersion` 决定，接口内部自动分派，不用手判

## 共有字段

| 字段 | 中文名 | 含义与取值 |
|---|---|---|
| `Coordinate` | 坐标 | 线性索引（同建筑）。**不在可写白名单里**：要移动单位用 `army apply` / `remove` 重建，或走复制粘贴 |
| `UnitType` | 兵种 | **`0`=步兵、`1`=骑兵、`2`=弓箭手、`4`=海军、`5`=空军**（注意没有 3） |
| `Level` | 等级 | 单位等级（升级直接改它） |
| `Organization` | 编制 / 编队 | **0-4** |
| `Direction` | 朝向 | **`0`=左、`1`=右** |
| `Mobility` | 机动 | 机动值 |
| `BuiltRound` / `ChangeRound` | 建造回合 / 变更回合 | 记录用 |
| `Experience` | 经验 | 经验值 |
| `HealthBonus` | 血量加成 | **基准 100 = 100%**。填 0 会让血量上限失效、血条显示异常（`DefaultHealthBonus = 100`） |
| `CurrentHealth` / `MaxHealth` | 当前 / 最大血量 | 数值本身 |
| `General` | 将领 ID | **`0` = 无将领**。⚠️ 校验器：**无将领却有军衔/爵位会被清零** —— 设 `Rank`/`Nobility` 前先把 `General` 设上 |
| `Rank` | 军衔 | **1-11** |
| `Nobility` | 爵位 / 贵族 | **1-4** |
| `KeyPoint` | 关键据点 / 红圈 | **0-2**（`0`=否、`1`=红圈、`2`=绿圈），上限 2 |
| `Policy` | 政策 | **`0`=自由行动、`1`=猛攻、`2`=缓慢进攻、`3`=坚守、`4`=撤退** |
| `Plan` | 方案 | **0-4**（编辑器里"方案"是常用批量项：`army set-plan` / `batch-plan-by-legion`） |
| `Morale` | 士气 | **`0`=正常、`253`=混乱、`254`=士气双降、`255`=士气下降** |
| `LegionId` | 所属军团 | **归属值 = 军团列表索引**（不是国家 ID）。`army set-field ... LegionId 3` |

## v1 独有（v3 结构里没有这两个字段）

| 字段 | 中文名 | 说明 |
|---|---|---|
| `CanAttack` | 可否攻击 | `0`/`1`（v3 的地图里没有这个字段；v3 快照里恒为 0） |
| `Dialogue` | 对话 | 对话/台词编号 |

## v3 独有（写在 v1 地图上会明确失败："该版本的数据结构没有它"）

| 字段 | 中文名 | 说明 |
|---|---|---|
| `HpLevel` | 血量等级 | v3 用血量等级表达强度 |
| `TransportShip` | 运输船 | 运输/登陆相关 |
| `Hatred` | 仇恨值 | AI 仇恨 |
| `AttackTarget` | 攻击目标 | 当前攻击目标 |
| `DeathDialogue` | 阵亡对话 | 阵亡时触发的对话 |
| `LevelMarkDisplay` | 等级标记显示 | 是否显示等级标记 |
| `AntiDefenseMode` | 反防模式 | 反防御模式开关 |
| `Medal1-3` / `Ribbon1-3` | 勋章 / 绶带 | 表彰类字段（当前不在可写白名单里，需要我再加） |

## 坑

- **`Rank`/`Nobility` 与 `General` 绑定**：校验器认为"无将领却有军衔"是错误并清零，批量给军衔时别忘了同时设将领。
- **`HealthBonus` 别填 0**：它是百分比基准（100 = 100%）。
- **v3 字段写在 v1 上是失败而不是忽略**：批量工具会自动加上 `a.Version >= 3` 条件跳过 v1 单位（这是设计如此，看到"成功 0 失败 0"就是被跳过了）。
- 单位不携带省区/归属数据：格子上的归属同样是**格子属性**（`map.SetBelong`）。
