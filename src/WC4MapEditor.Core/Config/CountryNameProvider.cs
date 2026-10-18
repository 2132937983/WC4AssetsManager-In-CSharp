using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 国家ID → 国家名 的统一解析。
/// <para>
/// 军团列表、军团编辑窗口、CLI 全部走这一份，避免各处各写一套。
/// </para>
/// <para>
/// 取值顺序（与军团编辑窗口原逻辑一致）：
/// <list type="number">
/// <item><c>CountrySettings.json</c> 里的 <c>Name</c> —— 新增国家的名字写在这里；</item>
/// <item>string table 的 <c>country_{id}</c>；</item>
/// <item>都没有时回退为 <c>国家{id}</c>。</item>
/// </list>
/// 第 1 步优先，是因为新加的国家只写进了 CountrySettings，string table 里还没有对应条目，
/// 只查 string table 会显示成"国家{id}"。
/// </para>
/// </summary>
public static class CountryNameProvider
{
    private static readonly Dictionary<int, string> _cache = new();

    static CountryNameProvider()
    {
        // string table 被重载后名字可能变，缓存要跟着失效
        ConfigManager.StringTableChanged += ClearCache;
    }

    /// <summary>取国家名（带缓存）。</summary>
    public static string Get(int countryId)
    {
        if (_cache.TryGetValue(countryId, out var cached)) return cached;

        string name = $"国家{countryId}";
        try
        {
            var setting = CountrySettingParser.Instance.GetCountryById(countryId);
            if (setting != null && !string.IsNullOrWhiteSpace(setting.Name))
            {
                name = setting.Name;
            }
            else
            {
                var value = ConfigManager.Instance.GetStringTableValue($"country_{countryId}");
                if (!string.IsNullOrWhiteSpace(value)) name = value;
            }
        }
        catch
        {
            // 配置没就绪时保持回退值，不影响显示
        }

        _cache[countryId] = name;
        return name;
    }

    /// <summary>清空缓存（string table / CountrySettings 变更后调用）。</summary>
    public static void ClearCache() => _cache.Clear();
}
