using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.BTL;

namespace WC4MapEditor.Parsers.Conquest;

/// <summary>
/// 征服模式解析器。
/// 使用 BTL 模块组合解析，与 StageParser 的主要区别：
/// 1. 没有 Terrain 数据
/// 2. Belong 偏移量处理逻辑不同
/// </summary>
public class ConquestParser
{
    public byte[] HexData { get; private set; } = null!;
    public string HexFilePath { get; private set; } = "";
    public BTLHeader Header { get; private set; } = null!;
    public int BtlVersion => Header?.BtlVersion ?? 0;

    // 归属偏移标记（征服模式特有）
    public bool BelongOffset { get; private set; }

    // 解析结果
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

    // 懒加载标记
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

        // 征服模式特有的归属偏移检测
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

    public bool SaveData(string outputPath)
    {
        if (Header == null)
        {
            Debug.WriteLine("[ConquestParser] 错误：头部数据尚未解析");
            return false;
        }

        // 更新头部计数
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

        // 写入文件头
        resultData.AddRange(Header.ToBytes());

        // 写入军团
        foreach (var legion in Legions)
        {
            var buf = new byte[300];
            legion.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入省份
        foreach (var province in Provinces)
        {
            var provinceBytes = new byte[2];
            province.ToBytes(provinceBytes, 0);
            resultData.AddRange(provinceBytes);
        }

        // 写入归属（处理偏移）
        foreach (var belong in Belongs)
        {
            byte belongByte = byte.Parse(belong, System.Globalization.NumberStyles.HexNumber);
            if (BelongOffset)
                belongByte = (byte)((belongByte - 1) & 0xFF);
            resultData.Add(belongByte);
        }

        // 写入建筑
        foreach (var building in Buildings)
        {
            var buf = new byte[32];
            building.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入部队
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

        // 写入陷阱
        foreach (var trap in Traps)
        {
            var buf = new byte[12];
            trap.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入方案
        foreach (var caseItem in Cases)
        {
            var buf = new byte[16];
            caseItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入天气
        foreach (var weather in Weathers)
        {
            var buf = new byte[16];
            weather.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入事件
        foreach (var eventItem in Events)
        {
            var buf = new byte[44];
            eventItem.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入增援
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

        // 写入空军
        foreach (var airforce in AirForces)
        {
            var buf = new byte[20];
            airforce.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入单位部署
        foreach (var unitPlace in UnitPlaces)
        {
            var buf = new byte[8];
            unitPlace.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入首都
        foreach (var capital in Capitals)
        {
            var buf = new byte[4];
            capital.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入战略建筑
        foreach (var sc in StrategyConstructions)
        {
            var buf = new byte[16];
            sc.ToBytes(buf, 0);
            resultData.AddRange(buf);
        }

        // 写入空中支援
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
}
