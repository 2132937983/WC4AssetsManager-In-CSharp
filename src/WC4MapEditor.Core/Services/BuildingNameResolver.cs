using System.Diagnostics;
using System.Globalization;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 建筑名称解析：把用户输入（命令行 / 脚本 / 编辑窗口）统一转成建筑名称 ID，也负责按 ID 查城市名。
/// <para>
/// 名称 ID 是无符号 16 位（0-65535，0xFFFF 表示无名称）。
/// </para>
/// <para>
/// 此前这段逻辑在 CliCommandHost.ParseBuildingName 与
/// BuildingSettingWindow.ParseNameField 里各写了一份，行为容易走偏，这里统一收口。
/// </para>
/// </summary>
public static class BuildingNameResolver
{
    /// <summary>无名称的名称 ID</summary>
    public const ushort NoName = 0xFFFF;

    private const string CityNameKeyPrefix = "battle_cityname_";

    /// <summary>
    /// 解析建筑名称，输入无效时退化为无名称（脚本 / 批处理不因一次命名失败而中断）。
    /// 需要把错误告诉用户的地方（编辑窗口、命令行）请用 <see cref="TryResolve"/>。
    /// </summary>
    public static ushort Resolve(string? input)
        => TryResolve(input, out ushort nameId, out _) ? nameId : NoName;

    /// <summary>
    /// 解析建筑名称：
    /// <list type="bullet">
    /// <item>空 / 空白 → 0xFFFF（无名称）</item>
    /// <item>数字 → 0-65535 原样作为名称 ID；沿用旧习惯输入 -1 时按 0xFFFF 处理；其它超出范围的数字视为无效
    /// （以前按 16 位截断：70000 会变成 4464，-5 会变成 65531）</item>
    /// <item>其它 → 查城市名称表，查不到则新建条目并保存</item>
    /// </list>
    /// </summary>
    /// <returns>解析成功返回 true；否则 <paramref name="error"/> 给出原因（已本地化）。</returns>
    public static bool TryResolve(string? input, out ushort nameId, out string? error)
    {
        nameId = NoName;
        error = null;
        if (string.IsNullOrWhiteSpace(input)) return true;

        var text = input.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
        {
            if (number == -1) return true;
            if (number < 0 || number > ushort.MaxValue)
            {
                error = Loc.F("Module.Building.Name.NumberOutOfRange", text);
                return false;
            }
            nameId = (ushort)number;
            return true;
        }

        // 视为城市名称。名称表可能尚未加载（配置缺失、脚本独立运行等），失败时如实报告，由调用方决定怎么处理。
        try
        {
            var parser = ConfigManager.Instance.GetStringTableParser();

            var existingId = parser.FindCityIdByName(text);
            if (existingId.HasValue)
                return TryToNameId(existingId.Value, text, out nameId, out error);

            int newId = parser.AddOrUpdateCityName(text);
            if (!TryToNameId(newId, text, out nameId, out error)) return false;

            parser.Save();
            ConfigManager.Instance.ReloadStringTable();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingNameResolver] 解析名称「{text}」失败: {ex.Message}");
            error = Loc.F("Module.Building.Name.CreateFailed", text);
            return false;
        }
    }

    /// <summary>按名称 ID 查城市名（battle_cityname_XXX）；无名称或查不到时返回 null。</summary>
    public static string? GetCityName(int nameId)
    {
        if (nameId == 0 || nameId == NoName) return null;
        try
        {
            var parser = ConfigManager.Instance.GetStringTableParser();
            string padded = parser.GetValue(CityNameKeyPrefix + nameId.ToString("D3", CultureInfo.InvariantCulture));
            if (padded.Length > 0) return padded;
            string plain = parser.GetValue(CityNameKeyPrefix + nameId.ToString(CultureInfo.InvariantCulture));
            return plain.Length > 0 ? plain : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingNameResolver] 查询城市名 {nameId} 失败: {ex.Message}");
            return null;
        }
    }

    private static bool TryToNameId(int cityId, string text, out ushort nameId, out string? error)
    {
        nameId = NoName;
        error = null;
        if (cityId < 0 || cityId >= NoName)
        {
            error = Loc.F("Module.Building.Name.CreateFailed", text);
            return false;
        }
        nameId = (ushort)cityId;
        return true;
    }
}
