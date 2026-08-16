# MainWindow.py - 主窗口模块详解

## 概述

`MainWindow.py` 是 WC4 地图编辑器的核心模块，负责管理应用程序的主窗口、场景切换和地图渲染。本模块采用场景（Scene）模式设计，实现了主菜单和地图编辑界面之间的切换。

---

## 类层次结构

```
Basic_Scene (基类)
    │
    ├── Begining_Scene (开始场景)
    │
    └── Render_Scene (渲染场景)
            │
            └── Render_Map (地图渲染)

MainWindow (主窗口控制器)
    │
    └── Setting_character (设置弹窗)
```

---

## 1. MainWindow - 主窗口类

**职责**: 应用程序主控制器，管理游戏主循环和场景切换。

### 类定义

```python
class MainWindow:
    def __init__(self):
        """初始化主窗口"""
```

### 初始化流程

```python
def __init__(self):
    """初始化流程"""
    # 1. 初始化 pygame
    pygame.init()
    
    # 2. 设置窗口参数
    self.width = 1200
    self.height = 600
    self.screen = pygame.display.set_mode((self.width, self.height))
    pygame.display.set_caption("祝的编辑器")
    
    # 3. 创建 UI 管理器
    self.ui_manager = pygame_gui.UIManager((self.width, self.height))
    
    # 4. 创建初始场景
    self.begining_scene = Begining_Scene(self.screen, self.ui_manager, self)
    
    # 5. 设置时钟
    self.clock = pygame.time.Clock()
    
    # 6. 运行标志
    self.running = True
```

### 核心方法

#### run - 主循环

```python
def run(self):
    """主游戏循环
    
    循环流程:
    1. 处理事件
    2. 更新场景
    3. 绘制界面
    4. 控制帧率
    """
    
    while self.running:
        # 计算时间增量
        time_delta = self.clock.tick(60) / 1000.0
        
        # 获取所有事件
        for event in pygame.event.get():
            # 退出事件
            if event.type == pygame.QUIT:
                self.running = False
            
            # 传递事件给 UI 管理器
            self.ui_manager.process_events(event)
            
            # 根据当前场景处理事件
            if self.current_scene:
                self.current_scene.handle_event(event)
        
        # 更新 UI 管理器
        self.ui_manager.update(time_delta)
        
        # 更新当前场景
        if self.current_scene:
            self.current_scene.update(time_delta)
        
        # 绘制当前场景
        if self.current_scene:
            self.current_scene.draw()
        
        # 更新 UI 层
        self.ui_manager.draw_ui(self.screen)
        
        # 刷新显示
        pygame.display.flip()
```

#### switch_to_render_scene - 切换到渲染场景

```python
def switch_to_render_scene(self, file_path, file_type):
    """切换到地图渲染场景
    
    Args:
        file_path: 地图文件路径
        file_type: 文件类型 ('campaign', 'conquest', 'bin')
        
    流程:
    1. 解析地图文件
    2. 创建 Render_Map 对象
    3. 创建 Render_Scene 对象
    4. 设置为当前场景
    """
```

#### switch_to_begining_scene - 切换回开始场景

```python
def switch_to_begining_scene(self):
    """切换回开始场景
    
    释放渲染场景资源，返回主菜单
    """
    if hasattr(self, 'render_scene'):
        del self.render_scene
    if hasattr(self, 'render_map'):
        del self.render_map
    
    self.current_scene = self.begining_scene
```

---

## 2. Basic_Scene - 场景基类

**职责**: 定义场景的通用接口，所有场景继承此类。

### 类定义

```python
class Basic_Scene:
    def __init__(self, screen, ui_manager, main_window):
        self.screen = screen
        self.ui_manager = ui_manager
        self.main_window = main_window
    
    def handle_event(self, event):
        """处理事件的抽象方法"""
        raise NotImplementedError
    
    def update(self, time_delta):
        """更新场景的抽象方法"""
        raise NotImplementedError
    
    def draw(self):
        """绘制场景的抽象方法"""
        raise NotImplementedError
```

---

## 3. Begining_Scene - 开始场景

**职责**: 显示主菜单界面，提供文件选择和功能入口。

### 类定义

```python
class Begining_Scene(Basic_Scene):
    def __init__(self, screen, ui_manager, main_window):
        """初始化开始场景"""
```

### 初始化流程

