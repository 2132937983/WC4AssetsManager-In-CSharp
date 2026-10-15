using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 按【手绘边界】生成省区（省份编辑模式 U 键画完后的 P 键）。
/// <para>
/// 与 <see cref="BackgroundProvinceGenerator"/> 的区别在于"界线从哪来"：
/// 那个是从背景图灰度里提取界线，这个直接用用户描好的线条。后半段（连通域、
/// 按省会多源扩展、界线格吸收）思路相同，但这里是<b>格子级</b>处理。
/// </para>
/// <para>
/// <b>为什么不做像素级</b>：手绘边界本来就是"看着编辑器画面描的"，
/// 天然属于渲染坐标系（列步进 <see cref="Camera.HexHorizontalSpacing"/>）。
/// 而图片识别那套内部用的是另一种列步进（hexW * 0.75），
/// 硬把两者对接会引入约半个格子的系统性偏差。既然边界已经是人手画的，
/// 直接落到格子级既准确又简单，不需要中间那层像素映射。
/// </para>
/// </summary>
public static class DrawnBoundaryProvinceGenerator
{
    /// <summary>识别结果统计</summary>
    public readonly record struct Result(
        int BoundaryCells,
        int RegionCount,
        int FilledRegions,
        int FilledCells,
        int SkippedRegions,
        int CapitalCount);

    /// <summary>
    /// 执行分区并直接写回 <paramref name="mapData"/> 的省区值。
    /// 返回 null 表示输入不可用（没有边界、没有省会、尺寸不符）。
    /// </summary>
    /// <param name="boundaryMask">手绘边界掩码，按地图逻辑像素组织</param>
    /// <param name="maskWidth">掩码宽（逻辑像素）</param>
    /// <param name="maskHeight">掩码高（逻辑像素）</param>
    /// <param name="dilateCells">
    /// 边界格向外加厚的格数。0 = 不加厚。
    /// <para>
    /// 需要它是因为"线条穿过的格子"未必严格相邻：线沿格子边缘走时可能只擦过
    /// 对角两格的一角，这两格在六边形邻接下并不连通，省区就会从缺口漏过去，
    /// 表现为"某个省吃掉了邻省的地盘"。加厚一圈正好把这个缺口堵上。
    /// </para>
    /// </param>
    public static Result? Generate(
        MapData mapData,
        bool[] boundaryMask,
        int maskWidth,
        int maskHeight,
        int dilateCells = 1)
    {
        if (mapData == null || boundaryMask == null) return null;
        if (maskWidth <= 0 || maskHeight <= 0) return null;
        if (boundaryMask.Length < (long)maskWidth * maskHeight) return null;

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return null;

        int total = mapWidth * mapHeight;

        // ---------- 0. 省会：既是"能不能填充"的判据，也是扩展源 ----------
        var capitalIndices = new HashSet<int>();

        foreach (var capital in mapData.Capitals)
        {
            int idx = capital.Coordinate;
            if (idx >= 0 && idx < total)
                capitalIndices.Add(idx);
        }

        for (int i = 0; i < total; i++)
        {
            ref var provinceRef = ref mapData.GetProvinceRef(i);
            if (provinceRef.IsValid && provinceRef.ProvinceValue == i)
                capitalIndices.Add(i);
        }

        if (capitalIndices.Count == 0) return null;

        // ---------- 1. 边界像素 → 边界格子 ----------
        // 反算每个边界像素落在哪个格子，比"逐格采样中心"更稳：
        // 线擦过格子边角时也能被算进来，不会因为没盖住中心就被忽略。
        var cellBoundary = new bool[total];

        for (int y = 0; y < maskHeight; y++)
        {
            int rowBase = y * maskWidth;

            for (int x = 0; x < maskWidth; x++)
            {
                if (!boundaryMask[rowBase + x]) continue;

                var (col, row) = MapPixelToHex(x, y);
                if (col < 0 || row < 0 || col >= mapWidth || row >= mapHeight) continue;

                cellBoundary[row * mapWidth + col] = true;
            }
        }

        int boundaryCellCount = 0;
        for (int i = 0; i < total; i++)
            if (cellBoundary[i]) boundaryCellCount++;

        if (boundaryCellCount == 0) return null;

        // ---------- 2. 边界加厚 ----------
        if (dilateCells > 0)
        {
            var dilated = new bool[total];
            var neighbors = new List<int>(6);

            for (int index = 0; index < total; index++)
            {
                if (!cellBoundary[index]) continue;

                dilated[index] = true;

                int col = index % mapWidth;
                int row = index / mapWidth;
                CollectNeighbors(col, row, mapWidth, mapHeight, neighbors, dilateCells);

                foreach (int n in neighbors)
                    dilated[n] = true;
            }

            cellBoundary = dilated;

            boundaryCellCount = 0;
            for (int i = 0; i < total; i++)
                if (cellBoundary[i]) boundaryCellCount++;
        }

        // ---------- 3. 格子级连通域（边界格不参与） ----------
        var visited = new bool[total];
        var groupQueue = new Queue<int>();
        var group = new List<int>();
        var neighborsBuf = new List<int>(6);

        var owner = new int[total];
        for (int i = 0; i < total; i++)
            owner[i] = -1;

        int regionCount = 0;
        int filledRegions = 0;
        int filledCells = 0;
        int skippedRegions = 0;

        for (int start = 0; start < total; start++)
        {
            if (visited[start] || cellBoundary[start]) continue;

            // 收一块连通区域
            group.Clear();
            groupQueue.Clear();
            groupQueue.Enqueue(start);
            visited[start] = true;

            while (groupQueue.Count > 0)
            {
                int index = groupQueue.Dequeue();
                group.Add(index);

                int col = index % mapWidth;
                int row = index / mapWidth;
                CollectNeighbors(col, row, mapWidth, mapHeight, neighborsBuf, 1);

                foreach (int n in neighborsBuf)
                {
                    if (visited[n] || cellBoundary[n]) continue;

                    visited[n] = true;
                    groupQueue.Enqueue(n);
                }
            }

            regionCount++;

            // ---------- 4. 区域内以省会为源做多源扩展 ----------
            var sources = new List<int>();
            foreach (int index in group)
            {
                if (capitalIndices.Contains(index))
                    sources.Add(index);
            }

            if (sources.Count == 0)
            {
                skippedRegions++;
                continue;
            }

            groupQueue.Clear();
            foreach (int source in sources)
            {
                owner[source] = source;
                groupQueue.Enqueue(source);
            }

            while (groupQueue.Count > 0)
            {
                int index = groupQueue.Dequeue();
                int col = index % mapWidth;
                int row = index / mapWidth;
                CollectNeighbors(col, row, mapWidth, mapHeight, neighborsBuf, 1);

                foreach (int n in neighborsBuf)
                {
                    if (owner[n] != -1) continue;
                    if (cellBoundary[n]) continue;      // 不越过边界

                    owner[n] = owner[index];
                    groupQueue.Enqueue(n);
                }
            }

            filledRegions++;
        }

        // ---------- 5. 边界格就近吸收 ----------
        // 边界本身也需要有归属，否则省区上会留一圈空洞。
        // 只吸收边界格（不侵占别省的内部）。
        groupQueue.Clear();
        for (int i = 0; i < total; i++)
        {
            if (owner[i] != -1)
                groupQueue.Enqueue(i);
        }

        while (groupQueue.Count > 0)
        {
            int index = groupQueue.Dequeue();
            int col = index % mapWidth;
            int row = index / mapWidth;
            CollectNeighbors(col, row, mapWidth, mapHeight, neighborsBuf, 1);

            foreach (int n in neighborsBuf)
            {
                if (owner[n] != -1) continue;
                if (!cellBoundary[n]) continue;

                owner[n] = owner[index];
                groupQueue.Enqueue(n);
            }
        }

        // ---------- 6. 写回省区值 ----------
        for (int index = 0; index < total; index++)
        {
            int sourceIndex = owner[index];
            if (sourceIndex < 0) continue;

            // 省会格保留原值，维持"省区值 == 省会索引"的约定
            if (capitalIndices.Contains(index)) continue;

            // 海洋不属于任何省区
            if (mapData.GetTerrainRef(index).TileType1 == 1) continue;

            mapData.GetProvinceRef(index).ProvinceValue =
                (ushort)(sourceIndex <= 0xFFFF ? sourceIndex : sourceIndex % 0x10000);

            filledCells++;
        }

        return new Result(
            boundaryCellCount, regionCount, filledRegions, filledCells,
            skippedRegions, capitalIndices.Count);
    }

