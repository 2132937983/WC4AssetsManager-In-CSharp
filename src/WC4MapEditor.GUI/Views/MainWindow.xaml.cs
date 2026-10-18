using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.GUI.Services;
// 与 WPF 的 System.Windows.Input.CommandManager 同名，使用别名消除歧义
using CoreCommandManager = WC4MapEditor.Core.Commands.CommandManager;
using WC4MapEditor.GUI.Views;
using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Views;

public partial class MainWindow : Window
{
    private const int WS_BORDER = 0x800000;
    private const int WS_CAPTION = 0xC00000;
    private const int GWL_STYLE = -16;
    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int BORDER_WIDTH = 8;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int WM_ACTIVATE = 0x0006;

    /// <summary>
    /// true = 这次最小化是用户主动发起的（点 ➖，或任务栏/Win+D 走的 WM_SYSCOMMAND）。
    /// <para>
    /// 用来区分"用户要最小化"和"关掉子窗口时被系统连累最小化"：前者放行，后者自动还原。
    /// </para>
    /// </summary>
    private bool _userMinimizeRequested;

    /// <summary>最近一次被激活的时刻（TickCount）。子窗口关闭会让主窗口被激活，紧接着的意外最小化多半与此有关。</summary>
    private int _lastActivatedTick = int.MinValue;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private HwndSource? _hwndSource;
    private Grid _mainGrid = null!;
    private UserControl? _currentScene;
    private bool _isClosing;

    /// <summary>
    /// 调试控制台窗口全局只留一个（原来是挂在场景基类上的静态字段）。
    /// <para>
    /// 创建一次、之后反复 Hide/Show：销毁一个 <c>Owner=主窗口</c> 的子窗口时 Windows 会把激活权
    /// 交还主窗口，程序刚被 Alt+Tab 切走过时这个交还动作偶尔会把整个程序带成最小化；
    /// 而且每次重建都得重画上千个日志 TextBlock。
    /// </para>
    /// <para>
    /// 放在主窗口持有，是因为 F3 要做成全局键（见 <see cref="ToggleDebugConsole"/>）。
    /// </para>
    /// </summary>
    private DebugConsoleWindow? _consoleWindow;

    /// <summary>
    /// 启动后直接进入的编辑场景（由 App 从命令行 <c>--scene &lt;name&gt;</c> 填入，名字与 AI 工具 open_scene 一致：
    /// layout / army / general / country …）。放在这里而不是 App 上，因为 GUI 程序集不能反向引用宿主 exe。
    /// </summary>
    public static string? StartupScene { get; set; }

    /// <summary>命令行 <c>--screenshot &lt;png&gt;</c>：场景就绪几秒后把整个界面渲染成 PNG 然后退出（自动化界面检查用）。</summary>
    public static string? StartupScreenshot { get; set; }

    /// <summary>命令行 <c>--open &lt;地图文件&gt;</c>：启动后直接加载该 .btl / .bin 进入对应渲染场景（走开始界面注册的 open 命令）。</summary>
    public static string? StartupOpenFile { get; set; }

    /// <summary>
    /// 鼠标输入管理器（由 DI 注入，供各渲染场景共享）
    /// </summary>
    public MouseManager MouseManager { get; }

    /// <summary>
    /// 调试控制台（由 DI 注入，全局唯一）
    /// </summary>
    public DebugConsole DebugConsole { get; }

    /// <summary>
    /// 键盘输入管理器（由 DI 注入，供主窗口与各渲染场景共享）
    /// </summary>
    public KeyboardManager KeyboardManager { get; }

    /// <summary>格子选择器（由 DI 注入，各渲染场景共享同一选择状态）</summary>
    public HexSelector HexSelector { get; }

    /// <summary>控制台命令管理器（由 DI 注入）</summary>
    public CoreCommandManager CommandManager { get; }

    /// <summary>编辑模式管理器（由 DI 注入）</summary>
    public EditModeManager EditModeManager { get; }

