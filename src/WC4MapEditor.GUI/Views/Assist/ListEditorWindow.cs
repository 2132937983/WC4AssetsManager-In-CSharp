using System.Collections.Specialized;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// "左列表 + 右字段面板"类编辑窗口的公共基类（事件、天气、方案、空袭、空中支援、战略建设、援军……）。
/// <para>
/// 统一外观与交互：无边框窗口 + 可拖动标题栏；列表下方 + / ⊕ / − 按钮；字段改动后按 Enter 或"应用"提交，
/// Esc 先撤回未提交的改动、再按一次关闭；切换列表项时自动提交（无效输入会留在当前项并提示）。
/// 增删改都经由 <see cref="RecordChange"/> 记录撤销（Ctrl+Z / Ctrl+Y 在地图上撤销）。
/// </para>
/// <para>
/// 子类实现数据访问（条目数、描述、读写字段、增删），并在类上标注 <c>[AssistEditor(EditorKinds.Xxx)]</c>，
/// 模式里 <c>context.OpenEditor(EditorKinds.Xxx)</c> 即可打开，无需改渲染场景。
/// </para>
/// </summary>
public abstract class ListEditorWindow : Window, IAssistEditor
{
    private readonly ListBox _list = new();
    private readonly StackPanel _fieldsHost = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptyHint = EditorTheme.EmptyHint("");
    private Button? _duplicateButton;
    private Button? _deleteButton;
    private Button? _applyButton;
    private bool _loading;
    private bool _dirty;
    private int _current = -1;
    private INotifyCollectionChanged? _observed;
    private bool _selfEdit;
    private bool _refreshQueued;
    private bool _closed;

