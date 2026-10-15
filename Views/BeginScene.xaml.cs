using System.Diagnostics;
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
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Views.Dialogs;

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
    private Button _generalButton = null!;
    private Button _countryButton = null!;
    private Button _checkDataButton = null!;

    private Button _recommendBtn = null!;
    private Button _navPrevButton = null!;
    private Button _navNextButton = null!;
    private int _navOffset;
    private int _navVisibleCount = int.MaxValue;
    private double _navTextWidth;
    private bool _recalculating;
    private bool _navAnimating;
    private Grid _bottomBar = null!;

    private Grid _campaignGroup = null!;
    private Grid _conquestGroup = null!;
    private Grid _mapGroup = null!;

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

        circlePanel.Children.Add(CreateCircleButton("AI", AiAssistantButton_Click));
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
        _bottomBar = new Grid();
        Grid.SetRow(_bottomBar, 2);
        _bottomBar.Margin = new Thickness(50, 0, 20, 0);

        _navPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform() };

        // 创建主按钮
        _campaignButton = CreateNavButton(ConfigManager.Instance.GetText("CampaignButtonText", "战役"), CampaignButton_Click);
        _conquestButton = CreateNavButton(ConfigManager.Instance.GetText("ConquestButtonText", "征服"), ConquestButton_Click);
        _mapButton = CreateNavButton(ConfigManager.Instance.GetText("TerrainsMapButtonText", "征服地图"), MapButton_Click);

        // 创建弹出按钮
        _newCampaignButton = CreatePopupButton(ConfigManager.Instance.GetText("NewCampaignButtonText", "新战役"), NewCampaignButton_Click);
        _newConquestButton = CreatePopupButton(ConfigManager.Instance.GetText("NewConquestButtonText", "新征服"), NewConquestButton_Click);
        _newMapButton = CreatePopupButton(ConfigManager.Instance.GetText("NewTerrainsMapButtonText", "新地图"), NewMapButton_Click);

        // 创建按钮组（主按钮 + 弹出按钮）
        _campaignGroup = CreateButtonGroup(_campaignButton, _newCampaignButton);
        _conquestGroup = CreateButtonGroup(_conquestButton, _newConquestButton);
        _mapGroup = CreateButtonGroup(_mapButton, _newMapButton);

        _assetButton = CreateNavButton(ConfigManager.Instance.GetText("AssetBrowserButtonText", "资源管理器"), AssetButton_Click);
        _generalButton = CreateNavButton(ConfigManager.Instance.GetText("GeneralButtonText", "将领编辑"), GeneralButton_Click);
        _countryButton = CreateNavButton(ConfigManager.Instance.GetText("CountryButtonText", "国家编辑"), CountryButton_Click);
        _checkDataButton = CreateNavButton(ConfigManager.Instance.GetText("CheckDataButtonText", "检查数据"), CheckDataButton_Click);
        _settingsButton = CreateNavButton(ConfigManager.Instance.GetText("SettingsButtonText", "设置"), SettingsButton_Click);

        _navPanel.Children.Add(_campaignGroup);
        _navPanel.Children.Add(_conquestGroup);
        _navPanel.Children.Add(_mapGroup);
        _navPanel.Children.Add(_assetButton);
        _navPanel.Children.Add(_generalButton);
        _navPanel.Children.Add(_countryButton);
        _navPanel.Children.Add(_checkDataButton);
        _navPanel.Children.Add(_settingsButton);
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("InGameTechButtonText", "局内科技"), InGameTechButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("EventEditorButtonText", "集团军事件编辑"), EventEditorButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("EventBuffButtonText", "事件的效果"), EventBuffButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("ConquerEventButtonText", "征服事件"), ConquerEventButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("SkillButtonText", "技能编辑"), SkillButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("ArmyBuffButtonText", "军队Buff"), ArmyBuffButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("ArmyButtonText", "兵种编辑"), ArmyButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("BuildingButtonText", "建筑与设施"), BuildingButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("ArmyGroupButtonText", "集团军与援军"), ArmyGroupButton_Click));
        _navPanel.Children.Add(CreateNavButton(ConfigManager.Instance.GetText("LayoutButtonText", "布局编辑器"), LayoutButton_Click));

        RegisterAiBridge();

        // 统一导航按钮宽度：取最长文本的期望宽度，保证全部按钮等宽且能容纳 setting 键值文本
        double navWidth = 0;
        foreach (UIElement child in _navPanel.Children)
            if (child is FrameworkElement fe)
            {
                fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                navWidth = Math.Max(navWidth, Math.Ceiling(fe.DesiredSize.Width));
            }
        _navTextWidth = navWidth;
        foreach (UIElement child in _navPanel.Children)
            if (child is FrameworkElement fe) fe.Width = navWidth;

        _recommendBtn = CreateCircleButton("👍", RecommendButton_Click);
        _recommendBtn.Width = 75;
        _recommendBtn.Height = 75;
        _recommendBtn.HorizontalAlignment = HorizontalAlignment.Right;
        _recommendBtn.VerticalAlignment = VerticalAlignment.Center;
        if (_recommendBtn.Content is TextBlock rtb) rtb.FontSize = 36;

        _navPrevButton = CreatePagerButton("◀", NavPrev_Click);
        _navNextButton = CreatePagerButton("▶", NavNext_Click);

        var navBarRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        navBarRow.Children.Add(_navPrevButton);
        navBarRow.Children.Add(_navPanel);
        navBarRow.Children.Add(_navNextButton);

        _bottomBar.Children.Add(navBarRow);
        _bottomBar.Children.Add(_recommendBtn);
        _mainGrid.Children.Add(_bottomBar);

        Loaded += (_, _) => Dispatcher.BeginInvoke(() => RecalculateNav(), DispatcherPriority.Loaded);
        _bottomBar.SizeChanged += (_, _) => RecalculateNav();

        Content = _mainGrid;
    }

    private Button CreatePopupButton(string text, RoutedEventHandler handler)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = 14,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        var btn = new Button
        {
            Content = tb,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(15, 8, 15, 8),
            Template = CreateNavButtonTemplate(),
            Opacity = 0,
            IsHitTestVisible = false
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

    private Grid CreateButtonGroup(Button mainButton, Button popupButton)
    {
        // 移除主按钮的外部 Margin，由 Grid 来统一控制间距
        mainButton.Margin = new Thickness(0);
        mainButton.HorizontalAlignment = HorizontalAlignment.Center;
        mainButton.VerticalAlignment = VerticalAlignment.Center;

        popupButton.HorizontalAlignment = HorizontalAlignment.Center;
        popupButton.VerticalAlignment = VerticalAlignment.Bottom;

        var group = new Grid
        {
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 主按钮在最底层
        group.Children.Add(mainButton);

        // 弹出按钮初始位置在主按钮上方（隐藏）
        // 计算主按钮的实际高度（包含Padding）
        double mainButtonHeight = mainButton.Padding.Top + mainButton.Padding.Bottom + 24; // 24是字体高度估算
        // 增加间距，让弹出按钮更高一些（主按钮高度 + 额外间距）
        popupButton.Margin = new Thickness(0, 0, 0, mainButtonHeight + 15);
        group.Children.Add(popupButton);

        bool isPopupVisible = false;

        // 鼠标进入主按钮时显示弹出按钮
        mainButton.MouseEnter += (s, e) =>
        {
            if (!isPopupVisible)
            {
                isPopupVisible = true;
                ShowPopupButton(popupButton);
            }
        };

        // 鼠标离开整个组时隐藏弹出按钮
        group.MouseLeave += (s, e) =>
        {
            if (isPopupVisible)
            {
                isPopupVisible = false;
                HidePopupButton(popupButton);
            }
        };

        return group;
    }

    private void ShowPopupButton(Button popupButton)
    {
        popupButton.IsHitTestVisible = true;

        // 向上滑出动画
        var translateTransform = popupButton.RenderTransform as TranslateTransform;
        if (translateTransform == null)
        {
            translateTransform = new TranslateTransform();
            popupButton.RenderTransform = translateTransform;
        }

        var slideUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        translateTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        popupButton.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void HidePopupButton(Button popupButton)
    {
        var translateTransform = popupButton.RenderTransform as TranslateTransform;
        if (translateTransform == null)
        {
            translateTransform = new TranslateTransform();
            popupButton.RenderTransform = translateTransform;
        }

        var slideDown = new DoubleAnimation(0, 20, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (s, e) =>
        {
            popupButton.IsHitTestVisible = false;
        };

        translateTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        popupButton.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void BeginScene_Unloaded(object? sender, RoutedEventArgs e) => _bgTimer?.Stop();

    private void Grid_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) _window.DragMove();
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
            Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
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
    private async void NewCampaignButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = MessageBox.Show("确定要创建新的战役文件吗？", "新建战役", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        using var widthDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入地图宽度（格子数）：",
            DefaultValue = "10",
            MinValue = 1
        };
        var widthStr = await widthDialog.ShowAsync();
        if (widthStr == null || !int.TryParse(widthStr, out int mapWidth) || mapWidth < 1)
        {
            if (widthStr != null) MessageBox.Show("请输入有效的地图宽度", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var heightDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入地图高度（格子数）：",
            DefaultValue = "10",
            MinValue = 1
        };
        var heightStr = await heightDialog.ShowAsync();
        if (heightStr == null || !int.TryParse(heightStr, out int mapHeight) || mapHeight < 1)
        {
            if (heightStr != null) MessageBox.Show("请输入有效的地图高度", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var legionDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入军团数量：",
            DefaultValue = "2",
            MinValue = 1
        };
        var legionStr = await legionDialog.ShowAsync();
        if (legionStr == null || !int.TryParse(legionStr, out int numLegions) || numLegions < 1)
        {
            if (legionStr != null) MessageBox.Show("请输入有效的军团数量", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var parser = new StageParser();
        if (!parser.CreateNew(mapWidth, mapHeight, numLegions))
        {
            MessageBox.Show("创建战役文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var saveResult = MessageBox.Show(
            $"已创建 {mapWidth}x{mapHeight} 的新战役，包含 {mapWidth * mapHeight} 个格子和 {numLegions} 个军团。\n\n是否保存新创建的战役文件？",
            "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (saveResult == MessageBoxResult.Yes)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "战役文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "保存战役文件",
                FileName = $"new_campaign_{mapWidth}x{mapHeight}.btl"
            };
            if (saveDialog.ShowDialog() == true)
            {
                if (parser.SaveData(saveDialog.FileName))
                {
                    MessageBox.Show($"战役文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new StageRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
                else
                {
                    MessageBox.Show("保存战役文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    private void ConquestButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ConquestRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private async void NewConquestButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = MessageBox.Show("确定要创建新的征服文件吗？", "新建征服", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        var mapDialog = new OpenFileDialog
        {
            Filter = "世界地图文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            Title = "选择地图文件（获取尺寸）"
        };
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var mapsFolder = System.IO.Path.Combine(baseDir, "Maps");
        if (Directory.Exists(mapsFolder)) mapDialog.InitialDirectory = mapsFolder;

        if (mapDialog.ShowDialog() != true) return;
        var selectedMapFilePath = mapDialog.FileName;

        var mapData = WorldParser.LoadFromFile(selectedMapFilePath);
        if (mapData == null)
        {
            MessageBox.Show("无法加载地图文件，请检查文件是否有效！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        int mapClipY = 2;
        int actualMapWidth = mapData.MapWidth;
        int actualMapHeight = mapData.MapHeight - 2 * mapClipY;

        int mapNumber = ExtractMapNumberFromFileName(System.IO.Path.GetFileName(selectedMapFilePath));
        if (mapNumber <= 0)
        {
            using var mapNumDialog = new SingleInputDialog(_window)
            {
                Title = "新建征服",
                Description = "无法从文件名识别地图编号，请手动输入地图编号 (1-255)：",
                DefaultValue = "1",
                MinValue = 1,
                MaxValue = 255
            };
            var mapNumStr = await mapNumDialog.ShowAsync();
            if (mapNumStr == null || !int.TryParse(mapNumStr, out mapNumber) || mapNumber < 1 || mapNumber > 255)
            {
                if (mapNumStr != null) MessageBox.Show("地图编号必须是1-255之间的整数！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        using var legionDialog = new SingleInputDialog(_window)
        {
            Title = "新建征服",
            Description = "请输入军团数量：",
            DefaultValue = "2",
            MinValue = 1
        };
        var legionStr = await legionDialog.ShowAsync();
        if (legionStr == null || !int.TryParse(legionStr, out int numLegions) || numLegions < 1)
        {
            if (legionStr != null) MessageBox.Show("请输入有效的军团数量", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var parser = new ConquestParser();
        if (!parser.CreateNew(actualMapWidth, actualMapHeight, numLegions, mapNumber))
        {
            MessageBox.Show("创建征服文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var mapFileName = System.IO.Path.GetFileNameWithoutExtension(selectedMapFilePath);
        var saveResult = MessageBox.Show(
            $"已创建基于 \"{mapFileName}\" 的新征服（{actualMapWidth}x{actualMapHeight}，地图编号 {mapNumber}，{numLegions} 个军团）。\n\n是否保存新创建的征服文件？",
            "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (saveResult == MessageBoxResult.Yes)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "征服文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "保存征服文件",
                FileName = $"conquest_{mapFileName}_{mapNumber}_{actualMapWidth}x{actualMapHeight}.btl"
            };
            if (saveDialog.ShowDialog() == true)
            {
                if (parser.SaveData(saveDialog.FileName))
                {
                    MessageBox.Show($"征服文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new ConquestRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
                else
                {
                    MessageBox.Show("保存征服文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    private void MapButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new MapRenderScene(_window);
        _window.SetCurrentScene(scene);
    }
    private async void NewMapButton_Click(object sender, RoutedEventArgs e)
    {
        using var inputDialog = new SingleInputDialog(_window)
        {
            Title = "创建新地图",
            Description = "请输入地图尺寸（格式：宽度x高度，如：100x100）：",
            DefaultValue = "100x100"
        };
        var sizeStr = await inputDialog.ShowAsync();
        if (sizeStr == null) return;

        try
        {
            var parts = sizeStr.Trim().Split('x', 'X', '*', ' ');
            if (parts.Length < 2)
            {
                MessageBox.Show("请输入正确的尺寸格式（如：100x100）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int width = int.Parse(parts[0].Trim());
            int height = int.Parse(parts[1].Trim());

            if (width < 10 || width > 500 || height < 10 || height > 500)
            {
                MessageBox.Show("地图尺寸必须在 10-500 之间", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var mapData = WorldParser.CreateNew(width, height);
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NewMap_{width}x{height}_{DateTime.Now:yyyyMMddHHmmss}.bin");
            WorldParser.SaveToFile(mapData, tempPath);

            var saveResult = MessageBox.Show(
                $"已创建 {width}x{height} 的新地图。\n\n是否保存地图文件？",
                "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (saveResult == MessageBoxResult.Yes)
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "地图文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                    Title = "保存地图文件",
                    FileName = $"new_map_{width}x{height}.bin"
                };
                if (saveDialog.ShowDialog() == true)
                {
                    File.Copy(tempPath, saveDialog.FileName, true);
                    MessageBox.Show($"地图文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new MapRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
            }
            else
            {
                var scene = new MapRenderScene(_window, tempPath);
                _window.SetCurrentScene(scene);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建新地图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int ExtractMapNumberFromFileName(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('_');
        foreach (var part in parts)
        {
            if (int.TryParse(part, out int num) && num > 0 && num <= 255)
                return num;
        }
        return 0;
    }
    private void AssetButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new AssetBrowserScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void GeneralButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new GeneralEditScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void CountryButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new CountryEditScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void CheckDataButton_Click(object sender, RoutedEventArgs e) { }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { }
    private void RecommendButton_Click(object sender, RoutedEventArgs e) { }

    private void EventEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ArmyGroupEventEditScene(_window);
        _window.SetCurrentScene(scene);
    }
    private void EventBuffButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new EventBuffEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void ConquerEventButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[BeginScene] ConquerEventButton_Click → 进入征服事件编辑场景");
        var scene = new ConquerEventEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void InGameTechButton_Click(object sender, RoutedEventArgs e)
    {
        // 局内科技复用科技树画布编辑器（CountryTechEditScene）。
        // 注：该场景当前读取 CountryTechSettings.json（国家科技）。
        // 若局内科技需编辑 TechnologySettings.json，其字段 schema 不同
        // （Level / NeedID / Position[col,row] / CostGold...），需另建解析器与场景。
        var scene = new CountryTechEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void SkillButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new SkillEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void ArmyBuffButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ArmyBuffEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void ArmyButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ArmyEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void BuildingButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new BuildingFacilityEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void ArmyGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new ArmyGroupEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    private void LayoutButton_Click(object sender, RoutedEventArgs e)
    {
        var scene = new LayoutEditScene(_window);
        _window.SetCurrentScene(scene);
    }

    // ============================================================= 导航栏翻页 =============================================================
    private Button CreatePagerButton(string text, RoutedEventHandler handler)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        var btn = new Button
        {
            Content = tb,
            BorderThickness = new Thickness(0),
            FontSize = 18,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(12, 10, 12, 10),
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

    private void NavPrev_Click(object sender, RoutedEventArgs e) => AnimateNavPage(_navOffset - 1);

    private void NavNext_Click(object sender, RoutedEventArgs e) => AnimateNavPage(_navOffset + 1);

    // 翻页滑动动画：整排按钮向点击方向滑出，切换后反向滑入
    private void AnimateNavPage(int targetOffset)
    {
        int total = _navPanel.Children.Count;
        targetOffset = Math.Max(0, Math.Min(targetOffset, Math.Max(0, total - _navVisibleCount)));
        if (_navAnimating || targetOffset == _navOffset) return;
        bool forward = targetOffset > _navOffset;
        _navAnimating = true;

        var tt = (TranslateTransform)_navPanel.RenderTransform!;
        double slide = forward ? -70 : 70;
        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var outX = new DoubleAnimation(0, slide, TimeSpan.FromMilliseconds(130)) { EasingFunction = easeIn };
        outX.Completed += (_, _) =>
        {
            _navOffset = targetOffset;
            ApplyNavVisibility();

            var inX = new DoubleAnimation(-slide, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = easeOut };
            inX.Completed += (_, _) =>
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                _navAnimating = false;
            };
            tt.BeginAnimation(TranslateTransform.XProperty, inX);
        };
        tt.BeginAnimation(TranslateTransform.XProperty, outX);
    }

    private void RecalculateNav()
    {
        if (_recalculating || _navPrevButton == null || _navNextButton == null || _recommendBtn == null) return;
        _recalculating = true;
        try
        {
            int total = _navPanel.Children.Count;
            if (total == 0) { UpdatePagerState(); return; }

            // 先全部显示，再测量各自期望宽度（包含 Margin）
            foreach (UIElement c in _navPanel.Children) c.Visibility = Visibility.Visible;
            _navPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double inner = _bottomBar.ActualWidth;
            double used = _navPrevButton.ActualWidth + _navNextButton.ActualWidth + _recommendBtn.ActualWidth + 40;
            double avail = inner - used;
            if (avail <= 0) avail = inner * 0.6;

            // 不改变导航栏总宽，确保至少装下 5 个按钮：扣除外边距后按 5 等分
            int want = Math.Min(5, total);
            double totalMargin = 0;
            foreach (UIElement c in _navPanel.Children)
                if (c is FrameworkElement fe) totalMargin += fe.Margin.Left + fe.Margin.Right;
            double targetW = Math.Min(_navTextWidth, Math.Floor((avail - totalMargin) / want));
            if (targetW > 0)
            {
                foreach (UIElement c in _navPanel.Children)
                    if (c is FrameworkElement fe) fe.Width = targetW;
                _navPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            double acc = 0;
            int count = 0;
            for (int i = 0; i < total; i++)
            {
                var child = _navPanel.Children[i];
                double w = child is FrameworkElement fe ? fe.DesiredSize.Width : 0;
                if (count == 0 || acc + w <= avail) { acc += w; count++; }
                else break;
            }

            _navVisibleCount = Math.Max(1, count);
            if (_navOffset > total - _navVisibleCount) _navOffset = Math.Max(0, total - _navVisibleCount);
            if (_navOffset < 0) _navOffset = 0;
            ApplyNavVisibility();
        }
        finally
        {
            _recalculating = false;
        }
    }

    private void ApplyNavVisibility()
    {
        int total = _navPanel.Children.Count;
        for (int i = 0; i < total; i++)
        {
            _navPanel.Children[i].Visibility = (i >= _navOffset && i < _navOffset + _navVisibleCount) ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdatePagerState();
    }

    private void UpdatePagerState()
    {
        int total = _navPanel.Children.Count;
        bool canPrev = _navOffset > 0;
        bool canNext = _navOffset + _navVisibleCount < total;
        _navPrevButton.IsEnabled = canPrev;
        _navNextButton.IsEnabled = canNext;
        _navPrevButton.Opacity = canPrev ? 1 : 0.3;
        _navNextButton.Opacity = canNext ? 1 : 0.3;
    }

    // ============================================================= AI 助手 =============================================================

    private static WC4MapEditor.AI.Ui.AiChatWindow? _aiChatWindow;

    /// <summary>
    /// 把"切换编辑场景"能力桥接给 AI 子模块（等效用户点导航按钮）。
    /// 渲染场景（战役/征服/战术地图）需加载地图文件流程，暂不开放给 AI。
    /// </summary>
    private void RegisterAiBridge()
    {
        WC4MapEditor.AI.Tools.AiBridge.UiMarshal ??= a => Dispatcher.Invoke(a);

        WC4MapEditor.AI.Tools.AiBridge.SceneOpener = name =>
        {
            UserControl scene;
            switch (name.Trim().ToLowerInvariant())
            {
                case "asset": scene = new AssetBrowserScene(_window); break;
                case "general": scene = new GeneralEditScene(_window); break;
                case "country": scene = new CountryEditScene(_window); break;
                case "army_group_event": scene = new ArmyGroupEventEditScene(_window); break;
                case "event_buff": scene = new EventBuffEditScene(_window); break;
                case "conquer_event": scene = new ConquerEventEditScene(_window); break;
                case "ingame_tech": scene = new CountryTechEditScene(_window); break;
                case "skill": scene = new SkillEditScene(_window); break;
                case "army_buff": scene = new ArmyBuffEditScene(_window); break;
                case "army": scene = new ArmyEditScene(_window); break;
                case "building": scene = new BuildingFacilityEditScene(_window); break;
                case "army_group": scene = new ArmyGroupEditScene(_window); break;
                case "layout": scene = new LayoutEditScene(_window); break;
                default: return false;
            }
            _window.SetCurrentScene(scene);
            return true;
        };

        WC4MapEditor.AI.Tools.AiBridge.SceneLister = () => new[]
        {
            "asset", "general", "country", "army_group_event", "event_buff",
            "conquer_event", "ingame_tech", "skill", "army_buff", "army",
            "building", "army_group", "layout"
        };

        WC4MapEditor.AI.Tools.AiBridge.MapDataProvider = () => null;

        WC4MapEditor.AI.Tools.AiBridge.LuaCodeRunner = _ => null;

        WC4MapEditor.AI.Tools.AiBridge.MapCreator = (type, width, height, legions, mapNumber) =>
        {
            try
            {
                switch (type.ToLowerInvariant())
                {
                    case "campaign":
                    {
                        var parser = new Core.Parsers.Stage.StageParser();
                        if (!parser.CreateNew(width, height, legions))
                            return (false, $"创建战役失败（{width}x{height}，{legions}军团）");
                        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                            $"NewCampaign_{width}x{height}_{DateTime.Now:yyyyMMddHHmmss}.btl");
                        if (!parser.SaveData(tempPath))
                            return (false, "战役保存临时文件失败");
                        var scene = new StageRenderScene(_window, tempPath);
                        _window.SetCurrentScene(scene);
                        return (true, $"已创建战役 {width}x{height}（{legions}军团）并打开编辑");
                    }
                    case "conquest":
                    {
                        var parser = new Core.Parsers.Conquest.ConquestParser();
                        if (!parser.CreateNew(width, height, legions, mapNumber))
                            return (false, $"创建征服失败（{width}x{height}，{legions}军团，地图编号{mapNumber}）");
                        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                            $"NewConquest_{width}x{height}_{mapNumber}_{DateTime.Now:yyyyMMddHHmmss}.btl");
                        if (!parser.SaveData(tempPath))
                            return (false, "征服保存临时文件失败");
                        var scene = new ConquestRenderScene(_window, tempPath);
                        _window.SetCurrentScene(scene);
                        return (true, $"已创建征服 {width}x{height}（{legions}军团，地图编号{mapNumber}）并打开编辑");
                    }
                    case "map":
                    {
                        var mapData = Core.Parsers.World.WorldParser.CreateNew(width, height);
                        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                            $"NewMap_{width}x{height}_{DateTime.Now:yyyyMMddHHmmss}.bin");
                        Core.Parsers.World.WorldParser.SaveToFile(mapData, tempPath);
                        var scene = new MapRenderScene(_window, tempPath);
                        _window.SetCurrentScene(scene);
                        return (true, $"已创建世界地图 {width}x{height} 并打开编辑");
                    }
                    default:
                        return (false, $"未知地图类型: {type}（支持 campaign/conquest/map）");
                }
            }
            catch (Exception ex)
            {
                return (false, $"创建{type}失败: {ex.Message}");
            }
        };

        WC4MapEditor.AI.Tools.AiBridge.CommandRunner = cmd =>
        {
            string trimmed = cmd.Trim();
            if (trimmed.StartsWith("open ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                return OpenMapFromCommand(trimmed);
            }

            // 开始界面也要能执行这里注册的命令（new_campaign / new_conquest / new_map …）：
            // 否则 AI 想在开始界面建图只能走专用工具，命令回退一律被拒。
            var cm = Core.Commands.CommandManager.Instance;
            if (cm.HasCommand(trimmed.Split(' ')[0]))
            {
                var sw = new StringWriter();
                bool ok;
                try
                {
                    var ctx = cm.GetContext();
                    var old = ctx?.Output;
                    if (ctx != null) ctx.Output = sw;
                    try { ok = cm.ExecuteCommand(trimmed).Success; }
                    finally { if (ctx != null) ctx.Output = old; }
                }
                catch (Exception ex)
                {
                    return (false, $"执行 '{trimmed}' 失败: {ex.Message}");
                }

                return (ok, sw.ToString().Trim());
            }

            return (false, "尚未加载地图，请先用 open <文件路径> 加载地图后再执行此命令");
        };

        WC4MapEditor.AI.Tools.AiBridge.DataMutated = _ => { };

        RegisterNewMapCommands();
    }

    /// <summary>注册 new_campaign / new_conquest / new_map CLI 命令。</summary>
    private void RegisterNewMapCommands()
    {
        var cm = Core.Commands.CommandManager.Instance;

        cm.RegisterCommand("new_campaign", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                System.Diagnostics.Debug.WriteLine("用法: new_campaign <宽度> <高度> [军团数]");
                return;
            }
            int legions = args.Length >= 3 && int.TryParse(args[2], out int l) ? l : 2;
            var parser = new Core.Parsers.Stage.StageParser();
            if (!parser.CreateNew(w, h, legions))
            {
                System.Diagnostics.Debug.WriteLine($"创建战役失败（{w}x{h}，{legions}军团）");
                return;
            }
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewCampaign_{w}x{h}_{DateTime.Now:yyyyMMddHHmmss}.btl");
            if (!parser.SaveData(tempPath))
            {
                System.Diagnostics.Debug.WriteLine("战役保存临时文件失败");
                return;
            }
            System.Diagnostics.Debug.WriteLine($"已创建战役 {w}x{h}（{legions}军团）: {tempPath}");
            _window.SetCurrentScene(new StageRenderScene(_window, tempPath));
        }, "创建新战役并打开编辑", "<宽度> <高度> [军团数]");

        cm.RegisterCommand("new_conquest", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                System.Diagnostics.Debug.WriteLine("用法: new_conquest <宽度> <高度> [军团数] [地图编号]");
                return;
            }
            int legions = args.Length >= 3 && int.TryParse(args[2], out int l) ? l : 2;
            int mapNum = args.Length >= 4 && int.TryParse(args[3], out int mn) ? mn : 1;
            var parser = new Core.Parsers.Conquest.ConquestParser();
            if (!parser.CreateNew(w, h, legions, mapNum))
            {
                System.Diagnostics.Debug.WriteLine($"创建征服失败（{w}x{h}，{legions}军团，地图编号{mapNum}）");
                return;
            }
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewConquest_{w}x{h}_{mapNum}_{DateTime.Now:yyyyMMddHHmmss}.btl");
            if (!parser.SaveData(tempPath))
            {
                System.Diagnostics.Debug.WriteLine("征服保存临时文件失败");
                return;
            }
            System.Diagnostics.Debug.WriteLine($"已创建征服 {w}x{h}（{legions}军团，地图编号{mapNum}）: {tempPath}");
            _window.SetCurrentScene(new ConquestRenderScene(_window, tempPath));
        }, "创建新征服并打开编辑", "<宽度> <高度> [军团数] [地图编号]");

        cm.RegisterCommand("new_map", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                System.Diagnostics.Debug.WriteLine("用法: new_map <宽度> <高度>");
                return;
            }
            if (w < 10 || w > 500 || h < 10 || h > 500)
            {
                System.Diagnostics.Debug.WriteLine("地图尺寸必须在 10-500 之间");
                return;
            }
            var mapData = Core.Parsers.World.WorldParser.CreateNew(w, h);
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewMap_{w}x{h}_{DateTime.Now:yyyyMMddHHmmss}.bin");
            Core.Parsers.World.WorldParser.SaveToFile(mapData, tempPath);
            System.Diagnostics.Debug.WriteLine($"已创建世界地图 {w}x{h}: {tempPath}");
            _window.SetCurrentScene(new MapRenderScene(_window, tempPath));
        }, "创建新世界地图并打开编辑", "<宽度> <高度>");
    }

    /// <summary>在主场景中处理 open 命令：解析路径 → 创建对应渲染场景 → 切换。</summary>
    private (bool Ok, string Output) OpenMapFromCommand(string command)
    {
        string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return (false, "用法: open <文件路径>（支持 .btl/.bin/.dat）");

        string rawPath = parts[1].Replace('\\', '/');
        string? resolved = ResolveMapFilePath(rawPath);
        if (resolved == null)
            return (false, $"找不到文件: {rawPath}（已尝试原路径/assets根/当前目录/程序目录）");

        var ext = System.IO.Path.GetExtension(resolved).ToLowerInvariant();
        if (ext != ".btl" && ext != ".bin" && ext != ".dat")
            return (false, $"不支持的文件类型: {ext}");

        string fileName = System.IO.Path.GetFileNameWithoutExtension(resolved).ToLowerInvariant();
        UserControl newScene;
        if (ext == ".bin" || ext == ".dat")
            newScene = new MapRenderScene(_window, resolved);
        else if (fileName.StartsWith("conquest"))
            newScene = new ConquestRenderScene(_window, resolved);
        else
            newScene = new StageRenderScene(_window, resolved);

        _window.SetCurrentScene(newScene);
        return (true, $"正在加载: {resolved}");
    }

    /// <summary>按优先级解析地图文件路径：原路径 → assets 根 → 当前目录 → 程序目录。</summary>
    private string? ResolveMapFilePath(string rawPath)
    {
        if (System.IO.File.Exists(rawPath)) return rawPath;

        try
        {
            string assetsRoot = Core.Assets.AssetManager.Default.AssetsRoot;
            if (!string.IsNullOrEmpty(assetsRoot))
            {
                string p = System.IO.Path.Combine(assetsRoot, rawPath);
                if (System.IO.File.Exists(p)) return p;
            }
        }
        catch { }

        string byCwd = System.IO.Path.Combine(Directory.GetCurrentDirectory(), rawPath);
        if (System.IO.File.Exists(byCwd)) return byCwd;

        string byBase = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);
        if (System.IO.File.Exists(byBase)) return byBase;

        return null;
    }

    private void AiAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiChatWindow != null && _aiChatWindow.IsLoaded)
        {
            _aiChatWindow.Activate();
            return;
        }

        _aiChatWindow = new WC4MapEditor.AI.Ui.AiChatWindow
        {
            Owner = Window.GetWindow(this)
        };
        _aiChatWindow.Closed += (_, _) => _aiChatWindow = null;
        _aiChatWindow.Show();
    }
}