using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 建筑部署模式（对应 VB BuildingModifier 的建筑编辑模式）：只决定"哪个键 → 建筑 / 归属模块的哪个函数"，
/// 数据操作都在 <see cref="BuildingModifier"/> 里。
/// <para>
/// 撤销：只涉及少数格子的操作（粘贴、删除、关键据点、对话框编辑、移动）用局部快照（建筑列表 + 这些格子的归属），
/// 批量操作用 <see cref="ModeContext.RecordBuildingsSnapshot"/>，同时改事件列表的 Ctrl+Q 用全实体快照。
/// </para>
/// </summary>
public sealed class BuildingDeployMode : ModeBase
{
    public override EditMode Mode => EditMode.BuildingDeploy;
    protected override string LocKey => "Building";
    protected override string DefaultDisplayName => "建筑部署";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Building;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Building, ModifierKind.Belong };
    public override bool RequiresSelection => true;

    /// <summary>对齐 VB：建筑、国家领域、归属国旗，外加数据范围外格子的红叉（VB 建筑模式 Draw 里的 DrawClippedTiles）。</summary>
    // 建筑模式不画逐格归属旗：高倍缩放时这一层会为大量格子准备 sprite，
    // 名称/领地已经足够定位；归属旗仍可在归属编辑模式查看。
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.Building | ModeRenderLayers.LegionDomain
                                                   | ModeRenderLayers.ClippedTiles;

    /// <summary>框选只选中有建筑的格子。</summary>
    public override Func<HexCoord, bool>? CreateSelectionFilter(MapData map)
        => coord => map.FindBuildingIndex(coord.Col, coord.Row) >= 0;

    /// <summary>
    /// 建筑移动工具（K 键）是否开启。开关状态只存在这里：GUI 的右键拖拽据此决定拖的是建筑还是框选，
    /// 拖动开始 / 结束时分发 move_begin / move_end，由本模式记录撤销并执行移动。
    /// </summary>
    public bool MoveToolActive { get; private set; }

    // 移动工具：按下右键时的源格（move_begin 记录，move_end 使用）
    private HexCoord? _moveSource;

    // 上次通过 Enter 选中的建筑所在格子序号（-1 表示尚未选中）。
    // 用坐标而非列表索引递推，避免"新增建筑被追加到列表末尾"打乱遍历顺序。
    private int _lastSelectedBuildingCoord = -1;

    // 按格子序号升序的建筑列表缓存；Buildings 集合增删改时通过 CollectionChanged 失效
    private List<Building>? _sortedBuildingsCache;
    private MapData? _sortedBuildingsSource;

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureCommand(ModeGesture.LeftClick, "select", "选择格子并显示建筑信息（点中省会时高亮同省格子）", SelectHex)
         .HelpLine("select_rect", ModeGesture.RightDrag, "框选多个建筑（Shift 加选，Ctrl 减选）")
         .Command("BD_U", KeyCodes.U, "toggle_names", "切换建筑名称显示", ToggleNames)
         .Command("BD_C", KeyCodes.C, "copy", "复制建筑数据", Copy)
         .Command("BD_CC", KeyChord.Ctrl(KeyCodes.C), "copy_levels", "仅复制建筑等级数据", CopyLevels)
         .Edit("BD_V", KeyCodes.V, "paste", "粘贴建筑数据", Paste)
         .Edit("BD_X", KeyCodes.X, "delete_belong", "删除建筑与归属", a => DeleteCells(a, a.TargetCells()))
         .Edit("BD_Del", KeyCodes.Delete, "delete_all", "删除所有建筑", DeleteAllAsync)
         .Edit("BD_CDel", KeyChord.Ctrl(KeyCodes.Delete), "delete_non_capital", "删除不在省会格子上的建筑", DeleteNonCapitalAsync)
         .Edit("BD_R", KeyCodes.R, "random_by_belong", "按归属随机建筑数据", RandomByBelongAsync)
         .Edit("BD_Y", KeyCodes.Y, "gen_capitals", "为所有建筑所在格子生成省会", GenerateCapitalsAsync)
         .Edit("BD_F", KeyCodes.F, "set_keypoint", "设置关键据点", CycleKeyPoint)
         .Edit("BD_CF", KeyChord.Ctrl(KeyCodes.F), "set_occupation_event", "修改占领触发事件", SetOccupationEventAsync)
         .Edit("BD_SF", KeyChord.Shift(KeyCodes.F), "mark_event_redcircle", "将所有事件建筑标记为红圈", MarkEventBuildings)
         .Edit("BD_H", KeyCodes.H, "random_named_type", "随机化有名称的建筑类型", a => RandomizeTypesAsync(a, named: true))
         .Edit("BD_CH", KeyChord.Ctrl(KeyCodes.H), "random_unnamed_type", "随机化无名称的建筑类型", a => RandomizeTypesAsync(a, named: false))
         .Edit("BD_Q", KeyCodes.Q, "open_create", "打开建筑创建窗口", OpenSettingDialogAsync)
         .Edit("BD_CQ", KeyChord.Ctrl(KeyCodes.Q), "link_occupation_event", "设置占领触发事件ID（同时把该事件改为占地触发）", LinkOccupationEventAsync)
         .Edit("BD_I", KeyCodes.I, "gen_by_probability", "按归属概率生成建筑", GenerateByProbabilityAsync)
         .Edit("BD_T", KeyCodes.T, "random_on_capitals", "在省会格子上随机生成建筑", RandomOnCapitals)
         .Edit("BD_G", KeyCodes.G, "smart_appearance", "智能设置建筑外观值", SmartAppearance)
         .Command("BD_Enter", KeyCodes.Enter, "cycle_select", "循环选中建筑并移动视角", CycleSelect)
         .Edit("BD_E", KeyCodes.E, "gen_coastal", "在海岸线海洋格子上生成建筑", GenerateCoastalAsync)
         .Command("BD_K", KeyCodes.K, "toggle_move_tool", "切换建筑移动工具（右键拖拽移动建筑）", ToggleMoveTool)
         .Command("BD_P", KeyCodes.P, "recognize_text", "识别背景图文字并生成建筑", RecognizeText)
         .HiddenEdit("apply", "放置建筑", PlaceDefault)
         .HiddenEdit("remove", "删除建筑与归属（单格）", a => DeleteCells(a, new[] { a.Cell }))
         .HiddenCommand("move_begin", "移动工具：拾起建筑", BeginMove)
         .HiddenEdit("move_end", "移动工具：放下建筑", EndMove)
         .Note("clipped", "红叉 - 归属数据范围外的格子（征服地图裁剪区外），游戏不读取");
    }

    #region 选择与显示

    /// <summary>左键选格：状态栏显示建筑信息；点中省会时高亮同省全部格子（对齐 VB HandleMouseDown 的左键分支）。</summary>
    private static bool SelectHex(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;

        var building = a.Module<BuildingModifier>();
        var overlay = BuildingEditOverlay.For(map);
        string info = building.DescribeBuilding(a.Col, a.Row);

        int province = building.GetCapitalProvince(a.Col, a.Row);
        if (province >= 0)
        {
            overlay.ShowProvince(a.Cell, province);
            info += Loc.F("Mode.Building.Msg.CapitalProvince", building.GetProvinceHexes(province).Count);
        }
        else
        {
            overlay.ClearProvince();
        }

        a.Status(info);
        return true;
    }

    private static bool ToggleNames(ModeActionArgs a)
    {
        // 名称开关的实际状态由渲染层持有（模式切换时会按模式重置），这里只负责触发切换，状态提示由渲染层给出。
        a.Context.NotifyToggleBuildingNames?.Invoke();
        return true;
    }

    private bool CycleSelect(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (map.Buildings.Count == 0)
        {
            a.Status(Loc.T("Mode.Building.Msg.NoBuildings"));
            return false;
        }

        // 按格子序号升序取建筑：Buildings 列表顺序会受新增建筑影响（新建筑被追加到末尾），
        // 直接按列表索引递推会导致遍历顺序与坐标顺序不一致。取上一个选中格之后的下一个，到末尾则回到第一个。
        var ordered = GetSortedBuildings(map);
        int pos = ordered.FindIndex(b => b.Coordinate > _lastSelectedBuildingCoord);
        if (pos < 0) pos = 0;

        var selected = ordered[pos];
        _lastSelectedBuildingCoord = selected.Coordinate;

        int col = selected.Coordinate % map.MapWidth;
        int row = selected.Coordinate / map.MapWidth;
        var selector = HexSelector.Instance;
        selector.ClearSelection();
        selector.Select(col, row, map.MapWidth, map.MapHeight);

        // 移动视角到该建筑（对齐 VB：_mapRenderer.Camera.CenterOnHex）
        a.Context.NotifyMoveCameraToHex?.Invoke(col, row);

        a.Status(Loc.F("Mode.Building.Msg.CycleSelected", pos + 1, ordered.Count,
            BuildingModifier.GetDisplayName(selected), BuildingModifier.GetTypeDisplayName(selected.BuildingType)));
        return true;
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

    private static bool RecognizeText(ModeActionArgs a)
    {
        // 完整流程（识别已加载的背景图 → OCR → 导出地图网格 → 生成 zme → 放置建筑）由 GUI 层实现。
        // 识别对象就是当前的视图层背景图，与省份模式 P 键一致，无需再选图。
        a.Context.NotifyRecognizeTextBuildings?.Invoke();
        return true;
    }

    #endregion

    #region 复制 / 粘贴 / 删除

    private static bool Copy(ModeActionArgs a)
    {
        var building = a.Module<BuildingModifier>();
        var targets = a.TargetCells();
        if (targets.Count > 1)
        {
            int count = building.CopyBuildingGroup(targets);
            if (count > 0)
            {
                a.Status(Loc.F("Mode.Building.Msg.GroupCopied", count));
                return true;
            }
        }

        var result = building.CopyBuilding(a.Col, a.Row);
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private static bool CopyLevels(ModeActionArgs a)
    {
        var result = a.Module<BuildingModifier>().CopyBuildingLevels(a.Col, a.Row);
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>
    /// 粘贴：剪贴板是建筑组时以焦点格为左上角整组粘贴；是单个建筑（完整或仅等级）时粘到每个目标格。
    /// 完整粘贴按所在省份的省会设置归属；全部可撤销，撤销时被覆盖的建筑原样放回。
    /// </summary>
    private static bool Paste(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var building = a.Module<BuildingModifier>();

        if (building.HasCopiedGroup)
        {
            int count = 0;
            RecordCells(a, Loc.F("Mode.Building.Undo.PasteGroup", a.Col, a.Row), building.GetGroupPasteCells(a.Col, a.Row),
                () => count = building.PasteBuildingGroup(a.Col, a.Row));
            a.Status(count > 0 ? Loc.F("Mode.Building.Msg.GroupPasted", count) : Loc.T("Mode.Building.Msg.GroupPasteFailed"));
            return count > 0;
        }

        if (!building.HasCopiedBuilding)
        {
            a.Status(Loc.T("Module.Building.NothingCopied"));
            return false;
        }

        var targets = a.TargetCells();
        var description = Loc.F(building.CopiedLevelsOnly ? "Mode.Building.Undo.PasteLevels" : "Mode.Building.Undo.Paste", a.Col, a.Row);
        ModifierResult last = default;
        int pasted = 0;
        RecordCells(a, description, targets, () =>
        {
            foreach (var cell in targets)
            {
                last = building.PasteBuilding(cell.Col, cell.Row);
                if (last.Success) pasted++;
            }
        });

        a.Status(targets.Count == 1 ? last.Message ?? "" : Loc.F("Mode.Building.Msg.MultiPasted", pasted, targets.Count));
        return pasted > 0;
    }

    /// <summary>删除格子上的建筑并清除归属（对齐 VB DeleteBuildingAndBelong）。</summary>
    private static bool DeleteCells(ModeActionArgs a, IReadOnlyList<HexCoord> cells)
    {
        if (a.Map == null) return false;
        var building = a.Module<BuildingModifier>();
        var belong = a.Module<BelongModifier>();

        RecordCells(a, Loc.F("Mode.Building.Undo.DeleteWithBelong", a.Col, a.Row), cells, () =>
        {
            foreach (var cell in cells)
            {
                building.Remove(cell.Col, cell.Row);
                belong.SetBelongByCountryId(cell.Col, cell.Row, 0xFF);
            }
        });

        a.Status(cells.Count == 1 ? Loc.T("Mode.Building.Msg.DeletedWithBelong") : Loc.F("Mode.Building.Msg.DeletedWithBelongMulti", cells.Count));
        return true;
    }

    private static async Task<bool> DeleteAllAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (!await ConfirmAsync(a, Loc.T("Mode.Building.Confirm.DeleteAll"))) return false;

        int count = map.Buildings.Count;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.DeleteAll"), building.RemoveAll);
        a.Status(Loc.F("Mode.Building.Msg.AllDeleted", count));
        return true;
    }

    private static async Task<bool> DeleteNonCapitalAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        if (!await ConfirmAsync(a, Loc.T("Mode.Building.Confirm.DeleteNonCapital"))) return false;

        int removed = 0;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.DeleteNonCapital"), () => removed = building.RemoveNonCapitalBuildings());
        a.Status(Loc.F("Mode.Building.Msg.NonCapitalDeleted", removed));
        return removed > 0;
    }

    #endregion

    #region 关键据点与占领事件

    /// <summary>F：关键据点按 无 → 红圈 → 绿圈 → 无 循环；多选时所有目标都设成焦点建筑的下一个状态。</summary>
    private static bool CycleKeyPoint(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var targets = BuildingTargets(a, map);
        if (targets.Count == 0)
        {
            a.Status(Loc.T("Module.Building.NoBuildingHere"));
            return false;
        }

        byte next = BuildingModifier.NextKeyPoint(CurrentBuilding(map, targets[0]).KeyPoint);
        var building = a.Module<BuildingModifier>();
        RecordCells(a, Loc.F("Mode.Building.Undo.KeyPoint", a.Col, a.Row), targets, () =>
        {
            foreach (var cell in targets) building.SetKeyPoint(cell.Col, cell.Row, next);
        });
        a.Status(BuildingModifier.KeyPointMessage(next));
        return true;
    }

    /// <summary>Ctrl+F：只改建筑上的占领触发事件 ID。</summary>
    private static async Task<bool> SetOccupationEventAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var targets = BuildingTargets(a, map);
        int? eventId = await AskOccupationEventAsync(a, map, targets);
        if (eventId is not { } id) return false;

        var building = a.Module<BuildingModifier>();
        RecordCells(a, Loc.F("Mode.Building.Undo.OccupationEvent", a.Col, a.Row), targets, () =>
        {
            foreach (var cell in targets) building.SetOccupationEvent(cell.Col, cell.Row, id);
        });
        a.Status(Loc.F("Module.Building.OccupationEventSet", id));
        return true;
    }

    /// <summary>Ctrl+Q：设置占领触发事件 ID，并把该序号的地图事件改为占地触发（对齐 VB SetBuildingOccupationEventById）。</summary>
    private static async Task<bool> LinkOccupationEventAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var targets = BuildingTargets(a, map);
        int? eventId = await AskOccupationEventAsync(a, map, targets);
        if (eventId is not { } id) return false;

        ModifierResult result = default;
        var building = a.Module<BuildingModifier>();
        // 同时改建筑列表与事件列表：用全实体快照，一次 Ctrl+Z 两样一起撤回
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Building.Undo.LinkOccupationEvent", id),
            () => result = building.LinkOccupationEvent(targets, id));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    /// <summary>
    /// 询问占领事件 ID（0-255，默认值取焦点建筑当前的值）。没有建筑、没有界面、取消或输入无效时返回 null 并给出提示
    /// （没有界面时不再像 VB 的 Ctrl+F 占位实现那样直接写死 1）。
    /// </summary>
    private static async Task<int?> AskOccupationEventAsync(ModeActionArgs a, MapData map, List<HexCoord> targets)
    {
        if (targets.Count == 0)
        {
            a.Status(Loc.T("Module.Building.NoBuildingHere"));
            return null;
        }
        if (a.Dialogs == null)
        {
            a.Status(Loc.T("Mode.Building.Msg.NeedsDialog"));
            return null;
        }

        var input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Building.Dialog.OccupationEventTitle"),
            Loc.T("Mode.Building.Dialog.OccupationEventPrompt"),
            CurrentBuilding(map, targets[0]).OccupationEvent.ToString(CultureInfo.InvariantCulture), 0, 255);
        if (input == null) return null;
        if (int.TryParse(input, out int eventId) && eventId is >= 0 and <= 255) return eventId;

        a.Status(Loc.T("Mode.Building.Msg.InvalidEventId"));
        return null;
    }

    private static bool MarkEventBuildings(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        int count = 0;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.MarkEventBuildings"), () => count = building.MarkAllEventBuildingsWithRedCircle());
        a.Status(Loc.F("Mode.Building.Msg.EventBuildingsMarked", count));
        return count > 0;
    }

    #endregion

    #region 随机化与批量生成

    private static async Task<bool> RandomByBelongAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;

        // 没有界面（命令行 / 脚本）时按"全部归属"执行
        int belongId = -1;
        if (a.Dialogs != null)
        {
            var input = await a.Dialogs.ShowInputDialogAsync(
                Loc.T("Mode.Building.Dialog.RandomByBelongTitle"), Loc.T("Mode.Building.Dialog.BelongIdPrompt"), "-1");
            if (input == null) return false;
            if (!int.TryParse(input, out belongId))
            {
                a.Status(Loc.T("Mode.Building.Msg.InvalidBelongId"));
                return false;
            }
        }

        ModifierResult result = default;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.F("Mode.Building.Undo.RandomByBelong", belongId),
            () => result = building.RandomizeBuildingsByBelong(belongId));
        a.Status(result.Message ?? "");
        return result.AffectedCount > 0;
    }

    private static async Task<bool> GenerateCapitalsAsync(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        var building = a.Module<BuildingModifier>();

        bool overwrite = false;
        int existing = building.CountBuildingsWithExistingProvince();
        if (existing > 0 && a.Dialogs != null)
        {
            var input = await a.Dialogs.ShowInputDialogAsync(
                Loc.T("Mode.Building.Dialog.GenCapitalsTitle"), Loc.F("Mode.Building.Dialog.GenCapitalsPrompt", existing), "2", 1, 2);
            // 关掉对话框 = 取消（以前关掉等于"仅处理空白格子"，操作没法取消）
            if (input == null || !int.TryParse(input, out int choice)) return false;
            overwrite = choice == 1;
        }

        int count = 0;
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.GenCapitals"), () => count = building.GenerateCapitalsForAllBuildings(overwrite));
        a.Status(Loc.F("Mode.Building.Msg.CapitalsGenerated", count));
        return count > 0;
    }

    private static async Task<bool> RandomizeTypesAsync(ModeActionArgs a, bool named)
    {
        if (a.Map == null || a.Dialogs == null) return false;

        var input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T(named ? "Mode.Building.Dialog.RandomNamedTitle" : "Mode.Building.Dialog.RandomUnnamedTitle"),
            Loc.T("Mode.Building.Dialog.RandomTypeModePrompt"), "2", 1, 2);
        if (input == null || !int.TryParse(input, out int choice)) return false;

        bool useCondition = choice == 1;
        ModifierResult result = default;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T(named ? "Mode.Building.Undo.RandomNamed" : "Mode.Building.Undo.RandomUnnamed"),
            () => result = named ? building.RandomizeNamedBuildingTypes(useCondition) : building.RandomizeUnnamedBuildingTypes(useCondition));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    private static async Task<bool> GenerateByProbabilityAsync(ModeActionArgs a)
    {
        if (a.Map == null || a.Dialogs == null) return false;

        var (confirmed, belongText, probabilityText) = await a.Dialogs.ShowDoubleInputDialogAsync(
            Loc.T("Mode.Building.Dialog.GenByProbabilityTitle"), Loc.T("Mode.Building.Dialog.BelongIdPrompt"),
            Loc.T("Mode.Building.Dialog.ProbabilityPrompt"), "-1", "50");
        if (!confirmed) return false;

        if (!int.TryParse(belongText, out int belongId))
        {
            a.Status(Loc.T("Mode.Building.Msg.InvalidBelongId"));
            return false;
        }
        if (!int.TryParse(probabilityText, out int probability))
        {
            a.Status(Loc.T("Mode.Building.Msg.InvalidProbability"));
            return false;
        }

        ModifierResult result = default;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.GenByProbability"),
            () => result = building.GenerateBuildingsByBelongProbability(belongId, probability));
        a.Status(result.Message ?? "");
        return result.AffectedCount > 0;
    }

    private static bool RandomOnCapitals(ModeActionArgs a)
    {
        if (a.Map == null) return false;
        int count = 0;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.RandomOnCapitals"), () => count = building.RandomizeBuildingsOnCapitals());
        a.Status(Loc.F("Mode.Building.Msg.CapitalBuildingsGenerated", count));
        return count > 0;
    }

    private static bool SmartAppearance(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        int count = 0;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.SmartAppearance"), () => count = building.SmartSetBuildingAppearance(map));
        a.Status(Loc.F("Mode.Building.Msg.AppearanceSet", count));
        return count > 0;
    }

    private static async Task<bool> GenerateCoastalAsync(ModeActionArgs a)
    {
        if (a.Map == null || a.Dialogs == null) return false;

        var input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Building.Dialog.CoastalTitle"), Loc.T("Mode.Building.Dialog.CoastalPrompt"), "50", 0, 100);
        if (input == null) return false;
        if (!int.TryParse(input, out int probability))
        {
            a.Status(Loc.T("Mode.Building.Msg.InvalidProbability"));
            return false;
        }

        ModifierResult result = default;
        var building = a.Module<BuildingModifier>();
        a.Context.RecordBuildingsSnapshot(Loc.T("Mode.Building.Undo.Coastal"), () => result = building.GenerateBuildingsOnCoastalHexes(probability));
        a.Status(result.Message ?? "");
        return result.AffectedCount > 0;
    }

    #endregion

    #region 建筑设置窗口与放置

    /// <summary>Q：格子上有建筑时编辑它，没有时新建（新建时按省会自动设置归属，对齐 VB OpenBuildingCreateWindow）。</summary>
    private static async Task<bool> OpenSettingDialogAsync(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        if (a.Dialogs == null)
        {
            a.Status(Loc.T("Mode.Building.Msg.NoDialogService"));
            return false;
        }

        var existing = map.GetBuildingAt(a.Col, a.Row);
        bool isNew = existing == null;
        var initial = existing ?? Building.CreateDefault(a.Row * map.MapWidth + a.Col);
        a.Status(Loc.T(isNew ? "Mode.Building.Msg.OpenCreate" : "Mode.Building.Msg.OpenEdit"));

        var (confirmed, edited) = await a.Dialogs.ShowBuildingSettingDialogAsync(initial, isNew);
        if (!confirmed)
        {
            a.Status(Loc.T("Mode.Building.Msg.DialogCancelled"));
            return false;
        }

        var building = a.Module<BuildingModifier>();
        RecordCells(a, Loc.F(isNew ? "Mode.Building.Undo.Create" : "Mode.Building.Undo.Edit", a.Col, a.Row), new[] { a.Cell },
            () => building.PlaceBuilding(a.Col, a.Row, edited, autoBelong: isNew));
        a.Status(isNew
            ? Loc.F("Mode.Building.Msg.Created", a.Col, a.Row, BuildingModifier.GetTypeDisplayName(edited.BuildingType))
            : Loc.F("Mode.Building.Msg.Updated", a.Col, a.Row));
        return true;
    }

    /// <summary>放置默认建筑（脚本 / AI 用的 apply 动作），按省会自动设置归属。</summary>
    private static bool PlaceDefault(ModeActionArgs a)
    {
        if (a.Map is not { } map) return false;
        var building = a.Module<BuildingModifier>();
        ModifierResult result = default;
        RecordCells(a, Loc.F("Mode.Building.Undo.Place", a.Col, a.Row), new[] { a.Cell },
            () => result = building.PlaceBuilding(a.Col, a.Row, Building.CreateDefault(a.Row * map.MapWidth + a.Col), autoBelong: true));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    #endregion

    #region 移动工具（K）

    private bool ToggleMoveTool(ModeActionArgs a)
    {
        MoveToolActive = !MoveToolActive;
        _moveSource = null;
        // 通知 GUI：关闭时结束进行中的拖拽（鼠标拖拽由 GUI 处理，开关状态从 MoveToolActive 读取）
        a.Context.NotifyToggleBuildingMoveTool?.Invoke();
        a.Status(Loc.T(MoveToolActive ? "Mode.Building.Msg.MoveToolOn" : "Mode.Building.Msg.MoveToolOff"));
        return true;
    }

    /// <summary>右键按在建筑上：记下源格。移动工具未开启或该格没有建筑时返回 false（GUI 改做框选）。</summary>
    private bool BeginMove(ModeActionArgs a)
    {
        _moveSource = null;
        if (!MoveToolActive || a.Map is not { } map) return false;

        var source = map.GetBuildingAt(a.Col, a.Row);
        if (source == null) return false;

        _moveSource = a.Cell;
        a.Status(Loc.F("Mode.Building.Msg.Moving", BuildingModifier.GetTypeDisplayName(source.Value.BuildingType)));
        return true;
    }

    /// <summary>松开右键：把建筑移到目标格（剪切归属），可撤销。落回原格或地图外时什么都不改。</summary>
    private bool EndMove(ModeActionArgs a)
    {
        if (_moveSource is not { } source || a.Map == null) return false;
        _moveSource = null;
        if (a.Cell == source) return false;

        var building = a.Module<BuildingModifier>();
        ModifierResult result = default;
        RecordCells(a, Loc.F("Mode.Building.Undo.Move", source.Col, source.Row, a.Col, a.Row), new[] { source, a.Cell },
            () => result = building.MoveBuilding(source.Col, source.Row, a.Col, a.Row));
        a.Status(result.Message ?? "");
        return result.Success;
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 只涉及少数格子的变更：执行前后各拍一份局部快照（建筑列表 + 这些格子的归属），有变化才记一条撤销。
    /// 撤销时被覆盖的建筑原样放回（以前粘贴覆盖已有建筑后撤销，会把原建筑删掉）。
    /// </summary>
    private static void RecordCells(ModeActionArgs a, string description, IReadOnlyCollection<HexCoord> cells, Action apply)
    {
        var building = a.Module<BuildingModifier>();
        var before = building.CaptureCells(cells);
        apply();
        var after = building.CaptureCells(cells);
        if (before.SameAs(after)) return;

        a.Context.RecordEntityChange(description, () => building.RestoreCells(after), () => building.RestoreCells(before));
    }

    /// <summary>有界面时弹确认框；命令行 / 脚本（没有对话框服务）直接执行。</summary>
    private static async Task<bool> ConfirmAsync(ModeActionArgs a, string message)
        => a.Dialogs == null
           || await a.Dialogs.ShowConfirmDialogAsync(Loc.T("Mode.Building.Confirm.Title"), message, Loc.T("Common.Ok"), Loc.T("Common.Cancel"));

    /// <summary>目标格（多选时为整个选区）里有建筑的那些；焦点格有建筑时排在最前（多选时以它的当前值为准）。</summary>
    private static List<HexCoord> BuildingTargets(ModeActionArgs a, MapData map)
    {
        var cells = a.TargetCells().Where(c => map.FindBuildingIndex(c.Col, c.Row) >= 0).ToList();
        int focus = cells.IndexOf(a.Cell);
        if (focus > 0)
        {
            cells.RemoveAt(focus);
            cells.Insert(0, a.Cell);
        }
        return cells;
    }

    private static Building CurrentBuilding(MapData map, HexCoord cell) => map.Buildings[map.FindBuildingIndex(cell.Col, cell.Row)];

    #endregion
}
