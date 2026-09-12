using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
            Debug.WriteLine($"[StageParser] 成功加载: {filePath}, 大小: {HexData.Length} 字节");

            ResetLoadFlags();
            GetHeaderData();
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

    public List<Terrain> GetTerrainData()
    {
        if (_terrainsLoaded) return Terrains;
        if (Header == null) GetHeaderData();
        if (!_legionsLoaded) GetLegionData();

        var offsets = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        Terrains.Clear();
        Terrains.AddRange(BTLTerrainModule.Parse(HexData, offsets.terrain, Header.SelectableTileCount));
        _terrainsLoaded = true;
        return Terrains;
    }

    public List<Province> GetProvinceData()
    {
        if (_provincesLoaded) return Provinces;
        if (Header == null) GetHeaderData();
        if (!_terrainsLoaded) GetTerrainData();

        var offsets = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        Provinces.Clear();
        Provinces.AddRange(BTLProvinceModule.Parse(HexData, offsets.province, Header.SelectableTileCount));
        _provincesLoaded = true;
        return Provinces;
    }

    public List<string> GetBelongData()
    {
        if (_belongsLoaded) return Belongs;
        if (Header == null) GetHeaderData();
        if (!_provincesLoaded) GetProvinceData();

        var offsets = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        Belongs.Clear();
        Belongs.AddRange(BTLBelongModule.Parse(HexData, offsets.belong, Header.SelectableTileCount));
        _belongsLoaded = true;
        return Belongs;
    }

    public List<Building> GetBuildingData()
    {
        if (_buildingsLoaded) return Buildings;
        if (Header == null) GetHeaderData();
        if (!_belongsLoaded) GetBelongData();

        var offsets = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        Buildings.Clear();
        Buildings.AddRange(BTLBuildingModule.Parse(HexData, offsets.building, Header.BuildingCount));
        _buildingsLoaded = true;
        return Buildings;
    }

    public List<Army> GetArmyData()
    {
        if (_armiesLoaded) return Armies;
        if (Header == null) GetHeaderData();
        if (!_buildingsLoaded) GetBuildingData();

        int dataEnd = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        Armies.Clear();
        ArmiesV3.Clear();

        var (v1, v3) = BTLArmyModule.Parse(HexData, dataEnd, Header.TroopCount, BtlVersion);
        Armies.AddRange(v1);
        ArmiesV3.AddRange(v3);
        _armiesLoaded = true;
        return Armies;
    }

    public List<Trap> GetTrapData()
    {
        if (_trapsLoaded) return Traps;
        if (Header == null) GetHeaderData();
        if (!_armiesLoaded) GetArmyData();

        int offset = CalculateDataEndOffset() + (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion));
        Traps.Clear();
        Traps.AddRange(BTLTrapModule.Parse(HexData, offset, Header.TrapCount));
        _trapsLoaded = true;
        return Traps;
    }

    public List<MapCase> GetCaseData()
    {
        if (_casesLoaded) return Cases;
        if (Header == null) GetHeaderData();
        if (!_trapsLoaded) GetTrapData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12);
        Cases.Clear();
        Cases.AddRange(BTLCaseModule.Parse(HexData, offset, Header.PlanCount));
        _casesLoaded = true;
        return Cases;
    }

    public List<Weather> GetWeatherData()
    {
        if (_weathersLoaded) return Weathers;
        if (Header == null) GetHeaderData();
        if (!_casesLoaded) GetCaseData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16);
        Weathers.Clear();
        Weathers.AddRange(BTLWeatherModule.Parse(HexData, offset, Header.WeatherCount));
        _weathersLoaded = true;
        return Weathers;
    }

    public List<MapEvent> GetEventData()
    {
        if (_eventsLoaded) return Events;
        if (Header == null) GetHeaderData();
        if (!_weathersLoaded) GetWeatherData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16);
        Events.Clear();
        Events.AddRange(BTLEventModule.Parse(HexData, offset, Header.EventCount));
        _eventsLoaded = true;
        return Events;
    }

    public List<Reinforcement> GetReinforcementData()
    {
        if (_reinforcementsLoaded) return Reinforcements;
        if (Header == null) GetHeaderData();
        if (!_eventsLoaded) GetEventData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44);
        Reinforcements.Clear();
        ReinforcementsV3.Clear();

        var (v1, v3) = BTLReinforcementModule.Parse(HexData, offset, Header.ReinforcementCount, BtlVersion);
        Reinforcements.AddRange(v1);
        ReinforcementsV3.AddRange(v3);
        _reinforcementsLoaded = true;
        return Reinforcements;
    }

    public List<AirForce> GetAirForceData()
    {
        if (_airForcesLoaded) return AirForces;
        if (Header == null) GetHeaderData();
        if (!_reinforcementsLoaded) GetReinforcementData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44) +
                     (Header.ReinforcementCount * BTLReinforcementModule.GetReinforcementSize(BtlVersion));
        AirForces.Clear();
        AirForces.AddRange(BTLAirForceModule.Parse(HexData, offset, Header.AirRaidCount));
        _airForcesLoaded = true;
        return AirForces;
    }

    public List<UnitPlacement> GetUnitPlaceData()
    {
        if (_unitPlacesLoaded) return UnitPlaces;
        if (Header == null) GetHeaderData();
        if (!_airForcesLoaded) GetAirForceData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44) +
                     (Header.ReinforcementCount * BTLReinforcementModule.GetReinforcementSize(BtlVersion)) +
                     (Header.AirRaidCount * 20);
        UnitPlaces.Clear();
        UnitPlaces.AddRange(BTLUnitPlacementModule.Parse(HexData, offset, Header.PlacementA + Header.PlacementB));
        _unitPlacesLoaded = true;
        return UnitPlaces;
    }

    public List<Capital> GetCapitalData()
    {
        if (_capitalsLoaded) return Capitals;
        if (Header == null) GetHeaderData();
        if (!_unitPlacesLoaded) GetUnitPlaceData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44) +
                     (Header.ReinforcementCount * BTLReinforcementModule.GetReinforcementSize(BtlVersion)) +
                     (Header.AirRaidCount * 20) +
                     ((Header.PlacementA + Header.PlacementB) * 8);
        Capitals.Clear();
        Capitals.AddRange(BTLCapitalModule.Parse(HexData, offset, Header.ConqueredFlagPosition));
        _capitalsLoaded = true;
        return Capitals;
    }

    public List<StrategicConstruction> GetStrategyConstructionData()
    {
        if (_strategyLoaded) return StrategyConstructions;
        if (Header == null) GetHeaderData();
        if (!_capitalsLoaded) GetCapitalData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44) +
                     (Header.ReinforcementCount * BTLReinforcementModule.GetReinforcementSize(BtlVersion)) +
                     (Header.AirRaidCount * 20) +
                     ((Header.PlacementA + Header.PlacementB) * 8) +
                     (Header.ConqueredFlagPosition * 4);
        StrategyConstructions.Clear();
        StrategyConstructions.AddRange(BTLStrategyModule.Parse(HexData, offset, Header.StrategyCount));
        _strategyLoaded = true;
        return StrategyConstructions;
    }

    public List<AirSupport> GetAirSupportData()
    {
        if (_airSupportsLoaded) return AirSupports;
        if (Header == null) GetHeaderData();
        if (!_strategyLoaded) GetStrategyConstructionData();

        int offset = CalculateDataEndOffset() +
                     (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion)) +
                     (Header.TrapCount * 12) +
                     (Header.PlanCount * 16) +
                     (Header.WeatherCount * 16) +
                     (Header.EventCount * 44) +
                     (Header.ReinforcementCount * BTLReinforcementModule.GetReinforcementSize(BtlVersion)) +
                     (Header.AirRaidCount * 20) +
                     ((Header.PlacementA + Header.PlacementB) * 8) +
                     (Header.ConqueredFlagPosition * 4) +
                     (Header.StrategyCount * 16);
        AirSupports.Clear();
        AirSupports.AddRange(BTLAirSupportModule.Parse(HexData, offset, Header.AirSupportCount));
        _airSupportsLoaded = true;
        return AirSupports;
    }

    private int CalculateDataEndOffset()
    {
        var offsets = StageOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        return offsets.dataEnd;
    }

    #region 直接操作 MapData 的静态方法 - 消除数据重复

    /// <summary>
    /// 从文件加载所有数据到 MapData，Parser 不持有数据副本。
    /// </summary>
    public static MapData LoadToMapData(string filePath)
    {
        var parser = new StageParser(filePath);
        var mapData = new MapData();
        PopulateMapData(parser, mapData);
        mapData.FilePath = filePath;
        return mapData;
    }

    /// <summary>
    /// 从 Parser 填充 MapData，Parser 用完即弃。
    /// </summary>
    public static void PopulateMapData(StageParser parser, MapData mapData)
    {
        mapData.Header = parser.GetHeaderData();
        mapData.MapWidth = parser.Header.MapLength;
        mapData.MapHeight = parser.Header.MapWidth;
        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

        var terrainData = parser.GetTerrainData();
        for (int i = 0; i < Math.Min(terrainData.Count, mapData.TerrainCount); i++)
            mapData.SetTerrain(i, terrainData[i].ToTerrainData());

        var provinceData = parser.GetProvinceData();
        for (int i = 0; i < Math.Min(provinceData.Count, mapData.TerrainCount); i++)
            mapData.SetProvince(i, provinceData[i]);

        mapData.Legions = new System.Collections.ObjectModel.ObservableCollection<Legion>(parser.GetLegionData());
        mapData.Belongs = parser.GetBelongData();
        mapData.Buildings = new System.Collections.ObjectModel.ObservableCollection<Building>(parser.GetBuildingData());

        var armies = parser.GetArmyData();
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
    }

    /// <summary>
    /// 创建新的战役 MapData。
    /// </summary>
    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions)
    {
        int totalTiles = mapWidth * mapHeight;
        var mapData = new MapData();
        mapData.Header = new BTLHeader
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
            SelectableTileCount = totalTiles,
            AccumulatedEconomy = 0,
            AccumulatedIndustry = 0,
            AccumulatedTech = 0
        };

        mapData.MapWidth = mapWidth;
        mapData.MapHeight = mapHeight;
        mapData.InitializeTerrain(mapWidth, mapHeight);

        for (int i = 0; i < numLegions; i++)
            mapData.Legions.Add(Legion.CreateDefault(i + 1));

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

    /// <summary>
    /// 从 MapData 保存到文件，不经过 Parser 数据副本。
    /// </summary>
    public static bool SaveFromMapData(MapData mapData, string outputPath)
    {
        if (mapData.Header == null) { Debug.WriteLine("[StageParser] 错误：头部数据尚未初始化"); return false; }

        var header = mapData.Header;
        header.ArmyCount = mapData.Legions.Count;
        header.BuildingCount = mapData.Buildings.Count;
        header.TroopCount = header.BtlVersion >= 3 ? mapData.ArmiesV3.Count : mapData.Armies.Count;
        header.PlanCount = mapData.Cases.Count;
        header.EventCount = mapData.Events.Count;
        header.WeatherCount = mapData.Weathers.Count;
        header.ReinforcementCount = header.BtlVersion >= 3 ? mapData.ReinforcementsV3.Count : mapData.Reinforcements.Count;
        header.AirRaidCount = mapData.AirForces.Count;
        header.TrapCount = mapData.Traps.Count;
        header.StrategyCount = mapData.StrategyConstructions.Count;
        header.AirSupportCount = mapData.AirSupports.Count;
        if (mapData.TerrainCount != header.SelectableTileCount) header.SelectableTileCount = mapData.TerrainCount;
        if (mapData.UnitPlaces.Count != header.PlacementA + header.PlacementB) { header.PlacementA = mapData.UnitPlaces.Count; header.PlacementB = 0; }
        if (mapData.Capitals.Count != header.ConqueredFlagPosition) header.ConqueredFlagPosition = mapData.Capitals.Count;

        var resultData = new List<byte>();
        resultData.AddRange(header.ToBytes());
        foreach (var legion in mapData.Legions) { var buf = new byte[300]; legion.ToBytes(buf, 0); resultData.AddRange(buf); }
        resultData.AddRange(mapData.TerrainsToBytes());
        resultData.AddRange(mapData.ProvincesToBytes());
        foreach (var belong in mapData.Belongs) resultData.Add(byte.Parse(belong, System.Globalization.NumberStyles.HexNumber));
        foreach (var building in mapData.Buildings) { var buf = new byte[32]; building.ToBytes(buf, 0); resultData.AddRange(buf); }
        if (header.BtlVersion >= 3) { foreach (var t in mapData.ArmiesV3) { var buf = new byte[64]; t.ToBytes(buf, 0); resultData.AddRange(buf); } }
        else { foreach (var t in mapData.Armies) { var buf = new byte[48]; t.ToBytes(buf, 0); resultData.AddRange(buf); } }
        foreach (var trap in mapData.Traps) { var buf = new byte[12]; trap.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var c in mapData.Cases) { var buf = new byte[16]; c.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var w in mapData.Weathers) { var buf = new byte[16]; w.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var ev in mapData.Events) { var buf = new byte[44]; ev.ToBytes(buf, 0); resultData.AddRange(buf); }
        if (header.BtlVersion >= 3) { foreach (var r in mapData.ReinforcementsV3) { var buf = new byte[104]; r.ToBytes(buf, 0); resultData.AddRange(buf); } }
        else { foreach (var r in mapData.Reinforcements) { var buf = new byte[80]; r.ToBytes(buf, 0); resultData.AddRange(buf); } }
        foreach (var af in mapData.AirForces) { var buf = new byte[20]; af.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var up in mapData.UnitPlaces) { var buf = new byte[8]; up.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var cap in mapData.Capitals) { var buf = new byte[4]; cap.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var sc in mapData.StrategyConstructions) { var buf = new byte[16]; sc.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var asup in mapData.AirSupports) { var buf = new byte[16]; asup.ToBytes(buf, 0); resultData.AddRange(buf); }

        try { File.WriteAllBytes(outputPath, resultData.ToArray()); Debug.WriteLine($"[StageParser] 成功保存到: {outputPath} ({resultData.Count} 字节)"); return true; }
        catch (Exception ex) { Debug.WriteLine($"[StageParser] 保存失败: {ex.Message}"); return false; }
    }

    #endregion

    #region 兼容旧接口 - 供 BTLAnalyzer / CLI 使用

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions)
    {
        try
        {
            int totalTiles = mapWidth * mapHeight;
            Header = new BTLHeader
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
                SelectableTileCount = totalTiles,
                AccumulatedEconomy = 0,
                AccumulatedIndustry = 0,
                AccumulatedTech = 0
            };
            Legions.Clear(); Terrains.Clear(); Provinces.Clear(); Belongs.Clear();
            Buildings.Clear(); Armies.Clear(); ArmiesV3.Clear(); Traps.Clear();
            Cases.Clear(); Weathers.Clear(); Events.Clear();
            Reinforcements.Clear(); ReinforcementsV3.Clear(); AirForces.Clear();
            UnitPlaces.Clear(); Capitals.Clear(); StrategyConstructions.Clear(); AirSupports.Clear();

            for (int i = 0; i < numLegions; i++)
                Legions.Add(Legion.CreateDefault(i + 1));
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
            Debug.WriteLine($"[StageParser] 创建新战役失败: {ex.Message}");
            return false;
        }
    }

    public bool SaveData(string outputPath)
    {
        if (Header == null) { Debug.WriteLine("[StageParser] 错误：头部数据尚未初始化"); return false; }

        Header.ArmyCount = Legions.Count;
        Header.BuildingCount = Buildings.Count;
        Header.TroopCount = BtlVersion >= 3 ? ArmiesV3.Count : Armies.Count;
        Header.PlanCount = Cases.Count;
        Header.EventCount = Events.Count;
        Header.WeatherCount = Weathers.Count;
        Header.ReinforcementCount = BtlVersion >= 3 ? ReinforcementsV3.Count : Reinforcements.Count;
        Header.AirRaidCount = AirForces.Count;
        Header.TrapCount = Traps.Count;
        Header.StrategyCount = StrategyConstructions.Count;
        Header.AirSupportCount = AirSupports.Count;
        if (Terrains.Count != Header.SelectableTileCount) Header.SelectableTileCount = Terrains.Count;
        if (Provinces.Count != Header.SelectableTileCount) Header.SelectableTileCount = Provinces.Count;
        if (Belongs.Count != Header.SelectableTileCount) Header.SelectableTileCount = Belongs.Count;
        if (UnitPlaces.Count != Header.PlacementA + Header.PlacementB) { Header.PlacementA = UnitPlaces.Count; Header.PlacementB = 0; }
        if (Capitals.Count != Header.ConqueredFlagPosition) Header.ConqueredFlagPosition = Capitals.Count;

        var resultData = new List<byte>();
        resultData.AddRange(Header.ToBytes());
        foreach (var legion in Legions) { var buf = new byte[300]; legion.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var terrain in Terrains) { var buf = new byte[16]; terrain.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var province in Provinces) { var b = new byte[2]; province.ToBytes(b, 0); resultData.AddRange(b); }
        foreach (var belong in Belongs) resultData.Add(byte.Parse(belong, System.Globalization.NumberStyles.HexNumber));
        foreach (var building in Buildings) { var buf = new byte[32]; building.ToBytes(buf, 0); resultData.AddRange(buf); }
        if (BtlVersion >= 3) { foreach (var t in ArmiesV3) { var buf = new byte[64]; t.ToBytes(buf, 0); resultData.AddRange(buf); } }
        else { foreach (var t in Armies) { var buf = new byte[48]; t.ToBytes(buf, 0); resultData.AddRange(buf); } }
        foreach (var trap in Traps) { var buf = new byte[12]; trap.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var c in Cases) { var buf = new byte[16]; c.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var w in Weathers) { var buf = new byte[16]; w.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var ev in Events) { var buf = new byte[44]; ev.ToBytes(buf, 0); resultData.AddRange(buf); }
        if (BtlVersion >= 3) { foreach (var r in ReinforcementsV3) { var buf = new byte[104]; r.ToBytes(buf, 0); resultData.AddRange(buf); } }
        else { foreach (var r in Reinforcements) { var buf = new byte[80]; r.ToBytes(buf, 0); resultData.AddRange(buf); } }
        foreach (var af in AirForces) { var buf = new byte[20]; af.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var up in UnitPlaces) { var buf = new byte[8]; up.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var cap in Capitals) { var buf = new byte[4]; cap.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var sc in StrategyConstructions) { var buf = new byte[16]; sc.ToBytes(buf, 0); resultData.AddRange(buf); }
        foreach (var asup in AirSupports) { var buf = new byte[16]; asup.ToBytes(buf, 0); resultData.AddRange(buf); }

        try { File.WriteAllBytes(outputPath, resultData.ToArray()); Debug.WriteLine($"[StageParser] 成功保存到: {outputPath} ({resultData.Count} 字节)"); return true; }
        catch (Exception ex) { Debug.WriteLine($"[StageParser] 保存失败: {ex.Message}"); return false; }
    }

    #endregion
}