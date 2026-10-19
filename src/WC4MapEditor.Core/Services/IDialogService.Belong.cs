namespace WC4MapEditor.Core.Services;

// 归属编辑模式需要的对话框（批量更改归属 T 键）
public partial interface IDialogService
{
    /// <summary>
    /// 带取值范围的双输入对话框：两个输入框各自限定在 [min, max] 内，越界时对话框内提示、不关闭。
    /// 用于批量更改归属（T 键，两个值都是 0-255 的归属值）。
    /// </summary>
    /// <returns>是否确认，以及两个输入值</returns>
    Task<(bool confirmed, string? value1, string? value2)> ShowRangedDoubleInputDialogAsync(
        string title, string description1, string description2,
        string defaultValue1, string defaultValue2,
        double min1, double max1, double min2, double max2);
}
