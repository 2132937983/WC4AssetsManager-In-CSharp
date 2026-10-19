using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Analyzers;

// BtlInspector：布局识别（数据块地址表）。
public sealed partial class BtlInspector
{
    private const int WorldHeaderSize = 16;

    /// <summary>单位记录大小：v1 = 48，v3（含 v2）= 64。</summary>
    public static int ArmyRecordSize(BtlRecordFamily family) => BTLArmyModule.GetArmySize(family == BtlRecordFamily.V3);

    /// <summary>援军记录大小：v1 = 80，v3（含 v2）= 104。</summary>
    public static int ReinforcementRecordSize(BtlRecordFamily family) => BTLReinforcementModule.GetReinforcementSize(family == BtlRecordFamily.V3);

    /// <summary>
    /// 版本号对应的记录族：v2、v3 都是 64 / 104 字节记录（与解析器同一规则，见 <see cref="BTLHeader.UsesV3RecordFamily"/>）；
    /// 检查器仍以文件大小为准，不一致时会提示。
    /// </summary>
    public static BtlRecordFamily FamilyForVersion(int version)
        => BTLHeader.UsesV3RecordFamily(version) ? BtlRecordFamily.V3 : BtlRecordFamily.V1;

    /// <summary>一种候选布局：文件种类 × 记录族 × 地形格数来源 × 是否带尾部扩展块。</summary>
    private readonly record struct LayoutCandidate(
        InspectedFileKind Kind,
        BtlRecordFamily Family,
        int TerrainCount,
        bool TerrainFromMapArea,
        bool WithExtension,
        int Priority);

    /// <summary>
    /// 识别 BTL 布局。
    /// <para>
    /// 依据（对 assets/stage 下全部 1358 个关卡逐一核对过，全部精确吻合）：
    /// ① 战役布局 = 头部 → 军团 → 地形 → 省份 → 归属 → 建筑 → 单位 …；征服布局没有地形块；
    /// ② 地形块的格数是 MapLength × MapWidth，省份 / 归属块是 SelectableTileCount（后者常向上补齐到 8 的倍数）；
    /// ③ v2 / v3 的单位 / 援军记录是 64 / 104 字节，v1 是 48 / 80 字节；
    /// ④ 头部 0x54（Unknown4）在 v3 文件里是尾部扩展数据块的字节数（内容为 protobuf 编码）。
    /// </para>
    /// <para>
    /// 做法：枚举候选布局，推算各自的总大小，与文件大小精确相等者胜出（同等时按优先级）；
    /// 都不相等时取"推算大小不超过文件、且最接近"的一个，并给出未解析 / 截断提示。
    /// </para>
    /// </summary>
    private static BtlLayout DetectBtlLayout(BTLHeader header, long fileLength, InspectLayoutOverride layoutOverride,
                                             List<BtlInspectIssue> issues)
    {
        ReportHeaderSanity(header, issues);

        LayoutCandidate? best = null;
        List<BtlSegment>? bestSegments = null;
        long bestEnd = 0;
        int bestRank = int.MaxValue;

        foreach (var candidate in EnumerateCandidates(header, layoutOverride))
        {
            var segments = BuildBtlSegments(header, candidate, fileLength, out long end);

            // 排名：精确吻合 < 不超过文件（差距越小越好）< 超过文件（超得越少越好）
            int rank = end == fileLength ? 0 : end < fileLength ? 1 : 2;
            bool better = best == null
                          || rank < bestRank
                          || (rank == bestRank && rank == 1 && end > bestEnd)
                          || (rank == bestRank && rank == 2 && end < bestEnd)
                          || (rank == bestRank && end == bestEnd && candidate.Priority < best.Value.Priority);
            if (!better) continue;

            best = candidate;
            bestSegments = segments;
            bestEnd = end;
            bestRank = rank;
        }

        var chosen = best!.Value;
        var layout = new BtlLayout
        {
            Kind = chosen.Kind,
            Family = chosen.Family,
            TerrainCount = chosen.Kind == InspectedFileKind.Stage ? chosen.TerrainCount : 0,
            TerrainFromMapArea = chosen.TerrainFromMapArea,
            HasExtension = chosen.WithExtension,
            ExpectedLength = bestEnd,
            FileLength = fileLength,
            Segments = bestSegments!
        };

        ReportLayout(header, layout, layoutOverride, issues);
        return layout;
    }

