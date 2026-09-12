using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

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

    public bool BelongOffset { get; private set; }

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

    public List<Province> GetProvinceData()
    {
        if (_provincesLoaded) return Provinces;
        if (Header == null) GetHeaderData();
        if (!_legionsLoaded) GetLegionData();

        var offsets = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
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

        var offsets = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);

        BelongOffset = false;
        if (Legions.Count > 0)
        {
            int minActionId = Legions.Min(l => l.ActionId);
            if (minActionId > 0)
            {
                BelongOffset = true;
                Debug.WriteLine($"[ConquestParser] 检测到军团actionid从{minActionId}开始，归属值将自动+1偏移");
            }
        }

        Belongs.Clear();
        Belongs.AddRange(BTLBelongModule.Parse(HexData, offsets.belong, Header.SelectableTileCount, BelongOffset));
        _belongsLoaded = true;
        return Belongs;
    }

    public List<Building> GetBuildingData()
    {
        if (_buildingsLoaded) return Buildings;
        if (Header == null) GetHeaderData();
        if (!_belongsLoaded) GetBelongData();

        var offsets = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount);
        Buildings.Clear();
        Buildings.AddRange(BTLBuildingModule.Parse(HexData, offsets.building, Header.BuildingCount));
        _buildingsLoaded = true;
        return Buildings;
    }

    public List<Army> GetTroopData()
    {
        if (_armiesLoaded) return Armies;
        if (Header == null) GetHeaderData();
        if (!_buildingsLoaded) GetBuildingData();

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
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
        if (!_armiesLoaded) GetTroopData();

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd + (Header.TroopCount * BTLArmyModule.GetArmySize(BtlVersion));
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

        int dataEnd = ConquestOffsets.Calculate(Header.ArmyCount, Header.SelectableTileCount, Header.BuildingCount).dataEnd;
        int offset = dataEnd +
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

    #region 直接操作 MapData 的静态方法 - 消除数据重复

    /// <summary>
    /// 从文件加载所有数据到 MapData，Parser 不持有数据副本。
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
    /// 从 Parser 填充 MapData，Parser 用完即弃。
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

        mapData.Legions = new System.Collections.ObjectModel.ObservableCollection<Legion>(parser.GetLegionData());
        mapData.Belongs = parser.GetBelongData();
        mapData.BelongOffset = parser.BelongOffset;
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
    }

    /// <summary>
    /// 创建新的征服 MapData。
    /// </summary>
    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions, int mapNumber = 0)
    {
        int totalTiles = mapWidth * mapHeight;
        var mapData = new MapData();
        mapData.Header = new BTLHeader
        {
            BtlVersion = 1,
            MapNumber = mapNumber,
            MapClipX = 0,
            MapClipY = 2,
            MapLength = mapHeight,
            MapWidth = mapWidth,
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
            mapData.Belongs.Add("FF");

        mapData.BelongOffset = false;
        mapData.FilePath = string.Empty;
        return mapData;
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

        if (mapData.TerrainCount != header.SelectableTileCount)
            header.SelectableTileCount = mapData.TerrainCount;

        if (mapData.UnitPlaces.Count != header.PlacementA + header.PlacementB)
        {
            header.PlacementA = mapData.UnitPlaces.Count;
            header.PlacementB = 0;
        }

        if (mapData.Capitals.Count != header.ConqueredFlagPosition)
            header.ConqueredFlagPosition = mapData.Capitals.Count;

        var resultData = new List<byte>();
        resultData.AddRange(header.ToBytes());

        foreach (var legion in mapData.Legions)
        {
            var buf = new byte[300];
            legion.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        resultData.AddRange(mapData.ProvincesToBytes());

        foreach (var belong in mapData.Belongs)
        {
            byte belongByte = byte.Parse(belong, System.Globalization.NumberStyles.HexNumber);
            if (mapData.BelongOffset)
                belongByte = (byte)((belongByte - 1) & 0xFF);
            resultData.Add(belongByte);
        }

        foreach (var building in mapData.Buildings)
        {
            var buf = new byte[32];
            building.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        if (header.BtlVersion >= 3)
        {
            foreach (var troop in mapData.ArmiesV3)
            {
                var buf = new byte[64];
                troop.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }
        else
        {
            foreach (var troop in mapData.Armies)
            {
                var buf = new byte[48];
                troop.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }

        foreach (var trap in mapData.Traps)
        {
            var buf = new byte[12];
            trap.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var caseItem in mapData.Cases)
        {
            var buf = new byte[16];
            caseItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var weather in mapData.Weathers)
        {
            var buf = new byte[16];
            weather.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var eventItem in mapData.Events)
        {
            var buf = new byte[44];
            eventItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        if (header.BtlVersion >= 3)
        {
            foreach (var r in mapData.ReinforcementsV3)
            {
                var buf = new byte[104];
                r.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }
        else
        {
            foreach (var r in mapData.Reinforcements)
            {
                var buf = new byte[80];
                r.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }

        foreach (var airforce in mapData.AirForces)
        {
            var buf = new byte[20];
            airforce.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var unitPlace in mapData.UnitPlaces)
        {
            var buf = new byte[8];
            unitPlace.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var capital in mapData.Capitals)
        {
            var buf = new byte[4];
            capital.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var sc in mapData.StrategyConstructions)
        {
            var buf = new byte[16];
            sc.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var airsupport in mapData.AirSupports)
        {
            var buf = new byte[16];
            airsupport.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        try
        {
            File.WriteAllBytes(outputPath, resultData.ToArray());
            Debug.WriteLine($"[ConquestParser] 成功保存到: {outputPath} ({resultData.Count} 字节)");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquestParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region 兼容旧接口 - 供 BTLAnalyzer / CLI 使用

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions, int mapNumber = 0)
    {
        try
        {
            int totalTiles = mapWidth * mapHeight;
            Header = new BTLHeader
            {
                BtlVersion = 1,
                MapNumber = mapNumber,
                MapClipX = 0,
                MapClipY = 2,
                MapLength = mapHeight,
                MapWidth = mapWidth,
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
                SelectableTileCount = totalTiles,
                AccumulatedEconomy = 0,
                AccumulatedIndustry = 0,
                AccumulatedTech = 0
            };
            Legions.Clear(); Provinces.Clear(); Belongs.Clear(); Buildings.Clear();
            Armies.Clear(); ArmiesV3.Clear(); Traps.Clear(); Cases.Clear();
            Weathers.Clear(); Events.Clear();
            Reinforcements.Clear(); ReinforcementsV3.Clear(); AirForces.Clear();
            UnitPlaces.Clear(); Capitals.Clear(); StrategyConstructions.Clear(); AirSupports.Clear();

            for (int i = 0; i < numLegions; i++)
                Legions.Add(Legion.CreateDefault(i + 1));
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
            BelongOffset = false;
            Debug.WriteLine($"[ConquestParser] 成功创建新征服: {mapWidth}x{mapHeight}, {numLegions}个军团, 地图编号{mapNumber}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquestParser] 创建新征服失败: {ex.Message}");
            return false;
        }
    }

    public bool SaveData(string outputPath)
    {
        if (Header == null)
        {
            Debug.WriteLine("[ConquestParser] 错误：头部数据尚未解析");
            return false;
        }

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

        if (Provinces.Count != Header.SelectableTileCount)
            Header.SelectableTileCount = Provinces.Count;

        if (Belongs.Count != Header.SelectableTileCount)
            Header.SelectableTileCount = Belongs.Count;

        int totalPlacementCount = Header.PlacementA + Header.PlacementB;
        if (UnitPlaces.Count != totalPlacementCount)
        {
            Header.PlacementA = UnitPlaces.Count;
            Header.PlacementB = 0;
        }

        if (Capitals.Count != Header.ConqueredFlagPosition)
            Header.ConqueredFlagPosition = Capitals.Count;

        var resultData = new List<byte>();
        resultData.AddRange(Header.ToBytes());

        foreach (var legion in Legions)
        {
            var buf = new byte[300];
            legion.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var province in Provinces)
        {
            var provinceBytes = new byte[2];
            province.ToBytes(provinceBytes, 0);
            resultData.AddRange(provinceBytes);
        }

        foreach (var belong in Belongs)
        {
            byte belongByte = byte.Parse(belong, System.Globalization.NumberStyles.HexNumber);
            if (BelongOffset)
                belongByte = (byte)((belongByte - 1) & 0xFF);
            resultData.Add(belongByte);
        }

        foreach (var building in Buildings)
        {
            var buf = new byte[32];
            building.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        if (BtlVersion >= 3)
        {
            foreach (var troop in ArmiesV3)
            {
                var buf = new byte[64];
                troop.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }
        else
        {
            foreach (var troop in Armies)
            {
                var buf = new byte[48];
                troop.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }

        foreach (var trap in Traps)
        {
            var buf = new byte[12];
            trap.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var caseItem in Cases)
        {
            var buf = new byte[16];
            caseItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var weather in Weathers)
        {
            var buf = new byte[16];
            weather.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var eventItem in Events)
        {
            var buf = new byte[44];
            eventItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        if (BtlVersion >= 3)
        {
            foreach (var r in ReinforcementsV3)
            {
                var buf = new byte[104];
                r.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }
        else
        {
            foreach (var r in Reinforcements)
            {
                var buf = new byte[80];
                r.ToBytes(buf, 0);
                resultData.AddRange(buf);
            }
        }

        foreach (var airforce in AirForces)
        {
            var buf = new byte[20];
            airforce.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var unitPlace in UnitPlaces)
        {
            var buf = new byte[8];
            unitPlace.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var capital in Capitals)
        {
            var buf = new byte[4];
            capital.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var sc in StrategyConstructions)
        {
            var buf = new byte[16];
            sc.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        foreach (var airsupport in AirSupports)
        {
            var buf = new byte[16];
            airsupport.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        try
        {
            File.WriteAllBytes(outputPath, resultData.ToArray());
            Debug.WriteLine($"[ConquestParser] 成功保存到: {outputPath} ({resultData.Count} 字节)");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquestParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    #endregion
}