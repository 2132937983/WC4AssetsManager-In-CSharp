using System.Text;
using WC4MapEditor.AI.Providers;
using WC4MapEditor.AI.Settings;
using WC4MapEditor.AI.Tools;

namespace WC4MapEditor.AI.Agent;

/// <summary>
/// Agent 循环：发送对话 → AI 返回工具调用 → 执行并把结果回传 → 重复，
/// 直到 AI 给出纯文本回复、出错或达到最大轮数。
/// </summary>
public sealed class AgentLoop
{
    private readonly AiSettings _settings;

    public AgentLoop(AiSettings settings) => _settings = settings;

    /// <summary>每轮事件的回调（UI 日志用）。</summary>
    public Action<string>? OnAssistantText { get; set; }
    public Action<string>? OnTextDelta { get; set; }         // 流式文本增量
    public Action<string, string>? OnToolCall { get; set; }       // (工具名, 参数 JSON)
    public Action<string, string, bool>? OnToolResult { get; set; } // (工具名, 结果, 成功?)
    public Action<string>? OnError { get; set; }
    /// <summary>状态提示（如"请求失败，2 秒后重试 1/5…"），用于界面显示进度而不是像卡死了。</summary>
    public Action<string>? OnStatus { get; set; }
    /// <summary>推理内容（思维链）回调：界面用它渲染"推理气泡"。</summary>
    public Action<string>? OnReasoning { get; set; }
    /// <summary>每一轮的诊断信息（轮次/正文长度/工具调用数），界面拿去写工具日志。</summary>
    public Action<string>? OnRound { get; set; }

    private List<AiChatMessage> _history = new();
    private CancellationTokenSource? _cts;

    public IReadOnlyList<AiChatMessage> History => _history;

    public bool IsRunning { get; private set; }

    /// <summary>开始新对话（保留 system prompt）。</summary>
    public void Reset()
    {
        _history.Clear();
        VisionRejected = false;   // 新对话重新给模型一次机会（可能换了模型/网关）
    }

