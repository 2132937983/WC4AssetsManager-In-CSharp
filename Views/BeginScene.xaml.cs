using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WC4MapEditor.Views;

public partial class BeginScene : UserControl
{
    private readonly MainWindow _window;
    private Grid _mainGrid = null!;
    private StackPanel _navPanel = null!;

    private Button _campaignButton = null!;
    private Button _newCampaignButton = null!;
    private Button _conquestButton = null!;
    private Button _newConquestButton = null!;
    private Button _mapButton = null!;
    private Button _newMapButton = null!;
    private Button _assetButton = null!;
    private Button _settingsButton = null!;
    private Button _checkDataButton = null!;

    private bool _isNewCampaignVisible;
    private bool _isNewConquestVisible;
    private bool _isNewMapVisible;
    private bool _isNavAnimating;

    private DispatcherTimer? _bgTimer;
    private ImageBrush _bgBrush1 = null!;
    private ImageBrush _bgBrush2 = null!;
    private int _bgIndex;
    private bool _useFirstBrush = true;
    private Border _bgBorder1 = null!;
    private Border _bgBorder2 = null!;

    public BeginScene(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        SetupUI();
        InitializeBackgroundSlideshow();
        Unloaded += BeginScene_Unloaded;
        _mainGrid.MouseLeftButtonDown += Grid_MouseLeftButtonDown;
    }

