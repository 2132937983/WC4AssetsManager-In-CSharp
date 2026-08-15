# WC4MapEditor C# 重写计划

## 项目概述

将现有的 VB.NET WPF/WinForms 混合架构地图编辑器（WC4MapEditor）完整重写为 C# / .NET Core 应用，采用 **CLI + GUI 分离架构**：

- **核心库（WC4MapEditor.Core）**：无 UI 依赖，承载全部数据模型、文件解析、编辑操作和地图构建逻辑
- **CLI（WC4MapEditor.Cli）**：命令行入口，地图/战役/征服等所有编辑操作均可脱离 GUI 独立执行
- **GUI（WC4MapEditor）**：WPF 界面，通过调用与 CLI 相同的核心命令提供可视化操作；主启动界面简化为轻量入口

**原项目路径**: `E:\VBProject\map`
**目标项目路径**: `E:\CSharpProject\WC4MapEditor`（待创建）

---

## 一、技术栈变更

| 维度 | 原项目 | 新项目 |
|------|--------|--------|
| 语言 | VB.NET | C# 12 / .NET 10 |
| 架构 | 单体 WPF + WinForms 混合 | Core 库 + CLI + WPF GUI 三层 |
| UI 框架 | WPF + WinForms 混合 | 纯 WPF（仅 GUI 层） |
| 主渲染 | SkiaSharp (SKElement/SKControl) | DirectX 11 (Vortice.Windows/Direct3D) |
| 备用渲染 | — | SkiaSharp 3.x (fallback) |
| 架构模式 | 部分 MVVM | 完整 MVVM + DI |
| 入口 | Program.vb (STAThread) | CLI: Program.cs / GUI: App.xaml.cs |
| 命令行 | 无 | System.CommandLine（所有编辑操作命令化） |

---

## 二、解决方案结构规划（CLI + GUI 分离）

