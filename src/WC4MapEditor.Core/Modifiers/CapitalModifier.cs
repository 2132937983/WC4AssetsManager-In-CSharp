using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 首都编辑模块。
/// <para>
/// 这是一个<b>独立模块</b>，不隶属于任何编辑模式。需要它的编辑模式通过
/// <see cref="Mode.IModeHandler.ModifierKinds"/> 把它引进来即可，
/// 目前由军团编辑模式引入使用（X 键对当前选中的格子切换首都）。
/// </para>
/// <para>
/// 只读写 <see cref="MapData.Capitals"/>，不触碰归属值 ——
/// 编辑首都期间不会误改归属数据。首都数量写进文件头的工作由保存流程统一完成。
/// </para>
/// </summary>
public sealed class CapitalModifier : ModifierBase
{
    public override string Name => "capital";
    public override string DisplayName => "首都编辑模块";

    /// <summary><see cref="ToggleCapital"/> 的结果。</summary>
    public enum ToggleResult
    {
        /// <summary>没有改动（地图未加载或坐标越界）</summary>
        None,
        Added,
        Removed
    }

    /// <summary>取指定格子上的首都，没有则返回 null</summary>
    public Capital? GetCapitalAtPosition(int hexIndex)
    {
        int index = IndexOfCapital(hexIndex);
        return index >= 0 ? _mapData!.Capitals[index] : null;
    }

    /// <summary>指定格子上的首都在 <see cref="MapData.Capitals"/> 中的位置，没有返回 -1。</summary>
    public int IndexOfCapital(int hexIndex)
    {
        if (_mapData == null) return -1;
        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex) return i;
        }
        return -1;
    }

    /// <summary>
    /// 该格是否为等级城（建筑类型 15-19）。游戏只认等级城上的首都，
    /// 规则与 <see cref="BTLRuleChecker.CheckCapitals"/> 一致。
    /// </summary>
    public bool IsOnLevelCity(int hexIndex)
    {
        if (_mapData == null || _mapData.MapWidth <= 0 || !IsValidHex(hexIndex)) return false;
        int buildingIndex = _mapData.FindBuildingIndex(hexIndex % _mapData.MapWidth, hexIndex / _mapData.MapWidth);
        if (buildingIndex < 0) return false;
        int type = _mapData.Buildings[buildingIndex].BuildingType;
        return type >= BTLRuleChecker.MinCapitalBuildingType && type <= BTLRuleChecker.MaxCapitalBuildingType;
    }

    public ModifierResult AddCapital(int hexIndex)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidHex(hexIndex)) return OutOfMap();

        // 不能用 FirstOrDefault 判重复：没找到时返回 default(Capital)，Coordinate 为 0，
        // 会把"在坐标 0 添加首都"误判成已有首都。
        if (IndexOfCapital(hexIndex) >= 0)
            return ModifierResult.Fail(Loc.T("Module.Capital.AlreadyExists", "该位置已有首都"));

        _mapData.Capitals.Add(new Capital { Coordinate = hexIndex });
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Capital.Added", hexIndex));
    }

    public ModifierResult RemoveCapital(int hexIndex)
    {
        if (_mapData == null) return NotInitialized();

        int index = IndexOfCapital(hexIndex);
        if (index < 0) return ModifierResult.Fail(Loc.T("Module.Capital.NotFound", "该位置没有首都"));

        _mapData.Capitals.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Capital.Removed", hexIndex));
    }

    /// <summary>该位置有首都则删除、没有则添加。</summary>
    public ToggleResult ToggleCapital(int hexIndex)
    {
        if (_mapData == null || !IsValidHex(hexIndex)) return ToggleResult.None;
        return IndexOfCapital(hexIndex) >= 0
            ? (RemoveCapital(hexIndex).Success ? ToggleResult.Removed : ToggleResult.None)
            : (AddCapital(hexIndex).Success ? ToggleResult.Added : ToggleResult.None);
    }

    private bool IsValidHex(int hexIndex)
        => _mapData != null && hexIndex >= 0 && hexIndex < _mapData.MapWidth * _mapData.MapHeight;

    private static ModifierResult NotInitialized()
        => ModifierResult.Fail(Loc.T("Module.Capital.NotInitialized", "地图数据未初始化"));

    private static ModifierResult OutOfMap()
        => ModifierResult.Fail(Loc.T("Module.Capital.OutOfMap", "坐标超出范围"));

    // ---------- IModifier：把本模块接入统一的格子操作接口 ----------

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        return AddCapital(row * _mapData.MapWidth + col);
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        return RemoveCapital(row * _mapData.MapWidth + col);
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (_mapData == null || !IsValidCoord(col, row)) return null;
        return GetCapitalAtPosition(row * _mapData.MapWidth + col);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (_mapData == null || data is not Capital || !IsValidCoord(col, row)) return false;
        return AddCapital(row * _mapData.MapWidth + col).Success;
    }
}