    private void SetupUI()
    {
        ConfigManager.Instance.Initialize();

        _mainGrid = new Grid();

        // Row definitions: title bar (40) | content (*) | bottom bar (100)
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });

        // Background borders for crossfade
        _bgBorder1 = new Border();
        _bgBorder2 = new Border();
        Grid.SetRowSpan(_bgBorder1, 3);
        Grid.SetRowSpan(_bgBorder2, 3);
        _bgBrush1 = new ImageBrush { Stretch = Stretch.UniformToFill };
        _bgBrush2 = new ImageBrush { Stretch = Stretch.UniformToFill };
        _bgBorder1.Background = _bgBrush1;
        _bgBorder2.Background = _bgBrush2;
        _bgBorder2.Opacity = 0;
        _mainGrid.Children.Add(_bgBorder1);
        _mainGrid.Children.Add(_bgBorder2);

        // Title bar
        var titleBar = new Grid { Background = Brushes.Transparent };
        Grid.SetRow(titleBar, 0);

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 0, 0)
        };

        var windowTitle = new TextBlock
        {
            Text = ConfigManager.Instance.GetText("TitleBarText", "WC4 Map Editor"),
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };
        titlePanel.Children.Add(windowTitle);

        titlePanel.Children.Add(new TextBlock
        {
            Text = "  |  ",
            FontSize = 12,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        });

        var wordCloudText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 600,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };
        titlePanel.Children.Add(wordCloudText);
        titleBar.Children.Add(titlePanel);
        _mainGrid.Children.Add(titleBar);

        StartWordCloudRotation(wordCloudText);

        // Circle buttons (right side)
        var circlePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 20, 37, 0)
        };
        Grid.SetRow(circlePanel, 0);
        Grid.SetRowSpan(circlePanel, 2);
        Panel.SetZIndex(circlePanel, 100);

        circlePanel.Children.Add(CreateCircleButton("➖", (_, _) => _window.MinimizeWindow()));
        circlePanel.Children.Add(CreateCircleButton("⬜", (_, _) => _window.ToggleMaximize()));
        circlePanel.Children.Add(CreateCircleButton("✕", (_, _) => _window.CloseApplication(), true));
        circlePanel.Children.Add(new Rectangle { Height = 10, Fill = Brushes.Transparent });
        circlePanel.Children.Add(CreateCircleButton("❚❚", MusicToggleButton_Click));

        _mainGrid.Children.Add(circlePanel);

        // Center content - big title
        var mainContent = new Grid();
        Grid.SetRow(mainContent, 1);

        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(50, 0, 0, 20)
        };

        var titleLabel = new TextBlock
        {
            Text = ConfigManager.Instance.GetText("WindowTitleText", "WC4 EDITOR"),
            FontSize = 72,
            FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(Colors.White),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 }
        };

        var versionLabel = new TextBlock
        {
            Text = ConfigManager.Instance.GetText("VersionText", "v5.0 开发版"),
            FontSize = 18,
            Foreground = Brushes.White,
            Margin = new Thickness(5, 10, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        titleStack.Children.Add(titleLabel);
        titleStack.Children.Add(versionLabel);
        mainContent.Children.Add(titleStack);
        _mainGrid.Children.Add(mainContent);

        // Bottom navigation bar
        var bottomBar = new Grid();
        Grid.SetRow(bottomBar, 2);
        bottomBar.Margin = new Thickness(50, 0, 20, 0);

        _navPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _campaignButton = CreateNavButton(ConfigManager.Instance.GetText("CampaignButtonText", "战役"), CampaignButton_Click);
        _newCampaignButton = CreateNavButton(ConfigManager.Instance.GetText("NewCampaignButtonText", "新战役"), NewCampaignButton_Click);
        _conquestButton = CreateNavButton(ConfigManager.Instance.GetText("ConquestButtonText", "征服"), ConquestButton_Click);
        _newConquestButton = CreateNavButton(ConfigManager.Instance.GetText("NewConquestButtonText", "新征服"), NewConquestButton_Click);
        _mapButton = CreateNavButton(ConfigManager.Instance.GetText("TerrainsMapButtonText", "征服地图"), MapButton_Click);
        _newMapButton = CreateNavButton(ConfigManager.Instance.GetText("NewTerrainsMapButtonText", "新地图"), NewMapButton_Click);
        _assetButton = CreateNavButton("资源管理器", AssetButton_Click);
        _checkDataButton = CreateNavButton("检查数据", CheckDataButton_Click);
        _settingsButton = CreateNavButton(ConfigManager.Instance.GetText("SettingsButtonText", "设置"), SettingsButton_Click);

        HideButtonImmediately(_newCampaignButton);
        HideButtonImmediately(_newConquestButton);
        HideButtonImmediately(_newMapButton);

        _navPanel.Children.Add(_campaignButton);
        _navPanel.Children.Add(_newCampaignButton);
        _navPanel.Children.Add(_conquestButton);
        _navPanel.Children.Add(_newConquestButton);
        _navPanel.Children.Add(_mapButton);
        _navPanel.Children.Add(_newMapButton);
        _navPanel.Children.Add(_assetButton);
        _navPanel.Children.Add(_checkDataButton);
        _navPanel.Children.Add(_settingsButton);

        var recommendBtn = CreateCircleButton("👍", RecommendButton_Click);
        recommendBtn.Width = 75;
        recommendBtn.Height = 75;
        recommendBtn.HorizontalAlignment = HorizontalAlignment.Right;
        recommendBtn.VerticalAlignment = VerticalAlignment.Center;
        if (recommendBtn.Content is TextBlock rtb) rtb.FontSize = 36;
        bottomBar.Children.Add(_navPanel);
        bottomBar.Children.Add(recommendBtn);
        _mainGrid.Children.Add(bottomBar);

        Content = _mainGrid;
    }

    private void HideButtonImmediately(Button btn)
    {
        btn.Opacity = 0;
        btn.IsHitTestVisible = false;
        btn.Width = 0;
        btn.Margin = new Thickness(0);
    }

    private void BeginScene_Unloaded(object? sender, RoutedEventArgs e) => _bgTimer?.Stop();

    private void Grid_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) _window.ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) _window.DragMove();
    }

    private void StartWordCloudRotation(TextBlock textBlock)
    {
        var wcConfig = ConfigManager.Instance.GetWordCloudConfig();
        var tokens = wcConfig.Tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        if (tokens.Length == 0) return;

        int index = 0;
        textBlock.Text = tokens[0];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (s, e) =>
        {
            index = (index + 1) % tokens.Length;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s2, e2) =>
            {
                textBlock.Text = tokens[index];
                textBlock.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            };
            textBlock.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        timer.Start();
    }

    private void InitializeBackgroundSlideshow()
    {
        string bgPath = ConfigManager.Instance.GetBackgroundPath();
        if (!Directory.Exists(bgPath)) return;

        var images = new List<string>();
        foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            images.AddRange(Directory.GetFiles(bgPath, "*" + ext));

        if (images.Count == 0) return;

        try { _bgBrush1.ImageSource = new BitmapImage(new Uri(images[0], UriKind.Absolute)); } catch { }
        if (images.Count > 1)
            try { _bgBrush2.ImageSource = new BitmapImage(new Uri(images[1], UriKind.Absolute)); } catch { }

        _bgIndex = 0;
        _useFirstBrush = true;

        _bgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _bgTimer.Tick += (s, e) =>
        {
            _bgIndex = (_bgIndex + 1) % images.Count;
            var nextUri = new Uri(images[_bgIndex], UriKind.Absolute);
            if (_useFirstBrush)
            {
                try { _bgBrush2.ImageSource = new BitmapImage(nextUri); } catch { }
                CrossfadeBackgrounds(true);
            }
            else
            {
                try { _bgBrush1.ImageSource = new BitmapImage(nextUri); } catch { }
                CrossfadeBackgrounds(false);
            }
            _useFirstBrush = !_useFirstBrush;
        };
        _bgTimer.Start();
    }

    private void CrossfadeBackgrounds(bool toSecond)
    {
        var target = toSecond ? _bgBorder2 : _bgBorder1;
        var source = toSecond ? _bgBorder1 : _bgBorder2;
        target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5)));
        source.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.5)));
    }

    private Button CreateCircleButton(string content, RoutedEventHandler handler, bool isClose = false)
    {
        var tb = new TextBlock
        {
            Text = content,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        var btn = new Button
        {
            Width = 42, Height = 42, Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 9),
            Cursor = Cursors.Hand, Content = tb,
            Template = CreateCircleButtonTemplate(isClose)
        };

        btn.MouseEnter += (s, e) =>
        {
            if (btn.Content is TextBlock ttb) { ttb.Foreground = isClose ? Brushes.White : Brushes.Black; ttb.Effect = null; }
        };
        btn.MouseLeave += (s, e) =>
        {
            if (btn.Content is TextBlock ttb) { ttb.Foreground = Brushes.White; ttb.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }; }
        };
        btn.Click += handler;
        return btn;
    }

    private ControlTemplate CreateCircleButtonTemplate(bool isClose)
    {
        var f = new FrameworkElementFactory(typeof(Border));
        f.Name = "border";
        f.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        f.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        f.AppendChild(cp);
        return new ControlTemplate(typeof(Button)) { VisualTree = f };
    }

    private Button CreateNavButton(string text, RoutedEventHandler handler)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = Brushes.White, FontSize = 16,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        var btn = new Button
        {
            Content = tb, BorderThickness = new Thickness(0), FontSize = 16,
            Margin = new Thickness(0, 0, 25, 0), Cursor = Cursors.Hand,
            Padding = new Thickness(20, 10, 20, 10),
            Template = CreateNavButtonTemplate()
        };

        btn.MouseEnter += (s, e) =>
        {
            tb.Foreground = Brushes.Black;
            tb.Effect = null;
        };
        btn.MouseLeave += (s, e) =>
        {
            tb.Foreground = Brushes.White;
            tb.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black };
        };
        btn.Click += handler;
        return btn;
    }

    private ControlTemplate CreateNavButtonTemplate()
    {
        var f = new FrameworkElementFactory(typeof(Border));
        f.Name = "border";
        f.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        f.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        f.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        f.AppendChild(cp);
        return new ControlTemplate(typeof(Button)) { VisualTree = f };
    }

    private void MusicToggleButton_Click(object sender, RoutedEventArgs e) { }
    private void CampaignButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new StageRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void NewCampaignButton_Click(object sender, RoutedEventArgs e) { }
    private void ConquestButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ConquestRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void NewConquestButton_Click(object sender, RoutedEventArgs e) { }
    private void MapButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new MapRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void NewMapButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new MapRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void AssetButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new AssetBrowserScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void CheckDataButton_Click(object sender, RoutedEventArgs e) { }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { }
    private void RecommendButton_Click(object sender, RoutedEventArgs e) { }
}
