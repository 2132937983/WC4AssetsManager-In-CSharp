using System.IO;
using System.Text;

namespace WC4MapEditor.GUI.Services;

/// <summary>
/// setting.txt 的逐行文档：读出各节的键值与说明注释，写回时只改动值变了的那几行，
/// 其余内容（注释、空行、键的写法与空格、引号、换行风格、BOM）原样保留。
/// <para>
/// 解析规则与 <see cref="SettingTxtParser"/> 保持一致：只有以 # 开头的整行是注释，
/// "键 = 值" 里等号后面的全部内容都是值（值里可以出现 #），值两侧的一对双引号不算值的一部分。
/// 第一个 [节] 之前的键也能编辑（归入名为空串的节）。
/// </para>
/// </summary>
internal sealed class SettingTxtDocument
{
    /// <summary>文件里的一个 "键 = 值" 行。</summary>
    public sealed class Entry
    {
        internal Entry(string section, string key, int occurrence, string value, bool quoted, string comment, int lineIndex)
        {
            Section = section;
            Key = key;
            Occurrence = occurrence;
            Value = value;
            Quoted = quoted;
            Comment = comment;
            LineIndex = lineIndex;
        }

        /// <summary>所在节（不带方括号；第一个节之前的键为空串）</summary>
        public string Section { get; }

        public string Key { get; }

        /// <summary>同一节里同名键的第几次出现（从 0 起），与 Section / Key 一起定位这一行</summary>
        public int Occurrence { get; }

        /// <summary>值（已去掉两侧的引号）</summary>
        public string Value { get; internal set; }

        /// <summary>写回时是否给值加引号（保持原文写法）</summary>
        public bool Quoted { get; internal set; }

        /// <summary>紧挨在该行上方的注释（去掉 #，多行以换行连接），没有则为空串</summary>
        public string Comment { get; }

        /// <summary>
        /// 文件后面还有同名键：程序读取配置时同名键以最后一个为准（不分节），这一行实际不生效
        /// </summary>
        public bool IsOverridden { get; internal set; }

        internal int LineIndex { get; }
    }

    /// <summary>节里的一项（按文件顺序）：键值行，或者不紧挨任何键的注释块。</summary>
    public sealed record Item(Entry? Entry, string? Note);

    public sealed class Section
    {
        internal Section(string name) => Name = name;

        /// <summary>节名（不带方括号；第一个节之前的内容为空串）</summary>
        public string Name { get; }

        public List<Item> Items { get; } = new();

        public IEnumerable<Entry> Entries => Items.Where(i => i.Entry != null).Select(i => i.Entry!);

        /// <summary>节标题所在行（第一个节之前的部分为 -1）</summary>
        internal int HeaderLine { get; init; } = -1;
    }

    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _hasBom;
    private readonly bool _trailingNewline;
    private List<Section> _sections = new();

    public string FilePath { get; }

    /// <summary>各节，按文件中第一次出现的顺序</summary>
    public IReadOnlyList<Section> Sections => _sections;

    public IEnumerable<Entry> Entries => _sections.SelectMany(s => s.Entries);

    private SettingTxtDocument(string path, List<string> lines, string newline, bool hasBom, bool trailingNewline)
    {
        FilePath = path;
        _lines = lines;
        _newline = newline;
        _hasBom = hasBom;
        _trailingNewline = trailingNewline;
        Parse();
    }

    /// <summary>读文件；文件不存在时返回空文档（保存时新建）。</summary>
    public static SettingTxtDocument Load(string path)
    {
        if (!File.Exists(path))
            return new SettingTxtDocument(path, new List<string>(), Environment.NewLine, false, true);

        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        bool trailing = text.EndsWith('\n');

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (trailing && lines.Count > 0) lines.RemoveAt(lines.Count - 1);
        return new SettingTxtDocument(path, lines, newline, bom, trailing);
    }

    public Section? FindSection(string name) => _sections.FirstOrDefault(s => s.Name == name);

    /// <summary>按 节 / 键 / 第几次出现 定位一行（文件被别处改过、行号变了也能找到同一个键）。</summary>
    public Entry? Find(string section, string key, int occurrence = 0)
        => FindSection(section)?.Entries.FirstOrDefault(e => e.Key == key && e.Occurrence == occurrence);

