using System.Diagnostics;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>操作历史里的一步：说明文字 + 这一步之后的完整快照。</summary>
public sealed class HistoryEntry
{
    internal HistoryEntry(string label, SessionState state)
    {
        Label = label;
        State = state;
        Time = DateTime.Now;
    }

    public string Label { get; }
    public DateTime Time { get; }
    internal SessionState State { get; }
}

/// <summary>会话快照：每张表的状态 + 每个归属者在字符串表里的待保存修改。</summary>
internal sealed class SessionState(object[] tables, object[] texts)
{
    public object[] Tables { get; } = tables;
    public object[] Texts { get; } = texts;
}

/// <summary>
/// 一个 JSON 编辑场景的编辑会话：把场景用到的设置解析器（表 + 字符串表归属者）当作一个整体，
/// 统一做加载错误汇总、脏检查、保存、放弃（从磁盘重新加载）与带撤销 / 重做的操作历史。
/// <para>
/// 解析器是全局单例，场景之间共享内存数据；会话只记录“这个场景改了什么”，
/// 字符串表的修改按解析器（归属者）区分，保存 / 放弃都只动自己名下的键。
/// </para>
/// </summary>
public sealed class JsonEditSession : IDisposable
{
    /// <summary>历史最多保留的步数（不含起点）。</summary>
    public const int MaxHistory = 50;

    /// <summary>连续输入合并成一步的等待时间。</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(700);

    private readonly IEditableTable[] _tables;
    private readonly (StringTableParser Strings, object Owner)[] _texts;
    private readonly List<HistoryEntry> _history = new();
    private readonly DispatcherTimer _commitTimer;
    private string? _pendingLabel;
    private int _index;
    private int _internalTextChange;
    private bool _disposed;

    public JsonEditSession(params IGameSettingsSource[] sources)
    {
        _tables = sources.SelectMany(s => s.Tables).Distinct().ToArray();
        _texts = sources.Where(s => s.Strings != null).Select(s => (s.Strings!, (object)s)).ToArray();

        _commitTimer = new DispatcherTimer { Interval = CommitDelay };
        _commitTimer.Tick += (_, _) => Flush();

        // 字符串表被外部程序改动时自动重新加载（只需开启一次，重复调用无副作用）
        if (SynchronizationContext.Current is DispatcherSynchronizationContext ui)
            StringTableStore.EnableAutoReload(ui);
        StringTableStore.Changed += OnStringTableChanged;

        ReloadStaleTables();
        ResetHistory(Loc.T("JsonEditor.History.Opened"));
    }

    /// <summary>历史 / 未保存状态变化（提交、撤销、保存之后）。</summary>
    public event Action? Changed;

    /// <summary>记录对象被整体替换（撤销 / 重做 / 回退 / 重新加载）：场景要重建列表与表单。</summary>
    public event Action? StateReplaced;

    /// <summary>字符串表被外部修改并重新加载：场景要刷新显示的文本。</summary>
    public event Action? TextsReloaded;

    public IReadOnlyList<HistoryEntry> History => _history;

    /// <summary>当前所处的历史位置（0 = 打开 / 重新加载时的状态）。</summary>
    public int HistoryIndex => _index;

    public bool CanUndo => _index > 0 || _pendingLabel != null;
    public bool CanRedo => _pendingLabel == null && _index < _history.Count - 1;

