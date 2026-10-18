using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 从背景图识别省区。
/// <para>
/// 流程：背景图整幅转灰度 → 求界线掩码（深色线 + 灰度梯度，再膨胀补断口）→
/// 在<b>像素级</b>把非界线像素分成一个个连通区域（即"像素省区"）→
/// 把像素区域按六边形几何映射回地图格子 → 每片格子区域内以省会格为源做多源扩展 →
/// 界线格就近吸收 → 写回省区值（跳过省会格与海洋）。
/// </para>
/// <para>
/// 两个关键点决定了效果：
/// <list type="number">
/// <item><b>必须在像素级分割</b>：省界只有一两像素宽，先在格子级判"含界线的格子"
/// 等于把细线加粗几十倍，省份会被切碎。</item>
/// <item><b>界线必须真的把两省隔开</b>：若界线有断口，两省会落进同一个像素区域，
/// 之后按"最近省会"扩展就会越过真实省界去抢地盘。</item>
/// </list>
/// </para>
/// </summary>
public static class BackgroundProvinceGenerator
{
    /// <summary>自动求灰度阈值（用 Otsu）</summary>
    public const int AutoDarkThreshold = -1;

    /// <summary>自动阈值下的默认值，等价于 <see cref="AutoDarkThreshold"/></summary>
    public const int DefaultDarkThreshold = AutoDarkThreshold;

    /// <summary>相邻像素灰度差达到此值即视为界线；0 表示禁用梯度判据（只认深色线）</summary>
    public const int DefaultGradientThreshold = 25;

    /// <summary>小于该像素数的连通区域视为噪点（斑点等），不参与格子归属</summary>
    public const int DefaultMinRegionPixels = 50;

    /// <summary>
    /// 界线连通块小于该像素数就当作噪点剔除（默认）。
    /// <para>
    /// 地图上的地名文字也是深色，会被深色/梯度判据判成界线，在省区内部留下一团团假边界。
    /// 真正的省界是细长的长条（动辄数千像素），文字笔画则是孤立小块，按连通块面积即可分开。
    /// </para>
    /// </summary>
    public const int DefaultMinBoundaryPixels = 100;

    /// <summary>识别结果统计（含诊断信息，便于现场调参）</summary>
    public readonly record struct Result(
        int RegionCount,
        int FilledRegions,
        int FilledCells,
        int SkippedRegions,
        int PixelRegionCount,
        int BoundaryCells,
        int CapitalCount,
        int Threshold,
        bool ThresholdIsAuto,
        int GradientThreshold,
        double DarkRatio,
        double BoundaryRatio,
        byte GrayMin,
        byte GrayMean,
        byte GrayMax,
        int RemovedBoundaryPixels,
        int MinBoundaryPixels);

