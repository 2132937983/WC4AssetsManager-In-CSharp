using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Commands;
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
using WC4MapEditor.ViewModels;
using WC4MapEditor.Views;

namespace WC4MapEditor;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        AllocConsole();
        Console.WriteLine("=== WC4MapEditor GUI 启动 ===");
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

        ConfigManager.Instance.Initialize();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = $"[致命错误] 进程即将终止: {ex?.ToString() ?? "未知异常"}";
        Console.WriteLine(message);
        LogCrash(message);
        MessageBox.Show(message, "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var message = $"[UI线程错误] {e.Exception}";
        Console.WriteLine(message);
        LogCrash(message);
        e.Handled = true;
        MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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