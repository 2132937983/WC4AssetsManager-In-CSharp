using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 军团编辑模式 - 对齐 VB 版 Builder/LegionModifier。
/// <para>
/// 键位与 VB 的 GetLegionEditModeKeys 一致：P/Q/C/U/R/F6/E/I/X（F 键暂不占用）。
/// 首都编辑是<b>独立模块</b>（<see cref="CapitalModifier"/>），本模式通过
/// <see cref="ModifierKinds"/> 把它引进来到此使用 —— X 键直接对当前选中格子
/// 添加/取消首都，不需要先进什么"首都编辑模式"再用鼠标点。
/// 军团数据本身的读写由 <see cref="LegionModifier"/> 承担，
/// 需要界面的部分（编辑器窗口、头部数据窗口、截图、征服配置）通过 ModeContext 回调交给 GUI 层。
/// </para>
/// </summary>
public sealed class LegionEditMode : IModeHandler
{
    public EditMode Mode => EditMode.LegionEdit;
    public string DisplayName => "军团编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Legion;
    // 军团编辑模式引入首都编辑模块（独立模块），在本模式内即可编辑首都
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Legion, ModifierKind.Capital };
    // 启用选择器：左键单击选择格子（Shift 加选 / Ctrl 减选），右键拖动框选多格。
    // 对应 VB 版 LegionModifier.HandleMouseDown 左键设置 _selectedHex 的行为。
    public bool RequiresSelection => true;

    public string HelpText =>
        "=== 军团编辑模式 ===\n" +
        "左键 - 选择格子（Shift 加选 / Ctrl 减选）\n" +
        "右键 - 查看军团信息；右键拖动 - 框选多格\n" +
        "Delete - 清除当前选中格子的军团领域\n" +
        "Shift+Delete - 删除指定归属的所有单位（可输入归属ID）\n" +
        "P - 进行军团范围截图\n" +
        "Q - 打开军团编辑器\n" +
        "C - 应用默认颜色到所有军团\n" +
        "U - 从setting.txt匹配颜色应用到所有军团\n" +
        "R - 随机化所有军团等级与经济\n" +
        "P - 把当前军团设为玩家军团（控制值 0，其余改为 1）\n" +
        "F4 - 进行军团范围截图\n" +
        "F6 - 更新征服国家设置\n" +
        "E - 打开头部数据编辑器\n" +
        "I - 修改所有军团的行动顺序和归属列表\n" +
        "X - 在当前选中格子添加/取消首都（不改归属）\n" +
        "[ / ] - 切换当前军团\n" +
        "ESC - 退出军团编辑模式";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("LE_P", KeyCodes.P, KeyModifiers.None, "set_player_legion", "把当前军团设为玩家军团"),
            new ModeKeyBinding("LE_F4", KeyCodes.F4, KeyModifiers.None, "capture_screenshot", "进行军团范围截图"),
            new ModeKeyBinding("LE_Q", KeyCodes.Q, KeyModifiers.None, "open_legion_setting", "打开军团编辑器"),
            new ModeKeyBinding("LE_C", KeyCodes.C, KeyModifiers.None, "apply_default_colors", "应用默认颜色到所有军团"),
            new ModeKeyBinding("LE_U", KeyCodes.U, KeyModifiers.None, "apply_settings_colors", "从setting.txt匹配颜色应用到所有军团"),
            new ModeKeyBinding("LE_R", KeyCodes.R, KeyModifiers.None, "randomize_levels", "随机化所有军团等级与经济"),
            new ModeKeyBinding("LE_F6", KeyCodes.F6, KeyModifiers.None, "update_conquer_settings", "更新征服国家设置"),
            new ModeKeyBinding("LE_E", KeyCodes.E, KeyModifiers.None, "open_header_setting", "打开头部数据编辑器"),
            new ModeKeyBinding("LE_I", KeyCodes.I, KeyModifiers.None, "rebuild_action_belong", "修改所有军团的行动顺序和归属列表"),
            new ModeKeyBinding("LE_X", KeyCodes.X, KeyModifiers.None, "toggle_capital", "在当前选中格子添加/删除首都"),
            new ModeKeyBinding("LE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清除当前选中格子的军团领域"),
            // 与单位部署模式的 Shift+Delete 对齐：按归属批量删除单位
            new ModeKeyBinding("LE_ShiftDelete", KeyCodes.Delete, KeyModifiers.Shift, "remove_by_belong", "删除指定归属的所有单位（可输入归属ID）"),
            new ModeKeyBinding("LE_BracketOpen", KeyCodes.OemOpenBrackets, KeyModifiers.None, "prev_legion", "上一个军团"),
            new ModeKeyBinding("LE_BracketClose", KeyCodes.OemCloseBrackets, KeyModifiers.None, "next_legion", "下一个军团"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var legion = context.GetModifier<LegionModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                {
                    // 军团领域就是归属值，必须用 RecordBelongChange 记录撤销：
                    // RecordProvinceChange 比较的是 Province，归属值变化不会产生差异，撤销会失效。
                    string? applyMessage = null;
                    context.RecordBelongChange(col, row, $"设置军团领域 ({col},{row})", () =>
                    {
                        var result = legion.Apply(col, row);
                        applyMessage = result.Message;
                        modified = result.Success;
                    });
                    context.RaiseStatusMessage?.Invoke(applyMessage ?? "已设置军团领域");
                    break;
                }

            case "remove":
                {
                    string? removeMessage = null;
                    context.RecordBelongChange(col, row, $"清除军团领域 ({col},{row})", () =>
                    {
                        var result = legion.Remove(col, row);
                        removeMessage = result.Message;
                        modified = result.Success;
                    });
                    context.RaiseStatusMessage?.Invoke(removeMessage ?? "已清除军团领域");
                    break;
                }

            // ---------------- 需要界面配合的功能（交给 GUI 层） ----------------

            case "remove_by_belong":
                // 需要弹输入框/确认框，本方法不是 async，转交异步实现
                return await RemoveArmiesByBelongAsync(context, col, row);


            case "capture_screenshot":
                context.NotifyCaptureLegionScreenshot?.Invoke();
                return true;

            case "set_player_legion":
                {
                    // P 键：弹出输入框接受军团序号，把该军团设为玩家军团（IsPlayerControlled = 0），
                    // 其余全部改为 AI（= 1）。
                    var mapData = context.MapData;
                    if (mapData == null || mapData.Legions.Count == 0)
                    {
                        context.RaiseStatusMessage?.Invoke("当前地图没有军团");
                        return false;
                    }

                    if (context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "设置玩家军团",
                        $"请输入军团序号（1-{mapData.Legions.Count}）：",
                        legion.SelectedLegionId.ToString(),
                        1, mapData.Legions.Count);
                    if (input == null) return false;

                    if (!int.TryParse(input, out int legionNo))
                    {
                        context.RaiseStatusMessage?.Invoke("输入的军团序号无效");
                        return false;
                    }

                    int index = legionNo - 1;
                    if (index < 0 || index >= mapData.Legions.Count)
                    {
                        context.RaiseStatusMessage?.Invoke(
                            $"军团序号 {legionNo} 超出范围（共 {mapData.Legions.Count} 个军团）");
                        return false;
                    }

                    var target = mapData.Legions[index];
                    var result = legion.SetPlayerControlledLegion(target.ActionId);

                    context.RaiseStatusMessage?.Invoke(result.Success
                        ? $"已设为玩家军团：第 {index + 1} 个（国家 {target.CountryId}，行动 {target.ActionId}），" +
                          $"其余 {mapData.Legions.Count - 1} 个改为 AI"
                        : result.Message);

                    modified = result.Success;
                    break;
                }

            case "open_legion_setting":
                context.NotifyOpenLegionSetting?.Invoke();
                return true;

            case "open_legion_list":
                context.NotifyOpenLegionList?.Invoke();
                return true;

            case "open_header_setting":
                context.NotifyOpenHeaderSetting?.Invoke();
                return true;

            case "update_conquer_settings":
                context.NotifyUpdateConquerSettings?.Invoke();
                return true;

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
                    // F / X 键：对当前选中格子添加或取消首都（col/row 来自选择器焦点格）。
                    // 直接按格切换，不经过任何"首都编辑模式"。
                    if (context.MapData == null) return false;

                    var capital = context.GetModifier<CapitalModifier>()!;
                    int hexIndex = row * context.MapData.MapWidth + col;
                    bool hadCapital = capital.GetCapitalAtPosition(hexIndex).HasValue;

                    // 按"添加/取消"分别记录：ToggleCapital 本身不可逆，
                    // 这样 Ctrl+Z 才能精确回退（而不是再 toggle 一次）。
                    if (hadCapital)
                        context.RecordEntityChange($"取消首都 ({col},{row})",
                            () => capital.RemoveCapital(hexIndex),
                            () => capital.AddCapital(hexIndex));
                    else
                        context.RecordEntityChange($"设置首都 ({col},{row})",
                            () => capital.AddCapital(hexIndex),
                            () => capital.RemoveCapital(hexIndex));

                    context.RaiseStatusMessage?.Invoke(
                        hadCapital ? $"已取消首都 ({col}, {row})" : $"已设置首都 ({col}, {row})");
                    modified = true;
                    break;
                }

            // ---------------- 当前军团切换（VB 中通过列表窗口选择，这里保留快捷切换） ----------------

            case "next_legion":
                legion.SelectedLegionId = (legion.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return true;

            case "prev_legion":
                legion.SelectedLegionId = ((legion.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return true;
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    /// <summary>
    /// Shift+Delete：按归属批量删除单位（对齐单位部署模式的同名功能）。
    /// <para>
    /// <see cref="HandleKeyAction"/> 不是 async，而这里要弹输入框和确认框，
    /// 所以把异步流程单独放这个方法里，按键分支直接转交。
    /// </para>
    /// <para>
    /// 底层 <c>DeleteArmiesByBelong</c> 是按<b>单位自身的 LegionId</b> 精确匹配的，
    /// <c>-1</c> 不对应任何单位（不是"全部"语义），所以这里把默认值兜到 0。
    /// </para>
    /// </summary>
    private static async Task<bool> RemoveArmiesByBelongAsync(ModeContext context, int col, int row)
    {
        if (context.DialogService == null) return false;

        int suggested = ResolveTargetBelong(context, col, row);
        if (suggested < 0) suggested = 0;

        var input = await context.DialogService.ShowInputDialogAsync(
            "删除指定归属的所有单位",
            "归属ID（按单位自身的军团归属精确匹配）：",
            suggested.ToString());
        if (input == null) return false;

        if (!int.TryParse(input, out int targetBelong))
        {
            context.RaiseStatusMessage?.Invoke("输入的归属ID无效");
            return false;
        }

        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
            "删除指定归属的所有单位",
            $"确定删除归属 [{targetBelong}] 的所有单位？此操作不可撤销。",
            "删除", "取消");
        if (!confirmed) return false;

        var result = RunArmyOp(context,
            v1 => v1.DeleteArmiesByBelong(targetBelong),
            v3 => v3.DeleteArmiesByBelong(targetBelong));

        context.NotifyDataModified?.Invoke();
        context.RaiseStatusMessage?.Invoke(result.Message);
        return result.Success;
    }

    /// <summary>建议归属：优先取所在省份的省会归属，其次取当前格归属；都无效返回 -1。</summary>
    private static int ResolveTargetBelong(ModeContext context, int col, int row)
    {
        int provinceBelong = context.MapData?.GetProvinceCapitalBelong(col, row) ?? -1;
        if (provinceBelong >= 0) return provinceBelong;
        return context.MapData?.GetBelongValue(col, row) ?? -1;
    }

    /// <summary>按地图版本把单位操作分发到 v1 / v3 修改器。</summary>
    private static ModifierResult RunArmyOp(
        ModeContext context,
        Func<ArmyModifier, ModifierResult> v1Op,
        Func<ArmyV3Modifier, ModifierResult> v3Op)
    {
        var mapData = context.MapData;
        if (mapData == null) return ModifierResult.Fail("地图数据未初始化");

        if (mapData.Header.BtlVersion >= 3)
        {
            var v3 = context.GetModifier<ArmyV3Modifier>();
            return v3 == null ? ModifierResult.Fail("v3单位修改器未初始化") : v3Op(v3);
        }

        var v1 = context.GetModifier<ArmyModifier>();
        return v1 == null ? ModifierResult.Fail("单位修改器未初始化") : v1Op(v1);
    }
}
