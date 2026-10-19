using System.Buffers.Binary;
using System.Globalization;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Analyzers;

// BtlInspector：尾部扩展数据块（头部 0x54 给出长度）的通用解码。
// 资源里的扩展块都是 protobuf 线格式（tag + varint / 长度前缀子消息），这里不假设具体的消息定义，
// 只按线格式逐层展开，让人能看出"哪个字段号、什么类型、什么值、在文件哪个位置"。
public sealed partial class BtlInspector
{
    private const int MaxExtensionRows = 4000;
    private const int MaxExtensionDepth = 6;
    private const ulong MaxFieldNumber = 536_870_911;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private IReadOnlyList<BtlFieldValue> DecodeExtension(long start, int length)
    {
        long end = Math.Min(start + length, Data.LongLength);
        var rows = new List<BtlFieldValue>();

        if (end > start && ParseMessage(start, end, start, "", 0, null))
        {
            ParseMessage(start, end, start, "", 0, rows);
            return rows;
        }

        // 不是合法的 protobuf：整块按原始字节显示，并注明
        var def = new BtlFieldDef(0, BtlFieldType.Blob, "ExtensionRaw", (int)(end - start));
        var bytes = Data.AsSpan((int)start, (int)(end - start));
        rows.Add(new BtlFieldValue(def, start, 0, Loc.F("DataCheck.Value.Bytes", bytes.Length), HexBytes(bytes, MaxBlobHexBytes), "",
                                   Loc.T("DataCheck.Value.NotProtobuf")));
        return rows;
    }

    /// <summary>
    /// 按 protobuf 线格式解析 [pos, end)。<paramref name="rows"/> 为 null 时只做合法性检查。
    /// 返回 false 表示这段字节不是完整合法的消息。
    /// </summary>
    private bool ParseMessage(long pos, long end, long blockStart, string prefix, int depth, List<BtlFieldValue>? rows)
    {
        while (pos < end)
        {
            long tagStart = pos;
            if (!TryReadVarint(ref pos, end, out ulong tag)) return false;

            ulong fieldNumber = tag >> 3;
            int wireType = (int)(tag & 7);
            if (fieldNumber == 0 || fieldNumber > MaxFieldNumber) return false;

            string path = prefix.Length == 0
                ? fieldNumber.ToString(CultureInfo.InvariantCulture)
                : prefix + "." + fieldNumber.ToString(CultureInfo.InvariantCulture);

            switch (wireType)
            {
                case 0:
                {
                    long valueStart = pos;
                    if (!TryReadVarint(ref pos, end, out ulong value)) return false;
                    long signed = (long)(value >> 1) ^ -(long)(value & 1);
                    rows?.Add(ProtoRow(path, "Varint", tagStart, pos, blockStart, value.ToString(CultureInfo.InvariantCulture),
                                       HexBytes(Data.AsSpan((int)valueStart, (int)(pos - valueStart)), MaxBlobHexBytes),
                                       signed < 0 ? Loc.F("DataCheck.Value.ZigZag", signed) : ""));
                    break;
                }
                case 1:
                {
                    if (end - pos < 8) return false;
                    ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan((int)pos, 8));
                    pos += 8;
                    rows?.Add(ProtoRow(path, "Fixed64", tagStart, pos, blockStart, value.ToString(CultureInfo.InvariantCulture),
                                       "0x" + value.ToString("X16", CultureInfo.InvariantCulture),
                                       BitConverter.Int64BitsToDouble((long)value).ToString("G10", CultureInfo.InvariantCulture)));
                    break;
                }
                case 2:
                {
                    if (!TryReadVarint(ref pos, end, out ulong length) || length > (ulong)(end - pos)) return false;
                    long payloadStart = pos;
                    long payloadEnd = pos + (long)length;
                    bool nested = depth < MaxExtensionDepth && length > 0
                                  && ParseMessage(payloadStart, payloadEnd, blockStart, path, depth + 1, null);
                    if (rows != null)
                    {
                        var payload = Data.AsSpan((int)payloadStart, (int)length);
                        if (nested)
                        {
                            rows.Add(ProtoRow(path, "Message", tagStart, payloadEnd, blockStart,
                                              Loc.F("DataCheck.Value.Message", length), HexBytes(payload, MaxBlobHexBytes), ""));
                            ParseMessage(payloadStart, payloadEnd, blockStart, path, depth + 1, rows);
                        }
                        else
                        {
                            string? text = TryText(payload);
                            rows.Add(ProtoRow(path, "Bytes", tagStart, payloadEnd, blockStart,
                                              text ?? Loc.F("DataCheck.Value.Bytes", length), HexBytes(payload, MaxBlobHexBytes), ""));
                        }
                    }
                    pos = payloadEnd;
                    break;
                }
                case 5:
                {
                    if (end - pos < 4) return false;
                    uint value = BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan((int)pos, 4));
                    pos += 4;
                    rows?.Add(ProtoRow(path, "Fixed32", tagStart, pos, blockStart, value.ToString(CultureInfo.InvariantCulture),
                                       "0x" + value.ToString("X8", CultureInfo.InvariantCulture),
                                       BitConverter.Int32BitsToSingle((int)value).ToString("G7", CultureInfo.InvariantCulture)));
                    break;
                }
                default:
                    return false;
            }

            if (rows != null && rows.Count >= MaxExtensionRows)
            {
                rows.Add(new BtlFieldValue(new BtlFieldDef((int)(pos - blockStart), BtlFieldType.Blob, "ExtensionMore", 0),
                                           pos, 0, Loc.T("DataCheck.Value.RowsTruncated"), "", "", ""));
                return true;
            }
        }
        return pos == end;
    }

    private BtlFieldValue ProtoRow(string path, string wireType, long start, long end, long blockStart,
                                   string value, string hex, string meaning)
    {
        var def = new BtlFieldDef((int)(start - blockStart), BtlFieldType.Varint, "ProtoField", (int)(end - start),
                                  nameArgs: new object[] { path, Loc.T("DataCheck.Proto." + wireType) });
        return new BtlFieldValue(def, start, 0, value, hex, "", meaning);
    }

    private bool TryReadVarint(ref long pos, long end, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && pos < end; shift += 7)
        {
            byte b = Data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    /// <summary>可打印的 UTF-8 文本返回字符串，否则返回 null。</summary>
    private static string? TryText(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return null;
        try
        {
            string text = StrictUtf8.GetString(payload);
            return text.Any(char.IsControl) ? null : "\"" + text + "\"";
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
