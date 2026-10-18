# project-architecture

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 分层与依赖方向、能力注册（回调注入）模式、单例/DI 现状、命令分发、构建验证。动 Core 或加新能力前先读这条。

---

## 程序集与依赖方向

| 程序集 | 职责 | 注意 |
|---|---|---|
| `WC4MapEditor.Core` | 数据模型 / 解析器 / 修改器（Modifier）/ 编辑模式 / 命令 / 脚本 API | **不引用 WPF、不引用 Rendering** |
| `WC4MapEditor.Rendering` | Skia 渲染 + 渲染辅助（如 `ScreenshotHelper`）、`DebugConsole` | 纯渲染库，不依赖 UI 框架；重绘靠宿主注入的 `InvalidateCallback` |
| `WC4MapEditor.Scripting` | MoonSharp 的 Lua 绑定（`LuaMapApi` / `IMapScriptApi`） | `io` 可用；`dofile`/`require` 无先例，脚本别依赖 |
| `WC4MapEditor.AI` | 模型 Provider、Agent 循环、AI 工具（`AiToolRegistry`）、聊天窗 | 通过 `AiBridge` 静态委托与主程序解耦 |
| 主项目（根目录 `App.xaml.cs` / `Views/`） | WPF 层；各"编辑场景"都是 `UserControl` | 由 `MainWindow.SetCurrentScene()` 挂进 `_mainGrid` |

**依赖方向永远是 GUI → Core，Core 绝不反向依赖 UI / 渲染。** 需要界面的能力统一走"Core 定义委托 + GUI 注册实现"：

| 能力 | Core 侧注册点 | 谁注册 |
|---|---|---|
| 地图截图（渲染地图数据） | `EditModeManager.SetScreenshotCallback` | 渲染场景 `RenderSceneBase` |
| 整场景截图（截窗口界面） | `EditModeManager.SetSceneScreenshotCallback` | `MainWindow` |
| 对话框 | `SetDialogService(IDialogService)` | `WpfDialogService` |
| 识别地形 / 经纬度换算 | `SetRecognizeTerrainCallback` / `SetGeo*Callback` | 渲染场景 |
| AI 工具能力（Lua/命令/截图/切场景） | `AiBridge.*` 静态委托 | `BeginScene` / 渲染场景 |

新增"需要界面配合"的能力照这个模式加，**不要在 Core 里写 `System.Windows.*`**。

## 单例与 DI（容易踩）

Core 内部大量 `Xxx.Instance` 单例；`App.xaml.cs` 把**同一个实例**注册进 DI：

```csharp
services.AddSingleton<EditModeManager>(_ => EditModeManager.Instance);
```

所以"注入进来的 manager"与 `EditModeManager.Instance` **是同一个对象**，注册回调时用哪种都行。**不要自己 new 这些管理器**（那会得到一份平行状态，回调注册不上）。

## 命令分发（两条路）

控制台（F3）执行一行命令时：

1. 先查 `Core.Commands.CommandManager` —— **本地命令表**，渲染场景注册的 `lua` / `goto` / 新建地图命令等；
2. 失败再回落到 `CliCommandHost`（System.CommandLine）—— `terrain` / `belong` / `building` / `army` / `screenshot(s)` 等业务命令都在这里。

给 AI / 脚本用的命令优先加在 `CliCommandHost`（控制台与独立 CLI 都能用）。
**多数业务命令都包在 `WithModifiers(...)` 里，拿不到 `MapData` 会直接打印"地图数据未加载"并返回** —— 详见记忆 `editor-shortcuts-and-pitfalls`。

## 撤销

- 单条命令：走 `EditModeManager.Record*`（归属快照 / 建筑快照）记一步；
- Lua 脚本：整批包成**一个**撤销单元，`Ctrl+Z` 一次性退回（脚本内部步骤不能单独撤销）；
- 批量模式（`BeginBatchMode`）下不记撤销，结束时统一触发 `DataModified`。

## 构建与验证

- 全量：`dotnet build WC4MapEditor.csproj -c Debug`（会带上被引用的子项目）。
- **编辑器正开着时**主 exe/dll 被占用会编译失败 → 只 build 改动所在的子项目（`WC4MapEditor.AI` / `.Core` / `.Scripting` / `.Cli`）即可验证语法。
- 改完看编译输出与 lint，确认 0 error、没有新增警告。

## 代码风格

- 注释用中文，写"**为什么**这么做"（尤其有取舍的地方），不要复述代码；
- 优先扩展现有接口/模式，不为了"优雅"引入新抽象层；
- UI 改动保持深色主题一致（工具按钮底色一类是 `0x45,0x45,0x48`，停止/危险态用暗红 `0x8B,0x3A,0x3A`）。
