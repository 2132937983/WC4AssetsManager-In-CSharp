using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

public interface IHexInfoProvider
{
    HexCellInfo? GetHexCellInfo(int col, int row, HexInfoDisplayMode mode = HexInfoDisplayMode.Default);

    /// <summary>一行格子摘要（坐标、序号、地形与该格上的主要数据），格子不在地图内时返回 null。</summary>
    string? DescribeHex(int col, int row);
}