    /// <summary>
    /// 执行识别并按省会填充省区值（直接修改 <paramref name="mapData"/>）。
    /// 返回 null 表示输入不可用（无图片、转灰度失败、图太大、或地图上还没有省会）。
    /// </summary>
    /// <param name="darkThreshold">
    /// 灰度低于此值算界线；传 <see cref="AutoDarkThreshold"/> 用 Otsu 自动求取。
    /// 调大 = 更多像素算界线（区域更碎），调小 = 界线更少（区域更整）。
    /// </param>
    /// <param name="gradientThreshold">
    /// 与相邻像素灰度差达到此值也算界线。用来抓浅色界线、以及两省颜色跳变处，
    /// 这类界线灰度并不低，只靠 <paramref name="darkThreshold"/> 会漏掉。
    /// </param>
    /// <param name="minRegionPixels">小于该像素数的区域按噪点丢弃，不参与格子归属</param>
    /// <param name="minBoundaryPixels">
    /// 界线连通块小于该像素数就当作噪点剔除，0 或 1 表示不过滤。
    /// 用来去掉地名文字 —— 它们同样是深色，会被判成界线，在省区内部制造假边界。
    /// </param>
    /// <param name="debugSink">
    /// 诊断快照接收器（可选）。传入非 null 时，会把<b>灰度图</b>与<b>界线掩码</b>
    /// （深色 + 梯度判定，再膨胀 1 像素之后的最终结果）回传给调用方，
    /// 供导出成图片人工核对界线是否合格。
    /// 参数依次为：灰度图、界线掩码（长度 = 宽 × 高）、灰度阈值。
    /// 正常识别传 null，不产生任何额外开销。
    /// </param>
    public static Result? Generate(
        MapData mapData,
        IViewLayerImageProvider provider,
        int darkThreshold = AutoDarkThreshold,
        int gradientThreshold = DefaultGradientThreshold,
        int minRegionPixels = DefaultMinRegionPixels,
        int minBoundaryPixels = DefaultMinBoundaryPixels,
        Action<GrayscaleImage, bool[], int>? debugSink = null)
    {
        if (mapData == null || provider == null || !provider.HasImage) return null;

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;
        if (mapWidth <= 0 || mapHeight <= 0) return null;

        int total = mapWidth * mapHeight;

        // 省会（省区中心点）所在格子：既是"区域是否可填充"的判据，也是扩展源。
        // 两种来源都算：
        //   1) 军团编辑模式 F 键设置的 Capital 列表；
        //   2) "省区值 == 自身索引"的格子 —— 省份编辑模式 X 键写入的约定，
        //      渲染层画省会标记用的也正是它。
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

        // ---------- 1. 背景图 → 灰度图 ----------
        GrayscaleImage? gray = provider.ExtractGrayscale();
        if (gray == null || gray.Width <= 0 || gray.Height <= 0) return null;

        int pixelWidth = gray.Width;
        int pixelHeight = gray.Height;
        long pixelTotalLong = (long)pixelWidth * pixelHeight;
        if (pixelTotalLong <= 0 || pixelTotalLong > int.MaxValue) return null;

        int pixelCount = (int)pixelTotalLong;

        // 摊平成紧凑数组；去掉 Skia 行填充后，索引就是 y * pixelWidth + x
        byte[] pixels = new byte[pixelCount];
        for (int y = 0; y < pixelHeight; y++)
            Buffer.BlockCopy(gray.Pixels, y * gray.RowStride, pixels, y * pixelWidth, pixelWidth);

        // ---------- 2. 灰度直方图 → Otsu 自动阈值 ----------
        var histogram = new int[256];
        long graySum = 0;
        byte grayMin = 255;
        byte grayMax = 0;

        for (int i = 0; i < pixelCount; i++)
        {
            byte g = pixels[i];
            histogram[g]++;
            graySum += g;
            if (g < grayMin) grayMin = g;
            if (g > grayMax) grayMax = g;
        }

        bool thresholdIsAuto = darkThreshold < 0 || darkThreshold > 255;
        int threshold = thresholdIsAuto ? ComputeOtsuThreshold(histogram, pixelCount) : darkThreshold;

        // ---------- 3. 界线掩码：深色线 + 灰度梯度，并膨胀 1 像素补断口 ----------
        var isBoundary = new bool[pixelCount];
        long darkPixels = 0;
        long boundaryPixels = 0;

        // 3.1 深色像素
        for (int i = 0; i < pixelCount; i++)
        {
            if (pixels[i] < threshold)
            {
                isBoundary[i] = true;
                darkPixels++;
            }
        }

        // 3.2 梯度：浅色界线、两省颜色跳变处灰度并不低，这一条才能抓到
        if (gradientThreshold > 0)
        {
            for (int y = 0; y < pixelHeight; y++)
            {
                int rowBase = y * pixelWidth;

                for (int x = 0; x < pixelWidth; x++)
                {
                    int i = rowBase + x;
                    if (isBoundary[i]) continue;

                    int g = pixels[i];
                    int maxDelta = 0;

                    if (x > 0) maxDelta = Math.Max(maxDelta, Math.Abs(g - pixels[i - 1]));
                    if (x + 1 < pixelWidth) maxDelta = Math.Max(maxDelta, Math.Abs(g - pixels[i + 1]));
                    if (y > 0) maxDelta = Math.Max(maxDelta, Math.Abs(g - pixels[i - pixelWidth]));
                    if (y + 1 < pixelHeight) maxDelta = Math.Max(maxDelta, Math.Abs(g - pixels[i + pixelWidth]));

                    if (maxDelta >= gradientThreshold)
                        isBoundary[i] = true;
                }
            }
        }

        // 3.3 膨胀 1 像素：地图上的省界常有极小断口（印刷断线、抗锯齿），
        //     不补上就会让相邻两省在像素级连通，之后按"最近省会"扩展时会越过真实省界。
        var inflated = new bool[pixelCount];
        for (int y = 0; y < pixelHeight; y++)
        {
            int rowBase = y * pixelWidth;

            for (int x = 0; x < pixelWidth; x++)
            {
                int i = rowBase + x;
                if (!isBoundary[i]) continue;

                inflated[i] = true;
                if (x > 0) inflated[i - 1] = true;
                if (x + 1 < pixelWidth) inflated[i + 1] = true;
                if (y > 0) inflated[i - pixelWidth] = true;
                if (y + 1 < pixelHeight) inflated[i + pixelWidth] = true;
            }
        }

        for (int i = 0; i < pixelCount; i++)
        {
            if (isBoundary[i]) inflated[i] = true;
            if (inflated[i]) boundaryPixels++;
        }

        double darkRatio = (double)darkPixels / pixelCount;

        // ---------- 3.4 剔除小块界线（地名文字等） ----------
        // 地名文字同样是深色，会被上面两条判据当成界线，在省区内部留下一团团假边界，
        // 把本该完整的省区切碎。真省界是细长的长条，文字笔画是孤立小块，按连通块面积即可分开。
        int removedBoundaryPixels =
            RemoveSmallBoundaryBlobs(inflated, pixelWidth, pixelHeight, minBoundaryPixels);
        if (removedBoundaryPixels > 0)
            boundaryPixels -= removedBoundaryPixels;

        double boundaryRatio = (double)boundaryPixels / pixelCount;

        // 诊断快照：把灰度图与最终界线掩码交给调用方（仅当传入 sink）。
        // 放在这里是因为此刻界线已定型（深色 + 梯度 + 膨胀 + 去文字噪点），
        // 正是"能不能把相邻两省隔开"的判据；再往后就是按它分割了。
        debugSink?.Invoke(
            new GrayscaleImage(pixels, pixelWidth, pixelHeight, pixelWidth), inflated, threshold);

        // ---------- 4. 像素级连通域（界线像素不参与，4 邻接） ----------
        var label = new int[pixelCount];
        var regionSizes = new List<int>();
        var queue = new Queue<int>();
        int labelCount = 0;

        for (int seed = 0; seed < pixelCount; seed++)
        {
            if (label[seed] != 0 || inflated[seed]) continue;

            labelCount++;
            label[seed] = labelCount;
            queue.Enqueue(seed);
            int size = 0;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                size++;

                int y = index / pixelWidth;
                int x = index - y * pixelWidth;

                if (x > 0) TryEnqueue(index - 1);
                if (x + 1 < pixelWidth) TryEnqueue(index + 1);
                if (y > 0) TryEnqueue(index - pixelWidth);
                if (y + 1 < pixelHeight) TryEnqueue(index + pixelWidth);
            }

            regionSizes.Add(size);

            void TryEnqueue(int n)
            {
                if (label[n] != 0 || inflated[n]) return;
                label[n] = labelCount;
                queue.Enqueue(n);
            }
        }

