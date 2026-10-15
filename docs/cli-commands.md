# CLI 指令手册

> 文档版本：v1.0
> 更新日期：2026-09-19
> 适用：WC4MapEditor 控制台（F3）
> 相关：[Lua 脚本接口](lua-scripting.md) ｜ [.zme 指令脚本](zme-scripts.md)

---

## 目录

1. [这是什么](#一这是什么)
2. [在哪儿用、怎么用](#二在哪儿用怎么用)
3. [通用规则](#三通用规则)
4. [全局输出重定向 `--out`](#四全局输出重定向---out)
5. [命令参考](#五命令参考)
6. [控制台本地命令](#六控制台本地命令)
7. [场景层注册的命令](#七场景层注册的命令)
8. [重要语义与坑](#八重要语义与坑)

---

## 一、这是什么

控制台里一行一条的命令，形如：

```
building add 87 25 -t 15 -n 北京
belong set 87 25 3
terrain greening -p 60
```

它们是**同一套命令解析器**（System.CommandLine）注册出来的，统一入口是 `CliCommandHost.Execute`。
`.zme` 指令脚本就是「一堆这样的命令逐行执行」，见 [.zme 指令脚本](zme-scripts.md)。

命令分**三类来源**，可用的东西取并集：

| 来源 | 谁注册的 | 例子 |
|---|---|---|
| **CLI 命令树** | `WC4MapEditor.Core/Commands/CliCommandHost.cs` | `building`、`belong`、`legion`、`army`、`terrain`、`province`、`trap`、`info`、`check`、`fix`、`run` |
| **控制台本地命令** | `DebugConsole` | `help`、`clear`、`exit`、`echo`、`history`、`log` |
| **场景层命令** | 当前场景（`RenderSceneBase`） | `undo`、`redo`、`lua`、`goto`、`geo_*`、`bg_province` 等 |

> ⚠️ `run xxx.zme` 里的每一行**只走 CLI 命令树**（不经过控制台本地表和场景命令）。
> 把 `.zme` 文件**拖进控制台**则三类都能用。要混用就拖放。

---

## 二、在哪儿用、怎么用

按 **F3** 打开控制台，输入命令回车。控制台里：

| 操作 | 效果 |
|---|---|
| `help` | 打印命令名与描述一览 |
| ↑ / ↓ | 翻命令历史 |
| Enter | 执行 |
| Esc / F3 | 关闭控制台 |
| 把 `.zme` / `.lua` 文件拖到窗口上 | 自动执行（`.lua` 等价于 `lua "<路径>"`） |

输出的每一条都会同时进控制台日志缓冲，`log` 命令可以回看最近的日志。

---

## 三、通用规则

**参数位置固定**，子命令后面按顺序给：`belong set <列> <行> <归属值>`。列在前、行在后。

**选项**以 `--` 或 `-` 开头，可放参数之后任意位置：

```
building add 87 25 -t 15 -n 北京
building add 87 25 --type 15 --name 北京     ← 等价
```

**注释**（`.zme` 里尤其常用）：`#`、`;`、`//` 前面是空白或位于行首时，从这里截断到行尾。

```
belong set 108 23 0    # 哈尔滨市
belong set 103 19 0    ; 齐齐哈尔市
```

**路径**：`run` / `check` / `fix` / `lua` 的相对路径基于**程序所在目录**，不是当前目录。路径含空格要加引号：

```
run "Maps/背景图/tno-russia-belong.zme"
```

**索引 vs 坐标**：多数命令用 `<列> <行>`；少数用「线性索引」= `行 × 地图宽 + 列`（如 `building add-by-idx`、`legion remove-at`）。

---

## 四、全局输出重定向 `--out`

**任意命令**末尾加 `--out`，就把这次输出**再写一份**到日志文件（控制台照常显示，是"分流"不是"改道"）。

```
building list -n --out                 → 自动写到 程序目录\Logs\时间戳_building.log
belong stats --out D:\dump.txt         → 写到指定文件
legion list --out=my-list.log          → 相对路径按程序目录展开
```

| 写法 | 结果 |
|---|---|
| `--out` | 自动命名：`Logs\<时间戳>_<命令名>.log` |
| `--out 文件名` | 用该名字；相对路径按程序目录展开，目录不存在会自动创建 |
| `--out=名字` | 同上；文件名以 `-` 开头时必须用这种写法 |

日志文件自带头尾（命令原文、开始时间、结束时间）。执行完控制台会提示：
`[日志] 本次输出已写入: <路径>`

覆盖范围：控制台手打、拖入的 `.zme`、`run xxx.zme` 里的每一行、Lua 里发起的命令 —— 都会经过同一个入口。
唯一例外是命令**自己声明了** `--out`（目前只有 `check`，它用它来另存完整报告），这时由该命令自己处理。

---

## 五、命令参考

以下内容由源码（`CliCommandHost.cs`）自动提取，与程序实际注册的一致。

### terrain — 地形修改操作

| 子命令 | 说明 | 参数 / 选项 |
|---|---|---|
| `greening` | 绿化平地 | `-p` 绿化概率(0-100) |
| `random-flat` | 随机平地变体 | `-p` 随机概率(0-100) |
| `random-variant` | 随机当前层变体 | `-p` 概率 ; `-l` 编辑层(1-3) |
| `create-coast` | 创建海岸线 | |
| `process-ocean-layer2` | 处理海洋第二层 | |
| `flood-fill` | 洪水填充地形 | `<col> <row> <type>` |
| `change-type` | 切换地形类型 | `<col> <row>` ; `-d` 变化量 |
| `change-decoration` | 切换地形变体 | `<col> <row>` ; `-d` 变化量 |
| `set-river` | 设置河流值 | `<col> <row>` ; `-v` 河流值(0-63) |
| `copy` / `paste` | 复制 / 粘贴地形 | `<col> <row>` |
| `remove` | 重置编辑层地形 | `<col> <row>` |
| `export-hd` | 导出 HD 文件 | |
| `connect-buildings` | 连接建筑（A* 寻路） | |
| `set-edit-layer` | 设置编辑层(1-3) | `<layer>` |
| `scale-map` | 按比例缩放地图 | `<scale>` |
| `resize-map` | 调整地图大小 | `<direction> <amount>` ; `-o/--ocean` 扩展填充海洋 |
| `get` | 查询指定格子地形数据 | `<col> <row>` |
| `stats` | 统计全图地形类型分布 | |

### province — 省份修改操作

| 子命令 | 说明 | 参数 |
|---|---|---|
| `clear` | 清空所有省份 | |
| `generate` | 生成孤立省会省区 | |
| `expand` | 扩展省区填满地图 | |
| `process` | 处理孤立和空白省区 | |
| `flood-fill` | 洪水填充省份 | `<col> <row>` |
| `set` | **设置指定格子的省区值**（`65535` = 无省区） | `<col> <row> <value>` |

> ⚠️ 省区值约定上是**省会格的索引**，逐格硬填只适合修边补漏；铺整块请用 `generate` / `expand` / `process` / `flood-fill`。

### building — 建筑修改操作

| 子命令 | 说明 | 参数 / 选项 |
|---|---|---|
| `add` | 在指定坐标添加建筑 | `<col> <row>` ; `-t` 类型 ; `-n` 名称 ; `-i` 格子索引(优先于坐标) |
| `add-by-idx` | 按格子索引添加建筑 | `<index>` ; `-t` `-n` |
| `remove` | 删除指定坐标建筑 | `<col> <row>` |
| `remove-all` | 删除所有建筑 | |
| `remove-non-capital` | 删除非首都建筑 | |
| `randomize-named` / `randomize-unnamed` | 随机有/无名称建筑类型 | |
| `randomize-by-belong` | 按归属随机建筑 | `<belongId>` |
| `generate-capitals` | 为所有建筑生成首都 | |
| `randomize-on-capitals` | 在首都随机建筑 | |
| `smart-appearance` | 智能设置建筑外观 | |
| `remove-by-probability` | 按概率删除建筑（每个独立掷骰） | `<probability>` ; `-k/--keep-named` |
| `list` | 列出所有建筑 | `-t` 类型过滤 ; `-n/--named` 仅有名称 ; `-b` 归属过滤 |
| `fields` | **列出建筑可写的字段名** | |
| `show` | **查看指定格子的建筑对象**（全部属性） | `<col> <row>` |
| `set-field` | **按字段名改建筑属性**（大小写不敏感，按字段宽度自动钳位） | `<col> <row> <field> <value>` |
| `set-type` | 改建筑类型 | `<col> <row> <type>` |
| `set-name` | 改建筑名称（省略 = 清除） | `<col> <row> [name]` |
| `move` | 把建筑移到另一格（源格归属一并剪切） | `<fromCol> <fromRow> <toCol> <toRow>` |

可写字段（`building fields` 会原样列出）：`Name` `BuildingType` `Appearance` `LandmarkBuilding` `DecorativeBuilding`
`SkillUnlock` `RewardCount` `HatredValue` `KeyPoint` `OccupationEvent` `FireIgnition` `FireDuration`
`AirDefenseWeapon` `AirDefenseRadar` `FactoryLevel` `ResearchLevel` `MedicalLevel` `AviationLevel`
`MissileLevel` `NuclearLevel`（`Coordinate` 不在内 —— 改坐标用 `move`，它要同步归属与索引）。

建筑类型（`-t`）常用值：`7` 工厂、`11`~`15` 一~五级城、`21` 灯塔。

`-n` 名称参数：省略/空 = 无名称；纯数字 = 名称ID；中文 = 查城市名称表（**查不到会新建条目并写配置**，循环里别乱传）。

### belong — 归属修改操作

| 子命令 | 说明 | 参数 |
|---|---|---|
| `clear` | 清空所有归属 | |
| `set` | 设置指定格子归属 | `<col> <row> <belong>` |
| `remove` | 删除指定格子归属 | `<col> <row>` |
| `copy` / `paste` | 复制 / 粘贴归属值 | `<col> <row>` |
| `fill-from-legion` | 按军团ID填充归属到建筑格子 | `<legionId>` |
| `stats` | 归属统计（会解析成势力名） | |
| `clean-orphan` | 清理孤立归属（无建筑/单位的格子） | |
| `randomize` | 随机化所有归属 | |
| `batch-set` | 把所有**已有归属**的格子统一改为目标值 | `<targetId>` |
| `remove-border-entities` | 删除归属边界上的单位与建筑 | |
| `remove-border-armies` | 删除归属边界上的单位 | |
| `get` | 查询指定格子归属（会解析成势力名） | `<col> <row>` |
| `replace` | 批量替换归属值 | `<oldId> <newId>` |
| `randomize-by` | 随机化指定归属值的格子 | `<belongId>` |
| `set-by-city` | 按城市名批量设置归属 | `<cityName> <belongId>` |
| `set-region` | 矩形区域批量设归属 | `<col1> <row1> <col2> <row2> <belongId>` |
| `set-province` | 按省会格子定位整片省区设归属 | `<capitalCol> <capitalRow> <belongId>` |

### legion — 军团修改操作

| 子命令 | 说明 | 参数 |
|---|---|---|
| `list` | 列出所有军团（**索引 / 国家ID / 国家名 / 颜色 / 玩家控制**） | |
| `get` | 查询军团详情（含列表索引） | `<legionId>` |
| `get-by-action` | 按 ActionId 查询 | `<actionId>` |
| `apply` | 设置军团领域到指定格子 | `<col> <row>` |
| `remove` | 清除指定格子军团领域 | `<col> <row>` |
| `select` | 选中当前军团 | `<legionId>` |
| `add` | 追加一个默认军团 | |
| `remove-at` | 按索引删除军团 | `<index>` |
| `set-color` | 设置军团颜色 | `<legionId> <r> <g> <b>` |
| `set-action` | 设置军团 ActionId | `<legionId> <actionId>` |
| `set-player` | 设置玩家控制军团 | `<actionId>` |
| `apply-default-colors` / `apply-settings-colors` | 应用默认 / 配置颜色 | |
| `randomize-levels` | 随机化所有军团等级与经济 | |
| `rebuild-action-belong` | 重建 ActionId 与归属映射 | |
| `set-economy` / `set-industry` / `set-tech` | 批量设置军团经济/工业/科技 | `<value>` |
| `fields` | **列出军团可写的字段名** | |
| `show-at` | **按列表索引查看军团**（索引即归属值） | `<index>` |
| `set-field` | **按字段名改军团属性** | `<index> <field> <value>` |
| `set-country-at` | 按列表索引设置国家ID（**校验唯一性**） | `<index> <countryId>` |
| `set-action-at` | 按列表索引设置 ActionId | `<index> <actionId>` |

> `set-field` 的字段含：初始经济/工业/科技、玩家控制、阵营、战败条件、血率/税率、颜色 RGB、四种核弹、初始科技、AI 行为，以及**全部科技等级字段**（`ArmorLevel`、`RadarLevel` …）。
> `CountryId` / `ActionId` **不在**通用字段里 —— 它们是定位键，用 `set-country-at` / `set-action-at`（带唯一性校验），否则会出现"按国家找不到军团"或重复国家 ID。

### army — 部队修改操作

单位有 v1 / v3 两种格式，命令内部自动分发，无需关心。

| 子命令 | 说明 | 参数 |
|---|---|---|
| `list` | 列出所有部队 | |
| `remove-all` | 删除所有部队 | |
| `apply` / `remove` | 放置 / 删除指定坐标单位 | `<col> <row>` |
| `copy` / `paste` | 复制 / 粘贴单位 | `<col> <row>` |
| `set-legion` | 设置单位军团ID | `<col> <row> <legionId>` |
| `cycle-plan` | 循环切换方案(0-4) | `<col> <row>` |
| `set-plan` | 设置单位方案(0-4) | `<col> <row> <plan>` |
| `batch-plan-by-legion` | 按归属批量改方案 | `<belong> <plan>` |
| `legion-strength` | 按配置修改军团强度 | `<belong> <level>`（`-1` = 全部/自动） |
| `randomize-legion` | 随机化军团单位 | `<belong>` |
| `generate-by-prob` | 按概率生成单位 | `<belong> <probability>` |
| `auto-general` | 自动分配将领 | `<belong> <count>` |
| `remove-by-belong` | 删除指定归属的所有单位 | `<belong>` |
| `erase-brush` | 橡皮擦批量删除 | `<col> <row> <radius>` |
| `fields` | **列出单位可写的字段名** | |
| `show` | **查看指定格子的单位对象**（含 v1/v3 版本号） | `<col> <row>` |
| `set-field` | **按字段名改单位属性** | `<col> <row> <field> <value>` |

> `set-field` 的字段含：`UnitType` `Level` `Organization` `Direction` `Mobility` `BuiltRound` `Experience`
> `HealthBonus` `CurrentHealth` `MaxHealth` `General` `Rank` `KeyPoint` `Policy` `Plan` `Morale`
> `CanAttack` `LegionId`，以及 **v3 专有**的 `HpLevel` `TransportShip` `Hatred` `AttackTarget`
> `DeathDialogue` `LevelMarkDisplay` `AntiDefenseMode`（写在 v1 地图上会明确失败，不会静默忽略）。

### trap — 陷阱修改操作

| 子命令 | 说明 |
|---|---|
| `list` | 列出所有陷阱 |
| `remove-all` | 删除所有陷阱 |

### info / check / fix

| 命令 | 说明 | 参数 |
|---|---|---|
| `info` | 显示地图信息（尺寸、建筑/单位/省区/归属格数等） | |
| `check [file]` | 检查 btl 格式与玩法规则；省略路径则检查当前地图 | `<file>` ; `-o/--out` 另存完整报告 |
| `fix <file>` | 按玩法规则修复 btl（会写回文件，自动备份 `.bak`） | `<file>` |

### run — 执行脚本文件

| 命令 | 说明 | 参数 |
|---|---|---|
| `run <file>` | 逐行执行 `.zme` 脚本 | `<file>` |

---

## 六、控制台本地命令

这些由 `DebugConsole` 自己处理，**不经过**命令解析器（`run xxx.zme` 里用不了）。

| 命令 | 说明 |
|---|---|
| `help` | 显示帮助信息 |
| `clear` | 清除控制台输出 |
| `exit` | 关闭控制台 |
| `echo <text>` | 输出文本 |
| `history` | 显示命令历史 |
| `log` | 查看最近日志 |

---

## 七、场景层注册的命令

由当前场景（`RenderSceneBase`）注册，只在编辑场景里可用（`run xxx.zme` 里用不了）。

| 命令 | 说明 |
|---|---|
| `undo` / `redo` | 撤销 / 重做上一步操作 |
| `lua <脚本路径.lua>` | 执行 Lua 脚本（见 [Lua 脚本接口](lua-scripting.md)） |
| `goto <col> <row>` \| `<坐标值>` \| `<建筑名称>` | 相机跳转 |
| `goto_building <关键字\|#nameId>` | 相机跳到指定建筑 |
| `list_buildings [关键字]` | 列出建筑 |
| `bg_province [灰度阈值] [梯度阈值] [最小区域像素数]` | 按底图自动切省区 |
| `greening` / `random_flat` / `random_variant` / `create_coast` / `process_ocean_layer2` / `export_hd` / `scale_map` / `resize_map` | 与同名 CLI 命令功能一致的快捷写法（下划线形式） |
| `geo_*` 系列 | 经纬度参考点/标尺：`geo_add_ref`、`geo_calc`、`geo_show_ref`、`geo_set_lat`、`geo_export_ref`、`geo_import_ref`、`geo_ruler` |

> 注意两套命名：CLI 树用**连字符**（`terrain create-coast`），场景层用**下划线**（`create_coast`）。

---

## 八、重要语义与坑

### 8.1 归属值存的是「军团索引」，不是国家ID

`belong set <col> <row> <值>` 里的 `<值>` 是**军团在 Legion 列表里的位置**（0 起，`legion list` 的第一列就是它）。

解析规则（`MapData.ResolveLegionByBelong`）：

```
值 <  军团总数  →  当作「索引」，取 Legions[值]
值 >= 军团总数  →  再按「国家ID」兜底查一次
```

所以**只有当国家ID ≥ 军团总数时**，才能直接填国家ID；小国家ID（比如 11、34）会被当成索引解析到别的军团上 —— 这是归属图"颜色对不上"最常见的根因。

```
legion list                 ← 先看索引表（第一列）
belong set 23 23 47         ← 填索引
belong stats                ← 复查：会把每个值解析成势力名
```

### 8.2 只有「有建筑/单位」的格子，归属才有意义

`belong clean-orphan` 会把没有建筑/单位/陷阱的格子的归属清成 `0xFF`（无归属），
有实体的格子则会与所在省区省会的归属对齐。所以**归属最终是标在城市/单位所在格上的**，
大片空地不需要手工刷。

### 8.3 批量执行不记撤销

`run xxx.zme` 和拖放 `.zme` 都会进入**批量模式**：期间不记录撤销（一次几百上千条命令全存快照会撑爆撤销栈）。
所以**跑脚本前先存盘**。单条手打命令则正常记撤销。

### 8.4 归属覆盖范围受地图裁剪区限制

征服地图的归属数据只覆盖裁剪区（`MapClip`）。给裁剪区外的格子设归属会**失败**并提示
`设置归属失败：(x,y) 不在归属数据范围内`，这不是 bug。若所有格子都设不上，说明该地图根本没有归属数据。

### 8.5 越界不报错的原则

| 类型 | 越界行为 |
|---|---|
| 查询类（`terrain get`、`belong get`） | 返回哨兵值 / 提示超范围 |
| 修改类（`belong set`、`building add` …） | 返回失败并说明原因 |

失败信息**一定会打印出来**，成功则安静执行，只看摘要的"影响 N 条"。

### 8.6 大脚本建议用 `--out` 留档

`.zme` 脚本动辄几百上千行，控制台会被刷走。给命令加 `--out`（或给整个脚本过程留档）比翻控制台可靠：

```
legion list --out
check D:\maps\my.btl --out
```

---

## 附：相关文件

| 路径 | 说明 |
|---|---|
| `WC4MapEditor.Core/Commands/CliCommandHost.cs` | CLI 命令树定义（本文档第五节的数据来源） |
| `WC4MapEditor.Rendering/Skia/DebugConsole.cs` | 控制台本地命令 |
| `Views/RenderSceneBase.cs` | 场景层命令注册 |
| `docs/lua-scripting.md` | Lua 脚本接口 |
| `docs/zme-scripts.md` | .zme 指令脚本写法 |
