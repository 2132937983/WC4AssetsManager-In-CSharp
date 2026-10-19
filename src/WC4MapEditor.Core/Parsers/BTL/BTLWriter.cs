using System.IO;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 文件序列化（战役 / 征服两种布局共用）：把 MapData 排成与 <see cref="BTLLayout"/> 一致的字节流。
/// <para>
/// 原则：保存不"规整"文件里原有的数量字段 —— 只有数量真的变了（增删了实体）才改头部；
/// 省份 / 归属块的格数取两者较大者，不足的省份补 0（官方文件补齐到 8 的倍数的格子就是全 0）、
/// 不足的归属补 0xFF；文件末尾的扩展块原样写回。
/// </para>
/// </summary>
public static class BTLWriter
{
    /// <summary>
    /// 把实体数量同步进文件头：军团、建筑、单位、方案、事件、天气、援军、空袭、陷阱、战略建设、空中支援。
    /// 单位放置（PlacementA + PlacementB）与首都数只在数量不符时才改写，保留头部里的 A / B 划分。
    /// </summary>
    public static void SyncEntityCounts(MapData map)
    {
        var header = map.Header;
        bool v3 = header.UsesV3Records;

        header.ArmyCount = map.Legions.Count;
        header.BuildingCount = map.Buildings.Count;
        header.TroopCount = v3 ? map.ArmiesV3.Count : map.Armies.Count;
        header.PlanCount = map.Cases.Count;
        header.EventCount = map.Events.Count;
        header.WeatherCount = map.Weathers.Count;
        header.ReinforcementCount = v3 ? map.ReinforcementsV3.Count : map.Reinforcements.Count;
        header.AirRaidCount = map.AirForces.Count;
        header.TrapCount = map.Traps.Count;
        header.StrategyCount = map.StrategyConstructions.Count;
        header.AirSupportCount = map.AirSupports.Count;

        if (map.UnitPlaces.Count != header.PlacementA + header.PlacementB)
        {
            header.PlacementA = map.UnitPlaces.Count;
            header.PlacementB = 0;
        }

        if (map.Capitals.Count != header.ConqueredFlagPosition)
            header.ConqueredFlagPosition = map.Capitals.Count;
    }

    /// <summary>
    /// 序列化整个文件。<paramref name="terrain"/> 为 null = 征服布局（没有地形块）；
    /// <paramref name="provinces"/> 是省份块的字节（按文件顺序，不含补齐部分）。
    /// 头部的 SelectableTileCount 会被设为实际写出的省份 / 归属格数。
    /// </summary>
    public static byte[] Build(MapData map, byte[]? terrain, byte[] provinces)
    {
        var header = map.Header;
        int tiles = Math.Max(provinces.Length / BTLSize.PROVINCE_SIZE, map.Belongs.Count);
        header.SelectableTileCount = tiles;

        using var stream = new MemoryStream();
        stream.Write(header.ToBytes());

        WriteRecords(stream, map.Legions, BTLSize.LEGION_SIZE, (r, b) => r.ToBytes(b, 0));
        if (terrain != null) stream.Write(terrain);

        stream.Write(provinces);
        stream.Write(new byte[tiles * BTLSize.PROVINCE_SIZE - provinces.Length]);

        for (int i = 0; i < tiles; i++)
            stream.WriteByte(i < map.Belongs.Count ? (byte)map.GetBelongValueByIndex(i) : (byte)0xFF);

        bool v3 = header.UsesV3Records;
        WriteRecords(stream, map.Buildings, BTLSize.BUILDING_SIZE, (r, b) => r.ToBytes(b, 0));
        if (v3) WriteRecords(stream, map.ArmiesV3, BTLArmyModule.GetArmySize(true), (r, b) => r.ToBytes(b, 0));
        else WriteRecords(stream, map.Armies, BTLArmyModule.GetArmySize(false), (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.Traps, BTLSize.TRAP_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.Cases, BTLSize.CASE_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.Weathers, BTLSize.WEATHER_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.Events, BTLSize.EVENT_SIZE, (r, b) => r.ToBytes(b, 0));
        if (v3) WriteRecords(stream, map.ReinforcementsV3, BTLReinforcementModule.GetReinforcementSize(true), (r, b) => r.ToBytes(b, 0));
        else WriteRecords(stream, map.Reinforcements, BTLReinforcementModule.GetReinforcementSize(false), (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.AirForces, BTLSize.AIRFORCE_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.UnitPlaces, BTLSize.UNITPLACEMENT_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.Capitals, BTLSize.CAPITAL_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.StrategyConstructions, BTLSize.STRATEGY_SIZE, (r, b) => r.ToBytes(b, 0));
        WriteRecords(stream, map.AirSupports, BTLSize.AIRSUPPORT_SIZE, (r, b) => r.ToBytes(b, 0));
        stream.Write(map.ExtensionData);

        return stream.ToArray();
    }

    private static void WriteRecords<T>(Stream stream, IEnumerable<T> records, int recordSize, Action<T, byte[]> write)
    {
        var buffer = new byte[recordSize];
        foreach (var record in records)
        {
            Array.Clear(buffer);
            write(record, buffer);
            stream.Write(buffer);
        }
    }
}
