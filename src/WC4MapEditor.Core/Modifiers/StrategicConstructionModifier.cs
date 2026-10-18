using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class StrategicConstructionModifier : ModifierBase
{
    public override string Name => "strategic_construction";
    public override string DisplayName => "战略建设修改器";

    private StrategicConstruction? _copiedConstruction;
    private readonly Random _random = new();

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        StrategicConstruction construction;
        if (parameter is StrategicConstruction sc)
            construction = sc;
        else
            construction = StrategicConstruction.CreateDefault();

        _mapData.StrategyConstructions.Add(construction);
        MarkModified();
        return ModifierResult.Ok("已添加战略建设");
    }

    public override ModifierResult Remove(int col, int row)
    {
        return ModifierResult.Fail("战略建设不支持按坐标删除，请使用DeleteConstruction");
    }

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;

    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 战略建设CRUD

    public ModifierResult AddConstruction(StrategicConstruction construction)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.StrategyConstructions.Add(construction);
        MarkModified();
        return ModifierResult.Ok("已添加战略建设");
    }

    public ModifierResult UpdateConstruction(int index, StrategicConstruction construction)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.StrategyConstructions.Count) return ModifierResult.Fail("战略建设索引超出范围");

        _mapData.StrategyConstructions[index] = construction;
        MarkModified();
        return ModifierResult.Ok("已更新战略建设");
    }

    public ModifierResult DeleteConstruction(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.StrategyConstructions.Count) return ModifierResult.Fail("战略建设索引超出范围");

        _mapData.StrategyConstructions.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除战略建设");
    }

    public ModifierResult DeleteAllConstructions()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.StrategyConstructions.Count;
        _mapData.StrategyConstructions.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有战略建设，共 {count} 个");
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyConstruction(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.StrategyConstructions.Count) return ModifierResult.Fail("战略建设索引超出范围");

        _copiedConstruction = _mapData.StrategyConstructions[index];
        return ModifierResult.Ok("已复制战略建设数据");
    }

    public ModifierResult PasteConstruction()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedConstruction == null) return ModifierResult.Fail("没有已复制的战略建设数据");

        _mapData.StrategyConstructions.Add(_copiedConstruction.Value);
        MarkModified();
        return ModifierResult.Ok("已粘贴战略建设数据");
    }

    #endregion

    #region 随机生成战略建设

    public ModifierResult GenerateRandomConstructions(int count)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int generatedCount = 0;
        var legionIds = _mapData.Legions.Select(l => l.ActionId).ToList();

        for (int i = 0; i < count; i++)
        {
            var construction = StrategicConstruction.CreateDefault();
            construction.LegionId = legionIds[_random.Next(legionIds.Count)];
            construction.ConstructionCode = _random.Next(1, 100);

            _mapData.StrategyConstructions.Add(construction);
            generatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已随机生成 {generatedCount} 个战略建设");
    }

    public ModifierResult GenerateConstructionsByLegion(int legionValue, int count)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int generatedCount = 0;

        if (legionValue == -1)
        {
            var legionIds = _mapData.Legions.Select(l => l.ActionId).Distinct().ToList();
            if (legionIds.Count == 0) legionIds.Add(0);

            foreach (var legionId in legionIds)
            {
                for (int i = 0; i < count; i++)
                {
                    var construction = StrategicConstruction.CreateDefault();
                    construction.LegionId = legionId;
                    construction.ConstructionCode = _random.Next(1, 100);

                    _mapData.StrategyConstructions.Add(construction);
                    generatedCount++;
                }
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var construction = StrategicConstruction.CreateDefault();
                construction.LegionId = legionValue;
                construction.ConstructionCode = _random.Next(1, 100);

                _mapData.StrategyConstructions.Add(construction);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已生成 {generatedCount} 个战略建设 (军团={legionValue})");
    }

    #endregion

    #region 查询

    public IReadOnlyList<StrategicConstruction> GetAllConstructions()
    {
        return _mapData?.StrategyConstructions ?? [];
    }

    public StrategicConstruction? GetConstruction(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.StrategyConstructions.Count) return null;
        return _mapData.StrategyConstructions[index];
    }

    #endregion
}