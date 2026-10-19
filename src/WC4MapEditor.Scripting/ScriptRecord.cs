namespace WC4MapEditor.Scripting;

/// <summary>
/// 脚本可读写的记录列表的种类名（<see cref="IMapScriptApi.GetRecordCount"/> 等方法的 kind 参数）。
/// </summary>
public static class ScriptRecordKinds
{
    /// <summary>事件</summary>
    public const string Event = "event";

    /// <summary>方案（AI 方针）</summary>
    public const string Case = "case";

    /// <summary>天气</summary>
    public const string Weather = "weather";

    /// <summary>战略建设</summary>
    public const string Strategic = "strategic";

    /// <summary>空袭（挂在机场格上）</summary>
    public const string AirRaid = "airraid";

    /// <summary>空中支援（没有坐标）</summary>
    public const string AirSupport = "airsupport";

    /// <summary>援军（按地图版本自动取 v1/v2 或 v3 的记录）</summary>
    public const string Reinforcement = "reinforcement";
}

/// <summary>
/// 暴露给脚本的一条记录快照（只读值对象）：数值字段名 → 值，加上几个文字标签（如天气类型名）。
/// 字段名与数据结构里的字段一致，改值请用 <see cref="IMapScriptApi.SetRecordField"/> —— 给快照赋值不生效。
/// </summary>
public sealed class ScriptRecord
{
    /// <summary>记录在列表中的下标（从 0 起）</summary>
    public int Index { get; init; }

    /// <summary>数值字段（名称 → 值），顺序与数据结构里的声明顺序一致</summary>
    public IReadOnlyList<KeyValuePair<string, double>> Values { get; init; } = Array.Empty<KeyValuePair<string, double>>();

    /// <summary>文字标签（名称 → 文本），如 TriggerEventName / WeatherTypeName；只读，不可写入</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Labels { get; init; } = Array.Empty<KeyValuePair<string, string>>();
}