    private static IEnumerable<LayoutCandidate> EnumerateCandidates(BTLHeader header, InspectLayoutOverride layoutOverride)
    {
        var preferred = FamilyForVersion(header.BtlVersion);
        var families = new[] { preferred, preferred == BtlRecordFamily.V1 ? BtlRecordFamily.V3 : BtlRecordFamily.V1 };

        int mapArea = ClampCount((long)header.MapLength * header.MapWidth);
        int selectable = ClampCount(header.SelectableTileCount);
        bool canHaveExtension = header.Unknown4 > 0;

        for (int f = 0; f < families.Length; f++)
        {
            for (int ext = 0; ext < (canHaveExtension ? 2 : 1); ext++)
            {
                bool withExtension = ext == 1;
                int basePriority = f * 100 + ext * 10;

                if (layoutOverride != InspectLayoutOverride.Conquest)
                {
                    yield return new LayoutCandidate(InspectedFileKind.Stage, families[f], mapArea, true, withExtension, basePriority);
                    if (selectable != mapArea)
                        yield return new LayoutCandidate(InspectedFileKind.Stage, families[f], selectable, false, withExtension, basePriority + 1);
                }

                if (layoutOverride != InspectLayoutOverride.Stage)
                    yield return new LayoutCandidate(InspectedFileKind.Conquest, families[f], 0, false, withExtension, basePriority + 2);
            }
        }
    }

    /// <summary>按候选布局排出全部数据块；<paramref name="end"/> = 已知数据块的结束偏移。</summary>
    private static List<BtlSegment> BuildBtlSegments(BTLHeader header, LayoutCandidate candidate, long fileLength, out long end)
    {
        var layout = BTLLayout.Create(header, candidate.Family == BtlRecordFamily.V3,
                                      candidate.Kind == InspectedFileKind.Stage ? candidate.TerrainCount : null,
                                      candidate.WithExtension ? ClampCount(header.Unknown4) : 0);

        var segments = new List<BtlSegment>(layout.Spans.Count + 1);
        foreach (var span in layout.Spans)
            segments.Add(new BtlSegment(span.Block, span.Start, span.RecordSize, span.Count, fileLength));

        end = layout.End;
        AddUnparsedTail(segments, end, fileLength);
        return segments;
    }

    /// <summary>已知数据块之后若还有字节，把它们收成一个"未解析数据"块。</summary>
    private static void AddUnparsedTail(List<BtlSegment> segments, long end, long fileLength)
    {
        if (end < fileLength)
            segments.Add(new BtlSegment(BtlBlock.Unparsed, end, ClampCount(fileLength - end), 1, fileLength));
    }

    private static BtlLayout BuildWorldLayout(WorldFileInfo world, long fileLength, List<BtlInspectIssue> issues)
    {
        long tilesLong = world.TileCount;
        int tiles = ClampCount(tilesLong);
        if (tiles != tilesLong || world.Width <= 0 || world.Height <= 0)
            issues.Add(new(BtlInspectIssueLevel.Error, Loc.F("DataCheck.Issue.WorldSize", world.Width, world.Height)));

        var segments = new List<BtlSegment>(4)
        {
            new(BtlBlock.Header, 0, WorldHeaderSize, 1, fileLength),
            new(BtlBlock.Terrain, WorldHeaderSize, BTLSize.TERRAIN_SIZE, tiles, fileLength),
            new(BtlBlock.Province, WorldHeaderSize + (long)tiles * BTLSize.TERRAIN_SIZE, BTLSize.PROVINCE_SIZE, tiles, fileLength)
        };
        long end = WorldHeaderSize + (long)tiles * (BTLSize.TERRAIN_SIZE + BTLSize.PROVINCE_SIZE);
        AddUnparsedTail(segments, end, fileLength);

        var layout = new BtlLayout
        {
            Kind = InspectedFileKind.World,
            Family = BtlRecordFamily.V1,
            TerrainCount = tiles,
            TerrainFromMapArea = true,
            HasExtension = false,
            ExpectedLength = end,
            FileLength = fileLength,
            Segments = segments
        };

        issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.WorldDetected", world.Version, world.Width, world.Height)));
        ReportSizeMatch(layout, issues);
        return layout;
    }

    private static BtlLayout BuildUnknownLayout(long fileLength)
    {
        var segments = new List<BtlSegment>(1);
        AddUnparsedTail(segments, 0, fileLength);
        return new BtlLayout
        {
            Kind = InspectedFileKind.Unknown,
            Family = BtlRecordFamily.V1,
            TerrainCount = 0,
            TerrainFromMapArea = false,
            HasExtension = false,
            ExpectedLength = 0,
            FileLength = fileLength,
            Segments = segments
        };
    }

    private static int ClampCount(long value) => BTLLayout.ClampCount(value);

    // ============================================================= 提示 =============================================================

