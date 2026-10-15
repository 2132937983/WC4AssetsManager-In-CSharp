using System.IO;
using System.Text.Json;

namespace WC4MapEditor.AI.Providers;

/// <summary>
/// Provider 无关的聊天消息模型。两种协议（OpenAI / Claude）的收发
/// 都在此模型与各自 JSON 之间转换。
/// </summary>
public sealed class AiChatMessage
{
    /// <summary>system / user / assistant / tool</summary>
    public string Role { get; set; } = "user";

    /// <summary>文本内容（tool 结果消息时为工具输出文本）</summary>
    public string? Text { get; set; }

    /// <summary>assistant 消息附带的工具调用（协议归一化后的形态）</summary>
    public List<AiToolCall>? ToolCalls { get; set; }

    /// <summary>tool 结果消息：对应的工具调用 ID</summary>
    public string? ToolCallId { get; set; }

    /// <summary>tool 结果消息：工具名（Claude 协议必需，OpenAI 用于日志）</summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// 随消息发给模型的图片（"AI 视觉"）。只有 user 消息会带，
    /// 且只有多模态模型认 —— 给纯文本模型发图整轮请求会直接失败。
    /// </summary>
    public List<AiImage>? Images { get; set; }

    /// <summary>assistant 的推理内容（思维链）。DeepSeek-R1 类走 reasoning_content，Claude 走 thinking 块。</summary>
    public string? Reasoning { get; set; }

    public static AiChatMessage System(string text) => new() { Role = "system", Text = text };
    public static AiChatMessage User(string text) => new() { Role = "user", Text = text };
    public static AiChatMessage ToolResult(string toolCallId, string toolName, string output) =>
        new() { Role = "tool", Text = output, ToolCallId = toolCallId, ToolName = toolName };

    public static AiChatMessage UserWithImages(string text, IEnumerable<AiImage> images) =>
        new() { Role = "user", Text = text, Images = images.ToList() };
}

/// <summary>
/// 一张随消息发送的图片（已 base64 编码）。
/// <para>
/// 直接存 base64 而不是存路径：历史消息每轮都会重发，存数据就不依赖文件是否还在，
/// 也不用每轮重新读盘。
/// </para>
/// </summary>
public sealed class AiImage
{
    /// <summary>MIME 类型：image/png、image/jpeg、image/webp</summary>
    public string MimeType { get; set; } = "image/png";

    /// <summary>base64 数据（不含 <c>data:</c> 前缀）</summary>
    public string Base64 { get; set; } = "";

    /// <summary>估算的字节数（用于判断是否超出接口限制）</summary>
    public int ApproxBytes => Base64.Length / 4 * 3;

    /// <summary>
    /// 从磁盘读一张图。文件不存在 / 空 / 过大时返回 null ——
    /// 超限的图塞进请求只会让整轮失败，不如提前放弃。
    /// </summary>
    public static AiImage? FromFile(string path, int maxBytes = 4 * 1024 * 1024)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > maxBytes) return null;

            string mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png",
            };

            return new AiImage
            {
                MimeType = mime,
                Base64 = Convert.ToBase64String(File.ReadAllBytes(path)),
            };
        }
        catch
        {
            return null;
        }
    }
}

public sealed class AiToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>参数原文（JSON 对象字符串）。解析失败时原样透传给工具执行器。</summary>
    public string ArgumentsJson { get; set; } = "{}";

    public JsonElement? ParsedArguments()
    {
        try
        {
            using var doc = JsonDocument.Parse(ArgumentsJson);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    public string GetStringArg(JsonElement args, string name, string def = "")
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? def : def;

    public int GetIntArg(JsonElement args, string name, int def = 0)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : def;
}

/// <summary>暴露给 AI 的工具定义（JSON Schema 形式）</summary>
public sealed class AiToolSpec
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>参数 JSON Schema 对象字符串</summary>
    public string ParametersJson { get; set; } = "{\"type\":\"object\",\"properties\":{}}";
}

public sealed class AiChatResult
{
    /// <summary>assistant 文本回复（可与 ToolCalls 并存，如"我先看一下地图信息…"）</summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 推理内容（思维链）。模型不返回时为空。
    /// 只用于界面展示 —— 按各家约定，推理内容**不应**回传给模型，所以发送时会被忽略。
    /// </summary>
    public string? Reasoning { get; set; }

    public List<AiToolCall> ToolCalls { get; set; } = new();

    /// <summary>网络 / 协议层错误（非空表示本次请求失败）</summary>
    public string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);
}

/// <summary>AI Provider 抽象：OpenAI 兼容协议与 Claude 协议各一个实现。</summary>
public interface IAiProvider
{
    string ProviderName { get; }

    /// <summary>
    /// 发送一轮对话（含历史与工具结果），返回 assistant 响应。
    /// 当 onTextDelta 非空时启用流式输出，逐块回调文本增量；最终完整文本仍在 AiChatResult.Text。
    /// <para>
    /// 请求失败时按 <see cref="RetryPolicy"/> 自动重试（最多 <see cref="RetryPolicy.MaxRetries"/> 次），
    /// 每次重试前通过 <paramref name="onRetry"/> 报一句进度，便于界面提示"正在重试"而不是像卡住了。
    /// </para>
    /// </summary>
    Task<AiChatResult> SendAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct = default,
        Action<string>? onTextDelta = null,
        Action<string>? onRetry = null);

    /// <summary>拉取可用模型 ID 列表（GET /v1/models）。失败抛异常。</summary>
    Task<List<string>> ListModelsAsync(CancellationToken ct = default);
}
