using System.Text;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 手势：模式可以把动作挂在 <see cref="LeftClick"/> / <see cref="RightClick"/> / <see cref="Escape"/> 上，
/// 场景做完通用处理（选区、画笔、拖拽等）后通过 <see cref="IModeHandler.GetGestureAction"/> 把动作分发给模式。
/// 其余手势只用于帮助文本里描述由场景实现的交互（例如右键拖动框选）。
/// </summary>
public enum ModeGesture
{
    LeftClick,
    RightClick,
    RightDrag,
    LeftDrag,
    Wheel,

    /// <summary>
    /// Esc：场景先交给模式（取消区域移动、清空选区……）。动作返回 true 表示已处理；
    /// 返回 false（没有可取消的东西）时场景再弹出"返回主界面"确认。Esc 是全局键，模式不能直接绑定它。
    /// </summary>
    Escape,
}

/// <summary>一个按键组合：键码（<see cref="KeyCodes"/>）+ 修饰键。int 可隐式转换为无修饰键的组合。</summary>
public readonly record struct KeyChord(int KeyCode, KeyModifiers Modifiers = KeyModifiers.None)
{
    public static implicit operator KeyChord(int keyCode) => new(keyCode);

    public static KeyChord Ctrl(int keyCode) => new(keyCode, KeyModifiers.Ctrl);
    public static KeyChord Shift(int keyCode) => new(keyCode, KeyModifiers.Shift);
    public static KeyChord Alt(int keyCode) => new(keyCode, KeyModifiers.Alt);
    public static KeyChord CtrlShift(int keyCode) => new(keyCode, KeyModifiers.Ctrl | KeyModifiers.Shift);

    public override string ToString() => KeyNames.Format(KeyCode, Modifiers);
}

/// <summary>某个动作上绑定的一个按键（主键或别名）。</summary>
public readonly record struct ModeKey(string BindingId, KeyChord Chord);

/// <summary>动作完成后分发器要不要替处理函数发"数据已修改"通知。</summary>
public enum ModeActionKind
{
    /// <summary>编辑：处理函数返回 true 表示数据已改，分发器随后调用 <see cref="ModeContext.NotifyDataModified"/>。</summary>
    Edit,

    /// <summary>命令：返回值原样返回、不自动通知（视图开关、打开窗口、复制等不改数据的动作，或自行通知的动作）。</summary>
    Command,
}

/// <summary>动作处理函数的调用参数：动作名、焦点格与模式上下文，外加常用的便捷访问。</summary>
public readonly struct ModeActionArgs
{
    public ModeActionArgs(string action, int col, int row, ModeContext context)
    {
        Action = action;
        Col = col;
        Row = row;
        Context = context;
    }

    public string Action { get; }
    public int Col { get; }
    public int Row { get; }
    public ModeContext Context { get; }

    public HexCoord Cell => new(Col, Row);
    public MapData? Map => Context.MapData;
    public IDialogService? Dialogs => Context.DialogService;

    /// <summary>
    /// 取已注册的模块。取不到说明模式声明的模块没有在 <see cref="EditModeManager"/> 里注册，
    /// 属于编程错误，直接抛出（比返回 null 再到处判空更容易定位）。
    /// </summary>
    public T Module<T>() where T : class, IModifier
        => Context.GetModifier<T>() ?? throw new InvalidOperationException($"Module {typeof(T).Name} is not registered in EditModeManager");

    /// <summary>在状态栏显示一条消息。</summary>
    public void Status(string message) => Context.RaiseStatusMessage?.Invoke(message);

    /// <summary>
    /// 本次操作的目标格：选区里有多个格子时返回整个选区（按行、列排序，结果稳定），否则只返回焦点格。
    /// 批量按键（删除 / 粘贴 / 设置值）统一用它，单格操作与多选操作的语义就一致了。
    /// </summary>
    public IReadOnlyList<HexCoord> TargetCells()
    {
        var selected = HexSelector.Instance.SelectedHexes;
        if (selected.Count > 1)
            return selected.OrderBy(h => h.Row).ThenBy(h => h.Col).ToList();
        return new[] { new HexCoord(Col, Row) };
    }
}

