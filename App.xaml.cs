using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using WC4MapEditor.ViewModels;
using WC4MapEditor.Views;

namespace WC4MapEditor;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        AllocConsole();
        Console.WriteLine("=== WC4MapEditor GUI 启动 ===");

        // 将 Debug.WriteLine 重定向到控制台
        Trace.Listeners.Add(new ConsoleTraceListener());
        Trace.AutoFlush = true;

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
}