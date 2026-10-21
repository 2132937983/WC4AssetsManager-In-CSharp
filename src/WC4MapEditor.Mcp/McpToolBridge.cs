using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WC4MapEditor.AI.Providers;
using WC4MapEditor.AI.Tools;

namespace WC4MapEditor.Mcp;

/// <summary>
/// 把 AI 的 35 个工具原样暴露成 MCP 工具。
/// <para>
/// 不在这里重新实现任何能力：MCP 调用最终仍走 <see cref="AiToolRegistry"/>，
/// 这样外部 agent 和内置 AI 助手用的是同一套行为（同样的 dry_run、撤销、路径校验）。
/// </para>
/// </summary>
internal static class McpToolBridge
{
    private static readonly JsonElement EmptySchema =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    /// <summary>tools/list：把 AiToolRegistry 的 Schema 转成 MCP 的 Tool。</summary>
    public static ListToolsResult ListTools()
    {
        var tools = new List<Tool>();

        foreach (var spec in AiToolRegistry.BuildTools())
        {
            // submit_plan 是内置对话循环专用的（外部 agent 有自己的计划机制），
            // 暴露出去只会得到一个 no-op 结果，反而误导客户端。
            if (string.Equals(spec.Name, "submit_plan", StringComparison.Ordinal)) continue;

            JsonElement schema = EmptySchema;
            try
            {
                if (!string.IsNullOrWhiteSpace(spec.ParametersJson))
                    schema = JsonDocument.Parse(spec.ParametersJson).RootElement.Clone();
            }
            catch
            {
                // Schema 坏了也不能让 tools/list 整体失败；给一个空对象，调用时仍会报具体参数问题
            }

            tools.Add(new Tool
            {
                Name = spec.Name,
                Description = spec.Description,
                InputSchema = schema,
                Annotations = BuildAnnotations(spec.Name),
            });
        }

        return new ListToolsResult { Tools = tools };
    }

    /// <summary>tools/call：参数序列化回 JSON，交给现有工具执行器；截图作为 MCP 图片内容返回。</summary>
    public static async Task<CallToolResult> CallAsync(CallToolRequestParams p, CancellationToken ct)
    {
        string argumentsJson = p.Arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(p.Arguments)
            : "{}";

        string output = "";
        IReadOnlyList<string> artifactPaths = Array.Empty<string>();

        // 工具内部会经 AiBridge 切回 WPF 线程；这里放到线程池，避免阻塞 HTTP 请求线程
        await Task.Run(
                () => artifactPaths = AiToolRegistry.ExecuteWithArtifacts(p.Name, argumentsJson, out output),
                ct)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(output))
            output = "（工具没有返回文本输出）";

        var content = new List<ContentBlock>
        {
            new TextContentBlock { Text = output }
        };

        // capture_screenshot 的产物：直接以图片内容返回，支持视觉的 MCP 客户端能看见
        foreach (string path in artifactPaths)
        {
            var image = AiImage.FromFile(path);
            if (image == null) continue;

            try
            {
                // Data 字段要求的是 base64 的 UTF-8 字节，直接塞原始 PNG 字节会被客户端判成非法 base64
                content.Add(ImageContentBlock.FromBytes(
                    Convert.FromBase64String(image.Base64),
                    image.MimeType));
            }
            catch
            {
                // base64 坏了就只返回文本，不让整次调用失败
            }
        }

        return new CallToolResult
        {
            Content = content,
            IsError = output.StartsWith("失败", StringComparison.Ordinal)
                      || output.StartsWith("[FAIL]", StringComparison.Ordinal)
                      || output.StartsWith("未知工具", StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// MCP 工具注解：客户端据此决定是否确认。
    /// 名字前缀与 AiToolRegistry 的命名约定一致（get_/list_/read_/search_ 为只读）。
    /// </summary>
    private static ToolAnnotations BuildAnnotations(string name)
    {
        bool readOnly = name.StartsWith("get_", StringComparison.Ordinal)
                        || name.StartsWith("list_", StringComparison.Ordinal)
                        || name.StartsWith("read_", StringComparison.Ordinal)
                        || name.StartsWith("search_", StringComparison.Ordinal);

        bool destructive = name is "write_file" or "delete_memory"
            or "run_lua" or "run_lua_file" or "apply_map_spec" or "run_cli_command"
            or "set_object_field" or "set_object_field_batch"
            or "open_map" or "new_map" or "new_campaign" or "new_conquest"
            or "open_scene";

        return new ToolAnnotations
        {
            ReadOnlyHint = readOnly,
            DestructiveHint = destructive,
            IdempotentHint = readOnly,
            OpenWorldHint = false,
        };
    }
}