    /// <summary>
    /// 地图逻辑像素 → 格子。与 <see cref="Camera.ScreenToHex"/> 同一套公式
    /// （只是省掉了 offset 与 zoom，因为掩码本身就存的是未变换的逻辑像素）。
    /// </summary>
    private static (int col, int row) MapPixelToHex(double mapX, double mapY)
    {
        int col = (int)Math.Round(mapX / Camera.HexHorizontalSpacing);
        double rowOffset = (col % 2 == 1) ? Camera.HexVerticalSpacing / 2.0 : 0;
        int row = (int)Math.Round((mapY - rowOffset) / Camera.HexVerticalSpacing);
        return (col, row);
    }

    /// <summary>
    /// 收集六边形邻居（奇数列下移，与渲染层的 (col % 2) * hexSpacingY / 2 一致）。
    /// <paramref name="rings"/> 为 1 时是 6 邻接，大于 1 时扩散到对应层数。
    /// </summary>
    private static void CollectNeighbors(
        int col, int row, int width, int height, List<int> output, int rings)
    {
        output.Clear();

        for (int dc = -rings; dc <= rings; dc++)
        {
            for (int dr = -rings; dr <= rings; dr++)
            {
                if (dc == 0 && dr == 0) continue;

                int c = col + dc;
                int r = row + dr;

                if (c < 0 || c >= width || r < 0 || r >= height) continue;

                // 用六边形距离判定，避免把"方块邻域"当成六边形邻域
                if (Brush.BrushEngine.CalculateHexDistance(col, row, c, r) > rings) continue;

                output.Add(r * width + c);
            }
        }
    }
}
