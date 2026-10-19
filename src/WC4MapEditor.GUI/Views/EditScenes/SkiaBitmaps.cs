using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>SkiaSharp 位图 → WPF 图像（WPF 不能直接解码 webp，预览图都走 Skia 解码再转换）。</summary>
public static class SkiaBitmaps
{
    /// <summary>转成冻结的 Pbgra32 图像；像素格式 / 透明度表示不同时由 Skia 换算。</summary>
    public static BitmapSource? ToBitmapSource(SKBitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var target = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
        target.Lock();
        try
        {
            using var pixels = bitmap.PeekPixels();
            if (pixels == null || !pixels.ReadPixels(info, target.BackBuffer, target.BackBufferStride)) return null;
            target.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
        }
        finally
        {
            target.Unlock();
        }
        target.Freeze();
        return target;
    }

    /// <summary>读取并解码一张图片文件（png / webp …）；文件不存在、读不出或解码失败时返回 null。</summary>
    public static BitmapSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            return bitmap == null ? null : ToBitmapSource(bitmap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[SkiaBitmaps] 读取图片失败 {path}: {ex.Message}");
            return null;
        }
    }
}
