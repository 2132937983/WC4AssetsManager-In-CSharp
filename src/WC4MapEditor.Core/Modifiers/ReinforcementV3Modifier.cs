using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ReinforcementV3Modifier : ModifierBase
{
    public override string Name => "reinforcement_v3";
    public override string DisplayName => "v3援军修改器";

    private Reinforcement_3? _copiedReinforcement;
    private int _selectedLegionId;

    public int SelectedLegionId
    {
        get => _selectedLegionId;
        set => _selectedLegionId = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int coordIndex = row * _mapData.MapWidth + col;

        Reinforcement_3 reinforcement;
        if (parameter is Reinforcement_3 r)
        {
            reinforcement = r;
            reinforcement.Coordinate = coordIndex;
        }
        else
        {
            reinforcement = Reinforcement_3.CreateDefault();
            reinforcement.Coordinate = coordIndex;
            reinforcement.OwnerLegion = _selectedLegionId;
        }

        int idx = FindReinforcementV3Index(col, row);
        if (idx >= 0)
            _mapData.ReinforcementsV3[idx] = reinforcement;
        else
            _mapData.ReinforcementsV3.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok($"已放置v3援军 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = FindReinforcementV3Index(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有v3援军");

        _mapData.ReinforcementsV3.RemoveAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除v3援军 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return FindReinforcementV3Index(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = FindReinforcementV3Index(col, row);
        return idx >= 0 ? _mapData!.ReinforcementsV3[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Reinforcement_3 reinforcement) return false;

        reinforcement.Coordinate = row * _mapData.MapWidth + col;
        int idx = FindReinforcementV3Index(col, row);
        if (idx >= 0)
            _mapData.ReinforcementsV3[idx] = reinforcement;
        else
            _mapData.ReinforcementsV3.Add(reinforcement);

        MarkModified();
        return true;
    }

    public ModifierResult CopyReinforcement(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = FindReinforcementV3Index(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有v3援军");
        _copiedReinforcement = _mapData!.ReinforcementsV3[idx];
        return ModifierResult.Ok("已复制v3援军数据");
    }

    public ModifierResult PasteReinforcement(int col, int row, int provinceBelong = -1)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedReinforcement == null) return ModifierResult.Fail("没有已复制的v3援军数据");

        var copied = _copiedReinforcement.Value;
        var reinforcement = new Reinforcement_3
        {
            Coordinate = copied.Coordinate,
            UnitType = copied.UnitType,
            Level = copied.Level,
            Organization = copied.Organization,
            BehaviorMode = copied.BehaviorMode,
            Direction = copied.Direction,
            Unknown = copied.Unknown,
            General = copied.General,
            Rank = copied.Rank,
            HpLevel = copied.HpLevel,
            Skill1Level = copied.Skill1Level,
            Skill2Level = copied.Skill2Level,
            Skill3Level = copied.Skill3Level,
            Skill4Level = copied.Skill4Level,
            Skill5Level = copied.Skill5Level,
            Waste1 = copied.Waste1,
            Waste2 = copied.Waste2,
            Waste3 = copied.Waste3,
            OwnerLegion = provinceBelong > 0 ? provinceBelong : copied.OwnerLegion,
            SpawnRound = copied.SpawnRound,
            Medal1 = copied.Medal1,
            Medal2 = copied.Medal2,
            Medal3 = copied.Medal3,
            Ribbon1 = copied.Ribbon1,
            Ribbon2 = copied.Ribbon2,
            Ribbon3 = copied.Ribbon3
        };

        return Apply(col, row, reinforcement);
    }

    public ModifierResult UpdateReinforcement(Reinforcement_3 reinforcement)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(reinforcement.Coordinate, _mapData.MapWidth);
        int idx = FindReinforcementV3Index(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReinforcementsV3[idx] = reinforcement;
        else
            _mapData.ReinforcementsV3.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok("已更新v3援军数据");
    }

    public ModifierResult DeleteAllReinforcements()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.ReinforcementsV3.Count;
        _mapData.ReinforcementsV3.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有v3援军，共 {count} 个");
    }

    public ModifierResult DeleteReinforcement(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.ReinforcementsV3.Count) return ModifierResult.Fail("v3援军索引超出范围");

        _mapData.ReinforcementsV3.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除v3援军");
    }

    public ModifierResult AddBelongToReinforcementCells(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int addedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var r in _mapData.ReinforcementsV3)
            existingCoords.Add(r.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int hexIndex = row * _mapData.MapWidth + col;
                int cellBelong = _mapData.GetBelongValue(col, row);

                if (cellBelong != belongValue) continue;
                if (existingCoords.Contains(hexIndex)) continue;

                var reinforcement = Reinforcement_3.CreateDefault();
                reinforcement.Coordinate = hexIndex;
                reinforcement.OwnerLegion = belongValue;

                _mapData.ReinforcementsV3.Add(reinforcement);
                existingCoords.Add(hexIndex);
                addedCount++;
            }
        }

        if (addedCount > 0) MarkModified();
        return ModifierResult.Ok($"已为归属 {belongValue} 的格子添加 {addedCount} 个v3援军");
    }

    public IReadOnlyList<Reinforcement_3> GetAllReinforcements()
    {
        return _mapData?.ReinforcementsV3 ?? [];
    }

    public Reinforcement_3? GetReinforcement(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.ReinforcementsV3.Count) return null;
        return _mapData.ReinforcementsV3[index];
    }

    public int GetReinforcementCount()
    {
        return _mapData?.ReinforcementsV3.Count ?? 0;
    }

    #region 随机化援军属性

    private readonly Random _random = new();

    public ModifierResult RandomizeReinforcementAttributes(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = FindReinforcementV3Index(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有v3援军");

        var reinforcement = _mapData.ReinforcementsV3[idx];
        reinforcement.Level = (byte)_random.Next(1, 6);
        reinforcement.Organization = (byte)_random.Next(1, 11);
        reinforcement.Rank = (byte)_random.Next(0, 6);
        reinforcement.HpLevel = (byte)_random.Next(0, 6);

        _mapData.ReinforcementsV3[idx] = reinforcement;
        MarkModified();
        return ModifierResult.Ok($"已随机化v3援军属性: 等级={reinforcement.Level}, 编制={reinforcement.Organization}");
    }

    public ModifierResult RandomizeReinforcementsByBelong(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        for (int i = 0; i < _mapData.ReinforcementsV3.Count; i++)
        {
            var reinforcement = _mapData.ReinforcementsV3[i];
            if (belongValue != -1 && reinforcement.OwnerLegion != belongValue) continue;

            reinforcement.Level = (byte)_random.Next(1, 6);
            reinforcement.Organization = (byte)_random.Next(1, 11);
            reinforcement.Rank = (byte)_random.Next(0, 6);
            reinforcement.HpLevel = (byte)_random.Next(0, 6);

            _mapData.ReinforcementsV3[i] = reinforcement;
            modifiedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已随机化 {modifiedCount} 个v3援军属性 (归属={belongValue})");
    }

    #endregion

    #region 按概率批量生成援军

    public ModifierResult GenerateReinforcementsByProbability(int belongValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");

        int generatedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var r in _mapData.ReinforcementsV3)
            existingCoords.Add(r.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                if (_random.Next(0, 100) >= probability) continue;

                int hexIndex = row * _mapData.MapWidth + col;
                int cellBelong = _mapData.GetBelongValue(col, row);

                if (belongValue != -1 && cellBelong != belongValue) continue;
                if (existingCoords.Contains(hexIndex)) continue;

                var reinforcement = Reinforcement_3.CreateDefault();
                reinforcement.Coordinate = hexIndex;
                reinforcement.OwnerLegion = cellBelong;
                reinforcement.Level = (byte)_random.Next(1, 6);
                reinforcement.Organization = (byte)_random.Next(1, 11);

                _mapData.ReinforcementsV3.Add(reinforcement);
                existingCoords.Add(hexIndex);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个v3援军 (归属={belongValue}, 概率={probability}%)");
    }

    #endregion

    private int FindReinforcementV3Index(int col, int row)
    {
        if (_mapData == null) return -1;
        int coordIndex = row * _mapData.MapWidth + col;
        for (int i = 0; i < _mapData.ReinforcementsV3.Count; i++)
        {
            if (_mapData.ReinforcementsV3[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }
}