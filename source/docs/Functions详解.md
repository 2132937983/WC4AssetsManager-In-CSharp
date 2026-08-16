# Functions.py - BTL 数据结构详解

## 概述

`Functions.py` 文件定义了 BTL (Battle) 文件格式的所有数据结构类。每个类对应 BTL 文件中的特定数据块。

---

## BTL 文件结构概览

```
BTL 文件结构
├── 文件头 (128 字节)           → BTLHeaderParser
├── 军团数据 (300 字节 × N)     → BTLLegion
├── 地形数据 (16 字节 × N)      → BTLTerrain
├── 省份数据 (20 字节 × N)     → BTLProvince
├── 建筑数据 (44 字节 × N)      → BTLBuilding
├── 军队数据 (48 字节 × N)      → BTLArmy
├── 陷阱数据 (36 字节 × N)     → BTLTrap
├── 事件数据 (24 字节 × N)      → BTLCase
├── 天气数据 (20 字节 × N)      → BTLWeather
├── 事件触发数据 (52 字节 × N)  → BTLEvent
├── 援军数据 (76 字节 × N)      → BTLReinforcement
├── 支援部队数据 (44 字节 × N)  → BTLSupportUnit
├── 单位放置数据 (76 字节 × N)  → BTLUnitPlacement
├── 首都数据 (28 字节 × N)      → BTLCapital
├── 战略建筑数据 (52 字节 × N)  → BTLStrategicConstruction
└── 空中支援数据 (48 字节 × N)  → BTLAirSupport
```

---

## 1. BTLHeaderParser - 文件头解析器

**字节数**: 128 字节
**位置**: 文件起始位置

### 属性详解

| 属性名 | 偏移 (Hex) | 类型 | 说明 | 示例值 |
|--------|-----------|------|------|--------|
| `btl_version` | 0x00 | uint32 | BTL 版本号 | 4 |
| `map_number` | 0x04 | uint32 | 地图编号 | 1 |
| `map_clip_x` | 0x08 | uint32 | 地图裁剪 X 起始 | 0 |
| `map_clip_y` | 0x0C | uint32 | 地图裁剪 Y 起始 | 0 |
| `map_length` | 0x10 | uint32 | 地图长度（行数） | 50 |
| `map_width` | 0x14 | uint32 | 地图宽度（列数） | 50 |
| `army_count` | 0x18 | uint32 | 军队单位数量 | 10 |
| `building_count` | 0x1C | uint32 | 建筑数量 | 5 |
| `troop_count` | 0x20 | uint32 | 部队数量 | 20 |
| `plan_count` | 0x24 | uint32 | 计划数量 | 0 |
| `event_count` | 0x28 | uint32 | 事件数量 | 3 |
| `weather_count` | 0x2C | uint32 | 天气数量 | 2 |
| `victory_condition` | 0x30 | uint32 | 胜利条件 | 1 |
| `min_turns` | 0x34 | uint32 | 最小回合数 | 10 |
| `max_turns` | 0x38 | uint32 | 最大回合数 | 50 |
| `reinforcement_count` | 0x3C | uint32 | 援军数量 | 5 |
| `air_raid_count` | 0x40 | uint32 | 空袭数量 | 2 |
| `placement_a` | 0x44 | uint32 | 放置位甲（玩家1起始位置） | 100 |
| `placement_b` | 0x48 | uint32 | 放置位乙（玩家2起始位置） | 1500 |
| `conquered_flag_position` | 0x4C | uint32 | 征服旗帜位置 | 2500 |
| `unknown3` | 0x50 | uint32 | 未知字段 | - |
| `unknown4` | 0x54 | uint32 | 未知字段 | - |
| `selectable_tile_count` | 0x58 | uint32 | 可选地块数量 | 100 |
| `accumulated_economy` | 0x5C | uint32 | 累计经济值 | 5000 |
| `accumulated_industry` | 0x60 | uint32 | 累计工业值 | 3000 |
| `accumulated_tech` | 0x64 | uint32 | 累计科技值 | 2000 |
| `trap_count` | 0x68 | uint32 | 陷阱数量 | 10 |
| `unknown5` | 0x6C | uint32 | 未知字段 | - |
| `strategy_count` | 0x70 | uint32 | 战略数量 | 0 |
| `unknown6` | 0x74 | uint32 | 未知字段 | - |
| `unknown7` | 0x78 | uint32 | 未知字段 | - |
| `air_support_count` | 0x7C | uint32 | 空中支援数量 | 3 |

