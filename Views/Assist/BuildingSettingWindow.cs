using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Models;

namespace WC4MapEditor.Views.Assist;

public sealed class BuildingSettingWindow : Window
{
    // Win32 API for IME switching
    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, nint[]? lpList);

    [DllImport("user32.dll")]
    private static extern nint ActivateKeyboardLayout(nint hkl, uint Flags);

    private const uint KLF_SETFORPROCESS = 0x00000100;
    private const int LANG_CHINESE_SIMPLIFIED = 0x0804;
    private const int LANG_ENGLISH_US = 0x0409;
    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

    private bool _isClosing;
    private bool _confirmed;
    private Building _building;
    private bool _isNewBuilding;

    // UI elements
    private readonly Dictionary<string, TextBox> _textBoxes = new();

    public Building ResultBuilding => _building;
    public bool Confirmed => _confirmed;

    public BuildingSettingWindow(Building building, bool isNew = false)
    {
        _building = building;
        _isNewBuilding = isNew;

        Title = isNew ? "创建建筑" : "编辑建筑";
        Width = 600;
        Height = 550;
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
        else if (e.Key == Key.Enter)
        {
            if (e.OriginalSource is TextBox textBox && textBox.AcceptsReturn)
                return;
            e.Handled = true;
            ConfirmAndClose();
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
            MoveFocusToNextField();
        }
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (_textBoxes.TryGetValue("name", out var nameTextBox))
            {
                nameTextBox.Clear();
                nameTextBox.Focus();
            }
        }
    }

    private static readonly string[] _fieldOrder =
    [
        "coordinate", "name", "building_type", "appearance",
        "landmark", "decorative", "skill_unlock", "reward_count",
        "hatred", "key_point", "occupation_event",
        "fire_ignition", "fire_duration", "air_defense_weapon", "air_defense_radar",
        "factory_level", "research_level", "medical_level", "aviation_level",
        "missile_level", "nuclear_level"
    ];

    private void MoveFocusToNextField()
    {
        var focusedElement = Keyboard.FocusedElement as TextBox;

        if (focusedElement == null)
        {
            if (_textBoxes.TryGetValue(_fieldOrder[0], out var firstTextBox))
            {
                firstTextBox.Focus();
                firstTextBox.SelectAll();
            }
            return;
        }

        string? currentKey = null;
        foreach (var kvp in _textBoxes)
        {
            if (kvp.Value == focusedElement)
            {
                currentKey = kvp.Key;
                break;
            }
        }

        if (currentKey == null)
        {
            if (_textBoxes.TryGetValue(_fieldOrder[0], out var firstTextBox))
            {
                firstTextBox.Focus();
                firstTextBox.SelectAll();
            }
            return;
        }

        int currentIndex = Array.IndexOf(_fieldOrder, currentKey);
        int nextIndex = (currentIndex + 1) % _fieldOrder.Length;
        string nextKey = _fieldOrder[nextIndex];

        if (_textBoxes.TryGetValue(nextKey, out var nextTextBox))
        {
            nextTextBox.Focus();
            nextTextBox.SelectAll();
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

        LoadBuildingData();
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
            Text = _isNewBuilding ? "创建建筑" : "编辑建筑",
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
            ("名称", "name"),
            ("建筑类型", "building_type"),
            ("外观", "appearance"),
        }));

        // Properties group
        contentPanel.Children.Add(CreateAttributeGroup("属性", new[]
        {
            ("地标建筑", "landmark"),
            ("装饰建筑", "decorative"),
            ("技能解锁", "skill_unlock"),
            ("奖励数量", "reward_count"),
            ("仇恨值", "hatred"),
            ("关键据点", "key_point"),
            ("占领事件", "occupation_event"),
        }));

        // Fire and defense group
        contentPanel.Children.Add(CreateAttributeGroup("火攻与防空", new[]
        {
            ("点火", "fire_ignition"),
            ("火持续时间", "fire_duration"),
            ("防空武器", "air_defense_weapon"),
            ("防空雷达", "air_defense_radar"),
        }));

        // Facility levels group
        contentPanel.Children.Add(CreateAttributeGroup("设施等级", new[]
        {
            ("工厂等级", "factory_level"),
            ("研究等级", "research_level"),
            ("医疗等级", "medical_level"),
            ("航空等级", "aviation_level"),
            ("导弹等级", "missile_level"),
            ("核武等级", "nuclear_level"),
        }));

        scrollViewer.Content = contentPanel;
        editBorder.Child = scrollViewer;

        return editBorder;
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

        textBox.GotFocus += (_, _) =>
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 255));
            if (key == "name")
                SwitchToChineseIME();
        };
        textBox.LostFocus += (_, _) =>
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 85));
            if (key == "name")
                SwitchToEnglishIME();
        };

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

    private void LoadBuildingData()
    {
        SetTextBoxValue("coordinate", _building.Coordinate);
        SetTextBoxValue("name", _building.Name);
        SetTextBoxValue("building_type", _building.BuildingType);
        SetTextBoxValue("appearance", _building.Appearance);
        SetTextBoxValue("landmark", _building.LandmarkBuilding);
        SetTextBoxValue("decorative", _building.DecorativeBuilding);
        SetTextBoxValue("skill_unlock", _building.SkillUnlock);
        SetTextBoxValue("reward_count", _building.RewardCount);
        SetTextBoxValue("hatred", _building.HatredValue);
        SetTextBoxValue("key_point", _building.KeyPoint);
        SetTextBoxValue("occupation_event", _building.OccupationEvent);
        SetTextBoxValue("fire_ignition", _building.FireIgnition);
        SetTextBoxValue("fire_duration", _building.FireDuration);
        SetTextBoxValue("air_defense_weapon", _building.AirDefenseWeapon);
        SetTextBoxValue("air_defense_radar", _building.AirDefenseRadar);
        SetTextBoxValue("factory_level", _building.FactoryLevel);
        SetTextBoxValue("research_level", _building.ResearchLevel);
        SetTextBoxValue("medical_level", _building.MedicalLevel);
        SetTextBoxValue("aviation_level", _building.AviationLevel);
        SetTextBoxValue("missile_level", _building.MissileLevel);
        SetTextBoxValue("nuclear_level", _building.NuclearLevel);
    }

    private void SetTextBoxValue(string key, object value)
    {
        if (_textBoxes.TryGetValue(key, out var textBox))
        {
            textBox.Text = value?.ToString() ?? "0";
        }
    }

    private void SaveBuildingData()
    {
        _building.Coordinate = ParseInt(_textBoxes.GetValueOrDefault("coordinate")?.Text);
        _building.Name = ParseNameField(_textBoxes.GetValueOrDefault("name")?.Text);
        _building.BuildingType = ParseByte(_textBoxes.GetValueOrDefault("building_type")?.Text);
        _building.Appearance = ParseByte(_textBoxes.GetValueOrDefault("appearance")?.Text);
        _building.LandmarkBuilding = ParseByte(_textBoxes.GetValueOrDefault("landmark")?.Text);
        _building.DecorativeBuilding = ParseByte(_textBoxes.GetValueOrDefault("decorative")?.Text);
        _building.SkillUnlock = ParseByte(_textBoxes.GetValueOrDefault("skill_unlock")?.Text);
        _building.RewardCount = ParseByte(_textBoxes.GetValueOrDefault("reward_count")?.Text);
        _building.HatredValue = ParseByte(_textBoxes.GetValueOrDefault("hatred")?.Text);
        _building.KeyPoint = ParseByte(_textBoxes.GetValueOrDefault("key_point")?.Text);
        _building.OccupationEvent = ParseByte(_textBoxes.GetValueOrDefault("occupation_event")?.Text);
        _building.FireIgnition = ParseByte(_textBoxes.GetValueOrDefault("fire_ignition")?.Text);
        _building.FireDuration = ParseByte(_textBoxes.GetValueOrDefault("fire_duration")?.Text);
        _building.AirDefenseWeapon = ParseByte(_textBoxes.GetValueOrDefault("air_defense_weapon")?.Text);
        _building.AirDefenseRadar = ParseByte(_textBoxes.GetValueOrDefault("air_defense_radar")?.Text);
        _building.FactoryLevel = ParseByte(_textBoxes.GetValueOrDefault("factory_level")?.Text);
        _building.ResearchLevel = ParseByte(_textBoxes.GetValueOrDefault("research_level")?.Text);
        _building.MedicalLevel = ParseByte(_textBoxes.GetValueOrDefault("medical_level")?.Text);
        _building.AviationLevel = ParseByte(_textBoxes.GetValueOrDefault("aviation_level")?.Text);
        _building.MissileLevel = ParseByte(_textBoxes.GetValueOrDefault("missile_level")?.Text);
        _building.NuclearLevel = ParseByte(_textBoxes.GetValueOrDefault("nuclear_level")?.Text);
    }

    /// <summary>
    /// 解析名称字段：如果是数字直接返回，如果是中文则查找或创建城市名称条目
    /// </summary>
    private static short ParseNameField(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        // 尝试解析为数字
        if (short.TryParse(text, out var numericValue))
            return numericValue;

        // 不是数字，视为中文城市名称
        var parser = ConfigManager.Instance.GetStringTableParser();

        // 查找是否已存在该名称
        var existingId = parser.FindCityIdByName(text);
        if (existingId.HasValue)
            return (short)existingId.Value;

        // 不存在则创建新条目
        int newId = parser.AddOrUpdateCityName(text);
        parser.Save();

        // 触发城市名称重载
        ConfigManager.Instance.ReloadStringTable();

        return (short)newId;
    }

    private static short ParseShort(string? text)
    {
        return short.TryParse(text, out var value) ? value : (short)0;
    }

    private static int ParseInt(string? text)
    {
        return int.TryParse(text, out var value) ? value : 0;
    }

    private static byte ParseByte(string? text)
    {
        return byte.TryParse(text, out var value) ? value : (byte)0;
    }

    /// <summary>
    /// 切换到中文输入法（简体）
    /// </summary>
    private static void SwitchToChineseIME()
    {
        try
        {
            int numLayouts = GetKeyboardLayoutList(0, null);
            if (numLayouts <= 0) return;

            var layouts = new nint[numLayouts];
            GetKeyboardLayoutList(numLayouts, layouts);

            nint targetLayout = nint.Zero;
            foreach (var layout in layouts)
            {
                int langId = (int)(layout & 0xFFFF);
                if (langId == LANG_CHINESE_SIMPLIFIED)
                {
                    targetLayout = layout;
                    break;
                }
            }

            if (targetLayout != nint.Zero)
            {
                ActivateKeyboardLayout(targetLayout, KLF_SETFORPROCESS);
            }
        }
        catch { }
    }

    /// <summary>
    /// 切换到英文输入法（美国）
    /// </summary>
    private static void SwitchToEnglishIME()
    {
        try
        {
            int numLayouts = GetKeyboardLayoutList(0, null);
            if (numLayouts <= 0) return;

            var layouts = new nint[numLayouts];
            GetKeyboardLayoutList(numLayouts, layouts);

            nint targetLayout = nint.Zero;
            foreach (var layout in layouts)
            {
                int langId = (int)(layout & 0xFFFF);
                if (langId == LANG_ENGLISH_US)
                {
                    targetLayout = layout;
                    break;
                }
            }

            if (targetLayout != nint.Zero)
            {
                ActivateKeyboardLayout(targetLayout, KLF_SETFORPROCESS);
            }
        }
        catch { }
    }

    private void ConfirmAndClose()
    {
        SaveBuildingData();
        _confirmed = true;
        PlayFadeOutAndClose();
    }

    private void CancelAndClose()
    {
        _confirmed = false;
        PlayFadeOutAndClose();
    }
}