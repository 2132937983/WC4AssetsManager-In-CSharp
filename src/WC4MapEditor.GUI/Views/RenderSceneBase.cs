using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Commands;
// 与 WPF 的 System.Windows.Input.CommandManager 同名，使用别名消除歧义
using CoreCommandManager = WC4MapEditor.Core.Commands.CommandManager;
using WC4MapEditor.Core.ErrorHandling;
using WC4MapEditor.Core.Geo;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Rendering.Helpers;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;
using WC4MapEditor.Scripting;
using SkiaSharp.Views.Desktop;

namespace WC4MapEditor.GUI.Views;

public abstract partial class RenderSceneBase : UserControl, IDisposable
{
    protected readonly MainWindow Window;
    private MainRender _renderEngine = null!;
    private SKElement _skElement = null!;
    private Grid _rootGrid = null!;
    private MapData? _mapData;
    private Camera _camera = null!;
    private bool _disposed;
    private bool _confirmDialogShowing;
    private HexInfoWindow? _hexInfoWindow;
    private BrushSettingsWindow? _brushSettingsWindow;
    private bool _brushDragging;
    private MousePosition _brushLastDragPos;
    private (int col, int row) _brushLastPaintedCell = (-1, -1);
    private const double BrushMinDragDistance = 5.0;
    private const double BrushInterpolationStep = 4.0;
    private int _sceneId = -1;

    internal void AssignSceneId(int sceneId)
    {
        _sceneId = sceneId;
    }

    private StackPanel? _sceneTabPanel;
    private Popup? _sceneTabPopup;
    private TextBlock? _sceneNameLabel;
    private TextBlock? _layerInfoLabel;

    private readonly MouseManager _mouseManager;
    private readonly KeyboardManager _keyboardManager;
    private readonly DebugConsole _debugConsole;
    private readonly EditModeManager _editModeManager;
    private readonly HexSelector _hexSelector;
    private readonly CoreCommandManager _commandManager;
    private readonly FileStateManager _fileStateManager = new();
    private Grid? _titleBar;

    protected MainRender RenderEngine => _renderEngine;

    protected abstract string SceneTitle { get; }
    protected abstract string SceneType { get; }
    protected abstract MapData? LoadMapData();
    protected abstract bool SaveMapData(MapData mapData, string outputPath);
    protected abstract MapData? ReloadMapData(string filePath);
    protected abstract void InitializeRenderers();

