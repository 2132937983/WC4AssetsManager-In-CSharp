using System.Windows.Media.Imaging;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 调试控制台窗口 - 跟随主窗口的独立弹窗（F3 开关）。
/// <para>
/// 标题栏 / 关闭按钮 / 输入框走 <see cref="EditorTheme"/>；日志区保留"背景图 + 黑色半透明遮罩 + 白色等宽字"
/// 的终端外观，那是控制台本身的用途，不跟主题走。
/// </para>
/// </summary>
public sealed class DebugConsoleWindow : Window
{
    /// <summary>弹窗默认尺寸（改成独立弹窗后不再跟随主窗口大小）。</summary>
    private const double DefaultWidth = 980;
    private const double DefaultHeight = 640;

    /// <summary>是否已经定过位（首次显示居中，之后沿用用户拖过的位置）。</summary>
    private bool _hasPositioned;
    private double _lastOwnerLeft;
    private double _lastOwnerTop;

    private readonly DebugConsole _console;
    private ScrollViewer _scrollViewer = null!;
    private StackPanel _textPanel = null!;
    private TextBox _inputTextBox = null!;
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorVisible = true;
    private int _renderedLineCount;

    /// <summary>已排队一次整屏刷新：连续写多行时合并成一次重建（以前每写一行就重建一遍上千个文本块）</summary>
    private bool _refreshQueued;

    public DebugConsoleWindow(DebugConsole console, Window ownerWindow)
    {
        EditorTheme.Apply(this);

        _console = console;

        // 窗口设置 —— 独立弹窗，不再铺满主窗口
        Owner = ownerWindow;
        WindowStyle = WindowStyle.None;
        // 淡入靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = DefaultWidth;
        Height = DefaultHeight;
        ShowInTaskbar = false;
        Opacity = 0;

        _lastOwnerLeft = ownerWindow.Left;
        _lastOwnerTop = ownerWindow.Top;
        // 跟随主窗口移动（保持相对偏移），但不再跟随主窗口大小
        ownerWindow.LocationChanged += OnOwnerLocationChanged;

        // 创建内容
        Content = CreateContent();

        // 光标闪烁定时器
        _cursorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            UpdateCursorVisibility();
        };

