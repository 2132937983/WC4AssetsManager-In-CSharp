# RenderCommand 详解

## 概述

`RenderCommand` 是渲染控制命令类，用于在控制台中控制渲染场景的各种选项，包括渲染层开关、地形编辑、图形绘制等功能。

**命令名称**: `render`

**基本语法**: `render <option> [value]`

---

## 命令列表

### 1. terrains - 控制陆地地形纹理渲染

**语法**: `render terrains <true|false>`

**功能**: 启用或禁用陆地地形纹理的渲染。

- **true** - 启用渲染（会重新加载资源）
- **false** - 禁用渲染（释放资源并显示释放的内存量）

**示例**:
```
render terrains true
render terrains false
```

**输出示例**:
```
陆地地形渲染已启用（资源已重新加载，内存变化: +12.50 MB）
陆地地形渲染已禁用（资源已释放，释放了 12.50 MB 内存）
```

---

### 2. background - 控制背景层渲染

**语法**: `render background <true|false>`

**功能**: 启用或禁用背景层的渲染。

- **true** - 启用渲染
- **false** - 禁用渲染

**示例**:
```
render background true
render background false
```

---

### 3. province - 控制省份层渲染

**语法**: `render province <true|false>`

**功能**: 启用或禁用省份边界的渲染显示。

**示例**:
```
render province true
render province false
```

---

### 4. provincecapital - 控制省会标记渲染

**语法**: `render provincecapital <true|false>`

**功能**: 启用或禁用省会标记的渲染显示。

**示例**:
```
render provincecapital true
render provincecapital false
```

---

### 5. building - 控制建筑层渲染

**语法**: `render building <true|false>`

**功能**: 启用或禁用建筑的渲染显示。

**示例**:
```
render building true
render building false
```

---

### 6. belongflag - 控制归属国旗渲染

**语法**: `render belongflag <true|false>`

**功能**: 启用或禁用归属国旗的渲染显示。

**示例**:
```
render belongflag true
render belongflag false
```

---

### 7. legiondomain - 控制军团领域渲染

**语法**: `render legiondomain <true|false|tile|calculate>`

**功能**: 控制军团领域的渲染模式和性能优化策略。

| 参数值 | 功能说明 |
|--------|----------|
| `true` | 启用军团领域渲染 |
| `false` | 禁用军团领域渲染 |
| `tile` | 瓦片缓存模式（高性能，适合不频繁修改领域） |
| `calculate` | 实时计算模式（适合频繁修改领域） |

**示例**:
```
render legiondomain true
render legiondomain tile
render legiondomain calculate
render legiondomain false
```

---

### 8. tilecache - 启用/禁用瓦片缓存

**语法**: `render tilecache <true|false>`

**功能**: 控制瓦片缓存的启用状态（新渲染器中此设置无效）。

**注意**: 新版本渲染器不需要此设置。

**示例**:
```
render tilecache true
render tilecache false
```

---

### 9. cleartiles - 清除瓦片缓存

**语法**: `render cleartiles`

**功能**: 清除当前缓存的瓦片数据。

**注意**: 新版本渲染器不需要此操作。

**示例**:
```
render cleartiles
```

---

### 10. fps - 显示/隐藏帧率

**语法**: `render fps`

**功能**: 切换帧率（FPS）的显示状态。每次执行会切换显示/隐藏。

**示例**:
```
render fps
```

**输出示例**:
```
FPS 显示已启用
FPS 显示已禁用
```

---

### 11. cameralocation - 移动视角位置

**语法**: `render cameralocation <hex_index>`

**功能**: 将视角中心移动到指定的格子序号位置。

**参数**:
- `hex_index` - 格子序号（整数）

**示例**:
```
render cameralocation 100
render cameralocation 0
```

---

### 12. terraindata - 修改指定格子的地形数据

**语法**:

#### 方式1: 字节位置格式
```
render terraindata <hex_index> {byte_pos,value}
```

#### 方式2: 字段名格式
```
render terraindata <hex_index> <field_name> <value>
```

**参数**:
- `hex_index` - 格子序号
- `byte_pos` - 字节位置 (0-15)
- `value` - 字节值（支持十进制如 `25` 或十六进制如 `0x19`）
- `field_name` - 字段名（见下方列表）

