namespace WC4MapEditor.Core.Commands;

/// <summary>
/// 命令行文本处理的公共实现。
/// <para>
/// 供命令宿主（<see cref="CliCommandHost"/> 的 <c>run</c> 命令）与宿主层
/// （.zme 脚本批量执行）共用，避免两边各写一份注释剥离规则、口径逐渐走偏。
/// </para>
/// </summary>
public static class CommandLineText
{
    /// <summary>整行就是注释（以 <c>;</c>、<c>#</c>、<c>//</c> 开头）——这类行直接跳过</summary>
    public static bool IsCommentLine(string line)
        => line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//");

    /// <summary>
    /// 去掉行尾注释：<c>#</c>、<c>;</c>、<c>//</c> 前面是空白（或位于行首）时，从这里截断。
    /// <para>
    /// 要求"前面是空白"是为了不误伤路径里的符号，
    /// 例如 <c>run "a#b.zme"</c> 里的 # 前面是引号，不会被当成注释。
    /// </para>
    /// </summary>
    public static string StripComment(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            bool isCommentStart = c == '#' || c == ';'
                || (c == '/' && i + 1 < line.Length && line[i + 1] == '/');

            if (!isCommentStart) continue;
            if (i > 0 && !char.IsWhiteSpace(line[i - 1])) continue;

            return line[..i].TrimEnd();
        }

        return line;
    }
}
