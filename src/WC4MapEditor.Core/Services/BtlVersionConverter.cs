using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

/// <summary>记录字节搬运：源记录 [Source, Source+Length) 复制到目标记录 [Target, Target+Length)。</summary>
public readonly record struct RecordByteMove(int Source, int Target, int Length);

/// <summary>某字段在源数据里有非零值，但目标格式放不下（转换后丢失）。</summary>
/// <param name="Records">有非零值的记录条数</param>
/// <param name="FirstRecord">第一条有值的记录序号</param>
/// <param name="FirstValue">该记录里的值（"值 (十六进制)"）；有真实数据时取第一条非填充记录</param>
/// <param name="FillerRecords">其中只是 0xCC 填充（未初始化内存，不是数据）的记录条数</param>
public sealed record BtlFieldLoss(BtlBlock Block, BtlFieldDef Field, int Records, int FirstRecord, string FirstValue, int FillerRecords);

/// <summary>
/// BTL 单位 / 援军记录的 v1 ↔ v3 转换（文件转换、往返检查、当前地图转换）。
/// <para>
/// <b>字段映射依据</b>：对 assets/stage 全部 1358 个关卡按"正确布局"逐字段统计取值分布（2026-10-18）：
/// v1 单位记录（48 字节）与 v3 单位记录（64 字节）的前 0x30 字节、v1 援军记录（80 字节）与 v3 援军记录
/// （104 字节）的前 0x50 字节，每个位置的取值分布都一致 —— 即 <b>v1 记录 = v3 记录的前一段，同位同义</b>；
/// VB / 编辑器给同一字节起了不同的名字（如 0x1E：v1 叫占领触发事件、v3 叫运输船；0x22：v1 叫行为方案、
/// v3 叫进攻目标；0x2C：v1 编辑器拿来存所属军团、v3 叫坚守远程攻击距离）。
/// 唯一的搬家是徽章：v3 把单位 0x14-0x16 / 援军 0x3C-0x47 的徽章挪到了新增的勋章位（单位 0x30-0x32、
/// 援军 0x50-0x5B），旧位置标为"废弃"（v3 资源里 42 条仍留有旧值的援军，其勋章位的值与之完全相同）。
/// </para>
/// <para>
/// 因此 v1 → v3 无损；v3 → v1 只丢 v1 没有位置的字段：勋带 1-3、废弃位、单位 0x36-0x3F（编辑器的所属军团、
/// 两个含义未知的字节 0x3C / 0x3E、保留）。资源里 v3 援军的废弃位几乎全是 0xCC 填充（未初始化内存），
/// 报告里单独计数，不算作数据丢失。
/// VB 版按字段名逐个赋值，把 v1 的 0x22 搬到 v3 的 0x24（v3 资源里该位置恒为 0）、把援军 0x10 搬到 0x14，
/// 并静默丢掉仇恨值 / 运输船 / 坚守距离等字段 —— 这里不照搬。
/// </para>
/// <para>
/// 文件转换只替换单位块和援军块、改写头部版本号，其余字节（含尾部扩展数据块）原样保留；
/// 布局必须能精确识别（每个字节都有归属）才会转换，避免把未知结构的文件写坏。源文件永不修改。
/// </para>
/// </summary>
public static class BtlVersionConverter
{
    /// <summary>单位 v1 (48) → v3 (64)。</summary>
    public static IReadOnlyList<RecordByteMove> ArmyV1ToV3 { get; } = new RecordByteMove[]
    {
        new(0x00, 0x00, 0x14),   // 坐标 … 爵位 / HP等级：同位同义
        new(0x14, 0x30, 0x03),   // 徽章 1-3 → 勋章 1-3
        new(0x17, 0x17, 0x19)    // 技能等级 1 … 坚守远程攻击距离：同位同义
    };

    /// <summary>单位 v3 (64) → v1 (48)。丢弃：废弃 1-3（0x14）、勋带 1-3（0x33）、0x36-0x3F。</summary>
    public static IReadOnlyList<RecordByteMove> ArmyV3ToV1 { get; } = new RecordByteMove[]
    {
        new(0x00, 0x00, 0x14),
        new(0x30, 0x14, 0x03),   // 勋章 1-3 → 徽章 1-3
        new(0x17, 0x17, 0x19)
    };

    /// <summary>援军 v1 (80) → v3 (104)。</summary>
    public static IReadOnlyList<RecordByteMove> ReinforcementV1ToV3 { get; } = new RecordByteMove[]
    {
        new(0x00, 0x00, 0x3C),   // 坐标 … 技能等级 5：同位同义
        new(0x3C, 0x50, 0x0C),   // 徽章 1-3 → 勋章 1-3
        new(0x48, 0x48, 0x08)    // 所属国家 / 军团、爆兵 / 出现回合
    };

    /// <summary>援军 v3 (104) → v1 (80)。丢弃：废弃 1-3（0x3C）、勋带 1-3（0x5C）。</summary>
    public static IReadOnlyList<RecordByteMove> ReinforcementV3ToV1 { get; } = new RecordByteMove[]
    {
        new(0x00, 0x00, 0x3C),
        new(0x50, 0x3C, 0x0C),   // 勋章 1-3 → 徽章 1-3
        new(0x48, 0x48, 0x08)
    };

