using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 一行一个元素的小配置 XML（def_armypos.xml / def_portraitpos.xml）。
/// <para>
/// 保存时保持原文件：没改过的行逐字保留，改过的行按原行缩进重写，删除的行去掉，
/// 新增的行插在根元素结束标签之前；行顺序、BOM、换行符与结尾换行都沿用原文件，不再整表排序重写。
/// </para>
/// </summary>
public sealed class XmlRowTable<TEntry> : IEditableTable where TEntry : class
{
    private readonly string _rootName;
    private readonly string _rowName;
    private readonly Func<XElement, TEntry?> _read;
    private readonly Func<TEntry, IReadOnlyList<KeyValuePair<string, string>>> _write;

    private ConditionalWeakTable<TEntry, RowOrigin> _origins = new();
    private List<(string Text, string Terminator)> _lines = new();
    private HashSet<int> _entryLines = new();
    private int _closingLine = -1;
    private int _skippedRows;
    private object _loadToken = new();
    private string _rowIndent = "\t";
    private string _newLine = "\r\n";
    private bool _bom;
    private bool _spaceBeforeSlash;
    private RowState _saved = new(Array.Empty<RowEntry>());
    private FileStamp _stamp;

    /// <param name="filePath">文件路径</param>
    /// <param name="rootName">根元素名</param>
    /// <param name="rowName">行元素名</param>
    /// <param name="read">从元素读出条目（无效时返回 null，该行原样保留）</param>
    /// <param name="write">条目写出的属性（按输出顺序）</param>
    public XmlRowTable(string filePath, string rootName, string rowName,
        Func<XElement, TEntry?> read, Func<TEntry, IReadOnlyList<KeyValuePair<string, string>>> write)
    {
        FilePath = filePath;
        _rootName = rootName;
        _rowName = rowName;
        _read = read;
        _write = write;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>条目（文件顺序 + 新增的条目）。</summary>
    public List<TEntry> Entries { get; } = new();

    public string? LoadError { get; private set; }
    public bool HasExternalChanges => FileStamp.Of(FilePath) != _stamp;
    public bool IsDirty => !IsSavedState(CaptureState());

    // ============================================================= 加载 =============================================================

    public void Reload() => Load();

    public void Load()
    {
        LoadError = null;
        _stamp = FileStamp.Of(FilePath);
        ResetText();

        if (!File.Exists(FilePath))
        {
            LoadError = Loc.F("JsonEditor.Error.FileMissing", FilePath);
        }
        else
        {
            try
            {
                var bytes = File.ReadAllBytes(FilePath);
                _bom = GameJson.HasBom(bytes);
                string text = Encoding.UTF8.GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
                var document = XDocument.Parse(text);
                if (document.Root?.Name.LocalName != _rootName)
                    throw new InvalidDataException(Loc.F("JsonEditor.Error.XmlRoot", _rootName));
                ParseLines(text);
                // 行元素跨行或一行多个时无法逐行保持原样：报错而不是冒险改写
                if (document.Root.Elements(_rowName).Count() != Entries.Count + _skippedRows)
                    throw new InvalidDataException(Loc.F("JsonEditor.Error.XmlLayout", _rowName));
            }
            catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException or FormatException or OverflowException)
            {
                ResetText();
                LoadError = ex.Message;
            }
        }

        if (LoadError != null) Debug.WriteLine($"[XmlRowTable] {FileName} 加载失败: {LoadError}");
        _saved = (RowState)CaptureState();
    }

    private void ResetText()
    {
        Entries.Clear();
        _origins = new ConditionalWeakTable<TEntry, RowOrigin>();
        _lines = new List<(string, string)>();
        _entryLines = new HashSet<int>();
        _closingLine = -1;
        _skippedRows = 0;
        _loadToken = new object();
    }

    private void ParseLines(string text)
    {
        int crlf = 0, lf = 0;
        bool firstRow = true;
        int pos = 0;
        while (pos < text.Length)
        {
            int end = text.IndexOf('\n', pos);
            int next = end < 0 ? text.Length : end + 1;
            int contentEnd = end < 0 ? text.Length : (end > pos && text[end - 1] == '\r' ? end - 1 : end);
            if (end >= 0)
            {
                if (contentEnd < end) crlf++; else lf++;
            }
            string line = text[pos..contentEnd];
            int index = _lines.Count;
            _lines.Add((line, text[contentEnd..next]));

            string trimmed = line.Trim();
            if (IsRowLine(trimmed))
            {
                if (firstRow)
                {
                    _rowIndent = line[..(line.Length - line.TrimStart().Length)];
                    _spaceBeforeSlash = trimmed.EndsWith(" />", StringComparison.Ordinal);
                    firstRow = false;
                }
                var entry = _read(XElement.Parse(trimmed));
                if (entry == null)
                {
                    _skippedRows++;
                }
                else
                {
                    _origins.AddOrUpdate(entry, new RowOrigin(index, Canon(entry), _loadToken));
                    _entryLines.Add(index);
                    Entries.Add(entry);
                }
            }
            else if (trimmed.Contains("</" + _rootName + ">", StringComparison.Ordinal))
            {
                _closingLine = index;
            }
            pos = next;
        }
        _newLine = lf > crlf ? "\n" : "\r\n";
    }

