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
    private Dictionary<int, int> _buildingCoordIndex = new();
    private ObservableCollection<Army> _armies;
    private ObservableCollection<Army_3> _armiesV3;
    private ObservableCollection<Legion> _legions;
    private ObservableCollection<Trap> _traps;
    private ObservableCollection<Reinforcement> _reinforcements;
    private ObservableCollection<Reinforcement_3> _reinforcementsV3;
    private ObservableCollection<Capital> _capitals;
    private List<string> _belongs;
    private ObservableCollection<MapCase> _cases;
    private ObservableCollection<Weather> _weathers;
    private ObservableCollection<MapEvent> _events;
    private ObservableCollection<AirForce> _airForces;
    private ObservableCollection<UnitPlacement> _unitPlaces;
    private ObservableCollection<StrategicConstruction> _strategyConstructions;
    private ObservableCollection<AirSupport> _airSupports;
    private bool _belongOffset;
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

    public ObservableCollection<Army_3> ArmiesV3
    {
        get => _armiesV3;
        set { _armiesV3 = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Legion> Legions
    {
        get => _legions;
        set { _legions = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Trap> Traps
    {
        get => _traps;
        set { _traps = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Reinforcement> Reinforcements
    {
        get => _reinforcements;
        set { _reinforcements = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Reinforcement_3> ReinforcementsV3
    {
        get => _reinforcementsV3;
        set { _reinforcementsV3 = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Capital> Capitals
    {
        get => _capitals;
        set { _capitals = value; OnPropertyChanged(); }
    }

    public List<string> Belongs
    {
        get => _belongs;
        set { _belongs = value; OnPropertyChanged(); }
    }

    public ObservableCollection<MapCase> Cases
    {
        get => _cases;
        set { _cases = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Weather> Weathers
    {
        get => _weathers;
        set { _weathers = value; OnPropertyChanged(); }
    }

    public ObservableCollection<MapEvent> Events
    {
        get => _events;
        set { _events = value; OnPropertyChanged(); }
    }

    public ObservableCollection<AirForce> AirForces
    {
        get => _airForces;
        set { _airForces = value; OnPropertyChanged(); }
    }

    public ObservableCollection<UnitPlacement> UnitPlaces
    {
        get => _unitPlaces;
        set { _unitPlaces = value; OnPropertyChanged(); }
    }

    public ObservableCollection<StrategicConstruction> StrategyConstructions
    {
        get => _strategyConstructions;
        set { _strategyConstructions = value; OnPropertyChanged(); }
    }

    public ObservableCollection<AirSupport> AirSupports
    {
        get => _airSupports;
        set { _airSupports = value; OnPropertyChanged(); }
    }

    public bool BelongOffset
    {
        get => _belongOffset;
        set { _belongOffset = value; OnPropertyChanged(); }
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

    public int GetBelongValue(int col, int row)
    {
        int index = row * _mapWidth + col;
        return GetBelongValueByIndex(index);
    }

    public int GetBelongValueByIndex(int index)
    {
        if (_belongs == null || index < 0 || index >= _belongs.Count) return 0xFF;
        string s = _belongs[index];
        if (s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(s.Substring(2), 16);
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : 0xFF;
    }

    public bool SetBelongValue(int col, int row, int belongValue)
    {
        int index = row * _mapWidth + col;
        return SetBelongValueByIndex(index, belongValue);
    }

    public bool SetBelongValueByIndex(int index, int belongValue)
    {
        if (_belongs == null || index < 0 || index >= _belongs.Count) return false;
        _belongs[index] = ((byte)Math.Clamp(belongValue, 0, 255)).ToString("X2");
        return true;
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
        _armiesV3 = new ObservableCollection<Army_3>();
        _legions = new ObservableCollection<Legion>();
        _traps = new ObservableCollection<Trap>();
        _reinforcements = new ObservableCollection<Reinforcement>();
        _reinforcementsV3 = new ObservableCollection<Reinforcement_3>();
        _capitals = new ObservableCollection<Capital>();
        _belongs = new List<string>();
        _cases = new ObservableCollection<MapCase>();
        _weathers = new ObservableCollection<Weather>();
        _events = new ObservableCollection<MapEvent>();
        _airForces = new ObservableCollection<AirForce>();
        _unitPlaces = new ObservableCollection<UnitPlacement>();
        _strategyConstructions = new ObservableCollection<StrategicConstruction>();
        _airSupports = new ObservableCollection<AirSupport>();
        _belongOffset = false;
        _mapWidth = 0;
        _mapHeight = 0;
        _filePath = string.Empty;
        _isModified = false;

        for (int i = 1; i <= 8; i++)
            Legions.Add(Legion.CreateDefault(i));
    }

    public MapData(int width, int height)
    {
        _header = BTLHeader.CreateDefault();
        _mapWidth = width;
        _mapHeight = height;
        _terrains = new TerrainData[width * height];
        _provinces = new Province[width * height];
        for (int i = 0; i < _provinces.Length; i++)
            _provinces[i] = new Province();
        _buildings = new ObservableCollection<Building>();
        _armies = new ObservableCollection<Army>();
        _armiesV3 = new ObservableCollection<Army_3>();
        _legions = new ObservableCollection<Legion>();
        _traps = new ObservableCollection<Trap>();
        _reinforcements = new ObservableCollection<Reinforcement>();
        _reinforcementsV3 = new ObservableCollection<Reinforcement_3>();
        _capitals = new ObservableCollection<Capital>();
        _belongs = new List<string>();
        _cases = new ObservableCollection<MapCase>();
        _weathers = new ObservableCollection<Weather>();
        _events = new ObservableCollection<MapEvent>();
        _airForces = new ObservableCollection<AirForce>();
        _unitPlaces = new ObservableCollection<UnitPlacement>();
        _strategyConstructions = new ObservableCollection<StrategicConstruction>();
        _airSupports = new ObservableCollection<AirSupport>();
        _belongOffset = false;
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

    public Army_3? GetArmyV3At(int col, int row)
    {
        foreach (Army_3 army in ArmiesV3)
        {
            HexCoord coord = HexCoord.FromIndex(army.Coordinate, MapWidth);
            if (coord.Col == col && coord.Row == row) return army;
        }
        return null;
    }

    public void AddBuilding(Building building)
    {
        HexCoord coord = HexCoord.FromIndex(building.Coordinate, MapWidth);
        int idx = FindBuildingIndex(coord.Col, coord.Row);
        if (idx >= 0)
        {
            _buildings[idx] = building;
            _buildingCoordIndex[building.Coordinate] = idx;
        }
        else
        {
            _buildings.Add(building);
            _buildingCoordIndex[building.Coordinate] = _buildings.Count - 1;
        }
        IsModified = true;
    }

    public void AddArmy(Army army)
    {
        HexCoord coord = HexCoord.FromIndex(army.Coordinate, MapWidth);
        int idx = FindArmyIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _armies[idx] = army;
        else
            _armies.Add(army);
        IsModified = true;
    }

    public void AddArmyV3(Army_3 army)
    {
        HexCoord coord = HexCoord.FromIndex(army.Coordinate, MapWidth);
        int idx = FindArmyV3Index(coord.Col, coord.Row);
        if (idx >= 0)
            _armiesV3[idx] = army;
        else
            _armiesV3.Add(army);
        IsModified = true;
    }

    public void RemoveBuildingAt(int col, int row)
    {
        int idx = FindBuildingIndex(col, row);
        if (idx >= 0)
        {
            _buildings.RemoveAt(idx);
            IsModified = true;
        }
    }

    public void RemoveArmyAt(int col, int row)
    {
        int idx = FindArmyIndex(col, row);
        if (idx >= 0)
        {
            _armies.RemoveAt(idx);
            IsModified = true;
        }
    }

    public void RemoveArmyV3At(int col, int row)
    {
        int idx = FindArmyV3Index(col, row);
        if (idx >= 0)
        {
            _armiesV3.RemoveAt(idx);
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

        Header.MapLength = newWidth;
        Header.MapWidth = newHeight;

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
        _armiesV3?.Clear();
        _legions?.Clear();
        _capitals?.Clear();
        _belongs?.Clear();
        _cases?.Clear();
        _weathers?.Clear();
        _events?.Clear();
        _airForces?.Clear();
        _unitPlaces?.Clear();
        _strategyConstructions?.Clear();
        _airSupports?.Clear();
        _traps?.Clear();
        _reinforcements?.Clear();
        _reinforcementsV3?.Clear();
        _belongOffset = false;
        _mapWidth = 0;
        _mapHeight = 0;

        Debug.WriteLine("[MapData] 地图数据已清空");
    }

    #region 高效索引访问 - ref返回零拷贝

    public ref TerrainData GetTerrainRef(int col, int row)
    {
        return ref _terrains[row * _mapWidth + col];
    }

    public ref TerrainData GetTerrainRef(int index)
    {
        return ref _terrains[index];
    }

    public ref Province GetProvinceRef(int col, int row)
    {
        return ref _provinces[row * _mapWidth + col];
    }

    public ref Province GetProvinceRef(int index)
    {
        return ref _provinces[index];
    }

    #endregion

    #region 高效集合操作 - 索引查找/替换/删除

    public int FindBuildingIndex(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        if (_buildingCoordIndex.Count > 0 && _buildingCoordIndex.TryGetValue(coordIndex, out int idx))
            return idx;
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (_buildings[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }

    public void RebuildBuildingCoordIndex()
    {
        _buildingCoordIndex = new Dictionary<int, int>(_buildings.Count);
        for (int i = 0; i < _buildings.Count; i++)
            _buildingCoordIndex[_buildings[i].Coordinate] = i;
    }

    public void InvalidateBuildingCoordIndex()
    {
        _buildingCoordIndex.Clear();
    }

    public void AddToBuildingCoordIndex(int coordIndex, int listIndex)
    {
        _buildingCoordIndex[coordIndex] = listIndex;
    }

    public int FindArmyIndex(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _armies.Count; i++)
        {
            if (_armies[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }

    public int FindArmyV3Index(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _armiesV3.Count; i++)
        {
            if (_armiesV3[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }

    public int FindTrapIndex(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _traps.Count; i++)
        {
            if (_traps[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }

    public int FindReinforcementIndex(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _reinforcements.Count; i++)
        {
            if (_reinforcements[i].Coordinate == coordIndex) return i;
        }
        return -1;
    }

    public int FindLegionIndex(int countryId)
    {
        for (int i = 0; i < _legions.Count; i++)
        {
            if (_legions[i].CountryId == countryId) return i;
        }
        return -1;
    }

    public void ReplaceBuilding(int index, Building building)
    {
        if ((uint)index < (uint)_buildings.Count)
        {
            _buildings[index] = building;
            _buildingCoordIndex[building.Coordinate] = index;
        }
    }

    public void ReplaceArmy(int index, Army army)
    {
        if ((uint)index < (uint)_armies.Count) _armies[index] = army;
    }

    public void ReplaceArmyV3(int index, Army_3 army)
    {
        if ((uint)index < (uint)_armiesV3.Count) _armiesV3[index] = army;
    }

    public void ReplaceTrap(int index, Trap trap)
    {
        if ((uint)index < (uint)_traps.Count) _traps[index] = trap;
    }

    public void ReplaceReinforcement(int index, Reinforcement reinforcement)
    {
        if ((uint)index < (uint)_reinforcements.Count) _reinforcements[index] = reinforcement;
    }

    public void ReplaceLegion(int index, Legion legion)
    {
        if ((uint)index < (uint)_legions.Count) _legions[index] = legion;
    }

    public void RemoveBuildingAt(int index)
    {
        if ((uint)index < (uint)_buildings.Count)
        {
            _buildingCoordIndex.Remove(_buildings[index].Coordinate);
            _buildings.RemoveAt(index);
            _buildingCoordIndex.Clear();
        }
    }

    public void RemoveArmyAt(int index)
    {
        if ((uint)index < (uint)_armies.Count) _armies.RemoveAt(index);
    }

    public void RemoveArmyV3At(int index)
    {
        if ((uint)index < (uint)_armiesV3.Count) _armiesV3.RemoveAt(index);
    }

    public void RemoveTrapAt(int index)
    {
        if ((uint)index < (uint)_traps.Count) _traps.RemoveAt(index);
    }

    public void RemoveReinforcementAt(int index)
    {
        if ((uint)index < (uint)_reinforcements.Count) _reinforcements.RemoveAt(index);
    }

    public void RemoveReinforcementV3At(int index)
    {
        if ((uint)index < (uint)_reinforcementsV3.Count) _reinforcementsV3.RemoveAt(index);
    }

    #endregion

    #region 兼容性查询接口 (保留旧方法供渲染层使用)

    public Legion? GetLegionByCountryId(int countryId)
    {
        int idx = FindLegionIndex(countryId);
        return idx >= 0 ? _legions[idx] : null;
    }

    public Trap? GetTrapAt(int col, int row)
    {
        int idx = FindTrapIndex(col, row);
        return idx >= 0 ? _traps[idx] : null;
    }

    public Reinforcement? GetReinforcementAt(int col, int row)
    {
        int idx = FindReinforcementIndex(col, row);
        return idx >= 0 ? _reinforcements[idx] : null;
    }

    #endregion
}