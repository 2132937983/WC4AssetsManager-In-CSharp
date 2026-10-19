using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>省份编辑的选择工具（R 矩形 / O 多边形），同一时刻最多开启一个。</summary>
public enum ProvinceSelectTool
{
    None,
    Rectangle,
    Polygon
}

/// <summary>多边形选择工具一次左键点击的结果类型。</summary>
public enum PolygonClickKind
{
    /// <summary>加入了一个新顶点</summary>
    VertexAdded,

    /// <summary>与上一个顶点是同一格，忽略</summary>
    Ignored,

    /// <summary>点回第一个顶点，但顶点不足 3 个，无法闭合</summary>
    TooFewVertices,

    /// <summary>点回第一个顶点，多边形闭合</summary>
    Closed
}

/// <summary>
/// 多边形选择工具一次点击的结果：<see cref="Hexes"/> 在闭合时是多边形覆盖的全部格子，
/// 否则是已画出的折线经过的格子（用来在选区里显示进行中的多边形）。
/// </summary>
public readonly record struct PolygonClick(PolygonClickKind Kind, IReadOnlyList<HexCoord> Hexes);

/// <summary>
/// 省份模块：对每格 16 位省区值的读写与批量生成。
/// <para>
/// 省区值 = 省会格子的索引；空白（不属于任何省区）用 <see cref="Province.EmptyValue"/>，
/// "有没有省区"统一按 <see cref="Province.IsValid"/> 判定（0 与 0xFFFF 都算空白）。
/// 六边形邻接按<b>列号</b>奇偶取（奇数列整体下移半格），与渲染层、背景图 / 手绘边界生成器、地形模块一致。
/// </para>
/// </summary>
public sealed class ProvinceModifier : ModifierBase, IBrushTarget
{
    public override string Name => "province";
    public override string DisplayName => "省份修改器";

    private readonly BrushEngine _brushEngine = new();
    private readonly Random _random = new();

    /// <summary>
    /// 区域复制时源地图的宽度。剪贴板只存相对坐标，粘贴时要把"区域里自带的省会"换算到新位置，
    /// 得知道源格子的索引；没有区域复制（或复制来源不明）时为 0，此时不做省会换算。
    /// </summary>
    private int _copiedRegionSourceWidth;

    private ProvinceSelectTool _selectTool;
    private HexCoord? _rectangleAnchor;
    private readonly List<HexCoord> _polygonVertices = new();

