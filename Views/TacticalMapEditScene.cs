using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views;

public class TacticalMapEditScene : UserControl, IDisposable
{
    private readonly MainWindow _window;
    private readonly TacticalMapEditor _editor = new();
    private OrthographicCamera? _camera;
    private SKElement _skElement = null!;
    private Grid _rootGrid = null!;
    private Grid? _titleBar;
    private TextBlock? _statusLabel;
    private TextBlock? _infoLabel;

    private bool _isPanning;
    private Point _lastMousePos;
    private TacticalMapObject? _dragObject;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private bool _dragStarted;
    private const double DragThreshold = 3.0;

    private SKBitmap? _sourceBitmap;
    private readonly Dictionary<string, SKBitmap> _objectBitmapCache = new();
    private string? _filterKeyword;
    private bool _disposed;
    private TacticalMapObject? _copiedObject;
    private SKTypeface? _cachedTypeface;
    private readonly string? _sourceDirectory;

    public TacticalMapEditScene(MainWindow window, string imageFilePath, string? xmlFilePath = null, string? sourceDirectory = null)
    {
        _window = window;
        _sourceDirectory = sourceDirectory;
        Focusable = true;
        SetupUI();
        LoadImage(imageFilePath, xmlFilePath);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void SetupUI()
    {
        _rootGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(30, 30, 36)), AllowDrop = true };
        _rootGrid.DragEnter += OnDragEnter;
        _rootGrid.DragOver += OnDragEnter;
        _rootGrid.Drop += OnDropFiles;

        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

        _skElement = new SKElement { IgnorePixelScaling = true, Focusable = true, AllowDrop = true };
        _skElement.DragEnter += OnDragEnter;
        _skElement.DragOver += OnDragEnter;
        _skElement.Drop += OnDropFiles;
        _skElement.PaintSurface += OnPaintSurface;
        Grid.SetRow(_skElement, 0);
        _rootGrid.Children.Add(_skElement);

        _titleBar = new Grid
        {
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(230, 25, 25, 30)),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(_titleBar, 1);

        var leftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var backButton = CreateButton("← 返回");
        backButton.Click += (s, e) => _window.ReturnFromTacticalEditor(_sourceDirectory);
        leftPanel.Children.Add(backButton);

        leftPanel.Children.Add(CreateSep());

        var saveButton = CreateButton("保存");
        saveButton.Click += OnSave;
        leftPanel.Children.Add(saveButton);

        var saveAsButton = CreateButton("另存为...");
        saveAsButton.Click += OnSaveAs;
        leftPanel.Children.Add(saveAsButton);

        leftPanel.Children.Add(CreateSep());

        var resetViewButton = CreateButton("重置视图");
        resetViewButton.Click += (s, e) => { _camera?.Reset(); _skElement.InvalidateVisual(); };
        leftPanel.Children.Add(resetViewButton);

        var fitButton = CreateButton("适应窗口");
        fitButton.Click += OnFitToWindow;
        leftPanel.Children.Add(fitButton);

        leftPanel.Children.Add(CreateSep());

        var copyButton = CreateButton("复制");
        copyButton.Click += OnCopy;
        leftPanel.Children.Add(copyButton);

        var pasteButton = CreateButton("粘贴");
        pasteButton.Click += OnPaste;
        leftPanel.Children.Add(pasteButton);

        var deleteButton = CreateButton("删除选中");
        deleteButton.Click += (s, e) => { _editor.DeleteSelectedObjects(); InvalidateCache(); _skElement.InvalidateVisual(); UpdateInfo(); };
        leftPanel.Children.Add(deleteButton);

        var clearSelButton = CreateButton("取消选中");
        clearSelButton.Click += (s, e) => { _editor.ClearSelection(); _skElement.InvalidateVisual(); };
        leftPanel.Children.Add(clearSelButton);

        leftPanel.Children.Add(CreateSep());

        // ---- ▼ 工具 收纳下拉菜单 ----
        var toolsMenuButton = CreateMenuButton("▼ 工具");
        var toolsMenu = new ContextMenu();
        var miImport = new MenuItem { Header = "导入图片..." };
        miImport.Click += OnImportImagesClick;
        toolsMenu.Items.Add(miImport);
        toolsMenu.Items.Add(new Separator());

        var miArrange = new MenuItem { Header = "整理对象" };
        miArrange.Click += OnArrangeObjects;
        toolsMenu.Items.Add(miArrange);

        var miFitBounds = new MenuItem { Header = "适应画布（裁剪到对象）" };
        miFitBounds.Click += OnFitObjectsBounds;
        toolsMenu.Items.Add(miFitBounds);

        var miResize = new MenuItem { Header = "调整画布大小..." };
        miResize.Click += OnResizeCanvasCustom;
        toolsMenu.Items.Add(miResize);

        toolsMenu.Items.Add(new Separator());

        var miFilter = new MenuItem { Header = "过滤对象...  (Ctrl+F)" };
        miFilter.Click += (s, e) => ShowFilterDialog();
        toolsMenu.Items.Add(miFilter);

        var miClearFilter = new MenuItem { Header = "清除过滤  (Ctrl+R)" };
        miClearFilter.Click += (s, e) => { _filterKeyword = null; _skElement.InvalidateVisual(); UpdateStatus("过滤已清除"); UpdateInfo(); };
        toolsMenu.Items.Add(miClearFilter);

        toolsMenu.Items.Add(new Separator());

        var miCopy = new MenuItem { Header = "复制  (Ctrl+C)" };
        miCopy.Click += OnCopy;
        toolsMenu.Items.Add(miCopy);

        var miPaste = new MenuItem { Header = "粘贴  (Ctrl+V)" };
        miPaste.Click += OnPaste;
        toolsMenu.Items.Add(miPaste);

        toolsMenu.Items.Add(new Separator());

        var miProps = new MenuItem { Header = "对象属性..." };
        miProps.Click += OnShowProperties;
        toolsMenu.Items.Add(miProps);

        var miSelectAll = new MenuItem { Header = "全选  (Ctrl+A)" };
        miSelectAll.Click += (s, e) =>
        {
            foreach (var obj in _editor.Objects) obj.IsSelected = true;
            _skElement.InvalidateVisual(); UpdateInfo();
        };
        toolsMenu.Items.Add(miSelectAll);

        toolsMenuButton.Click += (s, e) =>
        {
            toolsMenu.PlacementTarget = toolsMenuButton;
            toolsMenu.Placement = PlacementMode.Top;
            toolsMenu.IsOpen = true;
        };
        leftPanel.Children.Add(toolsMenuButton);

        leftPanel.Children.Add(CreateSep());

