using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 天气列表窗口（天气编辑模式 Q 键，对应 VB WeatherListSetting）：左侧天气列表，右侧天气类型 / 触发回合 / 持续回合。
/// <para>
/// 与 VB 的区别：修改按 Enter /"应用"/ 切换条目时提交、Esc 撤回未提交的修改，每次增删改都是一步撤销（VB 直接改实时列表，取消也撤不回）；
/// 触发回合与持续回合要求 ≥ 1（VB 对无效输入静默忽略、也不查范围）；保留字段 0x04 原样保留；
/// 右侧实时显示生效回合以及与哪些天气重叠。列表在窗口外被改动（地图上撤销 / 重做、R 随机生成、控制台命令）时自动刷新。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.WeatherList)]
public sealed class WeatherListWindow : ListEditorWindow
{
    private readonly TextBlock _period = EditorTheme.Muted("");
    private ComboBox _type = null!;
    private TextBox _round = null!;
    private TextBox _duration = null!;

    private WeatherModifier Weathers => Context.Module<WeatherModifier>()
        ?? throw new InvalidOperationException("WeatherModifier is not registered in EditModeManager");

    protected override string TitleText => Loc.T("Assist.Weather.Title", "天气列表管理");

    protected override int ItemCount => Map.Weathers.Count;

    protected override bool SupportsDuplicate => true;

    protected override string DescribeItem(int index)
    {
        var weather = Map.Weathers[index];
        return Loc.F("Assist.Weather.Item", index + 1, weather.GetWeatherTypeName(), weather.FormatRounds());
    }

    protected override void BuildFields(FieldPanel fields)
    {
        fields.Section(Loc.T("Assist.Weather.Section", "基本信息"));
        _type = fields.Choice(TypeLabel, Weather.GetTypeOptions(),
            Loc.T("Assist.Weather.Field.TypeTip", "从列表选择，或直接输入类型值（类型表在 setting.txt 的 [天气修改] WeatherList）"));
        _round = fields.Text(RoundLabel, Loc.T("Assist.Weather.Field.RoundTip", "天气从第几回合开始（≥ 1）"));
        _duration = fields.Text(DurationLabel, Loc.T("Assist.Weather.Field.DurationTip", "天气持续几回合（≥ 1）"));
        fields.Custom(Loc.T("Assist.Weather.Field.Period", "生效回合"), _period);
        fields.Hint(Loc.T("Assist.Weather.Hint", "同一回合只应有一种天气：生效回合重叠的天气在这里和地图右上角的面板上显示为红色。"));

        _period.TextWrapping = TextWrapping.Wrap;
        _period.VerticalAlignment = VerticalAlignment.Center;
        _round.TextChanged += (_, _) => UpdatePeriodHint();
        _duration.TextChanged += (_, _) => UpdatePeriodHint();
    }

    protected override void LoadFields(int index)
    {
        var weather = Map.Weathers[index];
        SetChoice(_type, weather.WeatherType);
        _round.Text = weather.TriggerRound.ToString(CultureInfo.InvariantCulture);
        _duration.Text = weather.Duration.ToString(CultureInfo.InvariantCulture);
        UpdatePeriodHint();
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        bool ok = TryChoice(_type, TypeLabel, 0, int.MaxValue, errors, out int type);
        ok &= TryInt(_round, RoundLabel, 1, WeatherModifier.MaxRound, errors, out int round);
        ok &= TryInt(_duration, DurationLabel, 1, WeatherModifier.MaxRound, errors, out int duration);
        if (!ok) return false;

        // 从原条目改起：保留字段 0x04 原样带过去
        var weather = Map.Weathers[index];
        weather.WeatherType = type;
        weather.TriggerRound = round;
        weather.Duration = duration;
        var result = Weathers.UpdateWeather(index, weather);
        if (!result.Success)
        {
            errors.Add(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
            return false;
        }

        // 类型表外的值照样保存，但要提醒；基类提交后会把状态行改成"已应用"，所以排到之后再显示
        if (result.Message != null && WeatherModifier.UnknownTypeWarning(type) != null)
        {
            string warning = result.Message;
            Dispatcher.InvokeAsync(() => SetStatus(warning, true), DispatcherPriority.Background);
        }
        return true;
    }

    protected override int AddItem()
        => Weathers.AddWeather(Weathers.CreateNextWeather()).Success ? Map.Weathers.Count - 1 : -1;

    protected override void RemoveItem(int index) => Weathers.DeleteWeather(index);

    protected override int DuplicateItem(int index) => Weathers.DuplicateWeather(index).Success ? index + 1 : -1;

    protected override void RecordChange(string description, Action apply)
        => Context.Editor.RecordCollectionChange(description, Map.Weathers, apply);

    protected override INotifyCollectionChanged? ObservedList => Map.Weathers;

    protected override string? ExternalChangeMessage => Loc.T("Assist.Weather.Refreshed", "天气列表已在窗口外被修改，已刷新");

    /// <summary>按当前输入显示生效回合，并列出与之重叠的其它天气（序号从 1 起）。</summary>
    private void UpdatePeriodHint()
    {
        int index = CurrentIndex;
        if (index < 0 || index >= Map.Weathers.Count
            || !int.TryParse(_round.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int round)
            || !int.TryParse(_duration.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int duration)
            || round < 1 || duration < 1)
        {
            _period.Text = "—";
            _period.Foreground = EditorTheme.TextMuted;
            return;
        }

        var probe = new Weather { TriggerRound = round, Duration = duration };
        var overlapping = new List<int>();
        for (int i = 0; i < Map.Weathers.Count; i++)
        {
            if (i != index && Map.Weathers[i].Overlaps(probe.TriggerRound, probe.EndRound))
                overlapping.Add(i + 1);
        }

        _period.Text = overlapping.Count == 0
            ? probe.FormatRounds()
            : Loc.F("Assist.Weather.OverlapWith", probe.FormatRounds(), string.Join(", ", overlapping));
        _period.Foreground = overlapping.Count == 0 ? EditorTheme.TextMuted : EditorTheme.Danger;
    }

    private static string TypeLabel => Loc.T("Assist.Weather.Field.Type", "天气类型");
    private static string RoundLabel => Loc.T("Assist.Weather.Field.Round", "触发回合");
    private static string DurationLabel => Loc.T("Assist.Weather.Field.Duration", "持续回合");
}
