# AssistWindow.py - 辅助窗口模块详解

## 概述

`AssistWindow.py` 是 WC4 地图编辑器的辅助窗口模块，提供了各种辅助编辑功能，包括河流修改、数值输入、画家工具等。

---

## 类层次结构

```
Assist_River_Modify     (河流修改窗口)
    │
    ├── Probability_Input  (概率输入窗口)
    │       │
    │       └── SingleValue_Input (单值输入窗口)
    │       │
    │       └── DoubleValue_Input (双值输入窗口)
    │
    └── Painter           (画家工具)
```

---

## 1. Assist_River_Modify - 河流修改窗口

**职责**: 提供图形化界面设置六边形格子的河流值。

### 类定义

```python
class Assist_River_Modify:
    def __init__(self, render_scene, terrain_obj):
        """初始化河流修改窗口
        
        Args:
            render_scene: 渲染场景对象
            terrain_obj: 地形对象，用于获取和设置河流值
        """
```

### 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `render_scene` | Render_Scene | 渲染场景 |
| `terrain_obj` | BTLTerrain | 地形对象 |
| `active` | bool | 窗口是否激活 |
| `width` | int | 面板宽度 (400) |
| `height` | int | 面板高度 (500) |
| `x` | int | 面板X坐标 |
| `y` | int | 面板Y坐标 |
| `river_edges` | list | 6条河流边状态 (True/False) |

### 颜色定义

| 属性名 | 值 | 用途 |
|--------|-----|------|
| `BACKGROUND_COLOR` | (240, 240, 240) | 面板背景 |
| `TEXT_COLOR` | (0, 0, 0) | 文本颜色 |
| `BUTTON_COLOR` | (70, 130, 180) | 按钮颜色 |
| `BUTTON_HOVER_COLOR` | (100, 149, 237) | 按钮悬停颜色 |
| `ACTIVE_BUTTON_COLOR` | (50, 205, 50) | 激活状态按钮颜色 |
| `HEXAGON_COLOR` | (200, 200, 200) | 六边形颜色 |
| `RIVER_COLOR` | (30, 144, 255) | 河流颜色 |

### 核心方法

#### init_ui

```python
def init_ui(self):
    """初始化UI元素
    
    创建以下元素:
    - 六边形中心点
    - 河流边按钮位置
    - 完成按钮
    - 取消按钮
    - 测试按钮
    """
    
    # 六边形参数
    self.hex_center_x = self.x + self.width // 2
    self.hex_center_y = self.y + self.height // 2 - 50
    self.hex_radius = 80
    
    # 河流边按钮半径
    self.edge_button_radius = 20
    
    # 按钮位置
    self.done_button_rect = pygame.Rect(...)
    self.cancel_button_rect = pygame.Rect(...)
    self.test_button_rect = pygame.Rect(...)
```

#### load_river_status

```python
def load_river_status(self):
    """从地形对象加载当前的河流状态
    
    支持三种加载方式:
    1. get_river_edges_status() 方法
    2. river_value 属性
    3. river 属性
    
    河流边对应关系:
    - 位0 (0x01): 边0
    - 位1 (0x02): 边1
    - 位2 (0x04): 边2
    - 位3 (0x08): 边3
    - 位4 (0x10): 边4
    - 位5 (0x20): 边5
    """
```

#### handle_events

```python
def handle_events(self, event):
    """处理窗口事件
    
    Args:
        event: pygame事件对象
        
    处理内容:
    - ESC键: 关闭窗口
    - 鼠标点击: 切换河流边状态
    - 按钮点击: 完成/取消/测试
    """
```

#### handle_mouse_click

```python
def handle_mouse_click(self, pos):
    """处理鼠标点击
    
    Args:
        pos: 鼠标位置 (x, y)
        
    检测区域:
    - 河流边按钮 (6个)
    - 完成按钮
    - 取消按钮
    - 测试按钮
    
    Returns:
        bool: 是否处理了点击事件
    """
```

#### get_edge_position

```python
def get_edge_position(self, edge_index):
    """获取指定边的位置
    
    Args:
        edge_index: 边索引 (0-5)
        
    Returns:
        tuple: 边的中心位置 (x, y)
        
    六边形边编号:
         0
        / \
       5   1
       |   |
       4   2
        \ /
         3
    """
    
    angle = edge_index * (2 * math.pi / 6) - math.pi / 2
    x = self.hex_center_x + self.hex_radius * math.cos(angle)
    y = self.hex_center_y + self.hex_radius * math.sin(angle)
    return (int(x), int(y))
```

