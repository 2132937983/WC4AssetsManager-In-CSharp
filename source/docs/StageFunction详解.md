# StageFunction.py - 剧本编辑功能模块详解

## 概述

`StageFunction.py` 是 WC4 地图编辑器的剧本编辑核心模块，定义了各种编辑器类用于编辑剧本元素（建筑、军队、军团、所属等）。

---

## 类层次结构

```
Stage_Editor (基类)
    │
    ├── Building_Editor  (建筑编辑器)
    ├── Army_Editor      (军队编辑器)
    ├── Legion_Editor    (军团编辑器)
    └── Belong_Editor   (所属编辑器)
```

---

## 1. Stage_Editor - 剧本编辑器基类

**文件路径**: `source/map_editor/StageFunction.py`

**职责**: 提供剧本编辑的基础功能，包括选中格子、获取建筑/省份信息、事件处理等。

### 初始化方法

```python
def __init__(self, render_map):
    """初始化剧本编辑器
    
    Args:
        render_map: Render_Map对象，用于访问地图数据
    """
```

### 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `render_map` | Render_Map | 地图渲染器对象 |
| `selected_hex` | dict | 当前选中的六边形信息 |
| `selected_hex_color` | tuple | 选中格子的边框颜色 (默认黄色) |
| `selected_hex_outline_width` | int | 边框宽度 |
| `copied_province_value` | int | 复制的省份值（用于粘贴）|
| `province_edit_mode` | bool | 省份编辑模式标志 |

### 核心方法

#### get_building_info

```python
def get_building_info(self, hex_data):
    """获取指定格子的建筑信息
    
    Args:
        hex_data: 地形数据对象 (BTLTerrain)
        
    Returns:
        dict: 包含建筑坐标、建筑名称、建筑类型等信息
        
    流程:
    1. 遍历所有建筑数据
    2. 检查建筑的coordinate是否匹配当前格子索引
    3. 返回匹配的建筑信息
    """
```

#### get_country_info_by_actionid

```python
def get_country_info_by_actionid(self, actionid):
    """根据actionid获取国家信息
    
    Args:
        actionid: 军团行动顺序ID
        
    Returns:
        dict: 包含国家ID、国家名称、颜色等信息
        
    流程:
    1. 遍历所有军团数据
    2. 匹配actionid
    3. 返回对应国家信息
    """
```

#### get_belong_info_display

```python
def get_belong_info_display(self, hex_data):
    """获取格子归属信息用于显示
    
    Args:
        hex_data: 地形数据对象
        
    Returns:
        str: 归属信息文本（国家名称或"无"）
    """
```

#### handle_event

```python
def handle_event(self, event):
    """处理编辑器事件
    
    Args:
        event: pygame事件对象
        
    处理内容:
    - 鼠标点击选中格子
    - 键盘快捷键
    - 复制粘贴操作
    """
```

#### draw

```python
def draw(self, surface):
    """绘制编辑器相关内容
    
    Args:
        surface: pygame绘图表面
        
    绘制内容:
    - 选中格子高亮
    - 省份边界
    - 省份颜色填充
    """
```

#### flood_fill_provinces

```python
def flood_fill_provinces(self, start_col, start_row, new_province_value):
    """使用泛洪算法填充省份
    
    使用泛洪算法将相连的同类型格子标记为同一省份
    
    Args:
        start_col: 起始列索引
        start_row: 起始行索引
        new_province_value: 新的省份值
        
    Returns:
        int: 填充的格子数量
    """
```

#### generate_provinces_for_all_isolated_capitals

```python
def generate_provinces_for_all_isolated_capitals(self):
    """为所有孤立的省会生成省份
    
    遍历所有建筑，找出省会类型的建筑，
    为每个省会周围的格子生成对应的省份
    
    Returns:
        int: 生成的省份数量
    """
```

#### expand_isolated_provinces_to_fill_map

```python
def expand_isolated_provinces_to_fill_map(self, isolated_capitals):
    """扩展孤立的省份填充地图
    
    使用BFS算法，从每个省会开始向外扩展，
    填充所有未分配的格子
    
    Args:
        isolated_capitals: 孤立省会列表
        
    Returns:
        bool: 是否成功
    """
```

#### expand_all_provinces_to_fill_map

```python
def expand_all_provinces_to_fill_map(self):
    """扩展所有省份填充地图
    
    遍历所有省份，从省会开始，
    使用泛洪算法填充所有格子
    
    填充规则:
    - 相同地形的格子优先被同省份填充
    - 不同地形的边界处停止填充
    
    Returns:
        int: 填充的格子数量
    """
```

#### generate_province_with_perlin_noise

```python
def generate_province_with_perlin_noise(self, center_col, center_row, province_value):
    """使用Perlin噪声生成省份
    
    以指定格子为中心，使用Perlin噪声生成自然的省份形状
    
    Args:
        center_col: 中心列索引
        center_row: 中心行索引
        province_value: 省份值
        
    Returns:
        list: 生成的省份格子列表
    """
```

