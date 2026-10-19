using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Controls.Primitives;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 军团设置窗口 - 对齐 VB 版 Assist/LegionSetting（军团编辑模式 Q 键，<see cref="EditorKinds.LegionSetting"/>）。
/// <para>
/// 左侧为军团列表（颜色条 + 国旗 + 序号 / 行动顺序 / 国家 / 阵营），右侧为所选军团的字段编辑面板。
/// 字段改动后按 Enter 或"应用"提交；切换军团、关闭窗口时自动提交有效的改动（无效输入会留在当前军团并提示），
/// Esc 先撤回未提交的改动、再按一次关闭。提交只写回用户改过的字段，窗口打开期间别处（C/U/R/P 键、撤销）
/// 改动的其它字段不会被旧值覆盖；列表会跟着刷新。所有修改都经由军团模块并记录撤销。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.LegionSetting)]
public sealed class LegionSettingWindow : Window, IAssistEditor
{
    /// <summary>字段定义：显示名的翻译键 / Legion 字段名 / 是否为浮点 / 提示的翻译键（null = 与显示名相同）</summary>
    private sealed record FieldDef(string LabelKey, string FieldName, bool IsFloat, string? TipKey = null);

    /// <summary>颜色字段（R/G/B 三字节）在编辑面板中的虚拟键名</summary>
    private const string ColorFieldKey = "__color";

    /// <summary>
    /// 全部可编辑字段，顺序即界面顺序（对齐 VB 版 LegionSetting，末尾补上两版都没有开放的 AI 行为与核弹数量）。
    /// 静态数组里只放翻译键不放文本：类型初始化只跑一次，之后切换语言时文本还要能跟着变。
    /// </summary>
    private static readonly FieldDef[] FieldDefs =
    {
        new("Assist.Legion.Field.ActionId", nameof(Legion.ActionId), false),
        new("Assist.Legion.Field.CountryId", nameof(Legion.CountryId), false),
        new("Assist.Legion.Field.Camp", nameof(Legion.Camp), false),
        new("Assist.Legion.Field.InitialEconomy", nameof(Legion.InitialEconomy), false),
        new("Assist.Legion.Field.InitialIndustry", nameof(Legion.InitialIndustry), false),
        new("Assist.Legion.Field.InitialTech", nameof(Legion.InitialTech), false),
        new("Assist.Legion.Field.PlayerControlled", nameof(Legion.IsPlayerControlled), false, "Assist.Legion.PlayerControlledTooltip"),
        new("Assist.Legion.Field.DefeatCondition", nameof(Legion.DefeatCondition), false),
        new("Assist.Legion.Field.CountryHpRate", nameof(Legion.CountryHpRate), true),
        new("Assist.Legion.Field.CountryTaxRate", nameof(Legion.CountryTaxRate), true),
        new("Common.Color", ColorFieldKey, false, "Assist.Legion.ColorHexTooltip"),
        new("Assist.Legion.Field.TechLevel", nameof(Legion.InitialTechLevel), false),
        new("Assist.Legion.Field.Unknown1", nameof(Legion.Unknown1), false),
        new("Assist.Legion.Field.Unknown2", nameof(Legion.Unknown2), false),
        new("Assist.Legion.Field.Unknown3", nameof(Legion.Unknown3), false),
        new("Assist.Legion.Field.Unknown4", nameof(Legion.Unknown4), false),
        new("Assist.Legion.Field.LaserLevel", nameof(Legion.InitialLaserGunLevel), false),
        new("Assist.Legion.Field.AiBehavior", nameof(Legion.CountryAiBehavior), false),
        new("Assist.Legion.Field.AtomicBombs", nameof(Legion.AtomicBombCount), false),
        new("Assist.Legion.Field.HydrogenBombs", nameof(Legion.HydrogenBombCount), false),
        new("Assist.Legion.Field.NeutronBombs", nameof(Legion.NeutronBombCount), false),
        new("Assist.Legion.Field.AntimatterBombs", nameof(Legion.AntimatterBombCount), false),
    };

    private static string LabelOf(FieldDef def) => Loc.T(def.LabelKey);

