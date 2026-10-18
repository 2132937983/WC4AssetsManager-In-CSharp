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

public abstract class RenderSceneBase : UserControl, IDisposable
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
            ".btl" => LoadBtlByFileName(filePath),
            _ => null
        };
    }

    protected static bool SaveMapDataByFileType(MapData mapData, string filePath)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".bin":
                case ".dat":
                    WorldParser.SaveToFile(mapData, filePath);
                    return true;
                case ".btl":
                    var name = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
                    if (name.StartsWith("conquest"))
                        return ConquestParser.SaveFromMapData(mapData, filePath);
                    return StageParser.SaveFromMapData(mapData, filePath);
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            // 关键：不要在这里把异常吞掉！调用方（OnQuickSave / 保存对话框）靠捕获异常
            // 才能把"文件被 Excel 等程序锁定 / 无写权限 / 磁盘空间不足"等真实原因展示出来。
            // 之前 return false 导致外层 catch 永远不触发，界面只显示无信息的"保存失败！"，无从排查。
            System.Diagnostics.Trace.WriteLine($"[RenderSceneBase] 保存失败: {filePath}{Environment.NewLine}" +
                                               $"  {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private static MapData? LoadBtlByFileName(string filePath)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        if (name.StartsWith("conquest"))
            return ConquestParser.LoadToMapData(filePath);
        return StageParser.LoadToMapData(filePath);
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
        closeButton.Click += (s, e) => Window.CloseApplication();
        rightPanel.Children.Add(closeButton);

        titleBar.Children.Add(titlePanel);
        titleBar.Children.Add(rightPanel);
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
            Margin = new Thickness(2, 0, 2, 0)
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

        _mapData = LoadMapData();
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
        // 【临时诊断】省份 P 键识别时导出灰度图与界线掩码，便于人工核对识别效果。
        // 挂钩在 modifier 实例上，而 EditModeManager.Initialize 只转调 modifier.Initialize、
        // 不会重建实例，所以这里设一次即可（新建地图后依然有效）。
        // 不需要时：删掉这一行 + ExportProvinceDebugImages / SaveGrayPng 两个方法。
        _editModeManager.GetModifier<ProvinceModifier>()?.DebugSink = ExportProvinceDebugImages;
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
        _editModeManager.OpenLegionSettingRequested += OnOpenLegionSettingRequested;
        _editModeManager.OpenLegionListRequested += OnOpenLegionListRequested;
        _editModeManager.OpenHeaderSettingRequested += OnOpenHeaderSettingRequested;
        _editModeManager.UpdateConquerSettingsRequested += OnUpdateConquerSettingsRequested;
        _editModeManager.MoveCameraToHexRequested += OnMoveCameraToHexRequested;
        _editModeManager.BuildingMoveToolToggled += OnBuildingMoveToolToggled;
        _editModeManager.RecognizeTextBuildingsRequested += OnRecognizeTextBuildingsRequested;

        _fileStateManager.OpenFile(_mapData, _mapData.FilePath, SceneType);
        _editModeManager.SetSceneType(SceneType);

        var availableModes = _editModeManager.GetAvailableModes(SceneType);
        if (availableModes.Count > 0)
            _editModeManager.SwitchMode(availableModes[0]);

        UpdateOverlayFromEditMode();

        _keyboardManager.KeyDown += OnKeyboardManagerKeyDown;

        // 注册错误恢复策略
        ErrorCollector.Instance.RegisterStrategy(new ModeSwitchRecoveryStrategy(_editModeManager));
        ErrorCollector.Instance.RegisterStrategy(new KeyboardRecoveryStrategy(_keyboardManager));
        ErrorCollector.Instance.ErrorOccurred += (s, e) =>
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.Error", e.Severity, e.Message));
        };
        ErrorCollector.Instance.ErrorRecovered += (s, e) =>
        {
            if (e.Recovered)
                _debugConsole.WriteLine(Loc.F("Render.Console.Recovered", e.RecoveryAction));
        };

        _debugConsole.VisibilityChanged += isVisible =>
        {
            // 控制台已改成独立弹窗，不再盖住主窗口，所以主窗口标题栏保持可见；
            // 只把地图上的帮助文本让出来。
            _renderEngine.ShowHelp = !isVisible;
            _skElement.InvalidateVisual();
        };
        _commandManager.SetContext(new CommandContext { MapData = _mapData });
        // 只依赖 ICommandHost 抽象：具体实现由 DebugConsole 决定（默认内置实现，可由 DI 替换）
        var commandHost = _debugConsole.CommandHost;
        commandHost.SetOutput(new Core.Commands.DebugConsoleWriter(_debugConsole));
        commandHost.DataModified += () =>
        {
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
        };
        RegisterAiBridge();
        Core.Config.ConfigManager.StringTableChanged += () =>
        {
            _renderEngine.ReloadBuildingCityNames();
            _skElement.InvalidateVisual();
        };
        _commandManager.RegisterCommand("undo", _ =>
        {
            if (_fileStateManager.Undo()) { _editModeManager_DataModifiedFromUndo(); }
            else { _debugConsole.WriteLine(Loc.T("Render.Console.CannotUndo")); }
        }, "撤销上一步操作");
        _commandManager.RegisterCommand("redo", _ =>
        {
            if (_fileStateManager.Redo()) { _editModeManager_DataModifiedFromUndo(); }
            else { _debugConsole.WriteLine(Loc.T("Render.Console.CannotRedo")); }
        }, "重做上一步操作");
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
        _keyboardManager.UnregisterBinding("QuickSave");
        UnregisterEditModeKeyBindings();
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
        _editModeManager.OpenLegionSettingRequested -= OnOpenLegionSettingRequested;
        _editModeManager.OpenLegionListRequested -= OnOpenLegionListRequested;
        _editModeManager.OpenHeaderSettingRequested -= OnOpenHeaderSettingRequested;
        _editModeManager.UpdateConquerSettingsRequested -= OnUpdateConquerSettingsRequested;
        _editModeManager.MoveCameraToHexRequested -= OnMoveCameraToHexRequested;
        _editModeManager.BuildingMoveToolToggled -= OnBuildingMoveToolToggled;
        _editModeManager.RecognizeTextBuildingsRequested -= OnRecognizeTextBuildingsRequested;
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
        _keyboardManager.ProcessKeyDown((int)key);
        e.Handled = true;
    }

    private void OnSkElementKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
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
        _keyboardManager.ProcessKeyDown((int)key);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keyboardManager.SetModifierState(GetCurrentKeyModifiers());
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

    private void OnQuickSave()
    {
        if (_mapData == null) return;

        // 如果已有文件路径，直接保存；否则弹出保存对话框
        if (!string.IsNullOrEmpty(_mapData.FilePath))
        {
            try
            {
                if (SaveMapDataByFileType(_mapData, _mapData.FilePath))
                {
                    _fileStateManager.MarkSaved();

                    // 已写回原始文件，场景的磁盘缓存就过期了：不失效的话，
                    // 切走再切回来会加载到比原文件更旧的缓存，看起来像"保存没生效"。
                    if (_sceneId >= 0)
                        RenderSceneManager.Instance.InvalidateSceneCache(_sceneId);

                    MessageBox.Show(Loc.F("Render.Save.SavedTo", _mapData.FilePath), Loc.T("Render.Save.SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    _debugConsole.WriteLine(Loc.F("Render.Console.SavedTo", _mapData.FilePath));
                }
                else
                {
                    MessageBox.Show(Loc.T("Render.Save.Failed"), Loc.T("Render.Save.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    _debugConsole.WriteLine(Loc.T("Render.Console.SaveFailed"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("Render.Save.FailedWithReason", ex.Message), Loc.T("Render.Save.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                _debugConsole.WriteLine(Loc.F("Render.Console.SaveFailedWithReason", ex.Message));
            }
        }
        else
        {
            // 没有文件路径，调用保存对话框
            SaveButton_Click(this, new RoutedEventArgs());
        }
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

    private async Task CaptureMapScreenshotAsync()
    {
        try
        {
            if (_mapData == null)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ScreenshotNoMap"));
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
            _debugConsole.WriteLine(Loc.T("Render.Console.ScreenshotStart"));

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

            if (success)
                _debugConsole.WriteLine(Loc.F("Render.Console.ScreenshotSaved", outputPath));
            else
                _debugConsole.WriteLine(Loc.T("Render.Console.ScreenshotFailed"));
        }
        catch (Exception ex)
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.ScreenshotError", ex.Message));
        }
    }

    private Task<ScreenshotMode> ShowScreenshotModeDialogAsync()
    {
        var tcs = new TaskCompletionSource<ScreenshotMode>();

        var parentGrid = Content as Grid;
        if (parentGrid == null)
        {
            tcs.SetResult(ScreenshotMode.Cancel);
            return tcs.Task;
        }

        var overlay = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var dialogPanel = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { ShadowDepth = 4, Opacity = 0.5 }
        };

        var stackPanel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = Loc.T("Render.Screenshot.ModeTitle"),
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        stackPanel.Children.Add(titleBlock);

        var descBlock = new TextBlock
        {
            Text = Loc.T("Render.Screenshot.ModeDesc"),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
        stackPanel.Children.Add(descBlock);

        var standardButton = new Button
        {
            Content = Loc.T("Render.Screenshot.ModeStandard"),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        standardButton.Click += (s, e) =>
        {
            parentGrid.Children.Remove(overlay);
            tcs.TrySetResult(ScreenshotMode.Standard);
        };
        stackPanel.Children.Add(standardButton);

        var hexBasedButton = new Button
        {
            Content = Loc.T("Render.Screenshot.ModeHex"),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        hexBasedButton.Click += (s, e) =>
        {
            parentGrid.Children.Remove(overlay);
            tcs.TrySetResult(ScreenshotMode.HexBased);
        };
        stackPanel.Children.Add(hexBasedButton);

        var cancelButton = new Button
        {
            Content = Loc.T("Common.Cancel"),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelButton.Click += (s, e) =>
        {
            parentGrid.Children.Remove(overlay);
            tcs.TrySetResult(ScreenshotMode.Cancel);
        };
        stackPanel.Children.Add(cancelButton);

        dialogPanel.Child = stackPanel;
        overlay.Child = dialogPanel;
        parentGrid.Children.Add(overlay);

        return tcs.Task;
    }

    /// <summary>
    /// 建筑名称显示开关（建筑编辑模式 U 键触发）。
    /// 全局生效且跨模式保持：关闭后切换到任何模式都不再显示建筑名称。
    /// </summary>
    private bool _buildingNamesVisible = true;

    /// <summary>建筑移动工具是否开启（建筑编辑模式 K 键触发，状态由 GUI 层持有）。</summary>
    private bool _buildingMoveToolActive;

    /// <summary>右键拖拽移动建筑时的源格坐标（-1 表示当前未在拖拽）。</summary>
    private int _dragBuildingSourceCol = -1;
    private int _dragBuildingSourceRow = -1;
    private bool _isDraggingBuilding;

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

    private void OnToggleHexInfo()
    {
        if (_hexInfoWindow != null)
        {
            try
            {
                if (_hexInfoWindow.IsVisible)
                {
                    _hexInfoWindow.PlayFadeOutAndClose();
                    _hexInfoWindow = null;
                    RestoreFocus();
                    return;
                }
            }
            catch { _hexInfoWindow = null; }
        }

        _hexInfoWindow = new HexInfoWindow();
        var mainWindow = Window;
        _hexInfoWindow.Left = mainWindow.Left + 10;
        _hexInfoWindow.Top = mainWindow.Top + 10;
        _hexInfoWindow.SetDisplayMode(HexInfoDisplayMode.Default);

        var primary = _hexSelector.PrimarySelected;
        if (primary.HasValue && _mapData != null)
            _hexInfoWindow.UpdateHexInfo(primary.Value.Col, primary.Value.Row);

        _hexInfoWindow.Show();
        RestoreFocus();
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
        if (_legionSettingWindow != null)
        {
            _legionSettingWindow.Close();
            _legionSettingWindow = null;
        }
        if (_debugConsole.IsVisible)
        {
            _debugConsole.HideConsole();
        }
    }

    private async Task ShowReturnConfirmDialog()
    {
        _confirmDialogShowing = true;

        using var dialog = new ConfirmDialog(Window)
        {
            Title = Loc.T("Render.ReturnConfirmTitle"),
            Message = Loc.T("Render.ReturnConfirmMessage"),
            ConfirmText = Loc.T("Common.Back"),
            CancelText = Loc.T("Common.Cancel")
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
                if (TrySetBelongAtRightClick(e)) break;
                if (TryShowLegionInfoAtRightClick(e)) break;
                HandleRightClickSelect(e);
                break;
        }
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
        if (!_editModeManager.IsSelectionActive) return;
        if (_mapData == null || _camera == null) return;
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;

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

    /// <summary>
    /// 归属编辑模式下右键单击：为光标所在格子设置归属。
    /// 对齐 VB 版 BelongModifier.HandleMouseDown 的右键分支 —— 归属值优先取「复制的归属」，
    /// 未复制过时回退到当前选中的国家ID。
    /// 仅在非画笔模式下生效（画笔模式右键用于绘制），右键拖动仍用于框选多选。
    /// </summary>
    private bool TrySetBelongAtRightClick(MouseActionEventArgs e)
    {
        if (_editModeManager.CurrentMode != EditMode.BelongEdit) return false;
        if (_mapData == null || _camera == null) return false;

        var belong = _editModeManager.GetModifier<BelongModifier>();
        if (belong == null) return false;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        byte countryId = belong.ResolveBrushCountryId();

        _editModeManager.RecordBelongChange(col, row, $"设置归属 ({col},{row})",
            () => belong.SetBelongByCountryId(col, row, countryId));

        _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.BelongSet", col, row, countryId));
        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>
    /// 军团编辑模式下左键单击：把当前选中军团的领域设到该格
    /// （对齐 VB 版 LegionModifier 的"左键点击设置军团"提示，军团由 [ / ] 切换）。
    /// <para>
    /// Shift / Ctrl 是选择器的加选、减选手势，这两种情况下不写数据，
    /// 避免批量选格（比如为 F 键挑首都位置）时误改归属。
    /// </para>
    /// </summary>
    private void TryLegionApplyAtClick(MouseActionEventArgs e)
    {
        if (_editModeManager.CurrentMode != EditMode.LegionEdit) return;
        if (_mapData == null || _camera == null) return;

        if ((e.Modifiers & Core.Input.KeyModifiers.Shift) != 0) return;
        if ((e.Modifiers & Core.Input.KeyModifiers.Ctrl) != 0) return;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;

        // 交给模式层的 apply 动作：撤销记录、状态消息都在那里统一处理
        _ = _editModeManager.HandleKeyAction("apply", col, row);
    }

    /// <summary>
    /// 军团编辑模式下右键查看该格军团信息（对齐 VB 版 LegionModifier.HandleMouseDown 右键分支）。
    /// </summary>
    private bool TryShowLegionInfoAtRightClick(MouseActionEventArgs e)
    {
        if (_editModeManager.CurrentMode != EditMode.LegionEdit) return false;
        if (_mapData == null || _camera == null) return false;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        int belong = _mapData.GetBelongValue(col, row);
        if (belong == 0xFF)
        {
            _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.NoLegionInfo", col, row));
            return true;
        }

        // 归属值约定为军团在 Legions 中的索引（与 LegionDomainRender / BelongFlagRender 一致）；
        // 个别地图可能把 CountryId 写进了归属值，索引非法时回退按 CountryId 再查一次。
        int index = belong;
        if (index < 0 || index >= _mapData.Legions.Count)
            index = _mapData.FindLegionIndex(belong);

        if (index < 0)
        {
            _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.BelongNoLegion", col, row, belong));
            return true;
        }

        var legion = _mapData.Legions[index];
        _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.LegionInfo",
            legion.ActionId, legion.CountryId,
            legion.ColorR, legion.ColorG, legion.ColorB,
            legion.InitialEconomy, legion.Camp));
        return true;
    }

    protected virtual Func<HexCoord, bool>? GetSelectionFilter()
    {
        if (_mapData == null) return null;
        if (_editModeManager.CurrentMode == EditMode.BuildingDeploy)
        {
            return coord => _mapData.FindBuildingIndex(coord.Col, coord.Row) >= 0;
        }
        if (_editModeManager.CurrentMode == EditMode.BelongEdit)
        {
            // 归属编辑引入了建筑/单位/陷阱/归属四个模块，框选多选需同时覆盖这四类目标：
            // 有归属值、或有建筑、或有单位（v1/v3）、或有陷阱的格子都算命中。
            return coord => _mapData.GetBelongValue(coord.Col, coord.Row) != 0xFF
                         || _mapData.FindBuildingIndex(coord.Col, coord.Row) >= 0
                         || _mapData.GetArmyAt(coord.Col, coord.Row) != null
                         || _mapData.GetArmyV3At(coord.Col, coord.Row) != null
                         || _mapData.FindTrapIndex(coord.Col, coord.Row) >= 0;
        }
        if (_editModeManager.CurrentMode == EditMode.ArmyDeploy)
        {
            // 单位部署模式：框选只命中存在单位的格子（v1 或 v3 均算）。
            return coord => _mapData.GetArmyAt(coord.Col, coord.Row) != null
                         || _mapData.GetArmyV3At(coord.Col, coord.Row) != null;
        }
        return null;
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

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        var maskIds = _brushSettingsWindow?.IsMaskEnabled == true
            ? _brushSettingsWindow!.MaskedTerrainIds
            : null;
        bool maskInclude = _brushSettingsWindow?.MaskIncludeMode ?? true;

        if (maskIds != null && maskIds.Count > 0)
            terrain.PaintWithBrushMasked(col, row, maskIds, maskInclude);
        else
            terrain.PaintWithBrush(col, row);

        _renderEngine.InvalidateTerrainCache();
        _renderEngine.InvalidateCoastCacheRegion(_mapData, col, row, terrain.BrushSize + 2);
        _skElement.InvalidateVisual();
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

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAllAssistWindows();
        Window.ReturnToMainScene();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fileStateManager.Undo())
            _editModeManager_DataModifiedFromUndo();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fileStateManager.Redo())
            _editModeManager_DataModifiedFromUndo();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Render.File.OpenTitle"),
            Filter = $"{Loc.T("Render.File.MapFilter")} (*.bin)|*.bin|{Loc.T("Render.File.BtlFilter")} (*.btl)|*.btl|{Loc.T("Common.AllFiles")} (*.*)|*.*",
            InitialDirectory = System.IO.Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var detectedType = RenderSceneManager.GetSceneTypeByFilePath(dialog.FileName);
                var currentType = RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType);

                if (detectedType != currentType)
                {
                    var sceneManager = RenderSceneManager.Instance;
                    var sceneName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                    var sceneCount = sceneManager.SceneCount;
                    var fullSceneName = Loc.F("Render.SceneNumbered", sceneCount + 1, sceneName);
                    var newSceneId = sceneManager.CreateScene(fullSceneName, dialog.FileName, detectedType);
                    sceneManager.RequestSceneSwitch(newSceneId);
                    return;
                }

                var mapData = ReloadMapData(dialog.FileName);
                if (mapData == null)
                {
                    System.Windows.MessageBox.Show(Loc.T("Render.File.LoadFailed"), Loc.T("Common.Error"));
                    return;
                }

                mapData.FilePath = dialog.FileName;

                var sceneManager2 = RenderSceneManager.Instance;

                if (_sceneId >= 0)
                {
                    sceneManager2.CacheSceneToDisk(_sceneId);
                    sceneManager2.ReleaseSceneMemory(_sceneId);
                }

                _mapData = mapData;
                _camera = new Camera
                {
                    MapWidth = _mapData.MapWidth,
                    MapHeight = _mapData.MapHeight,
                    ViewportWidth = (int)_skElement.ActualWidth,
                    ViewportHeight = (int)_skElement.ActualHeight
                };
                _camera.CenterOnMap();

                InitializeRenderers();
                _renderEngine.InvalidateTerrainCache();
                _renderEngine.InvalidateCoastCacheFull(_mapData);
                _skElement.InvalidateVisual();

                _editModeManager.Initialize(_mapData);
                _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
                _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetViewLayerImageProvider(_renderEngine.ViewLayerImageProvider);
                _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
                _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
                _fileStateManager.OpenFile(_mapData, dialog.FileName, SceneType);

                var sceneName2 = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                var sceneCount2 = sceneManager2.SceneCount;
                var fullSceneName2 = Loc.F("Render.SceneNumbered", sceneCount2 + 1, sceneName2);
                _sceneId = sceneManager2.CreateScene(fullSceneName2, dialog.FileName, RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType));
                sceneManager2.SetSceneMapData(_sceneId, _mapData);
                sceneManager2.ActivateScene(_sceneId);

                if (_sceneNameLabel != null)
                    _sceneNameLabel.Text = fullSceneName2;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.F("Render.File.OpenFailed", ex.Message), Loc.T("Common.Error"));
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mapData == null) return;

        bool isBtlScene = SceneType == "stage" || SceneType == "conquest";
        string currentExt = !string.IsNullOrEmpty(_mapData.FilePath)
            ? System.IO.Path.GetExtension(_mapData.FilePath).ToLowerInvariant()
            : "";

        string filter;
        int defaultFilterIndex;
        string defaultFileName;

        if (isBtlScene)
        {
            filter = $"{Loc.T("Render.File.BtlOnly")}|*.btl|{Loc.T("Render.File.BinOnly")}|*.bin|{Loc.T("Common.AllFiles")}|*.*";
            defaultFilterIndex = 1;
        }
        else
        {
            filter = $"{Loc.T("Render.File.BinOnly")}|*.bin|{Loc.T("Render.File.BtlOnly")}|*.btl|{Loc.T("Common.AllFiles")}|*.*";
            defaultFilterIndex = 1;
        }

        if (!string.IsNullOrEmpty(_mapData.FilePath))
        {
            defaultFileName = System.IO.Path.GetFileName(_mapData.FilePath);
        }
        else if (isBtlScene)
        {
            defaultFileName = SceneType == "conquest"
                ? $"conquest_{_mapData.MapWidth}x{_mapData.MapHeight}.btl"
                : $"stage_{_mapData.MapWidth}x{_mapData.MapHeight}.btl";
        }
        else
        {
            defaultFileName = $"new_map_{_mapData.MapWidth}x{_mapData.MapHeight}.bin";
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Render.File.SaveTitle"),
            Filter = filter,
            FilterIndex = defaultFilterIndex,
            FileName = defaultFileName,
            InitialDirectory = System.IO.Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (!SaveMapDataByFileType(_mapData, dialog.FileName))
                {
                    System.Windows.MessageBox.Show(Loc.T("Render.Save.Failed"), Loc.T("Common.Error"));
                    return;
                }
                _mapData.FilePath = dialog.FileName;
                _fileStateManager.MarkSaved();

                // 同 Ctrl+S：已写回文件，场景磁盘缓存作废
                if (_sceneId >= 0)
                    RenderSceneManager.Instance.InvalidateSceneCache(_sceneId);

                System.Windows.MessageBox.Show(Loc.F("Render.Save.SavedTo", dialog.FileName), Loc.T("Render.Save.SuccessTitle"));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.F("Render.Save.FailedWithReason", ex.Message), Loc.T("Common.Error"));
            }
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(Loc.T("Render.File.NewMapConfirm"), Loc.T("Render.File.NewMapTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var newMapData = new MapData(60, 60);
        _mapData = newMapData;
        _camera = new Camera
        {
            MapWidth = _mapData.MapWidth,
            MapHeight = _mapData.MapHeight,
            ViewportWidth = (int)_skElement.ActualWidth,
            ViewportHeight = (int)_skElement.ActualHeight
        };
        _camera.CenterOnMap();

        InitializeRenderers();
        _renderEngine.InvalidateTerrainCache();
        _skElement.InvalidateVisual();

        _editModeManager.Initialize(_mapData);
        _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
        _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetViewLayerImageProvider(_renderEngine.ViewLayerImageProvider);
        _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
        _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
        _fileStateManager.OpenFile(_mapData, "", SceneType);
    }

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
            IsEnabled = !string.IsNullOrEmpty(_renderEngine.ViewLayerVisible.ToString())
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

    private void ViewLayerToggle_Click(object sender, RoutedEventArgs e)
    {
        _renderEngine.ViewLayerVisible = !_renderEngine.ViewLayerVisible;
        _debugConsole.WriteLine(Loc.F("Render.Console.ViewLayerVisibility", _renderEngine.ViewLayerVisible ? Loc.T("Render.Visible") : Loc.T("Render.Hidden")));
        _skElement.InvalidateVisual();
    }

    private void ViewLayerClear_Click(object sender, RoutedEventArgs e)
    {
        _renderEngine.ClearViewLayerImage();
        _debugConsole.WriteLine(Loc.T("Render.Console.ViewLayerCleared"));
        _skElement.InvalidateVisual();
    }

    private void SceneTabButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = _sceneTabPopup;
        if (popup == null || _sceneTabPanel == null) return;

        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        RefreshSceneTabButtons();
        popup.IsOpen = true;
    }

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
                Tag = scene.SceneId
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

    private void SwitchToScene(int targetSceneId)
    {
        try
        {
            var sceneManager = RenderSceneManager.Instance;
            var targetScene = sceneManager.GetScene(targetSceneId);
            if (targetScene == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Render.SceneNotFound"), Loc.T("Common.Error"));
                return;
            }

            Debug.WriteLine($"[RenderScene] 切换到场景 {targetSceneId}");

            var targetType = RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType);
            if (targetScene.SceneType != targetType)
            {
                sceneManager.RequestSceneSwitch(targetSceneId);
                return;
            }

            if (_sceneId >= 0)
            {
                sceneManager.CacheSceneToDisk(_sceneId);
                sceneManager.ReleaseSceneMemory(_sceneId);
            }

            var mapData = sceneManager.LoadSceneFromDisk(targetSceneId);
            if (mapData == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Render.SceneLoadFailed"), Loc.T("Common.Error"));
                return;
            }

            _mapData = mapData;
            _camera = new Camera
            {
                MapWidth = _mapData.MapWidth,
                MapHeight = _mapData.MapHeight,
                ViewportWidth = (int)_skElement.ActualWidth,
                ViewportHeight = (int)_skElement.ActualHeight
            };
            _camera.CenterOnMap();

            InitializeRenderers();
            _renderEngine.InvalidateTerrainCache();
            _renderEngine.InvalidateCoastCacheFull(_mapData);
            _skElement.InvalidateVisual();

            _editModeManager.Initialize(_mapData!);
            _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
            _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetViewLayerImageProvider(_renderEngine.ViewLayerImageProvider);
            _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
            _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
            _editModeManager.SetSceneType(SceneType);
            _fileStateManager.OpenFile(_mapData, targetScene.MapFilePath, SceneType);

            sceneManager.ActivateScene(targetSceneId);
            _sceneId = targetSceneId;

            if (_sceneNameLabel != null)
                _sceneNameLabel.Text = targetScene.SceneName;

            Debug.WriteLine($"[RenderScene] 已切换到场景 {targetSceneId}: {targetScene.SceneName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RenderScene] 切换场景失败: {ex.Message}");
            System.Windows.MessageBox.Show(Loc.F("Render.SceneSwitchFailed", ex.Message), Loc.T("Common.Error"));
        }
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

        if (_hexInfoWindow == null) return;
        try
        {
            if (!_hexInfoWindow.IsVisible) return;
        }
        catch { return; }

        if (e.CurrentPrimary.HasValue && _mapData != null)
            _hexInfoWindow.UpdateHexInfo(e.CurrentPrimary.Value.Col, e.CurrentPrimary.Value.Row);
        else
            _hexInfoWindow.ClearHexInfo();
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
    /// 注册省区编辑相关命令。主要服务于"从背景图识别省区"（省份编辑模式 P 键）的现场调参：
    /// 阈值不合适时直接改参数重跑，不必改代码重编译。
    /// </summary>
    private void RegisterProvinceCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("bg_province", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

            var provider = _renderEngine.ViewLayerImageProvider;
            if (provider == null || !provider.HasImage)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.NoBackgroundImage"));
                return;
            }

            if (_mapData.Capitals.Count == 0)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.NoCapitals"));
                return;
            }

            int darkThreshold = Core.Services.BackgroundProvinceGenerator.AutoDarkThreshold;
            int gradientThreshold = Core.Services.BackgroundProvinceGenerator.DefaultGradientThreshold;
            int minRegionPixels = Core.Services.BackgroundProvinceGenerator.DefaultMinRegionPixels;
            int minBoundaryPixels = Core.Services.BackgroundProvinceGenerator.DefaultMinBoundaryPixels;

            if ((args.Length > 0 && !int.TryParse(args[0], out darkThreshold)) ||
                (args.Length > 1 && !int.TryParse(args[1], out gradientThreshold)) ||
                (args.Length > 2 && !int.TryParse(args[2], out minRegionPixels)) ||
                (args.Length > 3 && !int.TryParse(args[3], out minBoundaryPixels)))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line4"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line5"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line6"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line7"));
                _debugConsole.WriteLine(Loc.T("Render.Console.BgProvinceUsage.Line8"));
                return;
            }

            var modifier = _editModeManager.GetModifier<ProvinceModifier>();
            if (modifier == null) { _debugConsole.WriteLine(Loc.T("Render.Console.ProvinceModifierUnavailable")); return; }

            var result = modifier.GenerateProvincesFromBackground(
                provider, darkThreshold, gradientThreshold, minRegionPixels, minBoundaryPixels);
            _debugConsole.WriteLine(result.Message);

            _editModeManager.RaiseStatusMessage(result.Message);
            _skElement.InvalidateVisual();
        }, "从背景图识别省区（同省份编辑模式 P 键），可附带阈值调参", "[灰度阈值] [梯度阈值] [最小区域像素数]");
    }

    /// <summary>
    /// 【临时诊断】省份 P 键识别时，导出灰度图与界线掩码，用于人工核对识别效果。
    /// <para>
    /// 重点看 <c>2_boundary.png</c>：合格的界线在图上应当<b>闭合连续</b>。
    /// 有断口就会让相邻两省在像素级连通，之后按"最近省会"扩展时越过真实省界去抢地盘，
    /// 表现为"某个省的地盘明显伸进了邻省"。
    /// </para>
    /// <para>
    /// 不需要时：删掉本方法与 <c>SaveGrayPng</c>，并移除 <c>OnLoaded</c> 里给 DebugSink 赋值的那一行。
    /// </para>
    /// </summary>
    private void ExportProvinceDebugImages(GrayscaleImage gray, bool[] boundary, int threshold)
    {
        try
        {
            string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "province_debug");
            System.IO.Directory.CreateDirectory(dir);

            string grayPath = System.IO.Path.Combine(dir, "1_gray.png");
            string binaryPath = System.IO.Path.Combine(dir, "2_binary.png");
            string boundaryPath = System.IO.Path.Combine(dir, "3_boundary.png");

            SaveGrayPng(grayPath, gray, null);
            SaveBinaryPng(binaryPath, gray, threshold);
            SaveGrayPng(boundaryPath, gray, boundary);

            long boundaryCount = 0;
            for (int i = 0; i < boundary.Length; i++)
            {
                if (boundary[i]) boundaryCount++;
            }

            double ratio = boundary.Length > 0 ? boundaryCount * 100.0 / boundary.Length : 0;

            _debugConsole.WriteLine(
                Loc.F("Render.Console.ProvinceDiag.Summary", threshold, boundaryCount, ratio));
            _debugConsole.WriteLine(Loc.F("Render.Console.ProvinceDiag.ExportedTo", dir));
            _debugConsole.WriteLine(Loc.F("Render.Console.ProvinceDiag.Gray", gray.Width, gray.Height));
            _debugConsole.WriteLine(Loc.F("Render.Console.ProvinceDiag.Binary", threshold));
            _debugConsole.WriteLine(Loc.T("Render.Console.ProvinceDiag.Boundary"));
        }
        catch (Exception ex)
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.ProvinceDiag.ExportFailed", ex.Message));
        }
    }

    /// <summary>
    /// 按识别所用阈值二值化：低于阈值的像素（即被判为深色界线的）画黑，其余画白。
    /// <para>
    /// 与灰度图相比，这张图直接呈现"算法眼中的界线"，边界是否清晰一眼可见。
    /// 注意它只反映<b>深色</b>判定，不含梯度判定与膨胀 —— 那两者体现在 3_boundary.png 上。
    /// </para>
    /// </summary>
    private static void SaveBinaryPng(string path, GrayscaleImage gray, int threshold)
    {
        int width = gray.Width;
        int height = gray.Height;
        int rowStride = gray.RowStride;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var buffer = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * rowStride;
            int dstRow = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                byte v = gray.Pixels[srcRow + x] < threshold ? (byte)0 : (byte)255;
                int dst = dstRow + x * 4;

                buffer[dst] = v;
                buffer[dst + 1] = v;
                buffer[dst + 2] = v;
                buffer[dst + 3] = 255;
            }
        }

        using var image = SKImage.FromPixelCopy(info, buffer);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = System.IO.File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// 把灰度图写成 PNG。<paramref name="boundary"/> 非 null 时，界线像素画成红色。
    /// <para>
    /// 手工铺一整块像素缓冲后一次编码，不走逐像素 SetPixel —— 后者在大图上慢到不可用。
    /// </para>
    /// </summary>
    private static void SaveGrayPng(string path, GrayscaleImage gray, bool[]? boundary)
    {
        int width = gray.Width;
        int height = gray.Height;
        int rowStride = gray.RowStride;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var buffer = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * rowStride;
            int dstRow = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int src = srcRow + x;
                int dst = dstRow + x * 4;
                byte g = gray.Pixels[src];

                if (boundary != null && boundary[src])
                {
                    buffer[dst] = 255;      // R
                    buffer[dst + 1] = 0;    // G
                    buffer[dst + 2] = 0;    // B
                }
                else
                {
                    buffer[dst] = g;
                    buffer[dst + 1] = g;
                    buffer[dst + 2] = g;
                }

                buffer[dst + 3] = 255;      // A
            }
        }

        using var image = SKImage.FromPixelCopy(info, buffer);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = System.IO.File.Create(path);
        data.SaveTo(stream);
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

            string fileName = System.IO.Path.GetFileNameWithoutExtension(resolved).ToLowerInvariant();
            RenderSceneBase newScene;
            if (ext == ".bin" || ext == ".dat")
            {
                newScene = new MapRenderScene(Window, resolved);
            }
            else if (fileName.StartsWith("conquest"))
            {
                newScene = new ConquestRenderScene(Window, resolved);
            }
            else
            {
                newScene = new StageRenderScene(Window, resolved);
            }

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

    private void RegisterTerrainCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("greening", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"绿化平地 (概率={prob}%)", () => terrain.ApplyGreening(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.F("Render.Console.GreeningDone", prob));
        }, "绿化平地", "<概率0-100>");

        cm.RegisterCommand("random_flat", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"随机平地变体 (概率={prob}%)", () => terrain.RandomizeFlatTerrain(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.F("Render.Console.RandomFlatDone", prob));
        }, "随机平地地形变体", "<概率0-100>");

        cm.RegisterCommand("random_variant", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"随机当前层变体 (概率={prob}%)", () => terrain.RandomizeVariant(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.F("Render.Console.RandomVariantDone", terrain.EditLayer, prob));
        }, "随机当前层地形变体", "<概率0-100>");

        cm.RegisterCommand("create_coast", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            var selectedHexes = _hexSelector.SelectedHexes;
            var targetHexes = selectedHexes.Count > 0
                ? selectedHexes.Select(h => (h.Col, h.Row)).ToList()
                : null;

            _editModeManager.RecordMultiCellChange("创建海岸线", () => terrain.CreateCoast(targetHexes));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.T("Render.Console.CoastCreated"));
        }, "创建海岸线", "");

        cm.RegisterCommand("process_ocean_layer2", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            var selectedHexes = _hexSelector.SelectedHexes;
            var targetHexes = selectedHexes.Count > 0
                ? selectedHexes.Select(h => (h.Col, h.Row)).ToList()
                : null;

            _editModeManager.RecordMultiCellChange("处理海洋第二层", () => terrain.ProcessOceanSecondLayer(targetHexes));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.T("Render.Console.OceanLayer2Done"));
        }, "处理海洋第二层", "");

        cm.RegisterCommand("export_hd", async args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            var result = terrain.ExportHdFile();
            _debugConsole.WriteLine(result.Message ?? "");
        }, "导出HD文件", "");

        cm.RegisterCommand("scale_map", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            double scale = 1.0;
            if (args.Length > 0 && double.TryParse(args[0], out double s))
                scale = Math.Clamp(s, 0.1, 10.0);

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ScaleNoop"));
                return;
            }

            _editModeManager.RecordMapResizeChange($"缩放地图 (比例={scale:F2})", () => terrain.ScaleMap(scale));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.F("Render.Console.ScaleDone", scale));
        }, "按比例缩放地图", "<比例0.1-10.0>");

        cm.RegisterCommand("resize_map", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

            if (args.Length < 2)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line1"));
                _debugConsole.WriteLine("  direction: up, down, left, right");
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line4"));
                return;
            }

            string direction = args[0];
            if (!int.TryParse(args[1], out int amount) || amount == 0)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.InvalidAmount"));
                return;
            }

            bool useOcean = args.Length > 2 && args[2].Equals("ocean", StringComparison.OrdinalIgnoreCase);

            _editModeManager.RecordMapResizeChange($"调整地图大小 ({direction}, {amount})", () => terrain.ResizeMap(direction, amount, useOcean));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine(Loc.F("Render.Console.ResizeDone", direction, amount));
        }, "调整地图大小", "<direction> <amount> [ocean]");

        cm.RegisterCommand("geo_add_ref", args =>
        {
            if (args.Length < 4)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line4"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line5"));
                return;
            }

            if (!int.TryParse(args[0], out int row) || !int.TryParse(args[1], out int col) ||
                !double.TryParse(args[2], out double lat) || !double.TryParse(args[3], out double lon))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ArgFormatError"));
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            rp.Points.Add(new Core.Geo.GeoReferencePoint { Row = row, Col = col, Latitude = lat, Longitude = lon });

            var ruler = _renderEngine.GeoRuler;
            if (ruler != null)
            {
                ruler.AddMarker(row, col, lat, lon);
            }

            _debugConsole.WriteLine(Loc.F("Render.Console.RefAdded", row, col, lat, lon));
            _skElement.InvalidateVisual();
        }, "添加经纬度参考点", "<row> <col> <lat> <lon>");

        cm.RegisterCommand("geo_remove_ref", args =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int index) || index < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoRemoveRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoRemoveRefUsage.Line2"));
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            if (index > rp.Points.Count)
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RefIndexOutOfRange", index, rp.Points.Count));
                return;
            }

            var removed = rp.Points[index - 1];
            rp.Points.RemoveAt(index - 1);

            var ruler = _renderEngine.GeoRuler;
            if (ruler != null && ruler.Markers.Count >= index)
            {
                var marker = ruler.Markers[index - 1];
                ruler.RemoveMarker(marker.Id);
            }

            _debugConsole.WriteLine(Loc.F("Render.Console.RefRemoved", index, removed.Row, removed.Col));
            _skElement.InvalidateVisual();
        }, "删除经纬度参考点", "<index>");

        cm.RegisterCommand("geo_calc", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

            var rp = _geoCalculator.ReferencePoints;
            if (!rp.IsValid())
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RefIncomplete", rp.Points.Count));
                return;
            }

            string outputPath = args.Length > 0 ? args[0] : $"geo_coords_{DateTime.Now:yyyyMMddHHmmss}.json";

            try
            {
                _geoCalculator.CalculateAndExport(_mapData, outputPath);
                _debugConsole.WriteLine(Loc.F("Render.Console.GeoCalcDone", outputPath));
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Render.Geo.CalcFailed", ex.Message));
            }
        }, "计算并导出经纬度", "[输出文件路径]");

        cm.RegisterCommand("geo_show_ref", _ =>
        {
            var rp = _geoCalculator.ReferencePoints;
            var ruler = _renderEngine.GeoRuler;
            _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", rp.Points.Count));
            for (int i = 0; i < rp.Points.Count; i++)
            {
                var p = rp.Points[i];
                _debugConsole.WriteLine(Loc.F("Render.Console.RefRow", i + 1, p.Row, p.Col, p.Latitude, p.Longitude));
            }
            _debugConsole.WriteLine(Loc.F("Render.Console.RefValid", rp.IsValid()));
            if (ruler != null)
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RulerMarkerCount", ruler.Markers.Count));
                foreach (var m in ruler.Markers)
                {
                    _debugConsole.WriteLine(Loc.F("Render.Console.MarkerRow", m.Id, m.Row, m.Col, m.Latitude, m.Longitude));
                }
            }
        }, "显示当前经纬度参考点", "");

        cm.RegisterCommand("geo_set_lat", args =>
        {
            if (args.Length < 1 || !double.TryParse(args[0], out double lat))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line3"));
                _debugConsole.WriteLine(Loc.F("Render.Console.GeoSetLatUsage.Current",
                    _geoCalculator.CentralLatitude?.ToString("F4") ?? Loc.T("Render.Console.AutoEstimate")));
                return;
            }

            _geoCalculator.CentralLatitude = lat;
            _debugConsole.WriteLine(Loc.F("Render.Console.CentralLatSet", lat));
            _debugConsole.WriteLine(Loc.T("Render.Console.SphericalNote"));
        }, "设置地图中心纬度（用于球面修正）", "<latitude>");

        cm.RegisterCommand("geo_export_ref", args =>
        {
            string fileName = args.Length > 0 ? args[0] : $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json";
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            try
            {
                _geoCalculator.ExportReferencePointsOnly(path);
                _debugConsole.WriteLine(Loc.F("Render.Console.RefExported", path));
                _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", _geoCalculator.ReferencePoints.Points.Count));
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Common.ExportFailed", ex.Message));
            }
        }, "导出参考点配置到 JSON", "[filename]");

        cm.RegisterCommand("geo_import_ref", args =>
        {
            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoImportRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoImportRefUsage.Line2"));
                return;
            }

            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, args[0]);
            if (!System.IO.File.Exists(path))
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.FileNotExist", path));
                return;
            }

            try
            {
                _geoCalculator.ImportReferencePoints(path);
                var rp = _geoCalculator.ReferencePoints;
                _debugConsole.WriteLine(Loc.F("Render.Console.RefImported", path));
                _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", rp.Points.Count));

                // 同步到标尺
                var ruler = _renderEngine.GeoRuler;
                if (ruler != null)
                {
                    ruler.ClearMarkers();
                    foreach (var p in rp.Points)
                    {
                        ruler.AddMarker(p.Row, p.Col, p.Latitude, p.Longitude);
                    }
                    _debugConsole.WriteLine(Loc.T("Render.Console.RulerSynced"));
                }
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Render.ImportFailed", ex.Message));
            }
        }, "从 JSON 导入参考点配置", "<filename>");

        cm.RegisterCommand("geo_ruler", _ =>
        {
            _debugConsole.WriteLine(Loc.T("Render.Console.RulerAlwaysOn"));
        }, "经纬度标尺状态（始终显示）", "");
    }

    private async void DispatchEditModeAction(string action)
    {
        Debug.WriteLine($"[EditMode] DispatchEditModeAction: action={action}, mode={_editModeManager.CurrentMode}");

        if (action == "tab_action")
        {
            OnToggleHexInfo();
            return;
        }

        if (action == "cycle_mode")
        {
            _editModeManager.CycleMode(SceneType);
            return;
        }

        if (action == "toggle_show_layer2")
        {
            _renderEngine.ShowLayer2 = !_renderEngine.ShowLayer2;
            _skElement.InvalidateVisual();
            return;
        }

        if (action == "toggle_help_text")
        {
            _renderEngine.ShowHelp = !_renderEngine.ShowHelp;
            _skElement.InvalidateVisual();
            return;
        }

        if (action == "toggle_hex_borders")
        {
            _renderEngine.ShowHexBorders = !_renderEngine.ShowHexBorders;
            _skElement.InvalidateVisual();
            return;
        }

        if (action == "toggle_labels")
        {
            _renderEngine.CycleLabelMode();
            _skElement.InvalidateVisual();
            return;
        }

        if (action == "toggle_view_layer")
        {
            _renderEngine.ViewLayerVisible = !_renderEngine.ViewLayerVisible;
            _skElement.InvalidateVisual();
            return;
        }

        if (_mapData == null || _camera == null)
        {
            Debug.WriteLine($"[EditMode] Aborted: mapData={_mapData != null}, camera={_camera != null}");
            return;
        }

        var primary = _hexSelector.PrimarySelected;
        int col, row;

        if (primary.HasValue)
        {
            (col, row) = (primary.Value.Col, primary.Value.Row);
        }
        else
        {
            var mousePos = Mouse.GetPosition(_skElement);
            (col, row) = _camera.ScreenToHex(mousePos.X, mousePos.Y);
            Debug.WriteLine($"[EditMode] No primary selected, using mouse pos=({mousePos.X:F0},{mousePos.Y:F0}) -> hex=({col},{row})");
            if (col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight)
                _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }

        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight)
        {
            Debug.WriteLine($"[EditMode] Aborted: coord ({col},{row}) out of range ({_mapData.MapWidth}x{_mapData.MapHeight})");
            return;
        }

        Debug.WriteLine($"[EditMode] Calling HandleKeyAction({action}, {col}, {row})");
        bool handled = await _editModeManager.HandleKeyAction(action, col, row);
        Debug.WriteLine($"[EditMode] HandleKeyAction result: {handled}");
        _skElement.InvalidateVisual();
    }

    private void OnEditModeChanged(object? sender, EditModeChangedEventArgs e)
    {
        if (_brushSettingsWindow != null)
        {
            _brushSettingsWindow.PlayFadeOutAndHide();
            _brushSettingsWindow = null;
        }

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

        // 切出省份模式时要撤掉红色边界层，否则会残留在其它模式上
        RefreshBoundaryOverlay();

        _skElement.InvalidateVisual();
    }

    // ==================== 手绘边界（省份编辑模式 U 键） ====================

    /// <summary>是否正在拖拽画边界（右键按下到抬起之间）</summary>
    private bool _isDrawingBoundary;

    /// <summary>当前是否处于省份编辑模式；不在则返回 null</summary>
    private ProvinceEditMode? ActiveProvinceMode
        => _editModeManager.CurrentMode == EditMode.ProvinceEdit
            ? _editModeManager.GetModeHandler(EditMode.ProvinceEdit) as ProvinceEditMode
            : null;

    private void OnBoundaryDrawToggled(object? sender, EventArgs e)
    {
        RefreshBoundaryOverlay();
        _skElement.InvalidateVisual();
    }

    /// <summary>把当前模式的边界掩码同步给渲染层（无边界或不在该模式时关闭覆盖层）</summary>
    private void RefreshBoundaryOverlay()
    {
        var mode = ActiveProvinceMode;
        if (mode == null || mode.BoundaryMask == null)
        {
            _renderEngine.SetProvinceBoundaryOverlay(null, 0, 0);
            return;
        }

        _renderEngine.SetProvinceBoundaryOverlay(
            mode.BoundaryMask, mode.BoundaryPixelWidth, mode.BoundaryPixelHeight);
    }

    /// <summary>
    /// 尝试以"画边界"的方式处理这次右键按下。
    /// <para>
    /// 返回 true 表示这次右键已经被边界绘制消费掉，调用方不应再走原有的右键逻辑
    /// （地理标尺标记编辑等）—— 否则画线的同时会弹出标记对话框。
    /// </para>
    /// </summary>
    private bool TryBeginBoundaryDraw(MouseButtonEventArgs e)
    {
        if (_camera == null || _mapData == null) return false;

        var mode = ActiveProvinceMode;
        if (mode == null || !mode.IsBoundaryDrawMode) return false;

        mode.EnsureBoundaryCanvas(_mapData.MapWidth, _mapData.MapHeight);

        var pos = e.GetPosition(_skElement);
        var (mapX, mapY) = _camera.ScreenToWorldScaled(pos.X, pos.Y);
        mode.DrawBoundaryTo(mapX, mapY);

        _isDrawingBoundary = true;
        _skElement.CaptureMouse();

        RefreshBoundaryOverlay();
        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>右键拖拽中：把上一次落点连到当前点，画成连续的线</summary>
    private bool ContinueBoundaryDraw(MouseEventArgs e)
    {
        if (!_isDrawingBoundary || _camera == null) return false;

        var mode = ActiveProvinceMode;
        if (mode == null || !mode.IsBoundaryDrawMode)
        {
            _isDrawingBoundary = false;
            return false;
        }

        var pos = e.GetPosition(_skElement);
        var (mapX, mapY) = _camera.ScreenToWorldScaled(pos.X, pos.Y);

        if (mode.DrawBoundaryTo(mapX, mapY) > 0)
        {
            RefreshBoundaryOverlay();
            _skElement.InvalidateVisual();
        }

        return true;
    }

    /// <summary>结束这一笔</summary>
    private void EndBoundaryDraw()
    {
        if (!_isDrawingBoundary) return;

        _isDrawingBoundary = false;
        ActiveProvinceMode?.EndBoundaryStroke();
        _skElement.ReleaseMouseCapture();

        var mode = ActiveProvinceMode;
        if (mode != null)
        {
            _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.BoundaryDrawn", mode.DrawnBoundaryPixelCount));
        }
    }

    private void UpdateRenderLayersByMode(EditMode mode)
    {
        _renderEngine.EnableProvinceRender = mode == EditMode.ProvinceEdit;
        _renderEngine.EnableProvinceCapitalRender = mode == EditMode.ProvinceEdit;

        // 首都国旗层：军团编辑模式需要看到各首都的位置与归属（对齐 VB 在军团编辑模式绘制首都国旗）
        _renderEngine.EnableCapitalFlagRender = mode == EditMode.LegionEdit;

        // 单位部署、建筑部署、归属编辑、地形绘制都需要看到建筑
        _renderEngine.EnableBuildingRender = mode == EditMode.BuildingDeploy
                                          || mode == EditMode.BelongEdit
                                          || mode == EditMode.TerrainPaint
                                          || mode == EditMode.ArmyDeploy;
        // 名称显示 = 全局开关（建筑编辑模式 U 键，跨模式保持） 且 非地形绘制模式
        // （地形绘制模式按既有设计始终不显示名称）
        _renderEngine.ShowBuildingNames = _buildingNamesVisible && mode != EditMode.TerrainPaint;

        // 单位部署模式下显示部队与陷阱（该模式的快捷键包含陷阱创建/批量生成/随机等级，
        // 不显示陷阱就无法编辑）。
        // 归属编辑模式同样需要看到部队与陷阱：归属值要与驻地实体互相核对，只显示建筑不够
        // （对齐 VB 版归属模式：国家领域/建筑/单位/陷阱/归属国旗层全开）。
        _renderEngine.EnableArmyRender = mode == EditMode.ArmyDeploy
                                      || mode == EditMode.BelongEdit;
        _renderEngine.EnableTrapRender = mode == EditMode.ArmyDeploy
                                      || mode == EditMode.BelongEdit;

        // 国家领域着色（按归属值给每个格子铺国家颜色）：单位部署、建筑部署、归属编辑、
        // 军团编辑都需要 —— 军团编辑要看清每个军团的领域范围，以及与首都归属是否吻合。
        var needsDomain = mode == EditMode.BuildingDeploy
                       || mode == EditMode.BelongEdit
                       || mode == EditMode.ArmyDeploy
                       || mode == EditMode.LegionEdit;
        _renderEngine.EnableLegionDomainRender = needsDomain;

        // 归属国旗层是"每格一张小国旗"，军团编辑模式已有领域色块 + 首都国旗，
        // 再叠一层逐格国旗会互相干扰，所以只在其余三种模式打开。
        var needsBelongFlag = mode == EditMode.BuildingDeploy
                           || mode == EditMode.BelongEdit
                           || mode == EditMode.ArmyDeploy;
        _renderEngine.EnableBelongFlagRender = needsBelongFlag;

        if (needsBelongFlag && _mapData != null)
        {
            _renderEngine.PreloadBelongFlagAtlas(_mapData);
        }

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
        _skElement.InvalidateVisual();
    }

    private void _editModeManager_DataModifiedFromUndo()
    {
        OnEditModeDataModified(this, EventArgs.Empty);
    }

    private void UpdateOverlayFromEditMode()
    {
        _renderEngine.HelpText = _editModeManager.GetFullHelpText();
        _renderEngine.ModeName = _editModeManager.GetModeStatusText();
        _renderEngine.ShowHelp = true;
        _renderEngine.ShowModeName = _editModeManager.IsEditModeActive;
    }

    /// <summary>
    /// 获取当前焦点六边形坐标（选区中心或鼠标位置）
    /// </summary>
    private (int col, int row) GetFocusHex()
    {
        if (_mapData == null || _camera == null) return (0, 0);

        var primary = _hexSelector.PrimarySelected;
        if (primary.HasValue)
        {
            return (primary.Value.Col, primary.Value.Row);
        }

        var mousePos = Mouse.GetPosition(_skElement);
        var (col, row) = _camera.ScreenToHex(mousePos.X, mousePos.Y);
        if (col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight)
        {
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
            return (col, row);
        }

        return (0, 0);
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
        _renderEngine.ShowBuildingNames = _buildingNamesVisible
                                       && _editModeManager.CurrentMode != EditMode.TerrainPaint;

        _editModeManager.RaiseStatusMessage(_buildingNamesVisible ? Loc.T("Render.Status.BuildingNamesShown") : Loc.T("Render.Status.BuildingNamesHidden"));
        _skElement.InvalidateVisual();
    }

    /// <summary>建筑移动工具开关切换（建筑编辑模式 K 键）</summary>
    private void OnBuildingMoveToolToggled(object? sender, EventArgs e)
    {
        _buildingMoveToolActive = !_buildingMoveToolActive;

        if (!_buildingMoveToolActive)
        {
            // 关闭工具时结束进行中的拖拽
            _isDraggingBuilding = false;
            _dragBuildingSourceCol = -1;
            _dragBuildingSourceRow = -1;
            _hexSelector.ClearSelection();
        }

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

    /// <summary>建筑移动工具：右键按下开始拖拽建筑</summary>
    private bool TryStartBuildingDrag(MouseActionEventArgs e)
    {
        if (!_buildingMoveToolActive) return false;
        if (_editModeManager.CurrentMode != EditMode.BuildingDeploy) return false;
        if (_mapData == null || _camera == null) return false;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return false;

        var building = _mapData.GetBuildingAt(col, row);
        if (building == null) return false;

        _isDraggingBuilding = true;
        _dragBuildingSourceCol = col;
        _dragBuildingSourceRow = row;

        // 用选区高亮源格，提供视觉反馈
        _hexSelector.ClearSelection();
        _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);

        _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.MovingBuilding", building.Value.GetBuildingTypeName()));
        _skElement.InvalidateVisual();
        return true;
    }

    /// <summary>建筑移动工具：拖动过程中高亮当前目标格</summary>
    private bool TryContinueBuildingDrag(MouseActionEventArgs e)
    {
        if (!_isDraggingBuilding) return false;
        if (_mapData == null || _camera == null) return true;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return true;

        if (col != _dragBuildingSourceCol || row != _dragBuildingSourceRow)
        {
            _hexSelector.ClearSelection();
            _hexSelector.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
            _skElement.InvalidateVisual();
        }
        return true;
    }

    /// <summary>建筑移动工具：松开右键完成移动</summary>
    private bool TryEndBuildingDrag(MouseActionEventArgs e)
    {
        if (!_isDraggingBuilding) return false;

        int fromCol = _dragBuildingSourceCol;
        int fromRow = _dragBuildingSourceRow;

        _isDraggingBuilding = false;
        _dragBuildingSourceCol = -1;
        _dragBuildingSourceRow = -1;

        if (_mapData == null || _camera == null) return true;

        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        if (col >= 0 && col < _mapData.MapWidth && row >= 0 && row < _mapData.MapHeight)
        {
            var modifier = _editModeManager.GetModifier<BuildingModifier>();
            if (modifier != null)
            {
                var result = modifier.MoveBuilding(fromCol, fromRow, col, row);
                _editModeManager.RaiseStatusMessage(result.Message ?? Loc.T("Render.Status.BuildingMoved"));
            }
        }

        _hexSelector.ClearSelection();
        _skElement.InvalidateVisual();
        return true;
    }

    // ==================== 军团编辑模式（对齐 VB LegionModifier） ====================

    private Views.Assist.LegionSettingWindow? _legionSettingWindow;

    /// <summary>打开军团设置窗口（军团编辑模式 Q 键）</summary>
    private void OnOpenLegionSettingRequested(object? sender, EventArgs e)
    {
        if (_mapData == null) return;

        var modifier = _editModeManager.GetModifier<LegionModifier>();
        if (modifier == null) return;

        if (_legionSettingWindow != null)
        {
            _legionSettingWindow.Activate();
            return;
        }

        var window = new Views.Assist.LegionSettingWindow(_mapData, modifier);
        var owner = Window;
        if (owner != null) window.Owner = owner;

        window.DataModified += OnLegionSettingDataModified;
        window.Closed += (_, _) =>
        {
            if (_legionSettingWindow != null)
                _legionSettingWindow.DataModified -= OnLegionSettingDataModified;
            _legionSettingWindow = null;
        };

        _legionSettingWindow = window;
        window.Show();
        // 显式激活，确保键盘焦点落在子窗口上，避免 Esc 被主窗口的全局绑定接走
        window.Activate();
    }

    /// <summary>军团设置窗口数据修改 - 军团颜色/归属变化会影响领域层与国旗层</summary>
    private void OnLegionSettingDataModified(object? sender, EventArgs e)
    {
        _renderEngine.InvalidateViewLayerCache();
        // 军团设置窗口里可以改单个军团的颜色，领域层的颜色缓存要一起失效
        _renderEngine.InvalidateLegionDomainCache();
        // 军团增删改会改变国家 ID 集合，归属国旗图集必须重建，
        // 否则新增军团（拿到新的 CountryId）的国旗永远不在图集里。
        _renderEngine.InvalidateBelongFlagCache();
        _skElement.InvalidateVisual();
    }

    /// <summary>打开头部数据编辑窗口（军团编辑模式 E 键）</summary>
    private void OnOpenHeaderSettingRequested(object? sender, EventArgs e)
    {
        var header = _mapData?.Header;
        if (header == null)
        {
            MessageBox.Show(Loc.T("Render.Legion.HeaderUnavailable"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Views.Assist.HeaderSettingWindow.ShowDialog(Window, header))
        {
            _skElement.InvalidateVisual();
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.HeaderUpdated"));
        }
    }

    /// <summary>打开军团列表窗口（军团编辑模式 F 键）</summary>
    private void OnOpenLegionListRequested(object? sender, EventArgs e)
    {
        if (_mapData == null) return;

        var legions = _mapData.Legions.ToList();
        if (legions.Count == 0)
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.NoLegions"));
            return;
        }

        var window = new Views.Assist.LegionBelongListWindow(legions);
        var owner = Window;
        if (owner != null) window.Owner = owner;

        window.ShowDialog();

        if (window.IsConfirmed && window.SelectedActionId.HasValue)
        {
            var modifier = _editModeManager.GetModifier<LegionModifier>();
            if (modifier != null)
            {
                var legion = modifier.GetLegionByActionId(window.SelectedActionId.Value);
                if (legion.HasValue)
                    modifier.SelectedLegionId = legion.Value.CountryId;
            }

            _editModeManager.RaiseStatusMessage(Loc.F("Render.Status.LegionSelected", window.SelectedActionId.Value));
        }

        _skElement.InvalidateVisual();
    }

    /// <summary>更新征服国家设置（军团编辑模式 F6 键）</summary>
    private async void OnUpdateConquerSettingsRequested(object? sender, EventArgs e)
    {
        if (_mapData == null) return;

        var modifier = _editModeManager.GetModifier<LegionModifier>();
        if (modifier == null) return;

        var settings = WC4MapEditor.Core.Config.ConfigManager.Instance.GetConquerCountrySettingsData();
        if (settings.Count == 0)
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.NoConquerSettings"));
            return;
        }

        var dialogService = new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!);
        var input = await dialogService.ShowInputDialogAsync(
            Loc.T("Render.Legion.UpdateConquerTitle"), Loc.T("Render.Legion.ConquerIdPrompt"), "1", 1, 999);
        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out int conquerId)) return;

        try
        {
            var result = modifier.UpdateConquerCountrySettings(conquerId, settings);
            _editModeManager.RaiseStatusMessage(result.Message ?? Loc.T("Render.Status.ConquerUpdated"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Legion.UpdateConquerFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>军团范围截图（军团编辑模式 P 键）</summary>
    private async void OnCaptureLegionScreenshotRequested(object? sender, EventArgs e)
    {
        if (_mapData == null) return;

        try
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.CapturingLegion"));
            string path = await Task.Run(() => CaptureLegionScreenshot(_mapData));
            MessageBox.Show(Loc.F("Render.Legion.ScreenshotSaved", path), Loc.T("Render.Legion.ScreenshotTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Legion.ScreenshotFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 读取 setting.txt 的 screenshot_resolution 并换算为缩放系数（对齐 VB 版）。
    /// </summary>
    private static double GetScreenshotScaleFactor()
    {
        var resolutionMap = new Dictionary<string, double>
        {
            ["1x"] = 1.0, ["2x"] = 0.5, ["4x"] = 0.25, ["8x"] = 0.125,
            ["16x"] = 0.0625, ["32x"] = 0.03125, ["64x"] = 0.015625
        };

        string key = "1x";
        try
        {
            // 当前目录优先（允许临时覆盖），否则按与 Resource 相同的规则向上查找
            string path = "setting.txt";
            if (!System.IO.File.Exists(path))
                path = ProjectPaths.FindFile("setting.txt")
                       ?? System.IO.Path.Combine(AppContext.BaseDirectory, "setting.txt");

            if (System.IO.File.Exists(path))
            {
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    if (!line.StartsWith("screenshot_resolution", StringComparison.Ordinal)) continue;

                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string value = parts[1].Trim();
                        if (resolutionMap.ContainsKey(value)) key = value;
                    }
                    break;
                }
            }
        }
        catch { }

        return resolutionMap.TryGetValue(key, out double factor) ? factor : 1.0;
    }

    /// <summary>
    /// 离屏渲染军团范围图并保存为 PNG。
    /// 对齐 VB 版 LegionModifier.CaptureLegionScreenshot：按省区着色、
    /// 省会格绘制国旗、被裁剪格绘制红十字，输出到程序目录 screenshots 下。
    /// </summary>
    private static string CaptureLegionScreenshot(MapData mapData)
    {
        double scaleFactor = GetScreenshotScaleFactor();

        int startCol = mapData.Header?.MapClipX ?? 0;
        int startRow = mapData.Header?.MapClipY ?? 0;
        int cols = mapData.MapWidth - startCol;
        int rows = mapData.MapHeight - startRow;

        if (cols <= 0 || rows <= 0)
        {
            startCol = 0;
            startRow = 0;
            cols = mapData.MapWidth;
            rows = mapData.MapHeight;
        }

        int imageWidth = Math.Max(1, (int)(cols * 54 * scaleFactor));
        int imageHeight = Math.Max(1, (int)((rows + 2) * 62 * scaleFactor));
        double cellWidth = (double)imageWidth / cols;
        double cellHeight = (double)imageHeight / (rows + 2);
        float hexSize = (float)(cellHeight / Math.Sqrt(3));

        var emptyColor = new SKColor(240, 240, 240);

        // 省区索引 → 该省区颜色（取自省会格所属军团的颜色）
        var provinceColors = new Dictionary<int, SKColor>();
        var hexToProvince = new Dictionary<int, int>();
        var capitalCells = new HashSet<int>();
        int totalTiles = mapData.MapWidth * mapData.MapHeight;

        for (int row = 0; row < mapData.MapHeight; row++)
        {
            for (int col = 0; col < mapData.MapWidth; col++)
            {
                int index = row * mapData.MapWidth + col;
                int provinceValue = mapData.GetProvinceRef(col, row).ProvinceValue;
                if (provinceValue == 0 || provinceValue == 0xFFFF) continue;
                if (provinceValue < 0 || provinceValue >= totalTiles) continue;

                hexToProvince[index] = provinceValue;
                if (provinceValue == index) capitalCells.Add(index);

                if (provinceColors.ContainsKey(provinceValue)) continue;

                int capitalCol = provinceValue % mapData.MapWidth;
                int capitalRow = provinceValue / mapData.MapWidth;
                int belong = mapData.GetBelongValue(capitalCol, capitalRow);
                if (belong == 0xFF) continue;

                // 归属值解析成军团。以前这里写成 FindLegionIndex(belong) —— 那个方法是按
                // CountryId 匹配的，拿归属索引去当 CountryId 查，多数军团查不到（返回 -1）
                // 就被 skip，表现为"截图颜色对不上、且很多军团没颜色"。
                var legion = mapData.ResolveLegionByBelong(belong);
                if (legion == null) continue;

                provinceColors[provinceValue] =
                    new SKColor(legion.Value.ColorR, legion.Value.ColorG, legion.Value.ColorB);
            }
        }

        string stageMarkPath = WC4MapEditor.Core.Config.ConfigManager.Instance.GetStageMarkPath();
        var flagCache = new Dictionary<int, SKBitmap?>();

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(emptyColor);

        for (int rowIdx = 0; rowIdx < rows; rowIdx++)
        {
            int actualRow = startRow + rowIdx;
            if (actualRow >= mapData.MapHeight) break;

            for (int colIdx = 0; colIdx < cols; colIdx++)
            {
                int actualCol = startCol + colIdx;
                if (actualCol >= mapData.MapWidth) break;

                float x = (float)(actualCol * cellWidth + cellWidth / 2);
                float y = (float)((rowIdx + 1) * cellHeight + cellHeight / 2);
                if (actualCol % 2 == 1) y += (float)(cellHeight / 2);

                int index = actualRow * mapData.MapWidth + actualCol;
                bool clipped = IsTileClipped(mapData, actualCol, actualRow);

                SKColor color = emptyColor;
                if (!clipped
                    && hexToProvince.TryGetValue(index, out int provinceIdx)
                    && provinceColors.TryGetValue(provinceIdx, out var provinceColor))
                {
                    color = provinceColor;
                }

                DrawFlatTopHexagon(canvas, x, y, hexSize, color);

                if (clipped)
                {
                    DrawClippedCross(canvas, x, y, hexSize);
                    continue;
                }

                // 省会格 → 绘制国旗
                if (capitalCells.Contains(index))
                {
                    int belong = mapData.GetBelongValue(actualCol, actualRow);
                    if (belong == 0xFF) continue;

                    var legion = mapData.ResolveLegionByBelong(belong);
                    if (legion == null) continue;

                    int flagSize = Math.Max(4, (int)(hexSize * 0.8f));
                    DrawCapitalFlag(canvas, x, y, stageMarkPath, legion.Value.CountryId, flagSize, flagCache);
                }
            }
        }

        foreach (var bitmap in flagCache.Values)
            bitmap?.Dispose();

        string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "screenshots");
        System.IO.Directory.CreateDirectory(dir);
        string filePath = System.IO.Path.Combine(dir,
            $"legion_map_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = System.IO.File.OpenWrite(filePath);
        data.SaveTo(stream);

        return filePath;
    }

    private static bool IsTileClipped(MapData mapData, int col, int row)
    {
        int clipX = mapData.Header?.MapClipX ?? 0;
        int clipY = mapData.Header?.MapClipY ?? 0;
        if (clipY > 0 && row < clipY) return true;
        if (clipX > 0 && col < clipX) return true;
        return false;
    }

    private static void DrawFlatTopHexagon(SKCanvas canvas, float x, float y, float size, SKColor color)
    {
        using var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI / 3 * i;
            float px = x + size * (float)Math.Cos(angle);
            float py = y + size * (float)Math.Sin(angle);
            if (i == 0) path.MoveTo(px, py);
            else path.LineTo(px, py);
        }
        path.Close();

        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, fill);

        using var stroke = new SKPaint
        {
            Color = new SKColor(200, 200, 200),
            StrokeWidth = 1,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawPath(path, stroke);
    }

    private static void DrawClippedCross(SKCanvas canvas, float x, float y, float hexSize)
    {
        float len = hexSize * 0.6f;
        using var paint = new SKPaint { Color = SKColors.Red, StrokeWidth = 4, IsAntialias = true };
        canvas.DrawLine(x - len / 2, y - len / 2, x + len / 2, y + len / 2, paint);
        canvas.DrawLine(x - len / 2, y + len / 2, x + len / 2, y - len / 2, paint);
    }

    private static void DrawCapitalFlag(SKCanvas canvas, float x, float y, string stageMarkPath,
        int countryId, int flagSize, Dictionary<int, SKBitmap?> cache)
    {
        if (!cache.TryGetValue(countryId, out var bitmap))
        {
            bitmap = null;
            try
            {
                string basePath = System.IO.Path.Combine(stageMarkPath, "CountryFlag");
                string path = System.IO.Path.Combine(basePath, $"flag_{countryId}.png");
                if (!System.IO.File.Exists(path) && (countryId == 0 || countryId == 255))
                    path = System.IO.Path.Combine(basePath, "flag_1.png");

                if (System.IO.File.Exists(path))
                    bitmap = SKBitmap.Decode(path);
            }
            catch { }

            cache[countryId] = bitmap;
        }

        if (bitmap == null) return;

        float half = flagSize / 2f;
        canvas.DrawBitmap(bitmap, new SKRect(x - half, y - half, x + half, y + half));
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

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushActive = !terrain.BrushActive;

        if (terrain.BrushActive)
        {
            if (_brushSettingsWindow == null)
            {
                _brushSettingsWindow = new BrushSettingsWindow();
                _brushSettingsWindow.BrushSettingsChanged += OnBrushSettingsChanged;
                _brushSettingsWindow.InitializeTerrainList();
                _brushSettingsWindow.UpdateVariantList();
                _brushSettingsWindow.InitializeMaskTerrainList();
                _brushSettingsWindow.BrushSize = terrain.BrushSize;
                _brushSettingsWindow.SelectedTerrainType = terrain.BrushTerrainType;
                _brushSettingsWindow.SelectedTerrainVariant = terrain.BrushDecoration;
                _brushSettingsWindow.EditLayer = terrain.EditLayer;
                _brushSettingsWindow.BrushShape = terrain.BrushShape;

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
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOnTerrain"));
        }
        else
        {
            if (_brushSettingsWindow != null)
                _brushSettingsWindow.PlayFadeOutAndHide();
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOff"));
        }
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

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushSize = e.BrushSize;
        terrain.BrushTerrainType = e.TerrainType;
        terrain.BrushDecoration = e.TerrainVariant;
        terrain.BrushShape = e.BrushShape;

        // 刻意不回写 terrain.EditLayer：
        // 画笔设置窗口里没有"编辑层"控件，它的 EditLayer 只是窗口创建时同步的快照，
        // 按 Z 切换编辑层后不会跟着更新。若在此回写，窗口里任何动作
        // （改大小 / 选地形 / 改遮罩）都会把编辑层重置回旧值，
        // 表现就是"切到第 2 层后画笔仍然在改第一层"。
        // 编辑层统一由 Z 键（以及 CLI 的 --layer）控制。
    }

    private void OnBrushSizeChanged(object? sender, EventArgs e)
    {
        var (col, row) = GetFocusHex();

        if (_editModeManager.CurrentMode == EditMode.TerrainPaint)
        {
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) return;

            if (_brushSettingsWindow != null && _brushSettingsWindow.IsVisible)
                _brushSettingsWindow.BrushSize = terrain.BrushSize;

            if (terrain.BrushActive)
            {
                _renderEngine.SetBrushPreview(
                    col, row,
                    terrain.BrushSize, terrain.BrushShape,
                    _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
                    true);
                _skElement.InvalidateVisual();
            }
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

    private async Task<(bool success, int modifiedCount)> RecognizeTerrainFromViewLayer()
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return (false, 0);

        var provider = _renderEngine.ViewLayerImageProvider;
        if (provider == null)
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.NoViewLayerProvider"));
            return (false, 0);
        }

        terrain.TerrainRecognizer.SetImageProvider(provider);

        var cts = new CancellationTokenSource();
        var progressWindow = new Views.Dialogs.ProgressWindow();
        progressWindow.Closing += (_, _) =>
        {
            try
            {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS 已被释放，忽略
            }
        };

        var progress = new Progress<(int current, int total, int row, int col)>(p =>
        {
            int percent = p.total > 0 ? (int)((double)p.current / p.total * 100) : 0;
            progressWindow.UpdateProgress(p.current, p.total, p.row, p.col, percent);
        });

        progressWindow.Show();

        // 等待窗口加载完成并开始淡入动画后再执行识别
        await Task.Delay(250);

        var (success, modifiedCount) = await terrain.RecognizeTerrainAsync(progress, cts.Token);

        progressWindow.PlayFadeOutAndClose();

        // 延迟释放 CTS，确保窗口关闭事件处理完毕
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            cts.Dispose();
        });

        if (success && modifiedCount > 0)
        {
            _renderEngine.InvalidateTerrainCache();
            if (_mapData != null)
                _renderEngine.InvalidateCoastCacheFull(_mapData);
            _skElement.InvalidateVisual();
        }

        return (success, modifiedCount);
    }

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
        _editModeManager.ModeChanged -= OnEditModeChanged;
        _editModeManager.StatusMessageChanged -= OnEditModeStatusMessageChanged;
        _editModeManager.DataModified -= OnEditModeDataModified;
        _editModeManager.BrushToggled -= OnBrushToggled;

        // 场景销毁时关闭随本场景打开的子窗口，避免切换场景后残留
        try
        {
            _legionSettingWindow?.Close();
            _hexInfoWindow?.Close();
            _brushSettingsWindow?.Hide();
        }
        catch { }
        _legionSettingWindow = null;
        _hexInfoWindow = null;
        _brushSettingsWindow = null;
        if (_debugConsole.IsVisible) _debugConsole.HideConsole();

        _renderEngine?.Dispose();
    }
}