    public static IReadOnlyList<RecordByteMove> ArmyMoves(BtlRecordFamily from, BtlRecordFamily to)
        => from == to ? Identity(BtlInspector.ArmyRecordSize(from)) : from == BtlRecordFamily.V1 ? ArmyV1ToV3 : ArmyV3ToV1;

    public static IReadOnlyList<RecordByteMove> ReinforcementMoves(BtlRecordFamily from, BtlRecordFamily to)
        => from == to ? Identity(BtlInspector.ReinforcementRecordSize(from)) : from == BtlRecordFamily.V1 ? ReinforcementV1ToV3 : ReinforcementV3ToV1;

    private static RecordByteMove[] Identity(int size) => new[] { new RecordByteMove(0, 0, size) };

    /// <summary>目标版本号对应的记录族（1 → v1，3 → v3）。</summary>
    public static BtlRecordFamily FamilyOfTarget(int targetVersion) => targetVersion >= 3 ? BtlRecordFamily.V3 : BtlRecordFamily.V1;

    public static byte[] ConvertArmyRecord(ReadOnlySpan<byte> source, BtlRecordFamily from, BtlRecordFamily to)
        => ApplyMoves(source, ArmyMoves(from, to), BtlInspector.ArmyRecordSize(to));

    public static byte[] ConvertReinforcementRecord(ReadOnlySpan<byte> source, BtlRecordFamily from, BtlRecordFamily to)
        => ApplyMoves(source, ReinforcementMoves(from, to), BtlInspector.ReinforcementRecordSize(to));

    private static byte[] ApplyMoves(ReadOnlySpan<byte> source, IReadOnlyList<RecordByteMove> moves, int targetSize)
    {
        var target = new byte[targetSize];
        foreach (var move in moves)
            source.Slice(move.Source, move.Length).CopyTo(target.AsSpan(move.Target, move.Length));
        return target;
    }

    /// <summary>源字段表里没有被任何搬运完整覆盖的字段（转换后丢弃）。</summary>
    public static IReadOnlyList<BtlFieldDef> DroppedFields(IReadOnlyList<BtlFieldDef> sourceSchema, IReadOnlyList<RecordByteMove> moves)
        => sourceSchema.Where(f => !moves.Any(m => f.Offset >= m.Source && f.Offset + f.Size <= m.Source + m.Length)).ToList();

    /// <summary>目标字段表里没有被任何搬运写到的字段（转换后按 0 填充）。</summary>
    public static IReadOnlyList<BtlFieldDef> ZeroFilledFields(IReadOnlyList<BtlFieldDef> targetSchema, IReadOnlyList<RecordByteMove> moves)
        => targetSchema.Where(f => !moves.Any(m => f.Offset >= m.Target && f.Offset + f.Size <= m.Target + m.Length)).ToList();

    // ============================================================= 文件转换 =============================================================

    /// <summary>
    /// 把一个 BTL 文件的字节转换为目标版本（1 或 3），返回新文件字节与转换报告；源字节不会被修改。
    /// </summary>
    public static BtlConversionResult ConvertFile(byte[] source, int targetVersion, InspectLayoutOverride layoutOverride = InspectLayoutOverride.Auto)
        => ConvertFile(BtlInspector.FromBytes(source, "", layoutOverride), targetVersion);

    /// <summary>按已经识别好的布局转换（数据检查场景复用已打开的检查器）；源字节不会被修改。</summary>
    public static BtlConversionResult ConvertFile(BtlInspector source, int targetVersion)
    {
        if (targetVersion is not (1 or 3))
            return BtlConversionResult.Fail(Loc.F("DataCheck.Convert.Err.Target", targetVersion));

        string? error = ValidateSource(source);
        if (error != null) return BtlConversionResult.Fail(error);
        if (source.BtlVersion == targetVersion)
            return BtlConversionResult.Fail(Loc.F("DataCheck.Convert.Err.SameVersion", targetVersion));

        return Convert(source, targetVersion, FamilyOfTarget(targetVersion));
    }

    /// <summary>转换前提：BTL、整个文件已读入、布局精确吻合。不满足时返回原因。</summary>
    public static string? ValidateSource(BtlInspector inspector)
    {
        if (!inspector.IsBtl) return Loc.T("DataCheck.Convert.Err.NotBtl");
        if (inspector.IsPartial) return Loc.T("DataCheck.Convert.Err.Partial");
        if (!inspector.Layout.IsExact)
            return Loc.F("DataCheck.Convert.Err.Inexact", inspector.Layout.UnparsedBytes, inspector.Layout.MissingBytes);
        return null;
    }