```python
def __init__(self, screen, ui_manager, main_window):
    # 调用父类初始化
    super().__init__(screen, ui_manager, main_window)
    
    # 加载背景图片
    self.background_image = load_image("background.png")
    
    # 创建菜单按钮
    self.create_menu_buttons()
    
    # 创建文本标签
    self.create_labels()
```

### create_menu_buttons - 创建菜单按钮

```python
def create_menu_buttons(self):
    """创建主菜单按钮
    
    菜单项:
    - 新建剧本
    - 打开剧本
    - 打开征服文件
    - 打开地图文件
    - 设置
    - 退出
    """
    
    button_data = [
        ("新建剧本", self.on_new_campaign),
        ("打开剧本", self.on_open_campaign),
        ("打开征服文件", self.on_open_conquest),
        ("打开地图文件", self.on_open_bin),
        ("设置", self.on_settings),
        ("退出", self.on_exit)
    ]
    
    # 创建按钮...
```

### handle_event - 事件处理

```python
def handle_event(self, event):
    """处理开始场景的事件
    
    主要处理:
    - 按钮点击事件
    - 按钮悬停效果
    """
    
    if event.type == pygame.USEREVENT:
        if event.user_type == pygame_gui.UI_BUTTON_PRESSED:
            button_id = event.ui_element.unique_id
            
            # 根据按钮ID执行对应操作
            if button_id == "new_campaign":
                self.on_new_campaign()
            elif button_id == "open_campaign":
                self.on_open_campaign()
            # ...
```

### draw - 绘制场景

```python
def draw(self):
    """绘制开始场景
    
    绘制顺序:
    1. 绘制背景图片
    2. 绘制UI覆盖层
    """
    
    # 绘制背景
    if self.background_image:
        self.screen.blit(self.background_image, (0, 0))
    else:
        self.screen.fill((200, 200, 200))
    
    # 绘制UI
    self.draw_ui_overlay()
```

---

## 4. Render_Scene - 渲染场景

**职责**: 管理地图编辑界面，包含左右两个面板和中央地图区域。

### 类定义

```python
class Render_Scene(Basic_Scene):
    def __init__(self, screen, ui_manager, main_window, 
                 render_map, file_type):
        """初始化渲染场景"""
```

### 界面布局

```
┌──────────┬─────────────────────────┬──────────┐
│          │                         │          │
│  左侧面板  │      中央地图区域        │  右侧面板  │
│  (工具栏)  │                         │  (信息)   │
│          │                         │          │
│  - 地形   │    [地图渲染区域]        │  格子信息  │
│  - 省份   │                         │  地形类型  │
│  - 河流   │                         │  所属国家  │
│  - 建筑   │                         │  建筑信息  │
│  - 军队   │                         │  军队信息  │
│          │                         │          │
└──────────┴─────────────────────────┴──────────┘

面板宽度 = 窗口宽度 / 6
中央区域 = 窗口宽度 * 2 / 3
```

### create_left_panel - 创建左侧面板

```python
def create_left_panel(self):
    """创建左侧工具面板
    
    包含:
    - 工具切换按钮
    - 地形选择器
    - 省份选择器
    - 河流设置
    """
    
    # 工具分类
    self.tools = {
        'terrain': '地形',
        'province': '省份',
        'river': '河流',
        'building': '建筑',
        'army': '军队'
    }
    
    # 创建工具按钮...
```

### create_right_panel - 创建右侧信息面板

```python
def create_right_panel(self):
    """创建右侧信息面板
    
    显示:
    - 选中格子的坐标
    - 地形类型
    - 省份ID
    - 所属国家
    - 建筑详情（如果有）
    - 军队详情（如果有）
    """
```

### handle_event - 事件处理

```python
def handle_event(self, event):
    """处理渲染场景的事件
    
    处理类型:
    - 按钮点击
    - 鼠标移动
    - 鼠标点击
    - 键盘输入
    """
    
    # UI事件
    if event.type == pygame.USEREVENT:
        self.handle_ui_event(event)
    
    # 鼠标事件
    elif event.type == pygame.MOUSEBUTTONDOWN:
        self.handle_mouse_down(event)
    
    elif event.type == pygame.MOUSEBUTTONUP:
        self.handle_mouse_up(event)
    
    elif event.type == pygame.MOUSEMOTION:
        self.handle_mouse_motion(event)
    
    # 键盘事件
    elif event.type == pygame.KEYDOWN:
        self.handle_key_down(event)
```

### handle_mouse_down - 鼠标按下处理

