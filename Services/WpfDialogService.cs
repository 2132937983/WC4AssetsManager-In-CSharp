using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;
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
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.Confirmed, dialog.RiverValue));
    }

    public Task<(bool confirmed, Building building)> ShowBuildingSettingDialogAsync(Building building, bool isNew = false)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, building));

        var dialog = new BuildingSettingWindow(building, isNew)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.Confirmed, dialog.ResultBuilding));
    }

    public Task<(bool confirmed, Army army)> ShowArmySettingDialogAsync(Army army, bool isNew = false)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, army));

        var dialog = new ArmySettingWindow(army, isNew)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.Confirmed, dialog.ResultArmy));
    }

    public Task<(bool confirmed, Army_3 army3)> ShowArmySettingV3DialogAsync(Army_3 army3, bool isNew = false)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, army3));

        var dialog = new ArmySettingV3Window(army3, isNew)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.Confirmed, dialog.ResultArmy3));
    }

    public Task<(bool confirmed, Trap trap)> ShowTrapSettingDialogAsync(Trap trap, bool isNew = false)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, trap));

        var dialog = new TrapSettingWindow(trap, isNew)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.Confirmed, dialog.ResultTrap));
    }

    public Task<(bool confirmed, int belongValue)> ShowBelongListDialogAsync(List<Legion> legions)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, 0));

        var dialog = new BelongListWindow(legions)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.IsConfirmed, dialog.SelectedValue ?? 0));
    }

    public Task<(bool confirmed, int actionId)> ShowLegionBelongListDialogAsync(List<Legion> legions)
    {
        var window = _getOwnerWindow();
        if (window == null)
            return Task.FromResult((false, 0));

        var dialog = new LegionBelongListWindow(legions)
        {
            Owner = window
        };
        dialog.ShowDialog();
        RestoreFocusToSkElement(window);

        return Task.FromResult((dialog.IsConfirmed, dialog.SelectedActionId ?? 0));
    }

    public Task<(bool confirmed, string? value1, string? value2)> ShowDoubleInputDialogAsync(
        string title, string description1, string description2,
        string defaultValue1, string defaultValue2)
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
            DefaultValue2 = defaultValue2
        };

        return ShowDoubleInputDialogCore(dialog);
    }

    private async Task<(bool confirmed, string? value1, string? value2)> ShowDoubleInputDialogCore(DoubleInputDialog dialog)
    {
        var result = await dialog.ShowAsync();
        if (result == null)
            return (false, null, null);
        return (true, result.Value.Value1, result.Value.Value2);
    }

    private static void RestoreFocusToSkElement(Window owner)
    {
        var skElement = FindVisualChild<SkiaSharp.Views.WPF.SKElement>(owner);
        if (skElement != null)
            Keyboard.Focus(skElement);
        else
            owner.Focus();

        owner.Dispatcher.BeginInvoke(() =>
        {
            skElement = FindVisualChild<SkiaSharp.Views.WPF.SKElement>(owner);
            if (skElement != null)
                Keyboard.Focus(skElement);
            else
                owner.Focus();
        }, DispatcherPriority.Input);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }
}