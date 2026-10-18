using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.ErrorHandling;
// 与 WPF 的 System.Windows.Input.CommandManager 同名，使用别名消除歧义
using CoreCommandManager = WC4MapEditor.Core.Commands.CommandManager;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Helpers;
using WC4MapEditor.Rendering.Skia;
// 界面代码全部位于 WC4MapEditor.GUI 程序集，根项目只做组合根
using WC4MapEditor.GUI.ViewModels;
using WC4MapEditor.GUI.Views;

namespace WC4MapEditor;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;


    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        AllocConsole();
        // 命令行 --scene <name>：启动后直接进入某个编辑场景（调试时省去每次点导航）
        // --screenshot <png路径>：进入场景后把整个界面渲染成图并退出（无需窗口在前台，用于自动化检查界面）
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (string.Equals(e.Args[i], "--scene", StringComparison.OrdinalIgnoreCase)) WC4MapEditor.GUI.Views.MainWindow.StartupScene = e.Args[i + 1];
            else if (string.Equals(e.Args[i], "--screenshot", StringComparison.OrdinalIgnoreCase)) WC4MapEditor.GUI.Views.MainWindow.StartupScreenshot = e.Args[i + 1];
            else if (string.Equals(e.Args[i], "--open", StringComparison.OrdinalIgnoreCase)) WC4MapEditor.GUI.Views.MainWindow.StartupOpenFile = e.Args[i + 1];
        }
        Console.WriteLine("=== WC4MapEditor GUI 启动 ===");

        // 把诊断日志接到控制台。
        // 注意 .NET Core 的 Debug.WriteLine 既没有 Listeners 属性、也不会转发到 Trace.Listeners，
        // 因此关键日志（如"保存失败: ..."）统一用 Trace.WriteLine 输出，才能在这里被看到。
        Trace.Listeners.Add(new ConsoleTraceListener());
        Trace.AutoFlush = true;

        // 注册全局异常处理
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddSingleton<ConfigManager>(_ => ConfigManager.Instance);

        // 迁移期：以既有单例作为容器实例来源，确保与尚未迁移的调用点持有同一对象
        services.AddSingleton<MouseManager>(_ => MouseManager.Instance);
        services.AddSingleton<DebugConsole>(_ => DebugConsole.Instance);
        services.AddSingleton<AssetCache>(_ => AssetCache.Instance);
        services.AddSingleton<TextureDiskCache>(_ => TextureDiskCache.Instance);
        services.AddSingleton<CoastHelper>(_ => CoastHelper.Instance);
        services.AddSingleton<CoastMaskProcessor>(_ => CoastMaskProcessor.Instance);

        // 批次 2：仅注册进容器，不开放实例化 —— 原因见 docs/decoupling-plan.md 的 H-2.5
        services.AddSingleton<HexInfoService>(_ => HexInfoService.Instance);
        services.AddSingleton<GeneralSettingParser>(_ => GeneralSettingParser.Instance);
        services.AddSingleton<CountrySettingParser>(_ => CountrySettingParser.Instance);
        services.AddSingleton<ICommandHost>(_ => CliCommandHost.Instance);

        // 批次 3：仅在 View 层改用注入（KeyboardManager）；Core 内部仍走单例（Core 不依赖 DI 容器）
        services.AddSingleton<KeyboardManager>(_ => KeyboardManager.Instance);
        services.AddSingleton<ErrorCollector>(_ => ErrorCollector.Instance);
        services.AddSingleton<EditModeManager>(_ => EditModeManager.Instance);

        // 批次 4：选择 / 命令 / 战术图缓存 —— 仅在 View 层改用注入，其余（Core、Rendering 内部）保持单例
        services.AddSingleton<HexSelector>(_ => HexSelector.Instance);
        services.AddSingleton<CoreCommandManager>(_ => CoreCommandManager.Instance);
        services.AddSingleton<TacticalMapImageCache>(_ => TacticalMapImageCache.Instance);

        services.AddTransient<MainWindow>();
        services.AddTransient<BeginScene>();
        services.AddTransient<MainViewModel>();

        Services = services.BuildServiceProvider();

        // 让容器里的 ICommandHost 注册真正生效：
        // DebugConsole 默认会自行回落到内置实现，这里显式注入一次，
        // 以后要换命令宿主只改上面的注册即可（GUI 侧只依赖 ICommandHost）。
        Services.GetRequiredService<DebugConsole>().CommandHost = Services.GetRequiredService<ICommandHost>();

        ConfigManager.Instance.Initialize();
        // 界面文本本地化（Resource/Lang/*.json），语言偏好读 setting.txt 的 Language 键
        Loc.Initialize();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = Loc.F("Main.FatalErrorMessage", ex?.ToString() ?? Loc.T("Main.UnknownException"));
        Console.WriteLine(message);
        LogCrash(message);
        MessageBox.Show(message, Loc.T("Main.FatalError"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var message = Loc.F("Main.UiThreadError", e.Exception);
        Console.WriteLine(message);
        LogCrash(message);
        e.Handled = true;
        MessageBox.Show(message, Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var message = $"[任务异常] {e.Exception}";
        Console.WriteLine(message);
        LogCrash(message);
        e.SetObserved();
    }

    private static void LogCrash(string message)
    {
        try
        {
            var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            var logPath = System.IO.Path.Combine(logDir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{message}");
            Console.WriteLine($"崩溃日志已保存到: {logPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存崩溃日志失败: {ex.Message}");
        }
    }
}