    public MainWindow(MouseManager mouseManager, DebugConsole debugConsole, KeyboardManager keyboardManager,
                      HexSelector hexSelector, CoreCommandManager commandManager, EditModeManager editModeManager)
    {
        MouseManager = mouseManager;
        DebugConsole = debugConsole;
        KeyboardManager = keyboardManager;
        HexSelector = hexSelector;
        CommandManager = commandManager;
        EditModeManager = editModeManager;
        InitializeComponent();
        SetupWindow();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        // 兜住"关子窗口时被系统连累最小化"（详见 MainWindow_StateChanged）
        StateChanged += MainWindow_StateChanged;

        // 截图能力注册（两件都放主窗口，而不是渲染场景里）：
        //  · 场景截图（截窗口界面）跟"当前哪个场景、有没有地图"无关 → 军团/国家/建筑/开始界面都能用；
        //  · AI 视觉统一丢给 EditModeManager 分发，由它按 request.SceneMode 决定走地图截图还是场景截图。
        EditModeManager.SetSceneScreenshotCallback(CaptureSceneScreenshot);
        WC4MapEditor.AI.Tools.AiBridge.ScreenshotCapturer = EditModeManager.CaptureScreenshot;
    }

    private void SetupWindow()
    {
        Title = ConfigManager.Instance.GetText("TitleBarText", Loc.T("Main.Title"));
        Width = 1250;
        Height = 700;
        MinWidth = 125;
        MinHeight = 70;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;

        _mainGrid = new Grid();
        _mainGrid.Margin = new Thickness(10);
        _mainGrid.Effect = new DropShadowEffect
        {
            BlurRadius = 15,
            ShadowDepth = 5,
            Opacity = 0.6,
            Color = Colors.Black
        };
        _mainGrid.Background = new SolidColorBrush(ColorFromInt(ConfigManager.Instance.GetThemeColor()));

        Content = _mainGrid;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        RenderSceneManager.Instance.SceneSwitchRequested += OnSceneSwitchRequested;

        double targetTop = Top;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Top = screenHeight;
        Opacity = 0;

        var topAnim = new DoubleAnimation(screenHeight, targetTop, TimeSpan.FromMilliseconds(500))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));

        BeginAnimation(TopProperty, topAnim);
        BeginAnimation(OpacityProperty, opacityAnim);

        RemoveWindowBorder();
        ShowOpeningAnimation();
    }

    private void ShowOpeningAnimation()
    {
        var animType = ConfigManager.Instance.GetOpeningAnimationType();
        switch (animType)
        {
            case ConfigManager.OpeningAnimationType.None:
                SwitchToBeginScene();
                break;
            case ConfigManager.OpeningAnimationType.Logo:
                ShowLogoAnimationAsync();
                break;
            case ConfigManager.OpeningAnimationType.Token:
                ShowTokenAnimationAsync();
                break;
        }
    }

    private async void ShowLogoAnimationAsync()
    {
        string logoPath = IOPath.Combine(ConfigManager.Instance.GetLogoPath(), "Logo.png");
        if (!System.IO.File.Exists(logoPath)) { SwitchToBeginScene(); return; }

        var logoWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Width = 256, Height = 256,
            Topmost = true,
            ShowInTaskbar = false
        };

        var image = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5) };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch { SwitchToBeginScene(); return; }

        var grid = new Grid();
        grid.Children.Add(image);
        logoWindow.Content = grid;

        var rotateTransform = new RotateTransform(0);
        image.RenderTransform = rotateTransform;

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double groundY = screenHeight - 40 - logoWindow.Height;
        double centerY = (screenHeight - logoWindow.Height) / 2;

        logoWindow.Left = (screenWidth - logoWindow.Width) / 2;
        logoWindow.Top = -logoWindow.Height;
        logoWindow.Show();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        logoWindow.BeginAnimation(OpacityProperty, fadeIn);

        double gravity = 0.98, bounceDamping = 0.75, springStiffness = 0.06, springDamping = 0.92;
        double positionY = -logoWindow.Height, velocityY = 25;
        bool isBouncing = true, hasBounced = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (s, e) =>
        {
            if (isBouncing)
            {
                velocityY += gravity;
                positionY += velocityY;
                if (positionY >= groundY && velocityY > 0)
                {
                    positionY = groundY;
                    if (!hasBounced)
                    {
                        velocityY = -velocityY * bounceDamping;
                        hasBounced = true;
                        var rotAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(2400))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
                    }
                }
                if (hasBounced && positionY < centerY && velocityY < 0) isBouncing = false;
            }
            else
            {
                double disp = centerY - positionY;
                velocityY += disp * springStiffness;
                velocityY *= springDamping;
                positionY += velocityY;
                if (Math.Abs(disp) < 0.5 && Math.Abs(velocityY) < 0.5)
                {
                    positionY = centerY;
                    timer.Stop();
                    var stay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    stay.Tick += (s2, e2) =>
                    {
                        stay.Stop();
                        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += (s3, e3) => logoWindow.Close();
                        logoWindow.BeginAnimation(OpacityProperty, fadeOut);
                    };
                    stay.Start();
                }
            }
            logoWindow.Top = positionY;
        };
        timer.Start();
    }

    private async void ShowTokenAnimationAsync()
    {
        string tokenText = ConfigManager.Instance.GetMainToken();
        var wcConfig = ConfigManager.Instance.GetWordCloudConfig();
        if (string.IsNullOrWhiteSpace(tokenText)) { SwitchToBeginScene(); return; }

        var tokenWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowState = WindowState.Maximized,
            Topmost = true,
            ShowInTaskbar = false
        };

        var canvas = new Canvas();
        double sw = SystemParameters.PrimaryScreenWidth;
        double sh = SystemParameters.PrimaryScreenHeight;
        canvas.Width = sw; canvas.Height = sh;

        var mainViewbox = new Viewbox { Stretch = Stretch.Uniform, Width = sw - 200, Height = sh * 0.3 };
        Canvas.SetLeft(mainViewbox, 100);
        Canvas.SetTop(mainViewbox, (sh - mainViewbox.Height) / 2);

        var mainTb = new TextBlock
        {
            Text = tokenText, FontSize = 100, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 }
        };
        mainViewbox.Child = mainTb;
        canvas.Children.Add(mainViewbox);
        tokenWindow.Content = canvas;
        tokenWindow.Show();

        var mainFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        var mainFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1))
        { BeginTime = TimeSpan.FromSeconds(1 + wcConfig.AnimationKeep) };
        mainViewbox.BeginAnimation(UIElement.OpacityProperty, mainFadeIn);
        mainViewbox.BeginAnimation(UIElement.OpacityProperty, mainFadeOut);

        var validTokens = wcConfig.Tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        var random = new Random();
        double interval = wcConfig.AnimationKeep / Math.Max(validTokens.Length, 1);
        if (interval < 0.1) interval = 0.1;

        int idx = 0;
        var tokenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        tokenTimer.Tick += (s, e) =>
        {
            if (idx >= validTokens.Length) { tokenTimer.Stop(); return; }
            var tb = new TextBlock
            {
                Text = validTokens[idx], FontSize = 18, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Opacity = 0,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Arrange(new Rect(tb.DesiredSize));
            double margin = 50;
            Canvas.SetLeft(tb, random.NextDouble() * (sw - tb.ActualWidth - margin) + margin);
            Canvas.SetTop(tb, random.NextDouble() * (sh - tb.ActualHeight - margin) + margin);
            Panel.SetZIndex(tb, -1);
            canvas.Children.Add(tb);
            var fi = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5))
            { BeginTime = TimeSpan.FromSeconds(1.5), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            fo.Completed += (s2, e2) => canvas.Children.Remove(tb);
            tb.BeginAnimation(UIElement.OpacityProperty, fi);
            tb.BeginAnimation(UIElement.OpacityProperty, fo);
            idx++;
        };
        tokenTimer.Start();

        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1 + wcConfig.AnimationKeep + 1) };
        closeTimer.Tick += (s, e) => { closeTimer.Stop(); tokenWindow.Close(); };
        closeTimer.Start();
    }

    private void SwitchToBeginScene()
    {
        var beginScene = new BeginScene(this);
        SetCurrentScene(beginScene);

        // 命令行指定了启动场景：开始界面注册完 AI 桥之后，借同一条通道切过去（只生效一次）
        var startup = StartupScene;
        if (!string.IsNullOrWhiteSpace(startup))
        {
            StartupScene = null;
            Dispatcher.BeginInvoke(() => WC4MapEditor.AI.Tools.AiBridge.SceneOpener?.Invoke(startup), DispatcherPriority.ApplicationIdle);
        }

        var openFile = StartupOpenFile;
        if (!string.IsNullOrWhiteSpace(openFile))
        {
            StartupOpenFile = null;
            Dispatcher.BeginInvoke(() => WC4MapEditor.AI.Tools.AiBridge.CommandRunner?.Invoke("open " + openFile), DispatcherPriority.ApplicationIdle);
        }

        var shotPath = StartupScreenshot;
        if (!string.IsNullOrWhiteSpace(shotPath))
        {
            StartupScreenshot = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(string.IsNullOrWhiteSpace(openFile) ? 4 : 10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var result = CaptureSceneScreenshot(new WC4MapEditor.Core.Services.MapScreenshotRequest
                {
                    SceneMode = true,
                    OutputPath = shotPath,
                    MaxPixels = 2600
                });
                Console.WriteLine(result.Ok ? $"[screenshot] {result.Path}" : $"[screenshot] FAILED: {result.Message}");
                Application.Current.Shutdown();
            };
            timer.Start();
        }
    }

    private void RemoveWindowBorder()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_BORDER;
        style &= ~WS_CAPTION;
        SetWindowLong(hwnd, GWL_STYLE, style);
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            var mp = new Point(lParam.ToInt32() & 0xFFFF, (lParam.ToInt32() >> 16) & 0xFFFF);
            var wp = PointFromScreen(mp);
            bool l = wp.X < BORDER_WIDTH, r = wp.X > ActualWidth - BORDER_WIDTH;
            bool t = wp.Y < BORDER_WIDTH, b = wp.Y > ActualHeight - BORDER_WIDTH;
            if (t && l) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (t && r) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (b && l) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (b && r) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (l) { handled = true; return new IntPtr(HTLEFT); }
            if (r) { handled = true; return new IntPtr(HTRIGHT); }
            if (t) { handled = true; return new IntPtr(HTTOP); }
            if (b) { handled = true; return new IntPtr(HTBOTTOM); }
        }

        // 用户主动最小化：任务栏按钮、Win+D、系统菜单都会走 SC_MINIMIZE，放行。
        // 我们自己工具栏的 ➖ 走的是 MinimizeWindow()，也会置同样的标记。
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
        {
            _userMinimizeRequested = true;
        }
        else if (msg == WM_ACTIVATE)
        {
            _lastActivatedTick = Environment.TickCount;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 兜住"关子窗口时被系统连累最小化"：
    /// 只有用户主动最小化（<see cref="_userMinimizeRequested"/>）才放行；
    /// 否则如果这次最小化紧跟在一次激活之后（子窗口关闭会把激活权交还主窗口），
    /// 就判定是意外，自动还原。
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            _userMinimizeRequested = false;
            return;
        }

        if (_userMinimizeRequested)
        {
            _userMinimizeRequested = false;
            return;
        }

        int sinceActivate = unchecked(Environment.TickCount - _lastActivatedTick);
        if (sinceActivate < 0 || sinceActivate > 1500) return;   // 不是刚被激活，按用户操作处理

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        }), DispatcherPriority.Background);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) { base.OnClosing(e); return; }
        e.Cancel = true;
        _isClosing = true;
        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        var st = new ScaleTransform(1, 1);
        RenderTransform = st;
        var ea = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var sx = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        var sy = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        var oa = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        oa.Completed += (s, ev) => { RenderTransform = null; Application.Current.Shutdown(); };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        BeginAnimation(OpacityProperty, oa);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_currentScene is IDisposable d) d.Dispose();
        _currentScene = null;
    }

    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // 对话框覆盖层打开时按模态处理：窗口级快捷键让位给对话框自身的输入，
        // 这样 Tab 才能在输入框之间切换焦点，而不是触发“查看格子信息”。
        if (IsDialogOverlayOpen()) return;

        if (e.Key == Key.F3)
        {
            // 长按不重复开关（否则按住 F3 会疯狂闪窗）
            if (e.IsRepeat) { e.Handled = true; return; }

            // 先让场景自己接：注册了 F3 绑定的场景（渲染场景基类）能顺带把"调试控制台"
            // 列进模式帮助文本里。没人接住就落到全局实现 —— 军团/国家/建筑这些场景
            // 本来没注册 F3，以前按了没反应，现在一样能开。
            if (!KeyboardManager.ProcessKeyDown((int)Key.F3)) ToggleDebugConsole();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            KeyboardManager.ProcessKeyDown((int)Key.Tab);
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsDialogOverlayOpen()) return;

        if (e.Key == Key.F3)
        {
            KeyboardManager.ProcessKeyUp((int)Key.F3);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            KeyboardManager.ProcessKeyUp((int)Key.Tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 当前是否有叠加在主窗口内的对话框覆盖层。
    /// <para>
    /// DoubleInputDialog / SingleInputDialog / ObjectPropertiesDialog / NotificationDialog /
    /// ConfirmDialog 都是把 Border 挂进主窗口根面板的“伪对话框”，不是独立 Window。
    /// 因此它们内部控件的 Tab 焦点切换会被窗口级 PreviewKeyDown 抢先拦截，
    /// 需要在这里临时放行。各对话框统一以 ZIndex = 9999 挂载，以此作为判定依据。
    /// </para>
    /// </summary>
    private bool IsDialogOverlayOpen()
    {
        Panel? rootPanel = Content as Panel;
        if (rootPanel == null && Content is FrameworkElement contentElement)
            rootPanel = contentElement.Parent as Panel;

        if (rootPanel == null) return false;

        foreach (UIElement child in rootPanel.Children)
        {
            if (Panel.GetZIndex(child) >= 9999) return true;
        }
        return false;
    }

    private void OnSceneSwitchRequested(object? sender, SceneSwitchRequestEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var newScene = CreateRenderScene(e.TargetSceneType, e.MapFilePath);
            if (newScene != null)
            {
                newScene.AssignSceneId(e.TargetSceneId);
                RenderSceneManager.Instance.ActivateScene(e.TargetSceneId);
                SetCurrentScene(newScene);
            }
        });
    }

    private RenderSceneBase? CreateRenderScene(RenderSceneType sceneType, string mapFilePath)
    {
        return sceneType switch
        {
            RenderSceneType.Test => new MapRenderScene(this, mapFilePath),
            RenderSceneType.Stage => new StageRenderScene(this, mapFilePath),
            RenderSceneType.Conquest => new ConquestRenderScene(this, mapFilePath),
            RenderSceneType.Campaign => new StageRenderScene(this, mapFilePath),
            _ => null
        };
    }

    public void SetCurrentScene(UserControl scene)
    {
        if (_currentScene != null)
        {
            // 如果是渲染场景（战役/征服等），显示加载画面
            if (scene is RenderSceneBase)
            {
                ShowLoadingAndSwitchScene(scene);
            }
            else
            {
                FadeOutScene(() =>
                {
                    _mainGrid.Children.Remove(_currentScene);
                    if (_currentScene is IDisposable d) d.Dispose();
                    _currentScene = scene;
                    _mainGrid.Children.Add(scene);
                    FadeInScene(scene);
                });
            }
        }
        else
        {
            _currentScene = scene;
            _mainGrid.Children.Add(scene);
            FadeInScene(scene);
        }
    }

    private void ShowLoadingAndSwitchScene(UserControl newScene)
    {
        // 显示加载图像
        string loadingImagePath = ConfigManager.Instance.GetLoadingBackgroundImagePath();
        Image? loadingImage = null;

        if (!string.IsNullOrEmpty(loadingImagePath) && System.IO.File.Exists(loadingImagePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(loadingImagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                loadingImage = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0
                };
                _mainGrid.Children.Add(loadingImage);
                loadingImage.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            catch { }
        }

        // 淡出当前场景
        if (_currentScene != null)
        {
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fo.Completed += (s, e) =>
            {
                _mainGrid.Children.Remove(_currentScene);
                if (_currentScene is IDisposable d) d.Dispose();

                // 切换到新场景（先隐藏）
                newScene.Opacity = 0;
                _currentScene = newScene;
                _mainGrid.Children.Add(newScene);

                // 给新场景一点加载时间，然后淡出加载画面，显示新场景
                var loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                loadTimer.Tick += (s2, e2) =>
                {
                    loadTimer.Stop();

                    // 淡入新场景
                    newScene.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

                    // 淡出并移除加载图像
                    if (loadingImage != null)
                    {
                        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += (s3, e3) =>
                        {
                            if (_mainGrid.Children.Contains(loadingImage))
                                _mainGrid.Children.Remove(loadingImage);
                        };
                        loadingImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                };
                loadTimer.Start();
            };
            _currentScene.BeginAnimation(UIElement.OpacityProperty, fo);
        }
        else
        {
            newScene.Opacity = 0;
            _currentScene = newScene;
            _mainGrid.Children.Add(newScene);
            newScene.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

            if (loadingImage != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (s3, e3) =>
                {
                    if (_mainGrid.Children.Contains(loadingImage))
                        _mainGrid.Children.Remove(loadingImage);
                };
                loadingImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
    }

    private void FadeInScene(UserControl scene)
    {
        scene.Opacity = 0;
        scene.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void FadeOutScene(Action onComplete)
    {
        if (_currentScene == null) { onComplete(); return; }
        var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fo.Completed += (s, e) => onComplete();
        _currentScene.BeginAnimation(UIElement.OpacityProperty, fo);
    }

    #region 相机控制（转发到当前渲染场景）

    /// <summary>
    /// 把当前渲染场景的相机中心移动到指定格子（行列坐标）。
    /// 当前不是渲染场景、或坐标越界时返回 false。
    /// </summary>
    public bool MoveCameraToHex(int col, int row)
        => _currentScene is RenderSceneBase scene && scene.MoveCameraToHex(col, row);

    /// <summary>
    /// 把当前渲染场景的相机中心移动到指定格子（格子序号 = row * MapWidth + col）。
    /// 当前不是渲染场景、或序号越界时返回 false。
    /// </summary>
    public bool MoveCameraToHexIndex(int hexIndex)
        => _currentScene is RenderSceneBase scene && scene.MoveCameraToHexIndex(hexIndex);

    #endregion

    public void CloseApplication() => Close();
    public void MinimizeWindow()
    {
        _userMinimizeRequested = true;   // 用户主动最小化：放行，不要被自动还原逻辑拦下
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 开 / 关调试控制台（F3）。
    /// <para>
    /// 做成主窗口级而不是场景级：任何场景都能打开 —— 军团、国家、建筑这些编辑场景原先压根没注册
    /// F3 绑定，按了没反应。场景基类里那个 F3 绑定保留并转发到这里，只为让模式帮助文本还能列出它。
    /// </para>
    /// </summary>
    public void ToggleDebugConsole()
    {
        if (DebugConsole.IsVisible)
        {
            DebugConsole.HideConsole();
            // 收起控制台后，把可能被"连累"最小化的主窗口还原回来
            RestoreAfterChildWindow();
            return;
        }

        if (_consoleWindow == null || !_consoleWindow.IsLoaded)
            _consoleWindow = new DebugConsoleWindow(DebugConsole, this);

        // 每次都重新绑定一次（幂等）：宿主窗口万一被销毁重建，回调要跟着指向新窗口；
        // 也顺手自愈"回调被别处清空"导致关不掉窗口的情况。
        // 宿主关闭动作用 Hide 而不是 Close —— 窗口留着复用，不再销毁。
        DebugConsole.SetHostWindow(_consoleWindow.Hide);

        DebugConsole.ShowConsole();
        _consoleWindow.PrepareForShow(this);
        _consoleWindow.Show();
        _consoleWindow.Activate();
    }

    /// <summary>
    /// 整个场景（窗口界面）截图：把主窗口的视觉树渲染成 PNG。
    /// <para>
    /// 放在主窗口而不是渲染场景里，是因为它跟"当前是哪个场景、有没有加载地图"无关 ——
    /// 军团 / 国家 / 建筑等编辑场景、甚至开始界面都能截。通用 CLI 命令 <c>screenshotscene</c>
    /// 就是靠它做到"到处都能跑"。
    /// </para>
    /// <para>
    /// 取舍：走 <see cref="RenderTargetBitmap"/> 渲染视觉树，而不是抓屏（BitBlt）。
    /// 抓屏要求窗口在最前面且没被别的窗口压住，而脚本 / AI 静默调用时恰恰常常不满足；
    /// 渲染视觉树则与遮挡无关。代价两条：① 窗口最小化时拿不到；
    /// ② 不属于主窗口视觉树的东西（独立弹出的 AI 对话窗、右键菜单等）不会出现在图里。
    /// </para>
    /// </summary>
    private WC4MapEditor.Core.Services.MapScreenshotResult CaptureSceneScreenshot(
        WC4MapEditor.Core.Services.MapScreenshotRequest request)
    {
        // AI / CLI 可能从后台线程调进来，截图必须在 UI 线程做
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => CaptureSceneScreenshot(request));

        if (ActualWidth < 1 || ActualHeight < 1)
            return WC4MapEditor.Core.Services.MapScreenshotResult.Fail(Loc.T("Main.SceneScreenshotNoLayout"));

        if (WindowState == WindowState.Minimized)
            return WC4MapEditor.Core.Services.MapScreenshotResult.Fail(Loc.T("Main.SceneScreenshotMinimized"));

        try
        {
            // 输出路径：给了就用（相对路径按 exe 目录解析），没给就放到 screenshots/ 自动命名
            string path = request.OutputPath ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "screenshots");
                System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir,
                    WC4MapEditor.Rendering.Helpers.ScreenshotHelper.GenerateDefaultFileName());
            }
            else if (!System.IO.Path.IsPathRooted(path))
            {
                path = System.IO.Path.Combine(AppContext.BaseDirectory, path);
            }

            // WPF 的 ActualWidth 是 DIP，要按屏幕缩放折成物理像素，图才不糊
            var dpi = VisualTreeHelper.GetDpi(this);
            double pxW = ActualWidth * dpi.DpiScaleX;
            double pxH = ActualHeight * dpi.DpiScaleY;

            // 长边上限：整窗截图动辄 2000+ 像素，喂给模型又贵又容易被拒，按比例缩
            int maxPixels = request.MaxPixels > 0 ? request.MaxPixels : 1400;
            double shrink = Math.Max(1.0, Math.Max(pxW, pxH) / maxPixels);

            // DPI 参数同步除以 shrink，等于让 WPF 在栅格化时就缩放（比事后缩图清晰）
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(pxW / shrink)),
                Math.Max(1, (int)Math.Round(pxH / shrink)),
                96.0 * dpi.DpiScaleX / shrink,
                96.0 * dpi.DpiScaleY / shrink,
                PixelFormats.Pbgra32);

            bitmap.Render(this);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                encoder.Save(fs);

            string desc = Loc.F("Main.SceneScreenshotDesc", Title, ActualWidth.ToString("0"), ActualHeight.ToString("0"));
            if (shrink > 1.0) desc += Loc.F("Main.SceneScreenshotShrunk", maxPixels);

            DebugConsole.WriteLine(Loc.F("Main.ScreenshotLog", desc, path));
            return new WC4MapEditor.Core.Services.MapScreenshotResult(
                true, path, bitmap.PixelWidth, bitmap.PixelHeight, desc);
        }
        catch (Exception ex)
        {
            return WC4MapEditor.Core.Services.MapScreenshotResult.Fail(
                Loc.F("Main.SceneScreenshotFailed", ex.GetType().Name, ex.Message));
        }
    }

    /// <summary>
    /// 子窗口（控制台 / 各种弹窗）关闭或收起之后调用：
    /// 把"被连累最小化"的主窗口还原回来。
    /// <para>
    /// 为什么要这个：关掉一个 <c>Owner=主窗口</c> 的子窗口时，Windows 会把激活权交还主窗口；
    /// 如果这时程序刚被 Alt+Tab 切走过，这个交还动作偶尔会把整个程序带成最小化。
    /// 这里只在"我们自己刚关完子窗口"的瞬间生效，不会干扰用户主动点 ➖ / 任务栏 / Win+D 最小化
    /// （那几种情况窗口不会是"刚关完子窗口"的状态）。
    /// </para>
    /// </summary>
    public void RestoreAfterChildWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Activate();
        }

        // 个别情况下最小化发生在关闭动作之后的下一轮消息里，所以异步再兜一次：
        // 只有此刻真的处于最小化才还原，用户正常切走（没最小化）时什么都不做。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        }), DispatcherPriority.Background);

        // 再晚一点的第三道兜底，覆盖"关闭动画结束后才最小化"的情形
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        };
        timer.Start();
    }
    public void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    public void ReturnToMainScene()
    {
        // 语言可能刚切换过：回到开始界面时顺带刷新窗口标题
        Title = ConfigManager.Instance.GetText("TitleBarText", Loc.T("Main.Title"));
        SwitchToBeginScene();
    }

    /// <summary>
    /// 从 TacticalMapEditScene 返回：有来源目录（= 从资源管理器打开）就回资源管理器，否则回主场景
    /// </summary>
    public void ReturnFromTacticalEditor(string? sourceDirectory)
    {
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            // 来源是资源管理器：新建 AssetBrowserScene（自动默认定位到上次/对应分类即可，因为缓存已是全局的 AssetManager.Default）
            var scene = new AssetBrowserScene(this);
            SetCurrentScene(scene);
        }
        else
        {
            ReturnToMainScene();
        }
    }

    private static Color ColorFromInt(int val) =>
        Color.FromRgb(
            (byte)((val >> 16) & 0xFF),
            (byte)((val >> 8) & 0xFF),
            (byte)(val & 0xFF));
}