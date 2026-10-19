using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 战略建设模块：战略建设是与格子无关的全局列表 ——"某军团从第几回合起按哪条策略（def_strategy.xml）分配生产"。
/// 提供增删改、复制粘贴、新条目默认值、校验，以及军团 / 建设代码的选项与显示名。
/// </summary>
public sealed class StrategicConstructionModifier : ModifierBase
{
    /// <summary>执行回合的上限（格式里是 int，这里只拦明显的误输入）。</summary>
    public const int MaxRound = 9999;

    public override string Name => "strategic_construction";
    public override string DisplayName => Loc.T("Module.Strategic.Name", "战略建设修改器");

    private StrategicConstruction? _copiedConstruction;

    #region 基础CRUD（战略建设与格子无关）

    public override ModifierResult Apply(int col, int row, object? parameter = null) => NotCellBased();
    public override ModifierResult Remove(int col, int row) => NotCellBased();
    public override bool CanApply(int col, int row) => false;
    public override bool CanRemove(int col, int row) => false;
    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    // 早先的 Apply 无视坐标、每次追加一条默认战略建设：任何"点击即 apply"的路由都会悄悄往列表里加数据
    private static ModifierResult NotCellBased()
        => ModifierResult.Fail(Loc.T("Module.Strategic.NotCellBased", "战略建设是全局列表，不能按格子添加或删除，请在战略建设列表中编辑"));

    #endregion

    #region 战略建设CRUD

    public ModifierResult AddConstruction(StrategicConstruction construction)
    {
        if (_mapData == null) return NotReady();
        if (Validate(construction) is { } error) return ModifierResult.Fail(error);

        _mapData.StrategyConstructions.Add(construction);
        MarkModified();
        return ModifierResult.Ok(WithCodeWarning(
            Loc.F("Module.Strategic.Added", DescribeLegion(construction.LegionId), DescribeCode(construction.ConstructionCode)),
            construction.ConstructionCode));
    }

