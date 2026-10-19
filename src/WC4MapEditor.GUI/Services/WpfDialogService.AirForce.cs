using WC4MapEditor.Core.Services;
using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Services;

public sealed partial class WpfDialogService
{
    /// <summary>空袭 / 空中支援批量生成的一步：用双输入框，按钮文字与取值范围按 <paramref name="step"/> 设置。</summary>
    public async Task<(bool confirmed, string? value1, string? value2)> ShowAirGenerateStepAsync(AirGenerateStep step)
    {
        var window = _getOwnerWindow();
        if (window == null) return (false, null, null);

        using var dialog = new DoubleInputDialog(window)
        {
            Title = step.Title,
            Description1 = step.Label1,
            Description2 = step.Label2,
            DefaultValue1 = step.Default1,
            DefaultValue2 = step.Default2,
            ConfirmText = step.ConfirmText,
            CancelText = step.CancelText,
            MinValue1 = step.Min1,
            MaxValue1 = step.Max1,
            MinValue2 = step.Min2,
            MaxValue2 = step.Max2
        };
        var result = await dialog.ShowAsync();
        return result is { } values ? (true, values.Value1, values.Value2) : (false, null, null);
    }
}
