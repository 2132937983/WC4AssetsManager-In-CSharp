namespace WC4MapEditor.Scripting;

/// <summary>
/// 脚本调用数据操作的返回结果。
/// </summary>
public readonly struct ScriptOperationResult
{
    public bool Success { get; }
    public string Message { get; }
    public int AffectedCount { get; }

    public ScriptOperationResult(bool success, string message = "", int affectedCount = 0)
    {
        Success = success;
        Message = message;
        AffectedCount = affectedCount;
    }

    public static ScriptOperationResult Ok(string message = "", int affectedCount = 0)
        => new(true, message, affectedCount);

    public static ScriptOperationResult Fail(string message)
        => new(false, message);

    public override string ToString() => Message;
}
