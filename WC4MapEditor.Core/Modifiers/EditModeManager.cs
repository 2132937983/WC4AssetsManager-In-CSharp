using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public enum EditMode
{
    None = 0,
    TerrainPaint = 1,
    TerritoryEdit = 2,
    BuildingDeploy = 3,
    ArmyDeploy = 4,
    TrapDeploy = 5,
    ReinforcementDeploy = 6
}

public enum ModifierKind
{
    Terrain = 0,
    Province = 1,
    Building = 2,
    Army = 3,
    Legion = 4,
    Belong = 5,
    Trap = 6,
    Reinforcement = 7
}

public sealed class EditModeChangedEventArgs : EventArgs
{
    public EditMode PreviousMode { get; }
    public EditMode CurrentMode { get; }
    public IModifier? PrimaryModifier { get; }
    public IReadOnlyList<IModifier> ActiveModifiers { get; }

    public EditModeChangedEventArgs(EditMode previousMode, EditMode currentMode,
        IModifier? primaryModifier, IReadOnlyList<IModifier> activeModifiers)
    {
        PreviousMode = previousMode;
        CurrentMode = currentMode;
        PrimaryModifier = primaryModifier;
        ActiveModifiers = activeModifiers;
    }
}

public sealed class EditModeManager
{
    private static readonly object _lock = new();
    private static EditModeManager? _instance;

    public static EditModeManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EditModeManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<ModifierKind, IModifier> _modifiers = new();
    private readonly Dictionary<EditMode, ModeProfile> _modeProfiles = new();
    private readonly Dictionary<string, List<EditMode>> _sceneModeMap = new();
    private EditMode _currentMode = EditMode.None;
    private MapData? _mapData;
    private bool _isInitialized;
    private UndoManager? _undoManager;

    public event EventHandler<EditModeChangedEventArgs>? ModeChanged;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? DataModified;
    public event EventHandler? BrushToggled;

    private EditModeManager()
    {
        RegisterModifier(ModifierKind.Terrain, new TerrainModifier());
        RegisterModifier(ModifierKind.Province, new ProvinceModifier());
        RegisterModifier(ModifierKind.Building, new BuildingModifier());
        RegisterModifier(ModifierKind.Army, new ArmyModifier());
        RegisterModifier(ModifierKind.Legion, new LegionModifier());
        RegisterModifier(ModifierKind.Belong, new BelongModifier());
        RegisterModifier(ModifierKind.Trap, new TrapModifier());
        RegisterModifier(ModifierKind.Reinforcement, new ReinforcementModifier());

        BuildModeProfiles();

        foreach (var profile in _modeProfiles.Values)
            profile.Resolve(_modifiers);

        BuildSceneModeMap();
    }

    #region 公共属性

    public EditMode CurrentMode => _currentMode;
    public IModifier? PrimaryModifier => GetCurrentProfile()?.Primary;
    public IReadOnlyList<IModifier> ActiveModifiers => GetCurrentProfile()?.ActiveModifiers ?? Array.Empty<IModifier>();
    public bool IsEditModeActive => _currentMode != EditMode.None;

    public void SetUndoManager(UndoManager undoManager) => _undoManager = undoManager;

    #endregion

    #region 初始化

    public void Initialize(MapData mapData)
    {
        _mapData = mapData;
        _isInitialized = true;
        foreach (var modifier in _modifiers.Values)
            modifier.Initialize(mapData);
    }

    public void Deinitialize()
    {
        _currentMode = EditMode.None;
        foreach (var modifier in _modifiers.Values)
            modifier.Deinitialize();
        _mapData = null;
        _isInitialized = false;
    }

    public void UpdateMapData(MapData mapData)
    {
        _mapData = mapData;
        foreach (var modifier in _modifiers.Values)
            modifier.Initialize(mapData);
    }

    #endregion

    #region 修改器访问

    public T? GetModifier<T>() where T : class, IModifier
    {
        foreach (var modifier in _modifiers.Values)
        {
            if (modifier is T t) return t;
        }
        return null;
    }

    public IModifier? GetModifier(ModifierKind kind)
    {
        return _modifiers.TryGetValue(kind, out var m) ? m : null;
    }

