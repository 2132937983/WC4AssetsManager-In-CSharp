using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public interface IFlagEditorService
{
    SKBitmap? LoadSourceImage(string path);
    SKBitmap? CreateCanvasWithImage(SKBitmap sourceImage, int canvasWidth, int canvasHeight, SKColor bgColor, int srcX, int srcY);
    SKBitmap? CropCircularRegion(SKBitmap image, int x0, int y0, int x1, int y1);
    SKBitmap? ResizeImage(SKBitmap image, int width, int height);
    bool SaveImage(SKBitmap image, string path, SKEncodedImageFormat format, int quality);
    FlagEditorSaveResult SaveFlagImages(SKBitmap canvasImage, SKBitmap? circleCropped, string baseName, string outputDir, int resize1W, int resize1H, int resize2W, int resize2H);

    SKBitmap? CreateTiledCanvas(SKBitmap? sourceImage, int canvasWidth, int canvasHeight);
    SKBitmap? DrawTiledImage(SKBitmap canvas, SKBitmap sourceImage, float offsetX, float offsetY, float scale);
    SKBitmap? ExtendEdgesToCanvas(SKBitmap sourceImage, int canvasWidth, int canvasHeight, float offsetX = 0, float offsetY = 0, float scale = 1.0f);
    SKBitmap? CropAndResizeCircle(SKBitmap image, int x, int y, int size, int targetSize);
    SKBitmap? CreateMaskedFlag(SKBitmap circleFlag, SKBitmap overlay);

    /// <summary>
    /// 完整的国旗制作工作流，接受已处理好的150x150画布内容
    /// </summary>
    FlagMakerResult MakeFlagWorkflow(SKBitmap workingCanvas, int countryId, string outputDir);

    void PaintBrush(SKBitmap canvas, int x, int y, int radius, SKColor color);
    SKColor GetPixelColor(SKBitmap canvas, int x, int y);
}

public class FlagEditorSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FlagPath { get; set; }
    public string? CirclePath { get; set; }
    public string? SmallPath { get; set; }
}

public class FlagMakerResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FlagPath { get; set; }
    public string? SmallFlagPath { get; set; }
    public string? HdFlagPath { get; set; }
}