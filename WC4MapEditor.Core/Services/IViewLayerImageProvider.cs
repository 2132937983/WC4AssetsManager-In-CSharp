namespace WC4MapEditor.Core.Services;

public interface IViewLayerImageProvider
{
    bool HasImage { get; }
    int Width { get; }
    int Height { get; }
    byte[] ExtractRegionData(int x, int y, int width, int height);
}