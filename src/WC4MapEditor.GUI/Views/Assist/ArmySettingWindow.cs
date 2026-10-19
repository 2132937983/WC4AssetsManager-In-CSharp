using System.Globalization;
using System.Windows.Controls.Primitives;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 单位设置对话框的公共部分（v1 <see cref="ArmySettingWindow"/> / v3 <see cref="ArmySettingV3Window"/>）：
/// 左侧将领信息面板，右侧按分组排列的字段表单。外观走 <see cref="EditorTheme"/>。
/// <para>
/// 表单由子类的 <see cref="CreateFieldGroups"/> 描述：每个字段带存储范围、提示、下拉选项或搜索选择器，
/// 通过 Get / Set 委托读写子类持有的单位副本。对话框只改表里的字段，其余字段（含保留字节）原样保留，
/// 确定后由子类整体返回副本。
/// </para>
/// <para>
/// 输入实时校验：不是整数或超出存储范围为错误（红色，阻止确定，不会悄悄写成 0）；
/// 违反游戏规则（<see cref="ArmyRules"/>）为提示（黄色，不阻止）。
/// 按键：Enter 下一项，Ctrl+Enter 确定，Esc 取消。
/// </para>
/// </summary>
public abstract class ArmySettingWindowBase : Window
{
    private const double LabelWidth = 96;

    private readonly List<FieldEditor> _editors = new();
    private readonly ArmyGeneralPanel _generalPanel = new();
    private readonly TextBlock _status = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private Button? _confirmButton;
    private bool _loading;
    private bool _isClosing;

    /// <summary>是否按了确定（字段已写回子类的单位副本）。</summary>
    public bool Confirmed { get; private set; }

