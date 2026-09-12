using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 省份数据解析模块。
/// </summary>
public static class BTLProvinceModule
{
    private const int ProvinceSize = 2;

    public static List<Province> Parse(byte[] data, int startOffset, int count)
    {
        if (data == null) return new List<Province>();

        var provinces = new List<Province>(count);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * ProvinceSize);
            if (startPos + ProvinceSize > data.Length) break;

            provinces.Add(Province.FromBytes(data, startPos));
        }

        Debug.WriteLine($"[BTLProvinceModule] 解析 {provinces.Count} 个省份");
        return provinces;
    }

    public static int CalculateEndOffset(int startOffset, int count)
        => startOffset + (count * ProvinceSize);
}
