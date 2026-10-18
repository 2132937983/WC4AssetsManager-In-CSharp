namespace WC4MapEditor.Core.Helpers;

/// <summary>
/// 目录/文件的统一探测规则。
/// <para>
/// 起点固定为 <see cref="AppContext.BaseDirectory"/>（exe 所在目录，与启动时的工作目录无关），
/// 逐级向上最多 <see cref="DefaultMaxDepth"/> 层。顺序即优先级：
/// exe 同级 → 上一级 → 更上层，取最近的一份。
/// </para>
/// <para>
/// 这套规则同时覆盖两种部署形态：
/// exe 同级放 Resource（最常见），以及 Resource 与软件目录同级（放在上一级）。
/// </para>
/// </summary>
public static class ProjectPaths
{
    /// <summary>向上搜索的最大层数（防止误命中磁盘根目录等离谱位置）</summary>
    public const int DefaultMaxDepth = 8;

    /// <summary>从 exe 目录向上查找第一个存在的目录。找不到返回 null。</summary>
    /// <param name="relativePath">相对路径，可多级，如 <c>Resource/WC4DATA/assets</c></param>
    public static string? FindDirectory(string relativePath, int maxDepth = DefaultMaxDepth)
        => FindDirectoryFrom(AppContext.BaseDirectory, relativePath, maxDepth);

    /// <summary>从指定目录向上查找第一个存在的目录。找不到返回 null。</summary>
    public static string? FindDirectoryFrom(string? startDirectory, string relativePath, int maxDepth = DefaultMaxDepth)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return null;

        string? current = startDirectory;
        for (int i = 0; i < maxDepth && !string.IsNullOrEmpty(current); i++)
        {
            string candidate = Path.Combine(current, relativePath);
            if (Directory.Exists(candidate)) return candidate;
            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>从 exe 目录向上查找第一个存在的文件。找不到返回 null。</summary>
    /// <param name="relativePath">相对路径，如 <c>setting.txt</c></param>
    public static string? FindFile(string relativePath, int maxDepth = DefaultMaxDepth)
    {
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < maxDepth && !string.IsNullOrEmpty(current); i++)
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate)) return candidate;
            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
