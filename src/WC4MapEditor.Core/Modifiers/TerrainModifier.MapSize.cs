using System.IO;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>单边扩缩地图的方向。</summary>
public enum MapResizeDirection
{
    Up,
    Down,
    Left,
    Right
}

// 地图尺寸：按比例缩放（G 键）与单边扩缩（I/J/K/L 键）。
// 两者都重映射省份、归属与所有带格子坐标的实体（建筑、单位 v1/v3、陷阱、援军 v1/v3、空军、首都、单位部署、方案目标格）。
public sealed partial class TerrainModifier
{
    /// <summary>
    /// 是否为依附世界地图的 BTL（征服 / 世界底图地图）：文件头带地图编号或数据窗口（MapClip）。
    /// 这类地图的格子与世界地图逐格对齐，缩放 / 扩缩会破坏对齐，地形也不随文件保存，所以尺寸调整一律拒绝。
    /// 世界地图 .bin 与新建的空地图（文件头为默认值、没有 .btl 路径）不受影响。
    /// </summary>
    public bool IsWorldBasedMap => _mapData != null
        && string.Equals(Path.GetExtension(_mapData.FilePath), ".btl", StringComparison.OrdinalIgnoreCase)
        && (_mapData.Header.MapNumber > 0 || _mapData.Header.MapClipX != 0 || _mapData.Header.MapClipY != 0);

    /// <summary>解析命令行里的方向参数（up / down / left / right，不分大小写）。</summary>
    public static bool TryParseDirection(string? text, out MapResizeDirection direction)
        => Enum.TryParse(text, ignoreCase: true, out direction) && Enum.IsDefined(direction);

    /// <summary>方向的显示名（"向上" 等）。</summary>
    public static string DirectionName(MapResizeDirection direction) => direction switch
    {
        MapResizeDirection.Up => Loc.T("Module.Terrain.Dir.Up"),
        MapResizeDirection.Down => Loc.T("Module.Terrain.Dir.Down"),
        MapResizeDirection.Left => Loc.T("Module.Terrain.Dir.Left"),
        _ => Loc.T("Module.Terrain.Dir.Right")
    };

    #region 撤销记录

    /// <summary>
    /// 在一条撤销记录里执行尺寸调整（缩放 / 单边扩缩）。
    /// 地形、省份、归属、实体与文件头由 <see cref="MapResizeCommand"/> 快照；方案目标格（MapCase.TargetTile 是格子序号）
    /// 也会被重映射，而 MapResizeCommand 不含方案，这里一并快照，保证一次撤销就完整还原。
    /// 失败或没有改动时不记录。<paramref name="record"/> 把做好的撤销命令放进撤销栈（通常是 <see cref="UndoManager.Record"/>），
    /// 为 null（没有撤销栈，例如脚本批量执行）时直接执行。
    /// </summary>
    public static ModifierResult RecordResize(MapData map, Action<IUndoableCommand>? record, string description, Func<ModifierResult> resize)
    {
        if (record == null) return resize();

        var command = new MapResizeCommand(map, description);
        var casesBefore = map.Cases.ToArray();
        var result = resize();
        if (!result.Success || result.AffectedCount == 0) return result;

        command.CaptureAfterState();
        record(new ResizeWithCasesCommand(command, map, casesBefore, map.Cases.ToArray()));
        return result;
    }

    private sealed class ResizeWithCasesCommand(MapResizeCommand inner, MapData map, MapCase[] casesBefore, MapCase[] casesAfter)
        : IUndoableCommand
    {
        public string Description => inner.Description;

        public void Execute()
        {
            inner.Execute();
            RestoreCases(casesAfter);
        }

        public void Undo()
        {
            inner.Undo();
            RestoreCases(casesBefore);
        }

        private void RestoreCases(MapCase[] cases)
        {
            map.Cases.Clear();
            foreach (var item in cases) map.Cases.Add(item);
        }
    }

    #endregion

    #region G 键 - 按比例缩放

