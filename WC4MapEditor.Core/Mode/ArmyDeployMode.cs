using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class ArmyDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.ArmyDeploy;
    public string DisplayName => "单位部署";
    public ModifierKind PrimaryModifierKind => ModifierKind.Army;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Army, ModifierKind.Belong };

    public string HelpText =>
        "单位部署模式快捷键:\n" +
        "右键 - 放置单位\n" +
        "Delete - 删除单位\n" +
        "C - 复制单位\n" +
        "V - 粘贴单位\n" +
        "L - 按归属设置军团";

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var army = context.GetModifier<ArmyModifier>()!;
        var belong = context.GetModifier<BelongModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                context.RecordEntityChange($"放置单位 ({col},{row})", () => army.Apply(col, row), () => army.Remove(col, row));
                modified = true; break;
            case "copy":
                army.CopyArmy(col, row);
                return Task.FromResult(true);
            case "paste":
                context.RecordEntityChange($"粘贴单位 ({col},{row})", () => army.PasteArmy(col, row), () => army.Remove(col, row));
                modified = true; break;
            case "remove":
                {
                    var oldArmy = context.MapData?.GetArmyAt(col, row);
                    if (oldArmy != null)
                    {
                        var saved = oldArmy.Value;
                        context.RecordEntityChange($"删除单位 ({col},{row})",
                            () => army.Remove(col, row),
                            () => army.Apply(col, row, saved));
                    }
                    else
                    {
                        army.Remove(col, row);
                    }
                    modified = true; break;
                }
            case "set_legion":
                int legionId = belong.GetBelongValue(col, row) ?? 0;
                context.RecordEntityChange($"设置军团 ({col},{row})", () => army.SetLegionId(col, row, legionId), () => { });
                modified = true; break;
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}