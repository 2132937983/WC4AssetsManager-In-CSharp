# StageParser.py - 剧本解析器详解

## 概述

`StageParser.py` 是 WC4 地图编辑器的核心解析模块，负责解析、创建和保存剧本 (`.btl`) 文件。

---

## StageParser 类

**文件路径**: `source/map_editor/StageParser.py`

### 类定义

```python
class StageParser:
    def __init__(self, stage=None, hex_file_path=None):
        """初始化剧本解析器
        
        Args:
            stage: 可选的剧本数据对象
            hex_file_path: 可选的十六进制文件路径
        """
```

---

## 核心方法详解

### 1. 文件加载方法

#### load_hex_file

```python
def load_hex_file(self, file_path):
    """加载十六进制文件
    
    读取 BTL 文件并解析为十六进制字符串格式
    
    Args:
        file_path: BTL 文件路径
        
    Returns:
        str: 十六进制字符串
        
    Raises:
        FileNotFoundError: 文件不存在
    """
```

**工作流程**:
1. 打开二进制文件
2. 读取所有字节
3. 转换为十六进制字符串
4. 存储原始数据

#### get_hex_data

```python
def get_hex_data(self):
    """获取原始十六进制数据
    
    Returns:
        bytes: 原始十六进制数据
    """
```

#### get_hex_string

```python
def get_hex_string(self, separator=' '):
    """获取格式化的十六进制字符串
    
    Args:
        separator: 分隔符，默认空格
        
    Returns:
        str: 格式化后的十六进制字符串
    """
```

---

### 2. 数据获取方法

每个 `get_xxx_data` 方法都对应解析 BTL 文件中的一个数据段。

#### get_header_data

```python
def get_header_data(self):
    """获取文件头数据
    
    Returns:
        BTLHeaderParser: 包含所有头部信息的对象
    """
```

#### get_legion_data

```python
def get_legion_data(self):
    """获取军团数据
    
    Returns:
        list: BTLLegion 对象列表
    """
    
    legions = []
    # 从文件头获取军团数量
    army_count = self.header.army_count
    
    # 每个军团300字节
    offset = 128  # 文件头之后开始
    for i in range(army_count):
        legion_bytes = self.hex_data[offset:offset+300]
        legion = BTLLegion(legion_bytes)
        legions.append(legion)
        offset += 300
    
    return legions
```

#### get_terrain_data

```python
def get_terrain_data(self):
    """获取地形数据
    
    Returns:
        list: BTLTerrain 对象列表
        
    地形数据结构:
    - 每个六边形 16 字节
    - 总数 = map_length × map_width
    - 按行优先顺序存储
    """
```

#### get_province_data

```python
def get_province_data(self):
    """获取省份数据
    
    Returns:
        list: BTLProvince 对象列表
    """
```

#### get_building_data

```python
def get_building_data(self):
    """获取建筑数据
    
    Returns:
        list: BTLBuilding 对象列表
        
    建筑数据字段:
    - coordinate: 格子索引
    - name_offset: 名称偏移
    - type: 建筑类型
    - level: 等级
    - hp: 生命值
    - defense: 防御值
    - owner: 所属国家
    """
```

#### get_army_data

```python
def get_army_data(self):
    """获取军队数据
    
    Returns:
        list: BTLArmy 对象列表
        
    军队数据字段:
    - coordinate: 位置
    - unit_type: 兵种
    - level: 等级
    - hp: 生命值
    - morale: 士气
    - experience: 经验
    - owner: 所属国家
    - belong_to: 所属军团
    """
```

#### get_trap_data

```python
def get_trap_data(self):
    """获取陷阱/计策数据
    
    Returns:
        list: BTLTrap 对象列表
    """
```

#### get_case_data

```python
def get_case_data(self):
    """获取事件数据
    
    Returns:
        list: BTLCase 对象列表
    """
```

#### get_weather_data

```python
def get_weather_data(self):
    """获取天气数据
    
    Returns:
        list: BTLWeather 对象列表
    """
```

#### get_event_data

```python
def get_event_data(self):
    """获取事件触发数据
    
    Returns:
        list: BTLEvent 对象列表
    """
```

#### get_reinforcement_data

```python
def get_reinforcement_data(self):
    """获取援军数据
    
    Returns:
        list: BTLReinforcement 对象列表
    """
```

---

### 3. 数据保存方法

#### save_data

```python
def save_data(self, output_path=None):
    """保存所有数据到文件
    
    Args:
        output_path: 输出文件路径，如果为None则返回二进制数据
        
    Returns:
        bytes 或 None: 如果output_path为None则返回二进制数据
        
    工作流程:
    1. 创建文件头字节
    2. 依次添加各数据结构字节
    3. 写入文件或返回
    """
```

#### save_hex_data

```python
def save_hex_data(self, output_path):
    """保存十六进制数据到文件
    
    Args:
        output_path: 输出文件路径
    """
```

#### save_data_with_confirmation

```python
def save_data_with_confirmation(self, output_path=None):
    """带确认对话框的保存
    
    如果文件已存在，显示保存确认对话框
    """
```

---

### 4. 创建新剧本

#### create_new_campaign

```python
def create_new_campaign(self, map_width=10, map_height=10, num_legions=2):
    """创建新剧本
    
    Args:
        map_width: 地图宽度（列数）
        map_height: 地图高度（行数）
        num_legions: 军团/势力数量
        
    Returns:
        self: 返回解析器实例用于链式调用
    """
```