    private static void ReportHeaderSanity(BTLHeader header, List<BtlInspectIssue> issues)
    {
        if (header.BtlVersion is < 1 or > 3)
            issues.Add(new(BtlInspectIssueLevel.Warning, Loc.F("DataCheck.Issue.VersionRange", header.BtlVersion)));

        if (header.MapLength <= 0 || header.MapWidth <= 0)
            issues.Add(new(BtlInspectIssueLevel.Warning, Loc.F("DataCheck.Issue.MapSize", header.MapLength, header.MapWidth)));

        void Count(int value, string fieldKey)
        {
            if (value < 0)
                issues.Add(new(BtlInspectIssueLevel.Warning, Loc.F("DataCheck.Issue.NegativeCount", Loc.T(fieldKey), value)));
        }

        Count(header.ArmyCount, "DataCheck.Field.LegionCount");
        Count(header.SelectableTileCount, "DataCheck.Field.SelectableTileCount");
        Count(header.BuildingCount, "DataCheck.Field.BuildingCount");
        Count(header.TroopCount, "DataCheck.Field.TroopCount");
        Count(header.TrapCount, "DataCheck.Field.TrapCount");
        Count(header.PlanCount, "DataCheck.Field.PlanCount");
        Count(header.WeatherCount, "DataCheck.Field.WeatherCount");
        Count(header.EventCount, "DataCheck.Field.EventCount");
        Count(header.ReinforcementCount, "DataCheck.Field.ReinforcementCount");
        Count(header.AirRaidCount, "DataCheck.Field.AirRaidCount");
        Count(header.PlacementA, "DataCheck.Field.PlacementA");
        Count(header.PlacementB, "DataCheck.Field.PlacementB");
        Count(header.ConqueredFlagPosition, "DataCheck.Field.CapitalCount");
        Count(header.StrategyCount, "DataCheck.Field.StrategyCount");
        Count(header.AirSupportCount, "DataCheck.Field.AirSupportCount");
    }

    private static void ReportLayout(BTLHeader header, BtlLayout layout, InspectLayoutOverride layoutOverride, List<BtlInspectIssue> issues)
    {
        string kind = KindDisplayName(layout.Kind);
        issues.Add(new(BtlInspectIssueLevel.Info, layoutOverride == InspectLayoutOverride.Auto
            ? Loc.F("DataCheck.Issue.LayoutAuto", kind, FamilyDisplayName(layout.Family))
            : Loc.F("DataCheck.Issue.LayoutForced", kind, FamilyDisplayName(layout.Family))));

        if (layout.Family != FamilyForVersion(header.BtlVersion))
            issues.Add(new(BtlInspectIssueLevel.Warning,
                Loc.F("DataCheck.Issue.FamilyMismatch", header.BtlVersion, FamilyDisplayName(layout.Family))));

        int selectable = ClampCount(header.SelectableTileCount);
        if (layout.Kind == InspectedFileKind.Stage && layout.TerrainCount != selectable)
            issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.TerrainArea", layout.TerrainCount, selectable)));
        else if (layout.Kind == InspectedFileKind.Stage && !layout.TerrainFromMapArea)
            issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.TerrainSelectable", selectable)));

        if (layout.HasExtension)
            issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.Extension", header.Unknown4)));

        if (header.Unknown4 > 0 && !layout.HasExtension)
            issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.Unknown4", header.Unknown4)));

        ReportSizeMatch(layout, issues);
    }

    private static void ReportSizeMatch(BtlLayout layout, List<BtlInspectIssue> issues)
    {
        if (layout.IsExact)
            issues.Add(new(BtlInspectIssueLevel.Info, Loc.F("DataCheck.Issue.Exact", layout.FileLength)));
        else if (layout.MissingBytes > 0)
            issues.Add(new(BtlInspectIssueLevel.Error, Loc.F("DataCheck.Issue.Truncated", layout.ExpectedLength, layout.MissingBytes)));
        else
            issues.Add(new(BtlInspectIssueLevel.Warning, Loc.F("DataCheck.Issue.Unparsed", layout.UnparsedBytes, layout.ExpectedLength)));
    }

    /// <summary>记录族的显示名（"v1 记录 (48/80 字节)" 之类）。</summary>
    public static string FamilyDisplayName(BtlRecordFamily family) => Loc.T("DataCheck.Family." + family);

    /// <summary>布局与文件大小的吻合情况（一句话）。</summary>
    public string LayoutStatusText
    {
        get
        {
            if (Kind == InspectedFileKind.Unknown) return Loc.T("DataCheck.Layout.Unknown");
            if (Layout.IsExact) return Loc.T("DataCheck.Layout.Exact");
            return Layout.MissingBytes > 0
                ? Loc.F("DataCheck.Layout.Truncated", Layout.MissingBytes)
                : Loc.F("DataCheck.Layout.Unparsed", Layout.UnparsedBytes);
        }
    }
}
