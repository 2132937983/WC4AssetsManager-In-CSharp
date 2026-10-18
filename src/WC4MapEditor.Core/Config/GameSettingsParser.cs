using System.Diagnostics;
using System.Text.Json;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 玩法配置解析器：解析 assets/json 下的建筑表与兵种表。
/// <para>
/// 对应 Java 版的 <c>ResConfig.Config.DEF_WC4BUILD</c> 与 <c>DEF_WC4ARMY</c> ——
/// 校验「建筑类型是否合法」「兵种 ID 是否合法」时要以它们为准。
/// </para>
/// <para>
/// 与 <see cref="DefMapTerrainParser"/> 同样遵循职责划分：
/// 由 AssetManager 负责定位并读取 WC4DATA 内的文件，本类只负责解析语义。
/// </para>
/// </summary>
/// <remarks>
/// 已知字段（与手机端校验器读取的字段一致）：
/// <list type="bullet">
/// <item>BuildingSettings.json：每条的 <c>Id</c> 即建筑类型（buType）</item>
/// <item>ArmySettings.json：每条的 <c>Army</c> 即兵种 ID（baType）</item>
/// </list>
/// </remarks>
public static class GameSettingsParser
{
    /// <summary>建筑表在 assets 根目录下的相对路径</summary>
    public const string BuildingAssetPath = "json/BuildingSettings.json";

    /// <summary>兵种表在 assets 根目录下的相对路径</summary>
    public const string ArmyAssetPath = "json/ArmySettings.json";

    /// <summary>国家表在 assets 根目录下的相对路径</summary>
    public const string CountryAssetPath = "json/CountrySettings.json";

    /// <summary>兵种等级表在 assets 根目录下的相对路径</summary>
    public const string ArmyLevelAssetPath = "json/ArmyLevelSettings.json";

    /// <summary>将领军衔表在 assets 根目录下的相对路径</summary>
    public const string RankAssetPath = "json/GeneralLevelSettings.json";

    /// <summary>将领品质（HP 等级）表在 assets 根目录下的相对路径</summary>
    public const string QualityAssetPath = "json/GeneralQualitySettings.json";

    /// <summary>解析建筑表，返回合法的建筑类型（Id）集合</summary>
    public static HashSet<int> ParseBuildingIds(string jsonContent)
        => ParseIdSet(jsonContent, "Id", "建筑");

    /// <summary>解析兵种表，返回合法的兵种 ID（Army）集合</summary>
    public static HashSet<int> ParseArmyIds(string jsonContent)
        => ParseIdSet(jsonContent, "Army", "兵种");

    /// <summary>解析国家表，返回合法的国家 ID（Id）集合</summary>
    public static HashSet<int> ParseCountryIds(string jsonContent)
        => ParseIdSet(jsonContent, "Id", "国家");

    /// <summary>解析兵种等级表，返回合法的等级（Level）集合</summary>
    public static HashSet<int> ParseArmyLevels(string jsonContent)
        => ParseIdSet(jsonContent, "Level", "兵种等级");

    /// <summary>解析军衔表，返回合法的军衔（Id）集合</summary>
    public static HashSet<int> ParseRankIds(string jsonContent)
        => ParseIdSet(jsonContent, "Id", "军衔");

    /// <summary>解析将领品质表，返回合法的 HP 等级（Id）集合</summary>
    public static HashSet<int> ParseQualityIds(string jsonContent)
        => ParseIdSet(jsonContent, "Id", "将领品质");

    /// <summary>
    /// 从 JSON 数组里取出指定字段的整数值集合。
    /// 内容为空或解析失败时返回空集合 —— 调用方据此跳过该项校验，
    /// 而不是把「取不到配置」当成「所有类型都非法」。
    /// </summary>
    private static HashSet<int> ParseIdSet(string jsonContent, string fieldName, string label)
    {
        var result = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            Debug.WriteLine($"[GameSettingsParser] {label}配置内容为空");
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Debug.WriteLine($"[GameSettingsParser] {label}配置不是数组格式");
                return result;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty(fieldName, out var field)) continue;

                // 兼容数字与字符串两种写法
                if (field.ValueKind == JsonValueKind.Number)
                {
                    if (field.TryGetInt32(out int id)) result.Add(id);
                }
                else if (field.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(field.GetString(), out int id)) result.Add(id);
                }
            }

            Debug.WriteLine($"[GameSettingsParser] 解析到 {result.Count} 个合法{label}（字段 {fieldName}）");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameSettingsParser] 解析{label}配置失败: {ex.Message}");
        }

        return result;
    }
}
