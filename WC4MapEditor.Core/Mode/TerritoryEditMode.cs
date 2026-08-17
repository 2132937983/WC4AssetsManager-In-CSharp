using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class TerritoryEditMode : IModeHandler
{
    public EditMode Mode => EditMode.TerritoryEdit;
    public string DisplayName => "领域编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Legion;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Province, ModifierKind.Legion, ModifierKind.Belong };

    public string HelpText =>
        "领域编辑模式快捷键:\n" +
        "右键 - 设置领域(军团)\n" +
        "Delete - 清除领域\n" +
        "Q / E - 切换军团\n" +
        "C - 复制归属\n" +
        "V - 粘贴归属\n" +
        "S - 设置省份值\n" +
        "X - 清除省份值";

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var province = context.GetModifier<ProvinceModifier>()!;
        var legion = context.GetModifier<LegionModifier>()!;
        var belong = context.GetModifier<BelongModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置军团领域 ({col},{row})", () => legion.Apply(col, row));
                modified = true; break;
            case "remove":
                context.RecordProvinceChange(col, row, $"清除军团领域 ({col},{row})", () => legion.Remove(col, row));
                modified = true; break;
            case "copy":
                belong.CopyBelongValue(col, row);
                return Task.FromResult(true);
            case "paste":
                context.RecordProvinceChange(col, row, $"粘贴归属 ({col},{row})", () => belong.PasteBelongValue(col, row));
                modified = true; break;
            case "next_legion":
                legion.SelectedLegionId = (legion.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);
            case "prev_legion":
                legion.SelectedLegionId = ((legion.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);
            case "set_province":
                context.RecordProvinceChange(col, row, $"设置省份 ({col},{row})", () => province.Apply(col, row));
                modified = true; break;
            case "clear_province":
                context.RecordProvinceChange(col, row, $"清除省份 ({col},{row})", () => province.Remove(col, row));
                modified = true; break;
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}