using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.Conquest;

/// <summary>
/// 以世界地图为底图的征服 MapData（对齐 VB ConquestRenderScene：征服画在 world{MapNumber}.bin 上）。
/// <para>
/// 网格 = 世界地图尺寸，地形取自世界地图（只读，征服 BTL 没有地形块，保存时不会写出）；
/// 省份按 <see cref="ConquestWindow"/> 放到数据窗口所在的世界格子；
/// 归属数组保持文件里的窗口相对顺序，<see cref="GetBelongIndex"/> 按 <see cref="ConquestWindow"/> 换算：
/// 窗口以外的格子没有归属（-1），跨东西接缝的窗口在第 0 列一侧同样取得到；
/// 建筑/单位等实体坐标本来就是世界索引，原样保留。
/// </para>
/// <para>
/// 与普通（数据窗口布局）MapData 的区别在省份的摆放方式和归属换算，
/// <see cref="ConquestParser.SaveFromMapData"/> 据此决定保存时按窗口取回省份。
/// </para>
/// </summary>
public sealed class ConquestWorldMapData : MapData
{
    /// <summary>作为底图的世界地图文件（场景缓存重新加载时用它恢复同一底图）。</summary>
    public string WorldMapPath { get; }

    public ConquestWorldMapData(string worldMapPath)
    {
        WorldMapPath = worldMapPath;
    }

    /// <summary>世界网格格子 → 归属数组序号：窗口内按 <see cref="ConquestWindow.ToIndex"/>，窗口以外为 -1。</summary>
    public override int GetBelongIndex(int col, int row)
    {
        if (col < 0 || row < 0 || col >= MapWidth || row >= MapHeight) return -1;
        return ConquestWindow.FromHeader(Header).ToIndex(col, row, MapWidth);
    }
}
