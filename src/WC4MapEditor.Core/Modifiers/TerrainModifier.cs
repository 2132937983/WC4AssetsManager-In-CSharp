using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 地形模块：格子地形的读写、复制粘贴、类型 / 变体切换、河流与编辑层，供地形绘制模式、命令行与脚本复用。
/// <para>
/// 其余主题按文件拆分：画笔（TerrainModifier.Brush）、填充 / 批量生成 / 海岸线（.Batch）、
/// 地图缩放与单边扩缩（.MapSize）、建筑连路（.Roads）、区域移动 / 识别结果写入 / HD 导出（.Tools）。
/// </para>
/// <para>
/// 返回的 <see cref="ModifierResult.AffectedCount"/> 是真正改动的格子数：0 表示地图没有变化，
/// 模式据此决定要不要通知重绘。
/// </para>
/// </summary>
public sealed partial class TerrainModifier : ModifierBase, IBrushTarget
{
    public override string Name => "terrain";
    public override string DisplayName => "地形修改器";

    /// <summary>海洋的第一层地形类型。</summary>
    private const byte OceanType = 1;

    private TerrainData _copiedTerrain;
    private bool _hasCopiedTerrain;
    private Dictionary<(int col, int row), TerrainData>? _copiedTerrainGroup;
    private (int col, int row) _copyAnchor;
    private int _editLayer = 1;

    private readonly TerrainRecognizer _terrainRecognizer = new();

    public TerrainRecognizer TerrainRecognizer => _terrainRecognizer;

    public int EditLayer
    {
        get => _editLayer;
        set => _editLayer = Math.Clamp(value, 1, 3);
    }

