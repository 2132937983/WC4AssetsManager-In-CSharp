using System.Globalization;
using System.Runtime.CompilerServices;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Models.Modules;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 建筑模块：放置 / 删除 / 复制粘贴、关键据点与占领事件、按 setting.txt 随机化与批量生成、移动建筑等数据操作
/// （对齐 VB BuildingModifier 的各项功能）。哪个键调哪个函数由 <see cref="Mode.BuildingDeployMode"/> 决定。
/// </summary>
public sealed class BuildingModifier : ModifierBase
{
    public override string Name => "building";
    public override string DisplayName => "建筑修改器";

    /// <summary>"占地触发"在 <see cref="MapEvent.TriggerCondition"/> 里的取值。</summary>
    private const int OccupationTriggerCondition = 0;

    private static readonly int[] DefaultRandomTypes = { 11, 12, 13, 14, 15 };
    private static readonly int[] DefaultDirectionalTypes = { 31, 32, 33, 34 };
    private static readonly int[] DefaultSeaTypes = { 31, 32, 33, 34 };
    private static readonly int[] DefaultCommonTypes = { 2, 3, 21, 22, 31 };

    /// <summary>有固定名称的建筑类型（其它值显示为"未知(n)"）。</summary>
    private static readonly HashSet<int> KnownTypes = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 21, 22, 23, 31, 32, 33, 34 };

    private readonly Random _random = new();

    // 剪贴板：单个建筑（完整数据或仅等级）与多选复制的建筑组（相对左上角的偏移）
    private Building? _copiedBuilding;
    private bool _copiedLevelsOnly;
    private List<(int DCol, int DRow, Building Building)>? _copiedGroup;

    #region IModifier

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Building.OutOfRange"));

        int coordIndex = row * _mapData!.MapWidth + col;
        var building = parameter is Building b ? b : Building.CreateDefault(coordIndex);
        building.Coordinate = coordIndex;

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
            AddBuildingEntry(building);

        MarkModified();
        return coordIndex > 65535
            ? ModifierResult.Ok(Loc.F("Module.Building.PlacedTruncated", col, row, coordIndex))
            : ModifierResult.Ok(Loc.F("Module.Building.Placed", col, row));
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!TryFindBuilding(col, row, "Module.Building.NoBuildingAtPosition", out int idx, out var failure)) return failure;

        _mapData!.RemoveBuildingAt(idx);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Building.Removed", col, row));
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => IsValidCoord(col, row) && _mapData!.FindBuildingIndex(col, row) >= 0;

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindBuildingIndex(col, row);
        return idx >= 0 ? _mapData.Buildings[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
        => data is Building && Apply(col, row, data).Success;

    #endregion

    #region 复制 / 粘贴（C / Ctrl+C / V）

    /// <summary>剪贴板里有单个建筑（完整数据或仅等级）。</summary>
    public bool HasCopiedBuilding => _copiedBuilding.HasValue;

    /// <summary>剪贴板里的是"仅等级"数据（Ctrl+C）。只在复制成功时改变。</summary>
    public bool CopiedLevelsOnly => _copiedBuilding.HasValue && _copiedLevelsOnly;

    /// <summary>剪贴板里有多选复制的建筑组。</summary>
    public bool HasCopiedGroup => _copiedGroup is { Count: > 0 };

    /// <summary>复制完整建筑数据（含名称，对齐 VB CopyBuildingData）。</summary>
    public ModifierResult CopyBuilding(int col, int row)
    {
        if (!TryFindBuilding(col, row, "Module.Building.NoBuildingAtPosition", out int idx, out var failure)) return failure;

        _copiedBuilding = _mapData!.Buildings[idx];
        _copiedLevelsOnly = false;
        _copiedGroup = null;
        return ModifierResult.Ok(Loc.T("Module.Building.Copied"));
    }

    /// <summary>
    /// 复制多选格子上的建筑（按相对左上角的偏移保存）。选区里一个建筑都没有时返回 0，原剪贴板保持不变。
    /// </summary>
    public int CopyBuildingGroup(IEnumerable<HexCoord> cells)
    {
        if (_mapData == null) return 0;

        var found = new List<(HexCoord Cell, Building Building)>();
        foreach (var cell in cells)
        {
            if (!IsValidCoord(cell.Col, cell.Row)) continue;
            int idx = _mapData.FindBuildingIndex(cell.Col, cell.Row);
            if (idx >= 0) found.Add((cell, _mapData.Buildings[idx]));
        }
        if (found.Count == 0) return 0;

        int minCol = found.Min(f => f.Cell.Col);
        int minRow = found.Min(f => f.Cell.Row);
        _copiedGroup = found.Select(f => (f.Cell.Col - minCol, f.Cell.Row - minRow, f.Building)).ToList();
        _copiedBuilding = null;
        _copiedLevelsOnly = false;
        return found.Count;
    }

    /// <summary>仅复制设施等级（对齐 VB CopyBuildingLevels）。会替换掉之前复制的建筑组。</summary>
    public ModifierResult CopyBuildingLevels(int col, int row)
    {
        if (!TryFindBuilding(col, row, "Module.Building.NoBuildingAtPosition", out int idx, out var failure)) return failure;

        var src = _mapData!.Buildings[idx];
        var levels = new Building();
        BuildingModule.SetLevel(ref levels, src.FactoryLevel, src.ResearchLevel, src.MedicalLevel,
            src.AviationLevel, src.MissileLevel, src.NuclearLevel);
        _copiedBuilding = levels;
        _copiedLevelsOnly = true;
        _copiedGroup = null;
        return ModifierResult.Ok(Loc.T("Module.Building.LevelsCopied"));
    }

    /// <summary>
    /// 粘贴单个建筑：剪贴板是完整数据时放置建筑并按省会自动设置归属；是仅等级数据时只改目标建筑的等级
    /// （对齐 VB PasteBuildingData）。
    /// </summary>
    public ModifierResult PasteBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Building.OutOfRange"));
        if (_copiedBuilding is not { } copied) return ModifierResult.Fail(Loc.T("Module.Building.NothingCopied"));
        if (_copiedLevelsOnly) return PasteBuildingLevels(col, row);

        bool belongUpdated = GetPlacementBelong(col, row) >= 0;
        var result = PlaceBuilding(col, row, copied, autoBelong: true);
        if (!result.Success) return result;
        return ModifierResult.Ok(Loc.T(belongUpdated ? "Module.Building.PastedWithBelong" : "Module.Building.Pasted"));
    }

    /// <summary>把剪贴板里的设施等级写到目标建筑上（不改名称、类型与归属）。</summary>
    public ModifierResult PasteBuildingLevels(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail(Loc.T("Module.Building.OutOfRange"));
        if (_copiedBuilding is not { } copied) return ModifierResult.Fail(Loc.T("Module.Building.NothingCopied"));

        int idx = _mapData!.FindBuildingIndex(col, row);
        if (idx < 0) return ModifierResult.Fail(Loc.T("Module.Building.LevelsPasteNoBuilding"));

        var building = _mapData.Buildings[idx];
        BuildingModule.SetLevel(ref building, copied.FactoryLevel, copied.ResearchLevel, copied.MedicalLevel,
            copied.AviationLevel, copied.MissileLevel, copied.NuclearLevel);
        _mapData.ReplaceBuilding(idx, building);

        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.Building.LevelsPasted"));
    }

    /// <summary>以目标格为左上角粘贴建筑组，每个建筑都按所在省份的省会设置归属；返回实际粘贴的数量。</summary>
    public int PasteBuildingGroup(int targetCol, int targetRow)
    {
        if (_mapData == null || _copiedGroup is not { Count: > 0 } group) return 0;

        int count = 0;
        foreach (var (dCol, dRow, building) in group)
        {
            int col = targetCol + dCol;
            int row = targetRow + dRow;
            if (IsValidCoord(col, row) && PlaceBuilding(col, row, building, autoBelong: true).Success)
                count++;
        }
        return count;
    }

    /// <summary>以目标格为左上角粘贴建筑组时会写到的格子（地图外的略去），给撤销记录用。</summary>
    public IReadOnlyList<HexCoord> GetGroupPasteCells(int targetCol, int targetRow)
    {
        var cells = new List<HexCoord>();
        if (_copiedGroup == null) return cells;
        foreach (var (dCol, dRow, _) in _copiedGroup)
        {
            if (IsValidCoord(targetCol + dCol, targetRow + dRow))
                cells.Add(new HexCoord(targetCol + dCol, targetRow + dRow));
        }
        return cells;
    }

    #endregion

    #region 放置与查询

    /// <summary>
    /// 在格子上放置建筑（已有建筑则替换）。<paramref name="autoBelong"/> 为 true 时把该格归属设为所在省份省会的归属
    /// （对齐 VB SetBuilding 的 autoSetBelong）；省会无归属时保持该格原有归属。
    /// </summary>
    public ModifierResult PlaceBuilding(int col, int row, Building building, bool autoBelong)
    {
        var result = Apply(col, row, building);
        if (!result.Success || !autoBelong) return result;

        int belong = GetPlacementBelong(col, row);
        if (belong >= 0) _mapData!.SetBelongValue(col, row, belong);
        return result;
    }

    /// <summary>
    /// 放置建筑时自动采用的归属 = 所在省份省会格子的归属。无省区、省会越界、省会无归属（0xFF）
    /// 或在归属数据范围外时返回 -1，表示保持原归属（对齐 VB GetProvinceCapitalBelong：255 视为无效）。
    /// </summary>
    public int GetPlacementBelong(int col, int row)
    {
        if (_mapData == null) return -1;
        int belong = _mapData.GetProvinceCapitalBelong(col, row);
        return belong == 0xFF ? -1 : belong;
    }

    /// <summary>该格是省会格（省份值等于格子序号）时返回省份值，否则返回 -1。</summary>
    public int GetCapitalProvince(int col, int row)
    {
        if (!IsValidCoord(col, row)) return -1;
        int index = row * _mapData!.MapWidth + col;
        if (index >= 0xFFFF) return -1;
        return _mapData.GetProvinceRef(col, row).ProvinceValue == index ? index : -1;
    }

    /// <summary>省份值为 <paramref name="provinceValue"/> 的全部格子（对齐 VB GetAllHexesInProvince）。</summary>
    public List<HexCoord> GetProvinceHexes(int provinceValue)
    {
        var hexes = new List<HexCoord>();
        if (_mapData == null) return hexes;

        int width = _mapData.MapWidth;
        int total = width * _mapData.MapHeight;
        for (int i = 0; i < total; i++)
        {
            if (_mapData.GetProvinceRef(i).ProvinceValue == provinceValue)
                hexes.Add(HexCoord.FromIndex(i, width));
        }
        return hexes;
    }

    /// <summary>格子上建筑的一行说明（左键选中时显示在状态栏，对齐 VB GetBuildingInfo）。</summary>
    public string DescribeBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return Loc.T("Module.Building.OutOfRange");
        int idx = _mapData!.FindBuildingIndex(col, row);
        if (idx < 0) return Loc.T("Module.Building.NoBuildingHere");

        var b = _mapData.Buildings[idx];
        return Loc.F("Module.Building.Info", col, row, GetDisplayName(b),
            GetTypeDisplayName(b.BuildingType), b.BuildingType, b.Appearance,
            GetKeyPointDisplayName(b.KeyPoint), b.OccupationEvent,
            b.FactoryLevel, b.ResearchLevel, b.MedicalLevel, b.AviationLevel, b.MissileLevel, b.NuclearLevel);
    }

    /// <summary>建筑名称：城市名表里的名字；无名称时为"无名建筑"，名称表里查不到时为 #ID。</summary>
    public static string GetDisplayName(Building building)
    {
        if (building.Name == 0 || building.Name == BuildingNameResolver.NoName) return Loc.T("Module.Building.Unnamed");
        return BuildingNameResolver.GetCityName(building.Name) ?? "#" + building.Name.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>当前界面语言下的建筑类型名。</summary>
    public static string GetTypeDisplayName(int buildingType)
        => KnownTypes.Contains(buildingType)
            ? Loc.T($"Module.Building.Type.{buildingType}", new Building { BuildingType = (byte)buildingType }.GetBuildingTypeName())
            : Loc.F("Module.Building.Type.Unknown", buildingType);

    /// <summary>当前界面语言下的关键据点名（无 / 红圈 / 绿圈）。</summary>
    public static string GetKeyPointDisplayName(int keyPoint) => keyPoint switch
    {
        0 => Loc.T("Module.Building.KeyPoint.0"),
        1 => Loc.T("Module.Building.KeyPoint.1"),
        2 => Loc.T("Module.Building.KeyPoint.2"),
        _ => Loc.F("Module.Building.Type.Unknown", keyPoint)
    };

    #endregion

    #region 局部撤销快照

    /// <summary>撤销用的局部快照：整个建筑列表（保持原顺序）+ 指定格子的归属。</summary>
    public sealed class CellsSnapshot
    {
        internal CellsSnapshot(Building[] buildings, HexCoord[] cells, int[] belongs)
        {
            Buildings = buildings;
            Cells = cells;
            Belongs = belongs;
        }

        internal Building[] Buildings { get; }
        internal HexCoord[] Cells { get; }
        internal int[] Belongs { get; }

        public bool SameAs(CellsSnapshot other)
            => Buildings.SequenceEqual(other.Buildings) && Cells.SequenceEqual(other.Cells) && Belongs.SequenceEqual(other.Belongs);
    }

    /// <summary>
    /// 拍一份局部快照。只涉及少数格子的操作（粘贴、删除、移动、对话框编辑……）用它记录撤销，
    /// 比全图的省份 + 归属快照轻得多；撤销时建筑按原顺序恢复，被覆盖的建筑会放回原处。
    /// </summary>
    public CellsSnapshot CaptureCells(IEnumerable<HexCoord> cells)
    {
        var cellArray = cells.ToArray();
        var belongs = new int[cellArray.Length];
        if (_mapData == null) return new CellsSnapshot(Array.Empty<Building>(), cellArray, belongs);

        for (int i = 0; i < cellArray.Length; i++)
            belongs[i] = _mapData.GetBelongValue(cellArray[i].Col, cellArray[i].Row);
        return new CellsSnapshot(_mapData.Buildings.ToArray(), cellArray, belongs);
    }

    /// <summary>恢复 <see cref="CaptureCells"/> 拍下的状态。</summary>
    public void RestoreCells(CellsSnapshot snapshot)
    {
        if (_mapData == null) return;

        _mapData.Buildings.Clear();
        foreach (var building in snapshot.Buildings)
            _mapData.Buildings.Add(building);
        _mapData.RebuildBuildingCoordIndex();

        for (int i = 0; i < snapshot.Cells.Length; i++)
            _mapData.SetBelongValue(snapshot.Cells[i].Col, snapshot.Cells[i].Row, snapshot.Belongs[i]);

        MarkModified();
    }

    #endregion

    #region 关键据点与占领事件（F / Ctrl+F / Ctrl+Q / Shift+F）

    /// <summary>关键据点的循环顺序：无 → 红圈 → 绿圈 → 无（对齐 VB F 键）。</summary>
    public static byte NextKeyPoint(byte current) => current switch
    {
        0 => 1,
        1 => 2,
        _ => 0
    };

    /// <summary>设置关键据点后的提示（已设置红圈 / 绿圈标记、已取消标记）。</summary>
    public static string KeyPointMessage(byte keyPoint) => keyPoint switch
    {
        1 => Loc.T("Module.Building.KeyPointRed"),
        2 => Loc.T("Module.Building.KeyPointGreen"),
        _ => Loc.T("Module.Building.KeyPointCleared")
    };

    public ModifierResult SetKeyPoint(int col, int row, byte keyPoint)
    {
        if (!TryFindBuilding(col, row, "Module.Building.NoBuildingHere", out int idx, out var failure)) return failure;

        var building = _mapData!.Buildings[idx];
        BuildingModule.SetKeyPoint(ref building, keyPoint);
        _mapData.ReplaceBuilding(idx, building);
        MarkModified();
        return ModifierResult.Ok(KeyPointMessage(keyPoint));
    }

    public ModifierResult SetOccupationEvent(int col, int row, int eventId)
    {
        if (eventId is < 0 or > 255) return ModifierResult.Fail(Loc.T("Module.Building.EventIdOutOfRange"));
        if (!TryFindBuilding(col, row, "Module.Building.NoBuildingHere", out int idx, out var failure)) return failure;

        var building = _mapData!.Buildings[idx];
        BuildingModule.SetOccupationEvent(ref building, (byte)eventId);
        _mapData.ReplaceBuilding(idx, building);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Building.OccupationEventSet", eventId));
    }

    /// <summary>
    /// 设置建筑的占领触发事件，并把序号相同的地图事件改为占地触发（对齐 VB Ctrl+Q SetBuildingOccupationEventById）。
    /// 事件 ID 为 0 表示"没有占领事件"：只清除建筑上的值，不动事件列表。
    /// </summary>
    public ModifierResult LinkOccupationEvent(IEnumerable<HexCoord> cells, int eventId)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));
        if (eventId is < 0 or > 255) return ModifierResult.Fail(Loc.T("Module.Building.EventIdOutOfRange"));

        int count = 0;
        foreach (var cell in cells)
        {
            if (SetOccupationEvent(cell.Col, cell.Row, eventId).Success) count++;
        }
        if (count == 0) return ModifierResult.Fail(Loc.T("Module.Building.NoBuildingHere"));
        if (eventId == 0) return ModifierResult.Ok(Loc.T("Module.Building.OccupationCleared"), count);

        return SwitchEventToOccupationTrigger(eventId)
            ? ModifierResult.Ok(Loc.F("Module.Building.OccupationLinked", eventId), count)
            : ModifierResult.Ok(Loc.F("Module.Building.OccupationLinkedNoEvent", eventId), count);
    }

    /// <summary>把序号为 <paramref name="sequence"/> 的地图事件改为占地触发；找不到该事件时返回 false。</summary>
    private bool SwitchEventToOccupationTrigger(int sequence)
    {
        var events = _mapData!.Events;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Sequence != sequence) continue;
            if (events[i].TriggerCondition != OccupationTriggerCondition)
            {
                var mapEvent = events[i];
                mapEvent.TriggerCondition = OccupationTriggerCondition;
                events[i] = mapEvent;
                MarkModified();
            }
            return true;
        }
        return false;
    }

    /// <summary>把所有带占领事件的建筑标为红圈，返回带事件的建筑数（对齐 VB MarkAllEventBuildingsWithRedCircle）。</summary>
    public int MarkAllEventBuildingsWithRedCircle()
    {
        if (_mapData == null) return 0;

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.OccupationEvent == 0) continue;

            BuildingModule.SetKeyPoint(ref building, 1);
            _mapData.ReplaceBuilding(i, building);
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    #endregion

    #region 更新与删除

    public ModifierResult UpdateBuilding(Building building)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        var coord = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindBuildingIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceBuilding(idx, building);
        else
            AddBuildingEntry(building);

        MarkModified();
        return ModifierResult.Ok(Loc.T("Module.Building.Updated"));
    }

    public void RemoveAll()
    {
        if (_mapData == null) return;
        _mapData.Buildings.Clear();
        _mapData.InvalidateBuildingCoordIndex();
        MarkModified();
    }

    /// <summary>
    /// 删除不在省会格子上的建筑（建筑所在格的省份值 ≠ 格子序号）。坐标在地图范围外的建筑无法判断省份，原样保留
    /// （以前直接按序号取省份数组，征服地图与带 0xFFFF 占位建筑的关卡上越界抛异常）。
    /// </summary>
    public int RemoveNonCapitalBuildings()
    {
        if (_mapData == null) return 0;

        int removed = 0;
        for (int i = _mapData.Buildings.Count - 1; i >= 0; i--)
        {
            var building = _mapData.Buildings[i];
            if (!TryGetCell(building.Coordinate, out int col, out int row)) continue;
            if (_mapData.GetProvinceRef(col, row).ProvinceValue == building.Coordinate) continue;

            _mapData.RemoveBuildingAt(i);
            removed++;
        }

        if (removed > 0) MarkModified();
        return removed;
    }

    /// <summary>
    /// 按概率删除建筑：每个建筑独立掷骰，命中概率即删除（与「按归属概率生成建筑」「海岸线概率生成港口」互为逆操作）。
    /// <paramref name="keepNamedBuildings"/> 为 true 时保留有名称的建筑（城市、据点等）。
    /// </summary>
    public ModifierResult RemoveBuildingsByProbability(int probability, bool keepNamedBuildings = false)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        probability = Math.Clamp(probability, 0, 100);
        if (probability == 0)
            return ModifierResult.Ok(Loc.T("Module.Building.RemoveByProbabilityZero"), 0);

        int total = _mapData.Buildings.Count;
        int keptNamed = 0;
        var toRemove = new List<int>();
        for (int i = 0; i < total; i++)
        {
            var building = _mapData.Buildings[i];
            if (keepNamedBuildings && building.Name != 0 && building.Name != BuildingNameResolver.NoName)
            {
                keptNamed++;
                continue;
            }
            if (_random.Next(100) < probability) toRemove.Add(i);
        }

        for (int i = toRemove.Count - 1; i >= 0; i--)
            _mapData.RemoveBuildingAt(toRemove[i]);
        if (toRemove.Count > 0) MarkModified();

        string keptText = keptNamed > 0 ? Loc.F("Module.Building.RemoveByProbabilityKeptNamed", keptNamed) : "";
        return ModifierResult.Ok(
            Loc.F("Module.Building.RemoveByProbability", probability, toRemove.Count, total, keptText),
            toRemove.Count);
    }

    #endregion

    #region 建筑类型随机化（H / Ctrl+H，遵循 setting.txt 建筑配置）

    /// <summary>随机化「拥有名称」的建筑类型（对齐 VB RandomizeNamedBuildings）</summary>
    public ModifierResult RandomizeNamedBuildingTypes(bool useCondition)
        => RandomizeBuildingTypesByConfig(ownName: true, useCondition);

    /// <summary>随机化「没有名称」的建筑类型（对齐 VB RandomizeUnnamedBuildings）</summary>
    public ModifierResult RandomizeUnnamedBuildingTypes(bool useCondition)
        => RandomizeBuildingTypesByConfig(ownName: false, useCondition);

    private ModifierResult RandomizeBuildingTypesByConfig(bool ownName, bool useCondition)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var config = ownName
            ? buildingConfig.OwnNameBuildingRandomConfig
            : buildingConfig.NotOwnNameBuildingRandomConfig;
        var configName = ownName ? "OwnNameBuildingRandomConfig" : "NotOwnNameBuildingRandomConfig";

        if (config == null || (config.Rules.Count == 0 && config.DefaultRange == default))
            return ModifierResult.Fail(Loc.F("Module.Building.ConfigUnreadable", configName));

        // 可参与随机的建筑类型以 setting.txt 的 BuildingRandomCharacter 为准
        var randomizableTypes = ConfiguredOrDefault(buildingConfig.BuildingRandomCharacter, DefaultRandomTypes);

        // 预建坐标占用表，避免条件判断时逐格线性查找
        var occupied = OccupiedCoordinates();

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];

            bool hasName = building.Name != 0 && building.Name != BuildingNameResolver.NoName;
            if (hasName != ownName) continue;
            if (!randomizableTypes.Contains(building.BuildingType)) continue;

            if (ApplyBuildingNameRandomConfig(ref building, config, buildingConfig, occupied, useCondition))
            {
                _mapData.ReplaceBuilding(i, building);
                count++;
            }
        }

        if (count > 0) MarkModified();

        var modeText = Loc.T(useCondition ? "Module.Building.RandomMode.Condition" : "Module.Building.RandomMode.Default");
        var nameText = Loc.T(ownName ? "Module.Building.Named" : "Module.Building.NotNamed");
        return ModifierResult.Ok(Loc.F("Module.Building.TypesRandomized", modeText, count, nameText), count);
    }

    /// <summary>
    /// 应用建筑类型随机配置。
    /// DefaultRange 为默认随机范围；Rules 每项为 ((检测范围层数, 最少建筑数), 建筑类型)，
    /// 从最后一条规则往前检测，范围内建筑数达到下限时套用对应类型。
    /// </summary>
    private bool ApplyBuildingNameRandomConfig(
        ref Building building,
        BuildingNameRandomConfig config,
        BuildingConfig buildingConfig,
        HashSet<int> occupied,
        bool useCondition)
    {
        if (_mapData == null) return false;

        bool hasRules = config.Rules.Count > 0;
        // (0,0) 才表示没有配置默认范围；(13,13) 这种单值范围也是有效的默认值
        bool hasDefault = config.DefaultRange != default;
        if (!hasRules && !hasDefault) return false;

        int mapWidth = _mapData.MapWidth;
        int row = building.Coordinate / mapWidth;
        int col = building.Coordinate % mapWidth;

        int picked = 0;
        bool applied = false;

        if (useCondition && hasRules)
        {
            // 从最后一个条件开始检查（越靠后的条件要求越严格）
            for (int i = config.Rules.Count - 1; i >= 0; i--)
            {
                var (range, buildingType) = config.Rules[i];
                int rangeX = range.Min;      // 检测范围（六边形层数）
                int minCountY = range.Max;   // 范围内至少需要的建筑数

                // 六边形 BFS：统计 rangeX 层内（含自身）的建筑数，达到下限才套用该类型
                if (CountBuildingsInHexRange(col, row, rangeX, occupied) >= minCountY)
                {
                    picked = buildingType;
                    applied = true;
                    break;
                }
            }
        }

        if (!applied)
        {
            if (!hasDefault) return false;
            picked = RollRange(config.DefaultRange);
        }

        BuildingModule.SetBuildingType(ref building, (byte)picked);
        RandomizeBuildingLevelsFromConfig(ref building, buildingConfig);
        return true;
    }

    /// <summary>
    /// 按 setting.txt 的 BuildingRandomCharacter / BuildingRandomParameters 随机化设施等级
    /// （对齐 VB RandomizeBuildingLevels，每组 7 个字段：重要程度、工厂、研究所、补给、机场、导弹、核设施）。
    /// </summary>
    private void RandomizeBuildingLevelsFromConfig(ref Building building, BuildingConfig buildingConfig)
    {
        const int FieldsPerGroup = 7;

        var randomTypes = ConfiguredOrDefault(buildingConfig.BuildingRandomCharacter, DefaultRandomTypes);
        var allParams = buildingConfig.BuildingRandomParameters;
        int typeIndex = randomTypes.IndexOf(building.BuildingType);

        if (allParams == null || typeIndex < 0 || typeIndex >= allParams.Count)
        {
            RandomizeBuildingLevels(ref building);
            return;
        }

        var flat = allParams[typeIndex];
        int groupCount = flat.Count / FieldsPerGroup;
        if (groupCount == 0)
        {
            RandomizeBuildingLevels(ref building);
            return;
        }

        int start = _random.Next(groupCount) * FieldsPerGroup;
        BuildingModule.SetLevel(ref building,
            (byte)RollRange(flat[start + 1]), (byte)RollRange(flat[start + 2]), (byte)RollRange(flat[start + 3]),
            (byte)RollRange(flat[start + 4]), (byte)RollRange(flat[start + 5]), (byte)RollRange(flat[start + 6]));
        building.HatredValue = (byte)_random.Next(0, 256);
    }

    /// <summary>没有可用的随机参数时的兜底：各设施 0-4 级随机（对齐 VB 的默认等级范围）。</summary>
    private void RandomizeBuildingLevels(ref Building building)
    {
        BuildingModule.SetLevel(ref building,
            (byte)_random.Next(0, 5), (byte)_random.Next(0, 5), (byte)_random.Next(0, 5),
            (byte)_random.Next(0, 5), (byte)_random.Next(0, 5), (byte)_random.Next(0, 5));
        building.HatredValue = (byte)_random.Next(0, 256);
    }

    private int RollRange((int Min, int Max) range)
        => range.Max > range.Min ? _random.Next(range.Min, range.Max + 1) : range.Min;

    #endregion

    #region 按归属随机（R）

    /// <summary>
    /// 按归属随机建筑数据（对齐 VB ExecuteBuildingRandomization）：可随机的类型取 setting.txt 的 BuildingRandomCharacter，
    /// 新类型按各类型"重要程度"范围的宽度加权抽取，设施等级按该类型的随机参数生成。
    /// <paramref name="targetBelongId"/> 为 -1 时处理全部归属。
    /// </summary>
    public ModifierResult RandomizeBuildingsByBelong(int targetBelongId)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        var randomTypes = ConfiguredOrDefault(buildingConfig.BuildingRandomCharacter, DefaultRandomTypes);

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (!randomTypes.Contains(building.BuildingType)) continue;

            if (targetBelongId >= 0)
            {
                var cell = HexCoord.FromIndex(building.Coordinate, _mapData.MapWidth);
                if (_mapData.GetBelongValue(cell.Col, cell.Row) != targetBelongId) continue;
            }

            BuildingModule.SetBuildingType(ref building, (byte)PickTypeByWeight(randomTypes, buildingConfig.BuildingRandomParameters));
            RandomizeBuildingLevelsFromConfig(ref building, buildingConfig);
            _mapData.ReplaceBuilding(i, building);
            count++;
        }

        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Building.Randomized", count), count);
    }

    /// <summary>
    /// 按"重要程度"权重随机选建筑类型（对齐 VB GetRandomBuildingTypeByWeight）：
    /// 第 i 个类型的权重 = 第 i 组随机参数第一项（重要程度范围）的宽度 Max - Min + 1；没有参数或总权重为 0 时均匀随机。
    /// </summary>
    private int PickTypeByWeight(List<int> randomTypes, List<List<(int Min, int Max)>>? allParams)
    {
        if (allParams == null || allParams.Count == 0)
            return randomTypes[_random.Next(randomTypes.Count)];

        int n = Math.Min(randomTypes.Count, allParams.Count);
        var weights = new int[n];
        int totalWeight = 0;
        for (int i = 0; i < n; i++)
        {
            var group = allParams[i];
            weights[i] = group.Count == 0 ? 0 : Math.Max(0, group[0].Max - group[0].Min + 1);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0)
            return randomTypes[_random.Next(randomTypes.Count)];

        int roll = _random.Next(totalWeight);
        for (int i = 0; i < n; i++)
        {
            roll -= weights[i];
            if (roll < 0) return randomTypes[i];
        }
        return randomTypes[^1];
    }

    #endregion

    #region 省会与批量生成（Y / T / G / I / E）

    /// <summary>
    /// 为所有建筑所在格子生成省会（省份值 = 建筑所在格序号）。<paramref name="overwriteExisting"/> 为 false 时只处理没有省份的格子。
    /// 坐标在地图范围外的建筑跳过（以前越界抛异常）。返回实际改动的格子数。
    /// </summary>
    public int GenerateCapitalsForAllBuildings(bool overwriteExisting = false)
    {
        if (_mapData == null) return 0;

        int count = 0;
        foreach (var building in _mapData.Buildings)
        {
            if (!TryGetCell(building.Coordinate, out int col, out int row) || building.Coordinate >= 0xFFFF) continue;

            ref var province = ref _mapData.GetProvinceRef(col, row);
            bool blank = province.ProvinceValue == 0 || province.ProvinceValue == 0xFFFF;
            if (!blank && !overwriteExisting) continue;
            if (province.ProvinceValue == building.Coordinate) continue;

            province.ProvinceValue = (ushort)building.Coordinate;
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    /// <summary>所在格子已有省份数据的建筑数（Y 键先据此询问是否覆盖）。</summary>
    public int CountBuildingsWithExistingProvince()
    {
        if (_mapData == null) return 0;

        int count = 0;
        foreach (var building in _mapData.Buildings)
        {
            if (!TryGetCell(building.Coordinate, out int col, out int row)) continue;
            ushort value = _mapData.GetProvinceRef(col, row).ProvinceValue;
            if (value != 0 && value != 0xFFFF) count++;
        }
        return count;
    }

    /// <summary>在没有建筑的省会格子上随机生成建筑（对齐 VB RandomizeBuildingsOnCapitals）。</summary>
    public int RandomizeBuildingsOnCapitals()
    {
        if (_mapData == null) return 0;

        var randomTypes = ConfiguredOrDefault(ConfigManager.Instance.GetBuildingConfig().BuildingRandomCharacter, DefaultRandomTypes);
        var occupied = OccupiedCoordinates();

        int width = _mapData.MapWidth;
        int total = Math.Min(width * _mapData.MapHeight, 0xFFFF);
        int count = 0;
        for (int i = 0; i < total; i++)
        {
            // 省会：省区值等于格子索引
            if (_mapData.GetProvinceRef(i).ProvinceValue != i || occupied.Contains(i)) continue;

            // 对齐 VB：取不到归属（省会在归属数据范围外）时跳过；无归属（0xFF）的省会仍参与生成
            if (!_mapData.IsInBelongRange(i % width, i / width)) continue;

            CreateBuildingAtIndex(i, randomTypes[_random.Next(randomTypes.Count)], null, occupied);
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    /// <summary>
    /// 智能设置建筑外观值：按邻居海陆关系决定外观（对齐 VB SmartSetBuildingAppearance）。
    /// 参数保留给已有调用方，与 Initialize 时传入的是同一份地图。
    /// </summary>
    public int SmartSetBuildingAppearance(MapData mapData)
    {
        if (_mapData == null) return 0;

        var needDirection = ConfiguredOrDefault(ConfigManager.Instance.GetBuildingConfig().BuildingNeedDirection, DefaultDirectionalTypes);

        int count = 0;
        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];

            // 只处理需要方向的建筑类型
            if (!needDirection.Contains(building.BuildingType)) continue;

            int row = building.Coordinate / _mapData.MapWidth;
            int col = building.Coordinate % _mapData.MapWidth;

            BuildingModule.SetAppearance(ref building, (byte)DetermineAppearanceByNeighbors(GetNeighborTerrain(row, col)));
            _mapData.ReplaceBuilding(i, building);
            count++;
        }

        if (count > 0) MarkModified();
        return count;
    }

    /// <summary>
    /// 按归属概率在省区格子上生成建筑（对齐 VB ExecuteBuildingProbabilityGeneration）。
    /// targetBelongId = -1 表示对全部归属生效。
    /// </summary>
    public ModifierResult GenerateBuildingsByBelongProbability(int targetBelongId, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        probability = Math.Clamp(probability, 0, 100);

        var buildingConfig = ConfigManager.Instance.GetBuildingConfig();
        // 建筑类型以 setting.txt 的 CommonBuilding 为准，空则回退默认
        var commonBuildingTypes = ConfiguredOrDefault(buildingConfig.CommonBuilding, DefaultCommonTypes);
        var seaBuildingTypes = buildingConfig.BuildingOnSea ?? new List<int>();

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        // 省区值 -> 省会归属ID。归属必须按格子坐标取（裁剪地图上归属序号 ≠ 格子序号）
        var provinceBelongMap = new Dictionary<int, int>();
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                if (_mapData.GetProvinceRef(index).ProvinceValue != index) continue;

                int belongId = _mapData.GetBelongValue(col, row);
                if (belongId != 0xFF) provinceBelongMap[index] = belongId;
            }
        }

        var occupied = OccupiedCoordinates();
        int generatedCount = 0;
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                if (occupied.Contains(index)) continue;

                int provinceValue = _mapData.GetProvinceRef(index).ProvinceValue;

                // 跳过空白省区
                if (provinceValue == 0 || provinceValue == 0xFFFF) continue;

                if (!provinceBelongMap.TryGetValue(provinceValue, out int provinceBelongId)) continue;
                if (targetBelongId != -1 && provinceBelongId != targetBelongId) continue;

                if (_random.Next(1, 101) > probability) continue;

                // 海陆地形决定可用的建筑类型
                bool isOcean = GetTerrainTypeByIndex(index) == 1;
                var available = commonBuildingTypes.Where(t => isOcean == seaBuildingTypes.Contains(t)).ToList();
                if (available.Count == 0) continue;

                CreateBuildingAtIndex(index, available[_random.Next(available.Count)], provinceBelongId, occupied);
                generatedCount++;
            }
        }

        if (generatedCount > 0) MarkModified();

        return ModifierResult.Ok(targetBelongId == -1
            ? Loc.F("Module.Building.ProbabilityGeneratedAll", probability, generatedCount)
            : Loc.F("Module.Building.ProbabilityGeneratedBelong", probability, targetBelongId, generatedCount),
            generatedCount);
    }

    /// <summary>在海岸线海洋格子上按概率生成港口（对齐 VB GenerateBuildingsOnCoastalHexes）</summary>
    public ModifierResult GenerateBuildingsOnCoastalHexes(int probability)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));

        probability = Math.Clamp(probability, 0, 100);

        var buildingOnSea = ConfiguredOrDefault(ConfigManager.Instance.GetBuildingConfig().BuildingOnSea, DefaultSeaTypes);

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;
        var occupied = OccupiedCoordinates();

        int coastalHexCount = 0;
        int buildingsGenerated = 0;

        // 跳过地图边界
        for (int row = 1; row < mapHeight - 1; row++)
        {
            for (int col = 1; col < mapWidth - 1; col++)
            {
                int index = row * mapWidth + col;

                // 只处理海洋格子
                if (GetTerrainTypeByIndex(index) != 1) continue;

                // 邻居中同时存在海洋与非海洋 → 海岸线海洋格子
                var neighborTerrain = GetNeighborTerrain(row, col);
                bool hasOceanNeighbor = neighborTerrain.Values.Any(t => t == 1);
                bool hasLandNeighbor = neighborTerrain.Values.Any(t => t != 1);
                if (!hasOceanNeighbor || !hasLandNeighbor) continue;

                coastalHexCount++;

                if (occupied.Contains(index)) continue;
                if (_random.Next(100) >= probability) continue;

                CreateBuildingAtIndex(index, buildingOnSea[_random.Next(buildingOnSea.Count)], -1, occupied);
                SetBelongFromNeighborCapital(row, col, neighborTerrain);
                buildingsGenerated++;
            }
        }

        if (buildingsGenerated > 0) MarkModified();

        return ModifierResult.Ok(Loc.F("Module.Building.CoastalGenerated", coastalHexCount, buildingsGenerated), buildingsGenerated);
    }

    /// <summary>
    /// 把当前格归属设为随机一个陆地邻居所在省份的省会归属（对齐 VB SetBelongFromNeighborCapital）。
    /// 省会格就是省份值所指的格子，不必再全图扫描；归属按格子坐标取，省会无归属时保持原归属。
    /// </summary>
    private void SetBelongFromNeighborCapital(int row, int col, Dictionary<string, int> neighborTerrain)
    {
        if (_mapData == null) return;

        int mapWidth = _mapData.MapWidth;
        int total = mapWidth * _mapData.MapHeight;
        var neighborProvinces = new List<int>();

        foreach (var (name, dr, dc) in GetHexDirections(col))
        {
            // 只取非海洋邻居
            if (!neighborTerrain.TryGetValue(name, out int terrain) || terrain == 1) continue;

            int nr = row + dr;
            int nc = col + dc;
            if (nr < 0 || nr >= _mapData.MapHeight || nc < 0 || nc >= mapWidth) continue;

            int provinceValue = _mapData.GetProvinceRef(nr * mapWidth + nc).ProvinceValue;
            if (provinceValue != 0xFFFF)
                neighborProvinces.Add(provinceValue);
        }

        if (neighborProvinces.Count == 0) return;

        int capitalIndex = neighborProvinces[_random.Next(neighborProvinces.Count)];
        if (capitalIndex >= total || _mapData.GetProvinceRef(capitalIndex).ProvinceValue != capitalIndex) return;

        var capital = HexCoord.FromIndex(capitalIndex, mapWidth);
        int capitalBelong = _mapData.GetBelongValue(capital.Col, capital.Row);
        if (capitalBelong != 0xFF)
            _mapData.SetBelongValue(col, row, capitalBelong);
    }

    /// <summary>
    /// 在指定格子索引新建建筑（对齐 VB CreateBuildingAtIndex）。调用方已确认该格没有建筑。
    /// <paramref name="belongId"/> 为 null 时按"所在省份省会的归属"自动设置；为负数时不改归属。
    /// </summary>
    private void CreateBuildingAtIndex(int index, int buildingType, int? belongId, HashSet<int> occupied)
    {
        var needDirection = ConfiguredOrDefault(ConfigManager.Instance.GetBuildingConfig().BuildingNeedDirection, DefaultDirectionalTypes);

        var building = Building.CreateDefault(index);
        BuildingModule.SetBuildingType(ref building, (byte)buildingType);
        building.Name = 0;
        // 仅对需要方向的建筑类型随机外观值（1-4）
        BuildingModule.SetAppearance(ref building, needDirection.Contains(buildingType) ? (byte)_random.Next(1, 5) : (byte)0);

        AddBuildingEntry(building);
        occupied.Add(index);

        int col = index % _mapData!.MapWidth;
        int row = index / _mapData.MapWidth;
        int belong = belongId ?? GetPlacementBelong(col, row);
        if (belong >= 0) _mapData.SetBelongValue(col, row, belong);
    }

    #endregion

    #region 移动（K 移动工具 / 命令行 building move）

    /// <summary>
    /// 把建筑从源格子移动到目标格子（对齐 VB 的建筑移动工具）。
    /// 目标格已有建筑时先删除它；同时剪切归属值（源格归属移到目标格，源格置 0xFF 无归属）。
    /// 源格在归属数据范围外时不动归属（以前会把目标格归属冲成 0xFF）。
    /// </summary>
    public ModifierResult MoveBuilding(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (_mapData == null) return ModifierResult.Fail(Loc.T("Module.Building.NotInitialized"));
        if (!IsValidCoord(fromCol, fromRow)) return ModifierResult.Fail(Loc.T("Module.Building.MoveSourceOutOfRange"));
        if (!IsValidCoord(toCol, toRow)) return ModifierResult.Fail(Loc.T("Module.Building.MoveTargetOutOfRange"));
        if (fromCol == toCol && fromRow == toRow) return ModifierResult.Fail(Loc.T("Module.Building.MoveSameCell"));

        int sourceIdx = _mapData.FindBuildingIndex(fromCol, fromRow);
        if (sourceIdx < 0) return ModifierResult.Fail(Loc.T("Module.Building.MoveNoSource"));

        // Building 是 struct，先取副本；后续删除会改变集合索引
        var movingBuilding = _mapData.Buildings[sourceIdx];

        // 目标格已有建筑 → 先删除
        int existingIdx = _mapData.FindBuildingIndex(toCol, toRow);
        if (existingIdx >= 0) _mapData.RemoveBuildingAt(existingIdx);

        // 归属剪切：源格归属移到目标格，源格清空为无归属
        if (_mapData.IsInBelongRange(fromCol, fromRow))
        {
            _mapData.SetBelongValue(toCol, toRow, _mapData.GetBelongValue(fromCol, fromRow));
            _mapData.SetBelongValue(fromCol, fromRow, 0xFF);
        }

        // 移动建筑本体（删除后索引可能变化，重新查源格）
        movingBuilding.Coordinate = toRow * _mapData.MapWidth + toCol;
        int sourceIdxNow = _mapData.FindBuildingIndex(fromCol, fromRow);
        if (sourceIdxNow >= 0)
            _mapData.ReplaceBuilding(sourceIdxNow, movingBuilding);
        else
            AddBuildingEntry(movingBuilding);

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Building.Moved", toCol, toRow));
    }

    #endregion

    #region 六边形邻居辅助

    /// <summary>偶数列的六边形邻居偏移</summary>
    private static readonly (string Name, int DR, int DC)[] HexDirectionsEven =
    {
        ("up", -1, 0), ("up_right", -1, 1), ("down_right", 0, 1),
        ("down", 1, 0), ("down_left", 0, -1), ("up_left", -1, -1)
    };

    /// <summary>奇数列的六边形邻居偏移</summary>
    private static readonly (string Name, int DR, int DC)[] HexDirectionsOdd =
    {
        ("up", -1, 0), ("up_right", 0, 1), ("down_right", 1, 1),
        ("down", 1, 0), ("down_left", 1, -1), ("up_left", 0, -1)
    };

    /// <summary>六边形邻居方向偏移（奇偶列不同）</summary>
    private static (string Name, int DR, int DC)[] GetHexDirections(int col)
        => col % 2 == 0 ? HexDirectionsEven : HexDirectionsOdd;

    /// <summary>
    /// 六边形 BFS：统计以 (col,row) 为中心、range 层六边形邻域内的建筑数量（含中心格自身）。
    /// 每层的邻居偏移按该格自身所在列的奇偶性选取。
    /// </summary>
    private int CountBuildingsInHexRange(int col, int row, int range, HashSet<int> occupied)
    {
        if (_mapData == null) return 0;

        int mapWidth = _mapData.MapWidth;
        int mapHeight = _mapData.MapHeight;

        int centerIndex = row * mapWidth + col;
        int count = occupied.Contains(centerIndex) ? 1 : 0;

        if (range <= 0) return count;

        var visited = new HashSet<int> { centerIndex };
        var frontier = new List<(int Col, int Row)> { (col, row) };

        for (int step = 1; step <= range; step++)
        {
            var next = new List<(int Col, int Row)>();

            foreach (var (c, r) in frontier)
            {
                foreach (var (_, dr, dc) in GetHexDirections(c))
                {
                    int nc = c + dc;
                    int nr = r + dr;
                    if (nc < 0 || nc >= mapWidth || nr < 0 || nr >= mapHeight) continue;

                    int idx = nr * mapWidth + nc;
                    if (!visited.Add(idx)) continue;

                    if (occupied.Contains(idx)) count++;
                    next.Add((nc, nr));
                }
            }

            if (next.Count == 0) break;
            frontier = next;
        }

        return count;
    }

    /// <summary>获取邻居格子的地形类型（越界视为海洋=1）</summary>
    private Dictionary<string, int> GetNeighborTerrain(int row, int col)
    {
        var neighborTerrain = new Dictionary<string, int>();
        if (_mapData == null) return neighborTerrain;

        foreach (var (name, dr, dc) in GetHexDirections(col))
        {
            int nr = row + dr;
            int nc = col + dc;

            if (nr >= 0 && nr < _mapData.MapHeight && nc >= 0 && nc < _mapData.MapWidth)
                neighborTerrain[name] = GetTerrainTypeByIndex(nr * _mapData.MapWidth + nc);
            else
                neighborTerrain[name] = 1;
        }

        return neighborTerrain;
    }

    /// <summary>根据邻居海陆关系确定建筑外观值（对齐 VB DetermineAppearanceByNeighbors）</summary>
    private int DetermineAppearanceByNeighbors(Dictionary<string, int> neighborTerrain)
    {
        bool Land(string key) => neighborTerrain.TryGetValue(key, out int t) && t != 1;

        var validAppearances = new List<int>();

        // 第一轮：严格的 and 条件
        if (Land("up") && Land("up_right")) validAppearances.Add(1);
        if (Land("up") && Land("up_left")) validAppearances.Add(2);
        if (Land("down") && Land("down_left")) validAppearances.Add(3);
        if (Land("down") && Land("down_right")) validAppearances.Add(4);

        if (validAppearances.Count > 0)
            return validAppearances[_random.Next(validAppearances.Count)];

        // 第二轮：宽松的 or 条件
        if (Land("up") || Land("up_right")) validAppearances.Add(1);
        if (Land("up") || Land("up_left")) validAppearances.Add(2);
        if (Land("down") || Land("down_left")) validAppearances.Add(3);
        if (Land("down") || Land("down_right")) validAppearances.Add(4);

        if (validAppearances.Count > 0)
            return validAppearances[_random.Next(validAppearances.Count)];

        return _random.Next(1, 5);
    }

    #endregion

    #region 内部辅助

    /// <summary>坐标有效且该格有建筑时给出它在列表里的下标；否则给出失败结果（坐标越界 / <paramref name="missingKey"/>）。</summary>
    private bool TryFindBuilding(int col, int row, string missingKey, out int index, out ModifierResult failure)
    {
        index = -1;
        failure = default;
        if (!IsValidCoord(col, row))
        {
            failure = ModifierResult.Fail(Loc.T("Module.Building.OutOfRange"));
            return false;
        }

        index = _mapData!.FindBuildingIndex(col, row);
        if (index >= 0) return true;

        failure = ModifierResult.Fail(Loc.T(missingKey));
        return false;
    }

    /// <summary>建筑坐标（格子序号）在地图范围内时给出行列。</summary>
    private bool TryGetCell(int coordinate, out int col, out int row)
    {
        col = row = -1;
        if (_mapData == null || _mapData.MapWidth <= 0 || coordinate < 0) return false;
        col = coordinate % _mapData.MapWidth;
        row = coordinate / _mapData.MapWidth;
        return row < _mapData.MapHeight;
    }

    /// <summary>追加一个建筑并维护坐标索引（调用方保证该格原来没有建筑）。</summary>
    private void AddBuildingEntry(Building building)
    {
        _mapData!.Buildings.Add(building);
        _mapData.AddToBuildingCoordIndex(building.Coordinate, _mapData.Buildings.Count - 1);
    }

    /// <summary>已有建筑占用的格子序号（批量生成时代替逐格线性查找）。</summary>
    private HashSet<int> OccupiedCoordinates()
    {
        var occupied = new HashSet<int>();
        foreach (var building in _mapData!.Buildings) occupied.Add(building.Coordinate);
        return occupied;
    }

    private static List<int> ConfiguredOrDefault(List<int>? configured, int[] fallback)
        => configured is { Count: > 0 } ? configured : new List<int>(fallback);

    private int GetTerrainTypeByIndex(int hexIndex)
    {
        if (_mapData == null) return 1;
        if (hexIndex < 0 || hexIndex >= _mapData.TerrainCount) return 1;
        return _mapData.GetTerrainRef(hexIndex).TileType1;
    }

    #endregion
}

