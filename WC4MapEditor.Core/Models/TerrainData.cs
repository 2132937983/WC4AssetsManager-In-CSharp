using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainData
{
    public byte TileType1;
    public byte DecorationType1;
    public byte TextureOffsetX1;
    public byte TextureOffsetY1;
    public byte TileType2;
    public byte DecorationType2;
    public byte TextureOffsetX2;
    public byte TextureOffsetY2;
    public byte TileType3;
    public byte DecorationType3;
    public byte TextureOffsetX3;
    public byte TextureOffsetY3;
    public byte Reserved1;
    public byte Reserved2;
    public byte RiverValue;
    public byte Reserved3;

    public static TerrainData FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<TerrainData>(data[offset..]);

    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    public static TerrainData CreateDefault() => new TerrainData { TileType1 = 1 };
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Province
{
    public byte CountryId;
    public byte Reserved;

    public static Province FromBytes(ReadOnlySpan<byte> data, int offset) =>
        new Province { CountryId = data[offset], Reserved = data[offset + 1] };

    public void ToBytes(Span<byte> data, int offset)
    {
        data[offset] = CountryId;
        data[offset + 1] = Reserved;
    }

    public static Province CreateDefault() => new Province();
}
