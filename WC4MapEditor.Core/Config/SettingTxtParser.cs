using System.Diagnostics;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 专门负责解析 setting.txt 文件的解析器
/// ConfigManager 读取文件后，将内容交给此类解析
/// </summary>
public static class SettingTxtParser
{
    /// <summary>
    /// 解析 setting.txt 的所有行，返回解析结果
    /// </summary>
    public static SettingTxtData Parse(string[] lines)
    {
        var data = new SettingTxtData();
        var sectionContext = new SectionContext();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            // 检测 section 切换
            if (TrySwitchSection(line, sectionContext)) continue;

            // 解析键值对
            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            string key = line.Substring(0, eqIdx).Trim();
            string value = line.Substring(eqIdx + 1).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];

            // 所有键值对都存入文本配置字典
            data.TextConfig[key] = value;

            // 根据当前 section 进行专项解析
            ParseBySection(key, value, sectionContext, data);
        }

        Debug.WriteLine($"[SettingTxtParser] 解析完成: {data.TextConfig.Count} 个文本配置项");
        return data;
    }

    private static bool TrySwitchSection(string line, SectionContext ctx)
    {
        if (!line.StartsWith('[') || !line.EndsWith(']')) return false;

        ctx.Reset();

        switch (line)
        {
            case "[地形修改]": ctx.Current = SectionType.Terrain; break;
            case "[建筑修改]": ctx.Current = SectionType.Building; ctx.BuildingConfig = new BuildingConfig(); break;
            case "[编辑单位]": ctx.Current = SectionType.Army; ctx.ArmyEditConfig = new ArmyEditConfig(); break;
            case "[编辑军团]": ctx.Current = SectionType.Legion; ctx.LegionEditConfig = new LegionEditConfig(); break;
            case "[词云一言]": ctx.Current = SectionType.WordCloud; ctx.WordCloudConfig = new WordCloudConfig(); break;
            case "[事件修改]": ctx.Current = SectionType.Event; ctx.EventEditConfig = new EventEditConfig(); break;
            case "[空袭修改]": ctx.Current = SectionType.AirForce; ctx.AirForceEditConfig = new AirForceEditConfig(); break;
            case "[方针修改]": ctx.Current = SectionType.Case; ctx.CaseEditConfig = new CaseEditConfig(); break;
            case "[天气修改]": ctx.Current = SectionType.Weather; ctx.WeatherEditConfig = new WeatherEditConfig(); break;
            default: ctx.Current = SectionType.Unknown; break;
        }

        return true;
    }

    private static void ParseBySection(string key, string value, SectionContext ctx, SettingTxtData data)
    {
        switch (ctx.Current)
        {
            case SectionType.Terrain:
                if (key.StartsWith("ColorToTerrain"))
                    data.ColorToTerrainConfig = ColorToTerrainParser.Parse(value);
                break;

            case SectionType.Building:
                ParseBuildingConfig(key, value, ctx);
                break;

            case SectionType.Army:
                ParseArmyEditConfig(key, value, ctx);
                break;

            case SectionType.Legion:
                ParseLegionEditConfig(key, value, ctx);
                break;

            case SectionType.WordCloud:
                ParseWordCloudConfig(key, value, ctx);
                break;

            case SectionType.Event:
                // EventEditConfig 当前为空实现
                break;

            case SectionType.AirForce:
                // AirForceEditConfig 当前为空实现
                break;

            case SectionType.Case:
                // CaseEditConfig 当前为空实现
                break;

            case SectionType.Weather:
                // WeatherEditConfig 当前为空实现
                break;
        }

        // 国家颜色定义可以在任何 section 中（实际在[国家颜色定义] section）
        if (key.StartsWith("country_color_"))
            ParseCountryColor(key, value, data);
    }

    private static void ParseBuildingConfig(string key, string value, SectionContext ctx)
    {
        if (ctx.BuildingConfig == null) return;
        switch (key)
        {
            case "BuildingRandomCharacter": ctx.BuildingConfig.BuildingRandomCharacter = ParseIntList(value); break;
            case "LowStrengthBuilding": ctx.BuildingConfig.LowStrengthBuilding = ParseIntList(value); break;
            case "MediumStrengthBuilding": ctx.BuildingConfig.MediumStrengthBuilding = ParseIntList(value); break;
            case "HighStrengthBuilding": ctx.BuildingConfig.HighStrengthBuilding = ParseIntList(value); break;
            case "CommonBuilding": ctx.BuildingConfig.CommonBuilding = ParseIntList(value); break;
            case "BuidingOnSea":
            case "BuildingOnSea": ctx.BuildingConfig.BuildingOnSea = ParseIntList(value); break;
            case "BuildingNeedDirection": ctx.BuildingConfig.BuildingNeedDirection = ParseIntList(value); break;
        }
    }

    private static void ParseArmyEditConfig(string key, string value, SectionContext ctx)
    {
        if (ctx.ArmyEditConfig == null) return;
        switch (key)
        {
            case "LowStrengthArmy": ctx.ArmyEditConfig.LowStrengthArmy = ParseIntList(value); break;
            case "MediumStrengthArmy": ctx.ArmyEditConfig.MediumStrengthArmy = ParseIntList(value); break;
            case "HighStrengthArmy": ctx.ArmyEditConfig.HighStrengthArmy = ParseIntList(value); break;
            case "ArmyOnLand": ctx.ArmyEditConfig.ArmyOnLand = ParseIntList(value); break;
            case "ArmyOnSea": ctx.ArmyEditConfig.ArmyOnSea = ParseIntList(value); break;
            case "Infantry": ctx.ArmyEditConfig.Infantry = ParseIntList(value); break;
            case "Armor": ctx.ArmyEditConfig.Armor = ParseIntList(value); break;
            case "Artillery": ctx.ArmyEditConfig.Artillery = ParseIntList(value); break;
            case "Navy": ctx.ArmyEditConfig.Navy = ParseIntList(value); break;
            case "AirForce": ctx.ArmyEditConfig.AirForce = ParseIntList(value); break;
        }
    }

    private static void ParseLegionEditConfig(string key, string value, SectionContext ctx)
    {
        if (ctx.LegionEditConfig == null) return;
        switch (key)
        {
            case "LowStrengthGift": ctx.LegionEditConfig.LowStrengthGift = ParseGiftList(value); break;
            case "MediumStrengthGift": ctx.LegionEditConfig.MediumStrengthGift = ParseGiftList(value); break;
            case "HighStrengthGift": ctx.LegionEditConfig.HighStrengthGift = ParseGiftList(value); break;
            case "LowStrengthCountry": ctx.LegionEditConfig.LowStrengthCountry = ParseIntList(value); break;
            case "MediumStrengthCountry": ctx.LegionEditConfig.MediumStrengthCountry = ParseIntList(value); break;
            case "HighStrengthCountry": ctx.LegionEditConfig.HighStrengthCountry = ParseIntList(value); break;
        }
    }

    private static void ParseWordCloudConfig(string key, string value, SectionContext ctx)
    {
        if (ctx.WordCloudConfig == null) return;
        switch (key)
        {
            case "Main_Token": ctx.WordCloudConfig.MainToken = value; break;
            case "tokens_num":
                if (int.TryParse(value, out int n)) ctx.WordCloudConfig.TokensNum = n;
                break;
            case "animation_keep":
                if (int.TryParse(value, out int k)) ctx.WordCloudConfig.AnimationKeep = k;
                break;
            default:
                if (key.StartsWith("token_") && int.TryParse(key["token_".Length..], out int idx) && idx >= 1 && idx <= 30)
                    ctx.WordCloudConfig.Tokens[idx - 1] = value;
                break;
        }
    }

    private static void ParseCountryColor(string key, string value, SettingTxtData data)
    {
        try
        {
            string numPart = key["country_color_".Length..];
            if (int.TryParse(numPart, out int countryId))
            {
                string cs = value.Trim();
                int colorVal = cs.StartsWith("0x") ? Convert.ToInt32(cs, 16) : int.Parse(cs);
                data.CountryColors[countryId] = colorVal;
            }
        }
        catch { /* 忽略解析错误 */ }
    }

    private static List<int> ParseIntList(string value)
    {
        var result = new List<int>();
        try
        {
            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                value = value[1..^1];
                foreach (var part in value.Split(','))
                    if (int.TryParse(part.Trim(), out int n)) result.Add(n);
            }
        }
        catch { }
        return result;
    }

    private static List<List<(int Min, int Max)>> ParseGiftList(string value)
    {
        var result = new List<List<(int, int)>>();
        try
        {
            if (!value.StartsWith('[')) return result;
            value = value[1..^1];
            var tuples = new List<string>();
            int depth = 0;
            string current = "";
            foreach (char c in value)
            {
                if (c == '(') { depth++; current += c; }
                else if (c == ')') { depth--; current += c; if (depth == 0) { tuples.Add(current.Trim()); current = ""; } }
                else if (depth > 0) current += c;
            }
            foreach (var tuple in tuples)
            {
                if (!tuple.StartsWith('(')) continue;
                var nums = tuple[1..^1].Split(',');
                if (nums.Length >= 2)
                    result.Add(nums.Select(n => (Min: int.Parse(n.Trim()), Max: 0)).ToList());
            }
        }
        catch { }
        return result;
    }

    // Section 类型枚举
    private enum SectionType
    {
        Unknown,
        Terrain,
        Building,
        Army,
        Legion,
        WordCloud,
        Event,
        AirForce,
        Case,
        Weather
    }

    /// <summary>
    /// 解析过程中的 section 上下文状态
    /// </summary>
    private class SectionContext
    {
        public SectionType Current { get; set; } = SectionType.Unknown;
        public BuildingConfig? BuildingConfig { get; set; }
        public ArmyEditConfig? ArmyEditConfig { get; set; }
        public LegionEditConfig? LegionEditConfig { get; set; }
        public WordCloudConfig? WordCloudConfig { get; set; }
        public EventEditConfig? EventEditConfig { get; set; }
        public AirForceEditConfig? AirForceEditConfig { get; set; }
        public CaseEditConfig? CaseEditConfig { get; set; }
        public WeatherEditConfig? WeatherEditConfig { get; set; }

        public void Reset()
        {
            Current = SectionType.Unknown;
        }
    }
}