#### toggle_river_edge

```python
def toggle_river_edge(self, edge_index):
    """切换指定边的河流状态
    
    Args:
        edge_index: 边索引 (0-5)
        
    流程:
    1. 切换边状态
    2. 调用 MapEditor.toggle_river_edge()
    3. 记录日志
    """
```

#### apply_river_changes

```python
def apply_river_changes(self):
    """应用河流更改
    
    流程:
    1. 计算新的河流值 (6位掩码)
    2. 更新地形对象的 river_value
    3. 同步邻居格子的河流边
    4. 更新主窗口的六边形网格数据
    5. 标记缓存为脏，触发重新绘制
    
    河流值计算:
    new_river_value = sum(1 << i for i, has_river in enumerate(river_edges))
    """
```

### 绘制方法

#### draw

```python
def draw(self):
    """绘制河流设置面板
    
    绘制顺序:
    1. 半透明遮罩背景
    2. 面板背景
    3. 标题
    4. 六边形
    5. 河流边
    6. 河流边按钮
    7. 功能按钮
    """
```

#### draw_hexagon

```python
def draw_hexagon(self):
    """绘制中心六边形
    
    使用6个顶点绘制正六边形
    """
```

#### draw_river_edges

```python
def draw_river_edges(self):
    """绘制有河流的边
    
    对 river_edges[i] == True 的边绘制蓝色线条
    """
```

#### draw_edge_buttons

```python
def draw_edge_buttons(self):
    """绘制河流边按钮
    
    按钮颜色:
    - 有河流: ACTIVE_BUTTON_COLOR (绿色)
    - 无河流: BUTTON_COLOR (蓝色)
    - 悬停: BUTTON_HOVER_COLOR
    
    显示边编号 (1-6)
    """
```

---

## 2. Probability_Input - 概率输入窗口

**职责**: 提供数值输入功能，支持随机概率、地形绿化、地图尺寸调整等场景。

### 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `render_scene` | Render_Scene | 渲染场景 |
| `running` | bool | 窗口是否运行 |
| `confirmed` | bool | 用户是否确认 |
| `value` | int | 输入的数值 |
| `is_random_decoration_type` | bool | 是否随机地形 |
| `is_greenify_terrain` | bool | 是否绿化地形 |
| `is_map_resize` | bool | 是否地图调整 |

### 核心方法

#### show_dialog

```python
def show_dialog(self):
    """显示对话框并获取用户输入
    
    Returns:
        int: 用户输入的数值
        None: 用户取消
    """
```

#### apply_random_decoration

```python
def apply_random_decoration(self, probability):
    """应用随机地形装饰
    
    Args:
        probability: 随机概率 (0-100)
        
    流程:
    1. 遍历所有地形格子
    2. 根据概率随机设置装饰类型
    3. 标记缓存为脏
    """
```

---

## 3. SingleValue_Input - 单值输入窗口

**职责**: 简单数值输入窗口，用于输入单个数值。

### 核心方法

```python
def show_dialog(self):
    """显示单值输入对话框
    
    返回用户输入的单个整数值
    """
```

---

## 4. DoubleValue_Input - 双值输入窗口

**职责**: 双数值输入窗口，用于输入两个相关数值（如坐标、范围等）。

### 核心方法

```python
def show_dialog(self):
    """显示双值输入对话框
    
    返回用户输入的两个整数值 (value1, value2)
    """
```

---

## 5. Painter - 画家工具

**职责**: 提供批量绘制功能，用于快速修改多个格子的属性。

### 核心属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| `brush_size` | int | 画笔大小 |
| `brush_type` | str | 画笔类型 |
| `color` | tuple | 画笔颜色 |
| `is_painting` | bool | 是否正在绘制 |

### 核心方法

#### start_painting

```python
def start_painting(self, hex_index):
    """开始绘制
    
    Args:
        hex_index: 起始格子索引
    """
```

#### paint

```python
def paint(self, hex_index):
    """绘制单个格子
    
    Args:
        hex_index: 格子索引
        
    根据画笔类型应用相应的修改
    """
```

#### stop_painting

```python
def stop_painting(self):
    """停止绘制
    
    保存修改并清理状态
    """
```

