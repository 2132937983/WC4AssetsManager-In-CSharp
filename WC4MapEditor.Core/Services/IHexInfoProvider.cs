using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Services;

public interface IHexInfoProvider
{
    HexCellInfo? GetHexCellInfo(int col, int row, HexInfoDisplayMode mode = HexInfoDisplayMode.Default);
}