```
WC4MapEditor.sln
│
├── WC4MapEditor.Core/               # 核心库（无任何 UI 依赖，net10.0）
│   ├── WC4MapEditor.Core.csproj
│   ├── Models/                      # 数据模型（直接翻译，保持结构体布局）
│   │   ├── BTLConstants.cs
│   │   ├── HexCoord.cs
│   │   ├── TerrainData.cs           # [StructLayout(Sequential,Pack=1)] 16字节
│   │   ├── Province.cs              # [StructLayout(Sequential,Pack=1)] 2字节
│   │   ├── BTLHeader.cs
│   │   ├── MapData.cs               # 纯数据，无 INotifyPropertyChanged
│   │   ├── MapDataOptimized.cs      # 结构体数组存储
│   │   ├── Building.cs / Army.cs / Legion.cs / etc.
│   │   └── ConfigModels.cs
│   │
│   ├── Parsers/                     # 文件解析（二进制 BTL/BIN 格式）
│   │   ├── WorldParser.cs           # 主地图解析（支持流式大文件）
│   │   ├── BTLFileParser.cs
│   │   ├── StageParser.cs           # 战役文件解析
│   │   ├── ConquestParser.cs        # 征服文件解析
│   │   └── GeneralInCountryParser.cs
│   │
│   ├── Commands/                    # 核心命令层（CLI 与 GUI 共用）
│   │   ├── ICommand`1.cs            # 命令抽象: Execute(TContext) → Result
│   │   ├── MapCommands.cs           # LoadMap / SaveMap / ExportMap...
│   │   ├── TerrainCommands.cs       # SetTerrain / PaintBrush / FillRegion...
│   │   ├── BuildingCommands.cs      # AddBuilding / RemoveBuilding / SetBelong...
│   │   ├── ArmyCommands.cs          # SetArmy / MoveArmy / SetGeneral...
│   │   ├── StageCommands.cs         # 战役编辑命令
│   │   ├── ConquestCommands.cs      # 征服编辑命令
│   │   └── CommandContext.cs        # 命令执行上下文（已加载的地图、日志等）
│   │
│   ├── Services/                    # 业务服务
│   │   ├── UndoRedoService.cs       # 撤销/重做（替代 MapUndoRedoManager）
│   │   ├── TerrainModifierService.cs
│   │   ├── BuildingModifierService.cs
│   │   ├── ArmyModifierService.cs
│   │   ├── LegionModifierService.cs
│   │   └── ScreenshotService.cs     # 离屏渲染截图（无窗口，CLI 可用）
│   │
│   ├── Builders/                    # 地图构建（从 Builder 迁移）
│   │   ├── MapBuilder.cs
│   │   ├── StageModifierManager.cs
│   │   └── BrushPaintEngine.cs
│   │
│   ├── Helpers/                     # 工具类（无 WPF 依赖）
│   │   ├── CoastMaskProcessor.cs
│   │   ├── TerrainHelper.cs
│   │   └── TextureDiskCache.cs
│   │
│   └── Config/
│       └── ConfigManager.cs         # 全局配置（System.Text.Json）
│
├── WC4MapEditor.Rendering/          # 渲染库（net10.0-windows，供 GUI 和离屏截图使用）
│   ├── IRenderEngine.cs             # 渲染引擎接口（DX/Skia 双实现）
│   ├── DirectX/
│   │   ├── DXRenderEngine.cs        # Vortice.Windows 主渲染引擎
│   │   ├── DXBackGroundRender.cs
│   │   ├── DXLandTerrainsRender.cs
│   │   ├── DXProvinceRender.cs
│   │   ├── DXBuildingRender.cs
│   │   ├── DXBelongFlagRender.cs
│   │   ├── DXLegionDomainRender.cs
│   │   ├── DXArmyRender.cs
│   │   └── DXTrapRender.cs
│   ├── Skia/
│   │   ├── SkiaRenderEngine.cs      # Skia 备用渲染引擎
│   │   └── ...
│   ├── OffscreenRenderer.cs         # 离屏渲染（CLI 截图/导出用，无需窗口）
│   └── MainRender.cs                # 渲染协调器（Facade）
│
├── WC4MapEditor.Cli/                # CLI 入口（net10.0，仅依赖 Core + Rendering）
│   ├── WC4MapEditor.Cli.csproj
│   ├── Program.cs                   # System.CommandLine 入口
│   ├── Commands/                    # CLI 命令定义（薄壳，映射到 Core.Commands）
│   │   ├── MapCommand.cs            # wc4me map load/save/edit/export...
│   │   ├── StageCommand.cs          # wc4me stage ...
│   │   ├── ConquestCommand.cs       # wc4me conquest ...
│   │   └── ScreenshotCommand.cs     # wc4me screenshot map.btl out.png
│   └── ConsoleOutput.cs             # 控制台输出/进度/退出码
│
├── WC4MapEditor/                    # GUI 入口（net10.0-windows，WPF）
│   ├── WC4MapEditor.csproj
│   ├── App.xaml / App.xaml.cs       # GUI 入口，DI 容器配置
│   ├── AssemblyInfo.cs
│   ├── ViewModels/                  # MVVM 视图模型（调用 Core.Commands）
│   │   ├── MainViewModel.cs
│   │   ├── MapEditorViewModel.cs
│   │   ├── StageEditorViewModel.cs
│   │   ├── ConquestEditorViewModel.cs
│   │   └── SettingsViewModel.cs
│   ├── Views/                       # WPF 视图（替代 WinForms Form）
│   │   ├── MainWindow.xaml          # 轻量启动器（仅入口导航 + 最近文件）
│   │   ├── MapEditorView.xaml       # 地图编辑视图（替代 MapRenderScene）
│   │   ├── StageEditorView.xaml     # 战役编辑视图（替代 StageRenderScene）
│   │   ├── ConquestEditorView.xaml  # 征服编辑视图（替代 ConquestRenderScene）
│   │   ├── RenderSurfaceView.xaml   # 渲染画布视图（DX/Skia 切换）
│   │   ├── StatusBarView.xaml
│   │   └── Dialogs/
│   │       ├── HexInfoWindow.xaml
│   │       ├── DebugConsoleView.xaml
│   │       ├── BuildingSettingView.xaml
│   │       ├── ArmySettingView.xaml
│   │       └── ...
│   ├── Commands/                    # WPF ICommand 适配（RelayCommand → Core.Commands）
│   │   ├── RelayCommand.cs
│   │   └── MusicCommands.cs
│   ├── Services/                    # GUI 专属服务
│   │   └── MusicService.cs
│   └── Helpers/
│       └── WpfWindowHelper.cs
│
├── Resource/                        # 资源文件（解决方案级共享，从原项目复制）
│   ├── Texture/
│   ├── Music/
│   ├── Config/
│   └── Data/
│
└── docs/                            # 文档（从原项目复制）
```

**依赖方向**（单向，禁止反向引用）：

```
Cli ──────┐
          ├──► Core ◄──┐
