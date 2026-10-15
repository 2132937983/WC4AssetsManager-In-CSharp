using System.Diagnostics;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Rendering.Imaging;

/// <summary>
/// HD图集编辑器：管理HD国旗图集的读取、修改和保存
/// </summary>
public class HdAtlasEditor
{
    private string? _imageFilePath;
    private string? _xmlFilePath;
    private byte[]? _imageData;
    private readonly List<HdAtlasEntry> _entries = new();
    private readonly Dictionary<int, HdAtlasEntry> _entryById = new();

    public IReadOnlyList<HdAtlasEntry> Entries => _entries;
    public string? ImageFilePath => _imageFilePath;
    public string? XmlFilePath => _xmlFilePath;
    public byte[]? ImageData => _imageData;
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public bool IsLoaded { get; private set; }

    public event Action? DataChanged;

    /// <summary>
    /// 从文件加载HD图集
    /// </summary>
    public bool LoadFromFiles(string imageFilePath, string xmlFilePath)
    {
        try
        {
            _imageFilePath = imageFilePath;
            _xmlFilePath = xmlFilePath;

            if (!File.Exists(imageFilePath))
            {
                Debug.WriteLine($"[HdAtlasEditor] 图片文件不存在: {imageFilePath}");
                return false;
            }
            if (!File.Exists(xmlFilePath))
            {
                Debug.WriteLine($"[HdAtlasEditor] XML文件不存在: {xmlFilePath}");
                return false;
            }

            _imageData = File.ReadAllBytes(imageFilePath);
            ParseXmlConfig(xmlFilePath);

            // 获取图片尺寸
            try
            {
                using var stream = new MemoryStream(_imageData);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap != null)
                {
                    ImageWidth = bitmap.Width;
                    ImageHeight = bitmap.Height;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdAtlasEditor] 获取图片尺寸失败: {ex.Message}");
            }

            IsLoaded = true;
            Debug.WriteLine($"[HdAtlasEditor] 加载完成: {_entries.Count} 个条目");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 加载失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从AssetManager加载HD图集
    /// </summary>
    public bool LoadFromAssetManager(AssetManager am)
    {
        try
        {
            if (!am.IsLoaded)
            {
                Debug.WriteLine("[HdAtlasEditor] AssetManager未加载");
                return false;
            }

            var xmlEntry = am.Find("image/image_flags_hd.xml") ?? am.Find("image_flags_hd.xml");
            var imgEntry = am.Find("image/image_flags_hd.webp") ?? am.Find("image/image_flags_hd.webp")
                ?? am.Find("image/image_flags_hd.png") ?? am.Find("image_flags_hd.png");

            if (xmlEntry == null || imgEntry == null)
            {
                Debug.WriteLine("[HdAtlasEditor] AssetManager中未找到HD图集文件");
                return false;
            }

            _xmlFilePath = xmlEntry.FullPath;
            _imageFilePath = imgEntry.FullPath;
            _imageData = am.ReadBytes(imgEntry);

            ParseXmlConfigFromText(am.ReadText(xmlEntry));

            // 获取图片尺寸
            try
            {
                using var stream = new MemoryStream(_imageData);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap != null)
                {
                    ImageWidth = bitmap.Width;
                    ImageHeight = bitmap.Height;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdAtlasEditor] 获取图片尺寸失败: {ex.Message}");
            }

            IsLoaded = true;
            Debug.WriteLine($"[HdAtlasEditor] 从AssetManager加载完成: {_entries.Count} 个条目");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 从AssetManager加载失败: {ex.Message}");
            return false;
        }
    }

    private void ParseXmlConfig(string xmlPath)
    {
        try
        {
            string xmlContent = File.ReadAllText(xmlPath);
            ParseXmlConfigFromText(xmlContent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 解析XML失败: {ex.Message}");
        }
    }

    private void ParseXmlConfigFromText(string xmlContent)
    {
        try
        {
            _entries.Clear();
            _entryById.Clear();

            string tempXml = $"<root>{xmlContent}</root>";
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(tempXml);

            XmlNodeList? imageNodes = xmlDoc.SelectNodes("//Images/Image");
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

                var entry = new HdAtlasEntry { Name = name, Id = id };
                if (node.Attributes["x"] != null && int.TryParse(node.Attributes["x"]!.Value, out int x)) entry.X = x;
                if (node.Attributes["y"] != null && int.TryParse(node.Attributes["y"]!.Value, out int y)) entry.Y = y;
                if (node.Attributes["w"] != null && int.TryParse(node.Attributes["w"]!.Value, out int w)) entry.Width = w;
                if (node.Attributes["h"] != null && int.TryParse(node.Attributes["h"]!.Value, out int h)) entry.Height = h;

                if (entry.Width > 0 && entry.Height > 0)
                {
                    _entries.Add(entry);
                    _entryById[id] = entry;
                }
            }

            Debug.WriteLine($"[HdAtlasEditor] 解析了 {_entries.Count} 个HD国旗条目");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 解析XML失败: {ex.Message}");
        }
    }