    private static BtlConversionResult Convert(BtlInspector source, int targetVersion, BtlRecordFamily targetFamily)
    {
        byte[] data = source.Data;
        var army = source.GetSegment(BtlBlock.Army)!;
        var reinforcement = source.GetSegment(BtlBlock.Reinforcement)!;
        var from = source.Layout.Family;

        using var output = new MemoryStream(data.Length + (army.Count + reinforcement.Count) * 24);
        output.Write(data, 0, (int)army.Start);
        for (int i = 0; i < army.Count; i++)
            output.Write(ConvertArmyRecord(data.AsSpan((int)army.RecordOffset(i), army.RecordSize), from, targetFamily));
        output.Write(data, (int)army.End, (int)(reinforcement.Start - army.End));
        for (int i = 0; i < reinforcement.Count; i++)
            output.Write(ConvertReinforcementRecord(data.AsSpan((int)reinforcement.RecordOffset(i), reinforcement.RecordSize), from, targetFamily));
        output.Write(data, (int)reinforcement.End, (int)(data.LongLength - reinforcement.End));

        byte[] bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), targetVersion);

        // 自检：输出必须能按同一种布局精确识别，记录族、记录数都对得上
        var check = BtlInspector.FromBytes(bytes, "", source.Kind == InspectedFileKind.Stage ? InspectLayoutOverride.Stage : InspectLayoutOverride.Conquest);
        if (!check.Layout.IsExact || check.Layout.Family != targetFamily
            || check.GetRecordCount(BtlBlock.Army) != army.Count || check.GetRecordCount(BtlBlock.Reinforcement) != reinforcement.Count)
            return BtlConversionResult.Fail(Loc.T("DataCheck.Convert.Err.SelfCheck"));

        var losses = new List<BtlFieldLoss>();
        if (from != targetFamily)
        {
            CollectLosses(BtlBlock.Army, army.Count, i => data.AsSpan((int)army.RecordOffset(i), army.RecordSize),
                          BtlInspector.ArmySchema(from), ArmyMoves(from, targetFamily), losses);
            CollectLosses(BtlBlock.Reinforcement, reinforcement.Count, i => data.AsSpan((int)reinforcement.RecordOffset(i), reinforcement.RecordSize),
                          BtlInspector.ReinforcementSchema(from), ReinforcementMoves(from, targetFamily), losses);
        }

        return new BtlConversionResult
        {
            Success = true,
            Output = bytes,
            Kind = source.Kind,
            SourceVersion = source.BtlVersion,
            TargetVersion = targetVersion,
            SourceFamily = from,
            TargetFamily = targetFamily,
            ArmyCount = army.Count,
            ReinforcementCount = reinforcement.Count,
            ExtensionBytes = source.GetSegment(BtlBlock.Extension)?.Length ?? 0,
            Losses = losses
        };
    }

    private delegate ReadOnlySpan<byte> RecordAccessor(int index);

    /// <summary>统计"被丢弃的字段"里有非零值的记录。</summary>
    private static void CollectLosses(BtlBlock block, int count, RecordAccessor record, IReadOnlyList<BtlFieldDef> sourceSchema,
                                      IReadOnlyList<RecordByteMove> moves, List<BtlFieldLoss> losses)
    {
        foreach (var field in DroppedFields(sourceSchema, moves))
            AddLossIfAny(block, field, count, record, losses);
    }

    private static void AddLossIfAny(BtlBlock block, BtlFieldDef field, int count, RecordAccessor record, List<BtlFieldLoss> losses)
    {
        int records = 0, fillers = 0, first = -1;
        bool firstIsFiller = true;
        string firstValue = "";
        for (int i = 0; i < count; i++)
        {
            var bytes = record(i);
            var value = bytes.Slice(field.Offset, field.Size);
            if (value.IndexOfAnyExcept((byte)0) < 0) continue;

            records++;
            bool filler = IsFiller(value);
            if (filler) fillers++;
            if (first < 0 || (firstIsFiller && !filler))
            {
                first = i;
                firstIsFiller = filler;
                firstValue = BtlInspector.FormatFieldValue(field, bytes);
            }
        }
        if (records > 0) losses.Add(new BtlFieldLoss(block, field, records, first, firstValue, fillers));
    }

    /// <summary>
    /// 是否只是 0xCC 填充：官方导出工具留下的未初始化内存（MSVC 调试填充），资源里 v3 援军的废弃位几乎都是它。
    /// </summary>
    public static bool IsFiller(ReadOnlySpan<byte> value) => !value.IsEmpty && value.IndexOfAnyExcept((byte)0xCC) < 0;

    // ============================================================= 往返检查 =============================================================

    /// <summary>
    /// 往返检查（只在内存里做，不写任何文件）：v3 → v1 → v3（或 v1 → v3 → v1），逐字段统计哪些值没能回来，
    /// 并确认单位 / 援军块以外的字节完全不变。
    /// </summary>
    public static BtlRoundTripResult CheckRoundTrip(byte[] source, InspectLayoutOverride layoutOverride = InspectLayoutOverride.Auto)
        => CheckRoundTrip(BtlInspector.FromBytes(source, "", layoutOverride));

    /// <summary>对已经识别好布局的文件做往返检查（只在内存里）。</summary>
    public static BtlRoundTripResult CheckRoundTrip(BtlInspector inspector)
    {
        byte[] source = inspector.Data;
        string? error = ValidateSource(inspector);
        if (error != null) return BtlRoundTripResult.Fail(error);

        var from = inspector.Layout.Family;
        var via = from == BtlRecordFamily.V3 ? BtlRecordFamily.V1 : BtlRecordFamily.V3;
        int viaVersion = via == BtlRecordFamily.V3 ? 3 : 1;

        var first = Convert(inspector, viaVersion, via);
        if (!first.Success) return BtlRoundTripResult.Fail(first.Error!);

        var kindOverride = inspector.Kind == InspectedFileKind.Stage ? InspectLayoutOverride.Stage : InspectLayoutOverride.Conquest;
        var second = Convert(BtlInspector.FromBytes(first.Output!, "", kindOverride), inspector.BtlVersion, from);
        if (!second.Success) return BtlRoundTripResult.Fail(second.Error!);

        byte[] back = second.Output!;
        var army = inspector.GetSegment(BtlBlock.Army)!;
        var reinforcement = inspector.GetSegment(BtlBlock.Reinforcement)!;

        // 单位 / 援军块以外的字节应该一字不差
        long otherBytes = CountDifferences(source, back, 0, army.Start)
                          + CountDifferences(source, back, army.End, reinforcement.Start)
                          + CountDifferences(source, back, reinforcement.End, source.LongLength);

        var changes = new List<BtlRoundTripChange>();
        CollectRoundTripChanges(BtlBlock.Army, army, BtlInspector.ArmySchema(from), source, back, changes);
        CollectRoundTripChanges(BtlBlock.Reinforcement, reinforcement, BtlInspector.ReinforcementSchema(from), source, back, changes);

        return new BtlRoundTripResult
        {
            Success = true,
            Kind = inspector.Kind,
            SourceVersion = inspector.BtlVersion,
            ViaVersion = viaVersion,
            ArmyCount = army.Count,
            ReinforcementCount = reinforcement.Count,
            OtherDifferentBytes = otherBytes,
            LengthMatches = back.LongLength == source.LongLength,
            Changes = changes
        };
    }

    private static long CountDifferences(byte[] a, byte[] b, long start, long end)
    {
        if (end > b.LongLength) return end - start;
        long count = 0;
        for (long i = start; i < end; i++)
            if (a[i] != b[i]) count++;
        return count;
    }

    private static void CollectRoundTripChanges(BtlBlock block, BtlSegment segment, IReadOnlyList<BtlFieldDef> schema,
                                                byte[] before, byte[] after, List<BtlRoundTripChange> changes)
    {
        foreach (var field in schema)
        {
            int records = 0, fillers = 0, first = -1;
            bool firstIsFiller = true;
            string firstBefore = "", firstAfter = "";
            for (int i = 0; i < segment.Count; i++)
            {
                int start = (int)segment.RecordOffset(i);
                var a = before.AsSpan(start, segment.RecordSize);
                var b = after.AsSpan(start, segment.RecordSize);
                var valueBefore = a.Slice(field.Offset, field.Size);
                if (valueBefore.SequenceEqual(b.Slice(field.Offset, field.Size))) continue;

                records++;
                bool filler = IsFiller(valueBefore);
                if (filler) fillers++;
                if (first < 0 || (firstIsFiller && !filler))
                {
                    first = i;
                    firstIsFiller = filler;
                    firstBefore = BtlInspector.FormatFieldValue(field, a);
                    firstAfter = BtlInspector.FormatFieldValue(field, b);
                }
            }
            if (records > 0) changes.Add(new BtlRoundTripChange(block, field, records, first, firstBefore, firstAfter, fillers));
        }
    }

    // ============================================================= 当前地图转换 =============================================================

    /// <summary>
    /// 规划当前地图（MapData）的版本转换：用与文件转换相同的字节映射生成新列表，不立即改动地图。
    /// <para>
    /// 记录经过编辑器自己的结构体（Army / Army_3 / Reinforcement / Reinforcement_3）进出，
    /// 而这些结构体并不保存记录的每个字节。这里先探测它们"保存不了"的字节：目标结构体放不下的非零值记为
    /// <see cref="BtlMapConversion.ModelLosses"/>（调用方应拒绝转换，除非用户明确接受）；
    /// 源结构体放不下的字段在载入地图时就已丢失，记为 <see cref="BtlMapConversion.SourceBlindFields"/> 提醒用户。
    /// 需要无损转换时请对文件做转换（<see cref="ConvertFile(byte[], int, InspectLayoutOverride)"/>）。
    /// </para>
    /// </summary>
    public static BtlMapConversion PlanMapConversion(MapData map, int targetVersion)
    {
        if (targetVersion is not (1 or 3))
            return BtlMapConversion.Fail(Loc.F("DataCheck.Convert.Err.Target", targetVersion));

        int sourceVersion = map.Header.BtlVersion;
        if (sourceVersion == targetVersion)
            return BtlMapConversion.Fail(Loc.F("DataCheck.Convert.Err.SameVersion", targetVersion));

        // v2 / v3 地图的单位、援军放在 V3 列表里（记录大小 64 / 104 字节）
        var from = map.Header.UsesV3Records ? BtlRecordFamily.V3 : BtlRecordFamily.V1;
        var to = FamilyOfTarget(targetVersion);

        var sourceArmies = from == BtlRecordFamily.V3 ? map.ArmiesV3.Select(ArmyV3Bytes).ToList() : map.Armies.Select(ArmyV1Bytes).ToList();
        var sourceReinforcements = from == BtlRecordFamily.V3
            ? map.ReinforcementsV3.Select(ReinforcementV3Bytes).ToList()
            : map.Reinforcements.Select(ReinforcementV1Bytes).ToList();

        var armies = sourceArmies.Select(r => ConvertArmyRecord(r, from, to)).ToList();
        var reinforcements = sourceReinforcements.Select(r => ConvertReinforcementRecord(r, from, to)).ToList();

        var losses = new List<BtlFieldLoss>();
        var modelLosses = new List<BtlFieldLoss>();
        var sourceBlind = new List<BtlFieldDef>();
        if (from != to)
        {
            CollectLosses(BtlBlock.Army, sourceArmies.Count, i => sourceArmies[i], BtlInspector.ArmySchema(from), ArmyMoves(from, to), losses);
            CollectLosses(BtlBlock.Reinforcement, sourceReinforcements.Count, i => sourceReinforcements[i],
                          BtlInspector.ReinforcementSchema(from), ReinforcementMoves(from, to), losses);

            CollectModelLosses(BtlBlock.Army, armies, BtlInspector.ArmySchema(to), ArmyModelBlindSpots(to), modelLosses);
            CollectModelLosses(BtlBlock.Reinforcement, reinforcements, BtlInspector.ReinforcementSchema(to), ReinforcementModelBlindSpots(to), modelLosses);

            if (sourceArmies.Count > 0)
                sourceBlind.AddRange(BlindFields(BtlInspector.ArmySchema(from), ArmyModelBlindSpots(from)));
            if (sourceReinforcements.Count > 0)
                sourceBlind.AddRange(BlindFields(BtlInspector.ReinforcementSchema(from), ReinforcementModelBlindSpots(from)));
        }

        return new BtlMapConversion(map, sourceVersion, targetVersion, from, to,
                                    to == BtlRecordFamily.V1 ? armies.Select(b => Army.FromBytes(b, 0)).ToArray() : Array.Empty<Army>(),
                                    to == BtlRecordFamily.V3 ? armies.Select(b => Army_3.FromBytes(b, 0)).ToArray() : Array.Empty<Army_3>(),
                                    to == BtlRecordFamily.V1 ? reinforcements.Select(b => Reinforcement.FromBytes(b, 0)).ToArray() : Array.Empty<Reinforcement>(),
                                    to == BtlRecordFamily.V3 ? reinforcements.Select(b => Reinforcement_3.FromBytes(b, 0)).ToArray() : Array.Empty<Reinforcement_3>(),
                                    losses, modelLosses, sourceBlind);
    }

    private static byte[] ArmyV1Bytes(Army army) { var b = new byte[48]; army.ToBytes(b, 0); return b; }
    private static byte[] ArmyV3Bytes(Army_3 army) { var b = new byte[64]; army.ToBytes(b, 0); return b; }
    private static byte[] ReinforcementV1Bytes(Reinforcement r) { var b = new byte[80]; r.ToBytes(b, 0); return b; }
    private static byte[] ReinforcementV3Bytes(Reinforcement_3 r) { var b = new byte[104]; r.ToBytes(b, 0); return b; }

    /// <summary>编辑器单位结构体保存不了的字节位置（FromBytes → ToBytes 之后变了的位置）。</summary>
    private static bool[] ArmyModelBlindSpots(BtlRecordFamily family) => family == BtlRecordFamily.V3
        ? ProbeBlindSpots(64, b => ArmyV3Bytes(Army_3.FromBytes(b, 0)))
        : ProbeBlindSpots(48, b => ArmyV1Bytes(Army.FromBytes(b, 0)));

    /// <summary>编辑器援军结构体保存不了的字节位置。</summary>
    private static bool[] ReinforcementModelBlindSpots(BtlRecordFamily family) => family == BtlRecordFamily.V3
        ? ProbeBlindSpots(104, b => ReinforcementV3Bytes(Reinforcement_3.FromBytes(b, 0)))
        : ProbeBlindSpots(80, b => ReinforcementV1Bytes(Reinforcement.FromBytes(b, 0)));

    private static bool[] ProbeBlindSpots(int size, Func<byte[], byte[]> roundTrip)
    {
        var probe = new byte[size];
        for (int i = 0; i < size; i++) probe[i] = (byte)(0xA5 ^ i | 1);
        var back = roundTrip(probe);
        var blind = new bool[size];
        for (int i = 0; i < size; i++) blind[i] = back[i] != probe[i];
        return blind;
    }

    private static IEnumerable<BtlFieldDef> BlindFields(IReadOnlyList<BtlFieldDef> schema, bool[] blind)
        => schema.Where(f => Enumerable.Range(f.Offset, f.Size).Any(i => blind[i]));

    private static void CollectModelLosses(BtlBlock block, List<byte[]> records, IReadOnlyList<BtlFieldDef> targetSchema,
                                           bool[] blind, List<BtlFieldLoss> losses)
    {
        foreach (var field in BlindFields(targetSchema, blind))
            AddLossIfAny(block, field, records.Count, i => records[i], losses);
    }

    // ============================================================= 报告用 =============================================================

    /// <summary>字段映射说明（每段搬运一行 + 丢弃 / 补零的字段），转换报告与场景说明共用。</summary>
    public static IEnumerable<string> DescribeMapping(BtlBlock block, BtlRecordFamily from, BtlRecordFamily to)
    {
        bool isArmy = block == BtlBlock.Army;
        var moves = isArmy ? ArmyMoves(from, to) : ReinforcementMoves(from, to);
        var fromSchema = isArmy ? BtlInspector.ArmySchema(from) : BtlInspector.ReinforcementSchema(from);
        var toSchema = isArmy ? BtlInspector.ArmySchema(to) : BtlInspector.ReinforcementSchema(to);

        foreach (var move in moves)
        {
            string source = FieldSpan(fromSchema, move.Source, move.Length);
            string target = FieldSpan(toSchema, move.Target, move.Length);
            string sourceRange = ByteRange(move.Source, move.Length);
            string targetRange = ByteRange(move.Target, move.Length);
            yield return move.Source == move.Target
                ? Loc.F("DataCheck.Convert.MoveSame", sourceRange, source, target)
                : Loc.F("DataCheck.Convert.MoveTo", sourceRange, targetRange, source, target);
        }

        var dropped = DroppedFields(fromSchema, moves);
        if (dropped.Count > 0)
            yield return Loc.F("DataCheck.Convert.Dropped", string.Join(Loc.T("DataCheck.ListSeparator"), dropped.Select(f => f.DisplayName)));

        var zero = ZeroFilledFields(toSchema, moves);
        if (zero.Count > 0)
            yield return Loc.F("DataCheck.Convert.ZeroFilled", string.Join(Loc.T("DataCheck.ListSeparator"), zero.Select(f => f.DisplayName)));
    }

    private static string FieldSpan(IReadOnlyList<BtlFieldDef> schema, int offset, int length)
    {
        var fields = schema.Where(f => f.Offset >= offset && f.Offset < offset + length).ToList();
        return fields.Count switch
        {
            0 => "—",
            1 => fields[0].DisplayName,
            _ => fields[0].DisplayName + " … " + fields[^1].DisplayName
        };
    }

    private static string ByteRange(int offset, int length)
        => length <= 1
            ? "0x" + offset.ToString("X2", CultureInfo.InvariantCulture)
            : "0x" + offset.ToString("X2", CultureInfo.InvariantCulture) + "–0x" + (offset + length - 1).ToString("X2", CultureInfo.InvariantCulture);

    internal static string LossLine(BtlFieldLoss loss)
        => Loc.F("DataCheck.Convert.LossLine", BtlInspector.BlockDisplayName(loss.Block), loss.Field.DisplayName,
                 "0x" + loss.Field.Offset.ToString("X2", CultureInfo.InvariantCulture), loss.Records, loss.FirstRecord, loss.FirstValue)
           + FillerNote(loss.FillerRecords);

    /// <summary>"（其中 N 条只是 0xCC 填充）"，没有填充记录时为空。</summary>
    internal static string FillerNote(long fillerRecords)
        => fillerRecords > 0 ? Loc.F("DataCheck.Convert.FillerNote", fillerRecords) : "";

    internal static string VersionLabel(int version, BtlRecordFamily family)
        => Loc.F("DataCheck.Convert.VersionLabel", version, BtlInspector.FamilyDisplayName(family));
}

