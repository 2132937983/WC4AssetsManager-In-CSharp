# ZSC 脚本文件详解

## 概述

**ZSC** 是地图编辑器的脚本文件格式，用于批量执行渲染控制台命令。通过将一系列命令写入 `.zsc` 文件，用户可以一次性完成大量地图编辑操作，无需逐行手动输入。

**文件扩展名**: `.zsc`

**编码格式**: UTF-8（支持中文）

**执行方式**: 将 `.zsc` 文件拖放到渲染场景窗口中，程序会自动读取并逐行执行

---

## 文件格式

### 基本结构

```
# 这是注释行
render terraindata 1020 TileType1 12          # 修改地形
render buildingdata 1020 Name "徐州"          # 创建/修改建筑
render terraindata 1021 TileType1 0           # 另一块地形
```

### 语法规则

1. **每行一条命令**：每条命令独占一行
2. **命令格式**：与控制台输入完全一致
3. **注释支持**：以 `#`、`'` 或 `//` 开头的行会被忽略
4. **空行支持**：空行或纯空白字符的行会被自动跳过
5. **中文支持**：文件使用 UTF-8 编码，支持中文内容

---

## 支持的注释格式

```zsc
# Python 风格的注释
' VB 风格的注释
// C/C++/Java 风格的注释

/*
  注意：多行注释（块注释）不支持！
  每行注释必须单独标记
*/
```

---

## 地形类型

地形类型值	名称
0	平地
1	海洋
2	沙漠
3	矮雪山
4	中雪山
5	高雪山
6	矮土山
7	中土山
8	高土山
9	矮绿山
10	中绿山
11	高绿山
12	矮沙山
13	中沙山
14	高沙山
15	仙人掌
16	阔叶林
18	积雪阔叶林
20	针叶林
21	积雪针叶林
22	热带森林

## 建筑类型

建筑ID	名称
2	田1
3	田1
11	城1
12	城2
13	城3
14	城4
15	城5
21	厂1
22	厂2
23	厂3
31	港1
32	港2
33	港3
34	港4



## 可用命令

ZSC 文件中可以使用的命令与渲染控制台完全一致。以下是常用的命令分类：

### 1. 地形编辑命令

```zsc
# 修改指定格子的地形数据
render terraindata <hex_index> {byte_pos,value}
render terraindata <hex_index> <field_name> <value>

# 示例
render terraindata 100 TileType1 0      # 设置为平地
render terraindata 100 TileType1 11     # 设置为高绿山
render terraindata 200 {0,1}            # 字节位置方式：偏移0设为1
```

**可用字段名**:
- `TileType1`, `DecorationType1`, `TextureOffsetX1`, `TextureOffsetY1`
- `TileType2`, `DecorationType2`, `TextureOffsetX2`, `TextureOffsetY2`
- `TileType3`, `DecorationType3`, `TextureOffsetX3`, `TextureOffsetY3`
- `RiverValue`

### 2. 建筑编辑命令

```zsc
# 编辑或创建建筑数据
render buildingdata <coord|{x,y}> <field_name> <value>

# 示例
render buildingdata 1662 BuildingType 1           # 修改现有建筑
render buildingdata {12,33} BuildingType 11       # 在坐标处创建建筑
render buildingdata {15,25} Name "徐州"           # 设置城市名称（中文需双引号）
```

**可用字段名**:
- `Coordinate`, `Name`, `BuildingType`, `Appearance`, `LandmarkBuilding`
- `DecorativeBuilding`, `SkillUnlock`, `RewardCount`, `HatredValue`
- `KeyPoint`, `OccupationEvent`, `FireIgnition`, `FireDuration`
- `AirDefenseWeapon`, `AirDefenseRadar`, `FactoryLevel`, `ResearchLevel`
- `MedicalLevel`, `AviationLevel`, `MissileLevel`, `NuclearLevel`

### 3. 图形绘制命令

```zsc
# 在地图上绘制几何图形
render draw <shape> at {params} <land|sea>

# 示例
render draw line at {0,0,10,10} land      # 画直线，设为平地
render draw circle at {5,5,3} sea         # 画圆面，设为海洋
render draw square at {0,0,20,15} land    # 画矩形面，设为平地
```

**支持的图形**:
- `line` - 直线（参数: `{x1,y1,x2,y2}`）
- `hcircle` - 圆环（参数: `{centerX,centerY,radius}`）
- `circle` - 圆面（参数: `{centerX,centerY,radius}`）
- `hsquare` - 矩形环（参数: `{x1,y1,x2,y2}`）
- `square` - 矩形面（参数: `{x1,y1,x2,y2}`）