### 重要方法

```python
def parse_hex_string(self, hex_string):
    """解析128字节的16进制字符串"""
    # 将字符串按小端序每4字节解析为uint32
    pass

def get_hex_string(self):
    """根据属性值生成16进制字符串"""
    # 反向操作，用于保存文件
    pass

def print_header_info(self):
    """打印所有头部信息"""
    pass
```

---

## 2. BTLLegion - 军团数据

**字节数**: 300 字节
**数量**: 由文件头中的军团数量决定

### 基础属性

| 属性名 | 偏移 | 类型 | 说明 |
|--------|------|------|------|
| `actionid` | 0x00 | uint32 | 行动顺序 |
| `country_id` | 0x04 | uint32 | 国家ID (1-8) |
| `initial_economy` | 0x08 | uint32 | 初始经济值 |
| `initial_industry` | 0x0C | uint32 | 初始工业值 |
| `initial_tech` | 0x10 | uint32 | 初始科技值 |
| `is_player_controlled` | 0x14 | uint32 | 是否玩家控制 (0/1) |
| `camp` | 0x18 | uint32 | 阵营 |
| `defeat_condition` | 0x1C | uint32 | 战败条件 |
| `country_hp_rate` | 0x20 | uint32 | 国家血量比率 |
| `country_tax_rate` | 0x24 | uint32 | 国家税率 |
| `country_color` | 0x28 | uint32 | 国家颜色 |
| `atomic_bomb_count` | 0x2C | uint32 | 原子弹数量 |
| `hydrogen_bomb_count` | 0x30 | uint32 | 氢弹数量 |
| `neutron_bomb_count` | 0x34 | uint32 | 中子弹数量 |
| `antimatter_bomb_count` | 0x38 | uint32 | 反物质弹数量 |

### 陆军单位等级 (0x3C - 0x8C)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `mobility_level` | 0x3C | 机动等级 |
| `rifle_level` | 0x40 | 步枪等级 |
| `camouflage_level` | 0x44 | 伪装等级 |
| `engineer_level` | 0x48 | 工兵等级 |
| `grenade_level` | 0x4C | 手雷等级 |
| `mortar_level` | 0x50 | 迫击炮等级 |
| `march_level` | 0x54 | 行军等级 |
| `bulletproof_vest_level` | 0x58 | 防弹衣等级 |
| `armor_level` | 0x5C | 装甲等级 |
| `main_gun_level` | 0x60 | 主炮等级 |
| `vehicle_body_level` | 0x64 | 车体等级 |
| `engine_level` | 0x68 | 引擎等级 |
| `machine_gun_level` | 0x6C | 机枪等级 |
| `raid_level` | 0x70 | 突袭等级 |
| `vehicle_air_defense_level` | 0x74 | 车载防空等级 |
| `reinforced_body_level` | 0x78 | 强化车体等级 |
| `artillery_level` | 0x7C | 火炮等级 |
| `rocket_level` | 0x80 | 火箭弹等级 |
| `towing_level` | 0x84 | 牵引等级 |
| `artillery_armor_level` | 0x88 | 火炮装甲等级 |
| `firepower_level` | 0x8C | 火力等级 |

### 海军单位等级 (0x90 - 0xB8)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `navy_rocket_level` | 0x90 | 火箭等级 |
| `disguise_level` | 0x94 | 伪装等级 |
| `hull_level` | 0x98 | 舰体等级 |
| `propulsion_level` | 0x9C | 推进器等级 |
| `navy_armor_level` | 0xA0 | 装甲等级 |
| `weapon_level` | 0xA4 | 武器等级 |
| `naval_gun_level` | 0xA8 | 舰炮等级 |
| `torpedo_level` | 0xAC | 鱼雷等级 |
| `minesweeping_level` | 0xB0 | 扫雷等级 |
| `ship_air_defense_level` | 0xB4 | 舰载防空等级 |
| `modern_hull_level` | 0xB8 | 现代舰体等级 |

### 空军单位等级 (0xBC - 0xD0)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `aviation_fuel_level` | 0xBC | 航空燃油等级 |
| `aviation_engine_level` | 0xC0 | 航空发动机等级 |
| `aviation_bomb_level` | 0xC4 | 航空炸弹等级 |
| `air_raid_level` | 0xC8 | 空袭等级 |
| `bombing_level` | 0xCC | 轰炸等级 |
| `strategic_bombing_level` | 0xD0 | 战略轰炸等级 |
| `airdrop_level` | 0xD4 | 空降等级 |
| `jet_engine_level` | 0xD8 | 喷气发动机等级 |

