using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 轻量渲染分层计时。设置 WC4_RENDER_PROFILE=0 可关闭。
/// 每 30 帧把各层平均耗时追加到 %TEMP%\wc4-render-profile.log，
/// 用来判断卡顿到底发生在背景、建筑、领地还是 WPF 合成层。
/// </summary>
public static class RenderProfiler
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (double Ms, int Count)> Acc = new();
    private static long _frameStart;
    private static int _frames;
    private static string _tag = "";

    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("WC4_RENDER_PROFILE"), "0", StringComparison.Ordinal);

    public static void BeginFrame(string tag)
    {
        if (!Enabled) return;
        _tag = tag;
        _frameStart = Stopwatch.GetTimestamp();
    }

    public static void Add(string name, long startTimestamp)
    {
        if (!Enabled) return;
        double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        lock (Gate)
        {
            Acc.TryGetValue(name, out var item);
            Acc[name] = (item.Ms + ms, item.Count + 1);
        }
    }

    public static void EndFrame()
    {
        if (!Enabled) return;

        double total = (Stopwatch.GetTimestamp() - _frameStart) * 1000.0 / Stopwatch.Frequency;
        lock (Gate)
        {
            Acc.TryGetValue("total", out var t);
            Acc["total"] = (t.Ms + total, t.Count + 1);
            _frames++;

            if (_frames % 5 != 0) return;

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" tag=").Append(_tag);
            foreach (var kvp in Acc.OrderByDescending(k => k.Value.Ms))
            {
                double avg = kvp.Value.Count == 0 ? 0 : kvp.Value.Ms / kvp.Value.Count;
                sb.Append(' ').Append(kvp.Key).Append('=').Append(avg.ToString("0.00"));
            }
            sb.AppendLine();

            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "wc4-render-profile.log"), sb.ToString());
            }
            catch
            {
                // 诊断日志失败不影响渲染
            }

            Acc.Clear();
            _frames = 0;
        }
    }
}
