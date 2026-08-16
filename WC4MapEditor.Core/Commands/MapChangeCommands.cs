using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Commands;

public sealed class TerrainChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, TerrainData before, TerrainData after)[] _changes;

    public string Description { get; }

    public TerrainChangeCommand(MapData mapData, string description,
        (int col, int row, TerrainData before, TerrainData after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.GetTerrainRef(col, row) = after;
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.GetTerrainRef(col, row) = before;
        _mapData.IsModified = true;
    }
}

public sealed class ProvinceChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, Province before, Province after)[] _changes;

    public string Description { get; }

    public ProvinceChangeCommand(MapData mapData, string description,
        (int col, int row, Province before, Province after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.GetProvinceRef(col, row) = after;
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.GetProvinceRef(col, row) = before;
        _mapData.IsModified = true;
    }
}

public sealed class DelegateCommand : IUndoableCommand
{
    private readonly Action _execute;
    private readonly Action _undo;

    public string Description { get; }

    public DelegateCommand(string description, Action execute, Action undo)
    {
        Description = description;
        _execute = execute;
        _undo = undo;
    }

    public void Execute() => _execute();
    public void Undo() => _undo();
}