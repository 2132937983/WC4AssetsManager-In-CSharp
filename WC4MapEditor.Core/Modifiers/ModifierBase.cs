using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public abstract class ModifierBase : IModifier
{
    protected MapData? _mapData;

    public abstract string Name { get; }
    public abstract string DisplayName { get; }

    public virtual void Initialize(MapData mapData)
    {
        _mapData = mapData;
    }

    public virtual void Deinitialize()
    {
        _mapData = null;
    }

    public abstract ModifierResult Apply(int col, int row, object? parameter = null);
    public abstract ModifierResult Remove(int col, int row);
    public abstract bool CanApply(int col, int row);
    public abstract bool CanRemove(int col, int row);
    public abstract object? GetDataAt(int col, int row);
    public abstract bool SetDataAt(int col, int row, object data);

    public virtual ModifierResult ApplyRange(IEnumerable<HexCoord> coords, object? parameter = null)
    {
        int count = 0;
        foreach (var coord in coords)
        {
            var result = Apply(coord.Col, coord.Row, parameter);
            if (result.Success) count++;
        }
        return ModifierResult.Ok($"批量操作完成，影响 {count} 个格子", count);
    }

    public virtual ModifierResult RemoveRange(IEnumerable<HexCoord> coords)
    {
        int count = 0;
        foreach (var coord in coords)
        {
            var result = Remove(coord.Col, coord.Row);
            if (result.Success) count++;
        }
        return ModifierResult.Ok($"批量删除完成，影响 {count} 个格子", count);
    }

    protected bool IsValidCoord(int col, int row)
    {
        return _mapData != null && col >= 0 && row >= 0 && col < _mapData.MapWidth && row < _mapData.MapHeight;
    }

    protected void MarkModified()
    {
        if (_mapData != null) _mapData.IsModified = true;
    }
}