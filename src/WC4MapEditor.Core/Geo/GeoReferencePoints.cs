namespace WC4MapEditor.Core.Geo;

public class GeoReferencePoints
{
    public List<GeoReferencePoint> Points { get; set; } = [];

    public bool IsValid()
    {
        return Points.Count >= 2;
    }

    public GeoReferencePoints Clone()
    {
        return new GeoReferencePoints
        {
            Points = Points.Select(p => p.Clone()).ToList()
        };
    }
}