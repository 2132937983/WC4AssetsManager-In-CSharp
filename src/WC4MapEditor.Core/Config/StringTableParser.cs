using System.Diagnostics;
using System.Text;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 字符串表（stringtable_*.ini，<c>key=value</c> 一行一条）。
/// <para>
/// 同一个文件全程只有一个实例（由 <see cref="StringTableStore"/> 发放），所有编辑器看到同一份文本。
/// 修改先记在“待保存”层上（读取时优先），保存时重新读取磁盘上的当前文件，只改动 / 删除 / 追加待保存的键，
/// 其它行（注释、空行、别处新增的键）原样保留，不会因为某个编辑器加载得早而把别人的修改冲掉。
/// </para>
/// <para>
/// 每条待保存的修改带一个“归属者”（写入它的设置解析器；地图编辑器等旧调用为 null）。
/// 编辑场景只保存 / 放弃 / 撤销自己名下的修改，<see cref="Save()"/> 只保存无归属的修改。
/// </para>
/// <para>游戏用字面量 <c>\n</c> 表示换行，写入的真实换行会被转换成 <c>\n</c>，避免把一行拆成两行。</para>
/// </summary>
public sealed class StringTableParser
{
    private readonly object _gate = new();
    private Dictionary<string, string> _disk = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileStamp _stamp;

    /// <summary>重新加载、保存、放弃或恢复修改之后触发（不在每次 SetValue 时触发）。</summary>
    public event Action? Changed;

    public StringTableParser(string filePath)
    {
        FilePath = filePath;
        ReadDisk();
    }

    /// <summary>文件路径</summary>
    public string FilePath { get; }

    /// <summary>是否有任何未保存的修改。</summary>
    public bool IsDirty
    {
        get { lock (_gate) return _pending.Count > 0; }
    }

    /// <summary>条目数量（含未保存的新增，不含未保存的删除）。</summary>
    public int Count
    {
        get { lock (_gate) return Merged().Count; }
    }

    /// <summary>所有条目的快照（含未保存的修改）。</summary>
    public IReadOnlyDictionary<string, string> Entries
    {
        get { lock (_gate) return Merged(); }
    }

    /// <summary>某个归属者名下是否有未保存的修改。</summary>
    public bool HasPending(object? owner)
    {
        lock (_gate) return _pending.Values.Any(p => ReferenceEquals(p.Owner, owner));
    }

    // ============================================================= 读写 =============================================================