        int pixelRegionCount = labelCount;

        // ---------- 5. 像素区域 → 格子归属（按六边形实际形状取样） ----------
        // 六边形几何与渲染层 / TerrainRecognizer 保持一致：
        //   mapPixelWidth  = mapWidth * hexW * 0.75 + hexW * 0.25
        //   mapPixelHeight = mapHeight * hexH + hexH * 0.5
        //   格子中心 X = col * hexW * 0.75 + hexW / 2
        //   格子中心 Y = row * hexH + hexH / 2 + (col % 2) * hexH * 0.5
        // 关键是列步进只有 hexW * 0.75（六边形左右互相咬合），不是 hexW。
        var cellLabel = new int[total];
        var tally = new Dictionary<int, int>();

        double hexW = Camera.HexHorizontalSpacing;
        double hexH = Camera.HexVerticalSpacing;

        double mapPixelWidth = mapWidth * hexW * 0.75 + hexW * 0.25;
        double mapPixelHeight = mapHeight * hexH + hexH * 0.5;

        double scaleX = pixelWidth / mapPixelWidth;
        double scaleY = pixelHeight / mapPixelHeight;

        double halfWidth = hexW * scaleX / 2;      // 六边形在图片上的半宽
        double halfHeight = hexH * scaleY / 2;     // 六边形在图片上的半高
        double quarterHeight = hexH * scaleY / 4;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                double centerX = (col * hexW * 0.75 + hexW / 2) * scaleX;
                double centerY = (row * hexH + hexH / 2 + ((col & 1) == 1 ? hexH * 0.5 : 0)) * scaleY;

