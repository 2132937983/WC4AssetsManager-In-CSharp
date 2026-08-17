using SkiaSharp;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Rendering.Skia;

public sealed class SkiaViewLayerImageProvider : IViewLayerImageProvider
{
    private SKBitmap? _bitmap;

    public void UpdateBitmap(SKBitmap? bitmap) => _bitmap = bitmap;

    public bool HasImage => _bitmap != null;

    public int Width => _bitmap?.Width ?? 0;

    public int Height => _bitmap?.Height ?? 0;

    public byte[] ExtractRegionData(int x, int y, int width, int height)
    {
        if (_bitmap == null || width <= 0 || height <= 0)
            return [];

        width = Math.Min(width, _bitmap.Width - x);
        height = Math.Min(height, _bitmap.Height - y);

        if (width <= 0 || height <= 0)
            return [];

        var data = new byte[width * height * 4];
        int index = 0;

        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
            {
                if (px >= 0 && px < _bitmap.Width && py >= 0 && py < _bitmap.Height)
                {
                    var pixel = _bitmap.GetPixel(px, py);
                    data[index] = pixel.Red;
                    data[index + 1] = pixel.Green;
                    data[index + 2] = pixel.Blue;
                    data[index + 3] = pixel.Alpha;
                }
                index += 4;
            }
        }

        return data;
    }
}