#### perlin_noise

```python
def perlin_noise(self, x, y):
    """Perlin噪声算法实现
    
    Args:
        x: X坐标
        y: Y坐标
        
    Returns:
        float: 噪声值 (-1 到 1)
    """
```

#### set_province_value

```python
def set_province_value(self, index, province_value):
    """设置指定格子的省份值
    
    Args:
        index: 格子索引
        province_value: 省份值
    """
```

#### clear_all_provinces_to_ffff

```python
def clear_all_provinces_to_ffff(self):
    """清除所有省份设置为0xFFFF
    
    将地图上所有格子的省份值设置为0xFFFF（无所属）
    """
```

---

## 2. Building_Editor - 建筑编辑器

**职责**: 管理建筑的放置、删除、属性修改。

**继承自**: `Stage_Editor`

### 特有属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `buildings` | list | 建筑列表 |
| `building_types` | dict | 建筑类型定义 |
| `selected_building_type` | int | 当前选中的建筑类型 |
| `show_building_menu` | bool | 是否显示建筑菜单 |

### 特有方法

#### place_building

```python
def place_building(self, hex_index, building_type, building_name, level=1):
    """放置建筑
    
    Args:
        hex_index: 格子索引
        building_type: 建筑类型 (0=城市,1=县城,2=港口,3=关隘,4=要塞)
        building_name: 建筑名称
        level: 建筑等级
        
    Returns:
        bool: 是否成功放置
    """
```

#### delete_building

```python
def delete_building(self, hex_index):
    """删除建筑
    
    Args:
        hex_index: 格子索引
        
    Returns:
        bool: 是否成功删除
    """
```

#### modify_building

```python
def modify_building(self, hex_index, **kwargs):
    """修改建筑属性
    
    Args:
        hex_index: 格子索引
        **kwargs: 要修改的属性 (level, hp, defense, owner等)
        
    Returns:
        bool: 是否成功修改
    """
```

#### get_buildings_by_type

```python
def get_buildings_by_type(self, building_type):
    """获取指定类型的建筑列表
    
    Args:
        building_type: 建筑类型
        
    Returns:
        list: 建筑列表
    """
```

#### calculate_defense_bonus

```python
def calculate_defense_bonus(self, hex_index):
    """计算防御加成
    
    根据周围地形和建筑计算防御加成
    
    Args:
        hex_index: 格子索引
        
    Returns:
        int: 防御加成值
    """
```

---

## 3. Army_Editor - 军队编辑器

**职责**: 管理军队单位的放置、删除、属性修改。

**继承自**: `Stage_Editor`

### 特有属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `armies` | list | 军队列表 |
| `unit_types` | dict | 兵种类型定义 |
| `selected_unit_type` | int | 当前选中的兵种类型 |
| `show_army_menu` | bool | 是否显示军队菜单 |

### 特有方法

#### place_army

```python
def place_army(self, hex_index, unit_type, level=1, owner=None):
    """放置军队单位
    
    Args:
        hex_index: 格子索引
        unit_type: 兵种类型 (0=步兵,1=骑兵,2=弓兵,3=海军,4=空军)
        level: 等级
        owner: 所属国家ID
        
    Returns:
        bool: 是否成功放置
    """
```

#### delete_army

```python
def delete_army(self, hex_index):
    """删除军队单位
    
    Args:
        hex_index: 格子索引
        
    Returns:
        bool: 是否成功删除
    """
```

#### modify_army

```python
def modify_army(self, hex_index, **kwargs):
    """修改军队属性
    
    Args:
        hex_index: 格子索引
        **kwargs: 要修改的属性 (level, hp, morale, experience等)
        
    Returns:
        bool: 是否成功修改
    """
```

#### get_armies_by_owner

```python
def get_armies_by_owner(self, owner_id):
    """获取指定国家的军队
    
    Args:
        owner_id: 国家ID
        
    Returns:
        list: 军队列表
    """
```

#### calculate_movement_range

```python
def calculate_movement_range(self, hex_index, movement_points):
    """计算移动范围
    
    根据地形和移动力计算军队可移动的范围
    
    Args:
        hex_index: 起始格子索引
        movement_points: 移动力点数
        
    Returns:
        list: 可移动的格子列表
    """
```

---

## 4. Legion_Editor - 军团编辑器

**职责**: 管理军团的创建、编辑、配置。

**继承自**: `Stage_Editor`

### 特有属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `legions` | list | 军团列表 |
| `legion_count` | int | 军团数量 |
| `selected_legion` | int | 当前选中的军团索引 |

### 特有方法

#### create_legion

```python
def create_legion(self, country_id, initial_economy, initial_industry, initial_tech):
    """创建新军团
    
    Args:
        country_id: 国家ID
        initial_economy: 初始经济值
        initial_industry: 初始工业值
        initial_tech: 初始科技值
        
    Returns:
        BTLLegion: 创建的军团对象
    """
```

#### assign_army_to_legion

