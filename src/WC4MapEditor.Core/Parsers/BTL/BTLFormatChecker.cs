using System.Text;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>问题的严重程度</summary>
public enum BTLIssueLevel
{
    /// <summary>致命：文件结构已损坏，解析结果不可信</summary>
    Error,

    /// <summary>异常：能解析，但数据不合约定，游戏里可能出问题</summary>
    Warning,

    /// <summary>提示：可疑但未必是错</summary>
    Info
}

/// <summary>一条检查结果</summary>
/// <param name="Level">严重程度</param>
/// <param name="Category">分类，如「文件结构」「头部」「建筑」</param>
/// <param name="Message">说明</param>
/// <param name="Offset">相关偏移（未知为 -1）</param>
public readonly record struct BTLIssue(
    BTLIssueLevel Level,
    string Category,
    string Message,
    long Offset = -1);

/// <summary>一次检查的完整报告</summary>
public sealed class BTLCheckReport
{
    public required string FilePath { get; init; }
    public required long FileSize { get; init; }

    /// <summary>按头部字段推算出的应有大小；推算不出来时为 -1</summary>
    public long ExpectedSize { get; init; } = -1;

    public required IReadOnlyList<BTLIssue> Issues { get; init; }

    public int ErrorCount => Issues.Count(i => i.Level == BTLIssueLevel.Error);
    public int WarningCount => Issues.Count(i => i.Level == BTLIssueLevel.Warning);
    public int InfoCount => Issues.Count(i => i.Level == BTLIssueLevel.Info);

    /// <summary>没有致命问题就算通过</summary>
    public bool IsHealthy => ErrorCount == 0;

    /// <summary>头部信息（解析成功时非 null），用于报告里展示基本信息</summary>
    public BTLHeader? Header { get; init; }