Gui ─► Rendering ──────┘
```

- Core 不依赖 WPF/WinForms/渲染库，可在任意 .NET 10 环境（含无 GUI 服务器）运行
- CLI 通过 Core.Commands + OffscreenRenderer 完成全部编辑与导出操作
- GUI 的按钮/菜单最终调用与 CLI 相同的 Core.Commands，保证两条路径行为一致

---

## 二点五、CLI 命令设计

所有编辑操作命令化，GUI 与 CLI 共享同一套命令实现：

```
wc4me map load <file>                        # 加载并校验地图，输出摘要
wc4me map save <file> [--backup]
wc4me map export <file> -o <png> [--zoom 2]  # 离屏渲染导出图片
wc4me terrain set <col> <row> <type> [--file map.btl]
wc4me terrain brush <col> <row> --brush <name> --size <n>
wc4me building add <col> <row> --type city --belong <country>
wc4me army set <col> <row> --general <name> --soldiers <n>
wc4me stage list/edit <file> ...
wc4me conquest edit <file> ...
wc4me batch <script.wc4>                    # 批量执行命令脚本（JSON 行格式）
```

命令返回统一退出码：0 成功 / 1 参数错误 / 2 文件解析失败 / 3 编辑约束冲突。

---

## 三、核心架构设计

### 3.1 渲染引擎抽象

```csharp
// 渲染引擎接口 - DX 和 Skia 共用同一接口
public interface IRenderEngine : IDisposable
{
    bool IsAvailable { get; }           // DX 是否可用
    string EngineName { get; }
    
    void Initialize(IntPtr hwnd, int width, int height);
    void Resize(int width, int height);
    void Render(MapData mapData, Camera camera);
    void Invalidate();
    
    // 渲染开关
    bool EnableTerrainsRender { get; set; }
    bool EnableBackgroundRender { get; set; }
    bool EnableProvinceRender { get; set; }
    // ...
    
    // 交互
    (int col, int row) ScreenToHex(double screenX, double screenY);
    (double x, double y) HexToScreen(int col, int row);
}

