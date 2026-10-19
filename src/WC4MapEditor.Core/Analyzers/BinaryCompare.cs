using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>一段连续的字节差异（两文件按相同偏移逐字节对齐）。超出较短文件的尾部记为"仅一方有"。</summary>
public sealed record BinaryDiffRange(long Start, long Length, bool OnlyInA, bool OnlyInB)
{
    public long End => Start + Length;
}

public enum RecordDiffKind
{
    Changed,
    OnlyInA,
    OnlyInB
}

/// <summary>记录里一个字段的差异。</summary>
public sealed record FieldDiff(BtlFieldDef Field, string ValueA, string ValueB);

/// <summary>一条记录的差异（按记录序号对齐）。</summary>
public sealed record RecordDiff(BtlBlock Block, int Index, RecordDiffKind Kind, IReadOnlyList<FieldDiff> Fields);

/// <summary>一个数据块的对比结果。</summary>
public sealed class BlockDiff
{
    public required BtlBlock Block { get; init; }
    public int CountA { get; init; }
    public int CountB { get; init; }
    public int RecordSizeA { get; init; }
    public int RecordSizeB { get; init; }

    /// <summary>两边记录族不同（v1 / v3），B 的记录按版本转换映射对齐后才比较。</summary>
    public bool AlignedByConversion { get; init; }

    public int ChangedRecords { get; internal set; }
    public int OnlyInA => Math.Max(0, CountA - CountB);
    public int OnlyInB => Math.Max(0, CountB - CountA);
    public List<RecordDiff> Records { get; } = new();

    /// <summary>差异记录超过上限，<see cref="Records"/> 只列出了前一部分。</summary>
    public bool RecordsTruncated { get; internal set; }

    public bool IsIdentical => ChangedRecords == 0 && CountA == CountB;
}

/// <summary>两个文件的对比结果。</summary>
public sealed class BinaryCompareResult
{
    public required BtlInspector A { get; init; }
    public required BtlInspector B { get; init; }

    /// <summary>逐字节对齐时不同的字节数（含长度差）。</summary>
    public long DifferentBytes { get; init; }

    /// <summary>差异区间总数。</summary>
    public int RangeCount { get; init; }

    /// <summary>列出的差异区间（可能少于 <see cref="RangeCount"/>）。</summary>
    public required IReadOnlyList<BinaryDiffRange> Ranges { get; init; }

    public bool RangesTruncated => Ranges.Count < RangeCount;
    public required IReadOnlyList<BlockDiff> Blocks { get; init; }
    public bool Identical => DifferentBytes == 0;
}

/// <summary>
/// 两个 BTL / BIN 文件的二进制对比。
/// <para>
/// 两层结果：① 逐字节对齐的差异区间（连续差异合并成一段，两端分别用各自文件的数据块地址表定位）；
/// ② 按数据块、按记录序号对齐的结构化差异（逐字段列出 A / B 的值）。
/// 第②层不受"中间插入一条记录导致后面全部错位"影响：每个块在各自文件里定位，记录按序号一一比较。
/// 两边单位 / 援军记录族不同时（v1 对 v3），B 的记录先按 <see cref="BtlVersionConverter"/> 的映射对齐再比较。
/// </para>
/// </summary>
public static partial class BinaryCompare
{
    public const int DefaultMaxRanges = 20_000;
    public const int DefaultMaxRecordsPerBlock = 2_000;

    public static BinaryCompareResult Compare(BtlInspector a, BtlInspector b,
                                              int maxRanges = DefaultMaxRanges, int maxRecordsPerBlock = DefaultMaxRecordsPerBlock)
    {
        var ranges = new List<BinaryDiffRange>();
        int rangeCount = 0;
        long differentBytes = 0;

        long common = Math.Min(a.Data.LongLength, b.Data.LongLength);
        var spanA = a.Data.AsSpan(0, (int)common);
        var spanB = b.Data.AsSpan(0, (int)common);

        int i = 0;
        while (i < common)
        {
            i += spanA[i..].CommonPrefixLength(spanB[i..]);
            if (i >= common) break;

            int start = i;
            while (i < common && spanA[i] != spanB[i]) i++;

            differentBytes += i - start;
            rangeCount++;
            if (ranges.Count < maxRanges) ranges.Add(new BinaryDiffRange(start, i - start, false, false));
        }

        long lengthA = a.Data.LongLength, lengthB = b.Data.LongLength;
        if (lengthA != lengthB)
        {
            differentBytes += Math.Abs(lengthA - lengthB);
            rangeCount++;
            if (ranges.Count < maxRanges)
                ranges.Add(new BinaryDiffRange(common, Math.Abs(lengthA - lengthB), lengthA > lengthB, lengthB > lengthA));
        }

        return new BinaryCompareResult
        {
            A = a,
            B = b,
            DifferentBytes = differentBytes,
            RangeCount = rangeCount,
            Ranges = ranges,
            Blocks = CompareBlocks(a, b, maxRecordsPerBlock)
        };
    }

