using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;

namespace WC4MapEditor.Core.Parsers.Conquest;

/// <summary>
/// 征服模式解析器。
/// 纯文件 I/O 工具，直接操作 MapData，不持有运行时数据。
/// </summary>
public class ConquestParser
{
    public byte[] HexData { get; private set; } = null!;
    public string HexFilePath { get; private set; } = "";
    public BTLHeader Header { get; private set; } = null!;
    public int BtlVersion => Header?.BtlVersion ?? 0;

    public List<Legion> Legions { get; } = new();
    public List<Province> Provinces { get; } = new();
    public List<string> Belongs { get; } = new();
    public List<Building> Buildings { get; } = new();
    public List<Army> Armies { get; } = new();
    public List<Army_3> ArmiesV3 { get; } = new();
    public List<Trap> Traps { get; } = new();
    public List<MapCase> Cases { get; } = new();
    public List<Weather> Weathers { get; } = new();
    public List<MapEvent> Events { get; } = new();
    public List<Reinforcement> Reinforcements { get; } = new();
    public List<Reinforcement_3> ReinforcementsV3 { get; } = new();
    public List<AirForce> AirForces { get; } = new();
    public List<UnitPlacement> UnitPlaces { get; } = new();
    public List<Capital> Capitals { get; } = new();
    public List<StrategicConstruction> StrategyConstructions { get; } = new();
    public List<AirSupport> AirSupports { get; } = new();

    private bool _legionsLoaded;
    private bool _provincesLoaded;
    private bool _belongsLoaded;
    private bool _buildingsLoaded;
    private bool _armiesLoaded;
    private bool _trapsLoaded;
    private bool _casesLoaded;
    private bool _weathersLoaded;
    private bool _eventsLoaded;
    private bool _reinforcementsLoaded;
    private bool _airForcesLoaded;
    private bool _unitPlacesLoaded;
    private bool _capitalsLoaded;
    private bool _strategyLoaded;
    private bool _airSupportsLoaded;

    public ConquestParser() { }

    public ConquestParser(string hexFilePath)
    {
        LoadHexFile(hexFilePath);
    }

    public ConquestParser(byte[] hexData)
    {
        HexData = hexData;
        ParseAllData();
    }

