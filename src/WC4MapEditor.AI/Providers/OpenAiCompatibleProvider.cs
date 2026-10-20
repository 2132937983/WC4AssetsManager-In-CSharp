using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WC4MapEditor.AI.Settings;

namespace WC4MapEditor.AI.Providers;

/// <summary>
/// OpenAI 兼容协议 Provider（/chat/completions + tools + tool_calls）。
/// 适用于 OpenAI / DeepSeek / Kimi / Qwen / Ollama / vLLM 等一切兼容端点。
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly AiSettings _settings;

    public string ProviderName => "openai";

    public OpenAiCompatibleProvider(AiSettings settings) => _settings = settings;

    public async Task<AiChatResult> SendAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct = default,
        Action<string>? onTextDelta = null,
        Action<string>? onRetry = null)
    {
        try
        {
            string url = BuildUrl(_settings.BaseUrl, "chat/completions");

            var body = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["temperature"] = _settings.Temperature,
                ["messages"] = messages.Select(ToApiMessage).ToList(),
            };

            if (tools.Count > 0)
            {
                body["tools"] = tools.Select(t => new Dictionary<string, object?>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = JsonDocument.Parse(t.ParametersJson).RootElement.Clone(),
                    }
                }).ToList();
            }

            // 推理开关与强度：都只在明确设置时才发（不认识这些字段的模型/网关可能直接 400）
            string reasoningMode = (_settings.ReasoningMode ?? "default").Trim().ToLowerInvariant();
            string effort = (_settings.ReasoningEffort ?? "default").Trim().ToLowerInvariant();

            if (reasoningMode == "off")
                body["enable_thinking"] = false;      // 关：Qwen3 / 部分网关通用的关闭写法
            else if (reasoningMode == "on")
                body["enable_thinking"] = true;       // 开
            else if (effort is "low" or "medium" or "high")
                body["reasoning_effort"] = effort;    // 只给强度、不碰开关

            bool stream = onTextDelta != null;
            if (stream) body["stream"] = true;

            // 请求体只序列化一次；每次重试都新建 HttpRequestMessage（发过的请求不能重用）
            string payload = JsonSerializer.Serialize(body);
            bool emitted = false;   // 流式已经吐过内容就不能再重试，否则气泡里会出现重复内容

            return await RetryPolicy.ExecuteAsync(
                async (_, token) =>
                {
                    emitted = false;
                    using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                    ApplyClientHeaders(req, _settings, stream ? "text/event-stream" : "application/json");

                    return stream
                        ? await SendStreamAsync(req, delta => { emitted = true; onTextDelta!(delta); }, token)
                            .ConfigureAwait(false)
                        : await ParseResponseAsync(req, token).ConfigureAwait(false);
                },
                canRetry: _ => !emitted,
                onRetry: onRetry,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new AiChatResult { Error = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    /// <summary>非流式请求：发一次并解析 choices / tool_calls。</summary>
    private static async Task<AiChatResult> ParseResponseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return new AiChatResult { Error = $"HTTP {(int)resp.StatusCode}: {Truncate(respText, 600)}" };

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
            return new AiChatResult { Error = Truncate(err.ToString(), 600) };

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new AiChatResult { Error = "响应无 choices" };
        var msg = choices[0].GetProperty("message");

        var result = new AiChatResult
        {
            Text = msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? "" : ""
        };

        // 推理内容（非流式）：deepseek-reasoner 返回 reasoning_content
        if (msg.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            result.Reasoning = rc.GetString();
        else if (msg.TryGetProperty("reasoning", out var r2) && r2.ValueKind == JsonValueKind.String)
            result.Reasoning = r2.GetString();

        if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                string id = tc.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "";
                string name = "";
                string args = "{}";
                if (tc.TryGetProperty("function", out var fn))
                {
                    if (fn.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                    if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                        args = a.GetString() ?? "{}";
                }
                if (!string.IsNullOrEmpty(name))
                    result.ToolCalls.Add(new AiToolCall { Id = id, Name = name, ArgumentsJson = args });
            }
        }

        return result;
    }

    /// <summary>OpenAI SSE 流式解析：data: {json}\n\n 格式，delta.content 为文本增量，delta.tool_calls 为工具调用增量。
    /// 若 API 不支持流式返回普通 JSON，自动回退到非流式解析。</summary>
    private async Task<AiChatResult> SendStreamAsync(
        HttpRequestMessage req,
        Action<string> onTextDelta,
        CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new AiChatResult { Error = $"HTTP {(int)resp.StatusCode}: {Truncate(errText, 600)}" };
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var result = new AiChatResult();
        var textBuf = new StringBuilder();
        var reasoningBuf = new StringBuilder();
        var toolArgs = new Dictionary<int, StringBuilder>();
        var toolMeta = new Dictionary<int, (string id, string name)>();
        bool isSse = false;
        var nonSseBuf = new StringBuilder();
        string? finishReason = null;

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (!isSse)
            {
                if (line.Length == 0) continue;
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    isSse = true;
                }
                else
                {
                    nonSseBuf.AppendLine(line);
                    continue;
                }
            }

            if (line.Length == 0 || line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line["data:".Length..].TrimStart();
            if (data == "[DONE]") break;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(data); }
            catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    result.Error = Truncate(err.ToString(), 600);
                    return result;
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;

                // finish_reason=length 表示输出被截断；带工具调用时 arguments 必然是半截 JSON
                if (choices[0].TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();

                var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;

                if (delta.ValueKind == JsonValueKind.Object)
                {
                    if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        string chunk = c.GetString() ?? "";
                        textBuf.Append(chunk);
                        onTextDelta(chunk);
                    }

                    // 推理内容（思维链）：deepseek-reasoner / vLLM 用 reasoning_content，
                    // 有些网关叫 reasoning —— 都不算正文，单独攒起来给界面显示。
                    if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                        reasoningBuf.Append(rc.GetString());
                    else if (delta.TryGetProperty("reasoning", out var r2) && r2.ValueKind == JsonValueKind.String)
                        reasoningBuf.Append(r2.GetString());

                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            int idx = tc.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                            if (!toolArgs.ContainsKey(idx))
                            {
                                toolArgs[idx] = new StringBuilder();
                                toolMeta[idx] = ("", "");
                            }

                            if (tc.TryGetProperty("id", out var tid))
                            {
                                var meta = toolMeta[idx];
                                toolMeta[idx] = (tid.GetString() ?? meta.id, meta.name);
                            }

                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var n))
                                {
                                    var meta = toolMeta[idx];
                                    toolMeta[idx] = (meta.id, n.GetString() ?? meta.name);
                                }
                                if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                                    toolArgs[idx].Append(a.GetString() ?? "");
                            }
                        }
                    }
                }
            }
        }

        if (!isSse && nonSseBuf.Length > 0)
        {
            var fallback = ParseNonStreamResponse(nonSseBuf.ToString());
            if (!string.IsNullOrEmpty(fallback.Text))
                onTextDelta(fallback.Text);
            return fallback;
        }

        result.Text = textBuf.ToString();
        if (reasoningBuf.Length > 0) result.Reasoning = reasoningBuf.ToString();

        foreach (var idx in toolArgs.Keys.OrderBy(k => k))
        {
            var (id, name) = toolMeta[idx];
            if (!string.IsNullOrEmpty(name))
                result.ToolCalls.Add(new AiToolCall { Id = id, Name = name, ArgumentsJson = toolArgs[idx].ToString() });
        }

        // 被 max_tokens 截断的工具调用参数一定不完整：直接判本轮失败，
        // 别把一个半截 JSON 交给执行器（否则只会得到"缺少 code 参数"这种误导信息）。
        if (finishReason == "length" && result.ToolCalls.Count > 0)
        {
            result.Error = "本轮输出被截断（finish_reason=length），工具调用参数不完整。"
                         + "请把长代码/长参数拆成多段后重试。";
        }

        return result;
    }

    /// <summary>非流式 JSON 响应解析（流式回退用）。</summary>
    private AiChatResult ParseNonStreamResponse(string respText)
    {
        try
        {
            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return new AiChatResult { Error = Truncate(err.ToString(), 600) };

            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return new AiChatResult { Error = "响应无 choices" };
            var msg = choices[0].GetProperty("message");

            var result = new AiChatResult
            {
                Text = msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                    ? content.GetString() ?? "" : ""
            };

            if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    string id = tc.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "";
                    string name = "";
                    string args = "{}";
                    if (tc.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                        if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                            args = a.GetString() ?? "{}";
                    }
                    if (!string.IsNullOrEmpty(name))
                        result.ToolCalls.Add(new AiToolCall { Id = id, Name = name, ArgumentsJson = args });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            return new AiChatResult { Error = $"流式回退解析失败: {ex.Message}" };
        }
    }

    /// <summary>
    /// 拉取可用模型列表（GET {BaseUrl}/v1/models）。
    /// <para>
    /// 认证头**两种写法各试一次**：标准是 <c>Authorization: Bearer</c>，但中转网关有的只认
    /// <c>x-api-key</c>，用错就回 401（常见文案就是"不支持的客户端"）。这不属于标准行为，
    /// 但代价只有一次额外请求，比让人对着 401 猜要划算。
    /// </para>
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        string url = BuildUrl(_settings.BaseUrl, "models");

        var attempts = new (string Label, Action<HttpRequestMessage> Apply)[]
        {
            ("Authorization: Bearer", r => r.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey)),
            ("x-api-key", r => r.Headers.TryAddWithoutValidation("x-api-key", _settings.ApiKey)),
        };

        var log = new List<string>();
        int firstStatus = 0;
        string firstBody = "";

        foreach (var (label, apply) in attempts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            apply(req);
            ApplyClientHeaders(req, _settings);

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            string respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
                return ParseModelList(respText);

            // 只留第一次（标准写法）的响应原文：那才是网关对"正规请求"的说法，
            // 第二次的文案往往只是换了个头之后的另一种抱怨，反而会盖掉线索。
            if (firstStatus == 0) { firstStatus = (int)resp.StatusCode; firstBody = respText; }
            log.Add($"{label} → HTTP {(int)resp.StatusCode}");

            // 只有"认证被拒"才值得换个头再试：404（网关根本没这个路径）、5xx 换什么都没用
            if ((int)resp.StatusCode is not (401 or 403)) break;
        }

        throw new InvalidOperationException(
            $"HTTP {firstStatus}: {Truncate(firstBody, 400)}　（地址 {url}；试过 {string.Join("、", log)}）");
    }

    /// <summary>解析 OpenAI 风格的模型列表：<c>{"data":[{"id":"…"},…]}</c>。</summary>
    internal static List<string> ParseModelList(string respText)
    {
        using var doc = JsonDocument.Parse(respText);
        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in arr.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    models.Add(id.GetString() ?? "");
            }
        }
        models.RemoveAll(string.IsNullOrEmpty);
        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    private static Dictionary<string, object?> ToApiMessage(AiChatMessage m)
    {
        var dict = new Dictionary<string, object?> { ["role"] = m.Role };

        switch (m.Role)
        {
            case "tool":
                dict["tool_call_id"] = m.ToolCallId ?? "";
                dict["content"] = m.Text ?? "";
                break;

            case "assistant" when m.ToolCalls is { Count: > 0 }:
                // content 允许为空串，但不要给 null：部分网关会因此判请求非法
                dict["content"] = m.Text ?? "";
                dict["tool_calls"] = m.ToolCalls.Select(tc => new Dictionary<string, object?>
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = tc.Name,
                        // 历史里的坏参数（多为被截断的 JSON）必须在这里降级，否则整段对话每轮 400
                        ["arguments"] = tc.NormalizedArgumentsJson
                    }
                }).ToList();
                break;

            default:
                if (m.Images is { Count: > 0 })
                {
                    // 多模态消息：content 用数组，图片按 data URL 内联
                    var parts = new List<Dictionary<string, object?>>();
                    if (!string.IsNullOrEmpty(m.Text))
                        parts.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = m.Text });
                    foreach (var img in m.Images)
                    {
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object?>
                            {
                                ["url"] = $"data:{img.MimeType};base64,{img.Base64}"
                            }
                        });
                    }
                    dict["content"] = parts;
                }
                else
                {
                    dict["content"] = m.Text ?? "";
                }
                break;
        }

        return dict;
    }

    /// <summary>
    /// 给请求加"客户端标识（User-Agent）"与 Accept。
    /// <para>
    /// .NET 的 <see cref="HttpClient"/> **默认不发 User-Agent**，而有些中转网关正是靠它判断客户端，
    /// 认不出来就回 401（文案常写"不支持的客户端"）。要什么标识各家不同
    /// （Claude Code 系中转常见要求 <c>claude-cli/…</c>），所以做成设置项；留空就不发。
    /// </para>
    /// </summary>
    internal static void ApplyClientHeaders(
        HttpRequestMessage req, AiSettings settings, string accept = "application/json")
    {
        req.Headers.TryAddWithoutValidation("Accept", accept);

        string ua = (settings.UserAgent ?? "").Trim();
        if (ua.Length > 0)
            req.Headers.TryAddWithoutValidation("User-Agent", ua);   // 非标准写法只能用这个重载
    }

    internal static string BuildUrl(string baseUrl, string path)
    {
        baseUrl = baseUrl.TrimEnd('/');
        // 已带 /v1 的用户配置不再追加
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{baseUrl}/{path}";
        return $"{baseUrl}/v1/{path}";
    }

    internal static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
