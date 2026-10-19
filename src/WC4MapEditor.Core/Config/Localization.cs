using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WC4MapEditor.Core.Helpers;

namespace WC4MapEditor.Core.Config;

/// <summary>一种界面语言：代码（文件名）+ 自己语言里的显示名。</summary>
public sealed record LanguageInfo(string Code, string DisplayName);

/// <summary>
/// 界面文本本地化（翻译键 → 文本）。
/// <para>
/// 翻译文件放在 <c>Resource/Lang/&lt;code&gt;.json</c>（如 zh-CN.json / en-US.json），
/// 内容是一个 JSON 对象，键形如 <c>"Common.Save"</c>，值是文本；允许嵌套对象（会按点号拍平）、注释与末尾逗号。
/// 文件里的 <c>Language.Name</c> 键是这门语言在语言菜单里的显示名。
/// </para>
/// <para>
/// 查找顺序：当前语言 → 默认语言（zh-CN）→ 键本身。这样新增文本只要先写中文就不会在界面上出现空白，
/// 而缺失的英文翻译会以中文兜底而不是报错。
/// </para>
/// <para>
/// 为什么放在 Core：GUI / AI / Rendering 都可能需要文本，而 Core 不依赖任何界面框架，
/// 所有程序集都能引用；语言偏好沿用 setting.txt 的 <c>Language</c> 键，与其它全局设置放在一起。
/// </para>
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "zh-CN";
    public const string SettingKey = "Language";
    private const string LangFolder = "Lang";
    private const string NameKey = "Language.Name";

    private static readonly object _gate = new();
    private static Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private static List<LanguageInfo> _available = new();
    private static bool _initialized;

    /// <summary>当前语言代码（与翻译文件名一致，如 zh-CN）。</summary>
    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>语言切换完成后触发（已持有的场景需要自己重建界面才能换文字）。</summary>
    public static event Action? LanguageChanged;

    /// <summary>Resource/Lang 目录（找不到时返回 Resource 下的推测路径）。</summary>
    public static string LangDirectory
    {
        get
        {
            string resource = ConfigManager.Instance.ResourcePath;
            if (string.IsNullOrEmpty(resource))
                resource = ProjectPaths.FindDirectory("Resource") ?? Path.Combine(AppContext.BaseDirectory, "Resource");
            return Path.Combine(resource, LangFolder);
        }
    }

    /// <summary>可用语言列表（扫描 Lang 目录下的 *.json）。</summary>
    public static IReadOnlyList<LanguageInfo> AvailableLanguages
    {
        get { EnsureInitialized(); return _available; }
    }

    /// <summary>
    /// 初始化：扫描语言文件，按 setting.txt 的 Language 键（没有则 zh-CN）加载。
    /// 重复调用无副作用。
    /// </summary>
    public static void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true;

            ScanLanguages();
            string preferred = ConfigManager.Instance.GetText(SettingKey, DefaultLanguage).Trim();
            if (string.IsNullOrEmpty(preferred)) preferred = DefaultLanguage;
            LoadInternal(preferred);
        }
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }

    /// <summary>
    /// 切换语言。<paramref name="persist"/> 为 true 时写回 setting.txt。
    /// 语言文件不存在时返回 false 且不改变当前语言。
    /// </summary>
    public static bool SetLanguage(string code, bool persist = true)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();

        if (!File.Exists(Path.Combine(LangDirectory, code + ".json")))
        {
            Debug.WriteLine($"[Loc] 语言文件不存在: {code}");
            return false;
        }

        lock (_gate)
        {
            ScanLanguages();
            LoadInternal(code);
        }

        if (persist) SaveLanguagePreference(code);
        LanguageChanged?.Invoke();
        return true;
    }

    /// <summary>取文本：当前语言 → 默认语言 → 键本身。</summary>
    public static string T(string key)
    {
        EnsureInitialized();
        if (_current.TryGetValue(key, out var v)) return v;
        if (_fallback.TryGetValue(key, out v)) return v;
        return key;
    }

    /// <summary>取文本，两种语言都没有时返回 <paramref name="fallback"/>。</summary>
    public static string T(string key, string fallback)
    {
        EnsureInitialized();
        if (_current.TryGetValue(key, out var v)) return v;
        if (_fallback.TryGetValue(key, out v)) return v;
        return fallback;
    }

    /// <summary>
    /// 取文本并按 <see cref="string.Format(IFormatProvider, string, object[])"/> 填入参数（{0}、{1}…）。
    /// 翻译里占位符写错导致格式异常时，退回未格式化的文本，不让界面崩掉。
    /// </summary>
    public static string F(string key, params object?[] args)
    {
        string text = T(key);
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return text; }
    }

    /// <summary>当前语言或默认语言里是否存在该键。</summary>
    public static bool Has(string key)
    {
        EnsureInitialized();
        return _current.ContainsKey(key) || _fallback.ContainsKey(key);
    }

    // ============================================================= 加载 =============================================================

    private static void ScanLanguages()
    {
        var list = new List<LanguageInfo>();
        try
        {
            string dir = LangDirectory;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    if (code.StartsWith('_')) continue;   // 允许放 _template.json 之类的辅助文件
                    string name = code;
                    try
                    {
                        var table = LoadFile(file);
                        if (table.TryGetValue(NameKey, out var n) && !string.IsNullOrWhiteSpace(n)) name = n;
                    }
                    catch { /* 文件坏了也先列出来，加载时再报 */ }
                    list.Add(new LanguageInfo(code, name));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Loc] 扫描语言目录失败: {ex.Message}");
        }

        // 默认语言排最前，其余按代码排序，菜单顺序稳定
        list.Sort((a, b) =>
        {
            if (a.Code == DefaultLanguage) return -1;
            if (b.Code == DefaultLanguage) return 1;
            return string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
        });
        _available = list;
    }

    private static void LoadInternal(string code)
    {
        string dir = LangDirectory;
        _fallback = LoadLanguage(dir, DefaultLanguage);

        if (string.Equals(code, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _current = _fallback;
            CurrentLanguage = DefaultLanguage;
        }
        else
        {
            var table = LoadLanguage(dir, code);
            if (table.Count == 0 && !File.Exists(Path.Combine(dir, code + ".json")))
            {
                Debug.WriteLine($"[Loc] 语言 {code} 不存在，退回 {DefaultLanguage}");
                _current = _fallback;
                CurrentLanguage = DefaultLanguage;
            }
            else
            {
                _current = table;
                CurrentLanguage = code;
            }
        }

        Debug.WriteLine($"[Loc] 语言 {CurrentLanguage}：{_current.Count} 条，默认语言 {_fallback.Count} 条");
    }

    /// <summary>
    /// 读一门语言：主文件 <c>&lt;code&gt;.json</c>，再叠加同名子目录 <c>&lt;code&gt;/*.json</c> 里的分文件（按文件名排序）。
    /// 分文件用于按功能拆分翻译（例如 zh-CN/mapcase.json）；同一个键以主文件为准，分文件只补充新键。
    /// </summary>
    private static Dictionary<string, string> LoadLanguage(string dir, string code)
    {
        var table = TryLoad(Path.Combine(dir, code + ".json"));
        string subDir = Path.Combine(dir, code);
        if (!Directory.Exists(subDir)) return table;

        try
        {
            foreach (var file in Directory.EnumerateFiles(subDir, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var kv in TryLoad(file))
                    table.TryAdd(kv.Key, kv.Value);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Loc] 读取分文件目录 {subDir} 失败: {ex.Message}");
        }
        return table;
    }

    private static Dictionary<string, string> TryLoad(string path)
    {
        try
        {
            if (File.Exists(path)) return LoadFile(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Loc] 读取 {path} 失败: {ex.Message}");
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), options);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        Flatten(doc.RootElement, "", result);
        return result;
    }

    /// <summary>嵌套对象按点号拍平：{"Common":{"Save":"保存"}} 与 {"Common.Save":"保存"} 等价。</summary>
    private static void Flatten(JsonElement obj, string prefix, Dictionary<string, string> into)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            string key = prefix.Length == 0 ? prop.Name : prefix + "." + prop.Name;
            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(prop.Value, key, into);
                    break;
                case JsonValueKind.String:
                    into[key] = prop.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    into[key] = prop.Value.ToString();
                    break;
            }
        }
    }

    // ============================================================= 持久化 =============================================================

    /// <summary>
    /// 把语言偏好写回 setting.txt 的 <c>Language</c> 键：已有则原地替换，否则插到 [全局编辑] 节后面，
    /// 再没有就追加到文件末尾。只改这一行，其它内容原样保留。
    /// </summary>
    public static void SaveLanguagePreference(string code)
    {
        try
        {
            string path = ProjectPaths.FindFile("setting.txt") ?? Path.Combine(AppContext.BaseDirectory, "setting.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
            string newLine = $"{SettingKey} = \"{code}\"";

            int idx = lines.FindIndex(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith('#')) return false;
                int eq = t.IndexOf('=');
                return eq > 0 && t[..eq].Trim() == SettingKey;
            });

            if (idx >= 0)
            {
                lines[idx] = newLine;
            }
            else
            {
                int section = lines.FindIndex(l => l.Trim() == "[全局编辑]");
                if (section >= 0)
                {
                    lines.Insert(section + 1, "#界面语言（Resource/Lang 下的文件名）");
                    lines.Insert(section + 2, newLine);
                }
                else
                {
                    lines.Add("");
                    lines.Add("[全局编辑]");
                    lines.Add("#界面语言（Resource/Lang 下的文件名）");
                    lines.Add(newLine);
                }
            }

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Loc] 写入语言偏好失败: {ex.Message}");
        }
    }
}
