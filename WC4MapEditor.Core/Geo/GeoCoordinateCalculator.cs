using System.Text;
using System.Text.Json;
using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Geo;

public sealed class GeoCoordinateCalculator
{
    private const byte OCEAN_TILE_TYPE = 1;

    public GeoReferencePoints ReferencePoints { get; set; } = new();

    public GeoCoordinateResult Calculate(MapData mapData)
    {
        if (mapData == null) throw new ArgumentNullException(nameof(mapData));

        var rp = ReferencePoints;
        if (!rp.IsValid())
            throw new InvalidOperationException("参考点设置不完整：需要两个不同的行参考点和两个不同的列参考点");

        int mapWidth = mapData.MapWidth;
        int mapHeight = mapData.MapHeight;

        double latPerRow = (rp.LatRef2 - rp.LatRef1) / (rp.RowRef2 - rp.RowRef1);
        double lonPerCol = (rp.LonRef2 - rp.LonRef1) / (rp.ColRef2 - rp.ColRef1);

        var cells = new List<GeoCell>(mapWidth * mapHeight);

        for (int row = 0; row < mapHeight; row++)
        {
            double lat = rp.LatRef1 + latPerRow * (row - rp.RowRef1);
            for (int col = 0; col < mapWidth; col++)
            {
                double lon = rp.LonRef1 + lonPerCol * (col - rp.ColRef1);
                var terrain = mapData.GetTerrain(col, row);
                bool isOcean = terrain.TileType1 == OCEAN_TILE_TYPE;

                cells.Add(new GeoCell
                {
                    Position = [col, row],
                    Coordinate = [Math.Round(lon, 7), Math.Round(lat, 7)],
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

    public void ExportToJson(GeoCoordinateResult result, string filePath)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"mapWidth\": {result.MapWidth},");
        sb.AppendLine($"  \"mapHeight\": {result.MapHeight},");

        // ReferencePoints
        sb.AppendLine("  \"referencePoints\": {");
        sb.AppendLine($"    \"rowRefs\": [{result.ReferencePoints.RowRefs[0]}, {result.ReferencePoints.RowRefs[1]}],");
        sb.AppendLine($"    \"latRefs\": [{result.ReferencePoints.LatRefs[0]:F7}, {result.ReferencePoints.LatRefs[1]:F7}],");
        sb.AppendLine($"    \"colRefs\": [{result.ReferencePoints.ColRefs[0]}, {result.ReferencePoints.ColRefs[1]}],");
        sb.AppendLine($"    \"lonRefs\": [{result.ReferencePoints.LonRefs[0]:F7}, {result.ReferencePoints.LonRefs[1]:F7}]");
        sb.AppendLine("  },");

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
}