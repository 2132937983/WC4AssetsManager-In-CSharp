using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WC4MapEditor.GUI.Views.Assist;

public sealed class RiverSettingWindow : Window
{
    private readonly double _hexCenterX = 200;
    private readonly double _hexCenterY = 150;
    private readonly double _hexRadius = 80;
    private readonly double _edgeButtonRadius = 20;

    // 六边形与河流是"状态可视化"，颜色取主题语义色：未选 = 选中底色，悬停 = 强调色，已有河流 = 成功色，河流线 = 信息色
    private readonly Brush _buttonBrush = EditorTheme.Selection;
    private readonly Brush _buttonHoverBrush = EditorTheme.Accent;
    private readonly Brush _activeButtonBrush = EditorTheme.Success;
    private readonly Brush _riverBrush = EditorTheme.Info;

    private readonly List<Border> _edgeButtons = new();
    private readonly bool[] _riverEdges = new bool[6];
    private Canvas? _hexCanvas;

    private bool _isClosing;
    private byte _resultRiverValue;
    private bool _confirmed;

    public byte RiverValue => _resultRiverValue;
    public bool Confirmed => _confirmed;

    public RiverSettingWindow(byte initialRiverValue = 0)
    {
        EditorTheme.Apply(this);
        Title = Loc.T("Assist.River.Title");
        Width = 400;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        // 解析初始河流状态
        for (int i = 0; i < 6; i++)
            _riverEdges[i] = (initialRiverValue & (1 << i)) != 0;

        Content = CreateWindowContent();
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

    public void PlayFadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var sb = new Storyboard();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeOut);
        sb.Completed += (_, _) => Close();
        sb.Begin();
    }

    private FrameworkElement CreateWindowContent()
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
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 标题栏
        var titleBar = new Grid();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var titleLabel = EditorTheme.Title(Loc.T("Assist.River.Title"));
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

        // 标题栏拖动
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        // 内容面板
        var contentPanel = new Grid();
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(contentPanel, 1);

        // 六边形 Canvas
        _hexCanvas = new Canvas
        {
            Width = 400,
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_hexCanvas, 0);

        DrawHexagon(_hexCanvas);
        DrawRiverEdges(_hexCanvas);
        DrawEdgeButtons(_hexCanvas);

        contentPanel.Children.Add(_hexCanvas);

        // 按钮面板
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(buttonPanel, 1);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.Width = 200;
        cancelButton.Margin = new Thickness(0, 3, 0, 3);
        buttonPanel.Children.Add(cancelButton);

        var doneButton = EditorTheme.PrimaryButton(Loc.T("Assist.Common.Done"), (_, _) => ConfirmAndClose());
        doneButton.Width = 200;
        doneButton.Margin = new Thickness(0, 3, 0, 3);
        buttonPanel.Children.Add(doneButton);

        contentPanel.Children.Add(buttonPanel);
        mainGrid.Children.Add(contentPanel);

        outerBorder.Child = mainGrid;
        return outerBorder;
    }

    private void DrawHexagon(Canvas canvas)
    {
        var points = new PointCollection();
        for (int i = 0; i < 6; i++)
        {
            double angle = i * (2 * Math.PI / 6) - Math.PI / 3;
            double x = _hexCenterX + _hexRadius * Math.Cos(angle);
            double y = _hexCenterY + _hexRadius * Math.Sin(angle);
            points.Add(new Point(x, y));
        }

        var hexagon = new Polygon
        {
            Points = points,
            Fill = EditorTheme.Bg3,
            Stroke = EditorTheme.BorderStrong,
            StrokeThickness = 2
        };
        canvas.Children.Add(hexagon);
    }

    private void DrawRiverEdges(Canvas canvas)
    {
        for (int i = 0; i < 6; i++)
        {
            if (!_riverEdges[i]) continue;

            double angle1 = i * (2 * Math.PI / 6) - 2 * Math.PI / 3;
            double angle2 = (i + 1) * (2 * Math.PI / 6) - 2 * Math.PI / 3;

            double x1 = _hexCenterX + _hexRadius * Math.Cos(angle1);
            double y1 = _hexCenterY + _hexRadius * Math.Sin(angle1);
            double x2 = _hexCenterX + _hexRadius * Math.Cos(angle2);
            double y2 = _hexCenterY + _hexRadius * Math.Sin(angle2);

            var riverLine = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = _riverBrush,
                StrokeThickness = 4
            };
            canvas.Children.Add(riverLine);
        }
    }

    private void DrawEdgeButtons(Canvas canvas)
    {
        _edgeButtons.Clear();

        for (int i = 0; i < 6; i++)
        {
            int edgeIndex = i;
            double angle = i * (2 * Math.PI / 6) - Math.PI / 2;
            double x = _hexCenterX + _hexRadius * Math.Cos(angle);
            double y = _hexCenterY + _hexRadius * Math.Sin(angle);

            var button = new Border
            {
                Width = _edgeButtonRadius * 2,
                Height = _edgeButtonRadius * 2,
                Background = _riverEdges[i] ? _activeButtonBrush : _buttonBrush,
                BorderBrush = EditorTheme.BorderStrong,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(_edgeButtonRadius),
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(button, x - _edgeButtonRadius);
            Canvas.SetTop(button, y - _edgeButtonRadius);

            var textBlock = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 12,
                Foreground = EditorTheme.Text,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Child = textBlock;

            button.MouseEnter += (_, _) =>
            {
                if (!_riverEdges[edgeIndex])
                    button.Background = _buttonHoverBrush;
            };
            button.MouseLeave += (_, _) =>
            {
                if (!_riverEdges[edgeIndex])
                    button.Background = _buttonBrush;
            };
            button.MouseLeftButtonDown += (_, _) => ToggleRiverEdge(edgeIndex);

            _edgeButtons.Add(button);
            canvas.Children.Add(button);
        }
    }

    private void ToggleRiverEdge(int edgeIndex)
    {
        _riverEdges[edgeIndex] = !_riverEdges[edgeIndex];
        UpdateEdgeButtonAppearance(edgeIndex);
        RedrawRiverEdges();
    }

    private void UpdateEdgeButtonAppearance(int edgeIndex)
    {
        if (edgeIndex < 0 || edgeIndex >= _edgeButtons.Count) return;
        _edgeButtons[edgeIndex].Background = _riverEdges[edgeIndex] ? _activeButtonBrush : _buttonBrush;
    }

    private void RedrawRiverEdges()
    {
        if (_hexCanvas == null) return;

        // 清除河流线，保留六边形和按钮
        var linesToRemove = _hexCanvas.Children.OfType<Line>().Cast<UIElement>().ToList();
        foreach (var line in linesToRemove)
            _hexCanvas.Children.Remove(line);

        DrawRiverEdges(_hexCanvas);
    }

    private void ConfirmAndClose()
    {
        _resultRiverValue = 0;
        for (int i = 0; i < 6; i++)
        {
            if (_riverEdges[i])
                _resultRiverValue |= (byte)(1 << i);
        }
        _confirmed = true;
        PlayFadeOutAndClose();
    }

    private void CancelAndClose()
    {
        _confirmed = false;
        PlayFadeOutAndClose();
    }
}
