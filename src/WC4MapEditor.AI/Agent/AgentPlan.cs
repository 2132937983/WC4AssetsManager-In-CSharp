using System.Text;
using System.Text.Json;

namespace WC4MapEditor.AI.Agent;

/// <summary>
/// 模型通过 <c>submit_plan</c> 登记的多步计划。
/// <para>
/// 只做"记录 + 回灌给模型"，不在这里执行任何操作；执行仍走正常工具调用。
/// 这样计划是可选的，不会改变简单任务的单轮行为。
/// </para>
/// </summary>
internal sealed class AgentPlan
{
    public string Goal { get; init; } = "";
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public string Verify { get; init; } = "";

    public static AgentPlan? TryParse(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string goal = root.TryGetProperty("goal", out var g) && g.ValueKind == JsonValueKind.String
                ? g.GetString() ?? ""
                : "";

            var steps = new List<string>();
            if (root.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in s.EnumerateArray())
                {
                    string? step = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Object when item.TryGetProperty("step", out var st) && st.ValueKind == JsonValueKind.String
                            => st.GetString(),
                        _ => null,
                    };

                    if (!string.IsNullOrWhiteSpace(step)) steps.Add(step.Trim());
                }
            }

            if (steps.Count == 0) return null;

            string verify = root.TryGetProperty("verify", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";

            return new AgentPlan
            {
                Goal = goal,
                Steps = steps.Take(20).ToList(),
                Verify = verify,
            };
        }
        catch
        {
            return null;
        }
    }

    public string ToPromptText()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Goal)) sb.Append("目标：").AppendLine(Goal);
        for (int i = 0; i < Steps.Count; i++)
            sb.Append(i + 1).Append(". ").AppendLine(Steps[i]);
        if (!string.IsNullOrEmpty(Verify)) sb.Append("验证：").AppendLine(Verify);
        return sb.ToString().TrimEnd();
    }
}
