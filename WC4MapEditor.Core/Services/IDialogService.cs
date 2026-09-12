using WC4MapEditor.Core.Models;

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

    /// <summary>
    /// 显示建筑编辑对话框
    /// </summary>
    /// <param name="building">建筑数据</param>
    /// <param name="isNew">是否为新建筑</param>
    /// <returns>是否确认，以及编辑后的建筑数据</returns>
    Task<(bool confirmed, Building building)> ShowBuildingSettingDialogAsync(Building building, bool isNew = false);

    /// <summary>
    /// 显示单位编辑对话框
    /// </summary>
    /// <param name="army">单位数据</param>
    /// <param name="isNew">是否为新单位</param>
    /// <returns>是否确认，以及编辑后的单位数据</returns>
    Task<(bool confirmed, Army army)> ShowArmySettingDialogAsync(Army army, bool isNew = false);

    /// <summary>
    /// 显示v3单位编辑对话框
    /// </summary>
    /// <param name="army3">v3单位数据</param>
    /// <param name="isNew">是否为新单位</param>
    /// <returns>是否确认，以及编辑后的v3单位数据</returns>
    Task<(bool confirmed, Army_3 army3)> ShowArmySettingV3DialogAsync(Army_3 army3, bool isNew = false);

    /// <summary>
    /// 显示归属列表对话框 - 用于选择归属值（Q键）
    /// </summary>
    /// <param name="legions">军团列表</param>
    /// <returns>是否确认，以及选中的归属值（0-255）</returns>
    Task<(bool confirmed, int belongValue)> ShowBelongListDialogAsync(List<Legion> legions);

    /// <summary>
    /// 显示军团归属列表对话框 - 用于选择军团（Q键军团模式）
    /// </summary>
    /// <param name="legions">军团列表</param>
    /// <returns>是否确认，以及选中的军团ActionId</returns>
    Task<(bool confirmed, int actionId)> ShowLegionBelongListDialogAsync(List<Legion> legions);

    /// <summary>
    /// 显示双输入对话框 - 用于批量更改归属（T键）和随机归属（R键）
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="description1">第一个输入描述</param>
    /// <param name="description2">第二个输入描述</param>
    /// <param name="defaultValue1">第一个默认值</param>
    /// <param name="defaultValue2">第二个默认值</param>
    /// <returns>是否确认，以及两个输入值</returns>
    Task<(bool confirmed, string? value1, string? value2)> ShowDoubleInputDialogAsync(
        string title, string description1, string description2,
        string defaultValue1, string defaultValue2);
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