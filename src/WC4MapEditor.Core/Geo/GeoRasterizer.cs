using System.Text;
using System.Text.Json;

namespace WC4MapEditor.Core.Geo;

/// <summary>经纬度包围盒（西/南/东/北）。</summary>
public readonly record struct GeoBounds(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    public double LonSpan => MaxLon - MinLon;
    public double LatSpan => MaxLat - MinLat;

    public bool IsValid => LonSpan > 0 && LatSpan > 0;

    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{MinLon:0.####},{MinLat:0.####},{MaxLon:0.####},{MaxLat:0.####}");

    public static GeoBounds Parse(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            throw new FormatException("--bbox 需要 4 个数：minLon,minLat,maxLon,maxLat");

        var v = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                throw new FormatException($"--bbox 第 {i + 1} 个值不是数字：{parts[i]}");
        }

        var b = new GeoBounds(v[0], v[1], v[2], v[3]);
        if (!b.IsValid) throw new FormatException("--bbox 的东/北必须大于西/南");
        return b;
    }
}

public sealed class GeoRasterOptions
{
    public string GeoJsonPath { get; init; } = "";

    /// <summary>地图宽/高（格）。</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>经纬度窗口；为空时取 GeoJSON 数据本身的包围盒。</summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>true = 生成归属（势力）掩码，每格一个字符；false = 海陆掩码（X / .）。</summary>
    public bool OwnersMode { get; init; }

    /// <summary>归属模式下取哪个属性作为势力键（如 ISO_A2 / name）。留空自动尝试常见字段。</summary>
    public string Property { get; init; } = "";

    /// <summary>归属模式下若键不是数字，用这个属性取国家 ID。</summary>
    public string IdProperty { get; init; } = "";

    /// <summary>海陆掩码多数表决次数（只对海陆模式生效）。</summary>
    public int SmoothPasses { get; init; }

    /// <summary>小于这个格数的陆块会被删掉（0 = 不删）。</summary>
    public int MinIslandCells { get; init; }
}

public sealed class GeoRasterResult
{
    public string Mask { get; init; } = "";
    public string OwnerMap { get; init; } = "";
    public GeoBounds Bounds { get; init; }
    public int LandCells { get; init; }
    public int SeaCells { get; init; }
    public int Components { get; init; }
    public int LargestComponent { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public double LandRatio => LandCells + SeaCells == 0
        ? 0
        : (double)LandCells / (LandCells + SeaCells);

    public double LargestComponentRatio => LandCells == 0 ? 0 : (double)LargestComponent / LandCells;
}

/// <summary>
/// 把 GeoJSON 多边形栅格化成地图用的 ASCII 掩码。
/// <para>
/// 为什么要有它：LLM 没有全图的几何先验，让它逐格 <c>SetTerrain</c> 画大陆，
/// 结果一定是"一半海一半陆"或一团团城市斑块。正确做法是让模型选地区和参数，
/// 几何从真实地理数据（Natural Earth 等 GeoJSON）栅格化出来。
/// </para>
/// <para>
/// 投影与 <c>scripts/mapgen/build-map.lua</c> 的 <c>mode=window</c> 完全一致
/// （含"奇数列下移半格"），所以掩码、城市落点、归属能对齐。
/// </para>
/// </summary>
public static class GeoRasterizer
{
    private const char LandChar = 'X';
    private const char SeaChar = '.';
    private const char KeepChar = '-';

    /// <summary>归属掩码可用的字符池（62 个，够放国家/势力）。</summary>
    private const string OwnerChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static GeoRasterResult Rasterize(GeoRasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.GeoJsonPath) || !File.Exists(options.GeoJsonPath))
            throw new FileNotFoundException("找不到 GeoJSON 文件", options.GeoJsonPath);

        if (options.Width < 2 || options.Height < 2)
            throw new ArgumentException("地图宽高至少为 2");

        var warnings = new List<string>();
        var features = LoadFeatures(options.GeoJsonPath, warnings);
        if (features.Count == 0)
            throw new InvalidOperationException("GeoJSON 里没有可用的 Polygon / MultiPolygon 几何");

