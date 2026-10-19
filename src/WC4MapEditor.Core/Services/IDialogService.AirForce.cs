namespace WC4MapEditor.Core.Services;

/// <summary>
/// 空袭 / 空中支援批量生成对话框的一步：标题、两个输入框的说明 / 默认值 / 取值范围，以及按钮文字。
/// 第 1 步是"所属军团 + 生成数量"，第 2 步是"回合最小值 + 回合最大值"。
/// </summary>
public sealed record AirGenerateStep(
    string Title,
    string Label1, string Default1, int Min1, int Max1,
    string Label2, string Default2, int Min2, int Max2,
    string ConfirmText, string CancelText);

public partial interface IDialogService
{
    /// <summary>
    /// 显示批量生成的一步输入（按钮文字可定制为"下一步" / "生成"，并在对话框里检查取值范围，超范围时就地提示、不关闭）。
    /// <para>默认实现退回普通双输入框（按钮固定、不检查范围）：调用方无论如何都要自行解析并校验返回值。</para>
    /// </summary>
    /// <returns>是否确认，以及两个输入框的文本</returns>
    Task<(bool confirmed, string? value1, string? value2)> ShowAirGenerateStepAsync(AirGenerateStep step)
        => ShowDoubleInputDialogAsync(step.Title, step.Label1, step.Label2, step.Default1, step.Default2);
}