    public ModifierResult ScaleMap(double scale)
    {
        if (_mapData == null) return NoMapResult();
        if (IsWorldBasedMap) return ModifierResult.Fail(Loc.T("Module.Terrain.WorldMapReadOnly"));

        scale = Math.Clamp(scale, 0.1, 10.0);
        if (Math.Abs(scale - 1.0) < 0.001) return ModifierResult.Ok(Loc.T("Module.Terrain.ScaleNoop"), 0);

        int oldWidth = _mapData.MapWidth;
        int oldHeight = _mapData.MapHeight;
        var grid = new HexScaleGrid(oldWidth, oldHeight, Math.Max(1, (int)(oldWidth * scale)), Math.Max(1, (int)(oldHeight * scale)), scale);
        int newWidth = grid.NewWidth, newHeight = grid.NewHeight;

        string[] oldBelongs = ExtractBelongsByMapIndex();

        // 新格 → 旧格重采样地形与省份
        var newTerrains = new TerrainData[newWidth * newHeight];
        var sourceProvinces = new ushort[newWidth * newHeight];
        for (int newRow = 0; newRow < newHeight; newRow++)
        {
            for (int newCol = 0; newCol < newWidth; newCol++)
            {
                var (oldCol, oldRow) = grid.FindSource(newCol, newRow);
                int index = newRow * newWidth + newCol;
                newTerrains[index] = _mapData.GetTerrainRef(oldCol, oldRow);
                sourceProvinces[index] = _mapData.GetProvinceRef(oldCol, oldRow).ProvinceValue;
            }
        }

        _mapData.Resize(newWidth, newHeight);
        for (int i = 0; i < newTerrains.Length; i++)
            _mapData.GetTerrainRef(i) = newTerrains[i];

        var capitalMap = RebuildScaledProvinces(grid, sourceProvinces);

        // 归属与实体都走 旧格 → 新格 这一个方向，两者才严格一致（反方向 floor 取整与正方向不互逆，
        // 混用时建筑会落到"归属来自另一个旧格"的位置）。缩小时多个旧格并到同一新格：优先保留有归属的值。
        var newBelongs = new string[newWidth * newHeight];
        Array.Fill(newBelongs, "FF");
        for (int oldIndex = 0; oldIndex < oldBelongs.Length; oldIndex++)
        {
            int target = grid.MapIndex(oldIndex);
            if (newBelongs[target] == "FF" || oldBelongs[oldIndex] != "FF")
                newBelongs[target] = oldBelongs[oldIndex];
        }
        _mapData.Belongs.Clear();
        _mapData.Belongs.AddRange(newBelongs);

        // 建筑 / 陷阱 / 单位：同一新格只留一个（VB 同样去重）；站在省会上的建筑跟着省会走、并优先保留
        int merged = RemapEntities(_mapData.Buildings, b => b.Coordinate, (b, i) => { b.Coordinate = i; return b; },
            old => capitalMap.TryGetValue(old, out int capital) ? capital : grid.MapIndex(old),
            uniqueCells: true, preferred: b => capitalMap.ContainsKey(b.Coordinate));
        merged += RemapEntities(_mapData.Traps, t => t.Coordinate, (t, i) => { t.Coordinate = (short)i; return t; },
            old => grid.MapIndex(old), uniqueCells: true);
        merged += RemapEntities(_mapData.Armies, a => a.Coordinate, (a, i) => { a.Coordinate = (short)i; return a; },
            old => grid.MapIndex(old), uniqueCells: true);
        merged += RemapEntities(_mapData.ArmiesV3, a => a.Coordinate, (a, i) => { a.Coordinate = (short)i; return a; },
            old => grid.MapIndex(old), uniqueCells: true);
        RemapOtherEntities(old => grid.MapIndex(old), grid.MapIndex);
        _mapData.InvalidateBuildingCoordIndex();

        // 以建筑位置为省会修补空白省区（对齐 VB UpdateProvincesForBuildings）
        UpdateProvincesFromBuildings(newWidth, newHeight);

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        MarkModified();
        string message = Loc.F("Module.Terrain.Scaled", oldWidth, oldHeight, newWidth, newHeight, scale);
        if (merged > 0) message += Loc.F("Module.Terrain.ScaledMerged", merged);
        return ModifierResult.Ok(message, newWidth * newHeight);
    }

