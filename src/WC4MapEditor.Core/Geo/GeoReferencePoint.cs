namespace WC4MapEditor.Core.Geo;

public class GeoReferencePoint
{
    public int Row { get; set; }
    public int Col { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public GeoReferencePoint Clone()
    {
        return new GeoReferencePoint
        {
            Row = Row,
            Col = Col,
            Latitude = Latitude,
            Longitude = Longitude
        };
    }
}