**可用字段名**:
| 字段名 | 字节位置 | 说明 |
|--------|----------|------|
| `TileType1` | 0x00 | 地块类型1 |
| `DecorationType1` | 0x01 | 装饰种类1 |
| `TextureOffsetX1` | 0x02 | 贴图偏移X1 |
| `TextureOffsetY1` | 0x03 | 贴图偏移Y1 |
| `TileType2` | 0x04 | 地块类型2 |
| `DecorationType2` | 0x05 | 装饰种类2 |
| `TextureOffsetX2` | 0x06 | 贴图偏移X2 |
| `TextureOffsetY2` | 0x07 | 贴图偏移Y2 |
| `TileType3` | 0x08 | 地块类型3 |
| `DecorationType3` | 0x09 | 装饰种类3 |
| `TextureOffsetX3` | 0x0A | 贴图偏移X3 |
| `TextureOffsetY3` | 0x0B | 贴图偏移Y3 |
| `RiverValue` | 0x0E | 河流值 |

**示例**:
```
# 方式1: 将格子100的第4字节设为63
render terraindata 100 {4,63}

# 方式2: 将格子100的TileType1设为1（海洋）
render terraindata 100 TileType1 1

# 使用十六进制值
render terraindata 100 {0,0xFF}
```

---

### 13. belongdata - 修改指定格子的归属值

**语法**: 
```
render belongdata <coord|{x,y}> <belong_value>
```

**功能**: 修改指定格子的归属值（国家/势力归属）。

#### 指定格子的方式（2种）

| 格式 | 说明 | 示例 |
|------|------|------|
| **格子索引** (单数值) | 输入格子序号 | `render belongdata 1662 5` |
| **列行坐标** ({x,y}) | 使用花括号包裹的(列,行)坐标 | `render belongdata {12,33} 10` |

**归属值说明**:
- `0-127`: 有效的归属ID（对应不同国家/势力）
- `255` (或 `0xFF`): 无归属/空白地
- 支持十进制或十六进制格式

**示例**:
```
# 使用格子索引
render belongdata 1662 5        # 将索引1662处的格子归属设为5
render belongdata 100 0xFF      # 将索引100处的格子设为无归属

# 使用坐标
render belongdata {12,33} 10    # 将(列12,行33)的格子归属设为10
render belongdata {0,0} 255     # 将(0,0)的格子设为无归属
```

**输出示例**:
```
坐标模式: (12, 33) -> 索引 1662
✅ 归属值修改成功!
   格子索引: 1662
   旧值: &HFF
   新值: &H0A (10)
```

---

### 14. buildingdata - 编辑或创建建筑数据

**语法**: 
```
render buildingdata <index|coord|{x,y}> <field_name> <value>
render buildingdata <index|coord|{x,y}> {byte_pos,value}
```

**功能**: 修改指定建筑的数据字段，或创建新建筑。**支持2种方式指定建筑位置。**

#### 指定建筑的方式（2种）

| 格式 | 说明 | 示例 |
|------|------|------|
| **格子索引** (单数值) | 输入格子序号，自动查找该位置的建筑 | `render buildingdata 1662 BuildingType 1` |
| **列行坐标** ({x,y}) | 使用花括号包裹的(列,行)坐标 | `render buildingdata {12,33} BuildingType 11` |

**智能识别规则**:
- 输入以 `{` 开头且以 `}` 结尾 → **坐标模式** `{x,y}`，自动转换为格子索引
- 单数值 → **格子索引模式**，查找或创建该位置的建筑

**参数**:
- **coord/{x,y}** - 格子索引或 {列,行} 坐标
- **byte_pos** - 字节位置 (0-31)
- **value** - 要填入的值（支持十进制、十六进制和带双引号的字符串）
- **field_name** - 字段名（见下表）

**支持的字段名**:

| 字段名 | 字节位置 | 说明 | 类型 |
|--------|----------|------|------|
| `Coordinate` | 0x00-0x01 | 坐标 (2字节) | Int16 |
| `Name` | 0x02-0x03 | 名称 (2字节) | Int16 |
| `BuildingType` | 0x04 | 建筑类型 | Byte |
| `Appearance` | 0x05 | 外观 | Byte |
| `LandmarkBuilding` | 0x06 | 地标建筑 | Byte |
| `DecorativeBuilding` | 0x07 | 装饰建筑 | Byte |
| `SkillUnlock` | 0x08 | 技能解锁 | Byte |
| `RewardCount` | 0x09 | 奖励数量 | Byte |
| `HatredValue` | 0x0C | 仇恨值 [1-127正,128-255负] | Byte |
| `KeyPoint` | 0x0D | 关键据点 [00否,01红圈,02绿圈] | Byte |
| `OccupationEvent` | 0x0E | 占领触发事件 | Byte |
| `FireIgnition` | 0x14 | 火焰燃起 | Byte |
| `FireDuration` | 0x15 | 火焰持续回合 | Byte |
| `AirDefenseWeapon` | 0x16 | 防空武器 | Byte |
| `AirDefenseRadar` | 0x17 | 防空雷达 | Byte |
| `FactoryLevel` | 0x18 | 工厂等级 | Byte |
| `ResearchLevel` | 0x19 | 科研所等级 | Byte |
| `MedicalLevel` | 0x1A | 医疗等级 | Byte |
| `AviationLevel` | 0x1B | 航空等级 | Byte |
| `MissileLevel` | 0x1C | 导弹等级 | Byte |
| `NuclearLevel` | 0x1D | 核弹等级 | Byte |

