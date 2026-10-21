using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

public sealed partial class CliCommandHost : ICommandHost
{
    private static readonly object _lock = new();
    private static CliCommandHost? _instance;

    public static CliCommandHost Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new CliCommandHost();
            }
            return _instance;
        }
    }

    private readonly RootCommand _rootCommand;
    private TextWriter _output = Console.Out;

    private CliCommandHost()
    {
        _rootCommand = BuildRootCommand();
    }

    public void SetOutput(TextWriter output) => _output = output;

    /// <summary>
    /// 执行一条命令。
    /// <para>
    /// 任意命令都可以在末尾加 <c>--out [文件]</c>，把本次输出再落一份到日志文件：
    /// 不给文件名就自动写到程序目录的 <c>Logs\时间戳_命令名.log</c>。
    /// </para>
    /// </summary>
    public int Execute(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);

        // 只有"没有任何命令认领 --out"时才由这里接管。
        // 没把 --out 注册成全局选项，是因为命令里已经有同/近名的选项
        // （verify 自带 --out 写报告、terrain resize-map 有 -o/--ocean），
        // 注册成全局选项会和它们的解析打架。
        if (TrySplitOutOption(args, out string? outPath, out string[] rest))
        {
            // 判断 --out 是不是被命令自己认领了：完整参数能解析通过、不抱怨 --out，
            // 就说明是它自己的选项（verify 自带 --out），交给它自己处理；
            // 反之报"无法识别 --out"，就是没人认领，由这里接管。
            bool claimedByCommand = true;
            foreach (var err in _rootCommand.Parse(args).Errors)
            {
                if (err.Message.Contains("--out", StringComparison.Ordinal)) { claimedByCommand = false; break; }
            }

            if (!claimedByCommand)
                return ExecuteWithLogFile(rest, ResolveOutPath(outPath, commandLine), commandLine);
        }

        var parseResult = _rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    /// <summary>
    /// 把 <c>--out</c>（或 <c>--out=路径</c>）从参数里摘出来。
    /// 文件名可省略；省略时 <paramref name="outPath"/> 为 null，由 <see cref="ResolveOutPath"/> 自动命名。
    /// 文件名以 <c>-</c> 开头时请写成 <c>--out=名字</c>。
    /// </summary>
    private static bool TrySplitOutOption(string[] args, out string? outPath, out string[] rest)
    {
        outPath = null;
        bool found = false;
        var restList = new List<string>(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (!found && a == "--out")
            {
                found = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    outPath = args[i + 1];
                    i++;
                }
                continue;
            }

            if (!found && a.StartsWith("--out=", StringComparison.Ordinal))
            {
                found = true;
                string v = a["--out=".Length..];
                outPath = string.IsNullOrWhiteSpace(v) ? null : v;
                continue;
            }

            restList.Add(a);
        }

        rest = restList.ToArray();
        return found;
    }

    /// <summary>
    /// 解析 --out 的目标路径：给了就用（相对路径按程序目录展开），
    /// 没给就自动放到程序目录的 <c>Logs\</c> 下，文件名 = 时间戳 + 命令名。
    /// </summary>
    private static string ResolveOutPath(string? requested, string commandLine)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string p = Path.IsPathRooted(requested) ? requested : Path.Combine(AppContext.BaseDirectory, requested);
            string? dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return p;
        }

        string logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{CommandNameOf(commandLine)}.log");
    }

    /// <summary>取命令行里第一个非选项 token 作为命令名，只用于自动命名日志文件。</summary>
    private static string CommandNameOf(string commandLine)
    {
        foreach (var token in CommandLineToArgs(commandLine))
        {
            if (token.StartsWith('-')) continue;

            var sb = new System.Text.StringBuilder();
            foreach (char c in token)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);

            if (sb.Length > 0) return sb.ToString();
        }
        return "cmd";
    }

    /// <summary>把本次命令的输出同时写到控制台和日志文件。</summary>
    private int ExecuteWithLogFile(string[] args, string outPath, string commandLine)
    {
        var saved = _output;
        StreamWriter? file = null;
        bool ok = false;

        try
        {
            file = new StreamWriter(outPath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
            file.WriteLine("# WC4 Map Editor 命令日志");
            file.WriteLine($"# 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            file.WriteLine($"# 命令: {commandLine}");
            file.WriteLine(new string('-', 60));

            _output = new TeeWriter(saved, file);
            ok = _rootCommand.Parse(args).Invoke() == 0;
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            saved.WriteLine($"[日志] 写入失败: {ex.Message}");
            return 1;
        }
        finally
        {
            _output = saved;

            if (file != null)
            {
                file.WriteLine(new string('-', 60));
                file.WriteLine($"# 结束: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                file.Dispose();
            }

            saved.WriteLine(ok ? $"[日志] 本次输出已写入: {outPath}" : $"[日志] 已写入（命令返回非 0）: {outPath}");
        }
    }

    /// <summary>输出同时写往控制台与日志文件。</summary>
    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _file;

        public TeeWriter(TextWriter console, TextWriter file)
        {
            _console = console;
            _file = file;
        }

        public override System.Text.Encoding Encoding => _console.Encoding;
        public override void Write(char value) { _console.Write(value); _file.Write(value); }
        public override void Write(string? value) { _console.Write(value); _file.Write(value); }
        public override void WriteLine(string? value) { _console.WriteLine(value); _file.WriteLine(value); }
        public override void Flush() { _console.Flush(); _file.Flush(); }
    }

    public (List<string> Errors, string CommandName, string CommandDescription) GetParseErrors(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);
        var parseResult = _rootCommand.Parse(args);
        var errors = new List<string>();
        foreach (var error in parseResult.Errors)
            errors.Add(error.Message);
        var cmd = parseResult.CommandResult.Command;
        return (errors, cmd?.Name ?? "", cmd?.Description ?? "");
    }

    public string GetHelp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CLI修改命令 ===");
        foreach (var sub in _rootCommand.Subcommands)
        {
            sb.AppendLine($"  {sub.Name,-12} - {sub.Description}");
            foreach (var sub2 in sub.Subcommands)
                sb.AppendLine($"    {sub.Name} {sub2.Name,-16} - {sub2.Description}");
        }
        sb.AppendLine();
        sb.AppendLine("示例: building add 0 12 -t 11");
        sb.AppendLine("      building add 87 25 -t 15 -n 北京");
        sb.AppendLine("      building remove-by-probability 30 -k");
        sb.AppendLine("      building list -n            (列出所有有名称的建筑)");
        sb.AppendLine("      belong set 0 12 5");
        sb.AppendLine("      belong copy 0 12");
        sb.AppendLine("      belong paste 5 8");
        sb.AppendLine("      belong remove 3 4");
        sb.AppendLine("      belong clean-orphan");
        sb.AppendLine("      belong randomize");
        sb.AppendLine("      belong batch-set 3");
        sb.AppendLine("      belong get 89 36");
        sb.AppendLine("      belong set-by-city 北京 1");
        sb.AppendLine("      belong set-province 89 36 1");
        sb.AppendLine("      belong set-region 80 40 100 60 2");
        sb.AppendLine("      belong replace 1 2");
        sb.AppendLine("      belong randomize-by 1");
        sb.AppendLine("      belong remove-border-entities");
        sb.AppendLine("      belong remove-border-armies");
        sb.AppendLine("      terrain greening -p 50");
        sb.AppendLine("      army list");
        sb.AppendLine("      army remove 5 8");
        sb.AppendLine("      army set-legion 5 8 3");
        sb.AppendLine("      army set-plan 5 8 2");
        sb.AppendLine("      army legion-strength -1 3");
        sb.AppendLine("      army randomize-legion -1");
        sb.AppendLine("      army generate-by-prob -1 50");
        sb.AppendLine("      army auto-general -1 3");
        sb.AppendLine("      army remove-by-belong 5");
        sb.AppendLine("      army erase-brush 10 10 3");
        sb.AppendLine("      army remove-all");
        sb.AppendLine("      legion list");
        sb.AppendLine("      legion get 1");
        sb.AppendLine("      legion add");
        sb.AppendLine("      legion remove-at 3");
        sb.AppendLine("      legion set-player 1");
        sb.AppendLine("      legion set-economy 100");
        sb.AppendLine("      legion apply-default-colors");
        sb.AppendLine("      legion randomize-levels");
        sb.AppendLine("      legion rebuild-action-belong");
        sb.AppendLine("      terrain change-type 5 8 -d 1");
        sb.AppendLine("      terrain set-river 5 8 -v 3");
        sb.AppendLine("      terrain copy 5 8");
        sb.AppendLine("      terrain paste 10 10");
        sb.AppendLine("      terrain flood-fill 0 0 1");
        sb.AppendLine("      terrain connect-buildings");
        sb.AppendLine("      terrain scale-map 1.5");
        sb.AppendLine("      terrain resize-map down 2 --ocean");
        sb.AppendLine("      terrain set-edit-layer 2");
        sb.AppendLine("      screenshot                                            (整幅地图截图)");
        sb.AppendLine("      screenshot shots\\city.png -c 120 -r 78 --w 40 --h 30  (局部放大：以 120,78 为中心 40×30 格)");
        sb.AppendLine("      screenshotscene                                       (截整个场景/界面，任何场景都能用，不要求地图)");
        sb.AppendLine("      screenshotscene shots\\ui.png -p 2200                   (界面截图，长边放宽到 2200px)");
        sb.AppendLine();
        sb.AppendLine("任意命令都能加 --out，把本次输出另存为日志文件：");
        sb.AppendLine("      building list -n --out             (自动写到 Logs\\时间戳_building.log)");
        sb.AppendLine("      belong stats --out D:\\dump.txt    (写到指定文件)");
        return sb.ToString();
    }

    public Command GetRootCommand() => _rootCommand;

    private static string[] CommandLineToArgs(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in commandLine)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private RootCommand BuildRootCommand()
    {
        var root = new RootCommand("WC4 Map Editor");

        root.Subcommands.Add(BuildTerrainCommand());
        root.Subcommands.Add(BuildProvinceCommand());
        root.Subcommands.Add(BuildBuildingCommand());
        root.Subcommands.Add(BuildBelongCommand());
        root.Subcommands.Add(BuildLegionCommand());
        root.Subcommands.Add(BuildArmyCommand());
        root.Subcommands.Add(BuildTrapCommand());
        root.Subcommands.Add(BuildInfoCommand());
        root.Subcommands.Add(BuildCheckCommand());
        root.Subcommands.Add(BuildFixCommand());
        root.Subcommands.Add(BuildRunCommand());
        root.Subcommands.Add(BuildScreenshotCommand());
        root.Subcommands.Add(BuildScreenshotSceneCommand());

        // 分部文件里标了 [CliSubcommand] 的命令自动注册：新增命令组只要新建一个分部文件，不必改这里
        foreach (var method in typeof(CliCommandHost)
                     .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                     .Select(m => (Method: m, Attr: (CliSubcommandAttribute?)Attribute.GetCustomAttribute(m, typeof(CliSubcommandAttribute))))
                     .Where(x => x.Attr != null)
                     .OrderBy(x => x.Attr!.Order).ThenBy(x => x.Method.Name, StringComparer.Ordinal))
        {
            if (method.Method.Invoke(this, null) is Command extra)
                root.Subcommands.Add(extra);
        }

        return root;
    }
}

/// <summary>
/// 标在 <see cref="CliCommandHost"/> 分部文件里"无参、返回 <see cref="Command"/>"的实例方法上，
/// 该命令会被自动加入根命令（按 <see cref="Order"/>、再按方法名排序）。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CliSubcommandAttribute : Attribute
{
    public int Order { get; init; }
}
