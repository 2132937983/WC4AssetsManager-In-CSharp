using System.Windows.Controls.Primitives;

namespace WC4MapEditor.GUI.Theme;

/// <summary>
/// 编辑场景统一外观：调色板（画刷）+ 常用控件工厂。
/// <para>
/// 所有"非渲染、非开始界面"的场景、对话框、辅助窗口都只从这里取颜色与控件，
/// 场景代码里不再出现 <c>Color.FromRgb(...)</c>。颜色值本身定义在 <c>EditorTheme.xaml</c>，
/// 这里的属性只是把字典里的画刷按名字取出来（并冻结），改配色只改 XAML 一处。
/// </para>
/// <para>
/// <see cref="Apply(FrameworkElement)"/> 把主题字典合并到场景根元素的 Resources：
/// 隐式样式只作用于该场景子树，不会影响渲染场景与开始界面（它们保留原有自绘样式）。
/// </para>
/// </summary>
public static class EditorTheme
{
    private static ResourceDictionary? _dictionary;

    /// <summary>主题资源字典（惰性加载，全局共享一份）。</summary>
    public static ResourceDictionary Dictionary
    {
        get
        {
            if (_dictionary == null)
            {
                _dictionary = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/WC4MapEditor.GUI;component/Theme/EditorTheme.xaml", UriKind.Absolute)
                };
            }
            return _dictionary;
        }
    }

    /// <summary>把主题套到场景 / 窗口 / 对话框根元素上（可重复调用，不会重复合并）。</summary>
    public static void Apply(FrameworkElement root)
    {
        var merged = root.Resources.MergedDictionaries;
        if (!merged.Contains(Dictionary)) merged.Add(Dictionary);
        if (root is Control c && c.FontFamily == SystemFonts.MessageFontFamily)
            c.FontFamily = FontFamily;
        if (root is Window w)
            w.Background = Bg1;
        else if (root is Control ctrl && (ctrl.Background == null || ctrl.Background == SystemColors.ControlBrush))
            ctrl.Background = Bg0;
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
    }

    // ============================================================= 调色板 =============================================================

    private static Brush B(string key) => (Brush)Dictionary[key];
    private static Style S(string key) => (Style)Dictionary[key];

    /// <summary>最深底色：画布 / 输入框底。</summary>
    public static Brush Bg0 => B("Theme.Bg0");
    /// <summary>场景底色。</summary>
    public static Brush Bg1 => B("Theme.Bg1");
    /// <summary>工具栏 / 侧栏 / 状态栏底色。</summary>
    public static Brush Bg2 => B("Theme.Bg2");
    /// <summary>卡片 / 按钮 / 分组标题底色。</summary>
    public static Brush Bg3 => B("Theme.Bg3");
    public static Brush Border => B("Theme.Border");
    public static Brush BorderStrong => B("Theme.BorderStrong");
    public static Brush Text => B("Theme.Text");
    public static Brush TextMuted => B("Theme.TextMuted");
    public static Brush TextDim => B("Theme.TextDim");
    public static Brush Accent => B("Theme.Accent");
    public static Brush AccentHover => B("Theme.AccentHover");
    public static Brush AccentPressed => B("Theme.AccentPressed");
    public static Brush AccentSoft => B("Theme.AccentSoft");
    public static Brush Selection => B("Theme.Selection");
    public static Brush Hover => B("Theme.Hover");
    public static Brush Success => B("Theme.Success");
    public static Brush Warning => B("Theme.Warning");
    public static Brush Danger => B("Theme.Danger");
    public static Brush DangerHover => B("Theme.DangerHover");
    public static Brush Info => B("Theme.Info");

    public static FontFamily FontFamily => (FontFamily)Dictionary["Theme.FontFamily"];
    public static FontFamily MonoFontFamily => (FontFamily)Dictionary["Theme.MonoFontFamily"];

    /// <summary>画刷对应的颜色（给需要 Color 的 API 用，如 DropShadowEffect）。</summary>
    public static Color ColorOf(Brush brush) => brush is SolidColorBrush s ? s.Color : Colors.Transparent;

    // ============================================================= 命名样式 =============================================================

    public static Style PrimaryButtonStyle => S("Theme.Button.Primary");
    public static Style DangerButtonStyle => S("Theme.Button.Danger");
    public static Style GhostButtonStyle => S("Theme.Button.Ghost");
    public static Style ToolButtonStyle => S("Theme.Button.Tool");
    public static Style NavButtonStyle => S("Theme.Button.Nav");
    public static Style TabButtonStyle => S("Theme.Button.Tab");
    public static Style TabActiveButtonStyle => S("Theme.Button.TabActive");
    public static Style TitleTextStyle => S("Theme.Text.Title");
    public static Style SectionTextStyle => S("Theme.Text.Section");
    public static Style MutedTextStyle => S("Theme.Text.Muted");
    public static Style FieldLabelStyle => S("Theme.Text.FieldLabel");
    public static Style MonoTextBoxStyle => S("Theme.TextBox.Mono");

    // ============================================================= 按钮 =============================================================

    /// <summary>普通按钮（灰底）。</summary>
    public static Button Button(string text, RoutedEventHandler? onClick = null)
    {
        var b = new Button { Content = text };
        if (onClick != null) b.Click += onClick;
        return b;
    }

    /// <summary>主操作按钮（强调色底）：保存、确定这类。</summary>
    public static Button PrimaryButton(string text, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = PrimaryButtonStyle;
        return b;
    }

    /// <summary>危险操作按钮（红底）：删除这类。</summary>
    public static Button DangerButton(string text, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = DangerButtonStyle;
        return b;
    }

    /// <summary>无底色按钮：返回、关闭这类次要操作。</summary>
    public static Button GhostButton(string text, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = GhostButtonStyle;
        return b;
    }

    /// <summary>工具栏小按钮。</summary>
    public static Button ToolButton(string text, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = ToolButtonStyle;
        return b;
    }

    /// <summary>左侧导航式按钮（左对齐、透明底）。</summary>
    public static Button NavButton(string text, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = NavButtonStyle;
        return b;
    }

    /// <summary>把导航式按钮切换为选中 / 未选中外观。</summary>
    public static void SetNavSelected(Button b, bool selected)
    {
        b.Background = selected ? Selection : Brushes.Transparent;
        b.Foreground = selected ? Brushes.White : Text;
    }

    /// <summary>页签按钮（属性 / 图库 这类切换）。</summary>
    public static Button TabButton(string text, bool active, RoutedEventHandler? onClick = null)
    {
        var b = Button(text, onClick);
        b.Style = active ? TabActiveButtonStyle : TabButtonStyle;
        return b;
    }

    public static void SetTabActive(Button b, bool active) => b.Style = active ? TabActiveButtonStyle : TabButtonStyle;

    // ============================================================= 文本 =============================================================

    public static TextBlock Title(string text) => new() { Text = text, Style = TitleTextStyle };

    /// <summary>属性面板里的分组标题。</summary>
    public static TextBlock Section(string text) => new() { Text = text, Style = SectionTextStyle };

    public static TextBlock Muted(string text) => new() { Text = text, Style = MutedTextStyle };

    /// <summary>表单行左侧的字段名。</summary>
    public static TextBlock FieldLabel(string text) => new() { Text = text, Style = FieldLabelStyle };

    public static TextBlock Text_(string text, double size = 13, Brush? brush = null)
        => new() { Text = text, FontSize = size, Foreground = brush ?? Text, VerticalAlignment = VerticalAlignment.Center };

    // ============================================================= 输入 =============================================================

    public static TextBox TextBox(string text = "")
        => new() { Text = text };

    /// <summary>只读等宽多行文本（JSON 预览等）。</summary>
    public static TextBox MonoTextBox(double height = 200)
        => new() { Style = MonoTextBoxStyle, IsReadOnly = true, Height = height };

    /// <summary>
    /// 带占位提示的搜索框：真正的输入框文本永远是用户输入，占位符是叠在上面的一层灰字，
    /// 调用方不需要再用 "文本以『搜索』开头" 之类的判断去区分占位与输入。
    /// </summary>
    public static Grid SearchBox(string placeholder, out TextBox box, double? width = null)
    {
        var grid = new Grid();
        if (width.HasValue) grid.Width = width.Value;
        box = new TextBox();
        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = TextDim,
            FontSize = 12,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var tb = box;
        void Update() => hint.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return grid;
    }

    public static ComboBox ComboBox(double? width = null)
    {
        var c = new ComboBox();
        if (width.HasValue) c.Width = width.Value;
        return c;
    }

    public static CheckBox CheckBox(string label, bool initial = false, Action<bool>? changed = null)
    {
        var cb = new CheckBox { Content = label, IsChecked = initial };
        if (changed != null)
        {
            cb.Checked += (_, _) => changed(true);
            cb.Unchecked += (_, _) => changed(false);
        }
        return cb;
    }

    // ============================================================= 容器 =============================================================

    /// <summary>侧栏 / 面板底：浅一级的底色 + 右侧（或指定方向）一条分隔线。</summary>
    public static Border Panel(Thickness? borderThickness = null) => new()
    {
        Background = Bg1,
        BorderBrush = Border,
        BorderThickness = borderThickness ?? new Thickness(0)
    };

    /// <summary>卡片：圆角 + 边框，用于属性面板里的信息块。</summary>
    public static Border Card(UIElement? child = null, Thickness? padding = null) => new()
    {
        Background = Bg2,
        BorderBrush = Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = padding ?? new Thickness(10),
        Margin = new Thickness(0, 0, 0, 8),
        Child = child
    };

    /// <summary>
    /// 场景顶栏：左侧返回按钮 + 标题，右侧操作按钮。高度 44。
    /// </summary>
    public static Border TopBar(string title, RoutedEventHandler onBack, params UIElement[] rightActions)
    {
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var back = GhostButton("‹  " + Loc.T("Common.Back"), onBack);
        back.Padding = new Thickness(8, 4, 10, 4);
        left.Children.Add(back);
        left.Children.Add(new Border { Width = 1, Height = 18, Background = Border, Margin = new Thickness(6, 0, 10, 0) });
        left.Children.Add(Title(title));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var a in rightActions)
        {
            if (a is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(a);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Height = 44,
            Background = Bg2,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    /// <summary>工具栏容器（WrapPanel 内容），用于按钮很多的场景。</summary>
    public static Border ToolBar(Panel content) => new()
    {
        Background = Bg2,
        BorderBrush = Border,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(8, 5, 8, 5),
        Child = content
    };

    /// <summary>状态栏：高度 26，左侧状态文字。</summary>
    public static Border StatusBar(out TextBlock statusText, string initial = "")
    {
        statusText = new TextBlock
        {
            Text = initial,
            Foreground = TextMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        return new Border
        {
            Height = 26,
            Background = Bg2,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = statusText
        };
    }

    /// <summary>垂直分隔条（工具栏里按钮组之间）。</summary>
    public static Border VSeparator() => new()
    {
        Width = 1,
        Height = 20,
        Background = Border,
        Margin = new Thickness(4, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static GridSplitter VSplitter() => new() { Width = 4 };
    public static GridSplitter HSplitter() => new() { Height = 4 };

    /// <summary>
    /// 表单行：左侧固定宽度的字段名，右侧内容；底部一条细分隔线。
    /// </summary>
    public static Border FormRow(string label, UIElement content, double labelWidth = 180, string? tooltip = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = FieldLabel(label);
        lbl.Margin = new Thickness(0, 0, 10, 0);
        if (tooltip != null) lbl.ToolTip = tooltip;
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(content);
        return new Border
        {
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 5, 10, 5),
            Child = grid
        };
    }

    /// <summary>属性面板里的分组标题条。</summary>
    public static Border SectionHeader(string title, UIElement? right = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tb = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(tb);
        if (right != null)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        return new Border
        {
            Background = Bg2,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = grid
        };
    }

    /// <summary>空状态提示（居中灰字）。</summary>
    public static TextBlock EmptyHint(string text) => new()
    {
        Text = text,
        Foreground = TextDim,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24)
    };

    /// <summary>小圆点徽章（计数之类）。</summary>
    public static Border Badge(string text, Brush? bg = null) => new()
    {
        Background = bg ?? Bg3,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(7, 1, 7, 1),
        Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = TextMuted }
    };

    /// <summary>可选中的小标签（图集筛选 chips 这类）。</summary>
    public static Button Chip(string text, bool selected, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(2),
            MinHeight = 22,
            Background = selected ? Selection : Bg3,
            BorderBrush = selected ? Accent : Border,
            Foreground = selected ? Brushes.White : TextMuted
        };
        b.Click += onClick;
        return b;
    }
}
