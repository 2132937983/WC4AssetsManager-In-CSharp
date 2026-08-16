# Functions.py - 数据结构补充详解

## 概述

本文档补充 `Functions.py` 中建筑 (BTLBuilding) 和军队 (BTLArmy) 数据结构的详细说明。

---

## BTLBuilding - 建筑数据结构

**字节数**: 32 字节

### 属性详解

| 属性名 | 偏移 | 大小 | 类型 | 说明 |
|--------|------|------|------|------|
| `coordinate` | 0x00 | 2 | uint16 | 建筑所在格子坐标 (索引) |
| `name` | 0x02 | 2 | uint16 | 建筑名称索引 |
| `building_type` | 0x04 | 1 | uint8 | 建筑类型 |
| `appearance` | 0x05 | 1 | uint8 | 外观 |
| `landmark_building` | 0x06 | 1 | uint8 | 自带地标建筑 |
| `decorative_building` | 0x07 | 1 | uint8 | 特色装饰建筑 |
| `skill_unlock` | 0x08 | 1 | uint8 | 技能解锁 |
| `reward_count` | 0x09 | 1 | uint8 | 奖励数量 |
| `hatred_value` | 0x0C | 1 | int8 | 仇恨值 |
| `key_point` | 0x0D | 1 | uint8 | 关键据点 |
| `occupation_event` | 0x0E | 1 | uint8 | 占领触发事件 |
| `fire_ignition` | 0x14 | 1 | uint8 | 火焰燃起 |
| `fire_duration` | 0x15 | 1 | uint8 | 火焰持续回合 |
| `air_defense_weapon` | 0x16 | 1 | uint8 | 防空武器 |
| `air_defense_radar` | 0x17 | 1 | uint8 | 防空武器是否携带雷达 |
| `factory_level` | 0x18 | 1 | uint8 | 工厂等级 |
| `research_level` | 0x19 | 1 | uint8 | 科研所等级 |
| `medical_level` | 0x1A | 1 | uint8 | 医疗等级 |
| `aviation_level` | 0x1B | 1 | uint8 | 航空等级 |
| `missile_level` | 0x1C | 1 | uint8 | 导弹等级 |
| `nuclear_level` | 0x1D | 1 | uint8 | 核弹等级 |

### 建筑类型 (building_type)

| 值 | 类型名称 |
|----|----------|
| 0 | 城市 (City) |
| 1 | 县城 (County Town) |
| 2 | 港口 (Port) |
| 3 | 关隘 (Pass) |
| 4 | 要塞 (Fortress) |
| 5+ | 其他类型 |

### 关键据点 (key_point)

| 值 | 显示名称 |
|----|----------|
| 0 | 否 |
| 1 | 红圈 |
| 2 | 绿圈 |

### 仇恨值编码 (hatred_value)

```
编码规则:
- 0-127: 正值 (0-127)
- 128-255: 负值 (-128 到 -1)

计算公式:
正值: hatred_value
负值: hatred_value - 256

示例:
0x00 = 0
0x01 = 1
0x7F = 127
0x80 = -128
0xFF = -1
```

### 防空雷达 (air_defense_radar)

| 值 | 含义 |
|----|------|
| 0x00 | 否 |
| 0x02 | 是 |

### 使用示例

```python
from Functions import BTLBuilding

# 创建建筑
building = BTLBuilding()

# 设置属性
building.coordinate = 1000        # 位于格子1000
building.name = 1                  # 名称索引1
building.building_type = 0        # 城市
building.key_point = 1             # 关键据点-红圈
building.factory_level = 3         # 工厂等级3
building.research_level = 2        # 科研等级2

# 获取二进制数据
data = building.get_hex_data()
print(f"建筑数据: {data.hex()}")

# 解析现有数据
building2 = BTLBuilding()
building2.parse_building_data(data)
print(f"建筑类型: {building2.building_type}")
```

---

## BTLArmy - 军队数据结构

**字节数**: 48 字节

### 属性详解

| 属性名 | 偏移 | 大小 | 类型 | 说明 |
|--------|------|------|------|------|
| `coordinate` | 0x00 | 2 | uint16 | 所在格子坐标 |
| `unit_type` | 0x02 | 1 | uint8 | 兵种类型 |
| `level` | 0x03 | 1 | uint8 | 等级 |
| `organization` | 0x04 | 1 | uint8 | 编制 |
| `direction` | 0x05 | 1 | uint8 | 方向 |
| `mobility` | 0x06 | 1 | uint8 | 移动力 |
| `built_round` | 0x07 | 1 | uint8 | 建造回合 |
| `experience` | 0x08 | 2 | uint16 | 兵种经验 |
| `health_bonus` | 0x0A | 2 | int16 | 血量加成 |
| `current_health` | 0x0C | 2 | int16 | 当前血量 |
| `max_health` | 0x0E | 2 | int16 | 血量上限 |
| `general` | 0x10 | 2 | uint16 | 将领索引 |
| `rank` | 0x12 | 1 | uint8 | 军衔 |
| `nobility` | 0x13 | 1 | uint8 | 爵位 |
| `badge1` | 0x14 | 1 | uint8 | 胸章一 |
| `badge2` | 0x15 | 1 | uint8 | 胸章二 |

