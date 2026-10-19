using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WC4MapEditor.Core.Models;

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
    private bool _legionTrackingAttached;
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
    private int _mapWidth;
    private int _mapHeight;
    private byte[] _extensionData = Array.Empty<byte>();
    private string _filePath;
    private bool _isModified;

    public BTLHeader Header
    {
        get => _header;
        set { _header = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Building> Buildings
    {
        get => _buildings;
        // 解析器 / 撤销会整体替换集合，坐标索引必须跟着作废，否则会按旧下标找建筑
        set { _buildings = value; _buildingCoordIndex.Clear(); OnPropertyChanged(); }
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

    /// <summary>
    /// 军团集合。读写都会确保挂上变更订阅，用于把军团数量同步到
    /// <see cref="BTLHeader.ArmyCount"/>（见 <see cref="OnLegionsChanged"/>）。
    /// </summary>
    public ObservableCollection<Legion> Legions
    {
        get
        {
            EnsureLegionTracking();
            return _legions;
        }
        set
        {
            if (_legions != null)
                _legions.CollectionChanged -= OnLegionsChanged;

            _legions = value;
            _legionTrackingAttached = false;
            EnsureLegionTracking();

            OnPropertyChanged();
        }
    }

    /// <summary>幂等地给当前军团集合挂上变更订阅。</summary>
    private void EnsureLegionTracking()
    {
        if (_legions == null || _legionTrackingAttached) return;

        _legions.CollectionChanged += OnLegionsChanged;
        _legionTrackingAttached = true;
    }

    /// <summary>
    /// 军团集合增删时把数量同步到文件头 <see cref="BTLHeader.ArmyCount"/>。
    /// <para>
    /// 此前该字段只在保存（<c>StageParser.SaveFromMapData</c>）时才更新，
    /// 于是"新增/删除军团后，内存里的 ArmyCount 仍是旧值"，
    /// 任何按它判断的环节都会漏掉新军团。
    /// </para>
    /// <para>
    /// 整体替换集合（加载地图时 <c>mapData.Legions = new ...</c>）不会触发该事件，
    /// 因此不会污染刚从文件解析出来的 ArmyCount。
    /// </para>
    /// </summary>
    private void OnLegionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_header != null && _legions != null)
            _header.ArmyCount = _legions.Count;
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

    /// <summary>
    /// 文件末尾的扩展数据块（头部 0x54 给出长度，v3 文件里是 protobuf 编码的数据）。
    /// 编辑器不解读它，加载时原样取出、保存时原样写回。
    /// </summary>
    public byte[] ExtensionData
    {
        get => _extensionData;
        set { _extensionData = value; OnPropertyChanged(); }
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

    /// <summary>
    /// 地图坐标 → 归属数组索引，返回 -1 表示该格不在归属数据范围内。
    /// <para>
    /// 普通地图的归属数组与网格一一对应（row * MapWidth + col）。归属数组只覆盖一块数据窗口的地图
    /// （征服布局画在世界地图上，见 <c>ConquestWorldMapData</c>）会重写本方法，按窗口规则换算。
    /// 归属 / 军团 / 渲染一律经本方法取归属，不要自己用 MapClip 推算。
    /// </para>
    /// </summary>
    public virtual int GetBelongIndex(int col, int row)
    {
        if (col < 0 || row < 0 || col >= _mapWidth || row >= _mapHeight) return -1;
        return row * _mapWidth + col;
    }

    /// <summary>
    /// 该格是否在归属数据范围内（征服地图 MapClip 裁剪区以外、或超出归属数组的格子返回 false）。
    /// 编辑器用它给"数据范围外"的格子画红叉，提示这些格子的归属 / 实体不会被游戏读取。
    /// </summary>
    public bool IsInBelongRange(int col, int row)
    {
        int index = GetBelongIndex(col, row);
        return index >= 0 && _belongs != null && index < _belongs.Count;
    }

    public int GetBelongValue(int col, int row)
    {
        int index = GetBelongIndex(col, row);
        return index < 0 ? 0xFF : GetBelongValueByIndex(index);
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
        int index = GetBelongIndex(col, row);
        return index >= 0 && SetBelongValueByIndex(index, belongValue);
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
            _provinces[i] = Province.CreateDefault();
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
        _filePath = string.Empty;
        _isModified = false;

        for (int i = 1; i <= 8; i++)
            Legions.Add(Legion.CreateDefault(i));
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
            HexCoord coord = HexCoord.FromIndex(army.CoordinateIndex, MapWidth);
            if (coord.Col == col && coord.Row == row) return army;
        }
        return null;
    }

    public Army_3? GetArmyV3At(int col, int row)
    {
        foreach (Army_3 army in ArmiesV3)
        {
            HexCoord coord = HexCoord.FromIndex(army.CoordinateIndex, MapWidth);
            if (coord.Col == col && coord.Row == row) return army;
        }
        return null;
    }

    /// <summary>
    /// 获取指定格子所在省份的「省会格子」归属值。
    /// ProvinceValue 即省会格子的地图索引；无省区或越界时返回 -1。
    /// 放置单位、放置陷阱、批量生成陷阱都依赖它确定归属。
    /// </summary>
    public int GetProvinceCapitalBelong(int col, int row)
    {
        if (col < 0 || col >= _mapWidth || row < 0 || row >= _mapHeight) return -1;

        var province = GetProvinceRef(col, row);
        if (province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF) return -1;

        int capitalIndex = province.ProvinceValue;
        if (capitalIndex < 0 || capitalIndex >= _mapWidth * _mapHeight) return -1;

        var capitalCoord = HexCoord.FromIndex(capitalIndex, MapWidth);
        return GetBelongValue(capitalCoord.Col, capitalCoord.Row);
    }

    /// <summary>在建筑所在格放置建筑：该格已有建筑则替换，否则追加（坐标索引同步维护）。</summary>
    public void AddBuilding(Building building)
    {
        HexCoord coord = HexCoord.FromIndex(building.Coordinate, MapWidth);
        int idx = FindBuildingIndex(coord.Col, coord.Row);
        if (idx >= 0)
        {
            ReplaceBuilding(idx, building);
        }
        else
        {
            _buildings.Add(building);
            if (_buildingCoordIndex.Count > 0) _buildingCoordIndex[building.Coordinate] = _buildings.Count - 1;
        }
        IsModified = true;
    }

    public void AddArmy(Army army)
    {
        HexCoord coord = HexCoord.FromIndex(army.CoordinateIndex, MapWidth);
        int idx = FindArmyIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _armies[idx] = army;
        else
            _armies.Add(army);
        IsModified = true;
    }

    public void AddArmyV3(Army_3 army)
    {
        HexCoord coord = HexCoord.FromIndex(army.CoordinateIndex, MapWidth);
        int idx = FindArmyV3Index(coord.Col, coord.Row);
        if (idx >= 0)
            _armiesV3[idx] = army;
        else
            _armiesV3.Add(army);
        IsModified = true;
    }

    /// <summary>删除指定格上的建筑（走按下标删除，坐标索引同步失效）。</summary>
    public void RemoveBuildingAt(int col, int row)
    {
        int idx = FindBuildingIndex(col, row);
        if (idx >= 0)
        {
            RemoveBuildingAt(idx);
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
        // 结构体不可变，清除选择状态由渲染层处理（保留接口与 VB MapData 对齐）
    }

    /// <summary>
    /// 指定军团的所有 v1/v2 单位：单位所在格的归属值 = 军团下标。
    /// （单位的 <c>LegionId</c> 只是编辑器内部字段，不存进文件，读档后是 -1，不能用来判断归属。）
    /// </summary>
    public List<Army> GetArmiesByLegion(int legionId)
    {
        var result = new List<Army>();
        if (_mapWidth <= 0) return result;
        foreach (var a in Armies)
        {
            int c = a.CoordinateIndex;
            if (GetBelongValue(c % _mapWidth, c / _mapWidth) == legionId) result.Add(a);
        }
        return result;
    }

    /// <summary>
    /// 指定军团的所有建筑。
    /// <para>
    /// VB 版这里是个 TODO 空实现（省区不再存军团 ID）。现在的归属关系是"格子归属值 = 军团下标"，
    /// 所以建筑所在格的归属值等于 <paramref name="legionId"/> 即视为该军团的建筑。
    /// </para>
    /// </summary>
    public List<Building> GetBuildingsByLegion(int legionId)
    {
        var result = new List<Building>();
        if (_mapWidth <= 0) return result;
        foreach (var b in _buildings)
        {
            if (GetBelongValue(b.Coordinate % _mapWidth, b.Coordinate / _mapWidth) == legionId)
                result.Add(b);
        }
        return result;
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
        _extensionData = Array.Empty<byte>();
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
        // 索引可能过期（搬动建筑 / 外部直接改集合），命中后核对一次坐标，不对就走线性查找并修正
        if (_buildingCoordIndex.Count > 0 && _buildingCoordIndex.TryGetValue(coordIndex, out int idx))
        {
            if ((uint)idx < (uint)_buildings.Count && _buildings[idx].Coordinate == coordIndex) return idx;
            _buildingCoordIndex.Remove(coordIndex);
        }
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (_buildings[i].Coordinate == coordIndex)
            {
                if (_buildingCoordIndex.Count > 0) _buildingCoordIndex[coordIndex] = i;
                return i;
            }
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
            if (_armies[i].CoordinateIndex == coordIndex) return i;
        }
        return -1;
    }

    public int FindArmyV3Index(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _armiesV3.Count; i++)
        {
            if (_armiesV3[i].CoordinateIndex == coordIndex) return i;
        }
        return -1;
    }

    public int FindTrapIndex(int col, int row)
    {
        int coordIndex = row * _mapWidth + col;
        for (int i = 0; i < _traps.Count; i++)
        {
            if (_traps[i].CellIndex == coordIndex) return i;
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

    /// <summary>
    /// 把<b>归属值</b>解析成军团。
    /// <para>
    /// 归属值的约定是"军团在 <see cref="Legions"/> 中的索引"（与
    /// LegionDomainRender.GetLegionColor、BelongFlagRender 的解释一致），所以先按索引取。
    /// 个别地图可能把 CountryId 写进了归属值，索引越界时再按 CountryId 兜底查一次。
    /// </para>
    /// <para>
    /// <b>为什么要收口成这个方法</b>：以前各处直接写 <c>FindLegionIndex(belong)</c>，
    /// 而那个方法是按 CountryId 匹配的 —— 拿索引当 CountryId 查，绝大多数都查不到并返回 -1，
    /// 于是调用方把该省区/格子当成"没有军团"跳过，表现为"截图颜色对不上、很多军团没颜色"。
    /// </para>
    /// </summary>
    /// <returns>找不到时返回 null</returns>
    public Legion? ResolveLegionByBelong(int belongValue)
    {
        if (belongValue < 0) return null;

        if (belongValue < _legions.Count)
            return _legions[belongValue];

        int index = FindLegionIndex(belongValue);
        return index >= 0 ? _legions[index] : null;
    }

    public void ReplaceBuilding(int index, Building building)
    {
        if ((uint)index < (uint)_buildings.Count)
        {
            int oldCoord = _buildings[index].Coordinate;
            _buildings[index] = building;
            if (_buildingCoordIndex.Count > 0)
            {
                // 建筑搬到新格子：旧格子的索引项必须删掉，否则旧位置仍会"找到"这栋建筑
                if (oldCoord != building.Coordinate && _buildingCoordIndex.TryGetValue(oldCoord, out int oldIdx) && oldIdx == index)
                    _buildingCoordIndex.Remove(oldCoord);
                _buildingCoordIndex[building.Coordinate] = index;
            }
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