/// <summary>
/// 建筑编辑模式的临时叠加显示（不是地图数据，也不进撤销）：左键点中省会时同省格子的高亮
/// （对齐 VB DrawSameProvinceHexes），以及移动工具拖拽中的建筑虚影。
/// <para>
/// 模式与场景写入，BuildingRender 每帧读取；按地图实例区分（同一进程可能同时开着几张地图）。
/// 省会高亮跟着选区走：选区一旦不再是"只选中那个省会格"就自动清除，所以点别的格子、框选、
/// 切换模式（会清空选区）之后都不会残留。
/// </para>
/// </summary>
public sealed class BuildingEditOverlay
{
    private static readonly ConditionalWeakTable<MapData, BuildingEditOverlay> Overlays = new();

    private HexCoord? _capitalCell;
    private bool _trackingSelection;

    private BuildingEditOverlay()
    {
    }

    /// <summary>取（必要时创建）某张地图的叠加状态。</summary>
    public static BuildingEditOverlay For(MapData map) => Overlays.GetValue(map, _ => new BuildingEditOverlay());

    /// <summary>只读取、不创建：渲染每帧调用，没有叠加内容的地图不必建对象。</summary>
    public static BuildingEditOverlay? Find(MapData map) => Overlays.TryGetValue(map, out var overlay) ? overlay : null;