    /// <summary>渲染成可直接打印的多行文本</summary>
    public string ToText()
    {
        var sb = new StringBuilder();

        sb.AppendLine("========== BTL 格式检查 ==========");
        sb.AppendLine($"文件: {FilePath}");
        sb.AppendLine($"大小: {FileSize:N0} 字节");

        if (ExpectedSize >= 0)
        {
            string mark = FileSize switch
            {
                var s when s < ExpectedSize => $"（少于应有大小 {ExpectedSize - s:N0} 字节）",
                var s when s > ExpectedSize => $"（多出 {s - ExpectedSize:N0} 字节）",
                _ => "（一致）"
            };
            sb.AppendLine($"按头部推算应有: {ExpectedSize:N0} 字节 {mark}");
        }

        if (Header != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- 头部 ---");
            sb.AppendLine($"  版本={Header.BtlVersion}  地图号={Header.MapNumber}  " +
                          $"尺寸={Header.MapLength}x{Header.MapWidth}  " +
                          $"裁剪=({Header.MapClipX},{Header.MapClipY})");
            sb.AppendLine($"  军团={Header.ArmyCount}  建筑={Header.BuildingCount}  单位={Header.TroopCount}  " +
                          $"陷阱={Header.TrapCount}  事件={Header.EventCount}  天气={Header.WeatherCount}  " +
                          $"援军={Header.ReinforcementCount}  策略={Header.StrategyCount}  空援={Header.AirSupportCount}");
        }

        sb.AppendLine();
        if (Issues.Count == 0)
        {
            sb.AppendLine("未发现问题。");
        }
        else
        {
            sb.AppendLine($"--- 发现问题 {Issues.Count} 项" +
                          $"（致命 {ErrorCount} / 异常 {WarningCount} / 提示 {InfoCount}）---");

            foreach (var issue in Issues)
            {
                string level = issue.Level switch
                {
                    BTLIssueLevel.Error => "致命",
                    BTLIssueLevel.Warning => "异常",
                    _ => "提示"
                };

                string offset = issue.Offset >= 0 ? $" @0x{issue.Offset:X}" : "";
                sb.AppendLine($"  [{level}] [{issue.Category}] {issue.Message}{offset}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(IsHealthy ? "结论：结构完整，可以正常使用。" : "结论：存在致命问题，建议修复后再用。");

        return sb.ToString();
    }
}

/// <summary>
/// BTL 文件格式检查。
/// <para>
/// <b>为什么需要它</b>：<see cref="BTLParser"/> 解析时对每个区块都写成
/// <c>if (offset + size &lt;= data.Length)</c> —— 文件被截断或头部字段被改坏时，
/// 它不会报错，只是<b>静默跳过</b>读不到的部分。结果是"打开成功但数据少了一截"，
/// 很难从界面上看出来。这个检查类专门把这类问题显式暴露出来。
/// </para>
/// <para>
/// 检查分三类：文件结构（大小是否够、头部推算是否自洽）、头部字段（取值是否合理）、
/// 数据引用（坐标是否越界、外键是否指向存在的对象）。
/// </para>
/// </summary>
public static class BTLFormatChecker
{
    /// <summary>地图单边长度的合理上限（超过基本可以断定头部被写坏了）</summary>
    private const int MaxMapSide = 8192;

    /// <summary>各类数量字段的合理上限，用于识别明显异常的头部</summary>
    private const int MaxCountField = 1_000_000;

    /// <summary>检查一个 BTL 文件</summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="validTerrainIds">
    /// 合法地形组 ID 集合（对应 Java 版 ResConfig.Config.TERRAINIDS）。
    /// 传 null 时自动从 <see cref="ConfigManager"/> 取 —— 优先 def_mapterrain.xml（权威定义，
    /// 经 AssetManager 读取），取不到再退回 manager.json；两者都不可用就跳过地形 ID 检查，
    /// 避免把"没有配置"误报成"文件有问题"。
    /// </param>
    public static BTLCheckReport CheckFile(
        string filePath,
        IReadOnlyCollection<int>? validTerrainIds = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Fail("（空路径）", 0, "路径为空");

        if (!File.Exists(filePath))
            return Fail(filePath, 0, "文件不存在");

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            return Fail(filePath, 0, $"读取失败: {ex.Message}");
        }

        return CheckBytes(data, filePath, validTerrainIds);
    }

    /// <summary>
    /// 取合法地形组 ID 集合（对应 Java 版 ResConfig.Config.TERRAINIDS）。
    /// <para>
    /// 优先取 <c>assets/config/def_mapterrain.xml</c> —— 那是游戏对地形的<b>权威定义</b>，
    /// 由 AssetManager 定位文件、DefMapTerrainParser 解析语义。
    /// 取不到时回退到 <c>Texture/MapTerrian/manager.json</c> 的 terrain_types
    /// （两者内容通常一致，但前者才是权威来源）。
    /// </para>
    /// <para>
    /// 两个来源都不可用时返回空集合，调用方据此<b>跳过</b>地形检查 ——
    /// 绝不能把「取不到配置」当成「所有 ID 都非法」，否则会把正常文件全判为坏文件。
    /// </para>
    /// </summary>
    public static HashSet<int> GetValidTerrainIds()
    {
        try
        {
            // 权威来源：def_mapterrain.xml（经 AssetManager 获取 + DefMapTerrainParser 解析）
            var fromXml = ConfigManager.Instance.GetTerrainGroupIds();
            if (fromXml.Count > 0) return fromXml;

            // 回退：manager.json 的 terrain_types
            ConfigManager.Instance.Initialize();
            return ConfigManager.Instance.GetTerrainTypes().Keys.ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    // 地形三层在 16 字节格子里的字节偏移。
    // 组在该层第 0 字节，变体（Id）紧跟其后。
    private static readonly int[] GroupOffsets = [0, 4, 8];
    private static readonly int[] IdOffsets = [1, 5, 9];

    /// <summary>
    /// 纠错：就地修复非法的地形组 ID（对齐 Java 版 checkMapTerrainIds 的修复动作）。
    /// <para>
    /// 判据与 Java 版一致：组 ID 既<b>不在合法表内</b>、又<b>不是哨兵值 0 / 63</b> 即为非法。
    /// 修复方式也照搬：<b>组重置为 0，同层的变体重置为 255</b>（相当于清空该层装饰）。
    /// </para>
    /// <para>
    /// 注意这是<b>原地修改</b>传入的字节数组，调用方负责写回文件。
    /// </para>
    /// </summary>
    /// <param name="data">BTL 字节数据（会被修改）</param>
    /// <param name="validTerrainIds">合法组 ID 表；传 null 自动从配置读取，读不到则不修（返回 -1）</param>
    /// <returns>修复的层数；合法表不可用时返回 -1（未做任何修改）</returns>
    public static int FixTerrainGroups(byte[] data, IReadOnlyCollection<int>? validTerrainIds = null)
    {
        if (data == null || data.Length < StageOffsets.Calculate(0, 0, 0).terrain) return 0;

        if (validTerrainIds == null)
            validTerrainIds = GetValidTerrainIds();

        // 没有合法表就无法判断什么是"非法"，宁可不动也不要乱改
        if (validTerrainIds.Count == 0) return -1;

        var header = BTLHeader.Parse(data);
        int tileCount = header.SelectableTileCount;

        // 无地形数据的文件（如部分征服图）自然跳过
        if (tileCount <= 0) return 0;

        // 地形区起点必须用 StageOffsets 算：文件头 0x80，之后先是军团区（每个 300 字节），
        // 地形区排在军团区之后。绝不是「文件头紧接着地形」。
        int terrainStart = StageOffsets
            .Calculate(header.ArmyCount, tileCount, header.BuildingCount).terrain;

        // 地形区必须完整落在文件内，否则这个文件不含（完整的）地形数据。
        // 征服地图 conquest*.btl 就是这种情况：头部里的 SelectableTileCount 是另一套
        // 结构的遗留数字，按它去读会把军团/建筑数据当成地形乱改。
        // 宁可什么都不做，也不能改坏无关数据。
        long terrainEnd = (long)terrainStart + (long)tileCount * BTLSize.TERRAIN_SIZE;
        if (terrainEnd > data.Length) return 0;

        int fixedLayers = 0;

        for (int i = 0; i < tileCount; i++)
        {
            int baseOffset = terrainStart + i * BTLSize.TERRAIN_SIZE;

            for (int layer = 0; layer < GroupOffsets.Length; layer++)
            {
                int group = data[baseOffset + GroupOffsets[layer]];
                if (group == 0 || group == 63 || validTerrainIds.Contains(group)) continue;

                data[baseOffset + GroupOffsets[layer]] = 0;       // 组 → 0
                data[baseOffset + IdOffsets[layer]] = 255;        // 变体 → 255
                fixedLayers++;
            }
        }

        return fixedLayers;
    }

    /// <summary>检查一段 BTL 字节数据</summary>
    /// <param name="data">BTL 字节</param>
    /// <param name="filePath">用于报告的来源名</param>
    /// <param name="validTerrainIds">
    /// 合法地形组 ID 集合；传 null 时自动从配置读取，读不到则跳过该项检查。
    /// </param>
    public static BTLCheckReport CheckBytes(
        byte[] data,
        string filePath = "(内存数据)",
        IReadOnlyCollection<int>? validTerrainIds = null)
    {
        var issues = new List<BTLIssue>();
        long fileSize = data?.Length ?? 0;

        // ---------- 1. 文件结构 ----------
        if (data == null || data.Length == 0)
            return Fail(filePath, 0, "数据为空");

        if (data.Length < BTLSize.HEADER_SIZE)
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Error, "文件结构",
                $"文件只有 {data.Length} 字节，连 {BTLSize.HEADER_SIZE} 字节的文件头都不完整"));

            return new BTLCheckReport
            {
                FilePath = filePath,
                FileSize = fileSize,
                Issues = issues
            };
        }

        // ---------- 2. 头部字段 ----------
        var header = BTLHeader.Parse(data);

        // 地形 / 省区数据的格数是 SelectableTileCount（可编辑格数），
        // 不是 MapLength * MapWidth（地图标称面积）—— 两者不相等。
        // 关键：征服地图（conquest*.btl）没有地形数据，SelectableTileCount 为 0，
        // 若按标称面积去读，会一路读进军团/建筑区，把正常数据误判成损坏。
        // 解析器的区域起点也是用 SelectableTileCount 算的，这里必须与它一致。
        long totalTilesLong = header.SelectableTileCount;

        // 标称面积只用于头部自洽性校验
        long mapTilesLong = (long)header.MapLength * header.MapWidth;

        if (header.BtlVersion <= 0 || header.BtlVersion > 99)
            issues.Add(new BTLIssue(BTLIssueLevel.Warning, "头部",
                $"版本号 {header.BtlVersion} 不在常见范围（1~3）"));

        if (header.MapLength <= 0 || header.MapWidth <= 0)
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Error, "头部",
                $"地图尺寸非法：{header.MapLength} x {header.MapWidth}，两者都必须为正"));
        }
        else
        {
            if (header.MapLength > MaxMapSide || header.MapWidth > MaxMapSide)
                issues.Add(new BTLIssue(BTLIssueLevel.Warning, "头部",
                    $"地图尺寸 {header.MapLength}x{header.MapWidth} 超出常见范围（单边 > {MaxMapSide}）"));

            if (mapTilesLong > int.MaxValue)
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "头部",
                    $"地图总格数 {mapTilesLong:N0} 超出 32 位整数范围，偏移计算会溢出"));
        }

        if (header.MapClipX < 0 || header.MapClipY < 0 ||
            header.MapClipX > header.MapLength || header.MapClipY > header.MapWidth)
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Warning, "头部",
                $"裁剪偏移 ({header.MapClipX},{header.MapClipY}) 超出地图范围 {header.MapLength}x{header.MapWidth}"));
        }

        if (header.MinTurns > header.MaxTurns && header.MaxTurns > 0)
            issues.Add(new BTLIssue(BTLIssueLevel.Warning, "头部",
                $"最小回合数 {header.MinTurns} 大于最大回合数 {header.MaxTurns}"));

        // 数量字段：负数或大得离谱都说明头部被写坏了
        CheckCount(header.ArmyCount, "军团数");
        CheckCount(header.BuildingCount, "建筑数");
        CheckCount(header.TroopCount, "单位数");
        CheckCount(header.TrapCount, "陷阱数");
        CheckCount(header.EventCount, "事件数");
        CheckCount(header.WeatherCount, "天气数");
        CheckCount(header.ReinforcementCount, "援军数");
        CheckCount(header.StrategyCount, "策略数");
        CheckCount(header.AirSupportCount, "空中支援数");

        void CheckCount(int value, string label)
        {
            if (value < 0)
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "头部", $"{label}为负：{value}"));
            else if (value > MaxCountField)
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "头部",
                    $"{label}大得异常：{value:N0}（上限 {MaxCountField:N0}），头部可能已损坏"));
        }

        // ---------- 2.5 判定文件布局 ----------
        // 两种文件的唯一结构差别是「有无地形区」：
        //   关卡 .btl ：文件头(0x80) → 军团(300/个) → 地形(16/格) → 省区(2/格)
        //               → 归属(1/格) → 建筑(32/个) → 单位/陷阱/事件/天气…
        //   征服 .bin ：文件头(0x80) → 军团(300/个) → 省区(2/格) → 归属(1/格)
        //               → 建筑(32/个) → 单位/陷阱/事件/天气…        ← 没有地形区
        // 注意军团排在【最前】，紧跟在 0x80 的文件头之后，不是排在最后。
        var stageOffsets = StageOffsets.Calculate(
            header.ArmyCount, header.SelectableTileCount, header.BuildingCount);
        var conquestOffsets = ConquestOffsets.Calculate(
            header.ArmyCount, header.SelectableTileCount, header.BuildingCount);

        // 按关卡布局算出的地形区若装不进文件，就说明这是征服文件
        long stageTerrainEnd = (long)stageOffsets.terrain
                             + (long)header.SelectableTileCount * BTLSize.TERRAIN_SIZE;
        bool hasTerrain = !(header.SelectableTileCount > 0 && stageTerrainEnd > fileSize);

        // 后续所有区块起点都从这里取，不再各自硬算
        int provinceStart = hasTerrain ? stageOffsets.province : conquestOffsets.province;
        int buildingStart = hasTerrain ? stageOffsets.building : conquestOffsets.building;
        int dataEnd       = hasTerrain ? stageOffsets.dataEnd  : conquestOffsets.dataEnd;

        if (!hasTerrain && header.SelectableTileCount > 0)
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Info, "文件结构",
                "按征服布局解析（该文件不含地形区）"));
        }

        // ---------- 3. 按头部推算文件应有大小 ----------
        long expectedSize = -1;

        if (totalTilesLong > 0 && totalTilesLong <= int.MaxValue &&
            header.BuildingCount >= 0 && header.TroopCount >= 0 && header.ArmyCount >= 0)
        {
            // dataEnd 是建筑区结束的位置，即本节能推算出的最小应有大小；
            // 其后的单位/陷阱/事件等未计入，所以只判「截断」不判「多出」。
            expectedSize = dataEnd;

            if (fileSize < expectedSize)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "文件结构",
                    $"文件被截断：实际 {fileSize:N0} 字节，按头部推算至少应有 {expectedSize:N0} 字节，" +
                    $"缺少 {expectedSize - fileSize:N0} 字节。" +
                    "解析器会静默跳过读不到的部分，表现为「打开正常但数据缺一截」"));
            }
        }
        else
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Warning, "文件结构",
                "头部字段异常，无法推算文件应有大小"));
        }

        // ---------- 4. 地形 / 省区数据是否完整 ----------
        if (totalTilesLong > 0 && totalTilesLong <= int.MaxValue)
        {
            // 地形区只有关卡文件才有，征服文件直接跳过
            if (hasTerrain && stageTerrainEnd > fileSize)
            {
                long readable = Math.Max(0, fileSize - stageOffsets.terrain) / BTLSize.TERRAIN_SIZE;
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "地形",
                    $"地形区不完整：应有 {totalTilesLong:N0} 格，实际只能读到 {readable:N0} 格"));
            }

            long provinceEnd = (long)provinceStart + totalTilesLong * BTLSize.PROVINCE_SIZE;
            if (provinceEnd > fileSize)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "省区",
                    "省区数据不完整，读不到全部格子的省区值"));
            }
        }

        // ---------- 4.5 地形组 ID 合法性（对齐 Java 版 checkMapTerrainIds / E 键纠错）----------
        //
        // 每个格子的三层各有自己的"组 ID"（C# 侧是 TileType1/2/3，Java 侧是 bmTerrain1Group /
        // bmDoodad1Group / bmDoodad2Group），合法值来自配置的 terrain_types。
        // 出现表外的组 ID，说明数据被外部工具写坏、或与当前资源版本不匹配 ——
        // 游戏加载时那格会渲染成空白或花屏。
        //
        // 0 和 63 是哨兵值（表示"该层无装饰"），它们通常并不在 terrain_types 里，
        // 必须单独放行 —— Java 版的判据正是 !contains(g) && g != 0 && g != 63。
        if (validTerrainIds == null)
            validTerrainIds = GetValidTerrainIds();

        if (validTerrainIds.Count == 0)
        {
            issues.Add(new BTLIssue(BTLIssueLevel.Info, "地形",
                "取不到合法地形 ID 表（配置未加载？），已跳过地形组 ID 检查"));
        }
        else if (totalTilesLong > 0 && totalTilesLong <= int.MaxValue)
        {
            bool IsValidGroup(int group)
                => group == 0 || group == 63 || validTerrainIds.Contains(group);

            // 征服文件没有地形区，布局判定已经识别出来，直接跳过
            if (!hasTerrain)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Info, "地形",
                    "该文件不含地形数据，已跳过地形组 ID 检查"));
            }
            else
            {
                long terrainStart = stageOffsets.terrain;
                int badLayer1 = 0, badLayer2 = 0, badLayer3 = 0;
                long firstBadOffset = -1;
                var samples = new List<string>(8);
                int mapWidth = header.MapLength > 0 ? header.MapLength : 1;

                for (long i = 0; i < totalTilesLong; i++)
                {
                    long offset = terrainStart + i * BTLSize.TERRAIN_SIZE;
                    var terrain = Terrain.FromBytes(data, (int)offset);

                    bool bad1 = !IsValidGroup(terrain.TileType1);
                    bool bad2 = !IsValidGroup(terrain.TileType2);
                    bool bad3 = !IsValidGroup(terrain.TileType3);

                    if (bad1) badLayer1++;
                    if (bad2) badLayer2++;
                    if (bad3) badLayer3++;

                    if (!bad1 && !bad2 && !bad3) continue;

                    if (firstBadOffset < 0) firstBadOffset = offset;

                    // 只留少量样例：坏格子可能有几万个，全列出来会把报告淹掉
                    if (samples.Count < 8)
                    {
                        int col = (int)(i % mapWidth);
                        int row = (int)(i / mapWidth);
                        samples.Add($"({col},{row}) 第1层={terrain.TileType1} 第2层={terrain.TileType2} 第3层={terrain.TileType3}");
                    }
                }

                int badTotal = badLayer1 + badLayer2 + badLayer3;
                if (badTotal > 0)
                {
                    issues.Add(new BTLIssue(BTLIssueLevel.Error, "地形",
                        $"发现 {badTotal} 处非法地形组 ID（第1层 {badLayer1} / 第2层 {badLayer2} / 第3层 {badLayer3}）。" +
                        $"合法 ID 共 {validTerrainIds.Count} 个（另放行哨兵值 0 与 63）。" +
                        $"样例：{string.Join("；", samples)}",
                        firstBadOffset));
                }
            }
        }

        // ---------- 5. 引用完整性：军团 ----------
        // CountryId 重复会让 FindLegionIndex 之类的按国家查找产生歧义
        if (header.ArmyCount > 0 && header.ArmyCount <= MaxCountField)
        {
            // 军团紧跟在 0x80 的文件头之后，排在所有区块的最前面。
            // 之前误当成「排在最后」（还把地形/建筑尺寸一起加进去），读的其实是别处的数据，
            // 于是国家ID 看似大量重复 —— 那是假报。
            long legionStart = BTLSize.HEADER_SIZE;

            int readableLegions = 0;
            var seenCountryIds = new Dictionary<int, int>();

            for (int i = 0; i < header.ArmyCount; i++)
            {
                long offset = legionStart + (long)i * BTLSize.LEGION_SIZE;
                if (offset + BTLSize.LEGION_SIZE > fileSize) break;

                readableLegions++;
                var legion = Legion.FromBytes(data, (int)offset);

                if (seenCountryIds.TryGetValue(legion.CountryId, out int firstIndex))
                {
                    issues.Add(new BTLIssue(BTLIssueLevel.Warning, "军团",
                        $"第 {i} 个军团的国家ID {legion.CountryId} 与第 {firstIndex} 个重复，按国家查找会命中前一个",
                        offset));
                }
                else
                {
                    seenCountryIds[legion.CountryId] = i;
                }
            }

            if (readableLegions < header.ArmyCount)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Error, "军团",
                    $"军团区不完整：头部声明 {header.ArmyCount} 个，实际只能读到 {readableLegions} 个"));
            }

            // 归属值是 1 字节，最多 255 个军团能被引用到
            if (header.ArmyCount > 255)
                issues.Add(new BTLIssue(BTLIssueLevel.Warning, "军团",
                    $"军团数 {header.ArmyCount} 超过 255，超出部分无法被 1 字节的归属值引用"));
        }

        // ---------- 6. 建筑坐标越界 ----------
        // 坐标基准是「完整地图」，不是可编辑格数：
        // MapData 里按 MapWidth 取模换算行列，所以合法范围是 MapLength × MapWidth。
        // 征服地图这两者不等（可编辑格数只是其中一部分），用错基准会误报。
        if (header.BuildingCount > 0 && header.BuildingCount <= MaxCountField &&
            mapTilesLong > 0 && mapTilesLong <= int.MaxValue)
        {
            int totalTiles = (int)mapTilesLong;
            // buildingStart 已按实际布局（关卡 / 征服）算好，见上方 2.5 节

            int outOfRange = 0;
            long firstBadOffset = -1;

            for (int i = 0; i < header.BuildingCount; i++)
            {
                long offset = buildingStart + (long)i * BTLSize.BUILDING_SIZE;
                if (offset + BTLSize.BUILDING_SIZE > fileSize) break;

                var building = Building.FromBytes(data, (int)offset);
                if (building.Coordinate < 0 || building.Coordinate >= totalTiles)
                {
                    outOfRange++;
                    if (firstBadOffset < 0) firstBadOffset = offset;
                }
            }

            if (outOfRange > 0)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Warning, "建筑",
                    $"{outOfRange} 个建筑的坐标超出地图范围（0 ~ {totalTiles - 1}），" +
                    "渲染与坐标索引会忽略它们", firstBadOffset));
            }
        }

        // ---------- 7. 单位坐标越界 ----------
        // 同建筑：坐标基准是完整地图。
        // 但征服文件跳过：它的单位区布局与关卡不同 ——
        // 编辑器同款的 ConquestParser 对 conquest*.btl 解析出 0 个单位，
        // 而头部声称有数百个，说明那些计数是沿用关卡结构留下的遗留值。
        // 布局未确认前不报，避免假错。
        if (hasTerrain && header.TroopCount > 0 && header.TroopCount <= MaxCountField &&
            mapTilesLong > 0 && mapTilesLong <= int.MaxValue)
        {
            int totalTiles = (int)mapTilesLong;
            // 单位区紧跟在建筑区之后，起点就是 dataEnd
            long armyStart = dataEnd;

            // 单位条目大小随版本变化（v1 / v3 不同），不能固定用 BTLSize.ARMY_SIZE
            int armySize = BTLArmyModule.GetArmySize(header.BtlVersion);

            int outOfRange = 0;
            long firstBadOffset = -1;

            for (int i = 0; i < header.TroopCount; i++)
            {
                long offset = armyStart + (long)i * armySize;
                if (offset + armySize > fileSize) break;

                var army = Army.FromBytes(data, (int)offset);
                if (army.Coordinate < 0 || army.Coordinate >= totalTiles)
                {
                    outOfRange++;
                    if (firstBadOffset < 0) firstBadOffset = offset;
                }
            }

            if (outOfRange > 0)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Warning, "单位",
                    $"{outOfRange} 个单位的坐标超出地图范围（0 ~ {totalTiles - 1}）", firstBadOffset));
            }
        }

        // 按严重程度排序，让致命的排在前面
        issues.Sort((a, b) => a.Level.CompareTo(b.Level));

        return new BTLCheckReport
        {
            FilePath = filePath,
            FileSize = fileSize,
            ExpectedSize = expectedSize,
            Issues = issues,
            Header = header
        };
    }

    private static BTLCheckReport Fail(string filePath, long fileSize, string message)
    {
        return new BTLCheckReport
        {
            FilePath = filePath,
            FileSize = fileSize,
            Issues = new[] { new BTLIssue(BTLIssueLevel.Error, "文件", message) }
        };
    }
}