// 渲染引擎工厂 - 自动选择 DX 或 Skia
public static class RenderEngineFactory
{
    public static IRenderEngine Create()
    {
        var dxEngine = new DXRenderEngine();
        if (dxEngine.IsAvailable)
            return dxEngine;
        return new SkiaRenderEngine(); // fallback
    }
}
```

### 3.2 DI 容器配置（App.xaml.cs，仅 GUI 层）

CLI 入口不使用 WPF/DI 容器，直接构造 Core.Commands 执行；GUI 复用同一命令层：

```csharp
// WC4MapEditor.Cli/Program.cs
public static int Main(string[] args)
{
    var root = BuildRootCommand();   // map/stage/conquest/screenshot 子命令
    return root.Invoke(args);
}
```

```csharp
// WC4MapEditor/App.xaml.cs（GUI）
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        
        // 核心服务（来自 Core 库）
        services.AddSingleton<ConfigManager>();
        services.AddSingleton<IRenderEngine>(_ => RenderEngineFactory.Create());
        services.AddSingleton<IUndoRedoService, UndoRedoService>();
        
        // ViewModels（内部调用 Core.Commands）
        services.AddTransient<MainViewModel>();
        services.AddTransient<MapEditorViewModel>();
        
        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<MapEditorView>();
        
        Services = services.BuildServiceProvider();
        
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
```

### 3.3 主界面定位

GUI 主窗口（启动器）做轻：仅提供入口导航（新建/打开地图、最近文件、打开战役/征服）+ 设置入口。所有实际编辑功能在 MapEditorView / StageEditorView / ConquestEditorView 中，且每个操作最终调用 Core.Commands —— 这些命令同时可由 CLI 独立执行，GUI 崩溃或缺失不影响命令行工作流。

原项目使用 `SceneManager` 管理 WinForms Form 切换。新项目改为 WPF `ContentControl` + `DataTemplate`：

```xml
<!-- MainWindow.xaml -->
<Grid>
    <ContentControl Content="{Binding CurrentViewModel}" />
</Grid>
```

```csharp
// MainViewModel.cs
public class MainViewModel : ObservableObject
{
    private object _currentViewModel;
    public object CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }
    
    [RelayCommand]
    private void NavigateToMapEditor(MapData mapData)
    {
        CurrentViewModel = new MapEditorViewModel(mapData, _renderEngine, _undoRedoService);
    }
}
```

---

## 四、WinForms → WPF 迁移要点

### 4.1 BaseRenderScene 迁移

原 `BaseRenderScene : Form` 包含大量 WinForms 逻辑（状态栏、拖动、动画）。迁移策略：

| WinForms | WPF 替代 |
|----------|----------|
| `Form` | `Window` / `UserControl` |
| `SKControl` (WinForms) | `SKElement` (WPF) 或 DX `D3DImage` |
| `System.Windows.Forms.Timer` | `DispatcherTimer` |
| `Panel` / `FlowLayoutPanel` | `Grid` / `StackPanel` / `WrapPanel` |
| `Button.Click` | `ICommand` |
| `Form.Show/Close` | `Window.ShowDialog/Close` |
| `MessageBox.Show` | `System.Windows.MessageBox` |
| `Graphics` 自定义绘制 | SkiaSharp `SKPaintSurface` 事件 |
| `Opacity` (Layered Window) | `Window.Opacity` / `UIElement.Opacity` |

### 4.2 渲染画布控件

创建 `RenderSurfaceView.xaml` 作为 DX/Skia 双引擎的宿主：

```xml
<UserControl x:Class="WC4MapEditor.Views.RenderSurfaceView">
    <Grid>
        <!-- DirectX 渲染宿主 -->
        <D3DImage x:Name="DxSurface" Visibility="Visible" />
        <!-- Skia 备用渲染 -->
        <skia:SKElement x:Name="SkiaSurface" Visibility="Collapsed" 
                        PaintSurface="OnSkiaPaintSurface" />
    </Grid>
</UserControl>
```

### 4.3 状态栏迁移

原状态栏是 `BaseRenderScene` 内的 `Panel` + `FlowLayoutPanel` + `Button`。改为独立 `StatusBarView`：

```xml
<UserControl x:Class="WC4MapEditor.Views.StatusBarView">
    <Grid Height="30" Background="#2D2D30">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>   <!-- 状态信息 -->
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>   <!-- 按钮组 -->
        </Grid.ColumnDefinitions>
        
        <StackPanel Orientation="Horizontal" Grid.Column="0" Margin="10,0">
            <TextBlock Text="{Binding ZoomText}" Foreground="White" Margin="0,0,15,0"/>
            <TextBlock Text="{Binding PositionText}" Foreground="White" Margin="0,0,30,0"/>
            <TextBlock Text="{Binding SceneStatusText}" Foreground="White"/>
        </StackPanel>
        
        <StackPanel Orientation="Horizontal" Grid.Column="2">
            <!-- 使用 ICommand 绑定 -->
            <Button Command="{Binding UndoCommand}" Content="撤销"/>
            <Button Command="{Binding RedoCommand}" Content="重做"/>
            <!-- ... -->
        </StackPanel>
    </Grid>
