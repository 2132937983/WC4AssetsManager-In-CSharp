using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 归属模块：读写格子归属值。
/// <para>
/// 归属值 = 军团在 <see cref="MapData.Legions"/> 里的序号（0 起），<see cref="NoOwner"/>(0xFF) 表示无归属。
/// 征服地图的归属数据只覆盖 MapClip 裁剪区：区外的格子没有归属（<see cref="MapData.IsInBelongRange"/> 为 false），
/// 写入它们一律返回失败，而不是假装成功。
/// </para>
/// </summary>
public sealed class BelongModifier : ModifierBase, IBrushTarget
{
    /// <summary>无归属</summary>
    public const int NoOwner = 0xFF;

    public override string Name => "belong";
    public override string DisplayName => "归属修改器";

    private byte _copiedCountryId;
    private bool _hasCopiedBelong;
    private int _selectedCountryId;
    private readonly BrushEngine _brushEngine = new();

    /// <summary>当前归属值（Q 键选择，右键 / 画笔在没有复制归属时写入它）。</summary>
    public int SelectedCountryId
    {
        get => _selectedCountryId;
        set => _selectedCountryId = value;
    }

    /// <summary>是否已经按 C 复制过归属。</summary>
    public bool HasCopiedBelong => _hasCopiedBelong;

    /// <summary>复制的归属值（<see cref="HasCopiedBelong"/> 为 false 时无意义）。</summary>
    public int CopiedBelongValue => _copiedCountryId;

    /// <summary>
    /// 丢弃复制的归属：之后右键与画笔改用 <see cref="SelectedCountryId"/>。
    /// Q 键选了新归属后调用 —— 否则复制过一次之后，Q 的选择永远不会生效（VB 版的老问题）。
    /// </summary>
    public void ClearCopiedBelong() => _hasCopiedBelong = false;

