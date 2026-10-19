using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 格子信息浮窗：置顶、不抢焦点；显示场景指定的格子（选中格 / 查看的格子），内容视图随编辑模式切换；标题栏可拖动。
/// 外观走 <see cref="EditorTheme"/>，分区标题 / 字段名 / 等宽 hex 都用主题里的样式。
/// </summary>
public sealed class HexInfoWindow : Window
{
    private readonly IHexInfoProvider _provider;
    private Border _mainBorder = null!;
    private StackPanel _contentStack = null!;
    private ScrollViewer _scrollViewer = null!;

    private int _currentCol = -1;
    private int _currentRow = -1;
    private HexInfoDisplayMode _currentMode = HexInfoDisplayMode.Default;
    private bool _isClosing;

    private const int WindowWidth = 320;

    /// <param name="provider">格子信息来源（渲染场景传入绑定到其当前地图的服务）</param>
    public HexInfoWindow(IHexInfoProvider provider)
    {
        _provider = provider;
        EditorTheme.Apply(this);
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        Title = Loc.T("Assist.HexInfo.Title");
        Width = WindowWidth;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        // 淡入淡出靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;

        Content = CreateWindowContent();
        Loaded += (_, _) => PlayFadeInAnimation();
    }

    private void PlayFadeInAnimation()
    {
        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeIn);
        sb.Begin();
    }

    public void PlayFadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var sb = new Storyboard();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeOut);

        bool closed = false;
        void CloseOnce()
        {
            if (closed) return;
            closed = true;
            try { Close(); } catch { /* 已经关掉了 */ }
        }

        sb.Completed += (_, _) => CloseOnce();
        sb.Begin();

        // 兜底：同一个属性上再起动画时，旧动画会被替换，它的 Completed **不会**触发，
        // 窗口就会永远留在屏幕上；而调用方这时已经把引用置 null 了 —— 结果再也关不掉。
        // 所以淡出开始后无条件再补一次关闭（CloseOnce 保证只关一次）。
        var fallback = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        fallback.Tick += (_, _) =>
        {
            fallback.Stop();
            CloseOnce();
        };
        fallback.Start();
    }

    private FrameworkElement CreateWindowContent()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var dragHint = EditorTheme.Muted(Loc.T("Assist.HexInfo.DragHint"));
        dragHint.HorizontalAlignment = HorizontalAlignment.Center;
        dragHint.VerticalAlignment = VerticalAlignment.Center;

        var titleBar = new Border
        {
            Height = 30,
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
            Child = dragHint
        };

        titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        titleBar.MouseLeftButtonUp += TitleBar_MouseLeftButtonUp;
        titleBar.MouseMove += TitleBar_MouseMove;

        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _contentStack = new StackPanel { Margin = new Thickness(15) };

        _scrollViewer.Content = _contentStack;
        Grid.SetRow(_scrollViewer, 1);
        mainGrid.Children.Add(_scrollViewer);

        // 无边框窗口描一圈边，浮在地图上时才看得出边界
        _mainBorder = new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = mainGrid
        };

        return _mainBorder;
    }

    /// <summary>显示指定格子；<paramref name="mode"/> 为 null 时沿用当前视图。即使是同一格也会重新查询（数据可能刚改过）。</summary>
    public void UpdateHexInfo(int col, int row, HexInfoDisplayMode? mode = null)
    {
        _currentCol = col;
        _currentRow = row;
        if (mode.HasValue) _currentMode = mode.Value;
        Refresh();
    }

    /// <summary>重新查询正在显示的格子（数据修改、撤销 / 重做之后调用）。</summary>
    public void Refresh()
    {
        if (_currentCol < 0 || _currentRow < 0) return;

        Title = Loc.F("Assist.HexInfo.TitleWithCoord", _currentCol, _currentRow);
        var info = _provider.GetHexCellInfo(_currentCol, _currentRow, _currentMode);
        if (info == null)
        {
            ClearHexInfo();
            return;
        }

        RenderFromCellInfo(info);
    }

    private void RenderFromCellInfo(HexCellInfo info)
    {
        _contentStack.Children.Clear();

        // 模式标题与分区文字由 Core 的 HexCellInfo 给出
        AddTitle(info.GetModeTitle());

        var sections = info.GetSectionsForMode();
        for (int i = 0; i < sections.Count; i++)
        {
            if (i > 0) AddSeparator();
            RenderSection(sections[i]);
        }
    }

    private void RenderSection(HexInfoSection section)
    {
        if (!string.IsNullOrEmpty(section.Title) && section.Items.Count > 0)
        {
            AddSectionHeader(section.Title);

            foreach (var (label, value) in section.Items)
                AddInfoRow(label, value);

            if (section.RawData != null && section.RawData.Length > 0)
            {
                AddSeparator();
                AddSectionHeader(Loc.T("Assist.HexInfo.HexData"));
                AddHexDumpText(HexCellInfo.FormatHexDump(section.RawData));
            }
        }
    }

    private void AddTitle(string text)
    {
        var titleText = new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 15)
        };
        _contentStack.Children.Add(titleText);
    }

    private void AddSectionHeader(string title)
    {
        var header = EditorTheme.Section(title);
        header.FontSize = 13;
        header.Margin = new Thickness(0, 10, 0, 5);
        _contentStack.Children.Add(header);
    }

    private void AddInfoRow(string label, string value)
    {
        var panel = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = EditorTheme.FieldLabel(label + ":");
        Grid.SetColumn(labelText, 0);
        panel.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        panel.Children.Add(valueText);

        _contentStack.Children.Add(panel);
    }

    private void AddHexDumpText(string hexDump)
    {
        var hexText = new TextBlock
        {
            Text = hexDump,
            FontSize = 11,
            FontFamily = EditorTheme.MonoFontFamily,
            Foreground = EditorTheme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10)
        };
        _contentStack.Children.Add(hexText);
    }

    private void AddSeparator()
    {
        var separator = new Border
        {
            Height = 1,
            Background = EditorTheme.Border,
            Margin = new Thickness(0, 10, 0, 10)
        };
        _contentStack.Children.Add(separator);
    }

    public void ClearHexInfo()
    {
        _currentCol = -1;
        _currentRow = -1;

        _contentStack.Children.Clear();
        AddTitle(Loc.T("Assist.HexInfo.Title"));
        AddInfoRow(Loc.T("Assist.HexInfo.Coordinate"), "-");
    }

    #region Drag Support

    private bool _isDragging;
    private Point _dragStartPoint;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        ((Border)sender).CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((Border)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        Point current = e.GetPosition(this);
        Left += current.X - _dragStartPoint.X;
        Top += current.Y - _dragStartPoint.Y;
    }

    #endregion
}
