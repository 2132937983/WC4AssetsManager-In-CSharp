# WC4MapEditor 解耦重构方案

> 文档版本：v1.1
> 创建日期：2026-09-12
> 基线提交：`59da8c4`（保存解耦重构前的工作区现状）
> 回滚命令：`git reset --hard 59da8c4`

---

## 执行进度

| 阶段 | 状态 | 说明 |
|------|------|------|
| 复核 | ✅ 完成 | 发现 `ISkiaRenderer` 为死代码，方案由"新建 Rendering.Wpf 项目"简化为"删死代码 + Action 回调" |
| A：斩断 WPF 传染链 | ✅ 完成 | `Rendering` 与 `Cli` 已改为 `net10.0`，WPF 依赖归零 |
| B：断开渲染层 → CLI | ✅ 完成 | 新增 `ICommandHost` 接口 + `CommandHostProvider`，`DebugConsole` 不再依赖具体类 |
| C：统一命名空间 | ✅ 完成 | 125 个文件，命名空间已对齐程序集边界，四项编译全绿 |
| D：Core 剥离 SkiaSharp | ✅ 完成 | 9 个文件移出 Core，Core 已零 Skia 依赖，四项编译全绿 |
| E：CLI 框架移出 Core | 🔄 已重新评估 | 收益有限，见 [阶段 E 修订](#阶段-e-修订说明) |
| F：模型层去单例 | ✅ 完成 | `Core/Models` 单例依赖归零（3 个方法实为死代码，已删除） |
| G：场景管理下沉 | ✅ 完成 | 剪贴板抽为 Core 纯数据服务，`RenderSceneManager` 下沉到根项目 `Services/` |
| H：全面依赖注入 | ✅ 批次 1~4 完成 | 16 个单例已纳入 DI 容器；仅剩 `ConfigManager` 建议保持单例（理由见 H-3/H-4） |

---

## 目录

1. [现状评估](#一现状评估)
2. [目标架构](#二目标架构)
3. [问题清单与优先级](#三问题清单与优先级)
4. [迁移步骤](#四迁移步骤)
5. [回归验证清单](#五回归验证清单)
6. [风险与缓解](#六风险与缓解)

---

## 一、现状评估

### 1.1 当前依赖图（编译期）

```
WC4MapEditor (WPF 宿主, net10.0-windows)
   ├──→ WC4MapEditor.Core           (net10.0)
   └──→ WC4MapEditor.Rendering      (net10.0-windows, UseWPF)
              └──→ WC4MapEditor.Core

WC4MapEditor.Cli (Exe, net10.0-windows)
   ├──→ WC4MapEditor.Core
   └──→ WC4MapEditor.Rendering
```

### 1.2 已验证的健康项 ✅

经全量源码扫描确认：

| 检查项 | 结果 |
|--------|------|
| Core 引用 `System.Windows` | **0 处** |
| Core 引用 `WC4MapEditor.Rendering` | **0 处** |
| Core 引用 `WC4MapEditor.Views` | **0 处** |
| 项目间循环引用 | **无** |

**结论：依赖方向是正确的单向链，无环。架构骨架成立。**

### 1.3 实际存在的问题（6 类）

问题不在编译期依赖，而在**命名空间边界、宿主技术泄漏、全局单例**三处。

---

## 二、目标架构

### 2.1 目标依赖图

```
┌─────────────────────────────────────────────┐
│  WC4MapEditor (WPF 宿主)                     │  net10.0-windows
│  Views / ViewModels / App.xaml               │
└───────┬──────────────┬──────────────┬───────┘
        │              │              │
        ▼              ▼              ▼
┌───────────────┐ ┌──────────────┐ ┌──────────────────┐
│ Rendering.Wpf │ │  Rendering   │ │  Application     │
│ (新增)         │ │  (纯化)       │ │  (新增, 可选)     │
│ SKElement     │ │  Skia 图层    │ │  RenderSceneMgr  │
│ DebugConsole  │ │  net10.0     │ │  UseCases        │
│ net10.0-win   │ │  跨平台 ✅     │ │  net10.0         │
└───────┬───────┘ └──────┬───────┘ └────────┬─────────┘
        │                │                  │
        └────────────────┴──────────────────┘
                         ▼
              ┌────────────────────────┐
              │  WC4MapEditor.Core     │  net10.0, 跨平台
              │  命名空间统一为         │  无 Skia / 无 CLI
              │  WC4MapEditor.Core.*   │
              └───────────┬────────────┘
                          ▼
              ┌────────────────────────┐
              │  (可选) Imaging        │  SkiaSharp 实现
              │  Flag/Image/Tactical   │
              └────────────────────────┘
```

### 2.2 核心原则

1. **命名空间 = 程序集边界**：看到 `using X.Y` 就能判断依赖方向
2. **Core 零宿主依赖**：不引用 WPF、不引用 SkiaSharp、不引用 System.CommandLine
3. **宿主适配层隔离**：WPF 相关代码集中在 `Rendering.Wpf`，不污染渲染内核
4. **依赖注入替代单例**：已有 `Microsoft.Extensions.DependencyInjection`，充分利用

---

## 三、问题清单与优先级

### 🔴 P0-1：WPF 泄漏导致 Rendering / CLI 锁死 Windows

**位置**：`WC4MapEditor.Rendering/Skia/DebugConsole.cs`

```csharp
82:  private System.Windows.Window? _window;
442: public void SetWindow(System.Windows.Window? window) => _window = window;
```

**传染链**：
```
DebugConsole 引用 System.Windows.Window
  → Rendering.csproj 必须 UseWPF=true + net10.0-windows
    → 引用 Rendering 的 WC4MapEditor.Cli 被迫 net10.0-windows
      → CLI 无法在 Linux/macOS 运行
```

**矛盾点**：CLI 的核心工作（解析 BTL、导出 JSON、批量修改）是纯逻辑，本应跨平台。`bin/` 下已有 2 个 `.dylib`（macOS 产物），证明有跨平台需求。

**同类问题**：`ISkiaRenderer.Initialize(SKElement skElement)` 把 WPF 控件类型写进 Rendering 接口。

> **复核结论（2026-09-12）**：经全库扫描，`ISkiaRenderer.Initialize(SKElement)` **没有任何调用方** ——
> `Views/RenderSceneBase.cs:332` 调用的是 `IRenderEngine.Initialize(IntPtr, int, int)`。
> 因此 `SkiaRenderEngine._skElement` 恒为 `null`，`Invalidate()` 一直是空操作，`Render(MapData, Camera)` 直接返回。
> **这段代码属于死代码，可安全删除**，无需新建 `Rendering.Wpf` 项目来承载它。
> `ISkiaRenderer` 接口本身也无任何使用者（仅 `SkiaRenderEngine` 实现，且无任何变量/字段/参数声明为该类型），已一并删除。
>
> 该发现使阶段 A 的改动量从"新建项目 + 迁移 2 个类"降为"改 3 个源文件 + 2 个 csproj"。

---

### 🔴 P0-2：命名空间与程序集边界错位

`WC4MapEditor.Core.csproj` 声明 `RootNamespace=WC4MapEditor.Core`，但程序集内实际存在 **5 种命名空间前缀**：

| 实际命名空间 | 所在目录 |
|---|---|
| `WC4MapEditor.Models` | `Core/Models/`（大部分） |
| `WC4MapEditor.Parsers.*` | `Core/Parsers/BTL, Stage, Conquest, World` |
| `WC4MapEditor.Core.*` | Brush, Assets, Services, Mode, Commands, Input, Config, SceneManagement, Analyzers |
| `WC4MapEditor.Core.Models` | GeneralSettingData, CountrySettingData, Conquer* |
| `WC4MapEditor.Core.Parsers` | TacticalMap, HdAtlas, General, Country |

**致命点**：`WC4MapEditor.Models` 与 `WC4MapEditor.Core.Models` **同时存在**。

**后果**：
- 仅凭 `using` 无法判断依赖方向（`using WC4MapEditor.Models` 引用的是哪个程序集？）
- 未来若要拆出 `WC4MapEditor.Domain` 会全线崩溃
- IDE 查找引用 / 自动重构在此不可靠

**放大因素**：根项目 `GlobalUsings.cs` 全局注入 9 个命名空间，进一步掩盖真实依赖：

```csharp
global using WC4MapEditor.Core.Config;
global using WC4MapEditor.Core.Helpers;
global using WC4MapEditor.Models;          // ← 看似根项目，实际在 Core
global using WC4MapEditor.Parsers;
global using WC4MapEditor.Rendering;
global using WC4MapEditor.Rendering.Skia;
```

---

### 🟠 P1-1：Core 依赖 SkiaSharp（8 个文件）

```
Core/Services/ImageEditorService.cs              using SkiaSharp;
Core/Services/IImageEditorService.cs             using SkiaSharp;
Core/Services/FlagEditorService.cs               using SkiaSharp;
Core/Services/FlagImageService.cs                using SkiaSharp;
Core/Services/IFlagEditorService.cs              using SkiaSharp;
Core/Services/IFlagImageService.cs               using SkiaSharp;
Core/Parsers/TacticalMap/TacticalMapEditor.cs    using SkiaSharp;
Core/Parsers/HdAtlas/HdAtlasEditor.cs            using SkiaSharp;
```

数据/逻辑层绑定图形库，导致 `Core.csproj` 必须引 `SkiaSharp` 包。

---

### 🟠 P1-2：CLI 框架进入 Core

`Core/Commands/CliCommandHost.cs` 使用 `System.CommandLine`，`Core.csproj` 因此引入该包。

**额外耦合**：`Rendering/Skia/DebugConsole.cs` 有 **6 处**直接调用 `CliCommandHost.Instance` —— 渲染层穿过 Core 调用 CLI 框架。

---

### 🟠 P1-3：Core 持有 UI 概念

`Core/SceneManagement/RenderSceneManager.cs` —— "Render Scene" 是 UI 概念，位于 Core 层。

被 Core 内部直接引用：
```
Core/Mode/TerrainPaintMode.cs:321,346   → RenderSceneManager.Instance
Core/Mode/ProvinceEditMode.cs           → RenderSceneManager.Instance
```
（用途：读取跨场景剪贴板 `GlobalCopied*`）

---

### 🟡 P2：全局单例泛滥

全库 **62 个文件、约 200 处 `.Instance`**。非编译期依赖，但耦合强度更高（不可替换、不可测试、初始化顺序敏感）。

**单例清单**：
```
Core:      ConfigManager, EditModeManager, RenderSceneManager, AssetManager,
           KeyboardManager, MouseManager, HexSelector, CommandManager,
           CliCommandHost, GeneralSettingParser
Rendering: CoastHelper, CoastMaskProcessor, TacticalMapImageCache,
           TextureDiskCache, DebugConsole
```

**典型重灾区 ①：数据模型依赖全局配置**
```csharp
// Core/Models/Terrain.cs:89
public static string GetTerrainName(int terrainId)
    => ConfigManager.Instance.GetTerrainTypeName(terrainId);
```
纯数据结构被配置单例污染 —— 单元测试里 `new Terrain()` 想取名字，必须先初始化整套配置系统。

**典型重灾区 ②：单点耦合爆炸**
`Views/RenderSceneBase.cs` 单文件 **55 处 `.Instance`**。

---

## 四、迁移步骤

> **执行原则**：每个阶段独立提交，每阶段结束必须通过[回归验证清单](#五回归验证清单)。

---

### 阶段 A：斩断 WPF 传染链 ✅ 已完成

**实际做法（2026-09-12 执行）**：因 `ISkiaRenderer` 为死代码，无需新建 `Rendering.Wpf` 项目。实际改动：

| 文件 | 改动 |
|------|------|
| `Rendering/Skia/SkiaRenderEngine.cs` | 删除 `using SkiaSharp.Views.WPF`；删除 `ISkiaRenderer` 接口（含 `Initialize(SKElement)`）；删除 `SKElement` 字段；新增 `public Action? InvalidateCallback`，`Invalidate()` 与 `Render(MapData, Camera)` 改为调用它（未注入时为空操作，行为与改动前一致） |
| `Rendering/Skia/DebugConsole.cs` | `private System.Windows.Window? _window` → `private Action? _closeHostAction`；`SetWindow(Window)` → `SetHostWindow(Action)`；`HideConsole()` 改为调用回调 |
| `Views/RenderSceneBase.cs:632` | 调用点改为 `_debugConsole.SetHostWindow(window.Close)` |
| `Rendering.csproj` | `net10.0-windows` → `net10.0`；移除 `<UseWPF>true</UseWPF>` 与 `SkiaSharp.Views.WPF` 包引用 |
| `Cli.csproj` | `net10.0-windows` → `net10.0` |

**验证结果**：
- `dotnet build WC4MapEditor.Rendering -c Release` → **0 错误**（32 个既有警告，与本次改动无关）
- `Rendering/` 目录下 `System.Windows` / `SKElement` / `SkiaSharp.Views` 引用数 → **0**（仅剩一处注释文字）
- `SKElement` 的使用者全部收敛到 WPF 根项目（`Views/RenderSceneBase.cs`），根项目自带 `SkiaSharp.Views.WPF` 包引用，不受影响

---

<details>
<summary>原始方案（保留备查，已被上述简化方案取代）</summary>

**目标**：让 `Rendering` 与 `Cli` 脱离 Windows 依赖。

#### A-1. 新建 `WC4MapEditor.Rendering.Wpf` 项目

```
WC4MapEditor.Rendering.Wpf/
├── WC4MapEditor.Rendering.Wpf.csproj
│     TargetFramework: net10.0-windows
│     UseWPF: true
│     ProjectReference: ..\WC4MapEditor.Rendering
├── SkiaElementHost.cs        ← 从 Rendering 移入（SKElement 相关）
└── DebugConsoleWindow.cs     ← 从 DebugConsole 剥离窗口管理
```

#### A-2. 改造 `DebugConsole.cs`

```csharp
// 改前
private System.Windows.Window? _window;
public void SetWindow(System.Windows.Window? window) => _window = window;

// 改后：抽象为通知接口，WPF 实现放在 Rendering.Wpf
public interface IDebugConsoleHost
{
    void OnVisibilityChanged(bool visible);
    void OnLogAppended(string line);
}
public IDebugConsoleHost? Host { get; set; }
```

#### A-3. 解耦 `ISkiaRenderer.Initialize`

```csharp
// 改前（Rendering 接口泄漏 WPF 类型）
void Initialize(SKElement skElement);

// 改后（Rendering 只认尺寸，WPF 版本在 Rendering.Wpf 扩展）
void Initialize(int width, int height);
```

#### A-4. 修改 csproj

```xml
<!-- WC4MapEditor.Rendering.csproj -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>   <!-- 去掉 -windows -->
  <!-- 移除 <UseWPF>true</UseWPF> -->
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="SkiaSharp" Version="3.119.2" />
  <!-- 移除 SkiaSharp.Views.WPF -->
</ItemGroup>
```

```xml
<!-- WC4MapEditor.Cli.csproj -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>   <!-- 去掉 -windows -->
</PropertyGroup>
```

**验收**：`dotnet build WC4MapEditor.Cli -c Release` 成功，且 `dotnet publish -r linux-x64` 可产出可执行文件。

</details>

---

### 阶段 B：断开渲染层 → CLI ✅ 已完成

#### B-1. 新增命令宿主接口（已落地）

新增 `Core/Commands/ICommandHost.cs`：

```csharp
public interface ICommandHost
{
    int Execute(string commandLine);
    (List<string> Errors, string CommandName, string CommandDescription) GetParseErrors(string commandLine);
    string GetHelp();
    void SetOutput(TextWriter output);
    void BeginBatchMode();
    void EndBatchMode();
    event Action? DataModified;
}
```

接口面依据实际调用方裁剪：`DebugConsole` 用到 `Execute` / `GetParseErrors` / `GetHelp`，
`DebugConsoleWindow` 用到 `BeginBatchMode` / `EndBatchMode`，`RenderSceneBase` 用到 `SetOutput` / `DataModified`。
`GetRootCommand()` 返回 `System.CommandLine.Command`，会把框架类型带回渲染层，**故意未纳入接口**。

#### B-2. 收敛具体类型知识（已落地）

新增 `Core/Commands/CommandHostProvider.cs`，把"具体类型"的知识集中到单点：

```csharp
public static class CommandHostProvider
{
    private static ICommandHost? _default;
    public static ICommandHost Default => _default ??= CliCommandHost.Instance;
    public static void Override(ICommandHost host) => _default = host;
}
```

`CliCommandHost` 声明为 `: ICommandHost`。

#### B-3. `DebugConsole` 改为依赖接口（已落地）

```csharp
// 新增属性（默认走 Core 内置实现，行为与改动前完全一致）
private ICommandHost? _commandHost;
public ICommandHost CommandHost
{
    get => _commandHost ??= CommandHostProvider.Default;
    set => _commandHost = value;
}
```

原 6 处 `Core.Commands.CliCommandHost.Instance.Execute(...)` 统一改为局部变量取值：

```csharp
var commandHost = CommandHost;
var cliResult = commandHost.Execute(input);
var (errors, cmdName, cmdDesc) = commandHost.GetParseErrors(input);
var helpText = commandHost.GetHelp();
```

**验收结果**：`Rendering/` 目录下 `CliCommandHost` 出现次数 → **0** ✅
`Views/Assist/DebugConsoleWindow.cs` 仍直接使用 `CliCommandHost.Instance`，属宿主层正常用法，未改动。

---

### 阶段 C：统一命名空间 ✅ 已完成

**执行结果（2026-09-12）**：脚本批量替换 **125 个文件**（扫描 204 个），改动明细见下方小节末尾的"实际执行记录"。

#### C-1. 替换规则

```
WC4MapEditor.Models          → WC4MapEditor.Core.Models
WC4MapEditor.Parsers.BTL     → WC4MapEditor.Core.Parsers.BTL
WC4MapEditor.Parsers.Stage   → WC4MapEditor.Core.Parsers.Stage
WC4MapEditor.Parsers.Conquest→ WC4MapEditor.Core.Parsers.Conquest
WC4MapEditor.Parsers.World   → WC4MapEditor.Core.Parsers.World
```

#### C-2. 执行方式

**推荐 IDE 重构**（自动处理 `using` 与全限定名）：
- Visual Studio：`Ctrl+R, Ctrl+R` 重命名命名空间
- Rider：`Shift+F6` 或 `Refactor → Move`

**或用脚本批量替换**（需人工复核）：

```powershell
$files = Get-ChildItem -Recurse -Include *.cs,*.xaml -Path . |
          Where-Object { $_.FullName -notmatch '\\(bin|obj|\\.kilo)\\' }

$map = @{
    'WC4MapEditor\.Models'           = 'WC4MapEditor.Core.Models'
    'WC4MapEditor\.Parsers\.BTL'     = 'WC4MapEditor.Core.Parsers.BTL'
    'WC4MapEditor\.Parsers\.Stage'   = 'WC4MapEditor.Core.Parsers.Stage'
    'WC4MapEditor\.Parsers\.Conquest'= 'WC4MapEditor.Core.Parsers.Conquest'
    'WC4MapEditor\.Parsers\.World'   = 'WC4MapEditor.Core.Parsers.World'
}

foreach ($f in $files) {
    $c = [System.IO.File]::ReadAllText($f.FullName)
    $o = $c
    foreach ($k in $map.Keys) { $c = $c -replace $k, $map[$k] }
    if ($c -ne $o) {
        [System.IO.File]::WriteAllText($f.FullName, $c, (New-Object System.Text.UTF8Encoding $false))
        Write-Host "updated: $($f.FullName)"
    }
}
```

> ⚠️ **必须排除**：`bin/`、`obj/`、`.kilo/worktrees/`
> ⚠️ 替换前先执行 `git status` 确认工作区干净，便于回退。

#### C-3. 收尾

更新根项目 `GlobalUsings.cs`：
```csharp
global using WC4MapEditor.Core.Models;
global using WC4MapEditor.Core.Parsers;
```

**验收**：全库 `grep -rn "namespace WC4MapEditor\." | grep -v "WC4MapEditor.Core\."` 只返回根项目的 `Views` / `ViewModels` / `Services`。

#### C-4. 实际执行记录

**替换脚本**（PowerShell，已排除 `bin` / `obj` / `.kilo`，并保留原文件 BOM 状态）：

```powershell
$files = Get-ChildItem -Path . -Recurse -Include *.cs,*.xaml -File |
         Where-Object { $_.FullName -notmatch '\\(bin|obj|\.kilo)\\' }
foreach ($f in $files) {
    $t = [System.IO.File]::ReadAllText($f.FullName); $o = $t
    $t = $t -replace 'WC4MapEditor\.Models', 'WC4MapEditor.Core.Models'
    $t = $t -replace 'WC4MapEditor\.Parsers\.', 'WC4MapEditor.Core.Parsers.'
    # 该命名空间改名后将变空，不删会导致 CS0246
    $t = $t -replace '(?m)^[ \t]*global using WC4MapEditor\.Parsers;[ \t]*\r?\n', ''
    $t = $t -replace '(?m)^[ \t]*using WC4MapEditor\.Parsers;[ \t]*\r?\n', ''
    # ... 写回（保持 BOM）
}
```

**执行前的侦察结论**：

| 项 | 结果 |
|---|---|
| `WC4MapEditor.Models` 引用 | 122 处 |
| `WC4MapEditor.Parsers` 引用 | 49 处（其中 2 处为无子命名空间的无效 using） |
| XAML 中的 `xmlns` 引用 | **0 处**（无需处理 XAML） |
| `WC4MapEditor.Models.Modules` | 3 个文件，因前缀替换自动正确转换 |
| 与既有 `WC4MapEditor.Core.Models`（4 文件）类型重名 | **无** |
| 与既有 `WC4MapEditor.Core.Parsers`（4 文件）类型重名 | **无** |

**执行后的验证结果**：

| 检查 | 结果 |
|---|---|
| 项目内残留 `WC4MapEditor.Models` / `WC4MapEditor.Parsers` | **0** |
| 完全重复的 using 指令 | **0** |
| `dotnet build WC4MapEditor.Rendering -t:Rebuild` | 0 错误 |
| `dotnet build WC4MapEditor.csproj`（WPF 根项目） | **0 错误**（2 个既有警告） |
| `dotnet build WC4MapEditor.Cli` | **0 错误 0 警告** |

**替换后的命名空间分布**（已完全对齐程序集边界）：

| 前缀 | 文件数 | 归属程序集 |
|---|---|---|
| `WC4MapEditor.Core.Models`（+`.Modules`） | 28 + 3 | Core |
| `WC4MapEditor.Core.Parsers.*` | 22 | Core |
| `WC4MapEditor.Core.{Modifiers,Commands,Services,Mode,Geo,...}` | 60+ | Core |
| `WC4MapEditor.Rendering.*` | 29 | Rendering |
| `WC4MapEditor.Views.*` / `Services.*` / `ViewModels` / `Commands` | 33 | WPF 根项目 |
| `WC4MapEditor.Cli` | 1 | CLI |

> **注意**：脚本替换把 `WC4MapEditor.Parsers;`（无子命名空间）的 2 处无效 using 直接删除，
> 而非改成 `WC4MapEditor.Core.Parsers;` —— 后者会把 `TacticalMapParser` 等 4 个类型
> 意外引入全局作用域（`GlobalUsings.cs`），产生潜在歧义。

---

### 阶段 D：Core 剥离 SkiaSharp ✅ 已完成

**实际采用的策略与文档原计划相反**：侦察发现 `IFlagImageService` / `IFlagEditorService` / `IImageEditorService`
的**方法签名直接暴露 `SKBitmap` / `SKColor` / `SKEncodedImageFormat`** —— 接口本身即 Skia 绑定。
若按原计划"抽象成中立类型（byte[]）"，将迫使全部调用方改写，收益低、风险高。

因此改为**把这些文件整体移到 Rendering**：它们本质是图像处理，与 SkiaSharp 技术栈绑定，
不属于"纯数据/逻辑"的 Core 层。

#### D-0. 实际改动清单

| 文件 | 从 | 到 |
|------|-----|-----|
| `IFlagImageService.cs` / `IFlagEditorService.cs` / `IImageEditorService.cs` | `Core/Services/` | `Rendering/Imaging/` |
| `FlagImageService.cs` / `FlagEditorService.cs` / `ImageEditorService.cs` | `Core/Services/` | `Rendering/Imaging/` |
| `TacticalMapEditor.cs` | `Core/Parsers/TacticalMap/` | `Rendering/Imaging/` |
| `HdAtlasEditor.cs` | `Core/Parsers/HdAtlas/` | `Rendering/Imaging/` |
| `ConfigManager.cs` 中的 `FlagOverlayImage` | `Core/Config/`（字段+加载+属性） | 下沉为 `FlagEditorService.GetFlagOverlayImage()` 私有懒加载 |

**配套改动**：
- 命名空间统一为 `WC4MapEditor.Rendering.Imaging`
- `Core.csproj` 移除 `SkiaSharp` 包引用
- `Rendering.csproj` 增加 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
- 5 个 Views 文件补充 `using WC4MapEditor.Rendering.Imaging;`

> 文件移动使用 `git mv`，保留 git 历史。

#### D-0.1 执行中发现的 5 个坑（重要）

| # | 坑 | 表现 | 处理 |
|---|---|---|---|
| 1 | **全限定名漏检** | `ConfigManager.cs` 写作 `SkiaSharp.SKBitmap`（无 `using`），按 `grep "using SkiaSharp"` 扫描**完全漏掉** | 必须按**类型名**（`SKBitmap`）扫描，不能只扫 using |
| 2 | **unsafe 代码** | `FlagEditorService.cs` 含 2 处 `unsafe`（指针操作），Core 原本开着 `AllowUnsafeBlocks`，Rendering 没开 → `CS0227` | Rendering 开启 `AllowUnsafeBlocks` |
| 3 | **相对命名空间引用** | `Assets.AssetManager` 原先靠 `WC4MapEditor.Core.Parsers` 的层级向上解析，移出后解析失败 | 补 `using WC4MapEditor.Core.Assets;` + 改为 `AssetManager`（2 处） |
| 4 | **同命名空间隐式可见** | `TacticalMapEditor.cs` 使用 `TacticalMapParser` / `TacticalMapImageDef` 但**没有 using**（原先同属 `Core.Parsers`） | 移出后补 `using WC4MapEditor.Core.Parsers;` |
| 5 | **出口类型扩散** | `TacticalMapEditor.cs` 还导出 `TacticalMapObject`、`OrthographicCamera`，被 `Views/Dialogs/ObjectPropertiesDialog.cs` 使用 | 逐个排查导出类型，补 using |

**教训**：移动一个类之前，必须先列出它**导出的所有公共类型**和它**依赖的所有外部类型**，
仅按主类型名搜索引用是不够的。

#### D-0.2 验证结果

| 检查 | 结果 |
|---|---|
| `grep -rn "SkiaSharp\.\|\bSK[A-Z]\w+\b" WC4MapEditor.Core/` | **0** ✅ |
| `dotnet build WC4MapEditor.Core` | **0 错误**（24 个既有 nullable 警告） |
| `dotnet build WC4MapEditor.csproj`（WPF 根项目） | **0 错误**（2 个既有警告） |
| `dotnet build WC4MapEditor.Cli` | **0 错误 0 警告** |
| `Core.csproj` 剩余包引用 | 仅 `System.CommandLine` |

---

<details>
<summary>原始方案（保留备查，已被"整体移出"策略取代）</summary>

#### D-1. 抽象接口（留在 Core）

| 现有类型 | 改造方案 |
|---|---|
| `IFlagImageService` / `FlagImageService` | 接口留在 Core，但方法签名去掉 `SKBitmap`，改用 `byte[]` 或自定义 `RawImage` |
| `IFlagEditorService` / `FlagEditorService` | 同上 |
| `IImageEditorService` / `ImageEditorService` | 同上 |
| `TacticalMapEditor` | 图像处理部分移到实现层 |
| `HdAtlasEditor` | 同上 |

#### D-2. 新建 `WC4MapEditor.Imaging`（承载 Skia 实现）

```
WC4MapEditor.Imaging/
├── FlagImageService.cs        (Skia 实现)
├── FlagEditorService.cs
├── ImageEditorService.cs
├── TacticalMapImageWriter.cs
└── HdAtlasImageWriter.cs
```

#### D-3. 从 Core.csproj 移除

```xml
<!-- 移除 -->
<PackageReference Include="SkiaSharp" Version="3.119.2" />
```

**验收**：`Core.csproj` 仅剩 `System.CommandLine`（阶段 E 也会移除），`grep -r "using SkiaSharp" WC4MapEditor.Core/` 返回 0。

</details>

---

### 阶段 E：CLI 框架移出 Core 🔄 已重新评估

> **修订说明（2026-09-12）**：重新评估后，**本阶段建议降级或跳过**。
>
> 1. `System.CommandLine` 是跨平台、轻量的解析库；`CliCommandHost` 承载的是"命令定义"这一**领域能力**，
>    而非宿主技术细节。Core 引用它属合理关注点，不构成技术泄漏。
> 2. 真正的问题是渲染层依赖**具体类**，该问题已由阶段 B 的 `ICommandHost` 接口解决。
> 3. 若强行把 `CliCommandHost` 移出 Core，WPF 调试控制台同样需要它，将被迫让根项目引用
>    `WC4MapEditor.Cli`（一个 Exe 项目），反而制造出更差的依赖关系。
> 4. `CommandHostProvider` 已把具体类型知识收敛到单点，将来若确有拆分需要，改动成本已降至最低。
>
> **结论**：保持现状。下方原始方案保留备查。

#### 原始方案（备查）

#### E-1. 移动 `CliCommandHost`

```
WC4MapEditor.Core/Commands/CliCommandHost.cs
   ↓
WC4MapEditor.Cli/Commands/CliCommandHost.cs
```

同时为 WPF 调试控制台保留能力：让 `WC4MapEditor` 根项目可直接引用 `WC4MapEditor.Cli`（或把命令构建逻辑下沉到 `WC4MapEditor.Application`）。

#### E-2. 从 Core.csproj 移除

```xml
<!-- 移除 -->
<PackageReference Include="System.CommandLine" Version="2.0.0-beta5.25277.114" />
```

**验收**：`Core.csproj` 无任何 `PackageReference`。

---

### 阶段 F：模型层去单例 ✅ 已完成

**侦察结果**：`Core/Models/` 下的单例依赖仅 **2 处**：

```
Terrain.cs:89      public static string GetTerrainName(int terrainId) => ConfigManager.Instance.GetTerrainTypeName(terrainId);
HexCellInfo.cs:101 public static string GetTerrainName(int tileType)   => ConfigManager.Instance.GetTerrainTypeName(tileType);
```

**进一步发现：这 2 处所在的 3 个方法全部是死代码**：

| 方法 | 调用方 |
|------|--------|
| `Terrain.GetTerrainName(int)` | 仅被同文件的 `Terrain.GetTerrainTypeName()` 调用 |
| `Terrain.GetTerrainTypeName()`（实例） | **无调用方** |
| `HexCellInfo.GetTerrainName(int)` | **无调用方** |

**实际做法**：直接删除，而非按原计划抽象 `ITerrainNameProvider`。

> **有趣的旁证**：对比 `.kilo` 工作树中的旧副本，`Terrain.GetTerrainName` 原本是一个**硬编码 switch**
> （纯数据，无外部依赖），后来被改写成调用 `ConfigManager.Instance` —— 即该单例依赖是**后引入的污染**，
> 而非原始设计。删除后模型回归纯数据。

**改动**：
- `Terrain.cs`：删除 2 个方法 + `using WC4MapEditor.Core.Config;`
- `HexCellInfo.cs`：删除 1 个方法 + `using WC4MapEditor.Core.Config;`

**验证结果**：

| 检查 | 结果 |
|---|---|
| `grep -rn "\.Instance" WC4MapEditor.Core/Models/` | **0** ✅ |
| `grep -rn "using WC4MapEditor.Core.Config" WC4MapEditor.Core/Models/` | **0** ✅ |
| `dotnet build WC4MapEditor.csproj` | **0 错误** |
| `dotnet build WC4MapEditor.Cli` | **0 错误 0 警告** |

**结论**：`Core.Models` 现在不含任何全局状态引用，可独立进行单元测试。

#### 原始方案（备查）

```csharp
// 改前：Core/Models/Terrain.cs:89
public static string GetTerrainName(int terrainId)
    => ConfigManager.Instance.GetTerrainTypeName(terrainId);

// 方案 1：接口注入
public interface ITerrainNameProvider
{
    string GetTerrainTypeName(int terrainId);
}

// 方案 2：上移到服务层，模型保持纯数据
public sealed class TerrainNameService
{
    private readonly ITerrainNameProvider _provider;
    public string GetName(int terrainId) => _provider.GetTerrainTypeName(terrainId);
}
```

---

### 阶段 G：Core/SceneManagement 下沉 ✅ 已完成

#### G-0. 侦察结论（决定了方案）

| 检查项 | 结果 | 含义 |
|--------|------|------|
| Views 是否使用 `GlobalCopied*` | **0 处** | 剪贴板只在 Core 内部流转，可整体留在 Core |
| Core 对 `RenderSceneManager` 的依赖点 | 3 处（`TerrainPaintMode` / `ProvinceEditMode` / `ProvinceModifier`） | 全部只为剪贴板 |
| `GlobalCopiedFromSceneId` / `...ProvinceFromSceneId` | **只写不读** | 死字段，直接删除 |
| `ClearGlobalCopiedData()` | **无调用方** | 死方法，直接删除 |
| `RenderSceneManager` 中的场景管理能力 | 被 Views 大量使用（`CreateScene` / `ActivateScene` / 场景列表 / 磁盘换出等） | 留在应用层 |

**结论**：不需要引入 `IMapClipboardService` 接口 —— 剪贴板是纯数据，
直接作为一个独立的 Core 类即可；需要下沉的只是"场景管理"那部分 UI 概念。

#### G-1. 新增 `Core/Services/MapClipboard.cs`

把 12 个剪贴板属性（地形 6 个 + 省份 6 个）与 `Clear()` 从 `RenderSceneManager` 迁移过来。
纯数据、无 UI 依赖，Core 持有它不构成耦合。

#### G-2. `RenderSceneManager` 下沉到根项目

```
WC4MapEditor.Core/SceneManagement/RenderSceneManager.cs
   ↓ (git mv)
Services/RenderSceneManager.cs
```

- 命名空间：`WC4MapEditor.Core.SceneManagement` → `WC4MapEditor.Services`
- `Core/SceneManagement/` 目录随之清空并删除

#### G-3. Core 调用点改造

```csharp
// 改前
var sceneManager = RenderSceneManager.Instance;
sceneManager.GlobalCopiedTerrain = ...;
sceneManager.GlobalCopiedFromSceneId = sceneManager.CurrentSceneId;  // ← 死字段，删除

// 改后
var clipboard = MapClipboard.Instance;
clipboard.GlobalCopiedTerrain = ...;
```

涉及 3 个文件：`TerrainPaintMode.cs`、`ProvinceEditMode.cs`、`ProvinceModifier.cs`。
另修正 3 处 `using`（含 `ModeContext.cs` 中一处未使用的残留 using）。

#### G-4. 执行中发现的坑

| # | 坑 | 表现 | 处理 |
|---|---|---|---|
| 1 | **`Path` 类型歧义** | 文件从 Core 移到根项目后，`System.IO.Path` 与根项目 `GlobalUsings.cs` 里的 `global using System.Windows.Shapes;`（其 `Path` 是 WPF Shape）**冲突** → `CS0104` + 连锁类型推断错误 | 改用根项目已定义的 `IOPath` 别名（`global using IOPath = System.IO.Path;`） |
| 2 | 连锁错误放大 | 一个 `Path` 歧义引发 9 个报错（`ext`/`name` 变量类型推断失败） | 定位根因后一次修复 |

> **教训**：跨程序集移动文件时，目标程序集的 `GlobalUsings` 会引入**新的名字冲突源**。
> Core 没有 `System.Windows.Shapes`，根项目有 —— 这是"移入方向"特有的风险。

#### G-5. 验证结果

| 检查 | 结果 |
|---|---|
| `grep -rn "RenderSceneManager\|SceneManagement" WC4MapEditor.Core/` | **0**（仅剩 `MapClipboard.cs` 注释中的说明文字） ✅ |
| `dotnet build WC4MapEditor.csproj` | **0 错误** |
| `dotnet build WC4MapEditor.Cli` | **0 错误 0 警告** |

**结论**：Core 不再持有任何"渲染场景"概念；跨场景剪贴板成为 Core 内的纯数据服务。

#### 原始方案（备查）

```csharp
// 原计划：抽象 IMapClipboardService 接口注入
public interface IMapClipboardService
{
    TerrainData? CopiedTerrain { get; set; }
    Province? CopiedProvince { get; set; }
    void Clear();
}
// 实际：因剪贴板纯数据且 Views 不直接使用，无需接口，直接使用 MapClipboard 类
```

---

### 阶段 H：单例 → 依赖注入 🔄 试点完成（渐进迁移中）

#### H-0. 单例使用规模实测（2026-09-12）

| 单例 | 使用点 | 单例 | 使用点 |
|------|-------:|------|-------:|
| `ConfigManager` | 107 | `RenderSceneManager` | 9 |
| `HexSelector` | 41 | `KeyboardManager` | 8 |
| `CommandManager` | 15 | `EditModeManager` | 8 |
| `TacticalMapImageCache` | 11 | `ErrorCollector` | 7 |
| `MapClipboard` | 5 | `GeneralSettingParser` | 5 |
| `CliCommandHost` | 4 | `CountrySettingParser` | 4 |
| `HexInfoService` | 2 | `CoastMaskProcessor` / `AssetCache` / `CoastHelper` / `TextureDiskCache` / `DebugConsole` | 各 1 |

#### H-1. 试点：`MouseManager` ✅

选它作为首个试点的原因：**依赖链最短**（`DI → MainWindow → RenderSceneBase`），且只有 1 个消费点。

**改动**：

| 文件 | 改动 |
|------|------|
| `Core/Input/MouseManager.cs` | `private MouseManager()` → `public MouseManager()`；`Instance` 加【迁移中】说明 |
| `App.xaml.cs` | 新增 `services.AddSingleton<MouseManager>(_ => MouseManager.Instance);` |
| `Views/MainWindow.xaml.cs` | 构造注入 `MouseManager`，公开 `MouseManager` 属性 |
| `Views/RenderSceneBase.cs` | 字段去掉初始化器，构造体改为 `_mouseManager = window.MouseManager;` |

> 注意 `RenderSceneBase` 的坑：该字段原先用**字段初始化器**（`= MouseManager.Instance`），
> 而 `Window` 是构造函数体中才赋值的 —— 若直接改成 `= Window.MouseManager` 会 `NullReferenceException`
> （字段初始化器先于构造体执行）。必须改为在构造体中赋值。

**验证**：根项目 + CLI 编译均 **0 错误**。

#### H-1.5. 批次 1：5 个单消费点单例 ✅

| 单例 | 处理方式 |
|------|---------|
| `DebugConsole` | **完整注入链**（套用 `MouseManager` 模板）：构造改 public → `MainWindow` 构造注入并公开属性 → `RenderSceneBase` 构造体取值 |
| `TextureDiskCache` | 构造改 public + DI 注册（深度链，**暂缓**全链路改造，理由见下） |
| `CoastHelper` | 同上 |
| `CoastMaskProcessor` | 同上 |
| `AssetCache` | 构造本就是 public（`AssetManager` 已预留 `internal AssetManager(AssetCache cache)` 注入构造），仅做 DI 注册 |

**DI 注册**（`App.xaml.cs`）：

```csharp
services.AddSingleton<MouseManager>(_ => MouseManager.Instance);
services.AddSingleton<DebugConsole>(_ => DebugConsole.Instance);
services.AddSingleton<AssetCache>(_ => AssetCache.Instance);
services.AddSingleton<TextureDiskCache>(_ => TextureDiskCache.Instance);
services.AddSingleton<CoastHelper>(_ => CoastHelper.Instance);
services.AddSingleton<CoastMaskProcessor>(_ => CoastMaskProcessor.Instance);
```

**为什么后 3 个不做全链路注入**：

它们的消费点在渲染层深处 —— `SkiaRenderEngine` → `BackGroundRender` / `LandTerrainsRender`
→ `TerrainHelper`，共 3~5 层构造链要改；而它们本质是**进程级缓存 / 图集资源**，语义上就是全局的。

- 收益（可测试性）< 成本（6+ 个构造签名）
- 当前处理为"**可注入化 + 容器可解析**"，待出现真实测试需求时再打通链路

> 注：`RenderEngineFactory.Create()` 是渲染层依赖的天然注入切入点
> （`RenderSceneBase` → `MainWindow` → `RenderEngineFactory.Create(...)` → `SkiaRenderEngine`），
> 未来若需打通，从这里加可选参数即可。

#### H-1.6. 批次 2：解析器与服务类 ✅（差异化处理）

批次 2 **没有机械套用模板** —— 这 4 个类型的性质与批次 1 不同，逐类评估后分别处理：

| 单例 | 使用点 | 处理 | 理由 |
|------|-------:|------|------|
| `GeneralSettingParser` | 5 | **仅 DI 注册**，不改构造 | 构造体含 I/O（`ResolvePaths()` + `LoadAll()`）。开放 public 构造会**诱导重复加载配置**；单例在此是正确语义 |
| `CountrySettingParser` | 4 | **仅 DI 注册**，不改构造 | 同上 |
| `HexInfoService` | 2 | 构造改 public + DI 注册 | 有状态（持有 `_mapData`），且**已实现 `IHexInfoProvider` 接口** —— 抽象已就位，值得开放实例化 |
| `CliCommandHost` | 4 | **不改动** | 阶段 B 已通过 `ICommandHost` 抽象收敛；剩余 3 处全在 Views 宿主层，属正常用法 |

```csharp
services.AddSingleton<HexInfoService>(_ => HexInfoService.Instance);
services.AddSingleton<GeneralSettingParser>(_ => GeneralSettingParser.Instance);
services.AddSingleton<CountrySettingParser>(_ => CountrySettingParser.Instance);
services.AddSingleton<ICommandHost>(_ => CliCommandHost.Instance);   // 注册接口而非实现
```

> **关键判断**：必须写 `AddSingleton<T>(_ => T.Instance)` 而非 `AddSingleton<T>()`。
> 后者会让容器**自行 new 一个** —— 对构造含 I/O 的类型意味着重复加载，
> 且与既有单例**不是同一对象**，会造成状态分裂（两处 `_mapData` / 两套配置）。

#### H-1.7. 批次 3：输入与错误处理 ✅（有节制推进）

批次 3 的 3 个单例在 Core 内部**互相依赖**，形成依赖网：

```
EditModeManager ──→ ErrorCollector
       │
       └────────→ KeyboardManager ──→ ErrorCollector
```

| 单例 | 使用点 | 处理 |
|------|-------:|------|
| `KeyboardManager` | 8 | 构造改 public + DI 注册 + **View 层完整注入**：`MainWindow` 构造注入并公开属性（4 处按键处理改用属性）、`RenderSceneBase` 构造体取值 |
| `ErrorCollector` | 7 | 构造改 public + DI 注册；**消费点不改** |
| `EditModeManager` | 8 | 构造改 public + DI 注册；**消费点不改** |

**为什么只给 `KeyboardManager` 做完整注入**：

它的 View 层消费点集中且可控（`MainWindow` 4 处 + `RenderSceneBase` 2 处）。
而 `ErrorCollector` / `EditModeManager` 的主要消费点在 **Core 内部**
（`ModeContext` 5 处、`EditModeManager` 自身、`CliCommandHost` 1 处、CLI `Program.cs` 1 处）。

> **这暴露了一条 DI 改造的架构边界**：
> 依赖注入只能覆盖**宿主层**（Views / 组合根）。位于 **Core 内部**的服务定位调用，
> 要么保持单例，要么把整个 Core 改成"由宿主注入全部依赖"（那是另一个量级的工程）。
>
> 这也解释了为什么"消灭全部单例"不应该作为目标 —— **Core 库在没有 DI 容器的场景下
> （如 CLI 独立运行）天然需要某种默认实例入口**。

#### H-1.8. 批次 4：选择 / 命令 / 战术图缓存 ✅

| 单例 | 使用点 | 处理 |
|------|-------:|------|
| `HexSelector` | 41 | 构造改 public + DI 注册 + **View 层完整注入**（`RenderSceneBase` 29 处集中替换） |
| `CommandManager` | 15 | 构造改 public + DI 注册 + **View 层完整注入**（`RenderSceneBase` 8 处） |
| `TacticalMapImageCache` | 11 | 仅 DI 注册（已是 public 默认构造）；消费点全在渲染层内部，保持单例 |

**改动量**：`MainWindow` 构造从 3 参数增至 **6 参数**，公开 6 个服务属性；
`RenderSceneBase` 的 **39 处**单例访问改为字段注入。

**踩到两个坑（均有普遍性）**：

| # | 坑 | 表现 | 处理 |
|---|-----|------|------|
| 1 | **`replace_all` 误伤全限定名** | 替换 `HexSelector.Instance` → `_hexSelector` 时，源码里的 `Core.Selection.HexSelector.Instance` 被改成 `Core.Selection._hexSelector`；`CommandManager` 同理，共 10 处语法错误 | 追加两轮修复：`Core.Selection._hexSelector` → `_hexSelector`、`WC4MapEditor._hexSelector` → `_hexSelector` |
| 2 | **与 WPF 类型同名** | `WC4MapEditor.Core.Commands.CommandManager` 与 `System.Windows.Input.CommandManager`（根项目 GlobalUsings 引入）冲突 → `CS0104` | 三处（`MainWindow` / `RenderSceneBase` / `App.xaml.cs`）改用别名 `CoreCommandManager` |

> **教训 1**：批量替换 `X.Instance` 这类"点号后缀"模式前，必须先确认没有 `Namespace.X.Instance` 的**全限定写法**，
> 否则会破坏限定名。安全做法是加词边界（`\bX\.Instance\b`），或先统计全限定写法出现次数。
>
> **教训 2**：此坑与阶段 G 的 `Path` 歧义**同源** —— 根项目因 `UseWPF` 引入了大量 `System.Windows.*` 全局 using，
> 任何移入根项目的代码都可能撞上 WPF 的同名类型（`Path`、`CommandManager` …）。

#### H-2. 可复制的迁移模板

```
① 单例类      : 构造改 public；Instance 加【迁移中】注释
                （不用 [Obsolete] 特性 —— 迁移期调用点仍走 Instance，会产生大量警告噪音）
② App.xaml.cs : AddSingleton<T>(_ => T.Instance)   ← 过渡期保证与旧调用点同一实例
③ 最高层持有者 : 构造注入 + 公开属性
④ 下游消费点  : 从持有者取，替换 .Instance
⑤ 全部迁完后  : 删除 Instance 与①的注释
```

**关键约束**：每迁移一个单例都必须跑 **GUI 冒烟**。
单例改注入的典型故障（初始化顺序、`NullReferenceException`）**编译期发现不了**，只有运行时暴露。

#### H-3. 建议推进顺序（按"消费点数量 × 注入链深度"排序）

| 批次 | 单例 | 状态 |
|------|------|------|
| 1 | `DebugConsole` + `TextureDiskCache` / `CoastHelper` / `CoastMaskProcessor` / `AssetCache` | ✅ 完成 |
| 2 | `HexInfoService` / `GeneralSettingParser` / `CountrySettingParser` / `CliCommandHost` | ✅ 完成（差异化处理） |
| 3 | `ErrorCollector`(7)、`KeyboardManager`(8)、`EditModeManager`(8) | ✅ 完成（仅 `KeyboardManager` 做 View 层注入） |
| 4 | `HexSelector`(41)、`CommandManager`(15)、`TacticalMapImageCache`(11) | ✅ 完成（`RenderSceneBase` 39 处集中注入） |
| 5 | `ConfigManager`(107) | ⏸ 待办 |

**后续批次的预判**（基于批次 1/2 的实际经验）：

- **批次 3~4 的单例都是"有状态 + 多消费点"**：注入需透传多层
  （例如 `HexSelector` 被 41 处使用，含渲染层深处的 `SelectionRender`；
  `KeyboardManager` 同时被 `RenderSceneBase` 与 `MainWindow` 使用）。
  改动面远大于批次 1/2，且每个消费点都需保证运行时正确。
- **`ConfigManager`(107 处) 不建议做注入改造**：它是**应用级配置根**，语义上就是全局单例。
  合理做法是"注册进容器 + 保留 `Instance`"，而非让 107 个调用点改签名。
- **结论**：H 的**高收益部分已在批次 1/2 完成**；剩余部分边际收益递减、风险递增。

> `MapClipboard`(5) 与 `RenderSceneManager`(9) 承载**进程级状态**，语义上就是全局的，
> 是否纳入 DI 需单独评估 —— 强行改成 Scoped 可能引入 bug。

#### H-4. 关于是否继续 —— 建议在此收敛

H 与前序阶段性质不同：**不是修一处耦合，而是全局架构改造**。

- **收益**：可测试性、可替换性、依赖显式化
- **成本**：触及大量构造签名；每个单例都需 GUI 冒烟（编译通过 ≠ 运行时正常）
- **已完成**：批次 1（6 个单例）+ 批次 2（4 个单例），覆盖了**收益最高、风险最低**的部分

**建议**：H **到此收敛**，除非出现具体驱动力：

| 驱动力 | 是否支持继续改造 |
|--------|----------------|
| 需要为 Core/编辑逻辑写单元测试 | ✅ 支持 —— 优先 `EditModeManager`、`HexSelector` |
| 需要同进程多实例并发（如批量处理多张地图） | ✅ 支持 —— 优先 `EditModeManager`（改 Scoped） |
| 单纯追求"消灭所有单例" | ❌ 不支持 —— 收益是形式上的，风险是运行时的 |

**若继续**，务必遵守 H-2 模板 + 每批一次 GUI 冒烟；批次 3~5 的每批改动量都应视为独立迭代。

#### 原始方案（备查）

```csharp
// 过渡策略：保留 Instance 作为兼容层
public sealed class EditModeManager
{
    public EditModeManager(/* 依赖注入 */) { }          // 新增：供 DI 使用

    [Obsolete("请使用依赖注入获取实例")]
    public static EditModeManager Instance { get; } = new();  // 保留：存量代码
}
```

---

## 五、回归验证清单

### 5.1 每阶段必过（编译 + 冒烟）

```powershell
# 1. 全量编译
dotnet build WC4MapEditor.csproj -c Release
# 期望：0 Error

# 2. CLI 冒烟
dotnet run --project WC4MapEditor.Cli -- stage <测试.btl> -o out.json
dotnet run --project WC4MapEditor.Cli -- screenshot <测试.bin> out.png

# 3. GUI 冒烟（手动）
#   - 启动主程序，开场动画正常
#   - 打开一个 .btl 剧本 → 渲染出地图
#   - 打开一个 world.bin → 渲染出地图
#   - 切换编辑模式（地形/省份/建筑/部队）
#   - 撤销/重做
#   - 保存并重新打开，数据一致
```

### 5.2 阶段专项验收

| 阶段 | 验收命令 / 动作 | 期望结果 | 状态 |
|------|----------------|---------|------|
| A | `dotnet build WC4MapEditor.Rendering -c Release` | 0 错误 | ✅ 已实测 |
| A | `grep -rn "System.Windows" WC4MapEditor.Rendering/` | 返回 0 | ✅ 已实测 |
| A | `dotnet publish WC4MapEditor.Cli -r linux-x64 --self-contained false` | 构建成功 | ⏳ 待验证 |
| B | `grep -rn "CliCommandHost" WC4MapEditor.Rendering/` | 返回 0 | ✅ 已实测 |
| C | `grep -rn "namespace WC4MapEditor\.Models" --include=*.cs .` | 返回 0 | ✅ 已实测 |
| C | `grep -rn "namespace WC4MapEditor\.Parsers" --include=*.cs .` | 返回 0 | ✅ 已实测 |
| C | `dotnet build WC4MapEditor.csproj`（WPF 根项目） | 0 错误 | ✅ 已实测 |
| C | `dotnet build WC4MapEditor.Cli` | 0 错误 0 警告 | ✅ 已实测 |
| D | `grep -rn "SkiaSharp\.\|\bSK[A-Z]\w+\b" WC4MapEditor.Core/` | 返回 0 | ✅ 已实测 |
| D | `dotnet build WC4MapEditor.Core` 后检查输出目录 | 无 `libSkiaSharp` | ✅ 已实测 |
| D | `dotnet build WC4MapEditor.csproj`（WPF 根项目） | 0 错误 | ✅ 已实测 |
| D | `dotnet build WC4MapEditor.Cli` | 0 错误 0 警告 | ✅ 已实测 |
| E | — | 已重新评估，建议跳过 | 🔄 已修订 |
| F | `grep -rn "\.Instance" WC4MapEditor.Core/Models/` | 返回 0 | ✅ 已实测 |
| G | `grep -rn "RenderSceneManager" WC4MapEditor.Core/` | 返回 0 | ✅ 已实测 |
| G | `dotnet build WC4MapEditor.csproj` | 0 错误 | ✅ 已实测 |
| G | `dotnet build WC4MapEditor.Cli` | 0 错误 0 警告 | ✅ 已实测 |

### 5.2.1 阶段 A/B 后尚未执行的验证

阶段 A/B 改动后**根项目（WPF）与 CLI 的完整编译尚未跑通**，请补跑：

```powershell
# 根项目：验证 RenderSceneBase 的 SetHostWindow 调用点 + SKElement 仍在根项目可用
dotnet build WC4MapEditor.csproj -c Release

# CLI：验证 net10.0 目标框架切换
dotnet build WC4MapEditor.Cli/WC4MapEditor.Cli.csproj -c Release

# 跨平台验证（阶段 A 的核心收益）
dotnet publish WC4MapEditor.Cli/WC4MapEditor.Cli.csproj -c Release -r linux-x64 --self-contained false
```

**GUI 冒烟重点**（阶段 A 涉及控制台窗口生命周期）：
1. 按 F3 打开调试控制台 → 应正常弹出
2. 输入 `help` 等命令 → 应有输出
3. 按 Esc / F3 关闭控制台 → **窗口应正常关闭**（这条覆盖 `SetHostWindow(window.Close)` 的改动）
4. 拖入 `.zme` 脚本文件执行 → 批量模式正常

### 5.3 数据一致性验证（关键）

**BTL 往返测试**（防止解析器改动导致数据损坏）：

```powershell
# 原始文件
$orig = "测试.btl"

# 加载 → 保存 → 再加载
dotnet run --project WC4MapEditor.Cli -- stage $orig -o before.json -d
dotnet run --project WC4MapEditor.Cli -- modify stage $orig info   # 触发一次回写
dotnet run --project WC4MapEditor.Cli -- stage $orig -o after.json -d

# 对比
Compare-Object (Get-Content before.json) (Get-Content after.json)
# 期望：无差异
```

> ⚠️ 请在**副本**上测试，勿用原始存档。

### 5.4 性能回归（针对内存优化）

打开 1728 万格的 world.bin，记录：
- 内存占用（任务管理器）
- 首屏渲染耗时
- 平移/缩放帧率

与 `docs/memory-optimization-plan.md` 中的基线（~4403 MB）对比，**不应有明显劣化**。

---

## 六、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| **命名空间批量替换出错** | 大面积编译失败 | 阶段 C 前确保工作区干净；优先用 IDE 重构；分小批提交 |
| **Skia 抽象改动破坏旗帜/图像处理** | 旗帜编辑、战术地图功能异常 | 阶段 D 前后各做一次旗帜编辑手工验证；保留旧实现做对照 |
| **单例改造引入初始化顺序问题** | 运行时 NullReference | 阶段 H 采用 `Obsolete` 过渡法，不一次性删除；每个单例单独提交 |
| **BTL 二进制回写出错** | **存档损坏** | 5.3 往返测试必过；测试只在副本上进行 |
| **CLI 跨平台后路径分隔符问题** | Linux 下文件找不到 | 检查 `Path.Combine` 使用；避免硬编码 `\` |
| **`WC4MapEditor.sln` 已删除** | IDE 无法加载解决方案 | 需重建 sln：`dotnet new sln` + `dotnet sln add` 各项目 |

### 6.1 关于 `WC4MapEditor.sln` 缺失

基线提交中 `WC4MapEditor.sln` 处于**已删除**状态。开始重构前建议先恢复：

```powershell
dotnet new sln -n WC4MapEditor
dotnet sln add WC4MapEditor.csproj
dotnet sln add WC4MapEditor.Core/WC4MapEditor.Core.csproj
dotnet sln add WC4MapEditor.Rendering/WC4MapEditor.Rendering.csproj
dotnet sln add WC4MapEditor.Cli/WC4MapEditor.Cli.csproj
```

> 注：阶段 A 最终未新建 `Rendering.Wpf` 项目，故无需追加。

### 6.2 `.kilo` 工作树是否污染编译？—— 已核查，无问题

`.kilo/worktrees/scented-punch/` 下有 **87 个 `.cs`** 与 **4 个 `.xaml`**，且根项目 `WC4MapEditor.csproj`
的 `<Compile Remove>` 列表未显式排除该目录，曾引起"项目副本被重复编译导致类型冲突"的担忧。

**实测核查（2026-09-12）**：

```powershell
dotnet msbuild WC4MapEditor.csproj -getItem:Compile -p:Configuration=Release
# → 实际编译项中匹配 ".kilo" 的数量：0
```

**结论**：.NET SDK 的 `DefaultExcludesInProjectFolder` 默认包含 `**/.*/**`，
会自动排除所有以 `.` 开头的目录。`.kilo` 不参与编译，**无需额外配置**。

---

## 七、建议执行顺序

```
阶段 A（斩断 WPF）  ✅ 已完成
   ↓
阶段 B（断开 CLI）   ✅ 已完成
   ↓
【验证 + 提交】      ← ⏳ 待补跑根项目/CLI 编译与 GUI 冒烟
   ↓
阶段 C（命名空间）   ← 机械操作，建议在功能冻结期做
   ↓
阶段 D（剥离 Skia）  ✅ 已完成
阶段 E（移出 CLI）   🔄 已重新评估，建议跳过
   ↓
【验证 + 提交】
   ↓
阶段 F（模型去单例）  ✅ 已完成
阶段 G（场景管理下沉）  ✅ 已完成
   ↓
阶段 H（全面 DI）    ✅ 批次1/2完成，建议收敛（见 H-4）
```

---

## 附录：快速回滚

```powershell
# 回到基线（丢弃所有重构改动）
git reset --hard 59da8c4

# 回退单个阶段但保留改动
git reset --soft HEAD~1

# 查看各阶段提交
git --no-pager log --oneline
```
