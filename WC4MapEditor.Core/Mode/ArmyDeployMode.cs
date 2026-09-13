using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 单位部署模式 - 对齐VB版本功能
/// 创建单位时自动设置归属，支持v1和v3单位编辑窗口
/// </summary>
public sealed class ArmyDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.ArmyDeploy;
    public string DisplayName => "单位部署";
    public ModifierKind PrimaryModifierKind => ModifierKind.Army;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Army, ModifierKind.Belong, ModifierKind.Province, ModifierKind.Trap };
    // 启用选择器：左键单击选择、右键拖动框选（Shift 加选 / Ctrl 减选），
    // 框选只命中存在单位的格子（见渲染层 GetSelectionFilter 的 ArmyDeploy 分支）。
    public bool RequiresSelection => true;

    public string HelpText =>
        "单位编辑模式快捷键:\n" +
        "左键单击 - 选择单位（Shift 加选 / Ctrl 减选）\n" +
        "右键拖动 - 框选单位（Shift 并入 / Ctrl 剔除）\n" +
        "右键单击 - 选择单位\n" +
        "C - 复制单位（多选选中时批量复制）\n" +
        "V - 粘贴单位（有批量复制时按原布局粘贴，以当前格为左上角）\n" +
        "X - 删除单位（多选选中时批量删除）\n" +
        "Delete - 删除单位（多选选中时批量删除）\n" +
        "Q - 创建/修改单位\n" +
        "Ctrl+Q - 多选时设置方案/单选时设置占领事件\n" +
        "E - 按配置修改军团强度\n" +
        "R - 随机化军团单位\n" +
        "I - 按概率生成单位\n" +
        "G - 自动分配将领(基于配置)\n" +
        "T - 循环切换单位方案(0-4)\n" +
        "Y - 按归属批量修改方案(-1为所有军团)\n" +
        "Shift+Delete - 删除指定归属的所有单位\n" +
        "Ctrl+C - 复制单位/陷阱\n" +
        "Ctrl+V - 粘贴单位/陷阱\n" +
        "Ctrl+X - 删除单位/陷阱\n" +
        "F - 创建/修改陷阱\n" +
        "Ctrl+G - 按概率批量生成陷阱（跳过海洋与建筑格）\n" +
        "Ctrl+R - 随机化陷阱等级";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("AD_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "删除单位"),
            new ModeKeyBinding("AD_C", KeyCodes.C, KeyModifiers.None, "copy", "复制单位"),
            new ModeKeyBinding("AD_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴单位"),
            new ModeKeyBinding("AD_L", KeyCodes.L, KeyModifiers.None, "set_legion", "按归属设置军团"),
            new ModeKeyBinding("AD_Q", KeyCodes.Q, KeyModifiers.None, "open_create", "打开单位创建窗口"),

            // 陷阱编辑
            new ModeKeyBinding("AD_F", KeyCodes.F, KeyModifiers.None, "trap_place", "在光标格放置/修改陷阱"),
            new ModeKeyBinding("AD_CC", KeyCodes.C, KeyModifiers.Ctrl, "trap_copy", "复制陷阱"),
            new ModeKeyBinding("AD_CV", KeyCodes.V, KeyModifiers.Ctrl, "trap_paste", "粘贴陷阱"),
            new ModeKeyBinding("AD_CX", KeyCodes.X, KeyModifiers.Ctrl, "trap_remove", "删除光标格陷阱"),
            new ModeKeyBinding("AD_CG", KeyCodes.G, KeyModifiers.Ctrl, "trap_generate", "按概率批量生成陷阱（跳过海洋与建筑）"),
            new ModeKeyBinding("AD_CR", KeyCodes.R, KeyModifiers.Ctrl, "trap_random_levels", "随机化该归属陷阱等级"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var army = context.GetModifier<ArmyModifier>()!;
        var belong = context.GetModifier<BelongModifier>()!;
        var province = context.GetModifier<ProvinceModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                {
                    // 放置单位并自动设置归属
                    var oldBelong = context.MapData?.GetBelongValue(col, row);
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);

                    context.RecordEntityChange($"放置单位 ({col},{row})",
                        () =>
                        {
                            army.Apply(col, row);
                            // 自动设置归属：如果格子有省区，则设置为省会格子的归属
                            if (provinceBelong >= 0)
                            {
                                belong.SetBelongByCountryId(col, row, provinceBelong);
                            }
                        },
                        () =>
                        {
                            army.Remove(col, row);
                            // 恢复原来的归属
                            if (oldBelong.HasValue)
                            {
                                belong.SetBelongByCountryId(col, row, oldBelong.Value);
                            }
                        });
                    modified = true;
                    break;
                }
            case "copy":
                {
                    var selector = HexSelector.Instance;
                    if (selector.SelectedCount > 1)
                    {
                        var count = army.CopyArmyGroup(selector.SelectedHexes);
                        if (count > 0)
                        {
                            context.RaiseStatusMessage?.Invoke($"已复制 {count} 个单位（多选）");
                            return true;
                        }
                    }
                    var result = army.CopyArmy(col, row);
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    return result.Success;
                }
            case "paste":
                {
                    var pasteMapData = context.MapData;

                    // 多选粘贴：以当前格作为选区左上角，按原相对布局批量放置
                    if (army.HasCopiedArmyGroup && pasteMapData != null)
                    {
                        var pastedCoords = new List<HexCoord>();
                        var pasteBelongBackup = new List<(int Col, int Row, int? Belong)>();

                        context.RecordEntityChange($"粘贴单位组 ({col},{row})",
                            () =>
                            {
                                pastedCoords.Clear();
                                pasteBelongBackup.Clear();

                                foreach (var c in army.PasteArmyGroup(col, row))
                                {
                                    pasteBelongBackup.Add((c.Col, c.Row, pasteMapData.GetBelongValue(c.Col, c.Row)));
                                    pastedCoords.Add(c);
                                }

                                // 与单选一致：按各自格子所在省会自动设置归属
                                foreach (var c in pastedCoords)
                                {
                                    int pb = GetProvinceCapitalBelong(c.Col, c.Row, context);
                                    if (pb >= 0) belong.SetBelongByCountryId(c.Col, c.Row, pb);
                                }
                            },
                            () =>
                            {
                                foreach (var c in pastedCoords)
                                {
                                    pasteMapData.RemoveArmyAt(c.Col, c.Row);
                                    pasteMapData.RemoveArmyV3At(c.Col, c.Row);
                                }
                                foreach (var (c, r, b) in pasteBelongBackup)
                                {
                                    if (b.HasValue) belong.SetBelongByCountryId(c, r, b.Value);
                                }
                            });

                        context.NotifyDataModified?.Invoke();
                        context.RaiseStatusMessage?.Invoke($"已粘贴单位组到 ({col},{row})");
                        return true;
                    }

                    var oldBelong = context.MapData?.GetBelongValue(col, row);
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);

                    context.RecordEntityChange($"粘贴单位 ({col},{row})",
                        () =>
                        {
                            army.PasteArmy(col, row);
                            if (provinceBelong >= 0)
                            {
                                belong.SetBelongByCountryId(col, row, provinceBelong);
                            }
                        },
                        () =>
                        {
                            army.Remove(col, row);
                            if (oldBelong.HasValue)
                            {
                                belong.SetBelongByCountryId(col, row, oldBelong.Value);
                            }
                        });
                    modified = true;
                    break;
                }
            case "remove":
                {
                    var removeMapData = context.MapData;
                    var selector = HexSelector.Instance;

                    // 多选删除：清除选区中所有单位（v1 与 v3 一并处理，与框选过滤器的判定一致）
                    if (selector.SelectedCount > 1 && removeMapData != null)
                    {
                        var removed = new List<(int Col, int Row, Army? V1, Army_3? V3)>();
                        foreach (var coord in selector.SelectedHexes)
                        {
                            var v1 = removeMapData.GetArmyAt(coord.Col, coord.Row);
                            var v3 = removeMapData.GetArmyV3At(coord.Col, coord.Row);
                            if (v1.HasValue || v3.HasValue)
                                removed.Add((coord.Col, coord.Row, v1, v3));
                        }

                        if (removed.Count > 0)
                        {
                            var armyV3 = context.GetModifier<ArmyV3Modifier>();
                            var removeBelongBackup = new List<(int Col, int Row, int? Belong)>();

                            context.RecordEntityChange($"删除 {removed.Count} 个单位",
                                () =>
                                {
                                    removeBelongBackup.Clear();
                                    foreach (var (c, r, _, _) in removed)
                                        removeBelongBackup.Add((c, r, removeMapData.GetBelongValue(c, r)));

                                    foreach (var (c, r, _, _) in removed)
                                    {
                                        removeMapData.RemoveArmyAt(c, r);
                                        removeMapData.RemoveArmyV3At(c, r);
                                        belong.SetBelongByCountryId(c, r, 0xFF);
                                    }
                                },
                                () =>
                                {
                                    foreach (var (c, r, v1, v3) in removed)
                                    {
                                        if (v1.HasValue) army.Apply(c, r, v1.Value);
                                        if (v3.HasValue) armyV3?.Apply(c, r, v3.Value);
                                    }
                                    foreach (var (c, r, b) in removeBelongBackup)
                                    {
                                        if (b.HasValue) belong.SetBelongByCountryId(c, r, b.Value);
                                    }
                                });

                            context.NotifyDataModified?.Invoke();
                            context.RaiseStatusMessage?.Invoke($"已删除 {removed.Count} 个单位");
                            return true;
                        }
                    }

                    var oldArmy = context.MapData?.GetArmyAt(col, row);
                    var oldBelong = context.MapData?.GetBelongValue(col, row);
                    if (oldArmy != null)
                    {
                        var saved = oldArmy.Value;
                        context.RecordEntityChange($"删除单位 ({col},{row})",
                            () =>
                            {
                                army.Remove(col, row);
                                // 清除归属
                                belong.SetBelongByCountryId(col, row, 0xFF);
                            },
                            () =>
                            {
                                army.Apply(col, row, saved);
                                if (oldBelong.HasValue)
                                {
                                    belong.SetBelongByCountryId(col, row, oldBelong.Value);
                                }
                            });
                    }
                    else
                    {
                        army.Remove(col, row);
                        belong.SetBelongByCountryId(col, row, 0xFF);
                    }
                    modified = true;
                    break;
                }
            case "set_legion":
                {
                    int legionId = belong.GetBelongValue(col, row) ?? 0;
                    context.RecordEntityChange($"设置军团 ({col},{row})",
                        () => army.SetLegionId(col, row, legionId), () => { });
                    modified = true;
                    break;
                }
            case "open_create":
                // 打开单位创建窗口
                OpenArmyCreateWindow(context, col, row);
                return true;

            // ===== 陷阱编辑 =====

            case "trap_place":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null) return false;

                    // 陷阱的「所属军团」字段（Trap.LegionId）取当前格所在省份的省会格子归属值，
                    // 与放置单位的归属规则一致。
                    // 注意：只把归属值写入陷阱自身的字段，不改动所在格子归属。
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);
                    var before = trap.GetTrapAt(col, row);

                    context.RecordEntityChange($"放置陷阱 ({col},{row})",
                        () =>
                        {
                            // 有省区时用省会归属，否则退回当前格自身的归属值
                            trap.SelectedLegionId = provinceBelong >= 0
                                ? provinceBelong
                                : (context.MapData?.GetBelongValue(col, row) ?? 0);

                            // Apply 内部会把 SelectedLegionId 写入 Trap.LegionId
                            trap.Apply(col, row);
                        },
                        () =>
                        {
                            if (before.HasValue) trap.Apply(col, row, before.Value);
                            else trap.Remove(col, row);
                        });

                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(provinceBelong >= 0
                        ? $"已放置陷阱 ({col},{row})，所属军团={provinceBelong}（省会归属）"
                        : $"已放置陷阱 ({col},{row})");
                    return true;
                }
            case "trap_copy":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null) return false;
                    var r = trap.CopyTrap(col, row);
                    context.RaiseStatusMessage?.Invoke(r.Message);
                    return r.Success;
                }
            case "trap_paste":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null) return false;
                    var r = trap.PasteTrap(col, row);
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(r.Message);
                    return r.Success;
                }
            case "trap_remove":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null) return false;

                    var before = trap.GetTrapAt(col, row);
                    if (!before.HasValue)
                    {
                        context.RaiseStatusMessage?.Invoke("该位置没有陷阱");
                        return false;
                    }

                    context.RecordEntityChange($"删除陷阱 ({col},{row})",
                        () => trap.Remove(col, row),
                        () => trap.Apply(col, row, before.Value));

                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke($"已删除陷阱 ({col},{row})");
                    return true;
                }
            case "trap_generate":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null || context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "批量生成陷阱", "生成概率（1-100）：", "50", 1, 100);
                    if (input == null || !int.TryParse(input, out int probability)) return false;

                    // 按概率自由生成：仅跳过海洋/空地与已有建筑的格子，不按归属筛选
                    var r = trap.GenerateTrapsFree(probability);
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(r.Message);
                    return r.Success;
                }
            case "trap_random_levels":
                {
                    var trap = context.GetModifier<TrapModifier>();
                    if (trap == null || context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "随机化陷阱等级", "概率（1-100）：", "50", 1, 100);
                    if (input == null || !int.TryParse(input, out int probability)) return false;

                    // 归属筛选值同样取省会归属，与放置陷阱的规则一致
                    int legionValue = GetProvinceCapitalBelong(col, row, context);
                    if (legionValue < 0) legionValue = context.MapData?.GetBelongValue(col, row) ?? -1;
                    var r = trap.RandomizeTrapLevels(legionValue, probability);
                    context.NotifyDataModified?.Invoke();
                    context.RaiseStatusMessage?.Invoke(r.Message);
                    return r.Success;
                }
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    /// <summary>
    /// 获取指定格子所在省份的省会格子归属
    /// 通过 ProvinceValue 找到省会坐标，然后获取该坐标的归属值
    /// </summary>
    private int GetProvinceCapitalBelong(int col, int row, ModeContext context)
    {
        // 取值逻辑已提取到 MapData，供放置单位与陷阱生成共用一份
        return context.MapData?.GetProvinceCapitalBelong(col, row) ?? -1;
    }

    /// <summary>
    /// 打开单位创建窗口
    /// </summary>
    private async void OpenArmyCreateWindow(ModeContext context, int col, int row)
    {
        if (context.DialogService == null)
        {
            context.RaiseStatusMessage?.Invoke("对话框服务未初始化");
            return;
        }

        var mapData = context.MapData;
        if (mapData == null) return;

        int coord = row * mapData.MapWidth + col;

        // 根据地图版本选择v1或v3单位
        bool isV3 = mapData.Header.BtlVersion >= 3;

        if (isV3)
        {
            var army3 = Army_3.CreateDefault(coord);
            var (confirmed, resultArmy3) = await context.DialogService.ShowArmySettingV3DialogAsync(army3, isNew: true);
            if (confirmed)
            {
                var provinceBelong = GetProvinceCapitalBelong(col, row, context);
                var oldBelong = mapData.GetBelongValue(col, row);
                var armyV3Modifier = context.GetModifier<ArmyV3Modifier>()!;
                var belong = context.GetModifier<BelongModifier>()!;

                context.RecordEntityChange($"创建v3单位 ({col},{row})",
                    () =>
                    {
                        armyV3Modifier.Apply(col, row, resultArmy3);
                        if (provinceBelong >= 0)
                        {
                            belong.SetBelongByCountryId(col, row, provinceBelong);
                        }
                    },
                    () =>
                    {
                        armyV3Modifier.Remove(col, row);
                        belong.SetBelongByCountryId(col, row, oldBelong);
                    });

                context.NotifyDataModified?.Invoke();
                context.RaiseStatusMessage?.Invoke($"已创建v3单位 at ({col},{row})");
            }
        }
        else
        {
            var army = Army.CreateDefault(coord);
            var (confirmed, resultArmy) = await context.DialogService.ShowArmySettingDialogAsync(army, isNew: true);
            if (confirmed)
            {
                var provinceBelong = GetProvinceCapitalBelong(col, row, context);
                var oldBelong = mapData.GetBelongValue(col, row);
                var armyModifier = context.GetModifier<ArmyModifier>()!;
                var belong = context.GetModifier<BelongModifier>()!;

                context.RecordEntityChange($"创建单位 ({col},{row})",
                    () =>
                    {
                        armyModifier.Apply(col, row, resultArmy);
                        if (provinceBelong >= 0)
                        {
                            belong.SetBelongByCountryId(col, row, provinceBelong);
                        }
                    },
                    () =>
                    {
                        armyModifier.Remove(col, row);
                        belong.SetBelongByCountryId(col, row, oldBelong);
                    });

                context.NotifyDataModified?.Invoke();
                context.RaiseStatusMessage?.Invoke($"已创建单位 at ({col},{row})");
            }
        }
    }
}