using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class MapCaseModifier : ModifierBase
{
    public override string Name => "mapcase";
    public override string DisplayName => "方案修改器";

    private MapCase? _copiedCase;
    private readonly Random _random = new();

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        MapCase mapCase;
        if (parameter is MapCase mc)
            mapCase = mc;
        else
            mapCase = MapCase.CreateDefault();

        _mapData.Cases.Add(mapCase);
        MarkModified();
        return ModifierResult.Ok("已添加方案");
    }

    public override ModifierResult Remove(int col, int row)
    {
        return ModifierResult.Fail("方案不支持按坐标删除，请使用DeleteCase");
    }

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;

    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 方案CRUD

    public ModifierResult AddCase(MapCase mapCase)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.Cases.Add(mapCase);
        MarkModified();
        return ModifierResult.Ok($"已添加方案: 方针值={mapCase.PolicyNumber}");
    }

    public ModifierResult UpdateCase(int index, MapCase mapCase)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Cases.Count) return ModifierResult.Fail("方案索引超出范围");

        _mapData.Cases[index] = mapCase;
        MarkModified();
        return ModifierResult.Ok($"已更新方案: 方针值={mapCase.PolicyNumber}");
    }

    public ModifierResult DeleteCase(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Cases.Count) return ModifierResult.Fail("方案索引超出范围");

        _mapData.Cases.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除方案");
    }

    public ModifierResult DeleteAllCases()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Cases.Count;
        _mapData.Cases.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有方案，共 {count} 个");
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyCase(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Cases.Count) return ModifierResult.Fail("方案索引超出范围");

        _copiedCase = _mapData.Cases[index];
        return ModifierResult.Ok($"已复制方案: 方针值={_copiedCase.Value.PolicyNumber}");
    }

    public ModifierResult PasteCase()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedCase == null) return ModifierResult.Fail("没有已复制的方案数据");

        _mapData.Cases.Add(_copiedCase.Value);
        MarkModified();
        return ModifierResult.Ok("已粘贴方案数据");
    }

    #endregion

    #region 批量设置方案值

    public ModifierResult SetPolicyForSelectedUnits(IEnumerable<int> armyCoordinates, int policyValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        var coordSet = new HashSet<int>(armyCoordinates);

        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            if (coordSet.Contains(army.Coordinate))
            {
                army.Plan = (byte)Math.Clamp(policyValue, 0, 255);
                _mapData.ReplaceArmy(i, army);
                modifiedCount++;
            }
        }

        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            var army = _mapData.ArmiesV3[i];
            if (coordSet.Contains(army.Coordinate))
            {
                army.Plan = (byte)Math.Clamp(policyValue, 0, 255);
                _mapData.ReplaceArmyV3(i, army);
                modifiedCount++;
            }
        }

        if (modifiedCount > 0) MarkModified();
        return ModifierResult.Ok($"已设置 {modifiedCount} 个单位的方案值为 {policyValue}");
    }

    public ModifierResult SetPolicyByBelong(int belongValue, int policyValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;

        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            int armyBelong = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (belongValue != -1 && armyBelong != belongValue) continue;

            army.Plan = (byte)Math.Clamp(policyValue, 0, 255);
            _mapData.ReplaceArmy(i, army);
            modifiedCount++;
        }

        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            var army = _mapData.ArmiesV3[i];
            int armyBelong = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (belongValue != -1 && armyBelong != belongValue) continue;

            army.Plan = (byte)Math.Clamp(policyValue, 0, 255);
            _mapData.ReplaceArmyV3(i, army);
            modifiedCount++;
        }

        if (modifiedCount > 0) MarkModified();
        return ModifierResult.Ok($"已设置 {modifiedCount} 个单位的方案值为 {policyValue} (归属={belongValue})");
    }

    public ModifierResult RandomizePolicyByBelong(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;

        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            int armyBelong = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (belongValue != -1 && armyBelong != belongValue) continue;

            army.Plan = (byte)_random.Next(0, 256);
            _mapData.ReplaceArmy(i, army);
            modifiedCount++;
        }

        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            var army = _mapData.ArmiesV3[i];
            int armyBelong = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (belongValue != -1 && armyBelong != belongValue) continue;

            army.Plan = (byte)_random.Next(0, 256);
            _mapData.ReplaceArmyV3(i, army);
            modifiedCount++;
        }

        if (modifiedCount > 0) MarkModified();
        return ModifierResult.Ok($"已随机化 {modifiedCount} 个单位的方案值 (归属={belongValue})");
    }

    #endregion

    #region 查询

    public IReadOnlyList<MapCase> GetAllCases()
    {
        return _mapData?.Cases ?? [];
    }

    public MapCase? GetCase(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.Cases.Count) return null;
        return _mapData.Cases[index];
    }

    #endregion
}