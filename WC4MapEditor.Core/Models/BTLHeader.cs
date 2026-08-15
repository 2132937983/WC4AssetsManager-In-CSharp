using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

public class BTLHeader
{
    public int BtlVersion { get; set; }
    public int MapNumber { get; set; }
    public int MapClipX { get; set; }
    public int MapClipY { get; set; }
    public int MapLength { get; set; }
    public int MapWidth { get; set; }
    public int ArmyCount { get; set; }
    public int BuildingCount { get; set; }
    public int TroopCount { get; set; }
    public int PlanCount { get; set; }
    public int EventCount { get; set; }
    public int WeatherCount { get; set; }
    public int VictoryCondition { get; set; }
    public int MinTurns { get; set; }
    public int MaxTurns { get; set; }
    public int ReinforcementCount { get; set; }
    public int AirRaidCount { get; set; }
    public int PlacementA { get; set; }
    public int PlacementB { get; set; }
    public int ConqueredFlagPosition { get; set; }
    public int Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int SelectableTileCount { get; set; }
    public int AccumulatedEconomy { get; set; }
    public int AccumulatedIndustry { get; set; }
    public int AccumulatedTech { get; set; }
    public int TrapCount { get; set; }
    public int Unknown5 { get; set; }
    public int StrategyCount { get; set; }
    public int Unknown6 { get; set; }
    public int Unknown7 { get; set; }
    public int AirSupportCount { get; set; }

    public int ArmyGroupCount => TroopCount;

