using System.IO;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

// 区域移动（框选 + I/J/K/L + Enter）、地形识别结果写入（P 键）、HD 文件导出（F6 键）。
public sealed partial class TerrainModifier
{
    #region 区域移动

    /// <summary>
    /// 区域按 (<paramref name="offsetCol"/>, <paramref name="offsetRow"/>) 平移后落在地图内的格子（移动预览用）。
    /// 位移以第 0 列（偶数列）为基准经立方坐标换算，与 <see cref="MoveRegion"/> 完全一致，且不随区域内容变化：
    /// 偶数列位移等于直接加偏移；奇数列位移时奇数列格子多移半行，整块形状保持不变。
    /// </summary>
    public List<HexCoord> TranslateRegion(IEnumerable<(int col, int row)> cells, int offsetCol, int offsetRow)
    {
        var result = new List<HexCoord>();
        foreach (var cell in cells)
        {
            var dest = TranslateHex(cell, (0, 0), (offsetCol, offsetRow));
            if (IsValidCoord(dest.col, dest.row)) result.Add(new HexCoord(dest.col, dest.row));
        }
        return result;
    }

    /// <summary>
    /// 确认区域移动：把 <paramref name="originals"/> 的地形整体搬到平移后的位置。
    /// 腾出来的格子变成干净的海洋（三层与河流全部清掉；VB 只把第一层改成海洋，第二、三层与河流留在原地），
    /// 搬动后把河流同步到区域外的邻格。
    /// </summary>
    public ModifierResult MoveRegion(IReadOnlyCollection<(int col, int row)> originals, int offsetCol, int offsetRow)
    {
        if (_mapData == null) return NoMapResult();
        if (originals.Count == 0) return ModifierResult.Fail(Loc.T("Module.Terrain.MoveEmpty"));
        if (offsetCol == 0 && offsetRow == 0) return ModifierResult.Ok(Loc.T("Module.Terrain.MoveZero"), 0);

        var writes = new Dictionary<(int col, int row), TerrainData>();
        var moved = new List<((int col, int row) dest, TerrainData terrain)>();
        foreach (var source in originals)
        {
            var dest = TranslateHex(source, (0, 0), (offsetCol, offsetRow));
            if (!IsValidCoord(source.col, source.row) || !IsValidCoord(dest.col, dest.row) || dest == source) continue;

            moved.Add((dest, _mapData.GetTerrainRef(source.col, source.row)));
            writes[source] = TerrainData.CreateDefault();
        }
        foreach (var (dest, terrain) in moved)
            writes[dest] = terrain;

        int changed = WriteCellsWithRiverSync(writes);
        if (changed > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.Moved", writes.Count), changed);
    }

    #endregion

    #region 地形识别结果

    /// <summary>
    /// 把 <see cref="TerrainRecognizer"/> 算出的结果写进地图（第一层类型，变体归零）。
    /// 识别在后台线程只做计算；写入由 GUI 在 UI 线程、撤销记录里调用本方法，识别中途取消则地图不变。
    /// </summary>
    public ModifierResult ApplyRecognition(TerrainRecognitionResult recognition)
    {
        if (_mapData == null) return NoMapResult();

        int written = 0;
        foreach (var (index, tileType) in recognition.TileTypes)
        {
            if (index < 0 || index >= _mapData.TerrainCount) continue;
            ref var terrain = ref _mapData.GetTerrainRef(index);
            terrain.TileType1 = tileType;
            terrain.DecorationType1 = 0;
            written++;
        }

        if (written > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Terrain.Recognize.Done", written), written);
    }

    #endregion

    #region F6 导出 HD 文件

    /// <summary>
    /// 在地图旁边生成 <c>&lt;文件名&gt;_map_hd.bin</c>：两个尺寸值加上全 0xFFFFFFFE 的格子表。
    /// 尺寸按 Java 原版的单精度算法：value2 = (int)(2.0683076f × (int)(62.5f × 行数))。
    /// 原先用双精度一次乘完，行数为 52 时得 6721，而游戏自带的 map1_hd.bin 存的是 6722（与 Java 算法一致）。
    /// </summary>
    public ModifierResult ExportHdFile()
    {
        if (_mapData == null) return NoMapResult();

        string currentFilePath = _mapData.FilePath;
        if (string.IsNullOrEmpty(currentFilePath))
            return ModifierResult.Fail(Loc.T("Module.Terrain.Hd.NoPath"));

        try
        {
            int mapWidth = _mapData.MapWidth;
            int mapHeight = _mapData.MapHeight;

            string fileDir = Path.GetDirectoryName(currentFilePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(currentFilePath) + "_map_hd.bin";
            string hdFilePath = Path.Combine(fileDir, fileName);

            int value1 = mapWidth * 108;
            int defMapHeight = (int)(62.5f * mapHeight);
            float scaledHeight = 2.0683076f * defMapHeight;
            int value2 = (int)scaledHeight;

            int repeatCount = (value1 / 125 + 1) * (value2 / 125 + 1);

            using (var fs = new FileStream(hdFilePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write((uint)value1);
                writer.Write((uint)value2);
                const uint emptyCell = 0xFFFFFFFE;
                for (int i = 0; i < repeatCount; i++)
                    writer.Write(emptyCell);
            }

            long fileSize = 8 + repeatCount * 4L;
            string detail = Loc.F("Module.Terrain.Hd.Done", fileName, fileSize, mapWidth, mapHeight,
                $"{value1} (0x{value1:X8})", $"{value2} (0x{value2:X8})", repeatCount);
            return ModifierResult.Ok(detail, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModifierResult.Fail(Loc.F("Module.Terrain.Hd.Failed", ex.Message));
        }
    }

    #endregion
}
