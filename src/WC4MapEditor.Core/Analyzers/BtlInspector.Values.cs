using System.Buffers.Binary;
using System.Globalization;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>一个字段的解码结果（Offset 为文件内绝对偏移）。</summary>
public readonly record struct BtlFieldValue(BtlFieldDef Field, long Offset, long Raw, string Value, string Hex, string Binary, string Meaning);

/// <summary>一条记录的解码结果。</summary>
public sealed class BtlRecord
{
    public required BtlBlock Block { get; init; }
    public required int Index { get; init; }
    public required long Offset { get; init; }
    public required int Size { get; init; }
    public required IReadOnlyList<BtlFieldValue> Fields { get; init; }
}

// BtlInspector：记录解码、取值解释与列表标签。
public sealed partial class BtlInspector
{
    private const int MaxBlobHexBytes = 16;

    /// <summary>解码一条记录；越界或块不存在时返回 null。</summary>
    public BtlRecord? GetRecord(BtlBlock block, int index)
    {
        var segment = GetSegment(block);
        if (segment == null || index < 0 || index >= segment.ReadableCount) return null;

        long start = segment.RecordOffset(index);
        IReadOnlyList<BtlFieldValue> fields = block switch
        {
            BtlBlock.Extension => DecodeExtension(start, segment.RecordSize),
            BtlBlock.Unparsed => new[] { DecodeField(Blob(0, segment.RecordSize, "RawBytes"), start) },
            _ => GetSchema(block).Select(def => DecodeField(def, start)).ToArray()
        };

        return new BtlRecord { Block = block, Index = index, Offset = start, Size = segment.RecordSize, Fields = fields };
    }

    /// <summary>读取字段的原始整数值（按类型做符号扩展；浮点返回位模式）。</summary>
    public long ReadRaw(BtlFieldDef def, long recordStart)
    {
        long o = recordStart + def.Offset;
        if (o < 0 || o + def.Size > Data.LongLength) return 0;
        return ReadRaw(def.Type, Data.AsSpan((int)o, def.Size));
    }

    private static long ReadRaw(BtlFieldType type, ReadOnlySpan<byte> span) => type switch
    {
        BtlFieldType.U8 => span[0],
        BtlFieldType.I8 => (sbyte)span[0],
        BtlFieldType.U16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
        BtlFieldType.I16 => BinaryPrimitives.ReadInt16LittleEndian(span),
        BtlFieldType.U32 or BtlFieldType.Rgb => BinaryPrimitives.ReadUInt32LittleEndian(span),
        BtlFieldType.I32 or BtlFieldType.F32 => BinaryPrimitives.ReadInt32LittleEndian(span),
        _ => 0
    };

    /// <summary>
    /// 记录字节（从记录起点算）中某字段的"值 (十六进制)"文本；对比报告、转换报告用。
    /// 文本 / 原样字节类字段只给十六进制。
    /// </summary>
    public static string FormatFieldValue(BtlFieldDef def, ReadOnlySpan<byte> record)
    {
        if (def.Offset + def.Size > record.Length) return "—";
        var bytes = record.Slice(def.Offset, def.Size);
        long raw = ReadRaw(def.Type, bytes);
        string hex = FormatHex(def, raw, bytes);
        return def.Type is BtlFieldType.Text or BtlFieldType.Blob ? hex : $"{FormatValue(def, raw, bytes)} ({hex})";
    }

    /// <summary>解码一个字段（值 / 十六进制 / 二进制 / 含义）。</summary>
    public BtlFieldValue DecodeField(BtlFieldDef def, long recordStart)
    {
        long offset = recordStart + def.Offset;
        if (offset < 0 || offset + def.Size > Data.LongLength)
            return new BtlFieldValue(def, offset, 0, Loc.T("DataCheck.Value.OutOfFile"), "", "", "");

        long raw = ReadRaw(def, recordStart);
        var bytes = Data.AsSpan((int)offset, def.Size);
        return new BtlFieldValue(def, offset, raw, FormatValue(def, raw, bytes), FormatHex(def, raw, bytes),
                                 FormatBinary(def, raw), DescribeValue(def.Meaning, raw, def.Size));
    }

