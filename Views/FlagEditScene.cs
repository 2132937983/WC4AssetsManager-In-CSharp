using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Imaging;
using Path = System.IO.Path;

namespace WC4MapEditor.Views;

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
    private Button _paintModeBtn = null!;
    private TextBox _brushRadiusBox = null!;
    private Border _brushColorPreview = null!;

    // 预览
    private Image _hdFlagPreview = null!;
    private Image _tacticalFlagPreview = null!;
    private Image _smallFlagPreview = null!;

    public FlagEditScene(MainWindow window, int countryId, string countryName)
    {
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
        SetStatus("请打开图片或拖入图片开始编辑，R键切换平铺模式，E键拉伸边缘");
    }

    private void InitializeUI()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        BuildTopBar();
        BuildMainArea();
        BuildControlBar();
        BuildStatusBar();

        Content = _root;
    }

    private void BuildTopBar()
    {
        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Height = 50
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var backBtn = new Button
        {
            Content = "返回",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        backBtn.Click += (_, _) => _window.SetCurrentScene(new CountryEditScene(_window));
        sp.Children.Add(backBtn);

        var openBtn = new Button
        {
            Content = "打开图片",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        openBtn.Click += OnOpenImage;
        sp.Children.Add(openBtn);

        _makeFlagBtn = new Button
        {
            Content = "生成国旗",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsEnabled = false
        };
        _makeFlagBtn.Click += OnMakeFlag;
        sp.Children.Add(_makeFlagBtn);

        var legacyBtn = new Button
        {
            Content = "旧模式",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        legacyBtn.Click += (_, _) =>
        {
            _legacyMode = !_legacyMode;
            legacyBtn.Content = _legacyMode ? "新模式" : "旧模式";
            legacyBtn.Background = _legacyMode
                ? new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C))
                : new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
            UpdateDisplay();
            SetStatus(_legacyMode ? "切换到旧模式（圆形裁剪）" : "切换到新模式（国旗制作）");
        };
        sp.Children.Add(legacyBtn);

        var saveBtn = new Button
        {
            Content = "保存(旧)",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        saveBtn.Click += OnSaveLegacy;
        sp.Children.Add(saveBtn);

        var titleTb = new TextBlock
        {
            Text = $"国旗编辑 - {_countryName} (ID:{_countryId})",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        };
        sp.Children.Add(titleTb);

        topBar.Child = sp;
        _root.Children.Add(topBar);
    }

    private void BuildMainArea()
    {
        var mainGrid = new Grid();
        Grid.SetRow(mainGrid, 1);
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left panel
        var leftPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Margin = new Thickness(5)
        };

        leftPanel.Children.Add(new TextBlock
        {
            Text = "现有国旗预览",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(5)
        });

        leftPanel.Children.Add(new TextBlock
        {
            Text = "UI国旗 (HD图集)",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(5, 2, 5, 2)
        });
        _hdFlagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };
        leftPanel.Children.Add(_hdFlagPreview);

        leftPanel.Children.Add(new TextBlock
        {
            Text = "征服大国旗",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(5, 10, 5, 2)
        });
        _tacticalFlagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };
        leftPanel.Children.Add(_tacticalFlagPreview);

        leftPanel.Children.Add(new TextBlock
        {
            Text = "征服小国旗",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(5, 10, 5, 2)
        });
        _smallFlagPreview = new Image
        {
            Width = 50,
            Height = 50,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };
        leftPanel.Children.Add(_smallFlagPreview);

        var leftBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(5),
            Child = leftPanel
        };
        Grid.SetColumn(leftBorder, 0);
        mainGrid.Children.Add(leftBorder);

        // Right: canvas area
        var canvasBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(5),
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
        var controlBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 5, 0, 0)
        };
        Grid.SetRow(controlBorder, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int col = 0;

        // Offset X
        var lblX = new TextBlock { Text = "X偏移:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) };
        Grid.SetColumn(lblX, col++);
        grid.Children.Add(lblX);

        _offsetXBox = new TextBox { Text = "0", Width = 60, VerticalAlignment = VerticalAlignment.Center };
        _offsetXBox.TextChanged += (_, _) => UpdateTransformFromUI();
        Grid.SetColumn(_offsetXBox, col++);
        grid.Children.Add(_offsetXBox);

        // Offset Y
        var lblY = new TextBlock { Text = "Y偏移:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        Grid.SetColumn(lblY, col++);
        grid.Children.Add(lblY);

        _offsetYBox = new TextBox { Text = "0", Width = 60, VerticalAlignment = VerticalAlignment.Center };
        _offsetYBox.TextChanged += (_, _) => UpdateTransformFromUI();
        Grid.SetColumn(_offsetYBox, col++);
        grid.Children.Add(_offsetYBox);

        // Scale
        var lblS = new TextBlock { Text = "缩放:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        Grid.SetColumn(lblS, col++);
        grid.Children.Add(lblS);

        _scaleBox = new TextBox { Text = "1.0", Width = 60, VerticalAlignment = VerticalAlignment.Center };
        _scaleBox.TextChanged += (_, _) => UpdateTransformFromUI();
        Grid.SetColumn(_scaleBox, col++);
        grid.Children.Add(_scaleBox);

        // Extend edges checkbox
        _extendEdgesCheck = new CheckBox
        {
            Content = "拉伸边缘(E)",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        _extendEdgesCheck.Checked += (_, _) => { if (_sourceImage != null) { _extendEdges = true; UpdateWorkingCanvas(); } };
        _extendEdgesCheck.Unchecked += (_, _) => { _extendEdges = false; UpdateWorkingCanvas(); };
        Grid.SetColumn(_extendEdgesCheck, col++);
        grid.Children.Add(_extendEdgesCheck);

        // Tiled mode button
        var tiledBtn = new Button
        {
            Content = "平铺(R)",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        tiledBtn.Click += (_, _) =>
        {
            if (_sourceImage == null) { SetStatus("请先加载图片"); return; }
            _tiledMode = !_tiledMode;
            tiledBtn.Background = _tiledMode
                ? new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C))
                : new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
            UpdateWorkingCanvas();
            SetStatus(_tiledMode ? "平铺模式已开启 (R)" : "平铺模式已关闭 (R)");
        };
        Grid.SetColumn(tiledBtn, col++);
        grid.Children.Add(tiledBtn);

        // Reset button
        var resetBtn = new Button
        {
            Content = "重置",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        resetBtn.Click += (_, _) => ResetTransform();
        Grid.SetColumn(resetBtn, col++);
        grid.Children.Add(resetBtn);

        // Paint mode button
        _paintModeBtn = new Button
        {
            Content = "涂鸦(P)",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        _paintModeBtn.Click += (_, _) => TogglePaintMode();
        Grid.SetColumn(_paintModeBtn, col++);
        grid.Children.Add(_paintModeBtn);

        // Brush radius
        var lblRadius = new TextBlock { Text = "笔刷:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        Grid.SetColumn(lblRadius, col++);
        grid.Children.Add(lblRadius);

        _brushRadiusBox = new TextBox { Text = "5", Width = 40, VerticalAlignment = VerticalAlignment.Center };
        _brushRadiusBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(_brushRadiusBox.Text, out var r))
            {
                _brushRadius = Math.Clamp(r, 1, 50);
                UpdatePaintCursorSize();
            }
        };
        Grid.SetColumn(_brushRadiusBox, col++);
        grid.Children.Add(_brushRadiusBox);

        // Brush color preview
        _brushColorPreview = new Border
        {
            Width = 20,
            Height = 20,
            Background = new SolidColorBrush(Color.FromRgb(_brushColor.Red, _brushColor.Green, _brushColor.Blue)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_brushColorPreview, col++);
        grid.Children.Add(_brushColorPreview);

        controlBorder.Child = grid;
        _root.Children.Add(controlBorder);
    }

    private void BuildStatusBar()
    {
        _statusText = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(10, 5, 10, 5) };
        var statusBar = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), Child = _statusText };
        Grid.SetRow(statusBar, 3);
        _root.Children.Add(statusBar);
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
                SetStatus($"已加载UI国旗 (ID:{_countryId})");
            }
            else
            {
                SetStatus($"未找到UI国旗 (ID:{_countryId})");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"加载UI国旗失败: {ex.Message}");
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
                SetStatus($"已加载现有国旗: {tacticalFlag.Width}x{tacticalFlag.Height}");
            }
            else
            {
                SetStatus("未找到现有国旗，请打开图片或拖入图片");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"加载国旗失败: {ex.Message}");
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
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadImage(dlg.FileName);
    }

    private void LoadImage(string path)
    {
        var loaded = _editorService.LoadSourceImage(path);
        if (loaded == null)
        {
            SetStatus("加载图片失败");
            return;
        }

        _sourceImage?.Dispose();
        _sourceImage = loaded;
        _legacySourceImage = loaded.Copy();

        // 重置变换
        ResetTransform();

        _makeFlagBtn.IsEnabled = true;
        SetStatus($"已加载: {Path.GetFileName(path)} ({loaded.Width}x{loaded.Height})，滚轮缩放，拖动移动");
    }

    private void ResetTransform()
    {
        _offsetX = 0;
        _offsetY = 0;
        _imageScale = 1.0f;
        _extendEdges = false;
        _extendEdgesCheck.IsChecked = false;
        _tiledMode = false;

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
            SetStatus($"取色: RGB({color.Red},{color.Green},{color.Blue})");
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
                SetStatus($"显示缩放: {_displayScale:F1}x");
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
            SetStatus($"图片缩放: {_imageScale:F1}x");
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
                    SetStatus($"笔刷大小: {_brushRadius}");
                    e.Handled = true;
                    return;
                case Key.OemCloseBrackets: // ] 增大笔刷
                    _brushRadius = Math.Min(50, _brushRadius + 1);
                    _brushRadiusBox.Text = _brushRadius.ToString();
                    UpdatePaintCursorSize();
                    SetStatus($"笔刷大小: {_brushRadius}");
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
                SetStatus($"位置: ({_offsetX:F1}, {_offsetY:F1})");
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.E)
        {
            if (_sourceImage == null)
            {
                SetStatus("请先加载图片");
                e.Handled = true;
                return;
            }
            _extendEdges = !_extendEdges;
            _extendEdgesCheck.IsChecked = _extendEdges;
            UpdateWorkingCanvas();
            SetStatus(_extendEdges ? "边缘拉伸已开启 (E)" : "边缘拉伸已关闭 (E)");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.R)
        {
            if (_sourceImage == null)
            {
                SetStatus("请先加载图片");
                e.Handled = true;
                return;
            }
            _tiledMode = !_tiledMode;
            UpdateWorkingCanvas();
            SetStatus(_tiledMode ? "平铺模式已开启 (R)" : "平铺模式已关闭 (R)");
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
        _paintModeBtn.Background = _paintMode
            ? new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C))
            : new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));

        if (_paintMode)
        {
            // 进入涂鸦模式，显示十字光标，隐藏系统光标
            _paintCursor.Visibility = Visibility.Visible;
            UpdatePaintCursorSize();
            _imageCanvas.Cursor = Cursors.None;
            SetStatus("涂鸦模式已开启: 左键绘制, 右键取色, [ ]调整笔刷大小, P键退出");
        }
        else
        {
            // 退出涂鸦模式
            _paintCursor.Visibility = Visibility.Collapsed;
            _imageCanvas.Cursor = Cursors.Arrow;
            SetStatus("涂鸦模式已关闭");
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
            MessageBox.Show("请先加载图片或编辑画布", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string outputDir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            outputDir = Path.Combine(AppContext.BaseDirectory, "Output");

        var result = _editorService.MakeFlagWorkflow(_workingCanvas, _countryId, outputDir);

        if (result.Success)
        {
            _flagService.RefreshCache();

            var msg = "国旗生成成功！\n\n";
            if (result.FlagPath != null) msg += $"大国旗: {result.FlagPath}\n";
            if (result.SmallFlagPath != null) msg += $"小国旗: {result.SmallFlagPath}\n";
            if (result.HdFlagPath != null) msg += $"HD国旗: {result.HdFlagPath}\n";
            MessageBox.Show(msg, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"国旗生成成功: flag_{_countryId}.png");

            // 刷新预览
            LoadExistingFlags();
        }
        else
        {
            MessageBox.Show($"生成失败: {result.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"生成失败: {result.ErrorMessage}");
        }
    }

    // ========== 旧模式保存 ==========

    private void OnSaveLegacy(object sender, RoutedEventArgs e)
    {
        if (_legacyCanvasImage == null)
        {
            MessageBox.Show("没有可保存的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_selectRect.Width <= 0 || _selectRect.Height <= 0)
        {
            MessageBox.Show("请先在画布上拖动选择要裁剪的圆形区域", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var msg = $"保存成功！\n\n";
            if (result.FlagPath != null) msg += $"国旗: {result.FlagPath}\n";
            if (result.CirclePath != null) msg += $"圆形国旗: {result.CirclePath}\n";
            if (result.SmallPath != null) msg += $"小国旗: {result.SmallPath}\n";
            MessageBox.Show(msg, "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"保存成功: flag_{_countryId}.png");
        }
        else
        {
            MessageBox.Show($"保存失败: {result.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"保存失败: {result.ErrorMessage}");
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