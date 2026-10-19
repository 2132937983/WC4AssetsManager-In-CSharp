using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Services;

// 归属编辑模式需要的对话框（批量更改归属 T 键）
public sealed partial class WpfDialogService
{
    public Task<(bool confirmed, string? value1, string? value2)> ShowRangedDoubleInputDialogAsync(
        string title, string description1, string description2,
        string defaultValue1, string defaultValue2,
        double min1, double max1, double min2, double max2)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, (string?)null, (string?)null));

        var dialog = new DoubleInputDialog(window)
        {
            Title = title,
            Description1 = description1,
            Description2 = description2,
            DefaultValue1 = defaultValue1,
            DefaultValue2 = defaultValue2,
            MinValue1 = min1,
            MaxValue1 = max1,
            MinValue2 = min2,
            MaxValue2 = max2
        };

        return ShowDoubleInputDialogCore(dialog);
    }
}
