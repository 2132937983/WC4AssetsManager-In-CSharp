using System.Diagnostics;

namespace WC4MapEditor.Core.Commands;

public sealed class CommandManager
{
    private static readonly object _lock = new();
    private static CommandManager? _instance;

    public static CommandManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CommandManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, CommandEntry> _commands = new(StringComparer.OrdinalIgnoreCase);
    private CommandContext? _context;

    private CommandManager() { }

    public void SetContext(CommandContext context) => _context = context;

    public void RegisterCommand(string name, Action<string[]> execute, string description = "", string usage = "")
    {
        _commands[name] = new CommandEntry(name, execute, description, usage);
    }

    public void UnregisterCommand(string name) => _commands.Remove(name);

    public CommandResult ExecuteCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return CommandResult.Fail("空命令");

        string trimmed = input.Trim();
        int spaceIdx = trimmed.IndexOf(' ');
        string commandName = spaceIdx < 0 ? trimmed : trimmed[..spaceIdx];
        string[] args = spaceIdx < 0 ? [] : ParseArguments(trimmed[(spaceIdx + 1)..]);

        if (!_commands.TryGetValue(commandName, out var entry))
        {
            string msg = $"未知命令: {commandName}，输入 help 查看可用命令";
            _context?.Output?.WriteLine(msg);
            return CommandResult.Fail(msg);
        }

        try
        {
            entry.Execute(args);
            return CommandResult.Ok();
        }
        catch (Exception ex)
        {
            string msg = $"命令执行错误: {ex.Message}";
            _context?.Output?.WriteLine(msg);
            return CommandResult.Fail(msg);
        }
    }

    public string GetHelp()
    {
        if (_commands.Count == 0)
            return "暂无可用命令";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 可用命令 ===");
        foreach (var entry in _commands.Values.OrderBy(e => e.Name))
        {
            sb.Append($"  {entry.Name}");
            if (!string.IsNullOrEmpty(entry.Usage))
                sb.Append($" {entry.Usage}");
            if (!string.IsNullOrEmpty(entry.Description))
                sb.Append($" - {entry.Description}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("内置命令:");
        sb.AppendLine("  help    - 显示此帮助信息");
        sb.AppendLine("  clear   - 清除控制台输出");
        sb.AppendLine("  exit    - 关闭控制台");
        return sb.ToString();
    }

    private static string[] ParseArguments(string argString)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in argString)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private sealed class CommandEntry
    {
        public string Name { get; }
        public Action<string[]> Execute { get; }
        public string Description { get; }
        public string Usage { get; }

        public CommandEntry(string name, Action<string[]> execute, string description, string usage)
        {
            Name = name;
            Execute = execute;
            Description = description;
            Usage = usage;
        }
    }
}