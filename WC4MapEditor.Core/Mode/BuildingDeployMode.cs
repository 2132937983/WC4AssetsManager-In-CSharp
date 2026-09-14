using System.Collections.Specialized;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Mode;

public sealed class BuildingDeployMode : IModeHandler
{
    private bool _moveToolActive;
    // 上次通过 Enter 选中的建筑所在格子序号（-1 表示尚未选中）。
    // 用坐标而非列表索引递推，避免"新增建筑被追加到列表末尾"打乱遍历顺序。
    private int _lastSelectedBuildingCoord = -1;
    private bool _copiedLevelsOnly;
    private readonly Random _random = new();

    // 按格子序号升序的建筑列表缓存；Buildings 集合增删改时通过 CollectionChanged 失效
    private List<Building>? _sortedBuildingsCache;
    private MapData? _sortedBuildingsSource;

    public EditMode Mode => EditMode.BuildingDeploy;
    public string DisplayName => "建筑部署";
    public ModifierKind PrimaryModifierKind => ModifierKind.Building;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Building, ModifierKind.Belong };
    public bool RequiresSelection => true;

    public string HelpText =>
        "建筑编辑模式快捷键:\n" +
        "左键 - 选择格子\n" +
        "右键 - 放置建筑\n" +
        "U - 切换建筑名称显示\n" +
        "C - 复制建筑数据（排除名称）\n" +
        "Ctrl+C - 仅复制建筑等级数据\n" +
        "V - 粘贴建筑数据\n" +
        "X - 删除建筑与归属\n" +
        "Delete - 删除所有建筑\n" +
        "Ctrl+Delete - 删除不在省会格子上的建筑\n" +
        "R - 按归属随机建筑数据\n" +
        "Y - 为所有建筑所在格子生成省会\n" +
        "F - 设置选中建筑为关键据点（红圈）\n" +
        "Ctrl+F - 修改占领触发事件\n" +
        "Shift+F - 将所有事件建筑标记为红圈\n" +
        "H - 随机化有名称的建筑类型\n" +
        "Ctrl+H - 随机化无名称的建筑类型\n" +
        "Q - 打开建筑创建窗口\n" +
        "I - 按归属概率生成建筑\n" +
        "T - 在省会格子上随机生成建筑\n" +
        "G - 智能设置建筑外观值\n" +
        "Enter - 循环选中建筑并移动视角\n" +
        "E - 在海岸线海洋格子上生成建筑\n" +
        "K - 切换建筑移动工具（右键拖拽移动建筑）\n" +
        "P - 识别背景图文字并生成建筑";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("BD_U", KeyCodes.U, KeyModifiers.None, "toggle_names", "切换建筑名称显示"),
            new ModeKeyBinding("BD_C", KeyCodes.C, KeyModifiers.None, "copy", "复制建筑数据"),
            new ModeKeyBinding("BD_CC", KeyCodes.C, KeyModifiers.Ctrl, "copy_levels", "仅复制建筑等级数据"),
            new ModeKeyBinding("BD_V", KeyCodes.V, KeyModifiers.None, "paste", "粘贴建筑数据"),
            new ModeKeyBinding("BD_X", KeyCodes.X, KeyModifiers.None, "delete_belong", "删除建筑与归属"),
            new ModeKeyBinding("BD_Del", KeyCodes.Delete, KeyModifiers.None, "delete_all", "删除所有建筑"),
            new ModeKeyBinding("BD_CDel", KeyCodes.Delete, KeyModifiers.Ctrl, "delete_non_capital", "删除不在省会格子上的建筑"),
            new ModeKeyBinding("BD_R", KeyCodes.R, KeyModifiers.None, "random_by_belong", "按归属随机建筑数据"),
            new ModeKeyBinding("BD_Y", KeyCodes.Y, KeyModifiers.None, "gen_capitals", "为所有建筑所在格子生成省会"),
            new ModeKeyBinding("BD_F", KeyCodes.F, KeyModifiers.None, "set_keypoint", "设置关键据点"),
            new ModeKeyBinding("BD_CF", KeyCodes.F, KeyModifiers.Ctrl, "set_occupation_event", "修改占领触发事件"),
            new ModeKeyBinding("BD_SF", KeyCodes.F, KeyModifiers.Shift, "mark_event_redcircle", "将所有事件建筑标记为红圈"),
            new ModeKeyBinding("BD_H", KeyCodes.H, KeyModifiers.None, "random_named_type", "随机化有名称的建筑类型"),
            new ModeKeyBinding("BD_CH", KeyCodes.H, KeyModifiers.Ctrl, "random_unnamed_type", "随机化无名称的建筑类型"),
            new ModeKeyBinding("BD_Q", KeyCodes.Q, KeyModifiers.None, "open_create", "打开建筑创建窗口"),
            new ModeKeyBinding("BD_I", KeyCodes.I, KeyModifiers.None, "gen_by_probability", "按归属概率生成建筑"),
            new ModeKeyBinding("BD_T", KeyCodes.T, KeyModifiers.None, "random_on_capitals", "在省会格子上随机生成建筑"),
            new ModeKeyBinding("BD_G", KeyCodes.G, KeyModifiers.None, "smart_appearance", "智能设置建筑外观值"),
            new ModeKeyBinding("BD_Enter", KeyCodes.Enter, KeyModifiers.None, "cycle_select", "循环选中建筑并移动视角"),
            new ModeKeyBinding("BD_E", KeyCodes.E, KeyModifiers.None, "gen_coastal", "在海岸线海洋格子上生成建筑"),
            new ModeKeyBinding("BD_K", KeyCodes.K, KeyModifiers.None, "toggle_move_tool", "切换建筑移动工具"),
            new ModeKeyBinding("BD_P", KeyCodes.P, KeyModifiers.None, "recognize_text", "识别背景图文字并生成建筑"),
        };
    }

    public async Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var building = context.GetModifier<BuildingModifier>()!;
        var belong = context.GetModifier<BelongModifier>()!;
        var mapData = context.MapData;
        bool modified = false;

        switch (action)
        {
            case "apply":
                {
                    var oldBelong = mapData?.GetBelongValue(col, row);
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);

                    context.RecordEntityChange($"放置建筑 ({col},{row})",
                        () =>
                        {
                            building.Apply(col, row);
                            if (provinceBelong >= 0)
                            {
                                belong.SetBelongByCountryId(col, row, provinceBelong);
                            }
                        },
                        () =>
                        {
                            building.Remove(col, row);
                            if (oldBelong.HasValue)
                            {
                                belong.SetBelongByCountryId(col, row, oldBelong.Value);
                            }
                        });
                    modified = true;
                    break;
                }

            case "toggle_names":
                // 名称开关的实际状态由渲染层持有（模式切换时会按模式重置），
                // 这里只负责触发切换，状态提示由渲染层给出。
                context.NotifyToggleBuildingNames?.Invoke();
                return true;

            case "copy":
                {
                    var selector = HexSelector.Instance;
                    if (selector.SelectedCount > 1)
                    {
                        var count = building.CopyBuildingGroup(selector.SelectedHexes);
                        if (count > 0)
                        {
                            _copiedLevelsOnly = false;
                            context.RaiseStatusMessage?.Invoke($"已复制 {count} 个建筑数据（多选）");
                            return true;
                        }
                    }
                    var result = building.CopyBuilding(col, row);
                    _copiedLevelsOnly = false;
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    return result.Success;
                }

            case "copy_levels":
                {
                    var result = building.CopyBuildingLevels(col, row);
                    _copiedLevelsOnly = true;
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    return result.Success;
                }

            case "paste":
                {
                    if (mapData == null) return false;

                    if (building.HasCopiedGroup)
                    {
                        var count = building.PasteBuildingGroup(col, row);
                        if (count > 0)
                        {
                            context.NotifyDataModified?.Invoke();
                            context.RaiseStatusMessage?.Invoke($"已粘贴 {count} 个建筑数据（多选）");
                            return true;
                        }
                        context.RaiseStatusMessage?.Invoke("粘贴失败：没有可粘贴的建筑组");
                        return false;
                    }

                    var oldBelong = mapData.GetBelongValue(col, row);
                    var provinceBelong = GetProvinceCapitalBelong(col, row, context);

                    if (_copiedLevelsOnly)
                    {
                        var result = building.PasteBuildingLevels(col, row);
                        context.RaiseStatusMessage?.Invoke(result.Message);
                        return result.Success;
                    }

                    context.RecordEntityChange($"粘贴建筑 ({col},{row})",
                        () =>
                        {
                            building.PasteBuilding(col, row);
                            if (provinceBelong >= 0)
                            {
                                belong.SetBelongByCountryId(col, row, provinceBelong);
                            }
                        },
                        () =>
                        {
                            building.Remove(col, row);
                            belong.SetBelongByCountryId(col, row, oldBelong);
                        });
                    modified = true;
                    break;
                }

            case "delete_belong":
                {
                    if (mapData == null) return false;
                    var oldBuilding = mapData.GetBuildingAt(col, row);
                    var oldBelong = mapData.GetBelongValue(col, row);

                    context.RecordEntityChange($"删除建筑与归属 ({col},{row})",
                        () =>
                        {
                            building.Remove(col, row);
                            belong.SetBelongByCountryId(col, row, 0xFF);
                        },
                        () =>
                        {
                            if (oldBuilding != null)
                            {
                                building.Apply(col, row, oldBuilding.Value);
                            }
                            belong.SetBelongByCountryId(col, row, oldBelong);
                        });
                    modified = true;
                    break;
                }

            case "delete_all":
                {
                    if (mapData == null) return false;
                    if (context.DialogService != null)
                    {
                        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要删除所有建筑吗？\n此操作不可撤销！", "确定", "取消");
                        if (!confirmed) return false;
                    }

                    var oldBuildings = mapData.Buildings.ToList();
                    context.RecordEntityChange("删除所有建筑",
                        () => building.RemoveAll(),
                        () =>
                        {
                            foreach (var b in oldBuildings)
                            {
                                var c = HexCoord.FromIndex(b.Coordinate, mapData.MapWidth);
                                building.Apply(c.Col, c.Row, b);
                            }
                        });
                    modified = true;
                    break;
                }

            case "delete_non_capital":
                {
                    if (mapData == null) return false;
                    if (context.DialogService != null)
                    {
                        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作", "确定要删除不在省会格子上的建筑吗？\n此操作不可撤销！", "确定", "取消");
                        if (!confirmed) return false;
                    }

                    var removed = building.RemoveNonCapitalBuildings(mapData);
                    context.RaiseStatusMessage?.Invoke($"已删除 {removed} 个不在省会格子上的建筑");
                    modified = removed > 0;
                    break;
                }

            case "set_keypoint":
                {
                    var result = building.ToggleKeyPoint(col, row);
                    context.RaiseStatusMessage?.Invoke(result.Message);
                    return result.Success;
                }

            case "set_occupation_event":
                {
                    if (context.DialogService == null)
                    {
                        building.SetOccupationEvent(col, row, 1);
                        context.RaiseStatusMessage?.Invoke("已设置占领触发事件为1");
                        return true;
                    }

                    var currentBuilding = mapData?.GetBuildingAt(col, row);
                    if (currentBuilding == null)
                    {
                        context.RaiseStatusMessage?.Invoke("当前格子没有建筑");
                        return false;
                    }

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "设置占领触发事件", "输入事件ID (序号):",
                        currentBuilding.Value.OccupationEvent.ToString(), 0, 255);
                    if (input == null) return false;

                    if (int.TryParse(input, out int eventId))
                    {
                        building.SetOccupationEvent(col, row, eventId);
                        context.RaiseStatusMessage?.Invoke($"已设置占领触发事件为 {eventId}");
                        modified = true;
                    }
                    else
                    {
                        context.RaiseStatusMessage?.Invoke("请输入有效的事件ID");
                    }
                    break;
                }

            case "mark_event_redcircle":
                {
                    if (mapData == null) return false;
                    var count = building.MarkAllEventBuildingsWithRedCircle();
                    context.RaiseStatusMessage?.Invoke($"已将 {count} 个事件建筑标记为红圈");
                    modified = count > 0;
                    break;
                }

            case "random_named_type":
                {
                    if (mapData == null || context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "随机化有名称建筑的建筑类型",
                        "1 = 条件判断（按周围建筑密度分配类型）\n2 = 默认随机（在配置范围内随机）",
                        "2", 1, 2);
                    if (input == null || !int.TryParse(input, out int namedMode)) return false;

                    var result = building.RandomizeNamedBuildingTypes(useCondition: namedMode == 1);
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已完成");
                    modified = result.Success;
                    break;
                }

            case "random_unnamed_type":
                {
                    if (mapData == null || context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "随机化没有名称建筑的建筑类型",
                        "1 = 条件判断（按周围建筑密度分配类型）\n2 = 默认随机（在配置范围内随机）",
                        "2", 1, 2);
                    if (input == null || !int.TryParse(input, out int unnamedMode)) return false;

                    var result = building.RandomizeUnnamedBuildingTypes(useCondition: unnamedMode == 1);
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已完成");
                    modified = result.Success;
                    break;
                }

            case "open_create":
                OpenBuildingCreateWindow(context, col, row);
                return true;

            case "gen_by_probability":
                {
                    if (context.DialogService == null) return false;

                    var (confirmed, belongText, probText) = await context.DialogService.ShowDoubleInputDialogAsync(
                        "按归属概率生成建筑", "归属ID(-1为全部):", "概率(0-100):", "-1", "50");
                    if (!confirmed) return false;

                    if (!int.TryParse(belongText, out int targetBelongId))
                    {
                        context.RaiseStatusMessage?.Invoke("输入的归属ID无效");
                        return false;
                    }
                    if (!int.TryParse(probText, out int probability))
                    {
                        context.RaiseStatusMessage?.Invoke("输入的概率无效");
                        return false;
                    }

                    var result = building.GenerateBuildingsByBelongProbability(targetBelongId, probability);
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已完成");
                    modified = result.Success;
                    break;
                }

            case "random_by_belong":
                {
                    if (context.DialogService == null)
                    {
                        var count = building.RandomizeBuildingsByBelong(-1);
                        context.RaiseStatusMessage?.Invoke($"已随机化 {count} 个建筑");
                        modified = count > 0;
                        break;
                    }
                    var input = await context.DialogService.ShowInputDialogAsync(
                        "按归属随机建筑数据", "归属ID (-1为全部):", "-1");
                    if (input == null) return false;
                    if (int.TryParse(input, out int belongId))
                    {
                        var count = building.RandomizeBuildingsByBelong(belongId);
                        context.RaiseStatusMessage?.Invoke($"已随机化 {count} 个建筑");
                        modified = count > 0;
                    }
                    break;
                }

            case "gen_capitals":
                {
                    if (mapData == null) return false;
                    int existingCount = building.CountBuildingsWithExistingProvince(mapData);
                    bool overwriteExisting = false;
                    if (existingCount > 0 && context.DialogService != null)
                    {
                        var confirmed = await context.DialogService.ShowConfirmDialogAsync(
                            "确认操作",
                            $"有 {existingCount} 个建筑所在格子已有省份数据。\n是否覆盖这些格子的省份数据？\n\n选择\"确定\"将覆盖所有格子，选择\"取消\"仅处理空白格子。",
                            "确定（覆盖）", "取消（仅空白）");
                        overwriteExisting = confirmed;
                    }
                    var count = building.GenerateCapitalsForAllBuildings(mapData, overwriteExisting);
                    context.RaiseStatusMessage?.Invoke($"已为 {count} 个建筑生成省会");
                    modified = count > 0;
                    break;
                }

            case "random_on_capitals":
                {
                    if (mapData == null) return false;
                    var count = building.RandomizeBuildingsOnCapitals(mapData);
                    context.RaiseStatusMessage?.Invoke($"已在 {count} 个省会格子上随机生成建筑");
                    modified = count > 0;
                    break;
                }

            case "smart_appearance":
                {
                    if (mapData == null) return false;
                    var count = building.SmartSetBuildingAppearance(mapData);
                    context.RaiseStatusMessage?.Invoke($"已为 {count} 个需要方向的建筑智能设置外观值");
                    modified = count > 0;
                    break;
                }

            case "cycle_select":
                {
                    if (mapData == null) return false;
                    CycleSelectBuilding(context, mapData);
                    return true;
                }

            case "gen_coastal":
                {
                    if (context.DialogService == null) return false;

                    var input = await context.DialogService.ShowInputDialogAsync(
                        "海岸线港口生成概率", "生成概率(%):", "50", 0, 100);
                    if (input == null) return false;

                    if (!int.TryParse(input, out int coastalProbability))
                    {
                        context.RaiseStatusMessage?.Invoke("输入的概率无效");
                        return false;
                    }

                    var result = building.GenerateBuildingsOnCoastalHexes(coastalProbability);
                    context.RaiseStatusMessage?.Invoke(result.Message ?? "已完成");
                    modified = result.Success;
                    break;
                }

            case "toggle_move_tool":
                _moveToolActive = !_moveToolActive;
                // 通知 GUI 层同步拖拽工具状态（鼠标拖拽由 GUI 处理）
                context.NotifyToggleBuildingMoveTool?.Invoke();
                context.RaiseStatusMessage?.Invoke($"建筑移动工具已{(_moveToolActive ? "开启" : "关闭")} - 右键拖拽建筑进行移动");
                return true;

            case "recognize_text":
                // 完整流程（选图 → OCR → 导出地图网格 → 生成 zme → 放置建筑）由 GUI 层实现
                context.NotifyRecognizeTextBuildings?.Invoke();
                return true;

            case "remove":
                {
                    if (mapData == null) return false;
                    var oldBuilding = mapData.GetBuildingAt(col, row);
                    var oldBelong = mapData.GetBelongValue(col, row);
                    if (oldBuilding != null)
                    {
                        var saved = oldBuilding.Value;
                        context.RecordEntityChange($"删除建筑 ({col},{row})",
                            () =>
                            {
                                building.Remove(col, row);
                                belong.SetBelongByCountryId(col, row, 0xFF);
                            },
                            () =>
                            {
                                building.Apply(col, row, saved);
                                belong.SetBelongByCountryId(col, row, oldBelong);
                            });
                    }
                    else
                    {
                        building.Remove(col, row);
                        belong.SetBelongByCountryId(col, row, 0xFF);
                    }
                    modified = true;
                    break;
                }
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return modified;
    }

    private int GetProvinceCapitalBelong(int col, int row, ModeContext context)
    {
        var mapData = context.MapData;
        if (mapData == null) return -1;

        var province = mapData.GetProvinceRef(col, row);
        if (province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF) return -1;

        int capitalIndex = province.ProvinceValue;

        if (capitalIndex >= 0 && capitalIndex < mapData.MapWidth * mapData.MapHeight)
        {
            var capitalCoord = HexCoord.FromIndex(capitalIndex, mapData.MapWidth);
            return mapData.GetBelongValue(capitalCoord.Col, capitalCoord.Row);
        }

        return -1;
    }

    /// <summary>
    /// 获取按格子序号升序排列的建筑列表（带缓存）。
    /// 建筑集合发生增删改时自动失效重建；切换地图时重新订阅事件。
    /// </summary>
    private List<Building> GetSortedBuildings(MapData mapData)
    {
        if (!ReferenceEquals(_sortedBuildingsSource, mapData))
        {
            if (_sortedBuildingsSource != null)
                _sortedBuildingsSource.Buildings.CollectionChanged -= OnBuildingsCollectionChanged;

            mapData.Buildings.CollectionChanged += OnBuildingsCollectionChanged;
            _sortedBuildingsSource = mapData;
            _sortedBuildingsCache = null;
        }

        return _sortedBuildingsCache ??= mapData.Buildings.OrderBy(b => b.Coordinate).ToList();
    }

    private void OnBuildingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _sortedBuildingsCache = null;

    private void CycleSelectBuilding(ModeContext context, MapData mapData)
    {
        if (mapData.Buildings.Count == 0)
        {
            context.RaiseStatusMessage?.Invoke("没有建筑可以选中");
            return;
        }

        // 按格子序号升序取建筑：Buildings 列表顺序会受新增建筑影响（新建筑被追加到末尾），
        // 直接按列表索引递推会导致遍历顺序与坐标顺序不一致。
        var ordered = GetSortedBuildings(mapData);

        // 取上一个选中格之后的下一个；已到末尾则回到第一个
        int pos = ordered.FindIndex(b => b.Coordinate > _lastSelectedBuildingCoord);
        if (pos < 0) pos = 0;

        var selectedBuilding = ordered[pos];
        _lastSelectedBuildingCoord = selectedBuilding.Coordinate;

        var col = selectedBuilding.Coordinate % mapData.MapWidth;
        var row = selectedBuilding.Coordinate / mapData.MapWidth;

        var selector = HexSelector.Instance;
        selector.ClearSelection();
        selector.Select(col, row, mapData.MapWidth, mapData.MapHeight);

        // 移动视角到该建筑（对齐 VB：_mapRenderer.Camera.CenterOnHex）
        context.NotifyMoveCameraToHex?.Invoke(col, row);

        var buildingName = selectedBuilding.Name == 0 || selectedBuilding.Name == -1
            ? "无名建筑"
            : selectedBuilding.Name.ToString();
        context.RaiseStatusMessage?.Invoke(
            $"已选中建筑 {pos + 1}/{ordered.Count}: {buildingName} (类型: {selectedBuilding.GetBuildingTypeName()})");
    }

    private async void OpenBuildingCreateWindow(ModeContext context, int col, int row)
    {
        if (context.DialogService == null)
        {
            context.RaiseStatusMessage?.Invoke("对话框服务未初始化");
            return;
        }

        var mapData = context.MapData;
        if (mapData == null) return;

        int coord = row * mapData.MapWidth + col;

        // 检查当前格子是否已有建筑，有则编辑现有建筑，无则创建新建筑
        var existingBuilding = mapData.GetBuildingAt(col, row);
        bool isNew = existingBuilding == null;
        var building = isNew ? Building.CreateDefault(coord) : existingBuilding.Value;

        var (confirmed, resultBuilding) = await context.DialogService.ShowBuildingSettingDialogAsync(building, isNew: isNew);
        if (confirmed)
        {
            var provinceBelong = GetProvinceCapitalBelong(col, row, context);
            var oldBelong = mapData.GetBelongValue(col, row);
            var buildingModifier = context.GetModifier<BuildingModifier>()!;
            var belongModifier = context.GetModifier<BelongModifier>()!;

            if (isNew)
            {
                context.RecordEntityChange($"创建建筑 ({col},{row})",
                    () =>
                    {
                        buildingModifier.Apply(col, row, resultBuilding);
                        if (provinceBelong >= 0)
                        {
                            belongModifier.SetBelongByCountryId(col, row, provinceBelong);
                        }
                    },
                    () =>
                    {
                        buildingModifier.Remove(col, row);
                        belongModifier.SetBelongByCountryId(col, row, oldBelong);
                    });

                context.NotifyDataModified?.Invoke();
                context.RaiseStatusMessage?.Invoke($"已创建建筑 at ({col},{row})");
            }
            else
            {
                var oldBuilding = existingBuilding!.Value;
                context.RecordEntityChange($"编辑建筑 ({col},{row})",
                    () => buildingModifier.Apply(col, row, resultBuilding),
                    () => buildingModifier.Apply(col, row, oldBuilding));

                context.NotifyDataModified?.Invoke();
                context.RaiseStatusMessage?.Invoke($"已更新建筑 at ({col},{row})");
            }
        }
    }
}