    /// <summary>用户发一条消息并运行 Agent 循环。最终回复文本作为返回值。</summary>
    public async Task<string> RunUserTurnAsync(string userText)
    {
        if (IsRunning) return "（正在执行上一轮，请等待完成）";
        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var messages = new List<AiChatMessage>
            {
                AiChatMessage.System(BuildSystemPrompt(_settings))
            };
            messages.AddRange(_history);
            messages.Add(AiChatMessage.User(userText));

            // 已知模型看不了图：先把历史里的图片摘掉再发。
            // 否则历史会被整体重发，那张图每轮都去撞一次 400 —— 表现为"每次继续问都提示不能收图"。
            if (!_settings.VisionEnabled || VisionRejected)
            {
                StripImages(messages);
                OnRound?.Invoke("视觉已关闭/被拒：已清除历史中的图片，本轮按纯文字发送");
            }

            IAiProvider provider = CreateProvider();

            string finalText = "";
            int toolCallsTotal = 0;      // 本轮对话累计调用了几次工具
            int nudges = 0;              // "该调工具了"的提醒只发一次，免得空转烧 token
            int textToolRounds = 0;      // 文本协议回退执行了几段 Lua（限制总量，防跑飞）
            bool hadError = false;
            bool streaming = OnTextDelta != null && _settings.StreamEnabled;

            // 文本协议模式下**不发送工具定义**：模型没得"口头调用"，只能按回退协议写代码块。
            // 这正对"只会说'我将调用某某'却从不真的调用"的模型（推理模型、路由型模型常见）。
            bool textOnly = string.Equals(_settings.ToolsMode, "text", StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<AiToolSpec> tools = textOnly
                ? (IReadOnlyList<AiToolSpec>)Array.Empty<AiToolSpec>()
                : AiToolRegistry.BuildTools();

            for (int round = 0; round < Math.Max(1, _settings.MaxToolRounds); round++)
            {
                var result = await provider.SendAsync(messages, tools, _cts.Token,
                        streaming ? OnTextDelta : null, OnStatus)
                    .ConfigureAwait(true);

                if (result.HasError)
                {
                    // 模型拒绝图片输入（HTTP 400 model_incompatible / doesn't support image input）：
                    // 不该整轮结束 —— 把历史里的图剔掉、明确告诉它"你看不了图"，用文字继续跑。
                    if (LooksLikeImageRejection(result.Error) && HasImages(messages))
                    {
                        VisionRejected = true;
                        _settings.VisionEnabled = false;
                        StripImages(messages);
                        messages.Add(AiChatMessage.User(
                            "（系统）当前模型不支持图片输入，之前那些截图你收不到（已从对话里移除）。"
                            + "请依据文字信息继续：工具返回的文字、坐标、脚本打印的数字都可以用；"
                            + "需要看图时改为让脚本把关键数据打印出来。"));

                        OnStatus?.Invoke("该模型不支持图片输入：已自动去掉图片并用文字继续（后续不再发图）");
                        OnRound?.Invoke("模型拒绝图片输入 → 已剔除图片并改为纯文字重试");
                        continue;
                    }

                    hadError = true;
                    OnError?.Invoke(result.Error!);
                    finalText = $"（请求失败: {result.Error}）";
                    break;
                }

                // 把每轮的实况写进日志：用户能看出"是模型没给工具调用"还是"工具没执行"
                OnRound?.Invoke($"第 {round + 1} 轮：正文 {result.Text?.Length ?? 0} 字"
                    + $"，推理 {(string.IsNullOrEmpty(result.Reasoning) ? 0 : result.Reasoning!.Length)} 字"
                    + $"，工具调用 {result.ToolCalls.Count} 个（已提供 {tools.Count} 个工具）");

                if (!string.IsNullOrEmpty(result.Reasoning))
                    OnReasoning?.Invoke(result.Reasoning!);

                if (!string.IsNullOrEmpty(result.Text) && !streaming)
                    OnAssistantText?.Invoke(result.Text);

                if (result.ToolCalls.Count == 0)
                {
                    // 文本协议回退：不少推理模型（以及部分网关）根本不返回 tool_calls，
                    // 只会"思考 + 描述"。这时只要它把 Lua 写在 ```lua 里，我们就替它执行、
                    // 把输出回灌，让它能像正常工具调用一样一步步推进，而不是只会推理。
                    if ((_settings.TextToolFallback || textOnly) && textToolRounds < MaxTextToolRounds)
                    {
                        // ```lua → 执行 Lua；```cli → 执行编辑器命令。两种都算"文本协议"的一步。
                        string luaCode = ExtractFencedBlock(result.Text, "lua");
                        string cliCode = luaCode.Length > 0
                            ? ""
                            : ExtractFencedBlock(result.Text, "cli", "cmd", "命令");

                        if (luaCode.Length > 0 || cliCode.Length > 0)
                        {
                            textToolRounds++;
                            bool isLua = luaCode.Length > 0;
                            string stepName = isLua ? "lua(文本协议)" : "命令(文本协议)";

                            messages.Add(new AiChatMessage { Role = "assistant", Text = result.Text });
                            OnToolCall?.Invoke(stepName, Truncate(isLua ? luaCode : cliCode, 300));

                            string output = isLua ? RunLuaBlock(luaCode) : RunCliBlock(cliCode);
                            OnToolResult?.Invoke(stepName, output, !output.StartsWith("[FAIL]"));
                            OnRound?.Invoke($"文本协议：已执行模型给出的{(isLua ? " Lua" : " 命令")}（第 {textToolRounds} 段）");

                            messages.Add(AiChatMessage.User(
                                "（系统）上一步执行输出：\n" + output
                                + "\n请依据输出继续：需要更多信息就再给一个代码块（```lua 或 ```cli），完成后再给最终结论。"));
                            continue;
                        }
                    }

                    // 模型只"说要查工具"却没真调用时，直接 break 就等于整轮空转结束
                    // （用户看到的是"他说要查，然后就没下文了"）。所以先明确推它一把。
                    if (toolCallsTotal == 0 && nudges < 1 && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        nudges++;
                        // 如果它在正文里点了某个工具的名，就直接点破："你说了要调用 X，但没调用"
                        string mentioned = "";
                        foreach (var tool in tools)
                        {
                            if (result.Text!.Contains(tool.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                mentioned = tool.Name;
                                break;
                            }
                        }

                        messages.Add(new AiChatMessage { Role = "assistant", Text = result.Text });
                        messages.Add(AiChatMessage.User(mentioned.Length > 0
                            ? $"（系统）你在正文里提到了 {mentioned}，但并没有真的调用它 —— 文字描述不算执行。"
                              + $"现在请直接发起 {mentioned} 的工具调用。\n"
                              + "如果你的模型或网关做不到工具调用，就按回退协议把要执行的命令写进 ```cli 代码块"
                              + "（例如：\n```cli\nnew_campaign 160 90\n```\n），或把要跑的 Lua 写进 ```lua 代码块，"
                              + "系统会替你执行并把输出贴回来。"
                            : "（系统）你刚才没有真的执行任何操作。需要读取地图数据或执行操作时，请直接调用工具；"
                              + "做不到工具调用就按回退协议写 ```cli 或 ```lua 代码块，系统会替你执行。"
                              + "若确实不需要任何操作，请给出最终结论。"));
                        OnRound?.Invoke(mentioned.Length > 0
                            ? $"提醒：正文提到了 {mentioned} 却没调用，已催一次（并给出代码块写法）"
                            : "提醒：模型未返回工具调用，已追加一次「请调用工具或写代码块」");
                        continue;
                    }

                    finalText = result.Text;
                    messages.Add(new AiChatMessage { Role = "assistant", Text = result.Text });
                    break;
                }

                toolCallsTotal += result.ToolCalls.Count;

                // assistant 带工具调用 → 逐个执行 → 回传结果
                messages.Add(new AiChatMessage { Role = "assistant", Text = result.Text, ToolCalls = result.ToolCalls });

                foreach (var call in result.ToolCalls)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    OnToolCall?.Invoke(call.Name, call.ArgumentsJson);

                    string output = "";
                    var artifactPaths = await Task.Run(
                        () => AiToolRegistry.ExecuteWithArtifacts(call.Name, call.ArgumentsJson, out output),
                        _cts.Token).ConfigureAwait(true);

                    bool ok = !output.StartsWith("失败") && !output.StartsWith("[FAIL]") && !output.StartsWith("未知工具");
                    OnToolResult?.Invoke(call.Name, output, ok);

                    messages.Add(AiChatMessage.ToolResult(call.Id, call.Name, output));

                    // 截图类工具会产出图片：补一条带图消息，模型才真的"看得见"。
                    // 只发文本协议（OpenAI 的 tool 消息）塞不下图片，所以图片单独作为一条 user 消息。
                    if (artifactPaths.Count > 0 && (!_settings.VisionEnabled || VisionRejected))
                    {
                        // 图生成了但发不出去：告诉模型"别看图了，用文字" —— 否则它会一直等一张永远收不到的图
                        messages.Add(AiChatMessage.User(
                            "（系统）当前模型不支持图片输入，截图无法发送给你。"
                            + "请改用文字信息继续分析（坐标、工具输出、脚本打印的数据）。"));
                    }
                    else if (artifactPaths.Count > 0)
                    {
                        var images = new List<AiImage>();
                        foreach (var path in artifactPaths)
                        {
                            var img = AiImage.FromFile(path);
                            if (img != null) images.Add(img);
                        }

                        if (images.Count > 0)
                        {
                            messages.Add(AiChatMessage.UserWithImages(
                                "（系统）上面工具刚生成的图，请直接看图分析。图上的文字标注就是格子坐标（列,行）。",
                                images));
                        }
                        else
                        {
                            messages.Add(AiChatMessage.User(
                                "（系统）图片读取失败或超出大小限制，请只用文字信息继续分析。"));
                        }
                    }
                }
            }

            // 一次工具都没调用成功时，别让用户以为"AI 干完了"：
            // 说清楚是模型/网关没给工具调用，并给出可操作的建议。
            if (toolCallsTotal == 0 && textToolRounds == 0 && !hadError)
            {
                string hint = textOnly
                    ? "（提示：这一轮模型既没执行操作，也没写 ```cli / ```lua 代码块。"
                      + "可以把要求说得更具体（例如「执行 new_campaign 160 90」），或换一个指令跟随更好的模型。）"
                    : "（提示：这一轮模型没有真的调用任何工具。如果它只会说「我将调用某某」却不动手，"
                      + "说明该模型/网关不支持 function calling —— 到设置里把「工具调用」改成"
                      + "「text（不发工具定义）」再试，那样它会直接写 ```cli 代码块；或者换一个支持工具调用的模型。）";
                finalText = string.IsNullOrEmpty(finalText) ? hint : finalText + "\n\n" + hint;
            }

            // 持久化到历史（去掉 system，控件侧只关心对话本身）
            _history = messages.Where(m => m.Role != "system").ToList();
            return finalText;
        }
        catch (OperationCanceledException)
        {
            OnError?.Invoke("已停止");
            return "（已停止）";
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"{ex.GetType().Name}: {ex.Message}");
            return $"（执行异常: {ex.Message}）";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>停止当前循环（在下一个工具边界生效）。</summary>
    public void Stop() => _cts?.Cancel();

    /// <summary>历史消息条数。</summary>
    public int MessageCount => _history.Count;

    /// <summary>估算当前上下文 Token 数（含 system prompt + 全部历史）。</summary>
    public int EstimateTokenCount()
    {
        int tokens = EstimateTokens(BuildSystemPrompt(_settings)) + 10;
        foreach (var msg in _history)
        {
            tokens += EstimateTokens(msg.Text ?? "") + 4;
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                    tokens += EstimateTokens(tc.Name + tc.ArgumentsJson) + 10;
            }
        }
        return tokens;
    }