    private static readonly (int dc, int dr)[] EvenColNeighborOffsets =
        { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1) };

    private static readonly (int dc, int dr)[] OddColNeighborOffsets =
        { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, 1), (1, 1) };

    public BrushEngine Brush => _brushEngine;
    public int BrushRadius => _brushEngine.Radius;
    public bool IsBrushMode => _brushEngine.Active;

    /// <summary>当前开启的选择工具</summary>
    public ProvinceSelectTool SelectTool => _selectTool;
    public bool IsRectangleSelectMode => _selectTool == ProvinceSelectTool.Rectangle;
    public bool IsPolygonSelectMode => _selectTool == ProvinceSelectTool.Polygon;

    /// <summary>矩形选择的起点（左键设置）；未设置时为 null</summary>
    public HexCoord? RectangleAnchor => _rectangleAnchor;

    /// <summary>进行中的多边形的顶点（格子坐标，平移缩放后不会错位）</summary>
    public IReadOnlyList<HexCoord> PolygonVertices => _polygonVertices;

    bool IBrushTarget.BrushActive
    {
        get => _brushEngine.Active;
        set => _brushEngine.Active = value;
    }

    int IBrushTarget.BrushSize
    {
        get => _brushEngine.Radius;
        set => _brushEngine.Radius = value;
    }

    string IBrushTarget.BrushShape
    {
        get => _brushEngine.Shape;
        set => _brushEngine.Shape = value;
    }

    /// <summary>换地图时丢弃未完成的矩形 / 多边形：它们的格子坐标属于上一张地图。</summary>
    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        ResetShape();
    }

    public override void Deinitialize()
    {
        ResetShape();
        base.Deinitialize();
    }

    #region 单格读写

    /// <summary>
    /// 把格子的省区值设为 <paramref name="parameter"/>（<see cref="Province"/> / ushort / int / byte，0-65535，0xFFFF = 无省区）；
    /// 不带参数时用剪贴板里复制的省区值。写入的总是完整的 16 位省区值。
    /// </summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();

        if (parameter == null)
        {
            ushort? copied = CopiedProvinceValue;
            return copied.HasValue ? SetProvinceValue(col, row, copied.Value) : ModifierResult.Fail(NothingCopiedMessage());
        }

        return TryConvertValue(parameter, out int value)
            ? SetProvinceValue(col, row, value)
            : ModifierResult.Fail(Loc.F("Module.Province.Msg.UnsupportedValue", parameter.GetType().Name));
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();

        _mapData.GetProvinceRef(col, row).ProvinceValue = Province.EmptyValue;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.Cleared", col, row));
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetProvinceRef(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
        => TryConvertValue(data, out int value) && SetProvinceValue(col, row, value).Success;

    /// <summary>
    /// 直接写某个格子的省区值（0-65535，<c>0xFFFF</c> = 无省区）。
    /// <para>
    /// 脚本 / CLI 的省区写入走这里：和 <see cref="Remove"/> 一样改完调 <c>MarkModified()</c>，
    /// 保证界面刷新与保存一致。
    /// </para>
    /// </summary>
    public ModifierResult SetProvinceValue(int col, int row, int provinceValue)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();
        if (provinceValue < 0 || provinceValue > Province.EmptyValue)
            return ModifierResult.Fail(Loc.F("Module.Province.Msg.ValueOutOfRange", provinceValue));

        _mapData.GetProvinceRef(col, row).ProvinceValue = (ushort)provinceValue;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.ValueSet", col, row, provinceValue));
    }

    /// <summary>
    /// 把格子设为省会：省区值 = 自身索引。省区值表示不了的索引拒绝写入：
    /// 超过 65534（VB 版对 65536 取模，会悄悄指向一个毫不相干的格子），以及格子 0（省区值 0 按"无省区"处理）。
    /// </summary>
    public ModifierResult SetProvinceValueToIndex(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();

        int index = row * _mapData.MapWidth + col;
        if (!Province.CanBeCapitalIndex(index)) return CannotBeCapital(col, row, index);

        _mapData.GetProvinceRef(col, row).ProvinceValue = (ushort)index;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.IndexSet", col, row, index, index));
    }

    /// <summary>
    /// 用一组格子创建一个省区：省会取离这组格子几何中心最近的陆地格（没有陆地时取最近的格子），
    /// 所有格子的省区值都设为省会的索引。距离按渲染坐标（列距 30、行距 20√3）计算，
    /// 而不是 VB 那样直接拿行列号算直线距离。不能当省会的格子（见 <see cref="SetProvinceValueToIndex"/>）不参与挑选。
    /// </summary>
    public ModifierResult CreateProvinceFromHexes(IReadOnlyCollection<HexCoord> hexes)
    {
        if (_mapData == null) return NotInitialized();

        int width = _mapData.MapWidth;
        var cells = hexes.Where(h => IsValidCoord(h.Col, h.Row)).Distinct().ToList();
        if (cells.Count == 0) return ModifierResult.Fail(Loc.T("Module.Province.Msg.NoCells", "没有选中的格子"));

        double centerX = 0, centerY = 0;
        foreach (var hex in cells)
        {
            var (x, y) = HexCenter(hex.Col, hex.Row);
            centerX += x;
            centerY += y;
        }
        centerX /= cells.Count;
        centerY /= cells.Count;

        var eligible = cells.Where(h => Province.CanBeCapitalIndex(h.ToIndex(width))).ToList();
        if (eligible.Count == 0) return CannotBeCapital(cells[0].Col, cells[0].Row, cells[0].ToIndex(width));

        var land = eligible.Where(h => !IsSeaTerrain(h.ToIndex(width))).ToList();
        var capital = (land.Count > 0 ? land : eligible)
            .OrderBy(h => DistanceSquared(HexCenter(h.Col, h.Row), (centerX, centerY)))
            .ThenBy(h => h.Row).ThenBy(h => h.Col)
            .First();
        int capitalIndex = capital.ToIndex(width);

        foreach (var hex in cells)
            _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)capitalIndex;

        MarkModified();
        return ModifierResult.Ok(
            Loc.F("Module.Province.Msg.ProvinceCreated", capital.Col, capital.Row, capitalIndex, cells.Count),
            cells.Count);
    }

    #endregion

    #region 复制 / 粘贴（全部读写 MapClipboard：跨场景共享，画笔与洪水填充也从这里取值）

    /// <summary>剪贴板里复制的单个省区值（没复制过为 null）。</summary>
    public ushort? CopiedProvinceValue => MapClipboard.Instance.GlobalCopiedProvince?.ProvinceValue;

    /// <summary>剪贴板里是否有区域复制的数据（有则 V 键按区域粘贴）。</summary>
    public bool HasCopiedRegion => MapClipboard.Instance.GlobalCopiedProvinceHexes.Count > 0;

    /// <summary>复制单格省区值，同时清掉剪贴板里的区域数据。</summary>
    public ModifierResult CopyProvinceValue(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();

        var province = _mapData.GetProvinceRef(col, row);
        var clipboard = MapClipboard.Instance;
        clipboard.GlobalCopiedProvince = province;
        clipboard.GlobalCopiedProvinceFromCol = col;
        clipboard.GlobalCopiedProvinceFromRow = row;
        clipboard.GlobalCopiedProvinceHexes.Clear();
        _copiedRegionSourceWidth = 0;

        return ModifierResult.Ok(Loc.F("Module.Province.Msg.Copied", province.ProvinceValue));
    }

    /// <summary>
    /// 区域复制：按相对区域左上角 (minCol, minRow) 的偏移记下每格省区值。
    /// 单值剪贴板同时设为 <paramref name="focus"/> 格（不在区域里时取区域第一格）的值，供画笔与洪水填充使用。
    /// </summary>
    public ModifierResult CopyProvinceRegion(IReadOnlyCollection<HexCoord> cells, HexCoord focus)
    {
        if (_mapData == null) return NotInitialized();

        var valid = cells.Where(h => IsValidCoord(h.Col, h.Row)).Distinct().ToList();
        if (valid.Count == 0) return ModifierResult.Fail(Loc.T("Module.Province.Msg.NoCells", "没有选中的格子"));

        int minCol = valid.Min(h => h.Col), minRow = valid.Min(h => h.Row);

        var clipboard = MapClipboard.Instance;
        clipboard.GlobalCopiedProvinceHexes.Clear();
        foreach (var hex in valid)
            clipboard.GlobalCopiedProvinceHexes[(hex.Col - minCol, hex.Row - minRow)] = _mapData.GetProvinceRef(hex.Col, hex.Row);
        clipboard.GlobalCopiedProvinceRegionMinCol = minCol;
        clipboard.GlobalCopiedProvinceRegionMinRow = minRow;

        var single = valid.Contains(focus) ? focus : valid[0];
        clipboard.GlobalCopiedProvince = _mapData.GetProvinceRef(single.Col, single.Row);
        clipboard.GlobalCopiedProvinceFromCol = single.Col;
        clipboard.GlobalCopiedProvinceFromRow = single.Row;
        _copiedRegionSourceWidth = _mapData.MapWidth;

        return ModifierResult.Ok(Loc.F("Module.Province.Msg.RegionCopied", valid.Count), valid.Count);
    }

    /// <summary>把剪贴板里的单个省区值粘贴到格子。</summary>
    public ModifierResult PasteProvinceValue(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfRange();

        ushort? value = CopiedProvinceValue;
        if (!value.HasValue) return ModifierResult.Fail(NothingCopiedMessage());

        _mapData.GetProvinceRef(col, row).ProvinceValue = value.Value;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.Pasted", value.Value, col, row));
    }

    /// <summary>
    /// 按区域粘贴：复制区域的左上角 (minCol, minRow) 对齐到 (<paramref name="anchorCol"/>, <paramref name="anchorRow"/>)。
    /// <para>
    /// 与 VB 不同的两处（VB 的做法会出错）：
    /// 1) 平移在立方坐标里做，保持六边形形状 —— 奇数列整体下移半格，列偏移为奇数时直接加行列偏移会把形状错开半格；
    /// 2) 区域里自带的省会随区域一起移动 —— 省区值是省会格子的索引，原样粘贴会让新区域仍然指向原来的省会。
    ///    指向区域外省会的格子保持原值。
    /// </para>
    /// </summary>
    public ModifierResult PasteProvinceRegion(int anchorCol, int anchorRow)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(anchorCol, anchorRow)) return OutOfRange();

        var clipboard = MapClipboard.Instance;
        var region = clipboard.GlobalCopiedProvinceHexes;
        if (region.Count == 0) return ModifierResult.Fail(NothingCopiedMessage());

        int srcMinCol = clipboard.GlobalCopiedProvinceRegionMinCol;
        int srcMinRow = clipboard.GlobalCopiedProvinceRegionMinRow;
        int width = _mapData.MapWidth;

        var targets = new List<(HexCoord dst, ushort value)>(region.Count);
        var capitalMoves = new Dictionary<int, int>();    // 源省会索引 → 新位置的索引
        foreach (var ((relCol, relRow), province) in region)
        {
            int srcCol = srcMinCol + relCol;
            int srcRow = srcMinRow + relRow;
            var dst = TranslateHex(srcCol, srcRow, srcMinCol, srcMinRow, anchorCol, anchorRow);
            targets.Add((dst, province.ProvinceValue));

            if (_copiedRegionSourceWidth <= 0 || !province.IsValid) continue;
            int srcIndex = srcRow * _copiedRegionSourceWidth + srcCol;
            if (province.ProvinceValue != srcIndex || !IsValidCoord(dst.Col, dst.Row)) continue;

            int dstIndex = dst.ToIndex(width);
            if (Province.CanBeCapitalIndex(dstIndex))
                capitalMoves[srcIndex] = dstIndex;
        }

        int count = 0;
        foreach (var (dst, value) in targets)
        {
            if (!IsValidCoord(dst.Col, dst.Row)) continue;
            ushort written = capitalMoves.TryGetValue(value, out int moved) ? (ushort)moved : value;
            _mapData.GetProvinceRef(dst.Col, dst.Row).ProvinceValue = written;
            count++;
        }

        MarkModified();
        string message = capitalMoves.Count > 0
            ? Loc.F("Module.Province.Msg.RegionPastedWithCapitals", count, anchorCol, anchorRow, capitalMoves.Count)
            : Loc.F("Module.Province.Msg.RegionPasted", count, anchorCol, anchorRow);
        return ModifierResult.Ok(message, count);
    }

    /// <summary>
    /// 把格子 (col,row) 按"锚点 from → to"平移并保持六边形形状。
    /// 坐标是 odd-q 偏移坐标（奇数列下移半格），平移在立方坐标 x = col、z = row - floor(col / 2) 里做；
    /// 列偏移为偶数时结果与直接加行列偏移相同。
    /// </summary>
    private static HexCoord TranslateHex(int col, int row, int fromCol, int fromRow, int toCol, int toRow)
    {
        int dz = (toRow - (toCol >> 1)) - (fromRow - (fromCol >> 1));
        int newCol = col + (toCol - fromCol);
        int newZ = row - (col >> 1) + dz;
        return new HexCoord(newCol, newZ + (newCol >> 1));
    }

    private static string NothingCopiedMessage() => Loc.T("Module.Province.Msg.NothingCopied", "请先复制省份值（按C键）");

    #endregion

    #region 画笔

    public void IncreaseBrushRadius() => _brushEngine.IncreaseRadius();

    public void DecreaseBrushRadius() => _brushEngine.DecreaseRadius();

    /// <summary>用剪贴板里复制的省区值画一笔；没有复制过时不画（开画笔时模式会提示先复制）。</summary>
    public void PaintWithBrush(int centerCol, int centerRow) => Paint(centerCol, centerRow, null, true);

    /// <summary>
    /// 带地形遮罩的画笔：<paramref name="maskIncludeMode"/> 为 true 时只画在遮罩地形上，为 false 时跳过遮罩地形。
    /// </summary>
    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int> maskTerrainIds, bool maskIncludeMode)
        => Paint(centerCol, centerRow, maskTerrainIds, maskIncludeMode);

    private void Paint(int centerCol, int centerRow, HashSet<int>? maskTerrainIds, bool maskIncludeMode)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        ushort? value = CopiedProvinceValue;
        if (!value.HasValue) return;

        foreach (var (c, r) in _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight))
        {
            if (maskTerrainIds != null && maskTerrainIds.Contains(_mapData.GetTerrainRef(c, r).TileType1) != maskIncludeMode)
                continue;
            _mapData.GetProvinceRef(c, r).ProvinceValue = value.Value;
        }
        MarkModified();
    }

    #endregion

    #region 矩形 / 多边形选择工具（R / O 键）

    /// <summary>切换矩形选择工具，返回切换后是否开启（开启时关掉多边形工具）。画笔由模式负责关闭，GUI 要同步收起画笔窗口。</summary>
    public bool ToggleRectangleSelectMode() => ToggleSelectTool(ProvinceSelectTool.Rectangle);

    /// <summary>切换多边形选择工具，返回切换后是否开启（开启时关掉矩形工具）。</summary>
    public bool TogglePolygonSelectMode() => ToggleSelectTool(ProvinceSelectTool.Polygon);

    /// <summary>关闭选择工具并丢弃未完成的图形。</summary>
    public void DeactivateSelectTool()
    {
        _selectTool = ProvinceSelectTool.None;
        ResetShape();
    }

    /// <summary>丢弃未完成的图形（矩形起点、多边形顶点），返回是否确实丢弃了东西。</summary>
    public bool CancelShape()
    {
        bool hadShape = _rectangleAnchor.HasValue || _polygonVertices.Count > 0;
        ResetShape();
        return hadShape;
    }

    /// <summary>设置矩形选择的起点（终点由右键给出，选区交给 <c>HexSelector.SelectRange</c>）。</summary>
    public void SetRectangleAnchor(HexCoord anchor) => _rectangleAnchor = anchor;

    /// <summary>
    /// 多边形工具的一次左键：点回第一个顶点即闭合（同 <see cref="ClosePolygon"/>）；
    /// 否则加入顶点，返回已画折线经过的格子。
    /// </summary>
    public PolygonClick AddPolygonVertex(HexCoord hex)
    {
        if (_polygonVertices.Count > 0 && hex == _polygonVertices[^1])
            return new PolygonClick(PolygonClickKind.Ignored, GetPolylineHexes());

        if (_polygonVertices.Count > 0 && hex == _polygonVertices[0])
            return CloseShape();

        _polygonVertices.Add(hex);
        return new PolygonClick(PolygonClickKind.VertexAdded, GetPolylineHexes());
    }

    /// <summary>
    /// 闭合进行中的多边形（Enter 键）：至少 3 个顶点时返回多边形覆盖的格子并清空顶点，
    /// 否则返回 <see cref="PolygonClickKind.TooFewVertices"/>。没有进行中的多边形时返回 null。
    /// </summary>
    public PolygonClick? ClosePolygon() => _polygonVertices.Count == 0 ? null : CloseShape();

    private PolygonClick CloseShape()
    {
        if (_polygonVertices.Count < 3)
            return new PolygonClick(PolygonClickKind.TooFewVertices, GetPolylineHexes());

        var filled = GetHexesInPolygon(_polygonVertices, _mapData?.MapWidth ?? 0, _mapData?.MapHeight ?? 0);
        ResetShape();
        return new PolygonClick(PolygonClickKind.Closed, filled);
    }

    private List<HexCoord> GetPolylineHexes()
        => GetHexesOnPolyline(_polygonVertices, false, _mapData?.MapWidth ?? 0, _mapData?.MapHeight ?? 0);

    private bool ToggleSelectTool(ProvinceSelectTool tool)
    {
        _selectTool = _selectTool == tool ? ProvinceSelectTool.None : tool;
        ResetShape();
        return _selectTool == tool;
    }

    private void ResetShape()
    {
        _rectangleAnchor = null;
        _polygonVertices.Clear();
    }

    /// <summary>
    /// 多边形覆盖的格子：格子中心落在多边形内（奇偶规则），再加上边线经过的格子 ——
    /// 顶点本身就是格子中心，正好压在边上，不补边线的话顶点格会时有时无。
    /// </summary>
    private static List<HexCoord> GetHexesInPolygon(IReadOnlyList<HexCoord> vertices, int mapWidth, int mapHeight)
    {
        var points = vertices.Select(v => HexCenter(v.Col, v.Row)).ToArray();
        var result = new HashSet<HexCoord>(GetHexesOnPolyline(vertices, true, mapWidth, mapHeight));

        int minCol = Math.Max(0, vertices.Min(v => v.Col));
        int maxCol = Math.Min(mapWidth - 1, vertices.Max(v => v.Col));
        int minRow = Math.Max(0, vertices.Min(v => v.Row) - 1);
        int maxRow = Math.Min(mapHeight - 1, vertices.Max(v => v.Row) + 1);

        for (int col = minCol; col <= maxCol; col++)
        {
            for (int row = minRow; row <= maxRow; row++)
            {
                if (IsPointInPolygon(HexCenter(col, row), points))
                    result.Add(new HexCoord(col, row));
            }
        }

        return result.OrderBy(h => h.Row).ThenBy(h => h.Col).ToList();
    }

    /// <summary>折线（<paramref name="closed"/> 时首尾相连）经过的格子：沿每条边按 1/4 列距取样，取样点落在哪格就收哪格。</summary>
    private static List<HexCoord> GetHexesOnPolyline(IReadOnlyList<HexCoord> vertices, bool closed, int mapWidth, int mapHeight)
    {
        var result = new List<HexCoord>();
        var seen = new HashSet<HexCoord>();

        void Add(HexCoord hex)
        {
            if (hex.Col < 0 || hex.Row < 0 || hex.Col >= mapWidth || hex.Row >= mapHeight) return;
            if (seen.Add(hex)) result.Add(hex);
        }

        if (vertices.Count == 0) return result;
        Add(vertices[0]);

        int edges = closed ? vertices.Count : vertices.Count - 1;
        double step = Camera.HexHorizontalSpacing / 4;
        for (int i = 0; i < edges; i++)
        {
            var (x0, y0) = HexCenter(vertices[i].Col, vertices[i].Row);
            var next = vertices[(i + 1) % vertices.Count];
            var (x1, y1) = HexCenter(next.Col, next.Row);

            int samples = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(DistanceSquared((x0, y0), (x1, y1))) / step));
            for (int s = 1; s <= samples; s++)
            {
                double t = (double)s / samples;
                Add(WorldToHex(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t));
            }
        }

        return result;
    }

    /// <summary>奇偶规则判断点是否在多边形内。</summary>
    private static bool IsPointInPolygon((double x, double y) point, (double x, double y)[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > point.y) != (yj > point.y) &&
                point.x < (xj - xi) * (point.y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// 世界坐标（未缩放）落在哪个格子：取最近的格子中心。规则六边形网格里"最近的中心"就是"所在的格子"；
    /// 列距 30 而格子半宽 20，最近的中心一定在左右相邻的两列里。
    /// </summary>
    private static HexCoord WorldToHex(double x, double y)
    {
        int leftCol = (int)Math.Floor(x / Camera.HexHorizontalSpacing);
        HexCoord best = default;
        double bestDistance = double.MaxValue;

        for (int col = leftCol; col <= leftCol + 1; col++)
        {
            double offset = (col & 1) == 1 ? Camera.HexVerticalSpacing / 2 : 0;
            int row = (int)Math.Round((y - offset) / Camera.HexVerticalSpacing);
            double distance = DistanceSquared((x, y), HexCenter(col, row));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new HexCoord(col, row);
            }
        }

        return best;
    }

    #endregion

    #region 洪水填充

    /// <summary>用剪贴板里复制的省区值做洪水填充（F 键）。</summary>
    public ModifierResult FloodFill(int startCol, int startRow)
    {
        ushort? value = CopiedProvinceValue;
        if (!value.HasValue)
            return ModifierResult.Fail(Loc.T("Module.Province.Msg.FloodNeedsCopy", "请先按C键复制一个省份值，然后再按F键进行洪水填充"));
        return FloodFill(startCol, startRow, value.Value);
    }

    /// <summary>
    /// 洪水填充：从起点出发，把"省区值与起点相同、且与起点同为海洋或同为非海洋"的连通格子都设为 <paramref name="fillValue"/>。
    /// </summary>
    public ModifierResult FloodFill(int startCol, int startRow, ushort fillValue)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(startCol, startRow)) return OutOfRange();

        ushort startProvinceValue = _mapData.GetProvinceRef(startCol, startRow).ProvinceValue;
        byte startTerrainType = _mapData.GetTerrainRef(startCol, startRow).TileType1;

        if (startProvinceValue == fillValue)
            return ModifierResult.Ok(Loc.T("Module.Province.Msg.FloodSame", "起始格子省份值与填充值相同，无需填充"), 0);

        var queue = new Queue<(int col, int row)>();
        var visited = new HashSet<(int, int)>();
        int filledCount = 0;

        queue.Enqueue((startCol, startRow));
        visited.Add((startCol, startRow));

        while (queue.Count > 0)
        {
            var (col, row) = queue.Dequeue();

            ref var province = ref _mapData.GetProvinceRef(col, row);
            if (province.ProvinceValue != startProvinceValue ||
                !IsSameTerrainCategory(startTerrainType, _mapData.GetTerrainRef(col, row).TileType1))
                continue;

            province.ProvinceValue = fillValue;
            filledCount++;

            foreach (var (nc, nr) in GetHexNeighbors(col, row))
            {
                if (InMap(nc, nr) && visited.Add((nc, nr)))
                    queue.Enqueue((nc, nr));
            }
        }

        MarkModified();
        if (filledCount == 0)
            return ModifierResult.Ok(Loc.T("Module.Province.Msg.FloodNone", "洪水填充完成，没有填充任何格子"), 0);

        string category = startTerrainType == 1
            ? Loc.T("Module.Province.Terrain.Sea", "海洋")
            : Loc.T("Module.Province.Terrain.Land", "非海洋");
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.FloodDone", filledCount, category), filledCount);
    }

    #endregion

    #region 省区生成

    /// <summary>
    /// 按【手绘边界】生成省区（省份编辑模式 U 键画完后的 P 键）。
    /// <para>
    /// 与 <see cref="GenerateProvincesFromBackground"/> 并列的另一条入口：
    /// 界线不由图片提取，而是直接用用户描的线。适合图片界线识别不准的地图。
    /// </para>
    /// </summary>
    /// <param name="boundaryMask">手绘边界掩码，按地图逻辑像素组织</param>
    /// <param name="dilateCells">边界格向外加厚的格数，用于堵住线条在格子边缘留下的缺口</param>
    public ModifierResult GenerateProvincesFromDrawnBoundary(
        bool[] boundaryMask,
        int maskWidth,
        int maskHeight,
        int dilateCells = 1)
    {
        if (_mapData == null) return NotInitialized();

        var result = DrawnBoundaryProvinceGenerator.Generate(
            _mapData, boundaryMask, maskWidth, maskHeight, dilateCells);

        if (result == null)
            return ModifierResult.Fail(Loc.T("Module.Province.Msg.DrawnBoundaryFailed",
                "没有可用的手绘边界或省会：请先用 U 键描出省界，并确保地图上已有省会"));

        var r = result.Value;
        MarkModified();

        return ModifierResult.Ok(
            Loc.F("Module.Province.Msg.DrawnBoundaryDone",
                r.BoundaryCells, r.RegionCount, r.CapitalCount, r.FilledRegions, r.FilledCells, r.SkippedRegions),
            r.FilledCells);
    }

    /// <summary>
    /// 从背景图识别省区（省份编辑模式 P 键）。
    /// <para>
    /// 把背景图灰度化以突出行政边界，先在<b>像素级</b>按界线切出连通区域，
    /// 再把像素区域映射到格子成组，每个区域内若有省会格子，就整片填成该省会的格子索引；
    /// 没有省会的区域保持原样不填（由 G/E/I 等后续命令补齐）。
    /// </para>
    /// </summary>
    /// <param name="debugSink">
    /// 诊断用：识别时把灰度图与界线掩码回传给调用方导出成图片人工核对（控制台 bg_province 调参时用），平时传 null。
    /// </param>
    public ModifierResult GenerateProvincesFromBackground(
        IViewLayerImageProvider provider,
        int darkThreshold = BackgroundProvinceGenerator.AutoDarkThreshold,
        int gradientThreshold = BackgroundProvinceGenerator.DefaultGradientThreshold,
        int minRegionPixels = BackgroundProvinceGenerator.DefaultMinRegionPixels,
        int minBoundaryPixels = BackgroundProvinceGenerator.DefaultMinBoundaryPixels,
        Action<GrayscaleImage, bool[], int>? debugSink = null)
    {
        if (_mapData == null) return NotInitialized();
        if (provider == null || !provider.HasImage)
            return ModifierResult.Fail(Loc.T("Module.Province.Msg.NoBackground", "没有可用的背景图：请先加载视图层图片"));

        // 省会既可能来自 Capitals 列表（军团编辑模式 F 键），
        // 也可能是"省区值 == 自身索引"的格子（省份编辑模式 X 键），统一交给生成器识别。
        var result = BackgroundProvinceGenerator.Generate(
            _mapData, provider, darkThreshold, gradientThreshold,
            minRegionPixels, minBoundaryPixels, debugSink);
        if (result == null)
            return ModifierResult.Fail(Loc.T("Module.Province.Msg.BackgroundFailed",
                "没有可用的省会，或背景图尺寸无效：请先设置省会并加载视图层图片"));

        var r = result.Value;
        MarkModified();

        string message = Loc.F("Module.Province.Msg.BackgroundDone",
            r.RegionCount, r.CapitalCount, r.FilledRegions, r.FilledCells, r.SkippedRegions,
            r.PixelRegionCount, r.BoundaryCells);

        // 剔除了多少地名/噪点像素，是判断该不该调 minBoundaryPixels 的直接依据
        if (r.RemovedBoundaryPixels > 0)
            message += Loc.F("Module.Province.Msg.BackgroundRemovedNoise", r.RemovedBoundaryPixels, r.MinBoundaryPixels);

        // 诊断信息：阈值是否落在合理位置、界线有没有抓到，光看区域数是判断不出来的
        string thresholdKind = r.ThresholdIsAuto
            ? Loc.T("Module.Province.Msg.ThresholdAuto", "（自动）")
            : Loc.T("Module.Province.Msg.ThresholdManual", "（手动）");
        message += Loc.F("Module.Province.Msg.BackgroundDiagnostics",
            r.Threshold, thresholdKind, r.GradientThreshold, r.DarkRatio, r.BoundaryRatio, r.GrayMin, r.GrayMean, r.GrayMax);

        if (r.PixelRegionCount == 0)
            message += Loc.T("Module.Province.Msg.BackgroundNoRegion", "。没有识别到任何区域：请确认背景图已作为视图层加载，或调低灰度阈值");

        return ModifierResult.Ok(message, r.FilledCells);
    }

    public ModifierResult ClearAllProvinces()
    {
        if (_mapData == null) return NotInitialized();

        int total = _mapData.MapWidth * _mapData.MapHeight;
        for (int index = 0; index < total; index++)
            _mapData.GetProvinceRef(index).ProvinceValue = Province.EmptyValue;

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.ClearedAll", total), total);
    }

    /// <summary>为孤立省会（四周没有同省区格子的省会）同时向外生长出省区（G 键）。</summary>
    public ModifierResult GenerateProvincesForIsolatedCapitals()
    {
        if (_mapData == null) return NotInitialized();

        var isolatedCapitals = FindIsolatedCapitals();
        if (isolatedCapitals.Count == 0)
            return ModifierResult.Ok(Loc.T("Module.Province.Msg.NoIsolatedCapital", "没有找到孤立的省会"), 0);

        GenerateProvincesSimultaneously(isolatedCapitals);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.IsolatedGenerated", isolatedCapitals.Count), isolatedCapitals.Count);
    }

    /// <summary>孤立省会：省区值等于自身索引、但六个邻格都不属于该省区的格子（按行列顺序）。</summary>
    private List<(HexCoord hex, int provinceValue)> FindIsolatedCapitals()
    {
        var isolated = new List<(HexCoord hex, int provinceValue)>();
        int mapWidth = _mapData!.MapWidth;
        int mapHeight = _mapData.MapHeight;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                if (!IsCapitalCell(index)) continue;

                bool hasSameProvince = GetHexNeighbors(col, row)
                    .Any(n => InMap(n.col, n.row) && _mapData.GetProvinceRef(n.col, n.row).ProvinceValue == index);
                if (!hasSameProvince)
                    isolated.Add((new HexCoord(col, row), index));
            }
        }

        return isolated;
    }

    private void GenerateProvincesSimultaneously(List<(HexCoord hex, int provinceValue)> isolatedCapitals)
    {
        if (_mapData == null) return;

        int maxRadius = 15;

        var capitalRings = new Dictionary<int, HashSet<HexCoord>>();
        var capitalFilled = new Dictionary<int, HashSet<HexCoord>>();

        foreach (var (hex, provinceValue) in isolatedCapitals)
        {
            capitalRings[provinceValue] = new HashSet<HexCoord> { hex };
            capitalFilled[provinceValue] = new HashSet<HexCoord> { hex };
            _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)provinceValue;
        }

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            var expansionCandidates = new Dictionary<HexCoord, List<int>>();

            foreach (var (_, provinceValue) in isolatedCapitals)
            {
                var currentRing = capitalRings[provinceValue];
                var filled = capitalFilled[provinceValue];

                foreach (var ringHex in currentRing)
                {
                    foreach (var (nc, nr) in GetHexNeighbors(ringHex.Col, ringHex.Row))
                    {
                        if (!InMap(nc, nr)) continue;
                        var neighbor = new HexCoord(nc, nr);
                        if (filled.Contains(neighbor)) continue;
                        if (IsSeaTerrain(nr * _mapData.MapWidth + nc)) continue;
                        if (_mapData.GetProvinceRef(nc, nr).IsValid) continue;

                        if (!expansionCandidates.TryGetValue(neighbor, out var competitors))
                            expansionCandidates[neighbor] = competitors = new List<int>();
                        if (!competitors.Contains(provinceValue))
                            competitors.Add(provinceValue);
                    }
                }
            }

            if (expansionCandidates.Count == 0) break;

            foreach (var (hex, competingProvinces) in expansionCandidates)
            {
                int winnerProvince = competingProvinces.Count == 1
                    ? competingProvinces[0]
                    : competingProvinces[_random.Next(competingProvinces.Count)];

                double breakProbability = Math.Min(0.1 + (radius - 1) * 0.03, 0.5);
                double noiseValue = _random.NextDouble();
                var winnerCapital = capitalFilled[winnerProvince].First();
                double angleNoise = GetAngleNoise(winnerCapital.Col, winnerCapital.Row, hex.Col, hex.Row, radius);
                double combinedNoise = noiseValue * 0.5 + angleNoise * 0.5;

                if (combinedNoise > breakProbability)
                {
                    _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)winnerProvince;
                    capitalFilled[winnerProvince].Add(hex);
                }
            }

            capitalRings = BuildExpansionRings(isolatedCapitals, capitalFilled, null);
        }
    }

    /// <summary>
    /// 下一轮的扩展环：每个省区里还挨着空白陆地格的格子。<paramref name="allowed"/> 不为 null 时只看集合内的邻格。
    /// </summary>
    private Dictionary<int, HashSet<HexCoord>> BuildExpansionRings(
        List<(HexCoord hex, int provinceValue)> capitals,
        Dictionary<int, HashSet<HexCoord>> capitalFilled,
        HashSet<HexCoord>? allowed)
    {
        var rings = new Dictionary<int, HashSet<HexCoord>>();
        foreach (var (_, provinceValue) in capitals)
        {
            var filled = capitalFilled[provinceValue];
            var ring = new HashSet<HexCoord>();
            foreach (var hex in filled)
            {
                foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
                {
                    if (!InMap(nc, nr)) continue;
                    var neighbor = new HexCoord(nc, nr);
                    if (filled.Contains(neighbor)) continue;
                    if (allowed != null && !allowed.Contains(neighbor)) continue;
                    if (IsSeaTerrain(nr * _mapData!.MapWidth + nc)) continue;
                    if (!_mapData.GetProvinceRef(nc, nr).IsValid)
                    {
                        ring.Add(hex);
                        break;
                    }
                }
            }
            rings[provinceValue] = ring;
        }
        return rings;
    }

    /// <summary>
    /// 扩展所有省区填满地图（E 键）：先让各省区沿边界向外生长（空白陆地格归离它最近的省会），
    /// 再把仍未连通的空白陆地格直接分给最近的省会。距离按渲染坐标计算（列距 30、行距 20√3）。
    /// </summary>
    public ModifierResult ExpandAllProvincesToFillMap()
    {
        if (_mapData == null) return NotInitialized();

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        var assigned = new bool[mapHeight, mapWidth];
        for (int row = 0; row < mapHeight; row++)
            for (int col = 0; col < mapWidth; col++)
                assigned[row, col] = _mapData.GetProvinceRef(col, row).IsValid;

        var provinceCapitals = new Dictionary<int, HexCoord>();
        for (int index = 0; index < mapWidth * mapHeight; index++)
        {
            if (IsCapitalCell(index))
                provinceCapitals[index] = HexCoord.FromIndex(index, mapWidth);
        }

        int ClosestCapital(int col, int row)
        {
            var center = HexCenter(col, row);
            double minDistance = double.MaxValue;
            int closest = -1;
            foreach (var (provinceValue, capital) in provinceCapitals)
            {
                double distance = DistanceSquared(center, HexCenter(capital.Col, capital.Row));
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = provinceValue;
                }
            }
            return closest;
        }

        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (var (provinceValue, capital) in provinceCapitals)
            {
                foreach (var hex in FindProvinceHexes(capital, provinceValue))
                {
                    foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
                    {
                        if (!InMap(nc, nr) || assigned[nr, nc]) continue;
                        if (IsSeaTerrain(nr * mapWidth + nc)) continue;
                        if (ClosestCapital(nc, nr) != provinceValue) continue;

                        _mapData.GetProvinceRef(nc, nr).ProvinceValue = (ushort)provinceValue;
                        assigned[nr, nc] = true;
                        changed = true;
                    }
                }
            }
        }

        int additionalAssigned = 0;
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                if (assigned[row, col] || IsSeaTerrain(row * mapWidth + col)) continue;

                int closestProvince = ClosestCapital(col, row);
                if (closestProvince < 0) continue;

                _mapData.GetProvinceRef(col, row).ProvinceValue = (ushort)closestProvince;
                assigned[row, col] = true;
                additionalAssigned++;
            }
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Province.Msg.Expanded", additionalAssigned), additionalAssigned);
    }

    /// <summary>
    /// 处理孤立和空白省区（I 键），依次三步：
    /// 1) 省会无归属的省区被分成了几块时，与省会不连通的每一块整体并入它接壤最多的相邻省区；
    /// 2) 孤立省区格（四周没有同省区的格子，省会格除外）随机并入一个相邻省区；
    /// 3) 空白陆地格随机取一个相邻省区。
    /// </summary>
    public ModifierResult ProcessIsolatedAndEmptyProvinces()
    {
        if (_mapData == null) return NotInitialized();

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;
        int invalidCount = MergeDetachedPartsOfUnownedProvinces();
        int isolatedCount = 0;
        int emptyCount = 0;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ref var province = ref _mapData.GetProvinceRef(index);
                if (!province.IsValid || province.ProvinceValue == index) continue;

                ushort provinceValue = province.ProvinceValue;
                bool hasSameProvince = false;
                var neighborProvinces = new List<ushort>();
                foreach (var (nc, nr) in GetHexNeighbors(col, row))
                {
                    if (!InMap(nc, nr)) continue;
                    var neighbor = _mapData.GetProvinceRef(nc, nr);
                    if (neighbor.ProvinceValue == provinceValue)
                    {
                        hasSameProvince = true;
                        break;
                    }
                    if (neighbor.IsValid)
                        neighborProvinces.Add(neighbor.ProvinceValue);
                }

                if (!hasSameProvince && neighborProvinces.Count > 0)
                {
                    province.ProvinceValue = neighborProvinces[_random.Next(neighborProvinces.Count)];
                    isolatedCount++;
                }
            }
        }

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                ref var province = ref _mapData.GetProvinceRef(index);
                if (province.IsValid || IsSeaTerrain(index)) continue;

                var neighborProvinces = new List<ushort>();
                foreach (var (nc, nr) in GetHexNeighbors(col, row))
                {
                    if (!InMap(nc, nr)) continue;
                    var neighbor = _mapData.GetProvinceRef(nc, nr);
                    if (neighbor.IsValid)
                        neighborProvinces.Add(neighbor.ProvinceValue);
                }

                if (neighborProvinces.Count > 0)
                {
                    province.ProvinceValue = neighborProvinces[_random.Next(neighborProvinces.Count)];
                    emptyCount++;
                }
            }
        }

        MarkModified();
        return ModifierResult.Ok(
            Loc.F("Module.Province.Msg.Processed", invalidCount, isolatedCount, emptyCount),
            invalidCount + isolatedCount + emptyCount);
    }

    /// <summary>
    /// 无效省区处理：省会归属为 0xFF 的省区若被分成了几块，与省会不连通的每一块整体并入它接壤最多的相邻省区
    /// （平手时取省区值小的，结果稳定）。与省会连通的部分（含省会本身）保持不变；没有相邻省区的块保持原样。
    /// 返回改动的格子数。
    /// </summary>
    private int MergeDetachedPartsOfUnownedProvinces()
    {
        int mapWidth = _mapData!.MapWidth;
        int total = mapWidth * _mapData.MapHeight;

        var cellsByProvince = new Dictionary<int, List<int>>();
        for (int index = 0; index < total; index++)
        {
            var province = _mapData.GetProvinceRef(index);
            if (!province.IsValid) continue;
            if (!cellsByProvince.TryGetValue(province.ProvinceValue, out var list))
                cellsByProvince[province.ProvinceValue] = list = new List<int>();
            list.Add(index);
        }

        int merged = 0;
        foreach (var (provinceValue, cells) in cellsByProvince)
        {
            if (provinceValue >= total || !IsCapitalCell(provinceValue)) continue;

            var capital = HexCoord.FromIndex(provinceValue, mapWidth);
            if (_mapData.GetBelongValue(capital.Col, capital.Row) != 0xFF) continue;

            var handled = new HashSet<HexCoord>(FindProvinceHexes(capital, provinceValue));
            if (handled.Count == cells.Count) continue;

            foreach (int index in cells)
            {
                var start = HexCoord.FromIndex(index, mapWidth);
                if (handled.Contains(start)) continue;

                var part = FindProvinceHexes(start, provinceValue);
                handled.UnionWith(part);

                var borderCounts = new Dictionary<ushort, int>();
                foreach (var hex in part)
                {
                    foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
                    {
                        if (!InMap(nc, nr)) continue;
                        var neighbor = _mapData.GetProvinceRef(nc, nr);
                        if (!neighbor.IsValid || neighbor.ProvinceValue == provinceValue) continue;
                        borderCounts[neighbor.ProvinceValue] = borderCounts.GetValueOrDefault(neighbor.ProvinceValue) + 1;
                    }
                }

                if (borderCounts.Count == 0) continue;

                ushort target = borderCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                foreach (var hex in part)
                    _mapData.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = target;
                merged += part.Count;
            }
        }

        return merged;
    }

    /// <summary>
    /// 分析所有孤立省会（周围没有同省区格子的省会），找出每个孤立省会邻居中占比最大的包围省区，
    /// 以原省区格子集合为单位，在原范围内加入孤立省会后重新平分，不改变省区边界。
    /// </summary>
    public ModifierResult RebalanceIsolatedCapitals()
    {
        if (_mapData == null) return NotInitialized();

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        var isolatedCapitals = FindIsolatedCapitals();
        if (isolatedCapitals.Count == 0)
            return ModifierResult.Ok(Loc.T("Module.Province.Msg.NoIsolatedCapitalToRebalance", "没有找到孤立省会"), 0);

        var surroundingMap = new Dictionary<int, int>();
        foreach (var (hex, provinceValue) in isolatedCapitals)
        {
            var provinceCount = new Dictionary<int, int>();
            foreach (var (nc, nr) in GetHexNeighbors(hex.Col, hex.Row))
            {
                if (!InMap(nc, nr)) continue;
                var neighbor = _mapData.GetProvinceRef(nc, nr);
                if (!neighbor.IsValid || neighbor.ProvinceValue == provinceValue) continue;
                provinceCount[neighbor.ProvinceValue] = provinceCount.GetValueOrDefault(neighbor.ProvinceValue) + 1;
            }

            if (provinceCount.Count == 0) continue;

            surroundingMap[provinceValue] = provinceCount.OrderByDescending(k => k.Value).ThenBy(k => k.Key).First().Key;
        }

        if (surroundingMap.Count == 0)
            return ModifierResult.Ok(Loc.F("Module.Province.Msg.NoSurroundingProvince", isolatedCapitals.Count), 0);

        var groups = new Dictionary<int, List<(HexCoord hex, int provinceValue)>>();
        foreach (var (hex, provinceValue) in isolatedCapitals)
        {
            if (!surroundingMap.TryGetValue(provinceValue, out int surrounding)) continue;
            if (!groups.TryGetValue(surrounding, out var members))
                groups[surrounding] = members = new List<(HexCoord, int)>();
            members.Add((hex, provinceValue));
        }

        int totalRebalanced = 0;
        int totalGroups = 0;

        foreach (var (surroundingProvince, isolatedInGroup) in groups)
        {
            int capCol = surroundingProvince % mapWidth;
            int capRow = surroundingProvince / mapWidth;
            if (!InMap(capCol, capRow)) continue;
            if (_mapData.GetProvinceRef(capCol, capRow).ProvinceValue != surroundingProvince) continue;

            var surroundingCapital = new HexCoord(capCol, capRow);
            var hexSet = new HashSet<HexCoord>(FindProvinceHexes(surroundingCapital, surroundingProvince));
            foreach (var (isoHex, _) in isolatedInGroup)
                hexSet.Add(isoHex);

            var capitals = new List<(HexCoord hex, int provinceValue)> { (surroundingCapital, surroundingProvince) };
            capitals.AddRange(isolatedInGroup);

            totalRebalanced += RebalanceWithinHexes(capitals, hexSet, mapWidth, mapHeight);
            totalGroups++;
        }

        MarkModified();
        return ModifierResult.Ok(
            Loc.F("Module.Province.Msg.Rebalanced", isolatedCapitals.Count, totalGroups, totalRebalanced),
            totalRebalanced);
    }

    /// <summary>
    /// 在指定格子集合内做多源环形扩展重新平分，不改变集合外的格子。
    /// </summary>
    private int RebalanceWithinHexes(
        List<(HexCoord hex, int provinceValue)> capitals,
        HashSet<HexCoord> hexSet,
        int mapWidth, int mapHeight)
    {
        var capitalSet = new HashSet<HexCoord>(capitals.Select(c => c.hex));

        int cleared = 0;
        foreach (var hex in hexSet)
        {
            if (capitalSet.Contains(hex)) continue;
            ref var province = ref _mapData!.GetProvinceRef(hex.Col, hex.Row);
            if (province.ProvinceValue != Province.EmptyValue)
            {
                province.ProvinceValue = Province.EmptyValue;
                cleared++;
            }
        }

        var capitalFilled = new Dictionary<int, HashSet<HexCoord>>();
        var capitalRings = new Dictionary<int, HashSet<HexCoord>>();

        foreach (var (hex, provinceValue) in capitals)
        {
            _mapData!.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)provinceValue;
            capitalFilled[provinceValue] = new HashSet<HexCoord> { hex };
            capitalRings[provinceValue] = new HashSet<HexCoord> { hex };
        }

        int maxRadius = Math.Max(mapWidth, mapHeight);

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            var expansionCandidates = new Dictionary<HexCoord, List<int>>();

            foreach (var (_, provinceValue) in capitals)
            {
                var filled = capitalFilled[provinceValue];
                foreach (var ringHex in capitalRings[provinceValue])
                {
                    foreach (var (nc, nr) in GetHexNeighbors(ringHex.Col, ringHex.Row))
                    {
                        if (!InMap(nc, nr)) continue;
                        var neighbor = new HexCoord(nc, nr);
                        if (filled.Contains(neighbor) || !hexSet.Contains(neighbor)) continue;
                        if (IsSeaTerrain(nr * mapWidth + nc)) continue;
                        if (_mapData!.GetProvinceRef(nc, nr).IsValid) continue;

                        if (!expansionCandidates.TryGetValue(neighbor, out var competitors))
                            expansionCandidates[neighbor] = competitors = new List<int>();
                        if (!competitors.Contains(provinceValue))
                            competitors.Add(provinceValue);
                    }
                }
            }

            if (expansionCandidates.Count == 0) break;

            foreach (var (hex, competing) in expansionCandidates)
            {
                int winner = competing.Count == 1 ? competing[0] : competing[_random.Next(competing.Count)];

                double breakProbability = Math.Min(0.1 + (radius - 1) * 0.03, 0.5);
                double noiseValue = _random.NextDouble();
                var winnerCapital = capitalFilled[winner].First();
                double angleNoise = GetAngleNoise(winnerCapital.Col, winnerCapital.Row, hex.Col, hex.Row, radius);
                double combinedNoise = noiseValue * 0.5 + angleNoise * 0.5;

                if (combinedNoise > breakProbability)
                {
                    _mapData!.GetProvinceRef(hex.Col, hex.Row).ProvinceValue = (ushort)winner;
                    capitalFilled[winner].Add(hex);
                }
            }

            capitalRings = BuildExpansionRings(capitals, capitalFilled, hexSet);
        }

        return cleared;
    }

    #endregion

    #region 辅助

    private static bool TryConvertValue(object value, out int provinceValue)
    {
        switch (value)
        {
            case Province p: provinceValue = p.ProvinceValue; return true;
            case ushort u: provinceValue = u; return true;
            case int i: provinceValue = i; return true;
            case byte b: provinceValue = b; return true;
            default: provinceValue = 0; return false;
        }
    }

    private static ModifierResult NotInitialized()
        => ModifierResult.Fail(Loc.T("Module.Province.Msg.NotInitialized", "地图数据未初始化"));

    private static ModifierResult OutOfRange()
        => ModifierResult.Fail(Loc.T("Module.Province.Msg.OutOfRange", "坐标超出范围"));

    private static ModifierResult CannotBeCapital(int col, int row, int index)
        => ModifierResult.Fail(index > 0
            ? Loc.F("Module.Province.Msg.IndexTooLarge", col, row, index)
            : Loc.F("Module.Province.Msg.IndexZero", col, row));

    private bool InMap(int col, int row)
        => col >= 0 && row >= 0 && col < _mapData!.MapWidth && row < _mapData.MapHeight;

    /// <summary>该格是省会：省区有效且省区值等于自身索引。</summary>
    private bool IsCapitalCell(int index)
    {
        var province = _mapData!.GetProvinceRef(index);
        return province.IsValid && province.ProvinceValue == index;
    }

    /// <summary>
    /// 六边形邻格（可能越界，调用方自行判断）。奇数列整体下移半格，所以按<b>列号</b>奇偶取斜向邻格 ——
    /// 以前按格子序号 (row * 宽 + col) 的奇偶取，在宽度为奇数的地图上奇数行全部取错。
    /// </summary>
    private static IEnumerable<(int col, int row)> GetHexNeighbors(int col, int row)
    {
        foreach (var (dc, dr) in (col & 1) == 0 ? EvenColNeighborOffsets : OddColNeighborOffsets)
            yield return (col + dc, row + dr);
    }

    private bool IsSeaTerrain(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.MapWidth * _mapData.MapHeight) return false;
        return _mapData.GetTerrainRef(index).TileType1 == 1;
    }

    private static bool IsSameTerrainCategory(byte type1, byte type2) => (type1 == 1) == (type2 == 1);

    /// <summary>格子中心的世界坐标（未缩放，与渲染层一致：列距 30，行距 20√3，奇数列下移半格）。</summary>
    private static (double x, double y) HexCenter(int col, int row)
        => (col * Camera.HexHorizontalSpacing,
            row * Camera.HexVerticalSpacing + ((col & 1) == 1 ? Camera.HexVerticalSpacing / 2 : 0));

    private static double DistanceSquared((double x, double y) a, (double x, double y) b)
    {
        double dx = a.x - b.x;
        double dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    private static double GetAngleNoise(int centerCol, int centerRow, int x, int y, int seed)
    {
        double dx = x - centerCol;
        double dy = y - centerRow;
        double angle = Math.Atan2(dy, dx);
        if (angle < 0) angle += 2 * Math.PI;

        int angleSector = (int)(angle / (Math.PI / 3)) % 6;
        var rng = new Random(seed ^ (angleSector * 12345));
        return rng.NextDouble();
    }

    /// <summary>从 <paramref name="start"/> 出发、省区值为 <paramref name="provinceValue"/> 的连通格子（六邻接）。</summary>
    private List<HexCoord> FindProvinceHexes(HexCoord start, int provinceValue)
    {
        var result = new List<HexCoord>();
        var queue = new Queue<HexCoord>();
        var visited = new HashSet<HexCoord> { start };
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!InMap(current.Col, current.Row)) continue;
            if (_mapData!.GetProvinceRef(current.Col, current.Row).ProvinceValue != provinceValue) continue;

            result.Add(current);
            foreach (var (nc, nr) in GetHexNeighbors(current.Col, current.Row))
            {
                var neighbor = new HexCoord(nc, nr);
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return result;
    }

    #endregion
}
