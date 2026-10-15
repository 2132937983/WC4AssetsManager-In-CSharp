namespace WC4MapEditor.Core.Input;

[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4,
    XButton1 = 8,
    XButton2 = 16
}

public enum MouseActionKind
{
    Down,
    Up,
    Move,
    Wheel,
    Click,
    DoubleClick,
    DragStart,
    DragMove,
    DragEnd
}

public readonly struct MousePosition
{
    public double X { get; }
    public double Y { get; }

    public MousePosition(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double DistanceTo(MousePosition other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double DistanceSquaredTo(MousePosition other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public MousePosition Delta(MousePosition from) => new(X - from.X, Y - from.Y);

    public static MousePosition Empty => new(0, 0);

    public override string ToString() => $"({X:F1}, {Y:F1})";
}

public sealed class MouseActionEventArgs : EventArgs
{
    public MouseActionKind Action { get; }
    public MouseButtons Button { get; }
    public MousePosition Position { get; }
    public MousePosition DeltaPosition { get; }
    public double WheelDelta { get; }
    public int ClickCount { get; }
    public MouseButtons PressedButtons { get; }
    public KeyModifiers Modifiers { get; set; }
    public bool Handled { get; set; }

    public MouseActionEventArgs(
        MouseActionKind action,
        MouseButtons button,
        MousePosition position,
        MousePosition deltaPosition,
        double wheelDelta = 0,
        int clickCount = 1,
        MouseButtons pressedButtons = MouseButtons.None)
    {
        Action = action;
        Button = button;
        Position = position;
        DeltaPosition = deltaPosition;
        WheelDelta = wheelDelta;
        ClickCount = clickCount;
        PressedButtons = pressedButtons;
    }
}

public sealed class MouseBinding
{
    public string Id { get; }
    public MouseActionKind Action { get; }
    public MouseButtons Button { get; }
    public KeyModifiers Modifiers { get; }
    public Action<MouseActionEventArgs>? Callback { get; }
    public string? Description { get; }

    public MouseBinding(string id, MouseActionKind action, MouseButtons button, KeyModifiers modifiers = KeyModifiers.None, Action<MouseActionEventArgs>? callback = null, string? description = null)
    {
        Id = id;
        Action = action;
        Button = button;
        Modifiers = modifiers;
        Callback = callback;
        Description = description;
    }

    public bool Matches(MouseActionKind action, MouseButtons button, KeyModifiers modifiers)
    {
        return Action == action && Button == button && Modifiers == modifiers;
    }
}

public sealed class MouseManager
{
    private static readonly object _lock = new();
    private static MouseManager? _instance;

    /// <summary>
    /// 全局默认实例。
    /// <para>
    /// 【迁移中】新代码应通过依赖注入获取 <see cref="MouseManager"/>：
    /// 由容器注入 <c>MainWindow</c>，再经 <c>Window.MouseManager</c> 提供给各渲染场景。
    /// 待全部调用点迁移完毕后移除此属性。
    /// </para>
    /// </summary>
    public static MouseManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MouseManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, MouseBinding> _bindings = new();
    private readonly HashSet<MouseButtons> _pressedButtons = [];
    private MousePosition _currentPosition = MousePosition.Empty;
    private MousePosition _lastPosition = MousePosition.Empty;
    private MousePosition _mouseDownPosition = MousePosition.Empty;
    private MouseButtons _mouseDownButton = MouseButtons.None;
    private int _clickCount;
    private bool _isDragging;
    private double _dragThreshold = 5.0;
    private DateTime _lastClickTime = DateTime.MinValue;
    private double _doubleClickIntervalMs = 500;
    private KeyModifiers _currentModifiers = KeyModifiers.None;

    public event EventHandler<MouseActionEventArgs>? MouseAction;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public MouseManager() { }

    public MousePosition CurrentPosition => _currentPosition;
    public MousePosition LastPosition => _lastPosition;
    public MousePosition MouseDownPosition => _mouseDownPosition;
    public MouseButtons PressedButtons => AggregateButtons();
    public bool IsDragging => _isDragging;
    public double DragThreshold
    {
        get => _dragThreshold;
        set => _dragThreshold = Math.Max(0, value);
    }
    public double DoubleClickIntervalMs
    {
        get => _doubleClickIntervalMs;
        set => _doubleClickIntervalMs = Math.Max(0, value);
    }

    public MouseBinding RegisterBinding(string id, MouseActionKind action, MouseButtons button, KeyModifiers modifiers = KeyModifiers.None, Action<MouseActionEventArgs>? callback = null, string? description = null)
    {
        var binding = new MouseBinding(id, action, button, modifiers, callback, description);
        _bindings[id] = binding;
        return binding;
    }

    public bool UnregisterBinding(string id) => _bindings.Remove(id);

    public void ClearBindings() => _bindings.Clear();

    public MouseBinding? GetBinding(string id) => _bindings.TryGetValue(id, out var b) ? b : null;

    public IReadOnlyCollection<MouseBinding> GetAllBindings() => _bindings.Values;

    public void SetModifierState(KeyModifiers modifiers) => _currentModifiers = modifiers;

    public void ProcessMouseDown(MouseButtons button, double x, double y)
    {
        _pressedButtons.Add(button);
        _lastPosition = _currentPosition;
        _currentPosition = new MousePosition(x, y);
        _mouseDownPosition = _currentPosition;
        _mouseDownButton = button;
        _isDragging = false;

        var now = DateTime.UtcNow;
        double elapsedMs = (now - _lastClickTime).TotalMilliseconds;
        _lastClickTime = now;

        if (elapsedMs <= _doubleClickIntervalMs)
            _clickCount++;
        else
            _clickCount = 1;

        var args = new MouseActionEventArgs(
            MouseActionKind.Down, button, _currentPosition,
            _currentPosition.Delta(_lastPosition),
            clickCount: _clickCount,
            pressedButtons: AggregateButtons());
        args = WithModifiers(args);

        MouseAction?.Invoke(this, args);
        InvokeMatchingBindings(args);
    }

    public void ProcessMouseUp(MouseButtons button, double x, double y)
    {
        _lastPosition = _currentPosition;
        _currentPosition = new MousePosition(x, y);

        var wasDragging = _isDragging;

        if (_isDragging)
        {
            var dragArgs = new MouseActionEventArgs(
                MouseActionKind.DragEnd, _mouseDownButton, _currentPosition,
                _currentPosition.Delta(_lastPosition),
                pressedButtons: AggregateButtons());
            dragArgs = WithModifiers(dragArgs);
            MouseAction?.Invoke(this, dragArgs);
            InvokeMatchingBindings(dragArgs);
            _isDragging = false;
        }

        _pressedButtons.Remove(button);

        if (!wasDragging && _mouseDownButton == button)
        {
            var clickArgs = new MouseActionEventArgs(
                MouseActionKind.Click, button, _currentPosition,
                _currentPosition.Delta(_lastPosition),
                clickCount: _clickCount,
                pressedButtons: AggregateButtons());
            clickArgs = WithModifiers(clickArgs);
            MouseAction?.Invoke(this, clickArgs);
            InvokeMatchingBindings(clickArgs);

            if (_clickCount >= 2)
            {
                var dblClickArgs = new MouseActionEventArgs(
                    MouseActionKind.DoubleClick, button, _currentPosition,
                    _currentPosition.Delta(_lastPosition),
                    clickCount: _clickCount,
                    pressedButtons: AggregateButtons());
                dblClickArgs = WithModifiers(dblClickArgs);
                MouseAction?.Invoke(this, dblClickArgs);
                InvokeMatchingBindings(dblClickArgs);
            }
        }

        var upArgs = new MouseActionEventArgs(
            MouseActionKind.Up, button, _currentPosition,
            _currentPosition.Delta(_lastPosition),
            pressedButtons: AggregateButtons());
        upArgs = WithModifiers(upArgs);
        MouseAction?.Invoke(this, upArgs);
        InvokeMatchingBindings(upArgs);

        _mouseDownButton = MouseButtons.None;
    }

    public void ProcessMouseMove(double x, double y)
    {
        _lastPosition = _currentPosition;
        _currentPosition = new MousePosition(x, y);

        if (_mouseDownButton != MouseButtons.None && !_isDragging)
        {
            double dist = _currentPosition.DistanceTo(_mouseDownPosition);
            if (dist >= _dragThreshold)
            {
                _isDragging = true;
                var dragStartArgs = new MouseActionEventArgs(
                    MouseActionKind.DragStart, _mouseDownButton, _currentPosition,
                    _currentPosition.Delta(_mouseDownPosition),
                    pressedButtons: AggregateButtons());
                dragStartArgs = WithModifiers(dragStartArgs);
                MouseAction?.Invoke(this, dragStartArgs);
                InvokeMatchingBindings(dragStartArgs);
            }
        }

        var moveArgs = new MouseActionEventArgs(
            _isDragging ? MouseActionKind.DragMove : MouseActionKind.Move,
            _isDragging ? _mouseDownButton : MouseButtons.None,
            _currentPosition,
            _currentPosition.Delta(_lastPosition),
            pressedButtons: AggregateButtons());
        moveArgs = WithModifiers(moveArgs);
        MouseAction?.Invoke(this, moveArgs);
        InvokeMatchingBindings(moveArgs);
    }

    public void ProcessMouseWheel(double delta, double x, double y)
    {
        _lastPosition = _currentPosition;
        _currentPosition = new MousePosition(x, y);

        var args = new MouseActionEventArgs(
            MouseActionKind.Wheel, MouseButtons.None, _currentPosition,
            _currentPosition.Delta(_lastPosition),
            wheelDelta: delta,
            pressedButtons: AggregateButtons());
        args = WithModifiers(args);
        MouseAction?.Invoke(this, args);
        InvokeMatchingBindings(args);
    }

    public void ResetState()
    {
        _pressedButtons.Clear();
        _currentPosition = MousePosition.Empty;
        _lastPosition = MousePosition.Empty;
        _mouseDownPosition = MousePosition.Empty;
        _mouseDownButton = MouseButtons.None;
        _isDragging = false;
        _clickCount = 0;
        _currentModifiers = KeyModifiers.None;
    }

    public bool IsButtonDown(MouseButtons button) => _pressedButtons.Contains(button);
    public bool IsAnyButtonDown => _pressedButtons.Count > 0;

    private MouseButtons AggregateButtons()
    {
        var result = MouseButtons.None;
        foreach (var b in _pressedButtons)
            result |= b;
        return result;
    }

    private MouseActionEventArgs WithModifiers(MouseActionEventArgs args)
    {
        return new MouseActionEventArgs(
            args.Action, args.Button, args.Position, args.DeltaPosition,
            args.WheelDelta, args.ClickCount, args.PressedButtons)
        { Handled = args.Handled, Modifiers = _currentModifiers };
    }

    private void InvokeMatchingBindings(MouseActionEventArgs args)
    {
        foreach (var binding in _bindings.Values)
        {
            if (binding.Matches(args.Action, args.Button, _currentModifiers) && binding.Callback != null)
            {
                binding.Callback(args);
            }
        }
    }
}