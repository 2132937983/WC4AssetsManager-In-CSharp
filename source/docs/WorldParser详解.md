# WorldParser.py - 地图文件解析器详解

## 概述

`WorldParser.py` 是 WC4 地图编辑器的地图文件解析模块，负责解析、创建和保存世界地图 (`.world` / `.bin`) 文件。

---

## WorldParser 类

**文件路径**: `source/map_editor/WorldParser.py`

**职责**: 解析三国志4的地图数据文件，支持读取、创建和保存地图。

---

## 文件格式

### World 文件结构

```
World 文件结构
├── 文件头 (16 字节)
│   ├── 魔数 (8 字节): 0x59, 0x53, 0x41, 0x45, 0x04, 0x00, 0x00, 0x00 ("YSAE" + 版本号)
│   ├── 地图宽度 (2 字节): uint16 (小端序)
│   ├── 保留字节 (2 字节)
│   ├── 地图高度 (2 字节): uint16 (小端序)
│   └── 保留字节 (2 字节)
│
├── 地形数据 (16 字节 × N)
│   └── 每个格子的地形信息
│
└── 省份数据 (2 字节 × N)
    └── 每个格子的省份归属
```

### 文件头详情

| 偏移 | 大小 | 类型 | 说明 |
|------|------|------|------|
| 0x00 | 8 | bytes | 魔数: `59 53 41 45 04 00 00 00` ("YSAE" + v4) |
| 0x08 | 2 | uint16 | 地图宽度 (列数) |
| 0x0A | 2 | bytes | 保留字节: `00 00` |
| 0x0C | 2 | uint16 | 地图高度 (行数) |
| 0x0E | 2 | bytes | 保留字节: `00 00` |

---

## 类定义

```python
class WorldParser:
    def __init__(self, hex_data=None, hex_file_path=None):
        """初始化WorldParser
        
        Args:
            hex_data: 直接传入的十六进制数据
            hex_file_path: 十六进制文件路径
        """
```

---

## 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `hex_data` | bytes | 原始十六进制数据 |
| `hex_file_path` | str | 文件路径 |
| `width` | int | 地图宽度 (列数) |
| `height` | int | 地图高度 (行数) |
| `terrain_count` | int | 地形格子总数 |
| `terrain` | list | 地形数据列表 (BTLTerrain对象) |
| `provinces` | list | 省份数据列表 (BTLProvince对象) |

---

## 核心方法

### 初始化方法

#### __init__

```python
def __init__(self, hex_data=None, hex_file_path=None):
    """初始化并自动解析文件
    
    初始化流程:
    1. 验证文件头
    2. 解析地图尺寸
    3. 解析地形数据
    4. 解析省份数据
    """
```

### 文件加载方法

#### load_hex_file

```python
def load_hex_file(self, file_path):
    """加载十六进制文件
    
    Args:
        file_path: 文件路径
        
    Raises:
        FileNotFoundError: 文件不存在
        
    Returns:
        bytes: 文件的十六进制数据
    """
```

### 验证方法

#### validate_header

```python
def validate_header(self):
    """验证world文件头
    
    检查魔数是否为: 0x59, 0x53, 0x41, 0x45, 0x04, 0x00, 0x00, 0x00
    
    Note:
        验证失败只打印警告，不抛出异常，继续处理
    """
```

### 解析方法

#### parse_map_dimensions

```python
def parse_map_dimensions(self):
    """解析地图尺寸
    
    从文件头中读取地图宽度和高度
    
    读取位置:
    - 0x8-0x9: 宽度 (uint16, 小端序)
    - 0xC-0xD: 高度 (uint16, 小端序)
    
    计算:
    - terrain_count = width × height
    """
```

#### parse_terrain_data

```python
def parse_terrain_data(self):
    """解析地形数据
    
    数据位置: 从 0x10 开始
    数据大小: 每个地形 16 字节
    数据数量: terrain_count (width × height)
    
    地形数据结构 (BTLTerrain):
    ┌─────────────────────────────────────────┐
    │ tile_type1       (4字节) 第一层地形类型  │
    │ decoration_type1  (4字节) 第一层装饰类型  │
    │ texture_offset_x1 (4字节) 贴图偏移X     │
    │ texture_offset_y1 (4字节) 贴图偏移Y     │
    │ tile_type2       (4字节) 第二层地形类型  │
    │ decoration_type2  (4字节) 第二层装饰类型  │
    │ texture_offset_x2 (4字节) 贴图偏移X     │
    │ texture_offset_y2 (4字节) 贴图偏移Y     │
    │ tile_type3       (4字节) 第三层地形类型  │
    │ decoration_type3  (4字节) 第三层装饰类型  │
    │ texture_offset_x3 (4字节) 贴图偏移X     │
    │ texture_offset_y3 (4字节) 贴图偏移Y     │
    │ river_value      (4字节) 河流值         │
    └─────────────────────────────────────────┘
    
    Note:
        使用 min() 防止读取超出文件范围的数据
    """
```

