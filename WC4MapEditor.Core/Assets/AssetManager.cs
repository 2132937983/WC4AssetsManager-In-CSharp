using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Assets;

/// <summary>
/// 资源管理器：基于 <see cref="AssetCache"/> 的高层封装，提供便捷的资源扫描、查询与读取接口。
///
/// 典型用法：
/// <code>
/// var manager = AssetManager.Default;
/// manager.Scan(@"E:\CSharpProject\WC4MapEditor\Resource\WC4DATA\assets");
/// var stages = manager.Query(kind: AssetKind.Stage);
/// var bytes = manager.ReadBytes(stages[0]);
/// </code>
/// </summary>
public sealed class AssetManager
{
    private static AssetManager? _default;

    /// <summary>全局默认实例，内部共享同一个 <see cref="AssetCache"/> 单例。</summary>
    public static AssetManager Default => _default ??= new AssetManager();

    private readonly AssetCache _cache;

    /// <summary>默认 assets 根目录（相对 WC4DATA 结构自动解析）。</summary>
    public static string GetDefaultAssetsPath() => ResolveDefaultAssetsPath();

    private static string ResolveDefaultAssetsPath()
    {
        string? baseDir = AppContext.BaseDirectory;
        // 从执行目录向上查找 Resource/WC4DATA/assets
        // CLI 输出目录结构: <solution>/WC4MapEditor.Cli/bin/Debug/<tfm>/
        // 需要 4 层 .. 回到 solution 根目录
        string candidate = Path.GetFullPath(Path.Combine(
            baseDir ?? "", "..", "..", "..", "..", "Resource", "WC4DATA", "assets"));
        if (Directory.Exists(candidate)) return candidate;

        // 兜底：逐级向上搜索（最多 5 层）
        string? current = baseDir;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
        {
            candidate = Path.Combine(current, "Resource", "WC4DATA", "assets");
            if (Directory.Exists(candidate)) return candidate;
            current = Path.GetDirectoryName(current);
        }
        return candidate;
    }

    public AssetManager() : this(AssetCache.Instance) { }

    internal AssetManager(AssetCache cache)
    {
        _cache = cache;
    }

    /// <summary>已加载的 assets 根目录。</summary>
    public string AssetsRoot => _cache.AssetsRoot;

    /// <summary>是否已完成扫描。</summary>
    public bool IsLoaded => _cache.IsLoaded;

    /// <summary>缓存中的文件总数。</summary>
    public int Count => _cache.Count;

    // ---------------------------------------------------------------------
    // 扫描
    // ---------------------------------------------------------------------

    /// <summary>
    /// 扫描 assets 目录。若已加载相同目录则跳过（除非 <paramref name="forceReload"/> 为 true）。
    /// </summary>
    public int Scan(string assetsRoot, bool forceReload = false)
        => _cache.Load(assetsRoot, forceReload);

    /// <summary>使用默认路径扫描。首次调用时自动解析默认路径。</summary>
    public int ScanDefault(bool forceReload = false)
        => _cache.Load(GetDefaultAssetsPath(), forceReload);

    /// <summary>清空缓存。</summary>
    public void Clear() => _cache.Clear();

    // ---------------------------------------------------------------------
    // 查询
    // ---------------------------------------------------------------------

    /// <summary>返回全部缓存条目的快照。</summary>
    public IReadOnlyList<AssetEntry> ListAll() => _cache.GetAll();

    /// <summary>按语义类别查询。</summary>
    public IReadOnlyList<AssetEntry> ListByKind(AssetKind kind) => _cache.GetByKind(kind);

    /// <summary>按扩展名查询（不区分大小写，可带或不带点）。</summary>
    public IReadOnlyList<AssetEntry> ListByExtension(string extension)
        => _cache.GetByExtension(extension);

    /// <summary>按顶层目录查询。</summary>
    public IReadOnlyList<AssetEntry> ListByDirectory(string topDirectory)
        => _cache.GetByTopDirectory(topDirectory);

    /// <summary>
    /// 组合查询：可同时按类别、扩展名、顶层目录、文件名关键字过滤。
    /// 任意参数传 null 表示不限制该条件。
    /// </summary>
    public IReadOnlyList<AssetEntry> Query(
        AssetKind? kind = null,
        string? extension = null,
        string? topDirectory = null,
        string? nameContains = null)
        => _cache.Query(kind, extension, topDirectory, nameContains);

    /// <summary>按相对路径精确获取单个条目，找不到返回 null。</summary>
    public AssetEntry? Find(string relativePath) => _cache.GetByRelativePath(relativePath);