</UserControl>
```

---

## 五、DirectX 渲染实现要点

### 5.1 Vortice.Windows 初始化

Vortice.Windows 是 SharpDX 的现代替代品，由同一作者维护，支持 .NET Core / .NET 5+，API 与 SharpDX 高度兼容。

```csharp
// DXRenderEngine.cs 核心初始化
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

public class DXRenderEngine : IRenderEngine
{
    private ID3D11Device _device;
    private ID3D11DeviceContext _context;
    private IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView _renderTarget;
    private ID3D11Texture2D _backBuffer;
    
    public bool IsAvailable { get; private set; }

    public void Initialize(IntPtr hwnd, int width, int height)
    {
        // 创建 D3D11 设备
        var featureLevels = new[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 };
        D3D11Device.CreateDevice(
            null,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11.DeviceCreationFlags.BgraSupport,
            featureLevels,
            out _device,
            out _,
            out _context);

        // 创建 DXGI 工厂
        using var factory = _device.QueryInterface<IDXGIFactory4>();
        
        // 创建 SwapChain
        var swapChainDesc = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = DXGI.Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.Discard,
            AlphaMode = AlphaMode.Ignore,
            Flags = (int)SwapChainFlags.AllowModeSwitch
        };
        
        _swapChain = factory.CreateSwapChainForHwnd(_device, hwnd, ref swapChainDesc);
        factory.MakeWindowAssociation(hwnd, (int)WindowAssociationFlags.IgnoreAltEnter);
        
        Resize(width, height);
        IsAvailable = true;
    }
}
```

### 5.2 六边形网格渲染策略

原 Skia 实现使用逐格绘制。DX 实现采用：
1. **顶点缓冲区**：预生成六边形网格顶点（一次性）
2. **实例渲染**：每个格子一个实例，包含地形类型、装饰类型等属性
3. **像素着色器**：根据属性采样纹理图集，应用六边形裁剪
4. **分层渲染**：每个渲染层（地形/建筑/省份等）一个 DrawInstanced 调用

### 5.3 纹理管理

```csharp
using Vortice.Direct3D11;

public class TextureAtlas
{
    private ID3D11ShaderResourceView _atlasSRV;
    private Dictionary<string, (int x, int y, int w, int h)> _entries;
    
    public void LoadFromDirectory(ID3D11Device device, string path) { /* 加载所有地形纹理到图集 */ }
    public ID3D11ShaderResourceView GetEntry(string name) { /* 返回对应区域SRV */ }
}
```

---

## 六、数据模型翻译要点（VB → C#）

### 6.1 结构体保持布局

```csharp
// TerrainData.cs - 保持 16 字节紧凑布局
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainData
{
    public byte TileType1;           // 0x0
    public byte DecorationType1;     // 0x1
    public byte TextureOffsetX1;     // 0x2
    public byte TextureOffsetY1;     // 0x3
    public byte TileType2;           // 0x4
    public byte DecorationType2;     // 0x5
    public byte TextureOffsetX2;     // 0x6
    public byte TextureOffsetY2;     // 0x7
    public byte TileType3;           // 0x8
    public byte DecorationType3;     // 0x9
    public byte TextureOffsetX3;     // 0xA
    public byte TextureOffsetY3;     // 0xB
    public byte Reserved1;           // 0xC
    public byte Reserved2;           // 0xD
    public byte RiverValue;          // 0xE
    public byte Reserved3;           // 0xF

    public static TerrainData FromBytes(ReadOnlySpan<byte> data, int offset) { ... }
    public void ToBytes(Span<byte> data, int offset) { ... }
    public static TerrainData CreateDefault() { ... }
}
```

### 6.2 Module → static class

```csharp
// BTLConstants.cs - VB Module 翻译为 C# static class
public static class BTLConstants
{
    public const int HEADER_SIZE = 128;
    public const int TERRAIN_SIZE = 16;
    public const int PROVINCE_SIZE = 2;
    // ...
    
