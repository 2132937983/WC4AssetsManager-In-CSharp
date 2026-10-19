using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Parsers.Stage;

/// <summary>
/// 战役关卡解析器。
/// 纯文件 I/O 工具，直接操作 MapData，不持有运行时数据。
/// </summary>
public class StageParser
{
    public byte[] HexData { get; private set; } = null!;
    public string HexFilePath { get; private set; } = "";
    public BTLHeader Header { get; private set; } = null!;
    public int BtlVersion => Header?.BtlVersion ?? 0;

    public List<Legion> Legions { get; } = new();
    public List<Terrain> Terrains { get; } = new();
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
    private bool _terrainsLoaded;
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

    public StageParser() { }

    public StageParser(string hexFilePath)
    {
        LoadHexFile(hexFilePath);
    }

    public bool LoadHexFile(string filePath)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int fileLength = (int)fs.Length;
            HexData = new byte[fileLength];
            int offset = 0, remaining = fileLength;
            while (remaining > 0)
            {
                int readCount = fs.Read(HexData, offset, remaining);
                if (readCount == 0) break;
                offset += readCount;
                remaining -= readCount;
            }
            HexFilePath = filePath;
            Debug.WriteLine($"[StageParser] 成功加载: {filePath}, 大小: {HexData.Length} 字节, 读取耗时 {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            ResetLoadFlags();
            Header = null!;
            GetHeaderData();
            Debug.WriteLine($"[Timing-Load]   ResetLoadFlags+GetHeaderData: {sw.ElapsedMilliseconds}ms");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StageParser] 加载失败: {ex.Message}");
            return false;
        }
    }

    private void ResetLoadFlags()
    {
        _legionsLoaded = false;
        _terrainsLoaded = false;
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

    /// <summary>按文件头排出的数据块地址表（战役布局：地形块格数 = MapLength × MapWidth）。</summary>
    private BTLLayout Layout
    {
        get
        {
            if (Header == null) GetHeaderData();
            return BTLLayout.ForStage(Header);
        }
    }

    public List<Terrain> GetTerrainData()
    {
        if (_terrainsLoaded) return Terrains;
        if (!_legionsLoaded) GetLegionData();

        var span = Layout[BtlBlock.Terrain];
        Terrains.Clear();
        Terrains.AddRange(BTLTerrainModule.Parse(HexData, span.StartInt, span.Count));
        _terrainsLoaded = true;
        return Terrains;
    }

    public List<Province> GetProvinceData()
    {
        if (_provincesLoaded) return Provinces;
        if (!_terrainsLoaded) GetTerrainData();

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

    public List<Army> GetArmyData()
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
        if (!_armiesLoaded) GetArmyData();

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
    /// </summary>
    public static MapData LoadToMapData(string filePath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.WriteLine("[Timing-Load] LoadToMapData 开始");

        var parser = new StageParser(filePath);
        Debug.WriteLine($"[Timing-Load]   new StageParser(读文件+文件头): {sw.ElapsedMilliseconds}ms"); sw.Restart();

        var mapData = new MapData();
        Debug.WriteLine($"[Timing-Load]   new MapData(): {sw.ElapsedMilliseconds}ms"); sw.Restart();

        PopulateMapData(parser, mapData);
        mapData.FilePath = filePath;
        Debug.WriteLine($"[Timing-Load]   PopulateMapData: {sw.ElapsedMilliseconds}ms");

        return mapData;
    }

    /// <summary>
    /// 从 Parser 填充 MapData，Parser 用完即弃。
    /// </summary>
    public static void PopulateMapData(StageParser parser, MapData mapData)
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        Debug.WriteLine("[Timing-Load] PopulateMapData 开始");

        mapData.Header = parser.GetHeaderData();
        mapData.MapWidth = parser.Header.MapLength;
        mapData.MapHeight = parser.Header.MapWidth;
        Debug.WriteLine($"[Timing-Load]   Header: {t.ElapsedMilliseconds}ms"); t.Restart();

        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);
        Debug.WriteLine($"[Timing-Load]   InitializeTerrain({mapData.MapWidth}x{mapData.MapHeight}): {t.ElapsedMilliseconds}ms"); t.Restart();

        var terrainData = parser.GetTerrainData();
        for (int i = 0; i < Math.Min(terrainData.Count, mapData.TerrainCount); i++)
            mapData.SetTerrain(i, terrainData[i].ToTerrainData());
        Debug.WriteLine($"[Timing-Load]   地形(解析+填充): {t.ElapsedMilliseconds}ms"); t.Restart();

        var provinceData = parser.GetProvinceData();
        for (int i = 0; i < Math.Min(provinceData.Count, mapData.TerrainCount); i++)
            mapData.SetProvince(i, provinceData[i]);
        Debug.WriteLine($"[Timing-Load]   省份(解析+填充): {t.ElapsedMilliseconds}ms"); t.Restart();

        mapData.Legions = new System.Collections.ObjectModel.ObservableCollection<Legion>(parser.GetLegionData());
        Debug.WriteLine($"[Timing-Load]   Legions: {t.ElapsedMilliseconds}ms"); t.Restart();

        mapData.Belongs = parser.GetBelongData();
        Debug.WriteLine($"[Timing-Load]   Belongs: {t.ElapsedMilliseconds}ms"); t.Restart();

        mapData.Buildings = new System.Collections.ObjectModel.ObservableCollection<Building>(parser.GetBuildingData());
        Debug.WriteLine($"[Timing-Load]   Buildings: {t.ElapsedMilliseconds}ms"); t.Restart();

        var armies = parser.GetArmyData();
        mapData.Armies = new System.Collections.ObjectModel.ObservableCollection<Army>(armies);
        mapData.ArmiesV3 = new System.Collections.ObjectModel.ObservableCollection<Army_3>(parser.ArmiesV3);
        Debug.WriteLine($"[Timing-Load]   Armies: {t.ElapsedMilliseconds}ms"); t.Restart();

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
        Debug.WriteLine($"[Timing-Load]   其余模块(Trap/Case/Weather/Event/Reinforce/Air/Unit/Capital/Strategy/AirSupport): {t.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 创建新的战役 MapData。
    /// </summary>
    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions)
    {
        int totalTiles = mapWidth * mapHeight;
        var mapData = new MapData();
        mapData.Header = CreateNewHeader(mapWidth, mapHeight, numLegions);

        mapData.MapWidth = mapWidth;
        mapData.MapHeight = mapHeight;
        mapData.InitializeTerrain(mapWidth, mapHeight);

        // MapData 构造函数已预置 8 个默认军团（对齐 VB 的 MapData 构造），
        // 这里必须先清空，否则新战役会多出一批军团。
        mapData.Legions.Clear();
        for (int i = 0; i < numLegions; i++)
            mapData.Legions.Add(Legion.CreateForNewMap(i));

        for (int i = 0; i < totalTiles; i++)
            mapData.SetProvince(i, Province.Create(0xFFFF));

        mapData.Belongs = new List<string>();
        for (int i = 0; i < totalTiles; i++)
            mapData.Belongs.Add((i % numLegions).ToString("X2"));

        for (int i = 0; i < numLegions; i++)
            mapData.Capitals.Add(new Capital { Coordinate = i * (totalTiles / Math.Max(numLegions, 1)) });

        mapData.FilePath = string.Empty;
        return mapData;
    }

    private static BTLHeader CreateNewHeader(int mapWidth, int mapHeight, int numLegions) => new()
    {
        BtlVersion = 1,
        MapNumber = 0,
        MapClipX = 0,
        MapClipY = 0,
        MapLength = mapWidth,
        MapWidth = mapHeight,
        ArmyCount = numLegions,
        BuildingCount = 0,
        TroopCount = 0,
        PlanCount = 0,
        EventCount = 0,
        WeatherCount = 0,
        VictoryCondition = 0,
        MinTurns = 50,
        MaxTurns = 50,
        ReinforcementCount = 0,
        AirRaidCount = 0,
        TrapCount = 0,
        StrategyCount = 0,
        AirSupportCount = 0,
        PlacementA = 0,
        PlacementB = 0,
        ConqueredFlagPosition = numLegions,
        Unknown4 = 0,
        SelectableTileCount = mapWidth * mapHeight,
        AccumulatedEconomy = 0,
        AccumulatedIndustry = 0,
        AccumulatedTech = 0
    };

    /// <summary>
    /// 把 MapData 序列化成战役布局的文件字节。地形块按网格大小（= MapLength × MapWidth）写出，
    /// 省份 / 归属块的格数取两者较大者（见 <see cref="BTLWriter"/>），其余数量字段保持文件原值。
    /// </summary>
    public static byte[] BuildBytes(MapData mapData)
    {
        mapData.Header.MapLength = mapData.MapWidth;
        mapData.Header.MapWidth = mapData.MapHeight;
        BTLWriter.SyncEntityCounts(mapData);
        return BTLWriter.Build(mapData, mapData.TerrainsToBytes(), mapData.ProvincesToBytes());
    }

    /// <summary>
    /// 从 MapData 保存到文件，不经过 Parser 数据副本。
    /// </summary>
    public static bool SaveFromMapData(MapData mapData, string outputPath)
    {
        if (mapData.Header == null) { Debug.WriteLine("[StageParser] 错误：头部数据尚未初始化"); return false; }

        var data = BuildBytes(mapData);
        try
        {
            File.WriteAllBytes(outputPath, data);
            Debug.WriteLine($"[StageParser] 成功保存到: {outputPath} ({data.Length} 字节)");
            return true;
        }
        catch (Exception ex)
        {
            // 原样抛出，让调用方拿到真实原因（文件被 Excel 等程序锁定 / 无写权限 / 磁盘空间不足）。
            // 只 return false 的话，上层只能提示无信息的"保存失败！"，无从排查。
            Trace.WriteLine($"[StageParser] 保存失败: {outputPath}{Environment.NewLine}  {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region 兼容旧接口 - 供 BTLAnalyzer / CLI 使用

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions)
    {
        try
        {
            int totalTiles = mapWidth * mapHeight;
            Header = CreateNewHeader(mapWidth, mapHeight, numLegions);
            Legions.Clear(); Terrains.Clear(); Provinces.Clear(); Belongs.Clear();
            Buildings.Clear(); Armies.Clear(); ArmiesV3.Clear(); Traps.Clear();
            Cases.Clear(); Weathers.Clear(); Events.Clear();
            Reinforcements.Clear(); ReinforcementsV3.Clear(); AirForces.Clear();
            UnitPlaces.Clear(); Capitals.Clear(); StrategyConstructions.Clear(); AirSupports.Clear();

            for (int i = 0; i < numLegions; i++)
                Legions.Add(Legion.CreateForNewMap(i));
            for (int i = 0; i < totalTiles; i++)
                Terrains.Add(new Terrain { TileType1 = 0, TileType2 = 0x3F, DecorationType2 = 0xFF, TileType3 = 0x3F, DecorationType3 = 0xFF });
            for (int i = 0; i < totalTiles; i++)
                Provinces.Add(Province.Create(0xFFFF));
            for (int i = 0; i < totalTiles; i++)
                Belongs.Add((i % numLegions).ToString("X2"));
            for (int i = 0; i < numLegions; i++)
                Capitals.Add(new Capital { Coordinate = i * (totalTiles / Math.Max(numLegions, 1)) });

            ResetLoadFlags();
            _legionsLoaded = _terrainsLoaded = _provincesLoaded = _belongsLoaded = true;
            _buildingsLoaded = _armiesLoaded = _trapsLoaded = _casesLoaded = _weathersLoaded = true;
            _eventsLoaded = _reinforcementsLoaded = _airForcesLoaded = _unitPlacesLoaded = true;
            _capitalsLoaded = _strategyLoaded = _airSupportsLoaded = true;
            HexData = Array.Empty<byte>();
            HexFilePath = "";
            Debug.WriteLine($"[StageParser] 成功创建新战役: {mapWidth}x{mapHeight}, {numLegions}个军团");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[StageParser] 创建新战役失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>最近一次 SaveData 失败的原因（成功时为 null），供界面提示；SaveData 本身保持返回 bool 的旧契约。</summary>
    public string? LastError { get; private set; }

    /// <summary>把 Parser 当前持有的数据保存成文件（与 <see cref="SaveFromMapData"/> 走同一套序列化）。</summary>
    public bool SaveData(string outputPath)
    {
        LastError = null;
        if (Header == null) { LastError = "头部数据尚未初始化"; Debug.WriteLine("[StageParser] 错误：头部数据尚未初始化"); return false; }

        try
        {
            var mapData = new MapData();
            PopulateMapData(this, mapData);
            return SaveFromMapData(mapData, outputPath);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[StageParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    #endregion
}