### 防御工事等级 (0xDC - 0x100)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `bunker_level` | 0xDC | 机枪堡等级 |
| `fortress_gun_level` | 0xE0 | 要塞炮等级 |
| `coastal_gun_level` | 0xE4 | 海岸炮等级 |
| `rocket_launcher_level` | 0xE8 | 火箭发射器等级 |
| `fortification_level` | 0xEC | 工事等级 |
| `anti_aircraft_gun_level` | 0xF0 | 高射机枪等级 |
| `anti_aircraft_cannon_level` | 0xF4 | 防空炮等级 |
| `anti_aircraft_missile_level` | 0xF8 | 对空导弹等级 |
| `radar_level` | 0xFC | 雷达等级 |
| `missile_warhead_level` | 0x100 | 导弹弹头等级 |
| `solid_rocket_engine_level` | 0x104 | 固体火箭发动机等级 |
| `nuclear_bomb_breakthrough_level` | 0x108 | 核弹破防等级 |
| `nuclear_fusion_level` | 0x10C | 核聚变等级 |

---

## 3. BTLTerrain - 地形数据

**字节数**: 16 字节
**数量**: map_length × map_width (六边形格子总数)

### 属性详解

| 属性名 | 偏移 | 类型 | 说明 |
|--------|------|------|------|
| `hex_value` | 0x00 | uint32 | 六边形值（地形类型编码） |
| `river_value` | 0x04 | uint32 | 河流值（6位掩码） |
| `province_id` | 0x08 | uint32 | 省份ID |
| `unknown1` | 0x0C | uint32 | 未知字段 |

### hex_value 编码规则

```
hex_value 结构 (32位):
┌─────────┬─────────┬─────────┬───────────────────┐
│ 31-24   │ 23-16   │ 15-8    │ 7-0               │
│ 高度层  │ 地形类型 │ 变体ID  │ 基本地形ID         │
└─────────┴─────────┴─────────┴───────────────────┘

- 基本地形ID: 0x00-0xFF (256种)
- 变体ID: 0x00-0xFF (256种变体)
- 地形类型: 0x00-0xFF (256种)
- 高度层: 0x00-0xFF (256层)
```

### river_value 编码规则

```
river_value 结构 (32位):
┌───┬───┬───┬───┬───┬──────────────┐
│ 5 │ 4 │ 3 │ 2 │ 1 │ 0            │
│ 河流方向掩码 (1=有河流)           │
└───┴───┴───┴───┴───┴──────────────┘

六边形有6条边，river_value的每1位对应1条边：
- 位0: 边0 (通常指右下边)
- 位1: 边1 (右斜边)
- 位2: 边2 (左斜边)
- 位3: 边3 (左下边)
- 位4: 边4 (左斜边)
- 位5: 边5 (右斜边)
```

---

## 4. BTLProvince - 省份数据

**字节数**: 20 字节
**数量**: 由地图中实际省份数决定

### 属性详解

| 属性名 | 偏移 | 类型 | 说明 |
|--------|------|------|------|
| `province_id` | 0x00 | uint32 | 省份ID |
| `name_offset` | 0x04 | uint32 | 省份名称偏移 |
| `capital` | 0x08 | uint32 | 省会位置（格子索引） |
| `color` | 0x0C | uint32 | 省份颜色 |
| `unknown1` | 0x10 | uint32 | 未知字段 |

---

## 5. BTLBuilding - 建筑数据

**字节数**: 44 字节
**数量**: building_count (由文件头指定)

### 属性详解

| 属性名 | 偏移 | 类型 | 说明 |
|--------|------|------|------|
| `coordinate` | 0x00 | uint32 | 位置（格子索引） |
| `name_offset` | 0x04 | uint32 | 建筑名称偏移 |
| `type` | 0x08 | uint32 | 建筑类型 |
| `level` | 0x0C | uint32 | 建筑等级 |
| `hp` | 0x10 | uint32 | 生命值 |
| `defense` | 0x14 | uint32 | 防御值 |
| `owner` | 0x18 | uint32 | 所属国家ID |
| `production` | 0x1C | uint32 | 产出类型 |
| `production_rate` | 0x20 | uint32 | 产出速率 |
| `unknown1` | 0x24 | uint32 | 未知字段 |

### 建筑类型常量

