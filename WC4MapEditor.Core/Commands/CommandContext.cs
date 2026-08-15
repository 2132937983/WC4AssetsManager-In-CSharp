using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Commands;

public class CommandContext
{
    public MapData? MapData { get; set; }
    public string? ResourcePath { get; set; }
    public TextWriter? Output { get; set; }
    public CancellationToken CancellationToken { get; set; }

    public CommandContext()
    {
        Output = Console.Out;
        CancellationToken = CancellationToken.None;
    }
}
