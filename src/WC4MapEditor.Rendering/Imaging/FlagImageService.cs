using System.Diagnostics;
using System.Linq;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Rendering.Imaging;

public class FlagImageService : IFlagImageService
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly TacticalMapParser _tacticalMapParser = new();
    private SKBitmap? _hdAtlasImage;
    private readonly Dictionary<int, FlagAtlasEntry> _hdFlagEntries = new();
    private bool _hdAtlasLoaded;

    public FlagImageService()
    {
        LoadHdAtlas();
    }

    private void LoadHdAtlas()
    {
        try
        {
            var am = AssetManager.Default;

            // 如果AM未加载，尝试扫描默认路径（与其他Parser保持一致）
            if (!am.IsLoaded)
            {
                Debug.WriteLine("[FlagImageService] AssetManager未加载，尝试扫描默认路径");
                try
                {
                    var defaultPath = AssetManager.GetDefaultAssetsPath();
                    if (Directory.Exists(defaultPath))
                    {
                        am.Scan(defaultPath);
                        Debug.WriteLine($"[FlagImageService] AssetManager扫描完成: {defaultPath}, 文件数={am.Count}");
                    }
                    else
                    {
                        Debug.WriteLine($"[FlagImageService] 默认资源路径不存在: {defaultPath}");
                    }
                }
                catch (Exception scanEx)
                {
                    Debug.WriteLine($"[FlagImageService] AssetManager扫描失败: {scanEx.Message}");
                }
            }

            // 尝试从AM加载
            if (am.IsLoaded)
            {
                Debug.WriteLine("[FlagImageService] 尝试从AM查找HD图集");
                var xmlEntry = am.Find("image/image_flags_hd.xml") ?? am.Find("image_flags_hd.xml");
                var imgEntry = am.Find("image/image_flags_hd.webp") ?? am.Find("image_flags_hd.webp")
                    ?? am.Find("image/image_flags_hd.png") ?? am.Find("image_flags_hd.png");

                Debug.WriteLine($"[FlagImageService] AM查找结果: xml={xmlEntry?.RelativePath ?? "null"}, img={imgEntry?.RelativePath ?? "null"}");

                if (xmlEntry != null && imgEntry != null)
                {
                    ParseHdXml(am.ReadText(xmlEntry));
                    _hdAtlasImage = LoadImageFromBytes(am.ReadBytes(imgEntry));
                    Debug.WriteLine("[FlagImageService] 已从AssetManager加载HD图集");
                }
                else
                {
                    Debug.WriteLine("[FlagImageService] AM中未找到HD图集文件，尝试从文件系统加载");
                    LoadHdAtlasFromFileSystem();
                }
            }
            else
            {
                Debug.WriteLine("[FlagImageService] AssetManager仍未加载，尝试从文件系统加载");
                LoadHdAtlasFromFileSystem();
            }

            _hdAtlasLoaded = _hdAtlasImage != null && _hdFlagEntries.Count > 0;
            Debug.WriteLine($"[FlagImageService] HD图集加载完成: 图片={_hdAtlasImage != null}, 条目数={_hdFlagEntries.Count}, 成功={_hdAtlasLoaded}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 加载HD图集失败: {ex.Message}");
        }
    }

    private void LoadHdAtlasFromFileSystem()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            string? candidate = null;

            // 尝试多种路径查找策略
            var searchPaths = new List<string?>();

            // 策略1: 从BaseDirectory向上查找
            string? current = baseDir;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(current); i++)
            {
                searchPaths.Add(Path.Combine(current, "Resource", "WC4DATA", "assets"));
                current = Path.GetDirectoryName(current);
            }

            // 策略2: 尝试项目根目录的绝对路径
            var projectDir = Path.GetFullPath(Path.Combine(baseDir ?? "", "..", "..", "..", ".."));
            searchPaths.Add(Path.Combine(projectDir, "Resource", "WC4DATA", "assets"));

            // 策略3: 尝试当前工作目录
            searchPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "Resource", "WC4DATA", "assets"));

            // 策略4: 尝试特定路径
            searchPaths.Add(@"E:\CSharpProject\WC4MapEditor\Resource\WC4DATA\assets");

            foreach (var testPath in searchPaths)
            {
                if (!string.IsNullOrEmpty(testPath) && Directory.Exists(testPath))
                {
                    candidate = testPath;
                    Debug.WriteLine($"[FlagImageService] 找到资源目录: {candidate}");
                    break;
                }
            }

            if (candidate == null)
            {
                Debug.WriteLine("[FlagImageService] 未找到资源目录，尝试使用默认路径");
                candidate = Path.Combine(baseDir ?? "", "Resource", "WC4DATA", "assets");
            }

            var xmlPath = Path.Combine(candidate, "image", "image_flags_hd.xml");
            var imgPath = Path.Combine(candidate, "image", "image_flags_hd.webp");
            if (!File.Exists(imgPath))
                imgPath = Path.Combine(candidate, "image", "image_flags_hd.png");

            Debug.WriteLine($"[FlagImageService] 查找HD图集: xml={xmlPath}, exists={File.Exists(xmlPath)}");
            Debug.WriteLine($"[FlagImageService] 查找HD图集: img={imgPath}, exists={File.Exists(imgPath)}");

            if (!File.Exists(xmlPath) || !File.Exists(imgPath))
            {
                Debug.WriteLine("[FlagImageService] HD图集文件不存在，跳过加载");
                return;
            }

            ParseHdXml(File.ReadAllText(xmlPath));
            _hdAtlasImage = LoadImageFromFile(imgPath);
            Debug.WriteLine($"[FlagImageService] 从文件加载HD图集图片: {_hdAtlasImage != null}, 大小={_hdAtlasImage?.Width}x{_hdAtlasImage?.Height}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 从文件系统加载HD图集失败: {ex.Message}");
        }
    }

    private void ParseHdXml(string xmlContent)
    {
        try
        {
            string tempXml = $"<root>{xmlContent}</root>";
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(tempXml);

            var imageNodes = xmlDoc.SelectNodes("//Images/Image");
            if (imageNodes == null) return;

            foreach (XmlNode node in imageNodes)
            {
                if (node.Attributes == null) continue;
                var nameAttr = node.Attributes["name"];
                if (nameAttr == null) continue;

                var name = nameAttr.Value;
                if (!name.StartsWith("flag_") || !name.EndsWith(".png")) continue;

                var idStr = name[5..^4];
                if (!int.TryParse(idStr, out int id)) continue;

                var entry = new FlagAtlasEntry { Name = name };
                if (node.Attributes["x"] != null && int.TryParse(node.Attributes["x"]!.Value, out int ex)) entry.X = ex;
                if (node.Attributes["y"] != null && int.TryParse(node.Attributes["y"]!.Value, out int ey)) entry.Y = ey;
                if (node.Attributes["w"] != null && int.TryParse(node.Attributes["w"]!.Value, out int ew)) entry.Width = ew;
                if (node.Attributes["h"] != null && int.TryParse(node.Attributes["h"]!.Value, out int eh)) entry.Height = eh;

                if (entry.Width > 0 && entry.Height > 0)
                    _hdFlagEntries[id] = entry;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 解析HD XML失败: {ex.Message}");
        }
    }

    private static SKBitmap? LoadImageFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 加载图片失败: {ex.Message}");
            return null;
        }
    }

    private static SKBitmap? LoadImageFromBytes(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 加载图片失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? LoadFlagFromTacticalMap(string flagName)
    {
        try
        {
            _tacticalMapParser.EnsureLoadedFromAssets();
            var def = _tacticalMapParser.GetImageDef(flagName);
            if (def == null) return null;

            var surfaceData = _tacticalMapParser.SurfaceData;
            if (surfaceData == null) return null;

            using var stream = new MemoryStream(surfaceData);
            using var atlas = SKBitmap.Decode(stream);
            if (atlas == null) return null;

            var info = new SKImageInfo(def.Width, def.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.DrawBitmap(atlas, new SKRectI(def.X, def.Y, def.X + def.Width, def.Y + def.Height),
                new SKRect(0, 0, def.Width, def.Height));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 从TacticalMap加载国旗失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? LoadFlagFromHdAtlas(int countryId)
    {
        Debug.WriteLine($"[FlagImageService] 尝试加载HD国旗: countryId={countryId}, _hdAtlasLoaded={_hdAtlasLoaded}, _hdAtlasImage={_hdAtlasImage != null}");
        if (!_hdAtlasLoaded || _hdAtlasImage == null)
        {
            Debug.WriteLine("[FlagImageService] HD图集未加载，无法加载国旗");
            return null;
        }
        if (!_hdFlagEntries.TryGetValue(countryId, out var entry))
        {
            Debug.WriteLine($"[FlagImageService] 未找到countryId={countryId}的HD国旗条目，可用ID: {string.Join(",", _hdFlagEntries.Keys.Take(20))}...");
            return null;
        }

        try
        {
            Debug.WriteLine($"[FlagImageService] 找到HD国旗条目: {entry.Name}, x={entry.X}, y={entry.Y}, w={entry.Width}, h={entry.Height}");
            var info = new SKImageInfo(entry.Width, entry.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.DrawBitmap(_hdAtlasImage, new SKRectI(entry.X, entry.Y, entry.X + entry.Width, entry.Y + entry.Height),
                new SKRect(0, 0, entry.Width, entry.Height));
            Debug.WriteLine($"[FlagImageService] HD国旗裁剪成功: {result.Width}x{result.Height}");
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 从HD图集加载国旗失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? LoadFlagFromFile(string path)
    {
        return LoadImageFromFile(path);
    }

    public SKBitmap? LoadBigFlag(int countryId)
    {
        return LoadFlagFromTacticalMap($"flag_{countryId}.png");
    }

    public SKBitmap? LoadSmallFlag(int countryId)
    {
        return LoadFlagFromTacticalMap($"f_{countryId:D2}.png");
    }

    public List<string> GetAvailableTacticalMapFlags()
    {
        var result = new List<string>();
        try
        {
            _tacticalMapParser.EnsureLoadedFromAssets();
            foreach (var def in _tacticalMapParser.ImageDefinitions)
            {
                if (def.Name.StartsWith("flag_") || def.Name.StartsWith("f_"))
                    result.Add(def.Name);
            }
        }
        catch { }
        return result;
    }

    public List<int> GetAvailableHdFlags()
    {
        return new List<int>(_hdFlagEntries.Keys);
    }

    public bool SaveFlag(SKBitmap flag, string path, SKEncodedImageFormat format, int quality)
    {
        try
        {
            using var data = flag.Encode(format, quality);
            if (data == null) return false;
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 保存国旗失败: {ex.Message}");
            return false;
        }
    }

    public SKBitmap? ResizeFlag(SKBitmap flag, int width, int height)
    {
        try
        {
            var info = new SKImageInfo(width, height, flag.ColorType, flag.AlphaType);
            return flag.Resize(info, HighQuality);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 缩放国旗失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CropCircularFlag(SKBitmap flag, int x, int y, int size)
    {
        try
        {
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var path = new SKPath();
            path.AddCircle(size / 2f, size / 2f, size / 2f);
            canvas.ClipPath(path);

            canvas.DrawBitmap(flag, new SKRectI(x, y, x + size, y + size), new SKRect(0, 0, size, size));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 裁剪圆形国旗失败: {ex.Message}");
            return null;
        }
    }

    private class FlagAtlasEntry
    {
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public void RefreshCache()
    {
        Debug.WriteLine("[FlagImageService] 刷新缓存");

        _hdAtlasImage?.Dispose();
        _hdAtlasImage = null;
        _hdFlagEntries.Clear();
        _hdAtlasLoaded = false;

        _tacticalMapParser.Dispose();
        TacticalMapParser.ClearCache();

        LoadHdAtlas();

        Debug.WriteLine("[FlagImageService] 缓存已刷新");
    }
}