using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.ErrorHandling;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public enum EditMode
{
    None = 0,
    TerrainPaint = 1,
    ProvinceEdit = 2,
    BuildingDeploy = 3,
    ArmyDeploy = 4,
    LegionEdit = 6,
    BelongEdit = 5,
    ReinforcementDeploy = 8
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
    private Func<Task>? _geoCalculateCallback;
    private Func<Task>? _geoExportRefCallback;
    private Func<Task>? _geoImportRefCallback;
    private Func<Task>? _geoExportGridCallback;
    private Func<Task>? _addGeoRefCallback;
    private Func<(int col, int row)>? _getFocusHexCallback;
    private string _currentSceneType = "stage";

    public event EventHandler<EditModeChangedEventArgs>? ModeChanged;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? DataModified;
    public event EventHandler? BrushToggled;
    public event EventHandler? BrushSizeChanged;
    /// <summary>国家领域（归属着色 + 归属国旗）显示开关被切换</summary>
    public event EventHandler? DomainToggled;

    /// <summary>建筑名称显示开关被切换（建筑编辑模式 U 键）</summary>
    public event EventHandler? BuildingNamesToggled;

    /// <summary>请求把相机中心移动到指定格子（建筑编辑模式 Enter 键循环选中）</summary>
    public event Action<int, int>? MoveCameraToHexRequested;

    /// <summary>建筑移动工具开/关被切换（建筑编辑模式 K 键）</summary>
    public event EventHandler? BuildingMoveToolToggled;

    /// <summary>文字识别生成建筑被请求（建筑编辑模式 P 键）</summary>
    public event EventHandler? RecognizeTextBuildingsRequested;

    /// <summary>军团范围截图被请求（军团编辑模式 P 键）</summary>
    public event EventHandler? CaptureLegionScreenshotRequested;

    /// <summary>军团设置窗口被请求（军团编辑模式 Q 键）</summary>
    public event EventHandler? OpenLegionSettingRequested;

    /// <summary>军团列表窗口被请求（军团编辑模式 F 键）</summary>
    public event EventHandler? OpenLegionListRequested;

    /// <summary>头部数据编辑窗口被请求（军团编辑模式 E 键）</summary>
    public event EventHandler? OpenHeaderSettingRequested;

    /// <summary>更新征服国家设置被请求（军团编辑模式 F6 键）</summary>
    public event EventHandler? UpdateConquerSettingsRequested;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public EditModeManager()
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
        RegisterModeHandler(new BelongEditMode());
        RegisterModeHandler(new LegionEditMode());
        RegisterModeHandler(new ProvinceEditMode());
        RegisterModeHandler(new BuildingDeployMode());
        RegisterModeHandler(new ArmyDeployMode());

        RegisterModeHandler(new ReinforcementDeployMode());

        BuildSceneModeMap();
        RegisterNumberKeyBindings();
    }

    #region 公共属性

    public EditMode CurrentMode => _currentMode;
    public IModifier? PrimaryModifier => GetCurrentHandler() != null ? GetModifier(GetCurrentHandler()!.PrimaryModifierKind) : null;
    public IReadOnlyList<IModifier> ActiveModifiers => GetActiveModifiers();
    public bool IsEditModeActive => _currentMode != EditMode.None;
    public bool IsSelectionActive => GetCurrentHandler()?.RequiresSelection ?? false;
    public bool IsInitialized => _isInitialized;

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

    public void SetGeoCalculateCallback(Func<Task> callback)
    {
        _geoCalculateCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetGeoExportRefCallback(Func<Task> callback)
    {
        _geoExportRefCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetGeoImportRefCallback(Func<Task> callback)
    {
        _geoImportRefCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetGeoExportGridCallback(Func<Task> callback)
    {
        _geoExportGridCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetAddGeoRefCallback(Func<Task> callback)
    {
        _addGeoRefCallback = callback;
        if (_isInitialized) RebuildModeContext();
    }

    public void SetGetFocusHexCallback(Func<(int col, int row)> callback)
    {
        _getFocusHexCallback = callback;
    }

    public void SetSceneType(string sceneType)
    {
        _currentSceneType = sceneType;
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
        // 退出场景前注销按键绑定，否则会残留到下一个场景。
        // 这里遍历所有模式而非只注销当前模式：_currentMode 可能已被置为 None，
        // 此时按当前模式查不到 handler，残留的绑定就清不掉。
        foreach (var registered in _modeHandlers.Values)
        {
            UnregisterModeKeyBindings(registered);
        }

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

        try
        {

        // 注销按键绑定，并处理选择器交接
        // 这里遍历所有已注册模式，而不是只按 previousMode 注销：
        // 跨场景切换时旧场景的 Deinitialize 会把 _currentMode 置为 None，
        // 此时 previousMode 为 None，只按它注销会让上个场景的模式绑定残留在键盘上，
        // 导致新场景按同名键时两个模式的功能被同时触发。
        IModeHandler? oldHandler = null;
        _modeHandlers.TryGetValue(previousMode, out oldHandler);
        foreach (var registered in _modeHandlers.Values)
        {
            UnregisterModeKeyBindings(registered);
        }

        // 旧模式离开时，如果它持有选择器则清空选区
        if (oldHandler != null && oldHandler.RequiresSelection)
        {
            var selector = HexSelector.Instance;
            selector.ClearSelection();
            selector.SetSelectionMoving(false, 0, 0, null);
        }

        _currentMode = mode;
        var handler = GetCurrentHandler();

        // 注册新模式的按键绑定
        if (handler != null)
        {
            RegisterModeKeyBindings(handler);
        }

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
        catch (Exception ex)
        {
            var error = ErrorCollector.Instance.RecordError(ErrorSeverity.Critical,
                $"模式切换失败: {previousMode} -> {mode}", nameof(SwitchMode), ex);

            // 尝试回退到上一个模式
            try
            {
                if (previousMode != EditMode.None && _modeHandlers.ContainsKey(previousMode))
                {
                    _currentMode = previousMode;
                    var handler = GetCurrentHandler();
                    if (handler != null)
                    {
                        RegisterModeKeyBindings(handler);
                        ModeChanged?.Invoke(this, new EditModeChangedEventArgs(
                            mode, previousMode, PrimaryModifier, ActiveModifiers));
                        RaiseStatusMessage($"模式切换失败，已回退到: {handler.DisplayName}");
                    }
                }
                else
                {
                    _currentMode = EditMode.None;
                    ModeChanged?.Invoke(this, new EditModeChangedEventArgs(
                        mode, EditMode.None, null, Array.Empty<IModifier>()));
                    RaiseStatusMessage("模式切换失败，已退出编辑模式");
                }
            }
            catch (Exception recoveryEx)
            {
                ErrorCollector.Instance.RecordError(ErrorSeverity.Fatal,
                    "模式回退失败", nameof(SwitchMode), recoveryEx);
                _currentMode = EditMode.None;
            }

            return _currentMode;
        }
    }

    private void RegisterModeKeyBindings(IModeHandler handler)
    {
        var keyboard = KeyboardManager.Instance;
        foreach (var binding in handler.GetKeyBindings())
        {
            keyboard.RegisterBinding(binding.Id, binding.KeyCode, binding.Modifiers,
                () => DispatchModeAction(handler, binding.Action), binding.Description);
        }
    }

    private void UnregisterModeKeyBindings(IModeHandler handler)
    {
        var keyboard = KeyboardManager.Instance;
        foreach (var binding in handler.GetKeyBindings())
        {
            keyboard.UnregisterBinding(binding.Id);
        }
    }

    private void DispatchModeAction(IModeHandler handler, string action)
    {
        if (_mapData == null) return;
        // 获取当前焦点坐标（由渲染层提供）
        var (col, row) = _getFocusHexCallback?.Invoke() ?? (0, 0);
        _ = handler.HandleKeyAction(action, col, row, _modeContext!);
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

    private void RegisterNumberKeyBindings()
    {
        var keyboard = KeyboardManager.Instance;

        // 数字键 1-8 快速切换模式
        var modeMap = new (int keyCode, EditMode mode, string name)[]
        {
            (KeyCodes.D1, EditMode.TerrainPaint, "地形绘制"),
            (KeyCodes.D2, EditMode.ProvinceEdit, "省份编辑"),
            (KeyCodes.D3, EditMode.BuildingDeploy, "建筑部署"),
            (KeyCodes.D4, EditMode.ArmyDeploy, "单位部署"),
            (KeyCodes.D5, EditMode.BelongEdit, "归属编辑"),
            (KeyCodes.D6, EditMode.LegionEdit, "军团编辑"),
            (KeyCodes.D8, EditMode.ReinforcementDeploy, "援军部署")
        };

        foreach (var (keyCode, mode, name) in modeMap)
        {
            keyboard.RegisterBinding($"MODE_SWITCH_{mode}", keyCode, KeyModifiers.None,
                () =>
                {
                    var availableModes = GetAvailableModes(_currentSceneType);
                    if (availableModes.Contains(mode))
                    {
                        SwitchMode(mode);
                    }
                    else
                    {
                        RaiseStatusMessage($"当前场景不支持 {name} 模式");
                    }
                },
                $"切换到{name}模式");
        }
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

    public void RecordMultiCellProvinceChange(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var beforeSnapshot = new Province[_mapData.MapWidth * _mapData.MapHeight];
        for (int i = 0; i < beforeSnapshot.Length; i++)
            beforeSnapshot[i] = _mapData.GetProvinceRef(i);

        applyChange();

        RecordProvinceChangesFromSnapshot(description, beforeSnapshot);
    }

    private TerrainData[]? _brushStrokeSnapshot;
    private Province[]? _provinceBrushStrokeSnapshot;
    private byte[]? _belongBrushStrokeSnapshot;

    public IBrushTarget? GetActiveBrushTarget()
    {
        return _currentMode switch
        {
            EditMode.TerrainPaint => GetModifier<TerrainModifier>() as IBrushTarget,
            EditMode.ProvinceEdit => GetModifier<ProvinceModifier>() as IBrushTarget,
            EditMode.BelongEdit => GetModifier<BelongModifier>() as IBrushTarget,
            _ => null
        };
    }

    public void BeginBrushStroke()
    {
        if (_mapData == null)
        {
            _brushStrokeSnapshot = null;
            _provinceBrushStrokeSnapshot = null;
            _belongBrushStrokeSnapshot = null;
            return;
        }

        if (_currentMode == EditMode.ProvinceEdit)
        {
            var snapshot = new Province[_mapData.MapWidth * _mapData.MapHeight];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = _mapData.GetProvinceRef(i);
            _provinceBrushStrokeSnapshot = snapshot;
            _brushStrokeSnapshot = null;
            _belongBrushStrokeSnapshot = null;
        }
        else if (_currentMode == EditMode.BelongEdit)
        {
            var snapshot = new byte[_mapData.MapWidth * _mapData.MapHeight];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = (byte)_mapData.GetBelongValueByIndex(i);
            _belongBrushStrokeSnapshot = snapshot;
            _brushStrokeSnapshot = null;
            _provinceBrushStrokeSnapshot = null;
        }
        else
        {
            var snapshot = new TerrainData[_mapData.MapWidth * _mapData.MapHeight];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = _mapData.GetTerrainRef(i);
            _brushStrokeSnapshot = snapshot;
            _provinceBrushStrokeSnapshot = null;
            _belongBrushStrokeSnapshot = null;
        }
    }

    public void EndBrushStroke(string description)
    {
        if (_currentMode == EditMode.ProvinceEdit)
        {
            var snapshot = _provinceBrushStrokeSnapshot;
            _provinceBrushStrokeSnapshot = null;
            if (_mapData == null || _undoManager == null || snapshot == null) return;
            RecordProvinceChangesFromSnapshot(description, snapshot);
        }
        else if (_currentMode == EditMode.BelongEdit)
        {
            var snapshot = _belongBrushStrokeSnapshot;
            _belongBrushStrokeSnapshot = null;
            if (_mapData == null || _undoManager == null || snapshot == null) return;
            RecordBelongChangesFromSnapshot(description, snapshot);
        }
        else
        {
            var snapshot = _brushStrokeSnapshot;
            _brushStrokeSnapshot = null;
            if (_mapData == null || _undoManager == null || snapshot == null) return;
            RecordChangesFromSnapshot(description, snapshot);
        }
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
            // 如果变更数量超过阈值，使用完整快照模式（更省内存）
            const int FullSnapshotThreshold = 50000;
            IUndoableCommand command;
            if (changes.Count > FullSnapshotThreshold)
            {
                command = new TerrainFullSnapshotCommand(_mapData, description, beforeSnapshot);
            }
            else
            {
                command = new TerrainChangeCommand(_mapData, description, changes.ToArray());
            }
            _undoManager.Record(command);
        }
    }

    private void RecordProvinceChangesFromSnapshot(string description, Province[] beforeSnapshot)
    {
        if (_mapData == null || _undoManager == null) return;

        var changes = new List<(int col, int row, Province before, Province after)>();
        for (int r = 0; r < _mapData.MapHeight; r++)
        {
            for (int c = 0; c < _mapData.MapWidth; c++)
            {
                int idx = r * _mapData.MapWidth + c;
                Province after = _mapData.GetProvinceRef(c, r);
                if (!beforeSnapshot[idx].Equals(after))
                    changes.Add((c, r, beforeSnapshot[idx], after));
            }
        }

        if (changes.Count > 0)
        {
            // 如果变更数量超过阈值，使用完整快照模式（更省内存）
            const int FullSnapshotThreshold = 50000;
            IUndoableCommand command;
            if (changes.Count > FullSnapshotThreshold)
            {
                command = new ProvinceFullSnapshotCommand(_mapData, description, beforeSnapshot);
            }
            else
            {
                command = new ProvinceChangeCommand(_mapData, description, changes.ToArray());
            }
            _undoManager.Record(command);
        }
    }

    private void RecordBelongChangesFromSnapshot(string description, byte[] beforeSnapshot)
    {
        if (_mapData == null || _undoManager == null) return;

        var changes = new List<(int col, int row, byte before, byte after)>();
        for (int r = 0; r < _mapData.MapHeight; r++)
        {
            for (int c = 0; c < _mapData.MapWidth; c++)
            {
                int idx = r * _mapData.MapWidth + c;
                byte after = (byte)_mapData.GetBelongValue(c, r);
                if (beforeSnapshot[idx] != after)
                    changes.Add((c, r, beforeSnapshot[idx], after));
            }
        }

        if (changes.Count > 0)
        {
            // 如果变更数量超过阈值，使用完整快照模式（更省内存）
            const int FullSnapshotThreshold = 50000;
            IUndoableCommand command;
            if (changes.Count > FullSnapshotThreshold)
            {
                command = new BelongFullSnapshotCommand(_mapData, description, beforeSnapshot);
            }
            else
            {
                command = new BelongChangeCommand(_mapData, description, changes.ToArray());
            }
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

    /// <summary>
    /// 记录单格归属变更的撤销命令。
    /// <para>
    /// 归属编辑模式的单格操作（右键设置归属、粘贴、删除、选择军团）必须使用本方法：
    /// <see cref="RecordProvinceChange"/> 比较的是 Province，归属值变化不会产生差异，
    /// 因此用它记录会导致归属修改无法撤销。
    /// </para>
    /// </summary>
    public void RecordBelongChange(int col, int row, string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        byte before = (byte)_mapData.GetBelongValue(col, row);
        applyChange();
        byte after = (byte)_mapData.GetBelongValue(col, row);

        if (before == after) return;

        _undoManager.Record(new BelongChangeCommand(_mapData, description,
            new[] { (col, row, before, after) }));
    }

    /// <summary>
    /// 记录多格归属变更的撤销命令，供归属编辑模式的批量操作使用
    /// （清理所有归属、随机化归属、批量更改归属）。
    /// </summary>
    public void RecordMultiCellBelongChange(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var beforeSnapshot = new byte[_mapData.MapWidth * _mapData.MapHeight];
        for (int i = 0; i < beforeSnapshot.Length; i++)
            beforeSnapshot[i] = (byte)_mapData.GetBelongValueByIndex(i);

        applyChange();

        RecordBelongChangesFromSnapshot(description, beforeSnapshot);
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
            EditMode.TerrainPaint, EditMode.ProvinceEdit, EditMode.BuildingDeploy,
            EditMode.ArmyDeploy, EditMode.BelongEdit, EditMode.LegionEdit,
            EditMode.ReinforcementDeploy
        };
        _sceneModeMap["conquest"] = new List<EditMode>
        {
            EditMode.ProvinceEdit, EditMode.BuildingDeploy, EditMode.ArmyDeploy,
            EditMode.BelongEdit, EditMode.LegionEdit,
            EditMode.ReinforcementDeploy
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
            NotifyBrushSizeChanged = () => BrushSizeChanged?.Invoke(this, EventArgs.Empty),
            NotifyToggleDomain = () => DomainToggled?.Invoke(this, EventArgs.Empty),
            NotifyToggleBuildingNames = () => BuildingNamesToggled?.Invoke(this, EventArgs.Empty),
            NotifyCaptureLegionScreenshot = () => CaptureLegionScreenshotRequested?.Invoke(this, EventArgs.Empty),
            NotifyOpenLegionSetting = () => OpenLegionSettingRequested?.Invoke(this, EventArgs.Empty),
            NotifyOpenLegionList = () => OpenLegionListRequested?.Invoke(this, EventArgs.Empty),
            NotifyOpenHeaderSetting = () => OpenHeaderSettingRequested?.Invoke(this, EventArgs.Empty),
            NotifyUpdateConquerSettings = () => UpdateConquerSettingsRequested?.Invoke(this, EventArgs.Empty),
            NotifyMoveCameraToHex = (col, row) => MoveCameraToHexRequested?.Invoke(col, row),
            NotifyToggleBuildingMoveTool = () => BuildingMoveToolToggled?.Invoke(this, EventArgs.Empty),
            NotifyRecognizeTextBuildings = () => RecognizeTextBuildingsRequested?.Invoke(this, EventArgs.Empty),
            DialogService = _dialogService,
            CliCommandExecutor = _cliCommandExecutor,
            RecognizeTerrainCallback = _recognizeTerrainCallback,
            GeoCalculateCallback = _geoCalculateCallback,
            GeoExportRefCallback = _geoExportRefCallback,
            GeoImportRefCallback = _geoImportRefCallback,
            GeoExportGridCallback = _geoExportGridCallback,
            AddGeoRefCallback = _addGeoRefCallback
        };
    }

    public void RaiseStatusMessage(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    #endregion
}