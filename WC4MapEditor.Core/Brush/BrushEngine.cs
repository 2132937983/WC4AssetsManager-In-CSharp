namespace WC4MapEditor.Core.Brush;

public sealed class BrushEngine
{
    private bool _active;
    private int _radius;
    private string _shape = "圆形";

    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    public int Radius
    {
        get => _radius;
        set => _radius = Math.Max(0, value);
    }

    public string Shape
    {
        get => _shape;
        set => _shape = value;
    }

    public bool Toggle()
    {
        _active = !_active;
        return _active;
    }

    public void IncreaseRadius()
    {
        _radius++;
    }

    public void DecreaseRadius()
    {
        if (_radius > 0) _radius--;
    }

    public List<(int col, int row)> GetHexesInBrush(int centerCol, int centerRow, int mapWidth, int mapHeight)
    {
        if (_shape == "方形")
            return GetHexesInSquare(centerCol, centerRow, _radius, mapWidth, mapHeight);
        return GetHexesInRadius(centerCol, centerRow, _radius, mapWidth, mapHeight);
    }

    public static List<(int col, int row)> GetHexesInRadius(int centerCol, int centerRow, int radius, int mapWidth, int mapHeight)
    {
        var hexes = new List<(int, int)>();
        for (int row = Math.Max(0, centerRow - radius); row <= Math.Min(mapHeight - 1, centerRow + radius); row++)
        {
            for (int col = Math.Max(0, centerCol - radius); col <= Math.Min(mapWidth - 1, centerCol + radius); col++)
            {
                if (CalculateHexDistance(centerCol, centerRow, col, row) <= radius)
                    hexes.Add((col, row));
            }
        }
        return hexes;
    }

    public static List<(int col, int row)> GetHexesInSquare(int centerCol, int centerRow, int size, int mapWidth, int mapHeight)
    {
        var hexes = new List<(int, int)>();
        for (int row = Math.Max(0, centerRow - size); row <= Math.Min(mapHeight - 1, centerRow + size); row++)
        {
            for (int col = Math.Max(0, centerCol - size); col <= Math.Min(mapWidth - 1, centerCol + size); col++)
                hexes.Add((col, row));
        }
        return hexes;
    }

    public static int CalculateHexDistance(int col1, int row1, int col2, int row2)
    {
        int x1 = col1;
        int z1 = row1 - (col1 >> 1);
        int y1 = -x1 - z1;

        int x2 = col2;
        int z2 = row2 - (col2 >> 1);
        int y2 = -x2 - z2;

        return (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) + Math.Abs(z1 - z2)) >> 1;
    }
}