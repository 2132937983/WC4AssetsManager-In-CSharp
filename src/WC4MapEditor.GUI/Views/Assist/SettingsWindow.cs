using System.Diagnostics;
using System.Globalization;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 设置窗口：在线编辑 setting.txt（对齐 VB 版 Assist/Settings，开始界面"设置"按钮打开）。
/// <para>
/// 左侧按节列出，右侧是该节的键值表单（与文件顺序一致，说明注释显示在键名下方）；顶部搜索框可跨节查找键名 / 说明 / 值。
/// 常见键有专用输入：主题色与国家颜色带取色块，开屏动画 / 截图倍率 / 界面语言用下拉框，整数与颜色有格式校验，开关用勾选框。
/// </para>
/// <para>
/// 保存时重新读取磁盘上的文件，只改写值变化的那几行（注释、空行、写法原样保留），
/// 然后热重载 <see cref="ConfigManager"/>（触发 <see cref="ConfigManager.ConfigReloaded"/>），改动无需重启即可生效。
/// AIApiKey 这类密钥只用密码框编辑：界面上不显示明文，日志里也从不输出值。
/// </para>
/// </summary>
public sealed class SettingsWindow : Window
{
    private enum EditorKind { Text, LongText, Integer, Boolean, Color, Choice, Language, Secret }

    /// <summary>表单上的一个键：对应文件里的一行，记录打开时的值与当前编辑值</summary>
    private sealed class Field
    {
        public Field(SettingTxtDocument.Entry entry, EditorKind kind, string[] choices)
        {
            Entry = entry;
            Kind = kind;
            Choices = choices;
            Original = entry.Value;
            Current = entry.Value;
        }

        public SettingTxtDocument.Entry Entry { get; }
        public EditorKind Kind { get; }
        public string[] Choices { get; }
        public string Original { get; }
        public string Current { get; set; }
        public bool IsDirty => !string.Equals(Current, Original, StringComparison.Ordinal);
    }

    private static readonly string[] OpeningAnimationChoices = { "Logo", "Token", "None" };
    private static readonly string[] ScreenshotResolutionChoices = { "1x", "2x", "4x", "8x", "16x", "32x", "64x" };

    private readonly string _path = ConfigManager.SettingFilePath;
    private readonly Dictionary<SettingTxtDocument.Entry, Field> _fields = new();
    private SettingTxtDocument _document = null!;

    // 以下控件在每次 BuildContent 时新建（保存后切换了界面语言要整窗重建）
    private Grid _root = null!;
    private ListBox _sectionList = null!;
    private StackPanel _form = null!;
    private ScrollViewer _formScroll = null!;
    private TextBlock _statusText = null!;
    private TextBox _searchBox = null!;
    private bool _closingConfirmed;

