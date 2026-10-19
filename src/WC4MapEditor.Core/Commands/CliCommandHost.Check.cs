using System.CommandLine;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Check 命令组（从原单文件按命令组拆出，内容未改）。
public sealed partial class CliCommandHost
{
    /// <summary>
    /// check 命令：检查 btl 文件的格式完整性。
    /// <para>
    /// 直接读文件、不经过编辑模式，所以不需要 <c>WithModifiers</c> 的写入口
    /// （只在"省略路径、用当前地图"时才去问一下 <c>MapData.FilePath</c>）。
    /// </para>
    /// </summary>
    private Command BuildCheckCommand()
    {
        var cmd = new Command("check", "检查 btl 文件格式与玩法规则（省略路径则检查当前地图）");

        var fileArg = new Argument<string?>("file")
        {
            Description = "要检查的 btl 文件路径；省略则检查当前打开的地图文件",
            Arity = ArgumentArity.ZeroOrOne
        };
        cmd.Arguments.Add(fileArg);

        // 报告可能很长（上千条），默认完整输出到控制台；
        // --out 可另外落一份完整报告到文件，便于检索与比对。
        var outOpt = new Option<string?>("--out", "-o")
        {
            Description = "把完整报告写入指定文件（控制台仍会完整输出）"
        };
        cmd.Options.Add(outOpt);

        cmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(fileArg);
            var lines = new List<string>();

            // 统一收集，末尾一次性输出；指定 --out 时再落盘
            void Log(string s) { lines.Add(s); }

            if (string.IsNullOrWhiteSpace(path))
            {
                // 没给路径 → 用当前地图的文件路径
                string? current = null;
                WithModifiers((mapData, _) => current = mapData.FilePath, readOnly: true);

                if (string.IsNullOrWhiteSpace(current))
                {
                    _output.WriteLine("当前地图还没有对应的文件（可能尚未保存或来自新建）。");
                    _output.WriteLine("用法: check <btl文件路径>");
                    return;
                }

                path = current;
            }

            var report = BTLFormatChecker.CheckFile(path!);

            // ToText 已排好版，整段收进 lines
            foreach (var line in report.ToText().Split('\n'))
                Log(line.TrimEnd('\r'));

            // 规则层：玩法规则校验（对齐 Java 版 Wc4StageDAO.check）。
            // Check 内部在副本上跑一遍修复逻辑，因此不会改动文件。
            try
            {
                var ruleIssues = BTLRuleChecker.Check(File.ReadAllBytes(path!));
                Log("");
                if (ruleIssues.Count == 0)
                {
                    Log("[规则] 玩法规则全部通过");
                }
                else
                {
                    Log($"[规则] 发现 {ruleIssues.Count} 处不符合玩法规则（可用 fix 命令修复）：");
                    // 不截断：问题常常上千条，截断会让人误以为只有列出的这些
                    foreach (var issue in ruleIssues)
                        Log($"  [{issue.Level}] {issue.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"[规则] 校验失败: {ex.Message}");
            }

            // 事件引用层：军团 / 关联事件 / 进攻目标格 / 占领事件是否指向存在的对象。需要完整的 MapData：
            // 省略路径时用当前地图，给了路径则按文件头的布局把文件读成 MapData。
            try
            {
                MapData? eventMap = null;
                if (string.IsNullOrWhiteSpace(parseResult.GetValue(fileArg)))
                    WithModifiers((mapData, _) => eventMap = mapData, readOnly: true);
                else
                    eventMap = BtlTypeDetector.Detect(path!) == BTLFileType.Conquest
                        ? ConquestParser.LoadToMapData(path!)
                        : StageParser.LoadToMapData(path!);

                if (eventMap != null)
                {
                    var eventIssues = EventReferenceChecker.Check(eventMap);
                    if (eventIssues.Count == 0)
                    {
                        Log(Loc.T("Cli.Check.EventPassed", "[事件] 引用检查全部通过"));
                    }
                    else
                    {
                        Log(Loc.F("Cli.Check.EventIssues", eventIssues.Count));
                        foreach (var issue in eventIssues)
                            Log($"  [{issue.Level}] {issue.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Loc.F("Cli.Check.EventFailed", ex.Message));
            }

            // 首都是独立列表（MapData.Capitals），判定需要建筑数据，
            // 只能对当前打开的地图做；对文件路径的校验不涉及此项。
            if (string.IsNullOrWhiteSpace(parseResult.GetValue(fileArg)))
            {
                try
                {
                    WithModifiers((mapData, _) =>
                    {
                        var capitalIssues = BTLRuleChecker.CheckCapitals(mapData);
                        if (capitalIssues.Count == 0)
                        {
                            Log("[首都] 全部通过");
                            return;
                        }

                        Log($"[首都] 发现 {capitalIssues.Count} 处问题：");
                        foreach (var issue in capitalIssues)
                            Log($"  {issue.Message}");
                    }, readOnly: true);
                }
                catch (Exception ex)
                {
                    Log($"[首都] 校验失败: {ex.Message}");
                }
            }

            // ---- 统一输出 ----
            foreach (var line in lines)
                _output.WriteLine(line);

            // ---- 可选：落盘完整报告 ----
            string? outPath = parseResult.GetValue(outOpt);
            if (string.IsNullOrWhiteSpace(outPath)) return;

            try
            {
                File.WriteAllLines(outPath, lines);
                _output.WriteLine($"报告已写入：{outPath}（共 {lines.Count} 行）");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"写入报告失败: {ex.Message}");
            }
        });

        return cmd;
    }
}
