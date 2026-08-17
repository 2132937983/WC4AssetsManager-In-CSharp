using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Services;
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
    private readonly Dictionary<EditMode, IModeHandler> _modeHandlers = new();
    private readonly Dictionary<string, List<EditMode>> _sceneModeMap = new();
    private EditMode _currentMode = EditMode.None;
    private MapData? _mapData;
    private bool _isInitialized;
    private UndoManager? _undoManager;
    private ModeContext? _modeContext;
    private IDialogService? _dialogService;
    private ICliCommandExecutor? _cliCommandExecutor;
    private Func<Task<(bool success, int modifiedCount)>>? _recognizeTerrainCallback;

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

        RegisterModeHandler(new TerrainPaintMode());
        RegisterModeHandler(new TerritoryEditMode());
        RegisterModeHandler(new BuildingDeployMode());
        RegisterModeHandler(new ArmyDeployMode());
        RegisterModeHandler(new TrapDeployMode());
        RegisterModeHandler(new ReinforcementDeployMode());

        BuildSceneModeMap();
    }

    #region 公共属性

    public EditMode CurrentMode => _currentMode;
    public IModifier? PrimaryModifier => GetCurrentHandler() != null ? GetModifier(GetCurrentHandler()!.PrimaryModifierKind) : null;
    public IReadOnlyList<IModifier> ActiveModifiers => GetActiveModifiers();
    public bool IsEditModeActive => _currentMode != EditMode.None;

    public void SetUndoManager(UndoManager undoManager)
    {
        _undoManager = undoManager;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetDialogService(IDialogService dialogService)
    {
        _dialogService = dialogService;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetCliCommandExecutor(ICliCommandExecutor cliCommandExecutor)
    {
        _cliCommandExecutor = cliCommandExecutor;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetRecognizeTerrainCallback(Func<Task<(bool success, int modifiedCount)>> callback)
    {
        _recognizeTerrainCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    #endregion

    #region 初始化

    public void Initialize(MapData mapData)
    {
        _mapData = mapData;
        _isInitialized = true;
        foreach (var modifier in _modifiers.Values)
            modifier.Initialize(mapData);
        RebuildModeContext();
    }

    public void Deinitialize()
    {
        _currentMode = EditMode.None;
        foreach (var modifier in _modifiers.Values)
            modifier.Deinitialize();
        _mapData = null;
        _isInitialized = false;
        _modeContext = null;
    }

    public void UpdateMapData(MapData mapData)
    {
        _mapData = mapData;
        foreach (var modifier in _modifiers.Values)
            modifier.Initialize(mapData);
        RebuildModeContext();
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
        return _modeHandlers.TryGetValue(mode, out var handler) ? GetModifier(handler.PrimaryModifierKind) : null;
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

        if (mode != EditMode.None && !_modeHandlers.ContainsKey(mode))
        {
            RaiseStatusMessage($"不支持的模式: {mode}");
            return _currentMode;
        }

        _currentMode = mode;
        var handler = GetCurrentHandler();

        if (previousMode != _currentMode)
        {
            ModeChanged?.Invoke(this, new EditModeChangedEventArgs(
                previousMode, _currentMode, PrimaryModifier, ActiveModifiers));

            if (handler != null)
            {
                RaiseStatusMessage($"已切换到: {handler.DisplayName}");
                RaiseStatusMessage(handler.HelpText);
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
        var handler = GetCurrentHandler();
        if (handler == null) return result;

        foreach (var kind in handler.ModifierKinds)
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
        var handler = GetCurrentHandler();
        if (handler == null) return "未选择编辑模式，按 Space 切换模式";
        return handler.HelpText;
    }

    public string GetModeStatusText()
    {
        var handler = GetCurrentHandler();
        if (handler == null) return "浏览模式";
        return handler.DisplayName;
    }

    public string GetFullHelpText()
    {
        var handler = GetCurrentHandler();
        if (handler == null)
            return "浏览模式\nTab - 切换编辑模式";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(handler.DisplayName);
        sb.Append(handler.HelpText);
        sb.AppendLine();
        sb.AppendLine("Space - 切换模式");
        sb.AppendLine("Esc - 退出编辑");
        return sb.ToString();
    }

    #endregion

    #region 键盘动作分发

    public async Task<bool> HandleKeyAction(string action, int col, int row)
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

        var handler = GetCurrentHandler();
        if (handler == null || _modeContext == null) return false;

        return await handler.HandleKeyAction(action, col, row, _modeContext);
    }

    #endregion

    #region 撤销/重做辅助

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

        RecordChangesFromSnapshot(description, beforeSnapshot);
    }

    private TerrainData[]? _brushStrokeSnapshot;

    public void BeginBrushStroke()
    {
        if (_mapData == null)
        {
            _brushStrokeSnapshot = null;
            return;
        }

        var snapshot = new TerrainData[_mapData.MapWidth * _mapData.MapHeight];
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = _mapData.GetTerrainRef(i);
        _brushStrokeSnapshot = snapshot;
    }

    public void EndBrushStroke(string description)
    {
        var snapshot = _brushStrokeSnapshot;
        _brushStrokeSnapshot = null;
        if (_mapData == null || _undoManager == null || snapshot == null) return;
        RecordChangesFromSnapshot(description, snapshot);
    }

    private void RecordChangesFromSnapshot(string description, TerrainData[] beforeSnapshot)
    {
        if (_mapData == null || _undoManager == null) return;

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

    public void RecordProvinceChange(int col, int row, string description, Action applyChange)
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

    public void RecordEntityChange(string description, Action execute, Action undo)
    {
        if (_undoManager == null)
        {
            execute();
            return;
        }

        execute();
        _undoManager.Record(new DelegateCommand(description, execute, undo));
    }

    public void RecordMapResizeChange(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var command = new MapResizeCommand(_mapData, description);
        applyChange();
        command.CaptureAfterState();
        _undoManager.Record(command);
    }

    #endregion

    #region 内部方法

    private IModeHandler? GetCurrentHandler()
    {
        return _currentMode != EditMode.None && _modeHandlers.TryGetValue(_currentMode, out var h) ? h : null;
    }

    private IReadOnlyList<IModifier> GetActiveModifiers()
    {
        var handler = GetCurrentHandler();
        if (handler == null) return Array.Empty<IModifier>();

        var list = new List<IModifier>(handler.ModifierKinds.Length);
        foreach (var kind in handler.ModifierKinds)
        {
            if (_modifiers.TryGetValue(kind, out var modifier))
                list.Add(modifier);
        }
        return list;
    }

    private void RegisterModifier(ModifierKind kind, IModifier modifier)
    {
        _modifiers[kind] = modifier;
    }

    private void RegisterModeHandler(IModeHandler handler)
    {
        _modeHandlers[handler.Mode] = handler;
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

    private void RebuildModeContext()
    {
        _modeContext = new ModeContext
        {
            MapData = _mapData,
            UndoManager = _undoManager,
            RaiseStatusMessage = RaiseStatusMessage,
            NotifyDataModified = () => DataModified?.Invoke(this, EventArgs.Empty),
            NotifyBrushToggled = () => BrushToggled?.Invoke(this, EventArgs.Empty),
            DialogService = _dialogService,
            CliCommandExecutor = _cliCommandExecutor,
            RecognizeTerrainCallback = _recognizeTerrainCallback
        };
    }

    public void RaiseStatusMessage(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    #endregion
}