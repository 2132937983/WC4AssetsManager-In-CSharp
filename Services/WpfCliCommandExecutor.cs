using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Services;

/// <summary>
/// WPF CLI 命令执行器实现
/// </summary>
public sealed class WpfCliCommandExecutor : ICliCommandExecutor
{
    private readonly Core.Commands.CommandManager _commandManager;

    public WpfCliCommandExecutor(Core.Commands.CommandManager commandManager)
    {
        _commandManager = commandManager;
    }

    public bool Execute(string commandLine)
    {
        var result = _commandManager.ExecuteCommand(commandLine);
        return result.Success;
    }
}