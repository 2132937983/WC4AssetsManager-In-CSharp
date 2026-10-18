namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 单位（Army）编辑窗口：左侧将领信息，右侧按分组排列的字段表单。
/// 外观走 <see cref="EditorTheme"/>；<see cref="ArmySettingV3Window"/> 继承它复用整套界面，只换读写的数据对象。
/// </summary>
public class ArmySettingWindow : Window
{
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
        EditorTheme.Apply(this);

        _army = army;
        _isNewArmy = isNew;

        Title = isNew ? Loc.T("Assist.Army.CreateTitle") : Loc.T("Assist.Army.EditTitle");
        Width = 720;
        Height = 650;
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
        // 跟着 Window.Title 走：派生类（v3）在基类构造完之后才改标题，绑定能让标题栏同步
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

    private Grid CreateEditPanel()
    {
        var mainPanel = new Grid();
        mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left panel - general info
        CreateGeneralPanel(mainPanel);

        // Right panel - input fields
        CreateInputPanel(mainPanel);

        return mainPanel;
    }

    private void CreateGeneralPanel(Grid parentGrid)
    {
        var generalBorder = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        generalBorder.Background = EditorTheme.Bg2;
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
            Fill = EditorTheme.Bg3,
            Stroke = EditorTheme.Accent,
            StrokeThickness = 3,
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Accent),
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
            Text = Loc.T("Assist.Army.NoGeneral"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
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
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scrollViewer, 1);

        var contentPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // Basic info group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Common.BasicInfo"), new[]
        {
            (Loc.T("Assist.Army.Field.Coordinate"), "coordinate"),
            (Loc.T("Assist.Army.Field.UnitType"), "unit_type"),
            (Loc.T("Common.Level"), "level"),
            (Loc.T("Assist.Army.Field.Organization"), "organization"),
            (Loc.T("Assist.Army.Field.Direction"), "direction"),
        }));

        // Attributes group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Army.Section.Attributes"), new[]
        {
            (Loc.T("Assist.Army.Field.Mobility"), "mobility"),
            (Loc.T("Assist.Army.Field.BuiltRound"), "built_round"),
            (Loc.T("Assist.Army.Field.Experience"), "experience"),
            (Loc.T("Assist.Army.Field.HealthBonus"), "health_bonus"),
            (Loc.T("Assist.Army.Field.CurrentHealth"), "current_health"),
            (Loc.T("Assist.Army.Field.MaxHealth"), "max_health"),
        }));

        // General group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Army.Section.General"), new[]
        {
            (Loc.T("Assist.Army.Field.General"), "general"),
            (Loc.T("Assist.Army.Field.Rank"), "rank"),
            (Loc.T("Assist.Army.Field.Nobility"), "nobility"),
        }));

        // Equipment group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Army.Section.Equipment"), new[]
        {
            (Loc.T("Assist.Army.Field.Badge1"), "badge1"),
            (Loc.T("Assist.Army.Field.Badge2"), "badge2"),
            (Loc.T("Assist.Army.Field.Badge3"), "badge3"),
        }));

        // Skills group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Army.Section.Skills"), new[]
        {
            (Loc.T("Assist.Army.Field.SkillLevel1"), "skill_level1"),
            (Loc.T("Assist.Army.Field.SkillLevel2"), "skill_level2"),
            (Loc.T("Assist.Army.Field.SkillLevel3"), "skill_level3"),
            (Loc.T("Assist.Army.Field.SkillLevel4"), "skill_level4"),
            (Loc.T("Assist.Army.Field.SkillLevel5"), "skill_level5"),
        }));

        // Others group
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Army.Section.Others"), new[]
        {
            (Loc.T("Assist.Army.Field.KeyPoint"), "key_point"),
            (Loc.T("Assist.Army.Field.Policy"), "policy"),
            (Loc.T("Assist.Army.Field.Plan"), "plan"),
            (Loc.T("Assist.Army.Field.ChangeRound"), "change_round"),
            (Loc.T("Assist.Army.Field.Morale"), "morale"),
            (Loc.T("Assist.Army.Field.Duration"), "duration"),
            (Loc.T("Assist.Army.Field.Dialogue"), "dialogue"),
            (Loc.T("Assist.Army.Field.CanAttack"), "can_attack"),
        }));

        scrollViewer.Content = contentPanel;
        parentGrid.Children.Add(scrollViewer);
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
            _generalNameText.Text = Loc.F("Assist.Army.GeneralId", generalId);

        // TODO: Load general avatar and data from TacticalMapParser / GeneralSettingParser
        // For now, just show the ID
    }

    protected void ShowNoGeneral()
    {
        if (_generalAvatarImage != null)
            _generalAvatarImage.Visibility = Visibility.Collapsed;
        if (_generalAvatarEllipse != null)
            _generalAvatarEllipse.Fill = EditorTheme.Bg3;
        if (_generalNameText != null)
            _generalNameText.Text = Loc.T("Assist.Army.NoGeneral");
    }
}