    public IModifier? GetModifier(EditMode mode)
    {
        return _modeProfiles.TryGetValue(mode, out var profile) ? profile.Primary : null;
    }

    #endregion

    #region 模式切换

    public IReadOnlyList<EditMode> GetAvailableModes(string sceneType)
    {
        if (_sceneModeMap.TryGetValue(sceneType, out var modes))
            return modes;
        return Array.Empty<EditMode>();
    }

    public EditMode SwitchMode(EditMode mode)
    {
        if (!_isInitialized) return EditMode.None;

        var previousMode = _currentMode;
        var previousPrimary = PrimaryModifier;

        if (mode != EditMode.None && !_modeProfiles.ContainsKey(mode))
        {
            RaiseStatusMessage($"不支持的模式: {mode}");
            return _currentMode;
        }

        _currentMode = mode;
        var profile = GetCurrentProfile();

        if (previousMode != _currentMode)
        {
            ModeChanged?.Invoke(this, new EditModeChangedEventArgs(
                previousMode, _currentMode, profile?.Primary, ActiveModifiers));

            if (profile != null)
            {
                RaiseStatusMessage($"已切换到: {profile.DisplayName}");
                RaiseStatusMessage(profile.HelpText);
            }
            else
            {
                RaiseStatusMessage("浏览模式");
            }
        }

        return _currentMode;
    }

