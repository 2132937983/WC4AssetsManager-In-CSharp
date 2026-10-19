namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 渲染场景底栏的状态区：模式 / 操作消息、格子位置与缩放比例（对齐 VB BaseRenderScene 的状态栏）。
/// <para>
/// 以前 EditModeManager 的状态消息只进 Debug.WriteLine，模式切换、操作结果、失败原因用户一概看不到。
/// 状态区保持渲染场景自己的外观（白字 + 阴影，压在半透明黑底栏上），不套编辑主题。
/// </para>
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>消息显示多久后变暗，提示这是一条旧消息</summary>
    private static readonly TimeSpan SceneStatusFreshTime = TimeSpan.FromSeconds(6);
    private const double SceneStatusDimOpacity = 0.55;

    private TextBlock? _sceneStatusText;
    private TextBlock? _scenePositionText;
    private TextBlock? _sceneZoomText;
    private DispatcherTimer? _sceneStatusDimTimer;

    private double _shownZoom = double.NaN;
    private (int Col, int Row)? _shownPosition;
    private bool _shownPositionValid;
    private bool _readoutRefreshQueued;

    /// <summary>
    /// 在底栏左侧工具按钮与右侧窗口按钮之间放状态区：状态消息 + 位置、缩放读数。
    /// 状态区按两侧按钮的实际宽度留出边距，窗口太窄放不下时自动缩没，不会盖住按钮。
    /// </summary>
    private void BuildSceneStatusArea(Grid titleBar, FrameworkElement leftPanel, FrameworkElement rightPanel)
    {
        _sceneZoomText = CreateSceneStatusLabel(0.8);
        _sceneZoomText.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(_sceneZoomText, Dock.Right);

        _scenePositionText = CreateSceneStatusLabel(0.8);
        _scenePositionText.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(_scenePositionText, Dock.Right);

        _sceneStatusText = CreateSceneStatusLabel(1.0);
        _sceneStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        _sceneStatusText.Text = Loc.T("Main.ReadyStatus");

        var area = new DockPanel
        {
            LastChildFill = true,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true
        };
        area.Children.Add(_sceneZoomText);
        area.Children.Add(_scenePositionText);
        area.Children.Add(_sceneStatusText);

        // 状态区也是底栏的空白处：按住照样能拖动窗口
        area.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            Window.DragMove();
            e.Handled = true;
        };

        void FitBetweenPanels()
        {
            const double gap = 12;
            area.Margin = new Thickness(leftPanel.Margin.Left + leftPanel.ActualWidth + gap, 0,
                rightPanel.Margin.Right + rightPanel.ActualWidth + gap, 0);
        }
        // 左侧按钮条按内容宽度排布（默认会横向拉满整条底栏，量出来的宽度就是整条底栏，状态区被挤没），
        // 场景名、编辑层标签变长时它的宽度随之变化，状态区跟着让位
        leftPanel.HorizontalAlignment = HorizontalAlignment.Left;
        leftPanel.SizeChanged += (_, _) => FitBetweenPanels();
        rightPanel.SizeChanged += (_, _) => FitBetweenPanels();

        titleBar.Children.Add(area);
    }

    private static TextBlock CreateSceneStatusLabel(double opacity) => new()
    {
        Foreground = Brushes.White,
        Opacity = opacity,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black }
    };

    /// <summary>
    /// 在状态栏显示一条消息，并记入调试控制台。多行消息在状态栏只显示第一行，完整内容放进鼠标提示。
    /// 可以从任何线程调用。
    /// </summary>
    private void PostSceneStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PostSceneStatus(message));
            return;
        }
        if (_sceneStatusText == null || string.IsNullOrWhiteSpace(message)) return;

        string text = message.Replace("\r\n", "\n").Trim();
        int newline = text.IndexOf('\n');
        string shown = newline >= 0 ? text[..newline].TrimEnd() : text;

        // 同一条消息连续出现（例如按住一个被拦下的键）时只刷新显示，不在控制台里重复记录
        bool repeated = shown == _sceneStatusText.Text && _sceneStatusDimTimer?.IsEnabled == true;

        _sceneStatusText.Text = shown;
        _sceneStatusText.ToolTip = newline >= 0 ? text : null;
        _sceneStatusText.BeginAnimation(UIElement.OpacityProperty, null);
        _sceneStatusText.Opacity = 1.0;
        RestartSceneStatusDimTimer();

        if (repeated) return;
        foreach (var line in text.Split('\n'))
            _debugConsole.WriteLine(Loc.F("Scene.Console.Status", line));
    }

    private void RestartSceneStatusDimTimer()
    {
        if (_sceneStatusDimTimer == null)
        {
            _sceneStatusDimTimer = new DispatcherTimer { Interval = SceneStatusFreshTime };
            _sceneStatusDimTimer.Tick += (_, _) =>
            {
                _sceneStatusDimTimer.Stop();
                _sceneStatusText?.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(SceneStatusDimOpacity, TimeSpan.FromMilliseconds(400)));
            };
        }
        _sceneStatusDimTimer.Stop();
        _sceneStatusDimTimer.Start();
    }

    /// <summary>
    /// 刷新位置 / 缩放读数（合并到下一次调度执行）。重绘之后、鼠标移动、选区变化都会调用，
    /// 所以平移缩放、打开或切换地图（换了相机）之后读数也是新的。
    /// </summary>
    private void QueueSceneReadoutRefresh()
    {
        if (_readoutRefreshQueued) return;
        _readoutRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _readoutRefreshQueued = false;
            RefreshSceneReadouts();
        });
    }

    /// <summary>位置读数：鼠标在地图上时是鼠标所在格，否则是选中格；数值没变就不改文字。</summary>
    private void RefreshSceneReadouts()
    {
        if (_camera == null || _sceneZoomText == null || _scenePositionText == null) return;

        double zoom = _camera.ZoomLevel;
        if (zoom != _shownZoom)
        {
            _shownZoom = zoom;
            _sceneZoomText.Text = Loc.F("Scene.Status.Zoom", (int)Math.Round(zoom * 100));
        }

        var position = HoveredHex() ?? PrimaryHexInMap();
        if (_shownPositionValid && position == _shownPosition) return;
        _shownPositionValid = true;
        _shownPosition = position;
        _scenePositionText.Text = position is { } p
            ? Loc.F("Scene.Status.Position", p.Col, p.Row)
            : Loc.T("Scene.Status.PositionNone");
    }

    private void StopSceneStatusTimers()
    {
        _sceneStatusDimTimer?.Stop();
    }
}
