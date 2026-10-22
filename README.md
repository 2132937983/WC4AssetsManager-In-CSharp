# WC4MapEditor

用 C# 重写的《世界征服者 4》（World Conqueror 4）地图与资源编辑器。

支持战役（stage）、征服（conquest）与世界地图，提供基于 SkiaSharp 的所见即所得渲染、完整的编辑模式、BTL/BIN 数据检查、JSON 数据表编辑、UI 布局编辑，以及**控制台命令 / `.zme` 脚本 / Lua / 独立 CLI / 内置 AI 助手**五条自动化通道。

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![License](https://img.shields.io/badge/license-GPL--3.0-blue)

---

## 目录

- [主要特性](#主要特性)
- [环境要求](#环境要求)
- [快速开始](#快速开始)
- [功能说明](#功能说明)
- [自动化：命令 / 脚本 / CLI](#自动化命令--脚本--cli)
- [AI 助手](#ai-助手)
- [启动参数](#启动参数)
- [目录结构](#目录结构)
- [文档](#文档)
- [开发](#开发)
- [常见问题](#常见问题)
- [已知限制](#已知限制)
- [许可证](#许可证)

---

## 主要特性

### 地图编辑

- **三种地图**：战役（`.btl`）、征服（`.btl`，含世界地图 `world{N}.bin` 与数据裁剪区）、世界地图（`.bin/.dat`）。
- **数据结构**：同时支持 v1 / v2 / v3 记录格式；写入时保留保留字节，保存后可与原文件逐字节一致。
- **编辑内容**：地形与变体、河流、海岸线、省区、归属、建筑（类型/名称/等级/事件/关键点）、单位（兵种/等级/编制/士气/将领/方案）、陷阱、军团（颜色/阵营/经济/工业/科技/战败条件/玩家控制）、事件、AI 方针、天气、战略建设、空袭、空中支援、援军。
- **工具**：框选与多边形、洪水填充、批量替换、地形识别、A\* 连接建筑、HD 导出、撤销/重做（单步或整批）。
- **模式框架**：每个编辑模式由若干 Module + 一张按键表组成，按键、分发与本地化帮助文本都从同一张表生成，帮助与真实按键不会漂移。

### 数据检查

开始界面「检查数据」是一个 BTL/BIN 检查器：

- 文件头、分块地址表、每类记录的字段解码与文件偏移；
- 虚拟化十六进制查看器，支持跳转与搜索；
- 两个文件 A/B 二进制对比；
- v1 ↔ v3 记录格式转换。

对应 CLI 命令：`inspect`、`compare`、`convert`。

### 数据表与资源

- **JSON 数据表编辑器**：部队、兵种、建筑设施、国家、国家科技、技能、将领、增益、事件、征服事件等 11 个编辑器共用一套基础设施——加载错误提示、未保存拦截、撤销/重做与历史、复制粘贴、多选删除、排序、批量编辑、搜索、F1 帮助。未知字段与文件排版在保存时原样保留。
- **布局编辑器**：按游戏 `layout.xml` 实际使用的标签与属性生成属性面板；读取 `font_resource.xml`（支持 PHONE/HD、PAD/HD、PAD/SD 与位图字体 `contentscale`）与 `image_resource.xml`；支持 stringtable 多语言、撤销重做、`.bak` 备份保存、导出 XML/PNG、增删/复制/排序控件、方向键微调、吸附网格、从图集选图。
- **资源浏览器**：按类别/扩展名/目录浏览 `assets`，支持文本预览。
- **战术地图编辑器**、**旗帜编辑器**、**将领照片编辑器**等专用场景。

### 体验

- 统一深色主题（`EditorTheme`），全部标准 WPF 控件都有隐式样式。
- 界面本地化：`zh-CN` / `en-US` 各约 1,577 个键，支持按功能拆分覆盖（`Resource/Lang/<code>/*.json`）；缺失键回退到 `zh-CN`，再回退到键名。
- 开始界面集成设置窗口（在线编辑 `setting.txt`、热重载、密钥用密码框）、音乐播放器、推荐窗口、开发者模式与加载进度。

---

## 环境要求

| 用途 | 需要 |
|---|---|
| 运行发布版 | Windows 10/11，[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)；使用 MCP 服务时还需 ASP.NET Core Runtime 10 |
| 从源码构建 | Windows 10/11，.NET 10 SDK |
| 编辑地图 | 自备游戏 `assets` 数据（见下） |

> 项目只支持 Windows：GUI 使用 WPF，渲染使用 SkiaSharp。

### 游戏资源

仓库**不包含游戏原始数据**（`.gitignore` 排除了 `**/WC4DATA`）。使用时需要把游戏的 `assets` 目录放到：

```
Resource/WC4DATA/assets/
```

常见内容：

```
assets/
  stage/stage10101.btl        # 战役
  conquest*.btl               # 征服
  world1.bin                  # 世界地图
  json/*.json                 # 各类数据表
  layout_*.xml, font_resource.xml, image_resource.xml
  stringtable*.ini
```

`Resource/Texture`、`Resource/Config`、`Resource/Lang`、`Resource/Memory` 随仓库提供；`Resource/` 下其它内容默认不入库。

---

## 快速开始

### 1. 构建

仓库没有 `.sln`，直接构建根项目（会自动带上 `src/` 下的子项目）：

```powershell
dotnet restore WC4MapEditor.csproj
dotnet build WC4MapEditor.csproj -c Debug
dotnet run --project WC4MapEditor.csproj
```

### 2. 发布

```powershell
publish.bat
```

`publish.bat` 会**先删除** `bin/`、`obj/` 与各子项目的同名目录，然后以 Release、框架依赖、单文件模式发布到 `publish/`，产物为 `publish/WC4MapEditor.exe`。

> 原生库（`libSkiaSharp`、`wpfgfx_cor3` 等）不内嵌，必须与 exe 同目录；这样启动时不需要往 `%TEMP%` 解压。

### 3. 准备数据

把游戏的 `assets` 放到 `Resource/WC4DATA/assets/`，`setting.txt` 放在 exe 同级或仓库根目录即可（程序会从 exe 目录逐级向上查找 `Resource/` 与 `setting.txt`）。

### 4. 打开地图

- 开始界面选择「战役 / 征服 / 地图」后打开文件；
- 或直接带文件启动：`WC4MapEditor.exe --open "D:\maps\stage10101.btl"`；
- 仓库里的 `TestByAI.btl` 可用于快速试手。

按 **F3** 打开控制台，可执行命令、拖入 `.lua` / `.zme` 脚本。

---

## 功能说明

### 编辑模式

渲染场景（战役 / 征服 / 世界地图）共用一套模式框架，进入场景后按数字键或空格切换：

| 模式 | 主要内容 |
|---|---|
| 地形 | 地形类型/变体、河流、海岸线、绿化、批量替换、安全缩放 |
| 省区 | 矩形/多边形选择、洪水填充、以选区创建省区、省会校验 |
| 建筑 | 增删改、移动、占领事件、工厂/研究/医疗/航空等级、关键点、首都高亮 |
| 单位 | 放置/删除/移动、兵种/等级/编制/士气/将领、方案、按概率生成、自动分配将领 |
| 陷阱 | 放置/删除/等级循环、按省份生成、随机化 |
| 军团 / 归属 | 军团列表、颜色、阵营、经济/工业/科技、玩家军团、战败条件、归属刷子 |
| 增援 | v1/v3 增援编辑、复制/删除、同步归属 |
| 事件 / 空袭 / 方针 / 天气 / 战略建设 | 独立列表窗口，字段来自 `setting.txt` 的对应配置节 |

通用操作：`Ctrl+Z` / `Ctrl+Y` 撤销重做，方向键平移，`+/-` 缩放，`Esc` 先取消当前操作再询问返回，状态栏显示模式提示、格子坐标与缩放。

### 数据检查

「检查数据」场景提供：

- **概览**：文件头、版本、分块地址与长度；
- **记录**：按块列出建筑/单位/陷阱/事件/援军等，展示解码字段与文件偏移；
- **十六进制**：虚拟化列表，支持跳转与搜索；
- **对比**：两个文件的差异高亮；
- **转换**：v1 ↔ v3 互转。

### JSON 数据表编辑器

11 个表编辑器共享同一套交互：加载失败会指出文件与原因；切场景或关窗时拦截未保存修改；支持撤销/重做与历史（`Ctrl+H`）、复制粘贴、多选删除、拖动排序、批量改字段、搜索与 F1 帮助。保存时保留未知字段和原有排版，尽量只改动改过的值。

### 布局编辑器

- 属性面板只列出游戏 `layout.xml` 真正使用的标签/属性，并按类型给出专用编辑器（枚举、颜色、图片、字体、stringtable 键、边距、模板 ID 等）；
- 画布支持预设尺寸、边距参考线、隐藏控件虚化、缺图标记；
- 支持从图集双击选图、方向键微调、吸附网格、撤销重做、保存（自动 `.bak`）、导出 XML/PNG；
- 字符串支持 `cn/tw/en/ja/ko/de/es` 多语言，`es/de` 可单独指定字体。

---

## 自动化：命令 / 脚本 / CLI

三条通道共用同一个命令解析器（`CliCommandHost`），能力取并集：

```
控制台手打命令 ─┐
.zme 脚本 ──────┼─→ CliCommandHost
Lua 脚本 ───────┘（Lua 另有 map 对象，可直接读写数据）
```

### 控制台（F3）

```text
building add 87 25 -t 15 -n 北京
belong set 87 25 3
terrain greening -p 60
legion list
trap generate -1 40
screenshot shots\city.png -c 120 -r 78 --w 40 --h 30
```

- `help` 查看命令树；↑/↓ 翻历史；拖入 `.zme` / `.lua` 文件可直接执行；
- 任意命令加 `--out` 可把输出另存为日志（默认写到 `Logs/`）。

完整命令参考见 [docs/cli-commands.md](docs/cli-commands.md)。

### `.zme` 指令脚本

一行一条命令，适合固定流程的批量操作；拖进控制台或 `run xxx.zme` 执行，语义见 [docs/zme-scripts.md](docs/zme-scripts.md)。

### Lua 脚本（MoonSharp）

`map` 对象暴露建筑、单位、军团、地形、归属、省区、七类记录与截图接口。**一次执行 = 一个撤销单元**，改错了 `Ctrl+Z` 一次性退回。

内置的 AI 工具箱（`scripts/ai/`）把常用操作封装成高层接口：

```lua
local ai = require('ai')

ai.inspect.audit()                       -- 全景体检
ai.building.list{ type = 11, belong = 2 }
ai.army.bulk{ field = 'Level', value = 1, op = 'add', belong = 2, dry_run = true }
ai.legion.set{ index = 0, field = 'InitialEconomy', value = 2000 }
ai.terrain.belong_region{ col1 = 0, row1 = 0, col2 = 40, row2 = 30, value = 3 }
```

也可直接运行现成脚本：

```
lua scripts/ai/audit.lua     # 体检
lua scripts/ai/help.lua      # 速查表
```

完整接口参考见 [docs/lua-scripting.md](docs/lua-scripting.md)。

### 独立 CLI（`wc4me`）

`src/WC4MapEditor.Cli` 是一个 .NET Tool（命令名 `wc4me`），不重复定义命令，而是把命令体交给同一个命令宿主，适合无界面批处理：

```powershell
# 单条/多条命令，处理一张地图
dotnet run --project src/WC4MapEditor.Cli -- modify stage map.btl terrain greening -p 60
dotnet run --project src/WC4MapEditor.Cli -- modify conquest c.btl -o out.btl info

# 执行 .zme 批处理
dotnet run --project src/WC4MapEditor.Cli -- run map.btl batch.zme -o out.btl
```

`-o/--output` 指定输出文件（默认覆盖原文件），`--no-save` 只执行不写回。

### 启动参数

| 参数 | 作用 |
|---|---|
| `--open <file>` | 启动后打开指定 `.btl` / `.bin` / `.dat` |
| `--scene <name>` | 直接进入编辑场景（`layout`、`army`、`general`、`country`、`building`、`ingame_tech`、`skill`…） |
| `--mode <name>` | 进入渲染场景后直接切到某个编辑模式 |
| `--editor <name>` | 打开某个辅助编辑器窗口 |
| `--screenshot <png>` | 启动几秒后把整个窗口渲染成 PNG 并退出（无需窗口在前台，便于自动化检查界面） |

---

## AI 助手

开始界面的 AI 按钮打开聊天窗口。助手通过工具调用操作编辑器，等效于用户点击界面或敲命令。

### 支持的协议

- **OpenAI 兼容**（`/v1/chat/completions` + `tool_calls`）：OpenAI、DeepSeek、Kimi、Qwen、Ollama、vLLM 及各类中转网关；
- **Anthropic Claude**（`/v1/messages` + `tool_use`）。

两者都支持流式输出、思维链展示、工具调用增量拼接与自动重试（网络/限流/网关临时故障重试，鉴权类错误不重试）。

### 配置

首次运行后，配置保存在 **exe 同目录的 `ai_settings.json`**（已加入 `.gitignore`）。也可以在聊天窗「设置」面板里编辑：

| 字段 | 说明 |
|---|---|
| `Provider` | `openai`（默认）或 `claude` |
| `BaseUrl` / `ApiKey` / `Model` | 接口地址、密钥、模型名 |
| `UserAgent` | 部分中转网关按它识别客户端，留空则不发送 |
| `Temperature` | 采样温度 |
| `MaxToolRounds` | 单轮对话最多工具调用轮数（默认 12，防失控） |
| `VisionEnabled` | 是否允许发图（需要多模态模型） |
| `ReasoningMode` / `ReasoningEffort` | 推理开关与强度（默认不发送，避免不认识该字段的网关报 400） |
| `StreamEnabled` | 流式输出；个别网关流式下会丢工具调用，可关掉 |
| `TextToolFallback` | 文本协议回退：模型不会 function calling 时，把 ```lua / ```cli 代码块交给编辑器执行 |
| `ToolsMode` | `native`（原生工具调用）或 `text`（不发工具定义，只收代码块） |

> 密钥只存在本机。`setting.txt` 里历史遗留的 `[AI识别]` 配置节已不再被任何代码读取，请勿把真实密钥写回那里。

### 能力

- **38 个工具**：地图/场景（`get_map_info`、`open_map`、`new_campaign`…）、命令与 Lua 执行、资源与配置查询、建筑/单位/军团属性读写、批量改字段、文件读写、记忆、截图，多步任务用的 `submit_plan`，原地重跑建图 spec 的 `apply_map_spec`，以及随机放置单位的 `generate_units`。
- **视觉**：`capture_screenshot` 支持整幅、局部放大、整个编辑器窗口三种；截图会作为图片消息回传给模型，图片带坐标标注。
- **记忆**：`save_memory` 把跨会话知识写成 Markdown 存到 `Resource/Memory/`。
- **安全**：工具直接作用在当前地图或项目文件上；写文件会自动留 `.bak`，Lua 整批可撤销。

### 内置 Lua 工具箱

`scripts/ai/` 是一套为 AI 准备的高层接口，避免它反复手写 `map.*`：

| 模块 | 内容 |
|---|---|
| `ai.inspect` | 概况、建筑/单位/归属/地形分布、异常检查 |
| `ai.building` / `ai.army` / `ai.legion` | 查询、单条修改、批量修改（支持 `dry_run`） |
| `ai.terrain` | 地形/归属/省区区域写入与全局工具 |
| `ai.record` | 事件/方针/天气/战略建设/空袭/空中支援/援军 |
| `ai.shot` | 截图（整幅/局部/界面） |
| `ai.each_cell` | 逐格遍历（引擎没有 `map.ForEachCell`） |

AI 的 `run_lua` 描述与系统提示词已经指向这套接口；接手陌生地图时先跑 `audit.lua`。

> 长脚本请分段执行。一次塞太多会被 `max_tokens` 截断，工具参数会变成半截 JSON；编辑器会拦截并提示，不会把坏参数写进对话历史。

---

## MCP Server（外部 Agent 接入）

编辑器启动后会在本机开启一个 **MCP（Model Context Protocol）** 服务，把内置 AI 的 37 个工具暴露给外部 agent（Claude Desktop、Cline、Codex 等；`submit_plan` 仅内部对话使用，不对外）：

- 地址：`http://127.0.0.1:4396/wc4-mcp`（Streamable HTTP，仅监听回环）
- 端口：环境变量 `WC4MCP_PORT` 可覆盖
- 工具：与内置 AI 完全一致（`run_lua`、`run_lua_file`、`get_map_info`、`capture_screenshot`、`geo` 命令、记忆等）
- `apply_map_spec`：在**当前已打开的地图**上重跑 mapgen spec，不新建 `.btl`；spec 里 `clear_cities=true` 时会先清旧城市再重建，可反复调整海岸线/山脉
- 截图工具会以 MCP 图片内容返回，支持视觉的客户端可以直接看图

客户端配置示例：

```json
{
  "mcpServers": {
    "wc4-editor": {
      "url": "http://127.0.0.1:4396/wc4-mcp"
    }
  }
}
```

注意：

- 服务只绑定 `127.0.0.1`，没有鉴权；不要做端口转发或暴露到局域网。
- 不需要时设 `WC4MCP=0` 可完全禁用；端口用 `WC4MCP_PORT` 修改。
- 框架依赖发布时，需要安装 **ASP.NET Core Runtime 10**（MCP 使用 ASP.NET Core 的 HTTP 传输）；缺少时编辑器仍能启动，只是 MCP 服务不可用。
- 外部 agent 与内置 AI 共用同一份工具实现，行为（`dry_run`、撤销、路径校验）一致。

---

## Agent 轨迹与评测

内置 Agent 每轮会写轨迹到 `Logs/Agent/trajectory-yyyyMMdd.jsonl`（JSONL，`Logs/` 已忽略），记录：

- 每轮请求的消息数/工具数；
- 每次工具调用的参数（截断）、结果（截断）、成功与否、耗时；
- `submit_plan` 登记的计划；
- 整轮耗时、轮数、工具调用数。

用它做回归对比：

```powershell
dotnet run --project src/WC4MapEditor.AgentEval -- .\Logs\Agent\trajectory-20261021.jsonl
```

输出对话数、工具成功率、平均/最长耗时、平均轮数、失败工具排行。换模型、改提示词或改工具实现后，跑同一批任务再比一次即可判断有没有退化。

---

## 目录结构

```
.
├─ App.xaml / App.xaml.cs        # 应用入口、DI 组合根、启动参数
├─ WC4MapEditor.csproj           # 根项目（只做组合根）
├─ app.manifest, AssemblyInfo.cs
├─ publish.bat                   # 发布脚本（清 bin/obj → Release 单文件）
├─ setting.txt                   # 编辑器配置（主题、语言、颜色、旧版配置节等）
├─ CHANGELOG.md                  # 变更记录
├─ docs/                         # 命令 / 脚本 / Lua / 架构文档
├─ scripts/
│  ├─ ai/                        # AI Lua 工具箱（require('ai')）
│  ├─ mapgen/                    # 地图生成
│  │  ├─ core/                   # 通用建图引擎（build-map 等）
│  │  ├─ maps/                   # 各地区专用 run / 后处理脚本
│  │  └─ regions/                # spec、掩码、城市数据
│  └─ tools/
│     └─ legion-report.lua
├─ Resource/
│  ├─ Texture/ Config/ Lang/ Memory/   # 随仓库提供
│  └─ WC4DATA/assets/                  # 游戏数据（不入库，需自备）
├─ src/
│  ├─ WC4MapEditor.Core/         # 模型 / 解析器 / Modifier / 命令 / Lua API 实现
│  ├─ WC4MapEditor.Rendering/    # SkiaSharp 渲染与截图
│  ├─ WC4MapEditor.Scripting/    # MoonSharp 绑定（IMapScriptApi / LuaMapApi）
│  ├─ WC4MapEditor.AI/           # Provider / Agent 循环 / 工具 / 聊天窗
│  ├─ WC4MapEditor.Mcp/          # MCP Server（把 AI 工具暴露给外部 agent）
│  ├─ WC4MapEditor.AgentEval/    # Agent 轨迹统计（成功率/轮数/耗时/失败分布）
│  ├─ WC4MapEditor.GUI/          # 全部 WPF 界面（场景 / 对话框 / 主题）
│  └─ WC4MapEditor.Cli/          # 独立命令行工具（wc4me）
├─ ocr_map.py / ocr_map_gui.py   # 配套 OCR 小工具（Python）
└─ TestByAI.btl                  # 示例地图
```

---

## 文档

| 文档 | 内容 |
|---|---|
| [docs/README.md](docs/README.md) | 文档索引与“新接手三句话” |
| [docs/cli-commands.md](docs/cli-commands.md) | 控制台命令完整参考（命令树、参数、`--out`） |
| [docs/zme-scripts.md](docs/zme-scripts.md) | `.zme` 指令脚本格式与执行语义 |
| [docs/lua-scripting.md](docs/lua-scripting.md) | Lua `map` 接口全量参考、字段、坑与示例 |
| [docs/decoupling-plan.md](docs/decoupling-plan.md) | 模块解耦方案 |
| [docs/memory-optimization-plan.md](docs/memory-optimization-plan.md) | 内存优化方案 |
| [CHANGELOG.md](CHANGELOG.md) | 版本变更与已修复问题 |

---

## 开发

### 程序集与依赖方向

| 程序集 | 职责 | 约束 |
|---|---|---|
| `WC4MapEditor.Core` | 数据模型、解析器、Modifier、编辑模式、命令、脚本 API 实现 | 不引用 WPF / Rendering |
| `WC4MapEditor.Rendering` | Skia 渲染、截图辅助、调试控制台 | 不依赖 UI 框架，重绘靠宿主注入回调 |
| `WC4MapEditor.Scripting` | MoonSharp 绑定 | 不引用 Core（保持 `Core → Scripting` 单向） |
| `WC4MapEditor.AI` | Provider、Agent 循环、工具、聊天窗 | 通过 `AiBridge` 静态委托与宿主解耦 |
| `WC4MapEditor.GUI` | 全部界面 | 依赖 Core / Rendering / AI / Scripting |
| 根项目 | 应用入口与 DI | 不持有界面代码 |

“需要界面配合”的能力统一走 **Core 定义委托 + GUI 注册实现**（截图、对话框、地形识别、AI 桥等），Core 里不要出现 `System.Windows.*`。

### 扩展点

| 想加什么 | 改哪里 |
|---|---|
| 控制台命令 | `src/WC4MapEditor.Core/Commands/CliCommandHost*.cs`，同步更新 `docs/cli-commands.md` |
| Lua 能力 | `src/WC4MapEditor.Scripting/IMapScriptApi.cs` → `LuaMapApi.cs` → `src/WC4MapEditor.Core/Scripting/MapScriptApi.cs`，同步更新 `docs/lua-scripting.md` |
| AI 工具 | `src/WC4MapEditor.AI/Tools/AiToolRegistry.cs`（Schema + 执行分支）；需要宿主能力时在 `AiBridge` 加委托 |
| 编辑模式 | `Core/Mode` 下的 `ModeBase` + `ModeActionTable`，按键与帮助文本会从表中生成 |
| 界面文案 | `Resource/Lang/zh-CN.json` / `en-US.json` 或 `Resource/Lang/<code>/*.json` |

### 构建注意

- **编辑器正在运行时**主 exe/dll 被占用，根项目编译可能失败；此时只构建改动所在的子项目（`WC4MapEditor.AI` / `.Core` / `.Scripting`）即可验证语法。
- `publish.bat` 会删除 `bin/` 与 `obj/`，不要在还有未保存产物时运行。
- 工作区里出现 `Logs/`、`screenshots/`、`ai_settings.json`、`*.bak` 均属运行期产物，已加入 `.gitignore`。

### 代码风格

- 注释用中文，重点写“**为什么**这么做”，不复述代码；
- 优先扩展现有接口/模式，不为“优雅”引入新抽象；
- UI 保持深色主题一致。

---

## 常见问题

**Q：地图/资源打不开，提示找不到 `assets`？**
把游戏 `assets` 放到 `Resource/WC4DATA/assets/`。程序会从 exe 目录逐级向上查找 `Resource/`（最多 8 层）。

**Q：设置归属失败，提示“不在归属数据范围内”？**
征服地图的归属数据只覆盖裁剪区（`MapClip`）；裁剪区外，或者该地图本身没有归属数据，都会写入失败。这是数据限制，不是 bug。

**Q：v3 专有字段写不进去？**
单位 v3 专有字段（`HpLevel`、`TransportShip`、`Hatred` 等）在 v1 地图上不存在，写入会失败。先看 `a.Version`；`ai.army.bulk` 会自动跳过并计数。

**Q：AI 报 `Assistant tool call function.arguments must be valid JSON` / “缺少 code 参数”？**
通常是模型一次生成了过长的工具参数（例如一大段 Lua），输出被 `max_tokens` 截断，JSON 没闭合。把任务拆小（一次不超过约 150 行），或先 `write_file` 到 `scripts/` 再 `run_lua_file`。编辑器会拦截坏参数，不会污染对话历史。

**Q：AI 报 401「不支持的客户端」？**
部分中转网关靠 `User-Agent` 识别客户端，在 AI 设置里填上网关要求的 UA 再试。

**Q：AI 说要调用工具却不动手？**
该模型/网关可能不支持 function calling。把设置里的「工具调用」改成 `text`（不发送工具定义，模型改为输出 ```lua / ```cli 代码块），或换模型。

**Q：编译提示 DLL 被占用？**
编辑器还在运行。关闭程序，或只构建对应子项目。

---

## 已知限制

- 仅支持 Windows（WPF + SkiaSharp）。
- 主窗口使用 `AllowsTransparency=true` 时无法使用 GPU 渲染（`SKGLElement`）。
- 布局引擎规则是从游戏随附的 `layout.xml` / `font_resource.xml` / `image_resource.xml` 推断的，不是从游戏二进制解析；个别细节可能与实机不同。
- AI 聊天窗与部分调试日志、命令帮助文本仍为中文，未接入本地化。
- 独立 CLI 的 `stage` 命令仍把征服布局文件当作战役解析；GUI 与 `modify` 路径按文件头正确区分。
- 陷阱（trap）目前没有 Lua 接口，只能走控制台命令。

---

## 贡献

欢迎提交 Issue 与 PR。请遵守两条约定：

1. **能力只有一个来源**：命令加在 `CliCommandHost`，Lua 能力加在 `IMapScriptApi`，AI 工具加在 `AiToolRegistry`；
2. **文档与源码同步**：改命令/接口时一并更新 `docs/` 下对应文档，否则下一个人照着文档写会失败。

提交前请确认 `dotnet build` 为 0 error，并尽量不引入新警告。

---

## 许可证

本项目以 **GNU General Public License v3.0** 发布，详见 [LICENSE](LICENSE)。

《世界征服者 4》的商标、游戏数据与美术资源归其各自权利人所有；本仓库不包含游戏原始数据，使用者需自行准备合法获得的数据文件。