    /// <summary>要高亮的省份值（= 省会格序号）；-1 表示没有高亮。</summary>
    public int HighlightProvince { get; private set; } = -1;

    /// <summary>拖拽中的建筑；null 表示没有在拖拽。</summary>
    public Building? DragBuilding { get; private set; }

    /// <summary>拖拽虚影的中心（场景控件内的像素坐标）。</summary>
    public double DragScreenX { get; private set; }

    public double DragScreenY { get; private set; }

    /// <summary>高亮省会 <paramref name="capitalCell"/> 所在省份（省份值 <paramref name="provinceValue"/>）的全部格子。</summary>
    public void ShowProvince(HexCoord capitalCell, int provinceValue)
    {
        _capitalCell = capitalCell;
        HighlightProvince = provinceValue;
        if (_trackingSelection) return;
        HexSelector.Instance.SelectionChanged += OnSelectionChanged;
        _trackingSelection = true;
    }

    public void ClearProvince()
    {
        _capitalCell = null;
        HighlightProvince = -1;
        if (!_trackingSelection) return;
        HexSelector.Instance.SelectionChanged -= OnSelectionChanged;
        _trackingSelection = false;
    }

    public void BeginDrag(Building building, double screenX, double screenY)
    {
        DragBuilding = building;
        DragScreenX = screenX;
        DragScreenY = screenY;
    }

    public void MoveDrag(double screenX, double screenY)
    {
        DragScreenX = screenX;
        DragScreenY = screenY;
    }

    public void EndDrag() => DragBuilding = null;

    private void OnSelectionChanged(object? sender, HexSelectionChangedEventArgs e)
    {
        if (e.SelectedHexes.Count != 1 || e.CurrentPrimary != _capitalCell) ClearProvince();
    }
}