### 4. 归属编辑命令

```zsc
# 修改指定格子的归属值
render belongdata <coord|{x,y}> <belong_value>

# 示例
render belongdata 1662 5              # 将索引1662处的格子归属设为5
render belongdata {12,33} 10          # 将(列12,行33)的格子归属设为10
render belongdata 100 0xFF            # 将索引100处的格子设为无归属(255)
```

**归属值说明**:
- `0-127`: 有效的归属ID（对应不同国家/势力）
- `255` (或 `0xFF`): 无归属/空白地
- 支持十进制或十六进制格式

### 5. 信息导出命令

```zsc
# 导出地形信息到文件（会弹出保存对话框）
render getinfo terrains <field_name>

# 导出建筑信息到文件
render getinfo buildingdata <field_name>

# 示例
render getinfo terrains TileType1           # 导出所有格子的第一层地形类型
render getinfo terrains TileType2           # 导出第二层地形类型
render getinfo terrains RiverValue          # 导出河流值
render getinfo buildingdata Name            # 导出所有建筑的坐标和名称
render getinfo buildingdata BuildingType    # 导出所有建筑的坐标和类型
render getinfo buildingdata KeyPoint        # 导出所有建筑的坐标和关键据点值
render getinfo buildingdata FactoryLevel    # 导出所有建筑的坐标和工厂等级
```

**buildingdata 可用字段名**:
- `Coordinate`, `Name`, `BuildingType`, `Appearance`, `LandmarkBuilding`
- `DecorativeBuilding`, `SkillUnlock`, `RewardCount`, `HatredValue`
- `KeyPoint`, `OccupationEvent`, `FireIgnition`, `FireDuration`
- `AirDefenseWeapon`, `AirDefenseRadar`, `FactoryLevel`, `ResearchLevel`
- `MedicalLevel`, `AviationLevel`, `MissileLevel`, `NuclearLevel`

### 6. 渲染层控制命令

```zsc
# 控制各渲染层的显示/隐藏
render terrains true          # 显示地形纹理
render terrains false         # 隐藏地形纹理
render background true        # 显示背景
render province true          # 显示省份层
render building true          # 显示建筑层
render belongflag true        # 显示归属国旗
render legiondomain true      # 显示军团领域
```

### 7. 视角控制命令

```zsc
# 移动视角到指定格子
render cameralocation <hex_index>

# 示例
render cameralocation 0       # 移动到第一个格子
render cameralocation 5000    # 移动到第5000个格子
```

---

## 完整示例脚本

### 示例 1：创建简单地形

**文件名**: `create_landscape.zsc`

```zsc
# ============================================
# 创建基础地形脚本
# 描述：创建一个包含山脉、海洋和平原的简单地形
# ============================================

# --- 海洋区域 (左下角) ---
render draw square at {0,0,5,5} sea

# --- 山脉区域 (右上角) ---
render draw square at {15,15,19,19} land
render terraindata 340 TileType1 11     # 高绿山
render terraindata 341 TileType1 11
render terraindata 360 TileType1 11
render terraindata 361 TileType1 11

# --- 中间平原 ---
render draw square at {5,5,15,15} land

# --- 添加一些装饰 ---
render terraindata 100 TileType2 16      # 阔叶林
render terraindata 150 TileType2 20      # 针叶林
render terraindata 200 TileType2 26      # 农田
```

### 示例 2：创建城市

**文件名**: `create_cities.zsc`

```zsc
# ============================================
# 创建城市脚本
# 描述：在地图上创建多个城市
# ============================================

# --- 徐州 ---
render buildingdata {10,20} BuildingType 11
render buildingdata {10,20} Name "徐州"
render buildingdata {10,20} KeyPoint 1

# --- 南京 ---
render buildingdata {15,25} BuildingType 11
render buildingdata {15,25} Name "南京"
render buildingdata {15,25} KeyPoint 1
render buildingdata {15,25} FactoryLevel 3

# --- 上海 ---
render buildingdata {20,30} BuildingType 11
render buildingdata {20,30} Name "上海"
render buildingdata {20,30} KeyPoint 1
render buildingdata {20,30} AviationLevel 2

# 显示建筑层
render building true
```

### 示例 3：批量修改地形

**文件名**: `batch_terrain.zsc`

