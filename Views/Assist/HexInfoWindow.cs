using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Models;

namespace WC4MapEditor.Views.Assist;

public sealed class HexInfoWindow : Window
{
    private readonly SolidColorBrush _textBrush;
    private readonly SolidColorBrush _borderBrush;
    private readonly Color _themeColor;

    private Border _mainBorder = null!;
    private StackPanel _contentStack = null!;
    private ScrollViewer _scrollViewer = null!;

    private int _currentCol = -1;
    private int _currentRow = -1;
    private HexInfoDisplayMode _currentMode = HexInfoDisplayMode.Default;
    private bool _isClosing;

    private const int WindowWidth = 320;

    public HexInfoWindow()
    {
        _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

        double brightness = (_themeColor.R * 0.299 + _themeColor.G * 0.587 + _themeColor.B * 0.114) / 255;
        _textBrush = brightness > 0.5 ? Brushes.Black : Brushes.White;
        _borderBrush = new SolidColorBrush(brightness > 0.5
            ? Color.FromArgb(100, 0, 0, 0)
            : Color.FromArgb(100, 255, 255, 255));

        InitializeWindow();
    }

    private void InitializeWindow()
    {
        Title = "格子信息";
        Width = WindowWidth;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
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
        sb.Completed += (_, _) => Close();
        sb.Begin();
    }

    private FrameworkElement CreateWindowContent()
    {
        var outerGrid = new Grid();

        _mainBorder = new Border
        {
            Width = WindowWidth - 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(230, _themeColor.R, _themeColor.G, _themeColor.B)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10)
        };

        var mainStack = new StackPanel();

        var titleBar = new Border
        {
            Height = 30,
            Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "格子信息 (按住拖动)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(150,
                    _textBrush.Color.R, _textBrush.Color.G, _textBrush.Color.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        titleBar.MouseLeftButtonUp += TitleBar_MouseLeftButtonUp;
        titleBar.MouseMove += TitleBar_MouseMove;

        mainStack.Children.Add(titleBar);

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            MaxHeight = 450
        };

        _contentStack = new StackPanel { Margin = new Thickness(15) };

        _scrollViewer.Content = _contentStack;
        mainStack.Children.Add(_scrollViewer);
        _mainBorder.Child = mainStack;
        outerGrid.Children.Add(_mainBorder);

        return outerGrid;
    }

    public void SetDisplayMode(HexInfoDisplayMode mode)
    {
        if (_currentMode != mode)
            _currentMode = mode;
    }

    public void UpdateHexInfo(int col, int row, HexInfoDisplayMode mode = HexInfoDisplayMode.Default)
    {
        if (col == _currentCol && row == _currentRow && mode == _currentMode) return;

        _currentCol = col;
        _currentRow = row;
        _currentMode = mode;
        Title = $"格子信息 ({col}, {row})";

        var info = HexInfoService.Instance.GetHexCellInfo(col, row, mode);
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

        var titleText = new TextBlock
        {
            Text = info.GetModeTitle(),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 0, 0, 15)
        };
        _contentStack.Children.Add(titleText);

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
                AddSectionHeader("16进制数据");
                AddHexDumpText(HexCellInfo.FormatHexDump(section.RawData));
            }
        }
    }

    private void AddSectionHeader(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 10, 0, 5)
        };
        _contentStack.Children.Add(header);
    }

    private void AddInfoRow(string label, string value)
    {
        var panel = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label + ":",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(180,
                _textBrush.Color.R, _textBrush.Color.G, _textBrush.Color.B)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelText, 0);
        panel.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textBrush,
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
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromArgb(200,
                _textBrush.Color.R, _textBrush.Color.G, _textBrush.Color.B)),
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
            Background = new SolidColorBrush(Color.FromArgb(50,
                _textBrush.Color.R, _textBrush.Color.G, _textBrush.Color.B)),
            Margin = new Thickness(0, 10, 0, 10)
        };
        _contentStack.Children.Add(separator);
    }

    public void ClearHexInfo()
    {
        _currentCol = -1;
        _currentRow = -1;

        _contentStack.Children.Clear();
        var titleText = new TextBlock
        {
            Text = "格子信息",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 0, 0, 15)
        };
        _contentStack.Children.Add(titleText);
        AddInfoRow("坐标", "-");
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