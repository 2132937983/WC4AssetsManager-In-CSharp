using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Imaging;
using Path = System.IO.Path;

namespace WC4MapEditor.GUI.Views;

public class FlagEditScene : UserControl
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly MainWindow _window;
    private readonly IFlagEditorService _editorService = new FlagEditorService();
    private readonly IFlagImageService _flagService = new FlagImageService();
    private readonly CountrySettingParser _parser = CountrySettingParser.Instance;

    private int _countryId;
    private string _countryName = "";

    // 新国旗制作模式
    private SKBitmap? _sourceImage;
    private SKBitmap? _workingCanvas; // 150x150工作画布
    private SKBitmap? _displayImage;

    // 变换参数
    private float _offsetX = 0, _offsetY = 0;
    private float _imageScale = 1.0f;
    private bool _extendEdges = false;
    private bool _tiledMode = false; // R键切换平铺模式

    // 涂鸦引擎
    private bool _paintMode = false; // P键切换涂鸦模式
    private int _brushRadius = 5;
    private SKColor _brushColor = SKColors.Red;
    private bool _isPainting = false;
    private Point _paintCursorPos; // 涂鸦光标位置（相对于画布）
    private Border _paintCursor = null!; // 涂鸦光标可视化
    private SKBitmap? _paintLayer; // 涂鸦图层缓存

    // 显示参数
    private double _displayScale = 1.0;
    private const double DisplayScaleStep = 0.1;
    private const double MinDisplayScale = 0.5;
    private const double MaxDisplayScale = 5.0;

    // 拖动状态
    private bool _isDragging;
    private Point _dragStart;
    private float _dragStartOffsetX, _dragStartOffsetY;

    // 旧模式兼容
    private SKBitmap? _legacySourceImage;
    private SKBitmap? _legacyCanvasImage;
    private int _srcX, _srcY;
    private int _canvasWidth = 150, _canvasHeight = 150;
    private SKColor _bgColor = SKColors.Transparent;
    private bool _legacyMode;

    // 旧模式选择
    private bool _selecting;
    private Point _selectStart;
    private Rect _selectRect;
    private bool _movingSelection;
    private Point _moveSelStart;
    private Rect _moveSelOriginal;

    private int _resize1W = 100, _resize1H = 100;
    private int _resize2W = 37, _resize2H = 38;

    private Grid _root = null!;
    private Canvas _imageCanvas = null!;
    private Image _displayControl = null!;
    private Border _selectionBorder = null!;
    private TextBlock _statusText = null!;

    // 控制面板
    private TextBox _offsetXBox = null!, _offsetYBox = null!, _scaleBox = null!;
    private CheckBox _extendEdgesCheck = null!;
    private Button _makeFlagBtn = null!;
    // 模式开关统一用主题 ToggleButton 的选中态表示，不再手动换背景色
    private ToggleButton _legacyBtn = null!;
    private ToggleButton _tiledBtn = null!;
    private ToggleButton _paintModeBtn = null!;
    private TextBox _brushRadiusBox = null!;
    private Border _brushColorPreview = null!;

    // 预览
    private Image _hdFlagPreview = null!;
    private Image _tacticalFlagPreview = null!;
    private Image _smallFlagPreview = null!;

    public FlagEditScene(MainWindow window, int countryId, string countryName)
    {
        EditorTheme.Apply(this);
        _window = window;
        _countryId = countryId;
        _countryName = countryName;
        InitializeUI();
        LoadExistingFlags();
        InitializeEmptyCanvas(); // 初始化空白画布
        Focusable = true;
        Loaded += (_, _) => Keyboard.Focus(this);
        KeyDown += OnKeyDown;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void InitializeEmptyCanvas()
    {
        // 创建150x150的空白透明画布
        _workingCanvas?.Dispose();
        _paintLayer?.Dispose();
        _workingCanvas = _editorService.CreateTiledCanvas(null, 150, 150);
        _paintLayer = new SKBitmap(new SKImageInfo(150, 150, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(_paintLayer)) c.Clear(SKColors.Transparent);
        UpdateDisplay();
        SetStatus(Loc.T("Flag.StatusInit"));
    }

    private void InitializeUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        BuildTopBar();
        BuildMainArea();
        BuildControlBar();
        BuildStatusBar();

        Content = _root;
    }

    private void BuildTopBar()
    {
        _makeFlagBtn = EditorTheme.PrimaryButton(Loc.T("Flag.MakeFlag"), OnMakeFlag);
        _makeFlagBtn.IsEnabled = false;

        _legacyBtn = MakeToggle(Loc.T("Flag.LegacyMode"), (_, _) =>
        {
            _legacyMode = _legacyBtn.IsChecked == true;
            _legacyBtn.Content = _legacyMode ? Loc.T("Flag.NewMode") : Loc.T("Flag.LegacyMode");
            UpdateDisplay();
            SetStatus(Loc.T(_legacyMode ? "Flag.StatusSwitchedLegacy" : "Flag.StatusSwitchedNew"));
        });

        var topBar = EditorTheme.TopBar(Loc.F("Flag.Title", _countryName, _countryId),
            (_, _) => _window.SetCurrentScene(new CountryEditScene(_window)),
            EditorTheme.Button(Loc.T("Flag.OpenImage"), OnOpenImage),
            _legacyBtn,
            EditorTheme.Button(Loc.T("Flag.SaveLegacy"), OnSaveLegacy),
            _makeFlagBtn);
        Grid.SetRow(topBar, 0);
        _root.Children.Add(topBar);
    }

    private void BuildMainArea()
    {
        var mainGrid = new Grid();
        Grid.SetRow(mainGrid, 1);
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左栏：现有国旗预览
        var leftPanel = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

        var header = EditorTheme.Section(Loc.T("Flag.ExistingPreview"));
        header.Margin = new Thickness(0, 0, 0, 6);
        leftPanel.Children.Add(header);

        leftPanel.Children.Add(EditorTheme.Muted(Loc.T("Flag.HdFlag")));
        _hdFlagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        leftPanel.Children.Add(_hdFlagPreview);

        var bigLabel = EditorTheme.Muted(Loc.T("Flag.BigFlag"));
        bigLabel.Margin = new Thickness(0, 10, 0, 0);
        leftPanel.Children.Add(bigLabel);
        _tacticalFlagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        leftPanel.Children.Add(_tacticalFlagPreview);

        var smallLabel = EditorTheme.Muted(Loc.T("Flag.SmallFlag"));
        smallLabel.Margin = new Thickness(0, 10, 0, 0);
        leftPanel.Children.Add(smallLabel);
        _smallFlagPreview = new Image
        {
            Width = 50,
            Height = 50,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        leftPanel.Children.Add(_smallFlagPreview);

        var leftBorder = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        leftBorder.Child = leftPanel;
        Grid.SetColumn(leftBorder, 0);
        mainGrid.Children.Add(leftBorder);

        // 右侧：画布区（画布按显示缩放自适应大小，居中摆放）
        var canvasBorder = new Border
        {
            Background = EditorTheme.Bg1,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _imageCanvas = new Canvas();
        _imageCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
        _imageCanvas.MouseRightButtonDown += OnCanvasMouseDown;
        _imageCanvas.MouseMove += OnCanvasMouseMove;
        _imageCanvas.MouseLeftButtonUp += OnCanvasMouseUp;
        _imageCanvas.MouseRightButtonUp += OnCanvasMouseUp;
        _imageCanvas.MouseWheel += OnCanvasWheel;

        _displayControl = new Image { Stretch = Stretch.None };
        _imageCanvas.Children.Add(_displayControl);

        // 选区框 / 涂鸦光标叠在国旗像素上，必须与任意图片都有对比，保留固定颜色
        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.LimeGreen),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _imageCanvas.Children.Add(_selectionBorder);

        // 涂鸦十字光标
        _paintCursor = new Border
        {
            Width = 15,
            Height = 15,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        // 添加十字线
        var crossGrid = new Grid();
        crossGrid.Children.Add(new Border { Background = Brushes.Black, Height = 1, VerticalAlignment = VerticalAlignment.Center });
        crossGrid.Children.Add(new Border { Background = Brushes.Black, Width = 1, HorizontalAlignment = HorizontalAlignment.Center });
        _paintCursor.Child = crossGrid;
        _imageCanvas.Children.Add(_paintCursor);

        canvasBorder.Child = _imageCanvas;
        Grid.SetColumn(canvasBorder, 1);
        mainGrid.Children.Add(canvasBorder);

        _root.Children.Add(mainGrid);
    }

    private void BuildControlBar()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        void AddLabel(string text)
        {
            var lbl = EditorTheme.FieldLabel(text);
            lbl.Margin = new Thickness(panel.Children.Count == 0 ? 0 : 10, 0, 4, 0);
            panel.Children.Add(lbl);
        }

        // Offset X
        AddLabel(Loc.T("Flag.OffsetX"));
        _offsetXBox = new TextBox { Text = "0", Width = 60 };
        _offsetXBox.TextChanged += (_, _) => UpdateTransformFromUI();
        panel.Children.Add(_offsetXBox);

        // Offset Y
        AddLabel(Loc.T("Flag.OffsetY"));
        _offsetYBox = new TextBox { Text = "0", Width = 60 };
        _offsetYBox.TextChanged += (_, _) => UpdateTransformFromUI();
        panel.Children.Add(_offsetYBox);

        // Scale
        AddLabel(Loc.T("Flag.Scale"));
        _scaleBox = new TextBox { Text = "1.0", Width = 60 };
        _scaleBox.TextChanged += (_, _) => UpdateTransformFromUI();
        panel.Children.Add(_scaleBox);

        // Extend edges checkbox
        _extendEdgesCheck = new CheckBox
        {
            Content = Loc.T("Flag.ExtendEdges"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _extendEdgesCheck.Checked += (_, _) => { if (_sourceImage != null) { _extendEdges = true; UpdateWorkingCanvas(); } };
        _extendEdgesCheck.Unchecked += (_, _) => { _extendEdges = false; UpdateWorkingCanvas(); };
        panel.Children.Add(_extendEdgesCheck);

        panel.Children.Add(EditorTheme.VSeparator());

        // Tiled mode toggle
        _tiledBtn = MakeToggle(Loc.T("Flag.Tiled"), (_, _) =>
        {
            if (_sourceImage == null)
            {
                // 没有图片时不允许进入平铺，把刚被点击翻转的选中态还原
                _tiledBtn.IsChecked = false;
                SetStatus(Loc.T("Flag.LoadImageFirst"));
                return;
            }
            _tiledMode = _tiledBtn.IsChecked == true;
            UpdateWorkingCanvas();
            SetStatus(Loc.T(_tiledMode ? "Flag.TiledOn" : "Flag.TiledOff"));
        }, tool: true);
        panel.Children.Add(_tiledBtn);

        // Reset button
        panel.Children.Add(EditorTheme.ToolButton(Loc.T("Common.Reset"), (_, _) => ResetTransform()));

        // Paint mode toggle
        _paintModeBtn = MakeToggle(Loc.T("Flag.Paint"), (_, _) => TogglePaintMode(), tool: true);
        panel.Children.Add(_paintModeBtn);

        // Brush radius
        AddLabel(Loc.T("Flag.Brush"));
        _brushRadiusBox = new TextBox { Text = "5", Width = 40 };
        _brushRadiusBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(_brushRadiusBox.Text, out var r))
            {
                _brushRadius = Math.Clamp(r, 1, 50);
                UpdatePaintCursorSize();
            }
        };
        panel.Children.Add(_brushRadiusBox);

        // Brush color preview（色块本身是当前笔刷颜色，属于数据）
        _brushColorPreview = new Border
        {
            Width = 20,
            Height = 20,
            Background = new SolidColorBrush(Color.FromRgb(_brushColor.Red, _brushColor.Green, _brushColor.Blue)),
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_brushColorPreview);

        var controlBar = EditorTheme.ToolBar(panel);
        controlBar.BorderThickness = new Thickness(0, 1, 0, 0);
        Grid.SetRow(controlBar, 2);
        _root.Children.Add(controlBar);
    }

    private void BuildStatusBar()
    {
        var statusBar = EditorTheme.StatusBar(out _statusText);
        Grid.SetRow(statusBar, 3);
        _root.Children.Add(statusBar);
    }

    /// <summary>开关按钮：顶栏尺寸与主题 Button 对齐，工具条尺寸与 ToolButton 对齐；选中态由主题 ToggleButton 样式绘制。</summary>
    private static ToggleButton MakeToggle(string text, RoutedEventHandler onClick, bool tool = false)
    {
        var t = tool
            ? new ToggleButton { Content = text, MinHeight = 26, FontSize = 12, Margin = new Thickness(0, 0, 6, 0) }
            : new ToggleButton { Content = text, MinHeight = 28, FontSize = 13 };
        t.Click += onClick;
        return t;
    }

    private void LoadExistingFlags()
    {
        // Load HD flag
        try
        {
            var hdFlag = _flagService.LoadFlagFromHdAtlas(_countryId);
            if (hdFlag != null)
            {
                _hdFlagPreview.Source = SkBitmapToBitmapSource(hdFlag);
                hdFlag.Dispose();
                SetStatus(Loc.F("Flag.StatusHdLoaded", _countryId));
            }
            else
            {
                SetStatus(Loc.F("Flag.StatusHdNotFound", _countryId));
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Flag.StatusHdLoadFailed", ex.Message));
        }

        // Load tactical map flag
        try
        {
            var tacticalFlag = _flagService.LoadBigFlag(_countryId);
            if (tacticalFlag != null)
            {
                _tacticalFlagPreview.Source = SkBitmapToBitmapSource(tacticalFlag);
                _legacySourceImage = tacticalFlag;
                _canvasWidth = tacticalFlag.Width;
                _canvasHeight = tacticalFlag.Height;
                UpdateWorkingCanvas();
                SetStatus(Loc.F("Flag.StatusExistingLoaded", tacticalFlag.Width, tacticalFlag.Height));
            }
            else
            {
                SetStatus(Loc.T("Flag.StatusExistingNotFound"));
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("Flag.StatusFlagLoadFailed", ex.Message));
        }

        // Load small flag
        try
        {
            var smallFlag = _flagService.LoadSmallFlag(_countryId);
            if (smallFlag != null)
            {
                _smallFlagPreview.Source = SkBitmapToBitmapSource(smallFlag);
                smallFlag.Dispose();
            }
        }
        catch { }
    }

    // ========== 拖放支持 ==========

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                LoadImage(files[0]);
            }
            e.Handled = true;
        }
    }

    // ========== 图片加载 ==========

    private void OnOpenImage(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = $"{Loc.T("Flag.ImageFilter")}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|{Loc.T("Common.AllFiles")}|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadImage(dlg.FileName);
    }

    private void LoadImage(string path)
    {
        var loaded = _editorService.LoadSourceImage(path);
        if (loaded == null)
        {
            SetStatus(Loc.T("Flag.StatusLoadFailed"));
            return;
        }

        _sourceImage?.Dispose();
        _sourceImage = loaded;
        _legacySourceImage = loaded.Copy();

        // 重置变换
        ResetTransform();

        _makeFlagBtn.IsEnabled = true;
        SetStatus(Loc.F("Flag.StatusLoaded", Path.GetFileName(path), loaded.Width, loaded.Height));
    }

    private void ResetTransform()
    {
        _offsetX = 0;
        _offsetY = 0;
        _imageScale = 1.0f;
        _extendEdges = false;
        _extendEdgesCheck.IsChecked = false;
        _tiledMode = false;
        _tiledBtn.IsChecked = false;

        _offsetXBox.Text = "0";
        _offsetYBox.Text = "0";
        _scaleBox.Text = "1.0";

        // 清除涂鸦图层
        if (_paintLayer != null)
        {
            using (var c = new SKCanvas(_paintLayer)) c.Clear(SKColors.Transparent);
        }

        UpdateWorkingCanvas();
    }

    // ========== 变换更新 ==========

    private void UpdateTransformFromUI()
    {
        if (float.TryParse(_offsetXBox.Text, out var ox)) _offsetX = ox;
        if (float.TryParse(_offsetYBox.Text, out var oy)) _offsetY = oy;
        if (float.TryParse(_scaleBox.Text, out var sc)) _imageScale = Math.Max(0.1f, sc);

        UpdateWorkingCanvas();
    }

    private void UpdateWorkingCanvas()
    {
        _workingCanvas?.Dispose();

        // 初始化涂鸦图层（如果不存在）
        if (_paintLayer == null)
        {
            _paintLayer = new SKBitmap(new SKImageInfo(150, 150, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var c = new SKCanvas(_paintLayer)) c.Clear(SKColors.Transparent);
        }

        if (_sourceImage == null)
        {
            // 没有图片时显示空白画布 + 涂鸦层
            var emptyCanvas = _editorService.CreateTiledCanvas(null, 150, 150);
            if (emptyCanvas != null)
            {
                _workingCanvas = new SKBitmap(new SKImageInfo(150, 150, SKColorType.Rgba8888, SKAlphaType.Premul));
                using var surface = new SKCanvas(_workingCanvas);
                surface.Clear(SKColors.Transparent);
                surface.DrawBitmap(emptyCanvas, 0, 0);
                surface.DrawBitmap(_paintLayer, 0, 0);
                emptyCanvas.Dispose();
            }
            else
            {
                _workingCanvas = _paintLayer.Copy();
            }
            UpdateDisplay();
            return;
        }

        if (_extendEdges)
        {
            _workingCanvas = _editorService.ExtendEdgesToCanvas(_sourceImage, 150, 150, _offsetX, _offsetY, _imageScale);
        }
        else if (_tiledMode)
        {
            // 平铺模式
            _workingCanvas = _editorService.CreateTiledCanvas(_sourceImage, 150, 150);
            if (_workingCanvas != null && (Math.Abs(_offsetX) > 0.01f || Math.Abs(_offsetY) > 0.01f || Math.Abs(_imageScale - 1.0f) > 0.01f))
            {
                var newCanvas = _editorService.CreateTiledCanvas(null, 150, 150);
                if (newCanvas != null)
                {
                    using (_workingCanvas)
                    {
                        _workingCanvas = _editorService.DrawTiledImage(newCanvas, _sourceImage, _offsetX, _offsetY, _imageScale);
                    }
                }
            }
        }
        else
        {
            // 普通模式：只绘制一张图片，考虑偏移和缩放
            var info = new SKImageInfo(150, 150, SKColorType.Rgba8888, SKAlphaType.Premul);
            _workingCanvas = new SKBitmap(info);
            using var surface = new SKCanvas(_workingCanvas);
            surface.Clear(SKColors.Transparent);

            int scaledWidth = (int)(_sourceImage.Width * _imageScale);
            int scaledHeight = (int)(_sourceImage.Height * _imageScale);

            if (scaledWidth > 0 && scaledHeight > 0)
            {
                using var scaledImage = _sourceImage.Resize(
                    new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                    HighQuality);

                if (scaledImage != null)
                {
                    float drawX = (150 - scaledWidth) / 2 + _offsetX;
                    float drawY = (150 - scaledHeight) / 2 + _offsetY;
                    surface.DrawBitmap(scaledImage, drawX, drawY);
                }
            }
        }

        // 叠加涂鸦图层
        if (_workingCanvas != null)
        {
            using var surface = new SKCanvas(_workingCanvas);
            surface.DrawBitmap(_paintLayer, 0, 0);
        }

        UpdateDisplay();
    }

    // ========== 显示更新 ==========

    private void UpdateDisplay()
    {
        if (_legacyMode)
        {
            UpdateLegacyDisplay();
            return;
        }

        if (_workingCanvas == null) return;

        _displayImage?.Dispose();

        int dispW = (int)(150 * _displayScale);
        int dispH = (int)(150 * _displayScale);
        if (dispW <= 0 || dispH <= 0) return;

        _displayImage = _workingCanvas.Resize(new SKImageInfo(dispW, dispH, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
        if (_displayImage == null) return;

        _displayControl.Source = SkBitmapToBitmapSource(_displayImage);
        _imageCanvas.Width = dispW;
        _imageCanvas.Height = dispH;

        // 隐藏选择框
        _selectionBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateLegacyDisplay()
    {
        if (_legacyCanvasImage == null) return;
        _displayImage?.Dispose();

        int dispW = (int)(_legacyCanvasImage.Width * _displayScale);
        int dispH = (int)(_legacyCanvasImage.Height * _displayScale);
        if (dispW <= 0 || dispH <= 0) return;

        _displayImage = _legacyCanvasImage.Resize(new SKImageInfo(dispW, dispH, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
        if (_displayImage == null) return;

        _displayControl.Source = SkBitmapToBitmapSource(_displayImage);
        _imageCanvas.Width = dispW;
        _imageCanvas.Height = dispH;

        UpdateSelectionDisplay();
    }

    private void UpdateSelectionDisplay()
    {
        if (_selectRect.Width > 0 && _selectRect.Height > 0)
        {
            _selectionBorder.Visibility = Visibility.Visible;
            _selectionBorder.Width = _selectRect.Width;
            _selectionBorder.Height = _selectRect.Height;
            Canvas.SetLeft(_selectionBorder, _selectRect.X);
            Canvas.SetTop(_selectionBorder, _selectRect.Y);
        }
        else
        {
            _selectionBorder.Visibility = Visibility.Collapsed;
        }
    }

    // ========== 鼠标交互 ==========

    private Point CanvasPoint(MouseEventArgs e)
    {
        return e.GetPosition(_imageCanvas);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        var pos = CanvasPoint(e);

        if (_legacyMode)
        {
            OnLegacyMouseDown(pos);
            return;
        }

        // 涂鸦模式：左键落笔（绘制到涂鸦图层）
        if (_paintMode && e.LeftButton == MouseButtonState.Pressed && _paintLayer != null)
        {
            _isPainting = true;
            var canvasPos = ScreenToCanvasPos(pos);
            _editorService.PaintBrush(_paintLayer, (int)canvasPos.X, (int)canvasPos.Y, _brushRadius, _brushColor);
            UpdateWorkingCanvas(); // 重新合成显示
            _imageCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 涂鸦模式：右键取色
        if (_paintMode && e.RightButton == MouseButtonState.Pressed && _workingCanvas != null)
        {
            var canvasPos = ScreenToCanvasPos(pos);
            var color = _editorService.GetPixelColor(_workingCanvas, (int)canvasPos.X, (int)canvasPos.Y);
            _brushColor = color;
            _brushColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
            SetStatus(Loc.F("Flag.StatusPicked", color.Red, color.Green, color.Blue));
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && _sourceImage != null)
        {
            _isDragging = true;
            _dragStart = pos;
            _dragStartOffsetX = _offsetX;
            _dragStartOffsetY = _offsetY;
            _imageCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var pos = CanvasPoint(e);

        if (_legacyMode)
        {
            OnLegacyMouseMove(pos, e);
            return;
        }

        // 涂鸦模式：移动画笔和更新光标
        if (_paintMode && _workingCanvas != null)
        {
            _paintCursorPos = ScreenToCanvasPos(pos);

            // 更新十字光标位置（居中）
            Canvas.SetLeft(_paintCursor, pos.X - _paintCursor.Width / 2);
            Canvas.SetTop(_paintCursor, pos.Y - _paintCursor.Height / 2);

            // 拖动绘制（绘制到涂鸦图层）
            if (_isPainting && e.LeftButton == MouseButtonState.Pressed && _paintLayer != null)
            {
                _editorService.PaintBrush(_paintLayer, (int)_paintCursorPos.X, (int)_paintCursorPos.Y, _brushRadius, _brushColor);
                UpdateWorkingCanvas(); // 重新合成显示
            }
            e.Handled = true;
            return;
        }

        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx = (pos.X - _dragStart.X) / _displayScale;
            double dy = (pos.Y - _dragStart.Y) / _displayScale;

            _offsetX = _dragStartOffsetX + (float)dx;
            _offsetY = _dragStartOffsetY + (float)dy;

            _offsetXBox.Text = _offsetX.ToString("F1");
            _offsetYBox.Text = _offsetY.ToString("F1");

            UpdateWorkingCanvas();
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_legacyMode)
        {
            _selecting = false;
            _movingSelection = false;
            return;
        }

        if (_paintMode)
        {
            _isPainting = false;
            _imageCanvas.ReleaseMouseCapture();
            return;
        }

        _isDragging = false;
        _imageCanvas.ReleaseMouseCapture();
    }

    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if (_legacyMode)
        {
            double newScale = _displayScale + (e.Delta > 0 ? DisplayScaleStep : -DisplayScaleStep);
            newScale = Math.Max(MinDisplayScale, Math.Min(MaxDisplayScale, newScale));
            if (Math.Abs(newScale - _displayScale) > 0.001)
            {
                _displayScale = newScale;
                UpdateDisplay();
                SetStatus(Loc.F("Flag.StatusDisplayZoom", _displayScale));
            }
            return;
        }

        // 新模式：滚轮调整图片缩放
        if (_sourceImage != null)
        {
            float scaleDelta = e.Delta > 0 ? 0.1f : -0.1f;
            _imageScale = Math.Max(0.1f, _imageScale + scaleDelta);
            _scaleBox.Text = _imageScale.ToString("F1");
            UpdateWorkingCanvas();
            SetStatus(Loc.F("Flag.StatusImageZoom", _imageScale));
        }
    }

    // ========== 旧模式鼠标处理 ==========

    private void OnLegacyMouseDown(Point pos)
    {
        if (IsInsideSelection(pos))
        {
            _movingSelection = true;
            _moveSelStart = pos;
            _moveSelOriginal = _selectRect;
            return;
        }

        _selecting = true;
        _selectStart = pos;
        _selectRect = new Rect(pos.X, pos.Y, 0, 0);
        UpdateSelectionDisplay();
    }

    private void OnLegacyMouseMove(Point pos, MouseEventArgs e)
    {
        if (_movingSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx = pos.X - _moveSelStart.X;
            double dy = pos.Y - _moveSelStart.Y;
            _selectRect = new Rect(_moveSelOriginal.X + dx, _moveSelOriginal.Y + dy, _moveSelOriginal.Width, _moveSelOriginal.Height);
            UpdateSelectionDisplay();
            return;
        }

        if (_selecting && e.LeftButton == MouseButtonState.Pressed)
        {
            double x = Math.Min(_selectStart.X, pos.X);
            double y = Math.Min(_selectStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _selectStart.X);
            double h = Math.Abs(pos.Y - _selectStart.Y);
            double size = Math.Min(w, h);
            _selectRect = new Rect(x, y, size, size);
            UpdateSelectionDisplay();
        }
    }

    private bool IsInsideSelection(Point p)
    {
        return _selectRect.Width > 0 && _selectRect.Contains(p);
    }

    // ========== 键盘处理 ==========

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // P键切换涂鸦模式
        if (e.Key == Key.P)
        {
            TogglePaintMode();
            e.Handled = true;
            return;
        }

        // 涂鸦模式下的快捷键
        if (_paintMode)
        {
            switch (e.Key)
            {
                case Key.OemOpenBrackets: // [ 减小笔刷
                    _brushRadius = Math.Max(1, _brushRadius - 1);
                    _brushRadiusBox.Text = _brushRadius.ToString();
                    UpdatePaintCursorSize();
                    SetStatus(Loc.F("Flag.StatusBrushSize", _brushRadius));
                    e.Handled = true;
                    return;
                case Key.OemCloseBrackets: // ] 增大笔刷
                    _brushRadius = Math.Min(50, _brushRadius + 1);
                    _brushRadiusBox.Text = _brushRadius.ToString();
                    UpdatePaintCursorSize();
                    SetStatus(Loc.F("Flag.StatusBrushSize", _brushRadius));
                    e.Handled = true;
                    return;
            }
        }

        // 方向键/WASD微调图片位置（1px）- 只要不是在旧模式且不在涂鸦模式就可用
        if (!_legacyMode && !_paintMode)
        {
            bool moved = false;
            switch (e.Key)
            {
                case Key.Up:
                case Key.W:
                    _offsetY -= 1;
                    moved = true;
                    break;
                case Key.Down:
                case Key.S:
                    _offsetY += 1;
                    moved = true;
                    break;
                case Key.Left:
                case Key.A:
                    _offsetX -= 1;
                    moved = true;
                    break;
                case Key.Right:
                case Key.D:
                    _offsetX += 1;
                    moved = true;
                    break;
            }
            if (moved)
            {
                _offsetXBox.Text = _offsetX.ToString("F1");
                _offsetYBox.Text = _offsetY.ToString("F1");
                UpdateWorkingCanvas();
                SetStatus(Loc.F("Flag.StatusPosition", _offsetX, _offsetY));
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.E)
        {
            if (_sourceImage == null)
            {
                SetStatus(Loc.T("Flag.LoadImageFirst"));
                e.Handled = true;
                return;
            }
            _extendEdges = !_extendEdges;
            _extendEdgesCheck.IsChecked = _extendEdges;
            UpdateWorkingCanvas();
            SetStatus(Loc.T(_extendEdges ? "Flag.ExtendOn" : "Flag.ExtendOff"));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.R)
        {
            if (_sourceImage == null)
            {
                SetStatus(Loc.T("Flag.LoadImageFirst"));
                e.Handled = true;
                return;
            }
            _tiledMode = !_tiledMode;
            _tiledBtn.IsChecked = _tiledMode;
            UpdateWorkingCanvas();
            SetStatus(Loc.T(_tiledMode ? "Flag.TiledOn" : "Flag.TiledOff"));
            e.Handled = true;
            return;
        }

        if (_legacyMode && _legacySourceImage != null)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 1 : 5;

            switch (e.Key)
            {
                case Key.Up: _srcY -= step; UpdateLegacyCanvas(); UpdateDisplay(); e.Handled = true; break;
                case Key.Down: _srcY += step; UpdateLegacyCanvas(); UpdateDisplay(); e.Handled = true; break;
                case Key.Left: _srcX -= step; UpdateLegacyCanvas(); UpdateDisplay(); e.Handled = true; break;
                case Key.Right: _srcX += step; UpdateLegacyCanvas(); UpdateDisplay(); e.Handled = true; break;
            }
        }
    }

    private void TogglePaintMode()
    {
        _paintMode = !_paintMode;
        _paintModeBtn.IsChecked = _paintMode;

        if (_paintMode)
        {
            // 进入涂鸦模式，显示十字光标，隐藏系统光标
            _paintCursor.Visibility = Visibility.Visible;
            UpdatePaintCursorSize();
            _imageCanvas.Cursor = Cursors.None;
            SetStatus(Loc.T("Flag.PaintOn"));
        }
        else
        {
            // 退出涂鸦模式
            _paintCursor.Visibility = Visibility.Collapsed;
            _imageCanvas.Cursor = Cursors.Arrow;
            SetStatus(Loc.T("Flag.PaintOff"));
        }
    }

    /// <summary>
    /// 将屏幕坐标转换为画布坐标（150x150）
    /// </summary>
    private Point ScreenToCanvasPos(Point screenPos)
    {
        double canvasX = screenPos.X / _displayScale;
        double canvasY = screenPos.Y / _displayScale;
        return new Point(canvasX, canvasY);
    }

    /// <summary>
    /// 更新涂鸦光标大小以反映笔刷半径
    /// </summary>
    private void UpdatePaintCursorSize()
    {
        if (_paintCursor == null) return;

        // 光标大小 = 笔刷直径 * 显示缩放比例
        double cursorSize = _brushRadius * 2 * _displayScale;
        cursorSize = Math.Max(5, cursorSize); // 最小5px

        _paintCursor.Width = cursorSize;
        _paintCursor.Height = cursorSize;
    }

    private void UpdateLegacyCanvas()
    {
        if (_legacySourceImage == null) return;
        _legacyCanvasImage?.Dispose();
        _legacyCanvasImage = _editorService.CreateCanvasWithImage(_legacySourceImage, _canvasWidth, _canvasHeight, _bgColor, _srcX, _srcY);
    }

    // ========== 生成国旗 ==========

    private void OnMakeFlag(object sender, RoutedEventArgs e)
    {
        if (_workingCanvas == null)
        {
            MessageBox.Show(Loc.T("Flag.LoadOrEditFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string outputDir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            outputDir = Path.Combine(AppContext.BaseDirectory, "Output");

        var result = _editorService.MakeFlagWorkflow(_workingCanvas, _countryId, outputDir);

        if (result.Success)
        {
            _flagService.RefreshCache();

            var msg = Loc.T("Flag.MakeSuccessHeader") + "\n\n";
            if (result.FlagPath != null) msg += Loc.F("Flag.Path.Big", result.FlagPath) + "\n";
            if (result.SmallFlagPath != null) msg += Loc.F("Flag.Path.Small", result.SmallFlagPath) + "\n";
            if (result.HdFlagPath != null) msg += Loc.F("Flag.Path.Hd", result.HdFlagPath) + "\n";
            MessageBox.Show(msg, Loc.T("Common.Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(Loc.F("Flag.StatusMade", _countryId));

            // 刷新预览
            LoadExistingFlags();
        }
        else
        {
            MessageBox.Show(Loc.F("Flag.MakeFailed", result.ErrorMessage), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.F("Flag.MakeFailed", result.ErrorMessage));
        }
    }

    // ========== 旧模式保存 ==========

    private void OnSaveLegacy(object sender, RoutedEventArgs e)
    {
        if (_legacyCanvasImage == null)
        {
            MessageBox.Show(Loc.T("Flag.NothingToSave"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_selectRect.Width <= 0 || _selectRect.Height <= 0)
        {
            MessageBox.Show(Loc.T("Flag.SelectRegionFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int x0 = (int)(_selectRect.X / _displayScale);
        int y0 = (int)(_selectRect.Y / _displayScale);
        int x1 = (int)((_selectRect.X + _selectRect.Width) / _displayScale);
        int y1 = (int)((_selectRect.Y + _selectRect.Height) / _displayScale);

        x0 = Math.Max(0, Math.Min(x0, _legacyCanvasImage.Width));
        y0 = Math.Max(0, Math.Min(y0, _legacyCanvasImage.Height));
        x1 = Math.Max(0, Math.Min(x1, _legacyCanvasImage.Width));
        y1 = Math.Max(0, Math.Min(y1, _legacyCanvasImage.Height));

        var circleCropped = _editorService.CropCircularRegion(_legacyCanvasImage, x0, y0, x1, y1);

        string outputDir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            outputDir = Path.Combine(AppContext.BaseDirectory, "Output");

        var result = _editorService.SaveFlagImages(_legacyCanvasImage, circleCropped, _countryId.ToString(), outputDir, _resize1W, _resize1H, _resize2W, _resize2H);
        circleCropped?.Dispose();

        if (result.Success)
        {
            var msg = Loc.T("Flag.SavedHeader") + "\n\n";
            if (result.FlagPath != null) msg += Loc.F("Flag.Path.Flag", result.FlagPath) + "\n";
            if (result.CirclePath != null) msg += Loc.F("Flag.Path.Circle", result.CirclePath) + "\n";
            if (result.SmallPath != null) msg += Loc.F("Flag.Path.Small", result.SmallPath) + "\n";
            MessageBox.Show(msg, Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(Loc.F("Flag.StatusSaved", _countryId));
        }
        else
        {
            MessageBox.Show(Loc.F("Flag.SaveFailed", result.ErrorMessage), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.F("Flag.SaveFailed", result.ErrorMessage));
        }
    }

    // ========== 辅助方法 ==========

    private static BitmapSource? SkBitmapToBitmapSource(SKBitmap bitmap)
    {
        try
        {
            var info = bitmap.Info;
            var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                using var pixmap = bitmap.PeekPixels();
                var srcPtr = pixmap.GetPixels();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    var row = new byte[info.Width * 4];
                    System.Runtime.InteropServices.Marshal.Copy(System.IntPtr.Add(srcPtr, y * pixmap.RowBytes), row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4)
                    {
                        byte r = row[i], g = row[i + 1], b = row[i + 2], a = row[i + 3];
                        if (a < 255)
                        {
                            r = (byte)(r * a / 255); g = (byte)(g * a / 255); b = (byte)(b * a / 255);
                        }
                        row[i] = b; row[i + 1] = g; row[i + 2] = r; row[i + 3] = a;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, dstPtr, row.Length);
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }
}
