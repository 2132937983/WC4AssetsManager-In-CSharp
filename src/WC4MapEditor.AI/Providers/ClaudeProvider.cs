using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WC4MapEditor.AI.Settings;

namespace WC4MapEditor.AI.Providers;

/// <summary>
/// Anthropic Claude 协议 Provider（/v1/messages + tool_use / tool_result）。
/// 与 OpenAI 协议差异：system 是顶层字段、工具结果作为 user 消息的 content block、
/// 工具 schema 字段名为 input_schema。
/// </summary>
public sealed class ClaudeProvider : IAiProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly AiSettings _settings;

    public string ProviderName => "claude";

    public ClaudeProvider(AiSettings settings) => _settings = settings;

    public async Task<AiChatResult> SendAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct = default,
        Action<string>? onTextDelta = null,
        Action<string>? onRetry = null)
    {
        try
        {
            string url = BuildUrl(_settings.BaseUrl);

            // Claude 无 system 角色：抽出来放顶层
            string systemText = string.Join("\n\n",
                messages.Where(m => m.Role == "system").Select(m => m.Text ?? ""));

            var apiMessages = BuildApiMessages(messages);

            // 推理强度：Claude 用 thinking.budget_tokens 表达（预算要算进 max_tokens）。
            // 注意开启 thinking 时不能再传 temperature（Anthropic 要求默认温度），所以这时省掉它。
            int maxTokens = 4096;
            Dictionary<string, object?>? thinking = null;
            string reasoningMode = (_settings.ReasoningMode ?? "default").Trim().ToLowerInvariant();
            string effort = (_settings.ReasoningEffort ?? "default").Trim().ToLowerInvariant();
            bool effortSet = effort is "low" or "medium" or "high";

            if (reasoningMode == "off")
            {
                thinking = new Dictionary<string, object?> { ["type"] = "disabled" };
            }
            else if (reasoningMode == "on" || effortSet)
            {
                // 开了思考：预算按强度给（没给强度就当中等）
                int budget = effort switch { "low" => 2048, "high" => 16384, _ => 8192 };
                thinking = new Dictionary<string, object?> { ["type"] = "enabled", ["budget_tokens"] = budget };
                maxTokens = budget + 4096;
            }

            var body = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["max_tokens"] = maxTokens,
                ["messages"] = apiMessages,
            };
            if (thinking != null) body["thinking"] = thinking;
            else body["temperature"] = _settings.Temperature;
            if (!string.IsNullOrEmpty(systemText))
                body["system"] = systemText;

            if (tools.Count > 0)
            {
                body["tools"] = tools.Select(t => new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = JsonDocument.Parse(t.ParametersJson).RootElement.Clone()
                }).ToList();
            }

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
                    req.Headers.TryAddWithoutValidation("x-api-key", _settings.ApiKey);
                    req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    OpenAiCompatibleProvider.ApplyClientHeaders(
                        req, _settings, stream ? "text/event-stream" : "application/json");

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

    /// <summary>非流式请求：发一次并解析 content blocks / tool_use。</summary>
    private static async Task<AiChatResult> ParseResponseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return new AiChatResult { Error = $"HTTP {(int)resp.StatusCode}: {OpenAiCompatibleProvider.Truncate(respText, 600)}" };

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
            return new AiChatResult { Error = OpenAiCompatibleProvider.Truncate(err.ToString(), 600) };

        var result = new AiChatResult();
        if (root.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                string type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "text" && block.TryGetProperty("text", out var tx))
                    textParts.Add(tx.GetString() ?? "");
                else if (type == "tool_use")
                {
                    result.ToolCalls.Add(new AiToolCall
                    {
                        Id = block.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Name = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        ArgumentsJson = block.TryGetProperty("input", out var input)
                            ? input.GetRawText() : "{}"
                    });
                }
            }
            result.Text = string.Join("\n", textParts);
        }

        if (root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String)
        {
            var reason = stop.GetString();
            // 有工具调用时被截断，意味着 arguments 是半截 JSON —— 必须判失败，不能交给执行器
            if (reason == "max_tokens")
                result.Error = result.ToolCalls.Count > 0
                    ? "响应被 max_tokens 截断：工具调用参数不完整（不是合法 JSON），请把长内容拆成多段后重试"
                    : "响应被 max_tokens 截断";
        }

        return result;
    }

    /// <summary>Claude SSE 流式解析：event: {type}\ndata: {json}\n\n 格式。
    /// text_delta 为文本增量，input_json_delta 为工具参数增量。</summary>
    private async Task<AiChatResult> SendStreamAsync(
        HttpRequestMessage req,
        Action<string> onTextDelta,
        CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new AiChatResult { Error = $"HTTP {(int)resp.StatusCode}: {OpenAiCompatibleProvider.Truncate(errText, 600)}" };
        }

        var result = new AiChatResult();
        var textBuf = new StringBuilder();
        var reasoningBuf = new StringBuilder();
        var toolInput = new Dictionary<int, StringBuilder>();
        var toolMeta = new Dictionary<int, (string id, string name)>();
        bool isSse = false;
        bool maxTokensReached = false;
        var nonSseBuf = new StringBuilder();

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (!isSse)
            {
                if (line.Length == 0) continue;
                if (line.StartsWith("data:", StringComparison.Ordinal) || line.StartsWith("event:", StringComparison.Ordinal))
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
            if (line.StartsWith("event:", StringComparison.Ordinal)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line["data:".Length..].TrimStart();

            JsonDocument doc;
            try { doc = JsonDocument.Parse(data); }
            catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                string? type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                switch (type)
                {
                    case "error":
                        result.Error = OpenAiCompatibleProvider.Truncate(
                            root.TryGetProperty("error", out var e) ? e.ToString() : "未知错误", 600);
                        return result;

                    case "content_block_start":
                        if (root.TryGetProperty("index", out var idx) && root.TryGetProperty("content_block", out var cb))
                        {
                            int i = idx.GetInt32();
                            string? bt = cb.TryGetProperty("type", out var btt) ? btt.GetString() : null;
                            if (bt == "tool_use")
                            {
                                toolInput[i] = new StringBuilder();
                                toolMeta[i] = (
                                    cb.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "",
                                    cb.TryGetProperty("name", out var bn) ? bn.GetString() ?? "" : ""
                                );
                            }
                        }
                        break;

                    case "content_block_delta":
                        if (root.TryGetProperty("delta", out var delta))
                        {
                            string? dt = delta.TryGetProperty("type", out var dtt) ? dtt.GetString() : null;
                            if (dt == "text_delta" && delta.TryGetProperty("text", out var tx))
                            {
                                string chunk = tx.GetString() ?? "";
                                textBuf.Append(chunk);
                                onTextDelta(chunk);
                            }
                            else if (dt == "thinking_delta" && delta.TryGetProperty("thinking", out var th))
                            {
                                // 扩展思考（Claude 的思维链）：单独攒着，给界面显示用
                                reasoningBuf.Append(th.GetString() ?? "");
                            }
                            else if (dt == "input_json_delta" && delta.TryGetProperty("partial_json", out var pj))
                            {
                                if (root.TryGetProperty("index", out var di))
                                {
                                    int i = di.GetInt32();
                                    if (toolInput.TryGetValue(i, out var buf))
                                        buf.Append(pj.GetString() ?? "");
                                }
                            }
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("delta", out var md) &&
                            md.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String)
                        {
                            // 这里工具调用还没组装完，先记下来，等循环结束后再按有无 tool_use 决定文案
                            if (sr.GetString() == "max_tokens") maxTokensReached = true;
                        }
                        break;
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

        foreach (var idx in toolInput.Keys.OrderBy(k => k))
        {
            var (id, name) = toolMeta[idx];
            if (!string.IsNullOrEmpty(name))
                result.ToolCalls.Add(new AiToolCall { Id = id, Name = name, ArgumentsJson = toolInput[idx].ToString() });
        }

        if (maxTokensReached)
        {
            result.Error = result.ToolCalls.Count > 0
                ? "响应被 max_tokens 截断：工具调用参数不完整（不是合法 JSON），请把长内容拆成多段后重试"
                : "响应被 max_tokens 截断";
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
                return new AiChatResult { Error = OpenAiCompatibleProvider.Truncate(err.ToString(), 600) };

            var result = new AiChatResult();
            if (root.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                var textParts = new List<string>();
                foreach (var block in blocks.EnumerateArray())
                {
                    string type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "thinking" && block.TryGetProperty("thinking", out var tk))
                        result.Reasoning = (result.Reasoning ?? "") + (tk.GetString() ?? "");
                    else if (type == "text" && block.TryGetProperty("text", out var tx))
                        textParts.Add(tx.GetString() ?? "");
                    else if (type == "tool_use")
                    {
                        result.ToolCalls.Add(new AiToolCall
                        {
                            Id = block.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Name = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            ArgumentsJson = block.TryGetProperty("input", out var input)
                                ? input.GetRawText() : "{}"
                        });
                    }
                }
                result.Text = string.Join("\n", textParts);
            }
            return result;
        }
        catch (Exception ex)
        {
            return new AiChatResult { Error = $"流式回退解析失败: {ex.Message}" };
        }
    }

    private static List<Dictionary<string, object?>> BuildApiMessages(IReadOnlyList<AiChatMessage> messages)
    {
        var apiMessages = new List<Dictionary<string, object?>>();
        foreach (var m in messages)
        {
            if (m.Role == "system") continue;

            if (m.Role == "assistant")
            {
                var content = new List<Dictionary<string, object?>>();
                if (!string.IsNullOrEmpty(m.Text))
                    content.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = m.Text });
                if (m.ToolCalls != null)
                {
                    foreach (var tc in m.ToolCalls)
                    {
                        JsonElement input;
                        try { input = JsonDocument.Parse(tc.ArgumentsJson).RootElement.Clone(); }
                        catch { input = JsonDocument.Parse("{}").RootElement.Clone(); }
                        content.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.Name,
                            ["input"] = input
                        });
                    }
                }
                if (content.Count == 0)
                    content.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = "" });
                apiMessages.Add(new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = content });
            }
            else if (m.Role == "tool")
            {
                var block = new Dictionary<string, object?>
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = m.ToolCallId ?? "",
                    ["content"] = m.Text ?? ""
                };

                // Anthropic 要求 user/assistant 交替：多个工具结果必须放进同一条 user 消息，
                // 不能一条 tool 结果一条消息（否则第二个结果就会变成连续 user，被网关拒绝）。
                if (apiMessages.Count > 0 && apiMessages[^1]["role"] is "user")
                    ((List<Dictionary<string, object?>>)apiMessages[^1]["content"]!).Add(block);
                else
                    apiMessages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = new List<Dictionary<string, object?>> { block }
                    });
            }
            else // user
            {
                var content = new List<Dictionary<string, object?>>
                {
                    new() { ["type"] = "text", ["text"] = m.Text ?? "" }
                };

                if (m.Images is { Count: > 0 })
                {
                    foreach (var img in m.Images)
                    {
                        content.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "image",
                            ["source"] = new Dictionary<string, object?>
                            {
                                ["type"] = "base64",
                                ["media_type"] = img.MimeType,
                                ["data"] = img.Base64
                            }
                        });
                    }
                }

                // 截图消息紧跟在工具结果后面时，合并进同一条 user 消息，保持角色交替
                if (apiMessages.Count > 0 && apiMessages[^1]["role"] is "user")
                    ((List<Dictionary<string, object?>>)apiMessages[^1]["content"]!).AddRange(content);
                else
                    apiMessages.Add(new Dictionary<string, object?> { ["role"] = "user", ["content"] = content });
            }
        }
        return apiMessages;
    }

    /// <summary>
    /// 拉取可用模型列表（GET /v1/models）。认证头两种写法各试一次，理由同 OpenAI 兼容侧：
    /// 有的网关只认 <c>Authorization: Bearer</c>，有的只认 <c>x-api-key</c>，用错就回 401。
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        string url = BuildUrl(_settings.BaseUrl).Replace("/messages", "/models");

        var attempts = new (string Label, Action<HttpRequestMessage> Apply)[]
        {
            ("x-api-key", r => r.Headers.TryAddWithoutValidation("x-api-key", _settings.ApiKey)),
            ("Authorization: Bearer", r => r.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey)),
        };

        var log = new List<string>();
        int firstStatus = 0;
        string firstBody = "";

        foreach (var (label, apply) in attempts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            apply(req);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            OpenAiCompatibleProvider.ApplyClientHeaders(req, _settings);

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            string respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
                return OpenAiCompatibleProvider.ParseModelList(respText);

            // 只留第一次（标准写法 x-api-key）的响应原文，理由同 OpenAI 兼容侧
            if (firstStatus == 0) { firstStatus = (int)resp.StatusCode; firstBody = respText; }
            log.Add($"{label} → HTTP {(int)resp.StatusCode}");

            if ((int)resp.StatusCode is not (401 or 403)) break;
        }

        throw new InvalidOperationException(
            $"HTTP {firstStatus}: {OpenAiCompatibleProvider.Truncate(firstBody, 400)}　（地址 {url}；试过 {string.Join("、", log)}）");
    }

    private static string BuildUrl(string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase)) return baseUrl;
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return $"{baseUrl}/messages";
        return $"{baseUrl}/v1/messages";
    }
}