    protected ArmySettingWindowBase(string title)
    {
        EditorTheme.Apply(this);

        Title = title;
        Width = 840;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        // 淡入淡出靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Opacity = 0;

        _generalPanel.FillRequested += FillFromGeneral;
        Loaded += (_, _) => OnLoaded();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ================================================================== 子类接口

    /// <summary>字段表，分组与字段顺序即界面顺序。由 <see cref="BuildContent"/> 调用一次。</summary>
    protected abstract IReadOnlyList<ArmyFieldGroup> CreateFieldGroups();

    /// <summary>子类在构造函数里放好单位副本后调用：按字段表生成界面、载入数值并校验一次。</summary>
    protected void BuildContent()
    {
        Content = CreateWindowContent();
        _loading = true;
        foreach (var editor in _editors) editor.Load();
        _loading = false;
        Revalidate();
    }

    /// <summary>
    /// 表单里的一个整数字段。<see cref="Min"/> / <see cref="Max"/> 是存储宽度允许的范围（超出即为输入错误），
    /// 游戏规则另由 <see cref="Rule"/> 关联到 <see cref="ArmyRules"/>。<see cref="Set"/> 为 null 表示只读。
    /// </summary>
    protected sealed record ArmyField(string Label, int Min, int Max, Func<int> Get, Action<int>? Set)
    {
        /// <summary>字段说明（字段名与输入框的悬停提示）。</summary>
        public string? Tooltip { get; init; }

        /// <summary>对应的规则字段：规则提示显示在这个字段下面，"按将领填写"也按它找目标字段。</summary>
        public ArmyRuleField? Rule { get; init; }

        /// <summary>取值含义（可编辑下拉框，"值 - 名称"，也可以直接输入列表外的数值）。</summary>
        public IReadOnlyList<ArmyFieldOption>? Options { get; init; }

        /// <summary>搜索选择器的候选项（输入框右侧的 ▼），条目多时用它代替下拉框。</summary>
        public Func<IReadOnlyList<ArmyFieldOption>>? PickerItems { get; init; }

        /// <summary>当前值的说明，显示在输入框下面（如兵种名）。</summary>
        public Func<int, string>? Describe { get; init; }

        /// <summary>只读字段的显示文本。</summary>
        public Func<int, string>? Format { get; init; }
    }

    protected readonly record struct ArmyFieldOption(int Value, string Name);

    protected sealed record ArmyFieldGroup(string Title, IReadOnlyList<ArmyField> Fields);

    // ================================================================== 字段工厂（v1 / v3 共用）

    protected static ArmyField ByteField(string label, Func<int> get, Action<int> set)
        => new(label, byte.MinValue, byte.MaxValue, get, set);

    protected static ArmyField Int16Field(string label, Func<int> get, Action<int> set)
        => new(label, short.MinValue, short.MaxValue, get, set);

    protected static ArmyField Int32Field(string label, Func<int> get, Action<int> set)
        => new(label, int.MinValue, int.MaxValue, get, set);

    /// <summary>坐标：只读，显示格子序号。单位的位置由放置的格子决定，改这里不会移动单位。</summary>
    protected static ArmyField CoordinateField(Func<int> getIndex)
        => new(Loc.T("Assist.Army.Field.Coordinate"), 0, ushort.MaxValue, getIndex, null)
        {
            Format = index => Loc.F("Assist.Army.CoordinateValue", index),
            Tooltip = Loc.T("Assist.Army.Hint.Coordinate")
        };

    protected static ArmyField UnitTypeField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.UnitType"), get, set) with
        {
            Rule = ArmyRuleField.UnitType,
            Tooltip = Loc.T("Assist.Army.Hint.UnitType"),
            Describe = DescribeUnitType,
            PickerItems = UnitTypePickerItems
        };

    protected static ArmyField LevelField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Common.Level"), get, set) with
        {
            Rule = ArmyRuleField.Level,
            Tooltip = Loc.T("Assist.Army.Hint.Level")
        };

    protected static ArmyField OrganizationField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.Organization"), get, set) with
        {
            Rule = ArmyRuleField.Organization,
            Tooltip = Loc.T("Assist.Army.Hint.Organization")
        };

    protected static ArmyField DirectionField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.Direction"), get, set) with
        {
            Rule = ArmyRuleField.Direction,
            Options =
            [
                new(0, Loc.T("Assist.Army.Option.Direction.0")),
                new(1, Loc.T("Assist.Army.Option.Direction.1"))
            ]
        };

    protected static ArmyField HealthBonusField(Func<int> get, Action<int> set)
        => Int16Field(Loc.T("Assist.Army.Field.HealthBonus"), get, set) with
        {
            Rule = ArmyRuleField.HealthBonus,
            Tooltip = Loc.T("Assist.Army.Hint.HealthBonus")
        };

    protected static ArmyField GeneralField(Func<int> get, Action<int> set)
        => Int16Field(Loc.T("Assist.Army.Field.General"), get, set) with
        {
            Rule = ArmyRuleField.General,
            Tooltip = Loc.T("Assist.Army.Hint.General"),
            PickerItems = GeneralPickerItems
        };

    protected static ArmyField RankField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.Rank"), get, set) with
        {
            Rule = ArmyRuleField.Rank,
            Tooltip = Loc.T("Assist.Army.Hint.Rank")
        };

    /// <summary>将领 HP 等级：v1 叫爵位（Nobility），v3 叫 HP 等级，同一字节。</summary>
    protected static ArmyField HpLevelField(string label, Func<int> get, Action<int> set)
        => ByteField(label, get, set) with
        {
            Rule = ArmyRuleField.HpLevel,
            Tooltip = Loc.T("Assist.Army.Hint.HpLevel")
        };

    /// <summary>技能等级 1~5（<paramref name="slot"/> 为 1 起的序号）。</summary>
    protected static ArmyField SkillField(int slot, Func<int> get, Action<int> set)
        => ByteField(Loc.T($"Assist.Army.Field.SkillLevel{slot}"), get, set) with
        {
            Rule = ArmyRuleField.Skill1 + (slot - 1),
            Tooltip = Loc.F("Assist.Army.Hint.Skill", slot)
        };

    protected static ArmyField KeyPointField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.KeyPoint"), get, set) with
        {
            Rule = ArmyRuleField.KeyPoint,
            Options =
            [
                new(0, Loc.T("Assist.Army.Option.KeyPoint.0")),
                new(1, Loc.T("Assist.Army.Option.KeyPoint.1")),
                new(2, Loc.T("Assist.Army.Option.KeyPoint.2"))
            ]
        };

    protected static ArmyField PolicyField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.Policy"), get, set) with
        {
            Options =
            [
                new(0, Loc.T("Assist.Army.Option.Policy.0")),
                new(1, Loc.T("Assist.Army.Option.Policy.1")),
                new(2, Loc.T("Assist.Army.Option.Policy.2")),
                new(3, Loc.T("Assist.Army.Option.Policy.3")),
                new(4, Loc.T("Assist.Army.Option.Policy.4"))
            ]
        };

    protected static ArmyField PlanField(Func<int> get, Action<int> set)
        => Int16Field(Loc.T("Assist.Army.Field.Plan"), get, set) with
        {
            Tooltip = Loc.T("Assist.Army.Hint.Plan")
        };

    protected static ArmyField MoraleField(Func<int> get, Action<int> set)
        => ByteField(Loc.T("Assist.Army.Field.Morale"), get, set) with
        {
            Options =
            [
                new(0, Loc.T("Assist.Army.Option.Morale.0")),
                new(1, Loc.T("Assist.Army.Option.Morale.1")),
                new(253, Loc.T("Assist.Army.Option.Morale.253")),
                new(254, Loc.T("Assist.Army.Option.Morale.254")),
                new(255, Loc.T("Assist.Army.Option.Morale.255"))
            ]
        };

    private static string DescribeUnitType(int unitType) => unitType == 0
        ? Loc.T("Assist.Army.UnitType.None")
        : Army.ResolveUnitTypeName(unitType) ?? Loc.T("Assist.Army.UnitType.Unknown");

    private static IReadOnlyList<ArmyFieldOption> UnitTypePickerItems()
        => ArmySettingParser.Instance.Items
            .Select(unit => unit.Army)
            .Where(id => id > 0)
            .Distinct()
            .Order()
            .Select(id => new ArmyFieldOption(id, DescribeUnitType(id)))
            .ToList();

    private static IReadOnlyList<ArmyFieldOption> GeneralPickerItems()
    {
        var items = new List<ArmyFieldOption> { new(0, Loc.T("Assist.Army.NoGeneral")) };
        foreach (var general in GeneralSettingParser.Instance.All.Where(g => g.Id != 0).OrderBy(g => g.Id))
        {
            string name = string.IsNullOrWhiteSpace(general.Name) ? general.EName : general.Name;
            if (!string.IsNullOrWhiteSpace(general.EName) && general.EName != name)
                name = $"{name} ({general.EName})";
            items.Add(new ArmyFieldOption(general.Id, name));
        }
        return items;
    }

    // ================================================================== 布局

    private FrameworkElement CreateWindowContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(CreateTitleBar());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_generalPanel);
        var form = CreateForm();
        Grid.SetColumn(form, 1);
        body.Children.Add(form);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttonBar = CreateButtonBar();
        Grid.SetRow(buttonBar, 2);
        root.Children.Add(buttonBar);

