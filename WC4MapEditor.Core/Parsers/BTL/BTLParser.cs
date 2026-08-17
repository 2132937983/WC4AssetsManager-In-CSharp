using System.Diagnostics;
using System.IO;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.BTL;

namespace WC4MapEditor.Parsers.BTL;

/// <summary>
/// BTL 文件解析主入口。
/// 根据文件类型自动选择 StageParser 或 ConquestParser。
/// </summary>
public static class BTLParser
{
    /// <summary>
    /// 从文件加载地图数据。
    /// </summary>
    public static MapData LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        byte[] data = File.ReadAllBytes(filePath);
        Debug.WriteLine($"[BTLParser] 文件 {filePath} 大小：{data.Length} 字节");
        return LoadFromBytes(data, filePath);
    }

    /// <summary>
    /// 从字节数组加载地图数据。
    /// </summary>
    public static MapData LoadFromBytes(byte[] data, string filePath)
    {
        if (data.Length < BTLSize.HEADER_SIZE)
            throw new ArgumentException("数据长度不足，无法解析文件头");

        var mapData = new MapData();
        var header = BTLHeaderModule.Parse(data);

        mapData.Header = header;
        // 注意：与VB版本保持一致，MapLength存宽度，MapWidth存高度
        mapData.MapWidth = header.MapLength;
        mapData.MapHeight = header.MapWidth;
        mapData.FilePath = filePath;

        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

        // 使用固定偏移解析（通用 BTL 格式）
        long terrainOffset = BTLSize.HEADER_SIZE;
        long provinceOffset = terrainOffset + ((long)header.TotalTiles * BTLSize.TERRAIN_SIZE);
        long buildingOffset = provinceOffset + ((long)header.TotalTiles * BTLSize.PROVINCE_SIZE);
        long armyOffset = buildingOffset + ((long)header.BuildingCount * BTLSize.BUILDING_SIZE);
        long legionOffset = armyOffset + ((long)header.ArmyGroupCount * BTLSize.ARMY_SIZE);

        // 解析地形
        for (int i = 0; i < header.TotalTiles; i++)
        {
            long offset = terrainOffset + ((long)i * 16);
            if (offset + 16 <= data.Length)
            {
                var terrain = Terrain.FromBytes(data, (int)offset);
                int row = i / mapData.MapWidth;
                int col = i % mapData.MapWidth;
                mapData.SetTerrain(col, row, terrain.ToTerrainData());
            }
        }

        // 解析省份
        mapData.LoadProvincesFromBytes(data, provinceOffset, header.TotalTiles);

        // 解析建筑
        mapData.Buildings.Clear();
        for (int i = 0; i < header.BuildingCount; i++)
        {
            long offset = buildingOffset + ((long)i * BTLSize.BUILDING_SIZE);
            if (offset + BTLSize.BUILDING_SIZE <= data.Length)
            {
                var building = Building.FromBytes(data, (int)offset);
                mapData.Buildings.Add(building);
            }
        }

        // 解析部队
        mapData.Armies.Clear();
        for (int i = 0; i < header.ArmyGroupCount; i++)
        {
            long offset = armyOffset + ((long)i * BTLSize.ARMY_SIZE);
            if (offset + BTLSize.ARMY_SIZE <= data.Length)
            {
                var army = Army.FromBytes(data, (int)offset);
                mapData.Armies.Add(army);
            }
        }

        // 解析军团
        mapData.Legions.Clear();
        for (int i = 0; i < header.ArmyCount; i++)
        {
            long offset = legionOffset + ((long)i * BTLSize.LEGION_SIZE);
            if (offset + BTLSize.LEGION_SIZE <= data.Length)
            {
                var legion = Legion.FromBytes(data, (int)offset);
                mapData.Legions.Add(legion);
            }
        }

        mapData.IsModified = false;
        return mapData;
    }

    /// <summary>
    /// 保存地图数据到文件。
    /// </summary>
    public static void SaveToFile(MapData mapData, string filePath)
    {
        byte[] data = SaveToBytes(mapData);
        File.WriteAllBytes(filePath, data);
        mapData.FilePath = filePath;
        mapData.IsModified = false;
    }

    /// <summary>
    /// 将地图数据序列化为字节数组。
    /// </summary>
    public static byte[] SaveToBytes(MapData mapData)
    {
        // 注意：与VB版本保持一致，MapLength存宽度，MapWidth存高度
        mapData.Header.MapLength = mapData.MapWidth;
        mapData.Header.MapWidth = mapData.MapHeight;
        mapData.Header.ArmyCount = mapData.Legions.Count;
        mapData.Header.BuildingCount = mapData.Buildings.Count;
        mapData.Header.TroopCount = mapData.Armies.Count;

        long totalSize = BTLSize.HEADER_SIZE +
                         ((long)mapData.Header.TotalTiles * BTLSize.TERRAIN_SIZE) +
                         ((long)mapData.Header.TotalTiles * BTLSize.PROVINCE_SIZE) +
                         ((long)mapData.Header.BuildingCount * BTLSize.BUILDING_SIZE) +
                         ((long)mapData.Header.TroopCount * BTLSize.ARMY_SIZE) +
                         ((long)mapData.Header.ArmyCount * BTLSize.LEGION_SIZE);

        if (totalSize > int.MaxValue)
            throw new OverflowException($"地图数据太大，无法保存到字节数组。需要的字节数: {totalSize}");

        var data = new byte[(int)totalSize];

        byte[] headerBytes = mapData.Header.ToBytes();
        Array.Copy(headerBytes, 0, data, 0, BTLSize.HEADER_SIZE);

        long terrainOffset = BTLSize.HEADER_SIZE;
        long provinceOffset = terrainOffset + ((long)mapData.Header.TotalTiles * BTLSize.TERRAIN_SIZE);
        long buildingOffset = provinceOffset + ((long)mapData.Header.TotalTiles * BTLSize.PROVINCE_SIZE);
        long armyOffset = buildingOffset + ((long)mapData.Header.BuildingCount * BTLSize.BUILDING_SIZE);
        long legionOffset = armyOffset + ((long)mapData.Header.ArmyGroupCount * BTLSize.ARMY_SIZE);

        // 写入地形
        byte[] terrainBytes = mapData.TerrainsToBytes();
        Array.Copy(terrainBytes, 0, data, terrainOffset, terrainBytes.Length);

        // 写入省份
        byte[] provinceBytes = mapData.ProvincesToBytes();
        Array.Copy(provinceBytes, 0, data, provinceOffset, provinceBytes.Length);

        // 写入建筑
        for (int i = 0; i < mapData.Buildings.Count; i++)
        {
            long offset = buildingOffset + ((long)i * BTLSize.BUILDING_SIZE);
            mapData.Buildings[i].ToBytes(data.AsSpan(), (int)offset);
        }

        // 写入部队
        for (int i = 0; i < mapData.Armies.Count; i++)
        {
            long offset = armyOffset + ((long)i * BTLSize.ARMY_SIZE);
            mapData.Armies[i].ToBytes(data.AsSpan(), (int)offset);
        }

        // 写入军团
        for (int i = 0; i < mapData.Legions.Count; i++)
        {
            long offset = legionOffset + ((long)i * BTLSize.LEGION_SIZE);
            mapData.Legions[i].ToBytes(data.AsSpan(), (int)offset);
        }

        return data;
    }

    /// <summary>
    /// 创建新的空白地图。
    /// </summary>
    public static MapData CreateNew(int width, int height)
    {
        var mapData = new MapData();
        mapData.MapWidth = width;
        mapData.MapHeight = height;
        // 注意：与VB版本保持一致，MapLength存宽度，MapWidth存高度
        mapData.Header.MapLength = width;
        mapData.Header.MapWidth = height;
        mapData.FilePath = string.Empty;
        mapData.IsModified = true;
        return mapData;
    }
}