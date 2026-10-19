using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Modifiers;

// T 键 - 用平地把城市类建筑（类型 11-15）连成路网。
public sealed partial class TerrainModifier
{
    private const int FirstRoadBuildingType = 11;
    private const int LastRoadBuildingType = 15;

    /// <summary>
    /// 连接建筑：按六边形距离求最小生成树，再沿每条树边用 A* 找代价最小的路，把路上的非平地陆地改成平地（海洋不动）。
    /// VB 按列表顺序把第 i 个与第 i+1 个相连、并用方格曼哈顿距离当启发值，会画出很长的交叉道路，
    /// 曼哈顿距离在六边形网格上还会高估，找不到最短路。
    /// </summary>
    public ModifierResult ConnectBuildings()
    {
        if (_mapData == null) return NoMapResult();

        var points = new List<(int col, int row)>();
        var seen = new HashSet<(int, int)>();
        foreach (var building in _mapData.Buildings)
        {
            if (building.BuildingType < FirstRoadBuildingType || building.BuildingType > LastRoadBuildingType) continue;
            if (building.Coordinate < 0) continue;
            int col = building.Coordinate % _mapData.MapWidth;
            int row = building.Coordinate / _mapData.MapWidth;
            if (IsValidCoord(col, row) && seen.Add((col, row)))
                points.Add((col, row));
        }

        if (points.Count < 2)
            return ModifierResult.Ok(Loc.F("Module.Terrain.ConnectTooFew", points.Count), 0);

        var changedCells = new HashSet<(int, int)>();
        foreach (var (from, to) in BuildSpanningTree(points))
        {
            var path = FindPathWithAStar(points[from], points[to]);
            if (path == null) continue;

            for (int i = 1; i < path.Count - 1; i++)
            {
                var (pc, pr) = path[i];
                ref var terrain = ref _mapData.GetTerrainRef(pc, pr);
                if (terrain.TileType1 == 0 || terrain.TileType1 == OceanType) continue;

                terrain.TileType1 = 0;
                terrain.DecorationType1 = 0;
                changedCells.Add((pc, pr));
            }
        }

        if (changedCells.Count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.Connected", changedCells.Count), changedCells.Count);
    }

    /// <summary>Prim 最小生成树（边权 = 六边形距离），返回要连通的建筑序号对；平局取序号小的，结果确定。</summary>
    private static List<(int from, int to)> BuildSpanningTree(List<(int col, int row)> points)
    {
        int count = points.Count;
        var inTree = new bool[count];
        var bestDistance = new int[count];
        var parent = new int[count];
        Array.Fill(bestDistance, int.MaxValue);
        bestDistance[0] = 0;
        parent[0] = -1;

        var edges = new List<(int, int)>(count - 1);
        for (int step = 0; step < count; step++)
        {
            int next = -1;
            for (int i = 0; i < count; i++)
            {
                if (!inTree[i] && (next < 0 || bestDistance[i] < bestDistance[next])) next = i;
            }

            inTree[next] = true;
            if (parent[next] >= 0) edges.Add((parent[next], next));

            for (int i = 0; i < count; i++)
            {
                if (inTree[i]) continue;
                int distance = BrushEngine.CalculateHexDistance(points[next].col, points[next].row, points[i].col, points[i].row);
                if (distance < bestDistance[i])
                {
                    bestDistance[i] = distance;
                    parent[i] = next;
                }
            }
        }
        return edges;
    }

    /// <summary>A* 寻路：平地与海洋代价 1，其它地形代价 5；启发值为六边形距离（每步至少代价 1，不会高估）。</summary>
    private List<(int col, int row)>? FindPathWithAStar((int col, int row) start, (int col, int row) target)
    {
        if (_mapData == null) return null;
        if (start == target) return new List<(int, int)> { start };

        var openSet = new PriorityQueue<(int col, int row), int>();
        var closedSet = new HashSet<(int, int)>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), int> { [start] = 0 };
        openSet.Enqueue(start, Heuristic(start));

        int maxIterations = _mapData.MapWidth * _mapData.MapHeight * 2;
        for (int iterations = 0; openSet.Count > 0 && iterations < maxIterations; iterations++)
        {
            var current = openSet.Dequeue();
            if (current == target) return ReconstructPath(cameFrom, current);
            if (!closedSet.Add(current)) continue;

            foreach (var neighbor in GetHexNeighbors(current.col, current.row))
            {
                if (!IsValidCoord(neighbor.col, neighbor.row) || closedSet.Contains(neighbor)) continue;

                byte type = _mapData.GetTerrainRef(neighbor.col, neighbor.row).TileType1;
                int tentative = gScore[current] + (type == 0 || type == OceanType ? 1 : 5);
                if (gScore.TryGetValue(neighbor, out int existing) && tentative >= existing) continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative;
                openSet.Enqueue(neighbor, tentative + Heuristic(neighbor));
            }
        }
        return null;

        int Heuristic((int col, int row) cell) => BrushEngine.CalculateHexDistance(cell.col, cell.row, target.col, target.row);
    }

    private static List<(int col, int row)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current };
        while (cameFrom.TryGetValue(current, out current))
            path.Add(current);
        path.Reverse();
        return path;
    }
}
