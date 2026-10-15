using System.Diagnostics;
using MoonSharp.Interpreter;

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
/// </summary>
public sealed class LuaScriptEngine
{
    private readonly IMapScriptApi _api;
    private readonly LuaUiApi? _uiApi;
    private readonly List<string> _output = new();

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

        return Execute(code, Path.GetFileName(path));
    }

    /// <summary>执行一段 Lua 代码</summary>
    public ScriptResult Execute(string code, string scriptName = "script")
    {
        _output.Clear();
        var stopwatch = Stopwatch.StartNew();
        int affectedTotal = 0;

        var script = new Script();
        script.Options.DebugPrint = line => _output.Add(line ?? "");

        var luaMap = new LuaMapApi(_api, Log, n => affectedTotal += n);
        script.Globals["map"] = luaMap;

        if (_uiApi != null)
            script.Globals["ui"] = _uiApi;

        // print 由 Options.DebugPrint 接管；log 与之等价，便于脚本里表达"输出一行"
        script.Globals["log"] = (Action<string>)Log;

        try
        {
            script.DoString(code, null, scriptName);
        }
        catch (InterpreterException ex)
        {
            // SyntaxErrorException / ScriptRuntimeException 都走这里，DecoratedMessage 带行列信息
            return new ScriptResult
            {
                Success = false,
                Error = ex.DecoratedMessage,
                ErrorLocation = $"{scriptName}",
                Output = _output.ToArray(),
                Elapsed = stopwatch.Elapsed,
                AffectedCount = affectedTotal
            };
        }
        catch (Exception ex)
        {
            return new ScriptResult
            {
                Success = false,
                Error = $"{ex.GetType().Name}: {ex.Message}",
                ErrorLocation = scriptName,
                Output = _output.ToArray(),
                Elapsed = stopwatch.Elapsed,
                AffectedCount = affectedTotal
            };
        }

        return new ScriptResult
        {
            Success = true,
            Output = _output.ToArray(),
            Elapsed = stopwatch.Elapsed,
            AffectedCount = affectedTotal
        };
    }

    private void Log(string message) => _output.Add(message ?? "");
}