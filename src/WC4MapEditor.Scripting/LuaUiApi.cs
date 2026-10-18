using MoonSharp.Interpreter;

namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给 Lua 的 <c>ui</c> 对象，提供界面交互能力（文件对话框等）。
/// <para>
/// 脚本里写作 <c>ui.SaveFileDialog("标题", "JSON文件|*.json", "data.json")</c>，
/// 返回用户选择的路径字符串，取消则返回 <c>nil</c>。
/// </para>
/// </summary>
[MoonSharpUserData]
public sealed class LuaUiApi
{
    private readonly Func<string, string, string, string?> _saveFileDialog;
    private readonly Action<string> _log;

    public LuaUiApi(
        Func<string, string, string, string?> saveFileDialog,
        Action<string> log)
    {
        _saveFileDialog = saveFileDialog ?? throw new ArgumentNullException(nameof(saveFileDialog));
        _log = log;
    }

    /// <summary>
    /// 弹出"另存为"对话框，返回用户选择的文件路径；取消返回 <c>nil</c>。
    /// <para>
    /// Lua 用法：<c>local path = ui.SaveFileDialog("保存JSON", "JSON文件|*.json", "data.json")</c>
    /// </para>
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="filter">文件筛选器，格式如 "JSON文件|*.json|所有文件|*.*"</param>
    /// <param name="defaultFileName">默认文件名</param>
    /// <returns>选择的路径，取消返回 null</returns>
    public string? SaveFileDialog(string title, string filter, string defaultFileName)
    {
        try
        {
            var path = _saveFileDialog(title, filter, defaultFileName);
            if (path != null)
                _log($"[UI] 保存到: {path}");
            else
                _log("[UI] 用户取消了保存");
            return path;
        }
        catch (Exception ex)
        {
            _log($"[UI] SaveFileDialog 错误: {ex.Message}");
            return null;
        }
    }
}