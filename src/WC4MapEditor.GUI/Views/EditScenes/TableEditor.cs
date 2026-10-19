using System.ComponentModel;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>场景通过它把通用命令（删除、复制粘贴、排序、批量、搜索）交给当前的表编辑器。</summary>
public interface ITableEditor
{
    void DeleteSelected();
    void CopySelected();
    void Paste();
    void Duplicate();
    void MoveSelected(int delta);
    void OpenBatch();
    void FocusSearch();

    /// <summary>记录对象被整体替换（撤销 / 重新加载）后重建列表，按原位置恢复选中。</summary>
    void Refresh();

    /// <summary>字符串表重新加载后刷新列表文字与表单。</summary>
    void RefreshTexts();

    /// <summary>停止计时器（场景卸载时）。</summary>
    void Stop();
}

/// <summary>表编辑器的数据约定：怎么显示、怎么搜索、Id 怎么取 / 分配，删除与粘贴时要连带处理什么。</summary>
public sealed class TableEditorOptions<T> where T : class
{
    /// <summary>列表行文字。</summary>
    public required Func<T, string> Describe { get; init; }

    public required Func<T, int> GetId { get; init; }

    public required Action<T, int> SetId { get; init; }

    /// <summary>为粘贴的副本分配新 Id（副本尚未加入表）。</summary>
    public required Func<T, int> NextId { get; init; }

    /// <summary>搜索匹配（关键字已去掉首尾空白）；默认匹配列表行文字。</summary>
    public Func<T, string, bool>? Matches { get; init; }

    /// <summary>额外筛选（分类 / 分组下拉框）。</summary>
    public Func<T, bool>? Filter { get; init; }

    /// <summary>粘贴副本之后：(原记录 Id, 副本) —— 复制字符串表文本、XML 行、子表记录等。</summary>
    public Action<int, T>? AfterPaste { get; init; }

    /// <summary>删除一条记录（含级联删除）；默认只从表中移除。</summary>
    public Action<T>? Remove { get; init; }

    public string? SearchPlaceholder { get; init; }

    /// <summary>删除确认里附加的说明（级联删除了什么）。</summary>
    public string? DeleteNote { get; init; }

    /// <summary>列表上方的附加控件（筛选下拉框等）。</summary>
    public UIElement? Header { get; init; }
}

/// <summary>列表行：文字变化时通知列表就地刷新，不必重建整个列表。</summary>
internal sealed class RecordRow<T>(T item, string text) : INotifyPropertyChanged where T : class
{
    private string _text = text;

    public T Item { get; } = item;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => _text;
}

/// <summary>
/// 一张 JSON 表的“列表 + 表单”编辑器：搜索（防抖）、多选、按文件顺序显示与上下移动、
/// 复制 / 粘贴（新 Id + 连带文本）、批量修改、删除确认，每个操作都记入会话历史。
/// </summary>
public sealed class TableEditor<T> : ITableEditor where T : class
{
    /// <summary>每种记录一个剪贴板，跨场景实例保留（复制后换个场景再回来也能粘贴）。</summary>
    private static List<(T Copy, int SourceId)>? s_clipboard;

    private readonly JsonEditorScene _host;
    private readonly TableEditorOptions<T> _o;
    private readonly ListBox _list;
    private readonly TextBox _search;
    private readonly DispatcherTimer _searchTimer;
    private readonly TextBlock _countText;
    private readonly Dictionary<T, RecordRow<T>> _rowOf = new(ReferenceEqualityComparer.Instance);
    private List<RecordRow<T>> _rows = new();
    private T? _current;
    private int[] _selectedIndexes = Array.Empty<int>();
    private int _currentIndex = -1;
    private bool _updating;

