namespace WC4MapEditor.Core.Brush;

/// <summary>
/// 画笔：开关、半径、形状与"画笔覆盖哪些格子"的几何计算。
/// 地形 / 省份 / 归属 / 单位四个模块共用这一份，半径上限也只在这里定义一次。
/// </summary>
public sealed class BrushEngine
{
    /// <summary>
    /// 画笔半径上限。[ ] 键、画笔设置窗口的输入框与各模块的半径属性都按它钳位
    /// （以前 ] 键封顶 20 而窗口能输入任意值，窗口里设成 30 后按一下 ] 反而缩回 20）。
    /// </summary>
    public const int MaxRadius = 20;

    /// <summary>形状标识值：与渲染层、画笔设置窗口比较用，不是界面文字，不能翻译。</summary>
    public const string ShapeCircle = "圆形";

    /// <inheritdoc cref="ShapeCircle"/>
    public const string ShapeSquare = "方形";

    private bool _active;
    private int _radius;
    private string _shape = ShapeCircle;

    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    public int Radius
    {
        get => _radius;
        set => _radius = ClampRadius(value);
    }

    public string Shape
    {
        get => _shape;
        set => _shape = value;
    }

    /// <summary>把半径夹到 0 ~ <see cref="MaxRadius"/>。</summary>
    public static int ClampRadius(int radius) => Math.Clamp(radius, 0, MaxRadius);

    public bool Toggle()
    {
        _active = !_active;
        return _active;
    }

    public void IncreaseRadius()
    {
        Radius = _radius + 1;
    }

    public void DecreaseRadius()
    {
        Radius = _radius - 1;
    }

    public List<(int col, int row)> GetHexesInBrush(int centerCol, int centerRow, int mapWidth, int mapHeight)
    {
        if (_shape == ShapeSquare)
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
