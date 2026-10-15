namespace WC4MapEditor.Core.Commands;

public sealed class DebugConsoleWriter : TextWriter
{
    private readonly object _target;

    public DebugConsoleWriter(object debugConsole)
    {
        _target = debugConsole;
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        if (value == null) return;
        var method = _target.GetType().GetMethod("WriteLine", [typeof(string)]);
        method?.Invoke(_target, [value]);
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        var method = _target.GetType().GetMethod("Write", [typeof(string)]);
        method?.Invoke(_target, [value]);
    }
}