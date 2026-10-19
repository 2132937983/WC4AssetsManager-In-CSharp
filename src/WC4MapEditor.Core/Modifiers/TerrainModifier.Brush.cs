using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

// 地形画笔：开关 / 半径 / 形状交给与其它模块共用的 BrushEngine，这里只管"往格子里写什么"。
public sealed partial class TerrainModifier
{
    private readonly BrushEngine _brush = new();
    private int _brushTerrainType;
    private int _brushDecoration;

    /// <summary>画笔的开关、半径与形状（与省份 / 归属 / 单位模块同一套几何与半径上限）。</summary>
    public BrushEngine Brush => _brush;

    public int BrushTerrainType
    {
        get => _brushTerrainType;
        set => _brushTerrainType = value;
    }

    public int BrushDecoration
    {
        get => _brushDecoration;
        set => _brushDecoration = value;
    }

    /// <summary>画笔半径，夹在 0 ~ <see cref="BrushEngine.MaxRadius"/>。</summary>
    public int BrushSize
    {
        get => _brush.Radius;
        set => _brush.Radius = value;
    }

    public bool BrushActive
    {
        get => _brush.Active;
        set => _brush.Active = value;
    }

    public string BrushShape
    {
        get => _brush.Shape;
        set => _brush.Shape = value;
    }

    public void PaintWithBrush(int centerCol, int centerRow) => PaintWithBrushMasked(centerCol, centerRow, null, true);

    /// <summary>
    /// 在画笔范围内把当前编辑层写成画笔地形。
    /// </summary>
    /// <param name="maskedTerrainIds">蒙版地形；为空表示不限。</param>
    /// <param name="maskIncludeMode">true = 只画在蒙版地形上，false = 跳过蒙版地形。</param>
    /// <param name="region">只画在这些格子里（有框选时传框选，对齐 VB：存在框选时画笔只改选中的格子）；null 表示不限。</param>
    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int>? maskedTerrainIds, bool maskIncludeMode,
        IReadOnlySet<HexCoord>? region = null)
    {
        if (_mapData == null || !_brush.Active) return;

        bool painted = false;
        foreach (var (c, r) in _brush.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight))
        {
            if (region != null && !region.Contains(new HexCoord(c, r))) continue;
            if (!PassesMask(c, r, maskedTerrainIds, maskIncludeMode)) continue;
            ApplyBrush(ref _mapData.GetTerrainRef(c, r), _editLayer, _brushTerrainType, _brushDecoration);
            painted = true;
        }
        if (painted) MarkModified();
    }

    private static void ApplyBrush(ref TerrainData terrain, int layer, int terrainType, int decoration)
    {
        SetTileTypeByLayer(ref terrain, layer, (byte)terrainType);
        SetDecorationTypeByLayer(ref terrain, layer, (byte)decoration);
    }

    private bool PassesMask(int col, int row, HashSet<int>? maskedTerrainIds, bool maskIncludeMode)
    {
        if (maskedTerrainIds == null || maskedTerrainIds.Count == 0) return true;

        bool inMask = maskedTerrainIds.Contains(GetTileTypeByLayer(_mapData!.GetTerrainRef(col, row), _editLayer));
        return maskIncludeMode ? inMask : !inMask;
    }
}
