using System.Diagnostics;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Rendering.Imaging;

public class OrthographicCamera
{
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public double WorldWidth { get; set; }
    public double WorldHeight { get; set; }

    public double PositionX { get; set; }
    public double PositionY { get; set; }

    public double Zoom { get; set; } = 1.0;
    public double MinZoom { get; set; } = 0.1;
    public double MaxZoom { get; set; } = 5.0;

    public event Action? ViewChanged;

    public OrthographicCamera(double viewportWidth, double viewportHeight, double worldWidth, double worldHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
    }

    public (double screenX, double screenY) WorldToScreen(double worldX, double worldY)
    {
        double sx = (worldX - PositionX) * Zoom;
        double sy = (worldY - PositionY) * Zoom;
        return (sx, sy);
    }

    public (double worldX, double worldY) ScreenToWorld(double screenX, double screenY)
    {
        double wx = screenX / Zoom + PositionX;
        double wy = screenY / Zoom + PositionY;
        return (wx, wy);
    }

    public void Pan(double dx, double dy)
    {
        PositionX += dx / Zoom;
        PositionY += dy / Zoom;
        ViewChanged?.Invoke();
    }

    public void ZoomAtPoint(double zoomFactor, double screenX, double screenY)
    {
        double oldZoom = Zoom;
        double newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, Zoom * zoomFactor));
        if (Math.Abs(oldZoom - newZoom) > 0.0001)
        {
            var (wx, wy) = ScreenToWorld(screenX, screenY);
            Zoom = newZoom;
            PositionX = wx - screenX / Zoom;
            PositionY = wy - screenY / Zoom;
            ViewChanged?.Invoke();
        }
    }

    public void Reset()
    {
        PositionX = 0;
        PositionY = 0;
        Zoom = 1.0;
        ViewChanged?.Invoke();
    }

    public void CenterOn(double worldX, double worldY)
    {
        PositionX = worldX - ViewportWidth / 2 / Zoom;
        PositionY = worldY - ViewportHeight / 2 / Zoom;
        ViewChanged?.Invoke();
    }

    public void CenterOnWorld()
    {
        PositionX = (WorldWidth - ViewportWidth / Zoom) / 2;
        PositionY = (WorldHeight - ViewportHeight / Zoom) / 2;
        ViewChanged?.Invoke();
    }

    public void FitToWindow(double viewportW, double viewportH)
    {
        ViewportWidth = viewportW;
        ViewportHeight = viewportH;
        if (WorldWidth <= 0 || WorldHeight <= 0) return;
        double zoomX = viewportW / WorldWidth;
        double zoomY = viewportH / WorldHeight;
        Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(zoomX, zoomY) * 0.95));
        CenterOnWorld();
    }

    public bool IsVisible(double x, double y, double w, double h)
    {
        double visibleWidth = ViewportWidth / Zoom;
        double visibleHeight = ViewportHeight / Zoom;
        return x + w > PositionX && x < PositionX + visibleWidth &&
               y + h > PositionY && y < PositionY + visibleHeight;
    }

    public void SetViewBounds(double worldX, double worldY, double worldW, double worldH)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0 || worldW <= 0 || worldH <= 0) return;
        double zoomX = ViewportWidth / worldW;
        double zoomY = ViewportHeight / worldH;
        Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(zoomX, zoomY) * 0.95));
        double cx = worldX + worldW / 2.0;
        double cy = worldY + worldH / 2.0;
        PositionX = cx - ViewportWidth / 2.0 / Zoom;
        PositionY = cy - ViewportHeight / 2.0 / Zoom;
        ViewChanged?.Invoke();
    }
}

