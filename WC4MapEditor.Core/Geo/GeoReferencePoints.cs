namespace WC4MapEditor.Core.Geo;

public class GeoReferencePoints
{
    public int[] RowRefs { get; set; } = new int[2];
    public double[] LatRefs { get; set; } = new double[2];
    public int[] ColRefs { get; set; } = new int[2];
    public double[] LonRefs { get; set; } = new double[2];

    [System.Text.Json.Serialization.JsonIgnore]
    public int RowRef1 { get => RowRefs[0]; set => RowRefs[0] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public double LatRef1 { get => LatRefs[0]; set => LatRefs[0] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int RowRef2 { get => RowRefs[1]; set => RowRefs[1] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public double LatRef2 { get => LatRefs[1]; set => LatRefs[1] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ColRef1 { get => ColRefs[0]; set => ColRefs[0] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public double LonRef1 { get => LonRefs[0]; set => LonRefs[0] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ColRef2 { get => ColRefs[1]; set => ColRefs[1] = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public double LonRef2 { get => LonRefs[1]; set => LonRefs[1] = value; }

    public bool IsValid()
    {
        return RowRefs[0] != RowRefs[1] && ColRefs[0] != ColRefs[1];
    }

    public GeoReferencePoints Clone()
    {
        return new GeoReferencePoints
        {
            RowRefs = [RowRefs[0], RowRefs[1]],
            LatRefs = [LatRefs[0], LatRefs[1]],
            ColRefs = [ColRefs[0], ColRefs[1]],
            LonRefs = [LonRefs[0], LonRefs[1]]
        };
    }
}