    /// <summary>
    /// 缩放后的省份：每个新格沿用采样来源格的省份，省会序号换算到新地图。
    /// 换算后的省会格若已不属于本省（缩放的正反映射不互逆）或落在海上，就在本省剩余格子里重新设立省会
    /// （优先陆地，对齐 VB ScaleProvincesList），保证每个省会格的省份值等于它自己的序号。
    /// 返回 旧省会序号 → 新省会序号。
    /// </summary>
    private Dictionary<int, int> RebuildScaledProvinces(HexScaleGrid grid, ushort[] sourceProvinces)
    {
        int oldTotal = grid.OldWidth * grid.OldHeight;
        var cellsByProvince = new Dictionary<int, List<int>>();
        for (int i = 0; i < sourceProvinces.Length; i++)
        {
            int value = sourceProvinces[i];
            if (value == 0 || value == 0xFFFF || value >= oldTotal)
            {
                // 不属于任何省区（或省会序号本来就无效）的格子原样保留
                _mapData!.GetProvinceRef(i).ProvinceValue = (ushort)value;
                continue;
            }
            if (!cellsByProvince.TryGetValue(value, out var cells))
                cellsByProvince[value] = cells = new List<int>();
            cells.Add(i);
        }

        bool IsOcean(int index) => _mapData!.GetTerrainRef(index).TileType1 == OceanType;

        var capitalMap = new Dictionary<int, int>(cellsByProvince.Count);
        foreach (var (oldCapital, cells) in cellsByProvince)
        {
            int candidate = grid.MapIndex(oldCapital);
            bool candidateInProvince = cells.Contains(candidate);
            int capital;
            if (candidateInProvince && !IsOcean(candidate))
                capital = candidate;
            else
            {
                int land = cells.FindIndex(i => !IsOcean(i));
                capital = land >= 0 ? cells[land] : candidateInProvince ? candidate : cells[0];
            }

            capitalMap[oldCapital] = capital;
            foreach (int cell in cells)
                _mapData!.GetProvinceRef(cell).ProvinceValue = (ushort)capital;
        }
        return capitalMap;
    }

    /// <summary>
    /// 六边形感知的缩放映射。地图是 flat-top 六边形、奇数列下移半格，几何坐标是
    /// x = col·0.75、y = row + (col % 2)·0.5；只按 row·scale 缩放属于方形矩阵重采样（VB 的做法），
    /// 放大后奇偶列交界处会错开半格。
    /// </summary>
    private readonly record struct HexScaleGrid(int OldWidth, int OldHeight, int NewWidth, int NewHeight, double Scale)
    {
        private int MapCol(int oldCol) => Math.Clamp((int)(oldCol * Scale), 0, NewWidth - 1);

        private int MapRow(int oldCol, int oldRow)
        {
            double y = (oldRow + (oldCol & 1) * 0.5) * Scale;
            int newRow = (int)Math.Floor(y - (MapCol(oldCol) & 1) * 0.5 + 0.5);
            return Math.Clamp(newRow, 0, NewHeight - 1);
        }

        /// <summary>旧格序号 → 新格序号；不在旧地图范围内的序号（无效值 / 占位值）原样返回。</summary>
        public int MapIndex(int oldIndex)
        {
            if (oldIndex < 0 || oldIndex >= OldWidth * OldHeight) return oldIndex;
            int oldCol = oldIndex % OldWidth;
            return MapRow(oldCol, oldIndex / OldWidth) * NewWidth + MapCol(oldCol);
        }

        /// <summary>新格 → 采样来源的旧格（地形 / 省份重采样）。</summary>
        public (int Col, int Row) FindSource(int newCol, int newRow)
        {
            int col = Math.Clamp((int)Math.Floor(newCol / Scale), 0, OldWidth - 1);
            double y = (newRow + (newCol & 1) * 0.5) / Scale;
            int row = (int)Math.Floor(y - (col & 1) * 0.5 + 0.5);
            return (col, Math.Clamp(row, 0, OldHeight - 1));
        }
    }