    public EditMode CycleMode(string sceneType)
    {
        var modes = GetAvailableModes(sceneType);
        if (modes.Count == 0) return _currentMode;

        int currentIdx = -1;
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i] == _currentMode) { currentIdx = i; break; }
        }

        if (_currentMode == EditMode.None || currentIdx < 0)
            return SwitchMode(modes[0]);

        int nextIdx = (currentIdx + 1) % modes.Count;
        return SwitchMode(modes[nextIdx]);
    }

    public EditMode ExitEditMode()
    {
        return SwitchMode(EditMode.None);
    }

    #endregion

    #region 修改操作

    public ModifierResult ApplyCurrent(int col, int row, object? parameter = null)
    {
        var primary = PrimaryModifier;
        if (primary == null) return ModifierResult.Fail("没有激活的修改器");
        var result = primary.Apply(col, row, parameter);
        if (result.Success) DataModified?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public ModifierResult RemoveCurrent(int col, int row)
    {
        var primary = PrimaryModifier;
        if (primary == null) return ModifierResult.Fail("没有激活的修改器");
        var result = primary.Remove(col, row);
        if (result.Success) DataModified?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public ModifierResult ApplyCurrentRange(IEnumerable<HexCoord> coords, object? parameter = null)
    {
        var primary = PrimaryModifier;
        if (primary == null) return ModifierResult.Fail("没有激活的修改器");
        var result = primary.ApplyRange(coords, parameter);
        if (result.Success) DataModified?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public object? GetCurrentDataAt(int col, int row)
    {
        return PrimaryModifier?.GetDataAt(col, row);
    }

    public Dictionary<ModifierKind, object?> GetAllActiveDataAt(int col, int row)
    {
        var result = new Dictionary<ModifierKind, object?>();
        var profile = GetCurrentProfile();
        if (profile == null) return result;

        foreach (var kind in profile.ModifierKinds)
        {
            if (_modifiers.TryGetValue(kind, out var modifier))
                result[kind] = modifier.GetDataAt(col, row);
        }
        return result;
    }

    #endregion

    #region 状态查询

    public string GetModeHelpText()
    {
        var profile = GetCurrentProfile();
        if (profile == null) return "未选择编辑模式，按 Tab 切换模式";
        return profile.HelpText;
    }

    public string GetModeStatusText()
    {
        var profile = GetCurrentProfile();
        if (profile == null) return "浏览模式";
        return profile.DisplayName;
    }

    public string GetFullHelpText()
    {
        var profile = GetCurrentProfile();
        if (profile == null)
            return "浏览模式\nTab - 切换编辑模式";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(profile.DisplayName);
        sb.Append(profile.HelpText);
        sb.AppendLine();
        sb.AppendLine("Tab - 切换模式");
        sb.AppendLine("Esc - 退出编辑");
        return sb.ToString();
    }

    #endregion

    #region 键盘动作分发

    public bool HandleKeyAction(string action, int col, int row)
    {
        if (action == "undo")
        {
            bool result = _undoManager?.Undo() ?? false;
            if (result) DataModified?.Invoke(this, EventArgs.Empty);
            return result;
        }

        if (action == "redo")
        {
            bool result = _undoManager?.Redo() ?? false;
            if (result) DataModified?.Invoke(this, EventArgs.Empty);
            return result;
        }

        if (_currentMode == EditMode.None) return false;

        switch (_currentMode)
        {
            case EditMode.TerrainPaint:
                return HandleTerrainPaintAction(action, col, row);
            case EditMode.TerritoryEdit:
                return HandleTerritoryEditAction(action, col, row);
            case EditMode.BuildingDeploy:
                return HandleBuildingDeployAction(action, col, row);
            case EditMode.ArmyDeploy:
                return HandleArmyDeployAction(action, col, row);
            case EditMode.TrapDeploy:
                return HandleTrapDeployAction(action, col, row);
            case EditMode.ReinforcementDeploy:
                return HandleReinforcementDeployAction(action, col, row);
        }
        return false;
    }

    private bool HandleTerrainPaintAction(string action, int col, int row)
    {
        var terrain = GetModifier<TerrainModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "increase_type":
                RecordTerrainChange(col, row, $"增加地形类型 ({col},{row})", () => terrain.ChangeTerrainType(col, row, 1));
                modified = true; break;
            case "decrease_type":
                RecordTerrainChange(col, row, $"减少地形类型 ({col},{row})", () => terrain.ChangeTerrainType(col, row, -1));
                modified = true; break;
            case "increase_decoration":
                RecordTerrainChange(col, row, $"增加变体 ({col},{row})", () => terrain.ChangeDecoration(col, row, 1));
                modified = true; break;
            case "decrease_decoration":
                RecordTerrainChange(col, row, $"减少变体 ({col},{row})", () => terrain.ChangeDecoration(col, row, -1));
                modified = true; break;
            case "copy":
                {
                    var selected = HexSelector.Instance.SelectedHexes;
                    if (selected.Count > 1)
                    {
                        var coords = selected.Select(h => (h.Col, h.Row));
                        terrain.CopyTerrainGroup(coords);
                    }
                    else
                    {
                        terrain.CopyTerrain(col, row);
                    }
                    SyncCopiedTerrainToGlobal();
                    return true;
                }
            case "paste":
                SyncGlobalCopiedTerrainToLocal();
                RecordTerrainChange(col, row, $"粘贴地形 ({col},{row})", () => terrain.PasteTerrain(col, row));
                modified = true; break;
            case "flood_fill":
                RecordFloodFill(col, row, terrain);
                modified = true; break;
            case "toggle_layer":
                terrain.EditLayer = (terrain.EditLayer % 3) + 1;
                RaiseStatusMessage($"编辑层: {terrain.EditLayer}");
                return true;
            case "set_river":
                RecordTerrainChange(col, row, $"绘制河流 ({col},{row})", () => terrain.SetRiverValue(col, row, 1));
                modified = true; break;
            case "greening":
                RecordMultiCellChange("绿化平地", () => terrain.ApplyGreening(50));
                modified = true; break;
            case "randomize_flat":
                RecordMultiCellChange("随机平地变体", () => terrain.RandomizeFlatTerrain(50));
                modified = true; break;
            case "randomize_variant":
                RecordMultiCellChange("随机当前层变体", () => terrain.RandomizeVariant(50));
                modified = true; break;
            case "recognize_terrain":
                RaiseStatusMessage("识别地形功能需要视图层支持");
                return true;
            case "create_coast":
                if (_mapData != null)
                {
                    RecordMultiCellChange("创建海岸线", () => { });
                    RaiseStatusMessage("海岸线缓存已触发重建");
                }
                return true;
            case "process_ocean_layer2":
                RaiseStatusMessage("处理海洋第二层功能待实现");
                return true;
            case "export_hd":
                RaiseStatusMessage("导出HD文件功能待实现");
                return true;
            case "connect_buildings":
                RaiseStatusMessage("连接建筑功能待实现（需要建筑数据）");
                return true;
            case "scale_map":
                RaiseStatusMessage("缩放地图功能待实现");
                return true;
            case "toggle_brush":
                BrushToggled?.Invoke(this, EventArgs.Empty);
                return true;
        }
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    private void RecordTerrainChange(int col, int row, string description, Action applyChange)
    {
        RecordMultiCellChange(description, applyChange);
    }

    public void RecordMultiCellChange(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var beforeSnapshot = new TerrainData[_mapData.MapWidth * _mapData.MapHeight];
        for (int i = 0; i < beforeSnapshot.Length; i++)
            beforeSnapshot[i] = _mapData.GetTerrainRef(i);

        applyChange();

        var changes = new List<(int col, int row, TerrainData before, TerrainData after)>();
        for (int r = 0; r < _mapData.MapHeight; r++)
        {
            for (int c = 0; c < _mapData.MapWidth; c++)
            {
                int idx = r * _mapData.MapWidth + c;
                TerrainData after = _mapData.GetTerrainRef(c, r);
                if (!beforeSnapshot[idx].Equals(after))
                    changes.Add((c, r, beforeSnapshot[idx], after));
            }
        }

        if (changes.Count > 0)
        {
            var command = new TerrainChangeCommand(_mapData, description, changes.ToArray());
            _undoManager.Record(command);
        }
    }

    private void RecordFloodFill(int col, int row, TerrainModifier terrain)
    {
        RecordMultiCellChange($"洪水填充 ({col},{row})", () => terrain.FloodFill(col, row, (byte)terrain.BrushTerrainType));
    }

    private void SyncCopiedTerrainToGlobal()
    {
        var terrain = GetModifier<TerrainModifier>();
        if (terrain == null) return;

        var sceneManager = RenderSceneManager.Instance;
        var copiedData = terrain.GetCopiedTerrainData();
        var copiedGroup = terrain.GetCopiedTerrainGroup();
        var anchor = terrain.GetCopyAnchor();

        if (copiedData.HasValue)
        {
            sceneManager.GlobalCopiedTerrain = copiedData.Value;
            sceneManager.GlobalCopiedFromCol = anchor.col;
            sceneManager.GlobalCopiedFromRow = anchor.row;
            sceneManager.GlobalCopiedFromSceneId = sceneManager.CurrentSceneId;
        }

        sceneManager.GlobalCopiedHexes.Clear();
        if (copiedGroup != null && copiedGroup.Count > 0)
        {
            foreach (var kv in copiedGroup)
                sceneManager.GlobalCopiedHexes[kv.Key] = kv.Value;
            sceneManager.GlobalCopiedRegionMinCol = anchor.col;
            sceneManager.GlobalCopiedRegionMinRow = anchor.row;
        }
    }

    private void SyncGlobalCopiedTerrainToLocal()
    {
        var sceneManager = RenderSceneManager.Instance;
        if (sceneManager.GlobalCopiedTerrain == null) return;

        var terrain = GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.SetCopiedTerrainData(sceneManager.GlobalCopiedTerrain.Value,
            sceneManager.GlobalCopiedFromCol, sceneManager.GlobalCopiedFromRow);

        if (sceneManager.GlobalCopiedHexes.Count > 0)
        {
            terrain.SetCopiedTerrainGroup(sceneManager.GlobalCopiedHexes,
                sceneManager.GlobalCopiedRegionMinCol, sceneManager.GlobalCopiedRegionMinRow);
        }
    }

    private void RecordProvinceChange(int col, int row, string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        Province before = _mapData.GetProvinceRef(col, row);
        applyChange();
        Province after = _mapData.GetProvinceRef(col, row);

        if (before.Equals(after)) return;

        var command = new ProvinceChangeCommand(_mapData, description,
            new[] { (col, row, before, after) });
        _undoManager.Record(command);
    }

    private void RecordEntityChange(string description, Action execute, Action undo)
    {
        if (_undoManager == null)
        {
            execute();
            return;
        }

        execute();
        _undoManager.Record(new DelegateCommand(description, execute, undo));
    }

    private bool HandleTerritoryEditAction(string action, int col, int row)
    {
        var province = GetModifier<ProvinceModifier>()!;
        var legion = GetModifier<LegionModifier>()!;
        var belong = GetModifier<BelongModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                RecordProvinceChange(col, row, $"设置军团领域 ({col},{row})", () => legion.Apply(col, row));
                modified = true; break;
            case "remove":
                RecordProvinceChange(col, row, $"清除军团领域 ({col},{row})", () => legion.Remove(col, row));
                modified = true; break;
            case "copy":
                belong.CopyBelongValue(col, row);
                return true;
            case "paste":
                RecordProvinceChange(col, row, $"粘贴归属 ({col},{row})", () => belong.PasteBelongValue(col, row));
                modified = true; break;
            case "next_legion":
                legion.SelectedLegionId = (legion.SelectedLegionId % 8) + 1;
                RaiseStatusMessage($"选中军团: {legion.SelectedLegionId}");
                return true;
            case "prev_legion":
                legion.SelectedLegionId = ((legion.SelectedLegionId - 2 + 8) % 8) + 1;
                RaiseStatusMessage($"选中军团: {legion.SelectedLegionId}");
                return true;
            case "set_province":
                RecordProvinceChange(col, row, $"设置省份 ({col},{row})", () => province.Apply(col, row));
                modified = true; break;
            case "clear_province":
                RecordProvinceChange(col, row, $"清除省份 ({col},{row})", () => province.Remove(col, row));
                modified = true; break;
        }
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    private bool HandleBuildingDeployAction(string action, int col, int row)
    {
        var building = GetModifier<BuildingModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                RecordEntityChange($"放置建筑 ({col},{row})", () => building.Apply(col, row), () => building.Remove(col, row));
                modified = true; break;
            case "copy":
                building.CopyBuilding(col, row);
                return true;
            case "paste":
                RecordEntityChange($"粘贴建筑 ({col},{row})", () => building.PasteBuilding(col, row), () => building.Remove(col, row));
                modified = true; break;
            case "remove":
                {
                    var oldBuilding = _mapData?.GetBuildingAt(col, row);
                    if (oldBuilding != null)
                    {
                        var saved = oldBuilding.Value;
                        RecordEntityChange($"删除建筑 ({col},{row})",
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
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    private bool HandleArmyDeployAction(string action, int col, int row)
    {
        var army = GetModifier<ArmyModifier>()!;
        var belong = GetModifier<BelongModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                RecordEntityChange($"放置单位 ({col},{row})", () => army.Apply(col, row), () => army.Remove(col, row));
                modified = true; break;
            case "copy":
                army.CopyArmy(col, row);
                return true;
            case "paste":
                RecordEntityChange($"粘贴单位 ({col},{row})", () => army.PasteArmy(col, row), () => army.Remove(col, row));
                modified = true; break;
            case "remove":
                {
                    var oldArmy = _mapData?.GetArmyAt(col, row);
                    if (oldArmy != null)
                    {
                        var saved = oldArmy.Value;
                        RecordEntityChange($"删除单位 ({col},{row})",
                            () => army.Remove(col, row),
                            () => army.Apply(col, row, saved));
                    }
                    else
                    {
                        army.Remove(col, row);
                    }
                    modified = true; break;
                }
            case "set_legion":
                int legionId = belong.GetBelongValue(col, row) ?? 0;
                RecordEntityChange($"设置军团 ({col},{row})", () => army.SetLegionId(col, row, legionId), () => { });
                modified = true; break;
        }
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    private bool HandleTrapDeployAction(string action, int col, int row)
    {
        var trap = GetModifier<TrapModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                RecordEntityChange($"放置陷阱 ({col},{row})", () => trap.Apply(col, row), () => trap.Remove(col, row));
                modified = true; break;
            case "copy":
                trap.CopyTrap(col, row);
                return true;
            case "paste":
                RecordEntityChange($"粘贴陷阱 ({col},{row})", () => trap.PasteTrap(col, row), () => trap.Remove(col, row));
                modified = true; break;
            case "remove":
                RecordEntityChange($"删除陷阱 ({col},{row})", () => trap.Remove(col, row), () => trap.Apply(col, row));
                modified = true; break;
            case "next_legion":
                trap.SelectedLegionId = (trap.SelectedLegionId % 8) + 1;
                RaiseStatusMessage($"陷阱所属军团: {trap.SelectedLegionId}");
                return true;
            case "prev_legion":
                trap.SelectedLegionId = ((trap.SelectedLegionId - 2 + 8) % 8) + 1;
                RaiseStatusMessage($"陷阱所属军团: {trap.SelectedLegionId}");
                return true;
        }
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    private bool HandleReinforcementDeployAction(string action, int col, int row)
    {
        var reinforcement = GetModifier<ReinforcementModifier>()!;
        bool modified = false;
        switch (action)
        {
            case "apply":
                RecordEntityChange($"放置援军 ({col},{row})", () => reinforcement.Apply(col, row), () => reinforcement.Remove(col, row));
                modified = true; break;
            case "copy":
                reinforcement.CopyReinforcement(col, row);
                return true;
            case "paste":
                RecordEntityChange($"粘贴援军 ({col},{row})", () => reinforcement.PasteReinforcement(col, row), () => reinforcement.Remove(col, row));
                modified = true; break;
            case "remove":
                RecordEntityChange($"删除援军 ({col},{row})", () => reinforcement.Remove(col, row), () => reinforcement.Apply(col, row));
                modified = true; break;
            case "next_legion":
                reinforcement.SelectedLegionId = (reinforcement.SelectedLegionId % 8) + 1;
                RaiseStatusMessage($"援军所属军团: {reinforcement.SelectedLegionId}");
                return true;
            case "prev_legion":
                reinforcement.SelectedLegionId = ((reinforcement.SelectedLegionId - 2 + 8) % 8) + 1;
                RaiseStatusMessage($"援军所属军团: {reinforcement.SelectedLegionId}");
                return true;
        }
        if (modified) DataModified?.Invoke(this, EventArgs.Empty);
        return modified;
    }

    #endregion

    #region 内部方法

    private ModeProfile? GetCurrentProfile()
    {
        return _currentMode != EditMode.None && _modeProfiles.TryGetValue(_currentMode, out var p) ? p : null;
    }

    private void RegisterModifier(ModifierKind kind, IModifier modifier)
    {
        _modifiers[kind] = modifier;
    }

    private void BuildModeProfiles()
    {
        _modeProfiles[EditMode.TerrainPaint] = new ModeProfile(
            "地形绘制",
            "H - 使用画笔\n" +
            "C - 复制选中格子\n" +
            "V - 粘贴到选中格子\n" +
            "O - 控制是否显示视图\n" +
            "Y - 在该格绘制河流\n" +
            "P - 识别地形\n" +
            "U - 绿化平地\n" +
            "R - 随机平地变体\n" +
            "Shift+R - 随机当前层变体\n" +
            "F - 洪水填充\n" +
            "F4 - 创建海岸线\n" +
            "F5 - 处理海洋第二层\n" +
            "F6 - 导出HD文件\n" +
            "T - 连接建筑（平地连接11-15类型建筑）\n" +
            "G - 按比例缩放地图（0.1-10.0）\n" +
            "[, ] - 修改地形类型\n" +
            "Shift+[, ] - 修改变体\n" +
            "Z - 切换编辑层\n" +
            "B - 显示/隐藏网格\n" +
            "N - 显示/隐藏标签\n" +
            "F1 - 显示/隐藏第二层\n" +
            "Ctrl+F1 - 显示/隐藏帮助文本\n" +
            "W,A,S,D - 移动视角\n" +
            "I,J,K,L - 调整地图大小（无框选时）\n" +
            "框选操作：\n" +
            "  右键拖动 - 框选区域\n" +
            "  Shift+右键拖动 - 移除框选\n" +
            "  I,J,K,L - 移动选区\n" +
            "  Enter - 确认移动\n" +
            "  Esc - 取消移动\n" +
            "  O - 剔除海洋格子",
            ModifierKind.Terrain,
            new[] { ModifierKind.Terrain }
        );

        _modeProfiles[EditMode.TerritoryEdit] = new ModeProfile(
            "领域编辑",
            "领域编辑模式快捷键:\n" +
            "右键 - 设置领域(军团)\n" +
            "Delete - 清除领域\n" +
            "Q / E - 切换军团\n" +
            "C - 复制归属\n" +
            "V - 粘贴归属\n" +
            "S - 设置省份值\n" +
            "X - 清除省份值",
            ModifierKind.Legion,
            new[] { ModifierKind.Province, ModifierKind.Legion, ModifierKind.Belong }
        );

        _modeProfiles[EditMode.BuildingDeploy] = new ModeProfile(
            "建筑部署",
            "建筑部署模式快捷键:\n" +
            "右键 - 放置建筑\n" +
            "Delete - 删除建筑\n" +
            "C - 复制建筑\n" +
            "V - 粘贴建筑",
            ModifierKind.Building,
            new[] { ModifierKind.Building, ModifierKind.Province }
        );

        _modeProfiles[EditMode.ArmyDeploy] = new ModeProfile(
            "单位部署",
            "单位部署模式快捷键:\n" +
            "右键 - 放置单位\n" +
            "Delete - 删除单位\n" +
            "C - 复制单位\n" +
            "V - 粘贴单位\n" +
            "L - 按归属设置军团",
            ModifierKind.Army,
            new[] { ModifierKind.Army, ModifierKind.Belong }
        );

        _modeProfiles[EditMode.TrapDeploy] = new ModeProfile(
            "陷阱布置",
            "陷阱布置模式快捷键:\n" +
            "右键 - 放置陷阱\n" +
            "Delete - 删除陷阱\n" +
            "C - 复制陷阱\n" +
            "V - 粘贴陷阱\n" +
            "Q / E - 切换所属军团",
            ModifierKind.Trap,
            new[] { ModifierKind.Trap, ModifierKind.Belong }
        );

        _modeProfiles[EditMode.ReinforcementDeploy] = new ModeProfile(
            "援军配置",
            "援军配置模式快捷键:\n" +
            "右键 - 放置援军\n" +
            "Delete - 删除援军\n" +
            "C - 复制援军\n" +
            "V - 粘贴援军\n" +
            "Q / E - 切换所属军团",
            ModifierKind.Reinforcement,
            new[] { ModifierKind.Reinforcement, ModifierKind.Legion }
        );
    }

    private void BuildSceneModeMap()
    {
        _sceneModeMap["stage"] = new List<EditMode>
        {
            EditMode.TerrainPaint, EditMode.TerritoryEdit, EditMode.BuildingDeploy,
            EditMode.ArmyDeploy, EditMode.TrapDeploy, EditMode.ReinforcementDeploy
        };
        _sceneModeMap["conquest"] = new List<EditMode>
        {
            EditMode.TerritoryEdit, EditMode.BuildingDeploy,
            EditMode.ArmyDeploy, EditMode.TrapDeploy, EditMode.ReinforcementDeploy
        };
        _sceneModeMap["world"] = new List<EditMode>
        {
            EditMode.TerrainPaint
        };
    }

    public void RaiseStatusMessage(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    #endregion

    private sealed class ModeProfile
    {
        public string DisplayName { get; }
        public string HelpText { get; }
        public ModifierKind PrimaryKind { get; }
        public ModifierKind[] ModifierKinds { get; }
        public IModifier Primary { get; private set; }
        public IReadOnlyList<IModifier> ActiveModifiers { get; private set; }

        public ModeProfile(string displayName, string helpText,
            ModifierKind primaryKind, ModifierKind[] modifierKinds)
        {
            DisplayName = displayName;
            HelpText = helpText;
            PrimaryKind = primaryKind;
            ModifierKinds = modifierKinds;
            Primary = null!;
            ActiveModifiers = Array.Empty<IModifier>();
        }

        internal void Resolve(Dictionary<ModifierKind, IModifier> registry)
        {
            Primary = registry[PrimaryKind];
            var list = new List<IModifier>(ModifierKinds.Length);
            foreach (var kind in ModifierKinds)
            {
                if (registry.TryGetValue(kind, out var modifier))
                    list.Add(modifier);
            }
            ActiveModifiers = list;
        }
    }
}