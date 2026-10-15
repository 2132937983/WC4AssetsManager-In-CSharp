using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WC4MapEditor.AI.Settings;

/// <summary>
/// AI 助手配置（ai_settings.json，与 exe 同目录）。
/// 含 ApiKey，不入 git。
/// </summary>
public sealed class AiSettings
{
    /// <summary>openai = OpenAI 兼容 /chat/completions；claude = Anthropic /v1/messages</summary>
    public string Provider { get; set; } = "openai";

    /// <summary>API 根地址，如 https://api.deepseek.com 或 https://api.anthropic.com</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string ApiKey { get; set; } = "";

    /// <summary>
    /// 请求里的客户端标识（HTTP 的 User-Agent）。
    /// <para>
    /// .NET 的 <c>HttpClient</c> 默认**不发** User-Agent，而有些中转网关正是靠它认客户端，
    /// 认不出来就回 401（文案常写"不支持的客户端"）。各家要求不一样
    /// （Claude Code 系中转常见要求 <c>claude-cli/2.0.0 (external, cli)</c>），
    /// 所以做成可配置项。留空 = 不发送。
    /// </para>
    /// </summary>
    public string UserAgent { get; set; } = "WC4MapEditor-AI/1.0";

    public string Model { get; set; } = "deepseek-chat";
    public double Temperature { get; set; } = 0.7;

    /// <summary>单次 Agent 循环最多工具调用轮数，防失控</summary>
    public int MaxToolRounds { get; set; } = 12;

    /// <summary>
    /// 是否允许把地图截图发给模型（需要多模态模型）。
    /// 纯文本模型开着它，带图的那一轮请求会直接失败 —— 所以模型不支持时请在设置里关掉。
    /// </summary>
    public bool VisionEnabled { get; set; } = true;

    /// <summary>
    /// 推理气泡的最大高度（像素）。长思维链能把对话顶爆，所以在气泡上给了滑块随时调，
    /// 这里记住上次的值，下次开窗即用它。
    /// </summary>
    public double ReasoningMaxHeight { get; set; } = 160;

    /// <summary>
    /// 是否用流式输出（边生成边显示）。默认开。
    /// <para>
    /// 少数网关在流式模式下会丢 <c>tool_calls</c>，表现成"AI 说要去查工具，然后就没下文了"。
    /// 遇到这种情况在设置里关掉它，改走一次性响应。
    /// </para>
    /// </summary>
    public bool StreamEnabled { get; set; } = true;

    /// <summary>
    /// 文本协议回退：模型不会 function calling 时（推理模型大多如此，部分网关也会吞掉工具调用），
    /// 允许它把要执行的 Lua 写在 ```lua 代码块里，由助手替它执行并把输出回灌。
    /// 默认开 —— 没有它，"只会推理不会调工具"的模型就只能干看着。
    /// </summary>
    public bool TextToolFallback { get; set; } = true;

    /// <summary>
    /// 推理（思考）开关：default（不干预，按模型默认）/ on（要求思考）/ off（要求不思考）。
    /// <para>
    /// 各家的"关闭"写法不一样，这里按常见约定发：
    /// OpenAI 兼容协议 → <c>enable_thinking: true/false</c>（Qwen3 及不少网关认这个）；
    /// Claude → <c>thinking.type = enabled/disabled</c>。
    /// 不认识的模型/网关可能对未知字段报 400，遇到就把本项改回 default。
    /// 另外：像 deepseek-reasoner 这类模型没有关闭参数，只能换模型。
    /// </para>
    /// </summary>
    public string ReasoningMode { get; set; } = "default";

    /// <summary>
    /// 推理强度：default（不发送）/ low / medium / high。仅在「推理」为开时生效。
    /// <para>
    /// 只在非 default 时才写进请求：OpenAI 兼容协议发 <c>reasoning_effort</c>，
    /// Claude 发 <c>thinking.budget_tokens</c>。不认识这个字段的模型可能直接报 400，
    /// 所以默认不发送。
    /// </para>
    /// </summary>
    public string ReasoningEffort { get; set; } = "default";

    /// <summary>
    /// 工具调用方式：
    /// <list type="bullet">
    /// <item><c>native</c>（默认）：正常发送工具定义，走原生 function calling。</item>
    /// <item><c>text</c>：**不发送工具定义**，让模型按文本协议把要执行的东西写进 ```cli / ```lua 代码块。
    /// 适用于"只会说'我将调用某某'却从不真的调用"的模型（很多推理模型、路由型模型就是这样），
    /// 不发给它工具定义，它就没得"口头调用"，只能老老实实写代码块。</item>
    /// </list>
    /// </summary>
    public string ToolsMode { get; set; } = "native";

    /// <summary>
    /// 按模型名猜是否支持视觉。只是"提示"，不是权威判断：
    /// 名字千奇百怪（尤其走中转网关时），真正的确认方式是把一张小图发过去看它认不认。
    /// </summary>
    public static bool LikelySupportsVision(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        string m = model.ToLowerInvariant();

        // 有名的纯文本模型：这些开了视觉只会整轮失败
        string[] textOnly =
        {
            "deepseek-chat", "deepseek-reasoner", "deepseek-v3", "deepseek-v2", "deepseek-coder",
            "step-router", "step-2", "text-only", "embedding", "rerank",
        };
        foreach (var t in textOnly)
        {
            if (m.Contains(t)) return false;
        }

        // 已知的视觉家族
        string[] vision =
        {
            "gpt-4o", "gpt-4.1", "gpt-4-turbo", "gpt-4-vision", "gpt-4.5", "gpt-5", "o1", "o3", "o4",
            "claude-3", "claude-4", "claude-sonnet", "claude-opus", "claude-haiku",
            "gemini", "qwen-vl", "qwen2-vl", "qwen2.5-vl", "qwen3-vl", "qwen-omni",
            "glm-4v", "glm-4.5v", "llava", "pixtral", "internvl", "minicpm-v",
            "step-1v", "step-1o", "vision", "multimodal", "omni", "vl-", "-vl",
        };
        foreach (var v in vision)
        {
            if (m.Contains(v)) return true;
        }

        return false;
    }

    public static string DefaultFilePath
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            return Path.Combine(dir, "ai_settings.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AiSettings Load(string? path = null)
    {
        path ??= DefaultFilePath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<AiSettings>(json, JsonOpts);
                if (s != null) return s;
            }
        }
        catch
        {
            // 配置损坏时按默认值继续，保存时会覆盖
        }
        return new AiSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultFilePath;
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(path, json);
    }

    public AiSettings Clone() => (AiSettings)MemberwiseClone();
}
