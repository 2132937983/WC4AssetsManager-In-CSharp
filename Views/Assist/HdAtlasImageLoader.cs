using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// HD 图集图块加载器（供各编辑场景共用，如建筑的 <c>building_{Id}.png</c>）。
/// 做法与 <see cref="FlagImageLoader"/> 一致：整张图集只解码一次，
/// 按图块定义裁剪后转成 WPF 可显示的 <see cref="BitmapSource"/>，并按图块名缓存。
/// </summary>
internal static class HdAtlasImageLoader
{
    private sealed class AtlasCache
    {
        public SKBitmap? Surface;
        public readonly Dictionary<string, BitmapSource?> Tiles = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, AtlasCache> Caches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按图集名与图块名获取图块；找不到返回 null。</summary>
    public static BitmapSource? Load(string atlasBase, string imageName)
    {
        var cache = GetAtlas(atlasBase);
        if (cache.Tiles.TryGetValue(imageName, out var cached)) return cached;

        BitmapSource? result = null;
        try
        {
            var parser = HdAtlasParser.Get(atlasBase);
            var def = parser.GetImageDef(imageName);
            if (def != null && cache.Surface != null)
            {
                int x = Math.Max(0, def.X);
                int y = Math.Max(0, def.Y);
                int w = Math.Min(def.Width, cache.Surface.Width - x);
                int h = Math.Min(def.Height, cache.Surface.Height - y);
                if (w > 0 && h > 0)
                {
                    // 预乘 Bgra8888 与 WPF 的 Pbgra32 布局一致
                    var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var subset = new SKBitmap(info);
                    using (var canvas = new SKCanvas(subset))
                    {
                        canvas.Clear(SKColors.Transparent);
                        canvas.DrawBitmap(cache.Surface, new SKRectI(x, y, x + w, y + h), new SKRectI(0, 0, w, h));
                    }

                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null,
                        subset.GetPixels(), h * subset.RowBytes, subset.RowBytes);
                    bmp.Freeze();
                    result = bmp;
                }
            }
        }
        catch
        {
            result = null;
        }

        cache.Tiles[imageName] = result;
        return result;
    }

    /// <summary>地图/资源上下文切换后可调用，释放并清空全部图集缓存。</summary>
    public static void ClearCache()
    {
        foreach (var cache in Caches.Values) cache.Surface?.Dispose();
        Caches.Clear();
    }

    private static AtlasCache GetAtlas(string atlasBase)
    {
        if (Caches.TryGetValue(atlasBase, out var existing)) return existing;

        var cache = new AtlasCache();
        try
        {
            var parser = HdAtlasParser.Get(atlasBase);
            var data = parser.SurfaceData;
            if (data != null && data.Length > 0)
            {
                using var stream = new MemoryStream(data);
                cache.Surface = SKBitmap.Decode(stream);
            }
        }
        catch
        {
            cache.Surface = null;
        }

        Caches[atlasBase] = cache;
        return cache;
    }
}
