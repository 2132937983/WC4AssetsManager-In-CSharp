using System.Text;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Geo;

public sealed class GeoCoordinateCalculator
{
    private const byte OCEAN_TILE_TYPE = 1;
    private const double DEG2RAD = Math.PI / 180.0;

    public GeoReferencePoints ReferencePoints { get; set; } = new();

    /// <summary>
    /// 地图中心纬度（度），用于经度间距修正。
    /// null 表示自动从参考点估算。
    /// </summary>
    public double? CentralLatitude { get; set; }

    public GeoCoordinateResult Calculate(MapData mapData)
    {
        if (mapData == null) throw new ArgumentNullException(nameof(mapData));

        var rp = ReferencePoints;
        if (!rp.IsValid())
            throw new InvalidOperationException("参考点设置不完整：至少需要2个参考点");

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;

        // 1. 纬度拟合：lat = a * row + b（纬度本身是等间距的，保持线性）
        var (latA, latB) = FitLinear(rp.Points.Select(p => ((double)p.Row, p.Latitude)));

        // 2. 判断是否需要球面修正
        bool useSphericalCorrection = ShouldUseSphericalCorrection(rp.Points);

        // 3. 经度拟合
        double lonC, lonD;
        if (useSphericalCorrection && rp.Points.Count >= 3)
        {
            // 球面修正：使用稳定的线性化方法
            // 核心思想：在参考点所在纬度处，经度/列 的比率应与 cos(纬度) 成正比
            // 即：Δlon/Δcol = k * cos(lat)，其中 k 是赤道处的经度/列比率
            (lonC, lonD) = FitLongitudeSphericalStable(rp.Points, latA, latB);
        }
        else
        {
            // 简单线性拟合
            (lonC, lonD) = FitLinear(rp.Points.Select(p => ((double)p.Col, p.Longitude)));
        }

        var cells = new List<GeoCell>(mapWidth * mapHeight);

        for (int row = 0; row < mapHeight; row++)
        {
            double lat = latA * row + latB;

            for (int col = 0; col < mapWidth; col++)
            {
                double finalLon;
                if (useSphericalCorrection)
                {
                    // 球面修正：经度间距与 cos(纬度) 成正比
                    // lon = lonC * col * cos(lat) + lonD
                    double cosLat = Math.Cos(lat * DEG2RAD);
                    finalLon = lonC * col * cosLat + lonD;
                }
                else
                {
                    finalLon = lonC * col + lonD;
                }

                var terrain = mapData.GetTerrain(col, row);
                bool isOcean = terrain.TileType1 == OCEAN_TILE_TYPE;

                cells.Add(new GeoCell
                {
                    Position = [col, row],
                    Coordinate = [Math.Round(finalLon, 7), Math.Round(lat, 7)],
                    IsOcean = isOcean
                });
            }
        }

        return new GeoCoordinateResult
        {
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            ReferencePoints = rp.Clone(),
            Cells = cells
        };
    }

    /// <summary>
    /// 判断是否需要进行球面修正。
    /// 当参考点分布在较广纬度范围（>5度）且数量>=3时启用。
    /// </summary>
    private static bool ShouldUseSphericalCorrection(List<GeoReferencePoint> points)
    {
        if (points.Count < 3) return false;

        double minLat = points.Min(p => p.Latitude);
        double maxLat = points.Max(p => p.Latitude);
        double latSpan = maxLat - minLat;

        // 纬度跨度超过5度时启用球面修正
        return latSpan > 5.0;
    }

    /// <summary>
    /// 稳定的球面经度拟合。
    /// 模型：lon = k * col * cos(lat) + d
       /// 其中 k 是赤道处的经度/列比率，d 是基准经度
    /// 通过线性回归求解 k 和 d
    /// </summary>
    private static (double K, double D) FitLongitudeSphericalStable(List<GeoReferencePoint> points, double latA, double latB)
    {
        // 构建数据：X = col * cos(lat), Y = lon
        var transformedPoints = new List<(double X, double Y)>();

        foreach (var p in points)
        {
            double lat = latA * p.Row + latB;
            double cosLat = Math.Cos(lat * DEG2RAD);
            // 避免 cos(lat) 接近 0 的数值问题
            cosLat = Math.Max(cosLat, 0.01);

            double x = p.Col * cosLat;
            double y = p.Longitude;
            transformedPoints.Add((x, y));
        }

        // 对转换后的数据做线性回归
        return FitLinear(transformedPoints);
    }

