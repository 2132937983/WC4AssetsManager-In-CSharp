using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.World;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 为征服文件查找它所基于的世界地图 world{MapNumber}.bin。
/// <para>
/// 查找顺序（离 BTL 越近越优先）：BTL 所在目录 → 上一级目录（官方布局 assets/stage/x.btl 对应 assets/）
/// → 已扫描的 assets 根目录 → 默认 assets 目录 → Maps 文件夹 → 程序目录与其 bin/ → 当前目录与其 bin/、Maps/。
/// 每个目录先找 world{N}.bin；地图编号为 1 时再找 world.bin（官方数据里 assets/world.bin 就是 1 号世界地图）。
/// 只认文件头签名正确的世界地图。
/// </para>
/// <para>
/// 用户在界面上手动选过的底图按地图编号记在本次运行内（<see cref="Remember"/>），
/// 之后打开同编号的征服、切换场景都不再重复询问。
/// </para>
/// </summary>
public static class WorldMapLocator
{
    private static readonly ConcurrentDictionary<int, string> Remembered = new();
    private static readonly Regex WorldNumberPattern = new(@"world(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>查找地图编号对应的世界地图；找不到返回 null。</summary>
    /// <param name="mapNumber">BTL 文件头的 MapNumber（大于 0）</param>
    /// <param name="btlPath">征服文件路径（可空），用于优先在它附近查找</param>
    public static string? Find(int mapNumber, string? btlPath)
    {
        if (mapNumber <= 0) return null;

        if (Remembered.TryGetValue(mapNumber, out var remembered) && IsWorldMap(remembered))
            return remembered;

        foreach (var dir in SearchDirectories(btlPath))
        {
            foreach (var name in CandidateFileNames(mapNumber))
            {
                string candidate = Path.Combine(dir, name);
                if (IsWorldMap(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>记住用户为某个地图编号选定的世界地图（本次运行内有效）。</summary>
    public static void Remember(int mapNumber, string worldMapPath)
    {
        if (mapNumber > 0 && !string.IsNullOrEmpty(worldMapPath))
            Remembered[mapNumber] = Path.GetFullPath(worldMapPath);
    }

    /// <summary>手动挑选世界地图时对话框的起始目录：优先 Maps 文件夹，其次 assets 根目录，再次 BTL 所在目录。</summary>
    public static string? SuggestPickerDirectory(string? btlPath)
    {
        string? maps = ProjectPaths.FindDirectory("Maps");
        if (maps != null) return maps;

        string assets = AssetManager.GetDefaultAssetsPath();
        if (Directory.Exists(assets)) return assets;

        string? btlDir = GetDirectory(btlPath);
        return btlDir != null && Directory.Exists(btlDir) ? btlDir : null;
    }

    /// <summary>
    /// 从世界地图文件名提取地图编号：先按 world(\d+)（标准命名 world1.bin），
    /// 再按 "_" 分段找 1-255 的数字（兼容 xxx_3_yyy.bin 这类命名）；都没有返回 0。
    /// </summary>
    public static int ExtractMapNumber(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        var match = WorldNumberPattern.Match(name);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int number) && number is > 0 and <= 255)
            return number;

        foreach (var part in name.Split('_'))
        {
            if (int.TryParse(part, out int value) && value is > 0 and <= 255)
                return value;
        }
        return 0;
    }

    private static IEnumerable<string> CandidateFileNames(int mapNumber)
    {
        yield return $"world{mapNumber}.bin";
        if (mapNumber == 1) yield return "world.bin";
    }

    private static IEnumerable<string> SearchDirectories(string? btlPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in RawSearchDirectories(btlPath))
        {
            if (dir != null && Directory.Exists(dir) && seen.Add(Path.GetFullPath(dir)))
                yield return dir;
        }
    }

    private static IEnumerable<string?> RawSearchDirectories(string? btlPath)
    {
        string? btlDir = GetDirectory(btlPath);
        if (btlDir != null)
        {
            yield return btlDir;
            yield return Path.GetDirectoryName(btlDir);
        }

        yield return AssetManager.Default.AssetsRoot;
        yield return AssetManager.GetDefaultAssetsPath();
        yield return ProjectPaths.FindDirectory("Maps");

        string baseDir = AppContext.BaseDirectory;
        yield return baseDir;
        yield return Path.Combine(baseDir, "bin");

        string cwd = Environment.CurrentDirectory;
        yield return cwd;
        yield return Path.Combine(cwd, "bin");
        yield return Path.Combine(cwd, "Maps");
    }

    /// <summary>文件所在目录的绝对路径；路径为空或非法时返回 null。</summary>
    private static string? GetDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsWorldMap(string path) => WorldParser.TryReadHeader(path, out _, out _);
}
