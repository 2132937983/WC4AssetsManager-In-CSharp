using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public interface IImageEditorService
{
    SKBitmap? LoadSourceImage(string path);
    SKBitmap? CreateCanvasWithImage(SKBitmap sourceImage, int canvasWidth, int canvasHeight, SKColor bgColor, int srcX, int srcY);
    SKBitmap? CropCircularRegion(SKBitmap image, int x0, int y0, int x1, int y1);
    SKBitmap? ResizeImage(SKBitmap image, int width, int height);
    bool SaveImage(SKBitmap image, string path, SKEncodedImageFormat format, int quality);
    ImageEditorSaveResult SaveGeneralImages(SKBitmap canvasImage, SKBitmap? circleCropped, string baseName, string outputDir, int resize1W, int resize1H, int resize2W, int resize2H);
}

public class ImageEditorSaveResult
{
    public bool Success { get; set; }
    public string? GeneralPath { get; set; }
    public string? CirclePath { get; set; }
    public string? HeadPath { get; set; }
    public string? ErrorMessage { get; set; }
}