    /// <summary>获取值</summary>
    public string GetValue(string key, string defaultValue = "")
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var p)) return p.Value ?? defaultValue;
            return _disk.TryGetValue(key, out var v) ? v : defaultValue;
        }
    }

    /// <summary>检查是否包含键</summary>
    public bool ContainsKey(string key)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var p)) return p.Value != null;
            return _disk.ContainsKey(key);
        }
    }

    /// <summary>设置值（记入待保存层）。</summary>
    public void SetValue(string key, string value, object? owner = null)
    {
        value = EscapeLineBreaks(value);
        lock (_gate)
        {
            if (_disk.TryGetValue(key, out var onDisk) && onDisk == value)
                _pending.Remove(key);
            else
                _pending[key] = new PendingChange(value, owner);
        }
    }

    /// <summary>删除条目（记入待保存层）。</summary>
    public bool Remove(string key, object? owner = null)
    {
        lock (_gate)
        {
            bool existed = _pending.TryGetValue(key, out var p) ? p.Value != null : _disk.ContainsKey(key);
            if (_disk.ContainsKey(key)) _pending[key] = new PendingChange(null, owner);
            else _pending.Remove(key);
            return existed;
        }
    }

    /// <summary>把一个键的文本移到另一个键（记录 Id 改变时用），原键删除。</summary>
    public void Move(string fromKey, string toKey, object? owner = null)
    {
        if (string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            if (!ContainsKey(fromKey)) return;
            string value = GetValue(fromKey);
            Remove(fromKey, owner);
            SetValue(toKey, value, owner);
        }
    }

    /// <summary>真实换行转成游戏使用的字面量 \n。</summary>
    public static string EscapeLineBreaks(string value)
        => value.Replace("\r\n", "\\n").Replace('\r', '\n').Replace("\n", "\\n");

    /// <summary>查找以指定前缀开头的所有条目</summary>
    public Dictionary<string, string> FindByPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in Entries)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    /// <summary>查找城市名称条目 (battle_cityname_xxx)</summary>
    public Dictionary<int, string> FindCityNames()
    {
        var result = new Dictionary<int, string>();
        foreach (var kvp in FindByPrefix("battle_cityname_"))
        {
            if (int.TryParse(kvp.Key["battle_cityname_".Length..], out int cityId))
                result[cityId] = kvp.Value;
        }
        return result;
    }

    /// <summary>根据城市名称查找ID</summary>
    public int? FindCityIdByName(string cityName)
    {
        foreach (var kvp in FindCityNames())
        {
            if (string.Equals(kvp.Value, cityName, StringComparison.Ordinal))
                return kvp.Key;
        }
        return null;
    }

    /// <summary>获取最大的城市名称ID</summary>
    public int GetMaxCityId()
    {
        var names = FindCityNames();
        return names.Count == 0 ? 0 : Math.Max(0, names.Keys.Max());
    }

    /// <summary>添加或更新城市名称</summary>
    /// <returns>城市ID</returns>
    public int AddOrUpdateCityName(string cityName)
    {
        lock (_gate)
        {
            var existingId = FindCityIdByName(cityName);
            if (existingId.HasValue)
                return existingId.Value;

            int newId = GetMaxCityId() + 1;
            string key = $"battle_cityname_{newId:D3}";
            SetValue(key, cityName);
            Debug.WriteLine($"[StringTableParser] 添加新城市名称: {key}={cityName}");
            return newId;
        }
    }

    // ============================================================= 保存 / 加载 =============================================================

    /// <summary>保存无归属的修改（地图编辑器、脚本等旧调用）。</summary>
    public void Save()
    {
        if (!Save(null, out var error) && error != null)
            throw new IOException(error);
    }

    /// <summary>保存某个归属者名下的修改，其它待保存修改保持不动。</summary>
    public bool Save(object? owner, out string? error)
    {
        lock (_gate)
        {
            var selected = _pending.Where(kv => ReferenceEquals(kv.Value.Owner, owner))
                                   .ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
            {
                error = null;
                return true;
            }
            try
            {
                WriteChanges(selected);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = Loc.F("JsonEditor.Error.WriteFailed", FilePath, ex.Message);
                return false;
            }
            foreach (var (key, value) in selected)
            {
                _pending.Remove(key);
                if (value == null) _disk.Remove(key);
                else _disk[key] = value;
            }
        }
        Debug.WriteLine($"[StringTableParser] 已保存到 {FilePath}");
        Changed?.Invoke();
        error = null;
        return true;
    }

    /// <summary>
    /// 把选中的修改写进磁盘上的当前文件：替换 / 删除已有的行（保留缩进、原键名写法与行尾），
    /// 新键追加在末尾；BOM 与换行风格沿用原文件。
    /// </summary>
    private void WriteChanges(Dictionary<string, string?> changes)
    {
        byte[] bytes = File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : Array.Empty<byte>();
        bool hasBom = GameJson.HasBom(bytes);
        string text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        string newLine = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";

        var sb = new StringBuilder(text.Length + 256);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int pos = 0;
        while (pos < text.Length)
        {
            int end = text.IndexOf('\n', pos);
            int next = end < 0 ? text.Length : end + 1;
            int contentEnd = end < 0 ? text.Length : (end > pos && text[end - 1] == '\r' ? end - 1 : end);
            string line = text[pos..contentEnd];
            string terminator = text[contentEnd..next];

            string? key = ParseKey(line);
            if (key != null && changes.TryGetValue(key, out var value))
            {
                written.Add(key);
                if (value != null)
                {
                    string trimmed = line.TrimStart();
                    string indent = line[..(line.Length - trimmed.Length)];
                    string originalKey = trimmed[..trimmed.IndexOf('=')].Trim();
                    sb.Append(indent).Append(originalKey).Append('=').Append(value).Append(terminator);
                }
            }
            else
            {
                sb.Append(line).Append(terminator);
            }
            pos = next;
        }

        var appended = changes.Where(kv => kv.Value != null && !written.Contains(kv.Key)).ToList();
        if (appended.Count > 0)
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append(newLine);
            foreach (var (key, value) in appended)
                sb.Append(key).Append('=').Append(value).Append(newLine);
        }

        // 新文件按游戏 stringtable 的惯例带 BOM
        GameJson.WriteFileSafely(FilePath, GameJson.Encode(sb.ToString(), hasBom || bytes.Length == 0));
        _stamp = FileStamp.Of(FilePath);
    }

    /// <summary>重新加载磁盘文件；未保存的修改保留并继续覆盖在上面。</summary>
    public void Reload()
    {
        lock (_gate) ReadDisk();
        Changed?.Invoke();
    }

    /// <summary>磁盘文件自上次读写后被外部改过时重新加载，返回是否重新加载了。</summary>
    public bool RefreshIfChanged()
    {
        lock (_gate)
        {
            if (FileStamp.Of(FilePath) == _stamp) return false;
        }
        Reload();
        return true;
    }

    private void ReadDisk()
    {
        var stamp = FileStamp.Of(FilePath);
        var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(FilePath))
        {
            try
            {
                foreach (var rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string? key = ParseKey(rawLine);
                    if (key == null) continue;
                    string line = rawLine.Trim();
                    fresh[key] = line[(line.IndexOf('=') + 1)..].Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 文件正被别的程序写入时读不到：保留旧内容与旧时间戳，下次检查时重试
                Debug.WriteLine($"[StringTableParser] 读取 {FilePath} 失败: {ex.Message}");
                return;
            }
        }
        _disk = fresh;
        _stamp = stamp;
        // 与磁盘已经一致的待保存修改没有意义了
        foreach (var key in _pending.Where(kv => SameAsDisk(kv.Key, kv.Value.Value)).Select(kv => kv.Key).ToList())
            _pending.Remove(key);
        Debug.WriteLine($"[StringTableParser] 已解析 {_disk.Count} 个条目");
    }

    private bool SameAsDisk(string key, string? value)
        => value == null ? !_disk.ContainsKey(key) : _disk.TryGetValue(key, out var v) && v == value;

    /// <summary>注释（; #）、节头、空行与没有 '=' 的行返回 null。</summary>
    private static string? ParseKey(string rawLine)
    {
        string line = rawLine.Trim();
        if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') return null;
        int eq = line.IndexOf('=');
        if (eq < 0) return null;
        string key = line[..eq].Trim();
        return key.Length == 0 ? null : key;
    }

    private Dictionary<string, string> Merged()
    {
        var merged = new Dictionary<string, string>(_disk, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, change) in _pending)
        {
            if (change.Value == null) merged.Remove(key);
            else merged[key] = change.Value;
        }
        return merged;
    }

    // ============================================================= 撤销支持 =============================================================

    /// <summary>某个归属者名下待保存修改的快照（撤销历史用）。</summary>
    public object CapturePending(object owner)
    {
        lock (_gate)
        {
            return _pending.Where(kv => ReferenceEquals(kv.Value.Owner, owner))
                           .ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>两份待保存快照内容是否相同。</summary>
    public static bool PendingEquals(object a, object b)
    {
        var x = (Dictionary<string, string?>)a;
        var y = (Dictionary<string, string?>)b;
        return x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>把某个归属者名下的待保存修改恢复成快照里的样子。</summary>
    public void RestorePending(object owner, object snapshot)
    {
        var target = (Dictionary<string, string?>)snapshot;
        lock (_gate)
        {
            foreach (var key in _pending.Where(kv => ReferenceEquals(kv.Value.Owner, owner)).Select(kv => kv.Key).ToList())
                _pending.Remove(key);
            foreach (var (key, value) in target)
            {
                if (!SameAsDisk(key, value)) _pending[key] = new PendingChange(value, owner);
            }
        }
        Changed?.Invoke();
    }

    /// <summary>放弃某个归属者名下的全部待保存修改。</summary>
    public void DiscardPending(object owner)
    {
        lock (_gate)
        {
            foreach (var key in _pending.Where(kv => ReferenceEquals(kv.Value.Owner, owner)).Select(kv => kv.Key).ToList())
                _pending.Remove(key);
        }
        Changed?.Invoke();
    }

    /// <summary>一条待保存的修改：Value 为 null 表示删除。</summary>
    private readonly record struct PendingChange(string? Value, object? Owner);
}
