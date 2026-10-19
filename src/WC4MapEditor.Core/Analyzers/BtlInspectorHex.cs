using System.Buffers.Binary;
using System.Globalization;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>
/// 十六进制查看的通用工具：偏移解析、字节模式解析与查找、转储行格式化、按常见类型解读。
/// 数据检查场景的十六进制面板与 <c>inspect</c> 命令共用。
/// </summary>
public static class BtlInspectorHex
{
    public const int BytesPerLine = 16;

    /// <summary>
    /// 解析偏移。"0x" 前缀或 "h" 后缀按十六进制；含 A-F 字母的也按十六进制；其余按十进制。
    /// （VB 版不论有无前缀一律按十六进制解析，且失败时静默忽略；这里明确规则并把失败交给调用方提示。）
    /// </summary>
    public static bool TryParseOffset(string? text, out long offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim().Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        bool hex = false;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { s = s[2..]; hex = true; }
        else if (s.EndsWith('h') || s.EndsWith('H')) { s = s[..^1]; hex = true; }
        else if (s.Any(c => c is >= 'a' and <= 'f' or >= 'A' and <= 'F')) hex = true;

        if (s.Length == 0) return false;
        return hex
            ? long.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset) && offset >= 0
            : long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }

    /// <summary>解析格子坐标 "列,行"（可带括号，逗号可换成空格），如 "(27,2)"、"27 2"。</summary>
    public static bool TryParseCell(string? text, out int col, out int row)
    {
        col = row = -1;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Trim('(', ')', '（', '）')
                        .Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out col)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out row);
    }

    /// <summary>
    /// 解析搜索模式：十六进制字节序列（空格、短横、逗号可有可无，"??" 表示任意字节），
    /// 或用双引号括起来的文本（按 UTF-8 编码查找）。结果里 -1 表示通配。
    /// </summary>
    public static bool TryParsePattern(string? text, out short[] pattern)
    {
        pattern = Array.Empty<short>();
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            var bytes = Encoding.UTF8.GetBytes(s[1..^1]);
            if (bytes.Length == 0) return false;
            pattern = bytes.Select(b => (short)b).ToArray();
            return true;
        }

        string clean = new(s.Where(c => c is not (' ' or '-' or ',' or '\t')).ToArray());
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) clean = clean[2..];
        if (clean.Length == 0 || clean.Length % 2 != 0) return false;

        var result = new short[clean.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            string pair = clean.Substring(i * 2, 2);
            if (pair == "??") { result[i] = -1; continue; }
            if (!byte.TryParse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b)) return false;
            result[i] = b;
        }

        if (result.All(v => v < 0)) return false;
        pattern = result;
        return true;
    }

    /// <summary>模式的规范显示（"E9 00 ?? 00"）。</summary>
    public static string FormatPattern(short[] pattern)
        => string.Join(' ', pattern.Select(v => v < 0 ? "??" : v.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>
    /// 查找全部匹配的起始偏移（升序，最多 <paramref name="maxMatches"/> 个；超出时 <paramref name="truncated"/> 为 true）。
    /// 用第一个非通配字节做 IndexOf 定位，再逐字节比对。
    /// </summary>
    public static List<long> FindAll(byte[] data, short[] pattern, int maxMatches, out bool truncated)
    {
        var result = new List<long>();
        truncated = false;
        if (pattern.Length == 0 || data.Length < pattern.Length) return result;

        int anchor = Array.FindIndex(pattern, v => v >= 0);
        byte anchorByte = (byte)pattern[anchor];
        int lastStart = data.Length - pattern.Length;
        var span = data.AsSpan();

        int searchFrom = anchor;
        while (searchFrom < data.Length)
        {
            int rel = span[searchFrom..].IndexOf(anchorByte);
            if (rel < 0) break;

            int hit = searchFrom + rel;
            int start = hit - anchor;
            if (start > lastStart) break;

            if (Matches(span, start, pattern))
            {
                if (result.Count >= maxMatches) { truncated = true; break; }
                result.Add(start);
            }
            searchFrom = hit + 1;
        }
        return result;
    }

    private static bool Matches(ReadOnlySpan<byte> data, int start, short[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] >= 0 && data[start + i] != pattern[i]) return false;
        }
        return true;
    }

    /// <summary>经典转储行："地址  16 个字节（第 8 个后多一个空格）  |ASCII|"。</summary>
    public static string FormatLine(byte[] data, long lineStart)
    {
        var sb = new StringBuilder(80);
        sb.Append(lineStart.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
        int count = (int)Math.Clamp(data.LongLength - lineStart, 0, BytesPerLine);

        for (int i = 0; i < BytesPerLine; i++)
        {
            if (i == 8) sb.Append(' ');
            sb.Append(i < count ? data[lineStart + i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
        }

        sb.Append(" |");
        for (int i = 0; i < count; i++)
        {
            byte b = data[lineStart + i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        return sb.Append('|').ToString();
    }

    /// <summary>按常见类型解读某偏移处的字节（u8 / i8 / u16 / i16 / u32 / i32 / f32），长度不够的类型省略。</summary>
    public static IReadOnlyList<(string Type, string Value)> Interpret(byte[] data, long offset)
    {
        var list = new List<(string, string)>(7);
        if (offset < 0 || offset >= data.LongLength) return list;

        var span = data.AsSpan((int)offset);
        list.Add(("u8", span[0].ToString(CultureInfo.InvariantCulture)));
        list.Add(("i8", ((sbyte)span[0]).ToString(CultureInfo.InvariantCulture)));
        if (span.Length >= 2)
        {
            list.Add(("u16", BinaryPrimitives.ReadUInt16LittleEndian(span).ToString(CultureInfo.InvariantCulture)));
            list.Add(("i16", BinaryPrimitives.ReadInt16LittleEndian(span).ToString(CultureInfo.InvariantCulture)));
        }
        if (span.Length >= 4)
        {
            list.Add(("u32", BinaryPrimitives.ReadUInt32LittleEndian(span).ToString(CultureInfo.InvariantCulture)));
            list.Add(("i32", BinaryPrimitives.ReadInt32LittleEndian(span).ToString(CultureInfo.InvariantCulture)));
            list.Add(("f32", BinaryPrimitives.ReadSingleLittleEndian(span).ToString("G7", CultureInfo.InvariantCulture)));
        }
        return list;
    }
}