**BuildingType 可选值**:
| 值 | 类型名称 |
|----|----------|
| 0 | 无 |
| 1 | 城市 |
| 2 | 港口 |
| 3 | 关口 |
| 4 | 要塞 |
| 5 | 小镇 |
| 6 | 村庄 |
| 7 | 工厂 |
| 8 | 研究所 |
| 9 | 补给站 |
| 10 | 机场 |
| 11-15 | 一级城~五级城 |
| 21 | 灯塔 |
| 22 | 哨塔 |
| 23 | 要塞2 |
| 31-34 | 海上建筑 |

**示例**:
```
# ========== 1. 格子索引模式 ==========
# 修改索引1662处的建筑类型
render buildingdata 1662 BuildingType 1

# 索引50处没有建筑 → 自动创建新建筑
render buildingdata 50 BuildingType 2

# 使用字节位置修改
render buildingdata 1662 {4,7}


# ========== 2. {x,y} 坐标模式 ==========
# 在坐标(列12,行33)创建类型11的城市（一级城）
render buildingdata {12,33} BuildingType 11

# 设置名称为"巴黎"（支持中文）
render buildingdata {12,33} Name "巴黎"

# 设置关键据点为红圈
render buildingdata {12,33} KeyPoint 1
```

**输出示例**:
```
# 格子索引模式 - 找到已有建筑
格子索引模式: 使用索引 1662
找到位置 1662 处的建筑: 索引 #0, 类型: 城市
已修改建筑 0 的 BuildingType 为 1

# 坐标模式 - 创建新建筑
坐标模式: (12, 33) -> 索引 1662
找到位置 1662 处的建筑: 索引 #0, 类型: 城市
已修改建筑 0 的 Name 为 "巴黎"

# 坐标模式 - 创建新建筑
坐标模式: (5, 10) -> 索引 505
位置 505 处没有建筑，将创建新建筑 (索引: 3)
已修改建筑 3 的 BuildingType 为 2
```
已修改建筑 2 的 KeyPoint 为 1

# 坐标模式 - 创建新建筑
坐标 50 处没有建筑，将创建新建筑 (索引: 5)
已修改建筑 5 的 BuildingType 为 2
```

**注意**:
- **智能识别**: 输入值在序号范围内时按序号处理，超出范围时按坐标处理
- **自动创建**: 如果坐标处没有建筑且坐标有效，会自动创建新建筑
- **保留字段**: Reserved 字段无法修改，会提示错误
- **Coordinate 和 Name**: 是2字节整数 (Int16)，其他字段是单字节 (Byte)

---

### 15. getinfo - 导出地形或建筑数据

**语法**: 
```
render getinfo terrains <field_name>
render getinfo buildingdata <field_name>
```

**功能**: 导出指定类型的数据到文本文件（会弹出保存对话框）。

#### terrains - 导出地形数据

**可用字段名**:
| 字段名 | 说明 |
|--------|------|
| `TileType1` | 第一层地块类型 |
| `TileType2` | 第二层地块类型 |
| `TileType3` | 第三层地块类型 |
| `DecorationType1` | 第一层装饰种类 |
| `DecorationType2` | 第二层装饰种类 |
| `DecorationType3` | 第三层装饰种类 |
| `TextureOffsetX1` | 第一层贴图偏移X |
| `TextureOffsetY1` | 第一层贴图偏移Y |
| `TextureOffsetX2` | 第二层贴图偏移X |
| `TextureOffsetY2` | 第二层贴图偏移Y |
| `TextureOffsetX3` | 第三层贴图偏移X |
| `TextureOffsetY3` | 第三层贴图偏移Y |
| `RiverValue` | 河流值 |

**示例**:
```
render getinfo terrains TileType1      # 导出所有格子的第一层地形类型
render getinfo terrains RiverValue     # 导出河流值
```

#### buildingdata - 导出建筑数据

**可用字段名**:
| 字段名 | 说明 |
|--------|------|
| `Coordinate` | 建筑坐标 |
| `Name` | 城市名称（从stringtable解析） |
| `BuildingType` | 建筑类型 |
| `Appearance` | 外观值 |
| `LandmarkBuilding` | 地标建筑 |
| `DecorativeBuilding` | 装饰建筑 |
| `SkillUnlock` | 技能解锁 |
| `RewardCount` | 奖励数量 |
| `HatredValue` | 仇恨值 |
| `KeyPoint` | 关键据点 |
| `OccupationEvent` | 占领触发事件 |
| `FireIgnition` | 火焰燃起 |
| `FireDuration` | 火焰持续回合 |
| `AirDefenseWeapon` | 防空武器 |
| `AirDefenseRadar` | 防空雷达 |
| `FactoryLevel` | 工厂等级 |
| `ResearchLevel` | 科研所等级 |
| `MedicalLevel` | 医疗等级 |
| `AviationLevel` | 航空等级 |
| `MissileLevel` | 导弹等级 |
| `NuclearLevel` | 核弹等级 |

**示例**:
```
render getinfo buildingdata Name            # 导出所有建筑的坐标和名称
render getinfo buildingdata BuildingType    # 导出所有建筑的坐标和类型
render getinfo buildingdata KeyPoint        # 导出所有建筑的坐标和关键据点值
render getinfo buildingdata FactoryLevel    # 导出所有建筑的坐标和工厂等级
```

**输出格式**:
```
格子坐标: 值 (x,y)
==================
1662: 徐州 (12,33)
2000: 南京 (20,40)
3000: 上海 (30,50)
```

---

### 16. window - 切换窗口模式

**语法**: `render window <wpf|winform>`

**功能**: 切换到指定版本的测试渲染场景。

**参数**:
- `wpf` - WPF 版本渲染场景
- `winform` - WinForms 版本渲染场景

**示例**:
```
render window wpf
render window winform
```

---

### 17. draw - 绘制图形

**语法**: `render draw <shape> at {params} <land|sea>`

**功能**: 在地图上绘制几何图形，将指定区域的格子设置为平地或海洋。

**支持的图形类型**:

| 图形名称 | 参数格式 | 说明 |
|----------|----------|------|
| `line` | `{x1,y1,x2,y2}` | 直线（两点之间） |
| `hcircle` | `{centerX,centerY,radius}` | 圆环（只画边界） |
| `circle` | `{centerX,centerY,radius}` | 圆面（填充内部） |
| `hsquare` | `{x1,y1,x2,y2}` | 矩形环（只画边框） |
| `square` | `{x1,y1,x2,y2}` | 矩形面（填充内部） |

**地形类型**:
- `land` - 平地（TileType1=0）
- `sea` - 海洋（TileType1=1）

**示例**:
```
# 从(0,0)到(10,10)画直线，设为平地
render draw line at {0,0,10,10} land

