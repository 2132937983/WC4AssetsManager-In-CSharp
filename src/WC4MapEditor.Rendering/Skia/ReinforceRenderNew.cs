using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 援军渲染层，新样式（对齐 VB ReinforceRenderNew）：援军模式专用，图标排在格子中心的 2 列 6 行网格里，最多 12 个，
/// 超出时在格子下方显示 "X总数"。分组、叠加信息与绘制全部继承自 <see cref="ReinforceRender"/>，这里只改排布。
/// </summary>
public class ReinforceRenderNew : ReinforceRender
{
    private const int COLUMN_COUNT = 2;

    public ReinforceRenderNew(Camera camera, MapData mapData) : base(camera, mapData)
    {
    }

    protected override int MaxPerHex => 12;

    /// <summary>
    /// 第 0 个在左上、第 1 个在右上、第 2 个在左中……：列偏移 ±0.5 个图标，行从中心上方 2.5 行开始，行距 0.85 个图标（重叠 15%）。
    /// </summary>
    protected override SKPoint IconCenter(SKPoint hexCenter, int index, float iconSize, float hexHeight)
    {
        int row = index / COLUMN_COUNT;
        int col = index % COLUMN_COUNT;
        return new SKPoint(hexCenter.X + (col - 0.5f) * iconSize, hexCenter.Y + (row - 2.5f) * iconSize * 0.85f);
    }

    protected override SKPoint OverflowTextPosition(SKPoint hexCenter, int visibleCount, float iconSize, float hexHeight)
        => new(hexCenter.X, hexCenter.Y + hexHeight * 0.5f + iconSize);
}
