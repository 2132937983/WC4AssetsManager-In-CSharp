using System.Diagnostics;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 打开地图（切到渲染场景）时的加载画面，对齐 VB 版 LoadingScene：背景图（缺失时用渐变）、水印、标题、进度条与状态文字。
/// <para>
/// 渲染场景在 Loaded 里同步完成读图、建渲染器等重活，期间界面线程是卡住的，所以进度按看得见的阶段推进：
/// 准备（10%）→ 加载地图文件（30%，挂上场景、开始读图）→ 准备渲染资源（85%，场景 Loaded 完成）→ 完成（100%，第一帧画出后揭开）。
/// 加载画面在场景真正就绪后才淡出（不再是固定 300 ms）；场景中途退回开始界面或加载抛异常时撤掉加载画面，不会卡住。
/// </para>
/// <para>与开始界面一样保留自己的样式（白字阴影、蓝色进度条），不套编辑主题。</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>加载画面的层级：盖住场景，但在对话框覆盖层（9999）之下</summary>
    private const int LoadingOverlayZIndex = 1000;

    /// <summary>一次进行中的加载过渡</summary>
    private sealed class LoadingTransition
    {
        public LoadingTransition(UserControl scene, Grid overlay, ProgressBar progress, TextBlock status)
        {
            Scene = scene;
            Overlay = overlay;
            Progress = progress;
            Status = status;
        }

        public UserControl Scene { get; }
        public Grid Overlay { get; }
        public ProgressBar Progress { get; }
        public TextBlock Status { get; }
        public bool Finished { get; set; }
    }

    private LoadingTransition? _loading;

    private void ShowLoadingAndSwitchScene(UserControl newScene)
    {
        var overlay = BuildLoadingOverlay(out var progress, out var status);
        var loading = new LoadingTransition(newScene, overlay, progress, status);
        _loading = loading;
        Dispatcher.UnhandledException += OnLoadingUnhandledException;

        Panel.SetZIndex(overlay, LoadingOverlayZIndex);
        overlay.Opacity = 0;
        _mainGrid.Children.Add(overlay);
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        SetLoadingStage(loading, 10, Loc.T("Main.Loading.Preparing"));

        var outgoing = _currentScene;
        if (outgoing == null)
        {
            AttachLoadingScene(loading, null);
            return;
        }
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => AttachLoadingScene(loading, outgoing);
        outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>旧场景淡出后：移除旧场景，挂上新场景（先隐藏）开始加载。</summary>
    private void AttachLoadingScene(LoadingTransition loading, UserControl? outgoing)
    {
        if (_loading != loading) return;   // 淡出期间又切换了场景

        if (outgoing != null)
        {
            _mainGrid.Children.Remove(outgoing);
            if (outgoing is IDisposable d) d.Dispose();
        }

        var scene = loading.Scene;
        scene.Opacity = 0;
        _currentScene = scene;
        SetLoadingStage(loading, 30, Loc.T("Main.Loading.LoadingMap"));

        // 场景的 Loaded（读地图、建渲染器）会同步占住界面线程：先让状态文字画出来再挂场景。
        // 本处理器订阅在场景构造函数里的 OnLoaded 之后，所以会在场景加载完成后才执行。
        Dispatcher.BeginInvoke(() =>
        {
            if (_loading != loading) return;
            scene.Loaded += OnLoadingSceneLoaded;
            _mainGrid.Children.Add(scene);
        }, DispatcherPriority.Background);
    }

    private void OnLoadingSceneLoaded(object sender, RoutedEventArgs e)
    {
        var scene = (UserControl)sender;
        scene.Loaded -= OnLoadingSceneLoaded;
        var loading = _loading;
        if (loading == null || loading.Scene != scene) return;   // 场景加载中自己退回了开始界面

        SetLoadingStage(loading, 85, Loc.T("Main.Loading.PreparingRender"));
        // ContextIdle 晚于布局与渲染：此时场景第一帧已经画出，再揭开加载画面
        Dispatcher.BeginInvoke(() =>
        {
            if (_loading != loading) return;
            SetLoadingStage(loading, 100, Loc.T("Main.Loading.Finishing"));
            FinishLoadingTransition(loading, revealScene: true);
        }, DispatcherPriority.ContextIdle);
    }

    /// <summary>加载中又切换了场景（例如没选文件、场景自己退回开始界面）：撤掉加载画面。</summary>
    private void CancelLoadingTransition()
    {
        if (_loading is not { } loading) return;
        loading.Scene.Loaded -= OnLoadingSceneLoaded;
        FinishLoadingTransition(loading, revealScene: false);
    }

    private void FinishLoadingTransition(LoadingTransition loading, bool revealScene)
    {
        if (loading.Finished) return;
        loading.Finished = true;
        if (_loading == loading) _loading = null;
        Dispatcher.UnhandledException -= OnLoadingUnhandledException;

        if (revealScene)
            loading.Scene.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        var overlay = loading.Overlay;
        var fadeOut = new DoubleAnimation(overlay.Opacity, 0, TimeSpan.FromMilliseconds(300));
        fadeOut.Completed += (_, _) => _mainGrid.Children.Remove(overlay);
        overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>
    /// 场景加载时抛了异常（例如地图文件解析失败）：场景已不可用，撤掉加载画面回到开始界面。
    /// 异常本身照常交给 App 的全局处理弹窗报告，这里不吞掉。
    /// </summary>
    private void OnLoadingUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var loading = _loading;
        if (loading == null) return;
        Debug.WriteLine($"[MainWindow] 场景加载异常，返回开始界面: {e.Exception.Message}");
        Dispatcher.BeginInvoke(() =>
        {
            if (_loading == loading) ReturnToMainScene();
        });
    }

    private static void SetLoadingStage(LoadingTransition loading, double percent, string status)
    {
        loading.Status.Text = status;
        loading.Progress.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(percent, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>加载画面：背景图铺满（没有图片时用深色渐变），左下大号水印，底部渐暗条上是标题、状态与进度条。</summary>
    private static Grid BuildLoadingOverlay(out ProgressBar progress, out TextBlock status)
    {
        var overlay = new Grid { Background = Brushes.Black };

        if (LoadLoadingBackground() is { } image)
        {
            overlay.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
        }
        else
        {
            overlay.Background = new LinearGradientBrush(Color.FromRgb(20, 22, 28), Color.FromRgb(35, 38, 48), new Point(0, 0), new Point(1, 1));
        }

        overlay.Children.Add(new TextBlock
        {
            Text = ConfigManager.Instance.GetText("WindowTitleText", "WC4 EDITOR"),
            FontSize = 80,
            FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(30, 0, 0, 96),
            IsHitTestVisible = false
        });

        var title = new TextBlock
        {
            Text = Loc.T("Main.Loading.Title"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            FontStretch = FontStretches.Expanded,
            Foreground = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };
        status = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 215)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(20, 0, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(title);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            Margin = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215))
        };

        var panel = new StackPanel { Margin = new Thickness(50, 0, 50, 28), VerticalAlignment = VerticalAlignment.Bottom };
        panel.Children.Add(header);
        panel.Children.Add(progress);

        // 底部渐暗条：保证白字在任意背景图上都看得清
        overlay.Children.Add(new Border
        {
            Height = 130,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(200, 0, 0, 0), 90),
            Child = panel
        });
        return overlay;
    }

    private static BitmapImage? LoadLoadingBackground()
    {
        string path = ConfigManager.Instance.GetLoadingBackgroundImagePath();
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = 1920;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 加载画面背景图读取失败: {ex.Message}");
            return null;
        }
    }
}