/// <summary>
/// setting.txt 解析后的完整数据
/// </summary>
public class SettingTxtData
{
    /// <summary>所有文本键值对配置</summary>
    public Dictionary<string, string> TextConfig { get; set; } = new();

    /// <summary>地形颜色映射配置</summary>
    public ColorToTerrainConfig ColorToTerrainConfig { get; set; } = new();

    /// <summary>建筑配置</summary>
    public BuildingConfig BuildingConfig { get; set; } = new();

    /// <summary>单位编辑配置</summary>
    public ArmyEditConfig ArmyEditConfig { get; set; } = new();

    /// <summary>军团编辑配置</summary>
    public LegionEditConfig LegionEditConfig { get; set; } = new();

    /// <summary>词云配置</summary>
    public WordCloudConfig WordCloudConfig { get; set; } = new();

    /// <summary>事件编辑配置</summary>
    public EventEditConfig EventEditConfig { get; set; } = new();

    /// <summary>空袭编辑配置</summary>
    public AirForceEditConfig AirForceEditConfig { get; set; } = new();

    /// <summary>方针编辑配置</summary>
    public CaseEditConfig CaseEditConfig { get; set; } = new();

    /// <summary>天气编辑配置</summary>
    public WeatherEditConfig WeatherEditConfig { get; set; } = new();