```zsc
# ============================================
# 批量修改地形脚本
# 描述：将指定区域全部改为沙漠
# ============================================

# 方法1：使用 draw 命令（推荐，适合大面积）
render draw square at {0,0,50,50} land

# 然后修改这些格子的地形类型为沙漠
# 注意：这里需要知道格子的索引范围
# 假设地图宽度为100，索引 0-4999 对应区域 {0,0} 到 {49,49}

# 方法2：逐行修改（精确控制）
render terraindata 0 TileType1 2
render terraindata 1 TileType1 2
render terraindata 2 TileType1 2
# ... 更多行
```

### 示例 4：复杂场景设置

**文件名**: `setup_battlefield.zsc`

```zsc
# ============================================
# 战场场景设置脚本
# ============================================

# 1. 基础地形设置
render draw square at {0,0,100,100} land

# 2. 创建河流
render terraindata 500 RiverValue 1
render terraindata 501 RiverValue 1
render terraindata 502 RiverValue 1
render terraindata 503 RiverValue 1

# 3. 创建山脉屏障
render draw square at {20,20,30,30} land
render terraindata 2020 TileType1 8       # 高土山
render terraindata 2021 TileType1 8
render terraindata 2030 TileType1 8

# 4. 创建森林区域
render terraindata 1000 TileType2 16      # 阔叶林
render terraindata 1001 TileType2 16
render terraindata 1010 TileType2 16
render terraindata 1011 TileType2 16

# 5. 创建城市
render buildingdata {50,50} BuildingType 11
render buildingdata {50,50} Name "主城"
render buildingdata {50,50} KeyPoint 1

# 6. 设置城市归属（假设归属ID为5）
render belongdata {50,50} 5

# 7. 创建资源点
render buildingdata {10,10} BuildingType 5
render buildingdata {90,90} BuildingType 5

# 8. 设置资源点归属
render belongdata {10,10} 5
render belongdata {90,90} 3

# 9. 设置视角到主城
render cameralocation 5050

# 8. 显示所有层
render terrains true
render building true
render province true
```

---

## 执行流程

### 拖放执行步骤

```
1. 用户拖放 .zsc 文件到场景窗口
         ↓
2. 程序检查文件扩展名（必须是 .zsc）
         ↓
3. 自动显示渲染控制台（如果隐藏）
         ↓
4. 逐行读取文件内容
         ↓
5. 跳过空行和注释行
         ↓
6. 对每行命令：
   a. 显示命令内容和行号
   b. 通过 CommandManager 执行
   c. 记录成功/失败
         ↓
7. 显示执行统计报告
```

### 控制台输出示例

```
=== 开始执行 ZSC 脚本: setup_battlefield.zsc ===
文件路径: E:apscriptsetup_battlefield.zsc
时间: 2026-05-01 15:30:00

> [5] render draw square at {0,0,100,100} land
> [8] render terraindata 500 RiverValue 1
> [9] render terraindata 501 RiverValue 1
> [13] render terraindata 2020 TileType1 8
> [22] render buildingdata {50,50} BuildingType 11
> [23] render buildingdata {50,50} Name "主城"
> [29] render cameralocation 5050
视角已移动到格子 5050 (列: 50, 行: 50)
> [32] render terrains true
> [33] render building true
> [34] render province true

=== 脚本执行完成 ===
总行数: 34
成功: 31
失败: 0
跳过(空行/注释): 3
完成时间: 15:30:02
```

---

## 注意事项

### 1. 命令执行环境

- ZSC 脚本在**渲染控制台上下文**中执行
- 需要在 WinForms 渲染场景中才能正常工作
- 如果场景未加载，命令会失败

### 2. 错误处理

- **单条命令失败不会中断脚本**：即使某行命令执行失败，后续命令仍会继续执行
- **错误信息会显示在控制台**：可以看到哪一行失败了以及失败原因
- **建议先测试单条命令**：在写入 ZSC 文件前，先在控制台手动测试命令是否正确

### 3. 性能考虑

- **大量命令可能耗时较长**：如果脚本包含数千行命令，执行可能需要几秒钟
- **建议分批执行**：对于非常大的修改，可以分成多个 .zsc 文件
- **draw 命令比逐行修改更高效**：大面积修改建议使用 `render draw` 命令

### 4. 文件编码

- **必须使用 UTF-8 编码**：否则中文内容会显示为乱码
- **推荐使用 VS Code、Notepad++ 等专业编辑器**：Windows 记事本保存时请选择 "UTF-8" 编码

