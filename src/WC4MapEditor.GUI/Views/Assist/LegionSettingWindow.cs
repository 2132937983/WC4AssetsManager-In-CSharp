using System.Globalization;
using System.Reflection;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 军团设置窗口 - 对齐 VB 版 Assist/LegionSetting。
/// <para>
/// 左侧为军团列表（含颜色块与行动顺序），右侧为所选军团的字段编辑面板，
/// 支持全部字段：行动顺序/国家ID/阵营/经济/工业/科技/玩家控制/战败条件/
/// 血率/税率/颜色/科技等级/未知字段1-4/激光等级。
/// </para>
/// <para>
/// 非模态窗口：修改后立即写回 MapData 并触发 <see cref="DataModified"/>，
/// 由场景层刷新军团领域与归属国旗渲染。
/// </para>
/// </summary>
public sealed class LegionSettingWindow : Window
{
    /// <summary>字段定义：显示名的翻译键 / Legion 字段名 / 是否为浮点</summary>
    private sealed record FieldDef(string LabelKey, string FieldName, bool IsFloat);

    /// <summary>颜色字段（R/G/B 三字节）在编辑面板中的虚拟键名</summary>
    private const string ColorFieldKey = "__color";

    /// <summary>
    /// 全部可编辑字段，顺序即界面顺序（对齐 VB 版 LegionSetting）。
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
        new("Assist.Legion.Field.PlayerControlled", nameof(Legion.IsPlayerControlled), false),
        new("Assist.Legion.Field.DefeatCondition", nameof(Legion.DefeatCondition), false),
        new("Assist.Legion.Field.CountryHpRate", nameof(Legion.CountryHpRate), true),
        new("Assist.Legion.Field.CountryTaxRate", nameof(Legion.CountryTaxRate), true),
        new("Common.Color", ColorFieldKey, false),
        new("Assist.Legion.Field.TechLevel", nameof(Legion.InitialTechLevel), false),
        new("Assist.Legion.Field.Unknown1", nameof(Legion.Unknown1), false),
        new("Assist.Legion.Field.Unknown2", nameof(Legion.Unknown2), false),
        new("Assist.Legion.Field.Unknown3", nameof(Legion.Unknown3), false),
        new("Assist.Legion.Field.Unknown4", nameof(Legion.Unknown4), false),
        new("Assist.Legion.Field.LaserLevel", nameof(Legion.InitialLaserGunLevel), false),
    };

    private static string LabelOf(FieldDef def) => Loc.T(def.LabelKey);

    /// <summary>Legion 的公开字段索引，供读写</summary>
    private static readonly Dictionary<string, FieldInfo> LegionFields =
        typeof(Legion)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(float))
            .ToDictionary(f => f.Name);

    private readonly MapData _mapData;
    private readonly LegionModifier _modifier;
    private readonly ListBox _legionListBox = new();
    private readonly StackPanel _editorPanel = new();
    private readonly Dictionary<string, TextBox> _fieldBoxes = new();
    private readonly Border _colorPreview = new();

    /// <summary>批量操作栏里共用的数值输入框（统一经济/工业/科技都用它）</summary>
    private readonly TextBox _batchValueBox = new();

    private int _selectedIndex = -1;
    private bool _isSyncing;

    /// <summary>数据被修改时触发，供场景层即时刷新渲染</summary>
    public event EventHandler? DataModified;

    public LegionSettingWindow(MapData mapData, LegionModifier modifier)
    {
        EditorTheme.Apply(this);

        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));

        Title = Loc.T("Assist.Legion.Title");
        Width = 660;
        Height = 640;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        WindowStyle = WindowStyle.None;
        // 淡入靠 Opacity 动画，无边框窗口只有开了 AllowsTransparency 时 Opacity 才生效
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;

        // 清掉上次"没找到国旗"的缓存：之后补上了国旗资源，重开窗口就能显示出来
        FlagImageLoader.ClearMisses();

        Content = CreateUI();
        LoadLegionList();

        PreviewKeyDown += OnPreviewKeyDown;
        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fadeIn);
        };
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

        // 左侧：军团列表 + 增删按钮
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _legionListBox.Margin = new Thickness(4, 4, 4, 0);
        _legionListBox.SelectionChanged += OnLegionSelectionChanged;
        Grid.SetRow(_legionListBox, 0);
        leftGrid.Children.Add(_legionListBox);

        // 底部增删按钮（对齐 VB 版 LegionSetting 的 "+" / "-"）
        var addRemovePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8)
        };
        addRemovePanel.Children.Add(CreateSmallButton(EditorTheme.Button("+", (_, _) => OnAddLegion()),
            Loc.T("Assist.Legion.AddLegion")));
        addRemovePanel.Children.Add(CreateSmallButton(EditorTheme.DangerButton("-", (_, _) => OnDeleteLegion()),
            Loc.T("Assist.Legion.DeleteLegion")));
        Grid.SetRow(addRemovePanel, 1);
        leftGrid.Children.Add(addRemovePanel);

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
        button.Margin = new Thickness(4, 0, 4, 0);
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
            var box = new TextBox { IsEnabled = false };
            UIElement content;

            if (def.FieldName == ColorFieldKey)
            {
                box.ToolTip = Loc.T("Assist.Legion.ColorHexTooltip");
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
                box.ToolTip = label;
                content = box;
            }

            _fieldBoxes[def.FieldName] = box;
            _editorPanel.Children.Add(EditorTheme.FormRow(label, content, 100));
        }
    }

    /// <summary>
    /// 弹出国家选择菜单：首行是搜索栏，下方是按 ID 排序的国家列表（ID - 名称）。
    /// 国家名取 string table 的 <c>country_{id}</c>，不取 CountrySettings.json 的 Name。
    /// 回车 / 双击确认后回填到目标输入框（会触发既有的写回逻辑）。
    /// </summary>
    private void ShowCountryPicker(Button anchor, TextBox target)
    {
        var countries = WC4MapEditor.Core.Parsers.Country.CountrySettingParser.Instance.Countries
            .OrderBy(c => c.Id)
            .ToList();

        // 显示名取 string table 的 country_{id}，不取 CountrySettings.json 的 Name
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
                target.Text = id.ToString();
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
    /// <para>
    /// 这一栏作用于【所有军团】，与右侧编辑面板（只改当前选中的那一个）语义不同，
    /// 所以放在编辑区之外、底部按钮栏之上，免得误以为是在改选中的军团。
    /// </para>
    /// <para>
    /// 经济/工业/科技共用一个输入框：三者语义相同（把某个字段统一成同一个值），
    /// 分开三个框反而更啰嗦。
    /// </para>
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
            () => ApplyBatchValue(Loc.T("Assist.Legion.Economy"), _modifier.SetAllLegionsEconomy));
        AddBatchButton(Loc.T("Assist.Legion.UnifyIndustry"),
            () => ApplyBatchValue(Loc.T("Assist.Legion.Industry"), _modifier.SetAllLegionsIndustry));
        AddBatchButton(Loc.T("Assist.Legion.UnifyTech"),
            () => ApplyBatchValue(Loc.T("Assist.Legion.Tech"), _modifier.SetAllLegionsTech));
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

    /// <summary>
    /// 把批量输入框里的整数应用到所有军团的指定字段。
    /// </summary>
    private void ApplyBatchValue(string fieldLabel, Func<int, ModifierResult> apply)
    {
        if (!int.TryParse(_batchValueBox.Text.Trim(), out int value))
        {
            MessageBox.Show(
                Loc.F("Assist.Legion.BatchNeedInteger", fieldLabel),
                Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            _batchValueBox.Focus();
            return;
        }

        var result = apply(value);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.F("Assist.Legion.UnifyFailed", fieldLabel), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    private void OnRandomizeAllLegionLevels()
    {
        var confirm = MessageBox.Show(
            Loc.T("Assist.Legion.RandomizeConfirm"),
            Loc.T("Common.Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _modifier.RandomizeAllLegionLevels();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.T("Assist.Legion.RandomizeFailed"), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    private void OnApplyDefaultColors()
    {
        var result = _modifier.ApplyDefaultColorsToAllLegions();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.T("Assist.Legion.ApplyDefaultColorsFailed"), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    /// <summary>
    /// 批量操作后整体刷新。
    /// <para>
    /// 必须重载整张列表，而不是只刷当前项：这些操作一次性改动所有军团，
    /// 而编辑面板只绑定"当前选中"的那一个，不重载的话其它项显示的还是旧值
    /// （颜色类操作尤其明显 —— 左侧列表的色块全都得跟着变）。
    /// 顺便尽量保住原来的选中位置。
    /// </para>
    /// </summary>
    private void ReloadAfterBatchChange()
    {
        int keep = _selectedIndex;
        if (keep < 0 || keep >= _mapData.Legions.Count) keep = 0;

        ReloadLegionList(keep);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private Border CreateButtonBar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(CreateBarButton(EditorTheme.Button(Loc.T("Common.Reset"), (_, _) => ReloadSelected())));
        panel.Children.Add(CreateBarButton(EditorTheme.PrimaryButton(Loc.T("Common.Apply"), (_, _) => ApplyToSelected())));
        panel.Children.Add(CreateBarButton(EditorTheme.Button(Loc.T("Common.Close"), (_, _) => Close())));

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = panel
        };
    }

    private static Button CreateBarButton(Button button)
    {
        button.MinWidth = 80;
        button.Margin = new Thickness(8, 0, 0, 0);
        return button;
    }

    // -------------------------------------------------------------- 数据

    private void LoadLegionList()
    {
        _isSyncing = true;
        _legionListBox.Items.Clear();

        for (int i = 0; i < _mapData.Legions.Count; i++)
            _legionListBox.Items.Add(CreateLegionListItem(i, _mapData.Legions[i]));

        _isSyncing = false;

        if (_legionListBox.Items.Count > 0)
            _legionListBox.SelectedIndex = 0;
        else
            SetEditorEnabled(false);
    }

    /// <summary>
    /// 构建单个军团列表项：压缩后的颜色条 + 国旗 + 两行文本。
    /// 第二行用 string table 里的国家名称（与 ini 配置一致）替代原先固定的"国家{id}"。
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

        // 国旗 flag_{countryId}.png
        var flag = GetFlagImage(legion.CountryId);
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

        var text = new TextBlock
        {
            Text = Loc.F("Assist.Legion.ListItem", index + 1, legion.ActionId, GetCountryName(legion.CountryId), legion.Camp),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(text, 2);
        row.Children.Add(text);

        return row;
    }

    /// <summary>
    /// 加载国家国旗 flag_{countryId}.png。
    /// 实际由 <see cref="FlagImageLoader"/> 完成：战术地图图集优先，文件回退。
    /// </summary>
    private static BitmapSource? GetFlagImage(int countryId) => FlagImageLoader.Load(countryId);

    /// <summary>
    /// 取国家名称（CountrySettings.json → string table 的 country_{id} → 国家{id}）。
    /// 解析顺序收进 <see cref="WC4MapEditor.Core.Config.CountryNameProvider"/>，
    /// 与 CLI 的 legion list 共用一份，避免两处各写一套。
    /// </summary>
    private static string GetCountryName(int countryId)
        => WC4MapEditor.Core.Config.CountryNameProvider.Get(countryId);

    /// <summary>
    /// 新增军团。行动顺序 / 国家 ID 自动取现有最大值 +1，
    /// 其余字段取 Legion.CreateDefault 的默认值（经济 1000 / 工业 100 / 科技 50 /
    /// 血率 1.0 / 税率 0.1 / 颜色白色）。
    /// </summary>
    private void OnAddLegion()
    {
        var result = _modifier.AddLegion();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.T("Assist.Legion.AddFailed"), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadLegionList(_mapData.Legions.Count - 1);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>删除当前选中的军团（对齐 VB 版 "-" 按钮）</summary>
    private void OnDeleteLegion()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _mapData.Legions.Count)
        {
            MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            Loc.F("Assist.Legion.DeleteConfirm", _selectedIndex + 1), Loc.T("Assist.Legion.DeleteConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _modifier.RemoveLegionAt(_selectedIndex);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.T("Assist.Legion.DeleteFailed"), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadLegionList(Math.Min(_selectedIndex, _mapData.Legions.Count - 1));
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>重新加载列表并选中指定项</summary>
    private void ReloadLegionList(int selectIndex)
    {
        _selectedIndex = -1;
        LoadLegionList();

        if (_legionListBox.Items.Count == 0)
        {
            SetEditorEnabled(false);
            return;
        }

        _legionListBox.SelectedIndex = Math.Clamp(selectIndex, 0, _legionListBox.Items.Count - 1);
        ReloadSelected();
    }

    private void SetEditorEnabled(bool enabled)
    {
        foreach (var box in _fieldBoxes.Values)
            box.IsEnabled = enabled;
    }

    private void OnLegionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing) return;
        ReloadSelected();
    }

    private void ReloadSelected()
    {
        int index = _legionListBox.SelectedIndex;
        if (index < 0 || index >= _mapData.Legions.Count)
        {
            _selectedIndex = -1;
            SetEditorEnabled(false);
            return;
        }

        _selectedIndex = index;
        SetEditorEnabled(true);

        var legion = _mapData.Legions[index];
        _isSyncing = true;

        foreach (var def in FieldDefs)
        {
            if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;

            if (def.FieldName == ColorFieldKey)
            {
                box.Text = $"{legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2}";
                continue;
            }

            if (!LegionFields.TryGetValue(def.FieldName, out var field)) continue;
            object? value = field.GetValue(legion);
            box.Text = value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _isSyncing = false;
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box)) return;

        if (TryParseColor(box.Text, out byte r, out byte g, out byte b))
            _colorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        else
            _colorPreview.Background = Brushes.Transparent;
    }

    /// <summary>打开调色板，把结果写回颜色输入框与预览</summary>
    private void PickColor()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box)) return;

        Color initial = TryParseColor(box.Text, out byte r, out byte g, out byte b)
            ? Color.FromRgb(r, g, b)
            : Colors.White;

        var picked = ColorPickerDialog.Pick(Window.GetWindow(this), initial);
        if (picked == null) return;

        box.Text = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        UpdateColorPreview();
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

    /// <summary>把编辑面板的输入写回当前选中的军团</summary>
    private void ApplyToSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _mapData.Legions.Count) return;

        // Legion 是值类型：先装箱修改，再整体写回
        object boxed = _mapData.Legions[_selectedIndex];
        var errors = new List<string>();

        foreach (var def in FieldDefs)
        {
            if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;
            string text = box.Text.Trim();
            if (text.Length == 0) continue;

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
            MessageBox.Show(string.Join("\n", errors), Loc.T("Common.InputError"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updated = (Legion)boxed;

        // 按列表索引写回（不能用按 CountryId 查表的 UpdateLegion，
        // 否则改了 CountryId 就会被当成新军团插入）
        var result = _modifier.UpdateLegionAt(_selectedIndex, updated);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? Loc.T("Assist.Legion.ApplyFailed"), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshListItem(_selectedIndex, updated);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshListItem(int index, Legion legion)
    {
        if (index < 0 || index >= _legionListBox.Items.Count) return;

        // 直接重建该项：CountryId 可能已变，国旗需要一起换
        bool wasSelected = _legionListBox.SelectedIndex == index;

        _isSyncing = true;
        _legionListBox.Items[index] = CreateLegionListItem(index, legion);
        if (wasSelected) _legionListBox.SelectedIndex = index;
        _isSyncing = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                ApplyToSelected();
                e.Handled = true;
                break;
        }
    }
}
