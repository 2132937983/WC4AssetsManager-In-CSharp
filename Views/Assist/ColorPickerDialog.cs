using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 颜色选择对话框 - 对齐 VB 版 Assist/ColorPickerDialog。
/// <para>
/// 组成：HSV 取色画布（饱和度×明度）+ 色相滑块 + 颜色预览 + RGB/Hex 输入 + 预设色板。
/// 确认后通过 <see cref="SelectedColor"/> 返回所选颜色。
/// </para>
/// </summary>
public sealed class ColorPickerDialog : Window
{
    private static readonly Color[] PresetColors =
    {
        Colors.White, Color.FromRgb(230, 230, 230), Color.FromRgb(170, 170, 170), Color.FromRgb(90, 90, 90),
        Colors.Black, Color.FromRgb(255, 0, 0), Color.FromRgb(255, 128, 0), Color.FromRgb(255, 255, 0),
        Color.FromRgb(128, 255, 0), Color.FromRgb(0, 200, 0), Color.FromRgb(0, 200, 200), Color.FromRgb(0, 128, 255),
        Color.FromRgb(0, 0, 255), Color.FromRgb(128, 0, 255), Color.FromRgb(255, 0, 255), Color.FromRgb(255, 0, 128),
        Color.FromRgb(250, 220, 150), Color.FromRgb(200, 170, 120), Color.FromRgb(150, 110, 70), Color.FromRgb(90, 60, 30),
    };

    private readonly Grid _canvasHost = new();
    private readonly Rectangle _canvasBase = new();
    private readonly Rectangle _canvasShade = new();
    private readonly Ellipse _canvasCursor = new();
    private readonly Grid _hueHost = new();
    private readonly Rectangle _hueBar = new();
    private readonly Rectangle _hueCursor = new();
    private readonly Border _preview = new();
    private readonly TextBox _rBox = new();
    private readonly TextBox _gBox = new();
    private readonly TextBox _bBox = new();
    private readonly TextBox _hexBox = new();

    private const double CanvasSize = 240;
    private const double HueBarHeight = 22;

    private double _hue;
    private double _saturation;
    private double _value = 1;
    private bool _updating;
    private bool _draggingCanvas;
    private bool _draggingHue;

    /// <summary>用户确认的颜色</summary>
    public Color SelectedColor { get; private set; } = Colors.White;

    public ColorPickerDialog(Color initial)
    {
        Title = "选择颜色";
        Width = 470;
        Height = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 12;

        Content = CreateUI();
        SyncUiFromColor(initial);

        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>以模态方式选择颜色，取消返回 null</summary>
    public static Color? Pick(Window? owner, Color initial)
    {
        var dialog = new ColorPickerDialog(initial);
        if (owner != null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog.SelectedColor : null;
    }

    // ------------------------------------------------------------------ UI

    private FrameworkElement CreateUI()
    {
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(10),
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

        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 14) };
        root.Children.Add(CreateTitleBar());
        root.Children.Add(CreateCanvas());
        root.Children.Add(CreateHueBar());
        root.Children.Add(CreatePreviewRow());
        root.Children.Add(CreateInputRow());
        root.Children.Add(CreatePresetPanel());
        root.Children.Add(CreateButtonBar());

        outer.Child = root;
        return outer;
    }

