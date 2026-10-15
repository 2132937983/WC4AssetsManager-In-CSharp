using System.Diagnostics;

namespace WC4MapEditor.Core.ErrorHandling;

/// <summary>
/// 错误严重级别
/// </summary>
public enum ErrorSeverity
{
    /// <summary>警告，不影响功能</summary>
    Warning,
    /// <summary>错误，功能受限但程序可继续</summary>
    Error,
    /// <summary>严重错误，需要回退操作</summary>
    Critical,
    /// <summary>致命错误，程序可能不稳定</summary>
    Fatal
}

/// <summary>
/// 错误记录
/// </summary>
public sealed class ErrorRecord
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.Now;
    public ErrorSeverity Severity { get; }
    public string Message { get; }
    public string? Source { get; }
    public Exception? Exception { get; }
    public string? StackTrace { get; }
    public Dictionary<string, object> Context { get; } = new();

    public ErrorRecord(ErrorSeverity severity, string message, string? source = null, Exception? exception = null)
    {
        Severity = severity;
        Message = message;
        Source = source;
        Exception = exception;
        StackTrace = exception?.StackTrace ?? new StackTrace(2, true).ToString();
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{Timestamp:HH:mm:ss.fff}] [{Severity}] {Message}");
        if (!string.IsNullOrEmpty(Source))
            sb.AppendLine($"  Source: {Source}");
        if (Exception != null)
            sb.AppendLine($"  Exception: {Exception.GetType().Name}: {Exception.Message}");
        if (Context.Count > 0)
        {
            sb.AppendLine("  Context:");
            foreach (var kv in Context)
                sb.AppendLine($"    {kv.Key} = {kv.Value}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// 错误处理结果
/// </summary>
public readonly struct ErrorHandleResult
{
    public bool Recovered { get; }
    public string? RecoveryAction { get; }
    public ErrorRecord? OriginalError { get; }

    public ErrorHandleResult(bool recovered, string? recoveryAction = null, ErrorRecord? originalError = null)
    {
        Recovered = recovered;
        RecoveryAction = recoveryAction;
        OriginalError = originalError;
    }

    public static ErrorHandleResult Success(string action) => new(true, action);
    public static ErrorHandleResult Failed(ErrorRecord error) => new(false, null, error);
}

/// <summary>
/// 错误恢复策略
/// </summary>
public interface IRecoveryStrategy
{
    string Name { get; }
    bool CanHandle(ErrorRecord error);
    Task<ErrorHandleResult> RecoverAsync(ErrorRecord error);
}

/// <summary>
/// 错误状态收集器 - 集中管理错误检测、收集和恢复
/// </summary>
public sealed class ErrorCollector
{
    private static readonly object _lock = new();
    private static ErrorCollector? _instance;

    public static ErrorCollector Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ErrorCollector();
                }
            }
            return _instance;
        }
    }

    private readonly List<ErrorRecord> _errors = new();
    private readonly List<IRecoveryStrategy> _strategies = new();
    private readonly int _maxErrorHistory = 100;

    public IReadOnlyList<ErrorRecord> Errors => _errors.AsReadOnly();
    public bool HasErrors => _errors.Count > 0;
    public bool HasCriticalErrors => _errors.Any(e => e.Severity >= ErrorSeverity.Critical);
    public int ErrorCount => _errors.Count;

    public event EventHandler<ErrorRecord>? ErrorOccurred;
    public event EventHandler<ErrorHandleResult>? ErrorRecovered;
    public event EventHandler? ErrorsCleared;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public ErrorCollector() { }

    /// <summary>
    /// 注册恢复策略
    /// </summary>
    public void RegisterStrategy(IRecoveryStrategy strategy)
    {
        lock (_lock)
        {
            if (!_strategies.Contains(strategy))
                _strategies.Add(strategy);
        }
    }

    /// <summary>
    /// 注销恢复策略
    /// </summary>
    public void UnregisterStrategy(IRecoveryStrategy strategy)
    {
        lock (_lock)
        {
            _strategies.Remove(strategy);
        }
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public ErrorRecord RecordError(ErrorSeverity severity, string message, string? source = null, Exception? exception = null)
    {
        var record = new ErrorRecord(severity, message, source, exception);
        lock (_lock)
        {
            _errors.Add(record);
            // 限制历史记录数量
            if (_errors.Count > _maxErrorHistory)
                _errors.RemoveAt(0);
        }
        ErrorOccurred?.Invoke(this, record);
        Debug.WriteLine($"[ErrorCollector] {record}");
        return record;
    }

    /// <summary>
    /// 尝试执行操作，发生错误时自动记录并尝试恢复
    /// </summary>
    public async Task<(bool success, T? result, ErrorRecord? error)> TryExecuteAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        ErrorSeverity severity = ErrorSeverity.Error)
    {
        try
        {
            var result = await operation();
            return (true, result, null);
        }
        catch (Exception ex)
        {
            var error = RecordError(severity, $"操作失败: {operationName}", operationName, ex);
            var recovery = await TryRecoverAsync(error);
            if (recovery.Recovered)
            {
                ErrorRecovered?.Invoke(this, recovery);
                return (false, default, error);
            }
            return (false, default, error);
        }
    }

    /// <summary>
    /// 尝试执行操作（同步版本）
    /// </summary>
    public (bool success, T? result, ErrorRecord? error) TryExecute<T>(
        Func<T> operation,
        string operationName,
        ErrorSeverity severity = ErrorSeverity.Error)
    {
        try
        {
            var result = operation();
            return (true, result, null);
        }
        catch (Exception ex)
        {
            var error = RecordError(severity, $"操作失败: {operationName}", operationName, ex);
            var recovery = TryRecover(error);
            if (recovery.Recovered)
            {
                ErrorRecovered?.Invoke(this, recovery);
                return (false, default, error);
            }
            return (false, default, error);
        }
    }

    /// <summary>
    /// 尝试恢复错误
    /// </summary>
    public async Task<ErrorHandleResult> TryRecoverAsync(ErrorRecord error)
    {
        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandle(error))
            {
                try
                {
                    var result = await strategy.RecoverAsync(error);
                    if (result.Recovered)
                        return result;
                }
                catch (Exception ex)
                {
                    RecordError(ErrorSeverity.Error, $"恢复策略 '{strategy.Name}' 失败", strategy.Name, ex);
                }
            }
        }
        return ErrorHandleResult.Failed(error);
    }

    /// <summary>
    /// 尝试恢复错误（同步版本）
    /// </summary>
    public ErrorHandleResult TryRecover(ErrorRecord error)
    {
        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandle(error))
            {
                try
                {
                    var task = strategy.RecoverAsync(error);
                    task.Wait();
                    if (task.Result.Recovered)
                        return task.Result;
                }
                catch (Exception ex)
                {
                    RecordError(ErrorSeverity.Error, $"恢复策略 '{strategy.Name}' 失败", strategy.Name, ex);
                }
            }
        }
        return ErrorHandleResult.Failed(error);
    }

    /// <summary>
    /// 清除所有错误记录
    /// </summary>
    public void ClearErrors()
    {
        lock (_lock)
        {
            _errors.Clear();
        }
        ErrorsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 获取最近的错误
    /// </summary>
    public ErrorRecord? GetLastError()
    {
        lock (_lock)
        {
            return _errors.LastOrDefault();
        }
    }

    /// <summary>
    /// 获取指定级别的错误
    /// </summary>
    public IReadOnlyList<ErrorRecord> GetErrorsBySeverity(ErrorSeverity severity)
    {
        lock (_lock)
        {
            return _errors.Where(e => e.Severity == severity).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// 获取错误摘要
    /// </summary>
    public string GetErrorSummary()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"错误统计: 总计 {_errors.Count}");
            foreach (var g in _errors.GroupBy(e => e.Severity).OrderByDescending(g => g.Key))
            {
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
            if (_errors.Count > 0)
            {
                sb.AppendLine("最近错误:");
                foreach (var e in _errors.TakeLast(5))
                    sb.AppendLine($"  [{e.Severity}] {e.Message}");
            }
            return sb.ToString();
        }
    }
}