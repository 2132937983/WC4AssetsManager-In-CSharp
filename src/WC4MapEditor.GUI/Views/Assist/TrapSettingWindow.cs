using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 陷阱编辑窗口 - 对应 VB 原版 TrapModifier.ShowTrapEditDialog。
/// 可修改陷阱的三个可编辑字段：编制(Organization)、所属军团(LegionId)、血量(Health)。
/// </summary>
public sealed class TrapSettingWindow : Window
{
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
        EditorTheme.Apply(this);
        string titleText = isNew ? Loc.T("Assist.Trap.TitleNew") : Loc.T("Assist.Trap.TitleEdit");

        Title = titleText;
        Width = 360;
        Height = 310;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

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
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Bg0),
                Direction = 270,
                ShadowDepth = 6,
                BlurRadius = 20,
                Opacity = 0.6
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
        inputPanel.Children.Add(CreateInputRow(Loc.T("Assist.Trap.Organization"), _organizationBox, Loc.T("Assist.Trap.OrganizationHint")));
        inputPanel.Children.Add(CreateInputRow(Loc.T("Assist.Trap.Legion"), _legionBox, Loc.T("Assist.Trap.LegionHint")));
        inputPanel.Children.Add(CreateInputRow(Loc.T("Assist.Trap.Health"), _healthBox, Loc.T("Assist.Trap.HealthHint")));

        Grid.SetRow(inputPanel, 1);
        mainGrid.Children.Add(inputPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.Width = 120;
        cancelButton.Margin = new Thickness(6, 3, 6, 3);
        buttonPanel.Children.Add(cancelButton);

        var doneButton = EditorTheme.PrimaryButton(Loc.T("Assist.Common.Done"), (_, _) => ConfirmAndClose());
        doneButton.Width = 120;
        doneButton.Margin = new Thickness(6, 3, 6, 3);
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

        var titleLabel = EditorTheme.Title(titleText);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(titleLabel, 0);
        titleBar.Children.Add(titleLabel);

        var closeButton = EditorTheme.GhostButton("✕", (_, _) => CancelAndClose());
        closeButton.Width = 28;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.ToolTip = Loc.T("Assist.Common.CloseEsc");
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.VerticalAlignment = VerticalAlignment.Center;
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

        var labelBlock = EditorTheme.FieldLabel(label);
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(box);
        var hintBlock = EditorTheme.Muted(hint);
        hintBlock.FontSize = 11;
        hintBlock.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(hintBlock);

        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        return row;
    }

    private static TextBox CreateNumberBox(string value) => new() { Text = value };

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
