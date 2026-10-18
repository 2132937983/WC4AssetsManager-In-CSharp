using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WC4MapEditor.GUI.Views.Dialogs;

public sealed class ProgressWindow : Window
{
    private ProgressBar _progressBar = null!;
    private TextBlock _progressLabel = null!;
    private Button _cancelButton = null!;
    private bool _isClosing;
    private bool _cancelled;

    public bool IsCancelled => _cancelled;

    public ProgressWindow()
    {
        EditorTheme.Apply(this);
        Title = Loc.T("Dialog.ProgressTitle");
        Width = 420;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;

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
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6,
                Color = EditorTheme.ColorOf(EditorTheme.Bg0)
            }
        };

        var mainStack = new StackPanel();

        // 标题栏（可拖动）。调用方会按用途改 Title（截图 / 经纬度 / 地形识别），标题栏文字跟随窗口标题
        var titleText = EditorTheme.Muted("");
        titleText.HorizontalAlignment = HorizontalAlignment.Center;
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });

        var titleBar = new Border
        {
            Height = 32,
            Background = EditorTheme.Bg3,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Cursor = Cursors.Hand,
            Child = titleText
        };
        titleBar.MouseLeftButtonDown += (_, _) => DragMove();
        mainStack.Children.Add(titleBar);

        // 内容区域
        var contentPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };

        _progressLabel = new TextBlock
        {
            Text = Loc.T("Dialog.Preparing"),
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
            Margin = new Thickness(0, 0, 0, 15)
        };
        contentPanel.Children.Add(_progressBar);

        _cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => Cancel());
        _cancelButton.MinWidth = 96;
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Center;
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
        _cancelButton.Content = Loc.T("Dialog.Cancelling");
    }

    public void UpdateProgress(int current, int total, int row, int col, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isClosing) return;
            _progressBar.Value = Math.Min(percent, 100);
            _progressLabel.Text = Loc.F("Dialog.ProgressCell", row + 1, col + 1, current, total, percent);
        });
    }
}
