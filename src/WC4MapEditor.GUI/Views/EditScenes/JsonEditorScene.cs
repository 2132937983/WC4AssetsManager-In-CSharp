using System.Diagnostics;
using System.Windows.Controls.Primitives;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>
/// 有未保存修改的场景。离开前（返回、切换场景、关闭窗口）调用 <see cref="ConfirmLeave"/>：
/// 询问保存 / 放弃 / 取消，返回 false 表示用户取消、不要离开。
/// </summary>
public interface IUnsavedChangesScene
{
    bool ConfirmLeave();
}

/// <summary>
/// JSON 表编辑场景的公共外壳：编辑会话（历史 / 保存 / 放弃）、顶栏标准按钮、带未保存标记的状态栏、
/// 快捷键、F1 帮助、历史窗口、加载错误提示与离开前的保存询问。
/// <para>具体场景只负责布局和字段；列表类操作交给 <see cref="ActiveEditor"/>。</para>
/// </summary>
public abstract class JsonEditorScene : UserControl, IUnsavedChangesScene, IDisposable
{
    private readonly string _helpKey;
    private readonly List<ITableEditor> _editors = new();
    private Button? _saveButton, _undoButton, _redoButton;
    private TextBlock _statusText = null!;
    private Border _dirtyBadge = null!;
    private TextBlock _dirtyText = null!;
    private Window? _keyWindow;
    private bool _loadErrorShown;
    private bool _disposed;

    protected JsonEditorScene(MainWindow window, string helpKey, params IGameSettingsSource[] sources)
    {
        EditorTheme.Apply(this);
        AppWindow = window;
        _helpKey = helpKey;
        Session = new JsonEditSession(sources);
        Session.Changed += UpdateChrome;
        Session.StateReplaced += OnStateReplaced;
        Session.TextsReloaded += OnTextsReloaded;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected MainWindow AppWindow { get; }

    public JsonEditSession Session { get; }

    /// <summary>接收删除 / 复制 / 粘贴 / 排序 / 批量 / 搜索命令的表编辑器。</summary>
    protected ITableEditor? ActiveEditor { get; set; }

    /// <summary>模态对话框的所有者窗口。</summary>
    public Window DialogOwner => Window.GetWindow(this) ?? AppWindow;

    /// <summary>“新增”按钮的文字。</summary>
    protected virtual string AddButtonText => Loc.T("Common.Add");

    // ============================================================= 布局 =============================================================

    /// <summary>登记一个表编辑器：撤销 / 重新加载后由场景统一刷新。</summary>
    protected TableEditor<T> Register<T>(TableEditor<T> editor) where T : class
    {
        _editors.Add(editor);
        ActiveEditor ??= editor;
        return editor;
    }

    /// <summary>
    /// 顶栏：返回（离开前询问保存）+ 标题 | 场景自己的按钮 + 新增 / 删除 / 保存 / 重新加载 / 校验 + 撤销 / 重做 / 历史 / 帮助。
    /// </summary>
    protected Border CreateTopBar(string title, params UIElement[] sceneActions)
    {
        var actions = new List<UIElement>(sceneActions);
        if (sceneActions.Length > 0) actions.Add(EditorTheme.VSeparator());
        actions.Add(Action(EditorTheme.Button(AddButtonText), "JsonEditor.Tip.Add", OnAddCommand));
        actions.Add(Action(EditorTheme.DangerButton(Loc.T("Common.Delete")), "JsonEditor.Tip.Delete", OnDeleteCommand));
        _saveButton = Action(EditorTheme.PrimaryButton(Loc.T("Common.Save")), "JsonEditor.Tip.Save", () => SaveAll());
        actions.Add(_saveButton);
        actions.Add(Action(EditorTheme.Button(Loc.T("Common.Reload")), "JsonEditor.Tip.Reload", ReloadFromDisk));
        actions.Add(Action(EditorTheme.Button(Loc.T("Common.Validate")), "JsonEditor.Tip.Validate", OnValidateCommand));
        actions.Add(EditorTheme.VSeparator());
        _undoButton = Action(EditorTheme.ToolButton("↶"), "JsonEditor.Tip.Undo", Undo);
        _redoButton = Action(EditorTheme.ToolButton("↷"), "JsonEditor.Tip.Redo", Redo);
        actions.Add(_undoButton);
        actions.Add(_redoButton);
        actions.Add(Action(EditorTheme.ToolButton(Loc.T("JsonEditor.Action.History")), "JsonEditor.Tip.History", ShowHistory));
        actions.Add(Action(EditorTheme.ToolButton("?"), "JsonEditor.Tip.Help", ShowHelp));
        var bar = EditorTheme.TopBar(title, (_, _) => GoBack(), actions.ToArray());
        UpdateChrome();
        return bar;
    }

    private static Button Action(Button button, string tipKey, Action action)
    {
        button.ToolTip = Loc.T(tipKey);
        button.Focusable = false;   // 按钮不抢键盘焦点，避免空格 / 回车重复点击
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>状态栏：左侧状态文字，右侧“未保存”标记与 F1 提示。</summary>
    protected Border CreateStatusBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusText = new TextBlock
        {
            Foreground = EditorTheme.TextMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = Loc.T("Common.Ready")
        };
        grid.Children.Add(_statusText);

        _dirtyText = new TextBlock { FontSize = 11, Foreground = Brushes.White };
        _dirtyBadge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _dirtyText
        };
        Grid.SetColumn(_dirtyBadge, 1);
        grid.Children.Add(_dirtyBadge);

        var hint = EditorTheme.Muted(Loc.T("JsonEditor.Status.Hint"));
        hint.FontSize = 11;
        hint.Margin = new Thickness(0, 0, 12, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hint, 2);
        grid.Children.Add(hint);

        UpdateChrome();
        return new Border
        {
            Height = 26,
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid
        };
    }

