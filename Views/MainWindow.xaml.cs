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
using WC4MapEditor.Core.Input;
using WC4MapEditor.Services;
using WC4MapEditor.Views;

namespace WC4MapEditor.Views;

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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private HwndSource? _hwndSource;
    private Grid _mainGrid = null!;
    private UserControl? _currentScene;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void SetupWindow()
    {
        Title = ConfigManager.Instance.GetText("TitleBarText", "WC4 Map Editor");
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
        return IntPtr.Zero;
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
        if (e.Key == Key.F3)
        {
            KeyboardManager.Instance.ProcessKeyDown((int)Key.F3);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            KeyboardManager.Instance.ProcessKeyDown((int)Key.Tab);
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            KeyboardManager.Instance.ProcessKeyUp((int)Key.F3);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            KeyboardManager.Instance.ProcessKeyUp((int)Key.Tab);
            e.Handled = true;
        }
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

    public void CloseApplication() => Close();
    public void MinimizeWindow() => WindowState = WindowState.Minimized;
    public void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    public void ReturnToMainScene() => SwitchToBeginScene();

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