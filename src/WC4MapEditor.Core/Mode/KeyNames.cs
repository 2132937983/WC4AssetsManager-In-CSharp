using System.Globalization;
using System.Text;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Input;

namespace WC4MapEditor.Core.Mode;

/// <summary>按键与鼠标手势在帮助文本里的写法，例如 "Ctrl+Q"、"["、"F4"、"右键"。</summary>
public static class KeyNames
{
    public static string Format(KeyChord chord) => Format(chord.KeyCode, chord.Modifiers);

    public static string Format(int keyCode, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None) return Name(keyCode);
        var sb = new StringBuilder();
        if ((modifiers & KeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
        if ((modifiers & KeyModifiers.Shift) != 0) sb.Append("Shift+");
        if ((modifiers & KeyModifiers.Alt) != 0) sb.Append("Alt+");
        return sb.Append(Name(keyCode)).ToString();
    }

    public static string Name(int keyCode)
    {
        if (keyCode >= KeyCodes.A && keyCode <= KeyCodes.Z) return ((char)('A' + (keyCode - KeyCodes.A))).ToString();
        if (keyCode >= KeyCodes.D0 && keyCode <= KeyCodes.D9) return ((char)('0' + (keyCode - KeyCodes.D0))).ToString();
        if (keyCode >= KeyCodes.NumPad0 && keyCode <= KeyCodes.NumPad9) return "Num" + (keyCode - KeyCodes.NumPad0).ToString(CultureInfo.InvariantCulture);
        if (keyCode >= KeyCodes.F1 && keyCode <= KeyCodes.F12) return "F" + (keyCode - KeyCodes.F1 + 1).ToString(CultureInfo.InvariantCulture);

        return keyCode switch
        {
            KeyCodes.Space => Loc.T("Key.Space", "空格"),
            KeyCodes.Tab => "Tab",
            KeyCodes.Enter => "Enter",
            KeyCodes.Escape => "Esc",
            KeyCodes.Delete => "Delete",
            KeyCodes.Back => "Backspace",
            KeyCodes.Insert => "Insert",
            KeyCodes.Home => "Home",
            KeyCodes.End => "End",
            KeyCodes.PageUp => "PageUp",
            KeyCodes.PageDown => "PageDown",
            KeyCodes.Left => Loc.T("Key.Left", "←"),
            KeyCodes.Up => Loc.T("Key.Up", "↑"),
            KeyCodes.Right => Loc.T("Key.Right", "→"),
            KeyCodes.Down => Loc.T("Key.Down", "↓"),
            KeyCodes.OemOpenBrackets => "[",
            KeyCodes.OemCloseBrackets => "]",
            KeyCodes.OemComma => ",",
            KeyCodes.OemPeriod => ".",
            KeyCodes.OemPlus => "=",
            KeyCodes.OemMinus => "-",
            KeyCodes.OemQuestion => "/",
            KeyCodes.OemSemicolon => ";",
            KeyCodes.OemQuotes => "'",
            KeyCodes.OemTilde => "`",
            KeyCodes.OemPipe => "\\",
            KeyCodes.Add => "Num+",
            KeyCodes.Subtract => "Num-",
            KeyCodes.Multiply => "Num*",
            KeyCodes.Divide => "Num/",
            KeyCodes.Decimal => "Num.",
            _ => "#" + keyCode.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static string Gesture(ModeGesture gesture) => gesture switch
    {
        ModeGesture.LeftClick => Loc.T("Key.Mouse.LeftClick", "左键"),
        ModeGesture.RightClick => Loc.T("Key.Mouse.RightClick", "右键"),
        ModeGesture.RightDrag => Loc.T("Key.Mouse.RightDrag", "右键拖动"),
        ModeGesture.LeftDrag => Loc.T("Key.Mouse.LeftDrag", "左键拖动"),
        ModeGesture.Wheel => Loc.T("Key.Mouse.Wheel", "滚轮"),
        ModeGesture.Escape => "Esc",
        _ => gesture.ToString()
    };
}