#### parse_province_data

```python
def parse_province_data(self):
    """解析省份数据
    
    数据位置: 地形数据结束后
    数据大小: 每个省份 2 字节
    数据数量: (文件大小 - 地形数据大小) / 2
    
    省份数据结构:
    ┌────────────────────────┐
    │ province_data (2字节) │
    └────────────────────────┘
    
    Note:
        每个省份占用2字节，使用 province_data 属性存储原始数据
    """
```

### 查询方法

#### get_terrain_at

```python
def get_terrain_at(self, x, y):
    """获取指定坐标的地形
    
    Args:
        x: 列索引 (0 到 width-1)
        y: 行索引 (0 到 height-1)
        
    Returns:
        BTLTerrain 或 None: 指定位置的地形对象
    """
    
    # 检查边界
    if x < 0 or x >= self.width or y < 0 or y >= self.height:
        return None
    
    # 计算索引
    index = y * self.width + x
    
    # 返回地形
    if index < len(self.terrain):
        return self.terrain[index]
    return None
```

### 信息输出方法

#### print_world_info

```python
def print_world_info(self, detailed=False):
    """打印世界文件信息
    
    Args:
        detailed: 是否打印详细信息 (默认False)
        
    打印内容:
    - 文件大小
    - 地图宽度/高度
    - 地形总数
    - 已解析地形数
    - 已解析省份数
    
    Note:
        detailed=True 时打印前3个地形和省份的详细信息
    """
```

### 创建新地图方法

#### create_new_map

```python
def create_new_map(self, map_width, map_height):
    """创建新的地图文件
    
    Args:
        map_width: 地图宽度 (格子数)
        map_height: 地图高度 (格子数)
        
    Returns:
        bool: 是否成功创建
        
    创建流程:
    1. 设置地图尺寸
    2. 创建默认地形数据 (海洋)
    3. 创建默认省份数据 (0xFFFF)
    4. 生成文件头
    5. 组合所有数据到 hex_data
    """
    
    # 默认地形设置
    terrain.tile_type1 = 0      # 海洋地形类型
    terrain.decoration_type1 = 0 # 无装饰
    terrain.texture_offset_x1 = 0
    terrain.texture_offset_y1 = 0
    terrain.tile_type2 = 0
    terrain.decoration_type2 = 0
    terrain.texture_offset_x2 = 0
    terrain.texture_offset_y2 = 0
    terrain.tile_type3 = 0
    terrain.decoration_type3 = 0
    terrain.texture_offset_x3 = 0
    terrain.texture_offset_y3 = 0
    terrain.river_value = 0      # 无河流
    
    # 默认省份设置
    province.province_data = b'\xff\xff'  # 0xFFFF 表示无所属
```

### 保存方法

#### save_data

```python
def save_data(self, file_path, terrain_data=None, province_data=None):
    """保存地图数据到二进制文件
    
    Args:
        file_path: 保存文件的路径
        terrain_data: 可选，外部传入的地形数据列表
        province_data: 可选，外部传入的省份数据列表
        
    Returns:
        bool: 是否成功保存
        
    保存流程:
    1. 创建字节数组
    2. 添加文件头 (8字节)
    3. 添加地图尺寸信息 (8字节)
    4. 添加地形数据 (每个16字节)
    5. 添加省份数据 (每个2字节)
    6. 写入文件
    
    Note:
        如果传入的地形/省份数据数量不足，自动填充默认数据
        如果传入的数据过多，自动截断
    """
```

---

## 使用示例

### 1. 加载现有地图文件

```python
from WorldParser import WorldParser

# 创建解析器并加载文件
parser = WorldParser(hex_file_path="china.world")

# 获取地图信息
print(f"地图尺寸: {parser.width} x {parser.height}")
print(f"地形总数: {parser.terrain_count}")
print(f"省份总数: {len(parser.provinces)}")

# 获取指定位置的地形
terrain = parser.get_terrain_at(10, 20)
if terrain:
    print(f"地形类型: {terrain.tile_type1}")
    print(f"河流值: {terrain.river_value}")

# 打印详细信息
parser.print_world_info(detailed=True)
```