        _infoLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 150)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        leftPanel.Children.Add(_infoLabel);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0)
        };

        // ---- ▼ 导出 收纳下拉菜单 ----
        var exportMenuButton = CreateMenuButton("▼ 导出 PNG");
        var exportMenu = new ContextMenu();

        var miExpObj = new MenuItem { Header = "切出所有对象到文件夹..." };
        miExpObj.Click += OnExportObjectsPng;
        exportMenu.Items.Add(miExpObj);

        var miExpSel = new MenuItem { Header = "切出选中对象到文件夹..." };
        miExpSel.Click += OnExportSelectedPng;
        exportMenu.Items.Add(miExpSel);

        exportMenu.Items.Add(new Separator());

        var miExpCanvas = new MenuItem { Header = "导出完整画布 PNG" };
        miExpCanvas.Click += OnExportCanvasPng;
        exportMenu.Items.Add(miExpCanvas);

        exportMenuButton.Click += (s, e) =>
        {
            exportMenu.PlacementTarget = exportMenuButton;
            exportMenu.Placement = PlacementMode.Top;
            exportMenu.IsOpen = true;
        };
        rightPanel.Children.Add(exportMenuButton);

        rightPanel.Children.Add(CreateSep());

        _statusLabel = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        rightPanel.Children.Add(_statusLabel);

        _titleBar.Children.Add(leftPanel);
        _titleBar.Children.Add(rightPanel);
        _rootGrid.Children.Add(_titleBar);

        Content = _rootGrid;
    }

    private static Button CreateButton(string text)
    {
        var btn = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(2, 0, 2, 0)
        };
        btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        btn.MouseLeave += (s, e) => btn.Background = Brushes.Transparent;
        return btn;
    }

    private static Separator CreateSep() => new()
    {
        Width = 1, Height = 18,
        Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button CreateMenuButton(string text)
    {
        var btn = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(40, 120, 160, 220)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 150, 180, 220)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(2, 0, 2, 0)
        };
        btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(90, 120, 160, 220));
        btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(40, 120, 160, 220));
        return btn;
    }

    private void LoadImage(string imageFilePath, string? xmlFilePath)
    {
        try
        {
            if (!_editor.LoadFromFiles(imageFilePath, xmlFilePath))
            {
                UpdateStatus("加载失败");
                return;
            }

            _sourceBitmap = LoadBitmapFromBytes(_editor.ImageData!);
            if (_sourceBitmap == null)
            {
                UpdateStatus("图片解码失败");
                return;
            }

            _editor.SetImageSize(_sourceBitmap.Width, _sourceBitmap.Height);
            BuildObjectBitmapCache();
            UpdateStatus($"已加载: {IOPath.GetFileName(imageFilePath)} | {_editor.Objects.Count} 个对象");
            UpdateInfo();
        }
        catch (Exception ex)
        {
            UpdateStatus($"加载错误: {ex.Message}");
        }
    }

    private static SKBitmap? LoadBitmapFromBytes(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            using var decoded = SKBitmap.Decode(stream);
            if (decoded == null) return null;
            // 强制转换为 Premultiplied-RGBA 格式，确保透明通道保留
            var rgbaInfo = new SKImageInfo(decoded.Width, decoded.Height,
                SKColorType.Rgba8888, SKAlphaType.Premul, decoded.ColorSpace);
            if (decoded.Info.ColorType == SKColorType.Rgba8888 &&
                decoded.Info.AlphaType is SKAlphaType.Premul or SKAlphaType.Unpremul)
            {
                return decoded.Copy(); // 已是 RGBA 直接复制
            }
            var result = new SKBitmap(rgbaInfo);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(decoded, 0, 0);
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacEditScene] 解码图片失败: {ex.Message}");
            return null;
        }
    }

    private void BuildObjectBitmapCache()
    {
        // 【关键】改成"补缺"语义，绝不 Clear() 已有的 cache。
        // 已经存在的 _objectBitmapCache[obj.Name] 才是"权威真相"：
        //   - 用户通过 ImportImageFiles 拖入/更新的同名对象像素
        //   - 复制粘贴时按前缀/尺寸拷贝过来的独立像素
        // 如果这里 Clear 了再从 _sourceBitmap 切，等于把用户更新的纹理全部回滚！
        if (_sourceBitmap == null) return;

        foreach (var obj in _editor.Objects)
        {
            // 已有独立像素 → 直接跳过（保持权威）
            if (_objectBitmapCache.ContainsKey(obj.Name)) continue;

            try
            {
                int x = Math.Max(0, obj.X);
                int y = Math.Max(0, obj.Y);
                int w = Math.Min(obj.Width, _sourceBitmap.Width - x);
                int h = Math.Min(obj.Height, _sourceBitmap.Height - y);
                if (w <= 0 || h <= 0) continue;

                // 使用显式 Rgba8888+Premul 信息创建，确保透明通道保留
                var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
                var subset = new SKBitmap(info);
                using (var canvas = new SKCanvas(subset))
                {
                    canvas.Clear(SKColors.Transparent);
                    var srcRect = new SKRectI(x, y, x + w, y + h);
                    canvas.DrawBitmap(_sourceBitmap, srcRect, new SKRectI(0, 0, w, h));
                }
                _objectBitmapCache[obj.Name] = subset;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TacEditScene] 缓存对象 {obj.Name} 失败: {ex.Message}");
            }
        }

        // === 第 2 阶段：对于 XML/原图上根本不存在的新增对象（复制粘贴导入等），
        //     从已有同名前缀源对象拷贝一份独立像素（py版: obj.surface = src.surface.copy()）
        foreach (var obj in _editor.Objects)
        {
            if (_objectBitmapCache.ContainsKey(obj.Name)) continue;
            // 尝试从"同名前缀（_1/_2复制出的对象 → 源可能是原始名）"找像素
            TacticalMapObject? srcPeer = null;
            int u = obj.Name.LastIndexOf('_');
            if (u > 0 && int.TryParse(obj.Name.AsSpan(u + 1), out _))
            {
                string baseName = obj.Name.Substring(0, u);
                srcPeer = _editor.Objects.FirstOrDefault(o => o.Name == baseName && o.Width == obj.Width && o.Height == obj.Height);
            }
            // 否则看有没有尺寸完全一致的其他对象（一般复制粘贴会保持尺寸）
            srcPeer ??= _editor.Objects.FirstOrDefault(o => o.Width == obj.Width && o.Height == obj.Height && _objectBitmapCache.ContainsKey(o.Name));
            if (srcPeer == null) continue;
            var srcBmp = _objectBitmapCache[srcPeer.Name];
            var info = new SKImageInfo(obj.Width, obj.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srcBmp.Info.ColorSpace);
            var copy = new SKBitmap(info);
            using var cvc = new SKCanvas(copy);
            cvc.Clear(SKColors.Transparent);
            cvc.DrawBitmap(srcBmp, 0, 0);
            _objectBitmapCache[obj.Name] = copy;
        }
    }

    private void EnsureDefaultObjectIfEmpty()
    {
        if (_sourceBitmap == null) return;
        if (_editor.Objects.Count > 0) return;
        // 没有XML对象 → 自动把整张图作为"默认对象"加入（对齐py版"没有切图定义时整张图就是一个对象"的语义）
        string defaultName = IOPath.GetFileNameWithoutExtension(_editor.ImageFilePath) ?? "untitled";
        var obj = new TacticalMapObject
        {
            Name = _editor.GenerateUniqueName(defaultName),
            X = 0, Y = 0, Width = _sourceBitmap.Width, Height = _sourceBitmap.Height,
            RefX = 0, RefY = 0, IsSelected = false
        };
        _editor.AddObject(obj);
    }

    /// <summary>
    /// 用"每个对象持有的独立像素 cache"按 obj.X/Y 重拼一张新的合成底图，
    /// 替换当前 _sourceBitmap，保证 XML 的 obj.X/Y 在源图上 100% 对应对象纹理。
    /// ——对齐py版 export_canvas_as_png() "obj.surface blit 到 (obj.x,obj.y)"的保存逻辑
    /// </summary>
    private void RebuildCompositeSourceBitmap(int newWorldW, int newWorldH)
    {
        if (_sourceBitmap == null || newWorldW <= 0 || newWorldH <= 0) return;

        var info = new SKImageInfo(newWorldW, newWorldH, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
        var composite = new SKBitmap(info);
        using (var canvas = new SKCanvas(composite))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var obj in _editor.Objects)
            {
                if (obj.Width <= 0 || obj.Height <= 0) continue;
                if (obj.X >= newWorldW || obj.Y >= newWorldH) continue;
                if (!_objectBitmapCache.TryGetValue(obj.Name, out var bmp)) continue;

                // 【关键】以对象的独立像素 cache 为唯一真相：
                // 把 cache 像素按对象 XML 声明的 W/H 缩放到 (obj.X, obj.Y, obj.W, obj.H) 矩形内，
                // 然后用 canvas 自动裁剪超出画布的部分（而不是直接 continue 把整个对象丢掉）。
                // 这样 ImportImageFiles 更新尺寸/像素的内容在重建源图时也不会丢失。
                var dstRect = new SKRect(obj.X, obj.Y, obj.X + obj.Width, obj.Y + obj.Height);
                var srcRect = new SKRect(0, 0, bmp.Width, bmp.Height);
                canvas.DrawBitmap(bmp, srcRect, dstRect);
            }
        }

        // 不直接 Dispose 旧 _sourceBitmap（避免 PaintSurface 并发访问野指针），让 GC 延迟回收
        _sourceBitmap = composite;

        // 相机世界尺寸同步
        if (_camera != null)
        {
            double vw = _skElement.ActualWidth > 0 ? _skElement.ActualWidth : 800;
            double vh = _skElement.ActualHeight > 0 ? _skElement.ActualHeight : 600;
            _camera.WorldWidth = Math.Max(1, newWorldW);
            _camera.WorldHeight = Math.Max(1, newWorldH);
            _camera.FitToWindow(vw, vh);
        }
    }

    private void InvalidateCache()
    {
        BuildObjectBitmapCache();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        double vw = _skElement.ActualWidth > 0 ? _skElement.ActualWidth : 800;
        double vh = _skElement.ActualHeight > 0 ? _skElement.ActualHeight : 600;

        if (_sourceBitmap != null)
        {
            _camera = new OrthographicCamera(vw, vh, _sourceBitmap.Width, _sourceBitmap.Height);
            _camera.FitToWindow(vw, vh);
        }
        else
        {
            _camera = new OrthographicCamera(vw, vh, 1000, 1000);
        }

        _skElement.MouseLeftButtonDown += OnMouseLeftDown;
        _skElement.MouseLeftButtonUp += OnMouseLeftUp;
        _skElement.MouseRightButtonDown += OnMouseRightDown;
        _skElement.MouseRightButtonUp += OnMouseRightUp;
        _skElement.MouseMove += OnMouseMove;
        _skElement.MouseWheel += OnMouseWheel;
        _skElement.SizeChanged += OnSizeChanged;
        _skElement.KeyDown += OnKeyDown;
        this.MouseDoubleClick += OnMouseDoubleClick;

        _editor.DataChanged += OnEditorDataChanged;

        // === 关键：如果加载的是纯图片没XML，自动创建"整张图"对象 ===
        // 这样渲染逻辑就和py版统一：永远只画对象的独立cache像素
        EnsureDefaultObjectIfEmpty();
        BuildObjectBitmapCache();

        _skElement.Focus();
        Keyboard.Focus(_skElement);
        _skElement.InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _skElement.PaintSurface -= OnPaintSurface;
        _skElement.MouseLeftButtonDown -= OnMouseLeftDown;
        _skElement.MouseLeftButtonUp -= OnMouseLeftUp;
        _skElement.MouseRightButtonDown -= OnMouseRightDown;
        _skElement.MouseRightButtonUp -= OnMouseRightUp;
        _skElement.MouseMove -= OnMouseMove;
        _skElement.MouseWheel -= OnMouseWheel;
        _skElement.SizeChanged -= OnSizeChanged;
        _skElement.KeyDown -= OnKeyDown;
        this.MouseDoubleClick -= OnMouseDoubleClick;
        _editor.DataChanged -= OnEditorDataChanged;
        _cachedTypeface?.Dispose();
        _cachedTypeface = null;
    }

    private void OnEditorDataChanged()
    {
        _skElement.InvalidateVisual();
        UpdateInfo();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(new SKColor(30, 30, 36));

        if (_camera == null || _sourceBitmap == null) return;

        _camera.ViewportWidth = info.Width;
        _camera.ViewportHeight = info.Height;

        var (originX, originY) = _camera.WorldToScreen(0, 0);
        var (endX, endY) = _camera.WorldToScreen(_camera.WorldWidth, _camera.WorldHeight);
        float imgScreenW = (float)(endX - originX);
        float imgScreenH = (float)(endY - originY);

        using var bgPaint = new SKPaint { Color = new SKColor(45, 45, 50) };
        canvas.DrawRect((float)originX, (float)originY, imgScreenW, imgScreenH, bgPaint);

        using var borderPaint = new SKPaint { Color = new SKColor(80, 80, 90), StrokeWidth = 1, IsStroke = true, IsAntialias = true };
        canvas.DrawRect((float)originX, (float)originY, imgScreenW, imgScreenH, borderPaint);

        foreach (var obj in _editor.Objects)
        {
            if (!string.IsNullOrEmpty(_filterKeyword) && !obj.Name.Contains(_filterKeyword, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_camera.IsVisible(obj.X, obj.Y, obj.Width, obj.Height))
                continue;

            var (sx, sy) = _camera.WorldToScreen(obj.X, obj.Y);
            float sw = (float)(obj.Width * _camera.Zoom);
            float sh = (float)(obj.Height * _camera.Zoom);

            if (_objectBitmapCache.TryGetValue(obj.Name, out var bmp))
            {
                var destRect = new SKRect((float)sx, (float)sy, (float)sx + sw, (float)sy + sh);
                canvas.DrawBitmap(bmp, destRect);
            }
            else
            {
                using var placeholderPaint = new SKPaint { Color = new SKColor(60, 60, 70) };
                canvas.DrawRect((float)sx, (float)sy, sw, sh, placeholderPaint);
            }

            if (obj.IsSelected)
            {
                using var selPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 0, 60),
                    IsStroke = false,
                    IsAntialias = true
                };
                canvas.DrawRect((float)sx, (float)sy, sw, sh, selPaint);

                using var selBorderPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 0, 200),
                    IsStroke = true,
                    StrokeWidth = 2,
                    IsAntialias = true
                };
                canvas.DrawRect((float)sx, (float)sy, sw, sh, selBorderPaint);
            }
        }

        _cachedTypeface ??= LoadChineseTypeface();
        using var infoFont = new SKFont { Size = 13, Typeface = _cachedTypeface };
        using var infoPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        string zoomText = $"缩放: {_camera.Zoom:P0}";
        string posText = $"位置: ({_camera.PositionX:F0}, {_camera.PositionY:F0})";
        canvas.DrawText(zoomText, 10, info.Height - 50, SKTextAlign.Left, infoFont, infoPaint);
        canvas.DrawText(posText, 10, info.Height - 30, SKTextAlign.Left, infoFont, infoPaint);
    }

    private static SKTypeface LoadChineseTypeface()
    {
        // 优先直接从字体文件加载微软雅黑（Win10+自带）
        string[] fontPaths =
        {
            @"C:\Windows\Fonts\msyh.ttc",
            @"C:\Windows\Fonts\msyh.ttf",
            @"C:\Windows\Fonts\msyhbd.ttc",
            @"C:\Windows\Fonts\simhei.ttf",
            @"/System/Library/Fonts/PingFang.ttc",
            @"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            @"/usr/share/fonts/truetype/wqy/wqy-microhei.ttc"
        };
        foreach (var p in fontPaths)
        {
            if (File.Exists(p))
            {
                try { return SKTypeface.FromFile(p); }
                catch { /* ignore */ }
            }
        }
        // 退回到FamilyName匹配
        string[] names = { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "PingFang SC", "Noto Sans CJK SC", "WenQuanYi Micro Hei", "Arial Unicode MS" };
        foreach (var n in names)
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(n, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                if (tf != null) return tf;
            }
            catch { /* ignore */ }
        }
        return SKTypeface.Default;
    }

    #region Mouse Handling

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _skElement.Focus();
        Keyboard.Focus(_skElement);

        var pos = e.GetPosition(_skElement);
        _lastMousePos = pos;

        if (_camera == null) return;

        var (wx, wy) = _camera.ScreenToWorld(pos.X, pos.Y);
        var hitObj = _editor.HitTest(wx, wy);

        if (hitObj != null)
        {
            bool ctrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (ctrlDown)
            {
                _editor.ToggleSelection(hitObj);
            }
            else
            {
                _editor.SelectObject(hitObj);
            }

            _dragObject = hitObj;
            _dragOffsetX = wx - hitObj.X;
            _dragOffsetY = wy - hitObj.Y;
            _dragStarted = false;
        }
        else
        {
            _editor.ClearSelection();
            _dragObject = null;
        }

        _skElement.InvalidateVisual();
        UpdateInfo();
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragObject != null && !_dragStarted)
        {
            // It was a click, not a drag - selection already handled in MouseDown
        }
        _dragObject = null;
        _dragStarted = false;
    }

    private void OnMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        _skElement.Focus();
        Keyboard.Focus(_skElement);

        _isPanning = true;
        _lastMousePos = e.GetPosition(_skElement);
        _skElement.CaptureMouse();
    }

    private void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _skElement.ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_camera == null) return;
        var pos = e.GetPosition(_skElement);

        if (_isPanning)
        {
            double dx = _lastMousePos.X - pos.X;
            double dy = _lastMousePos.Y - pos.Y;
            _camera.Pan(dx, dy);
            _lastMousePos = pos;
            _skElement.InvalidateVisual();
            return;
        }

        if (_dragObject != null && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx = Math.Abs(pos.X - _lastMousePos.X);
            double dy = Math.Abs(pos.Y - _lastMousePos.Y);

            if (!_dragStarted && (dx > DragThreshold || dy > DragThreshold))
                _dragStarted = true;

            if (_dragStarted)
            {
                var (wx, wy) = _camera.ScreenToWorld(pos.X, pos.Y);
                int newX = (int)Math.Round(wx - _dragOffsetX);
                int newY = (int)Math.Round(wy - _dragOffsetY);
                _editor.MoveObject(_dragObject, newX, newY);
                _skElement.InvalidateVisual();
            }
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_camera == null) return;
        var pos = e.GetPosition(_skElement);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _camera.ZoomAtPoint(factor, pos.X, pos.Y);
        _skElement.InvalidateVisual();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_camera != null)
        {
            _camera.ViewportWidth = e.NewSize.Width;
            _camera.ViewportHeight = e.NewSize.Height;
        }
        _skElement.InvalidateVisual();
    }

    #endregion

    #region Keyboard

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete:
                _editor.DeleteSelectedObjects();
                InvalidateCache();
                _skElement.InvalidateVisual();
                UpdateInfo();
                e.Handled = true;
                break;
            case Key.R when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                _camera?.Reset();
                _skElement.InvalidateVisual();
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                ShowFilterDialog();
                e.Handled = true;
                break;
            case Key.A when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                foreach (var obj in _editor.Objects) obj.IsSelected = true;
                _skElement.InvalidateVisual();
                UpdateInfo();
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                OnCopy(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.V when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                OnPaste(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.R when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                _filterKeyword = null;
                _skElement.InvalidateVisual();
                UpdateStatus("过滤已清除");
                UpdateInfo();
                e.Handled = true;
                break;
            case Key.Escape:
                _editor.ClearSelection();
                _filterKeyword = null;
                _skElement.InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    #endregion

    #region Drag & Drop / Import Images

    private static readonly HashSet<string> ImportImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".tga"
    };

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        bool any = (e.Data.GetData(DataFormats.FileDrop) as string[])?
            .Any(p => ImportImageExtensions.Contains(IOPath.GetExtension(p))) == true;
        e.Effects = any ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;
        var imgFiles = files.Where(p => ImportImageExtensions.Contains(IOPath.GetExtension(p))).ToList();
        if (imgFiles.Count == 0) { UpdateStatus("拖拽的文件中没有支持的图片格式"); return; }
        ImportImageFiles(imgFiles, scaleOversized: true);
    }

    private void OnImportImagesClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.tga|所有文件|*.*",
            Title = "导入图片",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            ImportImageFiles(dlg.FileNames, scaleOversized: true);
    }

    /// <summary>
    /// 把外部图片文件作为对象加入/更新（对齐 py import_dropped_image / import_images）。
    ///   1. 同文件名的已存在对象 → 替换像素，保留旧对象的位置 X/Y
    ///   2. 新对象 → 新增对象，位置优先对齐"鼠标落点世界坐标"（dropPosWorld 不为空时）
    ///   3. 过大的图片 → 缩放到画布 0.8 倍（py版逻辑）
    ///   4. 画布不够大 → 自动扩展 _sourceBitmap 尺寸，旧像素复制过去
    /// </summary>
    private void ImportImageFiles(IEnumerable<string> files, bool scaleOversized = false, (double x, double y)? dropPosWorld = null)
    {
        if (_sourceBitmap == null) return; // 无画布不能导入

        try
        {
            // 1) 先把所有文件先解码、缩放完毕 -> 暂存到 pending 列表（失败的跳过）。
            //    所有需要 IDisposable 的资源都在这个阶段就"落地"为新的 SKBitmap（所有权转入 pending）。
            var pending = new List<(string objName, SKBitmap bmp)>();
            foreach (var f in files)
            {
                SKBitmap? final = null;
                try
                {
                    if (!File.Exists(f)) continue;

                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(f); }
                    catch { continue; }

                    using var loaded = LoadBitmapFromBytes(bytes);
                    if (loaded == null) continue;

                    int w = loaded.Width;
                    int h = loaded.Height;

                    if (scaleOversized)
                    {
                        int canvasW = Math.Max(1, _sourceBitmap.Width);
                        int canvasH = Math.Max(1, _sourceBitmap.Height);
                        if (w > canvasW || h > canvasH)
                        {
                            double sx = canvasW / (double)w;
                            double sy = canvasH / (double)h;
                            double s = Math.Min(sx, sy) * 0.8;
                            w = Math.Max(1, (int)Math.Round(w * s));
                            h = Math.Max(1, (int)Math.Round(h * s));
                        }
                    }

                    if (w == loaded.Width && h == loaded.Height)
                    {
                        final = loaded.Copy(); // 必须新建，loaded 会在 using 末尾释放
                    }
                    else
                    {
                        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
                        final = new SKBitmap(info);
                        using (var cc = new SKCanvas(final))
                        {
                            cc.Clear(SKColors.Transparent);
                            cc.DrawBitmap(loaded, new SKRect(0, 0, loaded.Width, loaded.Height), new SKRect(0, 0, w, h));
                        }
                    }
                    string objName = IOPath.GetFileName(f) ?? System.IO.Path.GetFileName(f) ?? Guid.NewGuid().ToString("N") + ".png";
                    pending.Add((objName, final));
                    final = null; // 所有权交给 pending
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TacEditScene] 导入图片失败 {f}: {ex.Message}");
                }
                finally
                {
                    // 若进入异常分支但 final 已经 new 出了，要立即释放避免泄漏
                    final?.Dispose();
                }
            }

            if (pending.Count == 0)
            {
                UpdateStatus("导入完成：没有可导入的图片（解码失败或格式不支持）");
                return;
            }

            // 2) 计算位置序列
            int placeX = dropPosWorld.HasValue ? (int)Math.Max(0, dropPosWorld.Value.x) : 0;
            int placeY = dropPosWorld.HasValue ? (int)Math.Max(0, dropPosWorld.Value.y) : 0;

            int added = 0, updated = 0, failed = 0;

            // 3) 一次性应用到编辑器和 cache。
            //    重点：cache 的旧位图不要主动 Dispose！PaintSurface 可能还在并发读取（Skia 会直接野指针崩）。
            //    旧 SKBitmap 若不再被任何 DrawBitmap 引用，GC + Skia 内部引用计数会正确回收。
            foreach (var (objName, bmp) in pending)
            {
                try
                {
                    var newObj = new TacticalMapObject
                    {
                        Name = objName,
                        X = placeX,
                        Y = placeY,
                        Width = bmp.Width,
                        Height = bmp.Height,
                        RefX = 0,
                        RefY = 0,
                        IsSelected = false
                    };
                    var (obj, isUpdate) = _editor.AddOrReplaceObject(newObj, preserveExistingPosition: true);
                    if (isUpdate) updated++; else added++;

                    // 写入 cache：不 Dispose 旧位图（交给 GC）
                    _objectBitmapCache[obj.Name] = bmp; // 所有权转入 cache，后续 IDisposable 会在场景 Dispose 时统一释放

                    if (!isUpdate)
                    {
                        placeX += 32;
                        placeY += 24;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TacEditScene] 应用对象 {objName} 失败: {ex.Message}");
                    bmp.Dispose();
                    failed++;
                }
            }

            // 4) 如果新增/更新导致 _editor.ImageWidth/Height 变大，扩展 _sourceBitmap 画布大小，保留旧像素
            EnsureSourceBitmapAtLeast(_editor.ImageWidth, _editor.ImageHeight);
            if (_camera != null)
            {
                _camera.WorldWidth = Math.Max(1, _sourceBitmap.Width);
                _camera.WorldHeight = Math.Max(1, _sourceBitmap.Height);
            }

            _skElement.InvalidateVisual();
            UpdateInfo();
            UpdateStatus($"导入完成：新增 {added} 张 / 更新 {updated} 张 / 失败 {failed} 张");
        }
        catch (Exception ex)
        {
            // 最外层兜底：防止 WPF DragDrop 未捕获异常导致直接闪退
            UpdateStatus($"导入失败：{ex.Message}");
            Debug.WriteLine($"[TacEditScene] 导入崩溃兜底: {ex}");
        }
    }

    private void EnsureSourceBitmapAtLeast(int minW, int minH)
    {
        if (_sourceBitmap == null) return;
        if (_sourceBitmap.Width >= minW && _sourceBitmap.Height >= minH) return;
        int newW = Math.Max(minW, _sourceBitmap.Width);
        int newH = Math.Max(minH, _sourceBitmap.Height);
        var info = new SKImageInfo(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
        var expanded = new SKBitmap(info);
        using var cc = new SKCanvas(expanded);
        cc.Clear(SKColors.Transparent);
        cc.DrawBitmap(_sourceBitmap, 0, 0);
        _sourceBitmap.Dispose();
        _sourceBitmap = expanded;
    }

    #endregion

    #region Toolbar Actions

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_editor.IsLoaded) return;

        bool xmlOk = true, imgOk = true;

        if (!string.IsNullOrEmpty(_editor.XmlFilePath))
            xmlOk = _editor.SaveXml();
        else
            xmlOk = SaveXmlWithDialog();

        if (!string.IsNullOrEmpty(_editor.ImageFilePath))
            imgOk = SaveCompositeImage(_editor.ImageFilePath);

        if (xmlOk && imgOk)
            UpdateStatus("已保存 (XML + 图片)");
        else if (xmlOk)
            UpdateStatus("XML已保存，图片保存失败");
        else if (imgOk)
            UpdateStatus("图片已保存，XML保存失败");
        else
            UpdateStatus("保存失败");
    }

    private void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (!_editor.IsLoaded) return;

        var dlg = new SaveFileDialog
        {
            Filter = "PNG图片|*.png|WebP图片|*.webp|所有文件|*.*",
            Title = "另存为",
            FileName = IOPath.GetFileNameWithoutExtension(_editor.ImageFilePath ?? "tacticalmap")
        };
        if (dlg.ShowDialog() != true) return;

        string imagePath = dlg.FileName;
        string xmlPath = IOPath.ChangeExtension(imagePath, ".xml");

        bool imgOk = SaveCompositeImage(imagePath);
        bool xmlOk = _editor.SaveXml(xmlPath);

        if (imgOk && xmlOk)
            UpdateStatus($"已另存为: {imagePath}");
        else
            UpdateStatus("另存为失败");
    }

    private bool SaveCompositeImage(string outputPath)
    {
        try
        {
            BuildObjectBitmapCache(); // 先确保每个对象都有独立像素（补缺语义，不会覆盖已导入更新的）
            int w = Math.Max(1, _sourceBitmap?.Width ?? 1);
            int h = Math.Max(1, _sourceBitmap?.Height ?? 1);

            // 【核心】与py版 export_canvas_as_png 完全一致：
            // 不用 _sourceBitmap 打底（它的像素是上次打包的结果，可能已过时），
            // 而是把每个对象持有的独立像素 按 obj.X/Y/W/H 缩放到 一张全新合成图上。
            // 这样保证 XML 的 obj.X/Y/W/H + 保存的图片像素，下次打开时100%对应，
            // 且 ImportImageFiles 更新的新像素/新尺寸 会完整写进 PNG。
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul,
                _sourceBitmap?.Info.ColorSpace);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            foreach (var obj in _editor.Objects)
            {
                if (obj.Width <= 0 || obj.Height <= 0) continue;
                if (!_objectBitmapCache.TryGetValue(obj.Name, out var bmp)) continue;
                var dstRect = new SKRect(obj.X, obj.Y, obj.X + obj.Width, obj.Y + obj.Height);
                var srcRect = new SKRect(0, 0, bmp.Width, bmp.Height);
                canvas.DrawBitmap(bmp, srcRect, dstRect);
            }

            using var image = surface.Snapshot();
            using var data = outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                ? image.Encode(SKEncodedImageFormat.Webp, 90)
                : image.Encode(SKEncodedImageFormat.Png, 100);

            if (data == null) return false;
            byte[] bytes = data.ToArray();
            File.WriteAllBytes(outputPath, bytes);
            Debug.WriteLine($"[TacEditScene] 图片已保存: {outputPath} ({w}x{h})");

            // 【保存后同步内存sourceBitmap】：把写盘的新合成图读回来替换内存中的_sourceBitmap。
            // 这样后续的整理对象、属性修改、切图导出等操作所基于的"源图"
            // 就和磁盘上的文件 100% 一致，不会出现"图像内容与xml数据对不上"。
            var newSource = LoadBitmapFromBytes(bytes);
            if (newSource != null)
            {
                // 不直接 Dispose 旧位图，避免 PaintSurface 并发访问时的野指针闪退
                _sourceBitmap = newSource;
                if (_camera != null)
                {
                    _camera.WorldWidth = Math.Max(1, newSource.Width);
                    _camera.WorldHeight = Math.Max(1, newSource.Height);
                }
                // 对象的cache（独立像素）永远持有正确纹理，不需要重建
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacEditScene] 保存图片失败: {ex.Message}");
            return false;
        }
    }

    private bool SaveCompositeImage(string outputPath, int clipX, int clipY, int clipW, int clipH)
    {
        try
        {
            if (clipW <= 0 || clipH <= 0) return false;
            BuildObjectBitmapCache();

            var info = new SKImageInfo(clipW, clipH, SKColorType.Rgba8888, SKAlphaType.Premul,
                _sourceBitmap?.Info.ColorSpace);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            // 【核心】同样仅用对象的独立像素按裁剪偏移+对象 W/H 缩放合成
            foreach (var obj in _editor.Objects)
            {
                if (obj.Width <= 0 || obj.Height <= 0) continue;
                if (!_objectBitmapCache.TryGetValue(obj.Name, out var bmp)) continue;
                var dstRect = new SKRect(obj.X - clipX, obj.Y - clipY, obj.X - clipX + obj.Width, obj.Y - clipY + obj.Height);
                var srcRect = new SKRect(0, 0, bmp.Width, bmp.Height);
                canvas.DrawBitmap(bmp, srcRect, dstRect);
            }

            using var image = surface.Snapshot();
            using var data = outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                ? image.Encode(SKEncodedImageFormat.Webp, 90)
                : image.Encode(SKEncodedImageFormat.Png, 100);

            if (data == null) return false;
            File.WriteAllBytes(outputPath, data.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacEditScene] 保存裁剪图片失败: {ex.Message}");
            return false;
        }
    }

    private bool SaveXmlWithDialog()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "XML文件|*.xml|所有文件|*.*",
            Title = "保存XML",
            FileName = IOPath.GetFileNameWithoutExtension(_editor.ImageFilePath ?? "tacticalmap") + ".xml"
        };
        if (dlg.ShowDialog() != true) return false;
        return _editor.SaveXml(dlg.FileName);
    }

    private void OnFitToWindow(object? sender, RoutedEventArgs e)
    {
        if (_camera == null || _sourceBitmap == null) return;
        _camera.FitToWindow(_skElement.ActualWidth, _skElement.ActualHeight);
        _skElement.InvalidateVisual();
    }

    private async void ShowFilterDialog()
    {
        var ownerWindow = System.Windows.Window.GetWindow(this) ?? _window;
        var dlg = new Views.Dialogs.SingleInputDialog(ownerWindow)
        {
            Title = "过滤对象",
            Description = "输入关键字（留空清除过滤）:",
            DefaultValue = _filterKeyword ?? ""
        };
        string? result = await dlg.ShowAsync();
        if (result != null)
        {
            _filterKeyword = string.IsNullOrWhiteSpace(result) ? null : result.Trim();
            _skElement.InvalidateVisual();
            UpdateStatus(string.IsNullOrEmpty(_filterKeyword) ? "过滤已清除" : $"过滤: {_filterKeyword}");
        }
    }

    #endregion

    private void UpdateStatus(string text)
    {
        if (_statusLabel == null) return;
        if (Dispatcher.CheckAccess())
            _statusLabel.Text = text;
        else
            Dispatcher.Invoke(() => _statusLabel.Text = text);
    }

    private void UpdateInfo()
    {
        if (_infoLabel == null) return;
        var selected = _editor.GetSelectedObjects();
        string info = $"对象: {_editor.Objects.Count}";
        if (selected.Count > 0)
        {
            var obj = selected[0];
            info += $" | 选中: {selected.Count} | {obj.Name} ({obj.X},{obj.Y}) {obj.Width}x{obj.Height}";
        }
        if (!string.IsNullOrEmpty(_filterKeyword))
            info += $" | 过滤: {_filterKeyword}";

        if (Dispatcher.CheckAccess())
            _infoLabel.Text = info;
        else
            Dispatcher.Invoke(() => _infoLabel.Text = info);
    }

    #region Copy Paste
    private void OnCopy(object? sender, EventArgs e)
    {
        var sel = _editor.GetSelectedObjects();
        if (sel.Count == 0) return;
        // 只复制第一个（与py版本一致）
        _copiedObject = TacticalMapObject.Clone(sel[0]);
        UpdateStatus($"已复制: {sel[0].Name}");
    }

    private void OnPaste(object? sender, EventArgs e)
    {
        if (_copiedObject == null)
        {
            UpdateStatus("剪贴板为空");
            return;
        }
        // 对齐py版 paste_copied_object 语义：
        //   - baseName = copied.name + "_copy"
        //   - 冲突自动加 _1/_2 后缀（DuplicateObject内部已做）
        //   - 位置 = 原位置 + (20,20)（DuplicateObject offsetX=20,offsetY=20 默认值）
        //   - 不弹对话框，要改名请用"对象属性..."窗口（与py版一致）
        string baseName = _copiedObject.Name.EndsWith("_copy", StringComparison.Ordinal)
            ? _copiedObject.Name
            : _copiedObject.Name + "_copy";
        var newObj = _editor.DuplicateObject(_copiedObject, baseName);
        _editor.ClearSelection();
        newObj.IsSelected = true;
        InvalidateCache();
        _skElement.InvalidateVisual();
        UpdateInfo();
        UpdateStatus($"已粘贴为: {newObj.Name}");
    }
    #endregion

    #region Arrange / Fit
    private void OnArrangeObjects(object? sender, RoutedEventArgs e)
    {
        if (_editor.Objects.Count == 0 || _sourceBitmap == null) return;
        int curW = Math.Max(1, _sourceBitmap.Width);
        int curH = Math.Max(1, _sourceBitmap.Height);

        // 1) 先确保每个对象都有独立像素（防止刚复制过没建cache）
        BuildObjectBitmapCache();

        // 2) 执行打包算法（自动扩/缩画布，只修改对象的 X/Y，不动纹理）
        var (newW, newH, placed, unplaced) = _editor.ArrangeObjects(curW, curH, padding: 2);

        // 3) 关键步骤！重新合成 _sourceBitmap：把对象各自独立的像素，
        //    按对象新的 X/Y/W/H 位置缩放到一张新图上（权威来源 = _objectBitmapCache）
        //    这样 XML 的 obj.X/Y 和 存盘的图像就 1:1 对应，下次打开切图不会错位
        RebuildCompositeSourceBitmap(newW, newH);

        // 【不再调用 BuildObjectBitmapCache】：
        //   - _objectBitmapCache 是对象的"独立像素权威"，
        //     刚 Rebuild 好的合成底图不应该反过来重新切像素覆盖它，
        //     否则会把用户 ImportImageFiles 更新的 W/H 不同的像素又"复原"。

        _skElement.InvalidateVisual();
        UpdateInfo();
        string msg = $"已整理 {placed}/{_editor.Objects.Count} 对象，画布 {curW}×{curH} → {newW}×{newH}";
        if (unplaced.Count > 0) msg += $"  ⚠ {unplaced.Count} 个对象仍放不下：{string.Join(", ", unplaced.Take(5))}";
        UpdateStatus(msg);
    }

    /// <summary>
    /// 画布适应（裁剪）：对齐 py 版 fit_canvas_to_objects 3156-3257 语义
    ///   - 只缩不扩（当前画布小于对象范围就直接返回）
    ///   - 目标尺寸 = 所有对象 max(X+W) + padding×2 / max(Y+H) + padding×2
    ///   - 真实修改 _sourceBitmap（裁右下）/ _editor.ImageWidth,ImageHeight / camera.WorldWidth,WorldHeight
    ///   - 裁剪后重建对象独立像素 cache
    /// </summary>
    private void OnFitObjectsBounds(object? sender, RoutedEventArgs e)
    {
        if (_camera == null || _editor.Objects.Count == 0 || _sourceBitmap == null) return;
        int curW = _sourceBitmap.Width;
        int curH = _sourceBitmap.Height;

        // 1) 计算边界（py版 padding=20，保持一致）
        int padding = 20;
        int maxX = 0, maxY = 0;
        foreach (var obj in _editor.Objects)
        {
            if (obj.X + obj.Width > maxX) maxX = obj.X + obj.Width;
            if (obj.Y + obj.Height > maxY) maxY = obj.Y + obj.Height;
        }
        int targetW = maxX + padding * 2;
        int targetH = maxY + padding * 2;

        // 2) py版语义："当前画布小于等于需要尺寸就不扩（只缩不扩）"
        if (targetW >= curW && targetH >= curH)
        {
            UpdateStatus($"画布已是紧凑尺寸（{curW}×{curH}），无需裁剪");
            return;
        }
        int finalW = Math.Min(curW, Math.Max(1, targetW));
        int finalH = Math.Min(curH, Math.Max(1, targetH));

        // 3) 执行真实裁剪，同步编辑器/相机/对象cache
        BuildObjectBitmapCache();
        ResizeSourceBitmap(finalW, finalH);
        BuildObjectBitmapCache();
        _skElement.InvalidateVisual();
        UpdateInfo();
        UpdateStatus($"画布已裁剪适应对象：{curW}×{curH} → {finalW}×{finalH}");
    }

    /// <summary>
    /// 真正地改变 _sourceBitmap 的尺寸，同步 _editor.ImageWidth/ImageHeight 和相机世界尺寸。
    ///   - 新尺寸 > 旧尺寸：右下追加透明像素（对齐py版 resize_canvas_to_custom_size 的扩大空白区行为）
    ///   - 新尺寸 < 旧尺寸：从左上角裁切，超出部分被丢弃
    /// 对象独立像素 cache 不变（永远持有正确独立纹理）；调用方按需决定是否重建cache。
    /// </summary>
    private void ResizeSourceBitmap(int newW, int newH)
    {
        if (_sourceBitmap == null || newW <= 0 || newH <= 0) return;
        int oldW = _sourceBitmap.Width;
        int oldH = _sourceBitmap.Height;
        if (newW == oldW && newH == oldH) return;

        var info = new SKImageInfo(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
        var dst = new SKBitmap(info);
        using (var cc = new SKCanvas(dst))
        {
            cc.Clear(SKColors.Transparent);
            int copyW = Math.Min(oldW, newW);
            int copyH = Math.Min(oldH, newH);
            if (copyW > 0 && copyH > 0)
                cc.DrawBitmap(_sourceBitmap, new SKRectI(0, 0, copyW, copyH), new SKRectI(0, 0, copyW, copyH));
        }
        _sourceBitmap.Dispose();
        _sourceBitmap = dst;

        _editor.SetImageSize(newW, newH);
        if (_camera != null)
        {
            _camera.WorldWidth = newW;
            _camera.WorldHeight = newH;
        }
    }

    /// <summary>
    /// 自定义调整画布大小（py版 resize_canvas_to_custom_size 3262-3338）：
    ///   连弹两次输入框分别要宽/高，数字无效则取消；执行 ResizeSourceBitmap
    /// </summary>
    private async void OnResizeCanvasCustom(object? sender, RoutedEventArgs e)
    {
        if (_sourceBitmap == null) return;
        var owner = Window.GetWindow(this) ?? _window;

        string? wStr = await new Views.Dialogs.SingleInputDialog(owner)
        {
            Title = "调整画布大小",
            Description = $"请输入新的画布宽度（当前：{_sourceBitmap.Width}）：",
            DefaultValue = _sourceBitmap.Width.ToString()
        }.ShowAsync();
        if (wStr == null) return;
        if (!int.TryParse(wStr.Trim(), out int newW) || newW <= 0)
        {
            UpdateStatus("宽度不是有效数字，已取消");
            return;
        }

        string? hStr = await new Views.Dialogs.SingleInputDialog(owner)
        {
            Title = "调整画布大小",
            Description = $"请输入新的画布高度（当前：{_sourceBitmap.Height}）：",
            DefaultValue = _sourceBitmap.Height.ToString()
        }.ShowAsync();
        if (hStr == null) return;
        if (!int.TryParse(hStr.Trim(), out int newH) || newH <= 0)
        {
            UpdateStatus("高度不是有效数字，已取消");
            return;
        }

        int oldW = _sourceBitmap.Width;
        int oldH = _sourceBitmap.Height;
        BuildObjectBitmapCache();
        ResizeSourceBitmap(newW, newH);
        BuildObjectBitmapCache();
        _skElement.InvalidateVisual();
        UpdateInfo();
        UpdateStatus($"画布已调整：{oldW}×{oldH} → {newW}×{newH}");
    }
    #endregion

    #region Properties
    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_camera == null) return;
        var pos = e.GetPosition(_skElement);
        var (wx, wy) = _camera.ScreenToWorld(pos.X, pos.Y);
        var obj = _editor.HitTest(wx, wy);
        if (obj != null)
        {
            _editor.ClearSelection();
            obj.IsSelected = true;
            _skElement.InvalidateVisual();
            UpdateInfo();
            ShowObjectPropertiesDialog(obj);
            e.Handled = true;
        }
    }

    private void OnShowProperties(object? sender, RoutedEventArgs e)
    {
        var sel = _editor.GetSelectedObjects();
        if (sel.Count == 0)
        {
            UpdateStatus("请先选择一个对象");
            return;
        }
        ShowObjectPropertiesDialog(sel[0]);
    }

    private async void ShowObjectPropertiesDialog(TacticalMapObject obj)
    {
        var owner = Window.GetWindow(this) ?? _window;
        int srcW = _sourceBitmap?.Width ?? int.MaxValue;
        int srcH = _sourceBitmap?.Height ?? int.MaxValue;
        using var dlg = new Views.Dialogs.ObjectPropertiesDialog(owner, obj, srcW, srcH);
        bool accepted = await dlg.ShowAsync();

        InvalidateCache();
        _skElement.InvalidateVisual();
        UpdateInfo();
        UpdateStatus(accepted ? $"属性已更新: {obj.Name}  ({obj.X},{obj.Y}) +{obj.Width}x{obj.Height}  ref({obj.RefX},{obj.RefY})"
                               : "已取消编辑");
    }
    #endregion

    #region Export PNG
    // ---------- helpers ----------
    private static readonly string[] _imageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".tga" };
    private static string StripImageExtensions(string name)
    {
        foreach (var ext in _imageExts)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        }
        return name;
    }
    private static string SanitizeFileName(string name)
    {
        char[] invalid = IOPath.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        var r = sb.ToString().Trim();
        return string.IsNullOrEmpty(r) ? "unnamed" : r;
    }

    /// <summary>
    /// 获取对象的独立像素（对齐 py 版 obj.get_surface() 语义）：
    ///   1) 优先从 _objectBitmapCache 取（永远是正确的独立纹理，整理/导入后永远不走sourceBitmap的旧像素）
    ///   2) 取不到再 fallback 从 _sourceBitmap 按 obj.X/Y/W/H 切（兼容刚从XML打开还没建cache的情况）
    /// 返回值是调用方持有的 SKBitmap，用完需 Dispose。
    /// </summary>
    private SKBitmap? GetObjectPixels(TacticalMapObject obj)
    {
        if (obj.Width <= 0 || obj.Height <= 0) return null;

        // 【首选】对象独立像素缓存（整理对象/导入图片后的唯一正确来源）
        if (_objectBitmapCache.TryGetValue(obj.Name, out var cached))
        {
            var info = new SKImageInfo(cached.Width, cached.Height, SKColorType.Rgba8888, SKAlphaType.Premul, cached.Info.ColorSpace);
            var clone = new SKBitmap(info);
            using (var cc = new SKCanvas(clone))
            {
                cc.Clear(SKColors.Transparent);
                cc.DrawBitmap(cached, 0, 0);
            }
            return clone;
        }

        // 【fallback】没有cache时按XML坐标从sourceBitmap切
        if (_sourceBitmap == null) return null;
        int x = Math.Clamp(obj.X, 0, Math.Max(0, _sourceBitmap.Width - 1));
        int y = Math.Clamp(obj.Y, 0, Math.Max(0, _sourceBitmap.Height - 1));
        int w = Math.Clamp(obj.Width, 1, Math.Max(1, _sourceBitmap.Width - x));
        int h = Math.Clamp(obj.Height, 1, Math.Max(1, _sourceBitmap.Height - y));
        var info2 = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul, _sourceBitmap.Info.ColorSpace);
        var subset = new SKBitmap(info2);
        using var subCanvas = new SKCanvas(subset);
        subCanvas.Clear(SKColors.Transparent);
        subCanvas.DrawBitmap(_sourceBitmap, new SKRectI(x, y, x + w, y + h), new SKRectI(0, 0, w, h));
        return subset;
    }

    // 保留旧函数名（兼容可能其他未查找到的地方），走新语义
    private SKBitmap? ClipFromSourceBitmap(TacticalMapObject obj) => GetObjectPixels(obj);

    private static bool SaveBitmapAsPng(SKBitmap bmp, string path)
    {
        using var img = bmp.Encode(SKEncodedImageFormat.Png, 100);
        if (img == null) return false;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        img.SaveTo(fs);
        return true;
    }

    #region Native FolderPicker (COM IFileDialog, no WinForms)
    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    [ClassInterface(ClassInterfaceType.None)]
    private class FileOpenDialogRCW { }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E40750105DCA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        [PreserveSig] int SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        [PreserveSig] int SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] int Advise(IFileDialogEvents pfde, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(FOS fos);
        [PreserveSig] int GetOptions(out FOS pfos);
        [PreserveSig] int SetDefaultFolder(IShellItem psi);
        [PreserveSig] int SetFolder(IShellItem psi);
        [PreserveSig] int GetFolder(out IShellItem ppsi);
        [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] int GetResult(out IShellItem ppsi);
        [PreserveSig] int AddPlace(IShellItem psi, int alignment);
        [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        [PreserveSig] int Close(int hr);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(IntPtr pFilter);
        [PreserveSig] int GetResults(out IntPtr ppenum);
        [PreserveSig] int GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("973510DB-7D7F-452B-8975-74A85828D354")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialogEvents { }

    [Flags]
    private enum FOS : uint
    {
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_ALLNONSTORAGEITEMS = 0x00000080,
        FOS_NOVALIDATE = 0x00000100,
        FOS_ALLOWMULTISELECT = 0x00000200,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_CREATEPROMPT = 0x00002000,
        FOS_SHAREAWARE = 0x00004000,
        FOS_NOREADONLYRETURN = 0x00008000,
        FOS_NOTESTFILECREATE = 0x00010000,
        FOS_HIDEMRUPLACES = 0x00020000,
        FOS_HIDEPINNEDPLACES = 0x00040000,
        FOS_NODEREFERENCELINKS = 0x00100000,
        FOS_DONTADDTORECENT = 0x02000000,
        FOS_FORCESHOWHIDDEN = 0x10000000,
        FOS_DEFAULTNOMINIMODE = 0x20000000
    }

    private enum SIGDN : uint
    {
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004C000,
        SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
        SIGDN_FILESYSPATH = 0x80058000,
        SIGDN_NORMALDISPLAY = 0,
        SIGDN_PARENTRELATIVE = 0x80080001,
        SIGDN_PARENTRELATIVEEDITING = 0x80031001,
        SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007C001,
        SIGDN_PARENTRELATIVEPARSING = 0x80018001,
        SIGDN_URL = 0x80068000
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    private string? PickFolder(string title)
    {
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);
            dialog.SetTitle(title);

            // 预设置初始目录
            string? initial = IOPath.GetDirectoryName(_editor.ImageFilePath ?? string.Empty);
            if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
            {
                try
                {
                    Guid shellItemIid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                    SHCreateItemFromParsingName(initial, IntPtr.Zero, ref shellItemIid, out var folder);
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
                catch { /* ignore, user can still browse */ }
            }

            var owner = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this) ?? _window).Handle;
            int hr = dialog.Show(owner);
            if (hr != 0) return null; // 用户取消或错误 (HRESULT: 1 = 用户取消)

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
            Marshal.ReleaseComObject(item);
            Marshal.ReleaseComObject(dialog);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            // Vista以下或COM失败时，退回到OpenFileDialog trick模式
            return PickFolderLegacy(title);
        }
    }

    private string? PickFolderLegacy(string title)
    {
        var dlg = new OpenFileDialog
        {
            Title = title + "  (选择文件夹后，在此处点‘打开’)",
            Filter = "Folder|*.folder.dummy",
            FilterIndex = 1,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "选择此文件夹"
        };
        if (dlg.ShowDialog(Window.GetWindow(this) ?? _window) != true) return null;
        string? dir = IOPath.GetDirectoryName(dlg.FileName);
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }
    #endregion

    // ---------- handlers ----------
    private void OnExportObjectsPng(object? sender, RoutedEventArgs e)
    {
        if (_sourceBitmap == null || _editor.Objects.Count == 0)
        {
            UpdateStatus("无对象可导出");
            return;
        }
        // 尊重过滤条件（与py版一致）
        List<TacticalMapObject> targets;
        string filterNote = string.Empty;
        if (!string.IsNullOrEmpty(_filterKeyword))
        {
            targets = _editor.Objects
                .Where(o => o.Name.IndexOf(_filterKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            filterNote = $" (过滤: '{_filterKeyword}')";
        }
        else
        {
            targets = _editor.Objects.ToList();
        }
        if (targets.Count == 0)
        {
            UpdateStatus($"没有可导出的对象{filterNote}");
            return;
        }

        string? folder = PickFolder($"选择保存{targets.Count}个对象PNG的文件夹{filterNote}");
        if (folder == null) { UpdateStatus("已取消导出"); return; }

        int ok = 0, fail = 0;
        var nameUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in targets)
        {
            try
            {
                using var bmp = ClipFromSourceBitmap(obj);
                if (bmp == null) { fail++; continue; }
                string baseName = SanitizeFileName(StripImageExtensions(obj.Name));
                string fileName = baseName + ".png";
                // 防止名称冲突
                if (nameUsed.Contains(fileName))
                {
                    int i = 1;
                    while (nameUsed.Contains($"{baseName}_{i}.png")) i++;
                    fileName = $"{baseName}_{i}.png";
                }
                nameUsed.Add(fileName);
                string fullPath = IOPath.Combine(folder, fileName);
                if (SaveBitmapAsPng(bmp, fullPath)) ok++;
                else fail++;
            }
            catch { fail++; }
        }
        UpdateStatus($"批量导出完成{filterNote}: 成功 {ok} 张, 失败 {fail} 张 → {IOPath.GetFileName(folder)}");
    }

    private void OnExportSelectedPng(object? sender, RoutedEventArgs e)
    {
        if (_sourceBitmap == null) return;
        var sel = _editor.GetSelectedObjects();
        if (sel.Count == 0)
        {
            UpdateStatus("请先选择要导出的对象");
            return;
        }

        string? folder = PickFolder($"选择保存 {sel.Count} 个选中对象 PNG 的文件夹");
        if (folder == null) { UpdateStatus("已取消导出"); return; }

        int ok = 0, fail = 0;
        var nameUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in sel)
        {
            try
            {
                using var bmp = ClipFromSourceBitmap(obj);
                if (bmp == null) { fail++; continue; }
                string baseName = SanitizeFileName(StripImageExtensions(obj.Name));
                string fileName = baseName + ".png";
                if (nameUsed.Contains(fileName))
                {
                    int i = 1;
                    while (nameUsed.Contains($"{baseName}_{i}.png")) i++;
                    fileName = $"{baseName}_{i}.png";
                }
                nameUsed.Add(fileName);
                string fullPath = IOPath.Combine(folder, fileName);
                if (SaveBitmapAsPng(bmp, fullPath)) ok++;
                else fail++;
            }
            catch { fail++; }
        }
        UpdateStatus($"选中对象导出完成: 成功 {ok} 张, 失败 {fail} 张 → {IOPath.GetFileName(folder)}");
    }

    private void OnExportCanvasPng(object? sender, RoutedEventArgs e)
    {
        if (_sourceBitmap == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "PNG图片|*.png",
            Title = "导出完整画布为PNG（源图原样）",
            FileName = IOPath.GetFileNameWithoutExtension(_editor.ImageFilePath ?? "canvas") + ".png"
        };
        if (dlg.ShowDialog() != true) return;
        using var encoded = _sourceBitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded == null) { UpdateStatus("导出失败"); return; }
        File.WriteAllBytes(dlg.FileName, encoded.ToArray());
        UpdateStatus($"已导出画布: {IOPath.GetFileName(dlg.FileName)} ({_sourceBitmap.Width}x{_sourceBitmap.Height})");
    }
    #endregion

    #region Right-click Context Menu
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        // 如果右键没有拖动平移(即简单点击),弹出上下文菜单
        if (_isPanning || e.Handled) return;
        if (_camera == null) return;
        var screenPos = e.GetPosition(this);
        var pos = e.GetPosition(_skElement);
        var (wx, wy) = _camera.ScreenToWorld(pos.X, pos.Y);
        var obj = _editor.HitTest(wx, wy);
        ShowContextMenu(screenPos, obj);
        e.Handled = true;
    }

    private void ShowContextMenu(Point screenPos, TacticalMapObject? hitObject)
    {
        var menu = new ContextMenu();

        if (hitObject != null)
        {
            // 选中此对象
            var select = new MenuItem { Header = $"选中: {hitObject.Name}" };
            select.Click += (s, e) =>
            {
                _editor.ClearSelection();
                hitObject.IsSelected = true;
                _skElement.InvalidateVisual();
                UpdateInfo();
            };
            menu.Items.Add(select);

            var props = new MenuItem { Header = "编辑属性..." };
            props.Click += (s, e) =>
            {
                _editor.ClearSelection();
                hitObject.IsSelected = true;
                ShowObjectPropertiesDialog(hitObject);
            };
            menu.Items.Add(props);

            var copy = new MenuItem { Header = "复制 (Ctrl+C)" };
            copy.Click += (s, e) => { _copiedObject = TacticalMapObject.Clone(hitObject); UpdateStatus($"已复制: {hitObject.Name}"); };
            menu.Items.Add(copy);

            var exportPng = new MenuItem { Header = "导出此对象为PNG..." };
            exportPng.Click += (s, e) =>
            {
                using var bmp = GetObjectPixels(hitObject);
                if (bmp == null) { UpdateStatus("导出失败：无法获取对象像素"); return; }
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG图片|*.png",
                    Title = $"导出对象: {hitObject.Name}",
                    FileName = SanitizeFileName(StripImageExtensions(hitObject.Name)) + ".png"
                };
                if (dlg.ShowDialog(Window.GetWindow(this) ?? _window) == true)
                {
                    if (SaveBitmapAsPng(bmp, dlg.FileName))
                        UpdateStatus($"已导出对象: {IOPath.GetFileName(dlg.FileName)} ({bmp.Width}×{bmp.Height})");
                    else
                        UpdateStatus("导出失败");
                }
            };
            menu.Items.Add(exportPng);

            menu.Items.Add(new Separator());

            var del = new MenuItem { Header = "删除 (Del)" };
            del.Click += (s, e) =>
            {
                hitObject.IsSelected = true;
                _editor.DeleteSelectedObjects();
                InvalidateCache();
                _skElement.InvalidateVisual();
                UpdateInfo();
            };
            menu.Items.Add(del);
        }
        else
        {
            var paste = new MenuItem { Header = "粘贴 (Ctrl+V)", IsEnabled = _copiedObject != null };
            paste.Click += (s, e) => OnPaste(s, EventArgs.Empty);
            menu.Items.Add(paste);

            menu.Items.Add(new Separator());

            var resetCam = new MenuItem { Header = "重置视图 (R)" };
            resetCam.Click += (s, e) => { _camera?.Reset(); _skElement.InvalidateVisual(); };
            menu.Items.Add(resetCam);

            var fitAll = new MenuItem { Header = "适应窗口" };
            fitAll.Click += (s, e) => OnFitToWindow(s, e);
            menu.Items.Add(fitAll);

            var fitBounds = new MenuItem { Header = "适应画布（裁剪到对象）", IsEnabled = _editor.Objects.Count > 0 };
            fitBounds.Click += (s, e) => OnFitObjectsBounds(s, e);
            menu.Items.Add(fitBounds);

            menu.Items.Add(new Separator());

            var arrange = new MenuItem { Header = "整理对象", IsEnabled = _editor.Objects.Count > 0 };
            arrange.Click += (s, e) => OnArrangeObjects(s, e);
            menu.Items.Add(arrange);

            var clearFilter = new MenuItem { Header = "清除过滤 (Ctrl+R)", IsEnabled = !string.IsNullOrEmpty(_filterKeyword) };
            clearFilter.Click += (s, e) => { _filterKeyword = null; _skElement.InvalidateVisual(); UpdateStatus("过滤已清除"); UpdateInfo(); };
            menu.Items.Add(clearFilter);
        }

        if (menu.Items.Count > 0)
        {
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = screenPos.X;
            menu.VerticalOffset = screenPos.Y;
            menu.IsOpen = true;
        }
    }
    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var bmp in _objectBitmapCache.Values)
            bmp.Dispose();
        _objectBitmapCache.Clear();

        _sourceBitmap?.Dispose();
        _sourceBitmap = null;
    }
}