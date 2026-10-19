using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 天气编辑模式（对应 VB 的 Weather / WeatherList 模式）：天气是与格子无关的全局列表，没有鼠标操作。
/// Q 打开天气列表窗口编辑；R 在一段回合里随机生成首尾相接的天气序列。
/// 显示国家领域（对齐 VB）和右上角的天气面板（C# 新增）。
/// </summary>
public sealed class WeatherEditMode : ModeBase
{
    public override EditMode Mode => EditMode.WeatherEdit;
    protected override string LocKey => "Weather";
    protected override string DefaultDisplayName => "天气编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Weather;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Weather };
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.Weather;

    protected override void DefineActions(ModeActionTable t)
    {
        t.Command("WE_Q", KeyCodes.Q, "open_list", "打开天气列表",
                a => a.Context.OpenEditor(EditorKinds.WeatherList, a.Col, a.Row))
         .Command("WE_R", KeyCodes.R, "random_weather", "随机生成天气数据", GenerateRandomAsync)
         .Note("panel", "右上角面板按回合列出天气；生效回合重叠的天气显示为红色");
    }

    /// <summary>
    /// R：输入初始 / 最终回合 → 确认（提示将替换的重叠天气）→ 生成（一步撤销）→ 完成提示（对齐 VB 的对话框流程）。
    /// 改完立即通知重绘，所以挂成命令动作：完成提示弹出时地图上的天气面板已经是新数据。
    /// </summary>
    private static async Task<bool> GenerateRandomAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs is not { } dialogs)
        {
            a.Status(Loc.T("Mode.Weather.Msg.NeedDialogs", "随机生成天气需要界面对话框（命令行请用 weather random）"));
            return false;
        }

        var (confirmed, startText, endText) = await dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Weather.Msg.RandomTitle", "随机生成天气数据"),
            Loc.T("Mode.Weather.Msg.StartPrompt", "初始回合 (>=1):"),
            Loc.T("Mode.Weather.Msg.EndPrompt", "最终回合 (>=初始回合):"),
            "1", "50");
        if (!confirmed) return false;

        if (!TryParseRound(startText, out int start) || !TryParseRound(endText, out int end))
            return await RejectAsync(dialogs, Loc.T("Mode.Weather.Msg.InvalidNumber", "输入的回合值无效，请输入有效的整数。"));
        if (WeatherModifier.ValidateRoundRange(start, end) is { } error)
            return await RejectAsync(dialogs, error);

        var weather = a.Module<WeatherModifier>();
        string question = Loc.F("Mode.Weather.Msg.ConfirmRandom", start, end);
        int overlapping = weather.CountOverlapping(start, end);
        if (overlapping > 0) question += "\n" + Loc.F("Mode.Weather.Msg.ConfirmReplace", overlapping);
        if (!await dialogs.ShowConfirmDialogAsync(Loc.T("Mode.Weather.Msg.ConfirmTitle", "确认生成"), question,
                Loc.T("Common.Ok", "确定"), Loc.T("Common.Cancel", "取消")))
            return false;

        ModifierResult result = default;
        a.Context.RecordCollectionChange(Loc.F("Module.Weather.UndoRandom", start, end), map.Weathers,
            () => result = weather.GenerateRandomWeatherSequence(start, end));
        if (result.Message != null) a.Status(result.Message);
        if (!result.Success) return false;

        a.Context.NotifyDataModified?.Invoke();
        await dialogs.ShowNotificationDialogAsync(Loc.T("Mode.Weather.Msg.DoneTitle", "生成完成"),
            Loc.F("Mode.Weather.Msg.Done", result.AffectedCount), Loc.T("Common.Ok", "确定"));
        return true;
    }

    private static bool TryParseRound(string? text, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static async Task<bool> RejectAsync(IDialogService dialogs, string message)
    {
        await dialogs.ShowNotificationDialogAsync(Loc.T("Common.InputError", "输入错误"), message, Loc.T("Common.Ok", "确定"));
        return false;
    }
}