public class TacticalMapObject
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefX { get; set; }
    public int RefY { get; set; }
    public bool IsSelected { get; set; }

    public TacticalMapObject() { }

    public TacticalMapObject(TacticalMapImageDef def)
    {
        Name = def.Name;
        X = def.X;
        Y = def.Y;
        Width = def.Width;
        Height = def.Height;
        RefX = def.RefX;
        RefY = def.RefY;
    }

    public TacticalMapImageDef ToImageDef()
    {
        return new TacticalMapImageDef
        {
            Name = Name,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            RefX = RefX,
            RefY = RefY
        };
    }

    public bool HitTest(double worldX, double worldY)
    {
        return worldX >= X && worldX <= X + Width &&
               worldY >= Y && worldY <= Y + Height;
    }

    public static TacticalMapObject Clone(TacticalMapObject src)
    {
        return new TacticalMapObject
        {
            Name = src.Name,
            X = src.X,
            Y = src.Y,
            Width = src.Width,
            Height = src.Height,
            RefX = src.RefX,
            RefY = src.RefY,
            IsSelected = false
        };
    }

    public override string ToString() => $"{Name} ({X}, {Y}, {Width}x{Height})";
}

public class TacticalMapEditor
{
    private string? _imageFilePath;
    private string? _xmlFilePath;
    private string? _textureName;
    private byte[]? _imageData;
    private readonly List<TacticalMapObject> _objects = new();
    private readonly Dictionary<string, TacticalMapObject> _objectByName = new();

    public IReadOnlyList<TacticalMapObject> Objects => _objects;
    public string? TextureName => _textureName;
    public string? ImageFilePath => _imageFilePath;
    public string? XmlFilePath => _xmlFilePath;
    public byte[]? ImageData => _imageData;
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public bool IsLoaded { get; private set; }

    public event Action? DataChanged;

