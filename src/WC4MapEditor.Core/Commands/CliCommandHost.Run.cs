using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Run 命令组（从原单文件按命令组拆出，内容未改）。
public sealed partial class CliCommandHost
{
    private Command BuildRunCommand()
    {
        var cmd = new Command("run", "执行脚本文件(.zme)");

        var fileArg = new Argument<string>("file") { Description = "脚本文件路径(.zme)，支持绝对路径或相对于程序目录的路径" };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            var filePath = parseResult.GetValue(fileArg)!;

            if (!Path.IsPathRooted(filePath))
                filePath = Path.Combine(AppContext.BaseDirectory, filePath);

            if (!filePath.EndsWith(".zme", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine("[脚本] 仅支持 .zme 脚本文件");
                return;
            }

            if (!File.Exists(filePath))
            {
                _output.WriteLine($"[脚本] 文件不存在: {filePath}");
                return;
            }

            _output.WriteLine($"[脚本] 加载脚本文件: {Path.GetFileName(filePath)}");

            var lines = File.ReadAllLines(filePath);
            int executedCount = 0;
            int skippedCount = 0;

            BeginBatchMode();
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    if (string.IsNullOrEmpty(line)) continue;

                    // 支持行尾注释：belong set 1 2 3   # 说明文字
                    line = CommandLineText.StripComment(line);
                    if (string.IsNullOrEmpty(line))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (CommandLineText.IsCommentLine(line))
                    {
                        skippedCount++;
                        continue;
                    }

                    // 统一走 Execute：脚本里的每条命令也就同样支持 --out 落日志
                    Execute(line);

                    executedCount++;
                }
            }
            finally
            {
                EndBatchMode();
            }

            _output.WriteLine($"[脚本] 执行完成: {executedCount} 条命令, {skippedCount} 条注释跳过, 共 {lines.Length} 行");
        });

        return cmd;
    }
}
