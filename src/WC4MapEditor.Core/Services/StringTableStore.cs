using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 一种记录在字符串表里的文本键（按 Id 格式化，例如 <c>army_buff_desc_{0}</c>），
/// 统一处理读写、删除记录、改 Id 与复制记录时的文本跟随。写入都记在 <paramref name="owner"/> 名下。
/// </summary>
public sealed class RecordTextKeys(StringTableParser strings, object owner, params string[] formats)
{
    public StringTableParser Strings { get; } = strings;

    public string Key(int slot, int id) => string.Format(CultureInfo.InvariantCulture, formats[slot], id);

    public string Get(int slot, int id) => Strings.GetValue(Key(slot, id));

    public bool Has(int slot, int id) => Strings.ContainsKey(Key(slot, id));

    public void Set(int slot, int id, string value) => Strings.SetValue(Key(slot, id), value, owner);

    /// <summary>删除记录时一并删除它的文本。</summary>
    public void Remove(int id)
    {
        for (int i = 0; i < formats.Length; i++) Strings.Remove(Key(i, id), owner);
    }

    /// <summary>记录 Id 改变时把文本移到新键，避免旧键成为孤儿、新记录没有名字。</summary>
    public void Move(int oldId, int newId)
    {
        if (oldId == newId) return;
        for (int i = 0; i < formats.Length; i++) Strings.Move(Key(i, oldId), Key(i, newId), owner);
    }

    /// <summary>复制记录时复制文本（只复制原记录存在的键）。</summary>
    public void Copy(int fromId, int toId)
    {
        for (int i = 0; i < formats.Length; i++)
        {
            if (Has(i, fromId)) Set(i, toId, Get(i, fromId));
        }
    }
}

/// <summary>
/// 字符串表的唯一来源：同一个 stringtable 文件全程只有一个 <see cref="StringTableParser"/> 实例，
/// 只加载一次，所有设置解析器、ConfigManager 与地图编辑器共用。
/// <para>
/// 调用 <see cref="EnableAutoReload"/> 之后监视文件：外部程序改了 stringtable 时，
/// 在界面线程上重新加载（未保存的修改保留），并触发 <see cref="Changed"/>。
/// </para>
/// </summary>
public static class StringTableStore
{
    private const string DefaultFileName = "stringtable_tw.ini";
    private static readonly object _gate = new();
    private static readonly Dictionary<string, StringTableParser> _tables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<FileSystemWatcher> _watchers = new();
    private static SynchronizationContext? _uiContext;
    private static Timer? _debounce;

    /// <summary>任意字符串表重新加载 / 保存 / 放弃 / 恢复修改之后触发（在执行该操作的线程上）。</summary>
    public static event Action? Changed;

    /// <summary>默认字符串表（assets/stringtable_tw.ini）。</summary>
    public static StringTableParser Default => Get(Path.Combine(GameJson.AssetsRoot, DefaultFileName));

    /// <summary>取某个文件的共享实例（首次访问时加载）。</summary>
    public static StringTableParser Get(string path)
    {
        string full = Path.GetFullPath(path);
        lock (_gate)
        {
            if (_tables.TryGetValue(full, out var existing)) return existing;
            var table = new StringTableParser(full);
            table.Changed += () => Changed?.Invoke();
            _tables[full] = table;
            if (_uiContext != null) Watch(full);
            return table;
        }
    }

    /// <summary>
    /// 开启文件监视。<paramref name="uiContext"/> 是界面线程的同步上下文，
    /// 重新加载与 <see cref="Changed"/> 都在它上面执行，订阅者不用自己切线程。重复调用无副作用。
    /// </summary>
    public static void EnableAutoReload(SynchronizationContext uiContext)
    {
        lock (_gate)
        {
            if (_uiContext != null) return;
            _uiContext = uiContext;
            foreach (var path in _tables.Keys) Watch(path);
        }
    }

    /// <summary>检查所有已加载的字符串表，磁盘文件变了就重新加载；返回是否有表被重新加载。</summary>
    public static bool RefreshAllIfChanged()
    {
        List<StringTableParser> tables;
        lock (_gate) tables = _tables.Values.ToList();
        bool any = false;
        foreach (var table in tables)
        {
            try { any |= table.RefreshIfChanged(); }
            catch (IOException ex) { Debug.WriteLine($"[StringTableStore] 重新加载 {table.FilePath} 失败: {ex.Message}"); }
        }
        return any;
    }

    private static void Watch(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir == null || !Directory.Exists(dir)) return;
        try
        {
            var watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += (_, _) => ScheduleRefresh();
            watcher.Created += (_, _) => ScheduleRefresh();
            watcher.Renamed += (_, _) => ScheduleRefresh();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[StringTableStore] 无法监视 {path}: {ex.Message}");
        }
    }

    /// <summary>文件写入往往触发多次事件：合并成一次，并在界面线程上执行。</summary>
    private static void ScheduleRefresh()
    {
        lock (_gate)
        {
            _debounce ??= new Timer(_ => _uiContext?.Post(_ => RefreshAllIfChanged(), null));
            _debounce.Change(400, Timeout.Infinite);
        }
    }
}

/// <summary>游戏字符串表里的富文本标记。</summary>
public static partial class GameText
{
    /// <summary>去掉 [#RRGGBB#文字] 颜色标记，只留文字（界面里的名称 / 预览不需要颜色）。</summary>
    public static string StripColorTags(string text) => ColorTag().Replace(text, "$1");

    [GeneratedRegex(@"\[#[0-9A-Fa-f]{6,8}#([^\]]*)\]")]
    private static partial Regex ColorTag();
}
