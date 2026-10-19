using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.Conquest;

/// <summary>
/// 征服布局的数据窗口：BTL 里的省份/归属数组只覆盖世界地图上的一块矩形区域
/// （起点 MapClipX/MapClipY，宽 MapLength 列、高 MapWidth 行）。
/// <para>
/// 官方数据核对过的规则：
/// <list type="bullet">
/// <item>窗口第 i 格位于世界网格 (MapClipX + i % MapLength, MapClipY + i / MapLength)；</item>
/// <item>世界地图东西向首尾相接：窗口越过右边缘时从第 0 列接续，<b>行号不变</b>
/// （frontier80407 / stage60023 的省份只有这样才全部落在陆地上，建筑坐标也只有这样才落在窗口内）；</item>
/// <item>建筑、单位等实体坐标以及省份值（省会序号）存的都是世界网格索引 row * 世界宽度 + col；</item>
/// <item>SelectableTileCount 可能大于窗口格数（游戏按 8 对齐补齐），补齐部分排在数组末尾，值全为 0。</item>
/// </list>
/// </para>
/// </summary>
public readonly record struct ConquestWindow(int ClipX, int ClipY, int Width, int Height)
{
    /// <summary>从文件头读取窗口（BTL 的 MapLength 是列数、MapWidth 是行数）。</summary>
    public static ConquestWindow FromHeader(BTLHeader header)
        => new(header.MapClipX, header.MapClipY, header.MapLength, header.MapWidth);

    /// <summary>窗口格数（不含 SelectableTileCount 的补齐部分）。</summary>
    public int CellCount => Width > 0 && Height > 0 ? Width * Height : 0;

    /// <summary>窗口能否完整放进指定尺寸的世界网格（东西向允许跨边缘接续，南北向不允许）。</summary>
    public bool FitsIn(int worldWidth, int worldHeight)
        => Width > 0 && Height > 0 && worldWidth > 0 &&
           Width <= worldWidth && ClipX >= 0 && ClipX < worldWidth &&
           ClipY >= 0 && ClipY + Height <= worldHeight;

    /// <summary>窗口第 <paramref name="index"/> 格在世界网格中的位置。</summary>
    public (int Col, int Row) ToWorld(int index, int worldWidth)
        => ((ClipX + index % Width) % worldWidth, ClipY + index / Width);

    /// <summary>
    /// 世界网格格子 (<paramref name="col"/>, <paramref name="row"/>) 在窗口数组里的序号（<see cref="ToWorld"/> 的逆运算）；
    /// 格子在窗口以外返回 -1。东西向首尾相接：窗口越过右边缘时从第 0 列接续，行号不变。
    /// </summary>
    public int ToIndex(int col, int row, int worldWidth)
    {
        if (Width <= 0 || Height <= 0 || worldWidth <= 0 || col < 0 || col >= worldWidth) return -1;

        int localRow = row - ClipY;
        if (localRow < 0 || localRow >= Height) return -1;

        int localCol = ((col - ClipX) % worldWidth + worldWidth) % worldWidth;
        return localCol < Width ? localRow * Width + localCol : -1;
    }
}
