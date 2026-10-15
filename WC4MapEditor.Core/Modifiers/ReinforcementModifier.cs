using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ReinforcementModifier : ModifierBase
{
    public override string Name => "reinforcement";
    public override string DisplayName => "援军修改器";

    private Reinforcement? _copiedReinforcement;
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

        int coordIndex = row * _mapData.MapWidth + col;

        Reinforcement reinforcement;
        if (parameter is Reinforcement r)
        {
            reinforcement = r;
            reinforcement.Coordinate = coordIndex;
        }
        else
        {
            reinforcement = Reinforcement.CreateDefault();
            reinforcement.Coordinate = coordIndex;
            reinforcement.OwnerCountry = _selectedLegionId;
        }

        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok($"已放置援军 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有援军");

        _mapData.RemoveReinforcementAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除援军 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindReinforcementIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindReinforcementIndex(col, row);
        return idx >= 0 ? _mapData.Reinforcements[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Reinforcement reinforcement) return false;

        reinforcement.Coordinate = row * _mapData.MapWidth + col;
        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return true;
    }

    #region 复制/粘贴

    public ModifierResult CopyReinforcement(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindReinforcementIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有援军");
        _copiedReinforcement = _mapData.Reinforcements[idx];
        return ModifierResult.Ok("已复制援军数据");
    }

    public ModifierResult PasteReinforcement(int col, int row, int provinceBelong = -1)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedReinforcement == null) return ModifierResult.Fail("没有已复制的援军数据");

        var copied = _copiedReinforcement.Value;
        var reinforcement = new Reinforcement
        {
            Coordinate = copied.Coordinate,
            UnitType = copied.UnitType,
            Level = copied.Level,
            Organization = copied.Organization,
            Direction = copied.Direction,
            General = copied.General,
            Rank = copied.Rank,
            Quality = copied.Quality,
            Skill1Level = copied.Skill1Level,
            Skill2Level = copied.Skill2Level,
            Skill3Level = copied.Skill3Level,
            Skill4Level = copied.Skill4Level,
            Skill5Level = copied.Skill5Level,
            Badge1 = copied.Badge1,
            Badge2 = copied.Badge2,
            Badge3 = copied.Badge3,
            OwnerCountry = provinceBelong > 0 ? provinceBelong : copied.OwnerCountry,
            SpawnRound = copied.SpawnRound
        };

        return Apply(col, row, reinforcement);
    }

    #endregion

    #region 基础CRUD

    public ModifierResult UpdateReinforcement(Reinforcement reinforcement)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(reinforcement.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindReinforcementIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceReinforcement(idx, reinforcement);
        else
            _mapData.Reinforcements.Add(reinforcement);

        MarkModified();
        return ModifierResult.Ok("已更新援军数据");
    }

    public ModifierResult DeleteAllReinforcements()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Reinforcements.Count;
        _mapData.Reinforcements.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有援军，共 {count} 个");
    }

    public ModifierResult DeleteReinforcement(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Reinforcements.Count) return ModifierResult.Fail("援军索引超出范围");

        _mapData.Reinforcements.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除援军");
    }

    #endregion

    #region 按归属添加援军到格子

    public ModifierResult AddBelongToReinforcementCells(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int addedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var r in _mapData.Reinforcements)
            existingCoords.Add(r.Coordinate);

        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int hexIndex = row * _mapData.MapWidth + col;
                int cellBelong = _mapData.GetBelongValue(col, row);

                if (cellBelong != belongValue) continue;
                if (existingCoords.Contains(hexIndex)) continue;

                var reinforcement = Reinforcement.CreateDefault();
                reinforcement.Coordinate = hexIndex;
                reinforcement.OwnerCountry = belongValue;

                _mapData.Reinforcements.Add(reinforcement);
                existingCoords.Add(hexIndex);
                addedCount++;
            }
        }

        if (addedCount > 0) MarkModified();
        return ModifierResult.Ok($"已为归属 {belongValue} 的格子添加 {addedCount} 个援军");
    }

    #endregion

    #region 随机化援军属性

    public ModifierResult RandomizeReinforcementAttributes(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindReinforcementIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有援军");

        var reinforcement = _mapData.Reinforcements[idx];
        reinforcement.Level = (byte)_random.Next(1, 6);
        reinforcement.Organization = (byte)_random.Next(1, 11);
        reinforcement.Quality = (byte)_random.Next(0, 6);
        reinforcement.Rank = (byte)_random.Next(0, 6);

        _mapData.ReplaceReinforcement(idx, reinforcement);
        MarkModified();
        return ModifierResult.Ok($"已随机化援军属性: 等级={reinforcement.Level}, 编制={reinforcement.Organization}");
    }

    #endregion

    #region 按归属批量随机化援军

    public ModifierResult RandomizeReinforcementsByBelong(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        for (int i = 0; i < _mapData.Reinforcements.Count; i++)
        {
            var reinforcement = _mapData.Reinforcements[i];
            if (belongValue != -1 && reinforcement.OwnerCountry != belongValue) continue;

            reinforcement.Level = (byte)_random.Next(1, 6);
            reinforcement.Organization = (byte)_random.Next(1, 11);
            reinforcement.Quality = (byte)_random.Next(0, 6);
            reinforcement.Rank = (byte)_random.Next(0, 6);

            _mapData.ReplaceReinforcement(i, reinforcement);
            modifiedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已随机化 {modifiedCount} 个援军属性 (归属={belongValue})");
    }

    #endregion

    #region 按概率批量生成援军

    public ModifierResult GenerateReinforcementsByProbability(int belongValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (probability < 0 || probability > 100) return ModifierResult.Fail("概率必须在0-100之间");

        int generatedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var r in _mapData.Reinforcements)
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

                var reinforcement = Reinforcement.CreateDefault();
                reinforcement.Coordinate = hexIndex;
                reinforcement.OwnerCountry = cellBelong;
                reinforcement.Level = (byte)_random.Next(1, 6);
                reinforcement.Organization = (byte)_random.Next(1, 11);

                _mapData.Reinforcements.Add(reinforcement);
                existingCoords.Add(hexIndex);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个援军 (归属={belongValue}, 概率={probability}%)");
    }

    #endregion

    #region 查询

    public IReadOnlyList<Reinforcement> GetAllReinforcements()
    {
        return _mapData?.Reinforcements ?? [];
    }

    public Reinforcement? GetReinforcement(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.Reinforcements.Count) return null;
        return _mapData.Reinforcements[index];
    }

    public int GetReinforcementCount()
    {
        return _mapData?.Reinforcements.Count ?? 0;
    }

    #endregion
}