using System.Diagnostics;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 专门解析 setting.txt 中 ColorToTerrain 配置的解析器
/// 被 SettingTxtParser 调用，负责地形颜色映射的专项解析
/// 格式: [陆地噪点概率, [颜色代码, 地形1, 地形2...], [颜色代码, 地形1...]...]
/// </summary>
public static class ColorToTerrainParser
{
    public static ColorToTerrainConfig Parse(string value)
    {
        var config = new ColorToTerrainConfig();

        try
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('[') || !value.EndsWith(']'))
            {
                Debug.WriteLine("[ColorToTerrainParser] 配置格式错误，不是有效的数组格式");
                return config;
            }

            // 去掉外层方括号
            value = value[1..^1];

            // 按逗号分割，但要处理嵌套方括号
            var elements = SplitElements(value);

            if (elements.Count > 0)
            {
                if (int.TryParse(elements[0].Trim(), out int prob))
                    config.LandNoiseProbability = prob;
            }

            for (int i = 1; i < elements.Count; i++)
            {
                ParseMapping(elements[i].Trim(), config);
            }

            Debug.WriteLine($"[ColorToTerrainParser] 解析完成: 噪点概率={config.LandNoiseProbability}, 映射数量={config.ColorMappings.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ColorToTerrainParser] 解析失败: {ex.Message}");
        }

        return config;
    }

    /// <summary>
    /// 按逗号分割元素，但保留嵌套方括号内的内容
    /// </summary>
    private static List<string> SplitElements(string value)
    {
        var elements = new List<string>();
        int depth = 0;
        var current = new System.Text.StringBuilder();

        foreach (char c in value)
        {
            if (c == '[')
            {
                depth++;
                current.Append(c);
            }
            else if (c == ']')
            {
                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                elements.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            elements.Add(current.ToString());

        return elements;
    }

    private static void ParseMapping(string element, ColorToTerrainConfig config)
    {
        if (!element.StartsWith('[') || !element.EndsWith(']'))
            return;

        // 去掉方括号
        element = element[1..^1];
        var parts = element.Split(',');
        if (parts.Length < 2)
            return;

        var mapping = new ColorTerrainMapping();
        string colorStr = parts[0].Trim();

        // 解析颜色值（支持 0x 前缀的十六进制）
        if (colorStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(colorStr[2..], System.Globalization.NumberStyles.HexNumber, null, out int colorValue))
                mapping.ColorValue = colorValue;
            else
                return;
        }
        else if (int.TryParse(colorStr, out int colorValue2))
        {
            mapping.ColorValue = colorValue2;
        }
        else
        {
            return;
        }

        // 解析地形类型列表
        for (int j = 1; j < parts.Length; j++)
        {
            if (int.TryParse(parts[j].Trim(), out int terrainType))
                mapping.TerrainTypes.Add(terrainType);
        }

        if (mapping.TerrainTypes.Count > 0)
        {
            config.ColorMappings.Add(mapping);
            Debug.WriteLine($"[ColorToTerrainParser] 添加映射: 颜色=0x{mapping.ColorValue:X6}, 地形=[{string.Join(",", mapping.TerrainTypes)}]");
        }
    }
}

public class ColorToTerrainConfig
{
    public int LandNoiseProbability { get; set; } = 30;
    public List<ColorTerrainMapping> ColorMappings { get; set; } = [];
    public double MinRatio { get; set; } = 0.3;
}

public class ColorTerrainMapping
{
    public int ColorValue { get; set; }
    public List<int> TerrainTypes { get; set; } = [];
}