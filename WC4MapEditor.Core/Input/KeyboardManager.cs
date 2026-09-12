using System.Diagnostics;
using WC4MapEditor.Core.ErrorHandling;

namespace WC4MapEditor.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4
}

public sealed class KeyBinding
{
    public string Id { get; }
    public int KeyCode { get; }
    public KeyModifiers Modifiers { get; }
    public string? Description { get; }
    public Action? Callback { get; }

    public KeyBinding(string id, int keyCode, KeyModifiers modifiers = KeyModifiers.None, Action? callback = null, string? description = null)
    {
        Id = id;
        KeyCode = keyCode;
        Modifiers = modifiers;
        Callback = callback;
        Description = description;
    }

    public bool Matches(int keyCode, KeyModifiers modifiers)
    {
        return KeyCode == keyCode && Modifiers == modifiers;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(KeyCode.ToString());
        return string.Join("+", parts);
    }
}

public sealed class KeyboardManager
{
    private static readonly object _lock = new();
    private static KeyboardManager? _instance;

    public static KeyboardManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new KeyboardManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, KeyBinding> _bindings = new();
    private readonly HashSet<int> _pressedKeys = [];
    private KeyModifiers _currentModifiers = KeyModifiers.None;
    private KeyModifiers _externalModifiers = KeyModifiers.None;
    private bool _useExternalModifiers;

    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<KeyboardEventArgs>? KeyUp;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public KeyboardManager() { }

    public KeyBinding RegisterBinding(string id, int keyCode, KeyModifiers modifiers = KeyModifiers.None, Action? callback = null, string? description = null)
    {
        var binding = new KeyBinding(id, keyCode, modifiers, callback, description);
        _bindings[id] = binding;
        return binding;
    }

    public bool UnregisterBinding(string id)
    {
        return _bindings.Remove(id);
    }

    public void ClearBindings()
    {
        _bindings.Clear();
    }

    public KeyBinding? GetBinding(string id)
    {
        return _bindings.TryGetValue(id, out var binding) ? binding : null;
    }

    public IReadOnlyCollection<KeyBinding> GetAllBindings() => _bindings.Values;

    public IReadOnlyCollection<KeyBinding> FindBindings(int keyCode, KeyModifiers modifiers)
    {
        return _bindings.Values.Where(b => b.Matches(keyCode, modifiers)).ToList();
    }

    public void SetModifierState(KeyModifiers modifiers)
    {
        _externalModifiers = modifiers;
        _useExternalModifiers = true;
    }

    public void ProcessKeyDown(int keyCode)
    {
        _pressedKeys.Add(keyCode);
        UpdateModifiers();

        Debug.WriteLine($"[KeyboardManager] ProcessKeyDown: KeyCode={keyCode}, Modifiers={_currentModifiers}, Bindings={_bindings.Count}");

        var args = new KeyboardEventArgs(keyCode, _currentModifiers, _pressedKeys);
        KeyDown?.Invoke(this, args);

        if (args.Handled)
        {
            Debug.WriteLine($"[KeyboardManager] KeyDown handled by subscriber, skipping bindings");
            return;
        }

        // 复制绑定列表以避免在遍历过程中修改集合（如模式切换时注册/注销绑定）
        var bindingsSnapshot = _bindings.Values.ToList();
        foreach (var binding in bindingsSnapshot)
        {
            if (binding.Matches(keyCode, _currentModifiers) && binding.Callback != null)
            {
                Debug.WriteLine($"[KeyboardManager] Binding matched: {binding.Id} ({binding})");
                try
                {
                    binding.Callback.Invoke();
                }
                catch (Exception ex)
                {
                    ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                        $"按键回调执行失败: {binding.Id}", nameof(ProcessKeyDown), ex);
                    // 继续处理其他绑定，不中断
                }
            }
        }
    }

    public void ProcessKeyUp(int keyCode)
    {
        _pressedKeys.Remove(keyCode);
        UpdateModifiers();

        var args = new KeyboardEventArgs(keyCode, _currentModifiers, _pressedKeys);
        KeyUp?.Invoke(this, args);
    }

    public void ResetState()
    {
        _pressedKeys.Clear();
        _currentModifiers = KeyModifiers.None;
    }

    public bool IsKeyDown(int keyCode) => _pressedKeys.Contains(keyCode);

    public KeyModifiers GetCurrentModifiers() => _currentModifiers;

    public bool IsCtrlPressed => _currentModifiers.HasFlag(KeyModifiers.Ctrl);
    public bool IsShiftPressed => _currentModifiers.HasFlag(KeyModifiers.Shift);
    public bool IsAltPressed => _currentModifiers.HasFlag(KeyModifiers.Alt);

    private void UpdateModifiers()
    {
        if (_useExternalModifiers)
        {
            _currentModifiers = _externalModifiers;
            return;
        }

        _currentModifiers = KeyModifiers.None;
        foreach (var key in _pressedKeys)
        {
            if (IsCtrlKey(key)) _currentModifiers |= KeyModifiers.Ctrl;
            else if (IsShiftKey(key)) _currentModifiers |= KeyModifiers.Shift;
            else if (IsAltKey(key)) _currentModifiers |= KeyModifiers.Alt;
        }
    }

    public static bool IsCtrlKey(int keyCode) => keyCode == CtrlKeyCode;
    public static bool IsShiftKey(int keyCode) => keyCode == ShiftKeyCode;
    public static bool IsAltKey(int keyCode) => keyCode == AltKeyCode;

    public const int CtrlKeyCode = 17;
    public const int ShiftKeyCode = 16;
    public const int AltKeyCode = 18;
}

public sealed class KeyboardEventArgs : EventArgs
{
    public int KeyCode { get; }
    public KeyModifiers Modifiers { get; }
    public IReadOnlyCollection<int> PressedKeys { get; }
    public bool Handled { get; set; }

    public KeyboardEventArgs(int keyCode, KeyModifiers modifiers, IReadOnlyCollection<int> pressedKeys)
    {
        KeyCode = keyCode;
        Modifiers = modifiers;
        PressedKeys = pressedKeys;
        Handled = false;
    }
}