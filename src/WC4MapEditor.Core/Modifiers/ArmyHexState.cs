using System.Collections.ObjectModel;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 若干格子上的部署数据快照：v1 / v3 单位、陷阱（连同它们在列表里的下标）以及格子归属。
/// <para>
/// 单格 / 选区操作前后各拍一份，撤销恢复"前"、重做恢复"后"。恢复时先删掉这些格子上现有的单位与陷阱，
/// 再按快照里的下标从小到大插回：其它格子的条目没有动过，所以列表顺序与拍快照时完全一致，
/// 撤销后存盘结果逐字节相同。比整表快照省内存，比"撤销 = 删除 / 重新放置"可靠
/// （旧做法撤销粘贴会把原有单位一起删掉、撤销删除会把单位挪到列表末尾）。
/// </para>
/// </summary>
public sealed class ArmyHexState
{
    private readonly HashSet<int> _cells;
    private readonly List<(int Index, Army Unit)> _armies;
    private readonly List<(int Index, Army_3 Unit)> _armiesV3;
    private readonly List<(int Index, Trap Trap)> _traps;
    private readonly List<(int Col, int Row, int Belong)> _belongs;

    private ArmyHexState(HashSet<int> cells, List<(int, Army)> armies, List<(int, Army_3)> armiesV3,
        List<(int, Trap)> traps, List<(int, int, int)> belongs)
    {
        _cells = cells;
        _armies = armies;
        _armiesV3 = armiesV3;
        _traps = traps;
        _belongs = belongs;
    }

    /// <summary>拍下指定格子（越界的格子忽略）上的单位、陷阱与归属。</summary>
    public static ArmyHexState Capture(MapData map, IEnumerable<HexCoord> cells)
    {
        var set = new HashSet<int>();
        var belongs = new List<(int, int, int)>();
        foreach (var c in cells)
        {
            if (c.Col < 0 || c.Row < 0 || c.Col >= map.MapWidth || c.Row >= map.MapHeight) continue;
            if (set.Add(c.ToIndex(map.MapWidth)))
                belongs.Add((c.Col, c.Row, map.GetBelongValue(c.Col, c.Row)));
        }

        return new ArmyHexState(set,
            Collect(map.Armies, set, a => (ushort)a.Coordinate),
            Collect(map.ArmiesV3, set, a => (ushort)a.Coordinate),
            Collect(map.Traps, set, t => t.CellIndex),
            belongs);
    }

    /// <summary>把这些格子恢复成快照时的状态（单位、陷阱回到原下标，归属写回）。</summary>
    public void Restore(MapData map)
    {
        Put(map.Armies, _armies, a => (ushort)a.Coordinate);
        Put(map.ArmiesV3, _armiesV3, a => (ushort)a.Coordinate);
        Put(map.Traps, _traps, t => t.CellIndex);
        foreach (var (col, row, belong) in _belongs)
        {
            if (map.GetBelongValue(col, row) != belong)
                map.SetBelongValue(col, row, belong);
        }
        map.IsModified = true;
    }

    /// <summary>两份快照（同一组格子）内容是否相同：相同说明操作没有改到任何东西，不必记录撤销。</summary>
    public bool SameAs(ArmyHexState other)
        => Same(_armies, other._armies) && Same(_armiesV3, other._armiesV3)
        && Same(_traps, other._traps) && Same(_belongs, other._belongs);

    private static List<(int, T)> Collect<T>(IList<T> list, HashSet<int> cells, Func<T, int> cellOf)
    {
        var found = new List<(int, T)>();
        for (int i = 0; i < list.Count; i++)
            if (cells.Contains(cellOf(list[i]))) found.Add((i, list[i]));
        return found;
    }

    private void Put<T>(ObservableCollection<T> list, List<(int Index, T Item)> items, Func<T, int> cellOf)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (_cells.Contains(cellOf(list[i]))) list.RemoveAt(i);
        foreach (var (index, item) in items)
            list.Insert(Math.Min(index, list.Count), item);
    }

    private static bool Same<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Count; i++)
            if (!cmp.Equals(a[i], b[i])) return false;
        return true;
    }
}