**创建流程**:
1. 初始化文件头（设置地图尺寸、版本等）
2. 创建默认地形数据（全陆地）
3. 创建默认省份
4. 创建指定数量的军团
5. 设置默认放置位置

---

### 5. 信息打印方法

每个数据获取方法都有对应的打印方法：

```python
def print_header_info(self):
    """打印文件头信息"""

def print_legion_info(self):
    """打印军团信息"""

def print_terrain_info(self):
    """打印地形信息"""
    
def print_province_info(self):
    """打印省份信息"""

def print_building_info(self):
    """打印建筑信息"""

def print_army_info(self):
    """打印军队信息"""

# ... 其他打印方法
```

---

## 数据读取流程图

```
┌─────────────────────────────────────────┐
│          load_hex_file()                │
│    读取 BTL 文件为十六进制字符串         │
└──────────────────┬──────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────┐
│      BTLHeaderParser.parse()            │
│   解析文件头 (128字节)                   │
│   获取: 地图尺寸、各类数据数量            │
└──────────────────┬──────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────┐
│         get_xxx_data()                  │
│   根据文件头中的数量信息读取对应数据       │
│   - 地形: 16字节 × (长×宽)              │
│   - 军团: 300字节 × N                   │
│   - 建筑: 44字节 × N                    │
│   - 军队: 48字节 × N                    │
└─────────────────────────────────────────┘
```

---

## 数据保存流程图

```
┌─────────────────────────────────────────┐
│          save_data()                    │
│    准备保存所有数据                       │
└──────────────────┬──────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────┐
│    BTLHeaderParser.get_hex_string()    │
│   生成文件头字节 (128字节)               │
└──────────────────┬──────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────┐
│      依次调用各 get_xxx_string()        │
│   - get_legion_string()                 │
│   - get_terrain_string()                │
│   - get_building_string()               │
│   - ...                                 │
└──────────────────┬──────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────┐
│           写入文件                       │
│    使用二进制模式写入所有字节             │
└─────────────────────────────────────────┘
```

---

## 使用示例

### 1. 读取现有剧本文件

```python
from StageParser import StageParser

# 创建解析器并加载文件
parser = StageParser()
parser.load_hex_file("scenario.btl")

# 获取各类数据
header = parser.get_header_data()
legions = parser.get_legion_data()
terrains = parser.get_terrain_data()
buildings = parser.get_building_data()
armies = parser.get_army_data()

# 打印信息
parser.print_header_info()
parser.print_legion_info()
parser.print_terrain_info()
```

### 2. 创建新剧本

```python
from StageParser import StageParser

# 创建新剧本
parser = StageParser()
parser.create_new_campaign(
    map_width=50,
    map_height=50,
    num_legions=4  # 4个势力
)

# 保存文件
parser.save_data("new_scenario.btl")
```

### 3. 修改地形数据

```python
from StageParser import StageParser
from Functions import BTLTerrain

# 加载文件
parser = StageParser()
parser.load_hex_file("scenario.btl")

# 获取地形数据
terrains = parser.get_terrain_data()

# 修改第100个格子的地形
terrains[100].hex_value = 0x01000001  # 设置为水域
terrains[100].province_id = 5         # 设为省份5

# 保存修改后的文件
parser.save_data("modified_scenario.btl")
```

### 4. 添加建筑

```python
from StageParser import StageParser
from Functions import BTLBuilding

# 加载文件
parser = StageParser()
parser.load_hex_file("scenario.btl")

# 创建新建筑
new_building = BTLBuilding()
new_building.coordinate = 100          # 位置
new_building.type = 0                  # 城市
new_building.level = 1                 # 等级1
new_building.hp = 1000                 # 生命值
new_building.defense = 100             # 防御
new_building.owner = 1                 # 属于国家1

# 添加到建筑列表
parser.building = [new_building]

# 更新文件头的建筑数量
parser.header.building_count = len(parser.building)

# 保存
parser.save_data("with_building.btl")
```

---

## 字段偏移计算公式

### 地形数据偏移

```python
def get_terrain_offset(col, row, map_width):
    """计算地形数据的字节偏移
    
    Args:
        col: 列索引 (0 到 map_width-1)
        row: 行索引 (0 到 map_length-1)
        map_width: 地图宽度
        
    Returns:
        int: 字节偏移量
    """
    hex_index = row * map_width + col  # 六边形索引
    offset = 128 + hex_index * 16      # 128是文件头大小
    return offset
```

### 格子索引计算

```python
def coord_to_index(col, row, map_width):
    """将列行坐标转换为格子索引
    
    Args:
        col: 列
        row: 行
        map_width: 地图宽度
        
    Returns:
        int: 格子索引
    """
    return row * map_width + col

def index_to_coord(index, map_width):
    """将格子索引转换为列行坐标
    
    Args:
        index: 格子索引
        map_width: 地图宽度
        
    Returns:
        tuple: (col, row)
    """
    row = index // map_width
    col = index % map_width
    return col, row
```

---

## 错误处理

```python
try:
    parser = StageParser()
    parser.load_hex_file("invalid_file.btl")
except FileNotFoundError:
    print("文件不存在")
except ValueError as e:
    print(f"文件格式错误: {e}")
except Exception as e:
    print(f"未知错误: {e}")
```

---

*文档生成时间: 2026-04-15*