    protected ListEditorWindow()
    {
        EditorTheme.Apply(this);
        Width = 760;
        Height = 560;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = EditorTheme.Bg1;
        ShowInTaskbar = false;
        PreviewKeyDown += OnPreviewKeyDown;
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    // ================================================================== 子类实现

    /// <summary>窗口标题（已本地化）。</summary>
    protected abstract string TitleText { get; }

    /// <summary>当前条目数。</summary>
    protected abstract int ItemCount { get; }

    /// <summary>列表里第 <paramref name="index"/> 项的显示文本。</summary>
    protected abstract string DescribeItem(int index);

    /// <summary>创建字段编辑控件（只调用一次）。</summary>
    protected abstract void BuildFields(FieldPanel fields);

    /// <summary>把第 <paramref name="index"/> 项显示到字段控件里。</summary>
    protected abstract void LoadFields(int index);

    /// <summary>校验字段并写回第 <paramref name="index"/> 项；有错误时写入 <paramref name="errors"/> 并返回 false（不写数据）。</summary>
    protected abstract bool TryCommitFields(int index, List<string> errors);

    /// <summary>新增一项（默认值），返回新项的下标；在 <see cref="RecordChange"/> 内调用，不必自己记撤销。</summary>
    protected abstract int AddItem();

    /// <summary>删除第 <paramref name="index"/> 项；在 <see cref="RecordChange"/> 内调用。</summary>
    protected abstract void RemoveItem(int index);

    /// <summary>
    /// 记录一次对所编辑列表的修改（撤销用）。通常一行即可：
    /// <c>Context.Editor.RecordCollectionChange(description, Map.Events, apply);</c>
    /// </summary>
    protected abstract void RecordChange(string description, Action apply);

    /// <summary>是否提供"复制一项"（⊕）。</summary>
    protected virtual bool SupportsDuplicate => false;

    /// <summary>复制第 <paramref name="index"/> 项，返回新项下标；在 <see cref="RecordChange"/> 内调用。</summary>
    protected virtual int DuplicateItem(int index) => -1;

    /// <summary>修改影响的数据范围（场景据此失效缓存）。</summary>
    protected virtual EditorDataScope ChangeScope => EditorDataScope.Entities;

    /// <summary>删除前是否需要确认。</summary>
    protected virtual bool ConfirmDelete => true;

    /// <summary>
    /// 窗口编辑的列表。提供后基类监听它：列表在窗口外被改动（地图上撤销 / 重做、按键批量生成、控制台命令）时，
    /// 合并同一批通知、空闲时整表刷新（未提交的字段改动丢弃，免得再按旧下标改错条目）。
    /// 本窗口自己经由 <see cref="RecordChange"/> 发起的修改不触发刷新。
    /// </summary>
    protected virtual INotifyCollectionChanged? ObservedList => null;

    /// <summary>因窗口外的改动刷新列表后，在状态行显示的提示；null 表示不提示。</summary>
    protected virtual string? ExternalChangeMessage => null;

    /// <summary>第 <paramref name="index"/> 项的列表图标；null 表示该项只显示文字。</summary>
    protected virtual ImageSource? ItemIcon(int index) => null;

    /// <summary>窗口附着到场景后调用（此时 <see cref="Context"/> 可用、列表已填充），可在此按请求定位条目。</summary>
    protected virtual void OnAttached() { }

    /// <summary>窗口已打开时又收到同类请求（例如在另一个格子上按 Q）。</summary>
    protected virtual void OnRetarget(EditorRequest request) { }

    // ================================================================== 供子类使用

    protected AssistEditorContext Context { get; private set; } = null!;
    protected MapData Map => Context.MapData;

    /// <summary>当前选中项下标（无则 -1）。</summary>
    protected int CurrentIndex => _current;

    /// <summary>当前项的字段是否有还没提交的修改。</summary>
    protected bool HasUncommittedChanges => _dirty;

    /// <summary>重新填充列表并选中 <paramref name="select"/>（越界时就近选择）。</summary>
    protected void RefreshList(int select)
    {
        _loading = true;
        try
        {
            _list.Items.Clear();
            int n = ItemCount;
            for (int i = 0; i < n; i++) _list.Items.Add(CreateRow(i));
            _emptyHint.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (n == 0)
            {
                _current = -1;
                SetFieldsEnabled(false);
            }
            else
            {
                select = Math.Clamp(select, 0, n - 1);
                _list.SelectedIndex = select;
                _list.ScrollIntoView(_list.SelectedItem);
                ShowItem(select);
            }
        }
        finally
        {
            _loading = false;
        }
        UpdateButtons();
    }

    /// <summary>选中某一项（会先尝试提交当前项）。</summary>
    protected void SelectItem(int index)
    {
        if (index < 0 || index >= ItemCount) return;
        _list.SelectedIndex = index;
    }

    /// <summary>在状态行显示提示（null 清空）。</summary>
    protected void SetStatus(string? message, bool isError = false)
    {
        _status.Text = message ?? "";
        _status.Foreground = isError ? EditorTheme.Danger : EditorTheme.TextMuted;
    }

    /// <summary>字段控件内容被用户修改时调用（<see cref="FieldPanel"/> 创建的控件会自动调用）。</summary>
    protected void MarkDirty()
    {
        if (_loading || _current < 0) return;
        _dirty = true;
        UpdateButtons();
    }

    /// <summary>通知场景数据已改（失效缓存并重绘）。</summary>
    protected void NotifyChanged() => Context.NotifyChanged(ChangeScope);

    // ================================================================== IAssistEditor

    void IAssistEditor.Attach(AssistEditorContext context)
    {
        Context = context;
        Title = TitleText;
        Content = BuildLayout();
        _observed = ObservedList;
        if (_observed != null) _observed.CollectionChanged += OnObservedListChanged;
        RefreshList(0);
        OnAttached();
    }

    void IAssistEditor.Retarget(EditorRequest request) => OnRetarget(request);

    // ================================================================== 布局

    private FrameworkElement BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = BuildTitleBar();
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260), MinWidth = 160 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // 左：列表 + 增删按钮
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _list.Margin = new Thickness(4, 4, 4, 0);
        _list.SelectionChanged += OnListSelectionChanged;
        left.Children.Add(_list);
        _emptyHint.Text = Loc.T("Assist.ListEditor.Empty", "列表为空，点击 + 新增");
        _emptyHint.Margin = new Thickness(8, 24, 8, 0);
        left.Children.Add(_emptyHint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8)
        };
        buttons.Children.Add(SmallButton(EditorTheme.Button("+", (_, _) => OnAdd()), Loc.T("Assist.ListEditor.Add", "新增")));
        if (SupportsDuplicate)
        {
            _duplicateButton = SmallButton(EditorTheme.Button("⊕", (_, _) => OnDuplicate()), Loc.T("Assist.ListEditor.Duplicate", "复制选中项"));
            buttons.Children.Add(_duplicateButton);
        }
        _deleteButton = SmallButton(EditorTheme.DangerButton("−", (_, _) => OnDelete()), Loc.T("Assist.ListEditor.Delete", "删除选中项"));
        buttons.Children.Add(_deleteButton);
        Grid.SetRow(buttons, 1);
        left.Children.Add(buttons);

        var leftPanel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        leftPanel.Child = left;
        body.Children.Add(leftPanel);

        var splitter = EditorTheme.VSplitter();
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        // 右：字段
        BuildFields(new FieldPanel(this, _fieldsHost));
        var scroll = new ScrollViewer
        {
            Content = _fieldsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 2);
        body.Children.Add(scroll);

        // 底：状态 + 应用 / 关闭
        var bottom = new Grid { Margin = new Thickness(10, 6, 10, 8) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _applyButton = EditorTheme.PrimaryButton(Loc.T("Assist.ListEditor.Apply", "应用"), (_, _) => CommitCurrent());
        _applyButton.ToolTip = Loc.T("Assist.ListEditor.ApplyTooltip", "提交当前项的修改（Enter）");
        _applyButton.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_applyButton);
        actions.Children.Add(EditorTheme.Button(Loc.T("Common.Close", "关闭"), (_, _) => Close()));
        Grid.SetColumn(actions, 1);
        bottom.Children.Add(actions);
        var bottomBorder = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bottom
        };
        Grid.SetRow(bottomBorder, 2);
        root.Children.Add(bottomBorder);

        return new Border { BorderBrush = EditorTheme.BorderStrong, BorderThickness = new Thickness(1), Child = root };
    }

    private Border BuildTitleBar()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = EditorTheme.Title(TitleText);
        label.Margin = new Thickness(12, 0, 0, 0);
        panel.Children.Add(label);
        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 32;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(0, 0, 6, 0);
        close.ToolTip = Loc.T("Common.Close", "关闭");
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);
        var bar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel
        };
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        return bar;
    }

    private static Button SmallButton(Button b, string tooltip)
    {
        b.Width = 36;
        b.Padding = new Thickness(0, 3, 0, 3);
        b.FontSize = 14;
        b.FontWeight = FontWeights.Bold;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = tooltip;
        return b;
    }

    // ================================================================== 交互

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        int target = _list.SelectedIndex;
        if (target == _current) return;

        // 切换前自动提交；输入无效则留在当前项
        if (_dirty && _current >= 0 && !CommitCurrent())
        {
            _loading = true;
            _list.SelectedIndex = _current;
            _loading = false;
            return;
        }
        if (target >= 0) ShowItem(target);
        UpdateButtons();
    }

    private void ShowItem(int index)
    {
        _current = index;
        _loading = true;
        try
        {
            SetFieldsEnabled(true);
            LoadFields(index);
        }
        finally
        {
            _loading = false;
        }
        _dirty = false;
        SetStatus(null);
        UpdateButtons();
    }

    /// <summary>提交当前项；成功（或没有改动）返回 true。</summary>
    protected bool CommitCurrent()
    {
        if (_current < 0 || _current >= ItemCount) return true;
        if (!_dirty) return true;

        var errors = new List<string>();
        int index = _current;
        bool ok = false;
        Record(Loc.F("Assist.ListEditor.UndoEdit", TitleText, index + 1), () => ok = TryCommitFields(index, errors));
        if (!ok)
        {
            SetStatus(string.Join("；", errors.Count > 0 ? errors : new List<string> { Loc.T("Assist.ListEditor.Invalid", "输入无效") }), true);
            return false;
        }

        _dirty = false;
        NotifyChanged();
        RefreshListText(index);
        SetStatus(Loc.T("Assist.ListEditor.Applied", "已应用"));
        UpdateButtons();
        return true;
    }

    private void RefreshListText(int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        _loading = true;
        try
        {
            _list.Items[index] = CreateRow(index);
            _list.SelectedIndex = index;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnAdd()
    {
        if (!CommitCurrent()) return;
        int newIndex = -1;
        Record(Loc.F("Assist.ListEditor.UndoAdd", TitleText), () => newIndex = AddItem());
        NotifyChanged();
        RefreshList(newIndex >= 0 ? newIndex : ItemCount - 1);
    }

    private void OnDuplicate()
    {
        if (_current < 0 || !CommitCurrent()) return;
        int source = _current;
        int newIndex = -1;
        Record(Loc.F("Assist.ListEditor.UndoDuplicate", TitleText, source + 1), () => newIndex = DuplicateItem(source));
        NotifyChanged();
        RefreshList(newIndex >= 0 ? newIndex : source);
    }

    private void OnDelete()
    {
        if (_current < 0) return;
        int index = _current;
        if (ConfirmDelete)
        {
            var answer = MessageBox.Show(this,
                Loc.F("Assist.ListEditor.ConfirmDelete", DescribeItem(index)),
                TitleText, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        _dirty = false;
        Record(Loc.F("Assist.ListEditor.UndoDelete", TitleText, index + 1), () => RemoveItem(index));
        NotifyChanged();
        RefreshList(index);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_dirty && _current >= 0)
            {
                ShowItem(_current);   // 撤回未提交的改动
                SetStatus(Loc.T("Assist.ListEditor.Reverted", "已撤回未提交的修改"));
            }
            else
            {
                Close();
            }
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !(e.OriginalSource is TextBox { AcceptsReturn: true }))
        {
            e.Handled = true;
            CommitCurrent();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 关闭时提交有效的改动；无效输入不阻止关闭（数据保持原样）
        if (_dirty && _current >= 0)
        {
            var errors = new List<string>();
            int index = _current;
            bool ok = false;
            Record(Loc.F("Assist.ListEditor.UndoEdit", TitleText, index + 1), () => ok = TryCommitFields(index, errors));
            if (ok) NotifyChanged();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_observed != null) _observed.CollectionChanged -= OnObservedListChanged;
        _observed = null;
        base.OnClosed(e);
    }

    /// <summary>经 <see cref="RecordChange"/> 记录一次修改；期间列表的变化通知是自己发起的，不触发整表刷新。</summary>
    private void Record(string description, Action apply)
    {
        _selfEdit = true;
        try
        {
            RecordChange(description, apply);
        }
        finally
        {
            _selfEdit = false;
        }
    }

    private void OnObservedListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selfEdit || _refreshQueued || _closed) return;
        _refreshQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _refreshQueued = false;
            if (_closed) return;
            RefreshList(_current);
            if (ExternalChangeMessage is { } message) SetStatus(message);
        }, DispatcherPriority.Background);
    }

    /// <summary>列表里一行的内容：没有图标时就是文字，有图标时是"图标 + 文字"。</summary>
    private object CreateRow(int index)
    {
        string text = DescribeItem(index);
        if (ItemIcon(index) is not { } icon) return text;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void SetFieldsEnabled(bool enabled) => _fieldsHost.IsEnabled = enabled;

    private void UpdateButtons()
    {
        bool has = _current >= 0 && _current < ItemCount;
        if (_deleteButton != null) _deleteButton.IsEnabled = has;
        if (_duplicateButton != null) _duplicateButton.IsEnabled = has;
        if (_applyButton != null) _applyButton.IsEnabled = has && _dirty;
    }

    // ================================================================== 字段面板

    /// <summary>
    /// 字段控件工厂：每个方法添加一行"字段名 + 编辑控件"，控件被修改时自动标记"有未提交改动"。
    /// 解析辅助（<see cref="TryInt"/> 等）把错误写进列表，提交时统一显示。
    /// </summary>
    protected sealed class FieldPanel
    {
        private readonly ListEditorWindow _owner;
        private readonly StackPanel _host;

        internal FieldPanel(ListEditorWindow owner, StackPanel host)
        {
            _owner = owner;
            _host = host;
        }

        /// <summary>字段名列宽。</summary>
        public double LabelWidth { get; set; } = 130;

        public void Section(string title)
        {
            var header = EditorTheme.SectionHeader(title);
            if (_host.Children.Count == 0) header.Margin = new Thickness(0);
            _host.Children.Add(header);
        }

        public void Hint(string text)
        {
            var t = EditorTheme.Muted(text);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(10, 4, 10, 4);
            _host.Children.Add(t);
        }

        public TextBox Text(string label, string? tooltip = null)
        {
            var box = new TextBox { ToolTip = tooltip };
            box.TextChanged += (_, _) => _owner.MarkDirty();
            Add(label, box, tooltip);
            return box;
        }

        /// <summary>
        /// 选项字段：可编辑下拉框，列出"值 - 名称"（来自 setting.txt 等配置），也允许直接输入列表外的数值。
        /// 读写用 <see cref="SetChoice"/> / <see cref="TryChoice"/>。
        /// </summary>
        public ComboBox Choice(string label, IReadOnlyList<ConfigKeyValue> options, string? tooltip = null)
        {
            var combo = EditorTheme.ComboBox();
            combo.IsEditable = true;
            combo.ToolTip = tooltip;
            foreach (var o in options) combo.Items.Add($"{o.Value} - {o.Name}");
            // 窗口显示时下拉框才套用模板，内部文本框此时同步一次文本也会触发 TextChanged：
            // 这种程序性的变化不是用户改的，只在键盘焦点在框内或下拉已展开时才标记"有未提交修改"
            void UserChanged()
            {
                if (combo.IsKeyboardFocusWithin || combo.IsDropDownOpen) _owner.MarkDirty();
            }
            combo.SelectionChanged += (_, _) => UserChanged();
            combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => UserChanged()));
            Add(label, combo, tooltip);
            return combo;
        }

        public CheckBox Flag(string label, string? tooltip = null)
        {
            var box = new CheckBox { ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center };
            box.Checked += (_, _) => _owner.MarkDirty();
            box.Unchecked += (_, _) => _owner.MarkDirty();
            Add(label, box, tooltip);
            return box;
        }

        /// <summary>任意自定义控件（自己负责在修改时调用 owner 的 MarkDirty —— 可用 <see cref="Changed"/>）。</summary>
        public void Custom(string label, UIElement content, string? tooltip = null) => Add(label, content, tooltip);

        /// <summary>给自定义控件用：内容改变时调用它标记未提交改动。</summary>
        public Action Changed => _owner.MarkDirty;

        private void Add(string label, UIElement content, string? tooltip)
            => _host.Children.Add(EditorTheme.FormRow(label, content, LabelWidth, tooltip));
    }

    // ================================================================== 解析辅助

    /// <summary>解析整数并检查范围；失败时把"字段名: 原因"加入 errors。</summary>
    protected static bool TryInt(TextBox box, string label, int min, int max, List<string> errors, out int value)
    {
        string s = box.Text.Trim();
        if (!int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            errors.Add(Loc.F("Assist.ListEditor.NotInteger", label));
            return false;
        }
        if (value < min || value > max)
        {
            errors.Add(Loc.F("Assist.ListEditor.OutOfRange", label, min, max));
            return false;
        }
        return true;
    }

    /// <summary>选项框显示某个值：列表里有则选中"值 - 名称"，否则直接显示数值。</summary>
    protected static void SetChoice(ComboBox combo, int value)
    {
        string prefix = value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " - ";
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is string s && s.StartsWith(prefix, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
        combo.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>读取选项框的值（取"值 - 名称"开头的数字，或手输的数字）并检查范围。</summary>
    protected static bool TryChoice(ComboBox combo, string label, int min, int max, List<string> errors, out int value)
    {
        string text = (combo.SelectedItem as string ?? combo.Text ?? "").Trim();
        int dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) text = text[..dash];
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            errors.Add(Loc.F("Assist.ListEditor.NotInteger", label));
            return false;
        }
        if (value < min || value > max)
        {
            errors.Add(Loc.F("Assist.ListEditor.OutOfRange", label, min, max));
            return false;
        }
        return true;
    }
}
