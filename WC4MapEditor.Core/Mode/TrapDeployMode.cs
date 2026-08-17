using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class TrapDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.TrapDeploy;
    public string DisplayName => "陷阱布置";
    public ModifierKind PrimaryModifierKind => ModifierKind.Trap;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Trap, ModifierKind.Belong };

    public string HelpText =>
        "陷阱布置模式快捷键:\n" +
        "右键 - 放置陷阱\n" +
        "Delete - 删除陷阱\n" +
        "C - 复制陷阱\n" +
        "V - 粘贴陷阱\n" +
        "Q / E - 切换所属军团";

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var trap = context.GetModifier<TrapModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                context.RecordEntityChange($"放置陷阱 ({col},{row})", () => trap.Apply(col, row), () => trap.Remove(col, row));
                modified = true; break;
            case "copy":
                trap.CopyTrap(col, row);
                return Task.FromResult(true);
            case "paste":
                context.RecordEntityChange($"粘贴陷阱 ({col},{row})", () => trap.PasteTrap(col, row), () => trap.Remove(col, row));
                modified = true; break;
            case "remove":
                context.RecordEntityChange($"删除陷阱 ({col},{row})", () => trap.Remove(col, row), () => trap.Apply(col, row));
                modified = true; break;
            case "next_legion":
                trap.SelectedLegionId = (trap.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"陷阱所属军团: {trap.SelectedLegionId}");
                return Task.FromResult(true);
            case "prev_legion":
                trap.SelectedLegionId = ((trap.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"陷阱所属军团: {trap.SelectedLegionId}");
                return Task.FromResult(true);
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}