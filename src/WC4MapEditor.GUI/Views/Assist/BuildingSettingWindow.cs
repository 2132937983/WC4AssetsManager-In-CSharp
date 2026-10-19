using System.Globalization;
using System.Runtime.InteropServices;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 建筑编辑窗口：按分组排列的字段表单，外观走 <see cref="EditorTheme"/>。
/// 名称框获得焦点时切到中文输入法、失焦切回英文，其它字段都是数字。
/// <para>
/// 确定时逐项校验（字段范围对齐 VB BuildingSetting.InitializeFields）：输入无效时在底部提示原因、
/// 选中出错的输入框并保持窗口打开，不再把非法输入悄悄变成 0。名称最后解析，前面的字段都有效才可能新建城市名。
/// </para>
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

    /// <summary>修改建筑某个字节字段（Building 是结构体，按引用传入）。</summary>
    private delegate void ByteSetter(ref Building building, byte value);

    /// <summary>一个数字字段：显示名的翻译键、上限（下限都是 0）、读写方法与可选的说明翻译键。</summary>
    private sealed record ByteField(string LabelKey, int Max, Func<Building, byte> Get, ByteSetter Set, string? HintKey = null);

    private static readonly ByteField[] BasicFields =
    {
        new("Assist.Building.Field.BuildingType", 255, b => b.BuildingType, (ref Building b, byte v) => b.BuildingType = v),
        new("Assist.Building.Field.Appearance", 255, b => b.Appearance, (ref Building b, byte v) => b.Appearance = v),
    };

    private static readonly ByteField[] PropertyFields =
    {
        new("Assist.Building.Field.Landmark", 255, b => b.LandmarkBuilding, (ref Building b, byte v) => b.LandmarkBuilding = v),
        new("Assist.Building.Field.Decorative", 255, b => b.DecorativeBuilding, (ref Building b, byte v) => b.DecorativeBuilding = v),
        new("Assist.Building.Field.SkillUnlock", 255, b => b.SkillUnlock, (ref Building b, byte v) => b.SkillUnlock = v),
        new("Assist.Building.Field.RewardCount", 255, b => b.RewardCount, (ref Building b, byte v) => b.RewardCount = v),
        new("Assist.Building.Field.Hatred", 255, b => b.HatredValue, (ref Building b, byte v) => b.HatredValue = v, "Assist.Building.Hint.Hatred"),
        new("Assist.Building.Field.KeyPoint", 2, b => b.KeyPoint, (ref Building b, byte v) => b.KeyPoint = v, "Assist.Building.Hint.KeyPoint"),
        new("Assist.Building.Field.OccupationEvent", 255, b => b.OccupationEvent, (ref Building b, byte v) => b.OccupationEvent = v),
    };

    private static readonly ByteField[] FireAndAirDefenseFields =
    {
        new("Assist.Building.Field.FireIgnition", 255, b => b.FireIgnition, (ref Building b, byte v) => b.FireIgnition = v),
        new("Assist.Building.Field.FireDuration", 255, b => b.FireDuration, (ref Building b, byte v) => b.FireDuration = v),
        new("Assist.Building.Field.AirDefenseWeapon", 255, b => b.AirDefenseWeapon, (ref Building b, byte v) => b.AirDefenseWeapon = v),
        new("Assist.Building.Field.AirDefenseRadar", 2, b => b.AirDefenseRadar, (ref Building b, byte v) => b.AirDefenseRadar = v, "Assist.Building.Hint.AirDefenseRadar"),
    };

    private static readonly ByteField[] LevelFields =
    {
        new("Assist.Building.Field.FactoryLevel", 255, b => b.FactoryLevel, (ref Building b, byte v) => b.FactoryLevel = v),
        new("Assist.Building.Field.ResearchLevel", 255, b => b.ResearchLevel, (ref Building b, byte v) => b.ResearchLevel = v),
        new("Assist.Building.Field.MedicalLevel", 255, b => b.MedicalLevel, (ref Building b, byte v) => b.MedicalLevel = v),
        new("Assist.Building.Field.AviationLevel", 255, b => b.AviationLevel, (ref Building b, byte v) => b.AviationLevel = v),
        new("Assist.Building.Field.MissileLevel", 255, b => b.MissileLevel, (ref Building b, byte v) => b.MissileLevel = v),
        new("Assist.Building.Field.NuclearLevel", 255, b => b.NuclearLevel, (ref Building b, byte v) => b.NuclearLevel = v),
    };

    private bool _isClosing;
    private bool _confirmed;
    private Building _building;

    private readonly TextBox _coordinateBox = new() { IsReadOnly = true, Focusable = false };
    private readonly TextBox _nameBox = new();
    private readonly List<(ByteField Field, TextBox Box)> _byteBoxes = new();

    /// <summary>Tab 键在这些可编辑的输入框之间循环（坐标只读，不参与）。</summary>
    private readonly List<TextBox> _tabOrder = new();

    private readonly TextBlock _statusText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 12, 0)
    };

    public Building ResultBuilding => _building;
    public bool Confirmed => _confirmed;

    public BuildingSettingWindow(Building building, bool isNew = false)
    {
        EditorTheme.Apply(this);

        _building = building;

        Title = isNew ? Loc.T("Assist.Building.CreateTitle") : Loc.T("Assist.Building.EditTitle");
        Width = 620;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        // 淡入淡出靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        Content = CreateWindowContent();
        Loaded += (_, _) => OnLoaded();
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void OnLoaded()
    {
        LoadBuildingData();
        PlayFadeInAnimation();

        // 对齐 VB：打开时焦点在名称框并全选，直接输入即可替换名称
        Dispatcher.BeginInvoke(() =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        }, DispatcherPriority.Input);
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
            MoveFocus(backward: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }
    }

    private void MoveFocus(bool backward)
    {
        int count = _tabOrder.Count;
        int current = Keyboard.FocusedElement is TextBox box ? _tabOrder.IndexOf(box) : -1;
        int next = current < 0
            ? (backward ? count - 1 : 0)
            : (current + (backward ? count - 1 : 1)) % count;

        _tabOrder[next].Focus();
        _tabOrder[next].SelectAll();
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

    private void PlayFadeOutAndClose()
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
        var contentPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // 名称只有中文文本，其它都是数字：进出名称框时顺手切换输入法
        _nameBox.GotFocus += (_, _) =>
        {
            SwitchToChineseIME();
            ShowHint("Assist.Building.Hint.Name");
        };
        _nameBox.LostFocus += (_, _) => SwitchToEnglishIME();
        _tabOrder.Add(_nameBox);

        var basicRows = new List<Border>
        {
            CreateFieldRow(Loc.T("Assist.Building.Field.Coordinate"), _coordinateBox, "Assist.Building.Hint.Coordinate"),
            CreateFieldRow(Loc.T("Common.Name"), _nameBox, "Assist.Building.Hint.Name"),
        };
        basicRows.AddRange(BasicFields.Select(CreateByteFieldRow));

        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Common.BasicInfo"), basicRows));
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.Properties"),
            PropertyFields.Select(CreateByteFieldRow).ToList()));
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.FireAndAirDefense"),
            FireAndAirDefenseFields.Select(CreateByteFieldRow).ToList()));
        contentPanel.Children.Add(CreateAttributeGroup(Loc.T("Assist.Building.Section.FacilityLevels"),
            LevelFields.Select(CreateByteFieldRow).ToList()));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contentPanel
        };
    }

    /// <summary>一个分组：主题分组标题 + 两列表单行（字段多，单列会拉得太长）。</summary>
    private static StackPanel CreateAttributeGroup(string title, IReadOnlyList<Border> rows)
    {
        var groupPanel = new StackPanel();
        groupPanel.Children.Add(EditorTheme.SectionHeader(title));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < rows.Count; i++)
        {
            if (i % 2 == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(rows[i], i / 2);
            Grid.SetColumn(rows[i], i % 2);
            grid.Children.Add(rows[i]);
        }

        groupPanel.Children.Add(grid);
        return groupPanel;
    }

    private Border CreateByteFieldRow(ByteField field)
    {
        var textBox = new TextBox();
        textBox.GotFocus += (_, _) => ShowHint(field.HintKey);
        _byteBoxes.Add((field, textBox));
        _tabOrder.Add(textBox);
        return CreateFieldRow(Loc.T(field.LabelKey), textBox, field.HintKey);
    }

    private static Border CreateFieldRow(string label, TextBox textBox, string? hintKey)
    {
        string? tooltip = hintKey == null ? null : Loc.T(hintKey);
        textBox.ToolTip = tooltip;
        return EditorTheme.FormRow(label, textBox, 96, tooltip);
    }

    private Border CreateButtonPanel()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_statusText, 0);
        grid.Children.Add(_statusText);

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

        Grid.SetColumn(buttonStack, 1);
        grid.Children.Add(buttonStack);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
    }

    private void LoadBuildingData()
    {
        _coordinateBox.Text = _building.Coordinate.ToString(CultureInfo.InvariantCulture);
        _nameBox.Text = _building.Name.ToString(CultureInfo.InvariantCulture);
        foreach (var (field, box) in _byteBoxes)
            box.Text = field.Get(_building).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>底部提示栏显示字段说明（没有说明时清空）。</summary>
    private void ShowHint(string? hintKey)
    {
        _statusText.Text = hintKey == null ? "" : Loc.T(hintKey);
        _statusText.Foreground = EditorTheme.TextMuted;
    }

    /// <summary>校验失败：选中出错的输入框，并在底部提示原因（先移焦点，免得焦点事件把提示换成字段说明）。</summary>
    private void ShowFieldError(string message, TextBox box)
    {
        box.Focus();
        box.SelectAll();
        _statusText.Text = message;
        _statusText.Foreground = EditorTheme.Danger;
    }

    private bool TryReadByte(ByteField field, TextBox box, out byte value)
    {
        value = 0;
        string label = Loc.T(field.LabelKey);
        string text = box.Text.Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            ShowFieldError(Loc.F("Assist.Building.Error.NotInteger", label, text), box);
            return false;
        }
        if (number < 0 || number > field.Max)
        {
            ShowFieldError(Loc.F("Assist.Building.Error.OutOfRange", label, number, 0, field.Max), box);
            return false;
        }

        value = (byte)number;
        return true;
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

    /// <summary>
    /// 确定：先校验全部数字字段，最后解析名称（可能新建城市名条目），任何一项无效都保持窗口打开。
    /// 坐标不在这里改，由选中的格子决定。
    /// </summary>
    private void ConfirmAndClose()
    {
        var result = _building;
        foreach (var (field, box) in _byteBoxes)
        {
            if (!TryReadByte(field, box, out byte value)) return;
            field.Set(ref result, value);
        }

        if (!BuildingNameResolver.TryResolve(_nameBox.Text, out ushort nameId, out string? nameError))
        {
            ShowFieldError(nameError ?? Loc.T("Common.InvalidInput"), _nameBox);
            return;
        }
        result.Name = nameId;

        _building = result;
        _confirmed = true;
        PlayFadeOutAndClose();
    }

    private void CancelAndClose()
    {
        _confirmed = false;
        PlayFadeOutAndClose();
    }
}
