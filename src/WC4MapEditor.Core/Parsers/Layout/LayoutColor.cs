using System.Globalization;

namespace WC4MapEditor.Core.Parsers.Layout;

/// <summary>
/// 布局里的颜色值解析。
/// <para>
/// 游戏写法是 <c>"r,g,b,a"</c> 四个 0~255 的整数（如 <c>255,232,140,255</c>、<c>255,255,255,0</c>），
/// 也兼容 <c>#RRGGBB</c> / <c>#RRGGBBAA</c> 与 0~1 的小数写法。
/// 注意 alpha 与 rgb 同一量纲：只要任一分量大于 1 就按 0~255 处理（旧版把 alpha 单独按 0~1 乘 255，
/// 导致 <c>…,128</c> 这类半透明值被钳到 255）。
/// </para>
/// </summary>
public static class LayoutColor
{
    public static bool TryParse(string? value, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = a = 255;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();

        if (v.StartsWith('#'))
        {
            var h = v[1..];
            if (h.Length == 6 && Hex(h[..2], out r) && Hex(h[2..4], out g) && Hex(h[4..6], out b)) { a = 255; return true; }
            if (h.Length == 8 && Hex(h[..2], out r) && Hex(h[2..4], out g) && Hex(h[4..6], out b) && Hex(h[6..8], out a)) return true;
            return false;
        }

        var parts = v.Split(',');
        if (parts.Length < 3) return false;
        var nums = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out nums[i])) return false;
        }

        bool byte255 = nums.Any(n => n > 1);
        double scale = byte255 ? 1 : 255;
        r = Clamp(nums[0] * scale);
        g = Clamp(nums[1] * scale);
        b = Clamp(nums[2] * scale);
        a = nums.Length > 3 ? Clamp(nums[3] * scale) : (byte)255;
        return true;
    }

    /// <summary>按游戏写法格式化："r,g,b,a"。</summary>
    public static string Format(byte r, byte g, byte b, byte a) => $"{r},{g},{b},{a}";

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    private static bool Hex(string s, out byte value)
        => byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