    public SettingsWindow()
    {
        EditorTheme.Apply(this);
        Width = 960;
        Height = 680;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = EditorTheme.Bg1;

        LoadDocument();
        BuildContent(null, "");

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>窗口标题：setting.txt 的 SettingTitle（VB 版同款可配置标题），没有则用翻译文本。</summary>
    private static string WindowTitle => ConfigManager.Instance.GetText("SettingTitle", Loc.T("Assist.Settings.Title"));

    // ============================================================= 文档 =============================================================

    private void LoadDocument()
    {
        _document = SettingTxtDocument.Load(_path);
        _fields.Clear();
        foreach (var entry in _document.Entries)
        {
            var (kind, choices) = KindOf(entry);
            _fields[entry] = new Field(entry, kind, choices);
        }
    }

    private static (EditorKind Kind, string[] Choices) KindOf(SettingTxtDocument.Entry entry)
    {
        string key = entry.Key;
        string value = entry.Value.Trim();
        if (IsSecretKey(key)) return (EditorKind.Secret, Array.Empty<string>());
        if (key == Loc.SettingKey) return (EditorKind.Language, Array.Empty<string>());
        if (key == "OpeningAnimation") return (EditorKind.Choice, OpeningAnimationChoices);
        if (key == "screenshot_resolution") return (EditorKind.Choice, ScreenshotResolutionChoices);
        if (key == "ThemeColor" || key.StartsWith("country_color_", StringComparison.Ordinal)) return (EditorKind.Color, Array.Empty<string>());
        if (key == MusicPlayerService.EnabledSettingKey || bool.TryParse(value, out _)) return (EditorKind.Boolean, Array.Empty<string>());
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return (EditorKind.Integer, Array.Empty<string>());
        if (value.Length > 60 || value.StartsWith('[')) return (EditorKind.LongText, Array.Empty<string>());
        return (EditorKind.Text, Array.Empty<string>());
    }

    /// <summary>密钥类的键（AIApiKey 等）：只用密码框，绝不显示或记录值</summary>
    private static bool IsSecretKey(string key)
        => key.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase)
           || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("Password", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<Field> DirtyFields => _fields.Values.Where(f => f.IsDirty);

    // ============================================================= 界面骨架 =============================================================

    /// <summary>（重新）搭建整个窗口内容，保留选中的节与搜索词。</summary>
    private void BuildContent(string? selectSection, string search)
    {
        Title = WindowTitle;
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = CreateTitleBar();
        Grid.SetRow(title, 0);
        _root.Children.Add(title);

        var toolbar = CreateToolbar(search);
        Grid.SetRow(toolbar, 1);
        _root.Children.Add(toolbar);

        var body = CreateBody();
        Grid.SetRow(body, 2);
        _root.Children.Add(body);

        var footer = CreateFooter();
        Grid.SetRow(footer, 3);
        _root.Children.Add(footer);

        Content = new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = _root
        };

        FillSectionList(selectSection);
        ShowCurrentView();
        UpdateStatus();
    }

    /// <summary>标题栏：按住可拖动，中间显示文件路径，右侧 ✕。</summary>
    private Border CreateTitleBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = EditorTheme.Title(WindowTitle);
        title.Margin = new Thickness(14, 0, 12, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);

