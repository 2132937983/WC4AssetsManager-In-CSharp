using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.Core.Scripting;

/// <summary>
/// Lua 脚本执行入口：组装 <see cref="MapScriptApi"/> 与 <see cref="LuaScriptEngine"/>。
/// <para>
/// 整次执行被 <see cref="EditModeManager.RecordEntitiesSnapshot"/> 包成一个撤销单元
/// （快照 所有实体列表 + 省份 + 归属），所以脚本无论改了多少格、改了哪种实体，<c>Ctrl+Z</c> 都能一次性退回执行前的状态。
/// 注意这是<b>整批</b>撤销：脚本内部的每一步不能单独撤销。
/// </para>
/// </summary>
public sealed class ScriptRunner
{
    private readonly MapData _mapData;
    private readonly EditModeManager _modeManager;
    private readonly LuaUiApi? _uiApi;

    public ScriptRunner(MapData mapData, EditModeManager modeManager, LuaUiApi? uiApi = null)
    {
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
        _uiApi = uiApi;
    }

    /// <summary>执行 .lua 脚本文件</summary>
    public ScriptResult RunFile(string path)
    {
        ScriptResult? result = null;
        string name = string.IsNullOrWhiteSpace(path) ? "script" : Path.GetFileName(path);

        _modeManager.RecordEntitiesSnapshot(Loc.F("Script.UndoRun", name),
            () => result = CreateEngine().ExecuteFile(path));

        return result ?? ScriptResult.Fail("脚本未执行");
    }

    /// <summary>执行一段 Lua 代码</summary>
    public ScriptResult RunCode(string code, string scriptName = "inline")
    {
        ScriptResult? result = null;

        _modeManager.RecordEntitiesSnapshot(Loc.F("Script.UndoRun", scriptName),
            () => result = CreateEngine().Execute(code, scriptName));

        return result ?? ScriptResult.Fail("脚本未执行");
    }

    private LuaScriptEngine CreateEngine()
        => new(new MapScriptApi(_mapData, _modeManager), _uiApi);
}