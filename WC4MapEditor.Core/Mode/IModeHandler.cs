using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

public interface IModeHandler
{
    EditMode Mode { get; }
    string DisplayName { get; }
    string HelpText { get; }
    ModifierKind PrimaryModifierKind { get; }
    ModifierKind[] ModifierKinds { get; }

    Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context);
}