    /// <summary>标准三段布局：顶栏 / 左列表 | 分隔条 | 右表单 / 状态栏。</summary>
    protected Grid CreateListFormLayout(Border topBar, UIElement left, UIElement right, double leftWidth = 300)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumnSpan(topBar, 3);
        root.Children.Add(topBar);
        Grid.SetRow(left, 1);
        root.Children.Add(left);
        var splitter = EditorTheme.VSplitter();
        Grid.SetRow(splitter, 1);
        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        var status = CreateStatusBar();
        Grid.SetRow(status, 2);
        Grid.SetColumnSpan(status, 3);
        root.Children.Add(status);
        return root;
    }

    public void SetStatus(string text)
    {
        if (_statusText != null) _statusText.Text = text;
        Debug.WriteLine($"[{GetType().Name}] {text}");
    }

    private void UpdateChrome()
    {
        if (_disposed) return;
        bool dirty = Session.HasUnsavedChanges;
        if (_dirtyBadge != null)
        {
            _dirtyBadge.Background = dirty ? EditorTheme.Warning : EditorTheme.Bg3;
            _dirtyText.Text = Loc.T(dirty ? "JsonEditor.Status.Unsaved" : "JsonEditor.Status.Clean");
            _dirtyText.Foreground = dirty ? Brushes.Black : EditorTheme.TextMuted;
        }
        if (_saveButton != null)
        {
            _saveButton.IsEnabled = Session.CanSave;
            _saveButton.ToolTip = Session.CanSave ? Loc.T("JsonEditor.Tip.Save") : Loc.T("JsonEditor.Tip.SaveDisabled");
        }
        if (_undoButton != null) _undoButton.IsEnabled = Session.CanUndo;
        if (_redoButton != null) _redoButton.IsEnabled = Session.CanRedo;
    }

    // ============================================================= 命令（场景覆盖） =============================================================

    protected virtual void OnAddCommand() { }

    protected virtual void OnDeleteCommand() => ActiveEditor?.DeleteSelected();

    protected virtual void OnValidateCommand() { }

    /// <summary>保存前的场景检查（例如集团军必须有援军）；返回拒绝原因或 null。</summary>
    protected virtual string? CheckBeforeSave() => null;

    /// <summary>保存成功之后（状态栏已显示“已保存”）；场景可以补充提示，例如校验时发现的非阻断警告。</summary>
    protected virtual void OnSaved() { }

    /// <summary>记录被整体替换之后（撤销 / 重做 / 重新加载）刷新界面。</summary>
    protected virtual void OnStateReplaced()
    {
        foreach (var editor in _editors) editor.Refresh();
    }

    /// <summary>字符串表被外部修改后刷新文本。</summary>
    protected virtual void OnTextsReloaded()
    {
        foreach (var editor in _editors) editor.RefreshTexts();
        SetStatus(Loc.T("JsonEditor.Status.TextsReloaded"));
    }

    /// <summary>场景自己的快捷键；返回 true 表示已处理。</summary>
    protected virtual bool HandleSceneKey(Key key, ModifierKeys modifiers, bool textFocused) => false;

    /// <summary>返回按钮的目的地。</summary>
    protected virtual void NavigateBack() => AppWindow.ReturnToMainScene();

    /// <summary>表单里还没写回的错误输入（保存前提醒）。</summary>
    protected virtual IReadOnlyList<string> InvalidFields() => Array.Empty<string>();

    // ============================================================= 保存 / 重新加载 / 离开 =============================================================

    /// <summary>
    /// 让正在输入的主键 / 关联键字段（失去焦点才提交）先提交，再记下等待合并的输入。
    /// 保存、撤销、重新加载、离开之前调用，避免输入框里的值没有算进去。
    /// </summary>
    private void CommitPendingInput()
    {
        if (Keyboard.FocusedElement is DependencyObject focused && IsAncestorOf(focused)) Keyboard.ClearFocus();
        Session.Flush();
    }

    /// <summary>保存会话里所有修改；被拒绝或失败时提示并返回 false。</summary>
    protected bool SaveAll()
    {
        CommitPendingInput();
        if (Session.LoadError is { } loadError)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("JsonEditor.LoadError.Title"), Loc.F("JsonEditor.Error.SaveBlockedSession", loadError));
            return false;
        }
        var invalid = InvalidFields();
        if (invalid.Count > 0 && !EditorDialogs.Confirm(DialogOwner, Loc.T("Common.Save"), Loc.F("JsonEditor.Save.InvalidFields", string.Join(", ", invalid))))
            return false;
        if (CheckBeforeSave() is { } blocked)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("JsonEditor.Save.BlockedTitle"), blocked);
            SetStatus(blocked);
            return false;
        }
        var external = Session.ExternallyChangedFiles();
        if (external.Count > 0 && !EditorDialogs.Confirm(DialogOwner, Loc.T("Common.Save"),
                Loc.F("JsonEditor.Save.ExternalChange", string.Join(", ", external)), Loc.T("JsonEditor.Save.Overwrite"), danger: true))
            return false;
        if (!Session.Save(out var error))
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.SaveFailed"), error ?? "");
            SetStatus(Loc.T("Common.SaveFailed"));
            return false;
        }
        SetStatus(Loc.F("JsonEditor.Status.Saved", string.Join(", ", Session.FileNames)));
        OnSaved();
        return true;
    }

    /// <summary>从磁盘重新加载（有未保存修改时先询问）。</summary>
    protected void ReloadFromDisk()
    {
        CommitPendingInput();
        if (Session.IsDirty)
        {
            switch (EditorDialogs.AskUnsaved(DialogOwner, Loc.T("JsonEditor.Unsaved.Reload")))
            {
                case UnsavedChoice.Save:
                    if (!SaveAll()) return;
                    break;
                case UnsavedChoice.Cancel:
                    return;
            }
        }
        Session.Reload();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
        ReportLoadErrors();
    }

    public bool ConfirmLeave()
    {
        CommitPendingInput();
        if (!Session.IsDirty) return true;
        switch (EditorDialogs.AskUnsaved(DialogOwner, Loc.T("JsonEditor.Unsaved.Leave")))
        {
            case UnsavedChoice.Save:
                return SaveAll();
            case UnsavedChoice.Discard:
                // 解析器是全局单例：放弃 = 从磁盘恢复，别的场景 / 渲染器不会再看到这些修改
                Session.Reload();
                return true;
            default:
                return false;
        }
    }

    protected void GoBack()
    {
        if (ConfirmLeave()) NavigateBack();
    }

    private void ReportLoadErrors()
    {
        if (Session.LoadError is not { } error) return;
        SetStatus(Loc.T("JsonEditor.LoadError.Status"));
        EditorDialogs.Error(DialogOwner, Loc.T("JsonEditor.LoadError.Title"), Loc.F("JsonEditor.LoadError.Message", error));
    }

    // ============================================================= 历史 / 帮助 =============================================================

    protected void Undo()
    {
        CommitPendingInput();
        string label = Session.History[Session.HistoryIndex].Label;
        if (Session.Undo()) SetStatus(Loc.F("JsonEditor.Status.Undone", label));
        else SetStatus(Loc.T("JsonEditor.Status.NothingToUndo"));
    }

    protected void Redo()
    {
        CommitPendingInput();
        if (Session.Redo()) SetStatus(Loc.F("JsonEditor.Status.Redone", Session.History[Session.HistoryIndex].Label));
        else SetStatus(Loc.T("JsonEditor.Status.NothingToRedo"));
    }

    protected void ShowHistory()
    {
        CommitPendingInput();
        HistoryWindow.Show(DialogOwner, Session, SetStatus);
    }

    protected void ShowHelp() => EditorDialogs.ShowText(DialogOwner, Loc.T("JsonEditor.Help.Title"),
        Loc.T("JsonEditor.Help.Common") + Environment.NewLine + Environment.NewLine + Loc.T("JsonEditor.Help." + _helpKey));

    /// <summary>校验结果：没问题时提示通过，否则列出前 80 条。</summary>
    protected void ShowValidation(IReadOnlyList<string> problems, int total)
    {
        if (problems.Count == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Validate"), Loc.F("Common.ValidationPassed", total));
        }
        else
        {
            var msg = string.Join(Environment.NewLine, problems.Take(80));
            if (problems.Count > 80) msg += Environment.NewLine + Loc.F("Common.ValidationMore", problems.Count, 80);
            EditorDialogs.Warning(DialogOwner, Loc.F("Common.ValidationProblems", problems.Count), msg);
        }
        SetStatus(Loc.F("Common.ValidationDone", problems.Count));
    }

    // ============================================================= 快捷键 =============================================================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _keyWindow = Window.GetWindow(this);
        if (_keyWindow != null) _keyWindow.PreviewKeyDown += OnWindowPreviewKeyDown;
        if (_loadErrorShown) return;
        _loadErrorShown = true;
        if (Session.LoadError != null) Dispatcher.BeginInvoke(ReportLoadErrors, DispatcherPriority.ApplicationIdle);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_keyWindow != null) _keyWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
        _keyWindow = null;
    }

    /// <summary>
    /// 挂在窗口上：焦点不在任何输入控件（例如刚点过按钮）时快捷键同样有效。
    /// 焦点在场景之外（主窗口里叠加的对话框）时不处理。
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || e.Handled) return;
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused != null && !ReferenceEquals(focused, _keyWindow) && !IsAncestorOf(focused)) return;

        bool textFocused = focused is TextBoxBase or PasswordBox || focused is ComboBox { IsEditable: true };
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleSceneKey(key, mods, textFocused) || HandleStandardKey(key, mods, textFocused))
            e.Handled = true;
    }

    private bool HandleStandardKey(Key key, ModifierKeys mods, bool textFocused)
    {
        bool ctrl = mods == ModifierKeys.Control;
        switch (key)
        {
            case Key.S when ctrl: SaveAll(); return true;
            case Key.Z when ctrl: Undo(); return true;
            case Key.Y when ctrl:
            case Key.Z when mods == (ModifierKeys.Control | ModifierKeys.Shift): Redo(); return true;
            case Key.H when ctrl: ShowHistory(); return true;
            case Key.F when ctrl: ActiveEditor?.FocusSearch(); return true;
            case Key.N when ctrl: OnAddCommand(); return true;
            case Key.D when ctrl: ActiveEditor?.Duplicate(); return true;
            case Key.B when ctrl: ActiveEditor?.OpenBatch(); return true;
            case Key.R when ctrl:
            case Key.F5 when mods == ModifierKeys.None: ReloadFromDisk(); return true;
            case Key.F1 when mods == ModifierKeys.None: ShowHelp(); return true;
            case Key.Enter when ctrl:
                CommitPendingInput();
                SetStatus(InvalidFields() is { Count: > 0 } invalid
                    ? Loc.F("JsonEditor.Status.InvalidFields", string.Join(", ", invalid))
                    : Loc.T("JsonEditor.Status.Applied"));
                return true;
            case Key.Up when mods == ModifierKeys.Alt: ActiveEditor?.MoveSelected(-1); return true;
            case Key.Down when mods == ModifierKeys.Alt: ActiveEditor?.MoveSelected(1); return true;
        }
        if (textFocused) return false;   // 输入框里的 Ctrl+C / Ctrl+V / Delete 留给文字编辑
        switch (key)
        {
            case Key.C when ctrl: ActiveEditor?.CopySelected(); return true;
            case Key.V when ctrl: ActiveEditor?.Paste(); return true;
            case Key.Delete when mods == ModifierKeys.None: OnDeleteCommand(); return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_keyWindow != null) _keyWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
        foreach (var editor in _editors) editor.Stop();
        Session.Dispose();
        DisposeScene();
        GC.SuppressFinalize(this);
    }

    /// <summary>场景自己的清理（图片缓存、计时器）。</summary>
    protected virtual void DisposeScene() { }
}
