namespace WC4MapEditor.Core.Geo;

public class GeoCoordinateResult
{
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public GeoReferencePoints ReferencePoints { get; set; } = new();
    public List<GeoCell> Cells { get; set; } = [];
}