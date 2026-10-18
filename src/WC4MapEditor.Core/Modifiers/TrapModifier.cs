using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class TrapModifier : ModifierBase
{
    public override string Name => "trap";
    public override string DisplayName => "陷阱修改器";

    private Trap? _copiedTrap;
    private int _selectedLegionId;
    private readonly Random _random = new();

    public int SelectedLegionId
    {
        get => _selectedLegionId;
        set => _selectedLegionId = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        short coordIndex = (short)(row * _mapData.MapWidth + col);

        Trap trap;
        if (parameter is Trap t)
        {
            trap = t;
            trap.Coordinate = coordIndex;
        }
        else
        {
            trap = Trap.CreateDefault(coordIndex);
            trap.LegionId = (short)_selectedLegionId;
        }

        int idx = _mapData.FindTrapIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return ModifierResult.Ok($"已放置陷阱 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindTrapIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有陷阱");

        _mapData.RemoveTrapAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除陷阱 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindTrapIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindTrapIndex(col, row);
        return idx >= 0 ? _mapData.Traps[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Trap trap) return false;

        trap.Coordinate = (short)(row * _mapData.MapWidth + col);
        int idx = _mapData.FindTrapIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return true;
    }

    #region 复制/粘贴

    public ModifierResult CopyTrap(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindTrapIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有陷阱");
        _copiedTrap = _mapData.Traps[idx];
        return ModifierResult.Ok("已复制陷阱数据");
    }

    public ModifierResult PasteTrap(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedTrap == null) return ModifierResult.Fail("没有已复制的陷阱数据");
        return Apply(col, row, _copiedTrap);
    }

    #endregion

    #region 基础CRUD

    public ModifierResult UpdateTrap(Trap trap)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(trap.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindTrapIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return ModifierResult.Ok("已更新陷阱数据");
    }

    public ModifierResult DeleteAllTraps()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Traps.Count;
        _mapData.Traps.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有陷阱，共 {count} 个");
    }

    #endregion

    #region 随机化陷阱属性 (R键)

    public ModifierResult RandomizeTrapAttributes(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindTrapIndex(col, row);
        Trap trap;
        if (idx >= 0)
        {
            trap = _mapData.Traps[idx];
        }
        else
        {
            trap = Trap.CreateDefault((short)(row * _mapData.MapWidth + col));
        }

        trap.Organization = (byte)_random.Next(1, 11);
        trap.LegionId = (short)_random.Next(0, 256);
        trap.Health = (byte)_random.Next(50, 101);

        if (idx >= 0)
            _mapData.ReplaceTrap(idx, trap);
        else
            _mapData.Traps.Add(trap);

        MarkModified();
        return ModifierResult.Ok($"已随机化陷阱: 编制={trap.Organization}, 军团={trap.LegionId}, 血量={trap.Health}");
    }

    #endregion

    #region 随机化陷阱等级 (Ctrl+R)

    public ModifierResult RandomizeTrapLevels(int legionValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 1 || probability > 100) return ModifierResult.Fail("概率必须在1-100之间");

        int modifiedCount;

        if (legionValue == -1)
        {
            modifiedCount = 0;
            var existingLegions = _mapData.Legions.Select(l => l.ActionId).Distinct();
            foreach (var legionId in existingLegions)
            {
                modifiedCount += RandomizeTrapLevelsForSingleLegion(legionId, probability);
            }
        }
        else
        {
            modifiedCount = RandomizeTrapLevelsForSingleLegion(legionValue, probability);
        }

        MarkModified();
        return ModifierResult.Ok($"随机化陷阱等级完成: 修改了 {modifiedCount} 个陷阱");
    }

    private int RandomizeTrapLevelsForSingleLegion(int legionValue, int probability)
    {
        int modifiedCount = 0;

        for (int i = 0; i < _mapData!.Traps.Count; i++)
        {
            var trap = _mapData.Traps[i];
            if (trap.LegionId != legionValue) continue;

            double[] weights = [probability, probability * 0.8, probability * 0.6, probability * 0.4];
            for (int w = 0; w < weights.Length; w++)
                weights[w] = Math.Max(weights[w], 1);

            double totalWeight = weights.Sum();
            double randValue = _random.NextDouble() * totalWeight;

            int newLevel;
            if (randValue <= weights[0])
                newLevel = 1;
            else if (randValue <= weights[0] + weights[1])
                newLevel = 2;
            else if (randValue <= weights[0] + weights[1] + weights[2])
                newLevel = 3;
            else
                newLevel = 4;

            trap.Organization = (byte)newLevel;
            _mapData.ReplaceTrap(i, trap);
            modifiedCount++;
        }

        return modifiedCount;
    }

    #endregion

    #region 批量生成陷阱 (G键)

    public ModifierResult BatchGenerateTraps(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");

        int generatedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var trap in _mapData.Traps)
            existingCoords.Add(trap.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                if (_random.Next(0, 100) >= probability) continue;

                int hexIndex = row * _mapData.MapWidth + col;
                if (existingCoords.Contains(hexIndex)) continue;

                var trap = Trap.CreateDefault((short)hexIndex);
                trap.Organization = (byte)_random.Next(1, 6);
                trap.LegionId = (short)_random.Next(0, 256);
                trap.Health = (byte)_random.Next(50, 101);

                _mapData.Traps.Add(trap);
                existingCoords.Add(hexIndex);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个陷阱");
    }

    #endregion

    #region 按归属批量生成陷阱 (Ctrl+G)

    public ModifierResult GenerateTrapsByBelong(int belongValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");

        int totalTrapsCreated;

        if (belongValue == -1)
        {
            totalTrapsCreated = 0;
            var existingLegions = _mapData.Legions.Select(l => l.ActionId).Distinct();
            foreach (var legionId in existingLegions)
            {
                totalTrapsCreated += GenerateTrapsForSingleBelong(legionId, probability);
            }
        }
        else
        {
            totalTrapsCreated = GenerateTrapsForSingleBelong(belongValue, probability);
        }

        MarkModified();
        return ModifierResult.Ok($"批量生成陷阱完成: 生成了 {totalTrapsCreated} 个陷阱");
    }

    private int GenerateTrapsForSingleBelong(int belongValue, int probability)
    {
        int trapsCreated = 0;
        var existingCoords = new HashSet<int>();
        foreach (var trap in _mapData!.Traps)
            existingCoords.Add(trap.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int hexIndex = row * _mapData.MapWidth + col;
                int cellBelong = _mapData.GetBelongValue(col, row);

                if (cellBelong != belongValue) continue;
                if (existingCoords.Contains(hexIndex)) continue;
                if (_random.Next(0, 100) >= probability) continue;

                var trap = Trap.CreateDefault((short)hexIndex);
                trap.LegionId = (short)belongValue;
                trap.Organization = (byte)_random.Next(1, 6);
                trap.Health = (byte)_random.Next(50, 101);

                _mapData.Traps.Add(trap);
                existingCoords.Add(hexIndex);
                trapsCreated++;
            }
        }

        return trapsCreated;
    }

    #endregion

    #region 按概率自由生成陷阱 (Ctrl+G)

    /// <summary>
    /// 按概率在地图上自由生成陷阱：跳过海洋/空地以及已有建筑的格子，其余格子按概率随机生成。
    /// 与 GenerateTrapsByBelong 不同，该方法不按归属筛选。
    /// </summary>
    public ModifierResult GenerateTrapsFree(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");

        int generatedCount = 0;

        // 预先收集所有需要排除的格子：避免在双重循环里逐格做线性查找
        // （FindBuildingIndex / FindArmyIndex 都是遍历整个集合，大地图上会非常慢）。
        var excluded = new HashSet<int>();

        // 已有陷阱
        foreach (var trap in _mapData.Traps)
            excluded.Add(trap.Coordinate);

        // 已有建筑
        foreach (var building in _mapData.Buildings)
            excluded.Add(building.Coordinate);

        // 已有单位（v1 与 v3 都算）
        foreach (var army in _mapData.Armies)
            excluded.Add(army.Coordinate);
        foreach (var army in _mapData.ArmiesV3)
            excluded.Add(army.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int hexIndex = row * _mapData.MapWidth + col;

                // 已有陷阱 / 建筑 / 单位的格子都不生成
                if (excluded.Contains(hexIndex)) continue;

                // 排除海洋与空地：TileType1 为 0 或 1 时地形层不绘制陆地（OCEAN_TILE_TYPE = 1）
                var terrain = _mapData.GetTerrainAt(col, row);
                if (terrain.TileType1 == 0 || terrain.TileType1 == 1) continue;

                if (_random.Next(0, 100) >= probability) continue;

                // 确定陷阱的所属军团（Trap.LegionId）：
                // 优先用该格自身的归属值；该格无归属（0xFF）时回退到所在省份「省会格子」的归属值；
                // 两者都取不到则跳过该格 —— 否则会生成 LegionId 无效（255）的陷阱，
                // 渲染层找不到对应国家，国旗会画成灰色方块。
                int cellBelong = _mapData.GetBelongValue(col, row);
                int legionId = (cellBelong >= 0 && cellBelong != 0xFF)
                    ? cellBelong
                    : _mapData.GetProvinceCapitalBelong(col, row);

                if (legionId < 0 || legionId >= 0xFF) continue;

                var newTrap = Trap.CreateDefault((short)hexIndex);
                newTrap.LegionId = (short)legionId;
                newTrap.Organization = (byte)_random.Next(1, 6);
                newTrap.Health = (byte)_random.Next(50, 101);

                _mapData.Traps.Add(newTrap);
                excluded.Add(hexIndex);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个陷阱");
    }

    #endregion

    #region 按省区批量生成陷阱 (Ctrl+I)

    public ModifierResult GenerateTrapsByProvince(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");
        if (_mapData.Provinces == null || _mapData.Provinces.Count == 0)
            return ModifierResult.Fail("没有省区数据");

        int generatedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var trap in _mapData.Traps)
            existingCoords.Add(trap.Coordinate);

        var provinceMap = new Dictionary<int, List<int>>();
        for (int i = 0; i < _mapData.Provinces.Count; i++)
        {
            int pv = _mapData.Provinces[i].ProvinceValue;
            if (!provinceMap.ContainsKey(pv))
                provinceMap[pv] = new List<int>();
            provinceMap[pv].Add(i);
        }

        foreach (var kvp in provinceMap)
        {
            if (_random.Next(0, 100) >= probability) continue;

            var hexIndices = kvp.Value;
            int selectedIndex = hexIndices[_random.Next(0, hexIndices.Count)];

            if (!existingCoords.Contains(selectedIndex))
            {
                var trap = Trap.CreateDefault((short)selectedIndex);
                trap.Organization = (byte)_random.Next(1, 6);
                trap.LegionId = (short)_random.Next(0, 256);
                trap.Health = (byte)_random.Next(50, 101);

                _mapData.Traps.Add(trap);
                existingCoords.Add(selectedIndex);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已按省区生成 {generatedCount} 个陷阱");
    }

    #endregion

    #region 查找陷阱 (F键)

    public ModifierResult FindTrapByOrganization(int targetLevel)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var foundTraps = new List<(int Index, Trap Trap)>();
        for (int i = 0; i < _mapData.Traps.Count; i++)
        {
            if (_mapData.Traps[i].Organization == targetLevel)
                foundTraps.Add((i, _mapData.Traps[i]));
        }

        if (foundTraps.Count > 0)
        {
            var first = foundTraps[0];
            var coord = HexCoord.FromIndex(first.Trap.Coordinate, _mapData.MapWidth);
            return ModifierResult.Ok($"找到 {foundTraps.Count} 个编制为 {targetLevel} 的陷阱，第一个在 ({coord.Col}, {coord.Row})");
        }

        return ModifierResult.Fail($"没有找到编制为 {targetLevel} 的陷阱");
    }

    #endregion

    #region 查询

    public IReadOnlyList<Trap> GetAllTraps()
    {
        return _mapData?.Traps ?? [];
    }

    public Trap? GetTrapAt(int col, int row)
    {
        if (_mapData == null) return null;
        int idx = _mapData.FindTrapIndex(col, row);
        return idx >= 0 ? _mapData.Traps[idx] : null;
    }

    public int GetTrapCount()
    {
        return _mapData?.Traps.Count ?? 0;
    }

    #endregion
}