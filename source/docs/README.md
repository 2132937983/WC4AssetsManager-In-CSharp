# WC4 地图编辑器 - 原项目代码分析文档

## 目录

1. [项目概述](#项目概述)
2. [项目结构](#项目结构)
3. [核心模块详解](#核心模块详解)
   - [MainWindow.py - 主窗口模块](MainWindow详解.md)
   - [Functions.py - 数据结构模块](Functions详解.md)
   - [Functions.py - 数据结构补充](Functions数据结构补充.md)
   - [StageFunction.py - 剧本编辑模块](StageFunction详解.md)
   - [MainWindowTools.py - 工具类模块](MainWindowTools.md)
   - [AssistWindow.py - 辅助窗口模块](AssistWindow详解.md)
   - [StageParser.py - 剧本解析模块](StageParser详解.md)
   - [ConquestParser.py - 征服模式解析模块](ConquestParser详解.md)
   - [WorldParser.py - 地图文件解析模块](WorldParser详解.md)
4. [数据模型类汇总](#数据模型类汇总)
5. [文件格式说明](#文件格式说明)
6. [编辑器交互说明](#编辑器交互说明)

---

## 项目概述

**WC4 地图编辑器** 是一个基于 Python (pygame) 开发的三国志4 (Romance of the Four Kingdoms IV) 游戏地图编辑器。

### 主要功能

- 地图渲染与编辑
- 剧本 (Campaign) 文件创建与修改
- 征服 (Conquest) 模式文件解析
- 地形、建筑、军队、军团等游戏元素编辑
- 世界地图 (.world/.bin) 文件解析

### 技术栈

- **GUI框架**: pygame + pygame_gui
- **图像处理**: OpenCV (可选)
- **数据解析**: 二进制文件解析（BTL格式）

---

## 项目结构

```
source/map_editor/
├── MainWindow.py          # 主窗口和场景管理
├── MainWindowTools.py     # 工具类（相机、地形助手、地图编辑）
├── Functions.py           # BTL数据结构类定义
├── StageFunction.py       # 剧本编辑功能类
├── AssistWindow.py        # 辅助窗口（河流修改、值输入等）
├── StageParser.py         # 剧本文件解析器
├── ConquestParser.py      # 征服模式解析器
├── WorldParser.py        # 世界地图文件解析器
├── main.py               # 程序入口
├── MapTerrian/           # 地形图片资源
├── StageMark/            # 剧本标记（军队图标、国旗等）
└── MapEditor/            # 编辑器配置
```

---

## 文档列表

| 文档文件 | 内容说明 |
|----------|----------|
| [MainWindow详解.md](MainWindow详解.md) | 主窗口模块：场景管理、事件流程、渲染架构 |
| [Functions详解.md](Functions详解.md) | BTL文件头和军团数据结构详解 |
| [Functions数据结构补充.md](Functions数据结构补充.md) | 建筑、军队等数据结构详解 |
| [StageFunction详解.md](StageFunction详解.md) | 剧本编辑器：建筑/军队/军团编辑器 |
| [MainWindowTools.md](MainWindowTools.md) | 工具类：相机、地形助手、地图编辑 |
| [AssistWindow详解.md](AssistWindow详解.md) | 辅助窗口：河流修改、数值输入、画家工具 |
| [StageParser详解.md](StageParser详解.md) | 剧本文件解析器和保存流程 |
| [ConquestParser详解.md](ConquestParser详解.md) | 征服模式文件解析 |
| [WorldParser详解.md](WorldParser详解.md) | 世界地图文件解析 |

---

## 数据模型类汇总

### BTL 文件数据结构对应关系

| 数据类型 | 解析类 | 字节数 | 主要属性 |
|---------|--------|--------|---------|
| 文件头 | `BTLHeaderParser` | 128 | 版本、地图尺寸、各类数量 |
| 军团 | `BTLLegion` | 300 | 国家ID、初始资源、科技等级 |
| 地形 | `BTLTerrain` | 16 | hex_value, river_value, province_id |
| 省份 | `BTLProvince` | 2 | 省份归属 |
| 建筑 | `BTLBuilding` | 32 | coordinate, building_type, level |
| 军队 | `BTLArmy` | 48 | coordinate, unit_type, level, hp |
| 陷阱 | `BTLTrap` | 16 | coordinate, type, probability |
| 天气 | `BTLWeather` | 16 | turn, type, area |
| 事件 | `BTLEvent` | 44 | trigger, condition, action |

### World 文件数据结构

| 数据类型 | 解析类 | 字节数 | 说明 |
|---------|--------|--------|------|
| 文件头 | - | 16 | 魔数、地图尺寸 |
| 地形 | `BTLTerrain` | 16×N | 三层地形+装饰+河流 |
| 省份 | `BTLProvince` | 2×N | 省份归属 |

---

## 文件格式说明

### 支持的文件类型

| 文件类型 | 扩展名 | 说明 |
|---------|--------|------|
| 剧本文件 | `.btl` | 战役剧本，可编辑所有元素 |
| 征服文件 | `.conquest` | 征服模式剧本 |
| 地图文件 | `.bin` / `.world` | 二进制地图数据 |
| 地形图片 | `.png` | 64×64 像素地图块 |
| 标记图片 | `.png` | 军队图标、国旗等 |

### 地图坐标系统

- 使用**六边形网格**坐标系统
- 支持**偏移坐标** (Offset Coordinates) 存储
- 每个六边形包含：
  - 地形类型 (terrain_type，3层)
  - 省份归属 (province_id)
  - 河流信息 (river_value，6位掩码)
  - 建筑/军队等上层数据

---

## 编辑器交互说明

### 鼠标操作

| 操作 | 功能 |
|------|------|
| 左键点击 | 选中格子/放置建筑 |
| 右键拖拽 | 绘制地形（地图编辑模式）|
| 中键拖拽 | 平移地图 |
| 滚轮 | 缩放地图 |
| Tab 键 | 打开设置菜单 |

### 编辑模式

1. **剧本编辑模式 (script)**: 编辑剧本元素（建筑、军队、军团等）
2. **地图编辑模式 (map)**: 编辑地形、河流等

---

## 版本历史

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-04-15 | 1.0 | 初始文档创建 |

---

*文档生成时间: 2026-04-15*