/// <summary>文件转换结果。</summary>
public sealed class BtlConversionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>转换后的完整文件字节（失败时为 null）。</summary>
    public byte[]? Output { get; init; }

    public InspectedFileKind Kind { get; init; }
    public int SourceVersion { get; init; }
    public int TargetVersion { get; init; }
    public BtlRecordFamily SourceFamily { get; init; }
    public BtlRecordFamily TargetFamily { get; init; }
    public int ArmyCount { get; init; }
    public int ReinforcementCount { get; init; }

    /// <summary>原样保留的尾部扩展数据块字节数。</summary>
    public long ExtensionBytes { get; init; }

    /// <summary>源文件里有值、目标格式放不下的字段。</summary>
    public IReadOnlyList<BtlFieldLoss> Losses { get; init; } = Array.Empty<BtlFieldLoss>();

    internal static BtlConversionResult Fail(string error) => new() { Success = false, Error = error };

    /// <summary>多行转换报告（当前语言）：概要、字段映射、丢失字段。</summary>
    public string ToReport(string sourcePath, string? outputPath)
    {
        var sb = new StringBuilder();
        if (!Success)
        {
            sb.AppendLine(Loc.F("DataCheck.Convert.Report.Failed", sourcePath, Error));
            return sb.ToString();
        }

        sb.AppendLine(Loc.F("DataCheck.Convert.Report.Title", BtlVersionConverter.VersionLabel(SourceVersion, SourceFamily),
                            BtlVersionConverter.VersionLabel(TargetVersion, TargetFamily), BtlInspector.KindDisplayName(Kind)));
        sb.AppendLine(Loc.F("DataCheck.Convert.Report.Source", sourcePath));
        if (outputPath != null) sb.AppendLine(Loc.F("DataCheck.Convert.Report.Output", outputPath, Output!.Length));
        sb.AppendLine(Loc.F("DataCheck.Convert.Report.Counts", ArmyCount, BtlInspector.ArmyRecordSize(SourceFamily), BtlInspector.ArmyRecordSize(TargetFamily),
                            ReinforcementCount, BtlInspector.ReinforcementRecordSize(SourceFamily), BtlInspector.ReinforcementRecordSize(TargetFamily)));
        sb.AppendLine(Loc.T("DataCheck.Convert.Report.Preserved"));
        if (ExtensionBytes > 0) sb.AppendLine(Loc.F("DataCheck.Convert.Report.Extension", ExtensionBytes));

        if (SourceFamily == TargetFamily)
        {
            sb.AppendLine(Loc.T("DataCheck.Convert.Report.SameFamily"));
            return sb.ToString();
        }

        AppendMapping(sb, SourceFamily, TargetFamily);
        sb.AppendLine();
        if (Losses.Count == 0)
        {
            sb.AppendLine(Loc.T("DataCheck.Convert.Report.NoLoss"));
        }
        else
        {
            sb.AppendLine(Loc.T("DataCheck.Convert.Report.LossHeader"));
            foreach (var loss in Losses) sb.AppendLine(BtlVersionConverter.LossLine(loss));
        }
        return sb.ToString();
    }

    /// <summary>单位 + 援军两段字段映射说明。</summary>
    public static void AppendMapping(StringBuilder sb, BtlRecordFamily from, BtlRecordFamily to)
    {
        foreach (var block in new[] { BtlBlock.Army, BtlBlock.Reinforcement })
        {
            sb.AppendLine();
            sb.AppendLine(Loc.F("DataCheck.Convert.Report.MappingHeader", BtlInspector.BlockDisplayName(block),
                                BtlInspector.FamilyDisplayName(from), BtlInspector.FamilyDisplayName(to)));
            foreach (var line in BtlVersionConverter.DescribeMapping(block, from, to)) sb.AppendLine(line);
        }
    }
}