        var bounds = options.Bounds ?? ComputeBounds(features);
        if (!bounds.IsValid)
            throw new InvalidOperationException("无法确定经纬度范围：请在数据里提供坐标或显式传 --bbox");

        int w = options.Width, h = options.Height;
        var land = new bool[w * h];
        var ownerKey = new string?[w * h];

        // 与 build-map 的 window 投影互逆：col/(W-1)、未修正行号/(H-1)
        double lonStep = bounds.LonSpan / (w - 1);
        double latStep = bounds.LatSpan / (h - 1);

        // 落在包围盒边上的点会让射线法出现"边界算不算在内"的抖动（表现为首/末行隔列海陆交替），
        // 所以采样点向内挪一个极小量；奇数列在最后一行还会多出半格，需要钳回边界。
        double epsLon = Math.Max(1e-9, bounds.LonSpan * 1e-9);
        double epsLat = Math.Max(1e-9, bounds.LatSpan * 1e-9);

        for (int col = 0; col < w; col++)
        {
            double lon = Math.Clamp(bounds.MinLon + col * lonStep,
                bounds.MinLon + epsLon, bounds.MaxLon - epsLon);

            for (int row = 0; row < h; row++)
            {
                double r0 = Math.Min(row + (((col % 2) == 1) ? 0.5 : 0.0), h - 1);
                double lat = Math.Clamp(bounds.MaxLat - r0 * latStep,
                    bounds.MinLat + epsLat, bounds.MaxLat - epsLat);

                int idx = row * w + col;
                for (int fi = 0; fi < features.Count; fi++)
                {
                    var f = features[fi];
                    if (lon < f.MinLon || lon > f.MaxLon || lat < f.MinLat || lat > f.MaxLat) continue;
                    if (!f.Contains(lon, lat)) continue;

                    land[idx] = true;
                    if (options.OwnersMode && ownerKey[idx] == null)
                        ownerKey[idx] = f.Key;
                    break;   // 先命中者优先；重叠在下面统计
                }
            }
        }

        if (options.SmoothPasses > 0 && !options.OwnersMode)
            Smooth(land, w, h, options.SmoothPasses);

        // 连通分量（六角邻居规则与 build-map 一致）
        var comp = new int[w * h];
        var sizes = new List<int>();
        for (int i = 0; i < comp.Length; i++) comp[i] = -1;
        for (int i = 0; i < land.Length; i++)
        {
            if (!land[i] || comp[i] >= 0) continue;
            int id = sizes.Count;
            int size = FloodFill(land, comp, w, h, i, id);
            sizes.Add(size);
        }

        if (options.MinIslandCells > 0 && !options.OwnersMode)
        {
            for (int i = 0; i < land.Length; i++)
            {
                if (land[i] && comp[i] >= 0 && sizes[comp[i]] < options.MinIslandCells)
                {
                    land[i] = false;
                    ownerKey[i] = null;
                }
            }
            // 重新统计
            sizes.Clear();
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            for (int i = 0; i < land.Length; i++)
            {
                if (!land[i] || comp[i] >= 0) continue;
                int id = sizes.Count;
                sizes.Add(FloodFill(land, comp, w, h, i, id));
            }
        }

        int landCells = land.Count(x => x);
        int largest = sizes.Count == 0 ? 0 : sizes.Max();

        // 归属字符映射
        var keyToChar = new Dictionary<string, char>(StringComparer.Ordinal);
        var ownerMapLines = new List<string>();
        if (options.OwnersMode)
        {
            var keys = ownerKey.Where(k => !string.IsNullOrEmpty(k)).Select(k => k!)
                               .Distinct(StringComparer.Ordinal)
                               .OrderBy(k => k, StringComparer.Ordinal)
                               .ToList();

            if (keys.Count > OwnerChars.Length)
            {
                warnings.Add($"势力数 {keys.Count} 超过 {OwnerChars.Length} 个可分配字符，多余的会变成 '?'");
            }

            for (int i = 0; i < keys.Count; i++)
            {
                char ch = i < OwnerChars.Length ? OwnerChars[i] : '?';
                keyToChar[keys[i]] = ch;
                ownerMapLines.Add($"{ch} = {keys[i]}");
            }

            int missingKey = ownerKey.Count(k => k == null);
            int emptyKey = ownerKey.Count(k => k != null && k.Length == 0);
            if (emptyKey > 0) warnings.Add($"{emptyKey} 个陆地格没有势力键（属性为空）");
            if (missingKey > 0 && emptyKey == 0) warnings.Add($"{missingKey} 个陆地格不在任何势力多边形内（保持无归属）");
        }

