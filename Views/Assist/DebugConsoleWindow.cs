using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Rendering.Skia;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 调试控制台窗口 - 占满父窗口全屏的WPF窗口
/// </summary>
public sealed class DebugConsoleWindow : Window
{
    private readonly DebugConsole _console;
    private ScrollViewer _scrollViewer = null!;
    private StackPanel _textPanel = null!;
    private TextBox _inputTextBox = null!;
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorVisible = true;
    private int _renderedLineCount;

    public DebugConsoleWindow(DebugConsole console, Window ownerWindow)
    {
        _console = console;

        // 窗口设置 - 占满父窗口全屏
        Owner = ownerWindow;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = ownerWindow.Left;
        Top = ownerWindow.Top;
        Width = ownerWindow.ActualWidth > 0 ? ownerWindow.ActualWidth : ownerWindow.Width;
        Height = ownerWindow.ActualHeight > 0 ? ownerWindow.ActualHeight : ownerWindow.Height;
        ShowInTaskbar = false;
        Opacity = 0;

        // 监听父窗口大小变化
        ownerWindow.LocationChanged += OnOwnerLocationChanged;
        ownerWindow.SizeChanged += OnOwnerSizeChanged;

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
            // 移除父窗口事件监听
            if (Owner != null)
            {
                Owner.LocationChanged -= OnOwnerLocationChanged;
                Owner.SizeChanged -= OnOwnerSizeChanged;
            }
            e.Cancel = false;
        };
        KeyDown += Window_KeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        // 绑定控制台输出重定向
        _console.InvalidateCallback = RefreshDisplay;
    }

    private void OnOwnerLocationChanged(object? sender, EventArgs e)
    {
        if (Owner != null)
        {
            Left = Owner.Left;
            Top = Owner.Top;
        }
    }

    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Owner != null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }
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

        // 添加半透明遮罩层（60%不透明度）
        var overlay = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)) // 60%不透明度的黑色遮罩
        };
        grid.Children.Add(overlay);

        // 滚动视图（日志区域）
        _scrollViewer = new ScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10, 10, 10, 50),
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
            Foreground = Brushes.LightGreen,
            FontFamily = new FontFamily("Consolas"),
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

        // 输入框
        _inputTextBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Padding = new Thickness(5, 2, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.LightGreen
        };
        _inputTextBox.PreviewKeyDown += InputTextBox_PreviewKeyDown;
        _inputTextBox.TextChanged += InputTextBox_TextChanged;
        Grid.SetColumn(_inputTextBox, 1);
        inputPanel.Children.Add(_inputTextBox);

        grid.Children.Add(inputPanel);

        return grid;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            e.Handled = true;
            _console.HideConsole();
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

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && files[0].EndsWith(".zme", StringComparison.OrdinalIgnoreCase))
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

        foreach (var file in files)
        {
            if (!file.EndsWith(".zme", StringComparison.OrdinalIgnoreCase)) continue;

            _console.WriteLine($"[脚本] 加载脚本文件: {System.IO.Path.GetFileName(file)}");

            var savedCallback = _console.InvalidateCallback;
            _console.InvalidateCallback = null;
            _console.BeginBatchMode();

            var cliHost = Core.Commands.CliCommandHost.Instance;
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
                    string line = lines[i].Trim();

                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//"))
                    {
                        skippedCount++;
                        continue;
                    }

                    _console.BatchExecuteInput(line);
                    executedCount++;

                    if (executedCount % refreshInterval == 0)
                    {
                        _console.WriteLine($"[脚本] 进度: {executedCount}/{totalLines}");
                        RefreshDisplayIncremental();
                        _scrollViewer.ScrollToBottom();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                }

                _console.WriteLine($"[脚本] 执行完成: {executedCount} 条命令, {skippedCount} 条注释跳过, 共 {totalLines} 行");
            }
            catch (Exception ex)
            {
                _console.WriteLine($"[脚本] 执行失败: {ex.Message}");
            }
            finally
            {
                cliHost.EndBatchMode();
                _console.EndBatchMode();
                _console.InvalidateCallback = savedCallback;
            }

            RefreshDisplayIncremental();
            _scrollViewer.ScrollToBottom();
        }
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
        Dispatcher.BeginInvoke(() =>
        {
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

    private static TextBlock CreateLogTextBlock(string line)
    {
        var textBlock = new TextBlock
        {
            Text = line,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
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

    private static void AddFallbackBackground(Grid grid)
    {
        var background = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)) // 60%不透明度的黑色背景
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