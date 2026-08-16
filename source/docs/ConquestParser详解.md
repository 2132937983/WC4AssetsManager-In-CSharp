# ConquestParser.py - 征服模式解析器详解

## 概述

`ConquestParser.py` 是 WC4 地图编辑器的征服模式文件解析模块，继承自 `StageParser`，用于解析和保存征服模式 (.conquest) 文件。

---

## 类层次结构

```
StageParser (基类)
    │
    └── ConquestParser  (征服模式解析器)
```

---

## ConquestParser 类

**文件路径**: `source/map_editor/ConquestParser.py`

**职责**: 解析三国志4征服模式文件，与剧本文件类似但数据结构略有不同。

### 与 StageParser 的区别

| 特性 | StageParser (.btl) | ConquestParser (.conquest) |
|------|-------------------|---------------------------|
| 文件类型 | 剧本文件 | 征服模式文件 |
| 地形数据 | 有 | **无** (只有省份和归属) |
| 省份数据 | 2字节 × N | 2字节 × N |
| 归属数据 | 1字节 × N | 1字节 × N |
| 建筑数据 | 44字节 | 32字节 |
| 军队数据 | 48字节 | 可能不同 |
| 用途 | 剧本编辑 | 征服模式 |

---

## 类定义

```python
class ConquestParser(StageParser):
    def __init__(self, hex_data=None, hex_file_path=None):
        """初始化征服模式解析器
        
        Args:
            hex_data: 直接传入的十六进制数据
            hex_file_path: 征服模式文件路径
        """
```

---

## 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `hex_data` | bytes | 原始十六进制数据 |
| `hex_file_path` | str | 文件路径 |
| `header_parser` | BTLHeaderParser | 文件头解析器 |
| `legion` | list | 军团列表 |
| `province` | list | 省份列表 |
| `belong` | list | 归属列表 |
| `building` | list | 建筑列表 |
| `troop` | list | 部队列表 |
| `trap` | list | 陷阱列表 |
| `case` | list | 方案列表 |
| `weather` | list | 天气列表 |
| `event` | list | 事件列表 |
| `reinforcement` | list | 援军列表 |
| `airforce` | list | 空袭列表 |
| `unit_place` | list | 单位位置列表 |
| `capital` | list | 首都列表 |
| `strategy_construction` | list | 战略建设列表 |
| `airsupport` | list | 空中支援列表 |

---

## 核心方法

### 文件加载

#### load_hex_file

```python
def load_hex_file(self, file_path):
    """加载征服模式文件
    
    加载流程:
    1. 读取文件为字节数据
    2. 自动解析所有数据段
    
    自动解析的数据:
    - 头部数据 (128字节)
    - 军团数据 (300字节 × N)
    - 省份数据 (2字节 × N)
    - 归属数据 (1字节 × N)
    - 建筑数据 (32字节 × N)
    - 部队数据 (48字节 × N)
    - 陷阱数据 (16字节 × N)
    - 方案数据 (16字节 × N)
    - 天气数据 (16字节 × N)
    - 事件数据 (44字节 × N)
    - 援军数据 (80字节 × N)
    - 空袭数据 (20字节 × N)
    - 单位位置数据 (8字节 × N)
    - 首都数据 (4字节 × N)
    - 战略建设数据 (16字节 × N)
    - 空中支援数据 (16字节 × N)
    """
```

### 数据保存

#### save_data

```python
def save_data(self, output_path=None):
    """保存征服模式数据到文件
    
    保存流程:
    1. 更新头部数据中的数量
    2. 验证数据一致性
    3. 组合所有数据为二进制格式
    4. 写入文件或返回数据
    
    数据段顺序:
    1. 文件头 (128字节)
    2. 军团数据 (300字节 × N)
    3. 省份数据 (2字节 × N)
    4. 归属数据 (1字节 × N)
    5. 建筑数据 (32字节 × N)
    6. 部队数据 (48字节 × N)
    7. 陷阱数据 (16字节 × N)
    8. 方案数据 (16字节 × N)
    9. 天气数据 (16字节 × N)
    10. 事件数据 (44字节 × N)
    11. 援军数据 (80字节 × N)
    12. 空袭数据 (20字节 × N)
    13. 单位位置数据 (8字节 × N)
    14. 首都数据 (4字节 × N)
    15. 战略建设数据 (16字节 × N)
    16. 空中支援数据 (16字节 × N)
    """
```

### 数据解析方法

#### get_header_data

```python
def get_header_data(self):
    """获取文件头数据
    
    Returns:
        BTLHeaderParser: 包含文件头信息的对象
    """
```

#### get_legion_data

```python
def get_legion_data(self):
    """获取军团数据
    
    数据位置: 0x80 (128字节) 后
    数据大小: 300字节 × army_count
    
    Returns:
        list: BTLLegion 对象列表
    """
```

#### get_province_data

```python
def get_province_data(self):
    """获取省份数据
    
    数据位置: 军团数据后
    数据大小: 2字节 × selectable_tile_count
    
    Returns:
        list: BTLProvince 对象列表
    """
```

#### get_belong_data

```python
def get_belong_data(self):
    """获取归属数据
    
    数据位置: 省份数据后
    数据大小: 1字节 × selectable_tile_count
    
    Returns:
        list: 归属值列表 (16进制字符串)
    """
    
    # 归属值对应国家ID
    # 0x00: 国家1
    # 0x01: 国家2
    # ...
    # 0xFF: 无所属
```

#### get_building_data

```python
def get_building_data(self):
    """获取建筑数据
    
    数据位置: 归属数据后
    数据大小: 32字节 × building_count
    
    Returns:
        list: BTLBuilding 对象列表
    """
```

