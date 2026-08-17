namespace WC4MapEditor.Core.Services;

/// <summary>
/// 对话框服务接口 - 由 GUI 层实现，通过依赖注入提供给 Core 层
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 显示输入对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="description">描述</param>
    /// <param name="defaultValue">默认值</param>
    /// <param name="minValue">最小值（可选）</param>
    /// <param name="maxValue">最大值（可选）</param>
    /// <returns>用户输入的字符串，取消则返回 null</returns>
    Task<string?> ShowInputDialogAsync(string title, string description, string defaultValue, double? minValue = null, double? maxValue = null);

    /// <summary>
    /// 显示确认对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息</param>
    /// <param name="confirmText">确认按钮文本</param>
    /// <param name="cancelText">取消按钮文本</param>
    /// <returns>是否确认</returns>
    Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, string cancelText);

    /// <summary>
    /// 显示通知对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息</param>
    /// <param name="buttonText">按钮文本</param>
    Task ShowNotificationDialogAsync(string title, string message, string buttonText);

    /// <summary>
    /// 显示河流设置对话框
    /// </summary>
    /// <param name="initialRiverValue">初始河流值</param>
    /// <returns>是否确认，以及新的河流值</returns>
    Task<(bool confirmed, byte riverValue)> ShowRiverSettingDialogAsync(byte initialRiverValue);
}

/// <summary>
/// CLI 命令执行器接口
/// </summary>
public interface ICliCommandExecutor
{
    /// <summary>
    /// 执行 CLI 命令
    /// </summary>
    /// <param name="commandLine">命令行</param>
    /// <returns>是否成功</returns>
    bool Execute(string commandLine);
}