### 5. 路径和坐标

- **格子索引**：`terraindata` 命令使用单数值索引（从0开始）
- **坐标格式**：`buildingdata` 和 `draw` 命令使用 `{x,y}` 格式（列,行）
- **坐标转换**：索引 = 行 × 地图宽度 + 列

### 6. 字符串值

- **中文名称需要双引号**：`render buildingdata {10,20} Name "徐州"`
- **英文名称可以不加引号**：`render buildingdata {10,20} Name Paris`
- **布尔值**：使用 `true` 或 `false`（小写）

---

## 调试技巧

### 1. 逐步调试

```zsc
# 在关键位置添加注释，方便定位问题
# ===== 步骤1：创建基础地形 =====
render draw square at {0,0,50,50} land

# ===== 步骤2：添加山脉 =====
render terraindata 100 TileType1 11

# ===== 步骤3：验证 =====
render getinfo terrains TileType1    # 导出查看结果
```

### 2. 使用 getinfo 验证

```zsc
# 修改后导出数据验证
render terraindata 100 TileType1 11
render getinfo terrains TileType1    # 检查第100个格子是否变为高绿山
```

### 3. 分段执行

```zsc
# 第一部分：地形
# ... 地形命令 ...

# 第二部分：建筑（可以注释掉不执行）
# render buildingdata {10,10} BuildingType 11
# render buildingdata {10,10} Name "测试城市"

# 第三部分：单位（可以注释掉不执行）
# ... 单位命令 ...
```

---

## 文件命名建议

| 文件名格式 | 说明 |
|-----------|------|
| `setup_terrain.zsc` | 地形设置脚本 |
| `create_cities.zsc` | 城市创建脚本 |
| `batch_modify.zsc` | 批量修改脚本 |
| `test_commands.zsc` | 测试命令脚本 |
| `battlefield_001.zsc` | 特定战场设置 |

---

## 与 Python 脚本的对比

| 特性 | ZSC 脚本 | Python 脚本 |
|------|---------|------------|
| 执行环境 | 渲染控制台 | Python 解释器 |
| 语法复杂度 | 简单（逐行命令） | 复杂（完整编程语言）|
| 条件判断 | 不支持 | 支持 |
| 循环 | 不支持 | 支持 |
| 变量 | 不支持 | 支持 |
| 适用场景 | 批量执行固定命令 | 复杂逻辑、动态生成 |
| 学习成本 | 低 | 高 |

**建议**：
- 简单的批量操作 → 使用 ZSC 脚本
- 复杂的逻辑控制 → 使用 Python 脚本

---

## 常见问题

### Q1: 拖放文件后没有反应？

**可能原因**：
- 文件扩展名不是 `.zsc`
- 文件编码不是 UTF-8
- 程序未在渲染场景中运行

**解决方法**：
- 确认文件名为 `.zsc` 结尾
- 使用 VS Code 或 Notepad++ 保存为 UTF-8 编码
- 确保已打开地图并处于渲染模式

### Q2: 中文显示乱码？

**解决方法**：
- 保存文件时选择 "UTF-8" 编码
- 避免使用 Windows 记事本的默认 "ANSI" 编码

### Q3: 某行命令执行失败？

**调试方法**：
1. 查看控制台输出的错误信息
2. 复制失败的命令到控制台手动测试
3. 检查参数格式是否正确

### Q4: 如何撤销脚本执行？

**解决方法**：
- ZSC 脚本没有内置撤销功能
- 建议执行前保存地图
- 可以编写反向脚本（将值改回原来的）

---

## 相关文件

- [RenderCommand详解.md](./RenderCommand详解.md) - 渲染命令详细文档
- [BuildingSetting.vb](../Assist/BuildingSetting.vb) - 建筑设置窗口
- [ArmySetting.vb](../Assist/ArmySetting.vb) - 单位设置窗口
- [DebugConsoleWinForms.vb](../Assist/DebugConsoleWinForms.vb) - 调试控制台实现

---

## 更新日志

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-05-01 | 1.0 | 初始版本，支持基本命令执行 |
| 2026-05-02 | 1.1 | 新增 `render belongdata` 命令，支持修改格子归属值 |
| 2026-05-02 | 1.2 | 新增 `render getinfo buildingdata` 命令，支持导出建筑数据 |

---

*文档生成时间: 2026-05-02*
*适用于: WC4MapEditor 项目*