/// <summary>模式里的一个动作：名称、文本、按键 / 手势与处理函数。</summary>
public sealed class ModeAction
{
    internal readonly List<ModeKey> KeyList = new();

    internal ModeAction(string action, ModeActionKind kind, string textKey, string fallbackText,
        Func<ModeActionArgs, Task<bool>> handler)
    {
        Action = action;
        Kind = kind;
        TextKey = textKey;
        FallbackText = fallbackText;
        Handler = handler;
    }

    /// <summary>动作名（<see cref="IModeHandler.HandleKeyAction"/> 收到的字符串）。</summary>
    public string Action { get; }
    public ModeActionKind Kind { get; }

    /// <summary>说明文本的翻译键（Mode.&lt;模式&gt;.Action.&lt;动作名&gt;）。</summary>
    public string TextKey { get; }

    /// <summary>翻译文件里没有该键时显示的文本（简体中文）。</summary>
    public string FallbackText { get; }

    /// <summary>当前语言下的说明文本。</summary>
    public string Text => Loc.T(TextKey, FallbackText);

    /// <summary>绑定的按键（主键在前，别名在后）。</summary>
    public IReadOnlyList<ModeKey> Keys => KeyList;

    /// <summary>挂在哪个鼠标手势上（没有则为 null）。</summary>
    public ModeGesture? Gesture { get; internal set; }

    /// <summary>是否出现在帮助文本里（隐藏动作仍可由脚本 / AI / GUI 分发）。</summary>
    public bool ShowInHelp { get; internal set; } = true;

    internal Func<ModeActionArgs, Task<bool>> Handler { get; }

    /// <summary>帮助文本里的触发方式，例如 "Q"、"X / Delete"、"右键"；无按键也无手势时为 null。</summary>
    public string? TriggerText
    {
        get
        {
            var parts = new List<string>(KeyList.Count + 1);
            foreach (var k in KeyList) parts.Add(KeyNames.Format(k.Chord.KeyCode, k.Chord.Modifiers));
            if (Gesture is { } g) parts.Add(KeyNames.Gesture(g));
            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }
    }
}

/// <summary>
/// 模式的动作表：声明"哪个键 / 手势 → 哪个动作 → 调哪个模块的哪个函数"。
/// <para>
/// 一处声明同时产出三样东西，彼此不会再对不上：按键绑定（<see cref="KeyBindings"/>）、
/// 动作分发（<see cref="DispatchAsync"/>）与帮助文本（<see cref="BuildHelpText"/>，按声明顺序排列）。
/// </para>
/// <para>
/// 文本全部走翻译键，键名按规则生成，不用手写：动作 = <c>{前缀}.Action.{动作名}</c>，
/// 帮助行 = <c>{前缀}.Help.{id}</c>，备注 = <c>{前缀}.Note.{id}</c>（前缀如 <c>Mode.Belong</c>）。
/// 翻译缺失时显示声明里给的简体中文。
/// </para>
/// </summary>
public sealed class ModeActionTable
{
    private abstract record HelpEntry;
    private sealed record ActionEntry(ModeAction Action) : HelpEntry;
    private sealed record LineEntry(string TextKey, string Fallback, KeyChord? Key, ModeGesture? Gesture) : HelpEntry;

    private readonly Dictionary<string, ModeAction> _byName = new(StringComparer.Ordinal);
    private readonly List<ModeAction> _actions = new();
    private readonly List<(ModeKey Key, ModeAction Action)> _bindings = new();
    private readonly List<HelpEntry> _help = new();

    public ModeActionTable(string prefix)
    {
        Prefix = prefix;
    }

    /// <summary>翻译键前缀，例如 <c>Mode.Belong</c>。</summary>
    public string Prefix { get; }