/// <summary>往返检查中，某字段没能原样回来的统计（<paramref name="FillerRecords"/> = 原值只是 0xCC 填充的记录数）。</summary>
public sealed record BtlRoundTripChange(BtlBlock Block, BtlFieldDef Field, int Records, int FirstRecord, string Before, string After, int FillerRecords);

/// <summary>往返检查结果。</summary>
public sealed class BtlRoundTripResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public InspectedFileKind Kind { get; init; }
    public int SourceVersion { get; init; }
    public int ViaVersion { get; init; }
    public int ArmyCount { get; init; }
    public int ReinforcementCount { get; init; }

    /// <summary>单位 / 援军块以外不同的字节数（应为 0）。</summary>
    public long OtherDifferentBytes { get; init; }

    public bool LengthMatches { get; init; }
    public IReadOnlyList<BtlRoundTripChange> Changes { get; init; } = Array.Empty<BtlRoundTripChange>();

    /// <summary>除被丢弃的字段外完全复原。</summary>
    public bool OtherDataIntact => LengthMatches && OtherDifferentBytes == 0;

    internal static BtlRoundTripResult Fail(string error) => new() { Success = false, Error = error };

    public string ToReport(string sourcePath)
    {
        var sb = new StringBuilder();
        if (!Success)
        {
            sb.AppendLine(Loc.F("DataCheck.Convert.Report.Failed", sourcePath, Error));
            return sb.ToString();
        }

        sb.AppendLine(Loc.F("DataCheck.RoundTrip.Title", SourceVersion, ViaVersion, BtlInspector.KindDisplayName(Kind)));
        sb.AppendLine(Loc.F("DataCheck.Convert.Report.Source", sourcePath));
        sb.AppendLine(Loc.F("DataCheck.RoundTrip.Counts", ArmyCount, ReinforcementCount));
        sb.AppendLine(OtherDataIntact
            ? Loc.T("DataCheck.RoundTrip.OtherIntact")
            : Loc.F("DataCheck.RoundTrip.OtherChanged", OtherDifferentBytes));

        if (Changes.Count == 0)
        {
            sb.AppendLine(Loc.T("DataCheck.RoundTrip.AllRestored"));
            return sb.ToString();
        }

        sb.AppendLine(Loc.T("DataCheck.RoundTrip.ChangeHeader"));
        foreach (var change in Changes)
            sb.AppendLine(Loc.F("DataCheck.RoundTrip.ChangeLine", BtlInspector.BlockDisplayName(change.Block), change.Field.DisplayName,
                                "0x" + change.Field.Offset.ToString("X2", CultureInfo.InvariantCulture), change.Records,
                                change.FirstRecord, change.Before, change.After)
                          + BtlVersionConverter.FillerNote(change.FillerRecords));
        return sb.ToString();
    }
}

