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

    // 归属编辑只改格子归属（PrimaryModifierKind = Belong），但同时引入建筑、单位、陷阱模块：
    // 这些实体在本模式下可见、可被选中，用于人工核对归属是否与驻地实体匹配
    // （对齐 VB 版归属模式：国家领域/建筑/单位/陷阱/归属国旗层全开）。
    public ModifierKind[] ModifierKinds => new[]
    {
        ModifierKind.Building, ModifierKind.Army, ModifierKind.Belong, ModifierKind.Trap
    };

    // 左键单击单选、右键单击/拖动多选（Shift 加选 / Ctrl 减选）。
    // 选择器只产出选中集合，具体编辑动作仍由本模式的按键（Q 设置归属等）完成。
    public bool RequiresSelection => true;

    public string HelpText =>
        "归属编辑模式:\n" +
        "  左键 - 选择格子\n" +
        "  右键 - 设置归属（画笔模式下按住右键绘制）\n" +
        "  右键拖动 - 框选多格（Shift 加选，Ctrl 减选）\n" +
        "  Q - 设置归属（从归属列表中选择）\n" +
        "  C - 复制归属\n" +
        "  V - 粘贴归属\n" +
        "  X - 删除归属\n" +
        "  G - 检查并清理归属（无归属时取最近归属）\n" +
        "  R - 随机化归属\n" +
        "  T - 批量更改归属\n" +
        "  Delete - 清理所有归属\n" +
        "  E - 删除地图边缘的建筑、单位与陷阱\n" +
        "  L - 切换国家领域显示\n" +
        "  H - 切换画笔模式\n" +
        "  [ - 减小画笔半径（仅画笔模式）\n" +
        "  ] - 增大画笔半径（仅画笔模式）\n" +
        "  空格 - 切换编辑模式（全局）\n" +
        "  建筑/单位/陷阱层同时可见，便于核对归属";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("BE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清理所有归属"),
            new ModeKeyBinding("BE_C", KeyCodes.C, KeyModifiers.None, "copy", "复制归属"),
            new ModeKeyBinding("BE_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴归属"),
            new ModeKeyBinding("BE_X", KeyCodes.X, KeyModifiers.None, "delete_belong", "删除归属"),
            new ModeKeyBinding("BE_G", KeyCodes.G, KeyModifiers.None, "clean_orphan", "检查并清理归属"),
            new ModeKeyBinding("BE_R", KeyCodes.R, KeyModifiers.None, "randomize", "随机化归属"),
            new ModeKeyBinding("BE_T", KeyCodes.T, KeyModifiers.None, "batch_set", "批量更改归属"),
            // 对齐 VB 实现：VB 的 HandleKeyDown 只有 E 键 → DeleteBorderUnitsAndBuildings
            //（删除边缘单位与建筑），没有 B 键分支；仅删除单位的 DeleteBorderUnitsOnly
            // 在 VB 中是保留但未绑定的方法，这里同样保留方法、不绑定按键。
            new ModeKeyBinding("BE_E", KeyCodes.E, KeyModifiers.None, "remove_border_entities", "删除地图边缘的建筑、单位与陷阱"),
            new ModeKeyBinding("BE_L", KeyCodes.L, KeyModifiers.None, "toggle_domain", "切换国家领域显示"),
            new ModeKeyBinding("BE_H", KeyCodes.H, KeyModifiers.None, "toggle_brush", "切换画笔模式"),
            new ModeKeyBinding("BE_Q", KeyCodes.Q, KeyModifiers.None, "select_legion", "设置归属（归属列表）"),
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
                context.RecordBelongChange(col, row, $"设置归属 ({col},{row})", () => belong.Apply(col, row));
                modified = true;
                break;

            case "copy":
                belong.CopyBelongValue(col, row);
                context.RaiseStatusMessage?.Invoke("已复制归属值");
                return true;

            case "paste":
                context.RecordBelongChange(col, row, $"粘贴归属 ({col},{row})", () => belong.PasteBelongValue(col, row));
                modified = true;
                break;

            case "delete_belong":
                context.RecordBelongChange(col, row, $"删除归属 ({col},{row})", () => belong.Remove(col, row));
                modified = true;
                break;

            case "remove":
                {
                    if (context.MapData == null) return false;
                    if (context.DialogService != null)
                    {
                        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要清理所有归属吗？\n（可用 Ctrl+Z 撤销）", "确定", "取消");
                        if (!confirmed) return false;
                    }
                    int count = 0;
                    context.RecordMultiCellBelongChange("清理所有归属", () => count = belong.ClearAllBelongs());
                    context.RaiseStatusMessage?.Invoke($"已清理所有归属 ({count} 个格子)");
                    modified = count > 0;
                    break;
                }

            case "clean_orphan":
                {
                    var count = belong.CleanOrphanBelongs();
                    var stats = belong.LastCleanStats;
                    context.RaiseStatusMessage?.Invoke(
                        $"已清理 {count} 个无实体格子的归属（省区同步 {stats.ProvinceSynced} 个，邻近归属填充 {stats.NearestFilled} 个，无归属修复 {stats.BelongFixed} 个）");
                    modified = count > 0 || stats.BelongFixed > 0 || stats.ProvinceSynced > 0 || stats.NearestFilled > 0;
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
                    int count = 0;
                    context.RecordMultiCellBelongChange($"随机化归属 (归属ID={targetBelongId})",
                        () => count = belong.RandomizeBelongByTargetId(targetBelongId, _random));
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
                    int count = 0;
                    context.RecordMultiCellBelongChange($"批量更改归属 ({oldBelongId} → {newBelongId})",
                        () => count = belong.ReplaceBelong(oldBelongId, newBelongId));
                    context.RaiseStatusMessage?.Invoke($"已将 {count} 个格子的归属从 {oldBelongId} 改为 {newBelongId}");
                    modified = count > 0;
                    break;
                }

            case "remove_border_entities":
                {
                    var count = belong.RemoveBorderEntities();
                    context.RaiseStatusMessage?.Invoke($"已删除地图边缘 {count} 个建筑、单位与陷阱");
                    modified = count > 0;
                    break;
                }

            // 仅删除边缘单位（对应 VB 的 DeleteBorderUnitsOnly）。
            // 对齐 VB：该方法在 VB 中未被绑定按键，这里同样保留能力但不占用键位。
            case "remove_border_armies":
                {
                    var count = belong.RemoveBorderArmies();
                    context.RaiseStatusMessage?.Invoke($"已删除地图边缘 {count} 个单位");
                    modified = count > 0;
                    break;
                }

            case "toggle_domain":
                // 对齐 VB 帮助文本「L: 切换国家领域显示」：切换归属着色与归属国旗层，
                // 而不是切换画笔（此前误接成 NotifyBrushToggled）。
                context.NotifyToggleDomain?.Invoke();
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
                    context.RecordBelongChange(col, row, $"设置归属 ({col},{row})",
                        () => belong.SetBelongByCountryId(col, row, dlgResult.belongValue));
                    context.RaiseStatusMessage?.Invoke($"已设置归属 ({col},{row}) = {dlgResult.belongValue}");
                    modified = true;
                    break;
                }

            case "brush_smaller":
                {
                    // 对齐 VB：仅在画笔模式下生效，最小半径 0
                    var belongModifier = context.GetModifier<BelongModifier>();
                    if (belongModifier != null && belongModifier.Brush.Active)
                    {
                        belongModifier.Brush.DecreaseRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径减小为: {belongModifier.Brush.Radius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                        return true;
                    }
                    return false;
                }

            case "brush_bigger":
                {
                    // 对齐 VB：仅在画笔模式下生效，半径无上限
                    var belongModifier = context.GetModifier<BelongModifier>();
                    if (belongModifier != null && belongModifier.Brush.Active)
                    {
                        belongModifier.Brush.IncreaseRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径增大为: {belongModifier.Brush.Radius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                        return true;
                    }
                    return false;
                }
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }
}