```python
def handle_mouse_down(self, event):
    """处理鼠标按下事件
    
    根据鼠标位置判断:
    - 是否在地图区域
    - 是否在UI区域
    
    Args:
        event: pygame 事件对象
    """
    
    mouse_pos = event.pos
    
    # 计算地图区域
    map_area = self.get_map_area()
    
    if map_area.collidepoint(mouse_pos):
        # 在地图区域内
        self.handle_map_click(mouse_pos, event.button)
    else:
        # 在UI区域内
        self.handle_ui_click(mouse_pos)
```

### handle_map_click - 地图点击处理

```python
def handle_map_click(self, mouse_pos, button):
    """处理地图区域点击
    
    Args:
        mouse_pos: 鼠标位置 (x, y)
        button: 鼠标按钮 (1=左键, 2=中键, 3=右键)
    
    左键: 选中格子/放置建筑
    中键: 开始拖拽
    右键: 绘制地形（地图编辑模式）
    """
    
    # 转换屏幕坐标到地图坐标
    map_pos = self.render_map.screen_to_map(mouse_pos)
    
    # 获取点击的格子
    hex_info = self.render_map.get_hex_at_position(map_pos)
    
    if hex_info:
        # 记录选中的格子
        self.selected_hex = hex_info
        
        # 根据当前工具处理
        if self.current_tool == 'terrain':
            self.handle_terrain_edit(hex_info)
        elif self.current_tool == 'building':
            self.handle_building_edit(hex_info)
        elif self.current_tool == 'army':
            self.handle_army_edit(hex_info)
```

### update - 更新场景

```python
def update(self, time_delta):
    """更新渲染场景
    
    Args:
        time_delta: 距离上一帧的时间（秒）
    
    更新内容:
    - 编辑器状态
    - 相机位置
    - 工具状态
    """
    
    # 更新编辑器
    if self.render_map.stage_editor:
        self.render_map.stage_editor.update()
    
    # 更新地图编辑器
    if self.render_map.map_editor:
        self.render_map.map_editor.update()
    
    # 更新相机
    self.render_map.camera.update(time_delta)
```

### draw - 绘制场景

```python
def draw(self):
    """绘制渲染场景
    
    绘制顺序:
    1. 绘制背景
    2. 绘制地图
    3. 绘制UI面板
    4. 绘制选中效果
    """
    
    # 绘制地图
    self.render_map.render()
    
    # 绘制左面板
    self.draw_left_panel()
    
    # 绘制右面板
    self.draw_right_panel()
    
    # 绘制悬停效果
    self.draw_hover_effect()
```

---

## 5. Render_Map - 地图渲染类

**职责**: 负责六边形地图的渲染、相机控制和地图数据管理。

### 类定义

```python
class Render_Map:
    def __init__(self, screen, parsed_data, file_type, scene=None):
        """初始化地图渲染器"""
```

### 初始化流程

```python
def __init__(self, screen, parsed_data, file_type, scene=None):
    self.screen = screen
    self.parsed_data = parsed_data  # StageParser 解析的数据
    self.file_type = file_type
    self.scene = scene
    
    # 获取窗口尺寸
    self.window_width = screen.get_width()
    self.window_height = screen.get_height()
    
    # 计算面板区域
    self.panel_width = self.window_width // 6
    self.center_x = self.panel_width
    self.center_width = self.window_width - 2 * self.panel_width
    
    # 初始化相机
    self.camera = Camera(self.window_width, self.window_height)
    
    # 初始化地形助手
    self.terrain_helper = TerrainHelper("MapTerrian")
    
    # 初始化编辑模式
    self.edit_mode = 'script'  # 'script' 或 'map'
    self.can_switch_mode = True
    
    # 初始化渲染缓存
    self.render_cache = {}
    self.hex_cache = {}
    
    # 初始化编辑器
    if self.file_type == 'bin':
        self.map_editor = MapEditor(self)
    else:
        self.stage_editor = Building_Editor(self)
```

### render - 主渲染方法