    /// <summary>全部动作（声明顺序）。</summary>
    public IReadOnlyList<ModeAction> Actions => _actions;

    // ------------------------------------------------------------------ 按键动作

    /// <summary>按键 → 编辑动作（处理函数返回 true = 数据已改，自动通知重绘）。</summary>
    public ModeActionTable Edit(string bindingId, KeyChord key, string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddKeyed(bindingId, key, action, fallback, ModeActionKind.Edit, handler);

    /// <inheritdoc cref="Edit(string, KeyChord, string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable Edit(string bindingId, KeyChord key, string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddKeyed(bindingId, key, action, fallback, ModeActionKind.Edit, Wrap(handler));

    /// <summary>按键 → 命令动作（返回值原样返回，不自动通知）。</summary>
    public ModeActionTable Command(string bindingId, KeyChord key, string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddKeyed(bindingId, key, action, fallback, ModeActionKind.Command, handler);

    /// <inheritdoc cref="Command(string, KeyChord, string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable Command(string bindingId, KeyChord key, string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddKeyed(bindingId, key, action, fallback, ModeActionKind.Command, Wrap(handler));

    /// <summary>给已声明的动作再绑一个键（帮助文本里显示为 "X / Delete"）。</summary>
    public ModeActionTable Alias(string bindingId, KeyChord key, string action)
    {
        if (!_byName.TryGetValue(action, out var a))
            throw new InvalidOperationException($"{Prefix}: alias {bindingId} refers to unknown action \"{action}\"");
        var mk = new ModeKey(bindingId, key);
        a.KeyList.Add(mk);
        _bindings.Add((mk, a));
        return this;
    }

    // ------------------------------------------------------------------ 鼠标动作

    /// <summary>鼠标手势 → 编辑动作（GUI 在通用处理后分发）。</summary>
    public ModeActionTable GestureEdit(ModeGesture gesture, string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddGesture(gesture, action, fallback, ModeActionKind.Edit, handler);

    /// <inheritdoc cref="GestureEdit(ModeGesture, string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable GestureEdit(ModeGesture gesture, string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddGesture(gesture, action, fallback, ModeActionKind.Edit, Wrap(handler));

    /// <summary>鼠标手势 → 命令动作。</summary>
    public ModeActionTable GestureCommand(ModeGesture gesture, string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddGesture(gesture, action, fallback, ModeActionKind.Command, handler);

    /// <inheritdoc cref="GestureCommand(ModeGesture, string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable GestureCommand(ModeGesture gesture, string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddGesture(gesture, action, fallback, ModeActionKind.Command, Wrap(handler));

    /// <summary>把已声明（通常已有按键）的动作同时挂到一个鼠标手势上。</summary>
    public ModeActionTable OnGesture(ModeGesture gesture, string action)
    {
        if (!_byName.TryGetValue(action, out var a))
            throw new InvalidOperationException($"{Prefix}: gesture refers to unknown action \"{action}\"");
        a.Gesture = gesture;
        return this;
    }

    // ------------------------------------------------------------------ 隐藏动作

    /// <summary>不绑键、不进帮助的编辑动作：给脚本 / AI / GUI 内部分发，或 VB 里存在但从未绑键的功能。</summary>
    public ModeActionTable HiddenEdit(string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddHidden(action, fallback, ModeActionKind.Edit, handler);

    /// <inheritdoc cref="HiddenEdit(string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable HiddenEdit(string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddHidden(action, fallback, ModeActionKind.Edit, Wrap(handler));

    /// <summary>不绑键、不进帮助的命令动作。</summary>
    public ModeActionTable HiddenCommand(string action, string fallback, Func<ModeActionArgs, Task<bool>> handler)
        => AddHidden(action, fallback, ModeActionKind.Command, handler);

    /// <inheritdoc cref="HiddenCommand(string, string, Func{ModeActionArgs, Task{bool}})"/>
    public ModeActionTable HiddenCommand(string action, string fallback, Func<ModeActionArgs, bool> handler)
        => AddHidden(action, fallback, ModeActionKind.Command, Wrap(handler));

    // ------------------------------------------------------------------ 纯帮助行

    /// <summary>只出现在帮助里的一行，描述由 GUI 实现的鼠标交互（如 "右键拖动 - 框选多格"）。</summary>
    public ModeActionTable HelpLine(string id, ModeGesture gesture, string fallback)
    {
        _help.Add(new LineEntry($"{Prefix}.Help.{id}", fallback, null, gesture));
        return this;
    }

    /// <summary>只出现在帮助里的一行，描述由别处处理的按键。</summary>
    public ModeActionTable HelpLine(string id, KeyChord key, string fallback)
    {
        _help.Add(new LineEntry($"{Prefix}.Help.{id}", fallback, key, null));
        return this;
    }

    /// <summary>帮助里的一行说明文字（不带按键）。</summary>
    public ModeActionTable Note(string id, string fallback)
    {
        _help.Add(new LineEntry($"{Prefix}.Note.{id}", fallback, null, null));
        return this;
    }

    // ------------------------------------------------------------------ 产出

    /// <summary>按键绑定（每次调用按当前语言生成说明文本）。</summary>
    public IEnumerable<ModeKeyBinding> KeyBindings
    {
        get
        {
            foreach (var (key, action) in _bindings)
                yield return new ModeKeyBinding(key.BindingId, key.Chord.KeyCode, key.Chord.Modifiers, action.Action, action.Text);
        }
    }

    public bool Contains(string action) => _byName.ContainsKey(action);

    /// <summary>本表用到的全部翻译键（动作说明、帮助行、备注），供翻译完整性检查。</summary>
    public IEnumerable<string> TextKeys()
    {
        foreach (var a in _actions) yield return a.TextKey;
        foreach (var e in _help)
            if (e is LineEntry line) yield return line.TextKey;
    }

    public ModeAction? Find(string action) => _byName.TryGetValue(action, out var a) ? a : null;

    /// <summary>手势对应的动作名；没有则为 null。</summary>
    public string? GetGestureAction(ModeGesture gesture)
    {
        foreach (var a in _actions)
            if (a.Gesture == gesture) return a.Action;
        return null;
    }

    /// <summary>
    /// 分发动作。未声明的动作返回 false（与旧的 switch 落到 default 一致）；
    /// 编辑动作返回 true 时通知数据已修改。处理函数抛出的异常原样留在返回的 Task 里，由调用方决定如何报告。
    /// </summary>
    public async Task<bool> DispatchAsync(string action, int col, int row, ModeContext context)
    {
        if (!_byName.TryGetValue(action, out var a)) return false;
        bool result = await a.Handler(new ModeActionArgs(action, col, row, context)).ConfigureAwait(true);
        if (result && a.Kind == ModeActionKind.Edit) context.NotifyDataModified?.Invoke();
        return result;
    }

    /// <summary>生成帮助文本：标题一行，之后每行 "  触发 - 说明"，按声明顺序。</summary>
    public string BuildHelpText(string title)
    {
        var sb = new StringBuilder(title);
        foreach (var entry in _help)
        {
            string? trigger;
            string text;
            switch (entry)
            {
                case ActionEntry ae:
                    if (!ae.Action.ShowInHelp) continue;
                    trigger = ae.Action.TriggerText;
                    text = ae.Action.Text;
                    break;
                case LineEntry le:
                    trigger = le.Key is { } k ? KeyNames.Format(k.KeyCode, k.Modifiers)
                            : le.Gesture is { } g ? KeyNames.Gesture(g)
                            : null;
                    text = Loc.T(le.TextKey, le.Fallback);
                    break;
                default:
                    continue;
            }

            sb.Append('\n').Append("  ");
            if (trigger != null) sb.Append(trigger).Append(" - ");
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 自检：绑定 ID 不能重复，同一模式里同一个按键组合不能绑两个动作。
    /// 违反时抛异常 —— 模式在 <see cref="EditModeManager"/> 构造时就会建表，问题在启动 / 测试时立刻暴露。
    /// </summary>
    public void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var chords = new Dictionary<KeyChord, string>();
        foreach (var (key, action) in _bindings)
        {
            if (ReservedChords.Contains(key.Chord))
                throw new InvalidOperationException(
                    $"{Prefix}: key {key.Chord} is a global scene key and cannot be bound by a mode (action \"{action.Action}\")");
            if (!ids.Add(key.BindingId))
                throw new InvalidOperationException($"{Prefix}: duplicate binding id \"{key.BindingId}\"");
            if (chords.TryGetValue(key.Chord, out var other))
                throw new InvalidOperationException(
                    $"{Prefix}: key {key.Chord} is bound to both \"{other}\" and \"{action.Action}\"");
            chords[key.Chord] = action.Action;
        }
    }

    /// <summary>
    /// 场景 / 模式管理器占用的全局按键：Space 切换模式、数字键直达模式、Tab 格子信息、Esc（用 <see cref="ModeGesture.Escape"/>）、
    /// F1 / Ctrl+F1 / B / N 视图开关、F2 截图、F3 控制台、Ctrl+Z / Ctrl+Y 撤销重做、Ctrl+S 保存。
    /// KeyboardManager 会触发所有匹配的绑定，模式再绑这些键就会和全局功能同时执行。
    /// </summary>
    public static readonly IReadOnlySet<KeyChord> ReservedChords = BuildReservedChords();

    private static HashSet<KeyChord> BuildReservedChords()
    {
        var set = new HashSet<KeyChord>
        {
            KeyCodes.Space, KeyCodes.Tab, KeyCodes.Escape, KeyCodes.F1, KeyChord.Ctrl(KeyCodes.F1), KeyCodes.F2, KeyCodes.F3,
            KeyCodes.B, KeyCodes.N, KeyChord.Ctrl(KeyCodes.Z), KeyChord.Ctrl(KeyCodes.Y), KeyChord.Ctrl(KeyCodes.S),
        };
        for (int d = 0; d <= 9; d++)
        {
            set.Add(KeyCodes.D0 + d);
            set.Add(KeyCodes.NumPad0 + d);
        }
        return set;
    }

    // ------------------------------------------------------------------ 内部

    private ModeActionTable AddKeyed(string bindingId, KeyChord key, string action, string fallback,
        ModeActionKind kind, Func<ModeActionArgs, Task<bool>> handler)
    {
        var a = AddAction(action, fallback, kind, handler);
        var mk = new ModeKey(bindingId, key);
        a.KeyList.Add(mk);
        _bindings.Add((mk, a));
        return this;
    }

    private ModeActionTable AddGesture(ModeGesture gesture, string action, string fallback,
        ModeActionKind kind, Func<ModeActionArgs, Task<bool>> handler)
    {
        var a = AddAction(action, fallback, kind, handler);
        a.Gesture = gesture;
        return this;
    }

    private ModeActionTable AddHidden(string action, string fallback, ModeActionKind kind, Func<ModeActionArgs, Task<bool>> handler)
    {
        var a = AddAction(action, fallback, kind, handler);
        a.ShowInHelp = false;
        return this;
    }

    private ModeAction AddAction(string action, string fallback, ModeActionKind kind, Func<ModeActionArgs, Task<bool>> handler)
    {
        if (_byName.ContainsKey(action))
            throw new InvalidOperationException($"{Prefix}: action \"{action}\" is declared twice (use Alias for a second key)");
        var a = new ModeAction(action, kind, $"{Prefix}.Action.{action}", fallback, handler);
        _byName[action] = a;
        _actions.Add(a);
        _help.Add(new ActionEntry(a));
        return a;
    }

    private static Func<ModeActionArgs, Task<bool>> Wrap(Func<ModeActionArgs, bool> handler)
        => args => Task.FromResult(handler(args));
}
