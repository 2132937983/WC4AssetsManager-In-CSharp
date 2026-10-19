using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 编辑会话可以快照 / 撤销 / 保存的数据源。
/// GUI 的 JSON 编辑场景只通过这个接口做撤销历史、脏检查与保存，不关心记录类型。
/// </summary>
public interface IEditableTable
{
    /// <summary>文件名（提示用）。</summary>
    string FileName { get; }

    /// <summary>完整路径。</summary>
    string FilePath { get; }

    /// <summary>加载错误；非 null 时禁止保存，避免把解析失败的残表写回磁盘。</summary>
    string? LoadError { get; }

    /// <summary>磁盘文件在本次加载 / 保存之后被外部改动过。</summary>
    bool HasExternalChanges { get; }

    /// <summary>当前内容与最近一次加载 / 保存时不同。</summary>
    bool IsDirty { get; }

    /// <summary>取当前内容的不可变快照（撤销历史用）。</summary>
    object CaptureState();

    /// <summary>恢复快照。记录对象会换成新实例，调用方要重新取当前记录。</summary>
    void RestoreState(object state);

    /// <summary>两份快照内容是否相同。</summary>
    bool StateEquals(object a, object b);

    /// <summary>快照是否与已保存的内容一致。</summary>
    bool IsSavedState(object state);

    /// <summary>从磁盘重新加载。</summary>
    void Reload();

    /// <summary>写回磁盘；失败时返回 false 并给出原因。</summary>
    bool Save(out string? error);
}

/// <summary>
/// 一个设置解析器（ArmyBuffSettingParser 等）交给编辑会话的内容：
/// 它管理的数据表，以及它写入的共享字符串表（写入时以解析器自身为归属者）。
/// </summary>
public interface IGameSettingsSource
{
    IReadOnlyList<IEditableTable> Tables { get; }

    /// <summary>该数据源写入的字符串表；不写 INI 的数据源为 null。</summary>
    StringTableParser? Strings { get; }
}

/// <summary>游戏设置 JSON 的公共约定：序列化选项、assets 路径、安全写文件。</summary>
public static class GameJson
{
    /// <summary>
    /// 读写共用的序列化选项：属性名大小写不敏感，容忍注释 / 末尾逗号 / 字符串数字，
    /// 输出紧凑且不转义中文（与游戏文件、Python 工具的写法一致）。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new SafeInt32Converter(), new SafeIntListConverter() }
    };

    /// <summary>预览用的缩进序列化（不转义中文）。</summary>
    public static JsonSerializerOptions PreviewOptions { get; } = new(Options) { WriteIndented = true };

    /// <summary>
    /// 游戏 assets 根目录：已扫描的 AssetManager 根目录优先，否则按默认规则向上查找 Resource/WC4DATA/assets。
    /// 与旧解析器一致，首次调用会顺带扫描 assets（其它代码依赖扫描结果按名字查资源）。
    /// </summary>
    public static string AssetsRoot
    {
        get
        {
            var manager = AssetManager.Default;
            if (!manager.IsLoaded)
            {
                try { manager.ScanDefault(); }
                catch (Exception ex) { Debug.WriteLine($"[GameJson] 扫描 assets 失败: {ex.Message}"); }
            }
            var root = manager.AssetsRoot;
            return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? root : AssetManager.GetDefaultAssetsPath();
        }
    }

    /// <summary>assets 下的文件路径，例如 <c>AssetPath("json", "ArmySettings.json")</c>。</summary>
    public static string AssetPath(params string[] parts) => Path.Combine([AssetsRoot, .. parts]);

    /// <summary>先写同目录临时文件再替换目标，避免写到一半中断时留下残缺的游戏文件。</summary>
    internal static void WriteFileSafely(string path, byte[] bytes)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "." + Path.GetFileName(full) + ".tmp");
        File.WriteAllBytes(tmp, bytes);
        try
        {
            if (File.Exists(full)) File.Replace(tmp, full, null, ignoreMetadataErrors: true);
            else File.Move(tmp, full);
        }
        catch (IOException)
        {
            // 个别文件系统不支持 Replace：退回覆盖复制
            File.Copy(tmp, full, overwrite: true);
            File.Delete(tmp);
        }
    }

    internal static byte[] Encode(string text, bool bom)
    {
        var body = Encoding.UTF8.GetBytes(text);
        if (!bom) return body;
        var result = new byte[body.Length + 3];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    internal static bool HasBom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}