        // 事件绑定
        Loaded += (_, _) =>
        {
            PlayFadeInAnimation();
            _cursorTimer.Start();
            _inputTextBox.Focus();
            RefreshDisplay();
        };
        Closing += (_, e) =>
        {
            _cursorTimer.Stop();
            // 同步控制台可见状态，避免窗口被直接关闭后状态残留
            _console.NotifyHostClosed();
            // 移除父窗口事件监听
            if (Owner != null)
            {
                Owner.LocationChanged -= OnOwnerLocationChanged;
            }
            e.Cancel = false;
        };
        KeyDown += Window_KeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        // 绑定控制台输出重定向
        _console.InvalidateCallback = RefreshDisplay;
    }

    /// <summary>
    /// 每次显示前调用（窗口改为复用后，Loaded 只触发一次，所以定位/焦点/刷新都放这里）。
    /// 跟随父窗口当前的位置与大小、把透明度复位、重启光标定时器，并把焦点落到输入框。
    /// </summary>
    public void PrepareForShow(Window ownerWindow)
    {
        Owner = ownerWindow;
        _lastOwnerLeft = ownerWindow.Left;
        _lastOwnerTop = ownerWindow.Top;

        // 首次显示：居中在主窗口上；之后沿用用户拖动过的位置
        // （主窗口移动时窗口会跟着平移，见 OnOwnerLocationChanged）
        if (!_hasPositioned)
        {
            Left = ownerWindow.Left + Math.Max(0, (ownerWindow.ActualWidth - Width) / 2);
            Top = ownerWindow.Top + Math.Max(0, (ownerWindow.ActualHeight - Height) / 2);
            _hasPositioned = true;
        }

        Opacity = 1.0;

        _cursorTimer.Start();

        // 隐藏期间产生的新日志要补上，并把焦点送回输入框
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshDisplay();
            _inputTextBox.Focus();
            Keyboard.Focus(_inputTextBox);
        }), DispatcherPriority.Input);
    }

    private void OnOwnerLocationChanged(object? sender, EventArgs e)
    {
        if (Owner == null) return;

        // 弹窗跟着主窗口平移，但保持自己相对主窗口的偏移（不再是覆盖窗那种贴合）
        Left += Owner.Left - _lastOwnerLeft;
        Top += Owner.Top - _lastOwnerTop;
        _lastOwnerLeft = Owner.Left;
        _lastOwnerTop = Owner.Top;
    }

    private Grid CreateContent()
    {
        var grid = new Grid();

        // Grid必须设置Background才能接收鼠标/拖放事件
        grid.Background = Brushes.Transparent;

        // 在内容Grid上启用拖放（透明窗口本身不支持拖放）
        grid.AllowDrop = true;
        grid.PreviewDrop += OnFileDrop;
        grid.PreviewDragOver += OnFileDragOver;

        // 加载背景图片
        string bgPath = ConfigManager.Instance.GetConsoleBackgroundPath();
        if (System.IO.File.Exists(bgPath))
        {
            try
            {
                var brush = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri(bgPath, UriKind.Absolute)),
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.6
                };
                var background = new Rectangle
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Fill = brush
                };
                grid.Children.Add(background);
            }
            catch
            {
                // 加载失败使用纯色背景
                AddFallbackBackground(grid);
            }
        }
        else
        {
            AddFallbackBackground(grid);
        }

        // 添加半透明遮罩层（60%不透明度）：压暗背景图让日志可读，属于控制台外观，不走主题
        var overlay = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0))
        };
        grid.Children.Add(overlay);

        // 标题栏（拖动移动窗口；✕ 只隐藏，窗口复用不销毁）
        grid.Children.Add(CreateTitleBar());

        // 滚动视图（日志区域），上边距让开标题栏
        _scrollViewer = new ScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10, 42, 10, 50),
            Padding = new Thickness(0, 0, 0, 10)
        };

        _textPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        _scrollViewer.Content = _textPanel;
        grid.Children.Add(_scrollViewer);

        // 输入面板（底部固定）
        var inputPanel = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 10, 10),
            Height = 30
        };
        inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 光标提示符 "> "
        var cursorText = new TextBlock
        {
            Text = "> ",
            Foreground = EditorTheme.Success,
            FontFamily = EditorTheme.MonoFontFamily,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        cursorText.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 2,
            ShadowDepth = 1,
            Opacity = 0.8
        };
        Grid.SetColumn(cursorText, 0);
        inputPanel.Children.Add(cursorText);

        // 输入框：配色交给主题的 TextBox 样式，只保留等宽字体
        _inputTextBox = new TextBox
        {
            FontFamily = EditorTheme.MonoFontFamily,
            FontSize = 14,
            Padding = new Thickness(5, 2, 5, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _inputTextBox.PreviewKeyDown += InputTextBox_PreviewKeyDown;
        _inputTextBox.TextChanged += InputTextBox_TextChanged;
        Grid.SetColumn(_inputTextBox, 1);
        inputPanel.Children.Add(_inputTextBox);

        grid.Children.Add(inputPanel);

        return grid;
    }

    /// <summary>
    /// 标题栏：按住可拖动窗口；✕ 是隐藏而不是关闭 —— 窗口是复用实例。
    /// </summary>
    private FrameworkElement CreateTitleBar()
    {
        var bar = new Border
        {
            Height = 32,
            VerticalAlignment = VerticalAlignment.Top,
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Loc.T("Assist.Console.Title"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(title, 0);
        row.Children.Add(title);

        var close = EditorTheme.GhostButton("✕", (_, _) => _console.HideConsole());
        close.Width = 36;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(0, 1, 2, 1);
        close.ToolTip = Loc.T("Assist.Console.HideTooltip");
        Grid.SetColumn(close, 1);
        row.Children.Add(close);

        bar.Child = row;
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        return bar;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            e.Handled = true;

            // 走主窗口的统一开关（而不是直接 HideConsole）：
            // 关子窗口时 Windows 可能把主窗口连累成最小化，那条路径里有还原动作。
            if (Owner is MainWindow main) main.ToggleDebugConsole();
            else _console.HideConsole();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _console.HideConsole();
        }
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                ExecuteInput();
                break;

            case Key.Up:
                e.Handled = true;
                NavigateHistory(-1);
                break;

            case Key.Down:
                e.Handled = true;
                NavigateHistory(1);
                break;

            case Key.PageUp:
                e.Handled = true;
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - 200);
                break;

            case Key.PageDown:
                e.Handled = true;
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + 200);
                break;

            case Key.Home:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    _scrollViewer.ScrollToTop();
                }
                break;

            case Key.End:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    _scrollViewer.ScrollToBottom();
                }
                break;
        }
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 同步输入到控制台
        _console.SetCurrentInput(_inputTextBox.Text);
    }

    private void ExecuteInput()
    {
        string input = _inputTextBox.Text;
        _inputTextBox.Clear();
        _console.ExecuteInput(input);
        RefreshDisplay();
        _scrollViewer.ScrollToBottom();
    }

    /// <summary>
    /// 可拖入执行的脚本类型：.zme（命令行脚本，逐行执行；.zsc 是 VB 版的叫法，按同样方式执行）与 .lua（Lua 脚本）。
    /// </summary>
    public static bool IsSupportedScript(string path)
        => path.EndsWith(".zme", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".zsc", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && IsSupportedScript(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
            }
        }
        e.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        foreach (var file in files.Where(IsSupportedScript))
        {
            await RunScriptFileAsync(_console, file, RefreshDisplayIncremental);
            RefreshDisplayIncremental();
            _scrollViewer.ScrollToBottom();
        }
    }

    /// <summary>
    /// 执行一个脚本文件：.lua 交给 lua 命令；.zme / .zsc 逐行执行（支持整行与行尾注释），每 200 行报一次进度。
    /// 控制台窗口的拖放与渲染场景的拖放共用这一套，输出、撤销都与手动输入完全一致。
    /// </summary>
    /// <param name="console">执行命令并接收输出的控制台</param>
    /// <param name="file">脚本路径</param>
    /// <param name="progressRefresh">执行期间刷新显示的方式；为 null 时用控制台原来的整屏刷新</param>
    public static async Task RunScriptFileAsync(DebugConsole console, string file, Action? progressRefresh = null)
    {
        if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            console.WriteLine(Loc.F("Assist.Console.RunLuaScript", System.IO.Path.GetFileName(file)));
            // 用 BatchExecuteInput：它会先查本地命令表再回退到命令宿主，
            // 而 ExecuteInput 在本地表里找不到时会先吐一句"未知命令"。路径加引号以兼容含空格的目录。
            console.BatchExecuteInput($"lua \"{file}\"");
            return;
        }

        console.WriteLine(Loc.F("Assist.Console.LoadScript", System.IO.Path.GetFileName(file)));

        // 批量执行期间摘掉"每写一行就整屏重建"的回调，按进度手动刷新
        var savedCallback = console.InvalidateCallback;
        var refresh = progressRefresh ?? savedCallback;
        console.InvalidateCallback = null;
        console.BeginBatchMode();

        // 只依赖 ICommandHost 抽象（由 DebugConsole 暴露），不直接碰具体命令宿主
        var cliHost = console.CommandHost;
        cliHost.BeginBatchMode();

        try
        {
            var lines = await System.IO.File.ReadAllLinesAsync(file);
            int totalLines = lines.Length;
            int executedCount = 0;
            int skippedCount = 0;
            const int refreshInterval = 200;

            for (int i = 0; i < totalLines; i++)
            {
                // 同样支持行尾注释，与 run 命令的解析保持一致
                string line = Core.Commands.CommandLineText.StripComment(lines[i].Trim());

                if (string.IsNullOrEmpty(line) || Core.Commands.CommandLineText.IsCommentLine(line))
                {
                    skippedCount++;
                    continue;
                }

                console.BatchExecuteInput(line);
                executedCount++;

                if (executedCount % refreshInterval == 0)
                {
                    console.WriteLine(Loc.F("Assist.Console.ScriptProgress", executedCount, totalLines));
                    refresh?.Invoke();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            console.WriteLine(Loc.F("Assist.Console.ScriptDone", executedCount, skippedCount, totalLines));
        }
        catch (Exception ex)
        {
            console.WriteLine(Loc.F("Assist.Console.ScriptFailed", ex.Message));
        }
        finally
        {
            cliHost.EndBatchMode();
            console.EndBatchMode();
            console.InvalidateCallback = savedCallback;
        }

        refresh?.Invoke();
    }

    private void NavigateHistory(int direction)
    {
        string? historyInput = _console.NavigateHistory(direction);
        if (historyInput != null)
        {
            _inputTextBox.Text = historyInput;
            _inputTextBox.CaretIndex = historyInput.Length;
        }
        else if (direction > 0)
        {
            _inputTextBox.Clear();
        }
    }

    private void RefreshDisplay()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            _textPanel.Children.Clear();

            var lines = _console.GetLogLines();
            foreach (var line in lines)
            {
                _textPanel.Children.Add(CreateLogTextBlock(line));
            }

            _renderedLineCount = lines.Count;
            _scrollViewer.ScrollToBottom();
        });
    }

    private void RefreshDisplayIncremental()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var lines = _console.GetLogLines();
            int currentCount = lines.Count;

            if (_renderedLineCount > currentCount)
            {
                _textPanel.Children.Clear();
                for (int i = 0; i < currentCount; i++)
                    _textPanel.Children.Add(CreateLogTextBlock(lines[i]));
            }
            else
            {
                for (int i = _renderedLineCount; i < currentCount; i++)
                    _textPanel.Children.Add(CreateLogTextBlock(lines[i]));
            }

            _renderedLineCount = currentCount;
            _scrollViewer.ScrollToBottom();
        });
    }

    /// <summary>日志行：白色等宽字 + 黑色描边阴影，压在背景图上也能读 —— 终端外观，不走主题色。</summary>
    private static TextBlock CreateLogTextBlock(string line)
    {
        var textBlock = new TextBlock
        {
            Text = line,
            Foreground = Brushes.White,
            FontFamily = EditorTheme.MonoFontFamily,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1)
        };
        textBlock.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 2,
            ShadowDepth = 1,
            Opacity = 0.8
        };
        return textBlock;
    }

    private void UpdateCursorVisibility()
    {
        // WPF TextBox自带光标，不需要额外处理
    }

    /// <summary>没有背景图时的纯色底：同样是控制台的黑色半透明，不走主题。</summary>
    private static void AddFallbackBackground(Grid grid)
    {
        var background = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0))
        };
        grid.Children.Add(background);
    }

    private void PlayFadeInAnimation()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        BeginAnimation(OpacityProperty, fadeIn);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_console.IsVisible)
            _console.HideConsole();
        base.OnClosed(e);
    }
}
