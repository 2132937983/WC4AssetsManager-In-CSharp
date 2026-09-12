using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class ProvinceEditMode : IModeHandler
{
    public EditMode Mode => EditMode.ProvinceEdit;
    public string DisplayName => "省份编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Province;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Province };
    public bool RequiresSelection => true;

    public string HelpText =>
        "省份编辑模式快捷键:\n" +
        "左键点击 - 选择格子\n" +
        "C - 复制当前格子的省份值\n" +
        "V - 粘贴省份值到当前格子\n" +
        "Delete - 清除当前格子的省份值\n" +
        "H - 切换画笔模式\n" +
        "[ / ] - 调整画笔半径（画笔模式下）\n" +
        "Q - 清空所有省份为0xFFFF\n" +
        "G - 为所有孤立省会生成省区\n" +
        "E - 扩展所有省区填满地图\n" +
        "I - 处理孤立和空白省区\n" +
        "X - 设置省区数据为格子索引\n" +
        "F - 洪水填充（需先复制省份值）\n" +
        "Esc - 清除所有选择";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("PE_C", KeyCodes.C, KeyModifiers.None, "copy_province", "复制省份值"),
            new ModeKeyBinding("PE_V", KeyCodes.V, KeyModifiers.None, "paste_province", "粘贴省份值"),
            new ModeKeyBinding("PE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清除省份值"),
            new ModeKeyBinding("PE_R", KeyCodes.R, KeyModifiers.None, "toggle_rect_select", "切换矩形选择模式"),
            new ModeKeyBinding("PE_P", KeyCodes.P, KeyModifiers.None, "toggle_polygon_select", "切换多边形选择模式"),
            new ModeKeyBinding("PE_H", KeyCodes.H, KeyModifiers.None, "toggle_brush", "切换画笔模式"),
            new ModeKeyBinding("PE_BracketOpen", KeyCodes.OemOpenBrackets, KeyModifiers.None, "decrease_brush_radius", "减小画笔半径"),
            new ModeKeyBinding("PE_BracketClose", KeyCodes.OemCloseBrackets, KeyModifiers.None, "increase_brush_radius", "增大画笔半径"),
            new ModeKeyBinding("PE_Q", KeyCodes.Q, KeyModifiers.None, "clear_all_provinces", "清空所有省份为0xFFFF"),
            new ModeKeyBinding("PE_G", KeyCodes.G, KeyModifiers.None, "generate_isolated_capitals", "为孤立省会生成省区"),
            new ModeKeyBinding("PE_E", KeyCodes.E, KeyModifiers.None, "expand_all_provinces", "扩展所有省区填满地图"),
            new ModeKeyBinding("PE_I", KeyCodes.I, KeyModifiers.None, "process_isolated_empty", "处理孤立和空白省区"),
            new ModeKeyBinding("PE_X", KeyCodes.X, KeyModifiers.None, "set_province_to_index", "设置省区数据为格子索引"),
            new ModeKeyBinding("PE_F", KeyCodes.F, KeyModifiers.None, "flood_fill", "洪水填充"),
            new ModeKeyBinding("PE_Escape", KeyCodes.Escape, KeyModifiers.None, "clear_selection", "清除所有选择"),
            new ModeKeyBinding("PE_S", KeyCodes.S, KeyModifiers.None, "apply", "设置省份值"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var province = context.GetModifier<ProvinceModifier>()!;
        var selector = HexSelector.Instance;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置省份 ({col},{row})", () => province.Apply(col, row));
                modified = true;
                break;

            case "remove":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量清除省份 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.Remove(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"清除省份 ({col},{row})", () => province.Remove(col, row));
                        modified = true;
                    }
                }
                break;

            case "copy_province":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        var coords = selected.Select(h => (h.Col, h.Row)).ToList();
                        var result = province.CopyProvinceGroup(coords);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    }
                    else
                    {
                        var result = province.CopyProvinceValue(col, row);
                        SyncCopiedProvinceToGlobal(province);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    }
                    return true;
                }

            case "paste_province":
                {
                    SyncGlobalCopiedProvinceToLocal(province);
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量粘贴省份 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.PasteProvinceValue(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"粘贴省份 ({col},{row})", () => province.PasteProvinceValue(col, row));
                        modified = true;
                    }
                }
                break;

            case "toggle_rect_select":
                {
                    bool newState = province.ToggleRectangleSelectMode();
                    context.RaiseStatusMessage?.Invoke(newState ? "已进入矩形选择模式" : "已退出矩形选择模式");
                }
                return true;

            case "toggle_polygon_select":
                {
                    bool newState = province.TogglePolygonSelectMode();
                    context.RaiseStatusMessage?.Invoke(newState ? "已进入多边形选择模式" : "已退出多边形选择模式");
                }
                return true;

            case "toggle_brush":
                context.NotifyBrushToggled?.Invoke();
                return true;

            case "decrease_brush_radius":
                {
                    if (province.IsBrushMode)
                    {
                        province.DecreaseBrushRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {province.BrushRadius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                }
                return true;

            case "increase_brush_radius":
                {
                    if (province.IsBrushMode)
                    {
                        province.IncreaseBrushRadius();
                        context.RaiseStatusMessage?.Invoke($"画笔半径: {province.BrushRadius}");
                        context.NotifyBrushSizeChanged?.Invoke();
                    }
                }
                return true;

            case "clear_all_provinces":
                {
                    if (context.DialogService != null)
                    {
                        bool confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要清空所有省份为0xFFFF吗？\n\n此操作不可撤销！", "确定", "取消");
                        if (!confirmed) return false;
                    }
                    context.RecordMultiCellProvinceChange("清空所有省份为0xFFFF", () =>
                    {
                        var result = province.ClearAllProvinces();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "generate_isolated_capitals":
                {
                    context.RecordMultiCellProvinceChange("为孤立省会生成省区", () =>
                    {
                        var result = province.GenerateProvincesForIsolatedCapitals();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "expand_all_provinces":
                {
                    context.RecordMultiCellProvinceChange("扩展所有省区填满地图", () =>
                    {
                        var result = province.ExpandAllProvincesToFillMap();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "process_isolated_empty":
                {
                    context.RecordMultiCellProvinceChange("处理孤立和空白省区", () =>
                    {
                        var result = province.ProcessIsolatedAndEmptyProvinces();
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "set_province_to_index":
                {
                    var selected = selector.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        context.RecordMultiCellProvinceChange($"批量设置省区索引 ({selected.Count}个格子)", () =>
                        {
                            foreach (var hex in selected)
                                province.SetProvinceValueToIndex(hex.Col, hex.Row);
                        });
                        modified = true;
                    }
                    else
                    {
                        context.RecordProvinceChange(col, row, $"设置省区索引 ({col},{row})", () => province.SetProvinceValueToIndex(col, row));
                        modified = true;
                    }
                }
                break;

            case "flood_fill":
                {
                    SyncGlobalCopiedProvinceToLocal(province);
                    context.RecordMultiCellProvinceChange($"洪水填充 ({col},{row})", () =>
                    {
                        var result = province.FloodFill(col, row);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                    });
                    modified = true;
                }
                break;

            case "clear_selection":
                selector.ClearSelection();
                context.RaiseStatusMessage?.Invoke("已清除所有选择");
                return true;
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    private static void SyncCopiedProvinceToGlobal(ProvinceModifier province)
    {
        var sceneManager = RenderSceneManager.Instance;
        var copiedData = province.GetCopiedProvinceData();
        var anchor = province.GetCopyAnchor();

        if (copiedData.HasValue)
        {
            sceneManager.GlobalCopiedProvince = copiedData.Value;
            sceneManager.GlobalCopiedProvinceFromCol = anchor.col;
            sceneManager.GlobalCopiedProvinceFromRow = anchor.row;
            sceneManager.GlobalCopiedProvinceFromSceneId = sceneManager.CurrentSceneId;
        }

        var copiedGroup = province.GetCopiedProvinceGroup();
        sceneManager.GlobalCopiedProvinceHexes.Clear();
        if (copiedGroup != null && copiedGroup.Count > 0)
        {
            foreach (var kv in copiedGroup)
                sceneManager.GlobalCopiedProvinceHexes[kv.Key] = kv.Value;
            sceneManager.GlobalCopiedProvinceRegionMinCol = anchor.col;
            sceneManager.GlobalCopiedProvinceRegionMinRow = anchor.row;
        }
    }

    private static void SyncGlobalCopiedProvinceToLocal(ProvinceModifier province)
    {
        var sceneManager = RenderSceneManager.Instance;
        if (sceneManager.GlobalCopiedProvince == null) return;

        province.SetCopiedProvinceData(sceneManager.GlobalCopiedProvince.Value,
            sceneManager.GlobalCopiedProvinceFromCol, sceneManager.GlobalCopiedProvinceFromRow);

        if (sceneManager.GlobalCopiedProvinceHexes.Count > 0)
        {
            province.SetCopiedProvinceGroup(sceneManager.GlobalCopiedProvinceHexes,
                sceneManager.GlobalCopiedProvinceRegionMinCol, sceneManager.GlobalCopiedProvinceRegionMinRow);
        }
    }
}