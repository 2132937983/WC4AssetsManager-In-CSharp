using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WC4MapEditor.Models;

public class MapData : INotifyPropertyChanged
{
    private TerrainData[] _terrains;
    private Province[] _provinces;
    private BTLHeader _header;
    private ObservableCollection<Building> _buildings;
    private ObservableCollection<Army> _armies;
    private ObservableCollection<Legion> _legions;
    private int _mapWidth;
    private int _mapHeight;
    private string _filePath;
    private bool _isModified;

    public BTLHeader Header
    {
        get => _header;
        set { _header = value; OnPropertyChanged(); }
    }

    public ReadOnlyObservableCollection<Terrain> Terrains => new(GetTerrainCollection());
    public ReadOnlyObservableCollection<Province> Provinces => new(GetProvinceCollection());

    public ObservableCollection<Building> Buildings
    {
        get => _buildings;
        set { _buildings = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Army> Armies
    {
        get => _armies;
        set { _armies = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Legion> Legions
    {
        get => _legions;
        set { _legions = value; OnPropertyChanged(); }
    }

    public int MapWidth
    {
        get => _mapWidth;
        set { _mapWidth = value; OnPropertyChanged(); }
    }

    public int MapHeight
    {
        get => _mapHeight;
        set { _mapHeight = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    public bool IsModified
    {
        get => _isModified;
        set { _isModified = value; OnPropertyChanged(); }
    }

    public int TerrainCount => _terrains?.Length ?? 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public MapData()
    {
        _header = BTLHeader.CreateDefault();
        _terrains = Array.Empty<TerrainData>();
        _provinces = Array.Empty<Province>();
        _buildings = new ObservableCollection<Building>();
        _armies = new ObservableCollection<Army>();
        _legions = new ObservableCollection<Legion>();
        _mapWidth = 0;
        _mapHeight = 0;
        _filePath = string.Empty;
        _isModified = false;

        for (int i = 1; i <= 8; i++)
            Legions.Add(Legion.CreateDefault(i));
    }

    private ObservableCollection<Terrain> GetTerrainCollection()
    {
        var collection = new ObservableCollection<Terrain>();
        if (_terrains == null) return collection;

        for (int row = 0; row < MapHeight; row++)
        {
            for (int col = 0; col < MapWidth; col++)
            {
                TerrainData terrainData = GetTerrain(col, row);
                collection.Add(Terrain.FromTerrainData(terrainData));
            }
        }
        return collection;
    }

    private ObservableCollection<Province> GetProvinceCollection()
    {
        var collection = new ObservableCollection<Province>();
        if (_provinces == null) return collection;

        for (int row = 0; row < MapHeight; row++)
        {
            for (int col = 0; col < MapWidth; col++)
            {
                collection.Add(GetProvince(col, row));
            }
        }
        return collection;
    }

    public TerrainData GetTerrain(int col, int row)
    {
        if (col < 0 || col >= _mapWidth || row < 0 || row >= _mapHeight)
            return TerrainData.CreateDefault();
        return GetTerrain(row * _mapWidth + col);
    }

    public TerrainData GetTerrain(int index)
    {
        if (_terrains == null || index < 0 || index >= _terrains.Length)
            return TerrainData.CreateDefault();
        return _terrains[index];
    }

    public void SetTerrain(int col, int row, TerrainData terrain)
    {
        if (col < 0 || col >= _mapWidth || row < 0 || row >= _mapHeight) return;
        SetTerrain(row * _mapWidth + col, terrain);
    }

    public void SetTerrain(int index, TerrainData terrain)
    {
        if (_terrains == null || index < 0 || index >= _terrains.Length) return;
        _terrains[index] = terrain;
    }

    public Province GetProvince(int col, int row)
    {
        if (col < 0 || col >= _mapWidth || row < 0 || row >= _mapHeight)
            return Province.CreateDefault();
        return GetProvince(row * _mapWidth + col);
    }

    public Province GetProvince(int index)
    {
        if (_provinces == null || index < 0 || index >= _provinces.Length)
            return Province.CreateDefault();
        return _provinces[index];
    }

    public void SetProvince(int col, int row, Province province)
    {
        if (col < 0 || col >= _mapWidth || row < 0 || row >= _mapHeight) return;
        SetProvince(row * _mapWidth + col, province);
    }

    public void SetProvince(int index, Province province)
    {
        if (_provinces == null || index < 0 || index >= _provinces.Length) return;
        _provinces[index] = province;
    }

    public void InitializeTerrain(int width, int height)
    {
        _mapWidth = width;
        _mapHeight = height;

        int terrainCount = width * height;

        if (terrainCount <= 0)
        {
            _terrains = Array.Empty<TerrainData>();
            _provinces = Array.Empty<Province>();
            return;
        }

        _terrains = new TerrainData[terrainCount];
        for (int i = 0; i < terrainCount; i++)
            _terrains[i] = TerrainData.CreateDefault();

        _provinces = new Province[terrainCount];
        for (int i = 0; i < terrainCount; i++)
            _provinces[i] = Province.CreateDefault();
    }

    public void LoadTerrainsFromBytes(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (_terrains == null || count > _terrains.Length) return;

        for (int i = 0; i < count; i++)
        {
            int terrainOffset = offset + i * 16;
            if (terrainOffset + 16 <= data.Length)
                _terrains[i] = TerrainData.FromBytes(data, terrainOffset);
        }
    }

    public void LoadTerrainsFromBytesChunk(ReadOnlySpan<byte> buffer, int startIndex, int count)
    {
        if (_terrains == null) return;

        for (int i = 0; i < count; i++)
        {
            int terrainIndex = startIndex + i;
            if (terrainIndex >= _terrains.Length) break;

            int bufferOffset = i * 16;
            if (bufferOffset + 16 <= buffer.Length)
                _terrains[terrainIndex] = TerrainData.FromBytes(buffer, bufferOffset);
        }
    }

    public void LoadProvincesFromBytes(ReadOnlySpan<byte> data, long offset, int count)
    {
        if (_provinces == null || count > _provinces.Length) return;

        for (int i = 0; i < count; i++)
        {
            long provinceOffset = offset + (long)i * 2;
            if (provinceOffset + 2 <= data.Length)
                _provinces[i] = Province.FromBytes(data, (int)provinceOffset);
        }
    }

    public byte[] TerrainsToBytes()
    {
        if (_terrains == null) return Array.Empty<byte>();

        long resultLength = (long)_terrains.Length * 16;
        if (resultLength > int.MaxValue)
            throw new OverflowException($"地形数据太大，无法转换为字节数组。地形数: {_terrains.Length}");

        var result = new byte[(int)resultLength];

        for (int i = 0; i < _terrains.Length; i++)
        {
            _terrains[i].ToBytes(result.AsSpan(), i * 16);
        }

        return result;
    }

    public byte[] ProvincesToBytes()
    {
        if (_provinces == null) return Array.Empty<byte>();

        long resultLength = (long)_provinces.Length * 2;
        if (resultLength > int.MaxValue)
            throw new OverflowException($"省份数据太大，无法转换为字节数组。省份数: {_provinces.Length}");

        var result = new byte[(int)resultLength];

        for (int i = 0; i < _provinces.Length; i++)
        {
            _provinces[i].ToBytes(result.AsSpan(), i * 2);
        }

        return result;
    }

    public Terrain GetTerrainAt(int col, int row)
    {
        if (col < 0 || row < 0 || col >= MapWidth || row >= MapHeight)
            return Terrain.CreateDefault();
        TerrainData terrainData = GetTerrain(col, row);
        return Terrain.FromTerrainData(terrainData);
    }

    public void SetTerrainAt(int col, int row, Terrain terrain)
    {
        if (col < 0 || row < 0 || col >= MapWidth || row >= MapHeight) return;
        SetTerrain(col, row, terrain.ToTerrainData());
        IsModified = true;
    }

    public Province GetProvinceAt(int col, int row)
    {
        if (col < 0 || row < 0 || col >= MapWidth || row >= MapHeight)
            return Province.CreateDefault();
        return GetProvince(col, row);
    }

    public void SetProvinceAt(int col, int row, Province province)
    {
        if (col < 0 || row < 0 || col >= MapWidth || row >= MapHeight) return;
        SetProvince(col, row, province);
        IsModified = true;
    }

    public Building? GetBuildingAt(int col, int row)
    {
        foreach (Building building in Buildings)
        {
            HexCoord coord = HexCoord.FromIndex(building.Coordinate, MapWidth);
            if (coord.Col == col && coord.Row == row) return building;
        }
        return null;
    }

    public Army? GetArmyAt(int col, int row)
    {
        foreach (Army army in Armies)
        {
            HexCoord coord = HexCoord.FromIndex(army.Coordinate, MapWidth);
            if (coord.Col == col && coord.Row == row) return army;
        }
        return null;
    }

    public void AddBuilding(Building building)
    {
        HexCoord coord = HexCoord.FromIndex(building.Coordinate, MapWidth);
        Building? existing = GetBuildingAt(coord.Col, coord.Row);
        if (existing.HasValue) Buildings.Remove(existing.Value);
        Buildings.Add(building);
        IsModified = true;
    }

    public void AddArmy(Army army)
    {
        HexCoord coord = HexCoord.FromIndex(army.Coordinate, MapWidth);
        Army? existing = GetArmyAt(coord.Col, coord.Row);
        if (existing.HasValue) Armies.Remove(existing.Value);
        Armies.Add(army);
        IsModified = true;
    }

    public void RemoveBuildingAt(int col, int row)
    {
        Building? building = GetBuildingAt(col, row);
        if (building.HasValue)
        {
            Buildings.Remove(building.Value);
            IsModified = true;
        }
    }

    public void RemoveArmyAt(int col, int row)
    {
        Army? army = GetArmyAt(col, row);
        if (army.HasValue)
        {
            Armies.Remove(army.Value);
            IsModified = true;
        }
    }

    public void Resize(int newWidth, int newHeight)
    {
        int oldWidth = MapWidth;
        int oldHeight = MapHeight;

        var newTerrains = new TerrainData[newWidth * newHeight];
        var newProvinces = new Province[newWidth * newHeight];

        for (int i = 0; i < newTerrains.Length; i++)
            newTerrains[i] = TerrainData.CreateDefault();
        for (int i = 0; i < newProvinces.Length; i++)
            newProvinces[i] = Province.CreateDefault();

        for (int row = 0; row < Math.Min(oldHeight, newHeight); row++)
        {
            for (int col = 0; col < Math.Min(oldWidth, newWidth); col++)
            {
                int oldIndex = row * oldWidth + col;
                int newIndex = row * newWidth + col;
                newTerrains[newIndex] = _terrains[oldIndex];
                newProvinces[newIndex] = _provinces[oldIndex];
            }
        }

        _terrains = newTerrains;
        _provinces = newProvinces;
        MapWidth = newWidth;
        MapHeight = newHeight;

        Header.MapWidth = newWidth;
        Header.MapLength = newHeight;

        IsModified = true;
    }

    public void ClearSelection()
    {
        // 结构体不可变，清除选择状态由渲染层处理
    }

    public List<Army> GetArmiesByLegion(int legionId)
    {
        return Armies.Where(a => a.LegionId == legionId).ToList();
    }

    public List<Building> GetBuildingsByLegion(int legionId)
    {
        return new List<Building>();
    }

    public double GetMemoryUsageMB()
    {
        long bytes = 0;
        if (_terrains != null) bytes += (long)_terrains.Length * 16;
        if (_provinces != null) bytes += (long)_provinces.Length * 2;
        return bytes / (1024.0 * 1024.0);
    }

    public void Clear()
    {
        _terrains = Array.Empty<TerrainData>();
        _provinces = Array.Empty<Province>();
        _buildings?.Clear();
        _armies?.Clear();
        _legions?.Clear();
        _mapWidth = 0;
        _mapHeight = 0;

        Debug.WriteLine("[MapData] 地图数据已清空");
    }
}