        // 输出
        var mask = new StringBuilder(w * h + h + 64);
        mask.Append("# generated by geo rasterize  bbox=").Append(bounds.ToString())
            .Append("  size=").Append(w).Append('x').Append(h)
            .Append(options.OwnersMode ? "  mode=owners" : "  mode=land")
            .Append('\n');

        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                int idx = row * w + col;
                if (!land[idx])
                {
                    mask.Append(SeaChar);
                    continue;
                }

                if (!options.OwnersMode)
                {
                    mask.Append(LandChar);
                    continue;
                }

                string? key = ownerKey[idx];
                mask.Append(key != null && keyToChar.TryGetValue(key, out char ch) ? ch : KeepChar);
            }
            mask.Append('\n');
        }

        // 重叠/比例之类的提示
        CheckAspect(bounds, w, h, warnings);

        return new GeoRasterResult
        {
            Mask = mask.ToString(),
            OwnerMap = ownerMapLines.Count == 0 ? "" : string.Join('\n', ownerMapLines) + "\n",
            Bounds = bounds,
            LandCells = landCells,
            SeaCells = w * h - landCells,
            Components = sizes.Count,
            LargestComponent = largest,
            Warnings = warnings,
        };
    }

    // ---------------------------------------------------------------- GeoJSON

    private sealed class Ring
    {
        public double[] X = Array.Empty<double>();
        public double[] Y = Array.Empty<double>();
        public double MinLon, MinLat, MaxLon, MaxLat;
    }

    private sealed class Feature
    {
        public List<Ring[]> Polygons { get; } = new();
        public string Key = "";
        public double MinLon = double.MaxValue, MinLat = double.MaxValue;
        public double MaxLon = double.MinValue, MaxLat = double.MinValue;

        // 按纬度分带的边索引：判断"点在多边形内"时只测当前带里的边。
        // 10m 数据一个大陆环可能有上万个顶点，逐格遍历全部边会慢到不可用。
        private double[] _x1 = Array.Empty<double>();
        private double[] _y1 = Array.Empty<double>();
        private double[] _x2 = Array.Empty<double>();
        private double[] _y2 = Array.Empty<double>();
        private int[][] _bands = Array.Empty<int[]>();
        private int _bandCount = 1;

        public void Grow(Ring r)
        {
            MinLon = Math.Min(MinLon, r.MinLon);
            MinLat = Math.Min(MinLat, r.MinLat);
            MaxLon = Math.Max(MaxLon, r.MaxLon);
            MaxLat = Math.Max(MaxLat, r.MaxLat);
        }

        /// <summary>把所有环的边收集起来并按纬度分桶（一次性，查询时 O(带内边数)）。</summary>
        public void BuildEdgeIndex(int bands = 256)
        {
            var x1 = new List<double>();
            var y1 = new List<double>();
            var x2 = new List<double>();
            var y2 = new List<double>();

            foreach (var polygon in Polygons)
            {
                foreach (var ring in polygon)
                {
                    for (int i = 0, j = ring.X.Length - 1; i < ring.X.Length; j = i++)
                    {
                        x1.Add(ring.X[j]); y1.Add(ring.Y[j]);
                        x2.Add(ring.X[i]); y2.Add(ring.Y[i]);
                    }
                }
            }

            _x1 = x1.ToArray(); _y1 = y1.ToArray();
            _x2 = x2.ToArray(); _y2 = y2.ToArray();

            _bandCount = Math.Clamp(bands, 1, 2048);
            var buckets = new List<int>[_bandCount];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();

            for (int e = 0; e < _x1.Length; e++)
            {
                int b0 = BandOf(Math.Min(_y1[e], _y2[e]));
                int b1 = BandOf(Math.Max(_y1[e], _y2[e]));
                for (int b = b0; b <= b1; b++) buckets[b].Add(e);
            }

            _bands = new int[_bandCount][];
            for (int i = 0; i < _bandCount; i++) _bands[i] = buckets[i].ToArray();
        }

        private int BandOf(double lat)
        {
            if (MaxLat <= MinLat) return 0;
            int b = (int)((lat - MinLat) / (MaxLat - MinLat) * _bandCount);
            return Math.Clamp(b, 0, _bandCount - 1);
        }

        /// <summary>射线法（even-odd）：环即洞，天然处理；只遍历当前纬度带内的边。</summary>
        public bool Contains(double lon, double lat)
        {
            if (lon < MinLon || lon > MaxLon || lat < MinLat || lat > MaxLat) return false;
            if (_bands.Length == 0) return false;

            bool inside = false;
            foreach (int e in _bands[BandOf(lat)])
            {
                double yi = _y1[e], yj = _y2[e];
                if ((yi > lat) == (yj > lat)) continue;

                double x = (_x2[e] - _x1[e]) * (lat - _y1[e]) / (_y2[e] - _y1[e]) + _x1[e];
                if (lon < x) inside = !inside;
            }
            return inside;
        }
    }

    private static List<Feature> LoadFeatures(string path, List<string> warnings)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var list = new List<Feature>();

        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        if (string.Equals(type, "FeatureCollection", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in root.GetProperty("features").EnumerateArray())
                AddFeature(item, list, warnings);
        }
        else if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(root, list, warnings);
        }
        else
        {
            // 裸几何
            var f = new Feature();
            if (ParseGeometry(root, f, warnings))
            {
                f.Key = "land";
                list.Add(f);
            }
        }

        // 去掉空几何，并为每个要素建纬度带索引（10m 数据靠它才跑得动）
        list.RemoveAll(f => f.Polygons.Count == 0);
        foreach (var f in list) f.BuildEdgeIndex();
        return list;
    }

    private static void AddFeature(JsonElement feature, List<Feature> list, List<string> warnings)
    {
        if (!feature.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Object)
            return;

        var f = new Feature();
        if (!ParseGeometry(geom, f, warnings)) return;

        string key = "";
        if (feature.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            key = ReadProperty(props, "id");
            if (key.Length == 0) key = ReadProperty(props, "ISO_A2");
            if (key.Length == 0) key = ReadProperty(props, "iso_a2");
            if (key.Length == 0) key = ReadProperty(props, "name");
            if (key.Length == 0) key = ReadProperty(props, "NAME");
            if (key.Length == 0) key = ReadProperty(props, "ADMIN");
        }
        f.Key = key;
        list.Add(f);
    }

    private static string ReadProperty(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static bool ParseGeometry(JsonElement geom, Feature f, List<string> warnings)
    {
        string type = geom.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        switch (type)
        {
            case "Polygon":
                AddPolygon(geom.GetProperty("coordinates"), f, warnings);
                return true;

            case "MultiPolygon":
                foreach (var poly in geom.GetProperty("coordinates").EnumerateArray())
                    AddPolygon(poly, f, warnings);
                return true;

            case "GeometryCollection":
                foreach (var g in geom.GetProperty("geometries").EnumerateArray())
                    ParseGeometry(g, f, warnings);
                return true;

            default:
                warnings.Add($"跳过不支持的几何类型：{type}");
                return false;
        }
    }

    private static void AddPolygon(JsonElement polygon, Feature f, List<string> warnings)
    {
        var rings = new List<Ring>();
        foreach (var ringJson in polygon.EnumerateArray())
        {
            var r = ParseRing(ringJson);
            if (r != null) rings.Add(r);
        }

        if (rings.Count == 0)
        {
            warnings.Add("跳过空多边形");
            return;
        }

        f.Polygons.Add(rings.ToArray());
        foreach (var r in rings) f.Grow(r);
    }

    private static Ring? ParseRing(JsonElement ringJson)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var pos in ringJson.EnumerateArray())
        {
            if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2) continue;
            xs.Add(pos[0].GetDouble());
            ys.Add(pos[1].GetDouble());
        }

        if (xs.Count < 3) return null;

        var r = new Ring { X = xs.ToArray(), Y = ys.ToArray() };
        r.MinLon = r.X.Min(); r.MaxLon = r.X.Max();
        r.MinLat = r.Y.Min(); r.MaxLat = r.Y.Max();
        return r;
    }

    private static GeoBounds ComputeBounds(List<Feature> features)
    {
        double minLon = double.MaxValue, minLat = double.MaxValue;
        double maxLon = double.MinValue, maxLat = double.MinValue;
        foreach (var f in features)
        {
            minLon = Math.Min(minLon, f.MinLon);
            minLat = Math.Min(minLat, f.MinLat);
            maxLon = Math.Max(maxLon, f.MaxLon);
            maxLat = Math.Max(maxLat, f.MaxLat);
        }
        return new GeoBounds(minLon, minLat, maxLon, maxLat);
    }

    // ---------------------------------------------------------------- 掩码处理

    private static void Smooth(bool[] land, int w, int h, int passes)
    {
        var copy = new bool[land.Length];
        for (int p = 0; p < passes; p++)
        {
            Array.Copy(land, copy, land.Length);
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    int count = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int c = col + dc, r = row + dr;
                            if (c < 0 || r < 0 || c >= w || r >= h) continue;
                            if (copy[r * w + c]) count++;
                        }
                    }
                    land[row * w + col] = count >= 5;
                }
            }
        }
    }

    private static int FloodFill(bool[] land, int[] comp, int w, int h, int start, int id)
    {
        var stack = new Stack<int>();
        stack.Push(start);
        comp[start] = id;

        int size = 0;
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            size++;
            int col = idx % w, row = idx / w;

            foreach (int n in Neighbors(col, row, w, h))
            {
                if (!land[n] || comp[n] >= 0) continue;
                comp[n] = id;
                stack.Push(n);
            }
        }
        return size;
    }

    /// <summary>六角邻居：奇数列下移半格（与 build-map / Camera 一致）。</summary>
    private static IEnumerable<int> Neighbors(int col, int row, int w, int h)
    {
        if (col > 0) yield return row * w + (col - 1);
        if (col < w - 1) yield return row * w + (col + 1);
        if (row > 0) yield return (row - 1) * w + col;
        if (row < h - 1) yield return (row + 1) * w + col;

        if ((col % 2) == 1)
        {
            if (col > 0 && row < h - 1) yield return (row + 1) * w + (col - 1);
            if (col < w - 1 && row < h - 1) yield return (row + 1) * w + (col + 1);
        }
        else
        {
            if (col > 0 && row > 0) yield return (row - 1) * w + (col - 1);
            if (col < w - 1 && row > 0) yield return (row - 1) * w + (col + 1);
        }
    }

    private static void CheckAspect(GeoBounds b, int w, int h, List<string> warnings)
    {
        double bboxAspect = b.LonSpan / Math.Max(1e-9, b.LatSpan);
        double mapAspect = (double)(w - 1) / Math.Max(1, h - 1);
        // 高纬度经度会被压缩，粗略按中纬度余弦修正
        double midLat = (b.MinLat + b.MaxLat) / 2.0;
        double adjusted = bboxAspect * Math.Cos(midLat * Math.PI / 180.0);

        if (adjusted > mapAspect * 1.35 || adjusted < mapAspect * 0.65)
        {
            warnings.Add(string.Format(
                "经纬范围的比例（约 {0:0.00}）与地图宽高比（{1:0.00}）差得多，形状会被拉伸；建议调整 --bbox 或地图尺寸",
                adjusted, mapAspect));
        }
    }
}
