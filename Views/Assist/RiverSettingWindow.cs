using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WC4MapEditor.Views.Assist;

public sealed class RiverSettingWindow : Window
{
    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

    private readonly double _hexCenterX = 200;
    private readonly double _hexCenterY = 150;
    private readonly double _hexRadius = 80;
    private readonly double _edgeButtonRadius = 20;

    private readonly Color _buttonColor = Color.FromRgb(70, 130, 180);
    private readonly Color _buttonHoverColor = Color.FromRgb(100, 149, 237);
    private readonly Color _activeButtonColor = Color.FromRgb(50, 205, 50);
    private readonly Color _hexagonColor = Color.FromRgb(200, 200, 200);
    private readonly Color _riverColor = Color.FromRgb(30, 144, 255);

    private readonly List<Border> _edgeButtons = new();
    private readonly bool[] _riverEdges = new bool[6];

    private bool _isClosing;
    private byte _resultRiverValue;
    private bool _confirmed;

    public byte RiverValue => _resultRiverValue;
    public bool Confirmed => _confirmed;

    public RiverSettingWindow(byte initialRiverValue = 0)
    {
        Title = "设置河流";
        Width = 400;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 14;

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
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 标题栏
        var titleBar = new Grid();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var titleLabel = new Label
        {
            Content = "设置河流",
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
        var hexCanvas = new Canvas
        {
            Width = 400,
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(hexCanvas, 0);

        DrawHexagon(hexCanvas);
        DrawRiverEdges(hexCanvas);
        DrawEdgeButtons(hexCanvas);

        contentPanel.Children.Add(hexCanvas);

        // 按钮面板
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(buttonPanel, 1);

        var cancelButton = CreateStyledButton("取消", new SolidColorBrush(Color.FromRgb(80, 80, 80)));
        cancelButton.Click += (_, _) => CancelAndClose();
        buttonPanel.Children.Add(cancelButton);

        var doneButton = CreateStyledButton("完成", new SolidColorBrush(_buttonColor));
        doneButton.Click += (_, _) => ConfirmAndClose();
        buttonPanel.Children.Add(doneButton);

        contentPanel.Children.Add(buttonPanel);
        mainGrid.Children.Add(contentPanel);

        outerBorder.Child = mainGrid;
        return outerBorder;
    }

    private Button CreateStyledButton(string text, Brush background)
    {
        var btn = new Button
        {
            Content = text,
            Width = 200,
            Height = 36,
            Margin = new Thickness(0, 3, 0, 3),
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
            Fill = new SolidColorBrush(_hexagonColor),
            Stroke = new SolidColorBrush(Colors.Gray),
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
                Stroke = new SolidColorBrush(_riverColor),
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
                Background = new SolidColorBrush(_riverEdges[i] ? _activeButtonColor : _buttonColor),
                BorderBrush = new SolidColorBrush(Colors.DarkGray),
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
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Child = textBlock;

            button.MouseEnter += (_, _) =>
            {
                if (!_riverEdges[edgeIndex])
                    button.Background = new SolidColorBrush(_buttonHoverColor);
            };
            button.MouseLeave += (_, _) =>
            {
                if (!_riverEdges[edgeIndex])
                    button.Background = new SolidColorBrush(_buttonColor);
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
        _edgeButtons[edgeIndex].Background = new SolidColorBrush(
            _riverEdges[edgeIndex] ? _activeButtonColor : _buttonColor);
    }

    private void RedrawRiverEdges()
    {
        if (Content is not Border outerBorder) return;
        if (outerBorder.Child is not Grid mainGrid) return;

        Canvas? hexCanvas = null;
        foreach (var child in mainGrid.Children)
        {
            if (child is Grid contentPanel)
            {
                foreach (var c in contentPanel.Children)
                {
                    if (c is Canvas canvas)
                    {
                        hexCanvas = canvas;
                        break;
                    }
                }
            }
        }

        if (hexCanvas == null) return;

        // 清除河流线，保留六边形和按钮
        var linesToRemove = hexCanvas.Children.OfType<Line>().Cast<UIElement>().ToList();
        foreach (var line in linesToRemove)
            hexCanvas.Children.Remove(line);

        DrawRiverEdges(hexCanvas);
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