    /// <summary>国家颜色映射</summary>
    public Dictionary<int, int> CountryColors { get; set; } = new();
}

// 配置数据类定义（从 ConfigManager 移出，保持独立）

public class BuildingConfig
{
    public List<int> BuildingRandomCharacter { get; set; } = [];
    public List<int> LowStrengthBuilding { get; set; } = [];
    public List<int> MediumStrengthBuilding { get; set; } = [];
    public List<int> HighStrengthBuilding { get; set; } = [];
    public List<int> CommonBuilding { get; set; } = [];
    public List<int> BuildingOnSea { get; set; } = [];
    public List<int> BuildingNeedDirection { get; set; } = [];
}

public class ArmyEditConfig
{
    public List<int> LowStrengthArmy { get; set; } = [];
    public List<int> MediumStrengthArmy { get; set; } = [];
    public List<int> HighStrengthArmy { get; set; } = [];
    public List<int> ArmyOnLand { get; set; } = [];
    public List<int> ArmyOnSea { get; set; } = [];
    public List<int> Infantry { get; set; } = [];
    public List<int> Armor { get; set; } = [];
    public List<int> Artillery { get; set; } = [];
    public List<int> Navy { get; set; } = [];
    public List<int> AirForce { get; set; } = [];
}

public class LegionEditConfig
{
    public List<List<(int, int)>> LowStrengthGift { get; set; } = [];
    public List<List<(int, int)>> MediumStrengthGift { get; set; } = [];
    public List<List<(int, int)>> HighStrengthGift { get; set; } = [];
    public List<int> LowStrengthCountry { get; set; } = [];
    public List<int> MediumStrengthCountry { get; set; } = [];
    public List<int> HighStrengthCountry { get; set; } = [];
}

public class WordCloudConfig
{
    public string MainToken { get; set; } = "";
    public int TokensNum { get; set; } = 30;
    public int AnimationKeep { get; set; } = 5;
    public string[] Tokens { get; set; } = new string[30];
}

public class EventEditConfig { }
public class AirForceEditConfig { }
public class CaseEditConfig { }
public class WeatherEditConfig { }