    public TableEditor(JsonEditorScene host, GameJsonTable<T> table, TableEditorOptions<T> options, double labelWidth = 200)
    {
        _host = host;
        _o = options;
        Table = table;
        Form = new RecordForm<T>(labelWidth);
        Form.FieldChanged += OnFieldChanged;
        Form.FieldRejected += (label, reason) => _host.SetStatus(Loc.F("JsonEditor.Status.Rejected", label, reason));

        _list = new ListBox { SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(RecordRow<T>.Text) };
        _list.SelectionChanged += OnSelectionChanged;

        var search = EditorTheme.SearchBox(options.SearchPlaceholder ?? Loc.T("Common.SearchPlaceholder"), out _search);
        search.Margin = new Thickness(8);
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Rebuild(null, _current, false);
        };
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        _countText = EditorTheme.Muted("");
        _countText.FontSize = 11;
        _countText.Margin = new Thickness(8, 0, 4, 0);
        ListPanel = BuildListPanel(search, options.Header);
    }

    public GameJsonTable<T> Table { get; }

    public RecordForm<T> Form { get; }

    /// <summary>左栏：[筛选] + 搜索框 + 列表 + 工具行。</summary>
    public UIElement ListPanel { get; }

    public ListBox List => _list;

    /// <summary>表单当前显示的记录（多选时是最后点选的那条）。</summary>
    public T? Current => _current;

    /// <summary>选中的记录（按列表顺序）。</summary>
    public IReadOnlyList<T> SelectedItems
    {
        get
        {
            var selected = new HashSet<object>(_list.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
            return _rows.Where(r => selected.Contains(r)).Select(r => r.Item).ToList();
        }
    }

    /// <summary>列表里可见的记录（按搜索 / 筛选过滤后，文件顺序）。</summary>
    public IReadOnlyList<T> VisibleItems => _rows.Select(r => r.Item).ToList();

    /// <summary>当前记录变化（表单已经加载新记录之后）。</summary>
    public event Action<T?>? CurrentChanged;

    /// <summary>某个字段被编辑并写回记录之后：(记录, 字段名)。</summary>
    public event Action<T, string>? FieldEdited;

    /// <summary>记录被增删、排序、批量修改或整体替换之后（分组下拉框、画布等据此刷新）。</summary>
    public event Action? RecordsChanged;

    /// <summary>短标签，用在历史与状态文字里。</summary>
    public string Tag(T item) => Loc.F("JsonEditor.RecordTag", _o.GetId(item));

    // ============================================================= 列表 =============================================================

    private UIElement BuildListPanel(UIElement search, UIElement? header)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        if (header != null)
        {
            Grid.SetRow(header, 0);
            grid.Children.Add(header);
        }
        Grid.SetRow(search, 1);
        grid.Children.Add(search);
        Grid.SetRow(_list, 2);
        grid.Children.Add(_list);

        var tools = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(Tool("▲", "JsonEditor.Tip.MoveUp", () => MoveSelected(-1)));
        tools.Children.Add(Tool("▼", "JsonEditor.Tip.MoveDown", () => MoveSelected(1)));
        tools.Children.Add(EditorTheme.VSeparator());
        tools.Children.Add(Tool(Loc.T("JsonEditor.Action.Copy"), "JsonEditor.Tip.Copy", CopySelected));
        tools.Children.Add(Tool(Loc.T("JsonEditor.Action.Paste"), "JsonEditor.Tip.Paste", Paste));
        tools.Children.Add(Tool(Loc.T("JsonEditor.Action.Batch"), "JsonEditor.Tip.Batch", OpenBatch));
        tools.Children.Add(_countText);
        var bar = EditorTheme.ToolBar(tools);
        bar.BorderThickness = new Thickness(0, 1, 0, 0);
        bar.Padding = new Thickness(6, 3, 6, 3);
        Grid.SetRow(bar, 3);
        grid.Children.Add(bar);

        var panel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        panel.Child = grid;
        return panel;
    }

    private static Button Tool(string text, string tipKey, Action action)
    {
        var b = EditorTheme.ToolButton(text, (_, _) => action());
        b.ToolTip = Loc.T(tipKey);
        b.Focusable = false;
        b.Margin = new Thickness(0, 0, 4, 0);
        return b;
    }

    /// <summary>重建列表：保持选中的记录（或选中 <paramref name="select"/>），表单只在当前记录变化时重新加载。</summary>
    public void RefreshList(IEnumerable<T>? select = null) => Rebuild(select, select == null ? _current : null, false);

    private void Rebuild(IEnumerable<T>? select, T? preferred, bool reloadForm)
    {
        var wanted = new HashSet<T>(select ?? SelectedItems, ReferenceEqualityComparer.Instance);
        if (select != null) preferred ??= select.LastOrDefault();
        string query = _search.Text.Trim();

        _updating = true;
        try
        {
            _rows = new List<RecordRow<T>>();
            _rowOf.Clear();
            foreach (var item in Table.Items)
            {
                if (_o.Filter != null && !_o.Filter(item)) continue;
                if (query.Length > 0 && !Match(item, query)) continue;
                var row = new RecordRow<T>(item, _o.Describe(item));
                _rows.Add(row);
                _rowOf[item] = row;
            }
            _list.ItemsSource = _rows;
            _list.SelectedItems.Clear();
            foreach (var row in _rows)
            {
                if (wanted.Contains(row.Item)) _list.SelectedItems.Add(row);
            }
        }
        finally
        {
            _updating = false;
        }
        _countText.Text = Loc.F("JsonEditor.List.Count", _rows.Count, Table.Items.Count);
        SyncCurrent(preferred, reloadForm);
        RememberSelection();
    }

    private bool Match(T item, string query)
        => _o.Matches?.Invoke(item, query) ?? _o.Describe(item).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _host.Session.Flush();
        T? preferred = e.AddedItems.Count > 0 && e.AddedItems[^1] is RecordRow<T> added ? added.Item : _current;
        SyncCurrent(preferred, false);
        RememberSelection();
    }

    private void SyncCurrent(T? preferred, bool reloadForm)
    {
        var selected = SelectedItems;
        T? next = preferred != null && selected.Contains(preferred) ? preferred : selected.LastOrDefault();
        bool changed = !ReferenceEquals(next, _current);
        _current = next;
        if (changed || reloadForm)
        {
            Form.Load(next);
            CurrentChanged?.Invoke(next);
        }
        if (next != null && _rowOf.TryGetValue(next, out var row)) _list.ScrollIntoView(row);
    }

    private void RememberSelection()
    {
        var items = Table.Items;
        _selectedIndexes = SelectedItems.Select(i => items.IndexOf(i)).Where(i => i >= 0).ToArray();
        _currentIndex = _current == null ? -1 : items.IndexOf(_current);
    }

    /// <summary>填充列表并选中第一条（场景打开时）。</summary>
    public void SelectFirst()
    {
        Rebuild(null, _current, false);
        if (_current == null && _rows.Count > 0) _list.SelectedItem = _rows[0];
    }

    /// <summary>选中一条记录（必要时清空搜索，让它出现在列表里）。</summary>
    public void Select(T item)
    {
        if (!_rowOf.ContainsKey(item) && _search.Text.Length > 0)
        {
            _search.Text = "";
            _searchTimer.Stop();
        }
        RefreshList(new[] { item });
    }

    /// <summary>把一条可见记录加入 / 移出多选（画布上 Ctrl+点击）。</summary>
    public void ToggleSelection(T item)
    {
        if (!_rowOf.TryGetValue(item, out var row)) return;
        if (_list.SelectedItems.Contains(row)) _list.SelectedItems.Remove(row);
        else _list.SelectedItems.Add(row);
    }

    /// <summary>刷新一条记录的列表文字。</summary>
    public void RefreshRow(T item)
    {
        if (_rowOf.TryGetValue(item, out var row)) row.Text = _o.Describe(item);
    }

    public void Refresh()
    {
        var items = Table.Items;
        var select = _selectedIndexes.Where(i => i < items.Count).Select(i => items[i]).ToList();
        if (select.Count == 0 && _selectedIndexes.Length > 0 && items.Count > 0)
            select.Add(items[Math.Min(_selectedIndexes[0], items.Count - 1)]);
        T? current = _currentIndex >= 0 && _currentIndex < items.Count ? items[_currentIndex] : select.LastOrDefault();
        Rebuild(select, current, true);
        RecordsChanged?.Invoke();
    }

    /// <summary>重新生成所有列表行的文字（共享文本改动后，别的记录的显示名也会变）。</summary>
    public void RefreshRows()
    {
        foreach (var row in _rows) row.Text = _o.Describe(row.Item);
    }

    public void RefreshTexts()
    {
        RefreshRows();
        Form.Reload();
    }

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    private void OnFieldChanged(T item, string label, bool immediate)
    {
        RefreshRow(item);
        string step = Loc.F("JsonEditor.History.Edit", Tag(item), label);
        if (immediate) _host.Session.Commit(step);
        else _host.Session.CommitLater(step);
        _host.SetStatus(Loc.F("JsonEditor.Status.Edited", Tag(item), label));
        FieldEdited?.Invoke(item, label);
    }

    // ============================================================= 增删 =============================================================

    /// <summary>插入新记录（默认追加到末尾）、选中它并记一步历史。</summary>
    public void Insert(T item, string historyLabel, int? index = null)
    {
        _host.Session.Flush();
        int at = Math.Clamp(index ?? Table.Items.Count, 0, Table.Items.Count);
        Table.Items.Insert(at, item);
        RecordsChanged?.Invoke();
        Select(item);
        _host.Session.Commit(historyLabel);
    }

    public void DeleteSelected()
    {
        var targets = SelectedItems;
        if (targets.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }
        string preview = string.Join(Environment.NewLine, targets.Take(8).Select(_o.Describe));
        if (targets.Count > 8) preview += Environment.NewLine + "…";
        if (_o.DeleteNote != null) preview += Environment.NewLine + Environment.NewLine + _o.DeleteNote;
        if (!EditorDialogs.Confirm(_host.DialogOwner, Loc.T("Common.Delete"), Loc.F("JsonEditor.Delete.Confirm", targets.Count, preview), Loc.T("Common.Delete"), danger: true))
            return;

        _host.Session.Flush();
        var visible = VisibleItems.ToList();
        int firstVisible = visible.IndexOf(targets[0]);
        foreach (var item in targets)
        {
            if (_o.Remove != null) _o.Remove(item);
            else Table.Items.Remove(item);
        }
        var remaining = visible.Where(v => !targets.Contains(v)).ToList();
        var next = remaining.Count == 0 ? Array.Empty<T>() : new[] { remaining[Math.Clamp(firstVisible, 0, remaining.Count - 1)] };
        RecordsChanged?.Invoke();
        Rebuild(next, next.FirstOrDefault(), true);
        _host.Session.Commit(Loc.F("JsonEditor.History.Delete", targets.Count));
        _host.SetStatus(Loc.F("JsonEditor.Status.Deleted", targets.Count));
    }

    // ============================================================= 复制 / 粘贴 =============================================================

    public void CopySelected()
    {
        var targets = SelectedItems;
        if (targets.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }
        s_clipboard = targets.Select(t => (Table.Clone(t), _o.GetId(t))).ToList();
        _host.SetStatus(Loc.F("JsonEditor.Status.Copied", targets.Count));
    }

    public void Paste()
    {
        if (s_clipboard == null || s_clipboard.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.ClipboardEmpty"));
            return;
        }
        PasteEntries(s_clipboard, "JsonEditor.History.Paste");
    }

    /// <summary>就地复制选中的记录（不占用剪贴板）。</summary>
    public void Duplicate()
    {
        var targets = SelectedItems;
        if (targets.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }
        PasteEntries(targets.Select(t => (Table.Clone(t), _o.GetId(t))).ToList(), "JsonEditor.History.Duplicate");
    }

    private void PasteEntries(List<(T Copy, int SourceId)> entries, string historyKey)
    {
        _host.Session.Flush();
        var items = Table.Items;
        var selected = SelectedItems;
        int at = selected.Count == 0 ? items.Count : selected.Max(s => items.IndexOf(s)) + 1;
        var pasted = new List<T>();
        foreach (var (template, sourceId) in entries)
        {
            var copy = Table.Clone(template);
            _o.SetId(copy, _o.NextId(copy));
            items.Insert(at++, copy);
            _o.AfterPaste?.Invoke(sourceId, copy);
            pasted.Add(copy);
        }
        if (_search.Text.Length > 0 || (_o.Filter != null && !pasted.All(_o.Filter)))
        {
            _search.Text = "";
            _searchTimer.Stop();
        }
        RecordsChanged?.Invoke();
        Rebuild(pasted, pasted[^1], true);
        _host.Session.Commit(Loc.F(historyKey, pasted.Count));
        _host.SetStatus(Loc.F("JsonEditor.Status.Pasted", pasted.Count, string.Join(", ", pasted.Select(_o.GetId))));
    }

    // ============================================================= 排序 =============================================================

    /// <summary>
    /// 选中的记录在可见列表里上移 / 下移一位（与相邻的可见记录交换文件位置）。
    /// 搜索时不允许调整顺序：搜索结果里相邻的两条在文件里可能隔得很远。
    /// </summary>
    public void MoveSelected(int delta)
    {
        if (_search.Text.Trim().Length > 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.MoveBlockedBySearch"));
            return;
        }
        var selected = new HashSet<T>(SelectedItems, ReferenceEqualityComparer.Instance);
        if (selected.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }

        _host.Session.Flush();
        var items = Table.Items;
        var visible = VisibleItems.ToList();
        var order = Enumerable.Range(0, visible.Count);
        if (delta > 0) order = order.Reverse();
        bool moved = false;
        foreach (int vi in order.ToList())
        {
            var item = visible[vi];
            if (!selected.Contains(item)) continue;
            int ni = vi + delta;
            if (ni < 0 || ni >= visible.Count || selected.Contains(visible[ni])) continue;
            var other = visible[ni];
            int a = items.IndexOf(item), b = items.IndexOf(other);
            items[a] = other;
            items[b] = item;
            visible[vi] = other;
            visible[ni] = item;
            moved = true;
        }
        if (!moved) return;
        RecordsChanged?.Invoke();
        Rebuild(selected.ToList(), _current, false);
        _host.Session.Commit(Loc.T("JsonEditor.History.Move"));
        _host.SetStatus(Loc.F("JsonEditor.Status.Moved", selected.Count));
    }

    // ============================================================= 批量 =============================================================

    public void OpenBatch()
    {
        var visible = VisibleItems;
        if (visible.Count == 0)
        {
            _host.SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }
        _host.Session.Flush();
        var result = BatchEditDialog.Show(_host.DialogOwner, SelectedItems, visible);
        if (result == null) return;
        RecordsChanged?.Invoke();
        Rebuild(null, _current, true);
        _host.Session.Commit(Loc.F("JsonEditor.History.Batch", result.Field, result.Changed));
        _host.SetStatus(Loc.F("JsonEditor.Status.Batch", result.Field, result.Changed, result.Failed));
    }

    public void Stop() => _searchTimer.Stop();
}
