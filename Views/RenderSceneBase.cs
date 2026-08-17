using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Geo;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.SceneManagement;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.World;
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

    private StackPanel? _sceneTabPanel;
    private Popup? _sceneTabPopup;
    private TextBlock? _sceneNameLabel;

    private readonly MouseManager _mouseManager = MouseManager.Instance;
    private readonly KeyboardManager _keyboardManager = KeyboardManager.Instance;
    private readonly DebugConsole _debugConsole = DebugConsole.Instance;
    private readonly EditModeManager _editModeManager = EditModeManager.Instance;
    private readonly FileStateManager _fileStateManager = new();
    private Grid? _titleBar;

    protected MainRender RenderEngine => _renderEngine;

    protected abstract string SceneTitle { get; }
    protected abstract string SceneType { get; }
    protected abstract MapData? LoadMapData();
    protected abstract void InitializeRenderers();

    protected RenderSceneBase(MainWindow window)
    {
        Window = window;
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
        _renderEngine = new MainRender(RenderEngineFactory.Create());

        _mapData = LoadMapData();
        if (_mapData == null)
        {
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

        InitializeRenderers();

        _renderEngine.Initialize(IntPtr.Zero, (int)_skElement.ActualWidth, (int)_skElement.ActualHeight);
        _renderEngine.Resize((int)_skElement.ActualWidth, (int)_skElement.ActualHeight);

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
        HexSelector.Instance.SelectionChanged += OnHexSelectionChanged;
        HexSelector.Instance.SelectionRectChanged += OnSelectionRectChanged;

        _keyboardManager.RegisterBinding("EscBack", (int)Key.Escape, KeyModifiers.None, OnEscPressed, "返回主场景");
        _keyboardManager.RegisterBinding("ToggleConsole", (int)Key.F3, KeyModifiers.None, OnToggleConsole, "调试控制台");
        _keyboardManager.RegisterBinding("Screenshot", (int)Key.F2, KeyModifiers.None, OnScreenshot, "截图");

        InitializeEditModeKeyBindings();

        _editModeManager.Initialize(_mapData);
        _editModeManager.SetUndoManager(_fileStateManager.UndoManager);
        _editModeManager.SetDialogService(new Services.WpfDialogService(() => System.Windows.Window.GetWindow(this)!));
        _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(Core.Commands.CommandManager.Instance));
        _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
        _editModeManager.ModeChanged += OnEditModeChanged;
        _editModeManager.StatusMessageChanged += OnEditModeStatusMessageChanged;
        _editModeManager.DataModified += OnEditModeDataModified;
        _editModeManager.BrushToggled += OnBrushToggled;

        _fileStateManager.OpenFile(_mapData, _mapData.FilePath, SceneType);

        var availableModes = _editModeManager.GetAvailableModes(SceneType);
        if (availableModes.Count > 0)
            _editModeManager.SwitchMode(availableModes[0]);

        UpdateOverlayFromEditMode();

        _keyboardManager.KeyDown += OnKeyboardManagerKeyDown;

        _debugConsole.InvalidateCallback = () => _skElement.InvalidateVisual();
        _debugConsole.InvokeOnUiThread = action => Dispatcher.Invoke(action);
        _debugConsole.VisibilityChanged += isVisible =>
        {
            if (_titleBar != null)
                _titleBar.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        };
        _debugConsole.StartCursorTimer();
        Core.Commands.CommandManager.Instance.SetContext(new CommandContext { MapData = _mapData });
        Core.Commands.CommandManager.Instance.RegisterCommand("undo", _ =>
        {
            if (_fileStateManager.Undo()) { _editModeManager_DataModifiedFromUndo(); }
            else { _debugConsole.WriteLine("无法撤销"); }
        }, "撤销上一步操作");
        Core.Commands.CommandManager.Instance.RegisterCommand("redo", _ =>
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
        if (sceneManager.CurrentSceneId < 0 && _mapData != null)
        {
            var sceneName = !string.IsNullOrEmpty(_mapData.FilePath)
                ? System.IO.Path.GetFileNameWithoutExtension(_mapData.FilePath)
                : SceneTitle;
            _sceneId = sceneManager.CreateScene(sceneName, _mapData.FilePath);
            sceneManager.SetSceneMapData(_sceneId, _mapData);
            sceneManager.ActivateScene(_sceneId);

            if (_sceneNameLabel != null)
                _sceneNameLabel.Text = sceneName;
        }
        else if (sceneManager.CurrentSceneId >= 0)
        {
            _sceneId = sceneManager.CurrentSceneId;
        }

        _skElement.InvalidateVisual();
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
        HexSelector.Instance.SelectionChanged -= OnHexSelectionChanged;
        HexSelector.Instance.SelectionRectChanged -= OnSelectionRectChanged;
        _keyboardManager.UnregisterBinding("EscBack");
        _keyboardManager.UnregisterBinding("ToggleConsole");
        UnregisterEditModeKeyBindings();
        _keyboardManager.KeyDown -= OnKeyboardManagerKeyDown;
        _editModeManager.ModeChanged -= OnEditModeChanged;
        _editModeManager.StatusMessageChanged -= OnEditModeStatusMessageChanged;
        _editModeManager.DataModified -= OnEditModeDataModified;
        _editModeManager.BrushToggled -= OnBrushToggled;
        _editModeManager.Deinitialize();
        _debugConsole.InvalidateCallback = null;

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

        _debugConsole.Render(canvas, info.Width, info.Height);
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
        if (_debugConsole.IsVisible && !string.IsNullOrEmpty(e.Text))
        {
            _debugConsole.HandleTextInput(e.Text);
            e.Handled = true;
        }
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
        if (!_debugConsole.IsVisible) return;

        var key = (System.Windows.Input.Key)e.KeyCode;

        if (key == System.Windows.Input.Key.F3)
            return;

        e.Handled = true;

        bool isControlKey = key is
            System.Windows.Input.Key.Enter or
            System.Windows.Input.Key.Up or
            System.Windows.Input.Key.Down or
            System.Windows.Input.Key.Left or
            System.Windows.Input.Key.Right or
            System.Windows.Input.Key.Escape or
            System.Windows.Input.Key.PageUp or
            System.Windows.Input.Key.PageDown or
            System.Windows.Input.Key.Home or
            System.Windows.Input.Key.End or
            System.Windows.Input.Key.Back or
            System.Windows.Input.Key.Delete or
            System.Windows.Input.Key.Tab or
            System.Windows.Input.Key.F1 or
            System.Windows.Input.Key.F2 or
            System.Windows.Input.Key.F3 or
            System.Windows.Input.Key.F4 or
            System.Windows.Input.Key.F5 or
            System.Windows.Input.Key.F6 or
            System.Windows.Input.Key.F7 or
            System.Windows.Input.Key.F8 or
            System.Windows.Input.Key.F9 or
            System.Windows.Input.Key.F10 or
            System.Windows.Input.Key.F11 or
            System.Windows.Input.Key.F12 or
            System.Windows.Input.Key.CapsLock or
            System.Windows.Input.Key.NumLock or
            System.Windows.Input.Key.Scroll or
            System.Windows.Input.Key.LeftShift or
            System.Windows.Input.Key.RightShift or
            System.Windows.Input.Key.LeftCtrl or
            System.Windows.Input.Key.RightCtrl or
            System.Windows.Input.Key.LeftAlt or
            System.Windows.Input.Key.RightAlt or
            System.Windows.Input.Key.LWin or
            System.Windows.Input.Key.RWin or
            System.Windows.Input.Key.PrintScreen or
            System.Windows.Input.Key.Pause or
            System.Windows.Input.Key.Insert or
            System.Windows.Input.Key.System;

        if (!isControlKey)
        {
            bool shift = (e.Modifiers & Core.Input.KeyModifiers.Shift) != 0;
            bool capsLock = System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock);
            char? ch = KeyToChar(key, shift, capsLock);
            if (ch.HasValue)
                _debugConsole.HandleTextInput(ch.Value.ToString());
        }

        _debugConsole.HandleKeyDown(e.KeyCode, e.KeyCode);
        _skElement.InvalidateVisual();
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
        _debugConsole.Toggle();
    }

    private async void OnScreenshot()
    {
        await CaptureMapScreenshotAsync();
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
                "1. 拖动顶部/左侧标记到目标位置\n" +
                "2. 右键点击标记输入经纬度\n" +
                "3. 需要至少设置两个行参考点和两个列参考点",
                "经纬度换算", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!rp.IsValid())
        {
            MessageBox.Show(
                "参考点设置不完整，需要两个不同的行参考点和两个不同的列参考点。\n" +
                $"当前: 行1={rp.RowRef1}(纬{rp.LatRef1}), 行2={rp.RowRef2}(纬{rp.LatRef2})\n" +
                $"      列1={rp.ColRef1}(经{rp.LonRef1}), 列2={rp.ColRef2}(经{rp.LonRef2})",
                "参考点不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var primary = HexSelector.Instance.PrimarySelected;
        if (primary.HasValue && _mapData != null)
            _hexInfoWindow.UpdateHexInfo(primary.Value.Col, primary.Value.Row);

        _hexInfoWindow.Show();
        RestoreFocus();
    }

    private async void OnEscPressed()
    {
        if (_debugConsole.IsVisible) return;
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
            ruler.DraggingMarker = markerId;
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
        if (ruler != null && ruler.DraggingMarker.HasValue)
        {
            ruler.SnapDraggingMarkerToNearest(pos.X, pos.Y);
            ruler.DraggingMarker = null;
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
            if (markerId == 1)
            {
                using var inputDialog = new SingleInputDialog(Window)
                {
                    Title = $"设置顶部标记1（第 {ruler.TopMarker1Col} 列）的经度",
                    Description = "请输入经度值（如：116.4074）：",
                    DefaultValue = ruler.TopMarker1Lon?.ToString("F7") ?? ""
                };
                var result = await inputDialog.ShowAsync();
                if (result != null && double.TryParse(result, out double lon))
                {
                    ruler.TopMarker1Lon = lon;
                    UpdateGeoReferenceFromRuler();
                    _skElement.InvalidateVisual();
                }
            }
            else if (markerId == 2)
            {
                using var inputDialog = new SingleInputDialog(Window)
                {
                    Title = $"设置顶部标记2（第 {ruler.TopMarker2Col} 列）的经度",
                    Description = "请输入经度值（如：116.4074）：",
                    DefaultValue = ruler.TopMarker2Lon?.ToString("F7") ?? ""
                };
                var result = await inputDialog.ShowAsync();
                if (result != null && double.TryParse(result, out double lon))
                {
                    ruler.TopMarker2Lon = lon;
                    UpdateGeoReferenceFromRuler();
                    _skElement.InvalidateVisual();
                }
            }
            else if (markerId == 3)
            {
                using var inputDialog = new SingleInputDialog(Window)
                {
                    Title = $"设置左侧标记1（第 {ruler.LeftMarker1Row} 行）的纬度",
                    Description = "请输入纬度值（如：39.9042）：",
                    DefaultValue = ruler.LeftMarker1Lat?.ToString("F7") ?? ""
                };
                var result = await inputDialog.ShowAsync();
                if (result != null && double.TryParse(result, out double lat))
                {
                    ruler.LeftMarker1Lat = lat;
                    UpdateGeoReferenceFromRuler();
                    _skElement.InvalidateVisual();
                }
            }
            else if (markerId == 4)
            {
                using var inputDialog = new SingleInputDialog(Window)
                {
                    Title = $"设置左侧标记2（第 {ruler.LeftMarker2Row} 行）的纬度",
                    Description = "请输入纬度值（如：39.9042）：",
                    DefaultValue = ruler.LeftMarker2Lat?.ToString("F7") ?? ""
                };
                var result = await inputDialog.ShowAsync();
                if (result != null && double.TryParse(result, out double lat))
                {
                    ruler.LeftMarker2Lat = lat;
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

        rp.RowRef1 = ruler.LeftMarker1Row;
        rp.LatRef1 = ruler.LeftMarker1Lat ?? 0;
        rp.RowRef2 = ruler.LeftMarker2Row;
        rp.LatRef2 = ruler.LeftMarker2Lat ?? 0;
        rp.ColRef1 = ruler.TopMarker1Col;
        rp.LonRef1 = ruler.TopMarker1Lon ?? 0;
        rp.ColRef2 = ruler.TopMarker2Col;
        rp.LonRef2 = ruler.TopMarker2Lon ?? 0;
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
        if (ruler != null && ruler.DraggingMarker.HasValue)
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
                if (_debugConsole.IsVisible)
                    _debugConsole.HandleMouseWheel(e.WheelDelta);
                else
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
        var selector = HexSelector.Instance;
        selector.ClearSelection();
        selector.SetSelectionMoving(false, 0, 0, null);
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
        HexSelector.Instance.BeginSelectionRect(e.Position.X, e.Position.Y);
    }

    private void HandleSelectionRectMove(MouseActionEventArgs e)
    {
        HexSelector.Instance.UpdateSelectionRect(e.Position.X, e.Position.Y);
    }

    private void HandleSelectionRectEnd(MouseActionEventArgs e)
    {
        if (_mapData == null || _camera == null) return;

        var filter = GetSelectionFilter();
        var matched = HexSelector.Instance.EndSelectionRect(
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
                    HexSelector.Instance.AddToSelection(coord);
            }
            else if (ctrlPressed)
            {
                foreach (var coord in matched)
                    HexSelector.Instance.RemoveFromSelection(coord);
            }
            else
            {
                HexSelector.Instance.SetSelection(matched);
            }
        }
    }

    private void HandleRightClickSelect(MouseActionEventArgs e)
    {
        if (_mapData == null || _camera == null) return;
        var (col, row) = _camera.ScreenToHex(e.Position.X, e.Position.Y);
        var coord = new HexCoord(col, row);
        var filter = GetSelectionFilter();
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;
        if (filter != null && !filter(coord)) return;

        bool shiftPressed = (e.Modifiers & Core.Input.KeyModifiers.Shift) != 0;
        bool ctrlPressed = (e.Modifiers & Core.Input.KeyModifiers.Ctrl) != 0;

        if (shiftPressed)
        {
            HexSelector.Instance.AddToSelection(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }
        else if (ctrlPressed)
        {
            HexSelector.Instance.RemoveFromSelection(coord);
        }
        else
        {
            HexSelector.Instance.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
        }
    }

    protected virtual Func<HexCoord, bool>? GetSelectionFilter() => null;

    private bool IsBrushModeActive()
    {
        if (!_editModeManager.IsEditModeActive || _editModeManager.CurrentMode != EditMode.TerrainPaint)
            return false;
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        return terrain != null && terrain.BrushActive;
    }

    private void UpdateBrushPreview(double screenX, double screenY)
    {
        if (_camera == null || !IsBrushModeActive())
        {
            _renderEngine.HideBrushPreview();
            return;
        }

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null)
        {
            _renderEngine.HideBrushPreview();
            return;
        }

        var (col, row) = _camera.ScreenToHex(screenX, screenY);
        _renderEngine.SetBrushPreview(
            col, row,
            terrain.BrushSize, terrain.BrushShape,
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

        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        var (col, row) = _camera.ScreenToHex(pos.X, pos.Y);
        if (col < 0 || col >= _mapData.MapWidth || row < 0 || row >= _mapData.MapHeight) return;
        if (col == _brushLastPaintedCell.col && row == _brushLastPaintedCell.row) return;
        _brushLastPaintedCell = (col, row);

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
            Filter = "地图文件 (*.bin)|*.bin|战役文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
            InitialDirectory = System.IO.Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var mapData = WorldParser.LoadFromFile(dialog.FileName);
                if (mapData == null)
                {
                    System.Windows.MessageBox.Show("加载地图文件失败！", "错误");
                    return;
                }

                var sceneManager = RenderSceneManager.Instance;

                if (_sceneId >= 0)
                {
                    sceneManager.CacheSceneToDisk(_sceneId);
                    sceneManager.ReleaseSceneMemory(_sceneId);
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
                _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(Core.Commands.CommandManager.Instance));
                _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
                _fileStateManager.OpenFile(_mapData, dialog.FileName, SceneType);

                var sceneName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                var sceneCount = sceneManager.SceneCount;
                var fullSceneName = $"场景 {sceneCount + 1}:{sceneName}";
                _sceneId = sceneManager.CreateScene(fullSceneName, dialog.FileName);
                sceneManager.SetSceneMapData(_sceneId, _mapData);
                sceneManager.ActivateScene(_sceneId);

                if (_sceneNameLabel != null)
                    _sceneNameLabel.Text = fullSceneName;
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

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存地图文件",
            Filter = "BIN文件|*.bin|所有文件|*.*",
            FileName = !string.IsNullOrEmpty(_mapData.FilePath)
                ? System.IO.Path.GetFileName(_mapData.FilePath)
                : $"new_map_{_mapData.MapWidth}x{_mapData.MapHeight}.bin",
            InitialDirectory = System.IO.Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                WorldParser.SaveToFile(_mapData, dialog.FileName);
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
        _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(Core.Commands.CommandManager.Instance));
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
            var btn = new Button
            {
                Content = scene.SceneName,
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
            _editModeManager.SetCliCommandExecutor(new Services.WpfCliCommandExecutor(Core.Commands.CommandManager.Instance));
            _editModeManager.SetRecognizeTerrainCallback(RecognizeTerrainFromViewLayer);
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

    private static readonly (string Id, Key Key, KeyModifiers Mods, string Action, string Desc)[] EditModeKeyDefs =
    {
        ("EditMode_Space", Key.Space, KeyModifiers.None, "cycle_mode", "切换模式"),
        ("EditMode_Tab", Key.Tab, KeyModifiers.None, "tab_action", "查看格子信息"),
        ("EditMode_BracketOpen", Key.OemOpenBrackets, KeyModifiers.None, "decrease_type", "上一个地形类型"),
        ("EditMode_BracketClose", Key.OemCloseBrackets, KeyModifiers.None, "increase_type", "下一个地形类型"),
        ("EditMode_ShiftBracketOpen", Key.OemOpenBrackets, KeyModifiers.Shift, "decrease_decoration", "上一个变体"),
        ("EditMode_ShiftBracketClose", Key.OemCloseBrackets, KeyModifiers.Shift, "increase_decoration", "下一个变体"),
        ("EditMode_H", Key.H, KeyModifiers.None, "toggle_brush", "切换画笔"),
        ("EditMode_C", Key.C, KeyModifiers.None, "copy", "复制"),
        ("EditMode_V", Key.V, KeyModifiers.None, "paste", "粘贴"),
        ("EditMode_O", Key.O, KeyModifiers.None, "remove_ocean_from_selection", "剔除海洋格子"),
        ("EditMode_Y", Key.Y, KeyModifiers.None, "set_river", "绘制河流"),
        ("EditMode_P", Key.P, KeyModifiers.None, "recognize_terrain", "识别地形"),
        ("EditMode_U", Key.U, KeyModifiers.None, "greening", "绿化平地"),
        ("EditMode_R", Key.R, KeyModifiers.None, "randomize_flat", "随机平地变体"),
        ("EditMode_ShiftR", Key.R, KeyModifiers.Shift, "randomize_variant", "随机当前层变体"),
        ("EditMode_F", Key.F, KeyModifiers.None, "flood_fill", "洪水填充"),
        ("EditMode_F4", Key.F4, KeyModifiers.None, "create_coast", "创建海岸线"),
        ("EditMode_F5", Key.F5, KeyModifiers.None, "process_ocean_layer2", "处理海洋第二层"),
        ("EditMode_F6", Key.F6, KeyModifiers.None, "export_hd", "导出HD文件"),
        ("EditMode_T", Key.T, KeyModifiers.None, "connect_buildings", "连接建筑"),
        ("EditMode_G", Key.G, KeyModifiers.None, "scale_map", "按比例缩放地图"),
        ("EditMode_I", Key.I, KeyModifiers.None, "ijkl_action", "I键-调整地图/移动选区"),
        ("EditMode_J", Key.J, KeyModifiers.None, "jkl_action", "J键-调整地图/移动选区"),
        ("EditMode_K", Key.K, KeyModifiers.None, "kl_action", "K键-调整地图/移动选区"),
        ("EditMode_Enter", Key.Enter, KeyModifiers.None, "confirm_selection_move", "确认选区移动"),
        ("EditMode_Escape", Key.Escape, KeyModifiers.None, "cancel_selection_move", "取消选区移动"),
        ("EditMode_Z", Key.Z, KeyModifiers.None, "toggle_layer", "切换编辑层"),
        ("EditMode_B", Key.B, KeyModifiers.None, "toggle_hex_borders", "显示/隐藏网格"),
        ("EditMode_N", Key.N, KeyModifiers.None, "toggle_labels", "显示/隐藏标签"),
        ("EditMode_F1", Key.F1, KeyModifiers.None, "toggle_show_layer2", "切换第二层地形显示"),
        ("EditMode_CtrlF1", Key.F1, KeyModifiers.Ctrl, "toggle_help_text", "显示/隐藏帮助文本"),
        ("EditMode_CtrlZ", Key.Z, KeyModifiers.Ctrl, "undo", "撤销"),
        ("EditMode_CtrlY", Key.Y, KeyModifiers.Ctrl, "redo", "重做"),
        ("EditMode_Delete", Key.Delete, KeyModifiers.None, "remove", "删除"),
        ("EditMode_Q", Key.Q, KeyModifiers.None, "prev_legion", "上一个军团"),
        ("EditMode_E", Key.E, KeyModifiers.None, "next_legion", "下一个军团"),
        ("EditMode_S", Key.S, KeyModifiers.None, "set_province", "设置省份"),
        ("EditMode_X", Key.X, KeyModifiers.None, "clear_province", "清除省份"),
        ("EditMode_L", Key.L, KeyModifiers.None, "l_action", "L键-调整地图/移动选区/设军团"),
        ("EditMode_Apply", Key.OemPlus, KeyModifiers.None, "apply", "应用"),
        ("EditMode_F7", Key.F7, KeyModifiers.None, "geo_calculate", "经纬度换算"),
    };

    private void InitializeEditModeKeyBindings()
    {
        foreach (var def in EditModeKeyDefs)
        {
            _keyboardManager.RegisterBinding(def.Id, (int)def.Key, def.Mods,
                () => DispatchEditModeAction(def.Action), def.Desc);
        }
    }

    private void UnregisterEditModeKeyBindings()
    {
        foreach (var def in EditModeKeyDefs)
            _keyboardManager.UnregisterBinding(def.Id);
    }

    private void RegisterTerrainCliCommands()
    {
        var cm = Core.Commands.CommandManager.Instance;

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

            var selectedHexes = WC4MapEditor.Core.Selection.HexSelector.Instance.SelectedHexes;
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

            var selectedHexes = WC4MapEditor.Core.Selection.HexSelector.Instance.SelectedHexes;
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
            _debugConsole.WriteLine(result.Message);
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

        cm.RegisterCommand("geo_set_ref", args =>
        {
            if (args.Length < 4)
            {
                _debugConsole.WriteLine("用法: geo_set_ref <row|col> <index> <value> <ref_num>");
                _debugConsole.WriteLine("  row|col: 设置行参考还是列参考");
                _debugConsole.WriteLine("  index: 行号或列号");
                _debugConsole.WriteLine("  value: 纬度或经度值");
                _debugConsole.WriteLine("  ref_num: 1或2，第一个或第二个参考点");
                return;
            }

            string type = args[0].ToLower();
            if (!int.TryParse(args[1], out int index) || !double.TryParse(args[2], out double value) || !int.TryParse(args[3], out int refNum) || (refNum != 1 && refNum != 2))
            {
                _debugConsole.WriteLine("参数格式错误");
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            var ruler = _renderEngine.GeoRuler;

            if (type == "row")
            {
                if (refNum == 1) { rp.RowRef1 = index; rp.LatRef1 = value; if (ruler != null) { ruler.LeftMarker1Row = index; ruler.LeftMarker1Lat = value; } }
                else { rp.RowRef2 = index; rp.LatRef2 = value; if (ruler != null) { ruler.LeftMarker2Row = index; ruler.LeftMarker2Lat = value; } }
                _debugConsole.WriteLine($"行参考点{refNum}: 行={index}, 纬度={value:F7}");
            }
            else if (type == "col")
            {
                if (refNum == 1) { rp.ColRef1 = index; rp.LonRef1 = value; if (ruler != null) { ruler.TopMarker1Col = index; ruler.TopMarker1Lon = value; } }
                else { rp.ColRef2 = index; rp.LonRef2 = value; if (ruler != null) { ruler.TopMarker2Col = index; ruler.TopMarker2Lon = value; } }
                _debugConsole.WriteLine($"列参考点{refNum}: 列={index}, 经度={value:F7}");
            }
            else
            {
                _debugConsole.WriteLine("类型必须是 row 或 col");
            }

            _skElement.InvalidateVisual();
        }, "设置经纬度参考点", "<row|col> <index> <value> <1|2>");

        cm.RegisterCommand("geo_calc", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine("地图数据未加载"); return; }

            var rp = _geoCalculator.ReferencePoints;
            if (!rp.IsValid())
            {
                _debugConsole.WriteLine("参考点设置不完整，需要两个不同的行参考点和两个不同的列参考点");
                _debugConsole.WriteLine($"当前: 行1={rp.RowRef1}(纬{rp.LatRef1:F7}), 行2={rp.RowRef2}(纬{rp.LatRef2:F7})");
                _debugConsole.WriteLine($"      列1={rp.ColRef1}(经{rp.LonRef1:F7}), 列2={rp.ColRef2}(经{rp.LonRef2:F7})");
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
            _debugConsole.WriteLine($"行参考点1: 行={rp.RowRef1}, 纬度={rp.LatRef1:F7}");
            _debugConsole.WriteLine($"行参考点2: 行={rp.RowRef2}, 纬度={rp.LatRef2:F7}");
            _debugConsole.WriteLine($"列参考点1: 列={rp.ColRef1}, 经度={rp.LonRef1:F7}");
            _debugConsole.WriteLine($"列参考点2: 列={rp.ColRef2}, 经度={rp.LonRef2:F7}");
            _debugConsole.WriteLine($"参考点有效: {rp.IsValid()}");
            if (ruler != null)
            {
                _debugConsole.WriteLine($"标尺: 顶部标记1(C{ruler.TopMarker1Col}, {ruler.TopMarker1Lon:F7}), 顶部标记2(C{ruler.TopMarker2Col}, {ruler.TopMarker2Lon:F7})");
                _debugConsole.WriteLine($"      左侧标记1(R{ruler.LeftMarker1Row}, {ruler.LeftMarker1Lat:F7}), 左侧标记2(R{ruler.LeftMarker2Row}, {ruler.LeftMarker2Lat:F7})");
            }
        }, "显示当前经纬度参考点", "");

        cm.RegisterCommand("geo_ruler", _ =>
        {
            _debugConsole.WriteLine("经纬度标尺始终显示，无需切换");
        }, "经纬度标尺状态（始终显示）", "");
    }

    private async Task HandleIjklAction(string action)
    {
        var selector = HexSelector.Instance;
        bool hasSelection = selector.SelectedHexes.Count > 0;

        if (_editModeManager.CurrentMode == EditMode.TerrainPaint)
        {
            if (hasSelection)
            {
                string moveAction = action switch
                {
                    "ijkl_action" => "move_selection_up",
                    "jkl_action" => "move_selection_left",
                    "kl_action" => "move_selection_down",
                    "l_action" => "move_selection_right",
                    _ => action
                };
                if (_mapData != null && _camera != null)
                {
                    var p = selector.PrimarySelected;
                    await _editModeManager.HandleKeyAction(moveAction, p?.Col ?? 0, p?.Row ?? 0);
                    _skElement.InvalidateVisual();
                }
            }
            else
            {
                string resizeAction = action switch
                {
                    "ijkl_action" => "resize_map_up",
                    "jkl_action" => "resize_map_left",
                    "kl_action" => "resize_map_down",
                    "l_action" => "resize_map_right",
                    _ => action
                };
                if (_mapData != null && _camera != null)
                {
                    var p = selector.PrimarySelected;
                    await _editModeManager.HandleKeyAction(resizeAction, p?.Col ?? 0, p?.Row ?? 0);
                    _skElement.InvalidateVisual();
                }
            }
            return;
        }

        if (action == "l_action")
        {
            if (_mapData != null && _camera != null)
            {
                var p = selector.PrimarySelected;
                await _editModeManager.HandleKeyAction("set_legion", p?.Col ?? 0, p?.Row ?? 0);
                _skElement.InvalidateVisual();
            }
        }
    }

    private async void DispatchEditModeAction(string action)
    {
        Debug.WriteLine($"[EditMode] DispatchEditModeAction: action={action}, mode={_editModeManager.CurrentMode}");

        if (action == "tab_action")
        {
            OnToggleHexInfo();
            return;
        }

        if (action is "ijkl_action" or "jkl_action" or "kl_action" or "l_action")
        {
            await HandleIjklAction(action);
            return;
        }

        if (action == "confirm_selection_move" || action == "cancel_selection_move" || action == "remove_ocean_from_selection")
        {
            if (_mapData != null && _camera != null)
            {
                var selPrimary = HexSelector.Instance.PrimarySelected;
                int selCol = selPrimary?.Col ?? 0;
                int selRow = selPrimary?.Row ?? 0;
                await _editModeManager.HandleKeyAction(action, selCol, selRow);
                _skElement.InvalidateVisual();
            }
            return;
        }

        if (action is "create_coast" or "process_ocean_layer2")
        {
            if (_mapData != null)
            {
                await _editModeManager.HandleKeyAction(action, 0, 0);
                _skElement.InvalidateVisual();
            }
            return;
        }

        if (action == "export_hd")
        {
            if (_mapData != null)
            {
                await _editModeManager.HandleKeyAction(action, 0, 0);
                _skElement.InvalidateVisual();
            }
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

        if (action == "geo_calculate")
        {
            await OnGeoCalculateAsync();
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

        var primary = HexSelector.Instance.PrimarySelected;
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
                HexSelector.Instance.Select(col, row, _mapData.MapWidth, _mapData.MapHeight);
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
        UpdateOverlayFromEditMode();
        _skElement.InvalidateVisual();
    }

    private void OnEditModeStatusMessageChanged(object? sender, string message)
    {
        Debug.WriteLine($"[EditMode] {message}");
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

    private void OnBrushToggled(object? sender, EventArgs e)
    {
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
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushSize = e.BrushSize;
        terrain.BrushTerrainType = e.TerrainType;
        terrain.BrushDecoration = e.TerrainVariant;
        terrain.EditLayer = e.EditLayer;
        terrain.BrushShape = e.BrushShape;
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
        HexSelector.Instance.SelectionChanged -= OnHexSelectionChanged;
        HexSelector.Instance.SelectionRectChanged -= OnSelectionRectChanged;
        HexSelector.Instance.CancelSelectionRect();
        HexSelector.Instance.ClearSelection();
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