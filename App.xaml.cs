using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
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

        // 注册全局异常处理
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddSingleton<ConfigManager>(_ => ConfigManager.Instance);

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