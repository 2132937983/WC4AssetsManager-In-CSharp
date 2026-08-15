using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.BTL;

namespace WC4MapEditor.Parsers.Stage;

/// <summary>
/// 战役关卡解析器。
/// 使用 BTL 模块组合解析，支持懒加载。
/// </summary>
public class StageParser
{
    public byte[] HexData { get; private set; } = null!;
    public string HexFilePath { get; private set; } = "";
    public BTLHeader Header { get; private set; } = null!;
    public int BtlVersion => Header?.BtlVersion ?? 0;

    // 解析结果
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

    // 懒加载标记
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

            // 重置加载标记
            ResetLoadFlags();

            // 解析文件头
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
}
