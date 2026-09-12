using SkiaSharp;

namespace WC4MapEditor.Core.Services;

public interface IFlagImageService
{
    SKBitmap? LoadFlagFromTacticalMap(string flagName);
    SKBitmap? LoadFlagFromHdAtlas(int countryId);
    SKBitmap? LoadFlagFromFile(string path);

    /// <summary>
    /// 按国家ID加载tacticalmap中的大国旗 (flag_{id:D2}.png)
    /// </summary>
    SKBitmap? LoadBigFlag(int countryId);

    /// <summary>
    /// 按国家ID加载tacticalmap中的小国旗 (f_{id:D2}.png)
    /// </summary>
    SKBitmap? LoadSmallFlag(int countryId);

    List<string> GetAvailableTacticalMapFlags();
    List<int> GetAvailableHdFlags();
    bool SaveFlag(SKBitmap flag, string path, SKEncodedImageFormat format, int quality);
    SKBitmap? ResizeFlag(SKBitmap flag, int width, int height);
    SKBitmap? CropCircularFlag(SKBitmap flag, int x, int y, int size);

    /// <summary>
    /// 刷新缓存（在图集文件被外部修改后调用，使下次加载时重新读取磁盘）
    /// </summary>
    void RefreshCache();
}