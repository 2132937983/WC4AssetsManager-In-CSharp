using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class BelongModifier : ModifierBase, IBrushTarget
{
    public override string Name => "belong";
    public override string DisplayName => "归属修改器";

    private byte _copiedCountryId;
    private bool _hasCopiedBelong;
    private int _selectedCountryId;
    private readonly BrushEngine _brushEngine = new();

    public int SelectedCountryId
    {
        get => _selectedCountryId;
        set => _selectedCountryId = value;
    }

    /// <summary>
    /// 画笔要写入的归属值：优先使用「复制」的归属，否则回退到当前选中的国家ID
    /// （对齐 VB 版 PaintWithBrush 的 If(_copiedBelong.HasValue, _copiedBelong.Value, _selectedCountryId)）。
    /// </summary>
    public byte ResolveBrushCountryId()
        => _hasCopiedBelong ? _copiedCountryId : (byte)Math.Clamp(_selectedCountryId, 0, 255);

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte countryId = parameter switch
        {
            byte b => b,
            int id => (byte)Math.Clamp(id, 0, 255),
            _ => (byte)Math.Clamp(_selectedCountryId, 0, 255)
        };

        _mapData.SetBelongValue(col, row, countryId);
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = {countryId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.SetBelongValue(col, row, 0xFF);
        MarkModified();
        return ModifierResult.Ok($"已清除归属 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        int belongValue = data switch
        {
            byte b => b,
            int i => Math.Clamp(i, 0, 255),
            _ => -1
        };

        if (belongValue < 0) return false;
        _mapData.SetBelongValue(col, row, belongValue);
        MarkModified();
        return true;
    }

    public ModifierResult CopyBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedCountryId = (byte)_mapData!.GetBelongValue(col, row);
        _hasCopiedBelong = true;
        return ModifierResult.Ok($"已复制归属值: {_copiedCountryId}");
    }

    public ModifierResult PasteBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.SetBelongValue(col, row, _copiedCountryId);
        MarkModified();
        return ModifierResult.Ok($"已粘贴归属值: {_copiedCountryId}");
    }

    public int? GetBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    public ModifierResult SetBelongByCountryId(int col, int row, int countryId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.SetBelongValue(col, row, countryId);
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = 国家ID {countryId}");
    }

    /// <summary>
    /// 最近一次 <see cref="CleanOrphanBelongs"/> 的统计：
    /// (清理数, 无归属修复数, 省区同步数, 邻近归属填充数)
    /// </summary>
    public (int Cleaned, int BelongFixed, int ProvinceSynced, int NearestFilled) LastCleanStats { get; private set; }

    /// <summary>
    /// 检查并清理归属，对齐 VB 版 BelongModifier.CheckAndCleanBelong：
    /// <list type="number">
    /// <item>没有建筑/单位/陷阱的格子 → 归属清为 0xFF（无归属）；</item>
    /// <item>有实体的格子 → 归属与所在省区的省会格子归属保持一致；</item>
    /// <item>本格与省会都没有归属（或无省区）→ 用 BFS 取最近的已有归属填充；</item>
    /// <item>周边完全找不到任何归属 → 默认设置为 0。</item>
    /// </list>
    /// </summary>
    /// <returns>被清理（格子上没有实体）的格子数</returns>
    public int CleanOrphanBelongs()
    {
        if (_mapData == null) return 0;

        int width = _mapData.MapWidth;
        int total = width * _mapData.MapHeight;
        if (total <= 0) return 0;

        int cleaned = 0;
        int belongFixed = 0;
        int provinceSynced = 0;
        int nearestFilled = 0;

        // 本格与省会都没有归属的实体格，留到第二阶段统一用「最近归属」填充
        var pendingFill = new List<int>();

        // ===== 阶段 1：清理无实体格子的归属，并同步省会归属 =====
        for (int i = 0; i < total; i++)
        {
            int col = i % width;
            int row = i / width;
            int belong = _mapData.GetBelongValueByIndex(i);

            // 归属有效的判定：格子上有建筑、单位（v1/v3）或陷阱
            bool hasEntity = _mapData.FindBuildingIndex(col, row) >= 0
                          || _mapData.GetArmyAt(col, row) != null
                          || _mapData.GetArmyV3At(col, row) != null
                          || _mapData.FindTrapIndex(col, row) >= 0;

            if (!hasEntity)
            {
                // 没有实体的格子：清理归属
                if (belong != 0xFF)
                {
                    _mapData.SetBelongValueByIndex(i, 0xFF);
                    cleaned++;
                }
                continue;
            }

            int capitalBelong = _mapData.GetProvinceCapitalBelong(col, row);

            // 省会归属有效 → 与本格不一致时同步
            if (capitalBelong >= 0 && capitalBelong != 0xFF)
            {
                if (belong != capitalBelong)
                {
                    _mapData.SetBelongValueByIndex(i, capitalBelong);
                    provinceSynced++;
                }
                continue;
            }

            // 本格与省会都没有归属（或无省区）→ 延后处理
            if (belong == 0xFF)
                pendingFill.Add(i);
        }

        // ===== 阶段 2：对仍无归属的实体格，用 BFS 得到的最近归属填充 =====
        if (pendingFill.Count > 0)
        {
            // 多源 BFS 只需跑一次：从所有已有归属的格子同时向外扩散
            var nearestBelongMap = BuildNearestBelongMap();

            foreach (int i in pendingFill)
            {
                int nearest = nearestBelongMap != null ? nearestBelongMap[i] : -1;
                if (nearest >= 0 && nearest != 0xFF)
                {
                    _mapData.SetBelongValueByIndex(i, nearest);
                    nearestFilled++;
                }
                else
                {
                    // 实在找不到任何可用归属 → 默认设置为 0
                    _mapData.SetBelongValueByIndex(i, 0);
                    belongFixed++;
                }
            }
        }

        if (cleaned + belongFixed + provinceSynced + nearestFilled > 0) MarkModified();
        LastCleanStats = (cleaned, belongFixed, provinceSynced, nearestFilled);
        return cleaned;
    }

    /// <summary>六边形邻接偏移（偶数列）</summary>
    private static readonly int[][] EvenColNeighborOffsets =
    {
        new[] { -1, 0 }, new[] { 1, 0 },
        new[] { 0, -1 }, new[] { 0, 1 },
        new[] { -1, -1 }, new[] { 1, -1 }
    };

    /// <summary>六边形邻接偏移（奇数列）</summary>
    private static readonly int[][] OddColNeighborOffsets =
    {
        new[] { -1, 0 }, new[] { 1, 0 },
        new[] { 0, -1 }, new[] { 0, 1 },
        new[] { -1, 1 }, new[] { 1, 1 }
    };

    /// <summary>
    /// 多源 BFS：从所有「已有有效归属（!= 0xFF）」的格子同时向外扩散，
    /// 得到每个格子最近的有效归属值。<br/>
    /// 一次遍历即可服务全部待填充格子，而不是每格各做一次全图 BFS。
    /// 地图上完全没有归属时返回 null。
    /// </summary>
    private int[]? BuildNearestBelongMap()
    {
        if (_mapData == null) return null;

        int width = _mapData.MapWidth;
        int height = _mapData.MapHeight;
        int total = width * height;
        if (total <= 0 || width <= 0) return null;

        var nearest = new int[total];
        Array.Fill(nearest, -1);
        var visited = new bool[total];
        var queue = new Queue<int>();

        // 源：所有已有有效归属的格子
        for (int i = 0; i < total; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong == 0xFF) continue;

            nearest[i] = belong;
            visited[i] = true;
            queue.Enqueue(i);
        }

        if (queue.Count == 0) return null;

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int col = index % width;
            int row = index / width;
            int sourceBelong = nearest[index];
            var offsets = col % 2 == 0 ? EvenColNeighborOffsets : OddColNeighborOffsets;

            foreach (var offset in offsets)
            {
                int nc = col + offset[0];
                int nr = row + offset[1];
                if (nc < 0 || nc >= width || nr < 0 || nr >= height) continue;

                int nIndex = nr * width + nc;
                if (visited[nIndex]) continue;

                visited[nIndex] = true;
                nearest[nIndex] = sourceBelong;
                queue.Enqueue(nIndex);
            }
        }

        return nearest;
    }

    /// <summary>地图物理最外圈（厚度 1 格）判定，对齐 VB 版的 borderThickness = 1</summary>
    private bool IsOnMapBorder(int col, int row)
        => _mapData != null
        && (col < 1 || col >= _mapData.MapWidth - 1 || row < 1 || row >= _mapData.MapHeight - 1);

    /// <summary>按实体坐标（地图索引）判断其是否位于地图最外圈</summary>
    private bool IsEntityOnMapBorder(int coordinate)
    {
        if (_mapData == null || coordinate < 0) return false;
        int width = _mapData.MapWidth;
        if (width <= 0) return false;
        return IsOnMapBorder(coordinate % width, coordinate / width);
    }

    /// <summary>
    /// 删除地图最外圈（厚度 1 格）上的建筑、单位（v1/v3）与陷阱。
    /// 对齐 VB 版 BelongModifier.DeleteBorderUnitsAndBuildings：该操作针对地图物理边缘，
    /// 用于清理贴边导致显示异常的实体，与格子的归属值无关。
    /// </summary>
    public int RemoveBorderEntities()
    {
        if (_mapData == null) return 0;

        int deleted = 0;

        // 建筑：按索引倒序删除，避免删除过程中索引位移
        var buildingIndices = new List<int>();
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            if (IsEntityOnMapBorder(_mapData.Buildings[i].Coordinate))
                buildingIndices.Add(i);
        }
        for (int i = buildingIndices.Count - 1; i >= 0; i--)
        {
            _mapData.Buildings.RemoveAt(buildingIndices[i]);
            deleted++;
        }

        // 单位 v1
        var armiesV1 = new List<Army>();
        foreach (var army in _mapData.Armies)
        {
            if (IsEntityOnMapBorder(army.Coordinate))
                armiesV1.Add(army);
        }
        foreach (var army in armiesV1)
        {
            _mapData.Armies.Remove(army);
            deleted++;
        }

        // 单位 v3：按索引倒序删除
        var armyV3Indices = new List<int>();
        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            if (IsEntityOnMapBorder(_mapData.ArmiesV3[i].Coordinate))
                armyV3Indices.Add(i);
        }
        for (int i = armyV3Indices.Count - 1; i >= 0; i--)
        {
            _mapData.ArmiesV3.RemoveAt(armyV3Indices[i]);
            deleted++;
        }

        // 陷阱：按索引倒序删除
        var trapIndices = new List<int>();
        for (int i = 0; i < _mapData.Traps.Count; i++)
        {
            if (IsEntityOnMapBorder(_mapData.Traps[i].Coordinate))
                trapIndices.Add(i);
        }
        for (int i = trapIndices.Count - 1; i >= 0; i--)
        {
            _mapData.Traps.RemoveAt(trapIndices[i]);
            deleted++;
        }

        if (deleted > 0) MarkModified();
        return deleted;
    }

    /// <summary>
    /// 删除地图最外圈（厚度 1 格）上的单位（v1/v3，保留建筑与陷阱）。
    /// 对齐 VB 版 BelongModifier.DeleteBorderUnitsOnly。
    /// </summary>
    public int RemoveBorderArmies()
    {
        if (_mapData == null) return 0;

        int deleted = 0;

        var armiesV1 = new List<Army>();
        foreach (var army in _mapData.Armies)
        {
            if (IsEntityOnMapBorder(army.Coordinate))
                armiesV1.Add(army);
        }
        foreach (var army in armiesV1)
        {
            _mapData.Armies.Remove(army);
            deleted++;
        }

        var armyV3Indices = new List<int>();
        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            if (IsEntityOnMapBorder(_mapData.ArmiesV3[i].Coordinate))
                armyV3Indices.Add(i);
        }
        for (int i = armyV3Indices.Count - 1; i >= 0; i--)
        {
            _mapData.ArmiesV3.RemoveAt(armyV3Indices[i]);
            deleted++;
        }

        if (deleted > 0) MarkModified();
        return deleted;
    }

    public int BatchSetBelongByProvince(int targetCountryId)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF && belong > 0)
            {
                _mapData.SetBelongValueByIndex(i, targetCountryId);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int ClearAllBelongs()
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF)
            {
                _mapData.SetBelongValueByIndex(i, 0xFF);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RandomizeBelongs(Random random)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF && belong > 0)
            {
                _mapData.SetBelongValueByIndex(i, random.Next(1, 16));
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RandomizeBelongByTargetId(int targetBelongId, Random random)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong == targetBelongId)
            {
                _mapData.SetBelongValueByIndex(i, random.Next(0, 256));
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int ReplaceBelong(int oldBelongId, int newBelongId)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong == oldBelongId)
            {
                _mapData.SetBelongValueByIndex(i, newBelongId);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    #region IBrushTarget Implementation

    public BrushEngine Brush => _brushEngine;

    bool IBrushTarget.BrushActive
    {
        get => _brushEngine.Active;
        set => _brushEngine.Active = value;
    }

    int IBrushTarget.BrushSize
    {
        get => _brushEngine.Radius;
        set => _brushEngine.Radius = value;
    }

    string IBrushTarget.BrushShape
    {
        get => _brushEngine.Shape;
        set => _brushEngine.Shape = value;
    }

    public void PaintWithBrush(int centerCol, int centerRow)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        byte countryId = ResolveBrushCountryId();
        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
            _mapData.SetBelongValue(c, r, countryId);
        MarkModified();
    }

    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int> maskTerrainIds, bool maskIncludeMode)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        byte countryId = ResolveBrushCountryId();
        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
        {
            byte terrainType = _mapData.GetTerrainRef(c, r).TileType1;
            bool isInMask = maskTerrainIds.Contains(terrainType);

            // maskIncludeMode=true: 只在选中地形上绘制
            // maskIncludeMode=false: 跳过选中地形
            if (maskIncludeMode && !isInMask) continue;
            if (!maskIncludeMode && isInMask) continue;

            _mapData.SetBelongValue(c, r, countryId);
        }
        MarkModified();
    }

    #endregion
}