/// <summary>
/// 当前地图的版本转换计划：已经算好的新列表 + 丢失统计；<see cref="Apply"/> / <see cref="Revert"/> 可重复调用（撤销 / 重做）。
/// </summary>
public sealed class BtlMapConversion
{
    private readonly Army[] _oldArmies = Array.Empty<Army>();
    private readonly Army_3[] _oldArmiesV3 = Array.Empty<Army_3>();
    private readonly Reinforcement[] _oldReinforcements = Array.Empty<Reinforcement>();
    private readonly Reinforcement_3[] _oldReinforcementsV3 = Array.Empty<Reinforcement_3>();
    private readonly Army[] _newArmies = Array.Empty<Army>();
    private readonly Army_3[] _newArmiesV3 = Array.Empty<Army_3>();
    private readonly Reinforcement[] _newReinforcements = Array.Empty<Reinforcement>();
    private readonly Reinforcement_3[] _newReinforcementsV3 = Array.Empty<Reinforcement_3>();

    private BtlMapConversion(string error)
    {
        Success = false;
        Error = error;
    }

    internal BtlMapConversion(MapData map, int sourceVersion, int targetVersion, BtlRecordFamily from, BtlRecordFamily to,
                              Army[] armies, Army_3[] armiesV3, Reinforcement[] reinforcements, Reinforcement_3[] reinforcementsV3,
                              IReadOnlyList<BtlFieldLoss> losses, IReadOnlyList<BtlFieldLoss> modelLosses, IReadOnlyList<BtlFieldDef> sourceBlind)
    {
        Success = true;
        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;
        SourceFamily = from;
        TargetFamily = to;
        Losses = losses;
        ModelLosses = modelLosses;
        SourceBlindFields = sourceBlind;

        _oldArmies = map.Armies.ToArray();
        _oldArmiesV3 = map.ArmiesV3.ToArray();
        _oldReinforcements = map.Reinforcements.ToArray();
        _oldReinforcementsV3 = map.ReinforcementsV3.ToArray();

        // 记录族不变（如 v2 → v1）时只改版本号，列表保持原样
        bool sameFamily = from == to;
        _newArmies = sameFamily ? _oldArmies : armies;
        _newArmiesV3 = sameFamily ? _oldArmiesV3 : armiesV3;
        _newReinforcements = sameFamily ? _oldReinforcements : reinforcements;
        _newReinforcementsV3 = sameFamily ? _oldReinforcementsV3 : reinforcementsV3;
    }

