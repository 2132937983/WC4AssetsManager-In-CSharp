using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WC4MapEditor.Views.Dialogs;

public sealed class ProgressWindow : Window
{
    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

    private ProgressBar _progressBar = null!;
    private TextBlock _progressLabel = null!;
    private Button _cancelButton = null!;
    private bool _isClosing;
    private bool _cancelled;

    public bool IsCancelled => _cancelled;

    public ProgressWindow()
    {
        Title = "地形识别进度";
        Width = 420;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 14;

        Content = CreateWindowContent();
        Loaded += (_, _) => PlayFadeInAnimation();
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
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
        sb.Completed += (_, _) => Close();
        sb.Begin();
    }

    private FrameworkElement CreateWindowContent()
    {
        var outerGrid = new Grid();

        var mainBorder = new Border
        {
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(240, _themeColor.R, _themeColor.G, _themeColor.B)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10)
        };

        var mainStack = new StackPanel();

        // 标题栏（可拖动）
        var titleBar = new Border
        {
            Height = 32,
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "地形识别进度",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        titleBar.MouseLeftButtonDown += (_, _) => DragMove();
        mainStack.Children.Add(titleBar);

        // 内容区域
        var contentPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };

        _progressLabel = new TextBlock
        {
            Text = "准备开始...",
            Foreground = _textBrush,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        contentPanel.Children.Add(_progressLabel);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 20,
            Margin = new Thickness(0, 0, 0, 15)
        };
        contentPanel.Children.Add(_progressBar);

        _cancelButton = new Button
        {
            Content = "取消",
            Width = 80,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };
        _cancelButton.Click += (_, _) => Cancel();
        contentPanel.Children.Add(_cancelButton);

        mainStack.Children.Add(contentPanel);
        mainBorder.Child = mainStack;
        outerGrid.Children.Add(mainBorder);

        return outerGrid;
    }

    private void Cancel()
    {
        _cancelled = true;
        _cancelButton.IsEnabled = false;
        _cancelButton.Content = "正在取消...";
    }

    public void UpdateProgress(int current, int total, int row, int col, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isClosing) return;
            _progressBar.Value = Math.Min(percent, 100);
            _progressLabel.Text = $"正在处理: 第{row + 1}行，第{col + 1}列 ({current}/{total}) {percent}%";
        });
    }
}