### 兵种类型 (unit_type)

| 值 | 类型名称 | 说明 |
|----|----------|------|
| 0 | 步兵 | 陆战基本单位 |
| 1 | 骑兵 | 高速机动单位 |
| 2 | 弓兵 | 远程攻击单位 |
| 3 | 步兵 | 陆战单位 |
| 4 | 海军 | 水上作战单位 |
| 5 | 空军 | 空中作战单位 |

### 编制 (organization)

| 值 | 名称 |
|----|------|
| 0 | 编制1 |
| 1 | 编制2 |
| ... | ... |

### 使用示例

```python
from Functions import BTLArmy

# 创建军队
army = BTLArmy()

# 设置属性
army.coordinate = 500          # 位于格子500
army.unit_type = 0              # 步兵
army.level = 3                  # 等级3
army.organization = 1            # 编制2
army.mobility = 10              # 移动力10
army.experience = 100            # 经验100
army.current_health = 100      # 当前血量100
army.max_health = 100           # 血量上限100
army.general = 5                # 将领5号

# 获取二进制数据
data = army.get_hex_data()
print(f"军队数据: {data.hex()}")
```

---

## 其他数据结构速查

### BTLProvince - 省份数据 (20字节)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `province_id` | 0x00 | 省份ID |
| `name_offset` | 0x04 | 名称偏移 |
| `capital` | 0x08 | 省会位置 |
| `color` | 0x0C | 省份颜色 |
| `province_data` | - | 原始2字节数据 |

### BTLTrap - 陷阱数据 (16字节)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `coordinate` | 0x00 | 位置 |
| `type` | 0x02 | 陷阱类型 |
| `probability` | 0x04 | 触发概率 |
| `damage` | 0x08 | 伤害值 |
| `owner` | 0x0C | 所属国家 |

### BTLCase - 方案数据 (16字节)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `id` | 0x00 | 方案ID |
| `trigger_type` | 0x04 | 触发类型 |
| `trigger_condition` | 0x08 | 触发条件 |
| `action` | 0x0C | 执行动作 |

### BTLWeather - 天气数据 (16字节)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `turn` | 0x00 | 生效回合 |
| `type` | 0x04 | 天气类型 |
| `area_start` | 0x08 | 影响区域起点 |
| `area_end` | 0x0C | 影响区域终点 |

### BTLEvent - 事件数据 (44字节)

| 属性名 | 偏移 | 说明 |
|--------|------|------|
| `event_id` | 0x00 | 事件ID |
| `trigger_type` | 0x04 | 触发类型 |
| `trigger_condition` | 0x08 | 触发条件 |
| `condition_value` | 0x0C | 条件值 |
| `action_type` | 0x10 | 动作类型 |
| `action_value` | 0x14 | 动作值 |

---

## 二进制数据读写示例

### 读取剧本文件

```python
from StageParser import StageParser

# 加载文件
parser = StageParser()
parser.load_hex_file("scenario.btl")

# 获取所有建筑
for i, building in enumerate(parser.building):
    print(f"建筑 {i+1}: 坐标={building.coordinate}, 类型={building.building_type}")

# 获取所有军队
for i, army in enumerate(parser.army):
    print(f"军队 {i+1}: 坐标={army.coordinate}, 兵种={army.unit_type}, 等级={army.level}")
```

### 修改并保存

```python
# 修改建筑
parser.building[0].building_type = 0  # 设为城市
parser.building[0].key_point = 1     # 设为关键据点

# 修改军队
parser.army[0].level = 5              # 升级
parser.army[0].experience = 500        # 增加经验

# 保存
parser.save_data("modified.btl")
```

---

## 数据校验

```python
def validate_building(building):
    """验证建筑数据是否有效"""
    errors = []
    
    # 检查坐标范围
    if building.coordinate < 0:
        errors.append("坐标不能为负数")
    
    # 检查建筑类型
    if building.building_type > 10:
        errors.append("无效的建筑类型")
    
    # 检查等级
    if building.level > 10:
        errors.append("等级超出范围")
    
    return errors

def validate_army(army):
    """验证军队数据是否有效"""
    errors = []
    
    if army.coordinate < 0:
        errors.append("坐标不能为负数")
    
    if army.unit_type > 20:
        errors.append("无效的兵种类型")
    
    if army.level > 99:
        errors.append("等级超出范围")
    
    if army.current_health > army.max_health:
        errors.append("当前血量不能超过血量上限")
    
    return errors
```

---

*文档生成时间: 2026-04-15*
