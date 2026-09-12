namespace WC4MapEditor.Core.Models.Modules;

/// <summary>
/// 地形修改模块 - 直接操作Terrain结构体数组
/// </summary>
public static class TerrainModule
{
    /// <summary>
    /// 设置地形类型
    /// </summary>
    public static void SetTerrainType(ref Terrain terrain, byte type)
    {
        terrain.TileType1 = type;
    }

    /// <summary>
    /// 设置地形变体
    /// </summary>
    public static void SetDecorationType(ref Terrain terrain, byte decoration)
    {
        terrain.DecorationType1 = decoration;
    }

    /// <summary>
    /// 设置贴图偏移
    /// </summary>
    public static void SetTextureOffset(ref Terrain terrain, byte x, byte y)
    {
        terrain.TextureOffsetX1 = x;
        terrain.TextureOffsetY1 = y;
    }

    /// <summary>
    /// 设置河流值
    /// </summary>
    public static void SetRiverValue(ref Terrain terrain, byte value)
    {
        terrain.RiverValue = value;
    }

    /// <summary>
    /// 批量设置地形类型
    /// </summary>
    public static void SetTerrainTypeBatch(Span<Terrain> terrains, byte type)
    {
        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            t.TileType1 = type;
            terrains[i] = t;
        }
    }

    /// <summary>
    /// 填充区域（洪水填充）
    /// </summary>
    public static void FloodFill(Span<Terrain> terrains, int width, int height,
        int startX, int startY, byte targetType, byte replacementType)
    {
        if (targetType == replacementType) return;

        var stack = new Stack<(int x, int y)>();
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            int index = y * width + x;
            ref Terrain terrain = ref terrains[index];

            if (terrain.TileType1 != targetType) continue;

            terrain.TileType1 = replacementType;
            terrains[index] = terrain;

            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }

    /// <summary>
    /// 复制地形数据
    /// </summary>
    public static void CopyTerrain(ref Terrain source, ref Terrain target)
    {
        target = source;
    }

    /// <summary>
    /// 随机变体
    /// </summary>
    public static void RandomizeDecoration(ref Terrain terrain, Random random)
    {
        terrain.DecorationType1 = (byte)random.Next(0, 8);
        terrain.TextureOffsetX1 = (byte)random.Next(0, 4);
        terrain.TextureOffsetY1 = (byte)random.Next(0, 4);
    }
}
