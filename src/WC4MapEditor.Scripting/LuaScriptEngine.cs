using System.Diagnostics;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace WC4MapEditor.Scripting;

/// <summary>
/// 基于 MoonSharp 的 Lua 脚本执行引擎。
/// <para>
/// 只依赖 <see cref="IMapScriptApi"/>，具体的数据操作由 Core 层实现，
/// 因此本程序集不需要引用 Core —— 依赖方向保持 <c>Core → Scripting</c>，不会形成循环。
/// </para>
/// <para>
/// 想给脚本开放新的数据操作时：在 <see cref="IMapScriptApi"/> 加方法 →
/// 在 Core 的 MapScriptApi 里实现 → 脚本中立即可用，无需再设计任何命令语法。
/// </para>
/// <para>
/// 每次执行都是全新环境（新的 <see cref="Script"/> 实例）。标准库缺口、
/// require 搜索路径、utf8 等由 <see cref="LuaStandardEnvironment"/> 统一补齐。
/// </para>
/// </summary>
public sealed class LuaScriptEngine
{
    /// <summary>单次执行最多保留的输出行数，防止脚本里的死循环把内存打爆</summary>
    private const int MaxOutputLines = 50_000;

    private readonly IMapScriptApi _api;
    private readonly LuaUiApi? _uiApi;
    private readonly List<string> _output = new();
    /// <summary>io.write 没有换行，先在这里攒着，凑满一行再进输出</summary>
    private readonly StringBuilder _stream = new();
    private bool _outputTruncated;

    static LuaScriptEngine()
    {
        // 让 MoonSharp 能直接读写下面这些 C# 类型的成员。
        // 注意：[MoonSharpUserData] 特性本身不会自动生效（需要 RegisterAssembly 才会被扫描到），
        // 必须像这样显式 RegisterType，否则赋值给 Globals 时会抛
        // "cannot convert clr type ..."。
        UserData.RegisterType<LuaMapApi>();
        UserData.RegisterType<LuaUiApi>();
        UserData.RegisterType<ScriptBuildingInfo>();
        UserData.RegisterType<ScriptArmyInfo>();
        UserData.RegisterType<ScriptLegionInfo>();
        UserData.RegisterType<ScriptOperationResult>();
    }

    public LuaScriptEngine(IMapScriptApi api, LuaUiApi? uiApi = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _uiApi = uiApi;
    }

    /// <summary>执行脚本文件（.lua）</summary>
    public ScriptResult ExecuteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ScriptResult.Fail("脚本路径为空");

        if (!File.Exists(path))
            return ScriptResult.Fail($"找不到脚本文件: {path}");

        string code;
        try
        {
            code = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return ScriptResult.Fail($"读取脚本失败: {ex.Message}");
        }

        // 把脚本所在目录加入 require 搜索路径：同目录的模块可以直接 require
        return Execute(code, Path.GetFileName(path), Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    /// <summary>执行一段 Lua 代码</summary>
    public ScriptResult Execute(string code, string scriptName = "script")
        => Execute(code, scriptName, null);

    private ScriptResult Execute(string code, string scriptName, string? scriptDirectory)
    {
        _output.Clear();
        _stream.Clear();
        _outputTruncated = false;

        var stopwatch = Stopwatch.StartNew();
        int affectedTotal = 0;

        // Debug 模块用于 debug.traceback（出错时能给出调用栈）
        var script = new Script(CoreModules.Preset_Default | CoreModules.Debug);
        script.Options.DebugPrint = line => AddOutput(line ?? "");

        string[] modulePaths = BuildModulePaths(scriptDirectory);
        script.Options.ScriptLoader = new LuaScriptLoader
        {
            ModulePaths = modulePaths,
            IgnoreLuaPathGlobal = false,   // 让 package.path 生效
        };

        // 标准库补全：os.clock / os.time、utf8、loadstring、io.write、package.path …
        LuaStandardEnvironment.Install(script, modulePaths, WriteStream);

        var luaMap = new LuaMapApi(_api, Log, n => affectedTotal += n);
        script.Globals["map"] = luaMap;

        if (_uiApi != null)
            script.Globals["ui"] = _uiApi;

        // print 由 Options.DebugPrint 接管；log 与之等价，便于脚本里表达"输出一行"
        script.Globals["log"] = (Action<string>)Log;

        bool success = true;
        string? error = null;

        try
        {
            script.DoString(code, null, scriptName);
        }
        catch (InterpreterException ex)
        {
            // SyntaxErrorException / ScriptRuntimeException 都走这里，DecoratedMessage 带行列信息
            success = false;
            error = DescribeRuntimeError(ex);
        }
        catch (Exception ex)
        {
            success = false;
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            FlushStream();
        }

        return new ScriptResult
        {
            Success = success,
            Error = error ?? "",
            ErrorLocation = success ? "" : scriptName,
            Output = _output.ToArray(),
            Elapsed = stopwatch.Elapsed,
            AffectedCount = affectedTotal
        };
    }

    /// <summary>
    /// require / dofile 的搜索路径。
    /// <para>
    /// 顺序：脚本所在目录 → 程序目录下的 scripts → 程序目录。
    /// 相对路径都以程序目录为基准，与 <c>lua</c> 命令的约定一致。
    /// </para>
    /// </summary>
    private static string[] BuildModulePaths(string? scriptDirectory)
    {
        var dirs = new List<string>();

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!dirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) dirs.Add(dir);
        }