```python
def render(self):
    """渲染地图的主方法
    
    渲染层次（从下到上）:
    1. 背景图片
    2. 六边形网格
    3. 地形层
    4. 第二层地形
    5. 建筑层
    6. 军队层
    7. 选中高亮
    8. 河流
    """
    
    # 清屏
    self.screen.fill((0, 0, 0))
    
    # 1. 渲染背景
    self.render_background()
    
    # 2. 渲染六边形网格
    if self.show_grid:
        self.render_hexagon_layer()
    
    # 3. 渲染地形
    self.render_terrain()
    
    # 4. 渲染第二层地形
    if self.show_second_layer:
        self.render_second_layer()
    
    # 5. 渲染建筑
    self.render_buildings()
    
    # 6. 渲染军队
    self.render_armies()
    
    # 7. 渲染高亮
    self.render_highlight()
    
    # 8. 渲染河流
    self.render_rivers()
```

### render_terrain - 渲染地形

```python
def render_terrain(self):
    """渲染地形层
    
    渲染策略:
    1. 计算可见区域内的格子
    2. 检查渲染缓存
    3. 使用地形助手获取纹理
    4. 绘制到屏幕
    """
    
    # 获取相机变换后的可见区域
    visible_rect = self.get_visible_rect()
    
    # 遍历可见区域内的所有格子
    for row in range(visible_rect.top, visible_rect.bottom + 1):
        for col in range(visible_rect.left, visible_rect.right + 1):
            # 获取格子索引
            index = self.coord_to_index(col, row)
            
            # 获取地形数据
            terrain = self.parsed_data.terrain[index]
            
            # 从缓存或创建新纹理
            if index in self.hex_cache:
                texture = self.hex_cache[index]
            else:
                # 从地形助手获取纹理
                texture = self.terrain_helper.get_terrain_image(
                    terrain.hex_value
                )
                self.hex_cache[index] = texture
            
            # 计算屏幕坐标
            screen_pos = self.map_to_screen(col, row)
            
            # 绘制纹理
            self.screen.blit(texture, screen_pos)
```

### screen_to_map - 屏幕坐标转地图坐标

```python
def screen_to_map(self, screen_pos):
    """将屏幕坐标转换为地图坐标
    
    Args:
        screen_pos: 屏幕坐标 (x, y)
        
    Returns:
        tuple: 地图坐标 (col, row)
    """
    
    # 应用相机偏移
    camera_offset = self.camera.offset
    
    # 应用相机缩放
    zoom = self.camera.zoom
    
    # 转换为地图坐标
    map_x = (screen_pos[0] - self.center_x) / zoom + camera_offset.x
    map_y = screen_pos[1] / zoom + camera_offset.y
    
    # 转换为列行索引
    col = int(map_x / HEX_WIDTH)
    row = int(map_y / HEX_HEIGHT)
    
    return col, row
```

### map_to_screen - 地图坐标转屏幕坐标

```python
def map_to_screen(self, col, row):
    """将地图坐标转换为屏幕坐标
    
    Args:
        col: 列索引
        row: 行索引
        
    Returns:
        tuple: 屏幕坐标 (x, y)
    """
    
    # 计算六边形中心点
    x = col * HEX_WIDTH + (row % 2) * (HEX_WIDTH / 2)
    y = row * HEX_HEIGHT * 0.75
    
    # 应用相机偏移
    x -= self.camera.offset.x
    y -= self.camera.offset.y
    
    # 应用缩放
    x = self.center_x + (x - self.center_x) * self.camera.zoom
    y = y * self.camera.zoom
    
    return x, y
```

---

## 6. Setting_character - 设置弹窗

**职责**: 地图编辑器设置界面。

### 类定义

```python
class Setting_character:
    def __init__(self, screen, main_window):
        """初始化设置弹窗"""
```

### 设置项

| 设置项 | 类型 | 说明 |
|--------|------|------|
| 显示网格 | 复选框 | 显示/隐藏六边形网格 |
| 显示河流 | 复选框 | 显示/隐藏河流 |
| 显示雪地变体 | 复选框 | 显示/隐藏雪地变体 |
| 地图缩放 | 滑块 | 缩放级别 0.1x - 5.0x |

---

## 事件流程图

### 主程序启动流程

```
main.py
    │
    ▼
MainWindow.__init__()
    │
    ├── pygame.init()
    ├── 创建 UI 管理器
    ├── 创建 Begining_Scene
    └── 设置当前场景
    │
    ▼
MainWindow.run()
    │
    ├── while running:
    │   │
    │   ├── 处理 pygame.QUIT 事件
    │   ├── ui_manager.process_events()
    │   ├── current_scene.handle_event()
    │   ├── ui_manager.update()
    │   ├── current_scene.update()
    │   ├── current_scene.draw()
    │   ├── ui_manager.draw_ui()
    │   └── pygame.display.flip()
    │
    ▼
pygame.quit()
```