    public ModifierResult UpdateConstruction(int index, StrategicConstruction construction)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);
        if (Validate(construction) is { } error) return ModifierResult.Fail(error);

        _mapData.StrategyConstructions[index] = construction;
        MarkModified();
        return ModifierResult.Ok(WithCodeWarning(Loc.F("Module.Strategic.Updated", index + 1), construction.ConstructionCode));
    }

    public ModifierResult DeleteConstruction(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        _mapData.StrategyConstructions.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Strategic.Deleted", index + 1));
    }

    public ModifierResult DeleteAllConstructions()
    {
        if (_mapData == null) return NotReady();

        int count = _mapData.StrategyConstructions.Count;
        _mapData.StrategyConstructions.Clear();
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Strategic.DeletedAll", count), count);
    }

    /// <summary>复制第 <paramref name="index"/> 条战略建设，副本插在它后面（下标 index + 1）。</summary>
    public ModifierResult DuplicateConstruction(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        _mapData.StrategyConstructions.Insert(index + 1, _mapData.StrategyConstructions[index]);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Strategic.Duplicated", index + 1));
    }

    /// <summary>
    /// 新增战略建设的默认值：第一个军团、第 1 回合执行、代码表里的第一条策略
    /// （VB 新增的是全 0：军团序号 0 不对应任何军团，代码 0 也不是有效策略）。
    /// </summary>
    public StrategicConstruction CreateDefaultConstruction()
    {
        int legionId = _mapData != null && _mapData.Legions.Count > 0 ? _mapData.Legions[0].ActionId : 0;
        int code = StrategyCatalog.All.Count > 0 ? StrategyCatalog.All[0].Id : 0;
        return new StrategicConstruction { LegionId = legionId, ExecuteRound = 1, ConstructionCode = code };
    }

    #endregion

    #region 校验与显示

    /// <summary>
    /// 校验一条战略建设（窗口、命令行共用）：军团序号必须是现有军团的 ActionId，
    /// 执行回合在 1–<see cref="MaxRound"/>，建设代码 ≥ 0（不在代码表里不算错误，显示为"未知代码"）。通过时返回 null。
    /// </summary>
    public string? Validate(in StrategicConstruction construction)
    {
        if (FindLegion(construction.LegionId) == null) return Loc.F("Module.Strategic.LegionNotFound", construction.LegionId);
        if (construction.ExecuteRound < 1 || construction.ExecuteRound > MaxRound) return Loc.F("Module.Strategic.InvalidRound", 1, MaxRound);
        if (construction.ConstructionCode < 0) return Loc.F("Module.Strategic.InvalidCode", construction.ConstructionCode);
        return null;
    }

    /// <summary>
    /// 建设代码不在代码表（def_strategy.xml）里时的提醒（照样允许保存）；在表里、或代码表读不到时返回 null。
    /// 新增 / 修改成功的消息里会带上它。
    /// </summary>
    public static string? UnknownCodeWarning(int code)
        => StrategyCatalog.All.Count == 0 || StrategyCatalog.Find(code) != null ? null : Loc.F("Module.Strategic.UnknownCode", code);

    private static string WithCodeWarning(string message, int code)
        => UnknownCodeWarning(code) is { } warning ? Loc.F("Module.Strategic.WithWarning", message, warning) : message;

    /// <summary>按 ActionId 找军团（战略建设里的"军团序号"就是 ActionId）。</summary>
    public Legion? FindLegion(int actionId)
    {
        if (_mapData == null) return null;
        foreach (var legion in _mapData.Legions)
            if (legion.ActionId == actionId) return legion;
        return null;
    }

    /// <summary>某军团的第一条战略建设的下标；没有时返回 -1。</summary>
    public int FirstIndexOfLegion(int actionId)
    {
        if (_mapData == null) return -1;
        for (int i = 0; i < _mapData.StrategyConstructions.Count; i++)
            if (_mapData.StrategyConstructions[i].LegionId == actionId) return i;
        return -1;
    }

    /// <summary>军团选项：值 = ActionId，名称 = 国家名。</summary>
    public IReadOnlyList<ConfigKeyValue> GetLegionOptions()
    {
        if (_mapData == null) return [];
        return _mapData.Legions.Select(l => new ConfigKeyValue { Value = l.ActionId, Name = CountryNameProvider.Get(l.CountryId) }).ToList();
    }

    /// <summary>建设代码选项：def_strategy.xml 的全部策略（值 = 策略 ID）。</summary>
    public static IReadOnlyList<ConfigKeyValue> GetCodeOptions()
        => StrategyCatalog.All.Select(d => new ConfigKeyValue { Value = d.Id, Name = d.DisplayName }).ToList();

    /// <summary>军团的显示名，例如 "军团 2（德国）"；军团不存在时注明。</summary>
    public string DescribeLegion(int actionId)
    {
        var legion = FindLegion(actionId);
        return legion is { } l
            ? Loc.F("Module.Strategic.LegionLabel", actionId, CountryNameProvider.Get(l.CountryId))
            : Loc.F("Module.Strategic.LegionMissing", actionId);
    }

    /// <summary>建设代码的显示名，例如 "1073 步兵4、3（步兵）"；不在代码表里时注明。</summary>
    public static string DescribeCode(int code)
    {
        var definition = StrategyCatalog.Find(code);
        return definition != null
            ? Loc.F("Module.Strategic.CodeLabel", code, definition.DisplayName)
            : Loc.F("Module.Strategic.CodeUnknown", code);
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyConstruction(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return IndexOutOfRange(index);

        _copiedConstruction = _mapData.StrategyConstructions[index];
        return ModifierResult.Ok(Loc.T("Module.Strategic.Copied", "已复制战略建设数据"));
    }

    public ModifierResult PasteConstruction()
    {
        if (_mapData == null) return NotReady();
        if (_copiedConstruction == null) return ModifierResult.Fail(Loc.T("Module.Strategic.NothingCopied", "没有已复制的战略建设数据"));

        _mapData.StrategyConstructions.Add(_copiedConstruction.Value);
        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.Strategic.Pasted", "已粘贴战略建设数据"));
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

    private bool IsValidIndex(int index) => _mapData != null && index >= 0 && index < _mapData.StrategyConstructions.Count;

    private static ModifierResult NotReady() => ModifierResult.Fail(Loc.T("Module.Strategic.NotReady", "地图数据未初始化"));

    private static ModifierResult IndexOutOfRange(int index)
        => ModifierResult.Fail(Loc.F("Module.Strategic.IndexOutOfRange", index));
}