        var path = EditorTheme.Muted(_path);
        path.VerticalAlignment = VerticalAlignment.Center;
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.ToolTip = _path;
        Grid.SetColumn(path, 1);
        grid.Children.Add(path);

        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 32;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(6, 0, 6, 0);
        close.ToolTip = Loc.T("Common.Close");
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        var bar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return bar;
    }

    private Border CreateToolbar(string search)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var searchBox = EditorTheme.SearchBox(Loc.T("Assist.Settings.SearchPlaceholder"), out var box, 320);
        _searchBox = box;
        _searchBox.Text = search;
        _searchBox.TextChanged += (_, _) => ShowCurrentView();
        grid.Children.Add(searchBox);

        var hint = EditorTheme.Muted(Loc.T("Assist.Settings.HotReloadHint"));
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        hint.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(hint, 1);
        grid.Children.Add(hint);

        return EditorTheme.ToolBar(grid);
    }

    private Grid CreateBody()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _sectionList = new ListBox { Margin = new Thickness(4) };
        _sectionList.SelectionChanged += OnSectionChanged;
        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        left.Child = _sectionList;
        grid.Children.Add(left);

        _form = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _formScroll = new ScrollViewer
        {
            Content = _form,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(_formScroll, 1);
        grid.Children.Add(_formScroll);
        return grid;
    }

    private Border CreateFooter()
    {
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        grid.Children.Add(_statusText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = EditorTheme.Button(Loc.T("Common.Refresh"), (_, _) => RefreshFromDisk());
        refresh.ToolTip = Loc.T("Assist.Settings.RefreshTooltip");
        refresh.MinWidth = 88;
        var save = EditorTheme.PrimaryButton(Loc.T("Common.Save"), (_, _) => Save());
        save.ToolTip = Loc.T("Assist.Settings.SaveTooltip");
        save.MinWidth = 88;
        save.Margin = new Thickness(8, 0, 8, 0);
        save.IsEnabled = _document.Sections.Count > 0;
        var close = EditorTheme.Button(Loc.T("Common.Close"), (_, _) => Close());
        close.MinWidth = 88;
        buttons.Children.Add(refresh);
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid
        };
    }

    // ============================================================= 节列表 / 表单 =============================================================

    private static string SectionTitle(string name) => name.Length == 0 ? Loc.T("Assist.Settings.NoSection") : name;

    private SettingTxtDocument.Section? SelectedSection => (_sectionList.SelectedItem as ListBoxItem)?.Tag as SettingTxtDocument.Section;

    private void FillSectionList(string? select)
    {
        _sectionList.SelectionChanged -= OnSectionChanged;
        foreach (var section in _document.Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = SectionTitle(section.Name), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(EditorTheme.Badge(section.Entries.Count().ToString(CultureInfo.InvariantCulture)));
            _sectionList.Items.Add(new ListBoxItem { Content = row, Tag = section });
        }
        int index = _document.Sections.ToList().FindIndex(s => s.Name == select);
        if (_sectionList.Items.Count > 0) _sectionList.SelectedIndex = Math.Max(0, index);
        _sectionList.SelectionChanged += OnSectionChanged;
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 搜索时点了某一节：清掉搜索词回到该节（清空会触发 TextChanged 重建表单）
        if (_searchBox.Text.Length > 0)
            _searchBox.Text = "";
        else
            ShowCurrentView();
    }

    /// <summary>按当前状态重建表单：有搜索词时跨节列出匹配项，否则显示选中的节。</summary>
    private void ShowCurrentView()
    {
        _form.Children.Clear();
        _formScroll.ScrollToTop();

        if (_document.Sections.Count == 0)
        {
            _form.Children.Add(EditorTheme.EmptyHint(Loc.F("Assist.Settings.FileMissing", _path)));
            return;
        }

        string query = _searchBox.Text.Trim();
        if (query.Length > 0)
        {
            ShowSearchResults(query);
            return;
        }

        var section = SelectedSection;
        if (section == null) return;
        var header = EditorTheme.SectionHeader(SectionTitle(section.Name));
        header.Margin = new Thickness(0);
        _form.Children.Add(header);
        foreach (var item in section.Items)
        {
            if (item.Entry != null) _form.Children.Add(CreateRow(_fields[item.Entry]));
            else if (item.Note != null) _form.Children.Add(CreateNote(item.Note));
        }
        if (!section.Entries.Any())
            _form.Children.Add(EditorTheme.EmptyHint(Loc.T("Assist.Settings.EmptySection")));
    }

    private void ShowSearchResults(string query)
    {
        int matches = 0;
        foreach (var section in _document.Sections)
        {
            var hits = section.Entries.Select(e => _fields[e]).Where(f => Matches(f, query)).ToList();
            if (hits.Count == 0) continue;
            _form.Children.Add(EditorTheme.SectionHeader(SectionTitle(section.Name)));
            foreach (var field in hits) _form.Children.Add(CreateRow(field));
            matches += hits.Count;
        }
        if (matches == 0)
            _form.Children.Add(EditorTheme.EmptyHint(Loc.F("Assist.Settings.NoMatch", query)));
    }

    /// <summary>搜索匹配键名、说明注释与值（密钥的值不参与匹配）</summary>
    private static bool Matches(Field field, string query)
        => field.Entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
           || field.Entry.Comment.Contains(query, StringComparison.OrdinalIgnoreCase)
           || (field.Kind != EditorKind.Secret && field.Current.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static TextBlock CreateNote(string note) => new()
    {
        Text = note,
        Foreground = EditorTheme.TextDim,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(14, 8, 14, 2)
    };

    /// <summary>一行：左侧键名（下方是说明注释、不生效标记），右侧按类型的输入控件</summary>
    private Border CreateRow(Field field)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 2, 12, 2) };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
        var label = EditorTheme.FieldLabel(field.Entry.Key);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.ToolTip = field.Entry.Key;
        keyRow.Children.Add(label);
        if (field.Entry.IsOverridden)
        {
            var badge = EditorTheme.Badge(Loc.T("Assist.Settings.Overridden"));
            if (badge.Child is TextBlock badgeText) badgeText.Foreground = EditorTheme.Warning;
            badge.ToolTip = Loc.T("Assist.Settings.OverriddenTooltip");
            keyRow.Children.Add(badge);
        }
        left.Children.Add(keyRow);
        if (field.Entry.Comment.Length > 0)
        {
            left.Children.Add(new TextBlock
            {
                Text = field.Entry.Comment,
                Foreground = EditorTheme.TextDim,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        grid.Children.Add(left);

        var editor = CreateEditor(field, () => OnFieldChanged(label, field));
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        ApplyDirtyLook(label, field);

        return new Border
        {
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 6, 14, 6),
            Child = grid
        };
    }

    private void OnFieldChanged(TextBlock label, Field field)
    {
        ApplyDirtyLook(label, field);
        UpdateStatus();
    }

    /// <summary>改过的键名用强调色标出</summary>
    private static void ApplyDirtyLook(TextBlock label, Field field)
    {
        label.Foreground = field.IsDirty ? EditorTheme.Accent : EditorTheme.TextMuted;
        label.FontWeight = field.IsDirty ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private FrameworkElement CreateEditor(Field field, Action changed)
    {
        switch (field.Kind)
        {
            case EditorKind.Secret:
                return CreateSecretEditor(field, changed);
            case EditorKind.Boolean:
            {
                var check = new CheckBox
                {
                    IsChecked = bool.TryParse(field.Current.Trim(), out bool b) && b,
                    Content = Loc.T("Assist.Settings.Enabled")
                };
                check.Checked += (_, _) => SetBoolean(field, true, changed);
                check.Unchecked += (_, _) => SetBoolean(field, false, changed);
                return check;
            }
            case EditorKind.Choice:
                return CreateCombo(field, field.Choices.Select(c => (c, c)).ToList(), changed);
            case EditorKind.Language:
                return CreateCombo(field, Loc.AvailableLanguages.Select(l => (l.Code, $"{l.DisplayName} ({l.Code})")).ToList(), changed);
            case EditorKind.Color:
                return CreateColorEditor(field, changed);
            default:
            {
                var box = EditorTheme.TextBox(field.Current);
                if (field.Kind == EditorKind.LongText)
                {
                    box.TextWrapping = TextWrapping.Wrap;
                    box.MaxHeight = 120;
                    box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
                if (field.Kind == EditorKind.Integer)
                {
                    box.Width = 160;
                    box.HorizontalAlignment = HorizontalAlignment.Left;
                }
                box.TextChanged += (_, _) =>
                {
                    field.Current = box.Text;
                    box.BorderBrush = IsValid(field) ? EditorTheme.Border : EditorTheme.Danger;
                    changed();
                };
                return box;
            }
        }
    }

    /// <summary>
    /// 密钥输入：原值不放进输入框（连长度都不显示），框里只显示"已设置 / 未设置"的灰字；
    /// 输入新值即替换，清空输入框则保持原值。
    /// </summary>
    private static Grid CreateSecretEditor(Field field, Action changed)
    {
        var box = new PasswordBox
        {
            ToolTip = Loc.T("Assist.Settings.SecretTooltip"),
            Background = EditorTheme.Bg0,
            Foreground = EditorTheme.Text,
            CaretBrush = EditorTheme.Text,
            BorderBrush = EditorTheme.Border,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        // 表单重建（切换节 / 搜索）时输入框是新的：已输入未保存的新值只用提示文字说明，不回填
        string HintText() => Loc.T(field.IsDirty ? "Assist.Settings.SecretChanged"
            : field.Original.Length > 0 ? "Assist.Settings.SecretSet" : "Assist.Settings.SecretUnset");
        var hint = new TextBlock
        {
            Text = HintText(),
            Foreground = EditorTheme.TextDim,
            FontSize = 12,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        box.PasswordChanged += (_, _) =>
        {
            field.Current = box.Password.Length > 0 ? box.Password : field.Original;
            hint.Text = HintText();
            hint.Visibility = box.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            changed();
        };

        // 主题没有给 PasswordBox 定样式：放一个隐藏的主题输入框撑出同样的行高，密码框拉伸填满，与其它输入框等高
        var grid = new Grid();
        grid.Children.Add(new TextBox { Visibility = Visibility.Hidden, IsTabStop = false });
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return grid;
    }

    /// <summary>勾回原来的状态时保留原文写法（True / true），不算修改</summary>
    private static void SetBoolean(Field field, bool value, Action changed)
    {
        field.Current = bool.TryParse(field.Original.Trim(), out bool original) && original == value
            ? field.Original
            : value ? "true" : "false";
        changed();
    }

    private static ComboBox CreateCombo(Field field, List<(string Value, string Display)> options, Action changed)
    {
        // 文件里写了列表以外的值也照样显示出来，不悄悄改掉
        if (!options.Any(o => string.Equals(o.Value, field.Current.Trim(), StringComparison.OrdinalIgnoreCase)))
            options.Add((field.Current, field.Current));

        var combo = EditorTheme.ComboBox(240);
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var (value, display) in options)
            combo.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        combo.SelectedIndex = options.FindIndex(o => string.Equals(o.Value, field.Current.Trim(), StringComparison.OrdinalIgnoreCase));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string value }) return;
            // 只是大小写不同（如 none / None）时保留原文，不算修改
            field.Current = string.Equals(value, field.Original.Trim(), StringComparison.OrdinalIgnoreCase) ? field.Original : value;
            changed();
        };
        return combo;
    }

    private FrameworkElement CreateColorEditor(Field field, Action changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var box = EditorTheme.TextBox(field.Current);
        box.Width = 160;
        var swatch = new Button
        {
            Width = 36,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = Loc.T("Assist.Settings.PickColor"),
            BorderBrush = EditorTheme.BorderStrong
        };

        void UpdateSwatch()
        {
            // 色块显示的是数据本身的颜色（不走主题）
            swatch.Background = TryParseColor(field.Current, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;
            box.BorderBrush = IsValid(field) ? EditorTheme.Border : EditorTheme.Danger;
        }

        box.TextChanged += (_, _) =>
        {
            field.Current = box.Text;
            UpdateSwatch();
            changed();
        };
        swatch.Click += (_, _) =>
        {
            var initial = TryParseColor(field.Current, out var c) ? c : Colors.White;
            if (ColorPickerDialog.Pick(this, initial) is { } picked)
                box.Text = FormatColor(picked, field.Original);
        };
        UpdateSwatch();

        panel.Children.Add(box);
        panel.Children.Add(swatch);
        return panel;
    }

    // ============================================================= 校验 =============================================================

    private static bool IsValid(Field field) => field.Kind switch
    {
        EditorKind.Integer => int.TryParse(field.Current.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        EditorKind.Color => TryParseColor(field.Current, out _),
        _ => true
    };

    /// <summary>与 ConfigManager 相同的颜色写法：十六进制 RRGGBB，可带 0x / 0X / # 前缀</summary>
    private static bool TryParseColor(string text, out Color color)
    {
        color = Colors.Transparent;
        string hex = text.Trim().TrimStart('#');
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (hex.Length == 0 || hex.Length > 8
            || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            return false;
        color = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    /// <summary>取色结果写成 0xRRGGBB；原值用大写十六进制就保持大写</summary>
    private static string FormatColor(Color c, string original)
    {
        string hex = $"{c.R:x2}{c.G:x2}{c.B:x2}";
        string digits = original.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? original.Trim()[2..] : original;
        return "0x" + (digits.Any(char.IsUpper) ? hex.ToUpperInvariant() : hex);
    }

    // ============================================================= 保存 / 刷新 / 关闭 =============================================================

    private void Save()
    {
        var dirty = DirtyFields.ToList();
        if (dirty.Count == 0)
        {
            ShowToast(Loc.T("Assist.Settings.NothingToSave"), EditorTheme.Info);
            return;
        }

        var invalid = dirty.Where(f => !IsValid(f)).ToList();
        if (invalid.Count > 0)
        {
            string lines = string.Join("\n", invalid.Select(f => Loc.F(
                f.Kind == EditorKind.Color ? "Assist.Settings.InvalidColor" : "Assist.Settings.InvalidInteger", f.Entry.Key)));
            MessageBox.Show(this, lines, Loc.T("Assist.Settings.InvalidTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int written;
        var missing = new List<string>();
        try
        {
            // 重新读磁盘上的文件再改：打开窗口之后界面语言 / 背景音乐偏好可能已被写入，不能拿打开时的快照覆盖
            var fresh = SettingTxtDocument.Load(_path);
            foreach (var field in dirty)
            {
                var entry = fresh.Find(field.Entry.Section, field.Entry.Key, field.Entry.Occurrence);
                if (entry == null)
                {
                    missing.Add(field.Entry.Key);
                    continue;
                }
                fresh.SetValue(entry, field.Current.Trim());
            }
            fresh.Save();
            written = dirty.Count - missing.Count;
            Debug.WriteLine($"[Settings] 已写入 {written} 项到 {_path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] 保存失败: {ex.Message}");
            MessageBox.Show(this, Loc.F("Assist.Settings.SaveFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 界面语言要先切：热重载会重建开始界面，重建时就用新语言
        string? language = dirty.FirstOrDefault(f => f.Kind == EditorKind.Language)?.Current.Trim();
        if (!string.IsNullOrEmpty(language) && !string.Equals(language, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            Loc.SetLanguage(language, persist: false);
        ConfigManager.Instance.Reload();

        ReloadView();
        ShowToast(missing.Count == 0
                ? Loc.F("Assist.Settings.Saved", written)
                : Loc.F("Assist.Settings.SavedPartially", written, string.Join(", ", missing)),
            missing.Count == 0 ? EditorTheme.Success : EditorTheme.Warning);
    }

    /// <summary>放弃未保存的修改，重新读取磁盘上的 setting.txt 并热重载（在外部改过文件时用）。</summary>
    private void RefreshFromDisk()
    {
        if (DirtyFields.Any() && !ConfirmDiscard()) return;
        ConfigManager.Instance.Reload();
        ReloadView();
        ShowToast(Loc.T("Assist.Settings.Reloaded"), EditorTheme.Success);
    }

    /// <summary>重读文件并重建整个窗口（保留选中的节与搜索词）。</summary>
    private void ReloadView()
    {
        string? section = SelectedSection?.Name;
        string search = _searchBox.Text;
        LoadDocument();
        BuildContent(section, search);
    }

    private bool ConfirmDiscard()
        => MessageBox.Show(this, Loc.F("Assist.Settings.DiscardConfirm", DirtyFields.Count()), Loc.T("Dialog.ConfirmTitle"),
               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed || !DirtyFields.Any()) return;
        if (ConfirmDiscard())
            _closingConfirmed = true;
        else
            e.Cancel = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save();
            e.Handled = true;
        }
    }

    // ============================================================= 状态 / 提示 =============================================================

    private void UpdateStatus()
    {
        int dirty = DirtyFields.Count();
        _statusText.Text = dirty > 0
            ? Loc.F("Assist.Settings.StatusDirty", dirty)
            : Loc.F("Assist.Settings.StatusSummary", _document.Sections.Count, _fields.Count);
        _statusText.Foreground = dirty > 0 ? EditorTheme.Warning : EditorTheme.TextMuted;
    }

    /// <summary>顶部居中的短暂提示，1.5 秒后淡出</summary>
    private void ShowToast(string message, Brush background)
    {
        var toast = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(18, 8, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 52, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = message, Foreground = EditorTheme.Text, FontWeight = FontWeights.SemiBold }
        };
        Grid.SetRowSpan(toast, 4);
        Panel.SetZIndex(toast, 100);
        var host = _root;
        host.Children.Add(toast);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(1500) };
        fade.Completed += (_, _) => host.Children.Remove(toast);
        toast.BeginAnimation(OpacityProperty, fade);
    }
}