    private static (double Slope, double Intercept) FitLinear(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        int n = list.Count;
        if (n < 2) throw new InvalidOperationException("至少需要2个点进行拟合");

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        foreach (var (x, y) in list)
        {
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10)
            throw new InvalidOperationException("参考点分布异常，无法拟合");

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        return (slope, intercept);
    }

    public void ExportToJson(GeoCoordinateResult result, string filePath)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"mapWidth\": {result.MapWidth},");
        sb.AppendLine($"  \"mapHeight\": {result.MapHeight},");

        // ReferencePoints
        sb.AppendLine("  \"referencePoints\": [");
        for (int i = 0; i < result.ReferencePoints.Points.Count; i++)
        {
            var p = result.ReferencePoints.Points[i];
            string comma = i < result.ReferencePoints.Points.Count - 1 ? "," : "";
            sb.AppendLine($"    {{\"row\": {p.Row}, \"col\": {p.Col}, \"latitude\": {p.Latitude:F7}, \"longitude\": {p.Longitude:F7}}}{comma}");
        }
        sb.AppendLine("  ],");

        // Cells
        sb.AppendLine("  \"cells\": [");
        for (int i = 0; i < result.Cells.Count; i++)
        {
            var cell = result.Cells[i];
            string comma = i < result.Cells.Count - 1 ? "," : "";
            sb.AppendLine($"    {{\"position\": [{cell.Position[0]}, {cell.Position[1]}], \"coordinate\": [{cell.Coordinate[0]:F7}, {cell.Coordinate[1]:F7}], \"isOcean\": {(cell.IsOcean ? "true" : "false")}}}{comma}");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(filePath, sb.ToString());
    }

    public GeoCoordinateResult CalculateAndExport(MapData mapData, string filePath)
    {
        var result = Calculate(mapData);
        ExportToJson(result, filePath);
        return result;
    }

    /// <summary>
    /// 仅导出参考点到 JSON 文件（不包含 cells 数据）。
    /// 用于保存参考点配置，下次可直接导入复用。
    /// </summary>
    public void ExportReferencePointsOnly(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

        var rp = ReferencePoints;
        if (!rp.IsValid())
            throw new InvalidOperationException("参考点设置不完整：至少需要2个参考点");

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"version\": 1,");
        sb.AppendLine($"  \"centralLatitude\": {CentralLatitude?.ToString("F7") ?? "null"},");
        sb.AppendLine("  \"referencePoints\": [");
        for (int i = 0; i < rp.Points.Count; i++)
        {
            var p = rp.Points[i];
            string comma = i < rp.Points.Count - 1 ? "," : "";
            sb.AppendLine($"    {{\"row\": {p.Row}, \"col\": {p.Col}, \"latitude\": {p.Latitude:F7}, \"longitude\": {p.Longitude:F7}}}{comma}");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(filePath, sb.ToString());
    }

    /// <summary>
    /// 从 JSON 文件导入参考点配置。
    /// </summary>
    public void ImportReferencePoints(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("参考点文件不存在", filePath);

        string json = File.ReadAllText(filePath);
        var points = new List<GeoReferencePoint>();

        // 简单解析 JSON（不引入额外依赖）
        var lines = json.Split('\n');
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("{\"") && trimmed.Contains("\"row\""))
            {
                var p = new GeoReferencePoint();
                // 解析 row
                var rowMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "\"row\"\\s*:\\s*(\\d+)");
                if (rowMatch.Success) p.Row = int.Parse(rowMatch.Groups[1].Value);
                // 解析 col
                var colMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "\"col\"\\s*:\\s*(\\d+)");
                if (colMatch.Success) p.Col = int.Parse(colMatch.Groups[1].Value);
                // 解析 latitude
                var latMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "\"latitude\"\\s*:\\s*([+-]?\\d+\\.?\\d*)");
                if (latMatch.Success) p.Latitude = double.Parse(latMatch.Groups[1].Value);
                // 解析 longitude
                var lonMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "\"longitude\"\\s*:\\s*([+-]?\\d+\\.?\\d*)");
                if (lonMatch.Success) p.Longitude = double.Parse(lonMatch.Groups[1].Value);

                points.Add(p);
            }
            // 解析 centralLatitude
            if (trimmed.Contains("\"centralLatitude\""))
            {
                var latMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "\"centralLatitude\"\\s*:\\s*([+-]?\\d+\\.?\\d*|null)");
                if (latMatch.Success && latMatch.Groups[1].Value != "null")
                    CentralLatitude = double.Parse(latMatch.Groups[1].Value);
            }
        }

        ReferencePoints = new GeoReferencePoints { Points = points };
    }
}