# 以(5,5)为圆心，半径3画圆面，设为海洋
render draw circle at {5,5,3} sea

# 画矩形区域，设为平地
render draw square at {0,0,20,15} land

# 画矩形边框，设为海洋
render draw hsquare at {10,10,30,30} sea

# 画圆环
render draw hcircle at {15,15,8} land
```

**算法说明**:
- **直线**: 使用 Bresenham 算法
- **圆形/圆环**: 使用 Bresenham 画圆算法（圆环只取边界点）
- **矩形**: 填充或边框模式

---

## 快速参考表

| 命令 | 参数 | 功能 |
|------|------|------|
| `terrains` | `<true\|false>` | 地形纹理渲染开关 |
| `background` | `<true\|false>` | 背景层渲染开关 |
| `province` | `<true\|false>` | 省份层渲染开关 |
| `provincecapital` | `<true\|false>` | 省会标记渲染开关 |
| `building` | `<true\|false>` | 建筑层渲染开关 |
| `belongflag` | `<true\|false>` | 归属国旗渲染开关 |
| `legiondomain` | `<true\|false\|tile\|calculate>` | 军团领域渲染控制 |
| `tilecache` | `<true\|false>` | 瓦片缓存开关（已废弃） |
| `cleartiles` | 无 | 清除瓦片缓存（已废弃） |
| `fps` | 无 | 切换FPS显示 |
| `cameralocation` | `<hex_index>` | 移动视角到指定格子 |
| `terraindata` | `<index> {pos,val}` 或 `<index> field val` | 修改地形数据 |
| `belongdata` | `<coord\|{x,y}> <belong_value>` | 修改格子归属值 |
| `buildingdata` | `<coord\|{x,y}> <field> <value>` | 编辑/创建建筑数据 |
| `getinfo` | `<terrains\|buildingdata> <field>` | 导出地形/建筑数据 |
| `window` | `<wpf\|winform>` | 切换窗口模式 |
| `draw` | `<shape> at {params} <land\|sea>` | 绘制图形 |

---

## 注意事项

1. **使用前提**: 大部分命令需要在 WinForms 渲染场景中才能正常工作
2. **内存管理**: `terrains` 和 `background` 命令会显示内存变化信息
3. **范围检查**: `draw` 命令会自动过滤超出地图范围的格子
4. **废弃命令**: `tilecache` 和 `cleartiles` 在新渲染器中不再需要
