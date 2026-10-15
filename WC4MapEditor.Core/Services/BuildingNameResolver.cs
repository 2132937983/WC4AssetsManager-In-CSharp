using System.Diagnostics;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 建筑名称解析：把用户输入（命令行 / 脚本 / 编辑窗口）统一转成建筑名称 ID。
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

    /// <summary>
    /// 解析建筑名称：
    /// <list type="bullet">
    /// <item>空 / 空白 → 0xFFFF（无名称）</item>
    /// <item>数字 → 按无符号 16 位解析；沿用旧习惯输入 -1 时按 0xFFFF 处理</item>
    /// <item>其它 → 查城市名称表，查不到则新建条目并保存</item>
    /// </list>
    /// </summary>
    public static ushort Resolve(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return NoName;

        var text = input.Trim();

        // 优先按无符号 16 位解析（0-65535）
        if (ushort.TryParse(text, out ushort numeric))
            return numeric;

        // 兼容旧输入习惯：-1 等价于 0xFFFF
        if (int.TryParse(text, out int signed))
            return unchecked((ushort)signed);

        // 视为城市名称。
        // 名称表可能尚未加载（配置缺失、脚本独立运行等），此时不能把异常抛给调用方：
        // 一次命名失败不该让整段脚本或整个编辑操作中断，退化为"无名称"继续即可。
        try
        {
            var parser = ConfigManager.Instance.GetStringTableParser();

            var existingId = parser.FindCityIdByName(text);
            if (existingId.HasValue)
                return (ushort)existingId.Value;

            int newId = parser.AddOrUpdateCityName(text);
            parser.Save();
            ConfigManager.Instance.ReloadStringTable();

            return (ushort)newId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildingNameResolver] 解析名称「{text}」失败，按无名称处理: {ex.Message}");
            return NoName;
        }
    }
}
