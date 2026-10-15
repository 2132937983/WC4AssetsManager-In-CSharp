using System.Diagnostics;
using System.IO;

namespace WC4MapEditor.Core.Assets;

/// <summary>
/// 资源目录（assets）的索引缓存。
///
/// 负责扫描一次 assets 目录，构建全部文件的元数据（<see cref="AssetEntry"/>）并缓存在内存中，
/// 之后可按类别、扩展名、顶层目录、文件名等条件快速查询，无需重复访问磁盘。
///
/// 典型 assets 根目录形如 E:\CSharpProject\WC4MapEditor\Resource\WC4DATA\assets。
///
/// 线程安全：构建与查询使用读写锁保护，可在多线程环境读取。
/// </summary>
public sealed class AssetCache
{
    private static readonly object _instanceLock = new();
    private static AssetCache? _instance;

    /// <summary>进程级单例，便于 CLI / GUI 共享同一份缓存。</summary>
    public static AssetCache Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new AssetCache();
                }
            }
            return _instance;
        }
    }

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly List<AssetEntry> _entries = new();

    // 索引：按类别 / 扩展名 / 顶层目录，加速查询。
    private readonly Dictionary<AssetKind, List<AssetEntry>> _byKind = new();
    private readonly Dictionary<string, List<AssetEntry>> _byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AssetEntry>> _byTopDirectory = new(StringComparer.OrdinalIgnoreCase);

    private string _assetsRoot = "";
    private bool _isLoaded;

    /// <summary>已加载的 assets 根目录绝对路径。</summary>
    public string AssetsRoot
    {
        get { _lock.EnterReadLock(); try { return _assetsRoot; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>是否已完成一次扫描。</summary>
    public bool IsLoaded
    {
        get { _lock.EnterReadLock(); try { return _isLoaded; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>缓存中的文件总数。</summary>
    public int Count
    {
        get { _lock.EnterReadLock(); try { return _entries.Count; } finally { _lock.ExitReadLock(); } }
    }

    public AssetCache() { }

    /// <summary>
    /// 扫描指定 assets 目录并构建缓存。若已加载相同目录且未要求强制刷新，则直接返回缓存。
    /// </summary>
    /// <param name="assetsRoot">assets 根目录。可以直接指向 assets 目录，也可以指向其父目录（会自动向下探测 assets 子目录）。</param>
    /// <param name="forceReload">为 true 时强制重新扫描。</param>
    /// <returns>扫描到的文件数量。</returns>
    public int Load(string assetsRoot, bool forceReload = false)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot))
            throw new ArgumentException("assetsRoot 不能为空", nameof(assetsRoot));

        string resolved = ResolveAssetsRoot(assetsRoot);
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"未找到 assets 目录: {resolved}");

        _lock.EnterWriteLock();
        try
        {
            if (_isLoaded && !forceReload &&
                string.Equals(_assetsRoot, resolved, StringComparison.OrdinalIgnoreCase))
            {
                return _entries.Count;
            }

            ClearInternal();
            _assetsRoot = resolved;

            var sw = Stopwatch.StartNew();
            foreach (string file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
            {
                AssetEntry entry = BuildEntry(resolved, file);
                _entries.Add(entry);
                IndexEntry(entry);
            }
            sw.Stop();

            _isLoaded = true;
            Debug.WriteLine($"[AssetCache] 已缓存 {_entries.Count} 个文件，耗时 {sw.ElapsedMilliseconds} ms，根目录: {resolved}");
            return _entries.Count;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>清空缓存。</summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try { ClearInternal(); }
        finally { _lock.ExitWriteLock(); }
    }

    // ---------------------------------------------------------------------
    // 查询接口
    // ---------------------------------------------------------------------

    /// <summary>返回全部缓存条目的快照。</summary>
    public IReadOnlyList<AssetEntry> GetAll()
    {
        _lock.EnterReadLock();
        try { return _entries.ToArray(); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按语义类别查询，例如 <see cref="AssetKind.Stage"/>。</summary>
    public IReadOnlyList<AssetEntry> GetByKind(AssetKind kind)
    {
        _lock.EnterReadLock();
        try
        {
            return _byKind.TryGetValue(kind, out var list)
                ? list.ToArray()
                : Array.Empty<AssetEntry>();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按扩展名查询（不区分大小写，可带或不带点，例如 "btl" 或 ".btl"）。</summary>
    public IReadOnlyList<AssetEntry> GetByExtension(string extension)
    {
        string ext = NormalizeExtension(extension);
        _lock.EnterReadLock();
        try
        {
            return _byExtension.TryGetValue(ext, out var list)
                ? list.ToArray()
                : Array.Empty<AssetEntry>();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按顶层目录查询（相对 assets 根目录的直属子目录名，例如 "stage"、"map"）。</summary>
    public IReadOnlyList<AssetEntry> GetByTopDirectory(string topDirectory)
    {
        _lock.EnterReadLock();
        try
        {
            return _byTopDirectory.TryGetValue(topDirectory ?? "", out var list)
                ? list.ToArray()
                : Array.Empty<AssetEntry>();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// 组合查询：可同时按类别、扩展名、顶层目录、文件名关键字过滤。
    /// 任意参数传 null 表示不限制该条件。
    /// </summary>
    /// <param name="kind">语义类别过滤。</param>
    /// <param name="extension">扩展名过滤（可带或不带点）。</param>
    /// <param name="topDirectory">顶层目录过滤。</param>
    /// <param name="nameContains">文件名（不含扩展名）包含的子串，不区分大小写。</param>
    public IReadOnlyList<AssetEntry> Query(
        AssetKind? kind = null,
        string? extension = null,
        string? topDirectory = null,
        string? nameContains = null)
    {
        string? ext = extension != null ? NormalizeExtension(extension) : null;

        _lock.EnterReadLock();
        try
        {
            IEnumerable<AssetEntry> query = _entries;

            if (kind.HasValue)
                query = query.Where(e => e.Kind == kind.Value);
            if (ext != null)
                query = query.Where(e => string.Equals(e.Extension, ext, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(topDirectory))
                query = query.Where(e => string.Equals(e.TopDirectory, topDirectory, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(nameContains))
                query = query.Where(e => e.FileNameWithoutExtension.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

            return query.ToArray();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按相对路径（用 / 或 \ 分隔均可）精确获取单个条目，找不到返回 null。</summary>
    public AssetEntry? GetByRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        string norm = relativePath.Replace('\\', '/').TrimStart('/');
        _lock.EnterReadLock();
        try
        {
            return _entries.FirstOrDefault(e =>
                string.Equals(e.RelativePath, norm, StringComparison.OrdinalIgnoreCase));
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// 返回全部缓存条目，按最后修改时间降序排列（最新的在前）。
    /// </summary>
    public IReadOnlyList<AssetEntry> GetAllSortedByModifiedTime()
    {
        _lock.EnterReadLock();
        try
        {
            return _entries.OrderByDescending(e => e.LastModifiedUtc).ToArray();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>读取指定条目的字节内容。</summary>
    public byte[] ReadBytes(AssetEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        return File.ReadAllBytes(entry.FullPath);
    }

    /// <summary>枚举已缓存的全部语义类别及其文件计数（用于概览）。</summary>
    public IReadOnlyDictionary<AssetKind, int> GetKindCounts()
    {
        _lock.EnterReadLock();
        try
        {
            return _byKind.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>枚举已缓存的顶层目录及其文件计数。</summary>
    public IReadOnlyDictionary<string, int> GetTopDirectoryCounts()
    {
        _lock.EnterReadLock();
        try
        {
            return _byTopDirectory.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ---------------------------------------------------------------------
    // 内部实现
    // ---------------------------------------------------------------------

    private void ClearInternal()
    {
        _entries.Clear();
        _byKind.Clear();
        _byExtension.Clear();
        _byTopDirectory.Clear();
        _assetsRoot = "";
        _isLoaded = false;
    }

    private void IndexEntry(AssetEntry entry)
    {
        if (!_byKind.TryGetValue(entry.Kind, out var kindList))
            _byKind[entry.Kind] = kindList = new List<AssetEntry>();
        kindList.Add(entry);

        if (!_byExtension.TryGetValue(entry.Extension, out var extList))
            _byExtension[entry.Extension] = extList = new List<AssetEntry>();
        extList.Add(entry);

        if (!_byTopDirectory.TryGetValue(entry.TopDirectory, out var dirList))
            _byTopDirectory[entry.TopDirectory] = dirList = new List<AssetEntry>();
        dirList.Add(entry);
    }

    private static AssetEntry BuildEntry(string root, string fullPath)
    {
        var info = new FileInfo(fullPath);
        string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        int slash = relative.IndexOf('/');
        string top = slash >= 0 ? relative[..slash] : "";
        string ext = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);

        return new AssetEntry
        {
            FileName = info.Name,
            FileNameWithoutExtension = nameNoExt,
            Extension = ext,
            RelativePath = relative,
            FullPath = fullPath,
            TopDirectory = top,
            Size = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc,
            Kind = ClassifyKind(top, nameNoExt, ext),
        };
    }

    /// <summary>依据顶层目录、文件名前缀和扩展名推断语义类别。</summary>
    private static AssetKind ClassifyKind(string topDir, string nameNoExt, string ext)
    {
        // stage 目录下的 .btl 关卡文件，按文件名前缀细分。
        if (string.Equals(topDir, "stage", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ext, "btl", StringComparison.OrdinalIgnoreCase))
        {
            string name = nameNoExt.ToLowerInvariant();
            if (name.StartsWith("stage")) return AssetKind.Stage;
            if (name.StartsWith("conquest")) return AssetKind.Conquest;
            if (name.StartsWith("event")) return AssetKind.Event;
            if (name.StartsWith("frontier")) return AssetKind.Frontier;
            if (name.StartsWith("generalstage")) return AssetKind.GeneralStage;
            if (name.StartsWith("legend")) return AssetKind.Legend;
            if (name.StartsWith("warzone")) return AssetKind.Warzone;
            if (name.StartsWith("invadecorps")) return AssetKind.InvadeCorps;
            return AssetKind.OtherStageBtl;
        }

        // 通用类型按扩展名归类。
        switch (ext)
        {
            case "png":
            case "webp":
            case "pkm":
            case "jpg":
            case "jpeg":
            case "bmp":
            case "shp":
                return AssetKind.Texture;
            case "xml":
                return AssetKind.Xml;
            case "json":
                return AssetKind.Json;
            case "bin":
                return AssetKind.Binary;
            case "ini":
                return AssetKind.StringTable;
            case "wav":
            case "mp3":
            case "ogg":
            case "m4a":
                return AssetKind.Audio;
            case "ttf":
            case "otf":
            case "fnt":
                return AssetKind.Font;
            case "vsh":
            case "fsh":
            case "glsl":
            case "shader":
                return AssetKind.Shader;
            default:
                return AssetKind.Unknown;
        }
    }

    /// <summary>
    /// 解析 assets 根目录：
    /// - 若传入目录本身名为 assets，直接使用；
    /// - 否则若其下存在 assets 子目录，使用该子目录；
    /// - 否则若其下存在 WC4DATA/assets，使用该路径；
    /// - 都没有则原样返回。
    /// </summary>
    private static string ResolveAssetsRoot(string input)
    {
        string full = Path.GetFullPath(input);

        if (string.Equals(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                "assets", StringComparison.OrdinalIgnoreCase))
            return full;

        string assetsChild = Path.Combine(full, "assets");
        if (Directory.Exists(assetsChild)) return assetsChild;

        string wc4Child = Path.Combine(full, "WC4DATA", "assets");
        if (Directory.Exists(wc4Child)) return wc4Child;

        return full;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "";
        return extension.TrimStart('.').ToLowerInvariant();
    }
}