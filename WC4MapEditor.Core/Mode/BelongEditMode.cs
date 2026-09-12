using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class BelongEditMode : IModeHandler
{
    private readonly Random _random = new();

    public EditMode Mode => EditMode.BelongEdit;
    public string DisplayName => "归属编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Belong;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Belong };
    public bool RequiresSelection => true;

    public string HelpText =>
        "归属编辑模式:\n" +
        "  左键 - 选择格子\n" +
        "  右键 - 设置归属\n" +
        "  Q - 选择军团\n" +
        "  C - 复制归属\n" +
        "  V - 粘贴归属\n" +
        "  X - 删除归属\n" +
        "  G - 检查并清理归属\n" +
        "  R - 随机化归属\n" +
        "  T - 批量更改归属\n" +
        "  Delete - 清理所有归属\n" +
        "  B - 删除边界单位与建筑\n" +
        "  E - 删除边界上单位\n" +
        "  L - 切换国家领域显示\n" +
        "  H - 切换画笔模式\n" +
        "  [ - 减小画笔半径\n" +
        "  ] - 增大画笔半径\n" +
        "  空格 - 设置归属";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("BE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清理所有归属"),
            new ModeKeyBinding("BE_C", KeyCodes.C, KeyModifiers.None, "copy", "复制归属"),
            new ModeKeyBinding("BE_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴归属"),
            new ModeKeyBinding("BE_X", KeyCodes.X, KeyModifiers.None, "delete_belong", "删除归属"),
            new ModeKeyBinding("BE_G", KeyCodes.G, KeyModifiers.None, "clean_orphan", "检查并清理孤立归属"),
            new ModeKeyBinding("BE_R", KeyCodes.R, KeyModifiers.None, "randomize", "随机化归属"),
            new ModeKeyBinding("BE_T", KeyCodes.T, KeyModifiers.None, "batch_set", "批量更改归属"),
            new ModeKeyBinding("BE_B", KeyCodes.B, KeyModifiers.None, "remove_border_entities", "删除边界单位与建筑"),
            new ModeKeyBinding("BE_E", KeyCodes.E, KeyModifiers.None, "remove_border_armies", "删除边界上单位"),
            new ModeKeyBinding("BE_L", KeyCodes.L, KeyModifiers.None, "toggle_domain", "切换国家领域显示"),
            new ModeKeyBinding("BE_H", KeyCodes.H, KeyModifiers.None, "toggle_brush", "切换画笔模式"),
            new ModeKeyBinding("BE_Q", KeyCodes.Q, KeyModifiers.None, "select_legion", "选择军团"),
            new ModeKeyBinding("BE_BracketL", KeyCodes.OemOpenBrackets, KeyModifiers.None, "brush_smaller", "缩小画笔"),
            new ModeKeyBinding("BE_BracketR", KeyCodes.OemCloseBrackets, KeyModifiers.None, "brush_bigger", "放大画笔"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var belong = context.GetModifier<BelongModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置归属 ({col},{row})", () => belong.Apply(col, row));
                modified = true;
                break;

            case "copy":
                belong.CopyBelongValue(col, row);
                context.RaiseStatusMessage?.Invoke("已复制归属值");
                return true;

            case "paste":
                context.RecordProvinceChange(col, row, $"粘贴归属 ({col},{row})", () => belong.PasteBelongValue(col, row));
                modified = true;
                break;

            case "delete_belong":
                context.RecordProvinceChange(col, row, $"删除归属 ({col},{row})", () => belong.Remove(col, row));
                modified = true;
                break;

            case "remove":
                {
                    if (context.MapData == null) return false;
                    if (context.DialogService != null)
                    {
                        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要清理所有归属吗？\n此操作不可撤销！", "确定", "取消");
                        if (!confirmed) return false;
                    }
                    var count = belong.ClearAllBelongs();
                    context.RaiseStatusMessage?.Invoke($"已清理所有归属 ({count} 个格子)");
                    modified = count > 0;
                    break;
                }

            case "clean_orphan":
                {
                    var count = belong.CleanOrphanBelongs();
                    context.RaiseStatusMessage?.Invoke($"已清理 {count} 个孤立归属（无建筑/单位的格子）");
                    modified = count > 0;
                    break;
                }

            case "randomize":
                {
                    if (context.DialogService == null)
                    {
                        context.RaiseStatusMessage?.Invoke("对话框服务未初始化");
                        return false;
                    }
                    var input = await context.DialogService.ShowInputDialogAsync(
                        "随机化归属", "请输入要随机化的归属ID (0-255):",
                        belong.SelectedCountryId.ToString(), 0, 255);
                    if (input == null || !int.TryParse(input, out int targetBelongId)) return false;
                    var count = belong.RandomizeBelongByTargetId(targetBelongId, _random);
                    context.RaiseStatusMessage?.Invoke($"已随机化 {count} 个归属ID为 {targetBelongId} 的格子");
                    modified = count > 0;
                    break;
                }

            case "batch_set":
                {
                    if (context.DialogService == null)
                    {
                        context.RaiseStatusMessage?.Invoke("对话框服务未初始化");
                        return false;
                    }
                    var result = await context.DialogService.ShowDoubleInputDialogAsync(
                        "批量更改归属", "原归属ID (0-255):", "新归属ID (0-255):",
                        "0", belong.SelectedCountryId.ToString());
                    if (!result.confirmed || result.value1 == null || result.value2 == null) return false;
                    if (!int.TryParse(result.value1, out int oldBelongId) || !int.TryParse(result.value2, out int newBelongId))
                    {
                        context.RaiseStatusMessage?.Invoke("输入无效，请输入0-255的整数");
                        return false;
                    }
                    var count = belong.ReplaceBelong(oldBelongId, newBelongId);
                    context.RaiseStatusMessage?.Invoke($"已将 {count} 个格子的归属从 {oldBelongId} 改为 {newBelongId}");
                    modified = count > 0;
                    break;
                }

            case "remove_border_entities":
                {
                    var count = belong.RemoveBorderEntities();
                    context.RaiseStatusMessage?.Invoke($"已删除 {count} 个边界上的单位与建筑");
                    modified = count > 0;
                    break;
                }

            case "remove_border_armies":
                {
                    var count = belong.RemoveBorderArmies();
                    context.RaiseStatusMessage?.Invoke($"已删除 {count} 个边界上的单位");
                    modified = count > 0;
                    break;
                }

            case "toggle_domain":
                context.NotifyBrushToggled?.Invoke();
                context.RaiseStatusMessage?.Invoke("国家领域显示已切换");
                return true;

            case "toggle_brush":
                context.NotifyBrushToggled?.Invoke();
                return true;

            case "select_legion":
                {
                    if (context.DialogService == null || context.MapData == null)
                    {
                        context.RaiseStatusMessage?.Invoke("对话框服务未初始化");
                        return false;
                    }
                    var legions = context.MapData.Legions.ToList();
                    if (legions.Count == 0)
                    {
                        context.RaiseStatusMessage?.Invoke("没有可用的军团数据");
                        return false;
                    }
                    var dlgResult = await context.DialogService.ShowBelongListDialogAsync(legions);
                    if (!dlgResult.confirmed) return false;
                    belong.SelectedCountryId = dlgResult.belongValue;
                    context.RecordProvinceChange(col, row, $"设置归属 ({col},{row})",
                        () => belong.SetBelongByCountryId(col, row, dlgResult.belongValue));
                    context.RaiseStatusMessage?.Invoke($"已设置归属 ({col},{row}) = {dlgResult.belongValue}");
                    modified = true;
                    break;
                }

            case "brush_smaller":
                {
                    var belongModifier = context.GetModifier<BelongModifier>();
                    if (belongModifier != null)
                    {
                        belongModifier.Brush.Radius = Math.Max(1, belongModifier.Brush.Radius - 1);
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {belongModifier.Brush.Radius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                    return true;
                }

            case "brush_bigger":
                {
                    var belongModifier = context.GetModifier<BelongModifier>();
                    if (belongModifier != null)
                    {
                        belongModifier.Brush.Radius = Math.Min(10, belongModifier.Brush.Radius + 1);
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {belongModifier.Brush.Radius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                    return true;
                }
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }
}