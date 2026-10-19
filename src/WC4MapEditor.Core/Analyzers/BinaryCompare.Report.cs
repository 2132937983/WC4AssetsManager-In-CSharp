using System.Globalization;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Analyzers;

// BinaryCompare：文本报告（数据检查场景"保存报告"与 compare 命令共用）。
public static partial class BinaryCompare
{
    public const int DefaultReportRanges = 1_000;
    public const int DefaultReportRecords = 300;

    /// <summary>
    /// 生成多行文本报告：两文件概况、结论、数据块对比表、记录差异（逐字段）、字节差异区间（两端定位 + 字节）。
    /// 区间 / 记录过多时只列前若干条，并注明总数（VB 版每个不同字节一行，尺寸一变报告就爆掉）。
    /// </summary>
    public static string BuildReport(BinaryCompareResult result, int maxRanges = DefaultReportRanges,
                                     int maxRecordsPerBlock = DefaultReportRecords)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("DataCheck.Compare.Report.Title"));
        sb.AppendLine(new string('=', 40));
        AppendFile(sb, "A", result.A);
        AppendFile(sb, "B", result.B);
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        sb.AppendLine();
        sb.AppendLine(SummaryText(result));

        if (result.Blocks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("DataCheck.Compare.Report.BlocksHeader"));
            foreach (var block in result.Blocks)
            {
                sb.AppendLine(Loc.F("DataCheck.Compare.Report.BlockLine", BtlInspector.BlockDisplayName(block.Block),
                                    block.CountA, block.CountB, block.ChangedRecords, block.OnlyInA, block.OnlyInB));
                if (block.AlignedByConversion) sb.AppendLine(Loc.T("DataCheck.Compare.Report.Aligned"));
            }

            sb.AppendLine();
            sb.AppendLine(Loc.F("DataCheck.Compare.Report.RecordsHeader", maxRecordsPerBlock));
            foreach (var block in result.Blocks)
                AppendRecords(sb, block, maxRecordsPerBlock);
        }

        sb.AppendLine();
        int shownRanges = Math.Min(maxRanges, result.Ranges.Count);
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.RangesHeader", result.RangeCount, shownRanges));
        for (int i = 0; i < shownRanges; i++)
            AppendRange(sb, result, result.Ranges[i]);
        if (result.RangeCount > shownRanges)
            sb.AppendLine(Loc.F("DataCheck.Compare.Report.More", result.RangeCount - shownRanges));

        sb.AppendLine();
        sb.AppendLine(Loc.T("DataCheck.Compare.Report.End"));
        return sb.ToString();
    }

    /// <summary>
    /// 单个数据块的明细（数据检查场景选中一行时显示）：该块的记录差异，以及起点落在该块（A 或 B 一侧）内的字节差异区间。
    /// </summary>
    public static string BuildBlockDetail(BinaryCompareResult result, BlockDiff block, int maxRanges = DefaultReportRanges)
    {
        var sb = new StringBuilder();
        string name = BtlInspector.BlockDisplayName(block.Block);
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.BlockLine", name, block.CountA, block.CountB,
                            block.ChangedRecords, block.OnlyInA, block.OnlyInB));
        if (block.AlignedByConversion) sb.AppendLine(Loc.T("DataCheck.Compare.Report.Aligned"));

        sb.AppendLine();
        if (block.Records.Count == 0) sb.AppendLine(Loc.T("DataCheck.Compare.NoRecordDiff"));
        else AppendRecords(sb, block, block.Records.Count);

        var ranges = result.Ranges.Where(r => InBlock(result.A, r.Start, block.Block) || InBlock(result.B, r.Start, block.Block)).ToList();
        if (ranges.Count == 0) return sb.ToString();

        sb.AppendLine();
        int shown = Math.Min(maxRanges, ranges.Count);
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.RangesHeader", ranges.Count, shown));
        foreach (var range in ranges.Take(shown)) AppendRange(sb, result, range);
        if (ranges.Count > shown)
            sb.AppendLine(Loc.F("DataCheck.Compare.Report.More", ranges.Count - shown));
        return sb.ToString();
    }

    private static bool InBlock(BtlInspector side, long offset, BtlBlock block)
        => offset < side.Data.LongLength && side.Locate(offset)?.Segment.Block == block;

    private static void AppendRange(StringBuilder sb, BinaryCompareResult result, BinaryDiffRange range)
    {
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.RangeLine", Hex(range.Start), Hex(range.End - 1), range.Length));
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.RangeSide", "A", DescribeSide(result.A, range.Start), RangeBytes(result.A, range)));
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.RangeSide", "B", DescribeSide(result.B, range.Start), RangeBytes(result.B, range)));
    }

    /// <summary>一句话结论。</summary>
    public static string SummaryText(BinaryCompareResult result)
        => result.Identical
            ? Loc.T("DataCheck.Compare.Identical")
            : Loc.F("DataCheck.Compare.Summary", result.DifferentBytes, result.RangeCount,
                    result.Blocks.Sum(b => b.ChangedRecords + b.OnlyInA + b.OnlyInB));

    private static void AppendFile(StringBuilder sb, string label, BtlInspector file)
    {
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.File", label, file.SourceName));
        sb.AppendLine(Loc.F("DataCheck.Compare.Report.FileInfo", BtlInspector.KindDisplayName(file.Kind), file.FileVersion,
                            file.FileLength, file.LayoutStatusText));
    }

    private static void AppendRecords(StringBuilder sb, BlockDiff block, int maxRecords)
    {
        string name = BtlInspector.BlockDisplayName(block.Block);
        int shown = 0;
        foreach (var record in block.Records)
        {
            if (shown++ >= maxRecords) break;
            switch (record.Kind)
            {
                case RecordDiffKind.OnlyInA:
                    sb.AppendLine(Loc.F("DataCheck.Compare.Report.OnlyIn", name, record.Index, "A"));
                    break;
                case RecordDiffKind.OnlyInB:
                    sb.AppendLine(Loc.F("DataCheck.Compare.Report.OnlyIn", name, record.Index, "B"));
                    break;
                default:
                    sb.AppendLine(Loc.F("DataCheck.Compare.Report.Record", name, record.Index));
                    foreach (var field in record.Fields)
                        sb.AppendLine(Loc.F("DataCheck.Compare.Report.Field", field.Field.DisplayName,
                                            "0x" + field.Field.Offset.ToString("X2", CultureInfo.InvariantCulture), field.ValueA, field.ValueB));
                    break;
            }
        }

        int total = block.ChangedRecords + block.OnlyInA + block.OnlyInB;
        int listed = Math.Min(block.Records.Count, maxRecords);
        if (total > listed)
            sb.AppendLine(Loc.F("DataCheck.Compare.Report.MoreRecords", name, total - listed));
    }

    private static string RangeBytes(BtlInspector side, BinaryDiffRange range)
    {
        if (range.Start >= side.Data.LongLength) return "—";
        int length = (int)Math.Min(range.Length, side.Data.LongLength - range.Start);
        return BtlInspector.HexBytes(side.Data.AsSpan((int)range.Start, length), 16);
    }

    private static string Hex(long value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
}