        // 无边框窗口描一圈边，免得与底下的主窗口连成一片
        return new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = root
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
        grid.Children.Add(titleText);

        var closeButton = EditorTheme.GhostButton("✕", (_, _) => CancelAndClose());
        closeButton.Width = 32;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.Margin = new Thickness(0, 0, 6, 0);
        closeButton.ToolTip = Loc.T("Assist.Common.CloseEsc");
        closeButton.IsTabStop = false;
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

    private ScrollViewer CreateForm()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var group in CreateFieldGroups())
            panel.Children.Add(CreateGroup(group));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
    }

    /// <summary>一个分组：主题分组标题 + 两列表单行（字段多，单列会拉得太长）。</summary>
    private StackPanel CreateGroup(ArmyFieldGroup group)
    {
        var groupPanel = new StackPanel();
        groupPanel.Children.Add(EditorTheme.SectionHeader(group.Title));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < group.Fields.Count; i++)
        {
            if (i % 2 == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row = CreateFieldRow(group.Fields[i]);
            Grid.SetRow(row, i / 2);
            Grid.SetColumn(row, i % 2);
            grid.Children.Add(row);
        }

        groupPanel.Children.Add(grid);
        return groupPanel;
    }

    private Border CreateFieldRow(ArmyField field)
    {
        var input = CreateInput(field);
        var editor = new FieldEditor(field, input);
        _editors.Add(editor);

        UIElement framed = input;
        if (field.PickerItems != null)
        {
            // 输入框右侧留一个 ▼，点开带搜索栏的候选列表（将领 / 兵种条目太多，不适合下拉框）
            var pickerRow = new Grid();
            pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pickerRow.Children.Add(input);

            var pickButton = EditorTheme.Button("▼");
            pickButton.Width = 28;
            pickButton.Padding = new Thickness(0);
            pickButton.FontSize = 10;
            pickButton.Margin = new Thickness(4, 0, 0, 0);
            pickButton.ToolTip = Loc.T("Assist.Army.Picker.Pick");
            pickButton.IsTabStop = false;
            pickButton.Click += (_, _) => ShowPicker(pickButton, editor);
            Grid.SetColumn(pickButton, 1);
            pickerRow.Children.Add(pickButton);
            framed = pickerRow;
        }
        editor.Frame.Child = framed;

        var content = new StackPanel();
        content.Children.Add(editor.Frame);
        content.Children.Add(editor.Description);
        content.Children.Add(editor.Message);
        return EditorTheme.FormRow(field.Label, content, LabelWidth, field.Tooltip);
    }

    private Control CreateInput(ArmyField field)
    {
        if (field.Set == null)
        {
            return new TextBox
            {
                IsReadOnly = true,
                IsTabStop = false,
                Foreground = EditorTheme.TextMuted,
                ToolTip = field.Tooltip
            };
        }

        if (field.Options != null)
        {
            var combo = EditorTheme.ComboBox();
            combo.IsEditable = true;
            // 关掉自动补全：输入数字时不要被补成列表项，列表外的数值也要能直接输入
            combo.IsTextSearchEnabled = false;
            combo.ToolTip = field.Tooltip;
            foreach (var option in field.Options)
                combo.Items.Add(FormatOption(option.Value, option.Name));
            combo.SelectionChanged += (_, _) => OnInputChanged();
            combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnInputChanged()));
            return combo;
        }

        var box = new TextBox { ToolTip = field.Tooltip };
        box.TextChanged += (_, _) => OnInputChanged();
        return box;
    }

    private static string FormatOption(int value, string name)
        => $"{value.ToString(CultureInfo.InvariantCulture)} - {name}";

    private Border CreateButtonBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _status.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _confirmButton = EditorTheme.PrimaryButton(Loc.T("Common.Ok"), (_, _) => ConfirmAndClose());
        _confirmButton.MinWidth = 84;
        _confirmButton.ToolTip = Loc.T("Assist.Army.ConfirmTooltip");
        buttons.Children.Add(_confirmButton);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.MinWidth = 84;
        cancelButton.Margin = new Thickness(8, 0, 0, 0);
        cancelButton.ToolTip = Loc.T("Assist.Common.CancelEsc");
        buttons.Children.Add(cancelButton);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
    }

    // ================================================================== 校验

    private void OnInputChanged()
    {
        if (!_loading) Revalidate();
    }

    /// <summary>重新解析全部字段：先标出输入错误，再对能解析的值跑一遍游戏规则检查。</summary>
    private void Revalidate()
    {
        foreach (var editor in _editors) editor.Parse();

        var warnings = ArmyRules.Check(CollectRuleValues())
            .GroupBy(issue => issue.Field)
            .ToDictionary(g => g.Key, g => string.Join(Environment.NewLine, g.Select(issue => issue.Message)));

        int errorCount = 0, warningCount = 0;
        foreach (var editor in _editors)
        {
            string? warning = null;
            if (editor.Error == null && editor.Field.Rule is { } rule)
                warnings.TryGetValue(rule, out warning);
            editor.ShowState(warning);

            if (editor.Error != null) errorCount++;
            else if (warning != null) warningCount++;
        }

        UpdateStatus(errorCount, warningCount);

        if (FindEditor(ArmyRuleField.General) is { } general)
            _generalPanel.ShowGeneral(general.Value ?? general.Field.Get());
    }

    /// <summary>规则检查用当前输入；某个字段输入无效时用它原来的值。</summary>
    private ArmyRuleValues CollectRuleValues()
    {
        int Value(ArmyRuleField rule) => FindEditor(rule) is { } editor ? editor.Value ?? editor.Field.Get() : 0;

        return new ArmyRuleValues(
            Value(ArmyRuleField.UnitType), Value(ArmyRuleField.Level), Value(ArmyRuleField.Organization),
            Value(ArmyRuleField.Direction), Value(ArmyRuleField.HealthBonus),
            Value(ArmyRuleField.General), Value(ArmyRuleField.Rank), Value(ArmyRuleField.HpLevel),
            Value(ArmyRuleField.Skill1), Value(ArmyRuleField.Skill2), Value(ArmyRuleField.Skill3),
            Value(ArmyRuleField.Skill4), Value(ArmyRuleField.Skill5),
            Value(ArmyRuleField.KeyPoint));
    }

    private FieldEditor? FindEditor(ArmyRuleField rule) => _editors.FirstOrDefault(e => e.Field.Rule == rule);

    private void UpdateStatus(int errorCount, int warningCount)
    {
        if (errorCount > 0)
        {
            _status.Text = Loc.F("Assist.Army.Status.Errors", errorCount);
            _status.Foreground = EditorTheme.Danger;
        }
        else if (warningCount > 0)
        {
            _status.Text = Loc.F("Assist.Army.Status.Warnings", warningCount);
            _status.Foreground = EditorTheme.Warning;
        }
        else
        {
            _status.Text = Loc.T("Assist.Army.Status.Keys");
            _status.Foreground = EditorTheme.TextMuted;
        }
    }

    // ================================================================== 将领

    /// <summary>
    /// "按将领填写"：军衔 = 将领军衔，HP 等级 = 将领 HP，技能等级 = 将领各技能的等级；
    /// 将领没有的技能槽填 0（与原版地图一致）。
    /// </summary>
    private void FillFromGeneral(GeneralSettingData general)
    {
        _loading = true;
        FindEditor(ArmyRuleField.Rank)?.SetValue(general.MilitaryRank);
        FindEditor(ArmyRuleField.HpLevel)?.SetValue(general.Hp);

        var skills = general.Skills ?? new List<int>();
        for (int slot = 0; slot < 5; slot++)
        {
            FindEditor(ArmyRuleField.Skill1 + slot)?.SetValue(
                slot < skills.Count ? GeneralSettingParser.CalculateSkillLevel(skills[slot]) : 0);
        }
        _loading = false;
        Revalidate();
    }

    /// <summary>
    /// 搜索选择器：首行是搜索栏，下方是"值 - 名称"列表；回车 / 双击回填到字段。
    /// （与军团设置窗口的国家选择器同一交互）
    /// </summary>
    private void ShowPicker(Button anchor, FieldEditor editor)
    {
        var items = editor.Field.PickerItems!();

        var searchHost = EditorTheme.SearchBox(Loc.T("Assist.Army.Picker.SearchHint"), out var searchBox);
        searchHost.Margin = new Thickness(6, 6, 6, 4);

        var listBox = new ListBox { MaxHeight = 320, Margin = new Thickness(6, 0, 6, 6) };

        var panel = new StackPanel();
        panel.Children.Add(searchHost);
        panel.Children.Add(listBox);

        var border = new Border
        {
            Child = panel,
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 }
        };
        // Popup 的内容不在窗口的可视树里，隐式样式要在它自己的根上再合并一次
        EditorTheme.Apply(border);

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Width = 300,
            Child = border
        };

        void Commit()
        {
            // 没手动选时取第一项，方便"搜出来直接回车"
            if (listBox.SelectedItem == null && listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
            if (listBox.SelectedItem is ListBoxItem { Tag: int value })
            {
                popup.IsOpen = false;
                editor.SetValue(value);
                editor.Input.Focus();
            }
        }

        void Fill(string keyword)
        {
            listBox.Items.Clear();
            foreach (var item in items)
            {
                string text = FormatOption(item.Value, item.Name);
                if (keyword.Length > 0 && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                listBox.Items.Add(new ListBoxItem { Content = text, Tag = item.Value });
            }
            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        searchBox.TextChanged += (_, _) => Fill(searchBox.Text.Trim());
        searchBox.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when listBox.Items.Count > 0:
                    listBox.Focus();
                    listBox.SelectedIndex = 0;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Commit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        };
        listBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        };
        listBox.MouseDoubleClick += (_, _) => Commit();

        Fill(string.Empty);
        popup.IsOpen = true;
        searchBox.Focus();
    }

    // ================================================================== 按键 / 关闭

    private void OnLoaded()
    {
        PlayFadeInAnimation();

        // 焦点放到第一个可编辑字段，直接用键盘逐项修改
        if (_editors.FirstOrDefault(e => e.Field.Set != null)?.Input is { } first)
            FocusInput(first);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 下拉框展开时 Enter / Esc 交给下拉框自己（选中 / 收起）
        if (_editors.Any(editor => editor.Input is ComboBox { IsDropDownOpen: true })) return;

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
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
                 && _editors.FindIndex(editor => editor.Input.IsKeyboardFocusWithin) is var index and >= 0)
        {
            // Enter：跳到下一个可编辑字段，最后一个字段之后到"确定"按钮。
            // 按字段表找下一项，不用 MoveFocus：从可编辑下拉框内部的输入框出发时 MoveFocus 会丢失焦点
            e.Handled = true;
            var next = _editors.Skip(index + 1).FirstOrDefault(editor => editor.Field.Set != null);
            if (next != null) FocusInput(next.Input);
            else _confirmButton?.Focus();
        }
    }

    /// <summary>把键盘焦点放进字段的输入控件并全选（可编辑下拉框要聚焦到它模板里的输入框）。</summary>
    private static void FocusInput(Control input)
    {
        input.BringIntoView();
        var box = input is ComboBox { IsEditable: true } combo
            ? combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox
            : input as TextBox;
        if (box == null)
        {
            input.Focus();
            return;
        }
        box.Focus();
        box.SelectAll();
    }

    private void ConfirmAndClose()
    {
        Revalidate();

        var firstError = _editors.FirstOrDefault(e => e.Error != null);
        if (firstError != null)
        {
            FocusInput(firstError.Input);
            return;
        }

        foreach (var editor in _editors)
        {
            if (editor.Field.Set is { } set && editor.Value is int value)
                set(value);
        }

        Confirmed = true;
        PlayFadeOutAndClose();
    }

    private void CancelAndClose()
    {
        Confirmed = false;
        PlayFadeOutAndClose();
    }

    private void PlayFadeInAnimation()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void PlayFadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    // ================================================================== 字段状态

    /// <summary>一个字段的界面与状态：输入控件、外框（错误 / 提示着色）、说明行、错误 / 提示行、最近一次解析出的值。</summary>
    private sealed class FieldEditor
    {
        public FieldEditor(ArmyField field, Control input)
        {
            Field = field;
            Input = input;
        }

        public ArmyField Field { get; }

        /// <summary>TextBox，或带选项的可编辑 ComboBox。</summary>
        public Control Input { get; }

        /// <summary>包住输入控件的外框：平时透明，出错 / 有提示时着色（下拉框模板不吃 BorderBrush，所以统一用外框）。</summary>
        public Border Frame { get; } = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            BorderBrush = Brushes.Transparent
        };

        public TextBlock Description { get; } = new()
        {
            FontSize = 11,
            Foreground = EditorTheme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };

        public TextBlock Message { get; } = new()
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };

        /// <summary>解析出的值；输入无效时为 null。</summary>
        public int? Value { get; private set; }

        /// <summary>输入错误（非整数 / 超出存储范围）；没有错误时为 null。</summary>
        public string? Error { get; private set; }

        private string Text => Input switch
        {
            ComboBox combo => combo.Text ?? "",
            TextBox box => box.Text,
            _ => ""
        };

        /// <summary>显示单位副本里的当前值。</summary>
        public void Load()
        {
            int value = Field.Get();
            if (Field.Set == null && Input is TextBox readOnly)
                readOnly.Text = Field.Format?.Invoke(value) ?? value.ToString(CultureInfo.InvariantCulture);
            else
                SetValue(value);
        }

        /// <summary>把数值放进输入控件：下拉框里有对应选项时选中"值 - 名称"，否则直接显示数值。</summary>
        public void SetValue(int value)
        {
            string number = value.ToString(CultureInfo.InvariantCulture);
            switch (Input)
            {
                case ComboBox combo:
                    string prefix = number + " - ";
                    var match = combo.Items.OfType<string>().FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
                    if (match != null)
                    {
                        combo.SelectedItem = match;
                        combo.Text = match;
                    }
                    else
                    {
                        combo.SelectedIndex = -1;
                        combo.Text = number;
                    }
                    break;
                case TextBox box:
                    box.Text = number;
                    break;
            }
        }

        /// <summary>解析输入（"值 - 名称"取前面的数字），检查存储范围，并刷新说明行。</summary>
        public void Parse()
        {
            if (Field.Set == null)
            {
                Value = Field.Get();
                Error = null;
                return;
            }

            string text = Text.Trim();
            int dash = text.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) text = text[..dash];

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                Value = null;
                Error = Loc.F("Assist.ListEditor.NotInteger", Field.Label);
            }
            else if (value < Field.Min || value > Field.Max)
            {
                Value = null;
                Error = Loc.F("Assist.ListEditor.OutOfRange", Field.Label, Field.Min, Field.Max);
            }
            else
            {
                Value = value;
                Error = null;
            }

            if (Field.Describe != null)
            {
                Description.Text = Value is int current ? Field.Describe(current) : "";
                Description.Visibility = Description.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>按错误（红）/ 规则提示（黄）给外框与消息行着色。</summary>
        public void ShowState(string? warning)
        {
            string? message = Error ?? warning;
            Brush? color = Error != null ? EditorTheme.Danger : warning != null ? EditorTheme.Warning : null;

            Frame.BorderBrush = color ?? Brushes.Transparent;
            Message.Text = message ?? "";
            Message.Foreground = color ?? EditorTheme.TextMuted;
            Message.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>
/// v1 / v2 单位（48 字节 <see cref="Army"/>）设置对话框：显示并修改传入单位的副本（新建或编辑都一样），
/// 确定后由 <see cref="ResultArmy"/> 返回；表单里没有的字段（保留字节、LegionId）原样保留。
/// </summary>
public sealed class ArmySettingWindow : ArmySettingWindowBase
{
    private Army _army;

    public ArmySettingWindow(Army army, bool isNew = false)
        : base(isNew ? Loc.T("Assist.Army.CreateTitle") : Loc.T("Assist.Army.EditTitle"))
    {
        _army = army;
        BuildContent();
    }

    /// <summary>编辑后的单位；未确定时为传入的原值。</summary>
    public Army ResultArmy => _army;

    protected override IReadOnlyList<ArmyFieldGroup> CreateFieldGroups() =>
    [
        new(Loc.T("Common.BasicInfo"),
        [
            CoordinateField(() => _army.CoordinateIndex),
            UnitTypeField(() => _army.UnitType, v => _army.UnitType = (byte)v),
            LevelField(() => _army.Level, v => _army.Level = (byte)v),
            OrganizationField(() => _army.Organization, v => _army.Organization = (byte)v),
            DirectionField(() => _army.Direction, v => _army.Direction = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Attributes"),
        [
            ByteField(Loc.T("Assist.Army.Field.Mobility"), () => _army.Mobility, v => _army.Mobility = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.BuiltRound"), () => _army.BuiltRound, v => _army.BuiltRound = (byte)v),
            Int16Field(Loc.T("Assist.Army.Field.Experience"), () => _army.Experience, v => _army.Experience = (short)v),
            HealthBonusField(() => _army.HealthBonus, v => _army.HealthBonus = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.CurrentHealth"), () => _army.CurrentHealth, v => _army.CurrentHealth = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.MaxHealth"), () => _army.MaxHealth, v => _army.MaxHealth = (short)v),
        ]),
        new(Loc.T("Assist.Army.Section.General"),
        [
            GeneralField(() => _army.General, v => _army.General = (short)v),
            RankField(() => _army.Rank, v => _army.Rank = (byte)v),
            HpLevelField(Loc.T("Assist.Army.Field.Nobility"), () => _army.Nobility, v => _army.Nobility = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Equipment"),
        [
            ByteField(Loc.T("Assist.Army.Field.Badge1"), () => _army.Badge1, v => _army.Badge1 = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Badge2"), () => _army.Badge2, v => _army.Badge2 = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Badge3"), () => _army.Badge3, v => _army.Badge3 = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Skills"),
        [
            SkillField(1, () => _army.SkillLevel1, v => _army.SkillLevel1 = (byte)v),
            SkillField(2, () => _army.SkillLevel2, v => _army.SkillLevel2 = (byte)v),
            SkillField(3, () => _army.SkillLevel3, v => _army.SkillLevel3 = (byte)v),
            SkillField(4, () => _army.SkillLevel4, v => _army.SkillLevel4 = (byte)v),
            SkillField(5, () => _army.SkillLevel5, v => _army.SkillLevel5 = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Others"),
        [
            KeyPointField(() => _army.KeyPoint, v => _army.KeyPoint = (byte)v),
            PolicyField(() => _army.Policy, v => _army.Policy = (byte)v),
            PlanField(() => _army.Plan, v => _army.Plan = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.ChangeRound"), () => _army.ChangeRound, v => _army.ChangeRound = (short)v),
            MoraleField(() => _army.Morale, v => _army.Morale = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Duration"), () => _army.Duration, v => _army.Duration = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Dialogue"), () => _army.Dialogue, v => _army.Dialogue = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.CanAttack"), () => _army.CanAttack, v => _army.CanAttack = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.OccupationEvent"), () => _army.OccupationEvent, v => _army.OccupationEvent = (byte)v)
                with { Tooltip = Loc.T("Assist.Army.Hint.OccupationEvent") },
        ]),
    ];
}