    /// <summary>压缩上下文：让 AI 摘要旧消息，保留最近几条不变。返回 (旧Token, 新Token)。</summary>
    public async Task<(int OldTokens, int NewTokens)> CompressHistoryAsync()
    {
        if (_history.Count <= 4 || IsRunning) return (0, 0);

        int oldTokens = EstimateTokenCount();

        int keepRecent = Math.Min(6, _history.Count);
        var oldMsgs = _history.Take(_history.Count - keepRecent).ToList();
        var recentMsgs = _history.Skip(_history.Count - keepRecent).ToList();

        if (oldMsgs.Count == 0) return (0, 0);

        var sb = new StringBuilder();
        foreach (var msg in oldMsgs)
        {
            string role = msg.Role switch
            {
                "user" => "用户",
                "assistant" => "AI",
                "tool" => $"工具({msg.ToolName})",
                _ => msg.Role
            };
            string text = msg.Text ?? "";
            if (text.Length > 300) text = text[..300] + "…";
            sb.AppendLine($"[{role}] {text}");
        }

        try
        {
            var provider = CreateProvider();
            var summaryMsgs = new List<AiChatMessage>
            {
                AiChatMessage.System("你是对话摘要助手。请用3-5句简体中文总结以下对话的关键信息，保留重要决策、偏好、操作结果和数据。不要添加额外解释。"),
                AiChatMessage.User(sb.ToString())
            };

            var result = await provider.SendAsync(summaryMsgs, Array.Empty<AiToolSpec>(), CancellationToken.None,
                    onTextDelta: null, onRetry: OnStatus)
                .ConfigureAwait(true);

            if (!result.HasError && !string.IsNullOrEmpty(result.Text))
            {
                _history = new List<AiChatMessage>
                {
                    new() { Role = "user", Text = "[上下文摘要] 以下是之前对话的概要：" },
                    new() { Role = "assistant", Text = result.Text }
                };
                _history.AddRange(recentMsgs);
            }
            else
            {
                _history = recentMsgs.ToList();
            }
        }
        catch
        {
            _history = recentMsgs.ToList();
        }

        int newTokens = EstimateTokenCount();
        return (oldTokens, newTokens);
    }

