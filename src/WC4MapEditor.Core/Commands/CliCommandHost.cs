using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

public sealed class CliCommandHost : ICommandHost
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
        // （check 自带 --out 写报告、terrain resize-map 有 -o/--ocean），
        // 注册成全局选项会和它们的解析打架。
        if (TrySplitOutOption(args, out string? outPath, out string[] rest))
        {
            // 判断 --out 是不是被命令自己认领了：完整参数能解析通过、不抱怨 --out，
            // 就说明是它自己的选项（check 自带 --out），交给它自己处理；
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

        return root;
    }

    /// <summary>
    /// screenshot：把地图渲染成 PNG。
    /// 给中心点与宽高就是局部放大图（区域越小格子在图上越大），AI 视觉就靠这个看细节。
    /// </summary>
    private Command BuildScreenshotCommand()
    {
        var cmd = new Command("screenshot", "地图截图存成 PNG（整幅或局部区域，供 AI 视觉 / 人工核对）");

        var pathArg = new Argument<string?>("path")
        {
            Description = "输出 PNG 路径；省略则放到程序目录 screenshots\\ 下自动命名",
            Arity = ArgumentArity.ZeroOrOne
        };
        var colOpt = new Option<int>("--col", "-c") { Description = "区域中心列（省略=整幅地图）", DefaultValueFactory = _ => -1 };
        var rowOpt = new Option<int>("--row", "-r") { Description = "区域中心行（省略=整幅地图）", DefaultValueFactory = _ => -1 };
        var wOpt = new Option<int>("--w") { Description = "区域宽（格）", DefaultValueFactory = _ => 0 };
        var hOpt = new Option<int>("--h") { Description = "区域高（格）", DefaultValueFactory = _ => 0 };
        var pxOpt = new Option<int>("--pixels", "-p") { Description = "长边像素上限（默认 1400；越小越省视觉 token）", DefaultValueFactory = _ => 1400 };
        var noLabelOpt = new Option<bool>("--no-labels") { Description = "不画坐标标注（默认画，AI 靠它把图像和格子对上）" };
        var gridOpt = new Option<bool>("--grid") { Description = "画六角网格线" };
        var sceneOpt = new Option<bool>("--scene") { Description = "截整个场景（当前窗口界面：工具栏/面板/地图视口/状态栏），而不是离屏渲染地图" };

        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(colOpt);
        cmd.Options.Add(rowOpt);
        cmd.Options.Add(wOpt);
        cmd.Options.Add(hOpt);
        cmd.Options.Add(pxOpt);
        cmd.Options.Add(noLabelOpt);
        cmd.Options.Add(gridOpt);
        cmd.Options.Add(sceneOpt);

        cmd.SetAction(parseResult =>
        {
            // --scene 截的是窗口界面，跟地图数据无关 → 不走 WithModifiers（不要求已加载地图）。
            // 想少打字的话，同一个功能还有个专用命令：screenshotscene。
            if (parseResult.GetValue(sceneOpt))
            {
                CaptureSceneScreenshot(parseResult.GetValue(pathArg), parseResult.GetValue(pxOpt));
                return;
            }

            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(colOpt);
                int row = parseResult.GetValue(rowOpt);

                var result = mgr.CaptureScreenshot(new Services.MapScreenshotRequest
                {
                    CenterCol = col,
                    CenterRow = row,
                    CellsW = parseResult.GetValue(wOpt),
                    CellsH = parseResult.GetValue(hOpt),
                    MaxPixels = parseResult.GetValue(pxOpt),
                    OutputPath = parseResult.GetValue(pathArg),
                    ShowHexLabels = !parseResult.GetValue(noLabelOpt),
                    ShowGridLines = parseResult.GetValue(gridOpt),
                    MarkCenter = col >= 0 && row >= 0,
                });

                if (!result.Ok)
                {
                    _output.WriteLine(result.Message);
                    return;
                }

                _output.WriteLine($"截图已保存: {result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）");
                _output.WriteLine(result.Message);
            }, readOnly: true);
        });

        return cmd;
    }

    /// <summary>
    /// 场景（窗口界面）截图的共用实现：<c>screenshotscene</c> 和 <c>screenshot --scene</c> 都走它。
    /// <para>
    /// 关键点：<b>不经过 <see cref="WithModifiers"/></b>。它要的是"屏幕上现在是什么样"，
    /// 跟地图数据无关，所以没加载地图、甚至在军团 / 国家 / 建筑这类场景里也能跑 ——
    /// 这正是它和 <c>screenshot</c> 的区别。
    /// </para>
    /// </summary>
    private void CaptureSceneScreenshot(string? path, int maxPixels)
    {
        var result = EditModeManager.Instance.CaptureScreenshot(new Services.MapScreenshotRequest
        {
            SceneMode = true,
            MaxPixels = maxPixels,
            OutputPath = path,
        });

        if (!result.Ok)
        {
            _output.WriteLine(result.Message);
            return;
        }

        _output.WriteLine($"场景截图已保存: {result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）");
        _output.WriteLine(result.Message);
    }

    /// <summary>
    /// screenshotscene：截"整个场景"，即当前编辑器窗口的界面（工具栏、编辑面板、地图视口、状态栏）。
    /// <para>
    /// 与 <c>screenshot</c> 的分工：那个渲染的是<b>地图数据</b>（必须先加载地图，且得在地图场景里）；
    /// 这个截的是<b>窗口界面</b>，任何场景、哪怕没打开地图都能用。
    /// </para>
    /// <para>
    /// 局限：主窗口最小化时会失败（会给出提示）；独立弹出的窗口（AI 对话窗、右键菜单）不属于
    /// 主窗口视觉树，不会出现在图里。无界面启动（纯命令行）时没有主窗口，也会明确报"未注册"。
    /// </para>
    /// </summary>
    private Command BuildScreenshotSceneCommand()
    {
        var cmd = new Command("screenshotscene", "截整个场景（编辑器窗口界面）存成 PNG；不要求已加载地图");

        var pathArg = new Argument<string?>("path")
        {
            Description = "输出 PNG 路径；省略则放到程序目录 screenshots\\ 下自动命名",
            Arity = ArgumentArity.ZeroOrOne
        };
        var pxOpt = new Option<int>("--pixels", "-p")
        {
            Description = "长边像素上限（默认 1400；越小越省视觉 token）",
            DefaultValueFactory = _ => 1400
        };

        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(pxOpt);

        cmd.SetAction(parseResult =>
            CaptureSceneScreenshot(parseResult.GetValue(pathArg), parseResult.GetValue(pxOpt)));

        return cmd;
    }

    private Command BuildTerrainCommand()
    {
        var cmd = new Command("terrain", "地形修改操作");

        var greeningCmd = new Command("greening", "绿化平地");
        var greeningProbOpt = new Option<int>("--probability", "-p") { Description = "绿化概率(0-100)", DefaultValueFactory = _ => 50 };
        greeningCmd.Options.Add(greeningProbOpt);
        greeningCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(greeningProbOpt), 0, 100);
                var result = terrain.ApplyGreening(prob);
                _output.WriteLine(result.Message ?? $"绿化完成，概率={prob}%");
            });
        });

        var randomFlatCmd = new Command("random-flat", "随机平地变体");
        var randomFlatProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        randomFlatCmd.Options.Add(randomFlatProbOpt);
        randomFlatCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(randomFlatProbOpt), 0, 100);
                var result = terrain.RandomizeFlatTerrain(prob);
                _output.WriteLine(result.Message ?? $"随机平地变体完成，概率={prob}%");
            });
        });

        var randomVariantCmd = new Command("random-variant", "随机当前层变体");
        var randomVariantProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        var randomVariantLayerOpt = new Option<int>("--layer", "-l") { Description = "编辑层(1-3)", DefaultValueFactory = _ => 1 };
        randomVariantCmd.Options.Add(randomVariantProbOpt);
        randomVariantCmd.Options.Add(randomVariantLayerOpt);
        randomVariantCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(randomVariantLayerOpt), 1, 3);
                int prob = Math.Clamp(parseResult.GetValue(randomVariantProbOpt), 0, 100);
                var result = terrain.RandomizeVariant(prob);
                _output.WriteLine(result.Message ?? $"随机变体完成，编辑层={terrain.EditLayer}，概率={prob}%");
            });
        });

        var createCoastCmd = new Command("create-coast", "创建海岸线");
        createCoastCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.CreateCoast();
                _output.WriteLine(result.Message ?? "海岸线创建完成");
            });
        });

        var processOceanCmd = new Command("process-ocean-layer2", "处理海洋第二层");
        processOceanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.ProcessOceanSecondLayer();
                _output.WriteLine(result.Message ?? "海洋第二层处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充地形");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        var fillTypeArg = new Argument<int>("type") { Description = "替换地形类型" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.Arguments.Add(fillTypeArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg), (byte)parseResult.GetValue(fillTypeArg));
                _output.WriteLine(result.Message ?? "洪水填充完成");
            });
        });

        cmd.Subcommands.Add(greeningCmd);
        cmd.Subcommands.Add(randomFlatCmd);
        cmd.Subcommands.Add(randomVariantCmd);
        cmd.Subcommands.Add(createCoastCmd);
        cmd.Subcommands.Add(processOceanCmd);
        cmd.Subcommands.Add(floodFillCmd);

        var changeTypeCmd = new Command("change-type", "切换地形类型");
        var ctCol = new Argument<int>("col") { Description = "列" };
        var ctRow = new Argument<int>("row") { Description = "行" };
        var ctDeltaOpt = new Option<int>("--delta", "-d") { Description = "变化量(正数递增/负数递减)", DefaultValueFactory = _ => 1 };
        changeTypeCmd.Arguments.Add(ctCol);
        changeTypeCmd.Arguments.Add(ctRow);
        changeTypeCmd.Options.Add(ctDeltaOpt);
        changeTypeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(ctCol);
                int row = parseResult.GetValue(ctRow);
                int delta = parseResult.GetValue(ctDeltaOpt);
                var result = terrain.ChangeTerrainType(col, row, delta);
                _output.WriteLine(result.Message ?? $"已切换地形类型 ({col},{row}) delta={delta}");
            });
        });

        var changeDecoCmd = new Command("change-decoration", "切换地形变体");
        var cdCol = new Argument<int>("col") { Description = "列" };
        var cdRow = new Argument<int>("row") { Description = "行" };
        var cdDeltaOpt = new Option<int>("--delta", "-d") { Description = "变化量", DefaultValueFactory = _ => 1 };
        changeDecoCmd.Arguments.Add(cdCol);
        changeDecoCmd.Arguments.Add(cdRow);
        changeDecoCmd.Options.Add(cdDeltaOpt);
        changeDecoCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(cdCol);
                int row = parseResult.GetValue(cdRow);
                int delta = parseResult.GetValue(cdDeltaOpt);
                var result = terrain.ChangeDecoration(col, row, delta);
                _output.WriteLine(result.Message ?? $"已切换变体 ({col},{row}) delta={delta}");
            });
        });

        var setRiverCmd = new Command("set-river", "设置河流值");
        var srCol = new Argument<int>("col") { Description = "列" };
        var srRow = new Argument<int>("row") { Description = "行" };
        var srValOpt = new Option<int>("--value", "-v") { Description = "河流值(0-63)", DefaultValueFactory = _ => 1 };
        setRiverCmd.Arguments.Add(srCol);
        setRiverCmd.Arguments.Add(srRow);
        setRiverCmd.Options.Add(srValOpt);
        setRiverCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(srCol);
                int row = parseResult.GetValue(srRow);
                byte val = (byte)Math.Clamp(parseResult.GetValue(srValOpt), 0, 63);
                var result = terrain.SetRiverValue(col, row, val);
                _output.WriteLine(result.Message ?? $"已设置河流值 ({col},{row})={val}");
            });
        });

        var copyCmd = new Command("copy", "复制地形");
        var cpCol = new Argument<int>("col") { Description = "列" };
        var cpRow = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(cpCol);
        copyCmd.Arguments.Add(cpRow);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(cpCol);
                int row = parseResult.GetValue(cpRow);
                var result = terrain.CopyTerrain(col, row);
                _output.WriteLine(result.Message ?? $"已复制地形 ({col},{row})");
            });
        });

        var pasteCmd = new Command("paste", "粘贴地形");
        var psCol = new Argument<int>("col") { Description = "列" };
        var psRow = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(psCol);
        pasteCmd.Arguments.Add(psRow);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(psCol);
                int row = parseResult.GetValue(psRow);
                var result = terrain.PasteTerrain(col, row);
                _output.WriteLine(result.Message ?? $"已粘贴地形 ({col},{row})");
            });
        });

        var removeCmd = new Command("remove", "重置编辑层地形");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int col = parseResult.GetValue(rmCol);
                int row = parseResult.GetValue(rmRow);
                var result = terrain.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已重置地形 ({col},{row})");
            });
        });

        var exportHdCmd = new Command("export-hd", "导出HD文件");
        exportHdCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.ExportHdFile();
                _output.WriteLine(result.Message ?? "HD文件导出完成");
            });
        });

        var connectBldCmd = new Command("connect-buildings", "连接建筑(A*寻路)");
        connectBldCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.ConnectBuildings();
                _output.WriteLine(result.Message ?? "建筑连接完成");
            });
        });

        var setLayerCmd = new Command("set-edit-layer", "设置编辑层");
        var slLayer = new Argument<int>("layer") { Description = "编辑层(1-3)" };
        setLayerCmd.Arguments.Add(slLayer);
        setLayerCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(slLayer), 1, 3);
                _output.WriteLine($"已设置编辑层={terrain.EditLayer}");
            });
        });

        var scaleMapCmd = new Command("scale-map", "按比例缩放地图");
        var smScale = new Argument<double>("scale") { Description = "缩放比例(0.1-10.0)" };
        scaleMapCmd.Arguments.Add(smScale);
        scaleMapCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                double scale = Math.Clamp(parseResult.GetValue(smScale), 0.1, 10.0);
                var result = terrain.ScaleMap(scale);
                _output.WriteLine(result.Message ?? $"已缩放地图 scale={scale:F2}");
            });
        });

        var resizeMapCmd = new Command("resize-map", "调整地图大小");
        var rmDir = new Argument<string>("direction") { Description = "方向(up/down/left/right)" };
        var rmAmount = new Argument<int>("amount") { Description = "程度(正数扩展/负数收缩)" };
        var rmOceanOpt = new Option<bool>("--ocean", "-o") { Description = "扩展填充海洋" };
        resizeMapCmd.Arguments.Add(rmDir);
        resizeMapCmd.Arguments.Add(rmAmount);
        resizeMapCmd.Options.Add(rmOceanOpt);
        resizeMapCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                string dir = parseResult.GetValue(rmDir) ?? "up";
                int amount = parseResult.GetValue(rmAmount);
                bool ocean = parseResult.GetValue(rmOceanOpt);
                var result = terrain.ResizeMap(dir, amount, ocean);
                _output.WriteLine(result.Message ?? $"已调整地图 {dir} {amount} ocean={ocean}");
            });
        });

        cmd.Subcommands.Add(changeTypeCmd);
        cmd.Subcommands.Add(changeDecoCmd);
        cmd.Subcommands.Add(setRiverCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(exportHdCmd);
        cmd.Subcommands.Add(connectBldCmd);
        cmd.Subcommands.Add(setLayerCmd);
        cmd.Subcommands.Add(scaleMapCmd);
        cmd.Subcommands.Add(resizeMapCmd);

        var getCmd = new Command("get", "查询指定格子的地形数据");
        var getColArg = new Argument<int>("col") { Description = "列坐标" };
        var getRowArg = new Argument<int>("row") { Description = "行坐标" };
        getCmd.Arguments.Add(getColArg);
        getCmd.Arguments.Add(getRowArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(getColArg);
                int row = parseResult.GetValue(getRowArg);
                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }
                ref var t = ref mapData.GetTerrainRef(col, row);
                _output.WriteLine($"({col},{row}) 地形数据:");
                _output.WriteLine($"  第一层: 类型={t.TileType1} 装饰={t.DecorationType1}");
                _output.WriteLine($"  第二层: 类型={t.TileType2} 装饰={t.DecorationType2}");
                _output.WriteLine($"  第三层: 类型={t.TileType3} 装饰={t.DecorationType3}");
                _output.WriteLine($"  河流值={t.RiverValue}");
            }, readOnly: true);
        });

        var statsCmd = new Command("stats", "统计全图地形类型分布");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var typeCount = new Dictionary<byte, int>();
                for (int i = 0; i < mapData.MapWidth * mapData.MapHeight; i++)
                {
                    byte t = mapData.GetTerrainRef(i).TileType1;
                    if (!typeCount.ContainsKey(t)) typeCount[t] = 0;
                    typeCount[t]++;
                }
                _output.WriteLine($"地形类型分布 (共 {mapData.MapWidth * mapData.MapHeight} 格):");
                _output.WriteLine();
                foreach (var kvp in typeCount.OrderBy(k => k.Key))
                {
                    double pct = kvp.Value * 100.0 / (mapData.MapWidth * mapData.MapHeight);
                    _output.WriteLine($"  类型 {kvp.Key,3}: {kvp.Value,6} 格 ({pct,5:F1}%)");
                }
            }, readOnly: true);
        });

        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(statsCmd);

        return cmd;
    }

    private Command BuildProvinceCommand()
    {
        var cmd = new Command("province", "省份修改操作");

        var clearCmd = new Command("clear", "清空所有省份");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ClearAllProvinces();
                _output.WriteLine(result.Message ?? "所有省份已清空");
            });
        });

        var generateCmd = new Command("generate", "生成孤立省会省区");
        generateCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.GenerateProvincesForIsolatedCapitals();
                _output.WriteLine(result.Message ?? "孤立省会省区生成完成");
            });
        });

        var expandCmd = new Command("expand", "扩展省区填满地图");
        expandCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ExpandAllProvincesToFillMap();
                _output.WriteLine(result.Message ?? "省区扩展完成");
            });
        });

        var processCmd = new Command("process", "处理孤立和空白省区");
        processCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ProcessIsolatedAndEmptyProvinces();
                _output.WriteLine(result.Message ?? "孤立和空白省区处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充省份");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg));
                _output.WriteLine(result.Message ?? "省份洪水填充完成");
            });
        });

        // province set：精确写单格省区值（与 Lua 的 map.SetProvince 同一实现）
        var setValueCmd = new Command("set", "设置指定格子的省区值(0-65535，65535=无省区)");
        var svColArg = new Argument<int>("col") { Description = "列" };
        var svRowArg = new Argument<int>("row") { Description = "行" };
        var svValueArg = new Argument<int>("value") { Description = "省区值(0-65535；65535=无省区)" };
        setValueCmd.Arguments.Add(svColArg);
        setValueCmd.Arguments.Add(svRowArg);
        setValueCmd.Arguments.Add(svValueArg);
        setValueCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetProvince(
                parseResult.GetValue(svColArg), parseResult.GetValue(svRowArg), parseResult.GetValue(svValueArg)))));

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(expandCmd);
        cmd.Subcommands.Add(processCmd);
        cmd.Subcommands.Add(floodFillCmd);
        cmd.Subcommands.Add(setValueCmd);

        return cmd;
    }

    private Command BuildBuildingCommand()
    {
        var cmd = new Command("building", "建筑修改操作");

        var addCmd = new Command("add", "在指定坐标添加建筑");
        var addColArg = new Argument<int>("col") { Description = "列坐标" };
        var addRowArg = new Argument<int>("row") { Description = "行坐标" };
        var addTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型(11=一级城,12=二级城,...,15=五级城)", DefaultValueFactory = _ => 11 };
        var addNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称,默认0xFFFF=无名称)", DefaultValueFactory = _ => "-1" };
        var addIndexOpt = new Option<int>("--index", "-i") { Description = "格子索引(优先于坐标)" };
        addCmd.Arguments.Add(addColArg);
        addCmd.Arguments.Add(addRowArg);
        addCmd.Options.Add(addTypeOpt);
        addCmd.Options.Add(addNameOpt);
        addCmd.Options.Add(addIndexOpt);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(addColArg);
                int row = parseResult.GetValue(addRowArg);
                int buildingType = parseResult.GetValue(addTypeOpt);
                string nameInput = parseResult.GetValue(addNameOpt) ?? "-1";
                ushort name = BuildingNameResolver.Resolve(nameInput);

                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }

                int coordIndex = row * mapData.MapWidth + col;
                var b = Building.CreateDefault(coordIndex);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (coordIndex > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {coordIndex} 超出文件格式上限 65535，保存时将被截断为 {coordIndex & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在 ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var addByIdxCmd = new Command("add-by-idx", "按格子索引添加建筑");
        var addIdxArg = new Argument<int>("index") { Description = "格子索引" };
        var addIdxTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型", DefaultValueFactory = _ => 11 };
        var addIdxNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称)", DefaultValueFactory = _ => "-1" };
        addByIdxCmd.Arguments.Add(addIdxArg);
        addByIdxCmd.Options.Add(addIdxTypeOpt);
        addByIdxCmd.Options.Add(addIdxNameOpt);
        addByIdxCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int idx = parseResult.GetValue(addIdxArg);
                int buildingType = parseResult.GetValue(addIdxTypeOpt);
                string nameInput = parseResult.GetValue(addIdxNameOpt) ?? "-1";
                ushort name = BuildingNameResolver.Resolve(nameInput);

                if (idx < 0 || idx >= mapData.MapWidth * mapData.MapHeight)
                {
                    _output.WriteLine($"索引 {idx} 超出范围 (0-{mapData.MapWidth * mapData.MapHeight - 1})");
                    return;
                }

                int col = idx % mapData.MapWidth;
                int row = idx / mapData.MapWidth;
                var b = Building.CreateDefault(idx);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (idx > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {idx} 超出文件格式上限 65535，保存时将被截断为 {idx & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在索引{idx} ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var removeCmd = new Command("remove", "删除指定坐标的建筑");
        var rmColArg = new Argument<int>("col") { Description = "列坐标" };
        var rmRowArg = new Argument<int>("row") { Description = "行坐标" };
        removeCmd.Arguments.Add(rmColArg);
        removeCmd.Arguments.Add(rmRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(rmColArg);
                int row = parseResult.GetValue(rmRowArg);
                var result = building.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已删除 ({col},{row}) 的建筑");
            });
        });

        var removeAllCmd = new Command("remove-all", "删除所有建筑");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                building.RemoveAll();
                _output.WriteLine("所有建筑已删除");
            });
        });

        var removeNonCapitalCmd = new Command("remove-non-capital", "删除非首都建筑");
        removeNonCapitalCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RemoveNonCapitalBuildings(mapData);
                _output.WriteLine($"已删除 {count} 个非首都建筑");
            });
        });

        var randomizeNamedCmd = new Command("randomize-named", "随机有名称建筑类型");
        randomizeNamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                var result = building.RandomizeNamedBuildingTypes(useCondition: false);
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeUnnamedCmd = new Command("randomize-unnamed", "随机无名称建筑类型");
        randomizeUnnamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                var result = building.RandomizeUnnamedBuildingTypes(useCondition: false);
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeByBelongCmd = new Command("randomize-by-belong", "按归属随机建筑");
        var belongIdArg = new Argument<int>("belongId") { Description = "归属ID" };
        randomizeByBelongCmd.Arguments.Add(belongIdArg);
        randomizeByBelongCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int belongId = parseResult.GetValue(belongIdArg);
                int count = building.RandomizeBuildingsByBelong(belongId);
                _output.WriteLine($"已按归属{belongId}随机 {count} 个建筑");
            });
        });

        var generateCapitalsCmd = new Command("generate-capitals", "为所有建筑生成首都");
        generateCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.GenerateCapitalsForAllBuildings(mapData);
                _output.WriteLine($"已生成 {count} 个首都");
            });
        });

        var randomizeOnCapitalsCmd = new Command("randomize-on-capitals", "在首都随机建筑");
        randomizeOnCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeBuildingsOnCapitals(mapData);
                _output.WriteLine($"已在首都随机 {count} 个建筑");
            });
        });

        var smartAppearanceCmd = new Command("smart-appearance", "智能设置建筑外观");
        smartAppearanceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.SmartSetBuildingAppearance(mapData);
                _output.WriteLine($"已智能设置 {count} 个建筑外观");
            });
        });

        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(addByIdxCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(removeNonCapitalCmd);
        cmd.Subcommands.Add(randomizeNamedCmd);
        cmd.Subcommands.Add(randomizeUnnamedCmd);
        cmd.Subcommands.Add(randomizeByBelongCmd);
        cmd.Subcommands.Add(generateCapitalsCmd);
        cmd.Subcommands.Add(randomizeOnCapitalsCmd);
        var removeByProbCmd = new Command("remove-by-probability", "按概率删除建筑（每个建筑独立掷骰）");
        var removeProbArg = new Argument<int>("probability") { Description = "删除概率(0-100)" };
        var removeKeepNamedOpt = new Option<bool>("--keep-named", "-k") { Description = "保留有名称的建筑（城市/据点）" };
        removeByProbCmd.Arguments.Add(removeProbArg);
        removeByProbCmd.Options.Add(removeKeepNamedOpt);
        removeByProbCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(removeProbArg), 0, 100);
                bool keepNamed = parseResult.GetValue(removeKeepNamedOpt);

                ModifierResult result = default;
                RecordEntityChange($"按 {prob}% 概率删除建筑",
                    () => result = building.RemoveBuildingsByProbability(prob, keepNamed));
                _output.WriteLine(result.Message ?? $"已删除 {result.AffectedCount} 个建筑");
            });
        });

        cmd.Subcommands.Add(smartAppearanceCmd);
        cmd.Subcommands.Add(removeByProbCmd);

        var listCmd = new Command("list", "列出所有建筑");
        var listTypeOpt = new Option<int>("--type", "-t") { Description = "按建筑类型过滤(0-255)", DefaultValueFactory = _ => -1 };
        var listNamedOpt = new Option<bool>("--named", "-n") { Description = "只显示有名称的建筑" };
        var listBelongOpt = new Option<int>("--belong", "-b") { Description = "按归属过滤(0-255)", DefaultValueFactory = _ => -1 };
        listCmd.Options.Add(listTypeOpt);
        listCmd.Options.Add(listNamedOpt);
        listCmd.Options.Add(listBelongOpt);
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int filterType = parseResult.GetValue(listTypeOpt);
                bool onlyNamed = parseResult.GetValue(listNamedOpt);
                int filterBelong = parseResult.GetValue(listBelongOpt);

                Dictionary<int, string> nameMap = new();
                try
                {
                    var parser = ConfigManager.Instance.GetStringTableParser();
                    foreach (var kvp in parser.FindCityNames())
                        nameMap[kvp.Key] = kvp.Value;
                }
                catch { }

                int count = 0;
                foreach (var b in mapData.Buildings)
                {
                    int col = mapData.MapWidth > 0 ? b.Coordinate % mapData.MapWidth : 0;
                    int row = mapData.MapWidth > 0 ? b.Coordinate / mapData.MapWidth : 0;

                    if (filterType >= 0 && b.BuildingType != filterType)
                        continue;

                    int belong = mapData.GetBelongValue(col, row);
                    if (filterBelong >= 0 && belong != filterBelong)
                        continue;

                    bool hasName = b.Name != 0 && b.Name != BuildingNameResolver.NoName;
                    if (onlyNamed && !hasName)
                        continue;

                    string name = hasName && nameMap.TryGetValue(b.Name, out var n) ? n : "";
                    string typeName = b.GetBuildingTypeName();
                    string belongStr = belong == 0xFF ? "无" : belong.ToString();
                    string nameStr = hasName ? (name.Length > 0 ? name : $"#{b.Name}") : "无";

                    _output.WriteLine($"({col,3},{row,3}) 类型={b.BuildingType}({typeName}) 名称={nameStr} 归属={belongStr}");
                    count++;
                }

                if (count == mapData.Buildings.Count)
                    _output.WriteLine($"共 {count} 个建筑");
                else
                    _output.WriteLine($"共 {count} 个建筑（筛选自 {mapData.Buildings.Count}）");
            }, readOnly: true);
        });

        // ---- 对象属性读写（与 Lua 的 SetBuildingField 同一实现）----
        var fieldsCmd = new Command("fields", "列出建筑可写的字段名");
        fieldsCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("建筑可写字段：" + string.Join(", ", ObjectApi(mapData).GetBuildingFields())),
            readOnly: true));

        var setFieldCmd = new Command("set-field", "按字段名改建筑属性（字段名见 building fields）");
        var sfColArg = new Argument<int>("col") { Description = "列" };
        var sfRowArg = new Argument<int>("row") { Description = "行" };
        var sfFieldArg = new Argument<string>("field") { Description = "字段名，如 FactoryLevel / KeyPoint / Appearance" };
        var sfValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位）" };
        setFieldCmd.Arguments.Add(sfColArg);
        setFieldCmd.Arguments.Add(sfRowArg);
        setFieldCmd.Arguments.Add(sfFieldArg);
        setFieldCmd.Arguments.Add(sfValueArg);
        setFieldCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetBuildingField(
                parseResult.GetValue(sfColArg), parseResult.GetValue(sfRowArg),
                parseResult.GetValue(sfFieldArg), parseResult.GetValue(sfValueArg)))));

        var showCmd = new Command("show", "查看指定格子的建筑对象（全部属性）");
        var shColArg = new Argument<int>("col") { Description = "列" };
        var shRowArg = new Argument<int>("row") { Description = "行" };
        showCmd.Arguments.Add(shColArg);
        showCmd.Arguments.Add(shRowArg);
        showCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(shColArg);
            int row = parseResult.GetValue(shRowArg);
            var b = ObjectApi(mapData).GetBuilding(col, row);
            if (b == null)
            {
                _output.WriteLine($"({col},{row}) 没有建筑");
                return;
            }

            _output.WriteLine($"建筑 ({b.Col},{b.Row}) 类型={b.BuildingTypeName}({b.BuildingType}) 名称={(b.HasName ? b.Name : "(无)")} 名称ID={b.NameId} 归属={b.Belong}");
            _output.WriteLine($"  外观={b.Appearance} 地标={b.LandmarkBuilding} 装饰={b.DecorativeBuilding} 技能解锁={b.SkillUnlock} 奖励={b.RewardCount} 仇恨={b.HatredValue}");
            _output.WriteLine($"  据点={b.KeyPointName}({b.KeyPoint}) 占领事件={b.OccupationEvent} 起火={b.FireIgnition} 燃烧={b.FireDuration}");
            _output.WriteLine($"  防空 武器={b.AirDefenseWeapon} 雷达={b.AirDefenseRadarName}({b.AirDefenseRadar})");
            _output.WriteLine($"  等级 工厂(工业)={b.FactoryLevel}/4 研究所(科研)={b.ResearchLevel}/4 补给站(医疗)={b.MedicalLevel}/4 机场(航空)={b.AviationLevel}/4 导弹={b.MissileLevel}/2 核弹={b.NuclearLevel}/2");
        }, readOnly: true));

        var setTypeCmd = new Command("set-type", "改建筑类型（0-255）");
        var stColArg = new Argument<int>("col") { Description = "列" };
        var stRowArg = new Argument<int>("row") { Description = "行" };
        var stTypeArg = new Argument<int>("type") { Description = "建筑类型，如 13=三级城" };
        setTypeCmd.Arguments.Add(stColArg);
        setTypeCmd.Arguments.Add(stRowArg);
        setTypeCmd.Arguments.Add(stTypeArg);
        setTypeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetBuildingType(
                parseResult.GetValue(stColArg), parseResult.GetValue(stRowArg), parseResult.GetValue(stTypeArg)))));

        var setNameCmd = new Command("set-name", "改建筑名称（名称留空或 -1 = 清除）");
        var snColArg = new Argument<int>("col") { Description = "列" };
        var snRowArg = new Argument<int>("row") { Description = "行" };
        var snNameArg = new Argument<string?>("name") { Description = "数字ID 或 中文名称；省略=清除", Arity = ArgumentArity.ZeroOrOne };
        setNameCmd.Arguments.Add(snColArg);
        setNameCmd.Arguments.Add(snRowArg);
        setNameCmd.Arguments.Add(snNameArg);
        setNameCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetBuildingName(
                parseResult.GetValue(snColArg), parseResult.GetValue(snRowArg), parseResult.GetValue(snNameArg)))));

        var moveCmd = new Command("move", "把建筑从一格移到另一格（源格归属一并剪切）");
        var mvFromColArg = new Argument<int>("fromCol") { Description = "源列" };
        var mvFromRowArg = new Argument<int>("fromRow") { Description = "源行" };
        var mvToColArg = new Argument<int>("toCol") { Description = "目标列" };
        var mvToRowArg = new Argument<int>("toRow") { Description = "目标行" };
        moveCmd.Arguments.Add(mvFromColArg);
        moveCmd.Arguments.Add(mvFromRowArg);
        moveCmd.Arguments.Add(mvToColArg);
        moveCmd.Arguments.Add(mvToRowArg);
        moveCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).MoveBuilding(
                parseResult.GetValue(mvFromColArg), parseResult.GetValue(mvFromRowArg),
                parseResult.GetValue(mvToColArg), parseResult.GetValue(mvToRowArg)))));

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(fieldsCmd);
        cmd.Subcommands.Add(setFieldCmd);
        cmd.Subcommands.Add(setTypeCmd);
        cmd.Subcommands.Add(setNameCmd);
        cmd.Subcommands.Add(moveCmd);

        return cmd;
    }

    private Command BuildBelongCommand()
    {
        var cmd = new Command("belong", "归属修改操作");

        var clearCmd = new Command("clear", "清空所有归属");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清空所有归属", () => count = belong.ClearAllBelongs());
                _output.WriteLine($"已清空 {count} 个格子的归属");
            });
        });

        var setCmd = new Command("set", "设置指定格子归属");
        var setColArg = new Argument<int>("col") { Description = "列" };
        var setRowArg = new Argument<int>("row") { Description = "行" };
        var setBelongArg = new Argument<int>("belong") { Description = "归属值(0-255, 255=无归属)" };
        setCmd.Arguments.Add(setColArg);
        setCmd.Arguments.Add(setRowArg);
        setCmd.Arguments.Add(setBelongArg);
        setCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(setColArg);
                int row = parseResult.GetValue(setRowArg);
                int belongVal = Math.Clamp(parseResult.GetValue(setBelongArg), 0, 255);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"设置归属 ({col},{row})",
                    () => result = belong.SetBelongByCountryId(col, row, belongVal));
                _output.WriteLine(result.Message ?? $"已设置归属 ({col},{row}) = {belongVal}");
            });
        });

        var fillCmd = new Command("fill-from-legion", "按军团ID填充归属到建筑格子");
        var fillLegionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        fillCmd.Arguments.Add(fillLegionIdArg);
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int legionId = parseResult.GetValue(fillLegionIdArg);
                int count = 0;
                RecordBelongChange($"按军团填充归属 ({legionId})", () =>
                {
                    foreach (var b in mapData.Buildings)
                    {
                        int col = b.Coordinate % mapData.MapWidth;
                        int row = b.Coordinate / mapData.MapWidth;
                        if (mapData.GetBelongValue(col, row) != legionId)
                        {
                            mapData.SetBelongValue(col, row, legionId);
                            count++;
                        }
                    }
                });
                _output.WriteLine($"已将 {count} 个建筑格子归属设为军团 {legionId}");
            });
        });

        var statsCmd = new Command("stats", "归属统计");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belongCounts = new Dictionary<int, int>();
                for (int i = 0; i < mapData.Belongs.Count; i++)
                {
                    int val = mapData.GetBelongValueByIndex(i);
                    if (val != 0xFF)
                    {
                        if (!belongCounts.ContainsKey(val)) belongCounts[val] = 0;
                        belongCounts[val]++;
                    }
                }
                _output.WriteLine("归属值分布:");
                foreach (var kv in belongCounts.OrderBy(x => x.Key))
                {
                    string name = "";
                    var legionInfo = mapData.ResolveLegionByBelong(kv.Key);
                    if (legionInfo != null) name = $" ({legionInfo.Value.DisplayName})";
                    _output.WriteLine($"  归属={kv.Key}{name}: {kv.Value} 格");
                }
                int emptyCount = mapData.Belongs.Count - belongCounts.Values.Sum();
                _output.WriteLine($"  无归属(0xFF): {emptyCount} 格");
            }, readOnly: true);
        });

        var removeCmd = new Command("remove", "删除指定格子归属");
        var removeColArg = new Argument<int>("col") { Description = "列" };
        var removeRowArg = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(removeColArg);
        removeCmd.Arguments.Add(removeRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(removeColArg);
                int row = parseResult.GetValue(removeRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"删除归属 ({col},{row})",
                    () => result = belong.Remove(col, row));
                _output.WriteLine(result.Message ?? $"已删除归属 ({col},{row})");
            });
        });

        var copyCmd = new Command("copy", "复制指定格子归属值");
        var copyColArg = new Argument<int>("col") { Description = "列" };
        var copyRowArg = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(copyColArg);
        copyCmd.Arguments.Add(copyRowArg);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(copyColArg);
                int row = parseResult.GetValue(copyRowArg);
                var result = belong.CopyBelongValue(col, row);
                _output.WriteLine(result.Message ?? $"已复制归属 ({col},{row})");
            }, readOnly: true);
        });

        var pasteCmd = new Command("paste", "粘贴归属值到指定格子");
        var pasteColArg = new Argument<int>("col") { Description = "列" };
        var pasteRowArg = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(pasteColArg);
        pasteCmd.Arguments.Add(pasteRowArg);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(pasteColArg);
                int row = parseResult.GetValue(pasteRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"粘贴归属 ({col},{row})",
                    () => result = belong.PasteBelongValue(col, row));
                _output.WriteLine(result.Message ?? $"已粘贴归属 ({col},{row})");
            });
        });

        var cleanOrphanCmd = new Command("clean-orphan", "清理孤立归属(无建筑/单位的格子)");
        cleanOrphanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清理孤立归属", () => count = belong.CleanOrphanBelongs());
                _output.WriteLine($"已清理 {count} 个孤立归属");
            });
        });

        var randomizeCmd = new Command("randomize", "随机化所有归属");
        randomizeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("随机化所有归属",
                    () => count = belong.RandomizeBelongs(new Random()));
                _output.WriteLine($"已随机化 {count} 个格子的归属");
            });
        });

        // 注意：这个命令改的是"已经有归属的格子"（0xFF 与 0 保持不动），
        // 并不是按省区操作 —— 需要按省区请用 belong set-province，
        // 需要连无归属格子一起刷请用 belong set-region。
        var batchSetCmd = new Command("batch-set", "把所有【已有归属】的格子统一改为目标ID（无归属格子不动）");
        var batchTargetArg = new Argument<int>("targetId") { Description = "目标国家ID(0-255)" };
        batchSetCmd.Arguments.Add(batchTargetArg);
        batchSetCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(batchTargetArg), 0, 255);
                int count = 0;
                RecordBelongChange($"批量设置归属 ({targetId})",
                    () => count = belong.BatchSetBelongByProvince(targetId));
                _output.WriteLine($"已将 {count} 个已有归属的格子改为国家ID {targetId}");
            });
        });

        var removeBorderEntitiesCmd = new Command("remove-border-entities", "删除归属边界上的单位与建筑");
        removeBorderEntitiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordEntityChange("删除地图边缘的建筑、单位与陷阱",
                    () => count = belong.RemoveBorderEntities());
                _output.WriteLine($"已删除 {count} 个边界上的单位与建筑");
            });
        });

        var removeBorderArmiesCmd = new Command("remove-border-armies", "删除归属边界上的单位");
        removeBorderArmiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordEntityChange("删除地图边缘的单位",
                    () => count = belong.RemoveBorderArmies());
                _output.WriteLine($"已删除 {count} 个边界上的单位");
            });
        });

        // ---- 补齐 Modifier 已有、但此前没有命令入口的归属能力 ----

        var getCmd = new Command("get", "查询指定格子的归属值");
        var getColArg = new Argument<int>("col") { Description = "列" };
        var getRowArg = new Argument<int>("row") { Description = "行" };
        getCmd.Arguments.Add(getColArg);
        getCmd.Arguments.Add(getRowArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(getColArg);
                int row = parseResult.GetValue(getRowArg);

                int? value = belong.GetBelongValue(col, row);
                if (!value.HasValue)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围");
                    return;
                }

                if (value.Value == 0xFF)
                {
                    _output.WriteLine($"({col},{row}) 归属= 无归属(0xFF)");
                    return;
                }

                var legionInfo = mapData.ResolveLegionByBelong(value.Value);
                string name = legionInfo != null ? $" ({legionInfo.Value.DisplayName})" : "";
                _output.WriteLine($"({col},{row}) 归属= {value.Value}{name}");
            }, readOnly: true);
        });

        var replaceCmd = new Command("replace", "批量替换归属值（把 oldId 的格子全部改成 newId）");
        var replaceOldArg = new Argument<int>("oldId") { Description = "原归属值(0-255)" };
        var replaceNewArg = new Argument<int>("newId") { Description = "新归属值(0-255)" };
        replaceCmd.Arguments.Add(replaceOldArg);
        replaceCmd.Arguments.Add(replaceNewArg);
        replaceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int oldId = Math.Clamp(parseResult.GetValue(replaceOldArg), 0, 255);
                int newId = Math.Clamp(parseResult.GetValue(replaceNewArg), 0, 255);

                int count = 0;
                RecordBelongChange($"批量替换归属 ({oldId} → {newId})",
                    () => count = belong.ReplaceBelong(oldId, newId));
                _output.WriteLine($"已将 {count} 个格子的归属从 {oldId} 改为 {newId}");
            });
        });

        var randomizeByCmd = new Command("randomize-by", "随机化指定归属值的格子");
        var randomizeByIdArg = new Argument<int>("belongId") { Description = "目标归属值(0-255)" };
        randomizeByCmd.Arguments.Add(randomizeByIdArg);
        randomizeByCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(randomizeByIdArg), 0, 255);

                int count = 0;
                RecordBelongChange($"随机化归属 ({targetId})",
                    () => count = belong.RandomizeBelongByTargetId(targetId, new Random()));
                _output.WriteLine($"已随机化 {count} 个归属为 {targetId} 的格子");
            });
        });

        var setByCityCmd = new Command("set-by-city", "按城市名称批量设置归属（匹配建筑名称）");
        var cityNameArg = new Argument<string>("cityName") { Description = "城市名称，如 北京" };
        var cityBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setByCityCmd.Arguments.Add(cityNameArg);
        setByCityCmd.Arguments.Add(cityBelongArg);
        setByCityCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                string cityName = parseResult.GetValue(cityNameArg)!;
                int belongId = Math.Clamp(parseResult.GetValue(cityBelongArg), 0, 255);

                // 城市名 → 城市ID：名称必须已经在城市名称表里
                //（即此前用 building add -n 写入过的名字）
                int? cityId = ConfigManager.Instance.GetStringTableParser().FindCityIdByName(cityName);
                if (!cityId.HasValue)
                {
                    _output.WriteLine($"没有名为「{cityName}」的城市：该名称还没写入过城市名称表");
                    return;
                }

                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange($"按城市名设置归属 ({cityName} → {belongId})", () =>
                {
                    foreach (var building in mapData.Buildings)
                    {
                        if (building.Name != cityId.Value) continue;
                        int col = building.Coordinate % mapData.MapWidth;
                        int row = building.Coordinate / mapData.MapWidth;
                        belong.SetBelongByCountryId(col, row, belongId);
                        count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将「{cityName}」所在的 {count} 个格子归属设为 {belongId}"
                    : $"地图上没有名为「{cityName}」的建筑");
            });
        });

        var setRegionCmd = new Command("set-region", "把矩形区域内的格子批量设为指定归属");
        var regionCol1Arg = new Argument<int>("col1") { Description = "角点1 列" };
        var regionRow1Arg = new Argument<int>("row1") { Description = "角点1 行" };
        var regionCol2Arg = new Argument<int>("col2") { Description = "角点2 列" };
        var regionRow2Arg = new Argument<int>("row2") { Description = "角点2 行" };
        var regionBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setRegionCmd.Arguments.Add(regionCol1Arg);
        setRegionCmd.Arguments.Add(regionRow1Arg);
        setRegionCmd.Arguments.Add(regionCol2Arg);
        setRegionCmd.Arguments.Add(regionRow2Arg);
        setRegionCmd.Arguments.Add(regionBelongArg);
        setRegionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col1 = Math.Min(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int col2 = Math.Max(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int row1 = Math.Min(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int row2 = Math.Max(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int belongId = Math.Clamp(parseResult.GetValue(regionBelongArg), 0, 255);

                if (col1 < 0 || row1 < 0 || col2 >= mapData.MapWidth || row2 >= mapData.MapHeight)
                {
                    _output.WriteLine($"区域超出地图范围（地图 {mapData.MapWidth}x{mapData.MapHeight}）");
                    return;
                }

                int count = (col2 - col1 + 1) * (row2 - row1 + 1);
                RecordBelongChange($"区域设置归属 ({col1},{row1})-({col2},{row2}) → {belongId}", () =>
                {
                    for (int row = row1; row <= row2; row++)
                        for (int col = col1; col <= col2; col++)
                            mapData.SetBelongValue(col, row, belongId);
                });
                _output.WriteLine($"已将 ({col1},{row1})-({col2},{row2}) 共 {count} 个格子的归属设为 {belongId}");
            });
        });

        var setProvinceCmd = new Command("set-province", "按省会格子定位省区，把整片省区设为指定归属");
        var provinceColArg = new Argument<int>("capitalCol") { Description = "省会所在格子的列" };
        var provinceRowArg = new Argument<int>("capitalRow") { Description = "省会所在格子的行" };
        var provinceBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setProvinceCmd.Arguments.Add(provinceColArg);
        setProvinceCmd.Arguments.Add(provinceRowArg);
        setProvinceCmd.Arguments.Add(provinceBelongArg);
        setProvinceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int capitalCol = parseResult.GetValue(provinceColArg);
                int capitalRow = parseResult.GetValue(provinceRowArg);
                int belongId = Math.Clamp(parseResult.GetValue(provinceBelongArg), 0, 255);

                if (capitalCol < 0 || capitalRow < 0 ||
                    capitalCol >= mapData.MapWidth || capitalRow >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({capitalCol},{capitalRow}) 超出范围");
                    return;
                }

                // 省区值约定为省会格子的线性索引
                int capitalIndex = capitalRow * mapData.MapWidth + capitalCol;
                int total = mapData.MapWidth * mapData.MapHeight;
                int count = 0;

                RecordBelongChange($"省区设置归属 (省会 {capitalCol},{capitalRow} → {belongId})", () =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        ref var province = ref mapData.GetProvinceRef(i);
                        if (province.ProvinceValue != capitalIndex) continue;

                        mapData.SetBelongValueByIndex(i, belongId);
                        count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将该省区的 {count} 个格子归属设为 {belongId}"
                    : $"({capitalCol},{capitalRow}) 不是一个省会格子（该格省区值不等于自身索引）");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(statsCmd);
        cmd.Subcommands.Add(cleanOrphanCmd);
        cmd.Subcommands.Add(randomizeCmd);
        cmd.Subcommands.Add(batchSetCmd);
        cmd.Subcommands.Add(removeBorderEntitiesCmd);
        cmd.Subcommands.Add(removeBorderArmiesCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(replaceCmd);
        cmd.Subcommands.Add(randomizeByCmd);
        cmd.Subcommands.Add(setByCityCmd);
        cmd.Subcommands.Add(setRegionCmd);
        cmd.Subcommands.Add(setProvinceCmd);

        return cmd;
    }

    /// <summary>
    /// 归属批量变更的统一入口：非脚本模式下记录撤销。
    /// <para>
    /// 脚本批量执行（<c>run xxx.zme</c>）时直接执行、不记撤销 —— 一份脚本动辄几百条命令，
    /// 每条都存一份全图归属快照会撑爆撤销栈，而且脚本本身属于一次性操作。
    /// </para>
    /// </summary>
    private void RecordBelongChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordMultiCellBelongChange(description, applyChange);
    }

    /// <summary>
    /// 单格归属变更的撤销入口。与 <see cref="RecordBelongChange"/> 同理，
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordSingleBelongChange(int col, int row, string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordBelongChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 实体（建筑/单位/陷阱）变更的撤销入口，走建筑快照。
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordEntityChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordBuildingsSnapshot(description, applyChange);
    }

    private Command BuildLegionCommand()
    {
        var cmd = new Command("legion", "军团修改操作");

        var listCmd = new Command("list", "列出所有军团");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legions = mapData.Legions;
                _output.WriteLine($"军团总数: {legions.Count}");
                _output.WriteLine();
                // 国家名与军团编辑窗口一致（CountrySettings.json → string table 的 country_{id} → 国家{id}）；
                // 索引列是 belong 要填的值 —— 归属存的是军团在列表里的位置，不是国家ID。
                _output.WriteLine($"{"索引",-5} {"国家ID",-7} {"国家名",-24} {"颜色",-10} {"玩家控制",-8}");
                _output.WriteLine(new string('-', 62));
                for (int i = 0; i < legions.Count; i++)
                {
                    var legion = legions[i];
                    _output.WriteLine($"{i,-5} {legion.CountryId,-7} {CountryNameProvider.Get(legion.CountryId),-24} ({legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2})    {(legion.IsPlayerControlled == 1 ? "是" : "否"),-8}");
                }
            }, readOnly: true);
        });

        var getCmd = new Command("get", "查询军团详情");
        var getIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        getCmd.Arguments.Add(getIdArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int id = parseResult.GetValue(getIdArg);
                var l = legion.GetLegion(id);
                if (l == null) { _output.WriteLine($"军团 {id} 不存在"); return; }
                var v = l.Value;
                _output.WriteLine($"军团 {v.CountryId}（{CountryNameProvider.Get(v.CountryId)}）  列表索引={mapData.FindLegionIndex(v.CountryId)}");
                _output.WriteLine($"  ActionId={v.ActionId}, 玩家控制={v.IsPlayerControlled}, 阵营={v.Camp}");
                _output.WriteLine($"  颜色=({v.ColorR},{v.ColorG},{v.ColorB})");
                _output.WriteLine($"  经济={v.InitialEconomy}, 工业={v.InitialIndustry}, 科技={v.InitialTech}");
            }, readOnly: true);
        });

        var getByActionCmd = new Command("get-by-action", "按ActionId查询军团");
        var gbaArg = new Argument<int>("actionId") { Description = "ActionId" };
        getByActionCmd.Arguments.Add(gbaArg);
        getByActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int actionId = parseResult.GetValue(gbaArg);
                var l = legion.GetLegionByActionId(actionId);
                if (l == null) { _output.WriteLine($"ActionId={actionId} 的军团不存在"); return; }
                var v = l.Value;
                _output.WriteLine($"军团 {v.CountryId}: ActionId={v.ActionId}, {v.DisplayName}");
            }, readOnly: true);
        });

        var applyCmd = new Command("apply", "设置军团领域到指定格子");
        var apCol = new Argument<int>("col") { Description = "列" };
        var apRow = new Argument<int>("row") { Description = "行" };
        applyCmd.Arguments.Add(apCol);
        applyCmd.Arguments.Add(apRow);
        applyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int col = parseResult.GetValue(apCol);
                int row = parseResult.GetValue(apRow);
                var result = legion.Apply(col, row);
                _output.WriteLine(result.Message ?? $"已设置军团领域 ({col},{row})");
            });
        });

        var removeCmd = new Command("remove", "清除指定格子军团领域");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int col = parseResult.GetValue(rmCol);
                int row = parseResult.GetValue(rmRow);
                var result = legion.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已清除军团领域 ({col},{row})");
            });
        });

        var selectCmd = new Command("select", "选中当前军团");
        var selId = new Argument<int>("legionId") { Description = "军团ID" };
        selectCmd.Arguments.Add(selId);
        selectCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                legion.SelectedLegionId = parseResult.GetValue(selId);
                _output.WriteLine($"已选中军团 {legion.SelectedLegionId}");
            });
        });

        var addCmd = new Command("add", "追加一个默认军团");
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                var result = legion.AddLegion();
                _output.WriteLine(result.Message ?? "已追加军团");
            });
        });

        var removeAtCmd = new Command("remove-at", "按索引删除军团");
        var raIdx = new Argument<int>("index") { Description = "军团列表索引(0-based)" };
        removeAtCmd.Arguments.Add(raIdx);
        removeAtCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int index = parseResult.GetValue(raIdx);
                var result = legion.RemoveLegionAt(index);
                _output.WriteLine(result.Message ?? $"已删除索引 {index} 的军团");
            });
        });

        var setColorCmd = new Command("set-color", "设置军团颜色");
        var setColorIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setColorRArg = new Argument<int>("r") { Description = "红色(0-255)" };
        var setColorGArg = new Argument<int>("g") { Description = "绿色(0-255)" };
        var setColorBArg = new Argument<int>("b") { Description = "蓝色(0-255)" };
        setColorCmd.Arguments.Add(setColorIdArg);
        setColorCmd.Arguments.Add(setColorRArg);
        setColorCmd.Arguments.Add(setColorGArg);
        setColorCmd.Arguments.Add(setColorBArg);
        setColorCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setColorIdArg);
                byte r = (byte)Math.Clamp(parseResult.GetValue(setColorRArg), 0, 255);
                byte g = (byte)Math.Clamp(parseResult.GetValue(setColorGArg), 0, 255);
                byte b = (byte)Math.Clamp(parseResult.GetValue(setColorBArg), 0, 255);
                var result = legion.SetLegionColor(legionId, r, g, b);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} 颜色为 ({r},{g},{b})");
            });
        });

        var setActionCmd = new Command("set-action", "设置军团ActionId");
        var setActionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setActionValArg = new Argument<int>("actionId") { Description = "ActionId" };
        setActionCmd.Arguments.Add(setActionIdArg);
        setActionCmd.Arguments.Add(setActionValArg);
        setActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setActionIdArg);
                int actionId = parseResult.GetValue(setActionValArg);
                var result = legion.SetLegionActionId(legionId, actionId);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} ActionId={actionId}");
            });
        });

        var setPlayerCmd = new Command("set-player", "设置玩家控制军团");
        var spActionId = new Argument<int>("actionId") { Description = "ActionId" };
        setPlayerCmd.Arguments.Add(spActionId);
        setPlayerCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int actionId = parseResult.GetValue(spActionId);
                var result = legion.SetPlayerControlledLegion(actionId);
                _output.WriteLine(result.Message ?? $"已设置玩家控制军团 ActionId={actionId}");
            });
        });

        var defaultColorsCmd = new Command("apply-default-colors", "应用默认颜色到所有军团");
        defaultColorsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                var result = legion.ApplyDefaultColorsToAllLegions();
                _output.WriteLine(result.Message ?? "已应用默认颜色");
            });
        });

        var settingsColorsCmd = new Command("apply-settings-colors", "从配置更新所有军团颜色");
        settingsColorsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                var result = legion.ApplyAllLegionsColorFromSettings();
                _output.WriteLine(result.Message ?? "已从配置更新颜色");
            });
        });

        var randomLevelsCmd = new Command("randomize-levels", "随机化所有军团等级与经济");
        randomLevelsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                var result = legion.RandomizeAllLegionLevels();
                _output.WriteLine(result.Message ?? "已随机化军团等级");
            });
        });

        var rebuildCmd = new Command("rebuild-action-belong", "重建所有军团ActionId与归属");
        rebuildCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                var result = legion.UpdateAllLegionsActionIdAndBelong();
                _output.WriteLine(result.Message ?? "已重建ActionId与归属");
            });
        });

        var setEconomyCmd = new Command("set-economy", "批量设置军团经济");
        var econVal = new Argument<int>("value") { Description = "经济值" };
        setEconomyCmd.Arguments.Add(econVal);
        setEconomyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(econVal);
                var result = legion.SetAllLegionsEconomy(val);
                _output.WriteLine(result.Message ?? $"已设置所有军团经济={val}");
            });
        });

        var setIndustryCmd = new Command("set-industry", "批量设置军团工业");
        var indVal = new Argument<int>("value") { Description = "工业值" };
        setIndustryCmd.Arguments.Add(indVal);
        setIndustryCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(indVal);
                var result = legion.SetAllLegionsIndustry(val);
                _output.WriteLine(result.Message ?? $"已设置所有军团工业={val}");
            });
        });

        var setTechCmd = new Command("set-tech", "批量设置军团科技");
        var techVal = new Argument<int>("value") { Description = "科技值" };
        setTechCmd.Arguments.Add(techVal);
        setTechCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(techVal);
                var result = legion.SetAllLegionsTech(val);
                _output.WriteLine(result.Message ?? $"已设置所有军团科技={val}");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(getByActionCmd);
        cmd.Subcommands.Add(applyCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(selectCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(removeAtCmd);
        cmd.Subcommands.Add(setColorCmd);
        cmd.Subcommands.Add(setActionCmd);
        cmd.Subcommands.Add(setPlayerCmd);
        cmd.Subcommands.Add(defaultColorsCmd);
        cmd.Subcommands.Add(settingsColorsCmd);
        cmd.Subcommands.Add(randomLevelsCmd);
        cmd.Subcommands.Add(rebuildCmd);
        // ---- 军团对象读写（与 Lua 的 SetLegionField 同一实现）----
        // 注意：这里按"列表索引"定位（索引 == 归属值），不是按国家ID —— 归属数据存的就是索引。
        var lfCmd = new Command("fields", "列出军团可写的字段名");
        lfCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("军团可写字段：" + string.Join(", ", ObjectApi(mapData).GetLegionFields())),
            readOnly: true));

        var lsaCmd = new Command("show-at", "按列表索引查看军团（索引即归属值）");
        var lsaIndexArg = new Argument<int>("index") { Description = "军团列表索引（= 归属值）" };
        lsaCmd.Arguments.Add(lsaIndexArg);
        lsaCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int idx = parseResult.GetValue(lsaIndexArg);
            var l = ObjectApi(mapData).GetLegionAt(idx);
            if (l == null)
            {
                _output.WriteLine($"军团索引 {idx} 超出范围（当前共 {mapData.Legions.Count} 个）");
                return;
            }

            _output.WriteLine($"军团[{idx}] 国家ID={l.CountryId} 行动顺序={l.ActionId} 玩家控制={(l.IsPlayer ? "是" : "否")} 阵营={l.Camp} 战败条件={l.DefeatCondition}");
            _output.WriteLine($"  初始 经济={l.InitialEconomy} 工业={l.InitialIndustry} 科技={l.InitialTech} 初始科技等级={l.InitialTechLevel} AI={l.CountryAiBehavior}");
            _output.WriteLine($"  颜色=({l.ColorR},{l.ColorG},{l.ColorB}) 血率={l.CountryHpRate:F2} 税率={l.CountryTaxRate:F2}");
            _output.WriteLine($"  核弹 原子={l.AtomicBombCount} 氢={l.HydrogenBombCount} 三相弹={l.NeutronBombCount} 反物质={l.AntimatterBombCount}");
            _output.WriteLine($"  平均等级 陆={l.AverageArmyLevel:F1} 海={l.AverageNavyLevel:F1} 空={l.AverageAirForceLevel:F1} 防御={l.AverageDefenseLevel:F1}");
        }, readOnly: true));

        var lsCmd = new Command("set-field", "按字段名改军团属性（字段名见 legion fields）");
        var lsIndexArg = new Argument<int>("index") { Description = "军团列表索引（= 归属值）" };
        var lsFieldArg = new Argument<string>("field") { Description = "字段名，如 InitialEconomy / ColorR / ArmorLevel" };
        var lsValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位，浮点字段可带小数）" };
        lsCmd.Arguments.Add(lsIndexArg);
        lsCmd.Arguments.Add(lsFieldArg);
        lsCmd.Arguments.Add(lsValueArg);
        lsCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetLegionField(
                parseResult.GetValue(lsIndexArg), parseResult.GetValue(lsFieldArg), parseResult.GetValue(lsValueArg)))));

        // 定位键（CountryId / ActionId）单独走带校验的命令：它们不在 set-field 的白名单里
        var sCountryCmd = new Command("set-country-at", "按列表索引设置国家ID（会校验唯一性）");
        var scIndexArg = new Argument<int>("index") { Description = "军团列表索引" };
        var scCountryArg = new Argument<int>("countryId") { Description = "国家ID（不能与其它军团重复）" };
        sCountryCmd.Arguments.Add(scIndexArg);
        sCountryCmd.Arguments.Add(scCountryArg);
        sCountryCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetLegionCountryIdAt(
                parseResult.GetValue(scIndexArg), parseResult.GetValue(scCountryArg)))));

        var sActionAtCmd = new Command("set-action-at", "按列表索引设置 ActionId（行动顺序）");
        var saIndexAtArg = new Argument<int>("index") { Description = "军团列表索引" };
        var saActionArg = new Argument<int>("actionId") { Description = "行动顺序" };
        sActionAtCmd.Arguments.Add(saIndexAtArg);
        sActionAtCmd.Arguments.Add(saActionArg);
        sActionAtCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetLegionActionIdAt(
                parseResult.GetValue(saIndexAtArg), parseResult.GetValue(saActionArg)))));

        cmd.Subcommands.Add(setEconomyCmd);
        cmd.Subcommands.Add(setIndustryCmd);
        cmd.Subcommands.Add(setTechCmd);
        cmd.Subcommands.Add(lfCmd);
        cmd.Subcommands.Add(lsaCmd);
        cmd.Subcommands.Add(lsCmd);
        cmd.Subcommands.Add(sCountryCmd);
        cmd.Subcommands.Add(sActionAtCmd);

        return cmd;
    }

    private Command BuildArmyCommand()
    {
        var cmd = new Command("army", "部队修改操作");

        ArmyV3Modifier? GetV3(MapData mapData)
        {
            if (mapData.Header.BtlVersion < 3) return null;
            var v3 = new ArmyV3Modifier();
            v3.Initialize(mapData);
            return v3;
        }

        var listCmd = new Command("list", "列出所有部队");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var armies = mapData.Armies;
                _output.WriteLine($"v1部队总数: {armies.Count}");
                if (armies.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {"军团",-6} {"方案",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var army = armies[i];
                        int col = army.Coordinate % mapData.MapWidth;
                        int row = army.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} ({col},{row})    {army.LegionId,-6} {army.Plan,-6}");
                    }
                }
                var v3List = mapData.ArmiesV3;
                if (v3List.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"v3部队总数: {v3List.Count}");
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {"军团",-6} {"方案",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < v3List.Count; i++)
                    {
                        var army = v3List[i];
                        int col = army.Coordinate % mapData.MapWidth;
                        int row = army.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} ({col},{row})    {army.LegionId,-6} {army.Plan,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有部队(v1+v3)");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Armies.Count + mapData.ArmiesV3.Count;
                mapData.Armies.Clear();
                mapData.ArmiesV3.Clear();
                _output.WriteLine($"已删除 {count} 个部队");
            });
        });

        var applyCmd = new Command("apply", "放置单位");
        var applyCol = new Argument<int>("col") { Description = "列" };
        var applyRow = new Argument<int>("row") { Description = "行" };
        applyCmd.Arguments.Add(applyCol);
        applyCmd.Arguments.Add(applyRow);
        applyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(applyCol);
                int row = parseResult.GetValue(applyRow);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.Apply(col, row) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.Apply(col, row);
                }
                _output.WriteLine(result.Message ?? $"已放置单位 ({col},{row})");
            });
        });

        var removeCmd = new Command("remove", "删除指定坐标单位");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(rmCol);
                int row = parseResult.GetValue(rmRow);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.Remove(col, row) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.Remove(col, row);
                }
                _output.WriteLine(result.Message ?? $"已删除单位 ({col},{row})");
            });
        });

        var copyCmd = new Command("copy", "复制单位");
        var cpCol = new Argument<int>("col") { Description = "列" };
        var cpRow = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(cpCol);
        copyCmd.Arguments.Add(cpRow);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(cpCol);
                int row = parseResult.GetValue(cpRow);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.CopyArmy(col, row) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.CopyArmy(col, row);
                }
                _output.WriteLine(result.Message ?? $"已复制单位 ({col},{row})");
            });
        });

        var pasteCmd = new Command("paste", "粘贴单位");
        var psCol = new Argument<int>("col") { Description = "列" };
        var psRow = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(psCol);
        pasteCmd.Arguments.Add(psRow);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(psCol);
                int row = parseResult.GetValue(psRow);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.PasteArmy(col, row) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.PasteArmy(col, row);
                }
                _output.WriteLine(result.Message ?? $"已粘贴单位 ({col},{row})");
            });
        });

        var setLegionCmd = new Command("set-legion", "设置单位军团ID");
        var slCol = new Argument<int>("col") { Description = "列" };
        var slRow = new Argument<int>("row") { Description = "行" };
        var slLegion = new Argument<int>("legionId") { Description = "军团ID" };
        setLegionCmd.Arguments.Add(slCol);
        setLegionCmd.Arguments.Add(slRow);
        setLegionCmd.Arguments.Add(slLegion);
        setLegionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(slCol);
                int row = parseResult.GetValue(slRow);
                int legionId = parseResult.GetValue(slLegion);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.SetLegionId(col, row, legionId) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.SetLegionId(col, row, legionId);
                }
                _output.WriteLine(result.Message ?? $"已设置 ({col},{row}) 军团ID={legionId}");
            });
        });

        var cyclePlanCmd = new Command("cycle-plan", "循环切换单位方案(0-4)");
        var cp2Col = new Argument<int>("col") { Description = "列" };
        var cp2Row = new Argument<int>("row") { Description = "行" };
        cyclePlanCmd.Arguments.Add(cp2Col);
        cyclePlanCmd.Arguments.Add(cp2Row);
        cyclePlanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(cp2Col);
                int row = parseResult.GetValue(cp2Row);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.QuickModifyArmyPlan(col, row) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.QuickModifyArmyPlan(col, row);
                }
                _output.WriteLine(result.Message ?? $"已切换 ({col},{row}) 方案");
            });
        });

        var setPlanCmd = new Command("set-plan", "设置单位方案");
        var spCol = new Argument<int>("col") { Description = "列" };
        var spRow = new Argument<int>("row") { Description = "行" };
        var spPlan = new Argument<int>("plan") { Description = "方案值(0-4)" };
        setPlanCmd.Arguments.Add(spCol);
        setPlanCmd.Arguments.Add(spRow);
        setPlanCmd.Arguments.Add(spPlan);
        setPlanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(spCol);
                int row = parseResult.GetValue(spRow);
                int plan = Math.Clamp(parseResult.GetValue(spPlan), 0, 4);
                if (mapData.Header.BtlVersion >= 3)
                {
                    int idx = mapData.FindArmyV3Index(col, row);
                    if (idx < 0) { _output.WriteLine($"({col},{row}) 无v3单位"); return; }
                    var a = mapData.ArmiesV3[idx];
                    a.Plan = (short)plan;
                    mapData.ArmiesV3[idx] = a;
                    _output.WriteLine($"已设置 ({col},{row}) 方案={plan}");
                }
                else
                {
                    int idx = mapData.FindArmyIndex(col, row);
                    if (idx < 0) { _output.WriteLine($"({col},{row}) 无单位"); return; }
                    var a = mapData.Armies[idx];
                    a.Plan = (short)plan;
                    mapData.Armies[idx] = a;
                    _output.WriteLine($"已设置 ({col},{row}) 方案={plan}");
                }
            });
        });

        var batchPlanCmd = new Command("batch-plan-by-legion", "按归属批量修改单位方案");
        var bpBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var bpPlan = new Argument<int>("plan") { Description = "方案值(0-4)" };
        batchPlanCmd.Arguments.Add(bpBelong);
        batchPlanCmd.Arguments.Add(bpPlan);
        batchPlanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(bpBelong);
                int plan = parseResult.GetValue(bpPlan);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.BatchModifyArmyPlanByLegion(belong, plan) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.BatchModifyArmyPlanByLegion(belong, plan);
                }
                _output.WriteLine(result.Message ?? $"已批量修改方案");
            });
        });

        var strengthCmd = new Command("legion-strength", "按配置修改军团强度");
        var lsBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var lsLevel = new Argument<int>("level") { Description = "强度等级(-1为自动)" };
        strengthCmd.Arguments.Add(lsBelong);
        strengthCmd.Arguments.Add(lsLevel);
        strengthCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(lsBelong);
                int level = parseResult.GetValue(lsLevel);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.ModifyLegionStrength(belong, level) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.ModifyLegionStrength(belong, level);
                }
                _output.WriteLine(result.Message ?? $"已修改军团强度");
            });
        });

        var randomCmd = new Command("randomize-legion", "随机化军团单位");
        var rdBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        randomCmd.Arguments.Add(rdBelong);
        randomCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(rdBelong);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.RandomizeLegionArmies(belong) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.RandomizeLegionArmies(belong);
                }
                _output.WriteLine(result.Message ?? $"已随机化军团单位");
            });
        });

        var genCmd = new Command("generate-by-prob", "按概率生成单位");
        var gnBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var gnProb = new Argument<int>("probability") { Description = "概率(1-100)" };
        genCmd.Arguments.Add(gnBelong);
        genCmd.Arguments.Add(gnProb);
        genCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(gnBelong);
                int prob = Math.Clamp(parseResult.GetValue(gnProb), 1, 100);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.GenerateArmiesByProbability(belong, prob) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.GenerateArmiesByProbability(belong, prob);
                }
                _output.WriteLine(result.Message ?? $"已按概率生成单位");
            });
        });

        var generalCmd = new Command("auto-general", "自动分配将领");
        var agBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var agCount = new Argument<int>("count") { Description = "将领数量" };
        generalCmd.Arguments.Add(agBelong);
        generalCmd.Arguments.Add(agCount);
        generalCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(agBelong);
                int count = parseResult.GetValue(agCount);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.AutoAssignGeneralsToArmies3(belong, count, false, true) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.AutoAssignGeneralsToArmies(belong, count, false, true);
                }
                _output.WriteLine(result.Message ?? $"已自动分配将领");
            });
        });

        var rmByBelongCmd = new Command("remove-by-belong", "删除指定归属的所有单位");
        var rbbBelong = new Argument<int>("belong") { Description = "归属值" };
        rmByBelongCmd.Arguments.Add(rbbBelong);
        rmByBelongCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(rbbBelong);
                ModifierResult result = default;
                if (mapData.Header.BtlVersion >= 3)
                {
                    var v3 = GetV3(mapData);
                    result = v3 != null ? v3.DeleteArmiesByBelong(belong) : ModifierResult.Fail("v3修改器未初始化");
                }
                else
                {
                    var army = mgr.GetModifier<ArmyModifier>()!;
                    result = army.DeleteArmiesByBelong(belong);
                }
                _output.WriteLine(result.Message ?? $"已删除归属={belong}的单位");
            });
        });

        var eraseCmd = new Command("erase-brush", "橡皮擦批量删除(v1+v3)");
        var esCol = new Argument<int>("col") { Description = "中心列" };
        var esRow = new Argument<int>("row") { Description = "中心行" };
        var esRadius = new Argument<int>("radius") { Description = "半径" };
        eraseCmd.Arguments.Add(esCol);
        eraseCmd.Arguments.Add(esRow);
        eraseCmd.Arguments.Add(esRadius);
        eraseCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(esCol);
                int row = parseResult.GetValue(esRow);
                int radius = parseResult.GetValue(esRadius);
                int deleted = 0;
                for (int dr = -radius; dr <= radius; dr++)
                {
                    for (int dc = -radius; dc <= radius; dc++)
                    {
                        int r = row + dr, c = col + dc;
                        if (r < 0 || r >= mapData.MapHeight || c < 0 || c >= mapData.MapWidth) continue;
                        if (Math.Sqrt(dr * dr + dc * dc) > radius) continue;
                        int idx = mapData.FindArmyIndex(c, r);
                        if (idx >= 0) { mapData.RemoveArmyAt(idx); deleted++; }
                        int v3Idx = mapData.FindArmyV3Index(c, r);
                        if (v3Idx >= 0) { mapData.RemoveArmyV3At(v3Idx); deleted++; }
                    }
                }
                _output.WriteLine($"橡皮擦删除了 {deleted} 个单位");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(applyCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(setLegionCmd);
        cmd.Subcommands.Add(cyclePlanCmd);
        cmd.Subcommands.Add(setPlanCmd);
        cmd.Subcommands.Add(batchPlanCmd);
        cmd.Subcommands.Add(strengthCmd);
        cmd.Subcommands.Add(randomCmd);
        cmd.Subcommands.Add(genCmd);
        // ---- 单位对象读写（与 Lua 的 SetArmyField 同一实现，v1/v3 内部自动分派）----
        var afCmd = new Command("fields", "列出单位可写的字段名");
        afCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("单位可写字段：" + string.Join(", ", ObjectApi(mapData).GetArmyFields())),
            readOnly: true));

        var asCmd = new Command("show", "查看指定格子的单位对象");
        var asColArg = new Argument<int>("col") { Description = "列" };
        var asRowArg = new Argument<int>("row") { Description = "行" };
        asCmd.Arguments.Add(asColArg);
        asCmd.Arguments.Add(asRowArg);
        asCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(asColArg);
            int row = parseResult.GetValue(asRowArg);
            var a = ObjectApi(mapData).GetArmy(col, row);
            if (a == null)
            {
                _output.WriteLine($"({col},{row}) 没有单位");
                return;
            }

            _output.WriteLine($"v{a.Version} 单位 ({a.Col},{a.Row}) 兵种={a.UnitTypeName}({a.UnitType}) 等级={a.Level} 编制={a.Organization}");
            _output.WriteLine($"  将领={a.General} 军衔={a.Rank} 方案={a.Plan} 士气={a.MoraleName} 政策={a.PolicyName}");
            _output.WriteLine($"  血量={a.CurrentHealth}/{a.MaxHealth} 加成={a.HealthBonus} 经验={a.Experience} 机动={a.Mobility} 朝向={a.DirectionName}");
            _output.WriteLine($"  据点={a.KeyPointName} 可攻击={a.CanAttack} 所属军团={a.LegionId}");
            if (a.Version >= 3)
                _output.WriteLine($"  v3: 血量等级={a.HpLevel} 运输船={a.TransportShip} 仇恨={a.Hatred} 攻击目标={a.AttackTarget} 反防模式={a.AntiDefenseMode}");
        }, readOnly: true));

        var asfCmd = new Command("set-field", "按字段名改单位属性（字段名见 army fields）");
        var asfColArg = new Argument<int>("col") { Description = "列" };
        var asfRowArg = new Argument<int>("row") { Description = "行" };
        var asfFieldArg = new Argument<string>("field") { Description = "字段名，如 Level / General / LegionId / Plan" };
        var asfValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位）" };
        asfCmd.Arguments.Add(asfColArg);
        asfCmd.Arguments.Add(asfRowArg);
        asfCmd.Arguments.Add(asfFieldArg);
        asfCmd.Arguments.Add(asfValueArg);
        asfCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetArmyField(
                parseResult.GetValue(asfColArg), parseResult.GetValue(asfRowArg),
                parseResult.GetValue(asfFieldArg), parseResult.GetValue(asfValueArg)))));

        cmd.Subcommands.Add(generalCmd);
        cmd.Subcommands.Add(rmByBelongCmd);
        cmd.Subcommands.Add(eraseCmd);
        cmd.Subcommands.Add(afCmd);
        cmd.Subcommands.Add(asCmd);
        cmd.Subcommands.Add(asfCmd);

        return cmd;
    }

    private Command BuildTrapCommand()
    {
        var cmd = new Command("trap", "陷阱修改操作");

        var listCmd = new Command("list", "列出所有陷阱");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var traps = mapData.Traps;
                _output.WriteLine($"陷阱总数: {traps.Count}");
                if (traps.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"坐标",-10} {"军团",-6} {"组织",-6} {"血量",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < traps.Count; i++)
                    {
                        var trap = traps[i];
                        int col = trap.Coordinate % mapData.MapWidth;
                        int row = trap.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} ({col},{row})    {trap.LegionId,-6} {trap.Organization,-6} {trap.Health,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有陷阱");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Traps.Count;
                mapData.Traps.Clear();
                _output.WriteLine($"已删除 {count} 个陷阱");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

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

    /// <summary>
    /// fix 命令：按玩法规则修复 btl（对齐 Java 版纠错）。
    /// <para>
    /// 与 check 不同，这个命令<b>会修改文件</b>，所以写回前先备份为 .bak。
    /// </para>
    /// </summary>
    private Command BuildFixCommand()
    {
        var cmd = new Command("fix", "按玩法规则修复 btl 文件（会写回文件，自动备份为 .bak）");

        var fileArg = new Argument<string?>("file")
        {
            Description = "要修复的 btl 文件路径；省略则修复当前打开的地图文件",
            Arity = ArgumentArity.ZeroOrOne
        };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(fileArg);

            // 省略路径时先取当前地图的文件路径，走与「fix <路径>」完全一致的字节层修复；
            // 之后再做只有 MapData 层能做的：首都（需要 Capitals 列表）、
            // 港口方向（需要四邻地形，复用 BuildingModifier 的智能设置）。
            bool alsoFixMapData = false;

            if (string.IsNullOrWhiteSpace(path))
            {
                string current = "";
                WithModifiers((mapData, _) => current = mapData.FilePath, readOnly: true);

                if (string.IsNullOrWhiteSpace(current))
                {
                    _output.WriteLine("当前地图还没有对应的文件（可能尚未保存或来自新建）。");
                    _output.WriteLine("用法: fix <btl文件路径>");
                    return;
                }

                path = current;
                alsoFixMapData = true;
            }

            if (!File.Exists(path))
            {
                _output.WriteLine($"文件不存在: {path}");
                return;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);

                // 规则层：建筑设施 / 单位参数 / 军团 / 归属 / 区划 等
                int count = BTLRuleChecker.Fix(data);

                // 地形组 ID 纠错（对应 Java 版 E 键 checkMapTerrainIds）。
                // 取不到合法 ID 表时返回 -1，表示未做任何修改，不要计入修复数。
                int terrainFixed = BTLFormatChecker.FixTerrainGroups(data);
                if (terrainFixed > 0) count += terrainFixed;

                File.WriteAllBytes(path, data);

                _output.WriteLine($"已修复 {count} 处，结果已写回：{path}");
                if (terrainFixed < 0)
                    _output.WriteLine("（地形组 ID 检查已跳过：未取到合法地形表）");

                if (alsoFixMapData)
                {
                    WithModifiers((mapData, mgr) =>
                    {
                        int capitals = BTLRuleChecker.FixCapitals(mapData);
                        var building = mgr.GetModifier<BuildingModifier>()!;
                        int appearances = building.SmartSetBuildingAppearance(mapData);

                        _output.WriteLine($"另外修复首都 {capitals} 处、港口方向 {appearances} 处");
                    });
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"修复失败: {ex.Message}");
            }
        });

        return cmd;
    }

    private Command BuildInfoCommand()
    {
        var cmd = new Command("info", "显示地图信息");

        cmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var header = mapData.Header;
                _output.WriteLine("========== 地图信息 ==========");
                _output.WriteLine($"  地图尺寸:    {header.MapWidth} x {header.MapLength}");
                _output.WriteLine($"  总格子数:    {header.TotalTiles}");
                _output.WriteLine($"  军团数:      {mapData.Legions.Count}");
                _output.WriteLine($"  建筑数:      {mapData.Buildings.Count}");
                _output.WriteLine($"  省份数据:    {mapData.Provinces.Count} 格");
                _output.WriteLine($"  归属数据:    {mapData.Belongs.Count} 格");
                _output.WriteLine($"  地形数据:    {mapData.Terrains.Count} 格");

                int provinceCount = mapData.Provinces.Count(p => p.ProvinceValue != 0);
                int belongCount = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                    if (mapData.GetBelongValueByIndex(i) != 0xFF) belongCount++;
                _output.WriteLine($"  有效省份:    {provinceCount} 格");
                _output.WriteLine($"  有效归属:    {belongCount} 格");
                _output.WriteLine("==============================");
            }, readOnly: true);
        });

        return cmd;
    }

    private bool _batchMode;
    private int _batchCommandCount;

    public void BeginBatchMode()
    {
        _batchMode = true;
        _batchCommandCount = 0;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.RebuildBuildingCoordIndex();
    }

    public void EndBatchMode()
    {
        _batchMode = false;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.InvalidateBuildingCoordIndex();
        if (_batchCommandCount > 0)
            DataModified?.Invoke();
        _batchCommandCount = 0;
    }

    /// <summary>
    /// 对象属性读写的统一入口：直接复用 Lua 那套实现
    /// （<c>MapScriptApi</c>：字段白名单 + 按类型钳位 + 转发 Modifier）。
    /// <para>
    /// CLI 和 Lua 走同一实现，就不会出现"命令能改、脚本不能改"或两边校验口径不一致的问题；
    /// 新增字段时也只改一处白名单。
    /// </para>
    /// </summary>
    private static WC4MapEditor.Core.Scripting.MapScriptApi ObjectApi(MapData mapData)
        => new(mapData, EditModeManager.Instance);

    /// <summary>把对象操作结果打到输出：成功给消息，失败必须给出原因（不能静默）。</summary>
    private void PrintOp(WC4MapEditor.Scripting.ScriptOperationResult result)
        => _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");

    private void WithModifiers(Action<MapData, EditModeManager> action, bool readOnly = false)
    {
        var ctx = CommandManager.Instance.GetContext();
        if (ctx?.MapData == null)
        {
            _output.WriteLine("地图数据未加载");
            return;
        }

        var mapData = ctx.MapData;
        var mgr = EditModeManager.Instance;

        if (!mgr.IsInitialized)
            mgr.Initialize(mapData);

        action(mapData, mgr);

        if (!readOnly)
        {
            if (_batchMode)
                _batchCommandCount++;
            else
                DataModified?.Invoke();
        }
    }

    public event Action? DataModified;

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