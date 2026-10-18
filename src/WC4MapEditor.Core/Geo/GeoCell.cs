namespace WC4MapEditor.Core.Geo;

public class GeoCell
{
    public int[] Position { get; set; } = new int[2];
    public double[] Coordinate { get; set; } = new double[2];
    public bool IsOcean { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Col => Position[0];

    [System.Text.Json.Serialization.JsonIgnore]
    public int Row => Position[1];

    [System.Text.Json.Serialization.JsonIgnore]
    public double Longitude => Coordinate[0];

    [System.Text.Json.Serialization.JsonIgnore]
    public double Latitude => Coordinate[1];
}