    private static int EstimateTokens(string text)
    {
        int chinese = 0, ascii = 0;
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) chinese++;
            else if (c > 31) ascii++;
        }
        return (int)(chinese * 1.5 + ascii * 0.25);
    }

    private IAiProvider CreateProvider()
        => string.Equals(_settings.Provider, "claude", StringComparison.OrdinalIgnoreCase)
            ? new ClaudeProvider(_settings)
            : new OpenAiCompatibleProvider(_settings);

    /// <summary>一轮对话里最多执行几段"文本协议"的 Lua（防模型跑飞）</summary>
    private const int MaxTextToolRounds = 4;

    /// <summary>
    /// 模型明确拒绝过图片输入（HTTP 400 之类）后置位：本轮之后不再发图，
    /// 也据此告诉模型"你看不到图，请用文字继续"。
    /// </summary>
    public bool VisionRejected { get; private set; }

    /// <summary>
    /// 判断错误是不是"模型不支持图片输入"。
    /// 各家措辞不同（英文/中文、走网关还可能换个 type），所以按"提到图片/视觉"或
    /// "model_incompatible 且说了支持/兼容"两类特征抓。
    /// </summary>
    private static bool LooksLikeImageRejection(string? error)
    {
        if (string.IsNullOrEmpty(error)) return false;

        string e = error.ToLowerInvariant();

        // 1) 明确提到图片/视觉 + 说了支持/兼容/禁止之类
        bool mentionsImage = e.Contains("image") || e.Contains("vision") || e.Contains("multimodal")
                             || e.Contains("图片") || e.Contains("图像") || e.Contains("视觉")
                             || e.Contains("多模态") || e.Contains("图文");
        bool saysUnsupported = e.Contains("support") || e.Contains("incompatible") || e.Contains("unsupported")
                               || e.Contains("not allowed") || e.Contains("invalid") || e.Contains("multi-modal")
                               || e.Contains("不支持") || e.Contains("无法处理") || e.Contains("不支持该");
        if (mentionsImage && saysUnsupported) return true;

        // 2) 网关常用 type=model_incompatible 表达"这个模型干不了这件事"
        if (e.Contains("model_incompatible")) return true;

        return false;
    }

    private static bool HasImages(List<AiChatMessage> messages)
    {
        foreach (var m in messages)
        {
            if (m.Images is { Count: > 0 }) return true;
        }
        return false;
    }

    /// <summary>把历史消息里的图片全部摘掉（模型不支持视觉时用）。</summary>
    private static void StripImages(List<AiChatMessage> messages)
    {
        foreach (var m in messages)
        {
            if (m.Images is { Count: > 0 }) m.Images = null;
        }
    }

    /// <summary>
    /// 取出回复里第一段指定语言的 ```代码块；没有则返回空串。
    /// 按 <paramref name="languages"/> 的顺序找（调用方把优先的放前面）。
    /// </summary>
    internal static string ExtractFencedBlock(string? text, params string[] languages)
    {
        if (string.IsNullOrEmpty(text)) return "";

        foreach (var lang in languages)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                text,
                "```(?:" + System.Text.RegularExpressions.Regex.Escape(lang) + ")[^\\n]*\\r?\\n?([\\s\\S]*?)```",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        return "";
    }

    /// <summary>执行若干行编辑器命令（一行一条，'#' 开头跳过），返回给模型看的文本。</summary>
    private static string RunCliBlock(string block)
    {
        var sb = new StringBuilder();
        foreach (var raw in block.Split('\n'))
        {
            string cmd = raw.Trim().TrimEnd('\r');
            if (cmd.Length == 0 || cmd.StartsWith('#')) continue;

            var (ok, output) = AiBridge.RunCommand(cmd);
            sb.AppendLine((ok ? "[OK] " : "[FAIL] ") + cmd);
            if (!string.IsNullOrWhiteSpace(output)) sb.AppendLine(output.Trim());
        }

        string text = sb.ToString().TrimEnd();
        return text.Length > 4000 ? text[..4000] + "\n…（输出过长已截断）" : text;
    }

    /// <summary>执行一段 Lua（走主程序注册的 Lua 执行器），整理成给模型看的文本。</summary>
    private static string RunLuaBlock(string code)
    {
        var result = AiBridge.RunLua(code);
        if (result == null) return "[FAIL] 主程序未注册 Lua 执行器（可能没有打开地图）";

        var sb = new StringBuilder();
        sb.AppendLine(result.Success ? "[OK]" : "[FAIL] " + (result.Error ?? "执行失败"));
        if (result.Output != null)
        {
            foreach (var line in result.Output) sb.AppendLine(line);
        }

        string text = sb.ToString().TrimEnd();
        return text.Length > 4000 ? text[..4000] + "\n…（输出过长已截断）" : text;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    internal static string BuildSystemPrompt(AiSettings settings)
    {
        string providerName = string.Equals(settings.Provider, "claude", StringComparison.OrdinalIgnoreCase)
            ? "Anthropic Claude" : "OpenAI 兼容协议";
        string prompt = $"""
你是"世界征服者4 地图编辑器"内置的 AI 助手，通过工具调用操作编辑器，等效于用户的操作（执行命令、运行脚本、切换编辑场景）。

【系统元数据】
当前会话实际调用的模型标识为：{settings.Model}
调用协议为：{providerName}
当用户询问你的模型身份（如"你是什么模型""你是什么大模型""你是谁"）时，直接回答上述模型标识，并说明这是系统注入的元数据，不是你猜测的。不要说"我不知道""我无法获取""我是一个 AI 助手"等回避话术。不要假装是其他产品。

工作准则：
1. 操作地图前先用 get_map_info 了解地图；不确定命令时先跑 help 或先查询再修改。
2. 需要打开地图文件（如 stage10101.btl）时用 open_map，路径可为 assets 内相对路径（如 stage/stage10101.btl）。
3. 修改类操作不可轻率：Lua 执行整批可撤销（Ctrl+Z），但请在回复里说明你改了什么、影响多大。
4. 批量/条件类修改优先用 run_lua（map 对象 API 全量可用），单条功能用 run_cli_command。
5. 需要看某个编辑界面时用 open_scene 切场景（先用 list_scenes 查名称）。
6. 查资源/图集用 list_assets、read_asset_text、get_asset_summary（AM 资产管理器）；
   查 setting.txt 文本用 get_config_text、查游戏文本(国家名/城市名/事件名)用 get_stringtable_value 或 list_stringtable_keys 或 search_stringtable、
   查地形类型表用 get_terrain_types（CM 配置管理器）。
7. 查地图建筑用 get_building_list、查军团用 get_legion_list、查单位用 get_army_list、查国家列表用 get_country_list。
8. 读写项目根目录下的文件用 read_file/write_file/list_dir（如 setting.txt、Resource/ 下的配置文件）。修改前先 read_file 确认内容，改后说明改了什么。
9. 所有回答用简体中文，简明扼要。工具结果里的 [OK]/[FAIL] 表示执行成败。
10. 工具报错时，读错误信息修正后重试，不要盲目重复同一调用。

记忆准则（跨会话持久化）：
11. 新会话开始时，先调用 list_memories 回顾已有记忆，读取与本任务相关的记忆内容，避免重复劳动。
12. 以下情况应主动 save_memory 保存记忆：
    - 用户明确表达了偏好或约定（如"我习惯先做地形再做建筑""默认用军团ID=3"）；
    - 你总结出了可复用的操作流程或经验（如"批量修改地形的最佳步骤""导出HD地图的注意事项"）；
    - 用户要求你记住某条信息；
    - 你发现了项目中的关键约定或结构（如"stage 文件命名规则""建筑类型ID含义"）。
13. 记忆标题用简短英文短横线命名（如 user-preferences、terrain-workflow、building-type-reference），内容用 Markdown 格式，结构清晰。
14. 更新已有记忆时直接 save_memory 覆盖（会自动备份旧版本），无需先删除。
15. 不要保存临时性/一次性信息（如"当前正在编辑 stage10101"），只保存跨会话仍有有效的知识。

执行准则（最重要）：
16. **描述不算执行**。不要在正文里写"我将调用 xxx"「接下来我会用 yyy」然后停下 —— 必须真的发起工具调用。
17. 任务没真正做完之前，不要输出"已完成/已创建"之类结论；只有工具的返回结果确认了才可以说完成。
""";

        // 文本协议模式下必然要带上回退说明（否则模型没有工具、也不知道能写代码块）
        bool textMode = string.Equals(settings.ToolsMode, "text", StringComparison.OrdinalIgnoreCase);
        return (settings.TextToolFallback || textMode) ? prompt + TextToolFallbackSection : prompt;
    }

    /// <summary>
    /// 没有 function calling 能力时的回退协议（拼进 system prompt）。
    /// 很多推理模型和部分网关不返回 tool_calls，只会"描述打算做什么"；
    /// 告诉它把 Lua 写在代码块里，助手替它执行，它就能继续推进。
    /// </summary>
    private const string TextToolFallbackSection = """


【没有工具调用能力时的回退协议】
如果你的模型或网关不支持 function calling（你无法真正调用工具，只能输出文字），
请不要停在"我打算去查/我将会做"，而是把要执行的 Lua 写进一个 ```lua 代码块，系统会替你执行并把输出贴回来：

```lua
return map.Width .. 'x' .. map.Height
```

要求：
- 一次只给一段**最小必要**的代码，拿到输出后再决定下一步；不要一口气把整个流程写完。
- 操作地图数据用 ```lua，全局对象是 map，例如 map.GetBelong(col,row)、map.SetBelong(col,row,v)、
  map.ForEachBuilding(function(b) ... end)；需要看画面时用 map.CaptureScreenshot('shots/x.png', col, row, w, h)。
- 执行编辑器命令用 ```cli，一行一条。例如新建一张 140×90 的战役地图：

```cli
new_campaign 140 90
```

  其他常用命令：new_conquest <宽> <高> [军团数] [地图编号]、new_map <宽> <高>、open <文件路径>、
  help（列出全部命令）。没有打开地图时，new_campaign / new_conquest / new_map / open 是可用的。
- 不要写需要人工交互的代码；不要重复执行同一段代码。
""";
}