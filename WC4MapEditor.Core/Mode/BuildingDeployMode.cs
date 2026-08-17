using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class BuildingDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.BuildingDeploy;
    public string DisplayName => "建筑部署";
    public ModifierKind PrimaryModifierKind => ModifierKind.Building;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Building, ModifierKind.Province };

    public string HelpText =>
        "建筑部署模式快捷键:\n" +
        "右键 - 放置建筑\n" +
        "Delete - 删除建筑\n" +
        "C - 复制建筑\n" +
        "V - 粘贴建筑";

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var building = context.GetModifier<BuildingModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                context.RecordEntityChange($"放置建筑 ({col},{row})", () => building.Apply(col, row), () => building.Remove(col, row));
                modified = true; break;
            case "copy":
                building.CopyBuilding(col, row);
                return Task.FromResult(true);
            case "paste":
                context.RecordEntityChange($"粘贴建筑 ({col},{row})", () => building.PasteBuilding(col, row), () => building.Remove(col, row));
                modified = true; break;
            case "remove":
                {
                    var oldBuilding = context.MapData?.GetBuildingAt(col, row);
                    if (oldBuilding != null)
                    {
                        var saved = oldBuilding.Value;
                        context.RecordEntityChange($"删除建筑 ({col},{row})",
                            () => building.Remove(col, row),
                            () => building.Apply(col, row, saved));
                    }
                    else
                    {
                        building.Remove(col, row);
                    }
                    modified = true; break;
                }
        }
        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}