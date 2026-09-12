namespace WC4MapEditor.Core.Commands;

/// <summary>
/// 命令宿主抽象。
/// <para>
/// 渲染层与 UI 层通过此接口执行命令，而不直接依赖具体实现类，
/// 使渲染库不必感知 CLI 框架（System.CommandLine）的存在。
/// </para>
/// </summary>
public interface ICommandHost
{
    /// <summary>执行一行命令，返回退出码（0 表示成功）</summary>
    int Execute(string commandLine);

    /// <summary>
    /// 仅解析命令而不执行，返回解析错误与命令元信息。
    /// </summary>
    (List<string> Errors, string CommandName, string CommandDescription) GetParseErrors(string commandLine);

    /// <summary>获取命令帮助文本</summary>
    string GetHelp();

    /// <summary>重定向命令输出目标</summary>
    void SetOutput(TextWriter output);

    /// <summary>进入批量模式（批量执行期间暂停索引重建）</summary>
    void BeginBatchMode();

    /// <summary>退出批量模式，并触发一次数据变更通知</summary>
    void EndBatchMode();

    /// <summary>命令修改了地图数据后触发</summary>
    event Action? DataModified;
}
