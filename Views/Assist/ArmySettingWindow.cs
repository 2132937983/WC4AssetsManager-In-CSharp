using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WC4MapEditor.Models;

namespace WC4MapEditor.Views.Assist;

public class ArmySettingWindow : Window
{
    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

    private bool _isClosing;
    private bool _confirmed;
    protected Army _army;
    protected bool _isNewArmy;

    // UI elements
    protected readonly Dictionary<string, TextBox> _textBoxes = new();

    // General info panel
    protected Ellipse? _generalAvatarEllipse;
    protected Image? _generalAvatarImage;
    protected TextBlock? _generalNameText;
    protected StackPanel? _generalDataPanel;

    public Army ResultArmy => _army;
    public bool Confirmed => _confirmed;

    public ArmySettingWindow(Army army, bool isNew = false)
    {
        _army = army;
        _isNewArmy = isNew;

        Title = isNew ? "创建单位" : "编辑单位";
        Width = 700;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 12;

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
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
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

        LoadArmyData();
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
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 80, 80, 90)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 8,
                Opacity = 0.6
            }
        };

        var mainGrid = new Grid { Margin = new Thickness(10) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        // Title bar
        var titleBar = CreateTitleBar();
        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        // Edit panel
        var editPanel = CreateEditPanel();
        Grid.SetRow(editPanel, 1);
        mainGrid.Children.Add(editPanel);

        // Button panel
        var buttonPanel = CreateButtonPanel();
        Grid.SetRow(buttonPanel, 2);
        mainGrid.Children.Add(buttonPanel);

        outerBorder.Child = mainGrid;
        return outerBorder;
    }

    private Grid CreateTitleBar()
    {
        var titleBar = new Grid();
        titleBar.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204));
        titleBar.Height = 40;

        var titleText = new TextBlock
        {
            Text = _isNewArmy ? "创建新单位" : "编辑单位",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleBar.Children.Add(titleText);

        var closeButton = new Button
        {
            Content = "x",
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 5, 0),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand
        };
        closeButton.Click += (_, _) => CancelAndClose();
        closeButton.MouseEnter += (_, _) => closeButton.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
        closeButton.MouseLeave += (_, _) => closeButton.Background = Brushes.Transparent;
        titleBar.Children.Add(closeButton);

        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        return titleBar;
    }

    private Border CreateEditPanel()
    {
        var editBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(100, 60, 60, 63)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 10)
        };

        var mainPanel = new Grid();
        mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left panel - general info
        CreateGeneralPanel(mainPanel);

        // Right panel - input fields
        CreateInputPanel(mainPanel);

        editBorder.Child = mainPanel;
        return editBorder;
    }

    private void CreateGeneralPanel(Grid parentGrid)
    {
        var generalBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
        };
        Grid.SetColumn(generalBorder, 0);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var generalPanel = new StackPanel
        {
            Margin = new Thickness(15),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Avatar container
        var avatarContainer = new Grid
        {
            Width = 100,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        };

        _generalAvatarEllipse = new Ellipse
        {
            Width = 100,
            Height = 100,
            Fill = new SolidColorBrush(Color.FromRgb(80, 80, 85)),
            Stroke = new SolidColorBrush(Color.FromRgb(0, 150, 255)),
            StrokeThickness = 3,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 150, 255),
                ShadowDepth = 0,
                BlurRadius = 15,
                Opacity = 0.7
            }
        };
        avatarContainer.Children.Add(_generalAvatarEllipse);

        _generalAvatarImage = new Image
        {
            Width = 94,
            Height = 94,
            Clip = new EllipseGeometry(new Point(47, 47), 47, 47),
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed
        };
        avatarContainer.Children.Add(_generalAvatarImage);

        generalPanel.Children.Add(avatarContainer);

        // General name
        _generalNameText = new TextBlock
        {
            Text = "无将领",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        generalPanel.Children.Add(_generalNameText);

        // General data panel
        _generalDataPanel = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        generalPanel.Children.Add(_generalDataPanel);

        scrollViewer.Content = generalPanel;
        generalBorder.Child = scrollViewer;
        parentGrid.Children.Add(generalBorder);
    }

    private void CreateInputPanel(Grid parentGrid)
    {
        var inputBorder = new Border
        {
            Background = Brushes.Transparent
        };
        Grid.SetColumn(inputBorder, 1);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var contentPanel = new StackPanel { Margin = new Thickness(10) };

        // Basic info group
        contentPanel.Children.Add(CreateAttributeGroup("基础信息", new[]
        {
            ("坐标", "coordinate"),
            ("兵种", "unit_type"),
            ("等级", "level"),
            ("编制", "organization"),
            ("方向", "direction"),
        }));

        // Attributes group
        contentPanel.Children.Add(CreateAttributeGroup("属性", new[]
        {
            ("移动力", "mobility"),
            ("建造回合", "built_round"),
            ("兵种经验", "experience"),
            ("血量加成", "health_bonus"),
            ("当前血量", "current_health"),
            ("血量上限", "max_health"),
        }));

        // General group
        contentPanel.Children.Add(CreateAttributeGroup("将领", new[]
        {
            ("将领", "general"),
            ("军衔", "rank"),
            ("爵位", "nobility"),
        }));

        // Equipment group
        contentPanel.Children.Add(CreateAttributeGroup("装备", new[]
        {
            ("胸章一", "badge1"),
            ("胸章二", "badge2"),
            ("胸章三", "badge3"),
        }));

        // Skills group
        contentPanel.Children.Add(CreateAttributeGroup("技能", new[]
        {
            ("技能等级1", "skill_level1"),
            ("技能等级2", "skill_level2"),
            ("技能等级3", "skill_level3"),
            ("技能等级4", "skill_level4"),
            ("技能等级5", "skill_level5"),
        }));

        // Others group
        contentPanel.Children.Add(CreateAttributeGroup("其他", new[]
        {
            ("关键据点", "key_point"),
            ("方针", "policy"),
            ("方案", "plan"),
            ("改变回合", "change_round"),
            ("士气", "morale"),
            ("持续回合", "duration"),
            ("关联对话", "dialogue"),
            ("能否攻击", "can_attack"),
        }));

        scrollViewer.Content = contentPanel;
        inputBorder.Child = scrollViewer;
        parentGrid.Children.Add(inputBorder);
    }

    private Border CreateAttributeGroup(string title, (string label, string key)[] fields)
    {
        var groupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 80, 80, 85)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10)
        };

        var groupPanel = new StackPanel();

        var titleText = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 255)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        groupPanel.Children.Add(titleText);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowIndex = 0;
        for (int i = 0; i < fields.Length; i += 2)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var panel1 = CreateFieldPanel(fields[i].label, fields[i].key);
            Grid.SetRow(panel1, rowIndex);
            Grid.SetColumn(panel1, 0);
            grid.Children.Add(panel1);

            if (i + 1 < fields.Length)
            {
                var panel2 = CreateFieldPanel(fields[i + 1].label, fields[i + 1].key);
                Grid.SetRow(panel2, rowIndex);
                Grid.SetColumn(panel2, 1);
                grid.Children.Add(panel2);
            }

            rowIndex++;
        }

        groupPanel.Children.Add(grid);
        groupBorder.Child = groupPanel;

        return groupBorder;
    }

    private StackPanel CreateFieldPanel(string label, string key)
    {
        var panel = new StackPanel { Margin = new Thickness(5) };

        var labelText = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3)
        };
        panel.Children.Add(labelText);

        var textBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            FontSize = 11,
            Height = 26
        };

        textBox.GotFocus += (_, _) => textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 255));
        textBox.LostFocus += (_, _) => textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 85));

        panel.Children.Add(textBox);
        _textBoxes[key] = textBox;

        return panel;
    }

    private Grid CreateButtonPanel()
    {
        var buttonPanel = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
            Height = 60
        };

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var confirmButton = CreateStyledButton("确定", new SolidColorBrush(Color.FromRgb(0, 122, 204)));
        confirmButton.Click += (_, _) => ConfirmAndClose();
        buttonStack.Children.Add(confirmButton);

        var cancelButton = CreateStyledButton("取消", new SolidColorBrush(Color.FromRgb(80, 80, 80)));
        cancelButton.Click += (_, _) => CancelAndClose();
        buttonStack.Children.Add(cancelButton);

        buttonPanel.Children.Add(buttonStack);
        return buttonPanel;
    }

    private Button CreateStyledButton(string text, Brush background)
    {
        var btn = new Button
        {
            Content = text,
            Width = 80,
            Height = 32,
            Margin = new Thickness(5),
            Background = background,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand
        };
        btn.MouseEnter += (_, _) => btn.Opacity = 0.8;
        btn.MouseLeave += (_, _) => btn.Opacity = 1.0;
        return btn;
    }

    protected virtual void LoadArmyData()
    {
        SetTextBoxValue("coordinate", _army.Coordinate);
        SetTextBoxValue("unit_type", _army.UnitType);
        SetTextBoxValue("level", _army.Level);
        SetTextBoxValue("organization", _army.Organization);
        SetTextBoxValue("direction", _army.Direction);
        SetTextBoxValue("mobility", _army.Mobility);
        SetTextBoxValue("built_round", _army.BuiltRound);
        SetTextBoxValue("experience", _army.Experience);
        SetTextBoxValue("health_bonus", _army.HealthBonus);
        SetTextBoxValue("current_health", _army.CurrentHealth);
        SetTextBoxValue("max_health", _army.MaxHealth);
        SetTextBoxValue("general", _army.General);
        SetTextBoxValue("rank", _army.Rank);
        SetTextBoxValue("nobility", _army.Nobility);
        SetTextBoxValue("badge1", _army.Badge1);
        SetTextBoxValue("badge2", _army.Badge2);
        SetTextBoxValue("badge3", _army.Badge3);
        SetTextBoxValue("skill_level1", _army.SkillLevel1);
        SetTextBoxValue("skill_level2", _army.SkillLevel2);
        SetTextBoxValue("skill_level3", _army.SkillLevel3);
        SetTextBoxValue("skill_level4", _army.SkillLevel4);
        SetTextBoxValue("skill_level5", _army.SkillLevel5);
        SetTextBoxValue("key_point", _army.KeyPoint);
        SetTextBoxValue("policy", _army.Policy);
        SetTextBoxValue("plan", _army.Plan);
        SetTextBoxValue("change_round", _army.ChangeRound);
        SetTextBoxValue("morale", _army.Morale);
        SetTextBoxValue("duration", _army.Duration);
        SetTextBoxValue("dialogue", _army.Dialogue);
        SetTextBoxValue("can_attack", _army.CanAttack);

        UpdateGeneralInfo(_army.General.ToString());
    }

    protected void SetTextBoxValue(string key, object value)
    {
        if (_textBoxes.TryGetValue(key, out var textBox))
        {
            textBox.Text = value?.ToString() ?? "0";
        }
    }

    protected virtual void SaveArmyData()
    {
        _army.Coordinate = ParseShort(_textBoxes.GetValueOrDefault("coordinate")?.Text);
        _army.UnitType = ParseByte(_textBoxes.GetValueOrDefault("unit_type")?.Text);
        _army.Level = ParseByte(_textBoxes.GetValueOrDefault("level")?.Text);
        _army.Organization = ParseByte(_textBoxes.GetValueOrDefault("organization")?.Text);
        _army.Direction = ParseByte(_textBoxes.GetValueOrDefault("direction")?.Text);
        _army.Mobility = ParseByte(_textBoxes.GetValueOrDefault("mobility")?.Text);
        _army.BuiltRound = ParseByte(_textBoxes.GetValueOrDefault("built_round")?.Text);
        _army.Experience = ParseShort(_textBoxes.GetValueOrDefault("experience")?.Text);
        _army.HealthBonus = ParseShort(_textBoxes.GetValueOrDefault("health_bonus")?.Text);
        _army.CurrentHealth = ParseShort(_textBoxes.GetValueOrDefault("current_health")?.Text);
        _army.MaxHealth = ParseShort(_textBoxes.GetValueOrDefault("max_health")?.Text);
        _army.General = ParseShort(_textBoxes.GetValueOrDefault("general")?.Text);
        _army.Rank = ParseByte(_textBoxes.GetValueOrDefault("rank")?.Text);
        _army.Nobility = ParseByte(_textBoxes.GetValueOrDefault("nobility")?.Text);
        _army.Badge1 = ParseByte(_textBoxes.GetValueOrDefault("badge1")?.Text);
        _army.Badge2 = ParseByte(_textBoxes.GetValueOrDefault("badge2")?.Text);
        _army.Badge3 = ParseByte(_textBoxes.GetValueOrDefault("badge3")?.Text);
        _army.SkillLevel1 = ParseByte(_textBoxes.GetValueOrDefault("skill_level1")?.Text);
        _army.SkillLevel2 = ParseByte(_textBoxes.GetValueOrDefault("skill_level2")?.Text);
        _army.SkillLevel3 = ParseByte(_textBoxes.GetValueOrDefault("skill_level3")?.Text);
        _army.SkillLevel4 = ParseByte(_textBoxes.GetValueOrDefault("skill_level4")?.Text);
        _army.SkillLevel5 = ParseByte(_textBoxes.GetValueOrDefault("skill_level5")?.Text);
        _army.KeyPoint = ParseByte(_textBoxes.GetValueOrDefault("key_point")?.Text);
        _army.Policy = ParseByte(_textBoxes.GetValueOrDefault("policy")?.Text);
        _army.Plan = ParseShort(_textBoxes.GetValueOrDefault("plan")?.Text);
        _army.ChangeRound = ParseShort(_textBoxes.GetValueOrDefault("change_round")?.Text);
        _army.Morale = ParseByte(_textBoxes.GetValueOrDefault("morale")?.Text);
        _army.Duration = ParseByte(_textBoxes.GetValueOrDefault("duration")?.Text);
        _army.Dialogue = ParseByte(_textBoxes.GetValueOrDefault("dialogue")?.Text);
        _army.CanAttack = ParseByte(_textBoxes.GetValueOrDefault("can_attack")?.Text);
    }

    protected static short ParseShort(string? text)
    {
        return short.TryParse(text, out var value) ? value : (short)0;
    }

    protected static byte ParseByte(string? text)
    {
        return byte.TryParse(text, out var value) ? value : (byte)0;
    }

    protected void ConfirmAndClose()
    {
        SaveArmyData();
        _confirmed = true;
        PlayFadeOutAndClose();
    }

    protected void CancelAndClose()
    {
        _confirmed = false;
        PlayFadeOutAndClose();
    }

    protected void UpdateGeneralInfo(string? generalIdText)
    {
        if (string.IsNullOrEmpty(generalIdText) || !short.TryParse(generalIdText, out var generalId) || generalId == 0)
        {
            ShowNoGeneral();
            return;
        }

        if (_generalNameText != null)
            _generalNameText.Text = $"将领ID: {generalId}";

        // TODO: Load general avatar and data from TacticalMapParser / GeneralSettingParser
        // For now, just show the ID
    }

    protected void ShowNoGeneral()
    {
        if (_generalAvatarImage != null)
            _generalAvatarImage.Visibility = Visibility.Collapsed;
        if (_generalAvatarEllipse != null)
            _generalAvatarEllipse.Fill = new SolidColorBrush(Color.FromRgb(80, 80, 85));
        if (_generalNameText != null)
            _generalNameText.Text = "无将领";
    }
}