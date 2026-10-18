using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.GUI.Views;

public class GeneralPhotoEditScene : UserControl
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly MainWindow _window;
    private readonly IImageEditorService _editorService = new ImageEditorService();
    private readonly GeneralSettingParser _parser = GeneralSettingParser.Instance;

    private string? _sourceImagePath;
    private string _baseName = "";
    private SKBitmap? _sourceImage;
    private SKBitmap? _canvasImage;
    private SKBitmap? _displayImage;

    private int _srcX, _srcY;
    private int _canvasWidth = 400, _canvasHeight = 326;
    private SKColor _bgColor = SKColors.Transparent;
    private double _scaleFactor = 1.0;
    private const double ScaleStep = 0.1;
    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;

    private bool _dragMode;
    private Point _dragStart;
    private bool _selecting;
    private Point _selectStart;
    private Rect _selectRect;
    private bool _movingSelection;
    private Point _moveSelStart;
    private Rect _moveSelOriginal;

    private int _resize1W = 150, _resize1H = 150;
    private int _resize2W = 60, _resize2H = 60;

    private Grid _root = null!;
    private Canvas _imageCanvas = null!;
    private Image _displayControl = null!;
    private Border _selectionBorder = null!;
    private TextBlock _statusText = null!;
    private TextBox _canvasWBox = null!, _canvasHBox = null!;
    private TextBox _resize1WBox = null!, _resize1HBox = null!;
    private TextBox _resize2WBox = null!, _resize2HBox = null!;
    // 拖动模式用主题的 ToggleButton 选中态表示，不再手动换背景色
    private ToggleButton _dragBtn = null!;
    private Border _colorPreview = null!;

    public GeneralPhotoEditScene(MainWindow window, string? initialEName = null)
    {
        EditorTheme.Apply(this);
        _window = window;
        _baseName = initialEName ?? "";
        BuildUI();
        if (!string.IsNullOrEmpty(initialEName))
            TryLoadGeneralPhoto(initialEName);
    }

    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        BuildTopBar();
        BuildCanvasArea();
        BuildBottomInputs();
        BuildStatusBar();

        Content = _root;
    }

    private void BuildTopBar()
    {
        _dragBtn = MakeToggle(Loc.T("GeneralPhoto.DragImage"), OnToggleDrag);

        var topBar = EditorTheme.TopBar(Loc.T("GeneralPhoto.Title"), OnBack,
            EditorTheme.Button(Loc.T("GeneralPhoto.OpenImage"), OnOpen),
            _dragBtn,
            EditorTheme.Button(Loc.T("GeneralPhoto.BgColor"), OnChooseColor),
            EditorTheme.Button(Loc.T("GeneralPhoto.TransparentBg"), OnTransparentBg),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave));
        Grid.SetRow(topBar, 0);
        _root.Children.Add(topBar);
    }

    private void BuildCanvasArea()
    {
        // 滚动区用最深底色，画布稍亮一级，这样画布边界在没铺满时也看得见
        var canvasBorder = new Border
        {
            Background = EditorTheme.Bg0,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 8, 8, 4)
        };
        Grid.SetRow(canvasBorder, 1);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };

        _imageCanvas = new Canvas
        {
            Background = EditorTheme.Bg1,
            Width = 800,
            Height = 600
        };

        _displayControl = new Image
        {
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Canvas.SetLeft(_displayControl, 0);
        Canvas.SetTop(_displayControl, 0);
        _imageCanvas.Children.Add(_displayControl);

        // 选区框叠在任意图片上都要看得见，保留红色而不用主题色
        _selectionBorder = new Border
        {
            BorderBrush = Brushes.Red,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Width = 0,
            Height = 0,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetLeft(_selectionBorder, 0);
        Canvas.SetTop(_selectionBorder, 0);
        _imageCanvas.Children.Add(_selectionBorder);

        scrollViewer.Content = _imageCanvas;
        canvasBorder.Child = scrollViewer;
        _root.Children.Add(canvasBorder);

        _imageCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
        _imageCanvas.MouseLeftButtonUp += OnCanvasMouseUp;
        _imageCanvas.MouseMove += OnCanvasMouseMove;
        _imageCanvas.MouseWheel += OnCanvasMouseWheel;
        _imageCanvas.MouseRightButtonDown += OnCanvasRightMouseDown;
        _imageCanvas.MouseRightButtonUp += OnCanvasRightMouseUp;

        KeyDown += OnKeyDown;
        Focusable = true;
    }

    private void BuildBottomInputs()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        AddInput(panel, Loc.T("GeneralPhoto.CanvasW"), _canvasWidth.ToString(), out _canvasWBox);
        AddInput(panel, Loc.T("GeneralPhoto.CanvasH"), _canvasHeight.ToString(), out _canvasHBox);
        AddInput(panel, Loc.T("GeneralPhoto.CircleW"), _resize1W.ToString(), out _resize1WBox);
        AddInput(panel, Loc.T("GeneralPhoto.CircleH"), _resize1H.ToString(), out _resize1HBox);
        AddInput(panel, Loc.T("GeneralPhoto.HeadW"), _resize2W.ToString(), out _resize2WBox);
        AddInput(panel, Loc.T("GeneralPhoto.HeadH"), _resize2H.ToString(), out _resize2HBox);

        _canvasWBox.TextChanged += (_, _) => OnCanvasSizeChanged();
        _canvasHBox.TextChanged += (_, _) => OnCanvasSizeChanged();

        // 色块显示的是当前背景色（数据），初始为透明
        _colorPreview = new Border
        {
            Width = 20, Height = 20,
            Background = Brushes.Transparent,
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        panel.Children.Add(_colorPreview);

        var bottomBar = EditorTheme.ToolBar(panel);
        bottomBar.BorderThickness = new Thickness(0, 1, 0, 0);
        Grid.SetRow(bottomBar, 2);
        _root.Children.Add(bottomBar);
    }

    private void BuildStatusBar()
    {
        var statusBar = EditorTheme.StatusBar(out _statusText, Loc.T("GeneralPhoto.StatusReady"));
        Grid.SetRow(statusBar, 3);
        _root.Children.Add(statusBar);
    }

    private static void AddInput(Panel panel, string label, string defaultVal, out TextBox box)
    {
        var lbl = EditorTheme.FieldLabel(label);
        lbl.Margin = new Thickness(panel.Children.Count == 0 ? 0 : 12, 0, 4, 0);
        panel.Children.Add(lbl);

        box = new TextBox { Text = defaultVal, Width = 60 };
        panel.Children.Add(box);
    }

    /// <summary>顶栏上的开关按钮：尺寸与主题 Button 对齐，选中态由主题 ToggleButton 样式绘制。</summary>
    private static ToggleButton MakeToggle(string text, RoutedEventHandler onClick)
    {
        var t = new ToggleButton { Content = text, MinHeight = 28, FontSize = 13 };
        t.Click += onClick;
        return t;
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[GeneralPhotoEditScene] {s}");
    }

    private void TryLoadGeneralPhoto(string ename)
    {
        var path = _parser.GetGeneralPhotoPath(ename);
        if (!string.IsNullOrEmpty(path))
        {
            LoadImage(path);
            SetStatus(Loc.F("GeneralPhoto.StatusLoadedGeneral", ename));
        }
        else
        {
            SetStatus(Loc.F("GeneralPhoto.StatusNotFound", ename));
        }
    }

    private void LoadImage(string path)
    {
        _sourceImagePath = path;
        if (string.IsNullOrEmpty(_baseName))
            _baseName = System.IO.Path.GetFileNameWithoutExtension(path);

        var loaded = _editorService.LoadSourceImage(path);
        if (loaded == null)
        {
            SetStatus(Loc.T("GeneralPhoto.LoadFailed"));
            return;
        }

        _sourceImage?.Dispose();
        _sourceImage = loaded;

        ReadCanvasSize();
        ComposeCanvasImage();
        UpdateDisplay();
        SetStatus(Loc.F("GeneralPhoto.StatusLoaded", System.IO.Path.GetFileName(path), loaded.Width, loaded.Height));
    }

    private void ReadCanvasSize()
    {
        if (int.TryParse(_canvasWBox.Text, out int w) && w > 0) _canvasWidth = w;
        if (int.TryParse(_canvasHBox.Text, out int h) && h > 0) _canvasHeight = h;
        if (int.TryParse(_resize1WBox.Text, out int r1w) && r1w > 0) _resize1W = r1w;
        if (int.TryParse(_resize1HBox.Text, out int r1h) && r1h > 0) _resize1H = r1h;
        if (int.TryParse(_resize2WBox.Text, out int r2w) && r2w > 0) _resize2W = r2w;
        if (int.TryParse(_resize2HBox.Text, out int r2h) && r2h > 0) _resize2H = r2h;
    }

    private void ComposeCanvasImage()
    {
        if (_sourceImage == null) return;

        _canvasImage?.Dispose();

        int srcW = _sourceImage.Width;
        int srcH = _sourceImage.Height;

        double ratio = Math.Min((double)_canvasWidth / srcW, (double)_canvasHeight / srcH);
        int newW = (int)(srcW * ratio);
        int newH = (int)(srcH * ratio);

        var scaled = _sourceImage.Resize(new SKImageInfo(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
        if (scaled == null) return;

        _srcX = (_canvasWidth - newW) / 2;
        _srcY = _canvasHeight - newH;

        _canvasImage = _editorService.CreateCanvasWithImage(scaled, _canvasWidth, _canvasHeight, _bgColor, _srcX, _srcY);
        scaled.Dispose();
    }

    private void UpdateDisplay()
    {
        if (_canvasImage == null) return;

        _displayImage?.Dispose();

        int dispW = (int)(_canvasImage.Width * _scaleFactor);
        int dispH = (int)(_canvasImage.Height * _scaleFactor);
        if (dispW <= 0 || dispH <= 0) return;

        _displayImage = _canvasImage.Resize(new SKImageInfo(dispW, dispH, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
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
                    var srcRowPtr = System.IntPtr.Add(srcPtr, y * pixmap.RowBytes);
                    System.Runtime.InteropServices.Marshal.Copy(srcRowPtr, new byte[info.Width * 4], 0, info.Width * 4);
                    // Convert RGBA -> BGRA and premultiply alpha
                    var row = new byte[info.Width * 4];
                    System.Runtime.InteropServices.Marshal.Copy(srcRowPtr, row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4)
                    {
                        byte r = row[i];
                        byte g = row[i + 1];
                        byte b = row[i + 2];
                        byte a = row[i + 3];

                        // Premultiply alpha
                        if (a < 255)
                        {
                            r = (byte)(r * a / 255);
                            g = (byte)(g * a / 255);
                            b = (byte)(b * a / 255);
                        }

                        // BGRA order for WPF
                        row[i] = b;
                        row[i + 1] = g;
                        row[i + 2] = r;
                        row[i + 3] = a;
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
        catch
        {
            return null;
        }
    }

    private Point CanvasPoint(MouseEventArgs e)
    {
        var pos = e.GetPosition(_imageCanvas);
        return pos;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        var pos = CanvasPoint(e);

        if (_dragMode)
        {
            _dragStart = pos;
            e.Handled = true;
            return;
        }

        if (IsInsideSelection(pos))
        {
            _movingSelection = true;
            _moveSelStart = pos;
            _moveSelOriginal = _selectRect;
            e.Handled = true;
            return;
        }

        _selecting = true;
        _selectStart = pos;
        _selectRect = new Rect();
        _selectionBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = CanvasPoint(e);

        if (_dragMode && _sourceImage != null)
        {
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;
            _srcX += (int)(dx / _scaleFactor);
            _srcY += (int)(dy / _scaleFactor);
            _dragStart = pos;
            RecomposeAndDisplay();
            return;
        }

        if (_movingSelection)
        {
            double dx = pos.X - _moveSelStart.X;
            double dy = pos.Y - _moveSelStart.Y;
            _selectRect = new Rect(_moveSelOriginal.X + dx, _moveSelOriginal.Y + dy, _moveSelOriginal.Width, _moveSelOriginal.Height);
            UpdateSelectionDisplay();
            return;
        }

        if (_selecting)
        {
            double x0 = _selectStart.X;
            double y0 = _selectStart.Y;
            double x1 = pos.X;
            double y1 = pos.Y;
            double side = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            if (x1 < x0) x1 = x0 - side; else x1 = x0 + side;
            if (y1 < y0) y1 = y0 - side; else y1 = y0 + side;
            _selectRect = new Rect(Math.Min(x0, x1), Math.Min(y0, y1), side, side);
            UpdateSelectionDisplay();
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragMode = false;
        _dragBtn.IsChecked = false;
        _selecting = false;
        _movingSelection = false;
    }

    private void OnCanvasRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = CanvasPoint(e);
        if (IsInsideSelection(pos))
        {
            _movingSelection = true;
            _moveSelStart = pos;
            _moveSelOriginal = _selectRect;
            e.Handled = true;
        }
    }

    private void OnCanvasRightMouseUp(object sender, MouseButtonEventArgs e)
    {
        _movingSelection = false;
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_canvasImage == null) return;
        double delta = e.Delta > 0 ? ScaleStep : -ScaleStep;
        double newScale = Math.Clamp(_scaleFactor + delta, MinScale, MaxScale);
        if (Math.Abs(newScale - _scaleFactor) < 1e-6) return;
        _scaleFactor = newScale;

        MapSelectionToImageCoords();
        UpdateDisplay();
        MapSelectionFromImageCoords();
        SetStatus(Loc.F("GeneralPhoto.StatusZoom", _scaleFactor));
        e.Handled = true;
    }

    private bool IsInsideSelection(Point pos)
    {
        return _selectRect.Width > 0 && _selectRect.Height > 0 &&
               pos.X >= _selectRect.X && pos.X <= _selectRect.X + _selectRect.Width &&
               pos.Y >= _selectRect.Y && pos.Y <= _selectRect.Y + _selectRect.Height;
    }

    private Rect _imageSpaceSelectRect;

    private void MapSelectionToImageCoords()
    {
        if (_selectRect.Width <= 0 || _canvasImage == null) return;
        double imgW = _canvasImage.Width;
        double imgH = _canvasImage.Height;
        double dispW = imgW * _scaleFactor;
        double dispH = imgH * _scaleFactor;
        double offsetX = 0;
        double offsetY = 0;
        _imageSpaceSelectRect = new Rect(
            (_selectRect.X - offsetX) / _scaleFactor,
            (_selectRect.Y - offsetY) / _scaleFactor,
            _selectRect.Width / _scaleFactor,
            _selectRect.Height / _scaleFactor
        );
    }

    private void MapSelectionFromImageCoords()
    {
        if (_imageSpaceSelectRect.Width <= 0) return;
        _selectRect = new Rect(
            _imageSpaceSelectRect.X * _scaleFactor,
            _imageSpaceSelectRect.Y * _scaleFactor,
            _imageSpaceSelectRect.Width * _scaleFactor,
            _imageSpaceSelectRect.Height * _scaleFactor
        );
        UpdateSelectionDisplay();
    }

    private void RecomposeAndDisplay()
    {
        if (_sourceImage == null) return;

        _canvasImage?.Dispose();

        int srcW = _sourceImage.Width;
        int srcH = _sourceImage.Height;
        double ratio = Math.Min((double)_canvasWidth / srcW, (double)_canvasHeight / srcH);
        int newW = (int)(srcW * ratio);
        int newH = (int)(srcH * ratio);

        var scaled = _sourceImage.Resize(new SKImageInfo(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
        if (scaled == null) return;

        _canvasImage = _editorService.CreateCanvasWithImage(scaled, _canvasWidth, _canvasHeight, _bgColor, _srcX, _srcY);
        scaled.Dispose();
        UpdateDisplay();
    }

    private void OnCanvasSizeChanged()
    {
        if (_sourceImage == null) return;
        if (int.TryParse(_canvasWBox.Text, out int w) && w > 0) _canvasWidth = w;
        if (int.TryParse(_canvasHBox.Text, out int h) && h > 0) _canvasHeight = h;
        ComposeCanvasImage();
        UpdateDisplay();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_sourceImage == null) return;
        int step = Keyboard.Modifiers == ModifierKeys.Control ? 1 : 10;
        switch (e.Key)
        {
            case Key.Left:
                _srcX -= step;
                RecomposeAndDisplay();
                e.Handled = true;
                break;
            case Key.Right:
                _srcX += step;
                RecomposeAndDisplay();
                e.Handled = true;
                break;
            case Key.Up:
                _srcY -= step;
                RecomposeAndDisplay();
                e.Handled = true;
                break;
            case Key.Down:
                _srcY += step;
                RecomposeAndDisplay();
                e.Handled = true;
                break;
            case Key.Z:
                _scaleFactor = Math.Clamp(_scaleFactor * 1.1, MinScale, MaxScale);
                MapSelectionToImageCoords();
                UpdateDisplay();
                MapSelectionFromImageCoords();
                e.Handled = true;
                break;
            case Key.X:
                _scaleFactor = Math.Clamp(_scaleFactor * 0.9, MinScale, MaxScale);
                MapSelectionToImageCoords();
                UpdateDisplay();
                MapSelectionFromImageCoords();
                e.Handled = true;
                break;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _window.ReturnToMainScene();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = $"{Loc.T("GeneralPhoto.ImageFilter")}|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|{Loc.T("Common.AllFiles")}|*.*",
            Title = Loc.T("GeneralPhoto.OpenDialogTitle")
        };
        if (dlg.ShowDialog() == true)
        {
            _scaleFactor = 1.0;
            _selectRect = new Rect();
            _selectionBorder.Visibility = Visibility.Collapsed;
            LoadImage(dlg.FileName);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_canvasImage == null)
        {
            MessageBox.Show(Loc.T("GeneralPhoto.LoadImageFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_selectRect.Width <= 0 || _selectRect.Height <= 0)
        {
            MessageBox.Show(Loc.T("GeneralPhoto.SelectRegionFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReadCanvasSize();

        int x0 = (int)(_selectRect.X / _scaleFactor);
        int y0 = (int)(_selectRect.Y / _scaleFactor);
        int x1 = (int)((_selectRect.X + _selectRect.Width) / _scaleFactor);
        int y1 = (int)((_selectRect.Y + _selectRect.Height) / _scaleFactor);

        x0 = Math.Max(0, Math.Min(x0, _canvasImage.Width));
        y0 = Math.Max(0, Math.Min(y0, _canvasImage.Height));
        x1 = Math.Max(0, Math.Min(x1, _canvasImage.Width));
        y1 = Math.Max(0, Math.Min(y1, _canvasImage.Height));

        var circleCropped = _editorService.CropCircularRegion(_canvasImage, x0, y0, x1, y1);

        string outputDir;
        if (!string.IsNullOrEmpty(_baseName))
        {
            outputDir = _parser.GeneralPhotoDir;
            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
                outputDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Output");
        }
        else
        {
            outputDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Output");
        }

        var result = _editorService.SaveGeneralImages(_canvasImage, circleCropped, _baseName, outputDir, _resize1W, _resize1H, _resize2W, _resize2H);

        circleCropped?.Dispose();

        if (result.Success)
        {
            var msg = Loc.T("GeneralPhoto.SavedHeader") + "\n\n";
            if (result.GeneralPath != null) msg += Loc.F("GeneralPhoto.Path.General", result.GeneralPath) + "\n";
            if (result.CirclePath != null) msg += Loc.F("GeneralPhoto.Path.Circle", result.CirclePath) + "\n";
            if (result.HeadPath != null) msg += Loc.F("GeneralPhoto.Path.Head", result.HeadPath) + "\n";
            MessageBox.Show(msg, Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(Loc.F("GeneralPhoto.StatusSaved", _baseName));
        }
        else
        {
            MessageBox.Show(Loc.F("GeneralPhoto.SaveFailed", result.ErrorMessage), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.F("GeneralPhoto.SaveFailed", result.ErrorMessage));
        }
    }

    private void OnToggleDrag(object sender, RoutedEventArgs e)
    {
        // ToggleButton 点击时 IsChecked 已经翻转，直接以它为准
        _dragMode = _dragBtn.IsChecked == true;
        if (_dragMode)
        {
            _selectRect = new Rect();
            _selectionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnChooseColor(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfColorDialog();
        dlg.Owner = _window;
        if (dlg.ShowDialog() == true && dlg.SelectedColor is Color c)
        {
            _bgColor = new SKColor(c.R, c.G, c.B, c.A);
            _colorPreview.Background = new SolidColorBrush(c);
            if (_sourceImage != null)
            {
                ComposeCanvasImage();
                UpdateDisplay();
            }
        }
    }

    private void OnTransparentBg(object sender, RoutedEventArgs e)
    {
        _bgColor = SKColors.Transparent;
        _colorPreview.Background = Brushes.Transparent;
        if (_sourceImage != null)
        {
            ComposeCanvasImage();
            UpdateDisplay();
        }
    }
}

internal class WpfColorDialog : Window
{
    public Color? SelectedColor { get; private set; }

    private Slider _rSlider = null!, _gSlider = null!, _bSlider = null!, _aSlider = null!;
    private Border _preview = null!;

    public WpfColorDialog()
    {
        EditorTheme.Apply(this);
        Background = EditorTheme.Bg1;
        Title = Loc.T("GeneralPhoto.ColorDialogTitle");
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(16) };

        _preview = new Border
        {
            Height = 40,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1)
        };
        panel.Children.Add(_preview);

        _rSlider = MakeSlider("R", 255, panel);
        _gSlider = MakeSlider("G", 255, panel);
        _bSlider = MakeSlider("B", 255, panel);
        _aSlider = MakeSlider("A", 255, panel);
        _aSlider.Value = 255;

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = EditorTheme.PrimaryButton(Loc.T("Common.Ok"), (_, _) =>
        {
            SelectedColor = Color.FromArgb((byte)_aSlider.Value, (byte)_rSlider.Value, (byte)_gSlider.Value, (byte)_bSlider.Value);
            DialogResult = true;
        });
        okBtn.Width = 80;
        okBtn.Margin = new Thickness(0, 0, 8, 0);
        var cancelBtn = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => DialogResult = false);
        cancelBtn.Width = 80;
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        Content = panel;

        _rSlider.ValueChanged += UpdatePreview;
        _gSlider.ValueChanged += UpdatePreview;
        _bSlider.ValueChanged += UpdatePreview;
        _aSlider.ValueChanged += UpdatePreview;
        UpdatePreview(null, null);
    }

    private Slider MakeSlider(string label, int max, StackPanel panel)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = EditorTheme.Text_(label);
        Grid.SetColumn(tb, 0);
        var slider = new Slider { Minimum = 0, Maximum = max, Value = 0, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 1);
        row.Children.Add(tb);
        row.Children.Add(slider);
        panel.Children.Add(row);
        return slider;
    }

    private void UpdatePreview(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        // 预览块显示的是用户正在选的颜色本身，属于数据而不是界面配色
        _preview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)_aSlider.Value, (byte)_rSlider.Value, (byte)_gSlider.Value, (byte)_bSlider.Value));
    }
}
