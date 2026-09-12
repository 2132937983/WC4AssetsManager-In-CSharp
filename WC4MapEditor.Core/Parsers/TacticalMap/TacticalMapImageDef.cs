namespace WC4MapEditor.Core.Parsers;

public class TacticalMapImageDef
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefX { get; set; }
    public int RefY { get; set; }

    public override string ToString()
    {
        return $"{Name} ({X}, {Y}, {Width}x{Height})";
    }
}