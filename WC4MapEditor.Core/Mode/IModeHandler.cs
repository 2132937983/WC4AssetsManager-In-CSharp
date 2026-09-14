using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 常用按键的 WPF Key 枚举值常量
/// </summary>
public static class KeyCodes
{
    public const int Space = 18;
    public const int Tab = 3;
    public const int Enter = 6;
    public const int Escape = 13;
    public const int Delete = 32;
    public const int F1 = 90;
    public const int F4 = 93;
    public const int F5 = 94;
    public const int F6 = 95;
    public const int F7 = 96;
    public const int F8 = 97;
    public const int F9 = 98;
    public const int F10 = 99;
    public const int H = 51;
    public const int C = 46;
    public const int V = 65;
    public const int O = 58;
    public const int Y = 68;
    public const int P = 59;
    public const int U = 64;
    public const int R = 61;
    public const int F = 49;
    public const int T = 63;
    public const int G = 50;
    public const int I = 52;
    public const int J = 53;
    public const int K = 54;
    public const int L = 55;
    public const int Q = 60;
    public const int E = 48;
    public const int S = 62;
    public const int X = 67;
    public const int Z = 69;
    public const int B = 45;
    public const int N = 57;
    public const int OemOpenBrackets = 149;
    public const int OemCloseBrackets = 151;
    public const int OemComma = 145;
    public const int OemPeriod = 147;
    public const int OemPlus = 141;
    public const int D1 = 34;
    public const int D2 = 35;
    public const int D3 = 36;
    public const int D4 = 37;
    public const int D5 = 38;
    public const int D6 = 39;
    public const int D7 = 40;
    public const int D8 = 41;
    public const int D9 = 42;
    public const int D0 = 43;
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
}