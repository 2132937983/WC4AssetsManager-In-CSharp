using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.ErrorHandling;

/// <summary>
/// 模式切换错误恢复策略 - 当模式切换失败时回退到上一个模式或退出编辑模式
/// </summary>
public sealed class ModeSwitchRecoveryStrategy : IRecoveryStrategy
{
    private readonly EditModeManager _editModeManager;

    public string Name => "ModeSwitchRecovery";

    public ModeSwitchRecoveryStrategy(EditModeManager editModeManager)
    {
        _editModeManager = editModeManager;
    }

    public bool CanHandle(ErrorRecord error)
    {
        return error.Source?.Contains("SwitchMode") == true
            || error.Message.Contains("模式")
            || error.Message.Contains("Mode");
    }

    public Task<ErrorHandleResult> RecoverAsync(ErrorRecord error)
    {
        try
        {
            // 尝试退出当前编辑模式，回到安全状态
            _editModeManager.ExitEditMode();
            return Task.FromResult(ErrorHandleResult.Success("已退出编辑模式，回到浏览状态"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                ErrorHandleResult.Failed(
                    new ErrorRecord(ErrorSeverity.Critical, "恢复策略失败", Name, ex)));
        }
    }
}

/// <summary>
/// 键盘输入错误恢复策略 - 当键盘处理失败时重置键盘状态
/// </summary>
public sealed class KeyboardRecoveryStrategy : IRecoveryStrategy
{
    private readonly Input.KeyboardManager _keyboardManager;

    public string Name => "KeyboardRecovery";

    public KeyboardRecoveryStrategy(Input.KeyboardManager keyboardManager)
    {
        _keyboardManager = keyboardManager;
    }

    public bool CanHandle(ErrorRecord error)
    {
        return error.Source?.Contains("Keyboard") == true
            || error.Message.Contains("键盘")
            || error.Message.Contains("Key");
    }

    public Task<ErrorHandleResult> RecoverAsync(ErrorRecord error)
    {
        try
        {
            _keyboardManager.ResetState();
            return Task.FromResult(ErrorHandleResult.Success("已重置键盘状态"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                ErrorHandleResult.Failed(
                    new ErrorRecord(ErrorSeverity.Critical, "键盘恢复失败", Name, ex)));
        }
    }
}

/// <summary>
/// 地图数据错误恢复策略 - 当地图操作失败时尝试回滚
/// </summary>
public sealed class MapDataRecoveryStrategy : IRecoveryStrategy
{
    public string Name => "MapDataRecovery";

    public bool CanHandle(ErrorRecord error)
    {
        return error.Source?.Contains("MapData") == true
            || error.Message.Contains("地图")
            || error.Message.Contains("Map");
    }

    public Task<ErrorHandleResult> RecoverAsync(ErrorRecord error)
    {
        // 地图数据错误通常无法自动恢复，记录错误并返回失败
        return Task.FromResult(
            ErrorHandleResult.Failed(
                new ErrorRecord(ErrorSeverity.Critical, "地图数据错误无法自动恢复", Name)));
    }
}