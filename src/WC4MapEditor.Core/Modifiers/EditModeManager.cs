using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
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
    BelongEdit = 5,
    LegionEdit = 6,
    /// <summary>事件编辑（对应 VB 的 EventEdit 模式）</summary>
    EventEdit = 7,
    ReinforcementDeploy = 8,
    /// <summary>空袭 / 空中支援编辑（VB AirForce）</summary>
    AirForceEdit = 9,
    /// <summary>方案（方针）编辑（VB MapCase）</summary>
    MapCaseEdit = 10,
    /// <summary>战略建设编辑（VB StrategicConstruction）</summary>
    StrategicEdit = 11,
    /// <summary>天气编辑（VB Weather + WeatherList）</summary>
    WeatherEdit = 12
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
    Reinforcement = 7,
    /// <summary>首都编辑模块 - 独立模块，由需要它的编辑模式通过 ModifierKinds 引入</summary>
    Capital = 8,
    /// <summary>v3 格式（BtlVersion ≥ 3）的单位修改器：与 Army 并存，由模式按版本选用</summary>
    ArmyV3 = 9,
    /// <summary>v3 格式的援军修改器</summary>
    ReinforcementV3 = 10,
    /// <summary>地图事件</summary>
    Event = 11,
    /// <summary>天气序列</summary>
    Weather = 12,
    /// <summary>空袭 / 空中支援</summary>
    AirForce = 13,
    /// <summary>方案（AI 方针）</summary>
    MapCase = 14,
    /// <summary>战略建设</summary>
    StrategicConstruction = 15
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
    private Func<MapScreenshotRequest, MapScreenshotResult>? _screenshotCallback;
    private Func<MapScreenshotRequest, MapScreenshotResult>? _sceneScreenshotCallback;
    private Func<(int col, int row)>? _getFocusHexCallback;
    private string _currentSceneType = "stage";

    public event EventHandler<EditModeChangedEventArgs>? ModeChanged;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? DataModified;
    public event EventHandler? BrushToggled;
    public event EventHandler? BrushSizeChanged;

    /// <summary>手绘边界状态变化（省份编辑模式 U 键）</summary>
    public event EventHandler? BoundaryDrawToggled;
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

    /// <summary>军团范围截图被请求（军团编辑模式 F4 键；P 键已让给"设为玩家军团"）</summary>
    public event EventHandler? CaptureLegionScreenshotRequested;

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
        RegisterModifier(ModifierKind.Capital, new CapitalModifier());
        // v3 修改器以前从未注册：模式里 GetModifier<ArmyV3Modifier>() 一直拿到 null，
        // v3 地图上放单位 / 援军直接空引用。注册后与 v1 实例一样随 Initialize 一起就绪。
        RegisterModifier(ModifierKind.ArmyV3, new ArmyV3Modifier());
        RegisterModifier(ModifierKind.ReinforcementV3, new ReinforcementV3Modifier());
        // 从 VB 版移植时预先写好的模块：由事件 / 天气 / 空袭 / 方案 / 战略建设模式引入
        RegisterModifier(ModifierKind.Event, new EventModifier());
        RegisterModifier(ModifierKind.Weather, new WeatherModifier());
        RegisterModifier(ModifierKind.AirForce, new AirForceModifier());
        RegisterModifier(ModifierKind.MapCase, new MapCaseModifier());
        RegisterModifier(ModifierKind.StrategicConstruction, new StrategicConstructionModifier());

        RegisterModeHandler(new TerrainPaintMode());
        RegisterModeHandler(new BelongEditMode());
        RegisterModeHandler(new LegionEditMode());
        RegisterModeHandler(new ProvinceEditMode());
        RegisterModeHandler(new BuildingDeployMode());
        RegisterModeHandler(new ArmyDeployMode());

        RegisterModeHandler(new ReinforcementDeployMode());
        RegisterModeHandler(new EventEditMode());
        RegisterModeHandler(new AirForceEditMode());
        RegisterModeHandler(new MapCaseEditMode());
        RegisterModeHandler(new StrategicConstructionEditMode());
        RegisterModeHandler(new WeatherEditMode());

        BuildSceneModeMap();
        RegisterNumberKeyBindings();
    }

    #region 公共属性

    public EditMode CurrentMode => _currentMode;

    /// <summary>当前模式的处理器（浏览模式下为 null）。</summary>
    public IModeHandler? CurrentHandler => GetCurrentHandler();
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

    private IViewLayerImageProvider? _viewLayerImageProvider;

    /// <summary>
    /// 注入背景图（视图层图片）的像素访问入口，由 GUI 层在初始化时调用。
    /// 省份编辑模式的"从背景图识别省区"（P 键）需要它。
    /// </summary>
    public void SetViewLayerImageProvider(IViewLayerImageProvider provider)
    {
        _viewLayerImageProvider = provider;
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

    /// <summary>
    /// 注册地图截图实现（由 GUI 场景基类提供；渲染器在 Rendering 程序集，Core 拿不到）。
    /// 注册后 Lua 的 map.CaptureScreenshot、CLI 的 screenshot 命令、AI 工具都走它。
    /// </summary>
    public void SetScreenshotCallback(Func<MapScreenshotRequest, MapScreenshotResult> callback)
    {
        _screenshotCallback = callback;
    }

    /// <summary>
    /// 注册"整个场景（窗口界面）截图"实现 —— 由主窗口注册。
    /// <para>
    /// 与 <see cref="SetScreenshotCallback"/> 的区别：那个截的是<b>地图数据</b>，必须有个渲染场景活着；
    /// 这个截的是<b>编辑器窗口界面</b>，而主窗口一直都在，所以跟"当前是哪个场景、有没有加载地图"
    /// 全部无关 —— 军团 / 国家 / 建筑等编辑场景、甚至开始界面都能截。
    /// </para>
    /// </summary>
    public void SetSceneScreenshotCallback(Func<MapScreenshotRequest, MapScreenshotResult> callback)
    {
        _sceneScreenshotCallback = callback;
    }

    /// <summary>
    /// 截图。供 Lua / CLI / AI 统一调用；未注册（如无界面启动）时返回失败而不是抛异常。
    /// <para>
    /// 按 <see cref="MapScreenshotRequest.SceneMode"/> 分发：场景模式走主窗口那份实现，
    /// 否则走渲染场景那份（地图）。这样调用方不用关心"现在到底有没有地图"。
    /// </para>
    /// <para>调用方需保证在 UI 线程上执行（实现方内部也会再兜一次）。</para>
    /// </summary>
    public MapScreenshotResult CaptureScreenshot(MapScreenshotRequest request)
    {
        var impl = request.SceneMode ? _sceneScreenshotCallback : _screenshotCallback;
        if (impl == null)
            return MapScreenshotResult.Fail(request.SceneMode
                ? "场景截图能力未注册（需要 GUI 主窗口；无界面启动时不可用）"
                : "截图能力未注册（需要 GUI 场景）");

        try
        {
            return impl(request);
        }
        catch (Exception ex)
        {
            return MapScreenshotResult.Fail($"截图失败: {ex.Message}");
        }
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

    /// <summary>
    /// 按模式取处理器实例。
    /// <para>
    /// 给 GUI 用：模式自身也可能持有编辑状态（例如省份模式的手绘边界掩码），
    /// 这些状态不属于任何 Modifier，只能从模式实例上取。
    /// </para>
    /// </summary>
    public IModeHandler? GetModeHandler(EditMode mode)
    {
        return _modeHandlers.TryGetValue(mode, out var handler) ? handler : null;
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
                RaiseStatusMessage(Loc.F("Mode.Status.SwitchedTo", handler.DisplayName));
                // 完整帮助显示在地图左上角，状态消息只带标题行
                RaiseStatusMessage(FirstLine(handler.HelpText));
            }
            else
            {
                RaiseStatusMessage(Loc.T("Mode.Browse", "浏览模式"));
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
        if (_mapData == null || _modeContext == null) return;
        // 获取当前焦点坐标（由渲染层提供）
        var (col, row) = _getFocusHexCallback?.Invoke() ?? (0, 0);
        _ = RunModeActionAsync(handler, action, col, row, _modeContext);
    }

    /// <summary>
    /// 执行按键动作并兜住异常：按键回调是"发出去不等"的，异常若不在这里接住就会被静默吞掉，
    /// 用户只看到按键没反应。这里记入错误收集器并在状态栏说明是哪个动作失败。
    /// </summary>
    private async Task RunModeActionAsync(IModeHandler handler, string action, int col, int row, ModeContext context)
    {
        try
        {
            await handler.HandleKeyAction(action, col, row, context);
        }
        catch (Exception ex)
        {
            ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                $"模式动作失败: {handler.Mode}/{action}", nameof(DispatchModeAction), ex);
            RaiseStatusMessage(Loc.F("Mode.Status.ActionFailed", handler.DisplayName, action, ex.Message));
        }
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

    /// <summary>
    /// 数字键 → 模式（主键盘与小键盘都可用）。当前场景不提供的模式按了只给提示。
    /// 顺序沿用 C# 版的编号：1 地形、2 省份、3 建筑、4 单位、5 归属、6 军团、7 事件、8 援军、9 空袭、0 方案；
    /// 战略建设与天气没有数字键，用 Space 循环切换到。
    /// </summary>
    private static readonly (int Digit, EditMode Mode)[] NumberKeyModes =
    {
        (1, EditMode.TerrainPaint),
        (2, EditMode.ProvinceEdit),
        (3, EditMode.BuildingDeploy),
        (4, EditMode.ArmyDeploy),
        (5, EditMode.BelongEdit),
        (6, EditMode.LegionEdit),
        (7, EditMode.EventEdit),
        (8, EditMode.ReinforcementDeploy),
        (9, EditMode.AirForceEdit),
        (0, EditMode.MapCaseEdit),
    };

    private void RegisterNumberKeyBindings()
    {
        var keyboard = KeyboardManager.Instance;

        foreach (var (digit, mode) in NumberKeyModes)
        {
            var target = mode;
            void Switch()
            {
                if (GetAvailableModes(_currentSceneType).Contains(target))
                    SwitchMode(target);
                else
                    RaiseStatusMessage(Loc.F("Mode.Status.NotInScene", GetModeHandler(target)?.DisplayName ?? target.ToString()));
            }

            string description = Loc.F("Mode.SwitchTo", GetModeHandler(mode)?.DisplayName ?? mode.ToString());
            keyboard.RegisterBinding($"MODE_SWITCH_{mode}", KeyCodes.D0 + digit, KeyModifiers.None, Switch, description);
            keyboard.RegisterBinding($"MODE_SWITCH_NUM_{mode}", KeyCodes.NumPad0 + digit, KeyModifiers.None, Switch, description);
        }
    }

    /// <summary>帮助里的数字键一览，例如 "1-地形绘制 2-省份编辑 …"，只列当前场景提供的模式。</summary>
    private string BuildNumberKeyHelpLine()
    {
        var available = GetAvailableModes(_currentSceneType);
        var parts = new List<string>();
        foreach (var (digit, mode) in NumberKeyModes)
        {
            if (!available.Contains(mode)) continue;
            var h = GetModeHandler(mode);
            if (h != null) parts.Add($"{digit}-{h.DisplayName}");
        }
        return string.Join(" ", parts);
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
        if (handler == null) return Loc.T("Mode.Help.NoMode", "未选择编辑模式，按 Space 切换模式");
        return handler.HelpText;
    }

    public string GetModeStatusText()
    {
        var handler = GetCurrentHandler();
        if (handler == null) return Loc.T("Mode.Browse", "浏览模式");
        return handler.DisplayName;
    }

    public string GetFullHelpText()
    {
        var handler = GetCurrentHandler();
        if (handler == null)
            return Loc.T("Mode.Browse", "浏览模式") + "\n" + Loc.T("Mode.Help.Footer.Space", "Space - 切换模式");

        // 统一用 '\n'：叠加层按 '\n' 分行，AppendLine 在 Windows 上写的是 "\r\n"，多出的 '\r' 会画成方框
        var sb = new System.Text.StringBuilder();
        sb.Append(handler.DisplayName).Append('\n');
        sb.Append(handler.HelpText.Replace("\r\n", "\n")).Append('\n');
        sb.Append(Loc.T("Mode.Help.Footer.Space", "Space - 切换模式")).Append('\n');
        string digits = BuildNumberKeyHelpLine();
        if (digits.Length > 0) sb.Append(digits).Append('\n');
        sb.Append(Loc.T("Mode.Help.Footer.Esc", "Esc - 返回主界面"));
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

    #region 编辑窗口请求

    private readonly Dictionary<string, Func<EditorRequest, bool>> _editors = new(StringComparer.Ordinal);

    /// <summary>
    /// 通用编辑窗口宿主（GUI 注入）：按 <see cref="EditorRequest.Kind"/> 找到对应窗口并打开 / 激活。
    /// 用 <see cref="RegisterEditor"/> 单独注册的种类优先。
    /// </summary>
    public Func<EditorRequest, bool>? EditorHost { get; set; }

    /// <summary>为某种窗口单独注册打开方式（同名覆盖）。</summary>
    public void RegisterEditor(string kind, Func<EditorRequest, bool> opener) => _editors[kind] = opener;

    public void UnregisterEditor(string kind) => _editors.Remove(kind);

    /// <summary>
    /// 请求打开编辑窗口。没有任何宿主能处理时（命令行、无界面启动、窗口尚未实现）
    /// 返回 false 并在状态栏说明，而不是静默无反应。
    /// </summary>
    public bool OpenEditor(EditorRequest request)
    {
        try
        {
            if (_editors.TryGetValue(request.Kind, out var opener) && opener(request)) return true;
            if (EditorHost != null && EditorHost(request)) return true;
        }
        catch (Exception ex)
        {
            ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                $"打开编辑窗口失败: {request.Kind}", nameof(OpenEditor), ex);
            RaiseStatusMessage(Loc.F("Mode.Status.EditorFailed", request.Kind, ex.Message));
            return false;
        }

        RaiseStatusMessage(Loc.F("Mode.Status.EditorUnavailable", request.Kind));
        return false;
    }

    #endregion

    #region 撤销/重做辅助

    /// <summary>
    /// 记录一次实体列表修改（事件 / 天气 / 方案 / 空袭 / 空中支援 / 战略建设 / 援军 / 军团……）的撤销：
    /// 执行前后各拍一份列表快照，撤销 / 重做时整体还原。列表没有变化时不记录。
    /// <para>
    /// 元素是结构体时快照是值拷贝；若结构体里有数组等引用类型字段且会被就地修改，
    /// 传入 <paramref name="clone"/> 做深拷贝，否则快照会跟着被改掉。
    /// </para>
    /// </summary>
    public void RecordCollectionChange<T>(string description, IList<T> list, Action applyChange, Func<T, T>? clone = null)
    {
        if (_undoManager == null)
        {
            applyChange();
            return;
        }

        var before = SnapshotList(list, clone);
        applyChange();
        var after = SnapshotList(list, clone);

        if (before.Length == after.Length)
        {
            var cmp = EqualityComparer<T>.Default;
            bool same = true;
            for (int i = 0; i < before.Length && same; i++)
                same = cmp.Equals(before[i], after[i]);
            if (same) return;
        }

        _undoManager.Record(new DelegateCommand(description,
            () => RestoreList(list, after, clone),
            () => RestoreList(list, before, clone)));
    }

    private static T[] SnapshotList<T>(IList<T> list, Func<T, T>? clone)
    {
        var copy = new T[list.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = clone != null ? clone(list[i]) : list[i];
        return copy;
    }

    private void RestoreList<T>(IList<T> list, T[] snapshot, Func<T, T>? clone)
    {
        list.Clear();
        foreach (var item in snapshot)
            list.Add(clone != null ? clone(item) : item);
        if (_mapData != null) _mapData.IsModified = true;
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

    /// <summary>
    /// 记录"建筑相关变更"的撤销：快照 建筑列表 + 省份 + 归属，执行后按前后状态记录。
    /// <para>
    /// 为什么需要单独一个：<see cref="RecordMultiCellChange"/> 只快照地形，
    /// <see cref="RecordMultiCellProvinceChange"/> 只快照省份，而建筑类的
    /// 批量操作（随机化类型、批量生成建筑、为所有建筑生成省会等）会同时
    /// 改到 Buildings / Provinces / Belongs 三者，用上面任一个都会漏。
    /// </para>
    /// </summary>
    public void RecordBuildingsSnapshot(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var before = CaptureEntitySnapshot();
        applyChange();
        var after = CaptureEntitySnapshot();

        _undoManager.Record(new DelegateCommand(description,
            () => RestoreEntitySnapshot(after),
            () => RestoreEntitySnapshot(before)));
    }

    /// <summary>
    /// 记录"任意实体变更"的撤销：快照全部实体列表（建筑、单位 v1/v3、军团、陷阱、援军 v1/v3、首都、方案、天气、事件、
    /// 空袭、单位位置、战略建设、空中支援）+ 省份 + 归属。
    /// <para>给范围不确定的批量操作用（命令行、脚本、单位橡皮擦）：比 <see cref="RecordBuildingsSnapshot"/> 覆盖面全，代价是内存稍大。</para>
    /// </summary>
    public void RecordEntitiesSnapshot(string description, Action applyChange)
    {
        if (_mapData == null || _undoManager == null)
        {
            applyChange();
            return;
        }

        var before = MapEntitiesSnapshot.Capture(_mapData);
        applyChange();
        var after = MapEntitiesSnapshot.Capture(_mapData);
        if (before.SameAs(after)) return;

        var map = _mapData;
        _undoManager.Record(new DelegateCommand(description, () => after.Restore(map), () => before.Restore(map)));
    }

    /// <summary>建筑相关变更的完整快照（建筑列表 + 省份 + 归属）</summary>
    private readonly record struct EntitySnapshot(Building[] Buildings, Province[] Provinces, byte[] Belongs);

    private EntitySnapshot CaptureEntitySnapshot()
    {
        int total = _mapData!.MapWidth * _mapData.MapHeight;

        var provinces = new Province[total];
        for (int i = 0; i < total; i++)
            provinces[i] = _mapData.GetProvinceRef(i);

        var belongs = new byte[total];
        for (int i = 0; i < total; i++)
            belongs[i] = (byte)_mapData.GetBelongValueByIndex(i);

        return new EntitySnapshot(_mapData.Buildings.ToArray(), provinces, belongs);
    }

    private void RestoreEntitySnapshot(EntitySnapshot snapshot)
    {
        if (_mapData == null) return;

        _mapData.Buildings.Clear();
        foreach (var building in snapshot.Buildings)
            _mapData.Buildings.Add(building);

        int total = _mapData.MapWidth * _mapData.MapHeight;
        for (int i = 0; i < total && i < snapshot.Provinces.Length; i++)
            _mapData.GetProvinceRef(i) = snapshot.Provinces[i];
        for (int i = 0; i < total && i < snapshot.Belongs.Length; i++)
            _mapData.SetBelongValueByIndex(i, snapshot.Belongs[i]);

        _mapData.IsModified = true;
    }

    private TerrainData[]? _brushStrokeSnapshot;
    private Province[]? _provinceBrushStrokeSnapshot;
    private byte[]? _belongBrushStrokeSnapshot;

    /// <summary>实体画笔（单位部署模式的橡皮擦）一笔开始时的实体快照</summary>
    private MapEntitiesSnapshot? _entityBrushStrokeSnapshot;

    public IBrushTarget? GetActiveBrushTarget()
    {
        return _currentMode switch
        {
            EditMode.TerrainPaint => GetModifier<TerrainModifier>() as IBrushTarget,
            EditMode.ProvinceEdit => GetModifier<ProvinceModifier>() as IBrushTarget,
            EditMode.BelongEdit => GetModifier<BelongModifier>() as IBrushTarget,
            EditMode.ArmyDeploy => GetModifier<ArmyModifier>() as IBrushTarget,
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

        _entityBrushStrokeSnapshot = null;
        if (_currentMode == EditMode.ArmyDeploy)
        {
            // 单位部署模式的画笔是橡皮擦：删的是单位，快照地形撤不回来
            _entityBrushStrokeSnapshot = MapEntitiesSnapshot.Capture(_mapData);
            _brushStrokeSnapshot = null;
            _provinceBrushStrokeSnapshot = null;
            _belongBrushStrokeSnapshot = null;
        }
        else if (_currentMode == EditMode.ProvinceEdit)
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
        if (_entityBrushStrokeSnapshot != null)
        {
            var before = _entityBrushStrokeSnapshot;
            _entityBrushStrokeSnapshot = null;
            if (_mapData == null || _undoManager == null) return;
            var after = MapEntitiesSnapshot.Capture(_mapData);
            if (before.SameAs(after)) return;
            var map = _mapData;
            _undoManager.Record(new DelegateCommand(description, () => after.Restore(map), () => before.Restore(map)));
            return;
        }

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
        // 顺序 = Space 循环顺序（对齐 VB CycleEditMode：…援军→事件→空袭→方案→战略建设→天气）
        _sceneModeMap["stage"] = new List<EditMode>
        {
            EditMode.TerrainPaint, EditMode.ProvinceEdit, EditMode.BuildingDeploy,
            EditMode.ArmyDeploy, EditMode.BelongEdit, EditMode.LegionEdit,
            EditMode.ReinforcementDeploy, EditMode.EventEdit, EditMode.AirForceEdit,
            EditMode.MapCaseEdit, EditMode.StrategicEdit, EditMode.WeatherEdit
        };
        _sceneModeMap["conquest"] = new List<EditMode>
        {
            EditMode.ProvinceEdit, EditMode.BuildingDeploy, EditMode.ArmyDeploy,
            EditMode.BelongEdit, EditMode.LegionEdit,
            EditMode.ReinforcementDeploy, EditMode.EventEdit, EditMode.AirForceEdit,
            EditMode.MapCaseEdit, EditMode.StrategicEdit, EditMode.WeatherEdit
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
            Owner = this,
            MapData = _mapData,
            UndoManager = _undoManager,
            RaiseStatusMessage = RaiseStatusMessage,
            NotifyDataModified = () => DataModified?.Invoke(this, EventArgs.Empty),
            NotifyBrushToggled = () => BrushToggled?.Invoke(this, EventArgs.Empty),
            NotifyBrushSizeChanged = () => BrushSizeChanged?.Invoke(this, EventArgs.Empty),
            NotifyBoundaryDrawToggled = () => BoundaryDrawToggled?.Invoke(this, EventArgs.Empty),
            NotifyToggleDomain = () => DomainToggled?.Invoke(this, EventArgs.Empty),
            NotifyToggleBuildingNames = () => BuildingNamesToggled?.Invoke(this, EventArgs.Empty),
            NotifyCaptureLegionScreenshot = () => CaptureLegionScreenshotRequested?.Invoke(this, EventArgs.Empty),
            NotifyMoveCameraToHex = (col, row) => MoveCameraToHexRequested?.Invoke(col, row),
            NotifyToggleBuildingMoveTool = () => BuildingMoveToolToggled?.Invoke(this, EventArgs.Empty),
            NotifyRecognizeTextBuildings = () => RecognizeTextBuildingsRequested?.Invoke(this, EventArgs.Empty),
            DialogService = _dialogService,
            ViewLayerImage = _viewLayerImageProvider,
            CliCommandExecutor = _cliCommandExecutor,
            RecognizeTerrainCallback = _recognizeTerrainCallback,
            GeoCalculateCallback = _geoCalculateCallback,
            GeoExportRefCallback = _geoExportRefCallback,
            GeoImportRefCallback = _geoImportRefCallback,
            GeoExportGridCallback = _geoExportGridCallback,
            AddGeoRefCallback = _addGeoRefCallback
        };
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }

    public void RaiseStatusMessage(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    #endregion
}