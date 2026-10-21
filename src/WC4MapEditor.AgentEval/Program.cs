using System.Text.Json;

// Agent 轨迹统计：读 Logs/Agent/trajectory-*.jsonl，汇总轮数、工具成功率、耗时与失败分布。
// 用法：
//   dotnet run --project src/WC4MapEditor.AgentEval -- [trajectory.jsonl]
// 不带参数时取当前目录 / exe 目录下最新的轨迹文件。

string? path = args.Length > 0 ? args[0] : FindLatest();

if (path == null || !File.Exists(path))
{
    Console.WriteLine("找不到轨迹文件。先让编辑器跑一轮对话，或用参数指定 .jsonl。");
    return 1;
}

int turns = 0, finishedTurns = 0, plans = 0, toolCalls = 0, toolOk = 0, toolFail = 0;
long toolMs = 0, toolMsMax = 0;
int rounds = 0;
var failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var roundHistogram = new Dictionary<int, int>();

foreach (string line in File.ReadLines(path))
{
    if (string.IsNullOrWhiteSpace(line)) continue;

    try
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var data = root.TryGetProperty("data", out var d) ? d : default;

        switch (type)
        {
            case "turn_start":
                turns++;
                break;

            case "turn_end":
                finishedTurns++;
                if (data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("rounds", out var r) && r.TryGetInt32(out int rc))
                {
                    rounds += rc;
                    roundHistogram[rc] = roundHistogram.GetValueOrDefault(rc) + 1;
                }
                break;

            case "plan":
                plans++;
                break;

            case "tool":
                toolCalls++;
                bool ok = data.ValueKind == JsonValueKind.Object &&
                          data.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (ok) toolOk++; else toolFail++;

                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("ms", out var msEl) && msEl.TryGetInt64(out long ms))
                {
                    toolMs += ms;
                    if (ms > toolMsMax) toolMsMax = ms;
                }

                if (!ok && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("name", out var n))
                {
                    string name = n.GetString() ?? "(unknown)";
                    failures[name] = failures.GetValueOrDefault(name) + 1;
                }
                break;
        }
    }
    catch
    {
        // 半行/损坏行跳过
    }
}

Console.WriteLine("== Agent 轨迹统计 ==");
Console.WriteLine("文件:      " + path);
Console.WriteLine($"轮次:      {turns} 次对话（完成 {finishedTurns}）");
Console.WriteLine($"计划:      {plans} 个");
Console.WriteLine($"工具调用:  {toolCalls} 次（成功 {toolOk}，失败 {toolFail}）");
if (toolCalls > 0)
{
    Console.WriteLine($"成功率:    {toolOk * 100.0 / toolCalls:0.0}%");
    Console.WriteLine($"平均耗时:  {toolMs / (double)toolCalls:0} ms，最长 {toolMsMax} ms");
}
if (rounds > 0)
{
    Console.WriteLine($"平均轮数:  {rounds / (double)Math.Max(1, finishedTurns):0.0}");
    Console.WriteLine("轮数分布:  " + string.Join("，", roundHistogram.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}轮×{kv.Value}")));
}
if (failures.Count > 0)
{
    Console.WriteLine("失败工具:");
    foreach (var kv in failures.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {kv.Key,-24} {kv.Value}");
}

return 0;

static string? FindLatest()
{
    foreach (string baseDir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        string dir = Path.Combine(baseDir, "Logs", "Agent");
        if (!Directory.Exists(dir)) continue;

        var file = new DirectoryInfo(dir)
            .GetFiles("trajectory-*.jsonl")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (file != null) return file.FullName;
    }
    return null;
}
