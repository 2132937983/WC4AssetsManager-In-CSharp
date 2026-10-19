using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

// 填充与批量生成：洪水填充、绿化、随机平地 / 变体、创建海岸线（F4）、处理海洋第二层（F5）。
// 批量操作的范围参数为 null 时作用于全图，传入选区时只处理选区（对齐 VB：有框选就只改框选）。
public sealed partial class TerrainModifier
{
    /// <summary>绿化使用的地形：阔叶林(16)、针叶林(20)、矮绿山(9)。</summary>
    private static readonly byte[] GreenTerrainTypes = [16, 20, 9];

    /// <summary>官方地图里"该层没有内容"的写法：类型 63、变体 255。</summary>
    private const byte EmptyLayerType = 63;
    private const byte EmptyLayerDecoration = 255;

    /// <summary>海岸线贴图所在的第二层地形类型。</summary>
    private const byte CoastLayerType = 31;

    private int _greeningValue = 50;

    /// <summary>上一次使用的绿化值（0-100），U 键对话框以它为默认值（对齐 VB）。</summary>
    public int GreeningValue
    {
        get => _greeningValue;
        set => _greeningValue = Math.Clamp(value, 0, 100);
    }

    #region 洪水填充

    /// <summary>
    /// 从起点向相连的同类地形（当前编辑层）扩散填充。
    /// 填充内容按优先级：显式给出的 <paramref name="replacementType"/>（命令行 / 脚本）→ 复制的地形
    /// （类型、变体与贴图偏移）→ 按 VB 规则在海洋与平地之间互转。
    /// </summary>
    public ModifierResult FloodFill(int startCol, int startRow, byte? replacementType = null)
    {
        if (_mapData == null) return NoMapResult();
        if (!IsValidCoord(startCol, startRow)) return OutOfRangeResult();

        byte startType = GetTileTypeByLayer(_mapData.GetTerrainRef(startCol, startRow), _editLayer);
        TerrainData? copied = replacementType == null && _hasCopiedTerrain ? _copiedTerrain : null;

        byte targetType;
        string direction;
        if (replacementType is byte explicitType)
        {
            targetType = explicitType;
            direction = Loc.F("Module.Terrain.Fill.Explicit", _editLayer, ConfigManager.Instance.GetTerrainTypeName(explicitType));
        }
        else if (copied is TerrainData source)
        {
            targetType = GetTileTypeByLayer(source, _editLayer);
            direction = Loc.F("Module.Terrain.Fill.Copied", _editLayer);
        }
        else if (startType == OceanType)
        {
            targetType = 0;
            direction = Loc.F("Module.Terrain.Fill.OceanToFlat", _editLayer);
        }
        else
        {
            targetType = OceanType;
            direction = Loc.F("Module.Terrain.Fill.ToOcean", _editLayer);
        }

        var (filled, changed) = FloodFillRegion(startCol, startRow, startType, targetType, copied);
        if (filled == 0) return ModifierResult.Ok(Loc.T("Module.Terrain.Fill.Nothing"), 0);

        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.Fill.Done", filled, direction), changed);
    }

    /// <summary>BFS 填充与起点同类型的相连格子；返回（填充格数，真正改动的格数）。</summary>
    private (int filled, int changed) FloodFillRegion(int startCol, int startRow, byte startType, byte targetType, TerrainData? copied)
    {
        var visited = new HashSet<(int, int)> { (startCol, startRow) };
        var queue = new Queue<(int col, int row)>();
        queue.Enqueue((startCol, startRow));

        int filled = 0, changed = 0;
        while (queue.Count > 0)
        {
            var (col, row) = queue.Dequeue();
            ref TerrainData terrain = ref _mapData!.GetTerrainRef(col, row);
            if (GetTileTypeByLayer(terrain, _editLayer) != startType) continue;

            var before = terrain;
            if (copied is TerrainData source)
                CopyLayer(ref terrain, source, _editLayer);
            else
            {
                SetTileTypeByLayer(ref terrain, _editLayer, targetType);
                SetDecorationTypeByLayer(ref terrain, _editLayer, 0);
            }

            filled++;
            if (!terrain.Equals(before)) changed++;

            foreach (var (nc, nr) in GetHexNeighbors(col, row))
            {
                if (IsValidCoord(nc, nr) && visited.Add((nc, nr)))
                    queue.Enqueue((nc, nr));
            }
        }
        return (filled, changed);
    }

    /// <summary>把来源格某一层的类型、变体与贴图偏移复制过来（VB 用复制地形填充时连偏移一起复制）。</summary>
    private static void CopyLayer(ref TerrainData target, TerrainData source, int layer)
    {
        switch (layer)
        {
            case 1:
                target.TileType1 = source.TileType1;
                target.DecorationType1 = source.DecorationType1;
                target.TextureOffsetX1 = source.TextureOffsetX1;
                target.TextureOffsetY1 = source.TextureOffsetY1;
                break;
            case 2:
                target.TileType2 = source.TileType2;
                target.DecorationType2 = source.DecorationType2;
                target.TextureOffsetX2 = source.TextureOffsetX2;
                target.TextureOffsetY2 = source.TextureOffsetY2;
                break;
            case 3:
                target.TileType3 = source.TileType3;
                target.DecorationType3 = source.DecorationType3;
                target.TextureOffsetX3 = source.TextureOffsetX3;
                target.TextureOffsetY3 = source.TextureOffsetY3;
                break;
        }
    }

