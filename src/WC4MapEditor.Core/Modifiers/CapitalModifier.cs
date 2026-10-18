using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 首都编辑模块。
/// <para>
/// 这是一个<b>独立模块</b>，不隶属于任何编辑模式。需要它的编辑模式通过
/// <see cref="Mode.IModeHandler.ModifierKinds"/> 把它引进来即可，
/// 目前由军团编辑模式引入使用（F 键直接对当前选中格子切换首都）。
/// </para>
/// <para>
/// 只读写 <see cref="MapData.Capitals"/>，不触碰归属值 ——
/// 编辑首都期间不会误改归属数据。
/// </para>
/// </summary>
public sealed class CapitalModifier : ModifierBase
{
    public override string Name => "capital";
    public override string DisplayName => "首都编辑模块";

    /// <summary>取指定格子上的首都，没有则返回 null</summary>
    public Capital? GetCapitalAtPosition(int hexIndex)
    {
        if (_mapData == null) return null;

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
                return _mapData.Capitals[i];
        }

        return null;
    }

    public ModifierResult AddCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (hexIndex < 0 || hexIndex >= _mapData.MapWidth * _mapData.MapHeight)
            return ModifierResult.Fail("坐标超出范围");

        // 不能用 FirstOrDefault 判重复：没找到时返回 default(Capital)，Coordinate 为 0，
        // 会把"在坐标 0 添加首都"误判成已有首都。
        if (GetCapitalAtPosition(hexIndex).HasValue)
            return ModifierResult.Fail("该位置已有首都");

        _mapData.Capitals.Add(new Capital { Coordinate = hexIndex });
        MarkModified();
        return ModifierResult.Ok($"已在位置 {hexIndex} 添加首都");
    }

    public ModifierResult RemoveCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
            {
                _mapData.Capitals.RemoveAt(i);
                MarkModified();
                return ModifierResult.Ok($"已删除位置 {hexIndex} 的首都");
            }
        }

        return ModifierResult.Fail("该位置没有首都");
    }

    /// <summary>该位置有首都则删除、没有则添加</summary>
    public ModifierResult ToggleCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
            {
                _mapData.Capitals.RemoveAt(i);
                MarkModified();
                return ModifierResult.Ok($"已删除位置 {hexIndex} 的首都");
            }
        }

        _mapData.Capitals.Add(new Capital { Coordinate = hexIndex });
        MarkModified();
        return ModifierResult.Ok($"已在位置 {hexIndex} 添加首都");
    }

    // ---------- IModifier：把本模块接入统一的格子操作接口 ----------

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        return AddCapital(row * _mapData.MapWidth + col);
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        return RemoveCapital(row * _mapData.MapWidth + col);
    }

    public override bool CanApply(int col, int row) => CanTouch(col, row);
    public override bool CanRemove(int col, int row) => CanTouch(col, row);

    private bool CanTouch(int col, int row)
        => _mapData != null
        && col >= 0 && row >= 0
        && col < _mapData.MapWidth && row < _mapData.MapHeight;

    public override object? GetDataAt(int col, int row)
    {
        if (_mapData == null) return null;
        return GetCapitalAtPosition(row * _mapData.MapWidth + col);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (_mapData == null || data is not Capital) return false;
        AddCapital(row * _mapData.MapWidth + col);
        return true;
    }
}