    /// <summary>Legion 的公开字段索引，供读写</summary>
    private static readonly Dictionary<string, FieldInfo> LegionFields =
        typeof(Legion)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(float))
            .ToDictionary(f => f.Name);

    private readonly ListBox _legionListBox = new();
    private readonly StackPanel _editorPanel = new();
    private readonly Dictionary<string, TextBox> _fieldBoxes = new();

    /// <summary>每个输入框载入时的文本：提交时只写回与它不同的字段</summary>
    private readonly Dictionary<string, string> _loadedText = new();

    private readonly Border _colorPreview = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private Button? _applyButton;

    /// <summary>批量操作栏里共用的数值输入框（统一经济/工业/科技都用它）</summary>
    private readonly TextBox _batchValueBox = new();

    private AssistEditorContext _context = null!;
    private int _selectedIndex = -1;
    private bool _isSyncing;
    private bool _dirty;

    /// <summary>本窗口自己在改军团列表：期间的集合变更通知不当作"别处改动"</summary>
    private bool _isApplying;
    private bool _reloadQueued;

    /// <summary>排队中的刷新里是否有增删 / 移动 / 整体替换（军团序号可能已变）</summary>
    private bool _structuralChangeQueued;

    public LegionSettingWindow()
    {
        EditorTheme.Apply(this);

        Title = Loc.T("Assist.Legion.Title");
        Width = 660;
        Height = 680;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        WindowStyle = WindowStyle.None;
        // 淡入靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ShowInTaskbar = false;

        // 清掉上次"没找到国旗"的缓存：之后补上了国旗资源，重开窗口就能显示出来
        FlagImageLoader.ClearMisses();

        PreviewKeyDown += OnPreviewKeyDown;
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private MapData Map => _context.MapData;
    private LegionModifier Module => _context.Module<LegionModifier>()
        ?? throw new InvalidOperationException("LegionModifier is not registered");

    // ------------------------------------------------------------------ IAssistEditor

    void IAssistEditor.Attach(AssistEditorContext context)
    {
        _context = context;
        Content = CreateUI();
        Map.Legions.CollectionChanged += OnLegionsCollectionChanged;
        ReloadLegionList(InitialSelection(context.Request));
    }

    /// <summary>窗口已打开时又按了 Q：切到该格所属的军团（先提交当前改动）。</summary>
    void IAssistEditor.Retarget(EditorRequest request)
    {
        int index = LegionIndexAt(request.Col, request.Row);
        if (index >= 0 && index != _selectedIndex) _legionListBox.SelectedIndex = index;
    }

    /// <summary>首次打开时选中请求格所属的军团，没有则选中当前军团。</summary>
    private int InitialSelection(EditorRequest request)
    {
        int index = LegionIndexAt(request.Col, request.Row);
        return index >= 0 ? index : Module.SelectedLegionId - 1;
    }

    private int LegionIndexAt(int col, int row)
    {
        if (col < 0 || row < 0 || col >= Map.MapWidth || row >= Map.MapHeight) return -1;
        int belong = Map.GetBelongValue(col, row);
        return belong >= 0 && belong < Map.Legions.Count ? belong : -1;
    }

    // ------------------------------------------------------------------ UI

    private FrameworkElement CreateUI()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = CreateTitleBar();
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var body = CreateBody();
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var batch = CreateBatchBar();
        Grid.SetRow(batch, 2);
        grid.Children.Add(batch);

        var buttons = CreateButtonBar();
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        // 无边框窗口描一圈边，免得与底下的主窗口连成一片
        return new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    /// <summary>标题栏：按住可拖动窗口，右侧 ✕ 关闭。</summary>
    private Border CreateTitleBar()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = EditorTheme.Title(Loc.T("Assist.Legion.Title"));
        label.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(label, 0);
        panel.Children.Add(label);

        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 32;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(0, 0, 6, 0);
        close.ToolTip = Loc.T("Assist.Legion.CloseTooltip");
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);

        var bar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return bar;
    }

    private Grid CreateBody()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧：军团列表 + 增删 / 上下移按钮
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _legionListBox.Margin = new Thickness(4, 4, 4, 0);
        _legionListBox.SelectionChanged += OnLegionSelectionChanged;
        Grid.SetRow(_legionListBox, 0);
        leftGrid.Children.Add(_legionListBox);

        // 底部按钮（"+" / "-" 对齐 VB 版 LegionSetting；▲ / ▼ 调整军团顺序，领地与单位跟着军团走）
        var listButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8)
        };
        listButtons.Children.Add(CreateSmallButton(EditorTheme.Button("+", (_, _) => OnAddLegion()),
            Loc.T("Assist.Legion.AddLegion")));
        listButtons.Children.Add(CreateSmallButton(EditorTheme.DangerButton("-", (_, _) => OnDeleteLegion()),
            Loc.T("Assist.Legion.DeleteLegion")));
        listButtons.Children.Add(CreateSmallButton(EditorTheme.Button("▲", (_, _) => OnMoveLegion(-1)),
            Loc.T("Assist.Legion.MoveUpTooltip", "上移（领地与单位跟着军团走，行动顺序不变）")));
        listButtons.Children.Add(CreateSmallButton(EditorTheme.Button("▼", (_, _) => OnMoveLegion(+1)),
            Loc.T("Assist.Legion.MoveDownTooltip", "下移（领地与单位跟着军团走，行动顺序不变）")));
        Grid.SetRow(listButtons, 1);
        leftGrid.Children.Add(listButtons);

        var leftPanel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        leftPanel.Child = leftGrid;
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var splitter = EditorTheme.VSplitter();
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        // 右侧：字段编辑面板
        BuildEditorFields();

        var scroll = new ScrollViewer
        {
            Content = _editorPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 2);
        grid.Children.Add(scroll);

        return grid;
    }

    /// <summary>把主题按钮改成列表底部的小方块尺寸</summary>
    private static Button CreateSmallButton(Button button, string tooltip)
    {
        button.Width = 36;
        button.Padding = new Thickness(0, 3, 0, 3);
        button.FontSize = 14;
        button.FontWeight = FontWeights.Bold;
        button.Margin = new Thickness(3, 0, 3, 0);
        button.ToolTip = tooltip;
        return button;
    }

    private void BuildEditorFields()
    {
        _editorPanel.Children.Clear();
        _fieldBoxes.Clear();

        var header = EditorTheme.SectionHeader(Loc.T("Assist.Legion.Section.Fields"));
        header.Margin = new Thickness(0);
        _editorPanel.Children.Add(header);

        foreach (var def in FieldDefs)
        {
            string label = LabelOf(def);
            var box = new TextBox { IsEnabled = false, ToolTip = Loc.T(def.TipKey ?? def.LabelKey) };
            box.TextChanged += (_, _) => MarkDirty();
            UIElement content;

            if (def.FieldName == ColorFieldKey)
            {
                box.TextChanged += (_, _) => UpdateColorPreview();

                var colorRow = new Grid();
                colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Grid.SetColumn(box, 0);
                colorRow.Children.Add(box);

                // 色块显示的是军团自己的颜色（数据），不走主题色
                _colorPreview.Width = 44;
                _colorPreview.Height = 26;
                _colorPreview.BorderBrush = EditorTheme.BorderStrong;
                _colorPreview.BorderThickness = new Thickness(1);
                _colorPreview.CornerRadius = new CornerRadius(4);
                _colorPreview.Background = Brushes.Transparent;
                _colorPreview.Margin = new Thickness(6, 0, 0, 0);
                _colorPreview.Cursor = Cursors.Hand;
                _colorPreview.ToolTip = Loc.T("Assist.Legion.OpenColorPicker");
                _colorPreview.MouseLeftButtonDown += (_, _) => PickColor();

                Grid.SetColumn(_colorPreview, 1);
                colorRow.Children.Add(_colorPreview);
                content = colorRow;
            }
            else if (def.FieldName == nameof(Legion.CountryId))
            {
                // 国家ID：输入框收窄，右侧让出一个按钮，点开带搜索栏的国家列表
                box.ToolTip = Loc.T("Assist.Legion.CountryIdTooltip");

                var countryRow = new Grid();
                countryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                countryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Grid.SetColumn(box, 0);
                countryRow.Children.Add(box);

                var pickButton = EditorTheme.Button("▼");
                pickButton.Width = 30;
                pickButton.Padding = new Thickness(0);
                pickButton.FontSize = 10;
                pickButton.Margin = new Thickness(6, 0, 0, 0);
                pickButton.ToolTip = Loc.T("Assist.Legion.PickCountry");
                pickButton.Click += (_, _) => ShowCountryPicker(pickButton, box);

                Grid.SetColumn(pickButton, 1);
                countryRow.Children.Add(pickButton);
                content = countryRow;
            }
            else
            {
                content = box;
            }

            _fieldBoxes[def.FieldName] = box;
            _editorPanel.Children.Add(EditorTheme.FormRow(label, content, 110));
        }
    }

    /// <summary>
    /// 弹出国家选择菜单：首行是搜索栏，下方是按 ID 排序的国家列表（ID - 名称）。
    /// 国家名取 string table 的 <c>country_{id}</c>，不取 CountrySettings.json 的 Name。
    /// 回车 / 双击确认后回填到目标输入框（之后照常按 Enter / 应用提交）。
    /// </summary>
    private void ShowCountryPicker(Button anchor, TextBox target)
    {
        var countries = WC4MapEditor.Core.Parsers.Country.CountrySettingParser.Instance.Countries
            .OrderBy(c => c.Id)
            .ToList();

        var displayNames = new Dictionary<int, string>();
        foreach (var c in countries)
        {
            var name = ConfigManager.Instance.GetStringTableValue($"country_{c.Id}");
            if (string.IsNullOrWhiteSpace(name)) name = Loc.F("Assist.Legion.CountryFallback", c.Id);
            displayNames[c.Id] = name;
        }

        // 第一行：搜索栏（按国家名或 ID 过滤）
        var searchHost = EditorTheme.SearchBox(Loc.T("Assist.Legion.CountrySearchHint"), out var searchBox);
        searchHost.Margin = new Thickness(6, 6, 6, 4);

        var listBox = new ListBox
        {
            MaxHeight = 300,
            Margin = new Thickness(6, 0, 6, 6)
        };

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

            if (listBox.SelectedItem is ListBoxItem item && item.Tag is int id)
            {
                target.Text = id.ToString(CultureInfo.InvariantCulture);
                popup.IsOpen = false;
            }
        }

        void FillList(string keyword)
        {
            listBox.Items.Clear();

            foreach (var country in countries)
            {
                string display = $"{country.Id} - {displayNames[country.Id]}";
                if (keyword.Length > 0 &&
                    display.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                listBox.Items.Add(new ListBoxItem { Content = display, Tag = country.Id });
            }

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        searchBox.TextChanged += (_, _) => FillList(searchBox.Text.Trim());

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

        FillList(string.Empty);

        popup.IsOpen = true;
        searchBox.Focus();
    }

    /// <summary>
    /// 批量操作栏：一个共用的数值输入框 + 三个"统一"按钮 + 两个直接执行的按钮。
    /// 这一栏作用于【所有军团】，与右侧编辑面板（只改当前选中的那一个）语义不同，所以放在编辑区之外。
    /// </summary>
    private Border CreateBatchBar()
    {
        // 英文按钮比中文长，一行放不下时让它折行，而不是被裁掉
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = EditorTheme.Muted(Loc.T("Assist.Legion.BatchLabel"));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 6, 0);
        panel.Children.Add(label);

        _batchValueBox.Width = 70;
        _batchValueBox.VerticalAlignment = VerticalAlignment.Center;
        _batchValueBox.ToolTip = Loc.T("Assist.Legion.BatchValueTooltip");
        _batchValueBox.Margin = new Thickness(0, 2, 8, 2);
        panel.Children.Add(_batchValueBox);

        void AddBatchButton(string text, Action onClick)
        {
            var button = EditorTheme.ToolButton(text, (_, _) => onClick());
            button.Margin = new Thickness(0, 2, 6, 2);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.ToolTip = text;
            panel.Children.Add(button);
        }

        AddBatchButton(Loc.T("Assist.Legion.UnifyEconomy"),
            () => ApplyBatchValue(Loc.T("Assist.Legion.Economy"), Module.SetAllLegionsEconomy));
        AddBatchButton(Loc.T("Assist.Legion.UnifyIndustry"),
            () => ApplyBatchValue(Loc.T("Assist.Legion.Industry"), Module.SetAllLegionsIndustry));
        AddBatchButton(Loc.T("Assist.Legion.UnifyTech"),
            () => ApplyBatchValue(Loc.T("Assist.Legion.Tech"), Module.SetAllLegionsTech));
        AddBatchButton(Loc.T("Assist.Legion.RandomizeLevels"), OnRandomizeAllLegionLevels);
        AddBatchButton(Loc.T("Assist.Legion.ApplyDefaultColors"), OnApplyDefaultColors);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            Child = panel
        };
    }

    private Border CreateButtonBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_status, 0);
        grid.Children.Add(_status);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var reset = CreateBarButton(EditorTheme.Button(Loc.T("Common.Reset"), (_, _) => RevertPending()));
        reset.ToolTip = Loc.T("Assist.Legion.ResetTooltip", "撤回未提交的修改（Esc）");
        panel.Children.Add(reset);
        _applyButton = CreateBarButton(EditorTheme.PrimaryButton(Loc.T("Common.Apply"), (_, _) => CommitPending()));
        _applyButton.ToolTip = Loc.T("Assist.Legion.ApplyTooltip", "提交当前军团的修改（Enter）");
        _applyButton.IsEnabled = false;
        panel.Children.Add(_applyButton);
        panel.Children.Add(CreateBarButton(EditorTheme.Button(Loc.T("Common.Close"), (_, _) => Close())));
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid
        };
    }

    private static Button CreateBarButton(Button button)
    {
        button.MinWidth = 80;
        button.Margin = new Thickness(8, 0, 0, 0);
        return button;
    }

    // -------------------------------------------------------------- 列表

    /// <summary>重新填充列表并选中指定项（越界时就近选择）。</summary>
    private void ReloadLegionList(int selectIndex)
    {
        _isSyncing = true;
        try
        {
            _legionListBox.Items.Clear();
            for (int i = 0; i < Map.Legions.Count; i++)
                _legionListBox.Items.Add(CreateLegionListItem(i, Map.Legions[i]));

            if (_legionListBox.Items.Count > 0)
            {
                _legionListBox.SelectedIndex = Math.Clamp(selectIndex, 0, _legionListBox.Items.Count - 1);
                _legionListBox.ScrollIntoView(_legionListBox.SelectedItem);
            }
        }
        finally
        {
            _isSyncing = false;
        }
        ShowSelected();
    }

    /// <summary>只重建列表项（保留选中项与输入框里未提交的内容），用于别处改了军团而本窗口有未提交改动时。</summary>
    private void RefreshListItems()
    {
        _isSyncing = true;
        try
        {
            int keep = _legionListBox.SelectedIndex;
            _legionListBox.Items.Clear();
            for (int i = 0; i < Map.Legions.Count; i++)
                _legionListBox.Items.Add(CreateLegionListItem(i, Map.Legions[i]));
            if (keep >= 0 && keep < _legionListBox.Items.Count) _legionListBox.SelectedIndex = keep;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// 构建单个军团列表项：压缩后的颜色条 + 国旗 + 两行文本（序号、行动顺序、国家名、阵营；玩家军团带标记）。
    /// </summary>
    private FrameworkElement CreateLegionListItem(int index, Legion legion)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // 颜色条
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });   // 国旗
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 军团颜色条：宽度压缩到 5px，把位置让给国旗（颜色是军团数据，不走主题）
        var colorBar = new Border
        {
            Width = 5,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(legion.ColorR, legion.ColorG, legion.ColorB)),
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.F("Assist.Legion.ColorTooltip", $"{legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2}")
        };
        Grid.SetColumn(colorBar, 0);
        row.Children.Add(colorBar);

        var flag = FlagImageLoader.Load(legion.CountryId);
        FrameworkElement flagElement;
        if (flag != null)
        {
            flagElement = new Image
            {
                Source = flag,
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = $"flag_{legion.CountryId}.png"
            };
        }
        else
        {
            var noFlag = EditorTheme.Muted(Loc.T("Assist.Legion.NoFlag"));
            noFlag.FontSize = 10;
            noFlag.VerticalAlignment = VerticalAlignment.Center;
            noFlag.Margin = new Thickness(5, 0, 0, 0);
            noFlag.ToolTip = Loc.F("Assist.Legion.FlagNotFound", legion.CountryId);
            flagElement = noFlag;
        }
        Grid.SetColumn(flagElement, 1);
        row.Children.Add(flagElement);

        string text = Loc.F("Assist.Legion.ListItem", index + 1, legion.ActionId,
            WC4MapEditor.Core.Config.CountryNameProvider.Get(legion.CountryId), legion.Camp);
        if (legion.IsPlayer) text += Loc.T("Assist.Legion.PlayerMark", "  [玩家]");
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(label, 2);
        row.Children.Add(label);

        return row;
    }

    private void OnLegionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing) return;
        int target = _legionListBox.SelectedIndex;
        if (target == _selectedIndex) return;

        // 切换前自动提交；输入无效则留在当前军团
        if (_dirty && !CommitPending())
        {
            _isSyncing = true;
            _legionListBox.SelectedIndex = _selectedIndex;
            _isSyncing = false;
            return;
        }
        ShowSelected();
    }

    /// <summary>把列表当前选中的军团显示到编辑面板。</summary>
    private void ShowSelected()
    {
        int index = _legionListBox.SelectedIndex;
        _selectedIndex = index >= 0 && index < Map.Legions.Count ? index : -1;
        SetEditorEnabled(_selectedIndex >= 0);

        _isSyncing = true;
        try
        {
            _loadedText.Clear();
            foreach (var def in FieldDefs)
            {
                if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;
                string text = _selectedIndex >= 0 ? FormatField(Map.Legions[_selectedIndex], def) : string.Empty;
                box.Text = text;
                _loadedText[def.FieldName] = text;
            }
        }
        finally
        {
            _isSyncing = false;
        }

        _dirty = false;
        UpdateColorPreview();
        UpdateApplyButton();
    }

    private static string FormatField(Legion legion, FieldDef def)
    {
        if (def.FieldName == ColorFieldKey)
            return $"{legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2}";
        if (!LegionFields.TryGetValue(def.FieldName, out var field)) return string.Empty;
        return Convert.ToString(field.GetValue(legion), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void SetEditorEnabled(bool enabled)
    {
        foreach (var box in _fieldBoxes.Values)
            box.IsEnabled = enabled;
    }

    private void MarkDirty()
    {
        if (_isSyncing || _selectedIndex < 0) return;
        _dirty = true;
        UpdateApplyButton();
    }

    private void UpdateApplyButton()
    {
        if (_applyButton != null) _applyButton.IsEnabled = _dirty && _selectedIndex >= 0;
    }

    private void SetStatus(string? message, bool isError = false)
    {
        _status.Text = message ?? string.Empty;
        _status.Foreground = isError ? EditorTheme.Danger : EditorTheme.TextMuted;
    }

    // -------------------------------------------------------------- 提交 / 撤回

    /// <summary>
    /// 提交当前军团的修改：只把用户改过的输入框写到军团的<b>当前</b>数据上（别处改过的其它字段不会被旧值覆盖），
    /// 经军团模块校验（国家 ID / 行动顺序不能与其它军团重复）后写回，并记录撤销。
    /// 没有改动或提交成功返回 true；输入无效返回 false 并在状态栏说明原因（数据不变）。
    /// </summary>
    private bool CommitPending()
    {
        if (!_dirty || _selectedIndex < 0 || _selectedIndex >= Map.Legions.Count) return true;

        object boxed = Map.Legions[_selectedIndex];
        var errors = new List<string>();

        foreach (var def in FieldDefs)
        {
            if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;
            string text = box.Text.Trim();
            if (_loadedText.TryGetValue(def.FieldName, out var loaded) && text == loaded.Trim()) continue;

            if (def.FieldName == ColorFieldKey)
            {
                if (!TryParseColor(text, out byte r, out byte g, out byte b))
                {
                    errors.Add(Loc.T("Assist.Legion.ColorFormatError"));
                    continue;
                }

                var legion = (Legion)boxed;
                legion.ColorR = r;
                legion.ColorG = g;
                legion.ColorB = b;
                boxed = legion;
                continue;
            }

            if (!LegionFields.TryGetValue(def.FieldName, out var field)) continue;

            if (def.IsFloat)
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    errors.Add(Loc.F("Assist.Legion.NeedNumber", LabelOf(def)));
                else
                    field.SetValue(boxed, f);
            }
            else
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    errors.Add(Loc.F("Assist.Legion.NeedInteger", LabelOf(def)));
                else
                    field.SetValue(boxed, v);
            }
        }

        if (errors.Count > 0)
        {
            SetStatus(string.Join("；", errors), isError: true);
            return false;
        }

        // 按列表索引写回（按 CountryId 查表的 UpdateLegion 会把改了国家 ID 的军团当成新军团插入）
        int index = _selectedIndex;
        var updated = (Legion)boxed;
        ModifierResult result = default;
        RunOwnChange(() => _context.Editor.RecordCollectionChange(
            Loc.F("Assist.Legion.UndoEdit", index + 1), Map.Legions, () => result = Module.UpdateLegionAt(index, updated)));
        if (!result.Success)
        {
            SetStatus(result.Message ?? Loc.T("Assist.Legion.ApplyFailed"), isError: true);
            return false;
        }

        _context.NotifyChanged(EditorDataScope.Legions);
        RefreshListItems();
        ShowSelected();
        SetStatus(Loc.T("Assist.Legion.Applied", "已应用"));
        return true;
    }

    /// <summary>撤回未提交的修改（重新载入当前军团）。</summary>
    private void RevertPending()
    {
        if (_selectedIndex < 0) return;
        bool wasDirty = _dirty;
        ShowSelected();
        if (wasDirty) SetStatus(Loc.T("Assist.Legion.Reverted", "已撤回未提交的修改"));
    }

    /// <summary>执行本窗口自己发起的军团改动：期间的集合变更通知不当作"别处改动"。</summary>
    private void RunOwnChange(Action change)
    {
        _isApplying = true;
        try { change(); }
        finally { _isApplying = false; }
    }

    /// <summary>
    /// 军团列表在别处被改了（C/U/R/P 键、撤销 / 重做、脚本……）：合并成一次刷新。
    /// 只是字段被改（替换元素）且有未提交改动时，只刷新列表项，输入框保持用户的内容
    /// （提交时只写改过的字段，不会把别处的修改覆盖回去）；否则整体重载 ——
    /// 增删 / 移动后军团序号可能已变，未提交的改动无法再对应到原来的军团，只能撤回。
    /// </summary>
    private void OnLegionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isApplying) return;
        _structuralChangeQueued |= e.Action != NotifyCollectionChangedAction.Replace;
        if (_reloadQueued) return;
        _reloadQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            bool structural = _structuralChangeQueued;
            _reloadQueued = false;
            _structuralChangeQueued = false;

            if (_dirty && !structural)
            {
                RefreshListItems();
                return;
            }
            bool dropped = _dirty;
            ReloadLegionList(_selectedIndex);
            if (dropped) SetStatus(Loc.T("Assist.Legion.PendingDropped", "军团列表在别处被增删或调整了顺序，未提交的修改已撤回"), isError: true);
        }, DispatcherPriority.Background);
    }

    // -------------------------------------------------------------- 增删 / 排序 / 批量

    /// <summary>新增军团：行动顺序 / 国家 ID 取现有最大值 +1，由 AI 控制，其余取默认值。</summary>
    private void OnAddLegion()
    {
        if (!CommitPending()) return;
        ModifierResult result = default;
        RunOwnChange(() => _context.Editor.RecordCollectionChange(Loc.T("Assist.Legion.UndoAdd", "新增军团"),
            Map.Legions, () => result = Module.AddLegion()));
        if (!result.Success)
        {
            SetStatus(result.Message ?? Loc.T("Assist.Legion.AddFailed"), isError: true);
            return;
        }

        _context.NotifyChanged(EditorDataScope.Legions);
        ReloadLegionList(Map.Legions.Count - 1);
        SetStatus(result.Message);
    }

    /// <summary>
    /// 删除当前军团（对齐 VB "-" 按钮）。确认框里列出受影响的格子与单位数量：
    /// 被删军团的归属与所属军团清空，后面军团的序号整体前移（军团模块统一改写），可撤销。
    /// </summary>
    private void OnDeleteLegion()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Map.Legions.Count)
        {
            SetStatus(Loc.T("Common.SelectItemFirst"), isError: true);
            return;
        }

        int index = _selectedIndex;
        var refs = Module.CountReferences(index);
        var confirm = MessageBox.Show(this,
            Loc.F("Assist.Legion.DeleteConfirm", index + 1) + "\n\n" +
            Loc.F("Assist.Legion.DeleteImpact", refs.Cells, refs.Units, refs.Others),
            Loc.T("Assist.Legion.DeleteConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _dirty = false;
        ModifierResult result = default;
        RunOwnChange(() => _context.Editor.RecordEntitiesSnapshot(Loc.F("Assist.Legion.UndoDelete", index + 1),
            () => result = Module.RemoveLegionAt(index)));
        if (!result.Success)
        {
            SetStatus(result.Message ?? Loc.T("Assist.Legion.DeleteFailed"), isError: true);
            return;
        }

        _context.NotifyChanged(EditorDataScope.Legions | EditorDataScope.Belongs | EditorDataScope.Entities);
        ReloadLegionList(Math.Min(index, Map.Legions.Count - 1));
        SetStatus(result.Message);
    }

    /// <summary>▲ / ▼：与相邻军团交换位置，归属与所有实体的所属军团序号随之改写，行动顺序不变。</summary>
    private void OnMoveLegion(int step)
    {
        int from = _selectedIndex;
        int to = from + step;
        if (from < 0 || to < 0 || to >= Map.Legions.Count) return;
        if (!CommitPending()) return;

        ModifierResult result = default;
        RunOwnChange(() => _context.Editor.RecordEntitiesSnapshot(Loc.F("Assist.Legion.UndoMove", from + 1, to + 1),
            () => result = Module.MoveLegion(from, to, renumberActionIds: false)));
        if (!result.Success)
        {
            SetStatus(result.Message, isError: true);
            return;
        }

        _context.NotifyChanged(EditorDataScope.Legions | EditorDataScope.Belongs | EditorDataScope.Entities);
        ReloadLegionList(to);
        SetStatus(result.Message);
    }

    /// <summary>把批量输入框里的整数应用到所有军团的指定字段（可撤销）。</summary>
    private void ApplyBatchValue(string fieldLabel, Func<int, ModifierResult> apply)
    {
        if (!int.TryParse(_batchValueBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            SetStatus(Loc.F("Assist.Legion.BatchNeedInteger", fieldLabel), isError: true);
            _batchValueBox.Focus();
            return;
        }

        RunLegionBatch(Loc.F("Assist.Legion.UndoUnify", fieldLabel, value), () => apply(value),
            Loc.F("Assist.Legion.UnifyFailed", fieldLabel));
    }

    private void OnRandomizeAllLegionLevels()
    {
        var confirm = MessageBox.Show(this,
            Loc.T("Assist.Legion.RandomizeConfirm"),
            Loc.T("Common.Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunLegionBatch(Loc.T("Assist.Legion.RandomizeLevels"), Module.RandomizeAllLegionLevels,
            Loc.T("Assist.Legion.RandomizeFailed"));
    }

    private void OnApplyDefaultColors()
        => RunLegionBatch(Loc.T("Assist.Legion.ApplyDefaultColors"), Module.ApplyDefaultColorsToAllLegions,
            Loc.T("Assist.Legion.ApplyDefaultColorsFailed"));

    /// <summary>
    /// 作用于所有军团的批量操作：先提交当前军团的改动，按军团列表快照记录撤销，然后整体刷新
    /// （颜色类操作尤其需要 —— 左侧列表的色块全都得跟着变），尽量保住原来的选中位置。
    /// </summary>
    private void RunLegionBatch(string undoText, Func<ModifierResult> operation, string failText)
    {
        if (!CommitPending()) return;
        ModifierResult result = default;
        RunOwnChange(() => _context.Editor.RecordCollectionChange(undoText, Map.Legions, () => result = operation()));
        if (!result.Success)
        {
            SetStatus(result.Message ?? failText, isError: true);
            return;
        }

        _context.NotifyChanged(EditorDataScope.Legions);
        ReloadLegionList(_selectedIndex);
        SetStatus(result.Message);
    }

    // -------------------------------------------------------------- 颜色

    private void UpdateColorPreview()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box)) return;

        if (TryParseColor(box.Text, out byte r, out byte g, out byte b))
            _colorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        else
            _colorPreview.Background = Brushes.Transparent;
    }

    /// <summary>打开调色板，把结果写回颜色输入框与预览（之后照常提交）</summary>
    private void PickColor()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box) || !box.IsEnabled) return;

        Color initial = TryParseColor(box.Text, out byte r, out byte g, out byte b)
            ? Color.FromRgb(r, g, b)
            : Colors.White;

        var picked = ColorPickerDialog.Pick(this, initial);
        if (picked == null) return;

        box.Text = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
    }

    private static bool TryParseColor(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;

        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return false;
        if (!byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return false;
        return true;
    }

    // -------------------------------------------------------------- 键盘 / 关闭

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_dirty) RevertPending();
                else Close();
                break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                CommitPending();
                break;
        }
    }

    /// <summary>
    /// 关闭时提交有效的改动（对齐 VB 关闭按钮自动保存）。输入无效时询问：
    /// 放弃这些修改并关闭，或者留在窗口里改正。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty && !CommitPending())
        {
            var answer = MessageBox.Show(this,
                Loc.F("Assist.Legion.DiscardOnClose", _status.Text),
                Loc.T("Assist.Legion.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_context != null) Map.Legions.CollectionChanged -= OnLegionsCollectionChanged;
        base.OnClosed(e);
    }
}
