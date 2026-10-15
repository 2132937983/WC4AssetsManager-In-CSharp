namespace WC4MapEditor.Scripting;

/// <summary>
/// 一次脚本执行的结果。
/// </summary>
public sealed class ScriptResult
{
    /// <summary>是否成功（语法错误、运行时错误都算失败）</summary>
    public bool Success { get; init; }

    /// <summary>错误信息（成功时为空）</summary>
    public string Error { get; init; } = "";

    /// <summary>错误发生的位置描述（如 "script.lua:12"）</summary>
    public string ErrorLocation { get; init; } = "";

    /// <summary>脚本里 print / log 产生的输出行</summary>
    public IReadOnlyList<string> Output { get; init; } = Array.Empty<string>();

    /// <summary>执行耗时</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>执行过程中数据操作的总影响条数</summary>
    public int AffectedCount { get; init; }

    public static ScriptResult Fail(string error, string location = "") => new()
    {
        Success = false,
        Error = error,
        ErrorLocation = location
    };
}