        // 1) 脚本自己所在目录（同目录模块）
        Add(scriptDirectory);

        // 2) exe 目录及其各级祖先下的 scripts —— 与 Resource 的检索规则一致：由近及远，
        //    这样把 scripts 放在软件目录的上一级（与 Resource 同级）也能 require 到。
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
        {
            Add(Path.Combine(current, "scripts"));
            current = Path.GetDirectoryName(current);
        }

        // 3) exe 目录本身
        Add(AppContext.BaseDirectory);

        var paths = new List<string>();
        foreach (var dir in dirs)
        {
            paths.Add(Path.Combine(dir, "?.lua"));
            paths.Add(Path.Combine(dir, "?", "init.lua"));
        }

        return paths.ToArray();
    }

    /// <summary>错误消息 + 调用栈（只保留最后 8 层，避免刷屏）</summary>
    private static string DescribeRuntimeError(InterpreterException ex)
    {
        string message = ex.DecoratedMessage;
        var stack = ex.CallStack;

        if (stack is not { Count: > 0 }) return message;

        var sb = new StringBuilder(message);
        sb.AppendLine();
        sb.AppendLine("调用栈:");
        string last = "";
        foreach (var frame in stack.Skip(Math.Max(0, stack.Count - 8)))
        {
            string where = frame.Location?.ToString() is { Length: > 0 } loc
                ? loc
                : string.IsNullOrEmpty(frame.Name) ? "main chunk" : frame.Name;

            if (where == last) continue;   // MoonSharp 偶尔给出重复帧
            last = where;
            sb.Append("  ").AppendLine(where);
        }

        return sb.ToString().TrimEnd();
    }

    private void Log(string message) => AddOutput(message ?? "");

    private void AddOutput(string line)
    {
        if (_output.Count < MaxOutputLines)
        {
            _output.Add(line);
            return;
        }

        if (_outputTruncated) return;

        _outputTruncated = true;
        _output.Add($"[输出过多，仅保留前 {MaxOutputLines} 行]");
    }

    /// <summary>io.write 的落点：按行拆分后并入统一输出</summary>
    private void WriteStream(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        _stream.Append(text.Replace("\r\n", "\n").Replace('\r', '\n'));

        string pending = _stream.ToString();
        int newline;
        while ((newline = pending.IndexOf('\n')) >= 0)
        {
            AddOutput(pending[..newline]);
            pending = pending[(newline + 1)..];
        }

        _stream.Clear();
        _stream.Append(pending);
    }

    /// <summary>把 io.write 剩下的半行落盘</summary>
    private void FlushStream()
    {
        if (_stream.Length == 0) return;

        AddOutput(_stream.ToString());
        _stream.Clear();
    }
}

/// <summary>
/// 在 MoonSharp 自带文件加载器基础上，让 <c>dofile</c> / <c>loadfile</c> 也能在模块搜索路径里找文件。
/// <para>
/// 原生 <see cref="FileSystemScriptLoader"/> 只对 <c>require</c> 使用 ModulePaths；
/// <c>dofile("helper.lua")</c> 直接按当前工作目录解析，脚本旁边的文件反而找不到。
/// </para>
/// </summary>
internal sealed class LuaScriptLoader : FileSystemScriptLoader
{
    public override string ResolveFileName(string filename, Table globalContext)
    {
        if (string.IsNullOrEmpty(filename)) return filename;

        // 绝对路径 / 当前目录能找到的，保持原样
        if (Path.IsPathRooted(filename) || File.Exists(filename))
            return filename;

        string relative = filename.Replace('/', Path.DirectorySeparatorChar)
                                  .Replace('\\', Path.DirectorySeparatorChar);

        var patterns = ModulePaths;
        if (patterns == null) return filename;

        foreach (var pattern in patterns)
        {
            string? dir = Path.GetDirectoryName(pattern);
            if (string.IsNullOrEmpty(dir) || dir.Contains('?')) continue;

            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
        }

        return filename;
    }
}