    /// <summary>返回全部条目，按最后修改时间降序排列（最新的在前）。</summary>
    public IReadOnlyList<AssetEntry> GetAllSortedByModifiedTime()
        => _cache.GetAllSortedByModifiedTime();

    // ---------------------------------------------------------------------
    // 读取
    // ---------------------------------------------------------------------

    /// <summary>读取指定条目的原始字节。</summary>
    public byte[] ReadBytes(AssetEntry entry) => _cache.ReadBytes(entry);

    /// <summary>以指定编码读取文本条目。</summary>
    public string ReadText(AssetEntry entry, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return encoding.GetString(ReadBytes(entry));
    }

    /// <summary>打开指定条目的只读文件流。</summary>
    public Stream OpenRead(AssetEntry entry)
        => new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    // ---------------------------------------------------------------------
    // 概览
    // ---------------------------------------------------------------------

    /// <summary>返回各语义类别的文件计数。</summary>
    public IReadOnlyDictionary<AssetKind, int> GetKindCounts() => _cache.GetKindCounts();

    /// <summary>返回各顶层目录的文件计数。</summary>
    public IReadOnlyDictionary<string, int> GetDirectoryCounts() => _cache.GetTopDirectoryCounts();

    /// <summary>
    /// 生成可读的资源概览报告。
    /// </summary>
    public string GetSummaryReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Assets Root: {AssetsRoot}");
        sb.AppendLine($"Total Files: {Count}");
        sb.AppendLine();

        sb.AppendLine("--- By Directory ---");
        foreach (var kv in GetDirectoryCounts().OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {kv.Key,-20} {kv.Value,6}");

        sb.AppendLine();
        sb.AppendLine("--- By Kind ---");
        foreach (var kv in GetKindCounts().OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {kv.Key,-20} {kv.Value,6}");

        return sb.ToString();
    }

    /// <summary>
    /// 打印概览到控制台。
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine(GetSummaryReport());
    }

    // ---------------------------------------------------------------------
    // Data 文件读取（从 assets 缓存中解析 JSON / INI）
    // ---------------------------------------------------------------------

    private List<ConquerCountryConfig>? _conquerCountrySettings;
    private List<GeneralSettings>? _generalSettings;
    private Dictionary<string, string>? _stringTable;

    public List<ConquerCountryConfig> GetConquerCountrySettings()
    {
        if (_conquerCountrySettings != null) return _conquerCountrySettings;
        var entry = Find("json/ConquerCountrySettings.json");
        if (entry != null)
        {
            try
            {
                _conquerCountrySettings = JsonSerializer.Deserialize<List<ConquerCountryConfig>>(ReadText(entry));
                Debug.WriteLine($"[AssetManager] 加载了 {_conquerCountrySettings?.Count ?? 0} 个征服国家配置");
            }
            catch (Exception ex) { Debug.WriteLine($"[AssetManager] 加载 ConquerCountrySettings.json 失败: {ex.Message}"); }
        }
        return _conquerCountrySettings ?? [];
    }

    public List<GeneralSettings> GetGeneralSettings()
    {
        if (_generalSettings != null) return _generalSettings;
        var entry = Find("json/GeneralSettings.json");
        if (entry != null)
        {
            try
            {
                _generalSettings = JsonSerializer.Deserialize<List<GeneralSettings>>(ReadText(entry));
                Debug.WriteLine($"[AssetManager] 加载了 {_generalSettings?.Count ?? 0} 个将领配置");
            }
            catch (Exception ex) { Debug.WriteLine($"[AssetManager] 加载 GeneralSettings.json 失败: {ex.Message}"); }
        }
        return _generalSettings ?? [];
    }

    public Dictionary<string, string> GetStringTable(string locale = "tw")
    {
        if (_stringTable != null) return _stringTable;
        var entry = Find($"stringtable_{locale}.ini");
        if (entry != null)
        {
            try
            {
                _stringTable = ParseIniFile(ReadText(entry));
                Debug.WriteLine($"[AssetManager] 加载了 {_stringTable.Count} 个字符串表条目 (locale={locale})");
            }
            catch (Exception ex) { Debug.WriteLine($"[AssetManager] 加载 stringtable_{locale}.ini 失败: {ex.Message}"); }
        }
        return _stringTable ?? [];
    }

    public string GetStringTableValue(string key, string defaultValue = "", string locale = "tw")
    {
        var table = GetStringTable(locale);
        return table.TryGetValue(key, out var v) ? v : defaultValue;
    }

    private static Dictionary<string, string> ParseIniFile(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();
            result[key] = val;
        }
        return result;
    }
}