/// <summary>文件时间戳 + 长度，用来判断磁盘文件是否被外部改动。</summary>
internal readonly record struct FileStamp(DateTime WriteTimeUtc, long Length)
{
    public static FileStamp Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : default;
        }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }
}

/// <summary>
/// 一张游戏设置 JSON 表（顶层是记录数组）。
/// <para>
/// 保存时尽量保持文件原样：没改过的记录按原文逐字写回；改过的记录按原属性顺序输出，
/// 模型不认识的字段（例如旧模型漏掉的 StageId）原样保留，没改过的值保留原始写法（如 1.0）；
/// 新记录按文件原有的缩进 / 换行风格（多行缩进或一行一条）排版，BOM 与结尾换行也照旧。
/// 因此“加载后不改直接保存”得到与原文件逐字节相同的结果。
/// </para>
/// <para>加载失败（语法错误、根不是数组、某条记录无法解析、文件不存在）时 <see cref="LoadError"/> 非空，保存会被拒绝。</para>
/// </summary>
public sealed class GameJsonTable<T> : IEditableTable where T : class
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private ConditionalWeakTable<T, RecordOrigin> _origins = new();
    private readonly ConditionalWeakTable<T, StrongBox<string>> _canonCache = new();
    private JsonFileLayout _layout = JsonFileLayout.Default;
    private TableState _saved = new(Array.Empty<Entry>());
    private FileStamp _stamp;

    public GameJsonTable(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>记录列表（实例固定不变，加载 / 撤销时原地替换内容）。</summary>
    public List<T> Items { get; } = new();

    public string? LoadError { get; private set; }

    /// <summary>记录对象被整体替换（加载 / 撤销恢复）时递增，供按 Id 建索引的调用方判断索引是否过期。</summary>
    public int Version { get; private set; }

    public bool HasExternalChanges => FileStamp.Of(FilePath) != _stamp;

    public bool IsDirty => !IsSavedState(CaptureState());

    // ============================================================= 加载 =============================================================

    public void Reload() => Load();

    public void Load()
    {
        Version++;
        Items.Clear();
        _origins = new ConditionalWeakTable<T, RecordOrigin>();
        _layout = JsonFileLayout.Default;
        LoadError = null;
        _stamp = FileStamp.Of(FilePath);

        if (!File.Exists(FilePath))
        {
            LoadError = Loc.F("JsonEditor.Error.FileMissing", FilePath);
        }
        else
        {
            try
            {
                var scan = JsonArrayScan.Run(File.ReadAllBytes(FilePath));
                _layout = JsonFileLayout.Detect(scan);
                var errors = new List<string>();
                var loadToken = new object();
                for (int i = 0; i < scan.Spans.Count; i++)
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<T>(scan.Slice(i).Span, GameJson.Options);
                        if (item == null)
                        {
                            errors.Add(Loc.F("JsonEditor.Error.RecordInvalid", i + 1, "null"));
                            continue;
                        }
                        _origins.AddOrUpdate(item, CreateOrigin(scan, i, item, loadToken));
                        Items.Add(item);
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
                    {
                        errors.Add(Loc.F("JsonEditor.Error.RecordInvalid", i + 1, ex.Message));
                    }
                }
                if (errors.Count > 0) LoadError = JoinErrors(errors);
            }
            catch (JsonException ex)
            {
                Items.Clear();
                LoadError = Loc.F("JsonEditor.Error.JsonSyntax", (ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Items.Clear();
                LoadError = ex.Message;
            }
        }

        if (LoadError != null) Debug.WriteLine($"[GameJsonTable] {FileName} 加载失败: {LoadError}");
        _saved = (TableState)CaptureState();
    }

    private static string JoinErrors(List<string> errors)
    {
        var text = string.Join(Environment.NewLine, errors.Take(5));
        if (errors.Count > 5) text += Environment.NewLine + Loc.F("JsonEditor.Error.MoreErrors", errors.Count - 5);
        return text;
    }

    private static RecordOrigin CreateOrigin(JsonArrayScan scan, int index, T item, object loadToken)
    {
        using var doc = JsonDocument.Parse(scan.Slice(index), DocumentOptions);
        string raw = scan.Text(scan.Spans[index].Start, scan.Spans[index].End);
        return new RecordOrigin
        {
            Element = doc.RootElement.Clone(),
            Fingerprint = Canon(item),
            RawText = raw,
            // 同一文件里可能混着不同工具写的记录（多行缩进 / 一行一条、CRLF / LF）：改过的记录沿用它自己的写法
            Compact = !raw.Contains('\n'),
            NewLine = raw.Contains("\r\n") ? "\r\n" : raw.Contains('\n') ? "\n" : null,
            LeadingSeparator = index == 0 ? null : scan.Text(scan.Spans[index - 1].End, scan.Spans[index].Start),
            Index = index,
            LoadToken = loadToken
        };
    }

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
            var bytes = GameJson.Encode(BuildText(), _layout.Bom);
            GameJson.WriteFileSafely(FilePath, bytes);
            _stamp = FileStamp.Of(FilePath);
            Rebase(bytes);
            _saved = (TableState)CaptureState();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = Loc.F("JsonEditor.Error.WriteFailed", FilePath, ex.Message);
            return false;
        }
    }

    /// <summary>生成要写入文件的完整文本（不含 BOM）。</summary>
    public string BuildText()
    {
        if (Items.Count == 0) return _layout.LeadIn + "[]" + _layout.Trailing;

        var sb = new StringBuilder(Items.Count * 256);
        sb.Append(_layout.Prefix);
        RecordOrigin? previous = null;
        for (int i = 0; i < Items.Count; i++)
        {
            _origins.TryGetValue(Items[i], out var origin);
            if (i > 0)
            {
                // 与原文件中的前一条仍然相邻时沿用原分隔（逐字节保持），否则用最常见的分隔
                bool adjacent = origin?.LeadingSeparator != null && previous != null
                                && ReferenceEquals(origin.LoadToken, previous.LoadToken)
                                && origin.Index == previous.Index + 1;
                sb.Append(adjacent ? origin!.LeadingSeparator : _layout.Separator);
            }
            sb.Append(RecordText(Items[i], origin));
            previous = origin;
        }
        sb.Append(_layout.Suffix);
        return sb.ToString();
    }

    private string RecordText(T item, RecordOrigin? origin)
    {
        string canon = Canon(item);
        if (origin?.RawText != null && string.Equals(canon, origin.Fingerprint, StringComparison.Ordinal))
            return origin.RawText;

        using var currentDoc = JsonDocument.Parse(canon);
        var current = currentDoc.RootElement;
        var props = new List<(string Name, JsonElement Value)>();

        if (origin == null)
        {
            foreach (var p in current.EnumerateObject()) props.Add((p.Name, p.Value));
            return _layout.FormatRecord(props, null);
        }

        using var loadedDoc = JsonDocument.Parse(origin.Fingerprint);
        var loaded = loadedDoc.RootElement;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in origin.Element.EnumerateObject())
        {
            if (TryGetProperty(current, p.Name, out var currentValue))
            {
                handled.Add(p.Name);
                // 值没变就写原始文本（保留 1.0、字符串数字等原写法），变了写新值
                bool unchanged = TryGetProperty(loaded, p.Name, out var loadedValue) && JsonElement.DeepEquals(currentValue, loadedValue);
                props.Add((p.Name, unchanged ? p.Value : currentValue));
            }
            else
            {
                // 模型不认识的字段原样保留
                props.Add((p.Name, p.Value));
            }
        }
        foreach (var p in current.EnumerateObject())
        {
            if (handled.Contains(p.Name)) continue;
            // 原记录没有的字段：只有改得和加载时的默认值不同才写出，避免“打开即多出字段”
            if (TryGetProperty(loaded, p.Name, out var loadedValue) && JsonElement.DeepEquals(p.Value, loadedValue)) continue;
            props.Add((p.Name, p.Value));
        }
        return _layout.FormatRecord(props, origin);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>保存后以刚写出的文本为新的“原文”，下次保存时未改动的记录继续逐字保持。</summary>
    private void Rebase(byte[] written)
    {
        try
        {
            var scan = JsonArrayScan.Run(written);
            if (scan.Spans.Count != Items.Count) return;
            _layout = JsonFileLayout.Detect(scan);
            var loadToken = new object();
            for (int i = 0; i < Items.Count; i++)
                _origins.AddOrUpdate(Items[i], CreateOrigin(scan, i, Items[i], loadToken));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            Debug.WriteLine($"[GameJsonTable] {FileName} 保存后重建原文失败: {ex.Message}");
        }
    }

    // ============================================================= 记录操作 =============================================================

    /// <summary>
    /// 深拷贝一条记录（复制 / 粘贴用）。副本继承原记录的字段顺序与模型不认识的字段，
    /// 但不继承原文（副本一定按新记录写出）。
    /// </summary>
    public T Clone(T source)
    {
        var copy = JsonSerializer.Deserialize<T>(Canon(source), GameJson.Options)!;
        if (_origins.TryGetValue(source, out var origin))
            _origins.AddOrUpdate(copy, origin.AsTemplate());
        return copy;
    }

    /// <summary>记录的规范 JSON（紧凑、不转义中文），用于比较与快照。</summary>
    public static string Canon(T item) => JsonSerializer.Serialize(item, GameJson.Options);

    // ============================================================= 快照 =============================================================

    public object CaptureState()
    {
        var entries = new Entry[Items.Count];
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            _origins.TryGetValue(item, out var origin);
            entries[i] = new Entry(CachedCanon(item), origin);
        }
        return new TableState(entries);
    }

    /// <summary>与上次快照相同的记录复用同一个字符串实例，长历史只占改动部分的内存。</summary>
    private string CachedCanon(T item)
    {
        string canon = Canon(item);
        if (_canonCache.TryGetValue(item, out var box) && string.Equals(box.Value, canon, StringComparison.Ordinal))
            return box.Value!;
        _canonCache.AddOrUpdate(item, new StrongBox<string>(canon));
        return canon;
    }

    public void RestoreState(object state)
    {
        var s = (TableState)state;
        var restored = new List<T>(s.Entries.Length);
        foreach (var entry in s.Entries)
        {
            var item = JsonSerializer.Deserialize<T>(entry.Canon, GameJson.Options)!;
            if (entry.Origin != null) _origins.AddOrUpdate(item, entry.Origin);
            _canonCache.AddOrUpdate(item, new StrongBox<string>(entry.Canon));
            restored.Add(item);
        }
        Version++;
        Items.Clear();
        Items.AddRange(restored);
    }

    public bool StateEquals(object a, object b)
    {
        var x = ((TableState)a).Entries;
        var y = ((TableState)b).Entries;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++)
        {
            if (!string.Equals(x[i].Canon, y[i].Canon, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public bool IsSavedState(object state) => StateEquals(state, _saved);

    // ============================================================= 内部类型 =============================================================

    private readonly record struct Entry(string Canon, RecordOrigin? Origin);

    private sealed class TableState(Entry[] entries)
    {
        public Entry[] Entries { get; } = entries;
    }
}

/// <summary>一条记录在原文件里的样子。</summary>
internal sealed class RecordOrigin
{
    /// <summary>原始记录（属性顺序、原始值写法）。</summary>
    public required JsonElement Element { get; init; }

    /// <summary>加载时的规范 JSON，用来判断记录 / 字段是否改过。</summary>
    public required string Fingerprint { get; init; }

    /// <summary>原记录文本；副本为 null（一定重新排版）。</summary>
    public string? RawText { get; init; }

    /// <summary>原文件里该记录之前的分隔文本。</summary>
    public string? LeadingSeparator { get; init; }

    /// <summary>原文件里的序号；副本为 -1。</summary>
    public int Index { get; init; } = -1;

    /// <summary>标识同一次加载，判断两条记录在原文件中是否相邻。</summary>
    public object? LoadToken { get; init; }

    /// <summary>原记录是一行一条的紧凑写法。</summary>
    public bool Compact { get; init; }

    /// <summary>原记录内部使用的换行符；紧凑记录为 null。</summary>
    public string? NewLine { get; init; }

    public RecordOrigin AsTemplate() => new() { Element = Element, Fingerprint = Fingerprint, Compact = Compact, NewLine = NewLine };
}

/// <summary>顶层数组的扫描结果：每条记录的字节范围与数组括号位置。</summary>
internal sealed class JsonArrayScan
{
    private JsonArrayScan(byte[] body, bool bom)
    {
        Body = body;
        Bom = bom;
    }

    public byte[] Body { get; }
    public bool Bom { get; }
    public List<(int Start, int End)> Spans { get; } = new();
    public int ArrayStart { get; private set; }
    public int ArrayEnd { get; private set; }

    public ReadOnlyMemory<byte> Slice(int index) => new(Body, Spans[index].Start, Spans[index].End - Spans[index].Start);

    public string Text(int start, int end) => Encoding.UTF8.GetString(Body, start, end - start);

    public static JsonArrayScan Run(byte[] bytes)
    {
        bool bom = GameJson.HasBom(bytes);
        var scan = new JsonArrayScan(bom ? bytes[3..] : bytes, bom);
        var reader = new Utf8JsonReader(scan.Body, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new InvalidDataException(Loc.T("JsonEditor.Error.RootNotArray"));
        scan.ArrayStart = (int)reader.TokenStartIndex;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                scan.ArrayEnd = (int)reader.TokenStartIndex;
                while (reader.Read()) { }
                return scan;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new InvalidDataException(Loc.F("JsonEditor.Error.RecordNotObject", scan.Spans.Count + 1));
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            scan.Spans.Add((start, (int)reader.BytesConsumed));
        }
        throw new InvalidDataException(Loc.T("JsonEditor.Error.Unterminated"));
    }
}

/// <summary>
/// JSON 文件的排版风格（从原文件推断）：BOM、换行符、多行缩进 / 一行一条、缩进字符与宽度，
/// 以及数组开头、记录之间、数组结尾的原始文本。
/// </summary>
internal sealed class JsonFileLayout
{
    public static JsonFileLayout Default => new();

    public bool Bom { get; private init; }
    public string NewLine { get; private init; } = "\r\n";
    public bool Indented { get; private init; } = true;
    public char IndentChar { get; private init; } = ' ';
    public int IndentSize { get; private init; } = 2;
    public string RecordIndent { get; private init; } = "  ";

    /// <summary>'[' 之前的文本。</summary>
    public string LeadIn { get; private init; } = "";

    /// <summary>开头到第一条记录之前的文本（含 '['）。</summary>
    public string Prefix { get; private init; } = "[\r\n  ";

    /// <summary>最常见的记录间分隔。</summary>
    public string Separator { get; private init; } = ",\r\n  ";

    /// <summary>最后一条记录之后到文件末尾的文本（含 ']'）。</summary>
    public string Suffix { get; private init; } = "\r\n]\r\n";

    /// <summary>']' 之后的文本。</summary>
    public string Trailing { get; private init; } = "\r\n";

    public static JsonFileLayout Detect(JsonArrayScan scan)
    {
        var body = scan.Body;
        int crlf = 0, lf = 0;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != (byte)'\n') continue;
            if (i > 0 && body[i - 1] == (byte)'\r') crlf++; else lf++;
        }
        string newLine = lf > crlf ? "\n" : "\r\n";
        string leadIn = scan.Text(0, scan.ArrayStart);
        string trailing = scan.Text(scan.ArrayEnd + 1, body.Length);

        if (scan.Spans.Count == 0)
        {
            return new JsonFileLayout
            {
                Bom = scan.Bom,
                NewLine = newLine,
                LeadIn = leadIn,
                Prefix = leadIn + "[" + newLine + "  ",
                Separator = "," + newLine + "  ",
                Suffix = newLine + "]" + trailing,
                Trailing = trailing
            };
        }

        string prefix = scan.Text(0, scan.Spans[0].Start);
        string first = scan.Text(scan.Spans[0].Start, scan.Spans[0].End);
        int lastBreak = prefix.LastIndexOf('\n');
        string recordIndent = lastBreak >= 0 ? prefix[(lastBreak + 1)..] : "";
        if (recordIndent.Any(c => c != ' ' && c != '\t')) recordIndent = "";

        bool indented = first.Contains('\n');
        char indentChar = ' ';
        int indentSize = 2;
        if (indented)
        {
            int lineStart = first.IndexOf('\n') + 1;
            int k = lineStart;
            while (k < first.Length && (first[k] == ' ' || first[k] == '\t')) k++;
            if (k > lineStart) indentChar = first[lineStart];
            int size = k - lineStart - recordIndent.Length;
            indentSize = size is >= 1 and <= 127 ? size : 2;
        }

        string separator;
        if (scan.Spans.Count > 1)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 1; i < scan.Spans.Count; i++)
            {
                var sep = scan.Text(scan.Spans[i - 1].End, scan.Spans[i].Start);
                counts[sep] = counts.TryGetValue(sep, out var c) ? c + 1 : 1;
            }
            separator = counts.MaxBy(kv => kv.Value).Key;
        }
        else
        {
            separator = "," + (lastBreak >= 0 ? newLine : "") + recordIndent;
        }

        return new JsonFileLayout
        {
            Bom = scan.Bom,
            NewLine = newLine,
            Indented = indented,
            IndentChar = indentChar,
            IndentSize = indentSize,
            RecordIndent = recordIndent,
            LeadIn = leadIn,
            Prefix = prefix,
            Separator = separator,
            Suffix = scan.Text(scan.Spans[^1].End, body.Length),
            Trailing = trailing
        };
    }

    /// <summary>
    /// 排版一条记录（第一行的缩进由前面的分隔文本提供）。来自原文件的记录沿用它自己的紧凑 / 缩进写法与换行符，
    /// 新记录按本文件的主流风格。
    /// </summary>
    public string FormatRecord(IReadOnlyList<(string Name, JsonElement Value)> props, RecordOrigin? origin)
    {
        bool indented = origin == null ? Indented : !origin.Compact;
        string newLine = origin?.NewLine ?? NewLine;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = indented,
            IndentCharacter = IndentChar,
            IndentSize = IndentSize,
            NewLine = newLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in props)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        string text = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return indented && RecordIndent.Length > 0 ? text.Replace(newLine, newLine + RecordIndent) : text;
    }
}