    public enum BuildingType { City = 0, CountyTown = 1, Port = 2, Pass = 3, Fortress = 4 }
    public enum TerrainType { Sea = 0, Plains = 1, Forest = 2, Mountain = 3, Hill = 4, Desert = 5, Swamp = 6, Snow = 7 }
    // ...
}
```

### 6.3 With 表达式翻译

```vb
' VB
Dim terrain As New TerrainData With {
    .TileType1 = 1,
    .DecorationType1 = 0
}
```

```csharp
// C#
var terrain = new TerrainData
{
    TileType1 = 1,
    DecorationType1 = 0
};
```

---

## 七、撤销/重做系统

原 `MapUndoRedoManager` 使用快照模式。C# 版本保持相同策略，但使用 `Span<T>` 优化内存拷贝：

```csharp
public class UndoRedoService
{
    private readonly Stack<Memento> _undoStack = new();
    private readonly Stack<Memento> _redoStack = new();
    private const int MaxHistory = 50;

    public void SaveState(MapData mapData, string actionName)
    {
        _undoStack.Push(new Memento(actionName, mapData));
        _redoStack.Clear();
        while (_undoStack.Count > MaxHistory)
            // 移除最旧
    }

    public bool Undo(MapData mapData) { ... }
    public bool Redo(MapData mapData) { ... }
}
```

---

## 八、实现顺序（建议）

### 阶段 0：解决方案拆分（预计 1-2 天）
1. 创建 WC4MapEditor.sln，拆分 Core / Rendering / Cli / GUI 四个项目
2. 现有 Models/Parsers/Commands 等目录迁入 Core（去除 UI 依赖）
3. CLI 骨架：System.CommandLine 入口 + `map load/save` 两个最小命令验证链路
4. GUI 项目引用 Core，确认 WPF 启动正常

### 阶段 1：核心库（预计 3-5 天）
1. 翻译 Models 命名空间（纯数据，无 UI 依赖）
2. 翻译 Parsers 命名空间（文件解析逻辑）
3. 实现 ConfigManager 单例（System.Text.Json）
4. 定义 Core.Commands 命令抽象（ICommand`1 + CommandContext）
5. CLI 接入：map/stage/conquest 的 load/save/list 命令

### 阶段 2：Skia 备用渲染（预计 5-7 天）
1. 实现 SkiaRenderEngine（直接翻译 MainRender 及各层渲染器）
2. 创建 RenderSurfaceView（SKElement 宿主）
3. 实现 Camera 视图控制（缩放/平移）
4. 实现 OffscreenRenderer（无窗口渲染，供 CLI export/screenshot）
5. 验证地图加载和渲染正确性（GUI 与 CLI 导出对比）

### 阶段 3：WPF UI 迁移（预计 5-7 天）
1. 实现轻量 MainWindow（启动器：入口导航 + 最近文件）+ MainViewModel
2. 实现 MapEditorView（替代 MapRenderScene）
3. 实现 StatusBarView
4. 实现 HexInfoWindow（WPF 窗口）
5. 迁移 Assist 命名空间中的设置对话框为 WPF UserControl

### 阶段 4：DirectX 主渲染（预计 7-10 天）
1. 实现 DXRenderEngine 基础初始化
2. 实现六边形顶点生成和实例渲染
3. 编写 HLSL 着色器（地形纹理采样 + 六边形裁剪）
4. 实现各渲染层的 DX 版本
5. 实现 DX/Skia 自动切换逻辑

