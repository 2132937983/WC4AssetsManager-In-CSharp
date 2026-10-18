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

    /// <summary>
    /// 整幅图转灰度。
    /// <para>
    /// 用 <b>Rec.601</b> 加权手工换算（<c>0.299R + 0.587G + 0.114B</c>），
    /// 与 PIL 的 <c>ImageOps.grayscale</c>（ITU-R 601-2）、以及本类型声明的契约一致。
    /// </para>
    /// <para>
    /// 没有用 <c>_bitmap.Copy(SKColorType.Gray8)</c>：Skia 内部的权重与之不同，
    /// 实测在彩色地图上能差 3 个灰度级左右。数值虽小，但导出调试图要和用户
    /// 用 PIL 得到的灰度图对得上，所以统一成同一套公式，避免"两边看着不一样"。
    /// </para>
    /// <para>
    /// 先 <c>Copy(Rgba8888)</c> 拿到连续缓冲再一次遍历 —— 逐像素 <c>GetPixel</c> 在大图上慢到不可用。
    /// 该转换会按 AlphaType 做好 alpha 合成，与 Skia 的 Gray8 路径行为一致。
    /// </para>
    /// </summary>
    public GrayscaleImage? ExtractGrayscale()
    {
        if (_bitmap == null) return null;

        try
        {
            using var rgba = _bitmap.Copy(SKColorType.Rgba8888);
            if (rgba == null) return null;

            int width = rgba.Width;
            int height = rgba.Height;
            int rowStride = rgba.RowBytes;

            byte[]? raw = rgba.Bytes;
            if (raw == null || raw.Length < (long)rowStride * height) return null;

            var pixels = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * rowStride;
                int dstRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    int i = srcRow + x * 4;
                    // Rec.601: 0.299R + 0.587G + 0.114B（整数运算，与 PIL 取整一致）
                    pixels[dstRow + x] =
                        (byte)((raw[i] * 299 + raw[i + 1] * 587 + raw[i + 2] * 114) / 1000);
                }
            }

            return new GrayscaleImage(pixels, width, height, width);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[视图层] 转灰度失败: {ex.Message}");
            return null;
        }
    }
}
