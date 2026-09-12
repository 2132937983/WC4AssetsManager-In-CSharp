using System.Diagnostics;
using System.IO;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.World;

/// <summary>
/// 世界地图解析器。
/// 支持大文件流式解析。
/// </summary>
public static class WorldParser
{
    private static readonly byte[] ExpectedHeader = { 0x59, 0x53, 0x41, 0x45, 0x04, 0x00, 0x00, 0x00 };

    public static MapData LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;

        Debug.WriteLine($"[WorldParser] 文件大小: {fileSize:N0} 字节 ({fileSize / (1024.0 * 1024.0 * 1024.0):F2} GB)");

        if (fileSize < 1024L * 1024 * 1024)
        {
            byte[] data = File.ReadAllBytes(filePath);
            return LoadFromBytes(data, filePath);
        }

        return LoadFromFileStream(filePath, fileSize);
    }

    private static MapData LoadFromFileStream(string filePath, long fileSize)
    {
        double memoryStart = GetProcessMemoryMB();
        Debug.WriteLine($"[WorldParser] 开始流式解析大文件，当前内存: {memoryStart:F2} MB");

        var mapData = new MapData();
        mapData.FilePath = filePath;

        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan))
        {
            var headerBuffer = new byte[16];
            if (stream.Read(headerBuffer, 0, 16) != 16)
                throw new ArgumentException("无法读取文件头");

            bool headerValid = true;
            for (int i = 0; i < 8; i++)
            {
                if (headerBuffer[i] != ExpectedHeader[i])
                {
                    headerValid = false;
                    break;
                }
            }

            if (!headerValid)
                Debug.WriteLine("[WorldParser] 警告：文件头不匹配");

            mapData.MapWidth = BitConverter.ToUInt16(headerBuffer, 8);
            mapData.MapHeight = BitConverter.ToUInt16(headerBuffer, 12);
            mapData.Header.MapWidth = mapData.MapWidth;
            mapData.Header.MapLength = mapData.MapHeight;

            Debug.WriteLine($"[WorldParser] 地图尺寸: {mapData.MapWidth}x{mapData.MapHeight}");

            int terrainCount = mapData.MapWidth * mapData.MapHeight;
            mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

            long terrainStart = WorldOffsets.GetTerrainStartOffset();
            var terrainBuffer = new byte[4096 * WorldOffsets.TerrainSize];

            stream.Position = terrainStart;
            int terrainIndex = 0;

            while (terrainIndex < terrainCount)
            {
                int remainingTerrains = terrainCount - terrainIndex;
                int terrainsToRead = Math.Min(remainingTerrains, 4096);
                int bytesToRead = terrainsToRead * WorldOffsets.TerrainSize;

                int bytesRead = stream.Read(terrainBuffer, 0, bytesToRead);
                if (bytesRead < bytesToRead)
                {
                    Debug.WriteLine($"[WorldParser] 警告: 只读取了 {bytesRead} 字节，期望 {bytesToRead}");
                    break;
                }

                mapData.LoadTerrainsFromBytesChunk(terrainBuffer, terrainIndex, terrainsToRead);
                terrainIndex += terrainsToRead;

                if (terrainIndex % 10000 == 0)
                    Debug.WriteLine($"[WorldParser] 已读取 {terrainIndex}/{terrainCount} 个地形 ({terrainIndex * 100 / terrainCount}%)");
            }

            Debug.WriteLine($"[WorldParser] 成功读取 {terrainIndex} 个地形");

            long terrainEnd = WorldOffsets.GetTerrainEndOffset(terrainCount);
            long remainingBytes = fileSize - terrainEnd;

            if (remainingBytes > 0)
            {
                int provinceCount = (int)(remainingBytes / WorldOffsets.ProvinceSize);
                provinceCount = Math.Min(provinceCount, terrainCount);

                Debug.WriteLine($"[WorldParser] 省份数据: {provinceCount} 个");

                stream.Position = terrainEnd;

                var provinceBuffer = new byte[8192];
                int provinceIndex = 0;

                while (provinceIndex < provinceCount)
                {
                    int remainingProvinces = provinceCount - provinceIndex;
                    int provincesToRead = Math.Min(remainingProvinces, provinceBuffer.Length / WorldOffsets.ProvinceSize);
                    int bytesToRead = provincesToRead * WorldOffsets.ProvinceSize;

                    int bytesRead = stream.Read(provinceBuffer, 0, bytesToRead);
                    if (bytesRead <= 0) break;

                    int actualProvinces = bytesRead / WorldOffsets.ProvinceSize;
                    for (int i = 0; i < actualProvinces; i++)
                    {
                        ushort provinceValue = BitConverter.ToUInt16(provinceBuffer, i * WorldOffsets.ProvinceSize);
                        mapData.SetProvince(provinceIndex + i, new Province
                        {
                            CountryId = (byte)(provinceValue & 0xFF),
                            Reserved = (byte)((provinceValue >> 8) & 0xFF)
                        });
                    }

                    provinceIndex += actualProvinces;
                }

                Debug.WriteLine($"[WorldParser] 成功读取 {provinceIndex} 个省份");
            }
        }

        double memoryEnd = GetProcessMemoryMB();
        Debug.WriteLine($"[WorldParser] 流式解析完成，内存增加: {memoryEnd - memoryStart:F2} MB");

        return mapData;
    }

    public static MapData LoadFromBytes(byte[] data, string filePath)
    {
        if (data.Length < 16)
            throw new ArgumentException("数据长度不足，无法解析文件头");

        var mapData = new MapData();
        mapData.FilePath = filePath;

        bool headerValid = true;
        for (int i = 0; i < 8; i++)
        {
            if (i < data.Length && data[i] != ExpectedHeader[i])
            {
                headerValid = false;
                break;
            }
        }

        if (!headerValid)
            Debug.WriteLine("[WorldParser] 警告：文件头不匹配");

        mapData.MapWidth = BitConverter.ToUInt16(data, 8);
        mapData.MapHeight = BitConverter.ToUInt16(data, 12);

        Debug.WriteLine($"[WorldParser] 解析到地图尺寸: 宽度={mapData.MapWidth}, 高度={mapData.MapHeight}");

        int terrainCount = mapData.MapWidth * mapData.MapHeight;

        mapData.Header.MapWidth = mapData.MapWidth;
        mapData.Header.MapLength = mapData.MapHeight;

        ParseTerrainDataOptimized(data, mapData, terrainCount);

        return mapData;
    }

    private static void ParseTerrainDataOptimized(byte[] data, MapData mapData, int terrainCount)
    {
        int maxTerrainCount = (data.Length - WorldOffsets.HeaderSize) / WorldOffsets.TerrainSize;
        int actualTerrainCount = Math.Min(terrainCount, maxTerrainCount);

        Debug.WriteLine($"[WorldParser] 期望地形数: {terrainCount}, 最大可用: {maxTerrainCount}, 实际解析: {actualTerrainCount}");

        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);
        mapData.LoadTerrainsFromBytes(data, WorldOffsets.HeaderSize, actualTerrainCount);

        Debug.WriteLine($"[WorldParser] 成功解析 {actualTerrainCount} 个地形到优化存储");
    }

    public static MapData CreateNew(int width, int height)
    {
        var mapData = new MapData();
        mapData.FilePath = string.Empty;
        mapData.MapWidth = width;
        mapData.MapHeight = height;
        mapData.Header.MapWidth = width;
        mapData.Header.MapLength = height;

        mapData.InitializeTerrain(width, height);

        long totalCells = (long)width * height;
        for (int y = 0; y < height; y++)
        {
            if (y % 1000 == 0)
                Debug.WriteLine($"[WorldParser.CreateNew] 正在处理第 {y}/{height} 行...");
            for (int x = 0; x < width; x++)
            {
                var terrainData = TerrainData.CreateDefault();
                terrainData.TileType1 = 0;
                terrainData.TileType2 = 0;
                terrainData.TileType3 = 0;
                mapData.SetTerrain(x, y, terrainData);
            }
        }

        Debug.WriteLine($"[WorldParser.CreateNew] 地图创建成功: {width}x{height}");
        return mapData;
    }

    public static void SaveToFile(MapData mapData, string filePath)
    {
        if (mapData == null)
            throw new ArgumentNullException(nameof(mapData));

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            byte[] header = { 0x59, 0x53, 0x41, 0x45, 0x04, 0x00, 0x00, 0x00 };
            fs.Write(header, 0, header.Length);

            byte[] widthBytes = BitConverter.GetBytes((ushort)mapData.MapWidth);
            fs.Write(widthBytes, 0, widthBytes.Length);

            fs.WriteByte(0x0);
            fs.WriteByte(0x0);

            byte[] heightBytes = BitConverter.GetBytes((ushort)mapData.MapHeight);
            fs.Write(heightBytes, 0, heightBytes.Length);

            fs.WriteByte(0x0);
            fs.WriteByte(0x0);

            byte[] terrainBytes = mapData.TerrainsToBytes();
            fs.Write(terrainBytes, 0, terrainBytes.Length);
        }

        Debug.WriteLine($"[WorldParser] 地图已保存到: {filePath}");
    }

    public static string CreateHdBinFile(MapData mapData, string baseFilePath)
    {
        if (mapData == null)
            throw new ArgumentNullException(nameof(mapData));

        if (string.IsNullOrEmpty(baseFilePath))
            return string.Empty;

        try
        {
            string? fileDir = System.IO.Path.GetDirectoryName(baseFilePath);
            if (fileDir == null)
                return string.Empty;
            string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(baseFilePath);
            string hdFilePath = System.IO.Path.Combine(fileDir, $"{fileNameWithoutExt}_map_hd.bin");

            int mapWidth = mapData.MapWidth;
            int mapHeight = mapData.MapHeight;

            int value1 = mapWidth * 108;
            int value2 = (int)(mapHeight * 62.5 * 2.0683076);

            int count1 = (value1 / 125) + 1;
            int count2 = (value2 / 125) + 1;
            int repeatCount = count1 * count2;

            using (var fs = new FileStream(hdFilePath, FileMode.Create, FileAccess.Write))
            {
                byte[] value1Bytes = BitConverter.GetBytes(value1);
                fs.Write(value1Bytes, 0, value1Bytes.Length);

                byte[] value2Bytes = BitConverter.GetBytes(value2);
                fs.Write(value2Bytes, 0, value2Bytes.Length);

                byte[] pattern = BitConverter.GetBytes(0xFFFFFFFE);
                for (int i = 0; i < repeatCount; i++)
                    fs.Write(pattern, 0, pattern.Length);
            }

            Debug.WriteLine($"[WorldParser] HD文件创建成功：{hdFilePath}");
            return hdFilePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WorldParser] 创建HD文件失败: {ex.Message}");
            return string.Empty;
        }
    }

    private static double GetProcessMemoryMB()
    {
        return Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
    }
}