### 2. 创建新地图

```python
from WorldParser import WorldParser

# 创建新地图
parser = WorldParser()
success = parser.create_new_map(50, 50)  # 50x50 地图

if success:
    # 保存地图文件
    parser.save_data("new_map.world")
    print("新地图已创建并保存")
```

### 3. 修改地形数据

```python
from WorldParser import WorldParser
from Functions import BTLTerrain

# 加载地图
parser = WorldParser(hex_file_path="china.world")

# 修改指定位置的地形
terrain = parser.get_terrain_at(10, 20)
if terrain:
    terrain.tile_type1 = 1  # 设为陆地
    terrain.river_value = 0x3F  # 设置河流 (所有边)
    
    # 保存修改后的地图
    parser.save_data("modified_map.world")
```

### 4. 批量修改地形

```python
from WorldParser import WorldParser

# 加载地图
parser = WorldParser(hex_file_path="china.world")

# 批量修改所有地形
for terrain in parser.terrain:
    if terrain.tile_type1 == 0:  # 海洋
        terrain.tile_type1 = 1     # 改为陆地

# 保存
parser.save_data("all_land.world")
```

---

## 数据流程图

### 加载流程

```
WorldParser.__init__(hex_file_path)
    │
    ├── load_hex_file()
    │   │
    │   └── 读取文件为字节数据
    │
    ├── validate_header()
    │   │
    │   └── 检查魔数 "YSAE"
    │
    ├── parse_map_dimensions()
    │   │
    │   └── 读取 width, height
    │
    ├── parse_terrain_data()
    │   │
    │   └── 解析 (width × height) 个地形
    │
    └── parse_province_data()
        │
        └── 解析剩余字节为省份数据
```

### 保存流程

```
WorldParser.save_data(file_path)
    │
    ├── 创建空字节数组
    │
    ├── 添加文件头
    │   └── 0x59, 0x53, 0x41, 0x45, 0x04, 0x00, 0x00, 0x00
    │
    ├── 添加地图尺寸
    │   ├── width (2字节, 小端序)
    │   ├── 保留 (2字节)
    │   ├── height (2字节, 小端序)
    │   └── 保留 (2字节)
    │
    ├── 添加地形数据
    │   └── 每个 terrain 16 字节
    │
    ├── 添加省份数据
    │   └── 每个 province 2 字节
    │
    └── 写入文件
```

---

## 地形类型参考

| tile_type1 值 | 地形类型 |
|--------------|----------|
| 0 | 海洋 |
| 1 | 陆地/平原 |
| 2 | 山地 |
| 3 | 森林 |
| 4 | 荒地 |
| ... | (更多类型需要查看游戏数据) |

### 地形层次

World 文件中的地形支持3层叠加:
- **tile_type1 / decoration_type1**: 第一层 (底层)
- **tile_type2 / decoration_type2**: 第二层 (中层)
- **tile_type3 / decoration_type3**: 第三层 (顶层)

### 河流值

```
river_value 结构 (32位):
┌───┬───┬───┬───┬───┬──────────────┐
│ 5 │ 4 │ 3 │ 2 │ 1 │ 0            │
│ 河流方向掩码 (1=有河流)           │
└───┴───┴───┴───┴───┴──────────────┘
```

---

## 错误处理

```python
try:
    parser = WorldParser(hex_file_path="invalid.world")
except FileNotFoundError:
    print("文件不存在")
except ValueError as e:
    print(f"文件格式错误: {e}")
except Exception as e:
    print(f"未知错误: {e}")

# 验证文件头
parser.validate_header()  # 会打印警告但不会中断

# 检查地图尺寸
if parser.width <= 0 or parser.height <= 0:
    print("无效的地图尺寸")
```

---

## 与 StageParser 的区别

| 特性 | WorldParser | StageParser |
|------|-------------|-------------|
| 文件扩展名 | .world / .bin | .btl / .conquest |
| 数据内容 | 纯地图数据 | 完整剧本数据 |
| 地形数据 | 16字节 × N | 16字节 × N |
| 省份数据 | 2字节 × N | 2字节 × N |
| 建筑/军队 | 不包含 | 包含 |
| 军团数据 | 不包含 | 包含 (300字节) |

---

*文档生成时间: 2026-04-15*