    #endregion

    #region 绿化 / 随机

    /// <summary>把当前编辑层的平地按概率改成森林或山地（随机变体）。</summary>
    public ModifierResult ApplyGreening(int greeningValue, IEnumerable<HexCoord>? targetHexes = null)
    {
        if (_mapData == null) return NoMapResult();
        greeningValue = Math.Clamp(greeningValue, 0, 100);

        double probability = greeningValue / 100.0;
        var rand = new Random();
        int flatCount = 0, convertedCount = 0;

        foreach (var (col, row) in GetTargetHexes(targetHexes))
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            if (GetTileTypeByLayer(terrain, _editLayer) != 0) continue;

            flatCount++;
            if (rand.NextDouble() >= probability) continue;

            byte newType = GreenTerrainTypes[rand.Next(GreenTerrainTypes.Length)];
            SetTileTypeByLayer(ref terrain, _editLayer, newType);

            // 第一层变成陆地时把第二层重置为空（VB 行为）；只在编辑第一层时这样做，免得动到别的层
            if (_editLayer == 1)
            {
                terrain.TileType2 = EmptyLayerType;
                terrain.DecorationType2 = EmptyLayerDecoration;
            }

            SetDecorationTypeByLayer(ref terrain, _editLayer, RandomVariant(newType, rand));
            convertedCount++;
        }

