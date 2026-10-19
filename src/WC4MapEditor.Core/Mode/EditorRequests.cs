namespace WC4MapEditor.Core.Mode;

/// <summary>
/// GUI 编辑窗口的种类名。模式通过 <see cref="ModeContext.OpenEditor"/> 按名字请求打开窗口，
/// 不需要知道窗口类，也不需要为每个窗口在 <see cref="Modifiers.EditModeManager"/> / <see cref="ModeContext"/>
/// 上再加一个专用事件。GUI 用 <c>[AssistEditor(EditorKinds.Xxx)]</c> 标注对应的窗口类即可接上。
/// </summary>
public static class EditorKinds
{
    public const string LegionSetting = "legion.setting";
    public const string LegionList = "legion.list";
    public const string HeaderSetting = "header.setting";
    public const string MapCaseList = "mapcase.list";
    public const string WeatherList = "weather.list";
    public const string StrategicConstructionList = "strategic.list";
    public const string EventSetting = "event.setting";
    public const string AirForceList = "airforce.list";
    public const string AirSupportList = "airsupport.list";
    public const string ReinforcementSetting = "reinforcement.setting";
}

/// <summary>打开编辑窗口的请求：窗口种类 + 触发时的焦点格（-1 表示无）+ 可选参数。</summary>
public sealed record EditorRequest(string Kind, int Col = -1, int Row = -1, object? Argument = null);
