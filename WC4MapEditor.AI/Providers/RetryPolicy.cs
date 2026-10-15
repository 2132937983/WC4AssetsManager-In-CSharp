using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace WC4MapEditor.AI.Providers;

/// <summary>
/// 请求重试策略：只重试"自己可能会好"的失败。
/// <para>
/// 会重试：网络异常、超时、408/409/425/429、5xx，以及中转网关常见的
/// <c>get_channel_failed</c>（分组下没有可用渠道）、overloaded、无可用渠道之类 —— 这些过一会儿
/// 换条渠道往往就成了。
/// </para>
/// <para>
/// 不重试：400/401/403/404 这种确定性错误（模型名写错、Key 无效、余额不足），
/// 重试五次只是白等 20 多秒，还是一样的报错。
/// </para>
/// </summary>
internal static class RetryPolicy
{
    /// <summary>重试次数：首次请求之外再重试这么多次，即最多 6 次请求</summary>
    public const int MaxRetries = 5;

    /// <summary>每次重试前的等待秒数（第 1~5 次重试依次取用）</summary>
    private static readonly double[] DelaySeconds = { 1, 2, 4, 8, 8 };

    /// <summary>错误正文里出现这些词就按"可重试"处理（各家网关措辞不统一）</summary>
    private static readonly string[] TransientHints =
    {
        "get_channel_failed", "channel", "无可用", "没有可用", "no available", "temporarily",
        "暂时", "overloaded", "overload", "rate limit", "too many requests", "timeout",
        "timed out", "upstream", "bad gateway", "service unavailable", "try again", "retry",
        "system busy", "internal error", "网关",
    };

    public static bool IsTransientStatus(int status)
        => status is 408 or 409 or 425 or 429 || status is >= 500 and <= 599;

    public static bool IsTransientBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        foreach (var hint in TransientHints)
        {
            if (body.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 异常是否值得重试。走到这里的 <see cref="OperationCanceledException"/> 已经排除了
    /// "用户点了停止"（那种在循环里直接抛出），所以按超时算。
    /// </summary>
    public static bool IsTransientException(Exception ex) => ex switch
    {
        OperationCanceledException => true,
        HttpRequestException => true,
        IOException => true,
        System.Net.Sockets.SocketException => true,
        _ => false,
    };

    /// <summary>错误结果是否值得重试：先看 <c>HTTP xxx</c> 状态码，没有状态码再看关键词</summary>
    public static bool IsTransient(AiChatResult result)
    {
        if (!result.HasError) return false;

        var match = Regex.Match(result.Error!, @"\bHTTP\s+(\d{3})\b");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int status))
            return IsTransientStatus(status);

        return IsTransientBody(result.Error);
    }

    /// <summary>把错误压成一句短的，用于重试提示（优先取网关 JSON 里的 code，最直观）</summary>
    public static string ShortReason(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "未知错误";

        var code = Regex.Match(error!, "\"code\"\\s*:\\s*\"([^\"]+)\"");
        if (code.Success) return code.Groups[1].Value;

        var oneLine = error!.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine[..60] + "…";
    }

    /// <summary>
    /// 反复执行 <paramref name="attempt"/>，直到成功、或失败且不该重试、或重试次数用尽。
    /// <para>
    /// <paramref name="attempt"/> 每次都会被调用，它必须**新建请求**（发送过的 HttpRequestMessage 不能重用）。
    /// </para>
    /// <para>
    /// <paramref name="canRetry"/> 是"额外否决权"：默认仍按 <see cref="IsTransient"/> 判断，
    /// 它再补一刀。流式请求用它来禁止"已经吐过内容还重试"（否则气泡里会出现重复内容）。
    /// </para>
    /// </summary>
    public static async Task<AiChatResult> ExecuteAsync(
        Func<int, CancellationToken, Task<AiChatResult>> attempt,
        Func<AiChatResult, bool>? canRetry = null,
        Action<string>? onRetry = null,
        CancellationToken ct = default)
    {
        AiChatResult last = new() { Error = "请求未执行" };

        for (int i = 0; i <= MaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            bool retryable;
            try
            {
                last = await attempt(i, ct).ConfigureAwait(false);
                retryable = IsTransient(last) && (canRetry?.Invoke(last) ?? true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;                                        // 用户点了停止：不重试
            }
            catch (Exception ex)
            {
                last = new AiChatResult { Error = $"{ex.GetType().Name}: {ex.Message}" };
                retryable = IsTransientException(ex) && (canRetry?.Invoke(last) ?? true);
            }

            if (!retryable) return last;

            if (i < MaxRetries)
            {
                double seconds = DelaySeconds[Math.Min(i, DelaySeconds.Length - 1)];
                onRetry?.Invoke($"请求失败（{ShortReason(last.Error)}），{seconds:0} 秒后重试 {i + 1}/{MaxRetries}…");
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
            }
        }

        last.Error = $"（已重试 {MaxRetries} 次仍失败）{last.Error}";
        return last;
    }
}