    public bool Success { get; }
    public string? Error { get; }
    public int SourceVersion { get; }
    public int TargetVersion { get; }
    public BtlRecordFamily SourceFamily { get; }
    public BtlRecordFamily TargetFamily { get; }
    public int ArmyCount => Math.Max(_newArmies.Length, _newArmiesV3.Length);
    public int ReinforcementCount => Math.Max(_newReinforcements.Length, _newReinforcementsV3.Length);

    /// <summary>映射丢弃、但源数据里有值的字段。</summary>
    public IReadOnlyList<BtlFieldLoss> Losses { get; } = Array.Empty<BtlFieldLoss>();

    /// <summary>编辑器的目标结构体放不下的非零字段（应用后再保存会丢失）。</summary>
    public IReadOnlyList<BtlFieldLoss> ModelLosses { get; } = Array.Empty<BtlFieldLoss>();

    /// <summary>编辑器的源结构体本来就不保存的字段（载入地图时已丢失，转换无法找回）。</summary>
    public IReadOnlyList<BtlFieldDef> SourceBlindFields { get; } = Array.Empty<BtlFieldDef>();

    internal static BtlMapConversion Fail(string error) => new(error);

    public void Apply(MapData map) => Assign(map, _newArmies, _newArmiesV3, _newReinforcements, _newReinforcementsV3, TargetVersion);