    protected static MapData? LoadMapDataByFileType(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".bin" or ".dat" => WorldParser.LoadFromFile(filePath),
            ".btl" => LoadBtlByHeader(filePath),
            _ => null
        };
    }

    protected RenderSceneBase(MainWindow window)
    {
        Window = window;
        _mouseManager = window.MouseManager;
        _debugConsole = window.DebugConsole;
        _keyboardManager = window.KeyboardManager;
        _editModeManager = window.EditModeManager;
        _hexSelector = window.HexSelector;
        _commandManager = window.CommandManager;
        Focusable = true;
        SetupUI();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void SetupUI()
    {
        _rootGrid = new Grid();
        _rootGrid.Background = Brushes.Black;

        _skElement = new SKElement
        {
            IgnorePixelScaling = true,
            Focusable = true
        };
        _skElement.PaintSurface += OnPaintSurface;
        _rootGrid.Children.Add(_skElement);

        _titleBar = new Grid
        {
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var titleBar = _titleBar;

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var backButton = CreateToolBarButton("←");
        backButton.Click += BackButton_Click;
        titlePanel.Children.Add(backButton);

        titlePanel.Children.Add(CreateSeparator());

        var undoButton = CreateToolBarButton(Loc.T("Common.Undo"));
        undoButton.Click += UndoButton_Click;
        titlePanel.Children.Add(undoButton);

        var redoButton = CreateToolBarButton(Loc.T("Common.Redo"));
        redoButton.Click += RedoButton_Click;
        titlePanel.Children.Add(redoButton);
        AttachUndoRedoButtons(undoButton, redoButton);

        titlePanel.Children.Add(CreateSeparator());

        var openButton = CreateToolBarButton(Loc.T("Common.Open"));
        openButton.Click += OpenButton_Click;
        titlePanel.Children.Add(openButton);

        var saveButton = CreateToolBarButton(Loc.T("Common.Save"));
        saveButton.Click += SaveButton_Click;
        titlePanel.Children.Add(saveButton);

        var newButton = CreateToolBarButton(Loc.T("Common.New"));
        newButton.Click += NewButton_Click;
        titlePanel.Children.Add(newButton);

        titlePanel.Children.Add(CreateSeparator());

        var viewButton = CreateToolBarButton(Loc.T("Render.Toolbar.ViewLayer"));
        viewButton.Click += ViewLayerButton_Click;
        titlePanel.Children.Add(viewButton);

        titlePanel.Children.Add(CreateSeparator());

        var sceneTabButton = CreateToolBarButton(Loc.T("Render.Toolbar.Scenes"));
        sceneTabButton.Click += SceneTabButton_Click;
        titlePanel.Children.Add(sceneTabButton);

        titlePanel.Children.Add(CreateSeparator());

        _sceneNameLabel = new TextBlock
        {
            Text = SceneTitle,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black }
        };
        titlePanel.Children.Add(_sceneNameLabel);

        _layerInfoLabel = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 150)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        titlePanel.Children.Add(_layerInfoLabel);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var minimizeButton = CreateToolBarButton("➖", 28);
        minimizeButton.Click += (s, e) => Window.MinimizeWindow();
        rightPanel.Children.Add(minimizeButton);

        var maximizeButton = CreateToolBarButton("⬜", 28);
        maximizeButton.Click += (s, e) => Window.ToggleMaximize();
        rightPanel.Children.Add(maximizeButton);

        var closeButton = CreateToolBarButton("✕", 28);
        closeButton.Foreground = Brushes.Red;
        closeButton.Click += CloseButton_Click;
        rightPanel.Children.Add(closeButton);

        titleBar.Children.Add(titlePanel);
        titleBar.Children.Add(rightPanel);
        BuildSceneStatusArea(titleBar, titlePanel, rightPanel);
        _rootGrid.Children.Add(titleBar);

        // 菜单栏空白区域拖动窗口
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.Source == titleBar || e.Source == titlePanel)
            {
                Window.DragMove();
            }
        };

        _sceneTabPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromArgb(230, 45, 45, 48)),
        };

        _sceneTabPopup = new Popup
        {
            Child = _sceneTabPanel,
            StaysOpen = false,
            Placement = PlacementMode.Top,
            PlacementTarget = titleBar,
            PopupAnimation = PopupAnimation.Fade,
            AllowsTransparency = true,
        };

        Content = _rootGrid;
    }

    private Button CreateToolBarButton(string text, double width = 0)
    {
        var btn = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            FontSize = 13,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(2, 0, 2, 0),
            // 不抢键盘焦点：否则点过按钮后空格 / Enter 会再次点击它，而不是交给地图（切换模式等）
            Focusable = false
        };

        if (width > 0)
            btn.Width = width;

        btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        btn.MouseLeave += (s, e) => btn.Background = Brushes.Transparent;
        return btn;
    }

    private static Separator CreateSeparator() => new()
    {
        Width = 1,
        Height = 20,
        Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
        Margin = new Thickness(6, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var swTotal = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();
        Debug.WriteLine("[Timing] ===== OnLoaded 开始 =====");

        _renderEngine = new MainRender(RenderEngineFactory.Create());
        Debug.WriteLine($"[Timing] 创建渲染引擎: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        _mapData = LoadInitialMapData();
        Debug.WriteLine($"[Timing] LoadMapData: {sw.ElapsedMilliseconds}ms"); sw.Restart();
        if (_mapData == null)
        {
            CloseAllAssistWindows();
            Window.ReturnToMainScene();
            return;
        }

        _camera = new Camera
        {
            MapWidth = _mapData.MapWidth,
            MapHeight = _mapData.MapHeight,
            ViewportWidth = (int)_skElement.ActualWidth,
            ViewportHeight = (int)_skElement.ActualHeight
        };
        _camera.CenterOnMap();
        Debug.WriteLine($"[Timing] Camera: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        InitializeRenderers();
        Debug.WriteLine($"[Timing] InitializeRenderers(场景开关): {sw.ElapsedMilliseconds}ms"); sw.Restart();

        _renderEngine.Initialize(IntPtr.Zero, (int)_skElement.ActualWidth, (int)_skElement.ActualHeight);
        _renderEngine.Resize((int)_skElement.ActualWidth, (int)_skElement.ActualHeight);

        _renderEngine.InitializeTacticalMapImageCache();
        Debug.WriteLine($"[Timing] InitializeTacticalMapImageCache: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        _renderEngine.PreloadBelongFlagAtlas(_mapData);
        Debug.WriteLine($"[Timing] PreloadBelongFlagAtlas: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        _skElement.MouseLeftButtonDown += OnWpfMouseLeftDown;
        _skElement.MouseLeftButtonUp += OnWpfMouseLeftUp;
        _skElement.MouseRightButtonDown += OnWpfMouseRightDown;
        _skElement.MouseRightButtonUp += OnWpfMouseRightUp;
        _skElement.MouseMove += OnWpfMouseMove;
        _skElement.MouseWheel += OnWpfMouseWheel;
        _skElement.MouseLeave += OnWpfMouseLeave;
        _skElement.SizeChanged += OnSizeChanged;
        _skElement.KeyDown += OnSkElementKeyDown;
        _skElement.KeyUp += OnSkElementKeyUp;
        _skElement.TextInput += OnSkElementTextInput;

        _mouseManager.MouseAction += OnMouseAction;
        _hexSelector.SelectionChanged += OnHexSelectionChanged;
        _hexSelector.SelectionRectChanged += OnSelectionRectChanged;

        _keyboardManager.RegisterBinding("EscBack", (int)Key.Escape, KeyModifiers.None, OnEscPressed, Loc.T("Render.Key.EscBack"));
        _keyboardManager.RegisterBinding("ToggleConsole", (int)Key.F3, KeyModifiers.None, OnToggleConsole, Loc.T("Render.Key.ToggleConsole"));
        _keyboardManager.RegisterBinding("Screenshot", (int)Key.F2, KeyModifiers.None, OnScreenshot, Loc.T("Render.Key.Screenshot"));
        _keyboardManager.RegisterBinding("QuickSave", (int)Key.S, KeyModifiers.Ctrl, OnQuickSave, Loc.T("Render.Key.QuickSave"));

        InitializeEditModeKeyBindings();

        _editModeManager.Initialize(_mapData);
        _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
        _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetViewLayerImageProvider(_renderEngine.ViewLayerImageProvider);
        _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
        _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
        _editModeManager.SetGeoCalculateCallback(OnGeoCalculateAsync);
        _editModeManager.SetGeoExportRefCallback(OnGeoExportRefAsync);
        _editModeManager.SetGeoImportRefCallback(OnGeoImportRefAsync);
        _editModeManager.SetGeoExportGridCallback(OnGeoExportGridAsync);
        _editModeManager.SetAddGeoRefCallback(OnAddGeoRefAsync);
        _editModeManager.SetGetFocusHexCallback(GetFocusHex);
        // 截图：Lua 的 map.CaptureScreenshot、CLI 的 screenshot 命令、AI 视觉都走这里
        _editModeManager.SetScreenshotCallback(CaptureMapScreenshot);
        _editModeManager.ModeChanged += OnEditModeChanged;
        _editModeManager.StatusMessageChanged += OnEditModeStatusMessageChanged;
        _editModeManager.DataModified += OnEditModeDataModified;
        _editModeManager.BrushToggled += OnBrushToggled;
        _editModeManager.BrushSizeChanged += OnBrushSizeChanged;
        _editModeManager.BoundaryDrawToggled += OnBoundaryDrawToggled;
        _editModeManager.DomainToggled += OnDomainToggled;
        _editModeManager.BuildingNamesToggled += OnBuildingNamesToggled;
        _editModeManager.CaptureLegionScreenshotRequested += OnCaptureLegionScreenshotRequested;
        _editModeManager.MoveCameraToHexRequested += OnMoveCameraToHexRequested;
        _editModeManager.BuildingMoveToolToggled += OnBuildingMoveToolToggled;
        _editModeManager.RecognizeTextBuildingsRequested += OnRecognizeTextBuildingsRequested;

        // 通用编辑窗口：模式按种类请求（EditorKinds），带 [AssistEditor] 标注的窗口类自动接上
        _assistEditors ??= new Services.AssistEditorHost(CreateAssistEditorContext, () => Window);
        _editModeManager.EditorHost = _assistEditors.Open;

        _fileStateManager.OpenFile(_mapData, _mapData.FilePath, SceneType);
        _editModeManager.SetSceneType(SceneType);

        var availableModes = _editModeManager.GetAvailableModes(SceneType);
        if (availableModes.Count > 0)
            _editModeManager.SwitchMode(availableModes[0]);

        UpdateOverlayFromEditMode();

        _keyboardManager.KeyDown += OnKeyboardManagerKeyDown;

        _commandManager.SetContext(new CommandContext { MapData = _mapData });
        // 只依赖 ICommandHost 抽象：具体实现由 DebugConsole 决定（默认内置实现，可由 DI 替换）
        var commandHost = _debugConsole.CommandHost;
        commandHost.SetOutput(new Core.Commands.DebugConsoleWriter(_debugConsole));
        // 错误恢复策略、控制台显隐、命令改数据、字符串表重载（卸载时全部解除）
        RegisterSceneEventHandlers(commandHost);
        RegisterAiBridge();
        _commandManager.RegisterCommand("undo", _ => PerformUndo(), "撤销上一步操作");
        _commandManager.RegisterCommand("redo", _ => PerformRedo(), "重做上一步操作");
        RegisterTerrainCliCommands();
        RegisterCameraCliCommands();
        RegisterProvinceCliCommands();
        RegisterScriptCliCommands();
        RegisterFileCliCommands();
        HexInfoService.Instance.SetMapData(_mapData);

        _skElement.Focus();
        Keyboard.Focus(_skElement);
        InputMethod.SetIsInputMethodEnabled(_skElement, true);

        RegisterToSceneManager();

        var sceneManager = RenderSceneManager.Instance;
        if (_sceneId >= 0)
        {
            sceneManager.ActivateScene(_sceneId);
            var existingScene = sceneManager.GetScene(_sceneId);
            if (existingScene != null && _sceneNameLabel != null)
                _sceneNameLabel.Text = existingScene.SceneName;
        }
        else if (_mapData != null)
        {
            var sceneName = !string.IsNullOrEmpty(_mapData.FilePath)
                ? System.IO.Path.GetFileNameWithoutExtension(_mapData.FilePath)
                : SceneTitle;
            _sceneId = sceneManager.CreateScene(sceneName, _mapData.FilePath, RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType));
            sceneManager.SetSceneMapData(_sceneId, _mapData);
            sceneManager.ActivateScene(_sceneId);

            if (_sceneNameLabel != null)
                _sceneNameLabel.Text = sceneName;
        }

        Debug.WriteLine($"[Timing] 命令注册/场景登记等: {sw.ElapsedMilliseconds}ms");

        // 主动同步一次渲染层开关。InitializeRenderers() 会把 EnableSelectionRender 等预设为 false，
        // 而负责刷新它们的 UpdateRenderLayersByMode 只挂在 ModeChanged 事件上；
        // 若本场景的初始模式与切换前相同（同类场景之间切换时很常见），事件不会触发，
        // 选区渲染就会一直停留在关闭状态，表现为"切换场景后选区高光消失"。
        UpdateRenderLayersByMode(_editModeManager.CurrentMode);
        AttachSceneUx();
        ReportSceneMemory();

        _skElement.InvalidateVisual();
        Debug.WriteLine($"[Timing] ===== OnLoaded 总计: {swTotal.ElapsedMilliseconds}ms =====");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _skElement.PaintSurface -= OnPaintSurface;
        _skElement.MouseLeftButtonDown -= OnWpfMouseLeftDown;
        _skElement.MouseLeftButtonUp -= OnWpfMouseLeftUp;
        _skElement.MouseRightButtonDown -= OnWpfMouseRightDown;
        _skElement.MouseRightButtonUp -= OnWpfMouseRightUp;
        _skElement.MouseMove -= OnWpfMouseMove;
        _skElement.MouseWheel -= OnWpfMouseWheel;
        _skElement.MouseLeave -= OnWpfMouseLeave;
        _skElement.SizeChanged -= OnSizeChanged;
        _skElement.KeyDown -= OnSkElementKeyDown;
        _skElement.KeyUp -= OnSkElementKeyUp;
        _skElement.TextInput -= OnSkElementTextInput;
        _mouseManager.MouseAction -= OnMouseAction;
        _hexSelector.SelectionChanged -= OnHexSelectionChanged;
        _hexSelector.SelectionRectChanged -= OnSelectionRectChanged;
        _keyboardManager.UnregisterBinding("EscBack");
        _keyboardManager.UnregisterBinding("ToggleConsole");
        _keyboardManager.UnregisterBinding("Screenshot");
        _keyboardManager.UnregisterBinding("QuickSave");
        UnregisterEditModeKeyBindings();
        DetachSceneUx();
        _keyboardManager.KeyDown -= OnKeyboardManagerKeyDown;
        _editModeManager.ModeChanged -= OnEditModeChanged;
        _editModeManager.StatusMessageChanged -= OnEditModeStatusMessageChanged;
        _editModeManager.DataModified -= OnEditModeDataModified;
        _editModeManager.BrushToggled -= OnBrushToggled;
        _editModeManager.BrushSizeChanged -= OnBrushSizeChanged;
        _editModeManager.BoundaryDrawToggled -= OnBoundaryDrawToggled;
        _editModeManager.DomainToggled -= OnDomainToggled;
        _editModeManager.BuildingNamesToggled -= OnBuildingNamesToggled;
        _editModeManager.CaptureLegionScreenshotRequested -= OnCaptureLegionScreenshotRequested;
        _editModeManager.MoveCameraToHexRequested -= OnMoveCameraToHexRequested;
        _editModeManager.BuildingMoveToolToggled -= OnBuildingMoveToolToggled;
        _editModeManager.RecognizeTextBuildingsRequested -= OnRecognizeTextBuildingsRequested;
        if (_assistEditors != null && _editModeManager.EditorHost == (Func<EditorRequest, bool>)_assistEditors.Open)
            _editModeManager.EditorHost = null;
        _assistEditors?.CloseAll();
        _editModeManager.Deinitialize();

        RenderSceneManager.Instance.SceneListChanged -= OnSceneListChanged;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_mapData == null || _camera == null) return;

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Black);

        _camera.ViewportWidth = info.Width;
        _camera.ViewportHeight = info.Height;

        _renderEngine.UpdateHelpFadeAnimation();
        _renderEngine.Render(canvas, _mapData, _camera);
        AfterScenePaint();
    }

    #region 相机控制

    /// <summary>
    /// 把相机中心移动到指定格子（行列坐标）。
    /// </summary>
    /// <param name="col">格子列号</param>
    /// <param name="row">格子行号</param>
    /// <returns>是否移动成功（坐标越界或相机未就绪时返回 false）</returns>
    public bool MoveCameraToHex(int col, int row)
    {
        if (_camera == null || _mapData == null || _skElement == null) return false;
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        _camera.ViewportWidth = _skElement.ActualWidth;
        _camera.ViewportHeight = _skElement.ActualHeight;

        _camera.CenterOnHex(col, row);
        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>
    /// 把相机中心移动到指定格子（格子序号 = row * MapWidth + col）。
    /// </summary>
    /// <param name="hexIndex">格子序号</param>
    /// <returns>是否移动成功（序号越界或相机未就绪时返回 false）</returns>
    public bool MoveCameraToHexIndex(int hexIndex)
    {
        if (_camera == null || _mapData == null || _skElement == null) return false;

        int totalCells = _mapData.MapWidth * _mapData.MapHeight;
        if (hexIndex < 0 || hexIndex >= totalCells) return false;

        int col = hexIndex % _mapData.MapWidth;
        int row = hexIndex / _mapData.MapWidth;
        return MoveCameraToHex(col, row);
    }

    /// <summary>处理模式层发来的相机移动请求（建筑编辑模式 Enter 键循环选中建筑）</summary>
    private void OnMoveCameraToHexRequested(int col, int row) => MoveCameraToHex(col, row);

    #endregion

    #region Keyboard Bridge

    private void OnSkElementKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
        if (!TryHandleSceneKeyDown(key, e.IsRepeat))
            _keyboardManager.ProcessKeyDown((int)key);
        e.Handled = true;
    }

    private void OnSkElementKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
        HandleSceneKeyUp(key);
        _keyboardManager.ProcessKeyUp((int)key);
        e.Handled = true;
    }

    private void OnSkElementTextInput(object sender, TextCompositionEventArgs e)
    {
        // 控制台现在使用WPF窗口，不再通过SkElement接收文本输入
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
        if (!TryHandleSceneKeyDown(key, e.IsRepeat))
            _keyboardManager.ProcessKeyDown((int)key);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
        HandleSceneKeyUp(key);
        _keyboardManager.ProcessKeyUp((int)key);
        base.OnKeyUp(e);
    }

    private void OnKeyboardManagerKeyDown(object? sender, Core.Input.KeyboardEventArgs e)
    {
        // 控制台现在使用WPF窗口，不再通过SkElement接收键盘事件
        // WPF窗口自己处理所有键盘输入
    }

    private static char? KeyToChar(System.Windows.Input.Key key, bool shift, bool capsLock)
    {
        bool upper = shift ^ capsLock;

        if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
        {
            char c = (char)('a' + (key - System.Windows.Input.Key.A));
            return upper ? char.ToUpper(c) : c;
        }

        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9 && !shift)
            return (char)('0' + (key - System.Windows.Input.Key.D0));

        if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            return (char)('0' + (key - System.Windows.Input.Key.NumPad0));

        if (shift)
        {
            return key switch
            {
                System.Windows.Input.Key.D1 => '!',
                System.Windows.Input.Key.D2 => '@',
                System.Windows.Input.Key.D3 => '#',
                System.Windows.Input.Key.D4 => '$',
                System.Windows.Input.Key.D5 => '%',
                System.Windows.Input.Key.D6 => '^',
                System.Windows.Input.Key.D7 => '&',
                System.Windows.Input.Key.D8 => '*',
                System.Windows.Input.Key.D9 => '(',
                System.Windows.Input.Key.D0 => ')',
                System.Windows.Input.Key.OemMinus => '_',
                System.Windows.Input.Key.OemPlus => '+',
                System.Windows.Input.Key.OemOpenBrackets => '{',
                System.Windows.Input.Key.OemCloseBrackets => '}',
                System.Windows.Input.Key.OemPipe => '|',
                System.Windows.Input.Key.OemSemicolon => ':',
                System.Windows.Input.Key.OemQuotes => '"',
                System.Windows.Input.Key.OemComma => '<',
                System.Windows.Input.Key.OemPeriod => '>',
                System.Windows.Input.Key.OemQuestion => '?',
                System.Windows.Input.Key.OemTilde => '~',
                System.Windows.Input.Key.Space => ' ',
                _ => null
            };
        }

        return key switch
        {
            System.Windows.Input.Key.OemMinus => '-',
            System.Windows.Input.Key.OemPlus => '=',
            System.Windows.Input.Key.OemOpenBrackets => '[',
            System.Windows.Input.Key.OemCloseBrackets => ']',
            System.Windows.Input.Key.OemPipe => '\\',
            System.Windows.Input.Key.OemSemicolon => ';',
            System.Windows.Input.Key.OemQuotes => '\'',
            System.Windows.Input.Key.OemComma => ',',
            System.Windows.Input.Key.OemPeriod => '.',
            System.Windows.Input.Key.OemQuestion => '/',
            System.Windows.Input.Key.OemTilde => '`',
            System.Windows.Input.Key.Space => ' ',
            _ => null
        };
    }

    /// <summary>
    /// 调试控制台的开/关已上收到主窗口（<see cref="MainWindow.ToggleDebugConsole"/>）：
    /// F3 要做成全局键、任何场景都能开，宿主就不能再挂在场景基类上。
    /// <para>
    /// 这里保留绑定并转发，有两个用处：模式帮助文本里仍能列出 F3；
    /// 万一哪条路径绕过了窗口级拦截（例如将来场景被挂到别的宿主窗口），也还能用。
    /// </para>
    /// </summary>
    private void OnToggleConsole() => Window.ToggleDebugConsole();

    private async void OnScreenshot()
    {
        await CaptureMapScreenshotAsync();
    }

    private enum ScreenshotMode { Standard, HexBased, Cancel }

    /// <summary>
    /// Core 截图请求的落地实现（Lua / CLI / AI 通用）。
    /// <para>
    /// 刻意不带保存对话框、不带进度窗 —— 脚本与 AI 调用必须能静默跑完；
    /// 需要挑路径、看进度的走原来的 Ctrl+S 截图流程（<see cref="CaptureMapScreenshotAsync"/>）。
    /// </para>
    /// <para>
    /// 只输出"看到的东西"（地形/海岸/城市/势力着色都按当前渲染设置走），不改任何数据。
    /// </para>
    /// </summary>
    private WC4MapEditor.Core.Services.MapScreenshotResult CaptureMapScreenshot(
        WC4MapEditor.Core.Services.MapScreenshotRequest request)
    {
        var mapData = _mapData;
        if (mapData == null) return WC4MapEditor.Core.Services.MapScreenshotResult.Fail(Loc.T("Render.Screenshot.NoMap"));

        // 路径：给了就用（相对路径按 exe 目录解析），没给就放到 screenshots/ 自动命名
        string path = request.OutputPath ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            System.IO.Directory.CreateDirectory(dir);
            path = System.IO.Path.Combine(dir, ScreenshotHelper.GenerateDefaultFileName());
        }
        else if (!System.IO.Path.IsPathRooted(path))
        {
            path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        // 注：request.SceneMode（截整个界面）在 EditModeManager.CaptureScreenshot 里就被分流到主窗口
        // 那份实现去了，走不到这里 —— 这里只管"把地图数据渲染成图"。

        // 中心点为负 = 整幅地图；否则取以它为中心的区域
        bool region = request.CenterCol >= 0 && request.CenterRow >= 0 && request.CellsW > 0 && request.CellsH > 0;
        int cellsW = region ? request.CellsW : mapData.MapWidth;
        int cellsH = region ? request.CellsH : mapData.MapHeight;
        int centerCol = region ? request.CenterCol : mapData.MapWidth / 2;
        int centerRow = region ? request.CenterRow : mapData.MapHeight / 2;

        var info = new ScreenshotHelper().CaptureRegion(mapData, centerCol, centerRow, cellsW, cellsH, path,
            request.MaxPixels, request.ShowGridLines, request.ShowHexLabels, RenderEngine.ShowLayer2,
            markCenter: region && request.MarkCenter);

        if (!info.Ok) return WC4MapEditor.Core.Services.MapScreenshotResult.Fail(Loc.F("Render.Screenshot.RenderFailed", path));

        int lastCol = info.OriginCol + info.CellsW - 1;
        int lastRow = info.OriginRow + info.CellsH - 1;
        double pxPerCell = info.PixelWidth / (double)Math.Max(1, info.CellsW);

        // 描述里必须带"图 ↔ 格子"的换算，AI 才能指着图上某处说出它是第几格
        string desc = region
            ? Loc.F("Render.Screenshot.RegionDesc", info.OriginCol, lastCol, info.OriginRow, lastRow, info.CellsW, info.CellsH, pxPerCell)
            : Loc.F("Render.Screenshot.FullDesc", lastCol, lastRow, info.CellsW, info.CellsH, pxPerCell);

        if (region && request.MarkCenter)
            desc += Loc.F("Render.Screenshot.CenterMarked", request.CenterCol, request.CenterRow);

        _debugConsole.WriteLine(Loc.F("Main.ScreenshotLog", desc, path));

        return new WC4MapEditor.Core.Services.MapScreenshotResult(true, path, info.PixelWidth, info.PixelHeight, desc);
    }

    /// <summary>F2 截图流程进行中（选模式、选路径、生成）：期间再按 F2 不会再开一套流程</summary>
    private bool _screenshotBusy;

    private async Task CaptureMapScreenshotAsync()
    {
        if (_screenshotBusy) return;
        _screenshotBusy = true;
        try
        {
            if (_mapData == null)
            {
                PostSceneStatus(Loc.T("Render.Console.ScreenshotNoMap"));
                return;
            }

            var screenshotMode = await ShowScreenshotModeDialogAsync();
            if (screenshotMode == ScreenshotMode.Cancel) return;

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = $"{Loc.T("Render.Screenshot.PngFilter")}|*.png|{Loc.T("Common.AllFiles")}|*.*",
                Title = Loc.T("Render.Screenshot.SaveTitle")
            };

            saveDialog.FileName = screenshotMode == ScreenshotMode.HexBased
                ? HexBasedScreenshotHelper.GenerateDefaultFileName()
                : ScreenshotHelper.GenerateDefaultFileName();

            var screenshotsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            if (!System.IO.Directory.Exists(screenshotsDir))
                System.IO.Directory.CreateDirectory(screenshotsDir);
            saveDialog.InitialDirectory = screenshotsDir;

            if (saveDialog.ShowDialog() != true) return;

            string outputPath = saveDialog.FileName;
            PostSceneStatus(Loc.T("Render.Console.ScreenshotStart"));

            var progressWindow = new ProgressWindow();
            progressWindow.Show();

            var progress = new Progress<(int current, int total, int row, int col, int percent)>(p =>
            {
                progressWindow.UpdateProgress(p.current, p.total, p.row, p.col, p.percent);
            });

            bool success;
            var mapData = _mapData;
            var showLayer2 = RenderEngine.ShowLayer2;

            switch (screenshotMode)
            {
                case ScreenshotMode.HexBased:
                    _debugConsole.WriteLine(Loc.T("Render.Console.ScreenshotHexMode"));
                    success = await Task.Run(() =>
                    {
                        using var helper = new HexBasedScreenshotHelper();
                        return helper.CaptureMap(mapData, outputPath, 1.0, false, false, showLayer2, progress);
                    });
                    break;

                default:
                    _debugConsole.WriteLine(Loc.T("Render.Console.ScreenshotStandardMode"));
                    success = await Task.Run(() =>
                    {
                        using var helper = new ScreenshotHelper();
                        return helper.CaptureMap(mapData, outputPath, 1.0, false, false, showLayer2, progress);
                    });
                    break;
            }

            progressWindow.PlayFadeOutAndClose();

            PostSceneStatus(success
                ? Loc.F("Render.Console.ScreenshotSaved", outputPath)
                : Loc.T("Render.Console.ScreenshotFailed"));
        }
        catch (Exception ex)
        {
            PostSceneStatus(Loc.F("Render.Console.ScreenshotError", ex.Message));
        }
        finally
        {
            _screenshotBusy = false;
        }
    }

    /// <summary>
    /// 选截图方式。对话框挂在主窗口上并拿走键盘焦点（模态）：打开期间场景快捷键不会在背后触发，Esc 取消。
    /// </summary>
    private async Task<ScreenshotMode> ShowScreenshotModeDialogAsync()
    {
        using var dialog = new ChoiceDialog(Window)
        {
            Title = Loc.T("Render.Screenshot.ModeTitle"),
            Message = Loc.T("Render.Screenshot.ModeDesc"),
            Choices = new[] { Loc.T("Render.Screenshot.ModeStandard"), Loc.T("Render.Screenshot.ModeHex") }
        };
        int choice = await dialog.ShowAsync();
        RestoreFocus();
        return choice switch
        {
            0 => ScreenshotMode.Standard,
            1 => ScreenshotMode.HexBased,
            _ => ScreenshotMode.Cancel
        };
    }

    /// <summary>
    /// 建筑名称显示开关（建筑编辑模式 U 键触发）。
    /// 全局生效且跨模式保持：关闭后切换到任何模式都不再显示建筑名称。
    /// </summary>
    private bool _buildingNamesVisible = true;

    private readonly GeoCoordinateCalculator _geoCalculator = new();

    private async Task OnAddGeoRefAsync()
    {
        if (_mapData == null)
        {
            MessageBox.Show(Loc.T("Render.MapNotLoaded"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var primary = _hexSelector.PrimarySelected;
        if (!primary.HasValue)
        {
            MessageBox.Show(Loc.T("Render.Geo.SelectCellFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int row = primary.Value.Row;
        int col = primary.Value.Col;

        using var inputDialog = new Views.Assist.DoubleInputDialog(Window)
        {
            Title = Loc.F("Render.Geo.AddRefTitle", row, col),
            Description1 = Loc.T("Render.Geo.Latitude"),
            Description2 = Loc.T("Render.Geo.Longitude"),
            DefaultValue1 = "",
            DefaultValue2 = ""
        };
        var result = await inputDialog.ShowAsync();
        if (!result.HasValue) return;

        var (latStr, lonStr) = result.Value;
        if (!double.TryParse(latStr, out double lat) || !double.TryParse(lonStr, out double lon))
        {
            MessageBox.Show(Loc.T("Render.Geo.InvalidLatLon"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var ruler = _renderEngine.GeoRuler;
        if (ruler == null)
        {
            ruler = new Rendering.Skia.GeoRulerRender();
            _renderEngine.GeoRuler = ruler;
        }

        ruler.MapWidth = _mapData.MapWidth;
        ruler.MapHeight = _mapData.MapHeight;
        ruler.AddMarker(row, col, lat, lon);
        UpdateGeoReferenceFromRuler();
        _skElement.InvalidateVisual();

        MessageBox.Show(Loc.F("Render.Geo.RefAdded", ruler.Markers.Count, row, col, lat, lon), Loc.T("Common.Success"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task OnGeoCalculateAsync()
    {
        if (_mapData == null)
        {
            MessageBox.Show(Loc.T("Render.MapNotLoaded"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateGeoReferenceFromRuler();

        var rp = _geoCalculator.ReferencePoints;

        if (!rp.IsValid())
        {
            MessageBox.Show(Loc.T("Render.Geo.NeedRefPoints"),
                Loc.T("Render.Geo.CalcTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{Loc.T("Render.Geo.JsonFilter")} (*.json)|*.json|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            Title = Loc.T("Render.Geo.SaveCalcTitle"),
            FileName = $"geo_coords_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (saveDialog.ShowDialog() != true) return;

        var progressWindow = new ProgressWindow();
        progressWindow.Title = Loc.T("Render.Geo.Calculating");
        progressWindow.Show();

        try
        {
            await Task.Run(() =>
            {
                _geoCalculator.CalculateAndExport(_mapData, saveDialog.FileName);
                Dispatcher.Invoke(() => progressWindow.UpdateProgress(100, 100, 0, 0, 100));
            });

            MessageBox.Show(Loc.F("Render.Geo.CalcSaved", saveDialog.FileName), Loc.T("Common.Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Geo.CalcFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressWindow.Close();
        }
    }

    private async Task OnGeoExportRefAsync()
    {
        UpdateGeoReferenceFromRuler();

        var rp = _geoCalculator.ReferencePoints;
        if (!rp.IsValid())
        {
            MessageBox.Show(Loc.T("Render.Geo.NeedRefPoints"),
                Loc.T("Render.Geo.ExportRefTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{Loc.T("Render.Geo.JsonFilter")} (*.json)|*.json|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            Title = Loc.T("Render.Geo.SaveRefTitle"),
            FileName = $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            await Task.Run(() => _geoCalculator.ExportReferencePointsOnly(saveDialog.FileName));
            MessageBox.Show(Loc.F("Render.Geo.RefSaved", saveDialog.FileName), Loc.T("Common.Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Common.ExportFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OnGeoImportRefAsync()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("Render.Geo.JsonFilter")} (*.json)|*.json|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            Title = Loc.T("Render.Geo.ImportRefTitle"),
            FileName = $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (openDialog.ShowDialog() != true) return;

        try
        {
            await Task.Run(() => _geoCalculator.ImportReferencePoints(openDialog.FileName));
            var rp = _geoCalculator.ReferencePoints;

            // 同步到标尺
            var ruler = _renderEngine.GeoRuler;
            if (ruler != null)
            {
                ruler.ClearMarkers();
                foreach (var p in rp.Points)
                {
                    ruler.AddMarker(p.Row, p.Col, p.Latitude, p.Longitude);
                }
            }

            _skElement.InvalidateVisual();
            MessageBox.Show(Loc.F("Render.Geo.RefImported", openDialog.FileName, rp.Points.Count), Loc.T("Common.Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.ImportFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 导出格子网格数据（不含经纬度），对应地形编辑模式的 F10。
    /// 与 F7 的经纬度导出同源，但无需设置参考点、不做经纬度换算，
    /// 输出结构中去掉 coordinate（经纬度）与 referencePoints。
    /// </summary>
    private async Task OnGeoExportGridAsync()
    {
        if (_mapData == null)
        {
            MessageBox.Show(Loc.T("Render.MapNotLoaded"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{Loc.T("Render.Geo.JsonFilter")} (*.json)|*.json|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            Title = Loc.T("Render.Geo.ExportGridTitle"),
            FileName = $"grid_data_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            await Task.Run(() => _geoCalculator.ExportGridOnly(_mapData, saveDialog.FileName));
            MessageBox.Show(Loc.F("Render.Geo.GridSaved", saveDialog.FileName), Loc.T("Common.Success"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Common.ExportFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnEscPressed()
    {
        // 有子窗口（军团编辑器 / 调色板 / 输入框等）处于活动状态时，Esc 归该子窗口处理。
        // 否则会在关闭子窗口的同时触发"返回主场景"，把整个编辑场景一起关掉。
        if (IsAnyOwnedWindowActive()) return;

        if (_debugConsole.IsVisible)
        {
            _debugConsole.HideConsole();
            return;
        }

        // 模式先处理 Esc（取消区域移动、清空选区……）；模式返回 false（没有可取消的东西）才弹出返回确认
        var escHandler = _editModeManager.CurrentHandler;
        string? escAction = escHandler?.GetGestureAction(ModeGesture.Escape);
        if (escHandler != null && escAction != null && _mapData != null)
        {
            var primary = _hexSelector.PrimarySelected;
            bool handled = false;
            try
            {
                handled = await _editModeManager.HandleKeyAction(escAction, primary?.Col ?? -1, primary?.Row ?? -1);
            }
            catch (Exception ex)
            {
                ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                    $"模式 Esc 动作失败: {escHandler.Mode}/{escAction}", nameof(OnEscPressed), ex);
                _editModeManager.RaiseStatusMessage(Loc.F("Mode.Status.ActionFailed", escHandler.DisplayName, escAction, ex.Message));
            }
            _skElement.InvalidateVisual();
            if (handled) return;
        }

        Debug.WriteLine("[Keyboard] Esc pressed - showing return confirm dialog");
        if (_confirmDialogShowing) return;
        await ShowReturnConfirmDialog();
    }

    /// <summary>当前是否有本程序的其他窗口（子窗口）处于活动状态</summary>
    private bool IsAnyOwnedWindowActive()
    {
        var main = Window;
        foreach (Window w in Application.Current.Windows)
        {
            if (ReferenceEquals(w, main)) continue;
            if (w.IsActive) return true;
        }
        return false;
    }

    private void RestoreFocus()
    {
        if (_skElement != null)
        {
            Keyboard.Focus(_skElement);
            Debug.WriteLine("[Keyboard] Focus restored to SKElement");
        }
    }

    private void CloseAllAssistWindows()
    {
        _assistEditors?.CloseAll();
        if (_hexInfoWindow != null)
        {
            _hexInfoWindow.PlayFadeOutAndClose();
            _hexInfoWindow = null;
        }
        if (_brushSettingsWindow != null)
        {
            _brushSettingsWindow.PlayFadeOutAndHide();
            _brushSettingsWindow = null;
        }
        DeactivateBrushOfMode(_editModeManager.CurrentMode);
        if (_debugConsole.IsVisible)
        {
            _debugConsole.HideConsole();
        }
    }

    private async Task ShowReturnConfirmDialog()
    {
        _confirmDialogShowing = true;

        // 有未保存的修改时明确告诉用户（以前不论有没有改过都是同一句泛泛的提示）
        bool dirty = _fileStateManager.IsDirty;
        using var dialog = new ConfirmDialog(Window)
        {
            Title = Loc.T("Render.ReturnConfirmTitle"),
            Message = dirty ? Loc.T("Scene.Return.DirtyMessage") : Loc.T("Render.ReturnConfirmMessage"),
            ConfirmText = Loc.T("Common.Back"),
            CancelText = Loc.T("Common.Cancel"),
            Destructive = dirty
        };

        bool result = await dialog.ShowAsync();

        RestoreFocus();

        if (result)
        {
            CloseAllAssistWindows();
            Window.ReturnToMainScene();
        }

        _confirmDialogShowing = false;
    }

    #endregion

    #region WPF Mouse Bridge

    private static MouseButtons ToMouseButtons(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtons.Left,
        MouseButton.Middle => MouseButtons.Middle,
        MouseButton.Right => MouseButtons.Right,
        MouseButton.XButton1 => MouseButtons.XButton1,
        MouseButton.XButton2 => MouseButtons.XButton2,
        _ => MouseButtons.None
    };

    private static KeyModifiers GetCurrentKeyModifiers()
    {
        var mods = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyModifiers.Alt;
        return mods;
    }

    private void OnWpfMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_skElement);

        var ruler = _renderEngine.GeoRuler;
        if (ruler != null && ruler.HitTestMarker(pos.X, pos.Y, out int markerId))
        {
            ruler.DraggingMarkerId = markerId;
            _skElement.CaptureMouse();
            _skElement.InvalidateVisual();
            e.Handled = true;
            return;
        }

        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseDown(MouseButtons.Left, pos.X, pos.Y);
        _skElement.CaptureMouse();
        if (!_skElement.IsKeyboardFocusWithin) Keyboard.Focus(_skElement);
        e.Handled = false;
    }

    private void OnWpfMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_skElement);

        var ruler = _renderEngine.GeoRuler;
        if (ruler != null && ruler.DraggingMarkerId.HasValue)
        {
            ruler.SnapDraggingMarkerToNearest(pos.X, pos.Y);
            ruler.DraggingMarkerId = null;
            _skElement.ReleaseMouseCapture();
            _skElement.InvalidateVisual();
            e.Handled = true;
            return;
        }

        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseUp(MouseButtons.Left, pos.X, pos.Y);
        _skElement.ReleaseMouseCapture();
        e.Handled = false;
    }

    private async void OnWpfMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        // 手绘边界优先：边界绘制模式下右键专门用来画线，不走下面的地理标尺标记编辑，
        // 否则画一笔会顺带弹出一个标记对话框。
        if (TryBeginBoundaryDraw(e))
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(_skElement);

        var ruler = _renderEngine.GeoRuler;
        if (ruler != null && ruler.HitTestMarker(pos.X, pos.Y, out int markerId))
        {
            var marker = ruler.Markers.FirstOrDefault(m => m.Id == markerId);
            if (marker == null) return;

            using var inputDialog = new Views.Assist.DoubleInputDialog(Window)
            {
                Title = Loc.F("Render.Geo.SetMarkerTitle", markerId, marker.Row, marker.Col),
                Description1 = Loc.T("Render.Geo.Latitude"),
                Description2 = Loc.T("Render.Geo.Longitude"),
                DefaultValue1 = marker.Latitude != 0 ? marker.Latitude.ToString("F7") : "",
                DefaultValue2 = marker.Longitude != 0 ? marker.Longitude.ToString("F7") : ""
            };
            var result = await inputDialog.ShowAsync();
            if (result.HasValue)
            {
                var (latStr, lonStr) = result.Value;
                if (double.TryParse(latStr, out double lat) && double.TryParse(lonStr, out double lon))
                {
                    marker.Latitude = lat;
                    marker.Longitude = lon;
                    UpdateGeoReferenceFromRuler();
                    _skElement.InvalidateVisual();
                }
            }
            e.Handled = true;
            return;
        }

        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseDown(MouseButtons.Right, pos.X, pos.Y);
        e.Handled = false;
    }

    private void UpdateGeoReferenceFromRuler()
    {
        var ruler = _renderEngine.GeoRuler;
        if (ruler == null) return;

        var rp = _geoCalculator.ReferencePoints;
        rp.Points.Clear();

        foreach (var marker in ruler.Markers)
        {
            rp.Points.Add(new Core.Geo.GeoReferencePoint
            {
                Row = marker.Row,
                Col = marker.Col,
                Latitude = marker.Latitude,
                Longitude = marker.Longitude
            });
        }
    }

    private void OnWpfMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        EndBoundaryDraw();

        var pos = e.GetPosition(_skElement);
        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseUp(MouseButtons.Right, pos.X, pos.Y);
        e.Handled = false;
    }

    private void OnWpfMouseMove(object sender, MouseEventArgs e)
    {
        TrackHoverPoint(e.GetPosition(_skElement));

        // 右键拖拽画边界：把上一落点连到当前位置，形成连续的线。
        // 命中时直接返回，避免同时刷新省区画笔预览。
        if (ContinueBoundaryDraw(e)) return;

        var pos = e.GetPosition(_skElement);

        var ruler = _renderEngine.GeoRuler;
        if (ruler != null && ruler.DraggingMarkerId.HasValue)
        {
            ruler.SnapDraggingMarkerToNearest(pos.X, pos.Y);
            _skElement.InvalidateVisual();
            e.Handled = true;
            return;
        }

        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseMove(pos.X, pos.Y);
        UpdateBrushPreview(pos.X, pos.Y);
    }

    private void OnWpfMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(_skElement);
        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseWheel(e.Delta, pos.X, pos.Y);
        e.Handled = false;
    }

    #endregion

    #region Mouse Action Handlers

    private void OnMouseAction(object? sender, MouseActionEventArgs e)
    {
        switch (e.Action)
        {
            case MouseActionKind.DragMove when e.Button == MouseButtons.Left:
                HandlePan(e);
                break;
            case MouseActionKind.Click when e.Button == MouseButtons.Left:
                HandleHexSelect(e);
                TryModeGesture(ModeGesture.LeftClick, e);
                break;
            case MouseActionKind.Wheel:
                // 控制台现在使用WPF窗口，不再通过SkElement接收滚轮事件
                HandleZoom(e);
                break;
            case MouseActionKind.DragStart when e.Button == MouseButtons.Right:
                if (TryStartBuildingDrag(e)) break;
                if (TryStartBrushDrag(e)) break;
                HandleSelectionRectStart(e);
                break;
            case MouseActionKind.DragMove when e.Button == MouseButtons.Right:
                if (TryContinueBuildingDrag(e)) break;
                if (TryContinueBrushDrag(e)) break;
                HandleSelectionRectMove(e);
                break;
            case MouseActionKind.DragEnd when e.Button == MouseButtons.Right:
                if (TryEndBuildingDrag(e)) break;
                if (TryEndBrushDrag(e)) break;
                HandleSelectionRectEnd(e);
                break;
            case MouseActionKind.Click when e.Button == MouseButtons.Right:
                if (TryBrushClick(e)) break;
                if (TryModeGesture(ModeGesture.RightClick, e)) break;
                HandleRightClickSelect(e);
                break;
        }
    }

    /// <summary>
    /// 把鼠标手势交给当前模式：模式在 Core 的动作表里声明"手势 → 动作"（<see cref="IModeHandler.GetGestureAction"/>），
    /// 这里只负责换算格子并分发。返回 true 表示模式接管了这次点击。
    /// </summary>
    private bool TryModeGesture(ModeGesture gesture, MouseActionEventArgs e)
    {
        var handler = _editModeManager.CurrentHandler;
        if (handler == null || _mapData == null || _camera == null) return false;
        if ((e.Modifiers & (Core.Input.KeyModifiers.Shift | Core.Input.KeyModifiers.Ctrl)) != 0) return false;

        string? action = handler.GetGestureAction(gesture);
        if (action == null) return false;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        _ = RunModeGestureAsync(handler, action, col, row);
        return true;
    }

    private async Task RunModeGestureAsync(IModeHandler handler, string action, int col, int row)
    {
        try
        {
            await _editModeManager.HandleKeyAction(action, col, row);
        }
        catch (Exception ex)
        {
            ErrorCollector.Instance.RecordError(ErrorSeverity.Error,
                $"模式鼠标动作失败: {handler.Mode}/{action}", nameof(TryModeGesture), ex);
            _editModeManager.RaiseStatusMessage(Loc.F("Mode.Status.ActionFailed", handler.DisplayName, action, ex.Message));
        }
        _skElement.InvalidateVisual();
    }

    private void HandlePan(MouseActionEventArgs e)
    {
        if (_camera == null) return;
        _camera.Pan(e.DeltaPosition.X, e.DeltaPosition.Y);
        _renderEngine.InvalidateViewLayerCache();
        _skElement.InvalidateVisual();
    }

    private void HandleHexSelect(MouseActionEventArgs e)
    {
        if (_mapData == null || _camera == null) return;
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;

        if (!_editModeManager.IsSelectionActive)
        {
            // 非选区模式不改选区，只显示这一格的位置与概况（以前单击毫无反馈）
            InspectHex(col, row);
            return;
        }

        bool shiftPressed = (e.Modifiers & Core.Input.KeyModifiers.Shift) != 0;
        bool ctrlPressed = (e.Modifiers & Core.Input.KeyModifiers.Ctrl) != 0;

        if (shiftPressed)
        {
            _hexSelector.AddToSelection(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }
        else if (ctrlPressed)
        {
            _hexSelector.RemoveFromSelection(new HexCoord(col, row));
        }
        else
        {
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }

        _hexSelector.SetSelectionMoving(false, 0, 0, null);

        // 选区变化后必须主动重绘：单击后鼠标通常不再移动，否则选区高光要等到
        // 下一次重绘（例如切换模式）才会出现。
        _skElement.InvalidateVisual();
    }

    private void HandleZoom(MouseActionEventArgs e)
    {
        if (_camera == null) return;
        double factor = e.WheelDelta > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = Math.Max(0.1, Math.Min(5.0, _camera.ZoomLevel * factor));
        _camera.ZoomAt(e.Position.X, e.Position.Y, newZoom);
        _renderEngine.InvalidateViewLayerCache();
        _skElement.InvalidateVisual();
    }

    private void HandleSelectionRectStart(MouseActionEventArgs e)
    {
        if (!_editModeManager.IsSelectionActive) return;
        _hexSelector.BeginSelectionRect(e.Position.X, e.Position.Y);
    }

    private void HandleSelectionRectMove(MouseActionEventArgs e)
    {
        if (!_editModeManager.IsSelectionActive) return;
        _hexSelector.UpdateSelectionRect(e.Position.X, e.Position.Y);
    }

    private void HandleSelectionRectEnd(MouseActionEventArgs e)
    {
        if (!_editModeManager.IsSelectionActive) return;
        if (_mapData == null || _camera == null) return;

        var filter = GetSelectionFilter();
        var matched = _hexSelector.EndSelectionRect(
            (x, y) => _camera.ScreenToHex(x, y),
            (c, r) => _camera.HexToScreen(c, r),
            _mapData.MapWidth, _mapData.MapHeight, filter);

        if (matched.Count > 0)
        {
            bool shiftPressed = (e.Modifiers & Core.Input.KeyModifiers.Shift) != 0;
            bool ctrlPressed = (e.Modifiers & Core.Input.KeyModifiers.Ctrl) != 0;
            if (shiftPressed)
            {
                foreach (var coord in matched)
                    _hexSelector.AddToSelection(coord);
            }
            else if (ctrlPressed)
            {
                foreach (var coord in matched)
                    _hexSelector.RemoveFromSelection(coord);
            }
            else
            {
                _hexSelector.SetSelection(matched);
            }

            // 框选结束后同样主动重绘，确保高光立即出现
            _skElement.InvalidateVisual();
        }
    }

    private void HandleRightClickSelect(MouseActionEventArgs e)
    {
        if (!_editModeManager.IsSelectionActive) return;
        if (_mapData == null || _camera == null) return;
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;
        var coord = new HexCoord(col, row);

        bool shiftPressed = (e.Modifiers & Core.Input.KeyModifiers.Shift) != 0;
        bool ctrlPressed = (e.Modifiers & Core.Input.KeyModifiers.Ctrl) != 0;

        if (shiftPressed)
        {
            _hexSelector.AddToSelection(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }
        else if (ctrlPressed)
        {
            _hexSelector.RemoveFromSelection(coord);
        }
        else
        {
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }

        // 选区变化后主动重绘，保证高光立即出现
        _skElement.InvalidateVisual();
        }

    /// <summary>框选 / 点选的命中过滤：由当前模式声明（<see cref="IModeHandler.CreateSelectionFilter"/>）。</summary>
    protected virtual Func<HexCoord, bool>? GetSelectionFilter()
    {
        if (_mapData == null) return null;
        return _editModeManager.CurrentHandler?.CreateSelectionFilter(_mapData);
    }

    private bool IsBrushModeActive()
    {
        if (!_editModeManager.IsEditModeActive) return false;
        var target = _editModeManager.GetActiveBrushTarget();
        return target != null && target.BrushActive;
    }

    // 上一次画笔预览的参数：鼠标在同一格里移动时不重绘整个画面（每个鼠标事件一帧太浪费）
    private int _lastBrushCol = int.MinValue, _lastBrushRow = int.MinValue, _lastBrushSize = -1;
    private string? _lastBrushShape;
    private double _lastBrushZoom = double.NaN, _lastBrushOffsetX = double.NaN, _lastBrushOffsetY = double.NaN;

    private void ResetBrushPreviewTracking()
    {
        _lastBrushCol = int.MinValue;
        _lastBrushRow = int.MinValue;
    }

    private void UpdateBrushPreview(double screenX, double screenY)
    {
        if (_camera == null || !IsBrushModeActive())
        {
            _renderEngine.HideBrushPreview();
            ResetBrushPreviewTracking();
            return;
        }

        var target = _editModeManager.GetActiveBrushTarget();
        if (target == null)
        {
            _renderEngine.HideBrushPreview();
            ResetBrushPreviewTracking();
            return;
        }

        var (col, row) = _camera.ScreenToHex(screenX, screenY);
        bool unchanged = col == _lastBrushCol && row == _lastBrushRow
                         && target.BrushSize == _lastBrushSize && target.BrushShape == _lastBrushShape
                         && _camera.ZoomLevel == _lastBrushZoom
                         && _camera.OffsetX == _lastBrushOffsetX && _camera.OffsetY == _lastBrushOffsetY;
        if (unchanged) return;

        _lastBrushCol = col; _lastBrushRow = row;
        _lastBrushSize = target.BrushSize; _lastBrushShape = target.BrushShape;
        _lastBrushZoom = _camera.ZoomLevel; _lastBrushOffsetX = _camera.OffsetX; _lastBrushOffsetY = _camera.OffsetY;

        _renderEngine.SetBrushPreview(
            col, row,
            target.BrushSize, target.BrushShape,
            _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
            true);
        _skElement.InvalidateVisual();
    }

    private void OnWpfMouseLeave(object sender, MouseEventArgs e)
    {
        ClearHoverPoint();
        _renderEngine.HideBrushPreview();
        ResetBrushPreviewTracking();
        _skElement.InvalidateVisual();
    }

    private void ApplyBrushAtPosition(MousePosition pos)
    {
        if (_mapData == null || _camera == null) return;

        var (col, row) = _camera.ScreenToHex(pos.X, pos.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;
        if (col == _brushLastPaintedCell.col && row == _brushLastPaintedCell.row) return;
        _brushLastPaintedCell = (col, row);

        if (_editModeManager.CurrentMode == EditMode.ProvinceEdit)
        {
            var province = _editModeManager.GetModifier<ProvinceModifier>();
            if (province == null) return;

            var provinceMaskIds = _brushSettingsWindow?.IsMaskEnabled == true
                ? _brushSettingsWindow!.MaskedTerrainIds
                : null;
            bool provinceMaskInclude = _brushSettingsWindow?.MaskIncludeMode ?? true;

            if (provinceMaskIds != null && provinceMaskIds.Count > 0)
                province.PaintWithBrushMasked(col, row, provinceMaskIds, provinceMaskInclude);
            else
                province.PaintWithBrush(col, row);

            _renderEngine.InvalidateProvinceCache();
            _skElement.InvalidateVisual();
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.BelongEdit)
        {
            var belong = _editModeManager.GetModifier<BelongModifier>();
            if (belong == null) return;

            var belongMaskIds = _brushSettingsWindow?.IsMaskEnabled == true
                ? _brushSettingsWindow!.MaskedTerrainIds
                : null;
            bool belongMaskInclude = _brushSettingsWindow?.MaskIncludeMode ?? true;

            if (belongMaskIds != null && belongMaskIds.Count > 0)
                belong.PaintWithBrushMasked(col, row, belongMaskIds, belongMaskInclude);
            else
                belong.PaintWithBrush(col, row);

            _renderEngine.Invalidate();
            _skElement.InvalidateVisual();
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.ArmyDeploy)
        {
            var army = _editModeManager.GetModifier<ArmyModifier>();
            if (army == null) return;

            army.PaintWithBrush(col, row);

            _renderEngine.Invalidate();
            _skElement.InvalidateVisual();
            return;
        }

        PaintTerrainBrushAt(col, row);
    }

    private void ApplyBrushStrokeSegment(MousePosition from, MousePosition to)
    {
        if (_mapData == null || _camera == null) return;

        double dist = from.DistanceTo(to);
        int steps = Math.Max(1, (int)Math.Ceiling(dist / BrushInterpolationStep));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            ApplyBrushAtPosition(new MousePosition(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t));
        }
    }

    private bool TryBrushClick(MouseActionEventArgs e)
    {
        if (!IsBrushModeActive()) return false;
        _brushLastPaintedCell = (-1, -1);
        _editModeManager.BeginBrushStroke();
        ApplyBrushAtPosition(e.Position);
        _editModeManager.EndBrushStroke("画笔绘制");
        return true;
    }

    private bool TryStartBrushDrag(MouseActionEventArgs e)
    {
        if (!IsBrushModeActive()) return false;
        _brushDragging = true;
        _brushLastDragPos = e.Position;
        _brushLastPaintedCell = (-1, -1);
        _editModeManager.BeginBrushStroke();
        ApplyBrushAtPosition(e.Position);
        return true;
    }

    private bool TryContinueBrushDrag(MouseActionEventArgs e)
    {
        if (!_brushDragging) return false;

        double dx = Math.Abs(e.Position.X - _brushLastDragPos.X);
        double dy = Math.Abs(e.Position.Y - _brushLastDragPos.Y);
        if (dx < BrushMinDragDistance && dy < BrushMinDragDistance)
            return true;

        ApplyBrushStrokeSegment(_brushLastDragPos, e.Position);
        _brushLastDragPos = e.Position;
        return true;
    }

    private bool TryEndBrushDrag(MouseActionEventArgs e)
    {
        if (!_brushDragging) return false;
        _brushDragging = false;
        _editModeManager.EndBrushStroke("画笔绘制");
        return true;
    }

    #endregion

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        int w = (int)_skElement.ActualWidth;
        int h = (int)_skElement.ActualHeight;
        if (w > 0 && h > 0)
            _renderEngine.Resize(w, h);
        _skElement.InvalidateVisual();
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // 与 Esc 一样先确认（以前点 ← 直接返回，未保存的修改一并丢掉）
        if (_confirmDialogShowing) return;
        await ShowReturnConfirmDialog();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => PerformUndo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void ViewLayerButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 45, 45, 48)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
        };

        var loadItem = new MenuItem { Header = Loc.T("Render.ViewLayer.Load"), Foreground = Brushes.White };
        loadItem.Click += ViewLayerLoad_Click;
        menu.Items.Add(loadItem);

        var toggleItem = new MenuItem
        {
            Header = _renderEngine.ViewLayerVisible ? Loc.T("Render.ViewLayer.Hide") : Loc.T("Render.ViewLayer.Show"),
            Foreground = Brushes.White,
            // 没加载底图时显示 / 隐藏没有意义（原条件对布尔值取 ToString，永远为真）
            IsEnabled = !string.IsNullOrEmpty(_renderEngine.ViewLayerImagePath)
        };
        toggleItem.Click += ViewLayerToggle_Click;
        menu.Items.Add(toggleItem);

        var clearItem = new MenuItem { Header = Loc.T("Render.ViewLayer.Clear"), Foreground = Brushes.White };
        clearItem.Click += ViewLayerClear_Click;
        menu.Items.Add(clearItem);

        menu.Items.Add(new Separator());

        var opacityUpItem = new MenuItem { Header = Loc.T("Render.ViewLayer.OpacityUp"), Foreground = Brushes.White };
        opacityUpItem.Click += (s, ev) =>
        {
            _renderEngine.ViewLayerOpacity = Math.Min(1f, _renderEngine.ViewLayerOpacity + 0.1f);
            _debugConsole.WriteLine(Loc.F("Render.Console.ViewLayerOpacity", _renderEngine.ViewLayerOpacity));
            _skElement.InvalidateVisual();
        };
        menu.Items.Add(opacityUpItem);

        var opacityDownItem = new MenuItem { Header = Loc.T("Render.ViewLayer.OpacityDown"), Foreground = Brushes.White };
        opacityDownItem.Click += (s, ev) =>
        {
            _renderEngine.ViewLayerOpacity = Math.Max(0f, _renderEngine.ViewLayerOpacity - 0.1f);
            _debugConsole.WriteLine(Loc.F("Render.Console.ViewLayerOpacity", _renderEngine.ViewLayerOpacity));
            _skElement.InvalidateVisual();
        };
        menu.Items.Add(opacityDownItem);

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ViewLayerLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Render.ViewLayer.SelectImageTitle"),
            Filter = $"{Loc.T("Render.ViewLayer.ImageFilter")}|*.png;*.jpg;*.jpeg;*.bmp;*.gif|{Loc.T("Common.AllFiles")}|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            if (_renderEngine.LoadViewLayerImage(dialog.FileName))
                _debugConsole.WriteLine(Loc.F("Render.Console.ViewLayerLoaded", dialog.FileName));
            else
                _debugConsole.WriteLine(Loc.F("Render.Console.ViewLayerLoadFailed", dialog.FileName));
            _skElement.InvalidateVisual();
        }
    }

    private void ViewLayerToggle_Click(object sender, RoutedEventArgs e) => ToggleViewLayerVisibility();

    private void ViewLayerClear_Click(object sender, RoutedEventArgs e)
    {
        _renderEngine.ClearViewLayerImage();
        _debugConsole.WriteLine(Loc.T("Render.Console.ViewLayerCleared"));
        _skElement.InvalidateVisual();
    }

    private void SceneTabButton_Click(object sender, RoutedEventArgs e) => ToggleSceneTabPopup();

    private void RefreshSceneTabButtons()
    {
        if (_sceneTabPanel == null) return;

        _sceneTabPanel.Children.Clear();

        var sceneManager = RenderSceneManager.Instance;
        var scenes = sceneManager.AllScenes;
        var currentId = sceneManager.CurrentSceneId;

        foreach (var scene in scenes)
        {
            var isActive = scene.SceneId == currentId;
            var typeLabel = RenderSceneManager.GetSceneTypeDisplayLabel(scene.SceneType);
            var displayText = $"{typeLabel} {scene.SceneName}";
            var btn = new Button
            {
                Content = displayText,
                Foreground = isActive ? Brushes.White : Brushes.LightGray,
                FontSize = 12,
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 122, 204))
                    : new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(1, 2, 1, 2),
                Tag = scene.SceneId,
                Focusable = false
            };

            btn.MouseEnter += (s, ev) =>
            {
                if (!isActive)
                    btn.Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
            };
            btn.MouseLeave += (s, ev) =>
            {
                if (!isActive)
                    btn.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
            };
            btn.Click += SceneTabItem_Click;
            _sceneTabPanel.Children.Add(btn);
        }

        if (scenes.Count == 0)
        {
            var emptyLabel = new TextBlock
            {
                Text = Loc.T("Render.NoOpenScenes"),
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(10, 6, 10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            _sceneTabPanel.Children.Add(emptyLabel);
        }
    }

    private void SceneTabItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int targetSceneId) return;

        var popup = _sceneTabPopup;
        if (popup != null)
            popup.IsOpen = false;

        if (targetSceneId == _sceneId) return;

        SwitchToScene(targetSceneId);
    }

    private void RegisterToSceneManager()
    {
        var sceneManager = RenderSceneManager.Instance;
        sceneManager.SceneListChanged += OnSceneListChanged;
    }

    private void OnSceneListChanged()
    {
        var popup = _sceneTabPopup;
        if (popup != null && popup.IsOpen)
            RefreshSceneTabButtons();
    }

    private void OnHexSelectionChanged(object? sender, HexSelectionChangedEventArgs e)
    {
        _skElement.InvalidateVisual();
        TrackPrimaryForModeSwitch(e);
        ShowHexInfoTarget();
        QueueSceneReadoutRefresh();
    }

    private void OnSelectionRectChanged(object? sender, SelectionRectChangedEventArgs e)
    {
        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 按建筑名称关键字跳转，供 goto（单参数非数字）与 goto_building 共用。
    /// 支持 <c>#&lt;nameId&gt;</c> 按字符串表 ID 精确匹配。
    /// </summary>
    private void GotoBuildingByKeyword(string keyword)
    {
        if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
        if (_mapData.Buildings.Count == 0) { _debugConsole.WriteLine(Loc.T("Render.Console.NoBuildings")); return; }

        // #<nameId> 形式：按字符串表 ID 精确匹配
        if (keyword.StartsWith('#') && int.TryParse(keyword.AsSpan(1), out int nameId))
        {
            bool found = false;
            for (int i = 0; i < _mapData.Buildings.Count; i++)
            {
                var b = _mapData.Buildings[i];
                if (b.Name != nameId) continue;

                JumpToBuildingCoord(b.Coordinate, $"#{nameId}");
                found = true;
                break;
            }
            if (!found) _debugConsole.WriteLine(Loc.F("Render.Console.NoBuildingWithNameId", nameId));
            return;
        }

        var cityNames = GetCityNameTable();
        var matches = new List<(string name, int coord)>();

        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var b = _mapData.Buildings[i];
            if (!cityNames.TryGetValue(b.Name, out string? name) || string.IsNullOrEmpty(name)) continue;
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                matches.Add((name, b.Coordinate));
        }

        if (matches.Count == 0)
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.NoBuildingContainsHint", keyword));
            return;
        }

        // 名称完全一致的优先，避免"柏林"被"柏林郊区"这类前缀更长的名字抢先
        int targetIndex = matches.FindIndex(m => string.Equals(m.name, keyword, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) targetIndex = 0;

        var (targetName, targetCoord) = matches[targetIndex];
        JumpToBuildingCoord(targetCoord, targetName);

        if (matches.Count > 1)
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.MultipleMatches", matches.Count));
            const int MaxListed = 10;
            for (int i = 0; i < matches.Count && i <= MaxListed; i++)
            {
                if (i == targetIndex) continue;
                var (n, c) = matches[i];
                _debugConsole.WriteLine($"  {n} ({c % _mapData.MapWidth}, {c / _mapData.MapWidth})");
            }
            if (matches.Count > MaxListed + 1)
                _debugConsole.WriteLine(Loc.F("Render.Console.MoreMatches", matches.Count - MaxListed - 1));
        }
    }

    #region EditMode Integration

    /// <summary>
    /// 全局按键绑定 - 与模式无关，始终可用
    /// </summary>
    // DescKey 存的是翻译键而不是文本：静态数组在类型初始化时求值一次，
    // 若直接存 Loc 结果，运行时切换语言后按键说明不会跟着变。
    private static readonly (string Id, Key Key, KeyModifiers Mods, string Action, string DescKey)[] GlobalKeyDefs =
    {
        ("Global_Space", Key.Space, KeyModifiers.None, "cycle_mode", "Render.Key.CycleMode"),
        ("Global_Tab", Key.Tab, KeyModifiers.None, "tab_action", "Render.Key.TabAction"),
        ("Global_F1", Key.F1, KeyModifiers.None, "toggle_show_layer2", "Render.Key.ToggleLayer2"),
        ("Global_CtrlF1", Key.F1, KeyModifiers.Ctrl, "toggle_help_text", "Render.Key.ToggleHelp"),
        ("Global_B", Key.B, KeyModifiers.None, "toggle_hex_borders", "Render.Key.ToggleGrid"),
        ("Global_N", Key.N, KeyModifiers.None, "toggle_labels", "Render.Key.ToggleLabels"),
        ("Global_CtrlZ", Key.Z, KeyModifiers.Ctrl, "undo", "Common.Undo"),
        ("Global_CtrlY", Key.Y, KeyModifiers.Ctrl, "redo", "Common.Redo"),
    };

    private void InitializeEditModeKeyBindings()
    {
        // 只注册全局按键 - 模式特有按键由 EditModeManager 在切换模式时动态注册
        foreach (var def in GlobalKeyDefs)
        {
            _keyboardManager.RegisterBinding(def.Id, (int)def.Key, def.Mods,
                () => DispatchEditModeAction(def.Action), Loc.T(def.DescKey));
        }
    }

    private void UnregisterEditModeKeyBindings()
    {
        foreach (var def in GlobalKeyDefs)
            _keyboardManager.UnregisterBinding(def.Id);
    }

    /// <summary>
    /// 注册摄像机跳转相关命令。与编辑模式无关，任何模式下都能用。
    /// 建筑名取自字符串表（同 BuildingRender 的城市名表：Building.Name 即 nameId）。
    /// </summary>
    private void RegisterCameraCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("goto", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoUsage.Header"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoUsage.ByColRow"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoUsage.ByCoord"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoUsage.ByName"));
                return;
            }

            // 形式一：两个参数 = 行列坐标
            if (args.Length >= 2)
            {
                if (!int.TryParse(args[0], out int c) || !int.TryParse(args[1], out int r))
                {
                    _debugConsole.WriteLine(Loc.T("Render.Console.CoordMustBeInt"));
                    return;
                }

                if (MoveCameraToHex(c, r))
                    _debugConsole.WriteLine(Loc.F("Render.Console.CameraMovedTo", c, r));
                else
                    _debugConsole.WriteLine(Loc.F("Render.Console.CoordOutOfRange", _mapData.MapWidth, _mapData.MapHeight));
                return;
            }

            string key = args[0];

            // 形式二：单个整数 = 地块序号（坐标值）
            if (int.TryParse(key, out int coord))
            {
                if (coord < 0 || coord >= _mapData.MapWidth * _mapData.MapHeight)
                {
                    _debugConsole.WriteLine(
                        Loc.F("Render.Console.CoordValueOutOfRange", _mapData.MapWidth * _mapData.MapHeight - 1));
                    return;
                }

                int col2 = coord % _mapData.MapWidth;
                int row2 = coord / _mapData.MapWidth;

                if (MoveCameraToHex(col2, row2))
                    _debugConsole.WriteLine(Loc.F("Render.Console.CameraMovedToCoord", coord, col2, row2));
                return;
            }

            // 形式三：非数字 = 建筑名称，复用 goto_building 的查找逻辑
            GotoBuildingByKeyword(key);
        }, "移动相机到指定格子", "<col> <row> | <坐标值> | <建筑名称>");

        cm.RegisterCommand("goto_building", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            if (_mapData.Buildings.Count == 0) { _debugConsole.WriteLine(Loc.T("Render.Console.NoBuildings")); return; }

            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoBuildingUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoBuildingUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoBuildingUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GotoBuildingUsage.Line4"));
                return;
            }

            GotoBuildingByKeyword(string.Join(" ", args));
        }, "移动相机到指定名称的建筑", "<关键字|#nameId>");

        cm.RegisterCommand("list_buildings", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            if (_mapData.Buildings.Count == 0) { _debugConsole.WriteLine(Loc.T("Render.Console.NoBuildings")); return; }

            string keyword = args.Length > 0 ? string.Join(" ", args) : "";
            var cityNames = GetCityNameTable();

            var rows = new List<(string name, int coord, ushort nameId)>();
            for (int i = 0; i < _mapData.Buildings.Count; i++)
            {
                var b = _mapData.Buildings[i];
                if (!cityNames.TryGetValue(b.Name, out string? name) || string.IsNullOrEmpty(name)) continue;
                if (keyword.Length > 0 && !name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add((name, b.Coordinate, b.Name));
            }

            if (rows.Count == 0)
            {
                _debugConsole.WriteLine(keyword.Length > 0
                    ? Loc.F("Render.Console.NoBuildingContains", keyword)
                    : Loc.T("Render.Console.NoNamedBuildings"));
                return;
            }

            rows.Sort((a, b) => a.coord.CompareTo(b.coord));

            _debugConsole.WriteLine(Loc.F("Render.Console.NamedBuildingsHeader", rows.Count,
                keyword.Length > 0 ? Loc.F("Render.Console.FilterSuffix", keyword) : "", _mapData.Buildings.Count));

            const int MaxRows = 40;
            int shown = Math.Min(rows.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                var (name, coord, nameId) = rows[i];
                _debugConsole.WriteLine(Loc.F("Render.Console.BuildingRow", name, coord % _mapData.MapWidth, coord / _mapData.MapWidth, nameId));
            }

            if (rows.Count > shown)
                _debugConsole.WriteLine(Loc.F("Render.Console.MoreBuildings", rows.Count - shown));
        }, "列出地图上的具名建筑（含坐标与名称ID）", "[关键字]");
    }

    /// <summary>把相机移到建筑所在格并输出结果。</summary>
    private void JumpToBuildingCoord(int coord, string displayName)
    {
        if (_mapData == null) return;

        int col = coord % _mapData.MapWidth;
        int row = coord / _mapData.MapWidth;

        if (MoveCameraToHex(col, row))
            _debugConsole.WriteLine(Loc.F("Render.Console.CameraMovedToBuilding", displayName, col, row));
        else
            _debugConsole.WriteLine(Loc.F("Render.Console.BuildingOutOfRange", displayName, col, row));
    }

    /// <summary>
    /// 读取城市名表（nameId → 名称）。与 BuildingRender 使用同一数据源，
    /// 供 goto_building / list_buildings 按名称查找建筑。
    /// </summary>
    private static Dictionary<int, string> GetCityNameTable()
    {
        var names = new Dictionary<int, string>();
        try
        {
            var parser = Core.Config.ConfigManager.Instance.GetStringTableParser();
            foreach (var kvp in parser.FindCityNames())
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                    names[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CameraCliCommands] 读取城市名表失败: {ex.GetType().Name}: {ex.Message}");
        }
        return names;
    }

    /// <summary>
    /// 注册 Lua 脚本命令。脚本通过 <c>map</c> 对象直接操作地图数据，
    /// 新增能力只需扩展 IMapScriptApi，不必再设计命令行语法。
    /// </summary>
    private void RegisterScriptCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("lua", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.LuaUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.LuaUsage.Line2"));
                _debugConsole.WriteLine("    map.AddBuilding(10, 20, 11, \"北京\")");
                _debugConsole.WriteLine("    map.RemoveBuildingsByProbability(30, true)");
                _debugConsole.WriteLine("    map.ForEachBuilding(function(b) print(b.Col, b.Row, b.Name) end)");
                return;
            }

            string path = args[0];
            if (!System.IO.Path.IsPathRooted(path))
                path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

            var runner = new Core.Scripting.ScriptRunner(_mapData, _editModeManager, CreateLuaUiApi());
            var result = runner.RunFile(path);

            foreach (var line in result.Output)
                _debugConsole.WriteLine(line);

            if (result.Success)
                _debugConsole.WriteLine(
                    Loc.F("Render.Console.LuaDone", result.Output.Count, result.AffectedCount, result.Elapsed.TotalMilliseconds));
            else
                _debugConsole.WriteLine(Loc.F("Render.Console.LuaFailed", result.ErrorLocation, result.Error));

            // 脚本可能批量改过建筑，坐标索引需要重建后再刷新画面
            _mapData.RebuildBuildingCoordIndex();
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
        }, "执行 Lua 脚本", "<脚本路径.lua>");
    }

    /// <summary>
    /// 注册文件操作 CLI 命令：open &lt;路径&gt; 加载 .btl/.bin/.dat 并切换到对应编辑场景。
    /// </summary>
    private void RegisterFileCliCommands()
    {
        _commandManager.RegisterCommand("open", args =>
        {
            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.OpenUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.OpenUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.OpenUsage.Line3"));
                return;
            }

            string rawPath = args[0].Replace('\\', '/');
            string? resolved = ResolveMapFilePath(rawPath);
            if (resolved == null)
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.FileNotFound", rawPath));
                _debugConsole.WriteLine(Loc.T("Render.Console.FileSearchedIn"));
                return;
            }

            var ext = System.IO.Path.GetExtension(resolved).ToLowerInvariant();
            if (ext != ".btl" && ext != ".bin" && ext != ".dat")
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.UnsupportedFileType", ext));
                return;
            }

            // .btl 按文件头区分战役 / 征服（地图编号大于 0 即征服布局），不看文件名
            RenderSceneBase newScene = Window.CreateRenderSceneForFile(resolved);

            _debugConsole.WriteLine(Loc.F("Render.Console.Loading", resolved));
            Window.SetCurrentScene(newScene);
        }, "打开地图文件并切换到对应编辑场景", "<文件路径>");

        _commandManager.RegisterCommand("new_campaign", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.NewCampaignUsage"));
                return;
            }
            int legions = args.Length >= 3 && int.TryParse(args[2], out int l) ? l : 2;
            var parser = new Core.Parsers.Stage.StageParser();
            if (!parser.CreateNew(w, h, legions))
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.CreateCampaignFailed", w, h, legions));
                return;
            }
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewCampaign_{w}x{h}_{DateTime.Now:yyyyMMddHHmmss}.btl");
            if (!parser.SaveData(tempPath))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.CampaignTempSaveFailed") + (parser.LastError != null ? ": " + parser.LastError : ""));
                return;
            }
            _debugConsole.WriteLine(Loc.F("Render.Console.CampaignCreated", w, h, legions, tempPath));
            Window.SetCurrentScene(new StageRenderScene(Window, tempPath));
        }, "创建新战役并打开编辑", "<宽度> <高度> [军团数]");

        _commandManager.RegisterCommand("new_conquest", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.NewConquestUsage"));
                return;
            }
            int legions = args.Length >= 3 && int.TryParse(args[2], out int l) ? l : 2;
            int mapNum = args.Length >= 4 && int.TryParse(args[3], out int mn) ? mn : 1;
            var parser = new Core.Parsers.Conquest.ConquestParser();
            if (!parser.CreateNew(w, h, legions, mapNum))
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.CreateConquestFailed", w, h, legions, mapNum));
                return;
            }
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewConquest_{w}x{h}_{mapNum}_{DateTime.Now:yyyyMMddHHmmss}.btl");
            if (!parser.SaveData(tempPath))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ConquestTempSaveFailed") + (parser.LastError != null ? ": " + parser.LastError : ""));
                return;
            }
            _debugConsole.WriteLine(Loc.F("Render.Console.ConquestCreated", w, h, legions, mapNum, tempPath));
            Window.SetCurrentScene(new ConquestRenderScene(Window, tempPath));
        }, "创建新征服并打开编辑", "<宽度> <高度> [军团数] [地图编号]");

        _commandManager.RegisterCommand("new_map", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out int w) || !int.TryParse(args[1], out int h))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.NewMapUsage"));
                return;
            }
            if (w < 10 || w > 500 || h < 10 || h > 500)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.MapSizeRange"));
                return;
            }
            var mapData = Core.Parsers.World.WorldParser.CreateNew(w, h);
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"NewMap_{w}x{h}_{DateTime.Now:yyyyMMddHHmmss}.bin");
            Core.Parsers.World.WorldParser.SaveToFile(mapData, tempPath);
            _debugConsole.WriteLine(Loc.F("Render.Console.MapCreated", w, h, tempPath));
            Window.SetCurrentScene(new MapRenderScene(Window, tempPath));
        }, "创建新世界地图并打开编辑", "<宽度> <高度>");
    }

    /// <summary>按优先级解析地图文件路径：原路径 → assets 根 → 当前目录 → 程序目录。</summary>
    private string? ResolveMapFilePath(string rawPath)
    {
        if (System.IO.File.Exists(rawPath)) return rawPath;

        try
        {
            string assetsRoot = Core.Assets.AssetManager.Default.AssetsRoot;
            if (!string.IsNullOrEmpty(assetsRoot))
            {
                string p = System.IO.Path.Combine(assetsRoot, rawPath);
                if (System.IO.File.Exists(p)) return p;
            }
        }
        catch { }

        string byCwd = System.IO.Path.Combine(Directory.GetCurrentDirectory(), rawPath);
        if (System.IO.File.Exists(byCwd)) return byCwd;

        string byBase = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);
        if (System.IO.File.Exists(byBase)) return byBase;

        return null;
    }

    /// <summary>
    /// 把本渲染场景的数据与执行能力桥接给 AI 子模块。
    /// 场景切换后新场景的初始化会覆盖旧注册，保证 AI 始终操作当前地图。
    /// </summary>
    private void RegisterAiBridge()
    {
        WC4MapEditor.AI.Tools.AiBridge.UiMarshal ??= a => Dispatcher.Invoke(a);
        WC4MapEditor.AI.Tools.AiBridge.MapDataProvider = () => _mapData;
        // AI 视觉：截图入口统一在主窗口注册（走 EditModeManager 分发，由它按 request.SceneMode
        // 选地图截图还是场景截图）。这里不再覆盖 —— 否则一旦切到非地图场景（如开始界面），
        // 场景截图就会被这个"只认地图"的实现挡住。
        WC4MapEditor.AI.Tools.AiBridge.LuaCodeRunner = code =>
        {
            if (_mapData == null) return null;
            var result = new Core.Scripting.ScriptRunner(_mapData, _editModeManager, CreateLuaUiApi())
                .RunCode(code, "ai-inline");
            if (result != null && result.Success)
            {
                // 与 lua 命令一致的刷新：重建坐标索引 + 触发数据变更 + 重绘
                _mapData.RebuildBuildingCoordIndex();
                _editModeManager_DataModifiedFromUndo();
                _skElement.InvalidateVisual();
            }
            return result;
        };
        WC4MapEditor.AI.Tools.AiBridge.CommandRunner = cmd =>
        {
            var sw = new StringWriter();
            bool ok;
            if (_commandManager.HasCommand(cmd))
            {
                var ctx = _commandManager.GetContext();
                var old = ctx?.Output;
                if (ctx != null) ctx.Output = sw;
                try { ok = _commandManager.ExecuteCommand(cmd).Success; }
                finally { if (ctx != null) ctx.Output = old; }
            }
            else
            {
                var host = _debugConsole.CommandHost;
                host.SetOutput(sw);
                try { ok = host.Execute(cmd) == 0; }
                finally { host.SetOutput(new Core.Commands.DebugConsoleWriter(_debugConsole)); }
            }
            return (ok, sw.ToString());
        };
        WC4MapEditor.AI.Tools.AiBridge.DataMutated = msg =>
        {
            _debugConsole.WriteLine($"[AI] {msg}");
        };
    }

    private LuaUiApi CreateLuaUiApi()
    {
        return new LuaUiApi(
            saveFileDialog: (title, filter, defaultFileName) =>
            {
                string? selectedPath = null;
                Window.Dispatcher.Invoke(() =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = string.IsNullOrEmpty(title) ? Loc.T("Render.File.SaveFileTitle") : title,
                        Filter = string.IsNullOrEmpty(filter) ? $"{Loc.T("Common.AllFiles")}|*.*" : filter,
                        FileName = defaultFileName ?? ""
                    };
                    if (dlg.ShowDialog() == true)
                        selectedPath = dlg.FileName;
                });
                return selectedPath;
            },
            log: msg => _debugConsole.WriteLine(msg));
    }

    private void DispatchEditModeAction(string action)
    {
        Debug.WriteLine($"[EditMode] DispatchEditModeAction: action={action}, mode={_editModeManager.CurrentMode}");

        // 开关类按键长按时不重复触发（以前按住 Tab 会让格子信息窗口反复开关）；撤销 / 重做允许按住连续执行
        if (_keyboardManager.LastKeyDownWasRepeat && action != "undo" && action != "redo") return;

        switch (action)
        {
            case "tab_action":
                OnToggleHexInfo();
                break;
            case "cycle_mode":
                CycleEditMode(1);
                break;
            case "toggle_show_layer2":
                _renderEngine.ShowLayer2 = !_renderEngine.ShowLayer2;
                _skElement.InvalidateVisual();
                break;
            case "toggle_help_text":
                ToggleHelpOverlay();
                break;
            case "toggle_hex_borders":
                _renderEngine.ShowHexBorders = !_renderEngine.ShowHexBorders;
                _skElement.InvalidateVisual();
                break;
            case "toggle_labels":
                _renderEngine.CycleLabelMode();
                _skElement.InvalidateVisual();
                break;
            // 撤销 / 重做不需要目标格：以前要先解析焦点格，没选格子且鼠标不在地图上时什么也不做，否则顺手选中鼠标下的格子
            case "undo":
                PerformUndo();
                break;
            case "redo":
                PerformRedo();
                break;
        }
    }

    // ==================== 通用编辑窗口（模式通过 EditorKinds 请求） ====================

    private Services.AssistEditorHost? _assistEditors;

    private Services.AssistEditorContext? CreateAssistEditorContext(EditorRequest request)
    {
        if (_mapData == null) return null;
        return new Services.AssistEditorContext
        {
            MapData = _mapData,
            Editor = _editModeManager,
            Request = request,
            Dialogs = new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!),
            DataChanged = OnAssistEditorDataChanged,
            MoveCamera = (col, row) => MoveCameraToHex(col, row),
        };
    }

    /// <summary>编辑窗口改了数据：按范围失效缓存，然后走与模式修改相同的刷新路径。</summary>
    private void OnAssistEditorDataChanged(Services.EditorDataScope scope)
    {
        if (_mapData != null) _mapData.IsModified = true;
        QueueHexInfoRefresh();
        if ((scope & (Services.EditorDataScope.Legions | Services.EditorDataScope.Belongs)) != 0)
        {
            _renderEngine.InvalidateLegionDomainCache();
            _renderEngine.InvalidateBelongFlagCache();
        }
        _renderEngine.InvalidateViewLayerCache();
        if ((scope & (Services.EditorDataScope.Terrain | Services.EditorDataScope.Provinces | Services.EditorDataScope.Legions)) != 0)
        {
            OnEditModeDataModified(this, EventArgs.Empty);
            return;
        }
        _skElement.InvalidateVisual();
    }

    private void OnEditModeChanged(object? sender, EditModeChangedEventArgs e)
    {
        // 编辑窗口跟着模式走：离开请求它的模式时关闭（窗口可通过 IAssistEditor.KeepOpenOnModeChange 选择保留）
        _assistEditors?.OnModeChanged(e.CurrentMode);

        if (_brushSettingsWindow != null)
        {
            _brushSettingsWindow.PlayFadeOutAndHide();
            _brushSettingsWindow = null;
        }
        DeactivateBrushOfMode(e.PreviousMode);

        // 注：早先这里有一个「离开军团编辑模式时收起首都编辑模式」的分支，
        // 但首都编辑现在已经是 LegionEditMode 通过 ModifierKinds 引入的**无状态模块**
        // （CapitalModifier），切模式时没有残留状态需要清理，分支已被移除。
        //
        // ⚠ 曾经留下的坑：那个分支删掉语句体后只剩一个没有花括号的 `if (...)`，
        // 于是紧随其后的 UpdateOverlayFromEditMode() 被当成了它的语句体 ——
        // 条件在「切进军团模式」时恰好为假，导致标题与按键说明永远不刷新。
        UpdateOverlayFromEditMode();
        UpdateLayerInfoVisibility();
        UpdateRenderLayersByMode(e.CurrentMode);
        CarryPrimaryAcrossModeSwitch();
        ShowHexInfoTarget();

        // 切出省份模式时要撤掉红色边界层，否则会残留在其它模式上
        RefreshBoundaryOverlay();

        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 按当前模式打开 / 关闭渲染层。每个模式在 Core 里声明自己要看到的层（<see cref="IModeHandler.RenderLayers"/>），
    /// 这里只负责把声明翻译成渲染引擎的开关，新增模式不必再改这里。
    /// </summary>
    private void UpdateRenderLayersByMode(EditMode mode)
    {
        var layers = mode == EditMode.None
            ? ModeRenderLayers.None
            : _editModeManager.GetModeHandler(mode)?.RenderLayers ?? ModeRenderLayers.None;
        bool Has(ModeRenderLayers layer) => (layers & layer) != 0;

        _renderEngine.EnableProvinceRender = Has(ModeRenderLayers.Province);
        _renderEngine.EnableProvinceCapitalRender = Has(ModeRenderLayers.ProvinceCapital);
        _renderEngine.EnableCapitalFlagRender = Has(ModeRenderLayers.CapitalFlag);
        _renderEngine.EnableBuildingRender = Has(ModeRenderLayers.Building);
        // 名称显示 = 全局开关（建筑编辑模式 U 键，跨模式保持） 且 模式没有要求隐藏（地形绘制模式始终不显示名称）
        _renderEngine.ShowBuildingNames = _buildingNamesVisible && !Has(ModeRenderLayers.HideBuildingNames);
        _renderEngine.EnableArmyRender = Has(ModeRenderLayers.Army);
        _renderEngine.EnableTrapRender = Has(ModeRenderLayers.Trap);
        _renderEngine.EnableLegionDomainRender = Has(ModeRenderLayers.LegionDomain);

        bool needsBelongFlag = Has(ModeRenderLayers.BelongFlag);
        _renderEngine.EnableBelongFlagRender = needsBelongFlag;
        if (needsBelongFlag && _mapData != null)
        {
            _renderEngine.PreloadBelongFlagAtlas(_mapData);
        }

        _renderEngine.EnableReinforceNewRender = Has(ModeRenderLayers.Reinforcement);
        _renderEngine.EnableStrategicConstructionRender = Has(ModeRenderLayers.StrategicConstruction);
        _renderEngine.EnableAirForceRender = Has(ModeRenderLayers.AirForce);
        _renderEngine.EnableWeatherRender = Has(ModeRenderLayers.Weather);
        _renderEngine.EnableMapCaseRender = Has(ModeRenderLayers.MapCase);
        _renderEngine.EnableClippedTileRender = Has(ModeRenderLayers.ClippedTiles);

        _renderEngine.EnableSelectionRender = _editModeManager.IsSelectionActive;

        _renderEngine.EnableTerrainsRender = true;
        _renderEngine.EnableBackgroundRender = true;
    }

    private void UpdateLayerInfoVisibility()
    {
        if (_layerInfoLabel == null) return;

        if (_editModeManager.CurrentMode == EditMode.TerrainPaint)
        {
            // 获取当前编辑层
            var terrainModifier = _editModeManager.GetModifier<TerrainModifier>();
            if (terrainModifier != null)
            {
                _layerInfoLabel.Text = Loc.F("Render.EditLayer", terrainModifier.EditLayer);
                _layerInfoLabel.Visibility = Visibility.Visible;
            }
        }
        else
        {
            _layerInfoLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnEditModeStatusMessageChanged(object? sender, string message)
    {
        Debug.WriteLine($"[EditMode] {message}");

        PostSceneStatus(message);

        // 更新地形编辑层显示：不再按消息文本前缀判断（文本已本地化，且由 Core 发出），
        // 改为直接从修改器读取当前编辑层重建标签，显示结果与原来一致。
        if (_layerInfoLabel != null && _editModeManager.CurrentMode == EditMode.TerrainPaint)
            UpdateLayerInfoVisibility();
    }

    private void OnEditModeDataModified(object? sender, EventArgs e)
    {
        _renderEngine.InvalidateTerrainCache();
        // 军团领域层按归属值缓存了国家颜色，军团配色一改这份缓存就是旧的，必须清掉
        // （C 键 / U 键批量改色、以及任何改动军团颜色的操作都走这里）
        _renderEngine.InvalidateLegionDomainCache();
        if (_mapData != null)
            _renderEngine.InvalidateCoastCacheFull(_mapData);
        QueueHexInfoRefresh();
        _skElement.InvalidateVisual();
    }

    private void _editModeManager_DataModifiedFromUndo()
    {
        OnEditModeDataModified(this, EventArgs.Empty);
    }

    private void UpdateOverlayFromEditMode()
    {
        // 帮助 = 模式帮助 + 模式管理器的页脚（空格 / 数字键 / Esc）+ 场景按键一览
        _renderEngine.HelpText = _editModeManager.GetFullHelpText() + "\n" + BuildSceneKeyHelp();
        _renderEngine.ModeName = _editModeManager.GetModeStatusText();
        // 切换模式时不再强制显示帮助：用户用 Ctrl+F1 关掉了就保持关闭
        ApplyHelpVisibility();
        _renderEngine.ShowModeName = _editModeManager.IsEditModeActive;
    }

    /// <summary>
    /// 切换国家领域（归属着色 + 归属国旗）显示，对齐 VB 归属模式的 L 键。
    /// </summary>
    private void OnDomainToggled(object? sender, EventArgs e)
    {
        bool enable = !_renderEngine.EnableLegionDomainRender;
        _renderEngine.EnableLegionDomainRender = enable;
        _renderEngine.EnableBelongFlagRender = enable;

        if (enable && _mapData != null)
            _renderEngine.PreloadBelongFlagAtlas(_mapData);

        _editModeManager.RaiseStatusMessage(enable ? Loc.T("Render.Status.DomainOn") : Loc.T("Render.Status.DomainOff"));
        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 切换建筑名称显示，对应建筑编辑模式的 U 键。
    /// </summary>
    private void OnBuildingNamesToggled(object? sender, EventArgs e)
    {
        // 全局开关：关闭后切换到任何模式都不再显示建筑名称
        _buildingNamesVisible = !_buildingNamesVisible;
        bool modeHidesNames = ((_editModeManager.CurrentHandler?.RenderLayers ?? ModeRenderLayers.None) & ModeRenderLayers.HideBuildingNames) != 0;
        _renderEngine.ShowBuildingNames = _buildingNamesVisible && !modeHidesNames;

        _editModeManager.RaiseStatusMessage(_buildingNamesVisible ? Loc.T("Render.Status.BuildingNamesShown") : Loc.T("Render.Status.BuildingNamesHidden"));
        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 建筑编辑模式 P 键：文字识别生成建筑。
    /// 流程：识别【当前已加载的视图层背景图】 → OCR 识别地名 → 导出当前地图网格
    /// （地形编辑模式 F10 的能力）→ 生成 zme 建筑脚本 → 批量放置建筑。
    /// <para>
    /// 与省份编辑模式的 P 键保持一致：直接用已经加载好的背景图，不再弹窗选图 ——
    /// 视图层显示的是哪张图，就识别哪张，用户不必再多选一次。
    /// </para>
    /// </summary>
    private async void OnRecognizeTextBuildingsRequested(object? sender, EventArgs e)
    {
        if (_mapData == null)
        {
            MessageBox.Show(Loc.T("Render.MapNotLoaded"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!Core.Services.ExternalToolRunner.IsOcrAvailable ||
            !Core.Services.ExternalToolRunner.IsGenBuildingsAvailable)
        {
            MessageBox.Show(
                Loc.T("Render.Ocr.MissingTools"),
                Loc.T("Render.Ocr.MissingToolsTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 1. 直接取已加载的视图层背景图（与省份模式 P 键同样的前置条件）
        string imagePath = _renderEngine.ViewLayerImagePath;
        if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
        {
            MessageBox.Show(
                Loc.T("Render.Ocr.NoBackground"),
                Loc.T("Render.Ocr.NoBackgroundTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 2. 源图像素尺寸（比例映射的依据，必须是磁盘上原图的像素尺寸，而不是地图格数）。
        //    尺寸取自引擎加载时的记录，避免在这里再解码一次大图。
        int imageWidth = _renderEngine.ViewLayerSourceWidth;
        int imageHeight = _renderEngine.ViewLayerSourceHeight;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            MessageBox.Show(Loc.T("Render.Ocr.NoImageSize"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 3. 选择建筑类型
        var dialogService = new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!);
        var typeInput = await dialogService.ShowInputDialogAsync(
            Loc.T("Render.Ocr.Title"),
            Loc.F("Render.Ocr.TypePrompt", imageWidth, imageHeight),
            "11", 11, 15);
        if (typeInput == null || !int.TryParse(typeInput, out int buildingType)) return;

        // 4. 中间产物与图片同目录
        string workDir = System.IO.Path.GetDirectoryName(imagePath) ?? AppContext.BaseDirectory;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(imagePath);
        string placesJson = System.IO.Path.Combine(workDir, baseName + "_places.json");
        string mapJson = System.IO.Path.Combine(workDir, baseName + "_map.json");
        string zmePath = System.IO.Path.Combine(workDir, baseName + "_buildings.zme");

        try
        {
            // 5. OCR 识别（大图可能需要数分钟）
            _editModeManager.RaiseStatusMessage(Loc.F("Render.Ocr.Recognizing", System.IO.Path.GetFileName(imagePath)));
            var ocr = await Core.Services.ExternalToolRunner.RunOcrAsync(imagePath, placesJson);
            if (!ocr.Ok)
            {
                MessageBox.Show(ocr.Message, Loc.T("Render.Ocr.FailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                _editModeManager.RaiseStatusMessage(Loc.T("Render.Ocr.FailedTitle"));
                return;
            }

            // 6. 导出当前地图网格数据（复用地形编辑模式 F10 的实现）
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Ocr.ExportingGrid"));
            _geoCalculator.ExportGridOnly(_mapData, mapJson);

            // 7. 按比例映射生成 zme 建筑脚本
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Ocr.GeneratingScript"));
            var gen = await Core.Services.ExternalToolRunner.RunGenBuildingsAsync(
                mapJson, placesJson, imageWidth, imageHeight, zmePath, buildingType);
            if (!gen.Ok)
            {
                MessageBox.Show(gen.Message, Loc.T("Render.Ocr.GenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                _editModeManager.RaiseStatusMessage(Loc.T("Render.Ocr.GenFailed"));
                return;
            }

            // 8. 执行 zme，批量放置建筑
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Ocr.Placing"));
            _debugConsole.CommandHost.Execute($"run \"{zmePath}\"");

            _skElement.InvalidateVisual();
            _editModeManager.RaiseStatusMessage(Loc.F("Render.Ocr.Done", buildingType));
            MessageBox.Show(
                Loc.F("Render.Ocr.DoneDetail",
                    System.IO.Path.GetFileName(placesJson),
                    System.IO.Path.GetFileName(mapJson),
                    System.IO.Path.GetFileName(zmePath),
                    gen.Message),
                Loc.T("Render.Ocr.DoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Ocr.Failed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBrushToggled(object? sender, EventArgs e)
    {
        if (_editModeManager.CurrentMode == EditMode.ProvinceEdit)
        {
            var province = _editModeManager.GetModifier<ProvinceModifier>();
            if (province == null) return;

            province.Brush.Active = !province.Brush.Active;

            if (province.Brush.Active)
            {
                if (_brushSettingsWindow == null)
                {
                    _brushSettingsWindow = new BrushSettingsWindow { IsTerrainMode = false };
                    _brushSettingsWindow.BrushSettingsChanged += OnBrushSettingsChanged;
                    _brushSettingsWindow.BrushSize = province.Brush.Radius;
                    _brushSettingsWindow.BrushShape = province.Brush.Shape;
                    _brushSettingsWindow.InitializeMaskTerrainList();

                    var mainWindow = Window;
                    if (mainWindow != null)
                    {
                        _brushSettingsWindow.Owner = mainWindow;
                        double left = mainWindow.Left + mainWindow.ActualWidth - _brushSettingsWindow.Width - 20;
                        double top = mainWindow.Top + 80;
                        _brushSettingsWindow.Left = left;
                        _brushSettingsWindow.Top = top;
                    }
                }
                _brushSettingsWindow.Show();
                _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.BrushOnProvince", province.Brush.Radius));
            }
            else
            {
                if (_brushSettingsWindow != null)
                    _brushSettingsWindow.PlayFadeOutAndHide();
                _renderEngine.HideBrushPreview();
                _skElement.InvalidateVisual();
                _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOff"));
            }
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.BelongEdit)
        {
            var belong = _editModeManager.GetModifier<BelongModifier>();
            if (belong == null) return;

            var brush = belong.Brush;
            brush.Active = !brush.Active;

            if (brush.Active)
            {
                if (_brushSettingsWindow == null)
                {
                    _brushSettingsWindow = new BrushSettingsWindow { IsTerrainMode = false };
                    _brushSettingsWindow.BrushSettingsChanged += OnBrushSettingsChanged;
                    _brushSettingsWindow.BrushSize = brush.Radius;
                    _brushSettingsWindow.BrushShape = brush.Shape;
                    _brushSettingsWindow.InitializeMaskTerrainList();

                    var mainWindow = Window;
                    if (mainWindow != null)
                    {
                        _brushSettingsWindow.Owner = mainWindow;
                        double left = mainWindow.Left + mainWindow.ActualWidth - _brushSettingsWindow.Width - 20;
                        double top = mainWindow.Top + 80;
                        _brushSettingsWindow.Left = left;
                        _brushSettingsWindow.Top = top;
                    }
                }
                _brushSettingsWindow.Show();
                _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.BrushOnBelong", brush.Radius));
            }
            else
            {
                if (_brushSettingsWindow != null)
                    _brushSettingsWindow.PlayFadeOutAndHide();
                _renderEngine.HideBrushPreview();
                _skElement.InvalidateVisual();
                _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOff"));
            }
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.ArmyDeploy)
        {
            var army = _editModeManager.GetModifier<ArmyModifier>();
            if (army == null) return;

            var brush = army.Brush;
            brush.Toggle();

            if (brush.Active)
            {
                if (_brushSettingsWindow == null)
                {
                    _brushSettingsWindow = new BrushSettingsWindow { IsTerrainMode = false };
                    _brushSettingsWindow.BrushSettingsChanged += OnBrushSettingsChanged;
                    _brushSettingsWindow.BrushSize = brush.Radius;
                    _brushSettingsWindow.BrushShape = brush.Shape;

                    var mainWindow = Window;
                    if (mainWindow != null)
                    {
                        _brushSettingsWindow.Owner = mainWindow;
                        double left = mainWindow.Left + mainWindow.ActualWidth - _brushSettingsWindow.Width - 20;
                        double top = mainWindow.Top + 80;
                        _brushSettingsWindow.Left = left;
                        _brushSettingsWindow.Top = top;
                    }
                }
                _brushSettingsWindow.Show();
                _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.EraserOn", brush.Radius));
            }
            else
            {
                if (_brushSettingsWindow != null)
                    _brushSettingsWindow.PlayFadeOutAndHide();
                _renderEngine.HideBrushPreview();
                _skElement.InvalidateVisual();
                _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.EraserOff"));
            }
            return;
        }

        ToggleTerrainBrush();
    }

    private void OnBrushSettingsChanged(object? sender, BrushSettingsEventArgs e)
    {
        if (_editModeManager.CurrentMode == EditMode.ProvinceEdit)
        {
            var province = _editModeManager.GetModifier<ProvinceModifier>();
            if (province == null) return;
            province.Brush.Radius = e.BrushSize;
            province.Brush.Shape = e.BrushShape;
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.BelongEdit)
        {
            var belong = _editModeManager.GetModifier<BelongModifier>();
            if (belong == null) return;
            belong.Brush.Radius = e.BrushSize;
            belong.Brush.Shape = e.BrushShape;
            return;
        }

        if (_editModeManager.CurrentMode == EditMode.ArmyDeploy)
        {
            var army = _editModeManager.GetModifier<ArmyModifier>();
            if (army == null) return;
            army.Brush.Radius = e.BrushSize;
            army.Brush.Shape = e.BrushShape;
            return;
        }

        ApplyTerrainBrushSettings(e);
    }

    private void OnBrushSizeChanged(object? sender, EventArgs e)
    {
        // 预览圈跟着鼠标走；不用 GetFocusHex —— 它在没有选区时会顺手把鼠标下的格子选中
        var (col, row) = HoveredHex() ?? (-1, -1);

        if (_editModeManager.CurrentMode == EditMode.TerrainPaint)
        {
            RefreshTerrainBrushSize(col, row);
        }
        else if (_editModeManager.CurrentMode == EditMode.ProvinceEdit)
        {
            var province = _editModeManager.GetModifier<ProvinceModifier>();
            if (province == null) return;

            if (_brushSettingsWindow != null && _brushSettingsWindow.IsVisible)
                _brushSettingsWindow.BrushSize = province.Brush.Radius;

            if (province.IsBrushMode)
            {
                _renderEngine.SetBrushPreview(
                    col, row,
                    province.Brush.Radius, province.Brush.Shape,
                    _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
                    true);
                _skElement.InvalidateVisual();
            }
        }
        else if (_editModeManager.CurrentMode == EditMode.BelongEdit)
        {
            var belong = _editModeManager.GetModifier<BelongModifier>();
            if (belong == null) return;

            if (_brushSettingsWindow != null && _brushSettingsWindow.IsVisible)
                _brushSettingsWindow.BrushSize = belong.Brush.Radius;

            if (belong.Brush.Active)
            {
                _renderEngine.SetBrushPreview(
                    col, row,
                    belong.Brush.Radius, belong.Brush.Shape,
                    _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
                    true);
                _skElement.InvalidateVisual();
            }
        }
        else if (_editModeManager.CurrentMode == EditMode.ArmyDeploy)
        {
            var army = _editModeManager.GetModifier<ArmyModifier>();
            if (army == null) return;

            if (_brushSettingsWindow != null && _brushSettingsWindow.IsVisible)
                _brushSettingsWindow.BrushSize = army.Brush.Radius;

            if (army.Brush.Active)
            {
                _renderEngine.SetBrushPreview(
                    col, row,
                    army.Brush.Radius, army.Brush.Shape,
                    _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
                    true);
                _skElement.InvalidateVisual();
            }
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mouseManager.MouseAction -= OnMouseAction;
        _hexSelector.SelectionChanged -= OnHexSelectionChanged;
        _hexSelector.SelectionRectChanged -= OnSelectionRectChanged;
        _hexSelector.CancelSelectionRect();
        _hexSelector.ClearSelection();
        _keyboardManager.UnregisterBinding("EscBack");
        _keyboardManager.UnregisterBinding("Screenshot");
        UnregisterEditModeKeyBindings();
        DetachSceneUx();
        _editModeManager.ModeChanged -= OnEditModeChanged;
        _editModeManager.StatusMessageChanged -= OnEditModeStatusMessageChanged;
        _editModeManager.DataModified -= OnEditModeDataModified;
        _editModeManager.BrushToggled -= OnBrushToggled;
        // EditModeManager 是跨场景的单例：漏掉的订阅会让已销毁的场景继续收到事件
        // （例如回到主界面再进入地图后，按 Q 会同时在旧场景和新场景各开一次窗口）。
        _editModeManager.BrushSizeChanged -= OnBrushSizeChanged;
        _editModeManager.BoundaryDrawToggled -= OnBoundaryDrawToggled;
        _editModeManager.DomainToggled -= OnDomainToggled;
        _editModeManager.BuildingNamesToggled -= OnBuildingNamesToggled;
        _editModeManager.CaptureLegionScreenshotRequested -= OnCaptureLegionScreenshotRequested;
        _editModeManager.MoveCameraToHexRequested -= OnMoveCameraToHexRequested;
        _editModeManager.BuildingMoveToolToggled -= OnBuildingMoveToolToggled;
        _editModeManager.RecognizeTextBuildingsRequested -= OnRecognizeTextBuildingsRequested;
        if (_assistEditors != null && _editModeManager.EditorHost == (Func<EditorRequest, bool>)_assistEditors.Open)
            _editModeManager.EditorHost = null;
        _assistEditors?.CloseAll();

        // 场景销毁时关闭随本场景打开的子窗口，避免切换场景后残留
        try
        {
            _hexInfoWindow?.Close();
            _brushSettingsWindow?.Hide();
        }
        catch { }
        _hexInfoWindow = null;
        _brushSettingsWindow = null;
        if (_debugConsole.IsVisible) _debugConsole.HideConsole();

        _renderEngine?.Dispose();
    }
}