    /// <summary>改写一行的值：保留 "键 =" 的原写法与等号后的空白、原有引号与行尾空白。</summary>
    public void SetValue(Entry entry, string value)
    {
        value = value.Replace("\r", "").Replace("\n", " ");
        // 首尾带空白的值不加引号会被解析器修剪掉，所以自动补引号
        bool quote = entry.Quoted || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));

        string raw = _lines[entry.LineIndex];
        int eq = raw.IndexOf('=');
        string prefix = raw[..(eq + 1)];
        string rest = raw[(eq + 1)..];

        string lead, trail;
        if (rest.Trim().Length == 0)
        {
            lead = " ";
            trail = "";
        }
        else
        {
            lead = rest[..(rest.Length - rest.TrimStart().Length)];
            trail = rest[rest.TrimEnd().Length..];
        }

        string formatted = quote ? $"\"{value}\"" : value;
        _lines[entry.LineIndex] = formatted.Length == 0 ? prefix : prefix + lead + formatted + trail;
        entry.Value = value;
        entry.Quoted = quote;
    }

    /// <summary>
    /// 设置一个全局键：文件里已有就改生效的那一行（最后一个同名键），
    /// 否则插到 <paramref name="section"/> 节末尾（没有该节则在文件末尾新建），可附一行说明注释。
    /// 返回文档是否有变化（值本来就相同时不改动，调用方可以省掉一次写盘）。
    /// </summary>
    public bool SetOrAdd(string section, string key, string value, string? comment = null)
    {
        var existing = Entries.Where(e => e.Key == key).OrderBy(e => e.LineIndex).LastOrDefault();
        if (existing != null)
        {
            if (existing.Value == value) return false;
            SetValue(existing, value);
            return true;
        }

        var newLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(comment)) newLines.Add("#" + comment);
        newLines.Add($"{key} = {value}");

        var target = FindSection(section);
        if (target != null)
        {
            _lines.InsertRange(SectionEndLine(target) + 1, newLines);
        }
        else
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.AddRange(newLines);
        }
        Parse();
        return true;
    }

    /// <summary>写回文件（先写临时文件再替换，写到一半出错也不会留下残缺的 setting.txt）。</summary>
    public void Save()
    {
        string text = string.Join(_newline, _lines) + (_trailingNewline && _lines.Count > 0 ? _newline : "");
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(_hasBom));
        File.Move(tmp, FilePath, overwrite: true);
    }

    // ============================================================= 解析 =============================================================

    private void Parse()
    {
        var sections = new List<Section>();
        var occurrences = new Dictionary<(string Section, string Key), int>();
        var pending = new List<string>();
        Section? current = null;

        Section Current()
        {
            if (current == null)
            {
                current = new Section("");
                sections.Add(current);
            }
            return current;
        }

        void FlushNote()
        {
            if (pending.Count == 0) return;
            Current().Items.Add(new Item(null, string.Join("\n", pending)));
            pending.Clear();
        }

        for (int i = 0; i < _lines.Count; i++)
        {
            string t = _lines[i].Trim();
            if (t.Length == 0)
            {
                FlushNote();
                continue;
            }
            if (t.StartsWith('#'))
            {
                pending.Add(t[1..].Trim());
                continue;
            }
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                string name = t[1..^1].Trim();
                current = sections.FirstOrDefault(s => s.Name == name);
                if (current == null)
                {
                    current = new Section(name) { HeaderLine = i };
                    sections.Add(current);
                }
                // 紧挨在节标题上方的注释是在说明这一节
                FlushNote();
                continue;
            }

            int eq = t.IndexOf('=');
            if (eq <= 0)
            {
                FlushNote();
                continue;
            }

            string key = t[..eq].Trim();
            string value = t[(eq + 1)..].Trim();
            bool quoted = value.Length >= 2 && value[0] == '"' && value[^1] == '"';
            if (quoted) value = value[1..^1];

            var section = Current();
            occurrences.TryGetValue((section.Name, key), out int occurrence);
            occurrences[(section.Name, key)] = occurrence + 1;

            section.Items.Add(new Item(new Entry(section.Name, key, occurrence, value, quoted, string.Join("\n", pending), i), null));
            pending.Clear();
        }
        FlushNote();

        // 程序把所有键放进同一张表（后写的覆盖先写的），据此标出不生效的行
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in sections.SelectMany(s => s.Entries).OrderByDescending(e => e.LineIndex))
            entry.IsOverridden = !seen.Add(entry.Key);

        _sections = sections;
    }

    /// <summary>节的最后一行（最后一个键值行；节里还没有键时为节标题行，文件开头的无名节为 -1）</summary>
    private static int SectionEndLine(Section section)
    {
        var last = section.Entries.OrderBy(e => e.LineIndex).LastOrDefault();
        return last?.LineIndex ?? section.HeaderLine;
    }
}
