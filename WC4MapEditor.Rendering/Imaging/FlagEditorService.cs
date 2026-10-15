using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.Rendering.Imaging;

public class FlagEditorService : IFlagEditorService
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private SKBitmap? _flagOverlayImage;
    private bool _flagOverlayLoaded;

    /// <summary>
    /// 懒加载 Texture/FlagOverlay/flagoverlay.png。
    /// 原先由 ConfigManager 承载，因涉及 SkiaSharp 类型已下沉到成像层。
    /// </summary>
    private SKBitmap? GetFlagOverlayImage()
    {
        if (_flagOverlayLoaded) return _flagOverlayImage;
        _flagOverlayLoaded = true;

        try
        {
            string path = Path.Combine(ConfigManager.Instance.GetTexturePath("FlagOverlay"), "flagoverlay.png");
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[FlagEditorService] flagoverlay.png 不存在: {path}");
                return null;
            }

            using var stream = File.OpenRead(path);
            _flagOverlayImage = SKBitmap.Decode(stream);
            Debug.WriteLine($"[FlagEditorService] 加载 flagoverlay.png: {_flagOverlayImage?.Width}x{_flagOverlayImage?.Height}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 加载 flagoverlay.png 失败: {ex.Message}");
        }

        return _flagOverlayImage;
    }

    public SKBitmap? LoadSourceImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 加载图片失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CreateCanvasWithImage(SKBitmap sourceImage, int canvasWidth, int canvasHeight, SKColor bgColor, int srcX, int srcY)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var canvas = new SKBitmap(info);
            using var surface = new SKCanvas(canvas);

            surface.Clear(bgColor);

            int drawX = srcX + (canvasWidth - sourceImage.Width) / 2;
            int drawY = srcY + (canvasHeight - sourceImage.Height) / 2;
            surface.DrawBitmap(sourceImage, drawX, drawY);

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建画布失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CropCircularRegion(SKBitmap image, int x0, int y0, int x1, int y1)
    {
        try
        {
            int cx = (x0 + x1) / 2;
            int cy = (y0 + y1) / 2;
            int diameter = Math.Min(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            int radius = diameter / 2;

            if (diameter <= 0) return null;

            var info = new SKImageInfo(diameter, diameter, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var path = new SKPath();
            path.AddCircle(radius, radius, radius);
            canvas.ClipPath(path);

            // 从源图像的指定区域绘制到目标圆形区域
            // 源区域: 以(cx,cy)为中心，直径为diameter的矩形
            int srcLeft = cx - radius;
            int srcTop = cy - radius;
            canvas.DrawBitmap(image, new SKRectI(srcLeft, srcTop, srcLeft + diameter, srcTop + diameter),
                new SKRect(0, 0, diameter, diameter));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 裁剪圆形区域失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? ResizeImage(SKBitmap image, int width, int height)
    {
        try
        {
            var info = new SKImageInfo(width, height, image.ColorType, image.AlphaType);
            return image.Resize(info, HighQuality);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 缩放图片失败: {ex.Message}");
            return null;
        }
    }

    public bool SaveImage(SKBitmap image, string path, SKEncodedImageFormat format, int quality)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var data = image.Encode(format, quality);
            if (data == null) return false;
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 保存图片失败: {ex.Message}");
            return false;
        }
    }

    public FlagEditorSaveResult SaveFlagImages(SKBitmap canvasImage, SKBitmap? circleCropped, string baseName, string outputDir, int resize1W, int resize1H, int resize2W, int resize2H)
    {
        var result = new FlagEditorSaveResult();
        try
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var flagPath = Path.Combine(outputDir, $"flag_{baseName}.png");
            if (!SaveImage(canvasImage, flagPath, SKEncodedImageFormat.Png, 100))
            {
                result.ErrorMessage = "保存国旗图片失败";
                return result;
            }
            result.FlagPath = flagPath;

            if (circleCropped != null)
            {
                var circlePath = Path.Combine(outputDir, $"flag_circle_{baseName}.png");
                if (SaveImage(circleCropped, circlePath, SKEncodedImageFormat.Png, 100))
                    result.CirclePath = circlePath;

                using var resizedSmall = ResizeImage(circleCropped, resize2W, resize2H);
                if (resizedSmall != null)
                {
                    var smallPath = Path.Combine(outputDir, $"f_{baseName}.png");
                    if (SaveImage(resizedSmall, smallPath, SKEncodedImageFormat.Png, 100))
                        result.SmallPath = smallPath;
                }
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    // ========== 新增国旗制作功能 ==========

    public SKBitmap? CreateTiledCanvas(SKBitmap? sourceImage, int canvasWidth, int canvasHeight)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var canvas = new SKBitmap(info);
            using var surface = new SKCanvas(canvas);

            // 透明背景
            surface.Clear(SKColors.Transparent);

            if (sourceImage != null)
            {
                // 平铺绘制图片
                DrawTiledPattern(surface, sourceImage, canvasWidth, canvasHeight, 0, 0, 1.0f);
            }

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建平铺画布失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? DrawTiledImage(SKBitmap canvas, SKBitmap sourceImage, float offsetX, float offsetY, float scale)
    {
        try
        {
            using var surface = new SKCanvas(canvas);

            // 计算缩放后的图片尺寸
            int scaledWidth = (int)(sourceImage.Width * scale);
            int scaledHeight = (int)(sourceImage.Height * scale);

            if (scaledWidth <= 0 || scaledHeight <= 0) return canvas;

            // 缩放图片
            using var scaledImage = sourceImage.Resize(
                new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                HighQuality);

            if (scaledImage == null) return canvas;

            // 平铺绘制，考虑偏移
            DrawTiledPattern(surface, scaledImage, canvas.Width, canvas.Height, offsetX, offsetY, 1.0f);

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 绘制平铺图片失败: {ex.Message}");
            return null;
        }
    }

    private void DrawTiledPattern(SKCanvas surface, SKBitmap pattern, int canvasWidth, int canvasHeight, float offsetX, float offsetY, float scale)
    {
        int patternWidth = (int)(pattern.Width * scale);
        int patternHeight = (int)(pattern.Height * scale);

        if (patternWidth <= 0 || patternHeight <= 0) return;

        // 计算起始位置（考虑偏移）
        float startX = offsetX % patternWidth;
        if (startX > 0) startX -= patternWidth;

        float startY = offsetY % patternHeight;
        if (startY > 0) startY -= patternHeight;

        // 平铺绘制
        for (float y = startY; y < canvasHeight; y += patternHeight)
        {
            for (float x = startX; x < canvasWidth; x += patternWidth)
            {
                surface.DrawBitmap(pattern, x, y);
            }
        }
    }

    public SKBitmap? ExtendEdgesToCanvas(SKBitmap sourceImage, int canvasWidth, int canvasHeight, float offsetX = 0, float offsetY = 0, float scale = 1.0f)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var surface = new SKCanvas(result);

            // 清空画布
            surface.Clear(SKColors.Transparent);

            // 计算缩放后的图片尺寸和位置
            int scaledWidth = (int)(sourceImage.Width * scale);
            int scaledHeight = (int)(sourceImage.Height * scale);

            if (scaledWidth <= 0 || scaledHeight <= 0) return result;

            // 缩放图片
            using var scaledImage = sourceImage.Resize(
                new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                HighQuality);

            if (scaledImage == null) return result;

            // 计算图片在画布中的位置（居中+偏移）
            float drawX = (canvasWidth - scaledWidth) / 2 + offsetX;
            float drawY = (canvasHeight - scaledHeight) / 2 + offsetY;

            // 先绘制原始图片
            surface.DrawBitmap(scaledImage, drawX, drawY);

            // 获取像素数据指针
            if (result.GetPixels() == IntPtr.Zero) return result;

            int rowBytes = result.RowBytes;
            unsafe
            {
                byte* pixels = (byte*)result.GetPixels();

                // 计算图片在画布中的整数边界（只处理图片实际覆盖的区域）
                int imgLeft = (int)Math.Round(drawX);
                int imgTop = (int)Math.Round(drawY);
                int imgRight = imgLeft + scaledWidth - 1;
                int imgBottom = imgTop + scaledHeight - 1;

                // 确保边界在画布范围内
                imgLeft = Math.Clamp(imgLeft, 0, canvasWidth - 1);
                imgTop = Math.Clamp(imgTop, 0, canvasHeight - 1);
                imgRight = Math.Clamp(imgRight, 0, canvasWidth - 1);
                imgBottom = Math.Clamp(imgBottom, 0, canvasHeight - 1);

                // 1. 先处理上下边缘的延伸（水平方向：只延伸到图片左右边界）
                // 上边缘延伸：将图片最上面1行像素向上延伸到画布顶部
                if (imgTop > 0)
                {
                    for (int y = 0; y < imgTop; y++)
                    {
                        for (int x = imgLeft; x <= imgRight; x++)
                        {
                            CopyPixel(pixels, x, y, x, imgTop, rowBytes);
                        }
                    }
                }

                // 下边缘延伸：将图片最下面1行像素向下延伸到画布底部
                if (imgBottom < canvasHeight - 1)
                {
                    for (int y = imgBottom + 1; y < canvasHeight; y++)
                    {
                        for (int x = imgLeft; x <= imgRight; x++)
                        {
                            CopyPixel(pixels, x, y, x, imgBottom, rowBytes);
                        }
                    }
                }

                // 2. 再处理左右边缘的延伸（垂直方向：覆盖整个画布高度）
                // 左边缘延伸：将图片最左边1列像素向左延伸到画布左边缘
                if (imgLeft > 0)
                {
                    for (int x = 0; x < imgLeft; x++)
                    {
                        for (int y = 0; y < canvasHeight; y++)
                        {
                            int srcX = imgLeft;
                            int srcY = Math.Clamp(y, 0, canvasHeight - 1);
                            CopyPixel(pixels, x, y, srcX, srcY, rowBytes);
                        }
                    }
                }

                // 右边缘延伸：将图片最右边1列像素向右延伸到画布右边缘
                if (imgRight < canvasWidth - 1)
                {
                    for (int x = imgRight + 1; x < canvasWidth; x++)
                    {
                        for (int y = 0; y < canvasHeight; y++)
                        {
                            int srcX = imgRight;
                            int srcY = Math.Clamp(y, 0, canvasHeight - 1);
                            CopyPixel(pixels, x, y, srcX, srcY, rowBytes);
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 拉伸边缘失败: {ex.Message}");
            return null;
        }
    }

    private static unsafe void CopyPixel(byte* pixels, int dstX, int dstY, int srcX, int srcY, int rowBytes)
    {
        byte* dst = pixels + dstY * rowBytes + dstX * 4;
        byte* src = pixels + srcY * rowBytes + srcX * 4;
        dst[0] = src[0];
        dst[1] = src[1];
        dst[2] = src[2];
        dst[3] = src[3];
    }

    public SKBitmap? CropAndResizeCircle(SKBitmap image, int x, int y, int size, int targetSize)
    {
        try
        {
            // 裁剪圆形区域
            var circleCropped = CropCircularRegion(image, x, y, x + size, y + size);
            if (circleCropped == null) return null;

            // 缩放到目标大小
            using (circleCropped)
            {
                return ResizeImage(circleCropped, targetSize, targetSize);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 裁剪并缩放圆形失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CreateMaskedFlag(SKBitmap circleFlag, SKBitmap overlay)
    {
        try
        {
            const int canvasWidth = 52;
            const int canvasHeight = 54;

            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var surface = new SKCanvas(result);

            surface.Clear(SKColors.Transparent);

            // 将圆形国旗贴到 (3, 2)
            surface.DrawBitmap(circleFlag, 3, 2);

            // 将 overlay 贴到 (0, 0)
            surface.DrawBitmap(overlay, 0, 0);

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建遮罩国旗失败: {ex.Message}");
            return null;
        }
    }

    public FlagMakerResult MakeFlagWorkflow(SKBitmap workingCanvas, int countryId, string outputDir)
    {
        var result = new FlagMakerResult();

        try
        {
            Debug.WriteLine($"[FlagEditorService] MakeFlagWorkflow开始: countryId={countryId}, outputDir={outputDir}");
            Debug.WriteLine($"[FlagEditorService] 画布: {workingCanvas.Width}x{workingCanvas.Height}");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            const int canvasSize = 150;
            const int circleTargetSize = 47;

            // 步骤1: 画布内容裁剪为150x150圆形，压缩为47x47圆形
            Debug.WriteLine($"[FlagEditorService] 步骤1: 裁剪画布为150x150圆形并压缩为47x47");
            var circle47 = CropAndResizeCircle(workingCanvas, 0, 0, canvasSize, circleTargetSize);
            if (circle47 == null)
            {
                result.ErrorMessage = "裁剪圆形失败";
                Debug.WriteLine($"[FlagEditorService] 错误: {result.ErrorMessage}");
                return result;
            }
            Debug.WriteLine($"[FlagEditorService] circle47: {circle47.Width}x{circle47.Height}");

            using (circle47)
            {
                // 步骤2: 创建52x54透明画布，贴circle47到(3,2)，贴overlay到(0,0)
                Debug.WriteLine($"[FlagEditorService] 步骤2: 创建52x54透明画布，贴circle47到(3,2)，贴overlay到(0,0)");
                var overlay = GetFlagOverlayImage();
                if (overlay == null)
                {
                    result.ErrorMessage = "flagoverlay.png 未加载";
                    Debug.WriteLine($"[FlagEditorService] 错误: {result.ErrorMessage}");
                    return result;
                }
                Debug.WriteLine($"[FlagEditorService] overlay: {overlay.Width}x{overlay.Height}");

                var maskedFlag = CreateMaskedFlag(circle47, overlay);
                if (maskedFlag == null)
                {
                    result.ErrorMessage = "创建遮罩国旗失败";
                    Debug.WriteLine($"[FlagEditorService] 错误: {result.ErrorMessage}");
                    return result;
                }
                Debug.WriteLine($"[FlagEditorService] maskedFlag: {maskedFlag.Width}x{maskedFlag.Height}");

                using (maskedFlag)
                {
                    // 步骤3: 将52x54压缩为37x38小国旗
                    Debug.WriteLine($"[FlagEditorService] 步骤3: 将52x54压缩为37x38小国旗");
                    using var smallFlag = ResizeImage(maskedFlag, 37, 38);
                    Debug.WriteLine($"[FlagEditorService] smallFlag: {smallFlag?.Width}x{smallFlag?.Height}");

                    // 步骤4: 通过tacticalmap模块添加大国旗flag_{id}.png并整理布局
                    Debug.WriteLine($"[FlagEditorService] 步骤4: 通过tacticalmap模块添加大国旗flag_{countryId}.png");
                    var (tacticalImagePath, tacticalXmlPath) = FindTacticalMapFiles();

                    if (!string.IsNullOrEmpty(tacticalImagePath) && !string.IsNullOrEmpty(tacticalXmlPath))
                    {
                        Debug.WriteLine($"[FlagEditorService] 使用路径: img={tacticalImagePath}, xml={tacticalXmlPath}");
                        var tmEditor = new TacticalMapEditor();
                        var loadOk = tmEditor.LoadFromFiles(tacticalImagePath, tacticalXmlPath);
                        Debug.WriteLine($"[FlagEditorService] TacticalMapEditor加载: {loadOk}");

                        if (loadOk)
                        {
                            // 步骤4+5: 批量添加大国旗和小国旗到tacticalmap并整理布局
                            Debug.WriteLine($"[FlagEditorService] 步骤4+5: 批量添加大国旗和小国旗并整理布局");
                            var imagesToAdd = new List<(string name, SKBitmap image)>();

                            var bigFlagCopy = maskedFlag.Copy();
                            imagesToAdd.Add(($"flag_{countryId}.png", bigFlagCopy));

                            SKBitmap? smallFlagCopy = null;
                            if (smallFlag != null)
                            {
                                smallFlagCopy = smallFlag.Copy();
                                imagesToAdd.Add(($"f_{countryId:D2}.png", smallFlagCopy));
                            }

                            var batchOk = tmEditor.AddImagesAndArrange(imagesToAdd, tacticalImagePath, tacticalXmlPath);
                            Debug.WriteLine($"[FlagEditorService] 批量添加并整理布局结果: {batchOk}");

                            bigFlagCopy.Dispose();
                            smallFlagCopy?.Dispose();

                            result.FlagPath = $"tacticalmap:flag_{countryId}.png";
                            result.SmallFlagPath = $"tacticalmap:f_{countryId:D2}.png";
                            Debug.WriteLine($"[FlagEditorService] TacticalMap整合完成");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[FlagEditorService] TacticalMap文件不存在，回退到独立文件保存");
                        var flagPath = Path.Combine(outputDir, $"flag_{countryId}.png");
                        SaveImage(maskedFlag, flagPath, SKEncodedImageFormat.Png, 100);
                        result.FlagPath = flagPath;
                        Debug.WriteLine($"[FlagEditorService] 保存大国旗: {flagPath}");

                        if (smallFlag != null)
                        {
                            var smallFlagPath = Path.Combine(outputDir, $"f_{countryId:D2}.png");
                            SaveImage(smallFlag, smallFlagPath, SKEncodedImageFormat.Png, 100);
                            result.SmallFlagPath = smallFlagPath;
                            Debug.WriteLine($"[FlagEditorService] 保存小国旗: {smallFlagPath}");
                        }
                    }

                    // 步骤6: 将处理后的52x54国旗(maskedFlag)加入HD UI图集
                    Debug.WriteLine($"[FlagEditorService] 步骤6: 将处理后的国旗加入HD UI图集");
                    var (hdImagePath, hdXmlPath) = FindHdAtlasFiles();

                    Debug.WriteLine($"[FlagEditorService] HD文件检查: img={hdImagePath}, xml={hdXmlPath}");
                    Debug.WriteLine($"[FlagEditorService] HD文件存在: img={File.Exists(hdImagePath ?? "")}, xml={File.Exists(hdXmlPath ?? "")}");

                    if (!string.IsNullOrEmpty(hdImagePath) && !string.IsNullOrEmpty(hdXmlPath)
                        && File.Exists(hdImagePath) && File.Exists(hdXmlPath))
                    {
                        var hdEditor = new HdAtlasEditor();
                        var hdLoadOk = hdEditor.LoadFromFiles(hdImagePath, hdXmlPath);
                        Debug.WriteLine($"[FlagEditorService] HdAtlasEditor加载: {hdLoadOk}");

                        if (hdLoadOk)
                        {
                            var hdFlagCopy = maskedFlag.Copy();
                            var hdOk = hdEditor.AddImageAndSave(countryId, hdFlagCopy, hdImagePath, hdXmlPath);
                            hdFlagCopy.Dispose();
                            Debug.WriteLine($"[FlagEditorService] HD国旗添加结果: {hdOk}");
                            result.HdFlagPath = $"hdatlas:flag_{countryId}.png";
                        }
                    }
                    else
                    {
                        var hdFlagPath = Path.Combine(outputDir, $"flag_hd_{countryId}.png");
                        SaveImage(maskedFlag, hdFlagPath, SKEncodedImageFormat.Png, 100);
                        result.HdFlagPath = hdFlagPath;
                        Debug.WriteLine($"[FlagEditorService] HD图集文件不存在，保存到: {hdFlagPath}");
                    }
                }
            }

            result.Success = true;
            Debug.WriteLine($"[FlagEditorService] MakeFlagWorkflow完成: Success={result.Success}");
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            Debug.WriteLine($"[FlagEditorService] MakeFlagWorkflow异常: {ex}");
            return result;
        }
    }

    private (string? imagePath, string? xmlPath) FindTacticalMapFiles()
    {
        string? imagePath = null;
        string? xmlPath = null;

        var am = AssetManager.Default;
        if (am.IsLoaded)
        {
            var imgEntry = am.Find("tacticalmap.webp") ?? am.Find("tacticalmap.png");
            var xmlEntry = am.Find("tacticalmap.xml");

            if (imgEntry != null) imagePath = imgEntry.FullPath;
            if (xmlEntry != null) xmlPath = xmlEntry.FullPath;
        }

        if (!string.IsNullOrEmpty(imagePath) && !string.IsNullOrEmpty(xmlPath))
            return (imagePath, xmlPath);

        var parser = CountrySettingParser.Instance;
        var tacticalMapDir = parser.TacticalMapDir;

        if (string.IsNullOrEmpty(tacticalMapDir))
            return (null, null);

        var dirs = new[] { tacticalMapDir, Path.GetDirectoryName(tacticalMapDir) ?? "" };
        var imgExts = new[] { ".png", ".webp" };

        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var ext in imgExts)
            {
                var imgPath = Path.Combine(dir, $"tacticalmap{ext}");
                var xmlPath2 = Path.Combine(dir, "tacticalmap.xml");
                if (File.Exists(imgPath) && File.Exists(xmlPath2))
                    return (imgPath, xmlPath2);
            }
        }

        return (null, null);
    }

    private (string? imagePath, string? xmlPath) FindHdAtlasFiles()
    {
        var parser = CountrySettingParser.Instance;
        var hdImagePath = parser.FlagsHdImagePath;
        var hdXmlPath = parser.FlagsHdXmlPath;

        if (!string.IsNullOrEmpty(hdImagePath) && !string.IsNullOrEmpty(hdXmlPath))
        {
            if (File.Exists(hdImagePath) && File.Exists(hdXmlPath))
                return (hdImagePath, hdXmlPath);

            var dir = Path.GetDirectoryName(hdImagePath) ?? "";
            var pngPath = Path.Combine(dir, "image_flags_hd.png");
            if (File.Exists(pngPath) && File.Exists(hdXmlPath))
                return (pngPath, hdXmlPath);
        }

        return (null, null);
    }

    // ========== 涂鸦引擎 ==========

    public void PaintBrush(SKBitmap canvas, int x, int y, int radius, SKColor color)
    {
        try
        {
            using var surface = new SKCanvas(canvas);
            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            surface.DrawCircle(x, y, radius, paint);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 绘制笔触失败: {ex.Message}");
        }
    }

    public SKColor GetPixelColor(SKBitmap canvas, int x, int y)
    {
        try
        {
            if (x < 0 || x >= canvas.Width || y < 0 || y >= canvas.Height)
                return SKColors.Transparent;

            return canvas.GetPixel(x, y);
        }
        catch
        {
            return SKColors.Transparent;
        }
    }
}