/// <summary>
/// 宽松的 int 读取转换器：容忍 null、字符串数字、浮点数、布尔等非常规写法。
/// 写出时总是写数字；没改过的字段由 <see cref="GameJsonTable{T}"/> 按原始写法保留。
/// </summary>
public sealed class SafeInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetDouble(out var d)) return Clamp(d);
                return 0;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)) return si;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)) return Clamp(sd);
                return 0;

            case JsonTokenType.True:
                return 1;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);

    private static int Clamp(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        if (d >= int.MaxValue) return int.MaxValue;
        if (d <= int.MinValue) return int.MinValue;
        return (int)Math.Round(d);
    }
}

/// <summary>宽松的 List&lt;int&gt; 读取转换器：容忍 null、单个数字、逗号分隔字符串等写法。</summary>
public sealed class SafeIntListConverter : JsonConverter<List<int>>
{
    private static readonly SafeInt32Converter IntReader = new();

    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<int>();
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                list.Add(IntReader.Read(ref reader, typeof(int), options));
                return list;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    foreach (var part in s.Split(',', ';', '|', ' ', '\t'))
                    {
                        if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                            list.Add(v);
                    }
                }
                return list;

            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    list.Add(IntReader.Read(ref reader, typeof(int), options));
                return list;

            default:
                return list;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteNumberValue(v);
        writer.WriteEndArray();
    }
}