### 阶段 5：编辑命令完善（预计 5-7 天）
1. 实现 TerrainModifierService（地形编辑）并封装为 Core.Commands
2. 实现 BuildingModifierService / ArmyModifierService 等并命令化
3. CLI 补齐 terrain/building/army/stage/conquest 全部子命令 + batch 脚本
4. 实现 UndoRedoService
5. GUI 按钮全部改走 Core.Commands（与 CLI 同源）

### 阶段 6：打磨和测试（预计 3-5 天）
1. 性能优化（DX 批处理、纹理缓存）
2. 内存优化验证
3. UI 动画和交互打磨
4. 大地图压力测试（GUI 与 CLI 双路径）
5. 打包发布配置（GUI 单 exe + CLI 单独发布 dotnet tool）

---

## 九、NuGet 包依赖

```xml
<!-- WC4MapEditor.Core -->
<ItemGroup>
  <PackageReference Include="System.Text.Json" Version="10.0.0" />
</ItemGroup>

<!-- WC4MapEditor.Rendering -->
<ItemGroup>
  <PackageReference Include="Vortice.Direct3D11" Version="3.5.2" />
  <PackageReference Include="Vortice.DXGI" Version="3.5.2" />
  <PackageReference Include="Vortice.Mathematics" Version="1.12.1" />
  <PackageReference Include="SkiaSharp" Version="3.119.2" />
</ItemGroup>

<!-- WC4MapEditor.Cli -->
<ItemGroup>
  <PackageReference Include="System.CommandLine" Version="2.0.0-beta5" />
  <ProjectReference Include="..\WC4MapEditor.Core\WC4MapEditor.Core.csproj" />
  <ProjectReference Include="..\WC4MapEditor.Rendering\WC4MapEditor.Rendering.csproj" />
</ItemGroup>

<!-- WC4MapEditor (GUI) -->
<ItemGroup>
  <PackageReference Include="SkiaSharp.Views.WPF" Version="3.116.1" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  <ProjectReference Include="..\WC4MapEditor.Core\WC4MapEditor.Core.csproj" />
  <ProjectReference Include="..\WC4MapEditor.Rendering\WC4MapEditor.Rendering.csproj" />
</ItemGroup>
```

---

## 十、风险点和注意事项

1. **Vortice.Windows 兼容性**：Vortice.Windows 是 SharpDX 的现代替代品（同一作者维护），API 与 SharpDX 高度兼容，支持 .NET Core / .NET 5+，持续活跃维护
2. **大文件解析**：WorldParser 的流式读取逻辑需完整保留，C# 中使用 `FileStream` + `Span<byte>` 实现
3. **结构体对齐**：`[StructLayout(Pack=1)]` 必须严格保持，否则 BTL 文件解析会错位
4. **WinForms 遗留依赖**：原项目中 `DebugConsoleWinForms`、`HexInfoWinForms` 等需完全重写为 WPF 版本
5. **Windows API 调用**：原 `BaseRenderScene` 中的 DWM 模糊效果、Layered Window 等需在 WPF 中使用 `WindowChrome` 或 `AcrylicBrush` 替代
6. **音乐播放**：音乐仅 GUI 层功能（MusicService 留在 GUI 项目），CLI 不涉及
7. **资源文件**：Texture/Music/Config 目录为解决方案级共享，Core 中通过相对路径/环境变量定位，CLI 需支持 `--resource-dir` 参数覆盖
8. **GUI/CLI 行为一致性**：所有编辑操作必须走 Core.Commands 单一实现，禁止 GUI 绕过命令层直接改数据，否则 CLI 与 GUI 行为会分叉
9. **离屏渲染**：CLI 的 export/screenshot 依赖 OffscreenRenderer（Skia 软件渲染即可，无需 DX 上下文）