    private FrameworkElement CreateTitleBar()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "选择颜色",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var close = new Button
        {
            Content = "✕",
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand
        };
        close.Click += (_, _) => CancelAndClose();
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);

        // 允许拖动窗口
        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        return grid;
    }

    private FrameworkElement CreateCanvas()
    {
        _canvasHost.Width = CanvasSize;
        _canvasHost.Height = CanvasSize / 1.6;
        _canvasHost.HorizontalAlignment = HorizontalAlignment.Center;
        _canvasHost.ClipToBounds = true;
        _canvasHost.Cursor = Cursors.Cross;

        // 底层：白 → 纯色（横向）
        _canvasBase.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvasBase.VerticalAlignment = VerticalAlignment.Stretch;
        _canvasHost.Children.Add(_canvasBase);

        // 上层：透明 → 黑（纵向）
        _canvasShade.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvasShade.VerticalAlignment = VerticalAlignment.Stretch;
        _canvasShade.Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90);
        _canvasHost.Children.Add(_canvasShade);

        _canvasCursor.Width = 10;
        _canvasCursor.Height = 10;
        _canvasCursor.Stroke = Brushes.White;
        _canvasCursor.StrokeThickness = 2;
        _canvasCursor.HorizontalAlignment = HorizontalAlignment.Left;
        _canvasCursor.VerticalAlignment = VerticalAlignment.Top;
        _canvasCursor.IsHitTestVisible = false;
        _canvasHost.Children.Add(_canvasCursor);

        _canvasHost.MouseLeftButtonDown += (_, e) =>
        {
            _draggingCanvas = true;
            _canvasHost.CaptureMouse();
            UpdateFromCanvas(e.GetPosition(_canvasHost));
        };
        _canvasHost.MouseMove += (_, e) =>
        {
            if (_draggingCanvas) UpdateFromCanvas(e.GetPosition(_canvasHost));
        };
        _canvasHost.MouseLeftButtonUp += (_, _) =>
        {
            _draggingCanvas = false;
            _canvasHost.ReleaseMouseCapture();
        };

        return _canvasHost;
    }

    private FrameworkElement CreateHueBar()
    {
        var host = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = "色相",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 0);
        host.Children.Add(label);

        _hueHost.Height = HueBarHeight;
        _hueHost.ClipToBounds = true;
        _hueHost.Cursor = Cursors.Hand;

        _hueBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _hueBar.VerticalAlignment = VerticalAlignment.Stretch;
        _hueBar.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Colors.Red, 0.0),
                new GradientStop(Colors.Yellow, 1.0 / 6),
                new GradientStop(Colors.Lime, 2.0 / 6),
                new GradientStop(Colors.Cyan, 3.0 / 6),
                new GradientStop(Colors.Blue, 4.0 / 6),
                new GradientStop(Colors.Magenta, 5.0 / 6),
                new GradientStop(Colors.Red, 1.0),
            }
        };
        _hueHost.Children.Add(_hueBar);

        _hueCursor.Width = 4;
        _hueCursor.Fill = Brushes.White;
        _hueCursor.HorizontalAlignment = HorizontalAlignment.Left;
        _hueCursor.IsHitTestVisible = false;
        _hueHost.Children.Add(_hueCursor);

        _hueHost.MouseLeftButtonDown += (_, e) =>
        {
            _draggingHue = true;
            _hueHost.CaptureMouse();
            UpdateFromHueBar(e.GetPosition(_hueHost));
        };
        _hueHost.MouseMove += (_, e) =>
        {
            if (_draggingHue) UpdateFromHueBar(e.GetPosition(_hueHost));
        };
        _hueHost.MouseLeftButtonUp += (_, _) =>
        {
            _draggingHue = false;
            _hueHost.ReleaseMouseCapture();
        };

        Grid.SetColumn(_hueHost, 1);
        host.Children.Add(_hueHost);

        return host;
    }

    private FrameworkElement CreatePreviewRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = "预览",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        _preview.Height = 28;
        _preview.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        _preview.BorderThickness = new Thickness(1);
        Grid.SetColumn(_preview, 1);
        grid.Children.Add(_preview);

        return grid;
    }

    private FrameworkElement CreateInputRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddLabeledInput(grid, 0, "R", _rBox);
        AddLabeledInput(grid, 1, "G", _gBox);
        AddLabeledInput(grid, 2, "B", _bBox);
        AddLabeledInput(grid, 3, "Hex", _hexBox);

        void Commit()
        {
            if (_updating) return;

            if (byte.TryParse(_rBox.Text.Trim(), out byte r)
                && byte.TryParse(_gBox.Text.Trim(), out byte g)
                && byte.TryParse(_bBox.Text.Trim(), out byte b))
            {
                SyncUiFromColor(Color.FromRgb(r, g, b));
            }
        }

        foreach (var box in new[] { _rBox, _gBox, _bBox })
        {
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };
        }

        _hexBox.LostFocus += (_, _) => CommitHex();
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitHex();
        };

        return grid;
    }

    private static void AddLabeledInput(Grid grid, int column, string label, TextBox box)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (column > 0) panel.Margin = new Thickness(6, 0, 0, 0);

        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });

        box.Width = 52;
        box.Height = 24;
        box.Background = new SolidColorBrush(Color.FromRgb(70, 70, 73));
        box.Foreground = Brushes.White;
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        box.BorderThickness = new Thickness(1);
        box.Padding = new Thickness(4, 1, 4, 1);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        panel.Children.Add(box);

        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private FrameworkElement CreatePresetPanel()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

        foreach (var color in PresetColors)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 4),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            };
            swatch.MouseLeftButtonDown += (_, _) => SyncUiFromColor(color);
            wrap.Children.Add(swatch);
        }

        return wrap;
    }

    private FrameworkElement CreateButtonBar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateButton("确定", Color.FromRgb(0, 122, 204), (_, _) => ConfirmAndClose()));
        panel.Children.Add(CreateButton("取消", Color.FromRgb(80, 80, 80), (_, _) => CancelAndClose()));

        return panel;
    }

    private static Button CreateButton(string content, Color background, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = content,
            Width = 80,
            Height = 30,
            Background = new SolidColorBrush(background),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0)
        };
        button.Click += onClick;

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(100, 149, 237))));
        template.Triggers.Add(hover);
        button.Template = template;

        return button;
    }

    // -------------------------------------------------------------- 交互

    private void UpdateFromCanvas(Point position)
    {
        double w = _canvasHost.ActualWidth;
        double h = _canvasHost.ActualHeight;
        if (w <= 0 || h <= 0) return;

        _saturation = Math.Clamp(position.X / w, 0, 1);
        _value = 1 - Math.Clamp(position.Y / h, 0, 1);
        ApplyColor(HsvToColor(_hue, _saturation, _value));
    }

    private void UpdateFromHueBar(Point position)
    {
        double w = _hueHost.ActualWidth;
        if (w <= 0) return;

        _hue = Math.Clamp(position.X / w, 0, 1) * 360;
        ApplyColor(HsvToColor(_hue, _saturation, _value));
    }

    /// <summary>按当前 HSV 刷新取色区底色与界面</summary>
    private void ApplyColor(Color color, bool updateInputs = true)
    {
        SelectedColor = color;

        var pure = HsvToColor(_hue, 1, 1);
        _canvasBase.Fill = new LinearGradientBrush(Colors.White, pure, 0);
        _preview.Background = new SolidColorBrush(color);

        Canvas.SetLeft(_canvasCursor, _saturation * _canvasHost.Width - _canvasCursor.Width / 2);
        Canvas.SetTop(_canvasCursor, (1 - _value) * _canvasHost.Height - _canvasCursor.Height / 2);

        _hueCursor.Height = HueBarHeight;
        Canvas.SetLeft(_hueCursor, _hue / 360 * _hueHost.ActualWidth - _hueCursor.Width / 2);

        if (!updateInputs) return;

        _updating = true;
        _rBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        _gBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        _bBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
        _hexBox.Text = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        _updating = false;
    }

    /// <summary>由外部颜色同步整个界面（H/S/V 与输入框）</summary>
    private void SyncUiFromColor(Color color)
    {
        var (h, s, v) = ColorToHsv(color);

        // 灰阶时保留原色相，避免跳变
        if (s > 0) _hue = h;
        _saturation = s;
        _value = v;

        ApplyColor(color);
    }

    private void CommitHex()
    {
        if (_updating) return;

        string text = _hexBox.Text.Trim().TrimStart('#');
        if (text.Length != 6) return;

        if (!byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)) return;
        if (!byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)) return;
        if (!byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)) return;

        SyncUiFromColor(Color.FromRgb(r, g, b));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CancelAndClose();
                e.Handled = true;
                break;
            case Key.Enter:
                ConfirmAndClose();
                e.Handled = true;
                break;
        }
    }

    private void ConfirmAndClose()
    {
        DialogResult = true;
        Close();
    }

    private void CancelAndClose()
    {
        DialogResult = false;
        Close();
    }

    // ---------------------------------------------------------- 颜色换算

    private static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) ColorToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }
}