    private static string FormatValue(BtlFieldDef def, long raw, ReadOnlySpan<byte> bytes) => def.Type switch
    {
        BtlFieldType.F32 => BitConverter.Int32BitsToSingle((int)raw).ToString("G7", CultureInfo.InvariantCulture),
        BtlFieldType.Rgb => $"{bytes[0]},{bytes[1]},{bytes[2]}",
        BtlFieldType.Text => AsciiText(bytes),
        BtlFieldType.Blob => Loc.F("DataCheck.Value.Bytes", bytes.Length),
        _ => raw.ToString(CultureInfo.InvariantCulture)
    };

    private static string FormatHex(BtlFieldDef def, long raw, ReadOnlySpan<byte> bytes) => def.Type switch
    {
        BtlFieldType.Rgb => $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}",
        BtlFieldType.Text or BtlFieldType.Blob or BtlFieldType.Varint => HexBytes(bytes, MaxBlobHexBytes),
        _ => "0x" + (raw & SizeMask(def.Size)).ToString("X" + def.Size * 2, CultureInfo.InvariantCulture)
    };

    private static string FormatBinary(BtlFieldDef def, long raw) => def.Type switch
    {
        BtlFieldType.U8 or BtlFieldType.I8 => Convert.ToString(raw & 0xFF, 2).PadLeft(8, '0'),
        BtlFieldType.U16 or BtlFieldType.I16 => Insert8(Convert.ToString(raw & 0xFFFF, 2).PadLeft(16, '0')),
        _ => ""
    };

    private static string Insert8(string bits) => bits[..8] + " " + bits[8..];

    private static long SizeMask(int size) => size >= 8 ? -1L : (1L << (size * 8)) - 1;

    /// <summary>字节序列的十六进制文本（超过上限时截断并加省略号）。</summary>
    public static string HexBytes(ReadOnlySpan<byte> bytes, int maxBytes)
    {
        int shown = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder(shown * 3 + 2);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (bytes.Length > shown) sb.Append(" …");
        return sb.ToString();
    }

    private static string AsciiText(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '.';
        return new string(chars);
    }

    // ============================================================= 含义 =============================================================

    private string DescribeValue(BtlValueKind kind, long raw, int size) => kind switch
    {
        BtlValueKind.None => "",
        BtlValueKind.Coordinate => DescribeCoordinate(raw, size),
        BtlValueKind.ProvinceValue => raw == 0xFFFF
            ? Loc.T("DataCheck.Value.NoProvince")
            : Loc.F("DataCheck.Value.ProvinceCapital", CellText(CoordinateToCell(raw))),
        BtlValueKind.BelongValue => raw == 0xFF ? Loc.T("DataCheck.Value.NoOwner") : Loc.F("DataCheck.Value.LegionIndex", raw),
        BtlValueKind.CityName => raw is 0 or 0xFFFF ? Loc.T("DataCheck.Value.NoName") : "",
        _ => EnumName(kind, raw)
    };

    private string DescribeCoordinate(long raw, int size)
    {
        bool sentinel = raw == -1 || (size == 2 && raw == 0xFFFF);
        if (sentinel) return Loc.T("DataCheck.Value.NoCoordinate");
        var cell = CoordinateToCell(raw);
        return cell == null ? Loc.T("DataCheck.Value.OutOfMap") : CellText(cell);
    }

    /// <summary>枚举值的显示名（翻译键 DataCheck.Enum.&lt;种类&gt;.&lt;十进制值&gt;；没有定义的值显示"未知值"）。</summary>
    public static string EnumName(BtlValueKind kind, long raw)
    {
        string key = "DataCheck.Enum." + kind + "." + raw.ToString(CultureInfo.InvariantCulture);
        return Loc.Has(key) ? Loc.T(key) : Loc.F("DataCheck.Enum.UnknownValue", raw);
    }

    /// <summary>(列,行) 文本；格子不存在时为 "—"。</summary>
    public static string CellText((int Col, int Row)? cell) => cell is { } c ? $"({c.Col},{c.Row})" : "—";

    // ============================================================= 列表标签 =============================================================

    /// <summary>记录在列表里的一行标签（以 #序号 开头，保证每行文本唯一）。</summary>
    public string DescribeRecord(BtlBlock block, int index)
    {
        var segment = GetSegment(block);
        if (segment == null || index < 0 || index >= segment.ReadableCount)
            return "#" + index.ToString(CultureInfo.InvariantCulture);

        long s = segment.RecordOffset(index);
        return block switch
        {
            BtlBlock.Legion => Loc.F("DataCheck.Item.Legion", index, I32At(s + 4), I32At(s)),
            BtlBlock.Terrain => Loc.F("DataCheck.Item.Terrain", index, CellText(CoordinateToCell(index)), Data[s], Data[s + 4], Data[s + 8]),
            BtlBlock.Province => Loc.F("DataCheck.Item.Value", index, CellText(CoordinateToCell(index)), ValueOrDash(U16At(s), 0xFFFF)),
            BtlBlock.Belong => Loc.F("DataCheck.Item.Value", index, CellText(BelongIndexToCell(index)), ValueOrDash(Data[s], 0xFF)),
            BtlBlock.Building => Loc.F("DataCheck.Item.Building", index, CellText(CoordinateToCell(U16At(s))), Data[s + 4],
                                       U16At(s + 2) is 0 or 0xFFFF ? "—" : U16At(s + 2).ToString(CultureInfo.InvariantCulture)),
            BtlBlock.Army => Loc.F("DataCheck.Item.Army", index, CellText(CoordinateToCell(U16At(s))), Data[s + 2], Data[s + 3]),
            BtlBlock.Trap => Loc.F("DataCheck.Item.Trap", index, CellText(CoordinateToCell(U16At(s))), (short)U16At(s + 2)),
            BtlBlock.Case => Loc.F("DataCheck.Item.Case", index, I32At(s), I32At(s + 8)),
            BtlBlock.Weather => Loc.F("DataCheck.Item.Weather", index, EnumName(BtlValueKind.WeatherType, I32At(s)), I32At(s + 8), I32At(s + 12)),
            BtlBlock.Event => Loc.F("DataCheck.Item.Event", index, I32At(s), EnumName(BtlValueKind.TriggerCondition, I32At(s + 8))),
            BtlBlock.Reinforcement => Loc.F("DataCheck.Item.Reinforcement", index, CellText(CoordinateToCell(I32At(s))), I32At(s + 4), I32At(s + 0x4C)),
            BtlBlock.AirRaid => Loc.F("DataCheck.Item.AirRaid", index, CellText(CoordinateToCell(I32At(s))),
                                      EnumName(BtlValueKind.AirUnitType, I32At(s + 4)), I32At(s + 16)),
            BtlBlock.UnitPlacement => Loc.F("DataCheck.Item.UnitPlacement", index, CellText(CoordinateToCell(I32At(s))), Data[s + 5]),
            BtlBlock.Capital => Loc.F("DataCheck.Item.Capital", index, CellText(CoordinateToCell(I32At(s)))),
            BtlBlock.Strategy => Loc.F("DataCheck.Item.Strategy", index, I32At(s), I32At(s + 12)),
            BtlBlock.AirSupport => Loc.F("DataCheck.Item.AirSupport", index, EnumName(BtlValueKind.AirUnitType, I32At(s)), I32At(s + 8), I32At(s + 12)),
            _ => Loc.F("DataCheck.Item.Blob", index, segment.RecordSize)
        };
    }

    private static string ValueOrDash(int value, int none) => value == none ? "—" : value.ToString(CultureInfo.InvariantCulture);

    private int I32At(long offset) => BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan((int)offset, 4));

    private int U16At(long offset) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan((int)offset, 2));
}