    public bool LoadFromFiles(string imageFilePath, string? xmlFilePath = null)
    {
        try
        {
            _imageFilePath = imageFilePath;
            _xmlFilePath = xmlFilePath;

            _imageData = File.ReadAllBytes(imageFilePath);
            if (_imageData.Length == 0)
            {
                Debug.WriteLine("[TacticalMapEditor] 图片文件为空");
                return false;
            }

            if (!string.IsNullOrEmpty(xmlFilePath) && File.Exists(xmlFilePath))
            {
                ParseXmlConfig(xmlFilePath);
            }
            else
            {
                string? autoXml = FindAssociatedXml(imageFilePath);
                if (autoXml != null)
                {
                    _xmlFilePath = autoXml;
                    ParseXmlConfig(autoXml);
                }
            }

            IsLoaded = true;

            // 从图片数据获取尺寸（与Scene中 _sourceBitmap.Width/Height 一致）
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
                Debug.WriteLine($"[TacticalMapEditor] 获取图片尺寸失败: {ex.Message}");
            }

            Debug.WriteLine($"[TacticalMapEditor] 加载完成: {_objects.Count} 个对象, 图片 {imageFilePath}, 尺寸 {ImageWidth}x{ImageHeight}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 加载失败: {ex.Message}");
            return false;
        }
    }

    public bool LoadFromParser(TacticalMapParser parser, string? imageFilePath = null)
    {
        try
        {
            _imageData = parser.SurfaceData;
            if (_imageData == null || _imageData.Length == 0)
            {
                Debug.WriteLine("[TacticalMapEditor] Parser 无图片数据");
                return false;
            }

            _imageFilePath = imageFilePath;
            _objects.Clear();
            _objectByName.Clear();

            foreach (var def in parser.ImageDefinitions)
            {
                var obj = new TacticalMapObject(def);
                _objects.Add(obj);
                _objectByName[obj.Name] = obj;
            }

            IsLoaded = true;

            // 从图片数据获取尺寸
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
                Debug.WriteLine($"[TacticalMapEditor] 获取图片尺寸失败: {ex.Message}");
            }

            Debug.WriteLine($"[TacticalMapEditor] 从Parser加载: {_objects.Count} 个对象, 尺寸 {ImageWidth}x{ImageHeight}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 从Parser加载失败: {ex.Message}");
            return false;
        }
    }

    public void SetImageSize(int width, int height)
    {
        ImageWidth = width;
        ImageHeight = height;
    }

    private static string? FindAssociatedXml(string imageFilePath)
    {
        string dir = Path.GetDirectoryName(imageFilePath) ?? "";
        string name = Path.GetFileNameWithoutExtension(imageFilePath);
        string xmlPath = Path.Combine(dir, name + ".xml");
        if (File.Exists(xmlPath)) return xmlPath;
        return null;
    }

    private void ParseXmlConfig(string xmlPath)
    {
        try
        {
            string xmlContent = File.ReadAllText(xmlPath);
            string tempXml = $"<root>{xmlContent}</root>";
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(tempXml);

            _objects.Clear();
            _objectByName.Clear();

            XmlNodeList? imageNodes = xmlDoc.SelectNodes("//Images/Image");
            if (imageNodes == null) return;

            foreach (XmlNode imageNode in imageNodes)
            {
                if (imageNode.Attributes == null) continue;

                var obj = new TacticalMapObject();

                var nameAttr = imageNode.Attributes["name"];
                if (nameAttr != null) obj.Name = nameAttr.Value;

                var xAttr = imageNode.Attributes["x"];
                if (xAttr != null && int.TryParse(xAttr.Value, out int x)) obj.X = x;

                var yAttr = imageNode.Attributes["y"];
                if (yAttr != null && int.TryParse(yAttr.Value, out int y)) obj.Y = y;

                var wAttr = imageNode.Attributes["w"];
                if (wAttr != null && int.TryParse(wAttr.Value, out int w)) obj.Width = w;

                var hAttr = imageNode.Attributes["h"];
                if (hAttr != null && int.TryParse(hAttr.Value, out int h)) obj.Height = h;

                var refxAttr = imageNode.Attributes["refx"];
                if (refxAttr != null && int.TryParse(refxAttr.Value, out int refx)) obj.RefX = refx;

                var refyAttr = imageNode.Attributes["refy"];
                if (refyAttr != null && int.TryParse(refyAttr.Value, out int refy)) obj.RefY = refy;

                if (!string.IsNullOrEmpty(obj.Name) && obj.Width > 0 && obj.Height > 0)
                {
                    _objects.Add(obj);
                    _objectByName[obj.Name] = obj;
                }
            }

            XmlNode? textureNode = xmlDoc.SelectSingleNode("//Texture");
            if (textureNode?.Attributes?["name"] != null)
                _textureName = textureNode.Attributes["name"].Value;

            Debug.WriteLine($"[TacticalMapEditor] 解析XML: {_objects.Count} 个对象");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 解析XML失败: {ex.Message}");
        }
    }

    public TacticalMapObject? GetObject(string name)
    {
        return _objectByName.TryGetValue(name, out var obj) ? obj : null;
    }

    public TacticalMapObject? HitTest(double worldX, double worldY)
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i].HitTest(worldX, worldY))
                return _objects[i];
        }
        return null;
    }

    public List<TacticalMapObject> HitTestAll(double worldX, double worldY)
    {
        var results = new List<TacticalMapObject>();
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i].HitTest(worldX, worldY))
                results.Add(_objects[i]);
        }
        return results;
    }

    public void SelectObject(TacticalMapObject obj, bool exclusive = true)
    {
        if (exclusive)
        {
            foreach (var o in _objects)
                o.IsSelected = false;
        }
        obj.IsSelected = true;
        DataChanged?.Invoke();
    }

    public void ToggleSelection(TacticalMapObject obj)
    {
        obj.IsSelected = !obj.IsSelected;
        DataChanged?.Invoke();
    }

    public void ClearSelection()
    {
        foreach (var o in _objects)
            o.IsSelected = false;
        DataChanged?.Invoke();
    }

    public List<TacticalMapObject> GetSelectedObjects()
    {
        return _objects.Where(o => o.IsSelected).ToList();
    }

    public void MoveObject(TacticalMapObject obj, int newX, int newY)
    {
        newX = Math.Max(0, Math.Min(ImageWidth - obj.Width, newX));
        newY = Math.Max(0, Math.Min(ImageHeight - obj.Height, newY));
        obj.X = newX;
        obj.Y = newY;
        DataChanged?.Invoke();
    }

    public void DeleteSelectedObjects()
    {
        int removed = _objects.RemoveAll(o => o.IsSelected);
        if (removed > 0)
        {
            RebuildNameIndex();
            DataChanged?.Invoke();
        }
    }

    public TacticalMapObject? FindByName(string name)
        => _objectByName.TryGetValue(name, out var v) ? v : null;

    public bool RemoveByName(string name)
    {
        if (!_objectByName.TryGetValue(name, out var obj)) return false;
        _objects.Remove(obj);
        _objectByName.Remove(name);
        DataChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 对齐py版 import_images / import_dropped_image 语义：
    ///   - 已存在同 Name 对象 → 删除旧的，位置/尺寸用新值（保留旧对象的 X/Y 用作默认位置！）
    ///   - 不存在 → 新增
    ///   - 如果新对象 X+W/H 超过画布尺寸，会把 ImageWidth/ImageHeight (画布尺寸) 自动扩
    /// </summary>
    /// <returns>(addedOrUpdatedObject, isUpdate)</returns>
    public (TacticalMapObject obj, bool isUpdate) AddOrReplaceObject(TacticalMapObject newObj, bool preserveExistingPosition = true)
    {
        if (newObj == null) throw new ArgumentNullException(nameof(newObj));
        bool isUpdate = false;
        if (_objectByName.TryGetValue(newObj.Name, out var existing))
        {
            // 【py版行为】删除旧对象，但保留它的 (x,y) 作为新对象的默认位置
            int px = preserveExistingPosition ? existing.X : newObj.X;
            int py = preserveExistingPosition ? existing.Y : newObj.Y;
            _objects.Remove(existing);
            _objectByName.Remove(existing.Name);
            newObj.X = px;
            newObj.Y = py;
            isUpdate = true;
        }
        _objects.Add(newObj);
        _objectByName[newObj.Name] = newObj;

        // 画布自动扩大（如果超过当前画布宽/高）
        int reqW = Math.Max(1, newObj.X + newObj.Width + 4);
        int reqH = Math.Max(1, newObj.Y + newObj.Height + 4);
        if (reqW > ImageWidth) ImageWidth = reqW;
        if (reqH > ImageHeight) ImageHeight = reqH;

        DataChanged?.Invoke();
        return (newObj, isUpdate);
    }

    public List<TacticalMapObject> SearchObjects(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return _objects.ToList();
        string lower = keyword.ToLowerInvariant();
        return _objects.Where(o => o.Name.ToLowerInvariant().Contains(lower)).ToList();
    }

    public bool SaveXml(string? outputPath = null)
    {
        try
        {
            string path = outputPath ?? _xmlFilePath ?? throw new InvalidOperationException("无XML文件路径");
            // 对齐 Py 版（tacticalmap_editor.py Line 2338-2353）：
            //   磁盘文件格式 = 两个平级节点 <Texture/> + <Images> 直接拼接（WC4 历史遗留，没有外层 <Root>）。
            //   .NET XmlWriter 强制"一个文档只能有一个根节点"，直接写两个会报错/提前截断（=之前只写了<Texture>就没了的根因）。
            //   解决方案：分两次独立 XmlWriter，每次只写一个合法根，再用字符串拼接写盘。
            var sb = new System.Text.StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true
            };

            // ===== 1) 写 <Texture> =====
            if (!string.IsNullOrEmpty(_textureName))
            {
                using (var sw = new System.IO.StringWriter(sb))
                using (var w = XmlWriter.Create(sw, settings))
                {
                    w.WriteStartElement("Texture");
                    w.WriteAttributeString("name", _textureName);
                    w.WriteEndElement();
                }
                sb.AppendLine(); // 节点之间换行，对齐 Py 的 prettyxml 输出
            }

            // ===== 2) 写 <Images> + 内部 <Image> 列表 =====
            using (var sw = new System.IO.StringWriter(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                w.WriteStartElement("Images");
                foreach (var obj in _objects)
                {
                    w.WriteStartElement("Image");
                    w.WriteAttributeString("name", obj.Name);
                    w.WriteAttributeString("x", obj.X.ToString());
                    w.WriteAttributeString("y", obj.Y.ToString());
                    w.WriteAttributeString("w", obj.Width.ToString());
                    w.WriteAttributeString("h", obj.Height.ToString());
                    w.WriteAttributeString("refx", obj.RefX.ToString());
                    w.WriteAttributeString("refy", obj.RefY.ToString());
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            // 写盘
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            Debug.WriteLine($"[TacticalMapEditor] XML已保存: {path}, 对象数={_objects.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 保存XML失败: {ex.Message}");
            return false;
        }
    }

    private void RebuildNameIndex()
    {
        _objectByName.Clear();
        foreach (var obj in _objects)
            _objectByName[obj.Name] = obj;
    }

    public TacticalMapObject DuplicateObject(TacticalMapObject src, string newName, int offsetX = 20, int offsetY = 20)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        string finalName = newName;
        int i = 1;
        while (_objectByName.ContainsKey(finalName))
        {
            finalName = $"{newName}_{i}";
            i++;
        }
        var dup = new TacticalMapObject
        {
            Name = finalName,
            X = Math.Max(0, src.X + offsetX),
            Y = Math.Max(0, src.Y + offsetY),
            Width = src.Width,
            Height = src.Height,
            RefX = src.RefX,
            RefY = src.RefY,
            IsSelected = false
        };
        _objects.Add(dup);
        _objectByName[finalName] = dup;
        DataChanged?.Invoke();
        return dup;
    }

    public void AddObject(TacticalMapObject obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        // 确保名称唯一
        if (_objectByName.ContainsKey(obj.Name))
            obj.Name = GenerateUniqueName(obj.Name);
        _objects.Add(obj);
        _objectByName[obj.Name] = obj;
        DataChanged?.Invoke();
    }

    public string GenerateUniqueName(string baseName)
    {
        if (!_objectByName.ContainsKey(baseName)) return baseName;
        int i = 1;
        while (true)
        {
            string candidate = $"{baseName}_{i}";
            if (!_objectByName.ContainsKey(candidate)) return candidate;
            i++;
        }
    }

    public (int minX, int minY, int maxX, int maxY) GetObjectsBounds()
    {
        if (_objects.Count == 0) return (0, 0, 0, 0);
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = 0, maxY = 0;
        foreach (var o in _objects)
        {
            if (o.X < minX) minX = o.X;
            if (o.Y < minY) minY = o.Y;
            if (o.X + o.Width > maxX) maxX = o.X + o.Width;
            if (o.Y + o.Height > maxY) maxY = o.Y + o.Height;
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// 自动整理对象（紧密排布+支持自动扩大画布），对齐py版算法。
    /// </summary>
    /// <param name="currentWorldW">当前画布宽</param>
    /// <param name="currentWorldH">当前画布高</param>
    /// <param name="padding">对象间距，默认2px</param>
    /// <returns>
    /// newWorldW : 计算后应使用的新画布宽（可能已扩大）
    /// newWorldH : 计算后应使用的新画布高
    /// placedCount : 成功放置的对象数
    /// unplacedNames : 仍放不下的对象名列表（极端情况）
    /// </returns>
    public (int newWorldW, int newWorldH, int placedCount, List<string> unplacedNames)
        ArrangeObjects(int currentWorldW, int currentWorldH, int padding = 2)
    {
        if (_objects.Count == 0) return (currentWorldW, currentWorldH, 0, new List<string>());

        // === 步骤 1: 计算总面积，必要时先按倍率扩大画布（与py一致） ===
        long totalArea = _objects.Sum(o => (long)o.Width * o.Height);
        long canvasArea = (long)currentWorldW * currentWorldH;
        int worldW = currentWorldW;
        int worldH = currentWorldH;
        if (totalArea > canvasArea)
        {
            double ratio = 1.0 + (totalArea - canvasArea) / (double)Math.Max(1, canvasArea);
            worldW = (int)Math.Ceiling(worldW * Math.Sqrt(ratio) + 8);
            worldH = (int)Math.Ceiling(worldH * Math.Sqrt(ratio) + 8);
        }

        // === 步骤 2: 放置所有对象 ===
        var (placedCount, unplaced, _) = PlaceObjectsIntoCanvas(worldW, worldH, padding);

        // === 步骤 3: 仍有未放置对象 → 画布再扩展 1.5x ===
        if (unplaced.Count > 0)
        {
            int newW = (int)Math.Ceiling(worldW * 1.5);
            int newH = (int)Math.Ceiling(worldH * 1.5);
            int morePlaced;
            (morePlaced, unplaced, _) = PlaceObjectsIntoCanvas(newW, newH, padding, unplaced,
                _objects.Where(o => !unplaced.Contains(o)).ToList());
            placedCount += morePlaced;
            worldW = newW;
            worldH = newH;
        }

        // === 步骤 4: 按对象实际占用 + 4px padding 收缩画布到刚好放下 ===
        var bounds = GetObjectsBounds();
        int shrinkW = Math.Max(1, bounds.maxX + padding * 2);
        int shrinkH = Math.Max(1, bounds.maxY + padding * 2);
        worldW = Math.Min(worldW, shrinkW);
        worldH = Math.Min(worldH, shrinkH);

        DataChanged?.Invoke();
        return (worldW, worldH, placedCount, unplaced.Select(o => o.Name).ToList());
    }

    private (int placedCount, List<TacticalMapObject> unplaced, List<TacticalMapObject> placedOrder)
        PlaceObjectsIntoCanvas(int worldW, int worldH, int padding,
            List<TacticalMapObject>? targets = null,
            List<TacticalMapObject>? prePlaced = null)
    {
        var sorted = (targets ?? _objects).OrderByDescending(o => o.Width * o.Height).ToList();
        var placed = new List<TacticalMapObject>(prePlaced ?? Enumerable.Empty<TacticalMapObject>());
        int placedCount = 0;
        int gridSize = 50;
        int gw = (worldW / gridSize) + 1;
        int gh = (worldH / gridSize) + 1;
        var grid = new List<TacticalMapObject>[gh, gw];
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                grid[y, x] = new List<TacticalMapObject>();

        void AddToGrid(TacticalMapObject obj, int x, int y)
        {
            int l = Math.Max(0, (x - padding) / gridSize);
            int r = Math.Min(gw - 1, (x + obj.Width + padding) / gridSize);
            int t = Math.Max(0, (y - padding) / gridSize);
            int b = Math.Min(gh - 1, (y + obj.Height + padding) / gridSize);
            for (int gy = t; gy <= b; gy++)
                for (int gx = l; gx <= r; gx++)
                    grid[gy, gx].Add(obj);
        }

        foreach (var p in placed) AddToGrid(p, p.X, p.Y);

        bool CheckOverlap(int x, int y, TacticalMapObject obj)
        {
            int l = Math.Max(0, (x - padding) / gridSize);
            int r = Math.Min(gw - 1, (x + obj.Width + padding) / gridSize);
            int t = Math.Max(0, (y - padding) / gridSize);
            int b = Math.Min(gh - 1, (y + obj.Height + padding) / gridSize);
            for (int gy = t; gy <= b; gy++)
                for (int gx = l; gx <= r; gx++)
                    foreach (var p in grid[gy, gx])
                    {
                        int pl = p.X - padding, pt = p.Y - padding;
                        int pr = p.X + p.Width + padding, pb = p.Y + p.Height + padding;
                        int ol = x, ot = y, or = x + obj.Width, ob = y + obj.Height;
                        if (ol < pr && or > pl && ot < pb && ob > pt) return true;
                    }
            return false;
        }

        var unplaced = new List<TacticalMapObject>();
        foreach (var obj in sorted)
        {
            bool found = false;
            int bestX = 0, bestY = 0;
            int bestDist = int.MaxValue;

            // --- 先尝试在已放置对象的右侧和下方 ---
            foreach (var p in placed)
            {
                int tx = p.X + p.Width + padding;
                int ty = p.Y;
                if (tx + obj.Width <= worldW && ty + obj.Height <= worldH && !CheckOverlap(tx, ty, obj))
                {
                    int d = tx + ty;
                    if (d < bestDist) { bestX = tx; bestY = ty; bestDist = d; found = true; }
                }
                tx = p.X;
                ty = p.Y + p.Height + padding;
                if (tx + obj.Width <= worldW && ty + obj.Height <= worldH && !CheckOverlap(tx, ty, obj))
                {
                    int d = tx + ty;
                    if (d < bestDist) { bestX = tx; bestY = ty; bestDist = d; found = true; }
                }
            }

            // --- 没找到 → 从左上角网格粗搜 + 精搜 ---
            if (!found)
            {
                int step = 20;
                for (int y = 0; y <= Math.Max(0, worldH - obj.Height); y += step)
                {
                    for (int x = 0; x <= Math.Max(0, worldW - obj.Width); x += step)
                    {
                        if (!CheckOverlap(x, y, obj))
                        {
                            int d = x + y;
                            if (d < bestDist) { bestX = x; bestY = y; bestDist = d; found = true; }
                            if (x == 0 && y == 0) goto endSearch1;
                        }
                    }
                    if (found && bestX == 0 && bestY == 0) break;
                }
                endSearch1:
                if (found)
                {
                    int refineStep = 5;
                    int range = 20;
                    int ys = Math.Max(0, bestY - range);
                    int ye = Math.Min(Math.Max(0, worldH - obj.Height), bestY + range);
                    int xs = Math.Max(0, bestX - range);
                    int xe = Math.Min(Math.Max(0, worldW - obj.Width), bestX + range);
                    for (int y = ys; y <= ye; y += refineStep)
                        for (int x = xs; x <= xe; x += refineStep)
                            if (!CheckOverlap(x, y, obj))
                            {
                                int d = x + y;
                                if (d < bestDist) { bestX = x; bestY = y; bestDist = d; }
                            }
                }
            }

            if (!found) { unplaced.Add(obj); continue; }

            // === 步骤 3: 向左 & 向上 逐像素贴紧 ===
            obj.X = bestX; obj.Y = bestY;
            while (true)
            {
                int nx = Math.Max(0, obj.X - 1);
                if (nx == obj.X) break;
                if (!CheckOverlap(nx, obj.Y, obj)) obj.X = nx; else break;
            }
            while (true)
            {
                int ny = Math.Max(0, obj.Y - 1);
                if (ny == obj.Y) break;
                if (!CheckOverlap(obj.X, ny, obj)) obj.Y = ny; else break;
            }
            placed.Add(obj);
            AddToGrid(obj, obj.X, obj.Y);
            placedCount++;
        }
        return (placedCount, unplaced, placed);
    }

    // ========== 图集表面渲染与保存 ==========

    /// <summary>
    /// 将当前所有对象渲染到图集表面（SKBitmap）
    /// </summary>
    public SKBitmap? RenderAtlasSurface()
    {
        if (_objects.Count == 0) return null;

        int width = ImageWidth > 0 ? ImageWidth : _objects.Max(o => o.X + o.Width) + 4;
        int height = ImageHeight > 0 ? ImageHeight : _objects.Max(o => o.Y + o.Height) + 4;

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
                    Debug.WriteLine($"[TacticalMapEditor] 绘制原始图集失败: {ex.Message}");
                }
            }

            return surface;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 渲染图集表面失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将指定图片绘制到图集表面的指定位置
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
            Debug.WriteLine($"[TacticalMapEditor] 绘制图片到图集失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存图集表面到文件（PNG格式）
    /// </summary>
    public bool SaveAtlasSurface(SKBitmap atlasSurface, string? outputPath = null)
    {
        try
        {
            string path = outputPath ?? _imageFilePath ?? throw new InvalidOperationException("无图片文件路径");
            using var data = atlasSurface.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null) return false;
            File.WriteAllBytes(path, data.ToArray());
            Debug.WriteLine($"[TacticalMapEditor] 图集表面已保存: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 保存图集表面失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 添加或替换图片到图集，并自动整理布局（高效版本：不创建对象像素缓存，直接从原图集裁剪重绘）
    /// </summary>
    public bool AddImageAndArrange(string name, SKBitmap image, string? imageOutputPath = null, string? xmlOutputPath = null)
    {
        return AddImagesAndArrange(new[] { (name, image) }, imageOutputPath, xmlOutputPath);
    }

    /// <summary>
    /// 批量添加或替换图片到图集，自动整理布局（高效版本）
    /// </summary>
    public bool AddImagesAndArrange(IEnumerable<(string name, SKBitmap image)> images, string? imageOutputPath = null, string? xmlOutputPath = null)
    {
        try
        {
            var imageList = images.ToList();
            if (imageList.Count == 0) return true;

            var newNames = new HashSet<string>(imageList.Select(i => i.name));

            // 1. 保存已有对象的旧位置（从原图集裁剪像素时需要原始坐标）
            var oldPositions = new Dictionary<string, (int X, int Y, int W, int H)>();
            foreach (var obj in _objects)
            {
                if (!newNames.Contains(obj.Name))
                    oldPositions[obj.Name] = (obj.X, obj.Y, obj.Width, obj.Height);
            }

            // 2. 批量添加/替换对象定义
            foreach (var (name, image) in imageList)
            {
                var newObj = new TacticalMapObject
                {
                    Name = name,
                    Width = image.Width,
                    Height = image.Height,
                    // 新增图片的参考点保持 (0,0)，不自动取图片中心
                    RefX = 0,
                    RefY = 0
                };
                AddOrReplaceObject(newObj, preserveExistingPosition: true);
            }

            // 3. 整理布局（自动避免重叠，只修改对象的 X/Y 坐标）
            var (newW, newH, _, _) = ArrangeObjects(ImageWidth, ImageHeight, padding: 2);
            ImageWidth = newW;
            ImageHeight = newH;

            // 4. 加载原图集（一次性解码，不创建每个对象的独立SKBitmap缓存）
            SKBitmap? origAtlas = null;
            if (_imageData != null && _imageData.Length > 0)
            {
                using var stream = new MemoryStream(_imageData);
                origAtlas = SKBitmap.Decode(stream);
            }

            // 5. 重建新图集：从原图集按旧位置裁剪像素 → 绘制到新位置
            var atlasInfo = new SKImageInfo(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul);
            var atlasSurface = new SKBitmap(atlasInfo);
            using (var canvas = new SKCanvas(atlasSurface))
            {
                canvas.Clear(SKColors.Transparent);

                // 5a. 绘制已有对象：直接从 origAtlas 按旧(X,Y)裁剪，画到新(X,Y)
                if (origAtlas != null)
                {
                    foreach (var obj in _objects)
                    {
                        if (newNames.Contains(obj.Name)) continue;
                        if (!oldPositions.TryGetValue(obj.Name, out var oldPos)) continue;

                        int srcX = Math.Max(0, Math.Min(oldPos.X, origAtlas.Width - 1));
                        int srcY = Math.Max(0, Math.Min(oldPos.Y, origAtlas.Height - 1));
                        int srcW = Math.Min(oldPos.W, origAtlas.Width - srcX);
                        int srcH = Math.Min(oldPos.H, origAtlas.Height - srcY);
                        if (srcW <= 0 || srcH <= 0) continue;

                        var srcRect = new SKRectI(srcX, srcY, srcX + srcW, srcY + srcH);
                        var dstRect = new SKRect(obj.X, obj.Y, obj.X + srcW, obj.Y + srcH);
                        canvas.DrawBitmap(origAtlas, srcRect, dstRect);
                    }
                }

                // 5b. 绘制新对象：使用传入的 image
                foreach (var (name, image) in imageList)
                {
                    var obj = FindByName(name);
                    if (obj == null) continue;
                    canvas.DrawBitmap(image, obj.X, obj.Y);
                }
            }

            origAtlas?.Dispose();

            // 6. 保存图集图片
            string imgPath = imageOutputPath ?? _imageFilePath ?? throw new InvalidOperationException("无图片输出路径");
            using (atlasSurface)
            {
                if (!SaveAtlasSurface(atlasSurface, imgPath))
                {
                    Debug.WriteLine("[TacticalMapEditor] 保存图集图片失败");
                    return false;
                }
            }

            // 7. 保存XML配置
            string xmlPath = xmlOutputPath ?? _xmlFilePath ?? Path.Combine(Path.GetDirectoryName(imgPath) ?? "", Path.GetFileNameWithoutExtension(imgPath) + ".xml");
            if (!SaveXml(xmlPath))
            {
                Debug.WriteLine("[TacticalMapEditor] 保存XML配置失败");
                return false;
            }

            // 8. 更新内存中的图集数据
            _imageData = File.ReadAllBytes(imgPath);

            Debug.WriteLine($"[TacticalMapEditor] 成功批量添加 {imageList.Count} 张图片，整理布局完成");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapEditor] 批量添加图片到图集失败: {ex.Message}");
            return false;
        }
    }
}