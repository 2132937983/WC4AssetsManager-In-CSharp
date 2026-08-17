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

public sealed class MapResizeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly int _beforeWidth;
    private readonly int _beforeHeight;
    private readonly TerrainData[] _beforeTerrains;
    private readonly Province[] _beforeProvinces;
    private readonly BTLHeader _beforeHeader;
    private int _afterWidth;
    private int _afterHeight;
    private TerrainData[]? _afterTerrains;
    private Province[]? _afterProvinces;
    private BTLHeader _afterHeader = default;
    private bool _hasAfterState;

    public string Description { get; }

    public MapResizeCommand(MapData mapData, string description)
    {
        _mapData = mapData;
        Description = description;
        _beforeWidth = mapData.MapWidth;
        _beforeHeight = mapData.MapHeight;
        _beforeTerrains = new TerrainData[mapData.MapWidth * mapData.MapHeight];
        _beforeProvinces = new Province[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _beforeTerrains.Length; i++)
        {
            _beforeTerrains[i] = mapData.GetTerrainRef(i);
            _beforeProvinces[i] = mapData.GetProvinceRef(i);
        }
        _beforeHeader = mapData.Header;
        _hasAfterState = false;
    }

    public void CaptureAfterState()
    {
        _afterWidth = _mapData.MapWidth;
        _afterHeight = _mapData.MapHeight;
        _afterTerrains = new TerrainData[_mapData.MapWidth * _mapData.MapHeight];
        _afterProvinces = new Province[_mapData.MapWidth * _mapData.MapHeight];
        for (int i = 0; i < _afterTerrains.Length; i++)
        {
            _afterTerrains[i] = _mapData.GetTerrainRef(i);
            _afterProvinces[i] = _mapData.GetProvinceRef(i);
        }
        _afterHeader = _mapData.Header;
        _hasAfterState = true;
    }

    public void Execute()
    {
        if (!_hasAfterState) return;
        RestoreState(_afterWidth, _afterHeight, _afterTerrains!, _afterProvinces!, _afterHeader);
    }

    public void Undo()
    {
        RestoreState(_beforeWidth, _beforeHeight, _beforeTerrains, _beforeProvinces, _beforeHeader);
    }

    private void RestoreState(int width, int height, TerrainData[] terrains, Province[] provinces, BTLHeader header)
    {
        _mapData.Resize(width, height);
        for (int i = 0; i < terrains.Length; i++)
        {
            _mapData.GetTerrainRef(i) = terrains[i];
            _mapData.GetProvinceRef(i) = provinces[i];
        }
        _mapData.Header = header;
        _mapData.IsModified = true;
    }
}