    private static List<BlockDiff> CompareBlocks(BtlInspector a, BtlInspector b, int maxRecords)
    {
        var result = new List<BlockDiff>();
        if (a.Kind == InspectedFileKind.Unknown || b.Kind == InspectedFileKind.Unknown) return result;
        if ((a.Kind == InspectedFileKind.World) != (b.Kind == InspectedFileKind.World)) return result;

        foreach (var block in Enum.GetValues<BtlBlock>())
        {
            var segA = a.GetSegment(block);
            var segB = b.GetSegment(block);
            int countA = segA?.ReadableCount ?? 0;
            int countB = segB?.ReadableCount ?? 0;
            if (countA == 0 && countB == 0) continue;

            bool converted = block is BtlBlock.Army or BtlBlock.Reinforcement
                             && segA != null && segB != null && a.Layout.Family != b.Layout.Family;
            var diff = new BlockDiff
            {
                Block = block,
                CountA = countA,
                CountB = countB,
                RecordSizeA = segA?.RecordSize ?? 0,
                RecordSizeB = segB?.RecordSize ?? 0,
                AlignedByConversion = converted
            };

            if (segA != null && segB != null)
                CompareRecords(a, b, segA, segB, diff, converted, maxRecords);

            for (int r = Math.Min(countA, countB); r < Math.Max(countA, countB); r++)
            {
                if (diff.Records.Count >= maxRecords) { diff.RecordsTruncated = true; break; }
                diff.Records.Add(new RecordDiff(block, r, countA > countB ? RecordDiffKind.OnlyInA : RecordDiffKind.OnlyInB, Array.Empty<FieldDiff>()));
            }

            result.Add(diff);
        }
        return result;
    }

    private static void CompareRecords(BtlInspector a, BtlInspector b, BtlSegment segA, BtlSegment segB,
                                       BlockDiff diff, bool converted, int maxRecords)
    {
        var schema = a.GetSchema(segA.Block);
        int common = Math.Min(segA.ReadableCount, segB.ReadableCount);

        for (int r = 0; r < common; r++)
        {
            ReadOnlySpan<byte> recordA = a.Data.AsSpan((int)segA.RecordOffset(r), segA.RecordSize);
            ReadOnlySpan<byte> recordB = b.Data.AsSpan((int)segB.RecordOffset(r), segB.RecordSize);
            if (converted)
            {
                recordB = segA.Block == BtlBlock.Army
                    ? BtlVersionConverter.ConvertArmyRecord(recordB, b.Layout.Family, a.Layout.Family)
                    : BtlVersionConverter.ConvertReinforcementRecord(recordB, b.Layout.Family, a.Layout.Family);
            }

            if (recordA.SequenceEqual(recordB)) continue;

            diff.ChangedRecords++;
            if (diff.Records.Count >= maxRecords)
            {
                diff.RecordsTruncated = true;
                continue;
            }

            diff.Records.Add(new RecordDiff(segA.Block, r, RecordDiffKind.Changed, DiffFields(schema, recordA, recordB)));
        }
    }

    private static List<FieldDiff> DiffFields(IReadOnlyList<BtlFieldDef> schema, ReadOnlySpan<byte> recordA, ReadOnlySpan<byte> recordB)
    {
        var fields = new List<FieldDiff>();
        if (schema.Count == 0)
        {
            // 扩展块 / 未解析数据没有字段表：整块按字节给出
            var whole = new BtlFieldDef(0, BtlFieldType.Blob, "RawBytes", Math.Min(recordA.Length, recordB.Length));
            fields.Add(new FieldDiff(whole, BtlInspector.HexBytes(recordA, 16), BtlInspector.HexBytes(recordB, 16)));
            return fields;
        }

        foreach (var def in schema)
        {
            if (def.Offset + def.Size > recordA.Length || def.Offset + def.Size > recordB.Length) continue;
            if (recordA.Slice(def.Offset, def.Size).SequenceEqual(recordB.Slice(def.Offset, def.Size))) continue;
            fields.Add(new FieldDiff(def, BtlInspector.FormatFieldValue(def, recordA), BtlInspector.FormatFieldValue(def, recordB)));
        }
        return fields;
    }

    /// <summary>区间一端在某文件里的定位描述（区间完全不在该文件里时给出"文件无此位置"）。</summary>
    public static string DescribeSide(BtlInspector side, long offset)
        => offset < side.Data.LongLength ? side.DescribeLocation(offset) : Loc.T("DataCheck.Compare.NoSuchOffset");
}