    public HdAtlasEntry? GetEntry(int id)
    {
        return _entryById.TryGetValue(id, out var entry) ? entry : null;
    }

    public bool HasEntry(int id)
    {
        return _entryById.ContainsKey(id);
    }

    /// <summary>
    /// 添加或替换HD国旗条目
    /// </summary>
    public (HdAtlasEntry entry, bool isUpdate) AddOrReplaceEntry(int id, int width, int height)
    {
        string name = $"flag_{id}.png";
        bool isUpdate = false;

        if (_entryById.TryGetValue(id, out var existing))
        {
            _entries.Remove(existing);
            _entryById.Remove(id);
            isUpdate = true;
        }

        var entry = new HdAtlasEntry
        {
            Name = name,
            Id = id,
            Width = width,
            Height = height,
            X = 0,
            Y = 0
        };

        _entries.Add(entry);
        _entryById[id] = entry;
        DataChanged?.Invoke();

        return (entry, isUpdate);
    }

    /// <summary>
    /// 整理HD图集布局（避免重叠）
    /// </summary>
    public void ArrangeEntries(int padding = 2)
    {
        if (_entries.Count == 0) return;

        // 按面积降序排序
        var sorted = _entries.OrderByDescending(e => e.Width * e.Height).ToList();

        int currentX = 0;
        int currentY = 0;
        int rowHeight = 0;
        int maxWidth = ImageWidth > 0 ? ImageWidth : 2048;

        foreach (var entry in sorted)
        {
            if (currentX + entry.Width > maxWidth && currentX > 0)
            {
                currentX = 0;
                currentY += rowHeight + padding;
                rowHeight = 0;
            }

            entry.X = currentX;
            entry.Y = currentY;
            currentX += entry.Width + padding;
            rowHeight = Math.Max(rowHeight, entry.Height);
        }

        // 更新图集尺寸
        ImageWidth = Math.Max(ImageWidth, _entries.Max(e => e.X + e.Width) + padding);
        ImageHeight = Math.Max(ImageHeight, _entries.Max(e => e.Y + e.Height) + padding);

        DataChanged?.Invoke();
    }

    /// <summary>
    /// 渲染HD图集表面
    /// </summary>
    public SKBitmap? RenderAtlasSurface()
    {
        if (_entries.Count == 0) return null;

        int width = ImageWidth > 0 ? ImageWidth : _entries.Max(e => e.X + e.Width) + 4;
        int height = ImageHeight > 0 ? ImageHeight : _entries.Max(e => e.Y + e.Height) + 4;

        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var surface = new SKBitmap(info);
            using var canvas = new SKCanvas(surface);
            canvas.Clear(SKColors.Transparent);

            // 如果有原始图集数据，先绘制原始图集
            if (_imageData != null && _imageData.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(_imageData);
                    using var originalAtlas = SKBitmap.Decode(stream);
                    if (originalAtlas != null)
                    {
                        canvas.DrawBitmap(originalAtlas, 0, 0);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HdAtlasEditor] 绘制原始图集失败: {ex.Message}");
                }
            }

