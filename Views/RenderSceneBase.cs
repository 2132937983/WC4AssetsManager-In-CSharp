using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Services;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Rendering.Helpers;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Views.Assist;
using WC4MapEditor.Views.Dialogs;
using SkiaSharp.Views.Desktop;

namespace WC4MapEditor.Views;

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
        catch
        {
            return false;
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

        var undoButton = CreateToolBarButton("撤销");
        undoButton.Click += UndoButton_Click;
        titlePanel.Children.Add(undoButton);

        var redoButton = CreateToolBarButton("重做");
        redoButton.Click += RedoButton_Click;
        titlePanel.Children.Add(redoButton);

        titlePanel.Children.Add(CreateSeparator());

        var openButton = CreateToolBarButton("打开");
        openButton.Click += OpenButton_Click;
        titlePanel.Children.Add(openButton);

        var saveButton = CreateToolBarButton("保存");
        saveButton.Click += SaveButton_Click;
        titlePanel.Children.Add(saveButton);

        var newButton = CreateToolBarButton("新建");
        newButton.Click += NewButton_Click;
        titlePanel.Children.Add(newButton);

        titlePanel.Children.Add(CreateSeparator());

        var viewButton = CreateToolBarButton("视图层");
        viewButton.Click += ViewLayerButton_Click;
        titlePanel.Children.Add(viewButton);

        titlePanel.Children.Add(CreateSeparator());

        var sceneTabButton = CreateToolBarButton("场景");
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

        _keyboardManager.RegisterBinding("EscBack", (int)Key.Escape, KeyModifiers.None, OnEscPressed, "返回主场景");
        _keyboardManager.RegisterBinding("ToggleConsole", (int)Key.F3, KeyModifiers.None, OnToggleConsole, "调试控制台");
        _keyboardManager.RegisterBinding("Screenshot", (int)Key.F2, KeyModifiers.None, OnScreenshot, "截图");
        _keyboardManager.RegisterBinding("QuickSave", (int)Key.S, KeyModifiers.Ctrl, OnQuickSave, "快速保存 (Ctrl+S)");

        InitializeEditModeKeyBindings();

        _editModeManager.Initialize(_mapData);
        _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
        _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
        _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
        _editModeManager.SetGeoCalculateCallback(OnGeoCalculateAsync);
        _editModeManager.SetGeoExportRefCallback(OnGeoExportRefAsync);
        _editModeManager.SetGeoImportRefCallback(OnGeoImportRefAsync);
        _editModeManager.SetAddGeoRefCallback(OnAddGeoRefAsync);
        _editModeManager.SetGetFocusHexCallback(GetFocusHex);
        _editModeManager.ModeChanged += OnEditModeChanged;
        _editModeManager.StatusMessageChanged += OnEditModeStatusMessageChanged;
        _editModeManager.DataModified += OnEditModeDataModified;
        _editModeManager.BrushToggled += OnBrushToggled;
        _editModeManager.BrushSizeChanged += OnBrushSizeChanged;

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
            _debugConsole.WriteLine($"[错误] [{e.Severity}] {e.Message}");
        };
        ErrorCollector.Instance.ErrorRecovered += (s, e) =>
        {
            if (e.Recovered)
                _debugConsole.WriteLine($"[恢复] {e.RecoveryAction}");
        };

        _debugConsole.VisibilityChanged += isVisible =>
        {
            if (_titleBar != null)
                _titleBar.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            // 显示控制台时隐藏帮助文本，关闭控制台时恢复
            _renderEngine.ShowHelp = !isVisible;
            _skElement.InvalidateVisual();
        };
        _commandManager.SetContext(new CommandContext { MapData = _mapData });
        Core.Commands.CliCommandHost.Instance.SetOutput(new Core.Commands.DebugConsoleWriter(_debugConsole));
        Core.Commands.CliCommandHost.Instance.DataModified += () =>
        {
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
        };
        Core.Config.ConfigManager.StringTableChanged += () =>
        {
            _renderEngine.ReloadBuildingCityNames();
            _skElement.InvalidateVisual();
        };
        _commandManager.RegisterCommand("undo", _ =>
        {
            if (_fileStateManager.Undo()) { _editModeManager_DataModifiedFromUndo(); }
            else { _debugConsole.WriteLine("无法撤销"); }
        }, "撤销上一步操作");
        _commandManager.RegisterCommand("redo", _ =>
        {
            if (_fileStateManager.Redo()) { _editModeManager_DataModifiedFromUndo(); }
            else { _debugConsole.WriteLine("无法重做"); }
        }, "重做上一步操作");
        RegisterTerrainCliCommands();
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

    private void OnToggleConsole()
    {
        if (_debugConsole.IsVisible)
        {
            _debugConsole.HideConsole();
        }
        else
        {
            _debugConsole.ShowConsole();
            var ownerWindow = System.Windows.Window.GetWindow(this);
            var window = new DebugConsoleWindow(_debugConsole, ownerWindow);
            _debugConsole.SetHostWindow(window.Close);
            window.Show();
        }
    }

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
                    MessageBox.Show($"地图已保存到:\n{_mapData.FilePath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    _debugConsole.WriteLine($"[保存] 已保存到: {_mapData.FilePath}");
                }
                else
                {
                    MessageBox.Show("保存失败！", "保存错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    _debugConsole.WriteLine("[保存] 保存失败！");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "保存错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _debugConsole.WriteLine($"[保存] 保存失败: {ex.Message}");
            }
        }
        else
        {
            // 没有文件路径，调用保存对话框
            SaveButton_Click(this, new RoutedEventArgs());
        }
    }

    private enum ScreenshotMode { Standard, HexBased, Cancel }

    private async Task CaptureMapScreenshotAsync()
    {
        try
        {
            if (_mapData == null)
            {
                _debugConsole.WriteLine("[截图] 地图数据为空，无法截图");
                return;
            }

            var screenshotMode = await ShowScreenshotModeDialogAsync();
            if (screenshotMode == ScreenshotMode.Cancel) return;

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 图片|*.png|所有文件|*.*",
                Title = "保存地图截图"
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
            _debugConsole.WriteLine("[截图] 开始生成地图截图...");

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
                    _debugConsole.WriteLine("[截图] 使用逐个六边形渲染截图");
                    success = await Task.Run(() =>
                    {
                        using var helper = new HexBasedScreenshotHelper();
                        return helper.CaptureMap(mapData, outputPath, 1.0, false, false, showLayer2, progress);
                    });
                    break;

                default:
                    _debugConsole.WriteLine("[截图] 使用标准截图");
                    success = await Task.Run(() =>
                    {
                        using var helper = new ScreenshotHelper();
                        return helper.CaptureMap(mapData, outputPath, 1.0, false, false, showLayer2, progress);
                    });
                    break;
            }

            progressWindow.PlayFadeOutAndClose();

            if (success)
                _debugConsole.WriteLine("[截图] 截图已保存: " + outputPath);
            else
                _debugConsole.WriteLine("[截图] 截图生成失败");
        }
        catch (Exception ex)
        {
            _debugConsole.WriteLine("[截图] 错误: " + ex.Message);
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
            Text = "选择截图模式",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        stackPanel.Children.Add(titleBlock);

        var descBlock = new TextBlock
        {
            Text = "标准截图：使用渲染引擎快速生成\n逐个六边形：逐个绘制地形纹理，质量更高",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
        stackPanel.Children.Add(descBlock);

        var standardButton = new Button
        {
            Content = "标准截图（快速）",
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
            Content = "逐个六边形渲染（高质量）",
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
            Content = "取消",
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

    private readonly GeoCoordinateCalculator _geoCalculator = new();

    private async Task OnAddGeoRefAsync()
    {
        if (_mapData == null)
        {
            MessageBox.Show("地图数据未加载", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var primary = _hexSelector.PrimarySelected;
        if (!primary.HasValue)
        {
            MessageBox.Show("请先选中一个格子", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int row = primary.Value.Row;
        int col = primary.Value.Col;

        using var inputDialog = new Views.Assist.DoubleInputDialog(Window)
        {
            Title = $"添加参考点 (R{row}C{col})",
            Description1 = "纬度 (Latitude)：",
            Description2 = "经度 (Longitude)：",
            DefaultValue1 = "",
            DefaultValue2 = ""
        };
        var result = await inputDialog.ShowAsync();
        if (!result.HasValue) return;

        var (latStr, lonStr) = result.Value;
        if (!double.TryParse(latStr, out double lat) || !double.TryParse(lonStr, out double lon))
        {
            MessageBox.Show("请输入有效的经纬度数值", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        MessageBox.Show($"已添加参考点 #{ruler.Markers.Count}: R{row}C{col} = ({lat:F7}, {lon:F7})", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task OnGeoCalculateAsync()
    {
        if (_mapData == null)
        {
            MessageBox.Show("地图数据未加载", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateGeoReferenceFromRuler();

        var rp = _geoCalculator.ReferencePoints;

        if (!rp.IsValid())
        {
            MessageBox.Show(
                "请先设置参考点：\n" +
                "1. 选中格子按Q键添加参考点\n" +
                "2. 或拖动现有标记到目标位置\n" +
                "3. 右键点击标记输入/修改经纬度\n" +
                "4. 需要至少2个参考点",
                "经纬度换算", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "保存经纬度换算结果",
            FileName = $"geo_coords_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (saveDialog.ShowDialog() != true) return;

        var progressWindow = new ProgressWindow();
        progressWindow.Title = "正在计算经纬度";
        progressWindow.Show();

        try
        {
            await Task.Run(() =>
            {
                _geoCalculator.CalculateAndExport(_mapData, saveDialog.FileName);
                Dispatcher.Invoke(() => progressWindow.UpdateProgress(100, 100, 0, 0, 100));
            });

            MessageBox.Show($"经纬度换算结果已保存到:\n{saveDialog.FileName}", "成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"经纬度换算失败: {ex.Message}", "错误",
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
            MessageBox.Show(
                "请先设置参考点：\n" +
                "1. 选中格子按Q键添加参考点\n" +
                "2. 或拖动现有标记到目标位置\n" +
                "3. 右键点击标记输入/修改经纬度\n" +
                "4. 需要至少2个参考点",
                "导出参考点", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "保存参考点配置",
            FileName = $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            await Task.Run(() => _geoCalculator.ExportReferencePointsOnly(saveDialog.FileName));
            MessageBox.Show($"参考点配置已保存到:\n{saveDialog.FileName}", "成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OnGeoImportRefAsync()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "导入参考点配置",
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
            MessageBox.Show($"参考点配置已导入:\n{openDialog.FileName}\n共 {rp.Points.Count} 个参考点", "成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败: {ex.Message}", "错误",
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
        if (_debugConsole.IsVisible)
        {
            _debugConsole.HideConsole();
            return;
        }
        Debug.WriteLine("[Keyboard] Esc pressed - showing return confirm dialog");
        if (_confirmDialogShowing) return;
        await ShowReturnConfirmDialog();
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
            Title = "返回确认",
            Message = "是否确认返回上一级？\n未保存的更改将会丢失。",
            ConfirmText = "返回",
            CancelText = "取消"
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
        var pos = e.GetPosition(_skElement);

        var ruler = _renderEngine.GeoRuler;
        if (ruler != null && ruler.HitTestMarker(pos.X, pos.Y, out int markerId))
        {
            var marker = ruler.Markers.FirstOrDefault(m => m.Id == markerId);
            if (marker == null) return;

            using var inputDialog = new Views.Assist.DoubleInputDialog(Window)
            {
                Title = $"设置标记 #{markerId} (R{marker.Row}C{marker.Col})",
                Description1 = "纬度 (Latitude)：",
                Description2 = "经度 (Longitude)：",
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
        var pos = e.GetPosition(_skElement);
        _mouseManager.SetModifierState(GetCurrentKeyModifiers());
        _mouseManager.ProcessMouseUp(MouseButtons.Right, pos.X, pos.Y);
        e.Handled = false;
    }

    private void OnWpfMouseMove(object sender, MouseEventArgs e)
    {
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
                if (TryStartBrushDrag(e)) break;
                HandleSelectionRectStart(e);
                break;
            case MouseActionKind.DragMove when e.Button == MouseButtons.Right:
                if (TryContinueBrushDrag(e)) break;
                HandleSelectionRectMove(e);
                break;
            case MouseActionKind.DragEnd when e.Button == MouseButtons.Right:
                if (TryEndBrushDrag(e)) break;
                HandleSelectionRectEnd(e);
                break;
            case MouseActionKind.Click when e.Button == MouseButtons.Right:
                if (TryBrushClick(e)) break;
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
            return coord => _mapData.GetBelongValue(coord.Col, coord.Row) != 0xFF;
        }
        return null;
    }

    private bool IsBrushModeActive()
    {
        if (!_editModeManager.IsEditModeActive) return false;
        var target = _editModeManager.GetActiveBrushTarget();
        return target != null && target.BrushActive;
    }

    private void UpdateBrushPreview(double screenX, double screenY)
    {
        if (_camera == null || !IsBrushModeActive())
        {
            _renderEngine.HideBrushPreview();
            return;
        }

        var target = _editModeManager.GetActiveBrushTarget();
        if (target == null)
        {
            _renderEngine.HideBrushPreview();
            return;
        }

        var (col, row) = _camera.ScreenToHex(screenX, screenY);
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
            Title = "选择地图文件",
            Filter = "地图文件 (*.bin)|*.bin|战役/征服文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
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
                    var fullSceneName = $"场景 {sceneCount + 1}:{sceneName}";
                    var newSceneId = sceneManager.CreateScene(fullSceneName, dialog.FileName, detectedType);
                    sceneManager.RequestSceneSwitch(newSceneId);
                    return;
                }

                var mapData = ReloadMapData(dialog.FileName);
                if (mapData == null)
                {
                    System.Windows.MessageBox.Show("加载地图文件失败！", "错误");
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
                _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(_commandManager));
                _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
                _fileStateManager.OpenFile(_mapData, dialog.FileName, SceneType);

                var sceneName2 = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                var sceneCount2 = sceneManager2.SceneCount;
                var fullSceneName2 = $"场景 {sceneCount2 + 1}:{sceneName2}";
                _sceneId = sceneManager2.CreateScene(fullSceneName2, dialog.FileName, RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType));
                sceneManager2.SetSceneMapData(_sceneId, _mapData);
                sceneManager2.ActivateScene(_sceneId);

                if (_sceneNameLabel != null)
                    _sceneNameLabel.Text = fullSceneName2;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开文件失败: {ex.Message}", "错误");
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
            filter = "BTL文件|*.btl|BIN文件|*.bin|所有文件|*.*";
            defaultFilterIndex = 1;
        }
        else
        {
            filter = "BIN文件|*.bin|BTL文件|*.btl|所有文件|*.*";
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
            Title = "保存地图文件",
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
                    System.Windows.MessageBox.Show("保存失败！", "错误");
                    return;
                }
                _mapData.FilePath = dialog.FileName;
                _fileStateManager.MarkSaved();
                System.Windows.MessageBox.Show($"地图已保存到:\n{dialog.FileName}", "保存成功");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误");
            }
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show("确定要创建新地图吗？当前未保存的修改将丢失。", "新建地图", MessageBoxButton.YesNo, MessageBoxImage.Question);
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

        var loadItem = new MenuItem { Header = "加载视图层图片...", Foreground = Brushes.White };
        loadItem.Click += ViewLayerLoad_Click;
        menu.Items.Add(loadItem);

        var toggleItem = new MenuItem
        {
            Header = _renderEngine.ViewLayerVisible ? "隐藏视图层" : "显示视图层",
            Foreground = Brushes.White,
            IsEnabled = !string.IsNullOrEmpty(_renderEngine.ViewLayerVisible.ToString())
        };
        toggleItem.Click += ViewLayerToggle_Click;
        menu.Items.Add(toggleItem);

        var clearItem = new MenuItem { Header = "清除视图层", Foreground = Brushes.White };
        clearItem.Click += ViewLayerClear_Click;
        menu.Items.Add(clearItem);

        menu.Items.Add(new Separator());

        var opacityUpItem = new MenuItem { Header = "透明度 +", Foreground = Brushes.White };
        opacityUpItem.Click += (s, ev) =>
        {
            _renderEngine.ViewLayerOpacity = Math.Min(1f, _renderEngine.ViewLayerOpacity + 0.1f);
            _debugConsole.WriteLine($"[视图层] 透明度: {_renderEngine.ViewLayerOpacity:P0}");
            _skElement.InvalidateVisual();
        };
        menu.Items.Add(opacityUpItem);

        var opacityDownItem = new MenuItem { Header = "透明度 -", Foreground = Brushes.White };
        opacityDownItem.Click += (s, ev) =>
        {
            _renderEngine.ViewLayerOpacity = Math.Max(0f, _renderEngine.ViewLayerOpacity - 0.1f);
            _debugConsole.WriteLine($"[视图层] 透明度: {_renderEngine.ViewLayerOpacity:P0}");
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
            Title = "选择视图层图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            if (_renderEngine.LoadViewLayerImage(dialog.FileName))
                _debugConsole.WriteLine($"[视图层] 已加载: {dialog.FileName}");
            else
                _debugConsole.WriteLine($"[视图层] 加载失败: {dialog.FileName}");
            _skElement.InvalidateVisual();
        }
    }

    private void ViewLayerToggle_Click(object sender, RoutedEventArgs e)
    {
        _renderEngine.ViewLayerVisible = !_renderEngine.ViewLayerVisible;
        _debugConsole.WriteLine($"[视图层] 可见性: {(_renderEngine.ViewLayerVisible ? "显示" : "隐藏")}");
        _skElement.InvalidateVisual();
    }

    private void ViewLayerClear_Click(object sender, RoutedEventArgs e)
    {
        _renderEngine.ClearViewLayerImage();
        _debugConsole.WriteLine("[视图层] 已清除");
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
                Text = "暂无打开的场景",
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
                System.Windows.MessageBox.Show("目标场景不存在！", "错误");
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
                System.Windows.MessageBox.Show("加载场景数据失败！", "错误");
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
            System.Windows.MessageBox.Show($"切换场景失败: {ex.Message}", "错误");
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

    #region EditMode Integration

    /// <summary>
    /// 全局按键绑定 - 与模式无关，始终可用
    /// </summary>
    private static readonly (string Id, Key Key, KeyModifiers Mods, string Action, string Desc)[] GlobalKeyDefs =
    {
        ("Global_Space", Key.Space, KeyModifiers.None, "cycle_mode", "切换模式"),
        ("Global_Tab", Key.Tab, KeyModifiers.None, "tab_action", "查看格子信息"),
        ("Global_F1", Key.F1, KeyModifiers.None, "toggle_show_layer2", "切换第二层地形显示"),
        ("Global_CtrlF1", Key.F1, KeyModifiers.Ctrl, "toggle_help_text", "显示/隐藏帮助文本"),
        ("Global_B", Key.B, KeyModifiers.None, "toggle_hex_borders", "显示/隐藏网格"),
        ("Global_N", Key.N, KeyModifiers.None, "toggle_labels", "显示/隐藏标签"),
        ("Global_CtrlZ", Key.Z, KeyModifiers.Ctrl, "undo", "撤销"),
        ("Global_CtrlY", Key.Y, KeyModifiers.Ctrl, "redo", "重做"),
    };

    private void InitializeEditModeKeyBindings()
    {
        // 只注册全局按键 - 模式特有按键由 EditModeManager 在切换模式时动态注册
        foreach (var def in GlobalKeyDefs)
        {
            _keyboardManager.RegisterBinding(def.Id, (int)def.Key, def.Mods,
                () => DispatchEditModeAction(def.Action), def.Desc);
        }
    }

    private void UnregisterEditModeKeyBindings()
    {
        foreach (var def in GlobalKeyDefs)
            _keyboardManager.UnregisterBinding(def.Id);
    }

    private void RegisterTerrainCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("greening", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"绿化平地 (概率={prob}%)", () => terrain.ApplyGreening(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine($"绿化完成，概率={prob}%");
        }, "绿化平地", "<概率0-100>");

        cm.RegisterCommand("random_flat", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"随机平地变体 (概率={prob}%)", () => terrain.RandomizeFlatTerrain(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine($"随机平地变体完成，概率={prob}%");
        }, "随机平地地形变体", "<概率0-100>");

        cm.RegisterCommand("random_variant", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            int prob = 50;
            if (args.Length > 0 && int.TryParse(args[0], out int p))
                prob = Math.Clamp(p, 0, 100);

            _editModeManager.RecordMultiCellChange($"随机当前层变体 (概率={prob}%)", () => terrain.RandomizeVariant(prob));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine($"随机变体完成，编辑层={terrain.EditLayer}，概率={prob}%");
        }, "随机当前层地形变体", "<概率0-100>");

        cm.RegisterCommand("create_coast", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            var selectedHexes = _hexSelector.SelectedHexes;
            var targetHexes = selectedHexes.Count > 0
                ? selectedHexes.Select(h => (h.Col, h.Row)).ToList()
                : null;

            _editModeManager.RecordMultiCellChange("创建海岸线", () => terrain.CreateCoast(targetHexes));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine("海岸线创建完成");
        }, "创建海岸线", "");

        cm.RegisterCommand("process_ocean_layer2", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            var selectedHexes = _hexSelector.SelectedHexes;
            var targetHexes = selectedHexes.Count > 0
                ? selectedHexes.Select(h => (h.Col, h.Row)).ToList()
                : null;

            _editModeManager.RecordMultiCellChange("处理海洋第二层", () => terrain.ProcessOceanSecondLayer(targetHexes));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine("海洋第二层处理完成");
        }, "处理海洋第二层", "");

        cm.RegisterCommand("export_hd", async args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            var result = terrain.ExportHdFile();
            _debugConsole.WriteLine(result.Message ?? "");
        }, "导出HD文件", "");

        cm.RegisterCommand("scale_map", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            double scale = 1.0;
            if (args.Length > 0 && double.TryParse(args[0], out double s))
                scale = Math.Clamp(s, 0.1, 10.0);

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                _debugConsole.WriteLine("缩放比例为1.0，无需调整");
                return;
            }

            _editModeManager.RecordMapResizeChange($"缩放地图 (比例={scale:F2})", () => terrain.ScaleMap(scale));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine($"地图缩放完成，比例={scale:F2}");
        }, "按比例缩放地图", "<比例0.1-10.0>");

        cm.RegisterCommand("resize_map", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }
            var terrain = _editModeManager.GetModifier<TerrainModifier>();
            if (terrain == null) { _debugConsole.WriteLine("未激活地形修改器"); return; }

            if (args.Length < 2)
            {
                _debugConsole.WriteLine("用法: resize_map <direction> <amount> [ocean]");
                _debugConsole.WriteLine("  direction: up, down, left, right");
                _debugConsole.WriteLine("  amount: 正数扩展，负数收缩");
                _debugConsole.WriteLine("  ocean: 可选，扩展时新格子使用海洋");
                return;
            }

            string direction = args[0];
            if (!int.TryParse(args[1], out int amount) || amount == 0)
            {
                _debugConsole.WriteLine("无效的调整量");
                return;
            }

            bool useOcean = args.Length > 2 && args[2].Equals("ocean", StringComparison.OrdinalIgnoreCase);

            _editModeManager.RecordMapResizeChange($"调整地图大小 ({direction}, {amount})", () => terrain.ResizeMap(direction, amount, useOcean));
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
            _debugConsole.WriteLine($"地图大小调整完成，方向={direction}，量={amount}");
        }, "调整地图大小", "<direction> <amount> [ocean]");

        cm.RegisterCommand("geo_add_ref", args =>
        {
            if (args.Length < 4)
            {
                _debugConsole.WriteLine("用法: geo_add_ref <row> <col> <lat> <lon>");
                _debugConsole.WriteLine("  row: 行号");
                _debugConsole.WriteLine("  col: 列号");
                _debugConsole.WriteLine("  lat: 纬度");
                _debugConsole.WriteLine("  lon: 经度");
                return;
            }

            if (!int.TryParse(args[0], out int row) || !int.TryParse(args[1], out int col) ||
                !double.TryParse(args[2], out double lat) || !double.TryParse(args[3], out double lon))
            {
                _debugConsole.WriteLine("参数格式错误");
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            rp.Points.Add(new Core.Geo.GeoReferencePoint { Row = row, Col = col, Latitude = lat, Longitude = lon });

            var ruler = _renderEngine.GeoRuler;
            if (ruler != null)
            {
                ruler.AddMarker(row, col, lat, lon);
            }

            _debugConsole.WriteLine($"已添加参考点: R{row}C{col} = ({lat:F7}, {lon:F7})");
            _skElement.InvalidateVisual();
        }, "添加经纬度参考点", "<row> <col> <lat> <lon>");

        cm.RegisterCommand("geo_remove_ref", args =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int index) || index < 1)
            {
                _debugConsole.WriteLine("用法: geo_remove_ref <index>");
                _debugConsole.WriteLine("  index: 参考点序号（从1开始）");
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            if (index > rp.Points.Count)
            {
                _debugConsole.WriteLine($"参考点序号 {index} 超出范围，当前共有 {rp.Points.Count} 个参考点");
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

            _debugConsole.WriteLine($"已删除参考点 {index}: R{removed.Row}C{removed.Col}");
            _skElement.InvalidateVisual();
        }, "删除经纬度参考点", "<index>");

        cm.RegisterCommand("geo_calc", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }

            var rp = _geoCalculator.ReferencePoints;
            if (!rp.IsValid())
            {
                _debugConsole.WriteLine($"参考点设置不完整，当前只有 {rp.Points.Count} 个参考点，至少需要2个");
                return;
            }

            string outputPath = args.Length > 0 ? args[0] : $"geo_coords_{DateTime.Now:yyyyMMddHHmmss}.json";

            try
            {
                _geoCalculator.CalculateAndExport(_mapData, outputPath);
                _debugConsole.WriteLine($"经纬度换算完成，结果已保存到: {outputPath}");
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine($"经纬度换算失败: {ex.Message}");
            }
        }, "计算并导出经纬度", "[输出文件路径]");

        cm.RegisterCommand("geo_show_ref", _ =>
        {
            var rp = _geoCalculator.ReferencePoints;
            var ruler = _renderEngine.GeoRuler;
            _debugConsole.WriteLine($"参考点数量: {rp.Points.Count}");
            for (int i = 0; i < rp.Points.Count; i++)
            {
                var p = rp.Points[i];
                _debugConsole.WriteLine($"  参考点{i + 1}: R{p.Row}C{p.Col}, 纬度={p.Latitude:F7}, 经度={p.Longitude:F7}");
            }
            _debugConsole.WriteLine($"参考点有效: {rp.IsValid()}");
            if (ruler != null)
            {
                _debugConsole.WriteLine($"标尺标记数: {ruler.Markers.Count}");
                foreach (var m in ruler.Markers)
                {
                    _debugConsole.WriteLine($"  标记#{m.Id}: R{m.Row}C{m.Col}, 纬度={m.Latitude:F7}, 经度={m.Longitude:F7}");
                }
            }
        }, "显示当前经纬度参考点", "");

        cm.RegisterCommand("geo_set_lat", args =>
        {
            if (args.Length < 1 || !double.TryParse(args[0], out double lat))
            {
                _debugConsole.WriteLine("用法: geo_set_lat <latitude>");
                _debugConsole.WriteLine("  latitude: 地图中心纬度（度），用于经度间距修正");
                _debugConsole.WriteLine("  例如: geo_set_lat 45.0");
                _debugConsole.WriteLine($"  当前值: {_geoCalculator.CentralLatitude?.ToString("F4") ?? "自动估算"}");
                return;
            }

            _geoCalculator.CentralLatitude = lat;
            _debugConsole.WriteLine($"已设置地图中心纬度: {lat:F4}°");
            _debugConsole.WriteLine("经度换算将使用球面修正（当参考点纬度跨度>5度时自动启用）");
        }, "设置地图中心纬度（用于球面修正）", "<latitude>");

        cm.RegisterCommand("geo_export_ref", args =>
        {
            string fileName = args.Length > 0 ? args[0] : $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json";
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            try
            {
                _geoCalculator.ExportReferencePointsOnly(path);
                _debugConsole.WriteLine($"参考点已导出: {path}");
                _debugConsole.WriteLine($"参考点数量: {_geoCalculator.ReferencePoints.Points.Count}");
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine($"导出失败: {ex.Message}");
            }
        }, "导出参考点配置到 JSON", "[filename]");

        cm.RegisterCommand("geo_import_ref", args =>
        {
            if (args.Length < 1)
            {
                _debugConsole.WriteLine("用法: geo_import_ref <filename>");
                _debugConsole.WriteLine("  例如: geo_import_ref geo_ref_20260818.json");
                return;
            }

            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, args[0]);
            if (!System.IO.File.Exists(path))
            {
                _debugConsole.WriteLine($"文件不存在: {path}");
                return;
            }

            try
            {
                _geoCalculator.ImportReferencePoints(path);
                var rp = _geoCalculator.ReferencePoints;
                _debugConsole.WriteLine($"参考点已导入: {path}");
                _debugConsole.WriteLine($"参考点数量: {rp.Points.Count}");

                // 同步到标尺
                var ruler = _renderEngine.GeoRuler;
                if (ruler != null)
                {
                    ruler.ClearMarkers();
                    foreach (var p in rp.Points)
                    {
                        ruler.AddMarker(p.Row, p.Col, p.Latitude, p.Longitude);
                    }
                    _debugConsole.WriteLine("标尺已同步更新");
                }
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine($"导入失败: {ex.Message}");
            }
        }, "从 JSON 导入参考点配置", "<filename>");

        cm.RegisterCommand("geo_ruler", _ =>
        {
            _debugConsole.WriteLine("经纬度标尺始终显示，无需切换");
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
        UpdateOverlayFromEditMode();
        UpdateLayerInfoVisibility();
        UpdateRenderLayersByMode(e.CurrentMode);
        _skElement.InvalidateVisual();
    }

    private void UpdateRenderLayersByMode(EditMode mode)
    {
        _renderEngine.EnableProvinceRender = mode == EditMode.ProvinceEdit;
        _renderEngine.EnableProvinceCapitalRender = mode == EditMode.ProvinceEdit;

        // 地形编辑模式下显示建筑但不显示建筑名称
        _renderEngine.EnableBuildingRender = mode == EditMode.BuildingDeploy || mode == EditMode.BelongEdit || mode == EditMode.TerrainPaint;
        _renderEngine.ShowBuildingNames = mode != EditMode.TerrainPaint;

        _renderEngine.EnableArmyRender = false;
        _renderEngine.EnableTrapRender = false;

        _renderEngine.EnableLegionDomainRender = mode == EditMode.BuildingDeploy || mode == EditMode.BelongEdit;
        _renderEngine.EnableBelongFlagRender = mode == EditMode.BuildingDeploy || mode == EditMode.BelongEdit;

        if ((mode == EditMode.BuildingDeploy || mode == EditMode.BelongEdit) && _mapData != null)
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
                _layerInfoLabel.Text = $"编辑层: {terrainModifier.EditLayer}";
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

        // 更新地形编辑层显示
        if (_layerInfoLabel != null && _editModeManager.CurrentMode == EditMode.TerrainPaint)
        {
            if (message.StartsWith("编辑层:"))
            {
                _layerInfoLabel.Text = message;
                _layerInfoLabel.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnEditModeDataModified(object? sender, EventArgs e)
    {
        _renderEngine.InvalidateTerrainCache();
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
                _editModeManager.RaiseStatusMessage($"画笔已开启 - 右键绘制省份，半径: {province.Brush.Radius}");
            }
            else
            {
                if (_brushSettingsWindow != null)
                    _brushSettingsWindow.PlayFadeOutAndHide();
                _renderEngine.HideBrushPreview();
                _skElement.InvalidateVisual();
                _editModeManager.RaiseStatusMessage("画笔已关闭");
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
                _editModeManager.RaiseStatusMessage($"画笔已开启 - 右键绘制归属，半径: {brush.Radius}");
            }
            else
            {
                if (_brushSettingsWindow != null)
                    _brushSettingsWindow.PlayFadeOutAndHide();
                _renderEngine.HideBrushPreview();
                _skElement.InvalidateVisual();
                _editModeManager.RaiseStatusMessage("画笔已关闭");
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
            _editModeManager.RaiseStatusMessage("画笔已开启 - 右键绘制地形");
        }
        else
        {
            if (_brushSettingsWindow != null)
                _brushSettingsWindow.PlayFadeOutAndHide();
            _editModeManager.RaiseStatusMessage("画笔已关闭");
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

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushSize = e.BrushSize;
        terrain.BrushTerrainType = e.TerrainType;
        terrain.BrushDecoration = e.TerrainVariant;
        terrain.EditLayer = e.EditLayer;
        terrain.BrushShape = e.BrushShape;
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
    }

    #endregion

    private async Task<(bool success, int modifiedCount)> RecognizeTerrainFromViewLayer()
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return (false, 0);

        var provider = _renderEngine.ViewLayerImageProvider;
        if (provider == null)
        {
            _editModeManager.RaiseStatusMessage("当前渲染引擎不支持视图层图片访问");
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
        _renderEngine?.Dispose();
    }
}