---

## 窗口交互流程

### 河流修改窗口流程

```
用户点击 "设置河流" 按钮
    │
    ├── 创建 Assist_River_Modify 对象
    │   │
    │   └── load_river_status()
    │       └── 从地形对象加载当前河流值
    │
    ├── 显示窗口 (draw())
    │   ├── 绘制半透明遮罩
    │   ├── 绘制六边形
    │   └── 绘制6个边按钮
    │
    └── 进入事件循环
        │
        ├── 用户点击边按钮
        │   ├── toggle_river_edge(i)
        │   └── 更新按钮颜色
        │
        ├── 用户点击 "完成"
        │   ├── apply_river_changes()
        │   │   ├── 计算新河流值
        │   │   ├── 更新地形对象
        │   │   └── 同步邻居格子
        │   └── 关闭窗口
        │
        ├── 用户点击 "取消"
        │   └── 关闭窗口 (不保存)
        │
        └── 用户点击 ESC
            └── 关闭窗口
```

---

## 数值输入流程

```
用户点击需要输入数值的操作
    │
    ├── 创建输入窗口对象
    │   ├── Probability_Input
    │   ├── SingleValue_Input
    │   └── DoubleValue_Input
    │
    ├── 显示输入界面
    │
    └── 等待用户输入
        │
        ├── 用户输入数值并确认
        │   └── 返回数值
        │
        └── 用户取消
            └── 返回 None
```

---

## 使用示例

### 使用河流修改窗口

```python
from AssistWindow import Assist_River_Modify
from MainWindow import Render_Map

# 假设已有 render_map 和 terrain 对象
# terrain = render_map.terrain[100]

# 创建河流修改窗口
river_modify = Assist_River_Modify(render_scene, terrain)

# 激活窗口
river_modify.active = True

# 在事件循环中处理
while running:
    for event in events:
        if river_modify.active:
            river_modify.handle_events(event)
    
    if river_modify.active:
        river_modify.draw()
```

### 使用概率输入

```python
from AssistWindow import Probability_Input

# 创建概率输入窗口
input_dialog = Probability_Input(render_scene)

# 显示对话框
probability = input_dialog.show_dialog()

if probability is not None:
    # 应用随机地形装饰
    input_dialog.apply_random_decoration(probability)
```

### 使用画家工具

```python
from AssistWindow import Painter

# 创建画家工具
painter = Painter(render_map)

# 设置画笔
painter.brush_size = 3
painter.brush_type = 'terrain'
painter.terrain_type = 1  # 陆地

# 开始绘制
painter.start_painting(100)

# 移动绘制
painter.paint(101)
painter.paint(102)

# 停止绘制
painter.stop_painting()
```

---

## 图形界面布局

### 河流修改窗口布局

```
┌─────────────────────────────────────┐
│            设置河流                   │
│                                     │
│                 1                   │
│               ╱   ╲                 │
│          6  ╱       ╲  2          │
│            ╱           ╲          │
│           ╱             ╲         │
│          ╱               ╲        │
│        5                 ○───1───2 │
│        │                 │        │
│        │    (六边形)     │        │
│        │                 │        │
│        4─────────────────3        │
│           ╱               ╲       │
│          ╱      ●          ╲     │
│         ╱    (边按钮)        ╲   │
│        ╱                      ╲  │
│    4───5───6                    │
│                                     │
├─────────────────────────────────────┤
│  [测试]   [取消]   [完成]           │
└─────────────────────────────────────┘

说明:
- 数字 1-6: 边编号
- 圆圈: 边按钮
- 实线: 有河流的边
- 虚线: 无河流的边
```

---

## 河流边编号说明

```
六边形边编号 (从顶部顺时针):

        0
       ╱ ╲
      ╱   ╲
     ╱     ╲
    5       1
    │       │
    │       │
    │       │
    4       2
     ╲     ╱
      ╲   ╱
       ╲ ╱
        3

河流值位掩码:
- 0x01 (位0): 边0
- 0x02 (位1): 边1
- 0x04 (位2): 边2
- 0x08 (位3): 边3
- 0x10 (位4): 边4
- 0x20 (位5): 边5

示例:
- 0x00: 无河流
- 0x3F: 所有边都有河流
- 0x15: 边0, 边2, 边4有河流
```

---

*文档生成时间: 2026-04-15*
