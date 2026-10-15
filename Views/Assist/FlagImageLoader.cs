using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 国家国旗加载器（供 Assist 下的各窗口共用）。
/// <para>
/// 加载顺序与渲染层一致：
/// 1. 战术地图图集中的 <c>flag_{countryId}.png</c>（资源的主要存放位置）；
/// 2. 回退到 StageMark/CountryFlag/flag_{countryId}.png 文件
///    （countryId 为 0/255 时回退 flag_1.png）。
/// </para>
/// <para>
/// 战术地图大图只解码一次并复用，结果按国家 ID 缓存。
/// </para>
/// </summary>
internal static class FlagImageLoader
{
    private static readonly Dictionary<int, BitmapSource?> Cache = new();
    private static SKBitmap? _tacticalSurface;
    private static bool _surfaceLoadAttempted;

    /// <summary>按国家 ID 加载国旗；找不到返回 null，由调用方决定回退显示。</summary>
    public static BitmapSource? Load(int countryId)
    {
        if (Cache.TryGetValue(countryId, out var cached)) return cached;

        BitmapSource? result = LoadFromTacticalMap(countryId) ?? LoadFromFile(countryId);
        Cache[countryId] = result;
        return result;
    }

    /// <summary>地图上下文切换后可调用，清空缓存避免持有过期资源</summary>
    public static void ClearCache()
    {
        Cache.Clear();
        _tacticalSurface?.Dispose();
        _tacticalSurface = null;
        _surfaceLoadAttempted = false;
    }

    /// <summary>
    /// 只清除"没找到"的缓存项。
    /// 这样之后新增了国家/国旗资源时，重开窗口就能显示出来，
    /// 而已有国旗的项保持缓存，不会重复解码战术地图大图。
    /// </summary>
    public static void ClearMisses()
    {
        if (Cache.Count == 0) return;
        foreach (var key in Cache.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
            Cache.Remove(key);
    }

    /// <summary>优先从战术地图图集裁剪国旗</summary>
    private static BitmapSource? LoadFromTacticalMap(int countryId)
    {
        try
        {
            var parser = ConfigManager.Instance.TacticalMapParser;
            if (parser == null) return null;

            var imageDef = parser.GetImageDef($"flag_{countryId}.png");
            if (imageDef == null) return null;

            var surface = GetTacticalSurface(parser);
            if (surface == null) return null;

            int x = imageDef.X;
            int y = imageDef.Y;
            int w = Math.Min(imageDef.Width, surface.Width - x);
            int h = Math.Min(imageDef.Height, surface.Height - y);
            if (x < 0 || y < 0 || w <= 0 || h <= 0) return null;

            // 预乘 Bgra8888 与 WPF 的 Pbgra32 布局一致
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var subset = new SKBitmap(info);
            using (var canvas = new SKCanvas(subset))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(surface, -x, -y);
            }

            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null,
                subset.GetPixels(), h * subset.RowBytes, subset.RowBytes);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>惰性解码战术地图大图，整张图只解码一次供所有国旗复用</summary>
    private static SKBitmap? GetTacticalSurface(Core.Parsers.TacticalMapParser parser)
    {
        if (_surfaceLoadAttempted) return _tacticalSurface;
        _surfaceLoadAttempted = true;

        var data = parser.SurfaceData;
        if (data == null) return null;

        try
        {
            using var stream = new System.IO.MemoryStream(data);
            _tacticalSurface = SKBitmap.Decode(stream);
        }
        catch
        {
            _tacticalSurface = null;
        }

        return _tacticalSurface;
    }

    /// <summary>回退：从 StageMark/CountryFlag 目录加载</summary>
    private static BitmapSource? LoadFromFile(int countryId)
    {
        try
        {
            string basePath = System.IO.Path.Combine(
                ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");

            string path = System.IO.Path.Combine(basePath, $"flag_{countryId}.png");
            if (!System.IO.File.Exists(path) && (countryId == 0 || countryId == 255))
                path = System.IO.Path.Combine(basePath, "flag_1.png");
            if (!System.IO.File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
