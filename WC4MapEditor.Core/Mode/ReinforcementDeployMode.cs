using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 援军配置模式 - 对齐VB版本功能
/// 粘贴援军时自动设置归属和OwnerCountry/OwnerLegion
/// </summary>
public sealed class ReinforcementDeployMode : IModeHandler
{
    public EditMode Mode => EditMode.ReinforcementDeploy;
    public string DisplayName => "援军配置";
    public ModifierKind PrimaryModifierKind => ModifierKind.Reinforcement;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Reinforcement, ModifierKind.Legion, ModifierKind.Belong };
    public bool RequiresSelection => false;

    public string HelpText =>
        "援军编辑模式快捷键:\n" +
        "右键 - 放置援军\n" +
        "Q - 打开援军编辑窗口\n" +
        "G - 为援军格子添加归属值\n" +
        "X - 删除选中格子的援军\n" +
        "C - 复制选中格子的援军数据和归属\n" +
        "V - 粘贴援军数据和归属\n" +
        "Delete - 删除所有援军数据\n" +
        "ESC - 退出编辑模式";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("RD_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "删除援军"),
            new ModeKeyBinding("RD_C", KeyCodes.C, KeyModifiers.None, "copy", "复制援军"),
            new ModeKeyBinding("RD_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴援军"),
            new ModeKeyBinding("RD_Q", KeyCodes.Q, KeyModifiers.None, "prev_legion", "上一个军团"),
            new ModeKeyBinding("RD_E", KeyCodes.E, KeyModifiers.None, "next_legion", "下一个军团"),
        };
    }

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var mapData = context.MapData;
        if (mapData == null) return Task.FromResult(false);

        bool isV3 = mapData.Header.BtlVersion >= 3;
        var reinforcement = context.GetModifier<ReinforcementModifier>()!;
        var reinforcementV3 = context.GetModifier<ReinforcementV3Modifier>()!;
        var belong = context.GetModifier<BelongModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                {
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);
                    if (isV3)
                    {
                        context.RecordEntityChange($"放置v3援军 ({col},{row})",
                            () =>
                            {
                                reinforcementV3.Apply(col, row);
                                if (provinceBelong > 0)
                                {
                                    belong.SetBelongByCountryId(col, row, provinceBelong);
                                }
                            },
                            () =>
                            {
                                reinforcementV3.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, 0xFF);
                            });
                    }
                    else
                    {
                        context.RecordEntityChange($"放置援军 ({col},{row})",
                            () =>
                            {
                                reinforcement.Apply(col, row);
                                if (provinceBelong > 0)
                                {
                                    belong.SetBelongByCountryId(col, row, provinceBelong);
                                }
                            },
                            () =>
                            {
                                reinforcement.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, 0xFF);
                            });
                    }
                    modified = true;
                    break;
                }
            case "copy":
                if (isV3)
                    reinforcementV3.CopyReinforcement(col, row);
                else
                    reinforcement.CopyReinforcement(col, row);
                return Task.FromResult(true);
            case "paste":
                {
                    var oldBelong = mapData.GetBelongValue(col, row);
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);

                    if (isV3)
                    {
                        context.RecordEntityChange($"粘贴v3援军 ({col},{row})",
                            () =>
                            {
                                reinforcementV3.PasteReinforcement(col, row, provinceBelong);
                                if (provinceBelong > 0)
                                {
                                    belong.SetBelongByCountryId(col, row, provinceBelong);
                                }
                            },
                            () =>
                            {
                                reinforcementV3.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            });
                    }
                    else
                    {
                        context.RecordEntityChange($"粘贴援军 ({col},{row})",
                            () =>
                            {
                                reinforcement.PasteReinforcement(col, row, provinceBelong);
                                if (provinceBelong > 0)
                                {
                                    belong.SetBelongByCountryId(col, row, provinceBelong);
                                }
                            },
                            () =>
                            {
                                reinforcement.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            });
                    }
                    modified = true;
                    break;
                }
            case "remove":
                {
                    var oldBelong = mapData.GetBelongValue(col, row);
                    if (isV3)
                    {
                        context.RecordEntityChange($"删除v3援军 ({col},{row})",
                            () =>
                            {
                                reinforcementV3.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            },
                            () =>
                            {
                                reinforcementV3.Apply(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            });
                    }
                    else
                    {
                        context.RecordEntityChange($"删除援军 ({col},{row})",
                            () =>
                            {
                                reinforcement.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            },
                            () =>
                            {
                                reinforcement.Apply(col, row);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            });
                    }
                    modified = true;
                    break;
                }
            case "next_legion":
                if (isV3)
                {
                    reinforcementV3.SelectedLegionId = (reinforcementV3.SelectedLegionId % 8) + 1;
                    context.RaiseStatusMessage?.Invoke($"v3援军所属军团: {reinforcementV3.SelectedLegionId}");
                }
                else
                {
                    reinforcement.SelectedLegionId = (reinforcement.SelectedLegionId % 8) + 1;
                    context.RaiseStatusMessage?.Invoke($"援军所属军团: {reinforcement.SelectedLegionId}");
                }
                return Task.FromResult(true);
            case "prev_legion":
                if (isV3)
                {
                    reinforcementV3.SelectedLegionId = ((reinforcementV3.SelectedLegionId - 2 + 8) % 8) + 1;
                    context.RaiseStatusMessage?.Invoke($"v3援军所属军团: {reinforcementV3.SelectedLegionId}");
                }
                else
                {
                    reinforcement.SelectedLegionId = ((reinforcement.SelectedLegionId - 2 + 8) % 8) + 1;
                    context.RaiseStatusMessage?.Invoke($"援军所属军团: {reinforcement.SelectedLegionId}");
                }
                return Task.FromResult(true);
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }

    /// <summary>
    /// 获取指定格子所在省份的省会格子归属
    /// 通过 ProvinceValue 找到省会坐标，然后获取该坐标的归属值
    /// </summary>
    private int GetProvinceCapitalBelong(int col, int row, ModeContext context)
    {
        var mapData = context.MapData;
        if (mapData == null) return -1;

        // 获取当前格子的省份值（省会索引）
        var province = mapData.GetProvinceRef(col, row);
        if (province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF) return -1;

        // ProvinceValue 是省会格子的地图索引
        int capitalIndex = province.ProvinceValue;

        // 获取省会格子的归属值
        if (capitalIndex >= 0 && capitalIndex < mapData.MapWidth * mapData.MapHeight)
        {
            var capitalCoord = HexCoord.FromIndex(capitalIndex, mapData.MapWidth);
            // 返回省会格子的归属值
            return mapData.GetBelongValue(capitalCoord.Col, capitalCoord.Row);
        }

        return -1;
    }
}