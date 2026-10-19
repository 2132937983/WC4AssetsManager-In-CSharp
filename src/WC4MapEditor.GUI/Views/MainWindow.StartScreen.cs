using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 主窗口里与开始界面 / 全局设置相关的部分：场景无关的控制台命令、背景音乐、配置热重载后的刷新、主窗口入场动画。
/// </summary>
public partial class MainWindow
{
    /// <summary>构造时调用一次：注册 music / system 控制台命令，订阅 setting.txt 热重载。</summary>
    private void InitializeStartScreenServices()
    {
        AppConsoleCommands.Register(CommandManager, DebugConsole);
        ConfigManager.ConfigReloaded += OnConfigReloaded;
        Closed += (_, _) => ConfigManager.ConfigReloaded -= OnConfigReloaded;
    }

    /// <summary>
    /// setting.txt 热重载后（设置窗口保存 / 刷新）：刷新窗口标题与主题色、按新配置同步背景音乐；
    /// 当前在开始界面就重建它，按钮文字、标题、词云、背景都换成新配置。
    /// </summary>
    private void OnConfigReloaded()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnConfigReloaded);
            return;
        }

        Title = ConfigManager.Instance.GetText("TitleBarText", Loc.T("Main.Title"));
        _mainGrid.Background = new SolidColorBrush(ColorFromInt(ConfigManager.Instance.GetThemeColor()));
        MusicPlayerService.Instance.ReloadConfig();
        if (_currentScene is BeginScene) ReturnToMainScene();
    }

    /// <summary>主窗口从屏幕底部滑入并淡入。</summary>
    private void PlayWindowEntrance(double targetTop)
    {
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Top = screenHeight;
        Opacity = 0;

        var topAnim = new DoubleAnimation(screenHeight, targetTop, TimeSpan.FromMilliseconds(500))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(TopProperty, topAnim);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
    }
}