```python
def assign_army_to_legion(self, army_index, legion_actionid):
    """将军队分配到军团
    
    Args:
        army_index: 军队索引
        legion_actionid: 军团actionid
        
    Returns:
        bool: 是否成功分配
    """
```

#### set_legion_tech_level

```python
def set_legion_tech_level(self, actionid, tech_type, level):
    """设置军团科技等级
    
    Args:
        actionid: 军团actionid
        tech_type: 科技类型 (army/navy/air/fortification)
        level: 等级
        
    Returns:
        bool: 是否成功设置
    """
```

---

## 5. Belong_Editor - 所属编辑器

**职责**: 管理格子的所属国家/省份。

**继承自**: `Stage_Editor`

### 特有属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `belong_data` | list | 归属数据列表 |
| `countries` | dict | 国家定义 |

### 特有方法

#### set_belong

```python
def set_belong(self, hex_index, country_id):
    """设置格子所属国家
    
    Args:
        hex_index: 格子索引
        country_id: 国家ID (0-7)
        
    Returns:
        bool: 是否成功设置
    """
```

#### copy_belong

```python
def copy_belong(self, hex_index):
    """复制格子归属
    
    Args:
        hex_index: 格子索引
        
    将选中格子的归属复制到剪贴板
    """
```

#### paste_belong

```python
def paste_belong(self, hex_index):
    """粘贴归属到格子
    
    Args:
        hex_index: 格子索引
        
    将剪贴板的归属粘贴到目标格子
    """
```

#### flood_fill_belong

```python
def flood_fill_belong(self, hex_index, country_id):
    """泛洪填充归属
    
    使用泛洪算法，将相连的同地形格子标记为同一国家
    
    Args:
        hex_index: 起始格子索引
        country_id: 国家ID
        
    Returns:
        int: 填充的格子数量
    """
```

---

## 键盘快捷键

| 按键 | 功能 |
|------|------|
| `C` | 复制省份/归属 |
| `V` | 粘贴省份/归属 |
| `Delete` | 删除选中格子的建筑/军队 |
| `1-5` | 选择建筑类型 |
| `Tab` | 切换省份编辑模式 |

---

## 数据流程图

### 建筑放置流程

```
用户点击格子
    │
    ▼
handle_event() 接收鼠标点击
    │
    ▼
获取点击位置的格子索引
    │
    ▼
检查格子是否已有建筑
    │
    ├── 已有建筑 → 删除旧建筑
    │
    ▼
创建新建筑对象 (BTLBuilding)
    │
    ▼
设置建筑属性 (类型、等级、HP等)
    │
    ▼
添加到建筑列表
    │
    ▼
更新文件头中的建筑数量
    │
    ▼
标记数据已修改
```

### 省份生成流程

```
用户选择"生成省份"功能
    │
    ▼
generate_provinces_for_all_isolated_capitals()
    │
    ├── 遍历所有建筑
    ├── 找出省会类型的建筑
    └── 为每个省会创建省份
    │
    ▼
expand_all_provinces_to_fill_map()
    │
    ├── 从每个省会开始
    ├── 使用BFS/泛洪算法扩展
    ├── 检查地形连续性
    └── 填充所有未分配格子
    │
    ▼
更新省份数据
```

---

## 使用示例

### 创建建筑编辑器

```python
from MainWindow import Render_Map
from StageFunction import Building_Editor

# 假设已有 Render_Map 对象
render_map = Render_Map(screen, parsed_data, 'campaign')

# 创建建筑编辑器
building_editor = Building_Editor(render_map)

# 放置城市
building_editor.place_building(
    hex_index=100,
    building_type=0,  # 城市
    building_name="洛阳",
    level=5
)

# 放置港口
building_editor.place_building(
    hex_index=200,
    building_type=2,  # 港口
    building_name="港口",
    level=3
)

# 获取所有城市
cities = building_editor.get_buildings_by_type(0)
```

### 编辑省份

```python
from StageFunction import Belong_Editor

# 创建归属编辑器
belong_editor = Belong_Editor(render_map)

# 设置格子所属国家
belong_editor.set_belong(100, 1)  # 格子100属于国家1

# 泛洪填充
belong_editor.flood_fill_belong(100, 1)  # 从格子100开始填充国家1

# 复制粘贴
belong_editor.copy_belong(100)  # 复制格子100的归属
belong_editor.paste_belong(200)  # 粘贴到格子200
```

---

## 性能优化

### 1. 缓存优化

```python
# 省份边界缓存
self.province_border_cache = {}

def draw_province_borders(self, surface, ...):
    # 检查缓存
    cache_key = (col, row, province_value)
    if cache_key in self.province_border_cache:
        # 使用缓存的边界
        ...
```

### 2. 泛洪算法优化

```python
def flood_fill_provinces(self, start_col, start_row, new_province_value):
    # 使用集合存储已访问节点
    visited = set()
    queue = [(start_col, start_row)]
    
    while queue:
        # BFS遍历
        ...
```

---

*文档生成时间: 2026-04-15*
