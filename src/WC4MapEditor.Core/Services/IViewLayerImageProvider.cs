namespace WC4MapEditor.Core.Services;

/// <summary>
/// 整幅图的灰度数据：每像素 1 字节，0 = 黑，255 = 白。
/// 用于"从背景图识别省区"这类灰度分析：先把彩色背景图转成灰度图，
/// 突出行政边界后再做区域分割。
/// </summary>
public sealed class GrayscaleImage
{
    /// <summary>灰度像素数据，按 RowStride 换行（已去除行填充）</summary>
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>每行的起始偏移（等于 Width，保留字段是为了明确换行方式）</summary>
    public int RowStride { get; }

    public GrayscaleImage(byte[] pixels, int width, int height, int rowStride)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        RowStride = rowStride;
    }

    /// <summary>读取 (x, y) 处的灰度值，越界返回 255（按白处理）。</summary>
    public byte At(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return 255;
        return Pixels[y * RowStride + x];
    }
}

public interface IViewLayerImageProvider
{
    bool HasImage { get; }
    int Width { get; }
    int Height { get; }
    byte[] ExtractRegionData(int x, int y, int width, int height);

    /// <summary>
    /// 把整幅背景图转换为灰度图（Rec.601 加权），返回 null 表示当前没有可用图片。
    /// </summary>
    GrayscaleImage? ExtractGrayscale();
}
