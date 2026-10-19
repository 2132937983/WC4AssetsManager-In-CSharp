using WC4MapEditor.Core.ErrorHandling;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 场景加载 / 卸载时的挂接与解除：地图文件解析失败时的提示、全局事件的订阅与解除。
/// <para>
/// 这些订阅以前是 OnLoaded 里的匿名 lambda，卸载时解不掉：每切换一次场景，错误信息就在控制台里多打一遍，
/// 已经销毁的场景还挂在单例上继续收事件、继续重绘。
/// </para>
/// </summary>
public abstract partial class RenderSceneBase
{
    private ModeSwitchRecoveryStrategy? _modeSwitchRecovery;
    private KeyboardRecoveryStrategy? _keyboardRecovery;
    private Core.Commands.ICommandHost? _sceneCommandHost;
    private bool _sceneUxAttached;

    /// <summary>
    /// 场景子类在 <see cref="LoadMapData"/> 里解析地图文件时用：解析失败（文件损坏、格式不符）时说明原因并返回 null，
    /// OnLoaded 随后回到主界面。以前异常直接抛出 OnLoaded，留下一个没有按键绑定的空白场景，背后再弹出通用错误框。
    /// </summary>
    protected static MapData? ParseMapFileOrReport(string path, Func<string, MapData?> parse)
    {
        try
        {
            return parse(path);
        }
        catch (Exception ex)
        {
            ErrorCollector.Instance.RecordError(ErrorSeverity.Error, $"加载地图失败: {path}: {ex.Message}", nameof(ParseMapFileOrReport), ex);
            MessageBox.Show(Loc.F("Render.File.OpenFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>注册错误恢复策略与全局事件（卸载时由 <see cref="DetachSceneUx"/> 全部解除）。</summary>
    private void RegisterSceneEventHandlers(Core.Commands.ICommandHost commandHost)
    {
        _modeSwitchRecovery = new ModeSwitchRecoveryStrategy(_editModeManager);
        _keyboardRecovery = new KeyboardRecoveryStrategy(_keyboardManager);
        ErrorCollector.Instance.RegisterStrategy(_modeSwitchRecovery);
        ErrorCollector.Instance.RegisterStrategy(_keyboardRecovery);
        ErrorCollector.Instance.ErrorOccurred += OnSceneErrorOccurred;
        ErrorCollector.Instance.ErrorRecovered += OnSceneErrorRecovered;
        _debugConsole.VisibilityChanged += OnDebugConsoleVisibilityChanged;
        _sceneCommandHost = commandHost;
        commandHost.DataModified += OnCommandHostDataModified;
        ConfigManager.StringTableChanged += OnStringTableChanged;
    }

    /// <summary>加载完成后挂上场景交互（按键闸门、撤销按钮状态、窗口激活、拖放脚本）。</summary>
    private void AttachSceneUx()
    {
        if (_sceneUxAttached) return;
        _sceneUxAttached = true;
        AttachSceneInput();
        AttachScriptDrop();
        // 底栏盖在画面底部：帮助文本比剩余高度长时在底栏之上分栏，不被底栏挡住
        if (_titleBar != null)
            _renderEngine.HelpBottomMargin = (int)Math.Ceiling(_titleBar.Height);
        ApplyHelpVisibility();
    }

    /// <summary>解除加载时挂上的一切（可重复调用：卸载与销毁都会调）。</summary>
    private void DetachSceneUx()
    {
        if (_modeSwitchRecovery != null) ErrorCollector.Instance.UnregisterStrategy(_modeSwitchRecovery);
        if (_keyboardRecovery != null) ErrorCollector.Instance.UnregisterStrategy(_keyboardRecovery);
        _modeSwitchRecovery = null;
        _keyboardRecovery = null;
        ErrorCollector.Instance.ErrorOccurred -= OnSceneErrorOccurred;
        ErrorCollector.Instance.ErrorRecovered -= OnSceneErrorRecovered;
        _debugConsole.VisibilityChanged -= OnDebugConsoleVisibilityChanged;
        if (_sceneCommandHost != null) _sceneCommandHost.DataModified -= OnCommandHostDataModified;
        _sceneCommandHost = null;
        ConfigManager.StringTableChanged -= OnStringTableChanged;

        if (!_sceneUxAttached) return;
        _sceneUxAttached = false;
        DetachSceneInput();
        DetachScriptDrop();
    }

    /// <summary>
    /// 场景加载完后在控制台记一行内存占用（对齐 VB 进入场景时的内存统计），排查大地图的内存问题时有用。
    /// </summary>
    private void ReportSceneMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        long workingSetMb = process.WorkingSet64 / (1024 * 1024);
        long managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
        _debugConsole.WriteLine(Loc.F("Scene.Console.Memory", workingSetMb, managedMb));
    }

    private void OnSceneErrorOccurred(object? sender, ErrorRecord e)
        => _debugConsole.WriteLine(Loc.F("Render.Console.Error", e.Severity, e.Message));

    private void OnSceneErrorRecovered(object? sender, ErrorHandleResult e)
    {
        if (e.Recovered)
            _debugConsole.WriteLine(Loc.F("Render.Console.Recovered", e.RecoveryAction));
    }

    /// <summary>控制台已改成独立弹窗，不再盖住主窗口；只把地图上的帮助文本让出来（关掉控制台后按用户的 Ctrl+F1 选择恢复）。</summary>
    private void OnDebugConsoleVisibilityChanged(bool isVisible)
    {
        ApplyHelpVisibility();
        _skElement.InvalidateVisual();
    }

    private void OnCommandHostDataModified()
    {
        _editModeManager_DataModifiedFromUndo();
        _skElement.InvalidateVisual();
    }

    private void OnStringTableChanged()
    {
        _renderEngine.ReloadBuildingCityNames();
        QueueHexInfoRefresh();
        _skElement.InvalidateVisual();
    }
}