                int py0 = (int)Math.Floor(centerY - halfHeight);
                int py1 = (int)Math.Ceiling(centerY + halfHeight);
                if (py0 < 0) py0 = 0;
                if (py1 > pixelHeight) py1 = pixelHeight;

                tally.Clear();
                int bestLabel = 0;
                int bestCount = 0;

                for (int y = py0; y < py1; y++)
                {
                    double dy = Math.Abs(y + 0.5 - centerY);
                    if (dy > halfHeight) continue;

                    // flat-top 六边形：中间四分之一高度处最宽，往上下两端收成一个尖点
                    double maxDx = dy <= quarterHeight
                        ? halfWidth
                        : halfWidth * (halfHeight - dy) / quarterHeight;

                    int xStart = (int)Math.Ceiling(centerX - maxDx);
                    int xEnd = (int)Math.Floor(centerX + maxDx);
                    if (xStart < 0) xStart = 0;
                    if (xEnd > pixelWidth - 1) xEnd = pixelWidth - 1;

                    int rowBase = y * pixelWidth;
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        int lb = label[rowBase + x];
                        if (lb == 0) continue;
                        if (regionSizes[lb - 1] < minRegionPixels) continue;

                        tally.TryGetValue(lb, out int seen);
                        seen++;
                        tally[lb] = seen;

                        if (seen > bestCount)
                        {
                            bestCount = seen;
                            bestLabel = lb;
                        }
                    }
                }

                // 界线像素占多数的格子保持 0，不归入任何区域
                if (bestLabel != 0)
                    cellLabel[row * mapWidth + col] = bestLabel;
            }
        }

        int boundaryCells = 0;
        for (int i = 0; i < total; i++)
            if (cellLabel[i] == 0) boundaryCells++;

        // ---------- 6. 格子级成组，并在每片像素省区内按省会做多源扩展 ----------
        var visited = new bool[total];
        var groupQueue = new Queue<int>();
        var group = new List<int>();
        var neighbors = new List<int>(6);
        var owner = new int[total];
        for (int i = 0; i < total; i++)
            owner[i] = -1;

        int regionCount = 0;
        int filledRegions = 0;
        int filledCells = 0;
        int skippedRegions = 0;

        for (int start = 0; start < total; start++)
        {
            if (visited[start]) continue;

            int seedLabel = cellLabel[start];
            if (seedLabel == 0) continue;

            // 先收集这一片像素省区覆盖到的所有格子
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

                CollectNeighbors(col, row, mapWidth, mapHeight, neighbors);
                foreach (int neighbor in neighbors)
                {
                    if (visited[neighbor]) continue;
                    if (cellLabel[neighbor] != seedLabel) continue;

                    visited[neighbor] = true;
                    groupQueue.Enqueue(neighbor);
                }
            }

            regionCount++;

            // 区域内的省会格就是扩展源；一片省区里可能有多个省会（县级的中心点），
            // 这时按"离哪个省会最近就归谁"来分，而不是让其中一个吃掉整片。
            var sources = new List<int>();
            foreach (int index in group)
            {
                if (capitalIndices.Contains(index))
                    sources.Add(index);
            }

            // 没有省会的区域先不填，留给用户后续处理（G/E/I 等命令）
            if (sources.Count == 0)
            {
                skippedRegions++;
                continue;
            }

            // 多源 BFS：从所有省会格同时向外扩展，各自吞下离自己最近的部分
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

                CollectNeighbors(col, row, mapWidth, mapHeight, neighbors);
                foreach (int neighbor in neighbors)
                {
                    if (owner[neighbor] != -1) continue;
                    if (cellLabel[neighbor] != seedLabel) continue;   // 不跨越省界

                    owner[neighbor] = owner[index];
                    groupQueue.Enqueue(neighbor);
                }
            }

            filledRegions++;
        }

        // ---------- 7. 界线格就近吸收 ----------
        // 被判为"界线像素占多数"的格子、以及被噪点门槛过滤掉的格子都没有归属，
        // 会在省界处留下一圈空洞。这里让已归属的格子向外扩散，
        // 把空洞按"离谁最近就归谁"补上。只吸收界线格（cellLabel == 0），不侵占其它省区的格子。
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

            CollectNeighbors(col, row, mapWidth, mapHeight, neighbors);
            foreach (int neighbor in neighbors)
            {
                if (owner[neighbor] != -1) continue;
                if (cellLabel[neighbor] != 0) continue;

                owner[neighbor] = owner[index];
                groupQueue.Enqueue(neighbor);
            }
        }

        // ---------- 8. 按归属写回省区值 ----------
        for (int index = 0; index < total; index++)
        {
            int sourceIndex = owner[index];
            if (sourceIndex < 0) continue;

            // 省会格一律保留原值：既避免把用户设好的省会冲掉，
            // 也保证"省区值 == 省会索引"的约定不被破坏。
            if (capitalIndices.Contains(index)) continue;

            // 海洋不属于任何省区
            if (IsSeaTerrain(mapData, index)) continue;

            int col = index % mapWidth;
            int row = index / mapWidth;
            ushort provinceValue = (ushort)(sourceIndex <= 0xFFFF ? sourceIndex : sourceIndex % 0x10000);
            mapData.GetProvinceRef(col, row).ProvinceValue = provinceValue;
            filledCells++;
        }

        return new Result(
            regionCount, filledRegions, filledCells, skippedRegions,
            pixelRegionCount, boundaryCells, capitalIndices.Count,
            threshold, thresholdIsAuto, gradientThreshold,
            darkRatio, boundaryRatio,
            grayMin, (byte)(graySum / pixelCount), grayMax,
            removedBoundaryPixels, minBoundaryPixels);
    }

    /// <summary>
    /// 剔除过小的界线连通块（地名文字、斑点等），就地修改 <paramref name="mask"/>。
    /// <para>
    /// <b>为什么需要</b>：地图上的地名（"北京""新疆"等）也是深色，会被深色/梯度判据判成界线。
    /// 这些文字团落在省区内部时，会把原本完整的一片省区切成好几块，导致
    /// "一地一省"，或让本该被省会填充的区域因为过小（受 minRegionPixels 限制）而被丢弃。
    /// </para>
    /// <para>
    /// <b>怎么区分</b>：真省界是细长的连通长条（动辄数千像素），文字笔画是孤立小块（数十像素），
    /// 所以按连通块面积过滤即可。用 8 邻接统计 —— 文字笔画常沿对角线相连，
    /// 4 邻接会把一个字拆成好几个更小的块，反而不利于整体剔除。
    /// </para>
    /// </summary>
    /// <param name="minPixels">小于该像素数的连通块被剔除；≤1 表示不过滤</param>
    /// <returns>被剔除的像素总数</returns>
    private static int RemoveSmallBoundaryBlobs(bool[] mask, int width, int height, int minPixels)
    {
        if (minPixels <= 1) return 0;

        int total = width * height;
        var visited = new bool[total];
        var stack = new Stack<int>();
        var blob = new List<int>();
        int removed = 0;

        for (int seed = 0; seed < total; seed++)
        {
            if (!mask[seed] || visited[seed]) continue;

            // 收集这一块界线
            blob.Clear();
            stack.Clear();
            stack.Push(seed);
            visited[seed] = true;

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                blob.Add(index);

                int y = index / width;
                int x = index - y * width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= height) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = x + dx;
                        if (nx < 0 || nx >= width) continue;

                        int n = ny * width + nx;
                        if (visited[n] || !mask[n]) continue;

                        visited[n] = true;
                        stack.Push(n);
                    }
                }
            }

            // 太小的块视为文字/噪点，整块抹掉
            if (blob.Count < minPixels)
            {
                foreach (int index in blob)
                    mask[index] = false;

                removed += blob.Count;
            }
        }

        return removed;
    }

    /// <summary>
    /// Otsu 最大类间方差法：在灰度直方图上找一个阈值，使"界线/底色"两类的类间方差最大。
    /// </summary>
    private static int ComputeOtsuThreshold(int[] histogram, long total)
    {
        double totalWeightedSum = 0;
        for (int i = 0; i < 256; i++)
            totalWeightedSum += (double)i * histogram[i];

        double backgroundWeight = 0;
        double backgroundSum = 0;
        double maxVariance = -1;
        int bestThreshold = 0;

        for (int t = 0; t < 256; t++)
        {
            backgroundWeight += histogram[t];
            if (backgroundWeight <= 0) continue;

            double foregroundWeight = total - backgroundWeight;
            if (foregroundWeight <= 0) break;

            backgroundSum += (double)t * histogram[t];

            double backgroundMean = backgroundSum / backgroundWeight;
            double foregroundMean = (totalWeightedSum - backgroundSum) / foregroundWeight;
            double diff = backgroundMean - foregroundMean;
            double variance = backgroundWeight * foregroundWeight * diff * diff;

            if (variance > maxVariance)
            {
                maxVariance = variance;
                bestThreshold = t;
            }
        }

        return bestThreshold;
    }

    /// <summary>
    /// 是否为海洋格（TileType1 == 1），判据与 <c>ProvinceModifier.IsSeaTerrain</c> 一致。
    /// 海洋不参与省区填充。
    /// </summary>
    private static bool IsSeaTerrain(MapData mapData, int index)
    {
        return mapData.GetTerrainRef(index).TileType1 == 1;
    }

    /// <summary>
    /// 收集六边形邻居（奇数列下移，与渲染层的 <c>(col % 2) * hexSpacingY / 2</c> 偏移一致）。
    /// </summary>
    private static void CollectNeighbors(int col, int row, int width, int height, List<int> output)
    {
        output.Clear();

        Add(col - 1, row);
        Add(col + 1, row);
        Add(col, row - 1);
        Add(col, row + 1);

        if ((col & 1) == 1)
        {
            Add(col - 1, row + 1);
            Add(col + 1, row + 1);
        }
        else
        {
            Add(col - 1, row - 1);
            Add(col + 1, row - 1);
        }

        void Add(int c, int r)
        {
            if (c < 0 || c >= width || r < 0 || r >= height) return;
            output.Add(r * width + c);
        }
    }
}