            return surface;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 渲染图集表面失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将图片绘制到HD图集表面的指定位置
    /// </summary>
    public bool DrawImageToAtlas(SKBitmap atlasSurface, SKBitmap image, int x, int y)
    {
        try
        {
            using var canvas = new SKCanvas(atlasSurface);
            canvas.DrawBitmap(image, x, y);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 绘制图片到图集失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存HD图集表面到文件
    /// </summary>
    public bool SaveAtlasSurface(SKBitmap atlasSurface, string? outputPath = null)
    {
        try
        {
            string path = outputPath ?? _imageFilePath ?? throw new InvalidOperationException("无图片文件路径");
            using var data = atlasSurface.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null) return false;
            File.WriteAllBytes(path, data.ToArray());
            Debug.WriteLine($"[HdAtlasEditor] 图集表面已保存: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 保存图集表面失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存HD图集XML配置
    /// </summary>
    public bool SaveXml(string? outputPath = null)
    {
        try
        {
            string path = outputPath ?? _xmlFilePath ?? throw new InvalidOperationException("无XML文件路径");

            var sb = new System.Text.StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true
            };

            // 写 <Images> + 内部 <Image> 列表
            using (var sw = new System.IO.StringWriter(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                w.WriteStartElement("Images");
                foreach (var entry in _entries)
                {
                    w.WriteStartElement("Image");
                    w.WriteAttributeString("name", entry.Name);
                    w.WriteAttributeString("x", entry.X.ToString());
                    w.WriteAttributeString("y", entry.Y.ToString());
                    w.WriteAttributeString("w", entry.Width.ToString());
                    w.WriteAttributeString("h", entry.Height.ToString());
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            Debug.WriteLine($"[HdAtlasEditor] XML已保存: {path}, 条目数={_entries.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 保存XML失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 添加图片到HD图集，自动整理布局，重建整张图集并保存
    /// </summary>
    public bool AddImageAndSave(int id, SKBitmap image, string? imageOutputPath = null, string? xmlOutputPath = null)
    {
        try
        {
            string newEntryName = $"flag_{id}.png";

            // 1. 保存已有条目的旧位置（从原图集裁剪像素时需要原始坐标）
            var oldPositions = new Dictionary<string, (int X, int Y, int W, int H)>();
            foreach (var entry in _entries)
            {
                if (entry.Name != newEntryName)
                    oldPositions[entry.Name] = (entry.X, entry.Y, entry.Width, entry.Height);
            }

            // 2. 添加/替换条目
            var (newEntry, isUpdate) = AddOrReplaceEntry(id, image.Width, image.Height);

            // 3. 整理布局
            ArrangeEntries(padding: 2);

            // 4. 加载原图集（一次性解码，不创建每个条目的独立SKBitmap缓存）
            SKBitmap? origAtlas = null;
            if (_imageData != null && _imageData.Length > 0)
            {
                using var stream = new MemoryStream(_imageData);
                origAtlas = SKBitmap.Decode(stream);
            }

            // 5. 重建新图集：从原图集按旧位置裁剪像素 → 绘制到新位置
            int atlasW = ImageWidth > 0 ? ImageWidth : (_entries.Count > 0 ? _entries.Max(e => e.X + e.Width) + 4 : 64);
            int atlasH = ImageHeight > 0 ? ImageHeight : (_entries.Count > 0 ? _entries.Max(e => e.Y + e.Height) + 4 : 64);
            var atlasInfo = new SKImageInfo(atlasW, atlasH, SKColorType.Rgba8888, SKAlphaType.Premul);
            var atlasSurface = new SKBitmap(atlasInfo);
            using (var canvas = new SKCanvas(atlasSurface))
            {
                canvas.Clear(SKColors.Transparent);

                // 5a. 绘制已有条目：直接从 origAtlas 按旧(X,Y)裁剪，画到新(X,Y)
                if (origAtlas != null)
                {
                    foreach (var e in _entries)
                    {
                        if (e.Name == newEntryName) continue;
                        if (!oldPositions.TryGetValue(e.Name, out var oldPos)) continue;

                        int srcX = Math.Max(0, Math.Min(oldPos.X, origAtlas.Width - 1));
                        int srcY = Math.Max(0, Math.Min(oldPos.Y, origAtlas.Height - 1));
                        int srcW = Math.Min(oldPos.W, origAtlas.Width - srcX);
                        int srcH = Math.Min(oldPos.H, origAtlas.Height - srcY);
                        if (srcW <= 0 || srcH <= 0) continue;

                        var srcRect = new SKRectI(srcX, srcY, srcX + srcW, srcY + srcH);
                        var dstRect = new SKRect(e.X, e.Y, e.X + srcW, e.Y + srcH);
                        canvas.DrawBitmap(origAtlas, srcRect, dstRect);
                    }
                }

                // 5b. 绘制新条目：使用传入的 image
                canvas.DrawBitmap(image, newEntry.X, newEntry.Y);
            }

            origAtlas?.Dispose();

            // 6. 保存图集图片
            string imgPath = imageOutputPath ?? _imageFilePath ?? throw new InvalidOperationException("无图片输出路径");
            using (atlasSurface)
            {
                if (!SaveAtlasSurface(atlasSurface, imgPath))
                {
                    Debug.WriteLine("[HdAtlasEditor] 保存图集图片失败");
                    return false;
                }
            }

            // 7. 保存XML配置
            string xmlPath = xmlOutputPath ?? _xmlFilePath ?? Path.Combine(Path.GetDirectoryName(imgPath) ?? "", "image_flags_hd.xml");
            if (!SaveXml(xmlPath))
            {
                Debug.WriteLine("[HdAtlasEditor] 保存XML配置失败");
                return false;
            }

            // 8. 更新内存中的图集数据
            _imageData = File.ReadAllBytes(imgPath);

            Debug.WriteLine($"[HdAtlasEditor] 成功添加图片 {newEntryName} 到HD图集，位置: ({newEntry.X}, {newEntry.Y})");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 添加图片到HD图集失败: {ex.Message}");
            return false;
        }
    }
}

public class HdAtlasEntry
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public override string ToString()
    {
        return $"{Name} ({X}, {Y}, {Width}x{Height})";
    }
}