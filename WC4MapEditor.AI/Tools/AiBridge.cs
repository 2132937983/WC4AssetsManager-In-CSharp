using WC4MapEditor.Core.Models;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.AI.Tools;

/// <summary>
/// 主程序 → AI 子模块 的能力桥。
/// <para>
/// AI 子项目只依赖 Core，无法直接触达主程序层（MapData 实例、场景切换、Lua UI 钩子），
/// 由主程序在启动/加载地图后向本类注册委托，工具执行器按需调用。
/// </para>
/// <para>
/// 全部委托在 UI 线程上调用（工具执行器已负责调度），实现方无需再 Invoke。
/// </para>
/// </summary>
public static class AiBridge
{
    /// <summary>当前地图数据（null = 未加载地图）</summary>
    public static Func<MapData?>? MapDataProvider { get; set; }

    /// <summary>
    /// 执行一段 Lua 代码（经 ScriptRunner.RunCode，整批可撤销）。
    /// 返回 (成功?, 汇总输出文本)。
    /// </summary>
    public static Func<string, ScriptResult?>? LuaCodeRunner { get; set; }

    /// <summary>
    /// 执行控制台命令（CommandManager 优先，回落 CliCommandHost）。
    /// 返回 (成功?, 合并输出文本)。
    /// </summary>
    public static Func<string, (bool Ok, string Output)>? CommandRunner { get; set; }

    /// <summary>按名称打开编辑场景（等效用户点导航按钮）。返回是否找到并切换。</summary>
    public static Func<string, bool>? SceneOpener { get; set; }

    /// <summary>列出可切换的场景名（供 AI 提示与校验）</summary>
    public static Func<IReadOnlyList<string>>? SceneLister { get; set; }

    /// <summary>调度到 UI 线程执行（如 WPF Dispatcher.Invoke）</summary>
    public static Action<Action>? UiMarshal { get; set; }

    /// <summary>通知主程序地图数据已被 AI 工具修改（触发重渲染/状态栏提示）</summary>
    public static Action<string>? DataMutated { get; set; }

    /// <summary>
    /// 地图截图（"AI 视觉"的眼睛）。主程序在渲染场景初始化时把它注册到场景基类的截图实现上。
    /// 注册后 AI 工具 capture_screenshot 就能取图给模型看。
    /// </summary>
    public static Func<WC4MapEditor.Core.Services.MapScreenshotRequest,
                        WC4MapEditor.Core.Services.MapScreenshotResult>? ScreenshotCapturer { get; set; }

    public static WC4MapEditor.Core.Services.MapScreenshotResult CaptureScreenshot(
        WC4MapEditor.Core.Services.MapScreenshotRequest request)
    {
        var capturer = ScreenshotCapturer;
        if (capturer == null)
            return WC4MapEditor.Core.Services.MapScreenshotResult.Fail("主程序未注册截图能力（当前场景不支持截图）");

        var result = WC4MapEditor.Core.Services.MapScreenshotResult.Fail("截图未执行");
        RunOnUi(() => result = capturer(request));
        return result;
    }

    /// <summary>
    /// 创建新地图/战役/征服并切换到编辑场景。
    /// 参数: (类型 "campaign"/"conquest"/"map", 宽度, 高度, 军团数, 征服地图编号)。
    /// 返回: (成功?, 消息)。
    /// </summary>
    public static Func<string, int, int, int, int, (bool Ok, string Message)>? MapCreator { get; set; }

    public static void RunOnUi(Action action)
    {
        var marshal = UiMarshal;
        if (marshal != null) marshal(action);
        else action();
    }

    public static MapData? GetMapData()
    {
        MapData? result = null;
        RunOnUi(() => result = MapDataProvider?.Invoke());
        return result;
    }

    public static ScriptResult? RunLua(string code)
    {
        ScriptResult? result = null;
        RunOnUi(() => result = LuaCodeRunner?.Invoke(code));
        return result;
    }

    public static (bool Ok, string Output) RunCommand(string command)
    {
        (bool, string) result = (false, "主程序未注册命令执行器");
        RunOnUi(() => result = CommandRunner?.Invoke(command) ?? (false, "命令执行器未注册"));
        return result;
    }

    public static bool OpenScene(string sceneName)
    {
        bool result = false;
        RunOnUi(() => result = SceneOpener?.Invoke(sceneName) ?? false);
        return result;
    }

    public static IReadOnlyList<string> ListScenes()
    {
        IReadOnlyList<string> result = Array.Empty<string>();
        RunOnUi(() => result = SceneLister?.Invoke() ?? result);
        return result;
    }

    public static (bool Ok, string Message) CreateMap(string type, int width, int height, int legions, int mapNumber)
    {
        (bool, string) result = (false, "主程序未注册地图创建器");
        RunOnUi(() => result = MapCreator?.Invoke(type, width, height, legions, mapNumber) ?? (false, "地图创建器未注册"));
        return result;
    }
}