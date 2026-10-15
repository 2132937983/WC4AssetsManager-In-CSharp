using WC4MapEditor.Core.Models;

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
    private readonly List<string> _beforeBelongs;
    private readonly EntitySnapshot _beforeEntities;
    private readonly BTLHeader _beforeHeader;
    private int _afterWidth;
    private int _afterHeight;
    private TerrainData[]? _afterTerrains;
    private Province[]? _afterProvinces;
    private List<string>? _afterBelongs;
    private EntitySnapshot? _afterEntities;
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
        // 归属也必须进快照：只用 Resize 重建地形/省份的话，
        // 撤销后归属会停留在缩放后的错位状态，撤不回来。
        _beforeBelongs = new List<string>(mapData.Belongs);
        // 实体坐标也会被 ResizeMap 改写（并且越界的会被删除），
        // 只恢复地形/省份/归属的话，撤销后实体仍停在调整后的位置。
        _beforeEntities = EntitySnapshot.Capture(mapData);
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
        _afterBelongs = new List<string>(_mapData.Belongs);
        _afterEntities = EntitySnapshot.Capture(_mapData);
        _afterHeader = _mapData.Header;
        _hasAfterState = true;
    }

    public void Execute()
    {
        if (!_hasAfterState) return;
        RestoreState(_afterWidth, _afterHeight, _afterTerrains!, _afterProvinces!, _afterBelongs!, _afterEntities!, _afterHeader);
    }

    public void Undo()
    {
        RestoreState(_beforeWidth, _beforeHeight, _beforeTerrains, _beforeProvinces, _beforeBelongs, _beforeEntities, _beforeHeader);
    }

    private void RestoreState(
        int width, int height,
        TerrainData[] terrains, Province[] provinces, List<string> belongs,
        EntitySnapshot entities, BTLHeader header)
    {
        _mapData.Resize(width, height);
        for (int i = 0; i < terrains.Length; i++)
        {
            _mapData.GetTerrainRef(i) = terrains[i];
            _mapData.GetProvinceRef(i) = provinces[i];
        }
        _mapData.Belongs = new List<string>(belongs);
        entities.RestoreTo(_mapData);
        _mapData.Header = header;
        _mapData.IsModified = true;
    }

    /// <summary>
    /// 实体集合快照，用于尺寸调整的撤销/重做。
    /// <para>
    /// 这些模型全部是值类型（struct），所以 <c>ToList()</c> 本身就是一份完整副本，
    /// 不需要额外深拷贝。
    /// </para>
    /// </summary>
    private sealed class EntitySnapshot
    {
        public List<Building> Buildings { get; init; } = [];
        public List<Trap> Traps { get; init; } = [];
        public List<Army> Armies { get; init; } = [];
        public List<Army_3> ArmiesV3 { get; init; } = [];
        public List<Reinforcement> Reinforcements { get; init; } = [];
        public List<Reinforcement_3> ReinforcementsV3 { get; init; } = [];
        public List<AirForce> AirForces { get; init; } = [];
        public List<Capital> Capitals { get; init; } = [];
        public List<UnitPlacement> UnitPlaces { get; init; } = [];

        public static EntitySnapshot Capture(MapData mapData) => new()
        {
            Buildings = mapData.Buildings.ToList(),
            Traps = mapData.Traps.ToList(),
            Armies = mapData.Armies.ToList(),
            ArmiesV3 = mapData.ArmiesV3.ToList(),
            Reinforcements = mapData.Reinforcements.ToList(),
            ReinforcementsV3 = mapData.ReinforcementsV3.ToList(),
            AirForces = mapData.AirForces.ToList(),
            Capitals = mapData.Capitals.ToList(),
            UnitPlaces = mapData.UnitPlaces.ToList()
        };

        public void RestoreTo(MapData mapData)
        {
            mapData.Buildings = [.. Buildings];
            mapData.Traps = [.. Traps];
            mapData.Armies = [.. Armies];
            mapData.ArmiesV3 = [.. ArmiesV3];
            mapData.Reinforcements = [.. Reinforcements];
            mapData.ReinforcementsV3 = [.. ReinforcementsV3];
            mapData.AirForces = [.. AirForces];
            mapData.Capitals = [.. Capitals];
            mapData.UnitPlaces = [.. UnitPlaces];
        }
    }
}

/// <summary>
/// 省份完整快照命令 - 用于大范围变更（比逐格记录更省内存）
/// </summary>
public sealed class ProvinceFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly Province[] _beforeSnapshot;
    private Province[]? _afterSnapshot;

    public string Description { get; }

    public ProvinceFullSnapshotCommand(MapData mapData, string description, Province[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new Province[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new Province[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = mapData.GetProvinceRef(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.GetProvinceRef(i) = _afterSnapshot[i];
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.GetProvinceRef(i) = _beforeSnapshot[i];
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 地形完整快照命令 - 用于大范围变更（比逐格记录更省内存）
/// </summary>
public sealed class TerrainFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly TerrainData[] _beforeSnapshot;
    private TerrainData[]? _afterSnapshot;

    public string Description { get; }

    public TerrainFullSnapshotCommand(MapData mapData, string description, TerrainData[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new TerrainData[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new TerrainData[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = mapData.GetTerrainRef(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.GetTerrainRef(i) = _afterSnapshot[i];
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.GetTerrainRef(i) = _beforeSnapshot[i];
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 归属变更命令 - 逐格记录归属变更
/// </summary>
public sealed class BelongChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, byte before, byte after)[] _changes;

    public string Description { get; }

    public BelongChangeCommand(MapData mapData, string description,
        (int col, int row, byte before, byte after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.SetBelongValue(col, row, after);
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.SetBelongValue(col, row, before);
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 归属完整快照命令 - 用于大范围变更
/// </summary>
public sealed class BelongFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly byte[] _beforeSnapshot;
    private byte[]? _afterSnapshot;

    public string Description { get; }

    public BelongFullSnapshotCommand(MapData mapData, string description, byte[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new byte[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new byte[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = (byte)mapData.GetBelongValueByIndex(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.SetBelongValueByIndex(i, _afterSnapshot[i]);
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.SetBelongValueByIndex(i, _beforeSnapshot[i]);
        _mapData.IsModified = true;
    }
}