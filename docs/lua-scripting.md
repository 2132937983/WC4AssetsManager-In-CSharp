# Lua 脚本接口说明

> 文档版本：v2.0
> 更新日期：2026-09-19
> 适用：WC4MapEditor（内置 Lua 引擎，MoonSharp）
> 相关：[CLI 指令手册](cli-commands.md) ｜ [.zme 指令脚本](zme-scripts.md)

---

## 目录

1. [这是什么](#一这是什么)
2. [快速开始](#二快速开始)
3. [Lua 语法要点](#三lua-语法要点)
4. [全局对象](#四全局对象)
5. [`map` 接口完整参考](#五map-接口完整参考)
6. [对象字段参考](#六对象字段参考)
7. [重要语义与坑](#七重要语义与坑)
8. [完整示例](#八完整示例)
9. [如何扩展新接口（开发者）](#九如何扩展新接口开发者)
10. [故障排查](#十故障排查)

---

## 一、这是什么

编辑器内置了一个 **Lua 解释器**（MoonSharp），脚本可以通过全局对象 `map` 直接读写地图数据。

**和命令行脚本（`.zme`）的区别**：

| | [`.zme` 命令行脚本](zme-scripts.md) | `.lua` Lua 脚本 |
|---|---|---|
| 本质 | 就是一堆控制台命令逐行执行 | 真正的编程语言 |
| 能力 | 限于已有命令 | 可循环、判断、算数、写函数；**已覆盖建筑/单位/军团/地形/归属全量操作** |
| 适合 | 固定流程的批量操作 | **带逻辑/条件/统计的复杂处理** |
| 执行 | 拖进控制台，或 `run xxx.zme` | 拖进控制台，或 `lua xxx.lua` |

**什么时候该写 Lua**：当你要做的事情**没法用固定的一串命令表达**时。比如：

- "把每个建筑按它所在格子的归属值分别处理"
- "找出所有 3 格内有水域的城市，把它们降级"
- "统计每种建筑类型的数量并打印报表"
- "只删除那些周围 5 格都没有其它建筑的孤立工厂"
- "按归属批量整理战区：清空旧单位 → 按概率生成 → 统一方案 → 分配将领"
- "把全图水域逐格改成草地，再绿化、创建海岸线"

这些都需要**读数据 → 判断 → 再决定怎么改**，命令行做不到，Lua 可以。

---

## 二、快速开始

### 1. 写脚本

在 `scripts/` 目录新建文件，比如 `scripts/hello.lua`：

```lua
print(string.format('地图 %d x %d，共 %d 格', map.Width, map.Height, map.TileCount))
print('建筑数: ' .. map.BuildingCount)
```

### 2. 运行

**方式 A — 控制台**（F3 打开）：

```
lua scripts/hello.lua
```

**方式 B — 拖放**：直接把 `.lua` 文件拖到控制台窗口上，等价于执行 `lua "<该文件路径>"`。

### 3. 看输出

```
地图 200 x 180，共 36000 格
建筑数: 1523
[Lua] 执行完成：2 行输出，影响 0 条数据，12 ms
```

最后那行是执行摘要：输出行数 / 改动条数 / 耗时。

### 关于路径

`lua` 命令的**相对路径基于程序所在目录**（不是当前工作目录，也不是脚本所在目录）：

```
lua scripts/hello.lua      -- 程序目录下的 scripts/hello.lua
lua D:/maps/tool.lua       -- 绝对路径，随意放
```

拖放不受此限——拖放时会自动转成绝对路径（带引号，兼容含空格的目录）。

---

## 三、Lua 语法要点

如果你不熟 Lua，看这一节就够了。

### 语言版本与可用库

引擎是 **MoonSharp**，语法遵循 **Lua 5.2**：

| 特性 | 可用 |
|---|---|
| `goto` / 标签 | ✅ |
| `//` 整除（5.3 特性） | ❌ 用 `math.floor(a / b)` |
| 全局 `unpack` | ✅ |
| `loadstring` | ✅ 等价于 `load`（5.1 兼容写法） |

默认开放的标准库：

| 库 | 可用 | 说明 |
|---|---|---|
| `string` | ✅ | `format` `rep` `sub` `find` `gsub` … |
| `table` | ✅ | `insert` `remove` `sort` `concat` … |
| `math` | ✅ | `floor` `max` `min` `random` … |
| `os` | ✅ | `clock` / `time` / `date` 可用；`exit` / `execute` 已禁用 |
| `io` | ✅ | `open` / `lines` 可读写文件；`io.write` 并入脚本输出 |
| `coroutine` | ✅ | 存在 |
| `debug` | ✅ | 仅用于 `debug.traceback` 与错误定位 |
| `utf8` | ✅ | `len` / `char` / `codepoint` / `offset` |
| `require` / `dofile` | ✅ | 搜索顺序：脚本目录 → `scripts/` → 程序目录 |

### 运行时环境细节

| 项 | 行为 |
|---|---|
| `os.clock()` | 进程启动以来的秒数（单调），可直接做耗时统计 |
| `os.time()` | 整数秒；`os.date('%Y-%m-%d %H:%M', os.time())` 可直接用 |
| `os.exit()` / `os.execute()` | **已禁用**：调用会抛错（`pcall` 能捕获），避免脚本杀掉编辑器或执行外部命令 |
| `require('mod')` | 依次在「脚本所在目录 → 程序目录\scripts → 程序目录」找 `mod.lua` / `mod/init.lua`；`require('a.b')` 对应 `a/b.lua` |
| `package.path` | 上述路径拼成的字符串，可读，也可在脚本里改成自定义值 |
| `utf8.len(s)` | 按**字符**计数（不是字节）：`utf8.len('北京abc') == 5` |
| `utf8.offset(s, n)` | 返回第 n 个字符的**字节位置**，配合 `string.sub` 截取中文 |
| `io.write(...)` | 输出并入脚本日志（与 `print` 一样脚本结束后统一显示）；`io.flush()` 为空操作 |
| `debug.traceback()` | 可用；运行时错误会**自动附上调用栈** |
| 输出上限 | 单次执行最多保留 50000 行，超出会提示截断（防止死循环刷屏） |

### 变量

```lua
local n = 10           -- 局部变量（推荐，加 local）
name = "北京"          -- 全局变量（不加 local，一般别这么写）
```

### 判断与循环

```lua
if n > 5 then
    print('大于5')
elseif n > 2 then
    print('大于2')
else
    print('很小')
end

for i = 1, 10 do              -- 1 到 10
    print(i)
end

while n > 0 do
    n = n - 1
end
```

### 函数

```lua
local function add(a, b)
    return a + b
end

print(add(1, 2))      -- 3
```

匿名函数（写回调时常用）：

```lua
map.ForEachBuilding(function(b)
    print(b.Col, b.Row)
end)
```

### 表（table）

Lua 的表既是数组也是字典：

```lua
local list = { 10, 20, 30 }        -- 数组
print(list[1])                     -- 10（Lua 下标从 1 开始！）

local dict = { }                   -- 字典
dict['北京'] = 5
print(dict['北京'])                -- 5
```

### 字符串

```lua
local s = "归属=" .. value         -- .. 是拼接
print(string.format('%d 个，%.1f%%', n, pct))   -- 格式化
print(#s)                          -- 取长度
```

> ⚠️ **`..` 只接受字符串和数字**。布尔值和 C# 对象直接拼会**报错**：
>
> ```lua
> print('成功: ' .. true)              -- ✗ attempt to concatenate a boolean value
> print('结果: ' .. result)            -- ✗ attempt to concatenate a userdata value
>
> print('成功: ' .. tostring(true))    -- ✓
> print('结果: ' .. tostring(result))  -- ✓
> print('结果: ' .. result.Message)    -- ✓ 取字段就没问题
> print(string.format('%d', 42))       -- ✓ 数字可以
> ```
>
> 这是脚本里最常见的报错来源，凡是拼布尔值或 `map.*` 的返回值，都套一层 `tostring()`。

常用格式符：

| 符 | 含义 | 例 |
|---|---|---|
| `%d` | 整数 | `print(string.format('%d', 42))` → `42` |
| `%.1f` | 保留 1 位小数 | `28.75` → `28.8` |
| `%s` | 字符串 | | 
| `%5d` | 宽度 5 右对齐 | | 

### 注释

```lua
-- 单行注释

--[[
   多行注释
]]
```

### 拼接数字要转换

```lua
print('数量: ' .. n)          -- 可以，Lua 自动转
```

但 C# 对象直接拼会报错，用 `string.format` 或 `.Message`：

```lua
print('结果: ' .. result.Message)    -- ✓
```

---

## 四、全局对象

脚本启动时，运行环境里有这些全局符号：

| 名称 | 类型 | 说明 |
|---|---|---|
| `map` | 对象 | **地图数据访问入口**，所有操作都通过它 |
| `print(...)` | 函数 | 输出到控制台 |
| `log(msg)` | 函数 | 同 `print`，语义更明确的写法 |

`print` 由引擎接管，输出会被收集起来、在脚本结束后一次性打到控制台。所以**脚本跑的过程中控制台不会实时刷新**，跑完才出结果。

---

## 五、`map` 接口完整参考

所有成员名都是 **PascalCase**（首字母大写），与 C# 侧一致。

### 5.1 地图信息（只读属性）

| 成员 | 类型 | 说明 |
|---|---|---|
| `map.Width` | 整数 | 地图列数 |
| `map.Height` | 整数 | 地图行数 |
| `map.TileCount` | 整数 | 格子总数（`Width × Height`） |
| `map.BuildingCount` | 整数 | 建筑总数 |
| `map.ArmyCount` | 整数 | 单位总数（v1 + v3） |
| `map.LegionCount` | 整数 | 军团总数 |

```lua
print(string.format('地图 %d x %d，单位 %d，军团 %d', map.Width, map.Height, map.ArmyCount, map.LegionCount))
```

> ⚠️ `map.BuildingCount` / `map.ArmyCount` / `map.LegionCount` 都是**实时值**。删完再读，拿到的是删完后的数量。

### 5.2 建筑操作

#### `map.ForEachBuilding(callback)`

遍历所有建筑。`callback` 接收一个建筑对象，**返回 `false` 可提前结束遍历**。

```lua
local named = 0
map.ForEachBuilding(function(b)
    if b.HasName then named = named + 1 end
end)
print('有名称的建筑: ' .. named)
```

提前结束：

```lua
local first
map.ForEachBuilding(function(b)
    if b.BuildingType == 11 then
        first = b
        return false          -- 找到就停
    end
end)
```

**遍历期间可以安全增删建筑** —— 引擎遍历的是内部副本，不会因为集合被改动而报错。但要注意：**副本意味着遍历开始时快照的那些建筑**，遍历中途新建的建筑不会出现在本轮遍历里。

#### `map.GetBuilding(col, row)`

取指定格子的建筑，没有则返回 **`nil`**。

```lua
local b = map.GetBuilding(10, 12)
if b then
    print(b.BuildingTypeName .. ' ' .. b.Name)
else
    print('该格没有建筑')
end
```

> 坐标越界也返回 `nil`，不报错。

#### `map.AddBuilding(col, row, buildingType [, name])`

在指定格子添加建筑，返回 [操作结果](#63-操作结果-scriptoperationresult)。

| 参数 | 类型 | 说明 |
|---|---|---|
| `col` | 整数 | 列 |
| `row` | 整数 | 行 |
| `buildingType` | 整数 | 建筑类型（0-255） |
| `name` | 字符串，可选 | 名称，见下 |

**`name` 参数的解析规则**：

| 传入 | 结果 |
|---|---|
| 省略 / `nil` / `""` | 无名称（`0xFFFF`） |
| `"123"` | 名称 ID 直接就是 123 |
| `"北京"` | 查城市名称表；**查不到会新建条目并写入配置** |
| `"-1"` | 等价于无名称（兼容旧输入习惯） |

> ⚠️ 传中文名称有两个注意点：
>
> 1. **有副作用**：名称表里没这个词时会新增一条并保存到配置文件。批量传各种随机名称会让名称表膨胀，别在循环里传不可控的字符串。
> 2. **失败是静默的**：名称表查不到、又无法新建时（配置未加载、名称表只读等），会退化成「无名称」，**不报错、不提示**。
>    - 表里**已有**的名称（如「北京」）随时可用
>    - **新**名称依赖名称表可写；写不进去就静默变成 `0xFFFF`
>
>    实测中「北京」成功、而一个自定义新名称拿到了 `0xFFFF`，就是这个原因。

```lua
map.AddBuilding(10, 12, 11, '北京')   -- 一级城，命名"北京"
map.AddBuilding(11, 12, 7)            -- 工厂，无名称
```

常见建筑类型（详见游戏数据表）：

| 类型 | 含义 |
|---|---|
| 7 | 工厂 |
| 11 ~ 15 | 一级城 ~ 五级城 |
| 21 | 灯塔 |

#### `map.RemoveBuilding(col, row)`

删除指定格子的建筑。

```lua
local r = map.RemoveBuilding(10, 12)
if not r.Success then
    print('删除失败: ' .. r.Message)
end
```

#### `map.RemoveBuildingsByProbability(probability [, keepNamed])`

**按概率删除建筑**，每个建筑**独立掷骰**。

| 参数 | 类型 | 说明 |
|---|---|---|
| `probability` | 整数 | 删除概率，0-100（百分比） |
| `keepNamed` | 布尔，可选 | `true` 时保留有名称的建筑（城市/据点），默认 `false` |

```lua
local r = map.RemoveBuildingsByProbability(30, true)
print(r.Message)
```

> ⚠️ **不是「按总数抽 30%」**。每个建筑各自掷一次骰子，所以实际删除数量会围绕 30% 上下波动 —— 1000 个建筑可能删掉 287 个（28.7%），跑第二次又是另一个数。这是刻意的：表达的是「每个建筑有 30% 概率消失」。

#### `map.GetBuildingFields()`

列出建筑对象上**可写**的字段名（字符串数组）。给脚本自省用：先拿清单，再 `map.SetBuildingField` 按名改写。

```lua
for _, f in ipairs(map.GetBuildingFields()) do print(f) end
```

#### `map.SetBuildingField(col, row, field, value)`

**按字段名改建筑的某个属性**（字段名大小写不敏感）。这是修改建筑对象属性的通用入口，覆盖：
`Name` `BuildingType` `Appearance` `LandmarkBuilding` `DecorativeBuilding` `SkillUnlock` `RewardCount`
`HatredValue` `KeyPoint` `OccupationEvent` `FireIgnition` `FireDuration` `AirDefenseWeapon`
`AirDefenseRadar` `FactoryLevel` `ResearchLevel` `MedicalLevel` `AviationLevel` `MissileLevel` `NuclearLevel`

| 参数 | 说明 |
|---|---|
| `field` | 字段名，大小写不敏感；不在清单里会失败并**列出可用字段** |
| `value` | 数值，按字段宽度自动钳位（byte 字段 0-255、`Name` 0-65535） |

```lua
map.SetBuildingField(10, 12, 'FactoryLevel', 3)   -- 工厂等级改 3
map.SetBuildingField(10, 12, 'KeyPoint', 1)       -- 标成红圈
map.SetBuildingField(10, 12, 'NuclearLevel', 99)  -- byte 字段：钳到 255（会打印警告性说明? 不会，静默钳位）
```

> ⚠️ 该格**没有建筑**时返回失败（`Success = false`），不会凭空新建。要先 `AddBuilding`，或直接 `AddBuilding` 时把类型/名称一次给全。

#### `map.SetBuildingType(col, row, buildingType)`

改建筑类型（0-255），等价于 `SetBuildingField(col, row, 'BuildingType', ...)` 的便捷写法。

```lua
map.SetBuildingType(10, 12, 13)   -- 改成三级城
```

#### `map.SetBuildingName(col, row [, name])`

改建筑名称，规则与 [`AddBuilding` 的 `name` 参数](#mapaddbuildingcol-row-buildingtype--name)完全一致（省略/空串/`"-1"` = 清除名称；中文名查不到会新建 stringtable 条目）。

```lua
map.SetBuildingName(10, 12, '柏林')
map.SetBuildingName(10, 12)       -- 清除名称
```

#### `map.MoveBuilding(fromCol, fromRow, toCol, toRow)`

把建筑从一格移到另一格。目标格已有建筑时先删掉它；**源格的归属值会一并剪切到目标格**（源格变无归属）。

```lua
local r = map.MoveBuilding(10, 12, 11, 12)
if r.Success then print('已移动：' .. r.Message) end
```

### 5.3 归属 / 省区 / 地形

#### `map.GetBelong(col, row)`

取归属值。**`255`（`0xFF`）表示无归属**。坐标越界也返回 `255`。

```lua
local belong = map.GetBelong(89, 36)
if belong == 255 then
    print('无归属')
else
    print('归属: ' .. belong)
end
```

#### `map.SetBelong(col, row, belongValue)`

设置归属值，返回操作结果。`belongValue` 范围 **0-255**。

```lua
local r = map.SetBelong(89, 36, 3)
if not r.Success then
    print('设置失败: ' .. r.Message)
end
```

> ⚠️ **征服地图要特别注意**：归属数据只覆盖裁剪区（`MapClip`）。给裁剪区外的格子设归属会**失败**并返回 `设置归属失败：(x,y) 不在归属数据范围内`。这不是 bug，是数据本身不覆盖那些格子。
>
> 同一个消息还有另一种来由：**该地图根本没有归属数据**。这时所有格子都设不上，表现是「清一色失败」。
>
> 好消息是**失败会自动打到输出里**，不用自己检查也能看见（见 [7.4](#74-失败会自动打印成功不会)）。

#### `map.GetProvince(col, row)`

取省区值。**`65535`（`0xFFFF`）表示无省区**。越界同样返回 `65535`。

```lua
print(map.GetProvince(89, 36))
```

#### `map.SetProvince(col, row, provinceValue)`

设置省区值，返回操作结果。`provinceValue` 范围 **0-65535**（`65535` = 无省区）。

```lua
local r = map.SetProvince(89, 36, 12)
if not r.Success then print('失败: ' .. r.Message) end
```

> ⚠️ 省区值约定上是**省会格的索引**（见 [`basic-concepts`](../Resource/Memory/basic-concepts.md)）。逐格硬填会破坏这层对应关系，省区填充/连通性判断都会失真。要铺一整块省区，优先用省区编辑模式或区域填充类操作；这里开放逐格写入是为了修边、补漏这类精确修补。

#### `map.GetTerrain(col, row)`

取该格子的第一层地形类型（`TileType1`）。越界返回 **`-1`**。

```lua
print('地形: ' .. map.GetTerrain(10, 10))
```

### 5.4 单位操作

单位操作覆盖 v1 和 v3 两种单位格式，接口内部自动按 `BtlVersion` 分发，脚本无需关心版本差异。

#### `map.AddArmy(col, row)`

在指定格子放置单位，返回操作结果。

```lua
local r = map.AddArmy(10, 20)
if r.Success then print('放置成功') end
```

#### `map.RemoveArmy(col, row)`

删除指定格子的单位。

```lua
map.RemoveArmy(10, 20)
```

#### `map.SetArmyLegion(col, row, belong)`

设置单位所属军团。**单位的归属就是它所在格子的归属**（单位记录里没有存军团），所以这里改的是该格的归属值，
等价于 `map.SetBelong(col, row, belong)`；该格没有单位时失败。

```lua
map.SetArmyLegion(10, 20, 3)   -- 归入 3 号军团（该格归属改为 3）
```

#### `map.SetArmyPlan(col, row, plan)`

设置单位方案，`plan` 范围 **0-4**。

```lua
map.SetArmyPlan(10, 20, 2)
```

#### `map.CycleArmyPlan(col, row)`

循环切换单位方案（0→1→2→3→4→0）。

```lua
map.CycleArmyPlan(10, 20)
```

#### `map.BatchModifyArmyPlanByLegion(belong, plan)`

按归属批量修改单位方案：将所有归属为 `belong` 的单位方案统一设为 `plan`。

```lua
-- 把归属 2 的所有单位方案设为 1
local r = map.BatchModifyArmyPlanByLegion(2, 1)
print(r.Message)
```

#### `map.RemoveArmyByBelong(belong)`

删除指定归属的所有单位。

```lua
local r = map.RemoveArmyByBelong(5)
print(string.format('删除 %d 个单位', r.AffectedCount))
```

#### `map.RemoveAllArmies()`

删除所有单位（v1 + v3）。

```lua
local before = map.ArmyCount
map.RemoveAllArmies()
print(string.format('清空 %d 个单位', before))
```

#### `map.EraseArmyBrush(col, row, radius)`

橡皮擦批量删除：以 `(col, row)` 为圆心、`radius` 为半径，删除圆形范围内所有单位。

```lua
-- 以 (50, 50) 为中心，半径 5 格内全部清除
map.EraseArmyBrush(50, 50, 5)
```

#### `map.ModifyLegionStrength(belong, level)`

按配置修改军团强度。

| 参数 | 类型 | 说明 |
|---|---|---|
| `belong` | 整数 | 归属值，**`-1` 表示所有归属** |
| `level` | 整数 | 军团等级，**`-1` 表示自动** |

```lua
map.ModifyLegionStrength(-1, -1)   -- 所有归属、自动等级
```

#### `map.RandomizeLegionArmies(belong)`

随机化军团单位。`belong = -1` 表示所有归属。

```lua
map.RandomizeLegionArmies(-1)
```

#### `map.GenerateArmiesByProbability(belong, probability)`

按概率生成单位。

| 参数 | 类型 | 说明 |
|---|---|---|
| `belong` | 整数 | 归属值，`-1` 表示所有归属 |
| `probability` | 整数 | 生成概率，0-100 |

```lua
map.GenerateArmiesByProbability(3, 50)   -- 归属 3，50% 概率
```

#### `map.AutoAssignGenerals(belong, count)`

自动分配将领。

| 参数 | 类型 | 说明 |
|---|---|---|
| `belong` | 整数 | 归属值 |
| `count` | 整数 | 分配数量 |

```lua
map.AutoAssignGenerals(2, 5)   -- 归属 2 分配 5 个将领
```

#### `map.ForEachArmy(callback)`

遍历所有单位（v1 / v3 内部自动分派）。回调参数见 [6.3 单位对象](#63-单位对象scriptarmyinfo)，返回 `false` 可提前结束。

```lua
map.ForEachArmy(function(a)
    if a.Belong == 2 and a.Level < 3 then
        map.SetArmyField(a.Col, a.Row, 'Level', 3)
    end
end)
```

#### `map.GetArmy(col, row)`

取指定格子的单位，没有返回 **`nil`**（坐标越界也是 `nil`）。

#### `map.GetArmyFields()` / `map.SetArmyField(col, row, field, value)`

**按字段名读写单位属性**（大小写不敏感）—— 这是修改单位对象的通用入口：

`UnitType` `Level` `Organization` `Direction` `Mobility` `BuiltRound` `Experience` `HealthBonus`
`CurrentHealth` `MaxHealth` `General` `Rank` `KeyPoint` `Policy` `Plan` `Morale` `CanAttack`
以及 **v3 专有**：`HpLevel` `TransportShip` `Hatred` `AttackTarget` `DeathDialogue` `LevelMarkDisplay` `AntiDefenseMode`

```lua
map.SetArmyField(10, 20, 'Level', 5)        -- 等级改 5
map.SetArmyField(10, 20, 'Morale', 255)     -- 士气下降
```

> ⚠️ v3 专有字段写在 **v1 地图**上会失败并提示"该版本的数据结构没有它"（不是静默忽略）。写之前看 `a.Version`。
> 所属军团不在字段清单里：它就是所在格子的归属，用 `map.SetArmyLegion` / `map.SetBelong` 改。

### 5.5 军团操作

#### `map.SetLegionColor(legionId, r, g, b)`

设置军团颜色，`r`/`g`/`b` 范围 **0-255**。

```lua
map.SetLegionColor(0, 255, 0, 0)   -- 0 号军团设为红色
```

#### `map.SetLegionActionId(legionId, actionId)`

设置军团 ActionId。

```lua
map.SetLegionActionId(0, 1)
```

#### `map.SetPlayerLegion(actionId)`

设置玩家军团（按 ActionId）：该军团的控制值设为 **0（玩家）**，其余军团设为 1（AI）。

```lua
map.SetPlayerLegion(1)
```

#### `map.SetPlayerLegionAt(index)`

同上，但按**列表索引**（= 归属值）指定军团；`index` 为负数表示没有玩家军团（全部 AI，征服地图由开局选择决定玩家）。

```lua
map.SetPlayerLegionAt(0)    -- 0 号军团是玩家
map.SetPlayerLegionAt(-1)   -- 全部 AI
```

#### `map.MoveLegion(from, to, renumberActionIds)`

把第 `from` 个军团移到 `to` 位置（列表索引）。地图上的归属值与各处对军团下标的引用随之改写；
`renumberActionIds` 为 `true` 时 ActionId 也按新顺序重排（默认 `false`）。

```lua
map.MoveLegion(3, 0)          -- 3 号军团挪到最前
map.MoveLegion(3, 0, true)    -- 同时重排 ActionId
```

#### `map.ApplyDefaultLegionColors()`

应用默认颜色到所有军团。

```lua
map.ApplyDefaultLegionColors()
```

#### `map.ApplyLegionColorsFromSettings()`

从配置文件更新所有军团颜色。

```lua
map.ApplyLegionColorsFromSettings()
```

#### `map.RandomizeLegionLevels()`

随机化所有军团等级与经济。

```lua
map.RandomizeLegionLevels()
```

#### `map.RebuildLegionActionBelong()`

重建所有军团 ActionId 与归属映射。

```lua
map.RebuildLegionActionBelong()
```

#### `map.SetAllLegionsEconomy(value)`

批量设置所有军团经济值。

```lua
map.SetAllLegionsEconomy(100)
```

#### `map.SetAllLegionsIndustry(value)`

批量设置所有军团工业值。

```lua
map.SetAllLegionsIndustry(80)
```

#### `map.SetAllLegionsTech(value)`

批量设置所有军团科技值。

```lua
map.SetAllLegionsTech(5)
```

#### `map.AddLegion()`

追加一个默认军团。

```lua
local r = map.AddLegion()
print(string.format('军团总数: %d', map.LegionCount))
```

#### `map.RemoveLegionAt(index)`

按索引删除军团。

```lua
map.RemoveLegionAt(3)   -- 删除索引 3 的军团
```

> ⚠️ 删除军团后索引会重排，倒序删除更安全：
>
> ```lua
> for i = map.LegionCount - 1, 0, -1 do
>     map.RemoveLegionAt(i)
> end
> ```

#### `map.GetLegionAt(index)`

按**列表索引**取军团（索引就是归属值），越界返回 `nil`。字段见 [6.4 军团对象](#64-军团对象scriptlegioninfo)。

```lua
local l = map.GetLegionAt(0)
if l then print(string.format('国家 %d，初始经济 %d', l.CountryId, l.InitialEconomy)) end
```

#### `map.ForEachLegion(callback)`

遍历所有军团，回调返回 `false` 可提前结束。

```lua
map.ForEachLegion(function(l)
    map.SetLegionField(l.Index, 'InitialEconomy', 1200)
end)
```

#### `map.GetLegionFields()` / `map.SetLegionField(index, field, value)`

按字段名改军团属性（按列表索引定位）。清单含：初始经济/工业/科技、玩家控制、阵营、战败条件、国家血率/税率、颜色 RGB、四种核弹数量、初始科技、AI 行为，以及**全部科技等级字段**（`MobilityLevel` … `InitialLaserGunLevel`，几十个）。

```lua
map.SetLegionField(0, 'InitialEconomy', 2000)
map.SetLegionField(0, 'ColorR', 255)
map.SetLegionField(0, 'ArmorLevel', 4)
```

> ⚠️ **`CountryId` 和 `ActionId` 不在白名单里**：这两个是军团定位键，改它们必须用带校验的
> `map.SetLegionCountryIdAt(index, countryId)` / `map.SetLegionActionIdAt(index, actionId)` ——
> 否则会出现"按国家找不到军团"或重复国家 ID（BTL 校验器会报这个错）。

### 5.6 地形修改

#### `map.SetTerrain(col, row, type)`

设置指定格子的地形类型。

```lua
map.SetTerrain(10, 20, 3)   -- 设为地形 3
```

#### `map.ChangeTerrainType(col, row, delta)`

切换地形类型，`delta` 为正数递增、负数递减。

```lua
map.ChangeTerrainType(10, 20, 1)   -- 地形 +1
map.ChangeTerrainType(10, 20, -1)  -- 地形 -1
```

#### `map.ChangeDecoration(col, row, delta)`

切换地形变体（装饰），`delta` 为正数递增、负数递减。

```lua
map.ChangeDecoration(10, 20, 1)
```

#### `map.SetRiver(col, row, value)`

设置河流值，范围 **0-63**。

```lua
map.SetRiver(10, 20, 15)
```

#### `map.CopyTerrain(col, row)`

复制指定格子的地形到内部剪贴板。

```lua
map.CopyTerrain(10, 20)
```

#### `map.PasteTerrain(col, row)`

将剪贴板地形粘贴到指定格子。需先 `CopyTerrain`。

```lua
map.CopyTerrain(10, 20)
map.PasteTerrain(11, 20)   -- 粘贴到右侧
map.PasteTerrain(12, 20)
```

#### `map.FloodFillTerrain(col, row, type)`

洪水填充：从 `(col, row)` 开始，将连通的同地形区域改为 `type`。

```lua
-- 把 (50, 50) 所在的连通水域全改成平原
map.FloodFillTerrain(50, 50, 0)
```

#### `map.Greening(probability)`

绿化平地，`probability` 为 0-100。

```lua
map.Greening(60)   -- 60% 概率绿化
```

#### `map.RandomizeFlatTerrain(probability)`

随机平地变体，`probability` 为 0-100。

```lua
map.RandomizeFlatTerrain(50)
```

#### `map.RandomizeVariant(probability)`

随机当前层变体，`probability` 为 0-100。

```lua
map.RandomizeVariant(50)
```

#### `map.CreateCoast()`

创建海岸线（全局操作，无需坐标）。

```lua
local r = map.CreateCoast()
print(r.Message)
```

#### `map.ProcessOceanSecondLayer()`

处理海洋第二层（全局操作）。

```lua
map.ProcessOceanSecondLayer()
```

#### `map.ExportHd()`

导出 HD 文件（全局操作）。

```lua
local r = map.ExportHd()
if r.Success then print('HD 导出成功') end
```

#### `map.ConnectBuildings()`

连接建筑（A* 寻路，全局操作）。

```lua
local r = map.ConnectBuildings()
print(string.format('连接 %d 条路径', r.AffectedCount))
```

### 5.7 输出

#### `map.Log(message)`

输出一行到控制台。和全局 `log` / `print` 等价，看个人习惯。

```lua
map.Log('处理完毕')
```

### 5.8 记录列表（事件 / 方案 / 天气 / 战略建设 / 空袭 / 空中支援 / 援军）

这七种数据都是"一条条记录的列表"，接口形状完全一致（下表以事件 `Event` 为例）：

| 成员 | 说明 |
|---|---|
| `map.EventCount` | 条数（实时值） |
| `map.GetEvent(i)` | 取第 `i` 条（**从 0 起**），返回字段表（见 [6.5](#65-记录对象)），越界返回 `nil` |
| `map.GetEventFields()` | 可写字段名清单 |
| `map.SetEventField(i, field, value)` | 按字段名改一个字段（大小写不敏感，按字段宽度钳位）；先经对应模块校验，不合法时失败并说明原因 |
| `map.AddEvent()` | 新增一条默认记录 |
| `map.RemoveEvent(i)` / `map.RemoveAllEvents()` | 删除一条 / 全部 |

其余六种把 `Event` 换成对应名字（复数同理）：

| 数据 | 名字 | 新增 | 说明 |
|---|---|---|---|
| 事件 | `Event` | `map.AddEvent()` | 新事件序号 = 最大序号 + 1 |
| 方案（AI 方针） | `Case` | `map.AddCase()` | 方针编号取下一个空闲编号 |
| 天气 | `Weather` | `map.AddWeather()` | 接在现有天气之后，持续 1 回合 |
| 战略建设 | `Strategic` | `map.AddStrategic()` | 第一个军团 + 代码表第一条策略 |
| 空袭 | `AirRaid` | `map.AddAirRaid(col, row)` | 挂在该格上，军团取该格归属 |
| 空中支援 | `AirSupport` | `map.AddAirSupport()` | 没有坐标 |
| 援军 | `Reinforcement` | `map.AddReinforcement(col, row)` | 按地图版本自动用 v1/v2 或 v3 记录；`Coordinate` 不能直接改（要换格子请删除后在新格子重新添加） |

军团类字段（事件的 `TriggerLegion` / `AffectedLegion`、战略建设的 `LegionId`、空袭 / 空中支援的 `OwnerLegion`、援军的 `OwnerCountry` / `OwnerLegion`）
存的都是军团的 **ActionId**，不是归属值。

```lua
-- 把所有 2 回合触发的事件改成 3 回合
for i = 0, map.EventCount - 1 do
    local e = map.GetEvent(i)
    if e.TriggerCondition == 2 and e.TriggerRound == 2 then
        map.SetEventField(i, 'TriggerRound', 3)
    end
end

-- 在 (40, 30) 的机场上加一条空袭并设回合
local r = map.AddAirRaid(40, 30)
if r.Success then map.SetAirRaidField(map.AirRaidCount - 1, 'TriggerRound', 5) end

-- 天气：列出全部
for i = 0, map.WeatherCount - 1 do
    local w = map.GetWeather(i)
    print(i, w.WeatherTypeName, w.TriggerRound, w.Duration)
end
```

> 这些修改与其它接口一样记入整批撤销（`Ctrl+Z` 一次退回整个脚本）。

---

## 六、对象字段参考

### 6.1 建筑对象（`ScriptBuildingInfo`）

`map.GetBuilding(...)` 的返回值和 `ForEachBuilding` 回调的参数都是它。

| 字段 | 类型 | 说明 |
|---|---|---|
| `b.Col` | 整数 | 所在格子的列 |
| `b.Row` | 整数 | 所在格子的行 |
| `b.Index` | 整数 | 线性索引（`row * Width + col`） |
| `b.BuildingType` | 整数 | 建筑类型编号 |
| `b.BuildingTypeName` | 字符串 | 建筑类型的中文名 |
| `b.NameId` | 整数 | 名称 ID（`0xFFFF` = 无名称） |
| `b.Name` | 字符串 | 名称文字（查不到时为空串） |
| `b.HasName` | 布尔 | 是否有名称 |
| `b.Belong` | 整数 | 该格子的归属值（`255` = 无归属） |
| `b.Appearance` | 整数 | 外观值 |
| `b.LandmarkBuilding` | 整数 | 地标建筑 |
| `b.DecorativeBuilding` | 整数 | 装饰建筑 |
| `b.SkillUnlock` | 整数 | 解锁技能 |
| `b.RewardCount` | 整数 | 奖励数量 |
| `b.HatredValue` | 整数 | 仇恨值 |
| `b.KeyPoint` | 整数 | 关键据点（0=否，1=红圈，2=绿圈） |
| `b.KeyPointName` | 字符串 | 关键据点中文名 |
| `b.OccupationEvent` | 整数 | 占领事件 ID |
| `b.FireIgnition` / `b.FireDuration` | 整数 | 起火 / 燃烧持续 |
| `b.AirDefenseWeapon` | 整数 | 防空武器 |
| `b.AirDefenseRadar` | 整数 | 防空雷达（0=否，2=是） |
| `b.AirDefenseRadarName` | 字符串 | 防空雷达中文名 |
| `b.FactoryLevel` | 整数 | 工厂等级（校验器叫"工业"）0-4 |
| `b.ResearchLevel` | 整数 | 研究所等级（校验器叫"科技"）0-4 |
| `b.MedicalLevel` | 整数 | 补给站等级（面板写"医疗"）0-4 |
| `b.AviationLevel` | 整数 | 机场等级（面板写"航空"）0-4 |
| `b.MissileLevel` | 整数 | 导弹等级 0-2 |
| `b.NuclearLevel` | 整数 | 核弹等级 0-2 |

上表里 **`BuildingType` 及之后的字段都能用 `map.SetBuildingField` 按名改写**（清单见 `map.GetBuildingFields()`）。

> ⚠️ **这是只读快照：给它赋值不报错，但也完全不生效。**
>
> ```lua
> b.HasName = true                   -- 不报错，然而地图上什么都没变
> map.SetBelong(b.Col, b.Row, 3)     -- ✓ 这才真正改到数据
> ```
>
> 这种「静默无效」比报错更危险 —— 脚本会一路跑完、输出一切正常，数据却一点没动。**要改地图只能走 `map.*` 方法**，别去改 `b` 的字段。
>
> 另外它是**取用那一刻的快照**。遍历过程中改了归属，已拿到的 `b.Belong` 仍是旧值。

```lua
map.ForEachBuilding(function(b)
    print(string.format('(%d,%d) %s 名称=%s 归属=%d',
        b.Col, b.Row, b.BuildingTypeName, b.Name, b.Belong))
end)
```

### 6.2 操作结果（`ScriptOperationResult`）

所有会改数据的方法都返回它。

| 字段 | 类型 | 说明 |
|---|---|---|
| `r.Success` | 布尔 | 是否成功 |
| `r.Message` | 字符串 | 描述信息（失败时是原因） |
| `r.AffectedCount` | 整数 | 本次影响的条数 |

```lua
local r = map.RemoveBuildingsByProbability(30)
if r.Success then
    print(string.format('删除 %d 个', r.AffectedCount))
else
    print('失败: ' .. r.Message)
end
```

它重写了 `ToString`，但**必须显式 `tostring()` 才能拼进字符串**：

```lua
print('结果: ' .. r)              -- ✗ attempt to concatenate a userdata value
print('结果: ' .. tostring(r))    -- ✓ 等价于 r.Message
print('结果: ' .. r.Message)      -- ✓ 也可以直接取字段
```

---

### 6.3 单位对象（`ScriptArmyInfo`）

`map.GetArmy(...)` 的返回值和 `ForEachArmy` 回调的参数都是它。

| 字段 | 说明 |
|---|---|
| `a.Col` / `a.Row` / `a.Index` | 所在格子 |
| `a.Version` | **1 = v1，3 = v3** |
| `a.UnitType` / `a.UnitTypeName` | 兵种 / 名称（按兵种配置表与字符串表解析，无兵种为"无兵种"） |
| `a.Level` / `a.Organization` | 等级 / 编制 |
| `a.Direction` / `a.DirectionName` | 朝向 |
| `a.Mobility` / `a.BuiltRound` / `a.Experience` | 机动 / 建造回合 / 经验 |
| `a.HealthBonus` / `a.CurrentHealth` / `a.MaxHealth` | 血量（基准 100） |
| `a.General` / `a.Rank` | 将领 ID / 军衔 |
| `a.KeyPoint` / `a.KeyPointName` | 关键据点 |
| `a.Policy` / `a.PolicyName` | 政策（自由行动/猛攻/缓慢进攻/坚守/撤退） |
| `a.Plan` | 方案（0-4） |
| `a.Morale` / `a.MoraleName` | 士气 |
| `a.CanAttack` | 可攻击 |
| `a.Belong` / `a.LegionId` | 所属军团的归属值（255 = 所在格子无归属）：单位的归属就是**所在格子的归属**，两个名字等价（`LegionId` 是旧脚本沿用的名字） |
| `a.HpLevel` `a.TransportShip` `a.Hatred` `a.AttackTarget` `a.DeathDialogue` `a.LevelMarkDisplay` `a.AntiDefenseMode` | **v3 专有**（v1 恒为 0） |

同样是**只读快照**，改属性用 `map.SetArmyField`。

### 6.4 军团对象（`ScriptLegionInfo`）

| 字段 | 说明 |
|---|---|
| `l.Index` | **列表索引 == 归属值** |
| `l.ActionId` / `l.CountryId` | 行动顺序 / 国家 ID |
| `l.InitialEconomy` / `l.InitialIndustry` / `l.InitialTech` | 初始经济 / 工业 / 科技 |
| `l.IsPlayerControlled` / `l.IsPlayer` | 控制值（**0 = 玩家，1 = AI**）/ 是否玩家控制（布尔便捷值） |
| `l.Camp` / `l.DefeatCondition` | 阵营 / 战败条件 |
| `l.CountryHpRate` / `l.CountryTaxRate` | 国家血率 / 税率（浮点） |
| `l.ColorR` / `l.ColorG` / `l.ColorB` | 颜色 |
| `l.AtomicBombCount` … `l.AntimatterBombCount` | 原子弹 / 氢弹 / **三相弹** / 反物质弹 |
| `l.InitialTechLevel` / `l.CountryAiBehavior` | 初始科技等级 / AI 行为 |
| `l.AverageArmyLevel` / `l.AverageNavyLevel` / `l.AverageAirForceLevel` / `l.AverageDefenseLevel` | 陆/海/空/防御平均等级（派生只读值） |

科技等级那几十个字段不在快照里逐个列出 —— 需要时用 `map.SetLegionField` 按名写，读的话直接查 `map.GetLegionFields()` 之外的模型字段（需要的话再补进快照）。

### 6.5 记录对象

`map.GetEvent(i)` 等返回的是一张 Lua 表（只读快照）：键是数据结构里的字段名（数值），再加

- `Index`：记录在列表中的下标；
- 文字标签（只读）：事件的 `TriggerConditionName` / `TriggerEventName`，天气的 `WeatherTypeName`，空袭的 `UnitTypeName` / `AmmoTypeName`，
  空中支援的 `AirForceTypeName` / `AmmoTypeName`，战略建设的 `ConstructionName`。

完整字段清单用 `map.GetXxxFields()` 查；改值请用 `map.SetXxxField`，给快照赋值不生效。

## 七、重要语义与坑

这一节是最容易踩的地方，建议完整读一遍。

### 7.1 撤销是整批的

**每执行一次 `lua` 命令，整次执行被包成一个撤销单元**（快照所有实体列表 + 省份 + 归属）。不管脚本改了多少格、执行了多少步，`Ctrl+Z` 都是**一次性**退回脚本执行前的状态。

这意味着：

- ✅ 脚本跑完发现不对，`Ctrl+Z` 就能全身而退，不用手动还原
- ❌ **脚本内部的每一步不能单独撤销** —— 不能指望撤销到「脚本执行到一半」的状态

所以：**跑之前先存盘**（或者至少心里有数，大改动前手动备份一份）。撤销栈不是无限的。

### 7.2 坐标越界不报错，返回「空值」

| 方法 | 越界时返回 |
|---|---|
| `map.GetBuilding` | `nil` |
| `map.GetBelong` | `255` |
| `map.GetProvince` | `65535` |
| `map.GetTerrain` | `-1` |
| `map.AddBuilding` / `RemoveBuilding` / `SetBelong` | 失败的结果对象（`Success = false`） |
| `map.AddArmy` / `RemoveArmy` / `SetArmyLegion` / `SetArmyPlan` / `CycleArmyPlan` / `EraseArmyBrush` | 失败的结果对象（`Success = false`） |
| `map.SetTerrain` / `ChangeTerrainType` / `ChangeDecoration` / `SetRiver` / `CopyTerrain` / `PasteTerrain` / `FloodFillTerrain` | 失败的结果对象（`Success = false`） |

**查询类静默返回空值，修改类明确失败**。这是有意区分的：查询时越界往往是正常情况（比如探测边缘），不该中断脚本。

判断时要注意区分「越界」和「真的是这个值」：

```lua
-- 这样写分不清"越界"和"无归属"，但通常按同样方式处理即可
local belong = map.GetBelong(col, row)
if belong == 255 then
    -- 无归属，或坐标越界
end
```

### 7.3 回调返回 `false` 才中断

`ForEachBuilding` 里，**只有显式返回 `false`** 才提前结束。返回 `nil`、不返回、返回其它值都视为继续：

```lua
map.ForEachBuilding(function(b)
    if b.Row > 100 then
        return false        -- ✓ 中断
    end
    -- 不写 return，或 return nil，都是继续
end)
```

> 注意 `return 0` 或 `return ""` 也**不会**中断 —— 只认布尔 `false`。

### 7.4 失败会自动打印，成功不会

会改数据的方法（`AddBuilding` / `SetBelong` / `RemoveBuildingsByProbability` …）内部统一处理输出：

- **失败** → 自动打印 `[脚本] <失败原因>`
- **成功** → **不打印**（只累加影响条数，最后在摘要里体现）

所以脚本没输出不代表没执行，看最后的 `影响 N 条数据`。反过来，失败信息你**不用自己检查也能看见**：

```
[脚本] 设置归属失败：(30,40) 不在归属数据范围内
[Lua] 执行完成：3 行输出，影响 12 条数据，45 ms
```

不过要**据此中断脚本**，还是得自己判断 `r.Success`。

### 7.5 每次执行都是全新环境

引擎每次执行都**新建一个 `Script` 实例**。所以：

- 上一次脚本定义的全局变量、函数，下一次**不存在**
- 每次都是从零开始的环境

脚本之间要共享逻辑，就用 `do...end` 包起来写在同一个文件里，或者复制粘贴。

### 7.6 `map.BuildingCount` 实时、建筑对象是快照

```lua
local before = map.BuildingCount          -- 实时读
map.RemoveBuildingsByProbability(50)
local after = map.BuildingCount           -- 删完后再读，是新的
print('删了 ' .. (before - after))
```

但建筑对象（`b`）是**取用时生成的副本**，改不了地图也不会自动更新。

### 7.7 性能

遍历是 O(n)，`map.GetBuilding` 是 O(n) 线性查找（**不是**哈希索引）。

```lua
-- ✗ 慢：外层遍历 n 个建筑，每个再线性查一次，整体 O(n²)
map.ForEachBuilding(function(b)
    local other = map.GetBuilding(b.Col + 1, b.Row)   -- 每次 O(n)
end)

-- ✓ 快：一次遍历把位置收进 table，之后查表是 O(1)
local byPos = {}
map.ForEachBuilding(function(b)
    byPos[b.Row .. ',' .. b.Col] = true
end)

-- 想判断"某格有没有建筑"，查表即可，不必再调 GetBuilding
map.ForEachBuilding(function(b)
    if byPos[b.Row .. ',' .. (b.Col + 1)] then
        -- 右侧相邻格也有建筑
    end
end)
```

几万个格子以内，直接写就好，不用过度优化。真要处理大地图的复杂逻辑，优先考虑**一次遍历收集数据，再统一处理**。

### 7.8 出错会给出位置

```lua
print('hello'    -- 少个右括号
```

控制台输出：

```
[Lua] 执行失败 @ my-script.lua: my-script.lua:(1,13): ')' expected (to close '(' at line 1) near '<eof>'
```

几个要点：

- `(1,13)` 是**行列号** —— 第 1 行第 13 列，对着改即可
- 行号是**相对脚本文件**的（不含任何包装代码），所见即所得
- 多行脚本也准：`multi.lua:(2,10-11): unexpected symbol near '='`
- 运行时错误同样带位置：`runtime.lua:(2,5-12): attempt to index a nil value`
- 消息里文件名会出现两次（`@ x.lua:` 一次、错误详情里又一次），不影响定位
- 运行时错误在消息下方还会给出**调用栈**（最多 8 层），能看出是哪一层函数调用的

---

## 八、完整示例

### 8.1 统计报表

```lua
-- 统计各类建筑数量 + 有归属/无归属
local typeCount, belongCount = {}, { has = 0, none = 0 }

map.ForEachBuilding(function(b)
    typeCount[b.BuildingTypeName] = (typeCount[b.BuildingTypeName] or 0) + 1

    if b.Belong == 255 then
        belongCount.none = belongCount.none + 1
    else
        belongCount.has = belongCount.has + 1
    end
end)

print('=== 建筑类型分布 ===')
for name, count in pairs(typeCount) do
    print(string.format('  %-10s %d', name, count))
end

print(string.format('=== 归属情况：有归属 %d，无归属 %d ===',
    belongCount.has, belongCount.none))
```

### 8.2 按概率删除建筑

`scripts/remove-buildings-30.lua`：

```lua
local PROBABILITY = 30          -- 删除概率
local KEEP_NAMED  = false       -- true = 保留城市/据点

local before = map.BuildingCount
print(string.format('删除前建筑数: %d', before))

local result = map.RemoveBuildingsByProbability(PROBABILITY, KEEP_NAMED)

if not result.Success then
    print('执行失败: ' .. result.Message)
    return
end

print(result.Message)
print(string.format('删除后建筑数: %d（实际删除 %d 个，占 %.1f%%）',
    map.BuildingCount,
    before - map.BuildingCount,
    before > 0 and (before - map.BuildingCount) * 100.0 / before or 0))
```

### 8.3 条件处理：给孤立工厂上归属

「找出周围 6 格内没有任何建筑的工厂，把它们的归属设成 5」：

```lua
-- 第一步：收集所有建筑位置
local occupied = {}
map.ForEachBuilding(function(b)
    occupied[b.Row .. ',' .. b.Col] = true
end)

-- 第二步：找出孤立工厂
local targets = {}
map.ForEachBuilding(function(b)
    if b.BuildingType ~= 7 then return end       -- 只要工厂

    -- 检查四邻（简化版，八个方向可自行扩展）
    local neighbors = {
        { b.Col + 1, b.Row }, { b.Col - 1, b.Row },
        { b.Col, b.Row + 1 }, { b.Col, b.Row - 1 },
    }

    local isolated = true
    for _, pos in ipairs(neighbors) do
        if occupied[pos[2] .. ',' .. pos[1]] then
            isolated = false
            break
        end
    end

    if isolated then targets[#targets + 1] = b end
end)

print(string.format('找到 %d 个孤立工厂', #targets))

-- 第三步：统一处理
local ok, fail = 0, 0
for _, b in ipairs(targets) do
    local r = map.SetBelong(b.Col, b.Row, 5)
    if r.Success then ok = ok + 1 else fail = fail + 1 end
end

print(string.format('设置完成：成功 %d，失败 %d', ok, fail))
```

### 8.4 逐格扫描地形

```lua
-- 统计地图上各类地形出现次数（隔行采样以提速）
local terrainCount = {}
local step = 2

for row = 0, map.Height - 1, step do
    for col = 0, map.Width - 1, step do
        local t = map.GetTerrain(col, row)
        terrainCount[t] = (terrainCount[t] or 0) + 1
    end
end

for t, count in pairs(terrainCount) do
    print(string.format('  地形 %d: %d 格', t, count))
end
```

### 8.5 进度提示

长循环里打印进度，免得以为卡死：

```lua
local total = map.BuildingCount
local done = 0

map.ForEachBuilding(function(b)
    done = done + 1
    if done % 500 == 0 then
        print(string.format('进度 %d / %d', done, total))
    end
end)
```

> 输出是脚本结束后才显示的，所以这只适合确认「跑到了哪一步」（失败时能看到最后一行进度），不是实时进度条。

### 8.6 清空并重建单位

```lua
-- 清空所有单位，然后沿地图中线每隔 5 格放一个
print(string.format('清空前单位数: %d', map.ArmyCount))

map.RemoveAllArmies()

local midRow = math.floor(map.Height / 2)
local placed = 0
for col = 0, map.Width - 1, 5 do
    local r = map.AddArmy(col, midRow)
    if r.Success then placed = placed + 1 end
end

print(string.format('清空后 %d，沿线放置 %d', map.ArmyCount, placed))
```

### 8.7 批量设置军团属性

```lua
-- 给所有军团统一经济/工业/科技，再随机化等级
print(string.format('军团总数: %d', map.LegionCount))

map.SetAllLegionsEconomy(100)
map.SetAllLegionsIndustry(80)
map.SetAllLegionsTech(3)
map.RandomizeLegionLevels()

-- 给 0 号军团设个醒目的颜色
map.SetLegionColor(0, 255, 80, 0)

print('军团属性已统一设置')
```

### 8.8 地形批量处理

```lua
-- 把全图所有水域（地形 1）改成草地（地形 2），逐格扫描
local changed = 0
for row = 0, map.Height - 1 do
    for col = 0, map.Width - 1 do
        if map.GetTerrain(col, row) == 1 then
            local r = map.SetTerrain(col, row, 2)
            if r.Success then changed = changed + 1 end
        end
    end
end
print(string.format('改造 %d 格水域为草地', changed))

-- 然后绿化 60%
map.Greening(60)

-- 最后创建海岸线
map.CreateCoast()
print('地形处理完成')
```

### 8.9 综合：按归属整理战区

```lua
-- 对每个归属：删除旧单位 → 按概率生成新单位 → 批量设方案
local BELONGS = { 0, 1, 2, 3 }    -- 要处理的归属列表
local PLAN    = 1                  -- 统一方案
local PROB    = 40                 -- 生成概率

for _, belong in ipairs(BELONGS) do
    map.RemoveArmyByBelong(belong)
    map.GenerateArmiesByProbability(belong, PROB)
    map.BatchModifyArmyPlanByLegion(belong, PLAN)
end

-- 自动分配将领（每个归属 3 个）
for _, belong in ipairs(BELONGS) do
    map.AutoAssignGenerals(belong, 3)
end

print(string.format('战区整理完成，当前单位 %d 个', map.ArmyCount))
```

### 8.10 复制地形区域

```lua
-- 从 (10,10) 复制地形，粘贴到 (20,10) 开始的 5x5 区域
local SRC_COL, SRC_ROW = 10, 10
local DST_COL, DST_ROW = 20, 10
local SIZE = 5

for dy = 0, SIZE - 1 do
    for dx = 0, SIZE - 1 do
        map.CopyTerrain(SRC_COL + dx, SRC_ROW + dy)
        map.PasteTerrain(DST_COL + dx, DST_ROW + dy)
    end
end

print(string.format('已将 %dx%d 区域从 (%d,%d) 复制到 (%d,%d)',
    SIZE, SIZE, SRC_COL, SRC_ROW, DST_COL, DST_ROW))
```

---

## 九、如何扩展新接口（开发者）

脚本能调用的能力完全由 `IMapScriptApi` 决定。**加一个新能力只需三步**，不用设计任何命令语法：

**① 在接口里加方法** — `src/WC4MapEditor.Scripting/IMapScriptApi.cs`

```csharp
/// <summary>取指定格子的陷阱数量</summary>
int GetTrapCount(int col, int row);
```

**② 在 Lua 包装层暴露它** — `src/WC4MapEditor.Scripting/LuaMapApi.cs`

```csharp
public int GetTrapCount(int col, int row) => _api.GetTrapCount(col, row);
```

**③ 在 Core 实现它** — `src/WC4MapEditor.Core/Scripting/MapScriptApi.cs`

```csharp
public int GetTrapCount(int col, int row)
    => IsValidCoord(col, row) ? _mapData.GetTrapCountAt(col, row) : 0;
```

编译后脚本里立刻可用：`map.GetTrapCount(10, 20)`。

### 设计约定

动手加接口时，请遵守现有约定：

| 约定 | 说明 |
|---|---|
| **转发到 Modifier** | 不要直接改 `mapData`，要走 `BuildingModifier` / `BelongModifier` 等，以复用校验、索引维护和 `MarkModified()` 通知 |
| **查询返回空值，修改返回结果** | 查询越界返回 `nil`/哨兵值；修改返回 `ScriptOperationResult` |
| **修改类要填 `AffectedCount`** | 它会被累加进执行摘要的「影响 N 条数据」 |
| **遍历用快照** | 参考 `ForEachBuilding`，遍历副本以防回调里增删 |
| **依赖方向** | 接口和 Lua 包装在 `WC4MapEditor.Scripting`（不引用 Core），实现在 Core —— 保持 `Core → Scripting` 单向依赖 |

### 关键文件

| 文件 | 职责 |
|---|---|
| `src/WC4MapEditor.Scripting/IMapScriptApi.cs` | 接口定义（能力的唯一来源） |
| `src/WC4MapEditor.Scripting/LuaMapApi.cs` | Lua 侧 `map` 对象，薄包装 + 输出/统计 |
| `src/WC4MapEditor.Scripting/LuaScriptEngine.cs` | MoonSharp 引擎、`print` 接管、错误捕获 |
| `src/WC4MapEditor.Scripting/ScriptBuildingInfo.cs` | 建筑快照对象 |
| `src/WC4MapEditor.Scripting/ScriptOperationResult.cs` | 操作结果对象 |
| `src/WC4MapEditor.Scripting/ScriptResult.cs` | 一次执行的整体结果 |
| `src/WC4MapEditor.Core/Scripting/MapScriptApi.cs` | 接口的 Core 实现（转发到 Modifier） |
| `src/WC4MapEditor.Core/Scripting/ScriptRunner.cs` | 执行入口，负责包撤销单元 |
| `Views/RenderSceneBase.cs` → `RegisterScriptCliCommands` | `lua` 命令注册 |
| `Views/Assist/DebugConsoleWindow.cs` | 拖放 `.lua` 的处理 |

### 新增 C# 类型要记得注册

要让 MoonSharp 能读写某个 C# 类型的成员，必须在 `LuaScriptEngine` 的静态构造里显式注册：

```csharp
static LuaScriptEngine()
{
    UserData.RegisterType<LuaMapApi>();
    UserData.RegisterType<ScriptBuildingInfo>();
    UserData.RegisterType<ScriptOperationResult>();
}
```

> 光加 `[MoonSharpUserData]` 特性**不够**（需要 `RegisterAssembly` 才会被扫描到）。漏注册的话，赋值到 `Globals` 时会抛 `cannot convert clr type ...`。

---

## 十、故障排查

| 现象 | 原因 / 处理 |
|---|---|
| `找不到脚本文件: xxx` | 相对路径基于**程序目录**，不是当前目录。用 `lua scripts/xxx.lua` 或写绝对路径 |
| `[Lua] 执行失败 @ xxx.lua: ...` | 语法/运行时错误，消息里带 `(行,列)`，对着改 |
| 脚本没任何输出 | 检查是否漏了 `print`。成功的数据操作**不打印**，看摘要的「影响 N 条数据」 |
| `设置归属失败：... 不在归属数据范围内` | 该格不在归属数据覆盖内（征服地图的裁剪区外），或该地图根本没有归属数据。属正常，不是 bug |
| 改了数据但画面没变 | 命令执行完会自动刷新并重建索引。若仍不对，检查是不是设到了裁剪区外 |
| `attempt to index a nil value` | 对 `nil` 取字段。常见于 `map.GetBuilding` 返回 `nil` 时直接访问 `b.Col`，先判断 `if b then` |
| `attempt to call a nil value` | 调了不存在的接口。核对 [第五节](#五map-接口完整参考) 的成员名（**区分大小写**，是 `map.BuildingCount` 不是 `map.buildingCount`） |
| `attempt to concatenate a userdata value` | 用 `..` 拼了 C# 对象。改用 `tostring(r)` 或 `r.Message` |
| `attempt to concatenate a boolean value` | 用 `..` 拼了布尔值。改用 `tostring(r.Success)` |
| 脚本输出正常、没报错，但数据没变 | 多半是给只读快照 `b` 赋了值（静默无效）。改数据必须调 `map.*` 方法，见 [6.1](#61-建筑对象-scriptbuildinginfo) |
| `attempt to perform arithmetic on a nil value` | 对 `nil` 做算术。`os.clock()` 已修复为返回数字；仍报错说明变量本身没取到值 |
| `cannot convert clr type ...` | MoonSharp 类型没注册。见 [9.3](#新增-c-类型要记得注册) |
| 撤销撤不回脚本的改动 | 脚本执行是包在撤销单元里的，`Ctrl+Z` 应能整体退回。若不行，检查是否在批处理模式下执行 |
| 循环里改集合报「集合已修改」 | `ForEachBuilding` 内部已用快照，不会出现。若自己用别的遍历方式，需自行处理 |
| `map.ArmyCount` 为 0 但画面上有单位 | 地图可能是 v3 格式，确认 `AddArmy` 返回的 `r.Success`。v1/v3 由接口内自动分发，无需手动判断 |
| `map.SetLegionColor` 颜色没变 | 检查 `legionId` 是否有效（0 到 `LegionCount - 1`）。越界返回失败结果但不中断脚本 |
| `map.RemoveLegionAt` 后索引错乱 | 删除后索引重排，倒序删除：`for i = map.LegionCount - 1, 0, -1 do map.RemoveLegionAt(i) end` |
| `map.PasteTerrain` 没效果 | 需先调 `map.CopyTerrain` 复制源格子到剪贴板 |
| `map.FloodFillTerrain` 填充范围太大 | 洪水填充遍历连通区域，大地图上可能耗时较长，属正常 |

---

## 附：相关文件

| 路径 | 说明 |
|---|---|
| `scripts/lua-example.lua` | 各接口的用法示例，随程序输出 |
| `scripts/pi.lua` | 纯计算示例（不碰地图） |
| `scripts/remove-buildings-30.lua` | 按 30% 概率删除建筑 |

`scripts/` 目录下的文件会随程序一起输出到运行目录（见 `WC4MapEditor.csproj` 的 `CopyToOutputDirectory` 配置），所以自己写的脚本也可以放在这里，跟着程序一起分发。