    /// <summary>所有表的加载错误（按文件列出）；为 null 表示全部加载成功。</summary>
    public string? LoadError
    {
        get
        {
            var errors = _tables.Where(t => t.LoadError != null).Select(t => $"{t.FileName}:{Environment.NewLine}{t.LoadError}").ToList();
            return errors.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, errors);
        }
    }

    public bool CanSave => _tables.All(t => t.LoadError == null);

    /// <summary>本会话涉及的文件名（提示用）。</summary>
    public IEnumerable<string> FileNames => _tables.Select(t => t.FileName).Concat(_texts.Select(x => IOPath.GetFileName(x.Strings.FilePath))).Distinct();

    /// <summary>与磁盘上已保存的内容是否不同（会先合并等待中的输入，并按当前内容重新比较）。</summary>
    public bool IsDirty
    {
        get
        {
            Flush();
            return _tables.Any(t => t.IsDirty) || _texts.Any(x => x.Strings.HasPending(x.Owner));
        }
    }

    /// <summary>
    /// 状态栏用的快速判断：按最近一步历史的快照比较，不重新序列化所有记录。
    /// </summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (_pendingLabel != null) return true;
            var state = _history[_index].State;
            for (int i = 0; i < _tables.Length; i++)
            {
                if (!_tables[i].IsSavedState(state.Tables[i])) return true;
            }
            return _texts.Any(x => x.Strings.HasPending(x.Owner));
        }
    }

    /// <summary>已修改且磁盘文件在加载之后又被外部改过的文件（保存前需要确认覆盖）。</summary>
    public IReadOnlyList<string> ExternallyChangedFiles()
        => _tables.Where(t => t.IsDirty && t.HasExternalChanges).Select(t => t.FileName).ToList();

    // ============================================================= 历史 =============================================================

    /// <summary>立即记录一步（内容没变化时忽略）。</summary>
    public void Commit(string label)
    {
        _commitTimer.Stop();
        _pendingLabel = null;
        var state = Capture();
        if (SameState(state, _history[_index].State))
        {
            Changed?.Invoke();
            return;
        }
        if (_index < _history.Count - 1)
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(new HistoryEntry(label, state));
        if (_history.Count > MaxHistory + 1) _history.RemoveAt(0);
        _index = _history.Count - 1;
        Changed?.Invoke();
    }

    /// <summary>
    /// 稍后记录一步：同一字段的连续输入合并成一步。换了字段、或执行其它操作前会先把等待中的一步落定。
    /// </summary>
    public void CommitLater(string label)
    {
        if (_pendingLabel != null && _pendingLabel != label) Flush();
        bool first = _pendingLabel == null;
        _pendingLabel = label;
        _commitTimer.Stop();
        _commitTimer.Start();
        if (first) Changed?.Invoke();
    }

    /// <summary>把等待中的输入立即记成一步。</summary>
    public void Flush()
    {
        if (_pendingLabel != null) Commit(_pendingLabel);
    }

    public bool Undo()
    {
        Flush();
        if (_index == 0) return false;
        Restore(_index - 1);
        return true;
    }

    public bool Redo()
    {
        Flush();
        if (_index >= _history.Count - 1) return false;
        Restore(_index + 1);
        return true;
    }

    /// <summary>回到历史中的某一步（之后的步骤保留，可以再前进）。</summary>
    public void RevertTo(int index)
    {
        Flush();
        if (index < 0 || index >= _history.Count || index == _index) return;
        Restore(index);
    }

    private void Restore(int index)
    {
        _index = index;
        var state = _history[index].State;
        _internalTextChange++;
        try
        {
            for (int i = 0; i < _tables.Length; i++) _tables[i].RestoreState(state.Tables[i]);
            for (int i = 0; i < _texts.Length; i++) _texts[i].Strings.RestorePending(_texts[i].Owner, state.Texts[i]);
        }
        finally
        {
            _internalTextChange--;
        }
        StateReplaced?.Invoke();
        Changed?.Invoke();
    }

    private SessionState Capture()
    {
        var tables = new object[_tables.Length];
        for (int i = 0; i < _tables.Length; i++) tables[i] = _tables[i].CaptureState();
        var texts = new object[_texts.Length];
        for (int i = 0; i < _texts.Length; i++) texts[i] = _texts[i].Strings.CapturePending(_texts[i].Owner);
        return new SessionState(tables, texts);
    }

    private bool SameState(SessionState a, SessionState b)
    {
        for (int i = 0; i < _tables.Length; i++)
        {
            if (!_tables[i].StateEquals(a.Tables[i], b.Tables[i])) return false;
        }
        for (int i = 0; i < _texts.Length; i++)
        {
            if (!StringTableParser.PendingEquals(a.Texts[i], b.Texts[i])) return false;
        }
        return true;
    }

    private void ResetHistory(string label)
    {
        _commitTimer.Stop();
        _pendingLabel = null;
        _history.Clear();
        _history.Add(new HistoryEntry(label, Capture()));
        _index = 0;
        Changed?.Invoke();
    }

    // ============================================================= 保存 / 重新加载 =============================================================

    /// <summary>保存所有改过的表与本会话名下的字符串表修改。任何表加载失败时拒绝保存。</summary>
    public bool Save(out string? error)
    {
        Flush();
        if (LoadError is { } loadError)
        {
            error = Loc.F("JsonEditor.Error.SaveBlockedSession", loadError);
            return false;
        }

        var errors = new List<string>();
        _internalTextChange++;
        try
        {
            foreach (var table in _tables)
            {
                if (!table.IsDirty) continue;
                if (!table.Save(out var e) && e != null) errors.Add(e);
            }
            foreach (var (strings, owner) in _texts)
            {
                if (!strings.Save(owner, out var e) && e != null) errors.Add(e);
            }
        }
        finally
        {
            _internalTextChange--;
        }
        Changed?.Invoke();
        error = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
        return errors.Count == 0;
    }

    /// <summary>放弃所有未保存的修改：表从磁盘重新加载，字符串表丢掉本会话名下的待保存修改。历史清空。</summary>
    public void Reload()
    {
        _internalTextChange++;
        try
        {
            foreach (var table in _tables) table.Reload();
            foreach (var (strings, owner) in _texts)
            {
                strings.DiscardPending(owner);
                strings.RefreshIfChanged();
            }
        }
        finally
        {
            _internalTextChange--;
        }
        ResetHistory(Loc.T("JsonEditor.History.Reloaded"));
        StateReplaced?.Invoke();
    }

    /// <summary>
    /// 打开场景时：没有未保存修改、但磁盘文件已被外部改过的表重新加载，避免显示过期数据并在保存时覆盖外部修改。
    /// </summary>
    private void ReloadStaleTables()
    {
        foreach (var table in _tables)
        {
            if (table.LoadError == null && table.HasExternalChanges && !table.IsDirty)
            {
                Debug.WriteLine($"[JsonEditSession] {table.FileName} 已被外部修改，重新加载");
                table.Reload();
            }
        }
    }

    private void OnStringTableChanged()
    {
        if (_disposed || _internalTextChange > 0) return;
        TextsReloaded?.Invoke();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commitTimer.Stop();
        StringTableStore.Changed -= OnStringTableChanged;
    }
}
