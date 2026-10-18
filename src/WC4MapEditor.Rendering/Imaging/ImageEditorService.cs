using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public class ImageEditorService : IImageEditorService
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public SKBitmap? LoadSourceImage(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            return SKBitmap.Decode(path);
        }
        catch
        {
            return null;
        }
    }

    public SKBitmap? CreateCanvasWithImage(SKBitmap sourceImage, int canvasWidth, int canvasHeight, SKColor bgColor, int srcX, int srcY)
    {
        if (sourceImage == null)
            return null;
        try
        {
            var canvas = new SKBitmap(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var skCanvas = new SKCanvas(canvas);
            skCanvas.Clear(bgColor);
            skCanvas.DrawBitmap(sourceImage, srcX, srcY);
            return canvas;
        }
        catch
        {
            return null;
        }
    }

    public SKBitmap? CropCircularRegion(SKBitmap image, int x0, int y0, int x1, int y1)
    {
        if (image == null)
            return null;
        try
        {
            int cropWidth = x1 - x0;
            int cropHeight = y1 - y0;
            int actualSize = Math.Max(cropWidth, cropHeight);
            if (actualSize <= 0)
                return null;

            int clampedX0 = Math.Max(0, x0);
            int clampedY0 = Math.Max(0, y0);
            int clampedX1 = Math.Min(image.Width, x0 + cropWidth);
            int clampedY1 = Math.Min(image.Height, y0 + cropHeight);

            var cropped = new SKBitmap(clampedX1 - clampedX0, clampedY1 - clampedY0, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var cropCanvas = new SKCanvas(cropped))
            {
                cropCanvas.Clear(SKColors.Transparent);
                var srcRect = new SKRectI(clampedX0, clampedY0, clampedX1, clampedY1);
                cropCanvas.DrawBitmap(image, srcRect, new SKRectI(0, 0, cropped.Width, cropped.Height));
            }

            if (cropped.Width != actualSize || cropped.Height != actualSize)
            {
                var resized = cropped.Resize(new SKImageInfo(actualSize, actualSize, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
                cropped.Dispose();
                cropped = resized;
                if (cropped == null)
                    return null;
            }

            var result = new SKBitmap(actualSize, actualSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var resultCanvas = new SKCanvas(result))
            {
                resultCanvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = true };
                using var clipPath = new SKPath();
                clipPath.AddCircle(actualSize / 2f, actualSize / 2f, actualSize / 2f);
                resultCanvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
                int pasteX = (actualSize - cropped.Width) / 2;
                int pasteY = (actualSize - cropped.Height) / 2;
                resultCanvas.DrawBitmap(cropped, pasteX, pasteY, paint);
            }
            cropped.Dispose();
            return result;
        }
        catch
        {
            return null;
        }
    }

    public SKBitmap? ResizeImage(SKBitmap image, int width, int height)
    {
        if (image == null || width <= 0 || height <= 0)
            return null;
        try
        {
            return image.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), HighQuality);
        }
        catch
        {
            return null;
        }
    }

    public bool SaveImage(SKBitmap image, string path, SKEncodedImageFormat format, int quality)
    {
        if (image == null || string.IsNullOrEmpty(path))
            return false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            using var data = image.Encode(format, quality);
            if (data == null)
                return false;
            File.WriteAllBytes(path, data.ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public ImageEditorSaveResult SaveGeneralImages(SKBitmap canvasImage, SKBitmap? circleCropped, string baseName, string outputDir, int resize1W, int resize1H, int resize2W, int resize2H)
    {
        var result = new ImageEditorSaveResult();
        try
        {
            System.IO.Directory.CreateDirectory(outputDir);

            string generalPath = System.IO.Path.Combine(outputDir, $"general_{baseName}.png");
            if (!SaveImage(canvasImage, generalPath, SKEncodedImageFormat.Png, 100))
            {
                result.ErrorMessage = $"保存 general_{baseName}.png 失败";
                return result;
            }
            result.GeneralPath = generalPath;

            if (circleCropped != null)
            {
                if (resize1W > 0 && resize1H > 0)
                {
                    using var resized1 = ResizeImage(circleCropped, resize1W, resize1H);
                    if (resized1 != null)
                    {
                        string circlePath = System.IO.Path.Combine(outputDir, $"general_circle_{baseName}.png");
                        if (SaveImage(resized1, circlePath, SKEncodedImageFormat.Png, 100))
                            result.CirclePath = circlePath;
                    }
                }

                if (resize2W > 0 && resize2H > 0)
                {
                    using var resized2 = ResizeImage(circleCropped, resize2W, resize2H);
                    if (resized2 != null)
                    {
                        string headPath = System.IO.Path.Combine(outputDir, $"head_{baseName}.png");
                        if (SaveImage(resized2, headPath, SKEncodedImageFormat.Png, 100))
                            result.HeadPath = headPath;
                    }
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
}