| 值 | 类型名称 |
|----|----------|
| 0 | 城市 |
| 1 | 县城 |
| 2 | 港口 |
| 3 | 关隘 |
| 4 | 要塞 |

---

## 6. BTLArmy - 军队数据

**字节数**: 48 字节
**数量**: army_count (由文件头指定)

### 属性详解

| 属性名 | 偏移 | 类型 | 说明 |
|--------|------|------|------|
| `coordinate` | 0x00 | uint32 | 位置（格子索引） |
| `unit_type` | 0x04 | uint32 | 兵种类型 |
| `level` | 0x08 | uint32 | 等级 |
| `hp` | 0x0C | uint32 | 生命值 |
| `morale` | 0x10 | uint32 | 士气值 |
| `experience` | 0x14 | uint32 | 经验值 |
| `owner` | 0x18 | uint32 | 所属国家ID |
| `belong_to` | 0x1C | uint32 | 所属军团actionid |
| `movement` | 0x20 | uint32 | 移动力 |
| `attack` | 0x24 | uint32 | 攻击力 |
| `defense` | 0x28 | uint32 | 防御力 |
| `unknown1` | 0x2C | uint32 | 未知字段 |

### 兵种类型常量

| 值 | 兵种名称 |
|----|----------|
| 0 | 步兵 |
| 1 | 骑兵 |
| 2 | 弓兵 |
| 3 | 步兵 |
| 4 | 海军 |
| 5 | 空军 |

---

## 7. 其他数据结构速查

### BTLTrap - 陷阱数据 (36字节)

| 属性名 | 说明 |
|--------|------|
| `coordinate` | 位置 |
| `type` | 陷阱类型 |
| `probability` | 触发概率 |
| `damage` | 伤害值 |
| `owner` | 所属国家 |

### BTLCase - 事件数据 (24字节)

| 属性名 | 说明 |
|--------|------|
| `id` | 事件ID |
| `trigger_type` | 触发类型 |
| `trigger_condition` | 触发条件 |
| `action` | 执行动作 |

### BTLWeather - 天气数据 (20字节)

| 属性名 | 说明 |
|--------|------|
| `turn` | 生效回合 |
| `type` | 天气类型 |
| `affected_area_start` | 影响区域起点 |
| `affected_area_end` | 影响区域终点 |
| `duration` | 持续回合 |

### BTLEvent - 事件触发 (52字节)

| 属性名 | 说明 |
|--------|------|
| `id` | 事件ID |
| `trigger_type` | 触发类型 |
| `trigger_condition` | 触发条件 |
| `condition_value` | 条件值 |
| `action_type` | 动作类型 |
| `action_value` | 动作值 |

---

## 数据解析示例

```python
# 读取BTL文件
def load_btl_file(filepath):
    with open(filepath, 'rb') as f:
        # 1. 读取文件头 (128字节)
        header_bytes = f.read(128)
        header = BTLHeaderParser()
        header.parse_hex_string(header_bytes)

        # 2. 读取军团数据
        legions = []
        for _ in range(header.placement_a):  # 或其他军团数量字段
            legion_bytes = f.read(300)
            legion = BTLLegion(legion_bytes)
            legions.append(legion)

        # 3. 读取地形数据
        terrains = []
        total_hex = header.map_length * header.map_width
        for _ in range(total_hex):
            terrain_bytes = f.read(16)
            terrain = BTLTerrain(terrain_bytes)
            terrains.append(terrain)

    return header, legions, terrains

# 解析地形类型
def get_terrain_type(hex_value):
    base_terrain = hex_value & 0xFF
    variant = (hex_value >> 8) & 0xFF
    terrain_type = (hex_value >> 16) & 0xFF
    height_layer = (hex_value >> 24) & 0xFF
    return base_terrain, variant, terrain_type, height_layer

# 检查河流
def has_river(hex_terrain, edge):
    """检查指定边是否有河流"""
    return (hex_terrain.river_value >> edge) & 1 == 1
```

---

## 数据保存示例

```python
def save_btl_file(filepath, header, legions, terrains):
    with open(filepath, 'wb') as f:
        # 1. 保存文件头
        header_bytes = header.get_hex_string()
        f.write(header_bytes)

        # 2. 保存军团数据
        for legion in legions:
            legion_bytes = legion.get_hex_string()
            f.write(legion_bytes)

        # 3. 保存地形数据
        for terrain in terrains:
            terrain_bytes = terrain.get_hex_string()
            f.write(terrain_bytes)
```

---

*文档生成时间: 2026-04-15*