        if (convertedCount > 0) MarkModified();
        double rate = flatCount > 0 ? convertedCount * 100.0 / flatCount : 0;
        return ModifierResult.Ok(
            Loc.F("Module.Terrain.Greening", _editLayer, convertedCount, flatCount, rate, ScopeText(targetHexes != null)),
            convertedCount);
    }

    /// <summary>把当前编辑层的平地按概率改成随机地形（海洋与平地除外，随机变体）。</summary>
    public ModifierResult RandomizeFlatTerrain(int probability, IEnumerable<HexCoord>? targetHexes = null)
    {
        if (_mapData == null) return NoMapResult();
        probability = Math.Clamp(probability, 0, 100);

        var availableTypes = ConfigManager.Instance.GetTerrainTypes().Keys.Where(k => k > OceanType).Select(k => (byte)k).ToArray();
        if (availableTypes.Length == 0) return ModifierResult.Fail(Loc.T("Module.Terrain.NoTerrainTypes"));

        double prob = probability / 100.0;
        var rand = new Random();
        int totalCount = 0, modifiedCount = 0;

        foreach (var (col, row) in GetTargetHexes(targetHexes))
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            if (GetTileTypeByLayer(terrain, _editLayer) != 0) continue;

            totalCount++;
            if (rand.NextDouble() >= prob) continue;

            byte newType = availableTypes[rand.Next(availableTypes.Length)];
            SetTileTypeByLayer(ref terrain, _editLayer, newType);
            SetDecorationTypeByLayer(ref terrain, _editLayer, RandomVariant(newType, rand));
            modifiedCount++;
        }

        if (modifiedCount > 0) MarkModified();
        double rate = totalCount > 0 ? modifiedCount * 100.0 / totalCount : 0;
        return ModifierResult.Ok(
            Loc.F("Module.Terrain.RandomFlat", modifiedCount, totalCount, rate, ScopeText(targetHexes != null)),
            modifiedCount);
    }

    /// <summary>按概率随机当前编辑层的变体（平地与海洋没有变体，跳过）。</summary>
    public ModifierResult RandomizeVariant(int probability, IEnumerable<HexCoord>? targetHexes = null)
    {
        if (_mapData == null) return NoMapResult();
        probability = Math.Clamp(probability, 0, 100);

        double prob = probability / 100.0;
        var rand = new Random();
        int totalCount = 0, rolledCount = 0, changedCount = 0;

        foreach (var (col, row) in GetTargetHexes(targetHexes))
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            byte currentType = GetTileTypeByLayer(terrain, _editLayer);
            if (currentType == 0 || currentType == OceanType) continue;

            totalCount++;
            if (rand.NextDouble() >= prob) continue;

            int variantCount = ConfigManager.Instance.GetTerrainVariantCount(currentType);
            if (variantCount <= 0) continue;

            // 消息里的修改数按"被抽中重新随机"的格子算（与概率对应）；随机到原变体的格子不算地图改动
            byte before = GetDecorationTypeByLayer(terrain, _editLayer);
            byte newVariant = (byte)rand.Next(variantCount);
            SetDecorationTypeByLayer(ref terrain, _editLayer, newVariant);
            rolledCount++;
            if (newVariant != before) changedCount++;
        }

        if (changedCount > 0) MarkModified();
        double rate = totalCount > 0 ? rolledCount * 100.0 / totalCount : 0;
        return ModifierResult.Ok(
            Loc.F("Module.Terrain.RandomVariant", _editLayer, rolledCount, totalCount, rate, ScopeText(targetHexes != null)),
            changedCount);
    }

    private static byte RandomVariant(byte terrainType, Random rand)
    {
        int variantCount = ConfigManager.Instance.GetTerrainVariantCount(terrainType);
        return variantCount > 0 ? (byte)rand.Next(variantCount) : (byte)0;
    }

    /// <summary>批量操作的目标格：给了选区就用选区（过滤掉越界的格子，例如缩小地图后残留的选区），否则全图。</summary>
    private List<(int col, int row)> GetTargetHexes(IEnumerable<HexCoord>? targetHexes)
    {
        var result = new List<(int col, int row)>();
        if (_mapData == null) return result;

        if (targetHexes != null)
        {
            foreach (var cell in targetHexes)
            {
                if (IsValidCoord(cell.Col, cell.Row)) result.Add((cell.Col, cell.Row));
            }
            return result;
        }

        for (int row = 0; row < _mapData.MapHeight; row++)
            for (int col = 0; col < _mapData.MapWidth; col++)
                result.Add((col, row));
        return result;
    }

    #endregion

    #region F4 创建海岸线 / F5 处理海洋第二层

    /// <summary>
    /// 创建海岸线：陆地的空第二层补成 63/255；海洋的第二层按相邻陆地的方向写海岸贴图（类型 31）。
    /// </summary>
    public ModifierResult CreateCoast(IEnumerable<HexCoord>? targetHexes = null)
    {
        if (_mapData == null) return NoMapResult();

        int landCount = 0, oceanCount = 0;
        foreach (var (col, row) in GetTargetHexes(targetHexes))
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            var before = terrain;

            if (terrain.TileType1 != OceanType)
            {
                if (terrain.TileType2 == 0)
                {
                    terrain.TileType2 = EmptyLayerType;
                    terrain.DecorationType2 = EmptyLayerDecoration;
                }
                if (!terrain.Equals(before)) landCount++;
                continue;
            }

            terrain.TileType2 = CoastLayerType;
            terrain.DecorationType2 = CoastDecoration(GetLandNeighborMask(col, row));
            if (!terrain.Equals(before)) oceanCount++;
        }

        int changed = landCount + oceanCount;
        if (changed > 0) MarkModified();
        return ModifierResult.Ok(
            Loc.F("Module.Terrain.CoastCreated", ScopeText(targetHexes != null), landCount, oceanCount),
            changed);
    }

    /// <summary>
    /// 海岸贴图编号：按"哪些方向是陆地"的 6 位掩码（位 0 上、1 右上、2 右下、3 下、4 左下、5 左上）取
    /// 74 - 掩码；四周全是海时用 10。这与 VB 按方向组合逐条列出的 64 种情况完全一致（渲染层 CoastMaskProcessor 同表）。
    /// </summary>
    private static byte CoastDecoration(int landMask) => landMask == 0 ? (byte)10 : (byte)(74 - landMask);

    /// <summary>六个方向中非海洋邻格的掩码。横向越界时绕到另一侧（世界地图左右相接），纵向越界的方向不计。</summary>
    private int GetLandNeighborMask(int col, int row)
    {
        bool evenColumn = (col & 1) == 0;
        ReadOnlySpan<int> dx = [0, 1, 1, 0, -1, -1];
        ReadOnlySpan<int> evenDy = [-1, -1, 0, 1, 0, -1];
        ReadOnlySpan<int> oddDy = [-1, 0, 1, 1, 1, 0];

        int mask = 0;
        for (int i = 0; i < 6; i++)
        {
            int nc = col + dx[i];
            int nr = row + (evenColumn ? evenDy[i] : oddDy[i]);
            if (nc < 0) nc = _mapData!.MapWidth - 1;
            else if (nc >= _mapData!.MapWidth) nc = 0;
            if (nr < 0 || nr >= _mapData.MapHeight) continue;

            if (_mapData.GetTerrainRef(nc, nr).TileType1 != OceanType) mask |= 1 << i;
        }
        return mask;
    }

    /// <summary>处理海洋第二层：海洋格子的第二层（非 0 时）统一重置为空（63/255）。</summary>
    public ModifierResult ProcessOceanSecondLayer(IEnumerable<HexCoord>? targetHexes = null)
    {
        if (_mapData == null) return NoMapResult();

        int changed = 0;
        foreach (var (col, row) in GetTargetHexes(targetHexes))
        {
            ref var terrain = ref _mapData.GetTerrainRef(col, row);
            if (terrain.TileType1 != OceanType || terrain.TileType2 == 0) continue;
            if (terrain.TileType2 == EmptyLayerType && terrain.DecorationType2 == EmptyLayerDecoration) continue;

            terrain.TileType2 = EmptyLayerType;
            terrain.DecorationType2 = EmptyLayerDecoration;
            changed++;
        }

        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.OceanLayer2", ScopeText(targetHexes != null), changed), changed);
    }

    #endregion
}
