namespace WC4MapEditor.Core.Commands;

public interface ICommand<in TContext>
{
    CommandResult Execute(TContext context);
}

public interface ICommand
{
    CommandResult Execute(CommandContext context);
}

public readonly struct CommandResult
{
    public bool Success { get; }
    public string? Error { get; }
    public object? Data { get; }

    public CommandResult(bool success, string? error = null, object? data = null)
    {
        Success = success;
        Error = error;
        Data = data;
    }

    public static CommandResult Ok(object? data = null) => new(true, null, data);
    public static CommandResult Fail(string error) => new(false, error);
}
