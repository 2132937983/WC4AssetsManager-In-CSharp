using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 陷阱编辑窗口 - 对应 VB 原版 TrapModifier.ShowTrapEditDialog。
/// 可修改陷阱的三个可编辑字段：编制(Organization)、所属军团(LegionId)、血量(Health)。
/// </summary>
public sealed class TrapSettingWindow : Window
{
    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);
    private readonly Color _buttonColor = Color.FromRgb(70, 130, 180);

    private readonly TextBox _organizationBox;
    private readonly TextBox _legionBox;
    private readonly TextBox _healthBox;

    private bool _isClosing;
    private Trap _resultTrap;
    private bool _confirmed;

    /// <summary>编辑后的陷阱数据（Coordinate 保持传入时的值）</summary>
    public Trap ResultTrap => _resultTrap;

    /// <summary>是否点击了「完成」</summary>
    public bool Confirmed => _confirmed;

    public TrapSettingWindow(Trap trap, bool isNew = false)
    {
        string titleText = isNew ? "创建陷阱" : "编辑陷阱";

        Title = titleText;
        Width = 360;
        Height = 310;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 14;

        _resultTrap = trap;
        _organizationBox = CreateNumberBox(trap.Organization.ToString());
        _legionBox = CreateNumberBox(trap.LegionId.ToString());
        _healthBox = CreateNumberBox(trap.Health.ToString());

        Content = CreateWindowContent(titleText);
        Loaded += (_, _) => PlayFadeInAnimation();
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelAndClose();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmAndClose();
        }
    }

    private FrameworkElement CreateWindowContent(string titleText)
    {
        var outerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, _themeColor.R, _themeColor.G, _themeColor.B)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 8,
                BlurRadius = 20,
                Opacity = 0.4
            }
        };

        var mainGrid = new Grid { Margin = new Thickness(16) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        mainGrid.Children.Add(CreateTitleBar(titleText));

        var inputPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };
        inputPanel.Children.Add(CreateInputRow("编制", _organizationBox, "1-255，决定陷阱图标数量"));
        inputPanel.Children.Add(CreateInputRow("所属军团", _legionBox, "0-255，决定陷阱显示的国旗"));
        inputPanel.Children.Add(CreateInputRow("血量", _healthBox, "0-255"));

        Grid.SetRow(inputPanel, 1);
        mainGrid.Children.Add(inputPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancelButton = CreateStyledButton("取消", new SolidColorBrush(Color.FromRgb(80, 80, 80)));
        cancelButton.Click += (_, _) => CancelAndClose();
        buttonPanel.Children.Add(cancelButton);

        var doneButton = CreateStyledButton("完成", new SolidColorBrush(_buttonColor));
        doneButton.Click += (_, _) => ConfirmAndClose();
        buttonPanel.Children.Add(doneButton);

        Grid.SetRow(buttonPanel, 2);
        mainGrid.Children.Add(buttonPanel);

        outerBorder.Child = mainGrid;
        return outerBorder;
    }

    private Grid CreateTitleBar(string titleText)
    {
        var titleBar = new Grid();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var titleLabel = new Label
        {
            Content = titleText,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleLabel, 0);
        titleBar.Children.Add(titleLabel);

        var closeButton = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Background = Brushes.Transparent,
            Foreground = _textBrush,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeButton.Click += (_, _) => CancelAndClose();
        closeButton.MouseEnter += (_, _) => closeButton.Background = new SolidColorBrush(Colors.Red);
        closeButton.MouseLeave += (_, _) => closeButton.Background = Brushes.Transparent;
        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(closeButton);

        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        Grid.SetRow(titleBar, 0);
        return titleBar;
    }

    private Grid CreateInputRow(string label, TextBox box, string hint)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = _textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(box);
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            Margin = new Thickness(0, 2, 0, 0)
        });

        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        return row;
    }

    private static TextBox CreateNumberBox(string value) => new()
    {
        Text = value,
        Height = 30,
        VerticalContentAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(6, 0, 0, 0),
        FontSize = 14
    };

    private static Button CreateStyledButton(string text, Brush background)
    {
        var btn = new Button
        {
            Content = text,
            Width = 120,
            Height = 36,
            Margin = new Thickness(6, 3, 6, 3),
            Background = background,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand
        };
        btn.MouseEnter += (_, _) => btn.Opacity = 0.8;
        btn.MouseLeave += (_, _) => btn.Opacity = 1.0;
        return btn;
    }

    private void ConfirmAndClose()
    {
        var trap = _resultTrap;
        // 解析失败时保留原值；超出范围时截断到合法区间（与 VB 原版的 Math.Max/Min 一致）
        trap.Organization = (short)ParseClamped(_organizationBox.Text, 1, 255, trap.Organization);
        trap.LegionId = (short)ParseClamped(_legionBox.Text, 0, 255, trap.LegionId);
        trap.Health = (short)ParseClamped(_healthBox.Text, 0, 255, trap.Health);

        _resultTrap = trap;
        _confirmed = true;
        PlayFadeOutAndClose();
    }

    private void CancelAndClose()
    {
        _confirmed = false;
        PlayFadeOutAndClose();
    }

    private static int ParseClamped(string? text, int min, int max, int fallback)
    {
        if (!int.TryParse(text, out int value)) return fallback;
        return Math.Max(min, Math.Min(max, value));
    }

    private void PlayFadeInAnimation()
    {
        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeIn);
        sb.Begin();
    }

    private void PlayFadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var sb = new Storyboard();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        sb.Completed += (_, _) => Close();
        sb.Begin();
    }
}
