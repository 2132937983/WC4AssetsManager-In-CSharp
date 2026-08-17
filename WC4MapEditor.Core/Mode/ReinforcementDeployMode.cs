using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class ReinforcementDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.ReinforcementDeploy;
    public string DisplayName => "援军配置";
    public ModifierKind PrimaryModifierKind => ModifierKind.Reinforcement;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Reinforcement, ModifierKind.Legion };

    public string HelpText =>
        "援军配置模式快捷键:\n" +
        "右键 - 放置援军\n" +
        "Delete - 删除援军\n" +
        "C - 复制援军\n" +
        "V - 粘贴援军\n" +
        "Q / E - 切换所属军团";

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var reinforcement = context.GetModifier<ReinforcementModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                context.RecordEntityChange($"放置援军 ({col},{row})", () => reinforcement.Apply(col, row), () => reinforcement.Remove(col, row));
                modified = true; break;
            case "copy":
                reinforcement.CopyReinforcement(col, row);
                return Task.FromResult(true);
            case "paste":
                context.RecordEntityChange($"粘贴援军 ({col},{row})", () => reinforcement.PasteReinforcement(col, row), () => reinforcement.Remove(col, row));
                modified = true; break;
            case "remove":
                context.RecordEntityChange($"删除援军 ({col},{row})", () => reinforcement.Remove(col, row), () => reinforcement.Apply(col, row));
                modified = true; break;
            case "next_legion":
                reinforcement.SelectedLegionId = (reinforcement.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"援军所属军团: {reinforcement.SelectedLegionId}");
                return Task.FromResult(true);
            case "prev_legion":
                reinforcement.SelectedLegionId = ((reinforcement.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"援军所属军团: {reinforcement.SelectedLegionId}");
                return Task.FromResult(true);
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}