    public TerrainData? GetCopiedTerrainData() => _hasCopiedTerrain ? _copiedTerrain : null;
    public Dictionary<(int col, int row), TerrainData>? GetCopiedTerrainGroup() => _copiedTerrainGroup;
    public (int col, int row) GetCopyAnchor() => _copyAnchor;

    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        _terrainRecognizer.UpdateMapData(mapData);
    }

    /// <summary>从全局剪贴板接收单格复制内容（会清掉本地的区域复制）。</summary>
    public void SetCopiedTerrainData(TerrainData data, int anchorCol, int anchorRow)
    {
        _copiedTerrain = data;
        _hasCopiedTerrain = true;
        _copiedTerrainGroup = null;
        _copyAnchor = (anchorCol, anchorRow);
    }

    /// <summary>从全局剪贴板接收区域复制内容（键是复制时的绝对坐标，锚点是区域左上角）。</summary>
    public void SetCopiedTerrainGroup(Dictionary<(int col, int row), TerrainData> group, int minCol, int minRow)
    {
        _copiedTerrainGroup = new Dictionary<(int, int), TerrainData>(group);
        _copyAnchor = (minCol, minRow);
        _hasCopiedTerrain = true;
        if (group.TryGetValue((minCol, minRow), out var terrain))
            _copiedTerrain = terrain;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NoMapResult();
        if (!IsValidCoord(col, row)) return OutOfRangeResult();

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);

        switch (parameter)
        {
            case TerrainData td:
                terrain = td;
                MarkModified();
                return ModifierResult.Ok(Loc.F("Module.Terrain.Set", col, row));

            case Terrain t:
                terrain = t.ToTerrainData();
                MarkModified();
                return ModifierResult.Ok(Loc.F("Module.Terrain.Set", col, row));

            case TerrainBrushInfo brushInfo:
                ApplyBrush(ref terrain, brushInfo.Layer, brushInfo.TerrainType, brushInfo.Decoration);
                MarkModified();
                return ModifierResult.Ok(Loc.F("Module.Terrain.BrushApplied", col, row));

            default:
                return ModifierResult.Fail(Loc.T("Module.Terrain.UnknownParameter"));
        }
    }

    /// <summary>
    /// 重置当前编辑层（类型与变体归零）。只动当前层：整格替换会让 Delete 把三层地形一起清掉，
    /// 与"按层编辑"的预期不符。
    /// </summary>
    public override ModifierResult Remove(int col, int row) => RemoveRange(new[] { new HexCoord(col, row) });

    /// <summary>批量重置当前编辑层（Delete 键对整个框选生效）。</summary>
    public override ModifierResult RemoveRange(IEnumerable<HexCoord> coords)
    {
        if (_mapData == null) return NoMapResult();

        int total = 0, changed = 0;
        HexCoord first = default;
        foreach (var cell in coords)
        {
            if (!IsValidCoord(cell.Col, cell.Row)) continue;
            if (total++ == 0) first = cell;

            ref var terrain = ref _mapData.GetTerrainRef(cell.Col, cell.Row);
            var before = terrain;
            SetTileTypeByLayer(ref terrain, _editLayer, 0);
            SetDecorationTypeByLayer(ref terrain, _editLayer, 0);
            if (!terrain.Equals(before)) changed++;
        }

        if (total == 0) return OutOfRangeResult();
        if (changed > 0) MarkModified();
        string message = total == 1
            ? Loc.F("Module.Terrain.LayerReset", _editLayer, first.Col, first.Row)
            : Loc.F("Module.Terrain.LayerResetCells", total, _editLayer);
        return ModifierResult.Ok(message, changed);
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetTerrainRef(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        ref TerrainData terrain = ref _mapData.GetTerrainRef(col, row);

        switch (data)
        {
            case TerrainData td:
                terrain = td;
                break;
            case Terrain t:
                terrain = t.ToTerrainData();
                break;
            default:
                return false;
        }

        MarkModified();
        return true;
    }

    #region 复制 / 粘贴

    public ModifierResult CopyTerrain(int col, int row)
    {
        if (!IsValidCoord(col, row)) return OutOfRangeResult();
        _copiedTerrain = _mapData!.GetTerrainRef(col, row);
        _hasCopiedTerrain = true;
        _copiedTerrainGroup = null;
        _copyAnchor = (col, row);
        return ModifierResult.Ok(Loc.F("Module.Terrain.Copied", col, row));
    }

    public ModifierResult CopyTerrainGroup(IEnumerable<HexCoord> coords)
    {
        if (_mapData == null) return NoMapResult();

        var group = new Dictionary<(int, int), TerrainData>();
        int minCol = int.MaxValue, minRow = int.MaxValue;
        foreach (var cell in coords)
        {
            if (!IsValidCoord(cell.Col, cell.Row)) continue;
            group[(cell.Col, cell.Row)] = _mapData.GetTerrainRef(cell.Col, cell.Row);
            minCol = Math.Min(minCol, cell.Col);
            minRow = Math.Min(minRow, cell.Row);
        }

        if (group.Count == 0) return ModifierResult.Fail(Loc.T("Module.Terrain.NothingToCopy"));

        _copiedTerrainGroup = group;
        _copyAnchor = (minCol, minRow);
        _hasCopiedTerrain = true;
        _copiedTerrain = group.TryGetValue((minCol, minRow), out var corner) ? corner : group.First().Value;
        return ModifierResult.Ok(Loc.F("Module.Terrain.CopiedGroup", group.Count, minCol, minRow), group.Count);
    }

    /// <summary>
    /// 粘贴。复制的是区域时，以 (<paramref name="col"/>, <paramref name="row"/>) 为区域左上角整体粘贴；
    /// 复制的是单格时，粘到 <paramref name="targets"/> 的每一格（不给就是该格本身）。
    /// <para>
    /// 区域按六边形立方坐标平移：奇数列下移半格，直接加列偏移会让奇数列偏移的区域错开半格、河流断开（VB 的问题）。
    /// 粘贴后把河流同步到区域外的相邻格子（河流画在两格共用的边上）。
    /// </para>
    /// </summary>
    public ModifierResult PasteTerrain(int col, int row, IEnumerable<HexCoord>? targets = null)
    {
        if (_mapData == null) return NoMapResult();
        if (!_hasCopiedTerrain) return ModifierResult.Fail(Loc.T("Module.Terrain.NothingCopied"));
        if (!IsValidCoord(col, row)) return OutOfRangeResult();

        var writes = new Dictionary<(int col, int row), TerrainData>();
        bool isRegion = _copiedTerrainGroup is { Count: > 1 };
        if (isRegion)
        {
            foreach (var (source, terrain) in _copiedTerrainGroup!)
            {
                var dest = TranslateHex(source, _copyAnchor, (col, row));
                if (IsValidCoord(dest.col, dest.row)) writes[dest] = terrain;
            }
        }
        else
        {
            foreach (var cell in targets ?? new[] { new HexCoord(col, row) })
            {
                if (IsValidCoord(cell.Col, cell.Row)) writes[(cell.Col, cell.Row)] = _copiedTerrain;
            }
        }

        int changed = WriteCellsWithRiverSync(writes);
        if (changed > 0) MarkModified();
        string message = isRegion || writes.Count > 1
            ? Loc.F("Module.Terrain.PastedGroup", writes.Count)
            : Loc.F("Module.Terrain.Pasted", col, row);
        return ModifierResult.Ok(message, changed);
    }

    /// <summary>
    /// 整体写入一组格子，再把河流变化同步到这组格子以外的邻格。
    /// 组内格子保持写入值：它们来自同一次复制 / 移动，彼此的河流边本来就一致。返回真正改动的格子数（含邻格）。
    /// </summary>
    private int WriteCellsWithRiverSync(IReadOnlyDictionary<(int col, int row), TerrainData> writes)
    {
        var oldRivers = new Dictionary<(int col, int row), byte>(writes.Count);
        int changed = 0;
        foreach (var (cell, terrain) in writes)
        {
            ref var target = ref _mapData!.GetTerrainRef(cell.col, cell.row);
            oldRivers[cell] = target.RiverValue;
            if (!target.Equals(terrain)) changed++;
            target = terrain;
        }

        var written = new HashSet<(int col, int row)>(writes.Keys);
        foreach (var (cell, terrain) in writes)
            changed += SyncRiverValueToNeighbors(cell.col, cell.row, terrain.RiverValue, oldRivers[cell], written);
        return changed;
    }

    #endregion

    #region 类型 / 变体切换

    public ModifierResult ChangeTerrainType(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return OutOfRangeResult();
        return ChangeTerrainType(new[] { new HexCoord(col, row) }, delta);
    }

    /// <summary>按配置里的地形类型顺序循环切换当前编辑层的类型（变体归零），每格从自己的当前类型出发。</summary>
    public ModifierResult ChangeTerrainType(IEnumerable<HexCoord> cells, int delta)
    {
        if (_mapData == null) return NoMapResult();

        int[] availableTypes = ConfigManager.Instance.GetTerrainTypes().Keys.Order().ToArray();
        var outcome = new CycleOutcome();
        foreach (var cell in cells)
        {
            if (!IsValidCoord(cell.Col, cell.Row)) continue;
            ref TerrainData terrain = ref _mapData.GetTerrainRef(cell.Col, cell.Row);
            var before = terrain;
            int newType = NextTerrainType(availableTypes, GetTileTypeByLayer(terrain, _editLayer), delta);
            SetTileTypeByLayer(ref terrain, _editLayer, (byte)newType);
            SetDecorationTypeByLayer(ref terrain, _editLayer, 0);
            outcome.Add(newType, !terrain.Equals(before));
        }

        if (outcome.Total == 0) return OutOfRangeResult();
        if (outcome.Changed > 0) MarkModified();

        string message;
        if (outcome.Mixed)
            message = Loc.F("Module.Terrain.TypeChangedMixed", outcome.Total, _editLayer);
        else
        {
            string typeName = ConfigManager.Instance.GetTerrainTypeName(outcome.Value);
            message = outcome.Total == 1
                ? Loc.F("Module.Terrain.TypeChanged", _editLayer, typeName)
                : Loc.F("Module.Terrain.TypeChangedCells", outcome.Total, _editLayer, typeName);
        }
        return ModifierResult.Ok(message, outcome.Changed);
    }

    private static int NextTerrainType(int[] availableTypes, byte currentType, int delta)
    {
        if (availableTypes.Length == 0)
        {
            int raw = currentType + delta;
            if (raw < 0) return byte.MaxValue;
            return raw > byte.MaxValue ? 0 : raw;
        }

        int currentIndex = Math.Max(0, Array.IndexOf(availableTypes, (int)currentType));
        int newIndex = currentIndex + delta;
        if (newIndex < 0) newIndex = availableTypes.Length - 1;
        else if (newIndex >= availableTypes.Length) newIndex = 0;
        return availableTypes[newIndex];
    }

    public ModifierResult ChangeDecoration(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return OutOfRangeResult();
        return ChangeDecoration(new[] { new HexCoord(col, row) }, delta);
    }

    /// <summary>在该地形类型的实际变体数内循环切换当前编辑层的变体（VB 不管类型一律按 16 个循环）。</summary>
    public ModifierResult ChangeDecoration(IEnumerable<HexCoord> cells, int delta)
    {
        if (_mapData == null) return NoMapResult();

        var outcome = new CycleOutcome();
        foreach (var cell in cells)
        {
            if (!IsValidCoord(cell.Col, cell.Row)) continue;
            ref TerrainData terrain = ref _mapData.GetTerrainRef(cell.Col, cell.Row);
            byte currentType = GetTileTypeByLayer(terrain, _editLayer);
            byte currentDeco = GetDecorationTypeByLayer(terrain, _editLayer);

            int maxDeco = Math.Max(0, ConfigManager.Instance.GetTerrainVariantCount(currentType) - 1);
            int newDeco = currentDeco + delta;
            if (newDeco < 0) newDeco = maxDeco;
            if (newDeco > maxDeco) newDeco = 0;

            SetDecorationTypeByLayer(ref terrain, _editLayer, (byte)newDeco);
            outcome.Add(newDeco, newDeco != currentDeco);
        }

        if (outcome.Total == 0) return OutOfRangeResult();
        if (outcome.Changed > 0) MarkModified();

        string message = outcome.Mixed
            ? Loc.F("Module.Terrain.VariantChangedMixed", outcome.Total, _editLayer)
            : outcome.Total == 1
                ? Loc.F("Module.Terrain.VariantChanged", _editLayer, outcome.Value)
                : Loc.F("Module.Terrain.VariantChangedCells", outcome.Total, _editLayer, outcome.Value);
        return ModifierResult.Ok(message, outcome.Changed);
    }

    /// <summary>批量循环切换的统计：处理格数、真正改动的格数，以及结果值是否一致（一致时消息里给出具体值）。</summary>
    private struct CycleOutcome
    {
        public int Total;
        public int Changed;
        public int Value;
        public bool Mixed;

        public void Add(int value, bool changed)
        {
            if (Total == 0) Value = value;
            else if (value != Value) Mixed = true;
            Total++;
            if (changed) Changed++;
        }
    }

    #endregion

    #region 河流

    public ModifierResult SetRiverValue(int col, int row, byte value)
    {
        if (_mapData == null) return NoMapResult();
        if (!IsValidCoord(col, row)) return OutOfRangeResult();

        byte oldValue = _mapData.GetTerrainRef(col, row).RiverValue;
        _mapData.GetTerrainRef(col, row).RiverValue = value;
        int changed = (oldValue != value ? 1 : 0) + SyncRiverValueToNeighbors(col, row, value, oldValue);

        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.RiverSet", value), changed);
    }

    /// <summary>
    /// 把一格河流值的变化同步到相邻格子：河流画在两格共用的边上，一边有河、邻格的对边也必须有。
    /// <paramref name="fixedCells"/> 里的格子（同一次操作整体写入的区域）保持写入值，不被反向改写。
    /// 返回被改动的邻格数。
    /// </summary>
    private int SyncRiverValueToNeighbors(int col, int row, byte newRiverValue, byte oldRiverValue,
        IReadOnlySet<(int col, int row)>? fixedCells = null)
    {
        if (_mapData == null) return 0;

        int changed = 0;
        for (int edgeIndex = 0; edgeIndex < 6; edgeIndex++)
        {
            bool oldHasRiver = (oldRiverValue & (1 << edgeIndex)) != 0;
            bool newHasRiver = (newRiverValue & (1 << edgeIndex)) != 0;
            if (oldHasRiver == newHasRiver) continue;

            var (neighborCol, neighborRow) = GetRiverNeighbor(col, row, edgeIndex);
            if (!IsValidCoord(neighborCol, neighborRow)) continue;
            if (fixedCells != null && fixedCells.Contains((neighborCol, neighborRow))) continue;

            ref var neighbor = ref _mapData.GetTerrainRef(neighborCol, neighborRow);
            int oppositeBit = 1 << GetOppositeEdge(edgeIndex);
            byte updated = newHasRiver
                ? (byte)(neighbor.RiverValue | oppositeBit)
                : (byte)(neighbor.RiverValue & ~oppositeBit);
            if (updated == neighbor.RiverValue) continue;

            neighbor.RiverValue = updated;
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// 河流边对应的邻格。边序号：0 上、1 右上、2 右下、3 下、4 左下、5 左上。
    /// 地图是奇数列下移半格的 flat-top 六边形，邻格要按<b>列</b>的奇偶取；
    /// VB 按线性序号的奇偶取，宽度为奇数的地图在奇数行会同步到错误的邻格。
    /// </summary>
    private static (int col, int row) GetRiverNeighbor(int col, int row, int edgeIndex)
    {
        bool evenColumn = (col & 1) == 0;
        return edgeIndex switch
        {
            0 => (col, row - 1),
            1 => evenColumn ? (col + 1, row - 1) : (col + 1, row),
            2 => evenColumn ? (col + 1, row) : (col + 1, row + 1),
            3 => (col, row + 1),
            4 => evenColumn ? (col - 1, row) : (col - 1, row + 1),
            5 => evenColumn ? (col - 1, row - 1) : (col - 1, row),
            _ => (col, row)
        };
    }

    /// <summary>对边序号：0↔3、1↔4、2↔5。</summary>
    private static int GetOppositeEdge(int edgeIndex) => (edgeIndex + 3) % 6;

    #endregion

    #region 公共工具

    private static byte GetTileTypeByLayer(TerrainData terrain, int layer) => layer switch
    {
        2 => terrain.TileType2,
        3 => terrain.TileType3,
        _ => terrain.TileType1
    };

    private static byte GetDecorationTypeByLayer(TerrainData terrain, int layer) => layer switch
    {
        2 => terrain.DecorationType2,
        3 => terrain.DecorationType3,
        _ => terrain.DecorationType1
    };

    private static void SetTileTypeByLayer(ref TerrainData terrain, int layer, byte value)
    {
        switch (layer)
        {
            case 1: terrain.TileType1 = value; break;
            case 2: terrain.TileType2 = value; break;
            case 3: terrain.TileType3 = value; break;
        }
    }

    private static void SetDecorationTypeByLayer(ref TerrainData terrain, int layer, byte value)
    {
        switch (layer)
        {
            case 1: terrain.DecorationType1 = value; break;
            case 2: terrain.DecorationType2 = value; break;
            case 3: terrain.DecorationType3 = value; break;
        }
    }

    /// <summary>六个邻格（奇数列下移半格的 odd-q 布局，不做边界检查）。</summary>
    private static (int col, int row)[] GetHexNeighbors(int col, int row) => (col & 1) == 0
        ? [(col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1), (col - 1, row - 1), (col + 1, row - 1)]
        : [(col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1), (col - 1, row + 1), (col + 1, row + 1)];

    /// <summary>
    /// 把格子按"锚点 → 目标"的位移平移，经六边形立方坐标换算。
    /// 偶数列位移与直接加偏移完全相同；奇数列位移时与锚点不同奇偶的列会多 / 少移半行，
    /// 这样整块区域的形状保持不变（直接加偏移会让它错开半格）。
    /// </summary>
    public static (int col, int row) TranslateHex((int col, int row) cell, (int col, int row) anchor, (int col, int row) target)
    {
        int dx = target.col - anchor.col;
        int dz = (target.row - (target.col >> 1)) - (anchor.row - (anchor.col >> 1));
        int newCol = cell.col + dx;
        int z = cell.row - (cell.col >> 1) + dz;
        return (newCol, z + (newCol >> 1));
    }

    private static ModifierResult NoMapResult() => ModifierResult.Fail(Loc.T("Module.Terrain.NoMap"));

    private static ModifierResult OutOfRangeResult() => ModifierResult.Fail(Loc.T("Module.Terrain.OutOfRange"));

    /// <summary>批量操作的范围说明：传入了选区时为"选中区域"，否则为"全图"。</summary>
    private static string ScopeText(bool hasRegion)
        => hasRegion ? Loc.T("Module.Terrain.ScopeSelection") : Loc.T("Module.Terrain.ScopeMap");

    #endregion
}

public sealed class TerrainBrushInfo
{
    public int Layer { get; init; } = 1;
    public int TerrainType { get; init; }
    public int Decoration { get; init; }
}