    /// <summary>
    /// 以建筑位置为省会修补省区数据（对齐 VB UpdateProvincesForBuildings）：
    /// 建筑所在格若省区为空（0 或 0xFFFF），就把它设成以该建筑索引为省会值的省区；
    /// 再从这些省会向四周扩散，填补同样为空的相邻陆地格子（跳过海洋）。
    /// </summary>
    private void UpdateProvincesFromBuildings(int mapWidth, int mapHeight)
    {
        if (_mapData == null) return;

        int total = mapWidth * mapHeight;
        var capitalIndices = new List<int>();
        foreach (var building in _mapData.Buildings)
        {
            int index = building.Coordinate;
            if (index < 0 || index >= total) continue;

            ref var province = ref _mapData.GetProvinceRef(index);
            if (province.ProvinceValue != 0 && province.ProvinceValue != 0xFFFF) continue;

            province.ProvinceValue = (ushort)index;
            capitalIndices.Add(index);
        }

        var visited = new bool[total];
        var queue = new Queue<int>();
        foreach (int capitalIndex in capitalIndices)
        {
            ushort fillValue = (ushort)capitalIndex;
            queue.Clear();
            queue.Enqueue(capitalIndex);
            visited[capitalIndex] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var (nc, nr) in GetHexNeighbors(current % mapWidth, current / mapWidth))
                {
                    if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;
                    int neighbor = nr * mapWidth + nc;
                    if (visited[neighbor]) continue;
                    visited[neighbor] = true;

                    ref var province = ref _mapData.GetProvinceRef(neighbor);
                    if (province.ProvinceValue != 0 && province.ProvinceValue != 0xFFFF) continue;
                    if (_mapData.GetTerrainRef(neighbor).TileType1 == OceanType) continue;

                    province.ProvinceValue = fillValue;
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    #endregion

    #region I/J/K/L 键 - 单边扩缩

    /// <summary>
    /// 在一侧扩展（<paramref name="amount"/> &gt; 0）或收缩（&lt; 0）地图。向上 / 向左时原有内容整体平移；
    /// 平移出界的实体被删除，扩展出来的格子按 <paramref name="useOcean"/> 填海洋或平地。
    /// </summary>
    public ModifierResult ResizeMap(MapResizeDirection direction, int amount, bool useOcean)
    {
        if (_mapData == null) return NoMapResult();
        if (IsWorldBasedMap) return ModifierResult.Fail(Loc.T("Module.Terrain.WorldMapReadOnly"));
        if (amount == 0) return ModifierResult.Fail(Loc.T("Module.Terrain.ResizeZero"));

        int oldWidth = _mapData.MapWidth;
        int oldHeight = _mapData.MapHeight;
        var (newWidth, newHeight, offsetCol, offsetRow) = direction switch
        {
            MapResizeDirection.Up => (oldWidth, oldHeight + amount, 0, amount),
            MapResizeDirection.Down => (oldWidth, oldHeight + amount, 0, 0),
            MapResizeDirection.Left => (oldWidth + amount, oldHeight, amount, 0),
            _ => (oldWidth + amount, oldHeight, 0, 0)
        };

        if (newWidth <= 0 || newHeight <= 0)
            return ModifierResult.Fail(Loc.T("Module.Terrain.SizeTooSmall"));

        string[] oldBelongs = ExtractBelongsByMapIndex();

        // 旧格 → 新格（落在新范围外返回 null）；不在旧地图范围内的序号（无效值 / 占位值）原样保留
        int? Translate(int oldIndex)
        {
            if (oldIndex < 0 || oldIndex >= oldWidth * oldHeight) return oldIndex;
            int col = oldIndex % oldWidth + offsetCol;
            int row = oldIndex / oldWidth + offsetRow;
            return col >= 0 && col < newWidth && row >= 0 && row < newHeight ? row * newWidth + col : null;
        }

        // 方案目标格：平移出界时夹到最近的边缘格（不能像实体那样直接删掉整条方案）
        int TranslateClamped(int oldIndex)
        {
            if (oldIndex < 0 || oldIndex >= oldWidth * oldHeight) return oldIndex;
            int col = Math.Clamp(oldIndex % oldWidth + offsetCol, 0, newWidth - 1);
            int row = Math.Clamp(oldIndex / oldWidth + offsetRow, 0, newHeight - 1);
            return row * newWidth + col;
        }

        var newTerrains = new TerrainData[newWidth * newHeight];
        var newProvinces = new Province[newWidth * newHeight];
        var fill = TerrainData.CreateDefault();
        fill.TileType1 = useOcean ? OceanType : (byte)0;
        Array.Fill(newTerrains, fill);
        Array.Fill(newProvinces, Province.CreateDefault());
        for (int oldIndex = 0; oldIndex < oldWidth * oldHeight; oldIndex++)
        {
            if (Translate(oldIndex) is not int target) continue;
            newTerrains[target] = _mapData.GetTerrainRef(oldIndex);
            newProvinces[target] = _mapData.GetProvinceRef(oldIndex);
        }

        _mapData.Resize(newWidth, newHeight);
        for (int i = 0; i < newTerrains.Length; i++)
        {
            _mapData.GetTerrainRef(i) = newTerrains[i];

            // ProvinceValue 是省会格的线性序号，尺寸一变必须按同一平移重算；省会被裁掉的省区随之失效
            ref var province = ref _mapData.GetProvinceRef(i);
            province = newProvinces[i];
            ushort capitalIndex = province.ProvinceValue;
            if (capitalIndex == 0 || capitalIndex == 0xFFFF) continue;
            province.ProvinceValue = Translate(capitalIndex) is int newCapital ? (ushort)newCapital : (ushort)0xFFFF;
        }

        // 修正：右 / 下扩缩时建筑原先改的是结构体副本、没有写回，宽度一变所有建筑都落到错误的格子
        RemapEntities(_mapData.Buildings, b => b.Coordinate, (b, i) => { b.Coordinate = i; return b; }, Translate);
        RemapEntities(_mapData.Traps, t => t.Coordinate, (t, i) => { t.Coordinate = (short)i; return t; }, Translate);
        RemapEntities(_mapData.Armies, a => a.Coordinate, (a, i) => { a.Coordinate = (short)i; return a; }, Translate);
        RemapEntities(_mapData.ArmiesV3, a => a.Coordinate, (a, i) => { a.Coordinate = (short)i; return a; }, Translate);
        RemapOtherEntities(Translate, TranslateClamped);
        _mapData.InvalidateBuildingCoordIndex();

        // Belongs 是逐格的字符串表，MapData.Resize 不会重建它：按 新格 → 旧格 重采样，新扩展的格子无归属
        _mapData.Belongs.Clear();
        for (int row = 0; row < newHeight; row++)
        {
            for (int col = 0; col < newWidth; col++)
            {
                int oldCol = col - offsetCol;
                int oldRow = row - offsetRow;
                bool inside = oldCol >= 0 && oldCol < oldWidth && oldRow >= 0 && oldRow < oldHeight;
                _mapData.Belongs.Add(inside ? oldBelongs[oldRow * oldWidth + oldCol] : "FF");
            }
        }

        _mapData.Header.MapLength = newWidth;
        _mapData.Header.MapWidth = newHeight;

        MarkModified();
        string message = Loc.F("Module.Terrain.Resized", DirectionName(direction),
            amount > 0 ? Loc.T("Module.Terrain.Expand") : Loc.T("Module.Terrain.Shrink"), Math.Abs(amount), newWidth, newHeight);
        // 奇数列下移半格：左侧增减奇数列后原有各列的奇偶互换，地形整体错开半格（海岸 / 河流需要重新处理）
        if (direction == MapResizeDirection.Left && amount % 2 != 0)
            message += Loc.T("Module.Terrain.ResizeOddColumns");
        return ModifierResult.Ok(message, newWidth * newHeight);
    }

    #endregion

    #region 实体重映射

    /// <summary>
    /// 归属数组按"地图坐标 → 归属索引"展开成逐格表（索引 = row * 宽 + col）。
    /// 归属是紧凑索引，直接按下标取会错位或越界（表现为归属不全），尺寸调整前必须先展开。
    /// </summary>
    private string[] ExtractBelongsByMapIndex()
    {
        int width = _mapData!.MapWidth, height = _mapData.MapHeight;
        var result = new string[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int index = _mapData.GetBelongIndex(col, row);
                result[row * width + col] = index >= 0 && index < _mapData.Belongs.Count ? _mapData.Belongs[index] : "FF";
            }
        }
        return result;
    }

    /// <summary>
    /// 援军 v1/v3、空军、首都、单位部署（不去重，<paramref name="map"/> 返回 null 的删除）
    /// 与方案目标格（按 <paramref name="caseTarget"/> 换算，方案本身不删）。
    /// </summary>
    private void RemapOtherEntities(Func<int, int?> map, Func<int, int> caseTarget)
    {
        var data = _mapData!;
        RemapEntities(data.Reinforcements, r => r.Coordinate, (r, i) => { r.Coordinate = i; return r; }, map);
        RemapEntities(data.ReinforcementsV3, r => r.Coordinate, (r, i) => { r.Coordinate = i; return r; }, map);
        RemapEntities(data.AirForces, a => a.Coordinate, (a, i) => { a.Coordinate = i; return a; }, map);
        RemapEntities(data.Capitals, c => c.Coordinate, (c, i) => { c.Coordinate = i; return c; }, map);
        RemapEntities(data.UnitPlaces, p => p.Coordinate, (p, i) => { p.Coordinate = i; return p; }, map);
        RemapEntities(data.Cases, c => c.TargetTile, (c, i) => { c.TargetTile = i; return c; }, old => caseTarget(old));
    }

    /// <summary>
    /// 按 <paramref name="map"/> 重写列表里每个实体的格子序号；返回 null 的实体被删除（平移出界）。
    /// <paramref name="uniqueCells"/> 为 true 时同一新格只保留一个（缩小地图时多个旧格并到同一格）：
    /// <paramref name="preferred"/> 标记的实体优先，其余按列表顺序保留第一个。保留下来的实体维持原来的先后顺序。
    /// 返回被删除的实体数。
    /// </summary>
    private static int RemapEntities<T>(IList<T> list, Func<T, int> getIndex, Func<T, int, T> setIndex,
        Func<int, int?> map, bool uniqueCells = false, Func<T, bool>? preferred = null)
    {
        var items = list.ToList();
        var newIndices = items.Select(item => map(getIndex(item))).ToArray();

        var winners = new Dictionary<int, int>();
        if (uniqueCells)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (newIndices[i] is not int cell) continue;
                bool better = !winners.TryGetValue(cell, out int current)
                    || (preferred != null && preferred(items[i]) && !preferred(items[current]));
                if (better) winners[cell] = i;
            }
        }

        list.Clear();
        int dropped = 0;
        for (int i = 0; i < items.Count; i++)
        {
            bool keep = newIndices[i] is int cell && (!uniqueCells || winners[cell] == i);
            if (keep) list.Add(setIndex(items[i], newIndices[i]!.Value));
            else dropped++;
        }
        return dropped;
    }

    #endregion
}
