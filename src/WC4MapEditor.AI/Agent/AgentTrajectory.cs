using System.IO;
using System.Text.Json;

namespace WC4MapEditor.AI.Agent;

/// <summary>
/// Agent 轨迹日志（JSONL，一行一个事件）。
/// <para>
/// 目的不是给用户看，而是让"哪一步失败、为什么失败"可复盘：
/// 换提示词 / 换模型 / 换工具实现后，可以用同一批任务重跑并对比轨迹。
/// 文件写在 <c>&lt;exe&gt;/Logs/Agent/trajectory-yyyyMMdd.jsonl</c>（Logs 已被 gitignore）。
/// </para>
/// </summary>
internal static class AgentTrajectory
{
    private static readonly object Gate = new();

    public static void Write(string type, object? data)
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Logs", "Agent");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, $"trajectory-{DateTime.Now:yyyyMMdd}.jsonl");
            string line = JsonSerializer.Serialize(new
            {
                ts = DateTimeOffset.Now,
                type,
                data,
            });

            lock (Gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 轨迹写不进去不能影响对话本身
        }
    }
}
