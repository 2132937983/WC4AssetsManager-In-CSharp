using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 常用按键的 WPF Key 枚举值常量（System.Windows.Input.Key 的整数值）。
/// <para>
/// GUI 把 <c>(int)e.Key</c> 原样交给 <see cref="KeyboardManager"/>，所以这里的数值必须与 WPF 完全一致。
/// 早先 D0–D9 整体错了一位（按 1 会切到 2 号模式），OemComma / OemPeriod 写成了
/// OemQuestion(/) / AbntC1 的值（地形模式的 , . 键完全不响应）。
/// </para>
/// </summary>
public static class KeyCodes
{
    public const int Back = 2;
    public const int Tab = 3;
    public const int Enter = 6;
    public const int Escape = 13;
    public const int Space = 18;
    public const int PageUp = 19;
    public const int PageDown = 20;
    public const int End = 21;
    public const int Home = 22;
    public const int Left = 23;
    public const int Up = 24;
    public const int Right = 25;
    public const int Down = 26;
    public const int Insert = 31;
    public const int Delete = 32;

    public const int D0 = 34;
    public const int D1 = 35;
    public const int D2 = 36;
    public const int D3 = 37;
    public const int D4 = 38;
    public const int D5 = 39;
    public const int D6 = 40;
    public const int D7 = 41;
    public const int D8 = 42;
    public const int D9 = 43;

    public const int A = 44;
    public const int B = 45;
    public const int C = 46;
    public const int D = 47;
    public const int E = 48;
    public const int F = 49;
    public const int G = 50;
    public const int H = 51;
    public const int I = 52;
    public const int J = 53;
    public const int K = 54;
    public const int L = 55;
    public const int M = 56;
    public const int N = 57;
    public const int O = 58;
    public const int P = 59;
    public const int Q = 60;
    public const int R = 61;
    public const int S = 62;
    public const int T = 63;
    public const int U = 64;
    public const int V = 65;
    public const int W = 66;
    public const int X = 67;
    public const int Y = 68;
    public const int Z = 69;

    public const int NumPad0 = 74;
    public const int NumPad1 = 75;
    public const int NumPad2 = 76;
    public const int NumPad3 = 77;
    public const int NumPad4 = 78;
    public const int NumPad5 = 79;
    public const int NumPad6 = 80;
    public const int NumPad7 = 81;
    public const int NumPad8 = 82;
    public const int NumPad9 = 83;
    public const int Multiply = 84;
    public const int Add = 85;
    public const int Subtract = 87;
    public const int Decimal = 88;
    public const int Divide = 89;

    public const int F1 = 90;
    public const int F2 = 91;
    public const int F3 = 92;
    public const int F4 = 93;
    public const int F5 = 94;
    public const int F6 = 95;
    public const int F7 = 96;
    public const int F8 = 97;
    public const int F9 = 98;
    public const int F10 = 99;
    public const int F11 = 100;
    public const int F12 = 101;

    public const int OemSemicolon = 140;
    public const int OemPlus = 141;
    public const int OemComma = 142;
    public const int OemMinus = 143;
    public const int OemPeriod = 144;
    public const int OemQuestion = 145;
    public const int OemTilde = 146;
    public const int OemOpenBrackets = 149;
    public const int OemPipe = 150;
    public const int OemCloseBrackets = 151;
    public const int OemQuotes = 152;
}

/// <summary>
/// 模式按键绑定定义
/// </summary>
public readonly record struct ModeKeyBinding(
    string Id,
    int KeyCode,
    KeyModifiers Modifiers,
    string Action,
    string Description
);

public interface IModeHandler
{
    EditMode Mode { get; }
    string DisplayName { get; }
    string HelpText { get; }
    ModifierKind PrimaryModifierKind { get; }
    ModifierKind[] ModifierKinds { get; }

    /// <summary>
    /// 该模式是否需要选择器。鼠标只有一个，同一时刻只有一个模式的选择器处于激活状态。
    /// 切换到需要选择器的模式时会激活选区交互，切换走时自动清空选区并停用。
    /// </summary>
    bool RequiresSelection { get; }

    /// <summary>
    /// 模式特有的按键绑定。切换到此模式时会自动注册，切换走时会自动注销。
    /// 全局按键（如Space切换模式、Tab、F1等）不应在此返回，由渲染层统一管理。
    /// </summary>
    IEnumerable<ModeKeyBinding> GetKeyBindings();

    Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context);

    /// <summary>
    /// 本模式需要打开的渲染层。GUI 在切换模式时据此设置渲染引擎的各个 Enable*Render 开关，
    /// 模式和"要看到哪些层"放在一起声明，新增模式不必再改渲染场景。
    /// </summary>
    ModeRenderLayers RenderLayers => ModeRenderLayers.None;

    /// <summary>
    /// 框选 / 点选的命中过滤（仅 <see cref="RequiresSelection"/> 为 true 时有意义）。
    /// 返回 null 表示任何格子都能被选中。
    /// </summary>
    Func<HexCoord, bool>? CreateSelectionFilter(MapData map) => null;

    /// <summary>
    /// 鼠标手势对应的模式动作名。GUI 做完通用处理（选区、画笔等）后，
    /// 若本方法返回非 null，就用 <see cref="HandleKeyAction"/> 把该动作分发给模式。
    /// 返回 null 表示本模式不处理这个手势。
    /// </summary>
    string? GetGestureAction(ModeGesture gesture) => null;
}