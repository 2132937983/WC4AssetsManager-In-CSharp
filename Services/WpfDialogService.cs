using System.Windows;
using System.Windows.Media;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Views.Dialogs;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Services;

/// <summary>
/// WPF 对话框服务实现 - 将 Core 层的 IDialogService 接口映射到 WPF 对话框控件
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly Func<Window> _getOwnerWindow;

    public WpfDialogService(Func<Window> getOwnerWindow)
    {
        _getOwnerWindow = getOwnerWindow;
    }

    public async Task<string?> ShowInputDialogAsync(string title, string description, string defaultValue, double? minValue = null, double? maxValue = null)
    {
        var window = _getOwnerWindow();
        if (window == null) return null;

        using var dialog = new SingleInputDialog(window)
        {
            Title = title,
            Description = description,
            DefaultValue = defaultValue,
            MinValue = minValue,
            MaxValue = maxValue
        };
        return await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, string cancelText)
    {
        var window = _getOwnerWindow();
        if (window == null) return false;

        using var dialog = new ConfirmDialog(window)
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText
        };
        return await dialog.ShowAsync();
    }

    public async Task ShowNotificationDialogAsync(string title, string message, string buttonText)
    {
        var window = _getOwnerWindow();
        if (window == null) return;

        using var dialog = new NotificationDialog(window)
        {
            Title = title,
            Message = message,
            ButtonText = buttonText,
            AccentBrush = new SolidColorBrush(Color.FromArgb(180, 80, 160, 220))
        };
        await dialog.ShowAsync();
    }

    public Task<(bool confirmed, byte riverValue)> ShowRiverSettingDialogAsync(byte initialRiverValue)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, initialRiverValue));

        var dialog = new RiverSettingWindow(initialRiverValue)
        {
            Owner = window
        };
        dialog.ShowDialog();

        return Task.FromResult((dialog.Confirmed, dialog.RiverValue));
    }
}