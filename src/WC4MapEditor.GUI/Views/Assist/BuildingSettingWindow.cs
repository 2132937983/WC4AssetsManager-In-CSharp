using System.Runtime.InteropServices;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 建筑编辑窗口：按分组排列的字段表单，外观走 <see cref="EditorTheme"/>。
/// 名称框获得焦点时切到中文输入法、失焦切回英文，其它字段都是数字。
/// </summary>
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
        EditorTheme.Apply(this);

        _building = building;
        _isNewBuilding = isNew;

        Title = _isNewBuilding ? Loc.T("Assist.Building.CreateTitle") : Loc.T("Assist.Building.EditTitle");
        Width = 620;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        // 淡入淡出靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

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
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = CreateTitleBar();
        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        var editPanel = CreateEditPanel();
        Grid.SetRow(editPanel, 1);
        mainGrid.Children.Add(editPanel);

        var buttonPanel = CreateButtonPanel();
        Grid.SetRow(buttonPanel, 2);
        mainGrid.Children.Add(buttonPanel);

        // 无边框窗口描一圈边，免得与底下的主窗口连成一片
        return new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = mainGrid
        };
    }

    /// <summary>标题栏：按住可拖动窗口，右侧 ✕ 等同取消。</summary>
    private Border CreateTitleBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = EditorTheme.Title(Title);
        titleText.Margin = new Thickness(12, 0, 0, 0);
        titleText.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });
        Grid.SetColumn(titleText, 0);
        grid.Children.Add(titleText);

        var closeButton = EditorTheme.GhostButton("✕", (_, _) => CancelAndClose());
        closeButton.Width = 32;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.Margin = new Thickness(0, 0, 6, 0);
        closeButton.ToolTip = Loc.T("Common.Close");
        Grid.SetColumn(closeButton, 1);
        grid.Children.Add(closeButton);

        var titleBar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        return titleBar;
    }

    private ScrollViewer CreateEditPanel()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var contentPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // Basic info group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Common.BasicInfo"), new[]
        {
            (Loc.T("Assist.Building.Field.Coordinate"), "coordinate"),
            (Loc.T("Common.Name"), "name"),
            (Loc.T("Assist.Building.Field.BuildingType"), "building_type"),
            (Loc.T("Assist.Building.Field.Appearance"), "appearance"),
        }));

        // Properties group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.Properties"), new[]
        {
            (Loc.T("Assist.Building.Field.Landmark"), "landmark"),
            (Loc.T("Assist.Building.Field.Decorative"), "decorative"),
            (Loc.T("Assist.Building.Field.SkillUnlock"), "skill_unlock"),
            (Loc.T("Assist.Building.Field.RewardCount"), "reward_count"),
            (Loc.T("Assist.Building.Field.Hatred"), "hatred"),
            (Loc.T("Assist.Building.Field.KeyPoint"), "key_point"),
            (Loc.T("Assist.Building.Field.OccupationEvent"), "occupation_event"),
        }));

        // Fire and defense group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.FireAndAirDefense"), new[]
        {
            (Loc.T("Assist.Building.Field.FireIgnition"), "fire_ignition"),
            (Loc.T("Assist.Building.Field.FireDuration"), "fire_duration"),
            (Loc.T("Assist.Building.Field.AirDefenseWeapon"), "air_defense_weapon"),
            (Loc.T("Assist.Building.Field.AirDefenseRadar"), "air_defense_radar"),
        }));

        // Facility levels group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.FacilityLevels"), new[]
        {
            (Loc.T("Assist.Building.Field.FactoryLevel"), "factory_level"),
            (Loc.T("Assist.Building.Field.ResearchLevel"), "research_level"),
            (Loc.T("Assist.Building.Field.MedicalLevel"), "medical_level"),
            (Loc.T("Assist.Building.Field.AviationLevel"), "aviation_level"),
            (Loc.T("Assist.Building.Field.MissileLevel"), "missile_level"),
            (Loc.T("Assist.Building.Field.NuclearLevel"), "nuclear_level"),
        }));

        scrollViewer.Content = contentPanel;
        return scrollViewer;
    }

    /// <summary>一个分组：主题分组标题 + 两列表单行（字段多，单列会拉得太长）。</summary>
    private StackPanel CreateAttributeGroup(string title, (string label, string key)[] fields)
    {
        var groupPanel = new StackPanel();
        groupPanel.Children.Add(EditorTheme.SectionHeader(title));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowIndex = 0;
        for (int i = 0; i < fields.Length; i += 2)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row1 = CreateFieldRow(fields[i].label, fields[i].key);
            Grid.SetRow(row1, rowIndex);
            Grid.SetColumn(row1, 0);
            grid.Children.Add(row1);

            if (i + 1 < fields.Length)
            {
                var row2 = CreateFieldRow(fields[i + 1].label, fields[i + 1].key);
                Grid.SetRow(row2, rowIndex);
                Grid.SetColumn(row2, 1);
                grid.Children.Add(row2);
            }

            rowIndex++;
        }

        groupPanel.Children.Add(grid);
        return groupPanel;
    }

    private Border CreateFieldRow(string label, string key)
    {
        var textBox = new TextBox();

        // 只有名称是中文文本，其它都是数字：进出名称框时顺手切换输入法
        if (key == "name")
        {
            textBox.GotFocus += (_, _) => SwitchToChineseIME();
            textBox.LostFocus += (_, _) => SwitchToEnglishIME();
        }

        _textBoxes[key] = textBox;
        return EditorTheme.FormRow(label, textBox, 96);
    }

    private Border CreateButtonPanel()
    {
        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var confirmButton = EditorTheme.PrimaryButton(Loc.T("Common.Ok"), (_, _) => ConfirmAndClose());
        confirmButton.MinWidth = 84;
        buttonStack.Children.Add(confirmButton);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.MinWidth = 84;
        cancelButton.Margin = new Thickness(8, 0, 0, 0);
        buttonStack.Children.Add(cancelButton);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = buttonStack
        };
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
        _building.Name = BuildingNameResolver.Resolve(_textBoxes.GetValueOrDefault("name")?.Text);
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