    /// <summary>换地图时复位：上一张图复制的归属、选中的归属值在新图上没有意义。</summary>
    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        _hasCopiedBelong = false;
        _copiedCountryId = 0;
        _selectedCountryId = 0;
    }

    /// <summary>
    /// 画笔 / 右键要写入的归属值：优先使用「复制」的归属，否则回退到当前选中的归属值
    /// （对齐 VB 版 PaintWithBrush 的 If(_copiedBelong.HasValue, _copiedBelong.Value, _selectedCountryId)）。
    /// </summary>
    public byte ResolveBrushCountryId()
        => _hasCopiedBelong ? _copiedCountryId : (byte)Math.Clamp(_selectedCountryId, 0, 255);

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        int value = parameter switch
        {
            byte b => b,
            int id => id,
            _ => _selectedCountryId
        };
        return SetBelongByCountryId(col, row, value);
    }

    public override ModifierResult Remove(int col, int row)
        => Write(col, row, NoOwner, Loc.F("Module.Belong.Cleared", col, row));

    public override bool CanApply(int col, int row) => IsWritable(col, row);
    public override bool CanRemove(int col, int row) => IsWritable(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        int belongValue = data switch
        {
            byte b => b,
            int i => i,
            _ => -1
        };
        return belongValue >= 0 && SetBelongByCountryId(col, row, belongValue).Success;
    }

    /// <summary>该格能否写归属：坐标在地图内且在归属数据范围内。</summary>
    public bool IsWritable(int col, int row)
        => IsValidCoord(col, row) && _mapData!.IsInBelongRange(col, row);

    public ModifierResult CopyBelongValue(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        if (!_mapData.IsInBelongRange(col, row)) return OutsideBelongRange(col, row);

        _copiedCountryId = (byte)_mapData.GetBelongValue(col, row);
        _hasCopiedBelong = true;
        return ModifierResult.Ok(Loc.F("Module.Belong.Copied", DescribeBelong(_copiedCountryId)));
    }

    public ModifierResult PasteBelongValue(int col, int row)
    {
        if (!_hasCopiedBelong)
            return ModifierResult.Fail(Loc.T("Module.Belong.NothingCopied", "尚未复制归属（先按 C 复制一个格子的归属）"));
        return Write(col, row, _copiedCountryId, Loc.F("Module.Belong.Pasted", col, row, DescribeBelong(_copiedCountryId)));
    }

    public int? GetBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    /// <summary>把一格归属设为指定值（0-255，255 = 无归属）。其它模式放置 / 删除实体时也用它同步归属。</summary>
    public ModifierResult SetBelongByCountryId(int col, int row, int countryId)
    {
        int value = Math.Clamp(countryId, 0, 255);
        return Write(col, row, value, Loc.F("Module.Belong.Set", col, row, value));
    }

    /// <summary>
    /// 归属值的可读描述："3（英国）"、"255（无归属）"；没有对应军团时只给数值。
    /// 状态栏与 CLI 输出共用，免得各处各拼一套。
    /// </summary>
    public string DescribeBelong(int belong)
    {
        if (belong == NoOwner) return Loc.F("Module.Belong.ValueNone", belong);
        var legion = _mapData?.ResolveLegionByBelong(belong);
        return legion == null
            ? belong.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : Loc.F("Module.Belong.ValueWithLegion", belong, CountryNameProvider.Get(legion.Value.CountryId));
    }

    /// <summary>
    /// 写一格归属的公共路径：地图未加载、坐标越界、不在归属数据范围内时返回失败。
    /// 成功时 AffectedCount = 1 表示值确实变了，0 表示原本就是这个值（调用方据此决定要不要标记修改、刷新）。
    /// </summary>
    private ModifierResult Write(int col, int row, int value, string message)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        if (!_mapData.IsInBelongRange(col, row)) return OutsideBelongRange(col, row);

        if (_mapData.GetBelongValue(col, row) == value)
            return ModifierResult.Ok(message, 0);

        _mapData.SetBelongValue(col, row, value);
        MarkModified();
        return ModifierResult.Ok(message, 1);
    }

    private static ModifierResult NotInitialized()
        => ModifierResult.Fail(Loc.T("Module.Belong.NotInitialized", "地图数据未初始化"));

    private static ModifierResult OutOfMap()
        => ModifierResult.Fail(Loc.T("Module.Belong.OutOfMap", "坐标超出范围"));

    private static ModifierResult OutsideBelongRange(int col, int row)
        => ModifierResult.Fail(Loc.F("Module.Belong.OutsideRange", col, row));

    // ------------------------------------------------------------------ 撤销快照

    /// <summary>按归属数组下标拍下全部归属值（撤销用）。</summary>
    public static byte[] CaptureBelongs(MapData map)
    {
        int count = map.Belongs?.Count ?? 0;
        var snapshot = new byte[count];
        for (int i = 0; i < count; i++) snapshot[i] = (byte)map.GetBelongValueByIndex(i);
        return snapshot;
    }

    /// <summary>按归属数组下标写回 <see cref="CaptureBelongs"/> 的快照。</summary>
    public static void RestoreBelongs(MapData map, byte[] snapshot)
    {
        int count = Math.Min(map.Belongs?.Count ?? 0, snapshot.Length);
        for (int i = 0; i < count; i++) map.SetBelongValueByIndex(i, snapshot[i]);
        map.IsModified = true;
    }

    /// <summary>
    /// 记录一次批量归属修改的撤销：执行前后按归属数组下标各拍一份快照，有变化才入栈。
    /// <para>
    /// 不走 <see cref="EditModeManager.RecordMultiCellBelongChange"/>：它按归属下标拍快照、却按地图坐标比较和还原，
    /// 在 MapClipX / MapClipY 不为 0 的征服地图上撤销会把归属整体错位。归属 / 军团模式与命令行的批量归属操作都走这里。
    /// </para>
    /// </summary>
    public static void RecordBulkChange(EditModeManager editor, MapData map, string description, Action applyChange)
    {
        var before = CaptureBelongs(map);
        applyChange();
        var after = CaptureBelongs(map);
        if (before.AsSpan().SequenceEqual(after)) return;
        editor.RecordEntityChange(description, () => RestoreBelongs(map, after), () => RestoreBelongs(map, before));
    }

    // ------------------------------------------------------------------ 检查并清理归属（G）

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
    /// <para>
    /// 省会归属一律取清理<b>之前</b>的值：省会格本身没有实体时会在第 1 步被清掉，
    /// 以前按格子下标顺序边改边读，排在省会前面的实体格同步到旧值、后面的却读到 0xFF，结果取决于遍历顺序。
    /// </para>
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

        // 地图索引 → 归属数组索引（-1 = 该格不在归属数据范围内，例如征服地图的裁剪区外）
        var belongIndexByMapIndex = new int[total];
        for (int i = 0; i < total; i++)
            belongIndexByMapIndex[i] = _mapData.GetBelongIndex(i % width, i / width);

        // 清理前的归属值（按地图索引），省会归属从这里读，结果与遍历顺序无关
        var originalBelong = new int[total];
        for (int i = 0; i < total; i++)
            originalBelong[i] = belongIndexByMapIndex[i] < 0 ? NoOwner : _mapData.GetBelongValueByIndex(belongIndexByMapIndex[i]);

        // 本格与省会都没有归属的实体格，留到第二阶段统一用「最近归属」填充
        var pendingFill = new List<int>();

        // ===== 阶段 1：清理无实体格子的归属，并同步省会归属 =====
        for (int i = 0; i < total; i++)
        {
            int belongIndex = belongIndexByMapIndex[i];
            if (belongIndex < 0) continue;

            int col = i % width;
            int row = i / width;
            int belong = originalBelong[i];

            bool hasEntity = _mapData.FindBuildingIndex(col, row) >= 0
                          || _mapData.GetArmyAt(col, row) != null
                          || _mapData.GetArmyV3At(col, row) != null
                          || _mapData.FindTrapIndex(col, row) >= 0;

            if (!hasEntity)
            {
                if (belong != NoOwner)
                {
                    _mapData.SetBelongValueByIndex(belongIndex, NoOwner);
                    cleaned++;
                }
                continue;
            }

            int capitalBelong = OriginalCapitalBelong(col, row, originalBelong);
            if (capitalBelong >= 0 && capitalBelong != NoOwner)
            {
                if (belong != capitalBelong)
                {
                    _mapData.SetBelongValueByIndex(belongIndex, capitalBelong);
                    provinceSynced++;
                }
                continue;
            }

            if (belong == NoOwner)
                pendingFill.Add(i);
        }

        // ===== 阶段 2：对仍无归属的实体格，用 BFS 得到的最近归属填充 =====
        if (pendingFill.Count > 0)
        {
            var nearestBelongMap = BuildNearestBelongMap();

            foreach (int i in pendingFill)
            {
                int nearest = nearestBelongMap != null ? nearestBelongMap[i] : -1;
                int belongIndex = belongIndexByMapIndex[i];

                if (nearest >= 0 && nearest != NoOwner)
                {
                    _mapData.SetBelongValueByIndex(belongIndex, nearest);
                    nearestFilled++;
                }
                else
                {
                    _mapData.SetBelongValueByIndex(belongIndex, 0);
                    belongFixed++;
                }
            }
        }

        if (cleaned + belongFixed + provinceSynced + nearestFilled > 0) MarkModified();
        LastCleanStats = (cleaned, belongFixed, provinceSynced, nearestFilled);
        return cleaned;
    }

    /// <summary>所在省区的省会格在清理前的归属；无省区或省会越界返回 -1。</summary>
    private int OriginalCapitalBelong(int col, int row, int[] originalBelong)
    {
        var province = _mapData!.GetProvinceRef(col, row);
        if (province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF) return -1;
        int capitalIndex = province.ProvinceValue;
        return capitalIndex < originalBelong.Length ? originalBelong[capitalIndex] : -1;
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
    /// 得到每个格子最近的有效归属值。一次遍历即可服务全部待填充格子。
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

        for (int i = 0; i < total; i++)
        {
            int belongIndex = _mapData.GetBelongIndex(i % width, i / width);
            if (belongIndex < 0) continue;

            int belong = _mapData.GetBelongValueByIndex(belongIndex);
            if (belong == NoOwner) continue;

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

    // ------------------------------------------------------------------ 删除地图边缘实体（E）

    /// <summary>删除地图边缘实体的分类统计。</summary>
    public readonly record struct BorderRemoval(int Buildings, int Armies, int ArmiesV3, int Traps)
    {
        public int Total => Buildings + Armies + ArmiesV3 + Traps;
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
    /// 删除地图最外圈（厚度 1 格）上的建筑、单位（v1/v3）与陷阱，返回分类数量。
    /// 对齐 VB 版 BelongModifier.DeleteBorderUnitsAndBuildings：该操作针对地图物理边缘，
    /// 用于清理贴边导致显示异常的实体，与格子的归属值无关。
    /// </summary>
    public BorderRemoval RemoveBorderEntities()
    {
        if (_mapData == null) return default;

        int buildings = RemoveWhere(_mapData.Buildings, b => IsEntityOnMapBorder(b.Coordinate));
        int armies = RemoveWhere(_mapData.Armies, a => IsEntityOnMapBorder(a.Coordinate));
        int armiesV3 = RemoveWhere(_mapData.ArmiesV3, a => IsEntityOnMapBorder(a.Coordinate));
        int traps = RemoveWhere(_mapData.Traps, t => IsEntityOnMapBorder(t.Coordinate));

        var removal = new BorderRemoval(buildings, armies, armiesV3, traps);
        if (removal.Total > 0) MarkModified();
        return removal;
    }

    /// <summary>
    /// 删除地图最外圈（厚度 1 格）上的单位（v1/v3，保留建筑与陷阱），返回删除数量。
    /// 对齐 VB 版 BelongModifier.DeleteBorderUnitsOnly。
    /// </summary>
    public int RemoveBorderArmies()
    {
        if (_mapData == null) return 0;

        int deleted = RemoveWhere(_mapData.Armies, a => IsEntityOnMapBorder(a.Coordinate))
                    + RemoveWhere(_mapData.ArmiesV3, a => IsEntityOnMapBorder(a.Coordinate));
        if (deleted > 0) MarkModified();
        return deleted;
    }

    /// <summary>按下标倒序删除满足条件的元素（删除过程中下标不会位移），返回删除数量。</summary>
    private static int RemoveWhere<T>(IList<T> list, Func<T, bool> predicate)
    {
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!predicate(list[i])) continue;
            list.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    // ------------------------------------------------------------------ 批量归属

    /// <summary>
    /// 把所有已有归属（≠ 0xFF）的格子统一改为目标值。
    /// 归属 0 是第 1 个军团，同样算"已有归属"（以前把 0 当成无归属跳过了）。
    /// </summary>
    public int BatchSetBelongByProvince(int targetCountryId)
    {
        if (_mapData == null) return 0;
        int target = Math.Clamp(targetCountryId, 0, 255);
        return RewriteBelongs(belong => belong != NoOwner ? target : belong);
    }

    public int ClearAllBelongs()
    {
        if (_mapData == null) return 0;
        return RewriteBelongs(_ => NoOwner);
    }

    /// <summary>
    /// 把所有已有归属（≠ 0xFF）的格子随机分给地图上的军团（归属值取 0 ~ 军团数-1）。
    /// 以前固定取 1-15：既可能分到不存在的军团，又永远分不到第 1 个军团（归属 0）和第 16 个以后的军团。
    /// </summary>
    public int RandomizeBelongs(Random random)
    {
        if (_mapData == null) return 0;
        int range = RandomBelongRange();
        return RewriteBelongs(belong => belong != NoOwner ? random.Next(0, range) : belong);
    }

    /// <summary>
    /// 把归属为指定值的格子随机分给地图上的军团（归属值取 0 ~ 军团数-1）。
    /// 以前取 0-255：会随机出 255（= 无归属）或没有对应军团的值。
    /// </summary>
    public int RandomizeBelongByTargetId(int targetBelongId, Random random)
    {
        if (_mapData == null) return 0;
        int range = RandomBelongRange();
        return RewriteBelongs(belong => belong == targetBelongId ? random.Next(0, range) : belong);
    }

    public int ReplaceBelong(int oldBelongId, int newBelongId)
    {
        if (_mapData == null) return 0;
        int target = Math.Clamp(newBelongId, 0, 255);
        return RewriteBelongs(belong => belong == oldBelongId ? target : belong);
    }

    /// <summary>随机归属的取值上限（不含）：有军团时为军团数，没有军团时退回 0-254（不会随机成"无归属"）。</summary>
    private int RandomBelongRange()
    {
        int legions = _mapData?.Legions.Count ?? 0;
        return legions > 0 ? Math.Min(legions, NoOwner) : NoOwner;
    }

    /// <summary>
    /// 地图格子能访问到的归属数组长度：不超过格子总数。个别地图的归属数组比格子数长，
    /// 多出来的尾部没有任何格子对应，批量操作不去碰它（与改写前的遍历范围一致）。
    /// </summary>
    public static int ReachableBelongCount(MapData map)
        => Math.Min(map.Belongs?.Count ?? 0, map.MapWidth * map.MapHeight);

    /// <summary>
    /// 逐个归属数组元素按规则改写（按归属数组下标遍历，征服地图的裁剪区外不会被碰到），
    /// 返回值确实改变的格子数；有改动才标记修改。
    /// </summary>
    private int RewriteBelongs(Func<int, int> rule)
    {
        var map = _mapData!;
        int count = 0;
        int total = ReachableBelongCount(map);
        for (int i = 0; i < total; i++)
        {
            int belong = map.GetBelongValueByIndex(i);
            int next = rule(belong);
            if (next == belong) continue;
            map.SetBelongValueByIndex(i, next);
            count++;
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