    private bool IsRowLine(string trimmed)
        => trimmed.StartsWith("<" + _rowName, StringComparison.Ordinal)
           && trimmed.Length > _rowName.Length + 1
           && (trimmed[_rowName.Length + 1] == ' ' || trimmed[_rowName.Length + 1] == '/')
           && trimmed.EndsWith("/>", StringComparison.Ordinal);

    // ============================================================= 保存 =============================================================

    public bool Save(out string? error)
    {
        if (LoadError != null)
        {
            error = Loc.F("JsonEditor.Error.SaveBlocked", FileName, LoadError);
            return false;
        }
        try
        {
            string text = BuildText();
            GameJson.WriteFileSafely(FilePath, GameJson.Encode(text, _bom));
            Rebase(text);
            _stamp = FileStamp.Of(FilePath);
            _saved = (RowState)CaptureState();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = Loc.F("JsonEditor.Error.WriteFailed", FilePath, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 以刚写出的内容为新的原文。写出顺序与 <see cref="Entries"/> 一致时沿用原条目对象（界面持有的引用继续有效），
    /// 否则换成新解析的条目。
    /// </summary>
    private void Rebase(string text)
    {
        var current = Entries.ToList();
        ResetText();
        ParseLines(text);
        if (current.Count != Entries.Count) return;
        for (int i = 0; i < current.Count; i++)
        {
            if (!string.Equals(Canon(current[i]), Canon(Entries[i]), StringComparison.Ordinal)) return;
        }
        for (int i = 0; i < current.Count; i++)
        {
            if (_origins.TryGetValue(Entries[i], out var origin)) _origins.AddOrUpdate(current[i], origin);
        }
        Entries.Clear();
        Entries.AddRange(current);
    }

    /// <summary>生成要写入文件的完整文本（不含 BOM）。</summary>
    public string BuildText()
    {
        var byLine = new Dictionary<int, TEntry>();
        var added = new List<TEntry>();
        foreach (var entry in Entries)
        {
            if (_origins.TryGetValue(entry, out var origin) && ReferenceEquals(origin.LoadToken, _loadToken) && byLine.TryAdd(origin.Line, entry))
                continue;
            added.Add(entry);
        }

        var sb = new StringBuilder();
        for (int i = 0; i < _lines.Count; i++)
        {
            var (line, terminator) = _lines[i];
            if (i == _closingLine) AppendRows(sb, added);
            if (!_entryLines.Contains(i))
            {
                sb.Append(line).Append(terminator);
                continue;
            }
            if (!byLine.TryGetValue(i, out var current)) continue;   // 已删除
            _origins.TryGetValue(current, out var o);
            if (o != null && string.Equals(Canon(current), o.LoadedCanon, StringComparison.Ordinal))
                sb.Append(line).Append(terminator);                    // 没改过：逐字保留
            else
                sb.Append(line[..(line.Length - line.TrimStart().Length)]).Append(FormatRow(current)).Append(terminator);
        }
        if (_closingLine < 0) AppendRows(sb, added);
        return sb.ToString();
    }

    private void AppendRows(StringBuilder sb, List<TEntry> rows)
    {
        foreach (var entry in rows)
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append(_newLine);
            sb.Append(_rowIndent).Append(FormatRow(entry)).Append(_newLine);
        }
        rows.Clear();
    }

    private string FormatRow(TEntry entry)
    {
        var sb = new StringBuilder("<").Append(_rowName);
        foreach (var (name, value) in _write(entry))
            sb.Append(' ').Append(name).Append("=\"").Append(SecurityElement.Escape(value)).Append('"');
        return sb.Append(_spaceBeforeSlash ? " />" : "/>").ToString();
    }

    private string Canon(TEntry entry) => string.Join("\u0001", _write(entry).Select(a => a.Key + "=" + a.Value));

    // ============================================================= 快照 =============================================================

    public object CaptureState()
    {
        var rows = new RowEntry[Entries.Count];
        for (int i = 0; i < Entries.Count; i++)
        {
            _origins.TryGetValue(Entries[i], out var origin);
            rows[i] = new RowEntry(_write(Entries[i]).ToArray(), Canon(Entries[i]), origin);
        }
        return new RowState(rows);
    }

    public void RestoreState(object state)
    {
        var restored = new List<TEntry>();
        foreach (var row in ((RowState)state).Rows)
        {
            var entry = _read(new XElement(_rowName, row.Attributes.Select(a => new XAttribute(a.Key, a.Value))));
            if (entry == null) continue;
            if (row.Origin != null) _origins.AddOrUpdate(entry, row.Origin);
            restored.Add(entry);
        }
        Entries.Clear();
        Entries.AddRange(restored);
    }

    public bool StateEquals(object a, object b)
    {
        var x = ((RowState)a).Rows;
        var y = ((RowState)b).Rows;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++)
        {
            if (!string.Equals(x[i].Canon, y[i].Canon, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public bool IsSavedState(object state) => StateEquals(state, _saved);

    private sealed record RowOrigin(int Line, string LoadedCanon, object LoadToken);

    private readonly record struct RowEntry(KeyValuePair<string, string>[] Attributes, string Canon, RowOrigin? Origin);

    private sealed class RowState(RowEntry[] rows)
    {
        public RowEntry[] Rows { get; } = rows;
    }
}