    public void Revert(MapData map) => Assign(map, _oldArmies, _oldArmiesV3, _oldReinforcements, _oldReinforcementsV3, SourceVersion);

    private static void Assign(MapData map, Army[] armies, Army_3[] armiesV3, Reinforcement[] reinforcements,
                               Reinforcement_3[] reinforcementsV3, int version)
    {
        Replace(map.Armies, armies);
        Replace(map.ArmiesV3, armiesV3);
        Replace(map.Reinforcements, reinforcements);
        Replace(map.ReinforcementsV3, reinforcementsV3);
        map.Header.BtlVersion = version;
        map.IsModified = true;
    }

    private static void Replace<T>(ObservableCollection<T> list, T[] items)
    {
        list.Clear();
        foreach (var item in items) list.Add(item);
    }

    public string ToReport()
    {
        var sb = new StringBuilder();
        if (!Success)
        {
            sb.AppendLine(Error);
            return sb.ToString();
        }

        sb.AppendLine(Loc.F("DataCheck.Convert.Map.Title", BtlVersionConverter.VersionLabel(SourceVersion, SourceFamily),
                            BtlVersionConverter.VersionLabel(TargetVersion, TargetFamily)));
        sb.AppendLine(Loc.F("DataCheck.Convert.Map.Counts", ArmyCount, ReinforcementCount));
        if (SourceFamily == TargetFamily)
        {
            sb.AppendLine(Loc.T("DataCheck.Convert.Report.SameFamily"));
            return sb.ToString();
        }

        foreach (var loss in Losses) sb.AppendLine(BtlVersionConverter.LossLine(loss));
        if (Losses.Count == 0) sb.AppendLine(Loc.T("DataCheck.Convert.Report.NoLoss"));

        if (ModelLosses.Count > 0)
        {
            sb.AppendLine(Loc.T("DataCheck.Convert.Map.ModelLossHeader"));
            foreach (var loss in ModelLosses) sb.AppendLine(BtlVersionConverter.LossLine(loss));
        }

        if (SourceBlindFields.Count > 0)
            sb.AppendLine(Loc.F("DataCheck.Convert.Map.SourceBlind",
                                string.Join(Loc.T("DataCheck.ListSeparator"), SourceBlindFields.Select(f => f.DisplayName).Distinct())));
        return sb.ToString();
    }
}
