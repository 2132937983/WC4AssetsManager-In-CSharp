# zh-CN 分文件翻译 / split translation files

`Resource/Lang/zh-CN.json` 存的是资源编辑器（列表 / 属性面板 / 资源管理器 / 战术地图）的文本；
本目录按功能存放地图编辑器的文本，运行时叠加到主文件之上。

## 加载规则

`Loc.LoadLanguage()`（`src/WC4MapEditor.Core/Config/Localization.cs`）的处理顺序：

1. 先读 `../zh-CN.json`；
2. 再按文件名（不分大小写）升序读本目录的 `*.json`，逐个 `TryAdd`。

由此得到三条约定：

- **主文件优先**：同一个键在两边都有时用主文件的值，分文件只补充新键。当前主文件 1577 条、本目录 2540 条，
  两边没有重复键。
- **文件之间不要重复**：本目录内部出现重复键时，先读到的文件生效（当前 21 个文件之间也没有重复键）。
- **缺键兜底**：当前语言 → `zh-CN.json` → 键名本身（`Loc.T` / `Loc.F`）。所以新增文本先写中文，
  界面上不会出现空白，也不会报错。

新增一个分文件会被自动发现，不用改代码：本目录下**所有** `*.json` 都会被读入，辅助文件请用别的扩展名。

## 格式约定

- **严格 JSON**：不写 `//` 注释、不留末尾逗号。`Loc` 读取时确实允许注释和末尾逗号，但注释会破坏
  IDE / 校验器 / 翻译工具的解析，本目录统一保持纯 JSON（2026-10 已全部清除注释）。
- 一个平铺对象，键名形如 `区域.条目`（`Mode.Army.Name`、`Module.Building.DeleteFailed`）。
  嵌套对象会按点号拍平，等价于平铺写法，但这里统一用平铺写法，便于按前缀排序和 diff。
- `{0}` `{1}` 是 `string.Format` 的占位符，翻译时必须保留，个数与顺序要和代码里的调用一致；
  写错只会在 `Loc.F` 里退回未格式化的原文，不会崩，但界面上会露出 `{0}`。
- UTF-8 无 BOM、LF 换行、文件末尾留一个换行。
- 键名（左侧）两种语言必须一致；只在这边加键、那边没加，英文界面会显示中文——这是兜底而不是错误。

## 文件对照

| 文件 | 覆盖的功能 | 主要键前缀 | 条数 |
| --- | --- | --- | --- |
| `mode-framework.json` | 编辑模式框架：模式基类、模式管理器、按键名。各模式的文本不写在这里 | `Mode.` `Key.` | 25 |
| `scene.json` | 渲染场景交互与反馈：底栏状态区、撤销 / 重做、场景按键帮助、关闭 / 返回确认、Tab 格子信息窗口 | `Scene.` `Assist.` | 159 |
| `startapp.json` | 开始界面与应用级：设置窗口、推荐、背景音乐、开发者模式、加载画面 | `Main.` `Begin.` `Assist.` `Console.` | 107 |
| `conquest.json` | 场景文件操作（打开 / 保存 / 新建 / 切换）与征服场景的世界底图 | `SceneFile.` `Conquest.` | 15 |
| `terrain.json` | 地形绘制模式、地形模块（TerrainModifier / TerrainRecognizer）、画笔窗口 | `Mode.` `Module.` `Assist.` | 154 |
| `province.json` | 省份编辑：模式、模块消息、省份命令 | `Mode.` `Module.` `Render.` | 101 |
| `belong.json` | 归属编辑模式、归属模块、归属列表窗口（Q） | `Mode.` `Module.` `Assist.` | 71 |
| `army.json` | 单位部署模式（单位 v1 / v3 + 陷阱）：模式文本、状态消息、对话框、撤销说明 | `Mode.` `Module.` | 177 |
| `army-dialogs.json` | 单位设置对话框（v1 / v3）、将领信息面板、Core ArmyRules 规则提示 | `Assist.` | 80 |
| `reinforcement.json` | 援军配置模式、援军模块（含 V3）、援军编辑器窗口 | `Mode.` `Module.` `Assist.` | 133 |
| `building.json` | 建筑部署模式、建筑模块（BuildingModifier / BuildingNameResolver）、建筑编辑窗口、建筑渲染 | `Mode.` `Module.` `Assist.` `Render.` | 177 |
| `airforce.json` | 空袭编辑模式、空袭 / 空中支援模块、两个列表窗口 | `Mode.` `Module.` `Assist.` | 120 |
| `legion.json` | 军团编辑模式、军团 / 首都模块、军团设置 / 列表 / 头部数据窗口 | `Mode.` `Module.` `Assist.` | 137 |
| `event.json` | 事件编辑模式、事件设置窗口、事件模块与引用检查 | `Mode.` `Module.` `Assist.` | 123 |
| `strategic.json` | 战略建设：模式、模块、列表窗口、地图右上角面板、strategic 命令 | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 66 |
| `weather.json` | 天气：模式、模块、列表窗口、地图右上角面板、weather 命令 | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 67 |
| `mapcase.json` | 方案（AI 方针）编辑：模式、方案列表窗口、模块、渲染层、命令行输出 | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 91 |
| `unitplacement.json` | `unitplace` 命令组的输出（只读数据，没有可编辑项） | `Cli.` | 4 |
| `datacheck.json` | 数据检查场景、inspect / compare / convert 命令、BtlInspector / BinaryCompare / BtlVersionConverter | `DataCheck.` | 572 |
| `json-editors.json` | JSON 表编辑场景的公共外壳（历史 / 保存 / 批量 / 帮助）与数据完整性提示 | `JsonEditor.` | 145 |
| `list-editor.json` | 列表 + 字段面板类编辑窗口的公共文本（`ListEditorWindow` 基类） | `Assist.` | 16 |

合计 2540 条，与 `../en-US/` 下的 21 个文件一一对应（文件名、键名必须一致）。

## 新增文本放哪里

- 某个模式 / 模块自己的文字 → 对应功能文件（如建筑相关放 `building.json`）。
- 多个模式共用的东西（模式切换提示、按键名）→ `mode-framework.json`。
- 状态栏、撤销提示、“确定返回？”这类场景级反馈 → `scene.json`。
- 列表 + 字段面板窗口的通用按钮与提示 → `list-editor.json`。
- 改键名要同步改代码里的 `Loc.T("...")` / `Loc.F("...")`；代码里搜不到的就是死键，可以删。
- 模式名统一叫 `Mode.<模式>.Name`，只定义一次；当前模式名分散在各功能文件里，`mode-framework.json` 只放框架文本。
