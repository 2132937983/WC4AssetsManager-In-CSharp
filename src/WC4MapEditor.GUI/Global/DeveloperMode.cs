namespace WC4MapEditor.GUI.Global;

/// <summary>
/// 开发者模式（控制台 <c>system dev true|false</c> 切换，对齐 VB 版 Global.DeveloperMode）。
/// 只在本次运行内有效，不写入 setting.txt；开始界面据 <see cref="Changed"/> 显示 / 隐藏开发者按钮。
/// </summary>
public static class DeveloperMode
{
    private static bool _enabled;

    /// <summary>开关状态变化后触发（在设置它的线程上）。</summary>
    public static event Action? Changed;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Changed?.Invoke();
        }
    }
}
