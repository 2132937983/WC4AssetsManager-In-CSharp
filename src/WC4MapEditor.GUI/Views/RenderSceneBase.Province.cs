using System.Windows.Input;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// RenderSceneBase 的省份编辑部分：手绘边界（省份编辑模式 U 键）的鼠标绘制与红色覆盖层、
/// 从背景图识别省区的调参命令 bg_province，以及省区图层 / 省会标记的控制台开关。
/// </summary>
public abstract partial class RenderSceneBase
{
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

    /// <summary>
    /// 把当前地图上画的边界同步给渲染层。不在省份模式、没建过画布、或画布属于别的地图（换了地图）时关闭覆盖层，
    /// 免得上一张地图的边界叠在新地图上。
    /// </summary>
    private void RefreshBoundaryOverlay()
    {
        var mode = ActiveProvinceMode;
        var mask = mode?.GetBoundaryMask(_mapData);
        if (mode == null || mask == null)
        {
            _renderEngine.SetProvinceBoundaryOverlay(null, 0, 0);
            return;
        }

        _renderEngine.SetProvinceBoundaryOverlay(mask, mode.BoundaryPixelWidth, mode.BoundaryPixelHeight);
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

        mode.EnsureBoundaryCanvas(_mapData);

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

    // ==================== 控制台命令 ====================

    /// <summary>
    /// 注册省区相关的控制台命令：
    /// <c>bg_province</c> 服务于"从背景图识别省区"（省份编辑模式 P 键）的现场调参，阈值不合适时直接改参数重跑；
    /// <c>render_province</c> / <c>render_provincecapital</c> 手动开关省区图层与省会标记（对齐 VB 控制台的
    /// <c>render province</c> / <c>render provincecapital</c>），比如在建筑模式里临时看一眼省界。
    /// </summary>
    private void RegisterProvinceCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("bg_province", RunBackgroundProvinceCommand,
            "从背景图识别省区（同省份编辑模式 P 键），可附带阈值调参，并导出诊断图",
            "[灰度阈值] [梯度阈值] [最小区域像素数] [最小界线像素数]");
        cm.RegisterCommand("render_province", args => SetProvinceLayerVisible(args, capitalMarkers: false),
            "显示 / 隐藏省区图层（不带参数时切换；切换编辑模式后按模式重新设置）", "[true|false]");
        cm.RegisterCommand("render_provincecapital", args => SetProvinceLayerVisible(args, capitalMarkers: true),
            "显示 / 隐藏省会标记（只在省区图层开着时可见；不带参数时切换）", "[true|false]");
    }

    private void RunBackgroundProvinceCommand(string[] args)
    {
        if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

        var provider = _renderEngine.ViewLayerImageProvider;
        if (provider == null || !provider.HasImage)
        {
            _debugConsole.WriteLine(Loc.T("Render.Console.NoBackgroundImage"));
            return;
        }

        int darkThreshold = BackgroundProvinceGenerator.AutoDarkThreshold;
        int gradientThreshold = BackgroundProvinceGenerator.DefaultGradientThreshold;
        int minRegionPixels = BackgroundProvinceGenerator.DefaultMinRegionPixels;
        int minBoundaryPixels = BackgroundProvinceGenerator.DefaultMinBoundaryPixels;

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

        // 省会既可能来自 Capitals 列表，也可能是 X 键设的"省区值 == 自身索引"的格子，
        // 所以不预先检查 Capitals（以前列表为空就拒绝执行），交给生成器统一识别、没有省会时由它报告。
        // 与 P 键一样整体记一步撤销。
        ModifierResult result = default;
        _editModeManager.RecordMultiCellProvinceChange(Loc.T("Render.Console.BgProvinceUndo", "从背景图识别省区"),
            () => result = modifier.GenerateProvincesFromBackground(
                provider, darkThreshold, gradientThreshold, minRegionPixels, minBoundaryPixels, ExportProvinceDebugImages));

        string message = result.Message ?? string.Empty;
        _debugConsole.WriteLine(message);
        _editModeManager.RaiseStatusMessage(message);
        if (result.Success)
            OnEditModeDataModified(this, EventArgs.Empty);
        else
            _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 手动开关省区图层 / 省会标记。只改渲染开关、不改数据；切换编辑模式时各层按新模式的声明重新设置。
    /// </summary>
    private void SetProvinceLayerVisible(string[] args, bool capitalMarkers)
    {
        bool current = capitalMarkers ? _renderEngine.EnableProvinceCapitalRender : _renderEngine.EnableProvinceRender;
        bool? requested = args.Length == 0 ? !current : ParseProvinceLayerSwitch(args[0]);
        if (requested is not bool visible)
        {
            _debugConsole.WriteLine(Loc.F("Render.Console.ProvinceLayer.Usage",
                capitalMarkers ? "render_provincecapital" : "render_province"));
            return;
        }

        if (capitalMarkers)
            _renderEngine.EnableProvinceCapitalRender = visible;
        else
            _renderEngine.EnableProvinceRender = visible;

        string state = visible ? Loc.T("Render.Console.ProvinceLayer.On", "开") : Loc.T("Render.Console.ProvinceLayer.Off", "关");
        _debugConsole.WriteLine(Loc.F(capitalMarkers ? "Render.Console.ProvinceLayer.Capital" : "Render.Console.ProvinceLayer.Province", state));
        _skElement.InvalidateVisual();
    }

    private static bool? ParseProvinceLayerSwitch(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "on" or "1" => true,
        "false" or "off" or "0" => false,
        _ => null
    };

    /// <summary>
    /// 诊断：bg_province 调参时导出灰度图与界线掩码，用于人工核对识别效果
    /// （只有这条控制台命令导出；P 键不再每次都往程序目录写图）。
    /// <para>
    /// 重点看 <c>3_boundary.png</c>：合格的界线在图上应当<b>闭合连续</b>。
    /// 有断口就会让相邻两省在像素级连通，之后按"最近省会"扩展时越过真实省界去抢地盘，
    /// 表现为"某个省的地盘明显伸进了邻省"。
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
}