#### get_troop_data

```python
def get_troop_data(self):
    """获取部队数据
    
    数据位置: 建筑数据后
    数据大小: 48字节 × troop_count
    
    Returns:
        list: BTLArmy 对象列表
    """
```

---

## 数据偏移计算

### 征服文件数据偏移

```python
def calculate_offsets(self):
    """计算各数据段的偏移量"""
    
    # 固定偏移
    HEADER_SIZE = 0x80  # 128字节
    
    # 军团数据偏移
    LEGION_START = HEADER_SIZE
    LEGION_SIZE = 300
    
    # 省份数据偏移
    province_count = self.header_parser.selectable_tile_count
    province_size = 2
    PROVINCE_START = LEGION_START + (self.header_parser.army_count * LEGION_SIZE)
    
    # 归属数据偏移
    belong_size = 1
    BELONG_START = PROVINCE_START + (province_count * province_size)
    
    # 建筑数据偏移
    building_size = 32
    BUILDING_START = BELONG_START + (province_count * belong_size)
    
    return {
        'legion': LEGION_START,
        'province': PROVINCE_START,
        'belong': BELONG_START,
        'building': BUILDING_START
    }
```

---

## 使用示例

### 1. 加载征服文件

```python
from ConquestParser import ConquestParser

# 创建解析器并加载文件
parser = ConquestParser()
parser.load_hex_file("conquest_01.conquest")

# 获取文件头信息
header = parser.get_header_data()
print(f"地图宽度: {header.map_width}")
print(f"地图高度: {header.map_length}")
print(f"军团数量: {header.army_count}")
print(f"建筑数量: {header.building_count}")

# 获取军团
legions = parser.get_legion_data()
print(f"军团数: {len(legions)}")

# 获取省份
provinces = parser.get_province_data()
print(f"省份数: {len(provinces)}")

# 获取归属
belong = parser.get_belong_data()
print(f"归属数据数: {len(belong)}")
```

### 2. 修改归属数据

```python
from ConquestParser import ConquestParser

# 加载文件
parser = ConquestParser()
parser.load_hex_file("conquest_01.conquest")

# 修改指定位置的归属
# 假设 belong[100] = "01" 表示国家1
parser.belong[100] = "02"  # 改为国家2

# 保存修改
parser.save_data("modified.conquest")
```

### 3. 添加建筑

```python
from ConquestParser import ConquestParser
from Functions import BTLBuilding

# 加载文件
parser = ConquestParser()
parser.load_hex_file("conquest_01.conquest")

# 创建新建筑
new_building = BTLBuilding()
new_building.coordinate = 500
new_building.type = 0  # 城市
new_building.level = 1
new_building.hp = 1000
new_building.owner = 1

# 添加到列表
parser.building.append(new_building)

# 更新头部数量
parser.header_parser.building_count = len(parser.building)

# 保存
parser.save_data("with_building.conquest")
```

---

## 数据流程图

### 加载流程

```
ConquestParser.load_hex_file(file_path)
    │
    ├── 读取文件字节数据
    │
    ├── get_header_data()
    │   └── 解析128字节文件头
    │
    ├── get_legion_data()
    │   └── 解析 300字节 × army_count
    │
    ├── get_province_data()
    │   └── 解析 2字节 × selectable_tile_count
    │
    ├── get_belong_data()
    │   └── 解析 1字节 × selectable_tile_count
    │
    ├── get_building_data()
    │   └── 解析 32字节 × building_count
    │
    └── get_troop_data()
        └── 解析 48字节 × troop_count
```

### 保存流程

```
ConquestParser.save_data(output_path)
    │
    ├── 更新头部数量
    │   ├── army_count = len(legion)
    │   ├── building_count = len(building)
    │   └── ...
    │
    ├── 验证数据一致性
    │
    ├── 组合二进制数据
    │   ├── 头部数据 (128字节)
    │   ├── 军团数据
    │   ├── 省份数据
    │   ├── 归属数据
    │   └── ...
    │
    └── 写入文件
```

---

## 征服模式特点

### 1. 无地形数据

征服模式文件不包含地形数据，只有:
- 省份归属 (每个格子属于哪个省份)
- 格子所属国家 (哪个国家控制这个格子)

### 2. 归属数据结构

```python
# 归属数据是单字节值
belong_data[i] = 0x00  # 国家1控制
belong_data[i] = 0x01  # 国家2控制
belong_data[i] = 0xFF  # 无控制
```

### 3. 省份数据结构

```python
# 省份数据是2字节值
province_data[i] = 0x0001  # 省份1
province_data[i] = 0xFFFF  # 无省份
```

---

## 与 StageParser 代码对比

### 主要区别

```python
# StageParser - 有地形数据
def get_terrain_data(self):
    # 解析16字节 × map_length × map_width 的地形数据
    
# ConquestParser - 无地形数据
# 没有 get_terrain_data() 方法
```

### 保存时的重要区别

```python
# StageParser 保存地形
for terrain in self.terrain:
    terrain_bytes = terrain.get_hex_data()
    result_data.extend(terrain_bytes)

# ConquestParser 不保存地形
# 直接从省份开始
```

---

## 错误处理

```python
try:
    parser = ConquestParser()
    parser.load_hex_file("invalid.conquest")
    
    if parser.header_parser is None:
        print("文件头解析失败")
        
    if not parser.legion:
        print("警告: 没有军团数据")
        
except FileNotFoundError:
    print("文件不存在")
except ValueError as e:
    print(f"数据格式错误: {e}")
except Exception as e:
    print(f"未知错误: {e}")
```

---

*文档生成时间: 2026-04-15*