    public bool LoadHexFile(string filePath)
    {
        try
        {
            HexData = File.ReadAllBytes(filePath);
            HexFilePath = filePath;
            Debug.WriteLine($"[ConquestParser] 成功加载: {filePath}, 大小: {HexData.Length} 字节");
            ParseAllData();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquestParser] 加载失败: {ex.Message}");
            return false;
        }
    }

    private void ParseAllData()
    {
        ResetLoadFlags();
        Header = null!;
        GetHeaderData();
    }

    private void ResetLoadFlags()
    {
        _legionsLoaded = false;
        _provincesLoaded = false;
        _belongsLoaded = false;
        _buildingsLoaded = false;
        _armiesLoaded = false;
        _trapsLoaded = false;
        _casesLoaded = false;
        _weathersLoaded = false;
        _eventsLoaded = false;
        _reinforcementsLoaded = false;
        _airForcesLoaded = false;
        _unitPlacesLoaded = false;
        _capitalsLoaded = false;
        _strategyLoaded = false;
        _airSupportsLoaded = false;
    }

    [MemberNotNull(nameof(Header))]
    public BTLHeader GetHeaderData()
    {
        if (Header == null)
        {
            Header = BTLHeaderModule.Parse(HexData);
        }
        return Header;
    }

    public List<Legion> GetLegionData()
    {
        if (_legionsLoaded) return Legions;
        if (Header == null) GetHeaderData();

        Legions.Clear();
        Legions.AddRange(BTLLegionModule.Parse(HexData, Header.ArmyCount));
        _legionsLoaded = true;
        return Legions;
    }

    /// <summary>按文件头排出的数据块地址表（征服布局：没有地形块）。</summary>
    private BTLLayout Layout
    {
        get
        {
            if (Header == null) GetHeaderData();
            return BTLLayout.ForConquest(Header);
        }
    }

    public List<Province> GetProvinceData()
    {
        if (_provincesLoaded) return Provinces;
        if (!_legionsLoaded) GetLegionData();

        var span = Layout[BtlBlock.Province];
        Provinces.Clear();
        Provinces.AddRange(BTLProvinceModule.Parse(HexData, span.StartInt, span.Count));
        _provincesLoaded = true;
        return Provinces;
    }

    public List<string> GetBelongData()
    {
        if (_belongsLoaded) return Belongs;
        if (!_provincesLoaded) GetProvinceData();

        var span = Layout[BtlBlock.Belong];
        Belongs.Clear();
        Belongs.AddRange(BTLBelongModule.Parse(HexData, span.StartInt, span.Count));
        _belongsLoaded = true;
        return Belongs;
    }

    public List<Building> GetBuildingData()
    {
        if (_buildingsLoaded) return Buildings;
        if (!_belongsLoaded) GetBelongData();

        var span = Layout[BtlBlock.Building];
        Buildings.Clear();
        Buildings.AddRange(BTLBuildingModule.Parse(HexData, span.StartInt, span.Count));
        _buildingsLoaded = true;
        return Buildings;
    }

    public List<Army> GetTroopData()
    {
        if (_armiesLoaded) return Armies;
        if (!_buildingsLoaded) GetBuildingData();

        var span = Layout[BtlBlock.Army];
        Armies.Clear();
        ArmiesV3.Clear();

        var (v1, v3) = BTLArmyModule.Parse(HexData, span.StartInt, span.Count, BtlVersion);
        Armies.AddRange(v1);
        ArmiesV3.AddRange(v3);
        _armiesLoaded = true;
        return Armies;
    }

    public List<Trap> GetTrapData()
    {
        if (_trapsLoaded) return Traps;
        if (!_armiesLoaded) GetTroopData();

        var span = Layout[BtlBlock.Trap];
        Traps.Clear();
        Traps.AddRange(BTLTrapModule.Parse(HexData, span.StartInt, span.Count));
        _trapsLoaded = true;
        return Traps;
    }

    public List<MapCase> GetCaseData()
    {
        if (_casesLoaded) return Cases;
        if (!_trapsLoaded) GetTrapData();

        var span = Layout[BtlBlock.Case];
        Cases.Clear();
        Cases.AddRange(BTLCaseModule.Parse(HexData, span.StartInt, span.Count));
        _casesLoaded = true;
        return Cases;
    }

    public List<Weather> GetWeatherData()
    {
        if (_weathersLoaded) return Weathers;
        if (!_casesLoaded) GetCaseData();

        var span = Layout[BtlBlock.Weather];
        Weathers.Clear();
        Weathers.AddRange(BTLWeatherModule.Parse(HexData, span.StartInt, span.Count));
        _weathersLoaded = true;
        return Weathers;
    }

    public List<MapEvent> GetEventData()
    {
        if (_eventsLoaded) return Events;
        if (!_weathersLoaded) GetWeatherData();

        var span = Layout[BtlBlock.Event];
        Events.Clear();
        Events.AddRange(BTLEventModule.Parse(HexData, span.StartInt, span.Count));
        _eventsLoaded = true;
        return Events;
    }

    public List<Reinforcement> GetReinforcementData()
    {
        if (_reinforcementsLoaded) return Reinforcements;
        if (!_eventsLoaded) GetEventData();

        var span = Layout[BtlBlock.Reinforcement];
        Reinforcements.Clear();
        ReinforcementsV3.Clear();

        var (v1, v3) = BTLReinforcementModule.Parse(HexData, span.StartInt, span.Count, BtlVersion);
        Reinforcements.AddRange(v1);
        ReinforcementsV3.AddRange(v3);
        _reinforcementsLoaded = true;
        return Reinforcements;
    }

    public List<AirForce> GetAirForceData()
    {
        if (_airForcesLoaded) return AirForces;
        if (!_reinforcementsLoaded) GetReinforcementData();

        var span = Layout[BtlBlock.AirRaid];
        AirForces.Clear();
        AirForces.AddRange(BTLAirForceModule.Parse(HexData, span.StartInt, span.Count));
        _airForcesLoaded = true;
        return AirForces;
    }

    public List<UnitPlacement> GetUnitPlaceData()
    {
        if (_unitPlacesLoaded) return UnitPlaces;
        if (!_airForcesLoaded) GetAirForceData();

        var span = Layout[BtlBlock.UnitPlacement];
        UnitPlaces.Clear();
        UnitPlaces.AddRange(BTLUnitPlacementModule.Parse(HexData, span.StartInt, span.Count));
        _unitPlacesLoaded = true;
        return UnitPlaces;
    }

    public List<Capital> GetCapitalData()
    {
        if (_capitalsLoaded) return Capitals;
        if (!_unitPlacesLoaded) GetUnitPlaceData();

        var span = Layout[BtlBlock.Capital];
        Capitals.Clear();
        Capitals.AddRange(BTLCapitalModule.Parse(HexData, span.StartInt, span.Count));
        _capitalsLoaded = true;
        return Capitals;
    }

    public List<StrategicConstruction> GetStrategyConstructionData()
    {
        if (_strategyLoaded) return StrategyConstructions;
        if (!_capitalsLoaded) GetCapitalData();

        var span = Layout[BtlBlock.Strategy];
        StrategyConstructions.Clear();
        StrategyConstructions.AddRange(BTLStrategyModule.Parse(HexData, span.StartInt, span.Count));
        _strategyLoaded = true;
        return StrategyConstructions;
    }

    public List<AirSupport> GetAirSupportData()
    {
        if (_airSupportsLoaded) return AirSupports;
        if (!_strategyLoaded) GetStrategyConstructionData();

        var span = Layout[BtlBlock.AirSupport];
        AirSupports.Clear();
        AirSupports.AddRange(BTLAirSupportModule.Parse(HexData, span.StartInt, span.Count));
        _airSupportsLoaded = true;
        return AirSupports;
    }

    /// <summary>文件末尾、最后一个数据块之后的字节（扩展数据块），原样取出；没有则为空。</summary>
    public byte[] GetExtensionData()
    {
        long start = Layout.End;
        return start < HexData.Length ? HexData[(int)start..] : Array.Empty<byte>();
    }

    #region 直接操作 MapData 的静态方法 - 消除数据重复

    /// <summary>
    /// 从文件加载所有数据到 MapData，Parser 不持有数据副本。
    /// <para>
    /// 数据窗口布局：网格 = MapLength x MapWidth（只有窗口本身），地形空白，省份按文件顺序摆放。
    /// CLI 与无界面工具走这条路径（归属数组与网格一一对应，实体坐标仍是世界索引）；界面上的征服场景改用
    /// <see cref="LoadOnWorldMap"/> 把征服画在世界地图上。
    /// </para>
    /// </summary>
    public static MapData LoadToMapData(string filePath)
    {
        var parser = new ConquestParser(filePath);
        var mapData = new MapData();
        PopulateMapData(parser, mapData);
        mapData.FilePath = filePath;
        return mapData;
    }

    /// <summary>
    /// 以世界地图为底图加载征服文件（对应 VB ConquestRenderScene.Create + FillConquestDataToMapData）。
    /// <para>
    /// 网格与地形取自世界地图，省份按 <see cref="ConquestWindow"/> 放回数据窗口所在的世界格子，
    /// 这样省份/省会、归属、建筑与单位全部落在同一套世界坐标上。
    /// 不照搬 VB 的错位：VB 加载时忽略 MapClipX、宽度用 "世界宽度 + MapClipX"，保存时却减去 MapClipX，
    /// 有横向偏移的文件每保存一次就错位一次。
    /// </para>
    /// </summary>
    /// <exception cref="InvalidDataException">不是世界地图文件，或数据窗口放不进该世界地图（选错了底图）</exception>
    public static ConquestWorldMapData LoadOnWorldMap(string filePath, string worldMapPath)
    {
        var world = LoadWorldMap(worldMapPath);
        var parser = new ConquestParser(filePath);
        var header = parser.GetHeaderData();
        var window = ConquestWindow.FromHeader(header);
        if (!window.FitsIn(world.MapWidth, world.MapHeight))
        {
            throw new InvalidDataException(Loc.F("Conquest.World.WindowMismatch",
                window.ClipX, window.ClipY, window.Width, window.Height, world.MapWidth, world.MapHeight));
        }

        var mapData = new ConquestWorldMapData(worldMapPath) { Header = header };
        CopyWorldTerrain(world, mapData);

        var provinceData = parser.GetProvinceData();
        int cells = Math.Min(window.CellCount, provinceData.Count);
        for (int i = 0; i < cells; i++)
        {
            var (col, row) = window.ToWorld(i, mapData.MapWidth);
            mapData.SetProvince(col, row, provinceData[i]);
        }

        PopulateScenarioData(parser, mapData);
        mapData.FilePath = filePath;
        return mapData;
    }

    /// <summary>
    /// 从 Parser 填充 MapData（数据窗口布局），Parser 用完即弃。
    /// </summary>
    public static void PopulateMapData(ConquestParser parser, MapData mapData)
    {
        mapData.Header = parser.GetHeaderData();
        mapData.MapWidth = parser.Header.MapLength;
        mapData.MapHeight = parser.Header.MapWidth;
        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

        var provinceData = parser.GetProvinceData();
        for (int i = 0; i < Math.Min(provinceData.Count, mapData.TerrainCount); i++)
            mapData.SetProvince(i, provinceData[i]);

        PopulateScenarioData(parser, mapData);
    }

    /// <summary>网格以外的数据（军团、归属、各实体列表），两种布局共用。</summary>
    private static void PopulateScenarioData(ConquestParser parser, MapData mapData)
    {
        mapData.Legions = new System.Collections.ObjectModel.ObservableCollection<Legion>(parser.GetLegionData());
        mapData.Belongs = parser.GetBelongData();
        mapData.Buildings = new System.Collections.ObjectModel.ObservableCollection<Building>(parser.GetBuildingData());

        var armies = parser.GetTroopData();
        mapData.Armies = new System.Collections.ObjectModel.ObservableCollection<Army>(armies);
        mapData.ArmiesV3 = new System.Collections.ObjectModel.ObservableCollection<Army_3>(parser.ArmiesV3);

        mapData.Traps = new System.Collections.ObjectModel.ObservableCollection<Trap>(parser.GetTrapData());
        mapData.Cases = new System.Collections.ObjectModel.ObservableCollection<MapCase>(parser.GetCaseData());
        mapData.Weathers = new System.Collections.ObjectModel.ObservableCollection<Weather>(parser.GetWeatherData());
        mapData.Events = new System.Collections.ObjectModel.ObservableCollection<MapEvent>(parser.GetEventData());

        var reinforcements = parser.GetReinforcementData();
        mapData.Reinforcements = new System.Collections.ObjectModel.ObservableCollection<Reinforcement>(reinforcements);
        mapData.ReinforcementsV3 = new System.Collections.ObjectModel.ObservableCollection<Reinforcement_3>(parser.ReinforcementsV3);

        mapData.AirForces = new System.Collections.ObjectModel.ObservableCollection<AirForce>(parser.GetAirForceData());
        mapData.UnitPlaces = new System.Collections.ObjectModel.ObservableCollection<UnitPlacement>(parser.GetUnitPlaceData());
        mapData.Capitals = new System.Collections.ObjectModel.ObservableCollection<Capital>(parser.GetCapitalData());
        mapData.StrategyConstructions = new System.Collections.ObjectModel.ObservableCollection<StrategicConstruction>(parser.GetStrategyConstructionData());
        mapData.AirSupports = new System.Collections.ObjectModel.ObservableCollection<AirSupport>(parser.GetAirSupportData());
        mapData.ExtensionData = parser.GetExtensionData();
    }

    /// <summary>读取并校验世界地图（文件头签名不对的 .bin 不是地图，直接拒绝，避免按垃圾尺寸分配内存）。</summary>
    private static MapData LoadWorldMap(string worldMapPath)
    {
        if (!WorldParser.TryReadHeader(worldMapPath, out _, out _))
            throw new InvalidDataException(Loc.F("Conquest.World.NotWorldMap", worldMapPath));
        return WorldParser.LoadFromFile(worldMapPath);
    }

    /// <summary>按世界地图尺寸建网格并复制地形；省份全部初始化为"无省份"（数据窗口以外没有省份）。</summary>
    private static void CopyWorldTerrain(MapData world, MapData target)
    {
        target.InitializeTerrain(world.MapWidth, world.MapHeight);
        for (int i = 0; i < target.TerrainCount; i++)
            target.SetTerrain(i, world.GetTerrain(i));
    }

    /// <summary>
    /// 创建新的征服 MapData。
    /// </summary>
    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions, int mapNumber = 0)
    {
        int totalTiles = mapWidth * mapHeight;
        var mapData = new MapData();
        mapData.Header = CreateNewHeader(mapWidth, mapHeight, numLegions, mapNumber);

        mapData.MapWidth = mapWidth;
        mapData.MapHeight = mapHeight;
        mapData.InitializeTerrain(mapWidth, mapHeight);

        // MapData 构造函数已预置 8 个默认军团（对齐 VB 的 MapData 构造），
        // 这里必须先清空，否则新征服会多出一批军团。
        mapData.Legions.Clear();
        for (int i = 0; i < numLegions; i++)
            mapData.Legions.Add(Legion.CreateForNewMap(i));

        for (int i = 0; i < totalTiles; i++)
            mapData.SetProvince(i, Province.Create(0xFFFF));

        mapData.Belongs = new List<string>();
        for (int i = 0; i < totalTiles; i++)
            mapData.Belongs.Add("FF");

        mapData.FilePath = string.Empty;
        return mapData;
    }

    /// <summary>
    /// 新建征服的文件头：MapLength = 列数（宽），MapWidth = 行数（高），与读取端、StageParser 一致
    /// （以前写反了，非正方形的新征服地图重新打开会被转置）。
    /// </summary>
    private static BTLHeader CreateNewHeader(int mapWidth, int mapHeight, int numLegions, int mapNumber) => new()
    {
        BtlVersion = 1,
        MapNumber = mapNumber,
        MapClipX = 0,
        MapClipY = 2,
        MapLength = mapWidth,
        MapWidth = mapHeight,
        ArmyCount = numLegions,
        BuildingCount = 0,
        TroopCount = 0,
        PlanCount = 0,
        EventCount = 0,
        WeatherCount = 0,
        VictoryCondition = 1,
        MinTurns = 999,
        MaxTurns = 999,
        ReinforcementCount = 0,
        AirRaidCount = 0,
        TrapCount = 0,
        StrategyCount = 0,
        AirSupportCount = 0,
        PlacementA = 0,
        PlacementB = 0,
        ConqueredFlagPosition = 0,
        Unknown4 = 1,
        SelectableTileCount = mapWidth * mapHeight,
        AccumulatedEconomy = 0,
        AccumulatedIndustry = 0,
        AccumulatedTech = 0
    };

    /// <summary>
    /// 在世界地图上新建征服（对齐 VB ConquestRenderScene.CreateNewMap）：
    /// MapClipX = 0、MapClipY = 2，数据窗口 = 世界宽度 x (世界高度 - 2 * MapClipY)，即南北两端各留 2 行。
    /// </summary>
    /// <exception cref="InvalidDataException">不是世界地图文件，或地图太小放不下数据窗口</exception>
    public static ConquestWorldMapData CreateNewOnWorldMap(string worldMapPath, int numLegions, int mapNumber)
    {
        const int clipY = 2;
        var world = LoadWorldMap(worldMapPath);
        int windowWidth = world.MapWidth;
        int windowHeight = world.MapHeight - 2 * clipY;
        if (windowWidth <= 0 || windowHeight <= 0)
            throw new InvalidDataException(Loc.F("Conquest.World.TooSmall", world.MapWidth, world.MapHeight));

        var template = CreateNewMapData(windowWidth, windowHeight, numLegions, mapNumber);
        var mapData = new ConquestWorldMapData(worldMapPath)
        {
            Header = template.Header,
            Legions = new System.Collections.ObjectModel.ObservableCollection<Legion>(template.Legions),
            Belongs = template.Belongs
        };
        CopyWorldTerrain(world, mapData);
        mapData.FilePath = string.Empty;
        return mapData;
    }

    /// <summary>
    /// 把 MapData 序列化成征服布局的文件字节（没有地形块）。
    /// <para>
    /// 省份数组按布局取回：世界底图布局（<see cref="ConquestWorldMapData"/>）按数据窗口逐格取回，
    /// 数据窗口布局本身就是文件顺序。省份 / 归属块的格数取两者较大者：官方文件按 8 对齐补齐的部分（值为 0）原样补回，
    /// 归属数组本来就含补齐部分（见 <see cref="BTLWriter"/>）。
    /// </para>
    /// <para>归属值就是文件里的原始字节（军团列表的 0 起下标，0xFF = 无归属），不做任何偏移。</para>
    /// </summary>
    public static byte[] BuildBytes(MapData mapData)
    {
        BTLWriter.SyncEntityCounts(mapData);
        return BTLWriter.Build(mapData, null, WindowProvincesToBytes(mapData));
    }

    /// <summary>
    /// 从 MapData 保存到文件，不经过 Parser 数据副本。
    /// </summary>
    public static bool SaveFromMapData(MapData mapData, string outputPath)
    {
        if (mapData.Header == null)
        {
            Debug.WriteLine("[ConquestParser] 错误：头部数据尚未解析");
            return false;
        }

        var data = BuildBytes(mapData);
        try
        {
            File.WriteAllBytes(outputPath, data);
            Debug.WriteLine($"[ConquestParser] 成功保存到: {outputPath} ({data.Length} 字节)");
            return true;
        }
        catch (Exception ex)
        {
            // 原样抛出，让调用方拿到真实原因（文件被 Excel 等程序锁定 / 无写权限 / 磁盘空间不足）。
            // 只 return false 的话，上层只能提示无信息的"保存失败！"，无从排查。
            Trace.WriteLine($"[ConquestParser] 保存失败: {outputPath}{Environment.NewLine}  {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 按文件顺序取出省份数组（不含补齐部分）。
    /// 世界底图布局按数据窗口逐格取回（窗口越出网格的格子 —— 文件头被改过时 —— 记为无省份）；
    /// 数据窗口布局的网格就是窗口本身。
    /// </summary>
    private static byte[] WindowProvincesToBytes(MapData mapData)
    {
        if (mapData is not ConquestWorldMapData || mapData.MapWidth <= 0)
            return mapData.ProvincesToBytes();

        var window = ConquestWindow.FromHeader(mapData.Header);
        var bytes = new byte[window.CellCount * 2];
        for (int i = 0; i < window.CellCount; i++)
        {
            var (col, row) = window.ToWorld(i, mapData.MapWidth);
            mapData.GetProvince(col, row).ToBytes(bytes, i * 2);
        }
        return bytes;
    }

    #endregion

    #region 兼容旧接口 - 供 BTLAnalyzer / CLI 使用

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions, int mapNumber = 0)
    {
        try
        {
            int totalTiles = mapWidth * mapHeight;
            Header = CreateNewHeader(mapWidth, mapHeight, numLegions, mapNumber);
            Legions.Clear(); Provinces.Clear(); Belongs.Clear(); Buildings.Clear();
            Armies.Clear(); ArmiesV3.Clear(); Traps.Clear(); Cases.Clear();
            Weathers.Clear(); Events.Clear();
            Reinforcements.Clear(); ReinforcementsV3.Clear(); AirForces.Clear();
            UnitPlaces.Clear(); Capitals.Clear(); StrategyConstructions.Clear(); AirSupports.Clear();

            for (int i = 0; i < numLegions; i++)
                Legions.Add(Legion.CreateForNewMap(i));
            for (int i = 0; i < totalTiles; i++)
                Provinces.Add(Province.Create(0xFFFF));
            for (int i = 0; i < totalTiles; i++)
                Belongs.Add("FF");

            ResetLoadFlags();
            _legionsLoaded = _provincesLoaded = _belongsLoaded = _buildingsLoaded = true;
            _armiesLoaded = _trapsLoaded = _casesLoaded = _weathersLoaded = _eventsLoaded = true;
            _reinforcementsLoaded = _airForcesLoaded = _unitPlacesLoaded = _capitalsLoaded = true;
            _strategyLoaded = _airSupportsLoaded = true;
            HexData = Array.Empty<byte>();
            HexFilePath = "";
            Debug.WriteLine($"[ConquestParser] 成功创建新征服: {mapWidth}x{mapHeight}, {numLegions}个军团, 地图编号{mapNumber}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[ConquestParser] 创建新征服失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>最近一次 SaveData 失败的原因（成功时为 null），供界面提示。</summary>
    public string? LastError { get; private set; }

    /// <summary>把 Parser 当前持有的数据保存成文件（与 <see cref="SaveFromMapData"/> 走同一套序列化）。</summary>
    public bool SaveData(string outputPath)
    {
        LastError = null;
        if (Header == null)
        {
            LastError = "头部数据尚未解析";
            Debug.WriteLine("[ConquestParser] 错误：头部数据尚未解析");
            return false;
        }

        try
        {
            var mapData = new MapData();
            PopulateMapData(this, mapData);
            return SaveFromMapData(mapData, outputPath);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[ConquestParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    #endregion
}
