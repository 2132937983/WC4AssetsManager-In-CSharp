using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 军团编辑模式 - 对齐 VB 版 Builder/LegionModifier。
/// <para>
/// 键位与 VB 的 GetLegionEditModeKeys 一致：P/Q/F/C/U/R/F6/E/I/X。
/// 首都编辑属于本模式的一部分（对齐 VB LegionModifier 的首都编辑能力），
/// F / X 键都直接对"当前选中格子"添加或删除首都，不设独立的首都编辑模式。
/// 军团数据本身的读写由 <see cref="LegionModifier"/> 承担，
/// 需要界面的部分（编辑器窗口、头部数据窗口、截图、征服配置）通过 ModeContext 回调交给 GUI 层。
/// </para>
/// </summary>
public sealed class LegionEditMode : IModeHandler
{
    public EditMode Mode => EditMode.LegionEdit;
    public string DisplayName => "军团编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Legion;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Legion };
    // 启用选择器：左键单击选择格子（Shift 加选 / Ctrl 减选），右键拖动框选多格。
    // 对应 VB 版 LegionModifier.HandleMouseDown 左键设置 _selectedHex 的行为。
    public bool RequiresSelection => true;

    public string HelpText =>
        "=== 军团编辑模式 ===\n" +
        "左键 - 选择格子（Shift 加选 / Ctrl 减选）\n" +
        "右键 - 查看军团信息；右键拖动 - 框选多格\n" +
        "P - 进行军团范围截图\n" +
        "Q - 打开军团编辑器\n" +
        "F - 在当前选中格子添加/删除首都（同 X）\n" +
        "C - 应用默认颜色到所有军团\n" +
        "U - 从setting.txt匹配颜色应用到所有军团\n" +
        "R - 随机化所有军团等级与经济\n" +
        "F6 - 更新征服国家设置\n" +
        "E - 打开头部数据编辑器\n" +
        "I - 修改所有军团的行动顺序和归属列表\n" +
        "X - 在当前选中格子添加/删除首都\n" +
        "[ / ] - 切换当前军团\n" +
        "ESC - 退出军团编辑模式";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("LE_P", KeyCodes.P, KeyModifiers.None, "capture_screenshot", "进行军团范围截图"),
            new ModeKeyBinding("LE_Q", KeyCodes.Q, KeyModifiers.None, "open_legion_setting", "打开军团编辑器"),
            new ModeKeyBinding("LE_F", KeyCodes.F, KeyModifiers.None, "toggle_capital", "在当前选中格子添加/删除首都"),
            new ModeKeyBinding("LE_C", KeyCodes.C, KeyModifiers.None, "apply_default_colors", "应用默认颜色到所有军团"),
            new ModeKeyBinding("LE_U", KeyCodes.U, KeyModifiers.None, "apply_settings_colors", "从setting.txt匹配颜色应用到所有军团"),
            new ModeKeyBinding("LE_R", KeyCodes.R, KeyModifiers.None, "randomize_levels", "随机化所有军团等级与经济"),
            new ModeKeyBinding("LE_F6", KeyCodes.F6, KeyModifiers.None, "update_conquer_settings", "更新征服国家设置"),
            new ModeKeyBinding("LE_E", KeyCodes.E, KeyModifiers.None, "open_header_setting", "打开头部数据编辑器"),
            new ModeKeyBinding("LE_I", KeyCodes.I, KeyModifiers.None, "rebuild_action_belong", "修改所有军团的行动顺序和归属列表"),
            new ModeKeyBinding("LE_X", KeyCodes.X, KeyModifiers.None, "toggle_capital", "在当前选中格子添加/删除首都"),
            new ModeKeyBinding("LE_BracketOpen", KeyCodes.OemOpenBrackets, KeyModifiers.None, "prev_legion", "上一个军团"),
            new ModeKeyBinding("LE_BracketClose", KeyCodes.OemCloseBrackets, KeyModifiers.None, "next_legion", "下一个军团"),
        };
    }

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var legion = context.GetModifier<LegionModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置军团领域 ({col},{row})", () => legion.Apply(col, row));
                modified = true;
                break;

            case "remove":
                context.RecordProvinceChange(col, row, $"清除军团领域 ({col},{row})", () => legion.Remove(col, row));
                modified = true;
                break;

            // ---------------- 需要界面配合的功能（交给 GUI 层） ----------------

            case "capture_screenshot":
                context.NotifyCaptureLegionScreenshot?.Invoke();
                return Task.FromResult(true);

            case "open_legion_setting":
                context.NotifyOpenLegionSetting?.Invoke();
                return Task.FromResult(true);

            case "open_legion_list":
                context.NotifyOpenLegionList?.Invoke();
                return Task.FromResult(true);

            case "open_header_setting":
                context.NotifyOpenHeaderSetting?.Invoke();
                return Task.FromResult(true);

            case "update_conquer_settings":
                context.NotifyUpdateConquerSettings?.Invoke();
                return Task.FromResult(true);

            // ---------------- 纯数据操作 ----------------

            case "apply_default_colors":
                {
                    var result = legion.ApplyDefaultColorsToAllLegions();
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已应用默认颜色到所有军团");
                    modified = result.Success;
                    break;
                }

            case "apply_settings_colors":
                {
                    var result = legion.ApplyAllLegionsColorFromSettings();
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已从配置更新军团颜色");
                    modified = result.Success;
                    break;
                }

            case "randomize_levels":
                {
                    var result = legion.RandomizeAllLegionLevels();
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已随机化所有军团等级与经济");
                    modified = result.Success;
                    break;
                }

            case "rebuild_action_belong":
                {
                    var result = legion.UpdateAllLegionsActionIdAndBelong();
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已修改所有军团的行动顺序与归属");
                    modified = result.Success;
                    break;
                }

            case "toggle_capital":
                {
                    // F / X 键：直接对当前选中格子添加或删除首都（col/row 来自选择器焦点格）。
                    if (context.MapData == null) return Task.FromResult(false);
                    int hexIndex = row * context.MapData.MapWidth + col;
                    var result = legion.ToggleCapital(hexIndex);
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已切换首都");
                    modified = result.Success;
                    break;
                }

            // ---------------- 当前军团切换（VB 中通过列表窗口选择，这里保留快捷切换） ----------------

            case "next_legion":
                legion.SelectedLegionId = (legion.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);

            case "prev_legion":
                legion.SelectedLegionId = ((legion.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}