    public static BTLHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128)
            throw new ArgumentException("文件头数据不足128字节");

        return new BTLHeader
        {
            BtlVersion = BitConverter.ToInt32(data[0x00..]),
            MapNumber = BitConverter.ToInt32(data[0x04..]),
            MapClipX = BitConverter.ToInt32(data[0x08..]),
            MapClipY = BitConverter.ToInt32(data[0x0C..]),
            MapLength = BitConverter.ToInt32(data[0x10..]),
            MapWidth = BitConverter.ToInt32(data[0x14..]),
            ArmyCount = BitConverter.ToInt32(data[0x18..]),
            BuildingCount = BitConverter.ToInt32(data[0x1C..]),
            TroopCount = BitConverter.ToInt32(data[0x20..]),
            PlanCount = BitConverter.ToInt32(data[0x24..]),
            EventCount = BitConverter.ToInt32(data[0x28..]),
            WeatherCount = BitConverter.ToInt32(data[0x2C..]),
            VictoryCondition = BitConverter.ToInt32(data[0x30..]),
            MinTurns = BitConverter.ToInt32(data[0x34..]),
            MaxTurns = BitConverter.ToInt32(data[0x38..]),
            ReinforcementCount = BitConverter.ToInt32(data[0x3C..]),
            AirRaidCount = BitConverter.ToInt32(data[0x40..]),
            PlacementA = BitConverter.ToInt32(data[0x44..]),
            PlacementB = BitConverter.ToInt32(data[0x48..]),
            ConqueredFlagPosition = BitConverter.ToInt32(data[0x4C..]),
            Unknown3 = BitConverter.ToInt32(data[0x50..]),
            Unknown4 = BitConverter.ToInt32(data[0x54..]),
            SelectableTileCount = BitConverter.ToInt32(data[0x58..]),
            AccumulatedEconomy = BitConverter.ToInt32(data[0x5C..]),
            AccumulatedIndustry = BitConverter.ToInt32(data[0x60..]),
            AccumulatedTech = BitConverter.ToInt32(data[0x64..]),
            TrapCount = BitConverter.ToInt32(data[0x68..]),
            Unknown5 = BitConverter.ToInt32(data[0x6C..]),
            StrategyCount = BitConverter.ToInt32(data[0x70..]),
            Unknown6 = BitConverter.ToInt32(data[0x74..]),
            Unknown7 = BitConverter.ToInt32(data[0x78..]),
            AirSupportCount = BitConverter.ToInt32(data[0x7C..])
        };
    }

    public byte[] ToBytes()
    {
        var data = new byte[128];
        Array.Copy(BitConverter.GetBytes(BtlVersion), 0, data, 0x00, 4);
        Array.Copy(BitConverter.GetBytes(MapNumber), 0, data, 0x04, 4);
        Array.Copy(BitConverter.GetBytes(MapClipX), 0, data, 0x08, 4);
        Array.Copy(BitConverter.GetBytes(MapClipY), 0, data, 0x0C, 4);
        Array.Copy(BitConverter.GetBytes(MapLength), 0, data, 0x10, 4);
        Array.Copy(BitConverter.GetBytes(MapWidth), 0, data, 0x14, 4);
        Array.Copy(BitConverter.GetBytes(ArmyCount), 0, data, 0x18, 4);
        Array.Copy(BitConverter.GetBytes(BuildingCount), 0, data, 0x1C, 4);
        Array.Copy(BitConverter.GetBytes(TroopCount), 0, data, 0x20, 4);
        Array.Copy(BitConverter.GetBytes(PlanCount), 0, data, 0x24, 4);
        Array.Copy(BitConverter.GetBytes(EventCount), 0, data, 0x28, 4);
        Array.Copy(BitConverter.GetBytes(WeatherCount), 0, data, 0x2C, 4);
        Array.Copy(BitConverter.GetBytes(VictoryCondition), 0, data, 0x30, 4);
        Array.Copy(BitConverter.GetBytes(MinTurns), 0, data, 0x34, 4);
        Array.Copy(BitConverter.GetBytes(MaxTurns), 0, data, 0x38, 4);
        Array.Copy(BitConverter.GetBytes(ReinforcementCount), 0, data, 0x3C, 4);
        Array.Copy(BitConverter.GetBytes(AirRaidCount), 0, data, 0x40, 4);
        Array.Copy(BitConverter.GetBytes(PlacementA), 0, data, 0x44, 4);
        Array.Copy(BitConverter.GetBytes(PlacementB), 0, data, 0x48, 4);
        Array.Copy(BitConverter.GetBytes(ConqueredFlagPosition), 0, data, 0x4C, 4);
        Array.Copy(BitConverter.GetBytes(Unknown3), 0, data, 0x50, 4);
        Array.Copy(BitConverter.GetBytes(Unknown4), 0, data, 0x54, 4);
        Array.Copy(BitConverter.GetBytes(SelectableTileCount), 0, data, 0x58, 4);
        Array.Copy(BitConverter.GetBytes(AccumulatedEconomy), 0, data, 0x5C, 4);
        Array.Copy(BitConverter.GetBytes(AccumulatedIndustry), 0, data, 0x60, 4);
        Array.Copy(BitConverter.GetBytes(AccumulatedTech), 0, data, 0x64, 4);
        Array.Copy(BitConverter.GetBytes(TrapCount), 0, data, 0x68, 4);
        Array.Copy(BitConverter.GetBytes(Unknown5), 0, data, 0x6C, 4);
        Array.Copy(BitConverter.GetBytes(StrategyCount), 0, data, 0x70, 4);
        Array.Copy(BitConverter.GetBytes(Unknown6), 0, data, 0x74, 4);
        Array.Copy(BitConverter.GetBytes(Unknown7), 0, data, 0x78, 4);
        Array.Copy(BitConverter.GetBytes(AirSupportCount), 0, data, 0x7C, 4);
        return data;
    }

    public static BTLHeader CreateDefault() => new BTLHeader
    {
        BtlVersion = 1,
        MapNumber = 1,
        MapClipX = 0,
        MapClipY = 0,
        MapLength = 30,
        MapWidth = 40,
        ArmyCount = 0,
        BuildingCount = 0,
        TroopCount = 0,
        PlanCount = 0,
        EventCount = 0,
        WeatherCount = 0,
        VictoryCondition = 0,
        MinTurns = 1,
        MaxTurns = 99,
        ReinforcementCount = 0,
        AirRaidCount = 0,
        PlacementA = 0,
        PlacementB = 0,
        ConqueredFlagPosition = 0,
        Unknown3 = 0,
        Unknown4 = 0,
        SelectableTileCount = 0,
        AccumulatedEconomy = 0,
        AccumulatedIndustry = 0,
        AccumulatedTech = 0,
        TrapCount = 0,
        Unknown5 = 0,
        StrategyCount = 0,
        Unknown6 = 0,
        Unknown7 = 0,
        AirSupportCount = 0
    };

    public int TotalTiles => MapWidth * MapLength;
}
