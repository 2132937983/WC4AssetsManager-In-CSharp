using System.Reflection;
using WC4MapEditor.Core.ErrorHandling;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using CoreKeyBinding = WC4MapEditor.Core.Input.KeyBinding;
using CoreKeyModifiers = WC4MapEditor.Core.Input.KeyModifiers;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 场景级快捷键：方向键平移、+/- 缩放、Shift+O 视图层、Shift+空格反向切换模式、单按 Alt 打开场景列表，
/// 以及帮助文本的显隐与淡入淡出、撤销 / 重做反馈、帮助里的场景按键一览。
/// <para>
/// 这些"软"按键不注册进 KeyboardManager（那里会同时触发所有匹配的绑定）：当前模式绑定了同一按键时让给模式，
/// 没绑定才由场景处理。W/A/S/D 被各模式占用，不用于平移。
/// </para>
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>方向键平移：每帧移动的像素（对齐 VB _viewMoveSpeed）</summary>
    private const double KeyboardPanStep = 15;

    /// <summary>键盘缩放的倍率，与滚轮一致</summary>
    private const double KeyboardZoomFactor = 1.15;

    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;

    private static readonly Key[] PanKeys = { Key.Up, Key.Down, Key.Left, Key.Right };

    private static readonly KeyChord ViewLayerChord = KeyChord.Shift(KeyCodes.O);
    private static readonly KeyChord ReverseCycleChord = KeyChord.Shift(KeyCodes.Space);

    private static bool _keyCodesVerified;

    private readonly HashSet<Key> _pressedPanKeys = new();
    private DispatcherTimer? _keyboardPanTimer;
    private DispatcherTimer? _helpFadeTimer;
    private bool _helpHiddenByUser;
    private bool _altPressedAlone;

    // ------------------------------------------------------------------ 挂接

    private void AttachSceneInput()
    {
        VerifyKeyCodesOnce();
        _keyboardManager.BindingGate = AllowSceneKeyBinding;
        _fileStateManager.UndoManager.StateChanged += UpdateUndoRedoButtons;
        Window.Activated += OnSceneWindowActivated;
        Window.Deactivated += OnSceneWindowDeactivated;
        _skElement.LostKeyboardFocus += OnSceneLostKeyboardFocus;
        UpdateUndoRedoButtons();
    }

    private void DetachSceneInput()
    {
        if (_keyboardManager.BindingGate == (Func<CoreKeyBinding, bool>)AllowSceneKeyBinding)
            _keyboardManager.BindingGate = null;
        _fileStateManager.UndoManager.StateChanged -= UpdateUndoRedoButtons;
        Window.Activated -= OnSceneWindowActivated;
        Window.Deactivated -= OnSceneWindowDeactivated;
        _skElement.LostKeyboardFocus -= OnSceneLostKeyboardFocus;
        StopKeyboardPan();
        _helpFadeTimer?.Stop();
        StopSceneStatusTimers();
        StopHexInfoRefresh();
    }

    /// <summary>主窗口重新激活（关掉子窗口、从别的程序切回来）后，把键盘焦点还给地图，按键才能直接生效。</summary>
    private void OnSceneWindowActivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var focused = Keyboard.FocusedElement;
            if (focused == null || ReferenceEquals(focused, Window) || ReferenceEquals(focused, this))
                RestoreFocus();
        });
    }

    private void OnSceneWindowDeactivated(object? sender, EventArgs e) => ReleaseHeldKeys();

    private void OnSceneLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ReleaseHeldKeys();

    /// <summary>
    /// 焦点离开地图时清掉"按住"状态：抬起事件会落到别的窗口，不清的话视图会一直漂移，
    /// 下次按同一个键也会被当成长按的自动重复。
    /// </summary>
    private void ReleaseHeldKeys()
    {
        StopKeyboardPan();
        _altPressedAlone = false;
        _keyboardManager.ResetState();
    }

    // ------------------------------------------------------------------ 按键入口

    /// <summary>场景先看一遍按下的键：是场景软按键就自己处理并返回 true，否则交给 KeyboardManager。</summary>
    private bool TryHandleSceneKeyDown(Key key, bool isRepeat)
    {
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            // 先按住 Ctrl / Shift 再按 Alt 是组合键的一部分，不算"单按 Alt"
            if (!isRepeat) _altPressedAlone = (GetCurrentKeyModifiers() & ~CoreKeyModifiers.Alt) == CoreKeyModifiers.None;
            return true;
        }
        _altPressedAlone = false;

        if (_camera == null || _mapData == null) return false;

        var mods = GetCurrentKeyModifiers();
        int code = (int)key;

        if (mods == CoreKeyModifiers.None && Array.IndexOf(PanKeys, key) >= 0 && !ModeBindsChord(code, mods))
        {
            StartKeyboardPan(key);
            return true;
        }

        bool zoomIn = key is Key.OemPlus or Key.Add;
        bool zoomOut = key is Key.OemMinus or Key.Subtract;
        if ((zoomIn || zoomOut) && (mods == CoreKeyModifiers.None || (mods == CoreKeyModifiers.Shift && key == Key.OemPlus))
            && !ModeBindsChord(code, mods))
        {
            ZoomByKeyboard(zoomIn);
            return true;
        }

        if (code == ViewLayerChord.KeyCode && mods == ViewLayerChord.Modifiers && !ModeBindsChord(code, mods))
        {
            if (!isRepeat) ToggleViewLayerVisibility();
            return true;
        }

        if (code == ReverseCycleChord.KeyCode && mods == ReverseCycleChord.Modifiers && !ModeBindsChord(code, mods))
        {
            if (!isRepeat) CycleEditMode(-1);
            return true;
        }

        return false;
    }

    /// <summary>抬起：结束方向键平移；单按 Alt（中间没按别的键）时开关场景列表。</summary>
    private void HandleSceneKeyUp(Key key)
    {
        if (Array.IndexOf(PanKeys, key) >= 0)
        {
            _pressedPanKeys.Remove(key);
            if (_pressedPanKeys.Count == 0) StopKeyboardPan();
            return;
        }

        if (key is Key.LeftAlt or Key.RightAlt)
        {
            bool alone = _altPressedAlone;
            _altPressedAlone = false;
            if (alone) ToggleSceneTabPopup();
        }
    }

    /// <summary>当前模式是否绑定了这个按键组合（绑定了就让给模式）。</summary>
    private bool ModeBindsChord(int keyCode, CoreKeyModifiers modifiers)
        => _editModeManager.CurrentHandler?.GetKeyBindings().Any(b => b.KeyCode == keyCode && b.Modifiers == modifiers) == true;

    // ------------------------------------------------------------------ 平移 / 缩放

    private void StartKeyboardPan(Key key)
    {
        _pressedPanKeys.Add(key);
        if (_keyboardPanTimer == null)
        {
            _keyboardPanTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _keyboardPanTimer.Tick += (_, _) => PanByKeyboardStep();
        }
        if (!_keyboardPanTimer.IsEnabled)
        {
            // 先走一步，按一下就有反应
            PanByKeyboardStep();
            _keyboardPanTimer.Start();
        }
    }

    private void StopKeyboardPan()
    {
        _pressedPanKeys.Clear();
        _keyboardPanTimer?.Stop();
    }

    private void PanByKeyboardStep()
    {
        // 抬起事件丢失（焦点被别的窗口拿走）时以实际按键状态为准，避免一直漂移
        _pressedPanKeys.RemoveWhere(k => !Keyboard.IsKeyDown(k));
        if (_camera == null || _pressedPanKeys.Count == 0)
        {
            StopKeyboardPan();
            return;
        }

        double dx = 0, dy = 0;
        if (_pressedPanKeys.Contains(Key.Left)) dx += KeyboardPanStep;
        if (_pressedPanKeys.Contains(Key.Right)) dx -= KeyboardPanStep;
        if (_pressedPanKeys.Contains(Key.Up)) dy += KeyboardPanStep;
        if (_pressedPanKeys.Contains(Key.Down)) dy -= KeyboardPanStep;
        if (dx == 0 && dy == 0) return;

        _camera.Pan(dx, dy);
        AfterKeyboardCameraChange();
    }

    /// <summary>以视口中心为锚点缩放（范围与滚轮相同：0.1 – 5.0）。</summary>
    private void ZoomByKeyboard(bool zoomIn)
    {
        if (_camera == null) return;
        double factor = zoomIn ? KeyboardZoomFactor : 1.0 / KeyboardZoomFactor;
        double zoom = Math.Clamp(_camera.ZoomLevel * factor, MinZoom, MaxZoom);
        _camera.ZoomAt(_skElement.ActualWidth / 2, _skElement.ActualHeight / 2, zoom);
        AfterKeyboardCameraChange();
    }

    private void AfterKeyboardCameraChange()
    {
        _renderEngine.InvalidateViewLayerCache();
        if (_hoverPoint is { } p) UpdateBrushPreview(p.X, p.Y);
        _skElement.InvalidateVisual();
    }

    private void ToggleViewLayerVisibility()
    {
        if (string.IsNullOrEmpty(_renderEngine.ViewLayerImagePath))
        {
            PostSceneStatus(Loc.T("Scene.Status.NoViewLayerImage"));
            return;
        }
        _renderEngine.ViewLayerVisible = !_renderEngine.ViewLayerVisible;
        string state = _renderEngine.ViewLayerVisible ? Loc.T("Render.Visible") : Loc.T("Render.Hidden");
        PostSceneStatus(Loc.F("Render.Console.ViewLayerVisibility", state));
        _skElement.InvalidateVisual();
    }

    private void ToggleSceneTabPopup()
    {
        var popup = _sceneTabPopup;
        if (popup == null) return;
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }
        RefreshSceneTabButtons();
        popup.IsOpen = true;
    }

    // ------------------------------------------------------------------ 模式循环

    /// <summary>
    /// 按场景的模式表循环切换，最后一个模式之后进入浏览模式（对齐 VB：空格依次经过"无模式"）。
    /// <paramref name="step"/> = 1 正向（空格），-1 反向（Shift+空格）。
    /// </summary>
    private void CycleEditMode(int step)
    {
        var modes = _editModeManager.GetAvailableModes(SceneType);
        if (modes.Count == 0) return;

        int count = modes.Count + 1;               // 最后一格是浏览模式
        int current = -1;
        for (int i = 0; i < modes.Count; i++)
            if (modes[i] == _editModeManager.CurrentMode) current = i;
        if (current < 0) current = modes.Count;    // 浏览模式（或当前场景没有的模式）

        int next = ((current + step) % count + count) % count;
        _editModeManager.SwitchMode(next == modes.Count ? EditMode.None : modes[next]);
    }

    // ------------------------------------------------------------------ 帮助文本

    /// <summary>帮助文本该不该显示：用户用 Ctrl+F1 关掉了，或调试控制台开着，都不显示。</summary>
    private bool HelpOverlayShouldShow => !_helpHiddenByUser && !_debugConsole.IsVisible;

    private void ToggleHelpOverlay()
    {
        _helpHiddenByUser = !_helpHiddenByUser;
        ApplyHelpVisibility();
    }

    /// <summary>按当前状态设置帮助文本显隐，并驱动淡入淡出直到完成。</summary>
    private void ApplyHelpVisibility()
    {
        _renderEngine.ShowHelp = HelpOverlayShouldShow;
        if (_helpFadeTimer == null)
        {
            _helpFadeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _helpFadeTimer.Tick += (_, _) =>
            {
                // 淡入淡出每次重绘推进一步（OnPaintSurface 里的 UpdateHelpFadeAnimation），这里只负责持续请求重绘
                if (!_renderEngine.IsHelpFading) _helpFadeTimer.Stop();
                _skElement.InvalidateVisual();
            };
        }
        if (!_helpFadeTimer.IsEnabled) _helpFadeTimer.Start();
    }

    /// <summary>
    /// 帮助文本末尾的场景按键一览（每行 5 项）。按键取自实际注册的绑定与上面的软按键定义，改了绑定这里自动跟着变。
    /// </summary>
    private string BuildSceneKeyHelp()
    {
        string? Global(string action)
        {
            foreach (var def in GlobalKeyDefs)
                if (def.Action == action) return KeyNames.Format((int)def.Key, def.Mods);
            return null;
        }

        string? Bound(string id) => _keyboardManager.GetBinding(id) is { } b ? KeyNames.Format(b.KeyCode, b.Modifiers) : null;

        string arrows = string.Concat(KeyNames.Name(KeyCodes.Up), KeyNames.Name(KeyCodes.Down),
            KeyNames.Name(KeyCodes.Left), KeyNames.Name(KeyCodes.Right));

        var items = new List<string>();
        void Add(string? trigger, string textKey)
        {
            if (trigger != null) items.Add($"{trigger} {Loc.T(textKey)}");
        }

        Add(Global("tab_action"), "Scene.Help.HexInfo");
        Add(Global("toggle_show_layer2"), "Scene.Help.Layer2");
        Add(Global("toggle_help_text"), "Scene.Help.Help");
        Add(Global("toggle_hex_borders"), "Scene.Help.Grid");
        Add(Global("toggle_labels"), "Scene.Help.Labels");
        Add(Bound("Screenshot"), "Scene.Help.Screenshot");
        Add(Bound("ToggleConsole"), "Scene.Help.Console");
        Add(Bound("QuickSave"), "Scene.Help.Save");
        Add(Global("undo"), "Scene.Help.Undo");
        Add(Global("redo"), "Scene.Help.Redo");
        Add(arrows, "Scene.Help.Pan");
        Add(Loc.T("Scene.Help.ZoomKeys"), "Scene.Help.Zoom");
        Add(KeyNames.Format(ViewLayerChord), "Scene.Help.ViewLayer");
        Add(KeyNames.Format(ReverseCycleChord), "Scene.Help.ReverseCycle");
        Add("Alt", "Scene.Help.SceneTabs");

        const int perLine = 5;
        var lines = new List<string>();
        for (int i = 0; i < items.Count; i += perLine)
            lines.Add(string.Join("   ", items.Skip(i).Take(perLine)));
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ 撤销 / 重做

    /// <summary>撤销上一步（Ctrl+Z、工具栏按钮、控制台 undo 共用）：不需要目标格，结果写到状态栏。</summary>
    private void PerformUndo()
    {
        string? description = _fileStateManager.UndoManager.LastUndoDescription;
        if (_fileStateManager.Undo())
        {
            _editModeManager_DataModifiedFromUndo();
            PostSceneStatus(Loc.F("Scene.Status.Undone", description ?? ""));
        }
        else
        {
            PostSceneStatus(Loc.T("Scene.Status.NothingToUndo"));
        }
    }

    private void PerformRedo()
    {
        string? description = _fileStateManager.UndoManager.LastRedoDescription;
        if (_fileStateManager.Redo())
        {
            _editModeManager_DataModifiedFromUndo();
            PostSceneStatus(Loc.F("Scene.Status.Redone", description ?? ""));
        }
        else
        {
            PostSceneStatus(Loc.T("Scene.Status.NothingToRedo"));
        }
    }

    // ------------------------------------------------------------------ 自检

    /// <summary>
    /// KeyCodes（Core）的数值必须与 WPF Key 完全一致：GUI 把 (int)e.Key 原样交给 KeyboardManager。
    /// 启动时按名字逐个核对一次，对不上的记入错误收集器（早先 D0–D9 与逗号、句号就是这样错了一位而没人发现）。
    /// </summary>
    private static void VerifyKeyCodesOnce()
    {
        if (_keyCodesVerified) return;
        _keyCodesVerified = true;

        foreach (var field in typeof(KeyCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.GetRawConstantValue() is not int value) continue;
            if (Enum.TryParse<Key>(field.Name, out var key) && (int)key != value)
            {
                ErrorCollector.Instance.RecordError(ErrorSeverity.Warning,
                    $"KeyCodes.{field.Name}={value} 与 WPF Key.{field.Name}={(int)key} 不一致", nameof(VerifyKeyCodesOnce));
            }
        }
    }
}