### 文件打开流程

```
用户点击 "打开剧本"
    │
    ▼
Begining_Scene.on_open_campaign()
    │
    ├── 显示文件选择对话框
    ├── 获取文件路径
    │
    ▼
MainWindow.switch_to_render_scene(file_path, 'campaign')
    │
    ├── StageParser.load_hex_file()
    ├── 获取所有数据
    ├── 创建 Render_Map()
    ├── 创建 Render_Scene()
    └── 设置 current_scene = render_scene
    │
    ▼
显示地图编辑界面
```

---

## 状态机

### 场景状态

```
┌─────────────┐
│   RUNNING    │◄──────────────────┐
└──────┬──────┘                   │
       │                          │
       │ user clicks              │
       │ "open file"              │
       ▼                          │
┌─────────────┐                   │
│ BEGINING    │                   │
│ SCENE       │                   │
└──────┬──────┘                   │
       │                          │
       │ scene.switch()           │
       ▼                          │
┌─────────────┐                   │
│  RENDER     │                   │
│  SCENE      │───────────────────┘
└─────────────┘
       │
       │ user clicks
       │ "back to menu"
       ▼
   (返回 BEGINING SCENE)
```

### 编辑模式

```
edit_mode: 'script' | 'map'
    │
    ├── 'script' 剧本编辑模式:
    │   ├── Building_Editor (建筑编辑)
    │   ├── Army_Editor (军队编辑)
    │   ├── Legion_Editor (军团编辑)
    │   └── Belong_Editor (所属编辑)
    │
    └── 'map' 地图编辑模式:
        └── MapEditor (地形编辑)
            ├── 绘制地形
            ├── 修改河流
            └── 调整省份
```

---

## 性能优化策略

### 1. 渲染缓存

```python
# Render_Map.__init__()
self.render_cache = {}
self.last_camera_pos = (0, 0)
self.last_camera_zoom = 1.0

def render(self):
    # 检查缓存是否有效
    cache_key = (
        self.camera.position,
        self.camera.zoom
    )
    
    if cache_key != self.last_cache_key:
        # 缓存失效，需要重新渲染
        self.cache_valid = False
    else:
        # 使用缓存
        self.screen.blit(self.cached_surface, (0, 0))
        return
```

### 2. 视锥剔除

```python
def get_visible_hexes(self):
    """获取可见区域内的格子
    
    只渲染屏幕可见的格子，提高性能
    """
    
    visible_rect = self.get_visible_rect()
    
    for row in range(visible_rect.top, visible_rect.bottom + 1):
        for col in range(visible_rect.left, visible_rect.right + 1):
            yield col, row
```

### 3. 纹理缓存

```python
# TerrainHelper 使用 LRU 缓存
self.texture_cache = {}
self.max_cache_size = 100
self.cache_access_order = []

def get_terrain_image(self, terrain_id):
    if terrain_id in self.texture_cache:
        # 移到缓存末尾（最近使用）
        self.cache_access_order.remove(terrain_id)
        self.cache_access_order.append(terrain_id)
        return self.texture_cache[terrain_id]
    
    # 加载纹理
    texture = self.load_texture(terrain_id)
    
    # 检查缓存大小
    if len(self.texture_cache) >= self.max_cache_size:
        # 移除最旧的缓存
        oldest = self.cache_access_order.pop(0)
        del self.texture_cache[oldest]
    
    # 添加新缓存
    self.texture_cache[terrain_id] = texture
    self.cache_access_order.append(terrain_id)
    
    return texture
```

---

## 关键常量

```python
# 窗口尺寸
WINDOW_WIDTH = 1200
WINDOW_HEIGHT = 600

# 面板尺寸
PANEL_WIDTH = WINDOW_WIDTH // 6  # 200

# 六边形尺寸
HEX_WIDTH = 64      # 六边形宽度
HEX_HEIGHT = 64     # 六边形高度
HEX_HORIZ_SPACING = HEX_WIDTH       # 水平间距
HEX_VERT_SPACING = HEX_HEIGHT * 0.75  # 垂直间距（奇偶行偏移）

# 帧率
FPS = 60

# 编辑模式
EDIT_MODE_SCRIPT = 'script'  # 剧本编辑
EDIT_MODE_MAP = 'map'        # 地图编辑
```

---

*文档生成时间: 2026-04-15*
