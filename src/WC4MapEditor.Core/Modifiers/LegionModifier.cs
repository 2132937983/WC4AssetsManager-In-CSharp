using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 军团模块：读写 <see cref="MapData.Legions"/>，以及引用军团的数据（格子归属、单位 / 陷阱等的所属军团）。
/// <para>
/// 约定：归属值与各实体的"所属军团"都是军团在列表里的<b>序号</b>（0 起）；
/// <see cref="Legion.ActionId"/> 是行动顺序（官方地图从 1 开始，可以不连续），与归属无关。
/// 因此删除、移动军团时必须同步改写所有序号引用，改行动顺序则不需要动归属。
/// </para>
/// </summary>
public sealed class LegionModifier : ModifierBase
{
    public override string Name => "legion";
    public override string DisplayName => "军团修改器";

    /// <summary>按列表顺序重排行动顺序时的起始值：官方地图的行动顺序从 1 开始（与新建地图的默认军团一致）。</summary>
    public const int FirstActionId = 1;

    private int _selectedLegionId = 1;
    private readonly Random _random = new();

    private static readonly int[] DefaultLegionColors =
    [
        0xFF0000, 0x0000FF, 0xFF8800, 0x00AA00,
        0x880088, 0x00AAAA, 0xFFFF00, 0xAA00AA
    ];

    /// <summary>
    /// 当前军团：1 起的"第 N 个军团"（军团模式左键设置领域、P 键默认值使用）。
    /// 取值范围随地图军团数变化（以前固定钳在 1-8，第 9 个以后的军团选不到）。
    /// </summary>
    public int SelectedLegionId
    {
        get => Math.Clamp(_selectedLegionId, 1, MaxLegionNumber);
        set => _selectedLegionId = Math.Clamp(value, 1, MaxLegionNumber);
    }

    private int MaxLegionNumber => Math.Max(1, _mapData?.Legions.Count ?? 1);

    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        _selectedLegionId = 1;
    }

    /// <summary>
    /// 把军团领域设到指定格子（军团编辑模式左键单击）：归属值 = 军团序号。
    /// 参数：int = 军团序号；<see cref="Legion"/> = 按国家 ID 定位；省略 = 当前军团。
    /// 成功时 AffectedCount = 1 表示归属确实变了，0 表示原本就属于该军团。
    /// </summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail(Loc.T("Module.Legion.NoLegionsOnMap", "当前地图没有军团"));
        if (!_mapData.IsInBelongRange(col, row)) return ModifierResult.Fail(Loc.F("Module.Belong.OutsideRange", col, row));

        int legionIndex = parameter switch
        {
            int id => id,
            Legion l => _mapData.FindLegionIndex(l.CountryId),
            _ => SelectedLegionId - 1
        };

        if (legionIndex < 0 || legionIndex >= _mapData.Legions.Count)
            return ModifierResult.Fail(Loc.F("Module.Legion.NumberOutOfRange", legionIndex + 1));

        int countryId = _mapData.Legions[legionIndex].CountryId;
        string message = Loc.F("Module.Legion.Applied", col, row, legionIndex + 1, countryId);
        if (_mapData.GetBelongValue(col, row) == legionIndex) return ModifierResult.Ok(message, 0);

        _mapData.SetBelongValue(col, row, legionIndex);
        MarkModified();
        return ModifierResult.Ok(message, 1);
    }

    /// <summary>清除指定格子的军团领域（归属设为 0xFF）。AffectedCount 含义同 <see cref="Apply"/>。</summary>
    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return NotInitialized();
        if (!IsValidCoord(col, row)) return OutOfMap();
        if (!_mapData.IsInBelongRange(col, row)) return ModifierResult.Fail(Loc.F("Module.Belong.OutsideRange", col, row));

        string message = Loc.F("Module.Legion.Cleared", col, row);
        if (_mapData.GetBelongValue(col, row) == BelongModifier.NoOwner) return ModifierResult.Ok(message, 0);

        _mapData.SetBelongValue(col, row, BelongModifier.NoOwner);
        MarkModified();
        return ModifierResult.Ok(message, 1);
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        // 归属值解析成军团：它约定是 Legions 的序号，不能拿去找 CountryId
        int belong = _mapData!.GetBelongValue(col, row);
        if (belong == BelongModifier.NoOwner) return null;

        return _mapData.ResolveLegionByBelong(belong);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (data is Legion legion)
        {
            return UpdateLegion(legion).Success;
        }
        return false;
    }

    #region 基础CRUD

    public ModifierResult UpdateLegion(Legion legion)
    {
        if (_mapData == null) return NotInitialized();

        int idx = _mapData.FindLegionIndex(legion.CountryId);
        if (idx >= 0)
            return UpdateLegionAt(idx, legion);

        _mapData.Legions.Add(legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.Updated", legion.CountryId));
    }

    /// <summary>
    /// 按列表索引更新军团（编辑窗口、脚本按字段写军团都走这里）。
    /// <para>
    /// 不能用 <see cref="UpdateLegion"/>：它按 CountryId 查表定位，
    /// 一旦用户把 CountryId 改成了地图里不存在的值，就会被当成新军团插入。
    /// </para>
    /// <para>
    /// 国家 ID 与行动顺序被改动时校验唯一性（与 <see cref="SetLegionCountryIdAt"/> 同一规则）：
    /// 国家 ID 是按国家查找军团的键，重复会让查找产生歧义；行动顺序重复则轮到谁行动说不清。
    /// 没改动的字段不校验，已有重复的旧地图仍能编辑其它字段。
    /// </para>
    /// </summary>
    public ModifierResult UpdateLegionAt(int index, Legion legion)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);

        var current = _mapData.Legions[index];
        if (legion.CountryId != current.CountryId)
        {
            var taken = CheckCountryIdFree(index, legion.CountryId);
            if (!taken.Success) return taken;
        }
        if (legion.ActionId != current.ActionId)
        {
            var taken = CheckActionIdFree(index, legion.ActionId);
            if (!taken.Success) return taken;
        }

        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.Updated", index + 1));
    }

    public Legion? GetLegion(int legionId)
    {
        int idx = _mapData?.FindLegionIndex(legionId) ?? -1;
        return idx >= 0 ? _mapData!.Legions[idx] : null;
    }

    /// <summary>
    /// 新增一个军团（对应 VB 版 LegionSetting 的"+"按钮）。
    /// <para>
    /// 行动顺序、国家 ID 各自取现有最大值 +1（列表为空时从 1 / 1 开始），
    /// 其余字段取 <see cref="Legion.CreateDefault"/> 的默认值；新军团一律由 AI 控制。
    /// VB 原版的"+"按钮是逐字段置 0 的模板（行动顺序与第 1 个军团撞号、国家 ID 0 无效），这里不照搬。
    /// </para>
    /// </summary>
    public ModifierResult AddLegion(Legion? legion = null)
    {
        if (_mapData == null) return NotInitialized();

        var added = legion ?? CreateAppendedLegion(_mapData);
        _mapData.Legions.Add(added);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.Added", added.CountryId, _mapData.Legions.Count));
    }

    /// <summary>
    /// 构造"追加到列表末尾"的新军团：行动顺序、国家 ID 各自取现有最大值 +1，避免与已有军团冲突。
    /// </summary>
    private static Legion CreateAppendedLegion(MapData mapData)
    {
        int actionId = FirstActionId;
        int countryId = 1;

        for (int i = 0; i < mapData.Legions.Count; i++)
        {
            var existing = mapData.Legions[i];
            if (existing.ActionId >= actionId) actionId = existing.ActionId + 1;
            if (existing.CountryId >= countryId) countryId = existing.CountryId + 1;
        }

        var legion = Legion.CreateDefault(countryId);
        legion.ActionId = actionId;
        // CreateDefault 把阵营设成国家 ID，新增军团统一从 0（无阵营）开始
        legion.Camp = 0;
        // CreateDefault 把国家 ID 为 1 的军团设成玩家；追加的军团一律交给 AI（控制值 1）
        legion.IsPlayerControlled = 1;
        // VB 的 CreateDefault 不含初始科技等级（保持 0），新增军团给 1 才有可玩的起点
        legion.InitialTechLevel = 1;
        return legion;
    }

    /// <summary>引用某个军团序号的数据量（删除确认用）。</summary>
    public readonly record struct LegionReferences(int Cells, int Units, int Others)
    {
        public int Total => Cells + Units + Others;
    }

    /// <summary>统计引用军团序号 <paramref name="index"/> 的格子归属、单位 / 陷阱与其它实体（援军、空袭、空中支援、战略建设、事件）。</summary>
    public LegionReferences CountReferences(int index)
    {
        if (_mapData == null) return default;

        int cells = 0;
        int belongCount = BelongModifier.ReachableBelongCount(_mapData);
        for (int i = 0; i < belongCount; i++)
            if (_mapData.GetBelongValueByIndex(i) == index) cells++;

        // 单位的归属取所在格的归属值（Army.LegionId 是编辑器内部字段，读档后为 -1）
        int w = _mapData.MapWidth;
        int units = w <= 0 ? 0
                  : _mapData.Armies.Count(a => _mapData.GetBelongValue(a.CoordinateIndex % w, a.CoordinateIndex / w) == index)
                  + _mapData.ArmiesV3.Count(a => _mapData.GetBelongValue(a.CoordinateIndex % w, a.CoordinateIndex / w) == index)
                  + _mapData.Traps.Count(t => t.LegionId == index);

        int others = _mapData.Reinforcements.Count(r => r.OwnerCountry == index)
                   + _mapData.ReinforcementsV3.Count(r => r.OwnerLegion == index)
                   + _mapData.AirForces.Count(a => a.OwnerLegion == index)
                   + _mapData.AirSupports.Count(a => a.OwnerLegion == index)
                   + _mapData.StrategyConstructions.Count(s => s.LegionId == index)
                   + _mapData.Events.Count(e => e.TriggerLegion == index || e.AffectedLegion == index);

        return new LegionReferences(cells, units, others);
    }

    /// <summary>
    /// 删除指定索引的军团（对齐 VB 版 LegionSetting 的"-"按钮），并改写所有序号引用：
    /// 被删军团的格子归属、单位 / 陷阱等的所属军团清为 0xFF，排在它后面的军团序号整体减 1。
    /// VB 原版只删列表项不改引用，后面每个军团的领地都会挪到前一个军团名下。
    /// </summary>
    public ModifierResult RemoveLegionAt(int index)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);

        int count = _mapData.Legions.Count;
        var references = CountReferences(index);
        var map = new int[count];
        for (int i = 0; i < count; i++)
            map[i] = i < index ? i : i == index ? BelongModifier.NoOwner : i - 1;

        // 当前军团跟着走：排在被删军团后面的序号减 1；删的正是当前军团时落到顶替它位置的军团上
        int selected = SelectedLegionId - 1;
        int newSelected = selected > index ? selected - 1 : selected;
        _mapData.Legions.RemoveAt(index);
        RemapLegionReferences(map);
        SelectedLegionId = newSelected + 1;
        MarkModified();

        return ModifierResult.Ok(Loc.F("Module.Legion.Removed", _mapData.Legions.Count, references.Cells,
            references.Units + references.Others));
    }

    /// <summary>
    /// 把第 <paramref name="from"/> 个军团移到第 <paramref name="to"/> 位（序号均 0 起），
    /// 同步改写格子归属与所有实体的所属军团序号，领地与单位跟着军团走。
    /// <paramref name="renumberActionIds"/> 为 true 时再按新顺序重排行动顺序（对齐 VB 军团列表拖动排序的本意）。
    /// </summary>
    public ModifierResult MoveLegion(int from, int to, bool renumberActionIds)
    {
        if (_mapData == null) return NotInitialized();
        int count = _mapData.Legions.Count;
        if (from < 0 || from >= count) return IndexOutOfRange(from);
        if (to < 0 || to >= count) return IndexOutOfRange(to);
        if (from == to) return ModifierResult.Ok(Loc.F("Module.Legion.Moved", from + 1, to + 1), 0);

        // order[新位置] = 旧序号 → 反过来得到 旧序号 → 新位置
        var order = Enumerable.Range(0, count).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);
        var map = new int[count];
        for (int newIndex = 0; newIndex < count; newIndex++)
            map[order[newIndex]] = newIndex;

        int selected = SelectedLegionId - 1;
        _mapData.Legions.Move(from, to);
        RemapLegionReferences(map);
        if (renumberActionIds) RenumberActionIds();
        SelectedLegionId = map[selected] + 1;
        MarkModified();

        return ModifierResult.Ok(Loc.F("Module.Legion.Moved", from + 1, to + 1));
    }

    /// <summary>
    /// 把"旧序号 → 新序号"映射套到所有引用军团序号的数据上：格子归属、单位 v1/v3、陷阱、援军 v1/v3、
    /// 空袭、空中支援、战略建设、事件的触发 / 受影响军团。映射范围外的值（0xFF、本来就越界的值）保持不变。
    /// </summary>
    private void RemapLegionReferences(int[] map)
    {
        var data = _mapData!;
        int Remap(int value) => value >= 0 && value < map.Length ? map[value] : value;

        int belongCount = BelongModifier.ReachableBelongCount(data);
        for (int i = 0; i < belongCount; i++)
        {
            int belong = data.GetBelongValueByIndex(i);
            int next = Remap(belong);
            if (next != belong) data.SetBelongValueByIndex(i, next);
        }

        RemapList(data.Armies, a => a.LegionId, (a, v) => { a.LegionId = v; return a; }, Remap);
        RemapList(data.ArmiesV3, a => a.LegionId, (a, v) => { a.LegionId = v; return a; }, Remap);
        RemapList(data.Traps, t => t.LegionId, (t, v) => { t.LegionId = (short)v; return t; }, Remap);
        RemapList(data.Reinforcements, r => r.OwnerCountry, (r, v) => { r.OwnerCountry = v; return r; }, Remap);
        RemapList(data.ReinforcementsV3, r => r.OwnerLegion, (r, v) => { r.OwnerLegion = v; return r; }, Remap);
        RemapList(data.AirForces, a => a.OwnerLegion, (a, v) => { a.OwnerLegion = v; return a; }, Remap);
        RemapList(data.AirSupports, a => a.OwnerLegion, (a, v) => { a.OwnerLegion = v; return a; }, Remap);
        RemapList(data.StrategyConstructions, s => s.LegionId, (s, v) => { s.LegionId = v; return s; }, Remap);
        RemapList(data.Events, e => e.TriggerLegion, (e, v) => { e.TriggerLegion = v; return e; }, Remap);
        RemapList(data.Events, e => e.AffectedLegion, (e, v) => { e.AffectedLegion = v; return e; }, Remap);
    }

    /// <summary>逐个元素改写某个军团序号字段，只替换确实变化的元素。</summary>
    private static void RemapList<T>(IList<T> list, Func<T, int> get, Func<T, int, T> set, Func<int, int> remap)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int value = get(list[i]);
            int next = remap(value);
            if (next != value) list[i] = set(list[i], next);
        }
    }

    public Legion? GetLegionByActionId(int actionId)
    {
        if (_mapData == null) return null;

        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            if (_mapData.Legions[i].ActionId == actionId)
                return _mapData.Legions[i];
        }

        return null;
    }

    public ModifierResult SetLegionColor(int legionId, byte r, byte g, byte b)
    {
        if (_mapData == null) return NotInitialized();
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail(Loc.F("Module.Legion.NotFound", legionId));

        var legion = _mapData.Legions[idx];
        legion.ColorR = r;
        legion.ColorG = g;
        legion.ColorB = b;
        _mapData.ReplaceLegion(idx, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.ColorSet", legionId));
    }

    /// <summary>按国家 ID 定位军团并设置行动顺序（重复校验同 <see cref="SetLegionActionIdAt"/>）。</summary>
    public ModifierResult SetLegionActionId(int legionId, int actionId)
    {
        if (_mapData == null) return NotInitialized();
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail(Loc.F("Module.Legion.NotFound", legionId));

        var result = SetLegionActionIdAt(idx, actionId);
        return result.Success ? ModifierResult.Ok(Loc.F("Module.Legion.ActionIdSet", legionId)) : result;
    }

    /// <summary>
    /// 按<b>列表索引</b>设置军团阵营值（Camp）。
    /// </summary>
    public ModifierResult SetLegionCampAt(int index, int camp)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);

        var legion = _mapData.Legions[index];
        legion.Camp = camp;
        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.CampSet", index, camp));
    }

    /// <summary>
    /// 按<b>列表索引</b>设置军团战败条件（DefeatCondition）。
    /// </summary>
    public ModifierResult SetLegionDefeatConditionAt(int index, int value)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);

        var legion = _mapData.Legions[index];
        legion.DefeatCondition = value;
        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.DefeatConditionSet", index, value));
    }

    /// <summary>
    /// 按<b>列表索引</b>设置国家 ID（CountryId）。
    /// <para>
    /// 不能用按国家 ID 定位的方法：要改的正是国家 ID 本身，只能用列表索引定位，否则改完就找不到自己了。
    /// 同时校验唯一性：国家 ID 是 <c>FindLegionIndex</c> 的键，重复会让按国家查找产生歧义。
    /// </para>
    /// </summary>
    public ModifierResult SetLegionCountryIdAt(int index, int countryId)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);
        if (countryId < 0) return ModifierResult.Fail(Loc.T("Module.Legion.CountryIdNegative", "国家ID 不能为负数"));

        var free = CheckCountryIdFree(index, countryId);
        if (!free.Success) return free;

        var legion = _mapData.Legions[index];
        int old = legion.CountryId;
        legion.CountryId = countryId;
        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.CountryIdChanged", index, old, countryId));
    }

    /// <summary>按<b>列表索引</b>设置 ActionId（行动顺序），不能与其它军团重复。</summary>
    public ModifierResult SetLegionActionIdAt(int index, int actionId)
    {
        if (_mapData == null) return NotInitialized();
        if (index < 0 || index >= _mapData.Legions.Count) return IndexOutOfRange(index);

        var free = CheckActionIdFree(index, actionId);
        if (!free.Success) return free;

        var legion = _mapData.Legions[index];
        int old = legion.ActionId;
        legion.ActionId = actionId;
        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.ActionIdChanged", index, old, actionId));
    }

    private ModifierResult CheckCountryIdFree(int index, int countryId)
    {
        for (int i = 0; i < _mapData!.Legions.Count; i++)
        {
            if (i != index && _mapData.Legions[i].CountryId == countryId)
                return ModifierResult.Fail(Loc.F("Module.Legion.CountryIdTaken", countryId, i));
        }
        return ModifierResult.Ok();
    }

    private ModifierResult CheckActionIdFree(int index, int actionId)
    {
        for (int i = 0; i < _mapData!.Legions.Count; i++)
        {
            if (i != index && _mapData.Legions[i].ActionId == actionId)
                return ModifierResult.Fail(Loc.F("Module.Legion.ActionIdTaken", actionId, i));
        }
        return ModifierResult.Ok();
    }

    private ModifierResult IndexOutOfRange(int index)
        => ModifierResult.Fail(Loc.F("Module.Legion.IndexOutOfRange", index, _mapData?.Legions.Count ?? 0));

    private static ModifierResult NotInitialized()
        => ModifierResult.Fail(Loc.T("Module.Legion.NotInitialized", "地图数据未初始化"));

    private static ModifierResult OutOfMap()
        => ModifierResult.Fail(Loc.T("Module.Legion.OutOfMap", "坐标超出范围"));

    private ModifierResult? RequireLegions()
    {
        if (_mapData == null) return NotInitialized();
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail(Loc.T("Module.Legion.NoLegionData", "没有军团数据"));
        return null;
    }

    #endregion

    #region 随机化军团等级与经济 (R键)

    public ModifierResult RandomizeAllLegionLevels()
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;

        int updatedCount = 0;
        for (int i = 0; i < data.Legions.Count; i++)
        {
            var legion = data.Legions[i];

            legion.MobilityLevel = _random.Next(0, 6);
            legion.RifleLevel = _random.Next(0, 6);
            legion.CamouflageLevel = _random.Next(0, 6);
            legion.EngineerLevel = _random.Next(0, 6);
            legion.GrenadeLevel = _random.Next(0, 6);
            legion.MortarLevel = _random.Next(0, 6);
            legion.MarchLevel = _random.Next(0, 6);
            legion.BulletproofVestLevel = _random.Next(0, 6);
            legion.ArmorLevel = _random.Next(0, 6);
            legion.MainGunLevel = _random.Next(0, 6);
            legion.VehicleBodyLevel = _random.Next(0, 6);
            legion.EngineLevel = _random.Next(0, 6);
            legion.MachineGunLevel = _random.Next(0, 6);
            legion.RaidLevel = _random.Next(0, 6);
            legion.VehicleAirDefenseLevel = _random.Next(0, 6);
            legion.ReinforcedBodyLevel = _random.Next(0, 6);
            legion.ArtilleryLevel = _random.Next(0, 6);
            legion.RocketLevel = _random.Next(0, 6);
            legion.TowingLevel = _random.Next(0, 6);
            legion.ArtilleryArmorLevel = _random.Next(0, 6);
            legion.FirepowerLevel = _random.Next(0, 6);

            legion.NavyRocketLevel = _random.Next(0, 6);
            legion.DisguiseLevel = _random.Next(0, 6);
            legion.HullLevel = _random.Next(0, 6);
            legion.PropulsionLevel = _random.Next(0, 6);
            legion.NavyArmorLevel = _random.Next(0, 6);
            legion.WeaponLevel = _random.Next(0, 6);
            legion.NavalGunLevel = _random.Next(0, 6);
            legion.TorpedoLevel = _random.Next(0, 6);
            legion.MinesweepingLevel = _random.Next(0, 6);
            legion.ShipAirDefenseLevel = _random.Next(0, 6);
            legion.ModernHullLevel = _random.Next(0, 6);

            legion.AviationFuelLevel = _random.Next(0, 6);
            legion.AviationEngineLevel = _random.Next(0, 6);
            legion.AviationBombLevel = _random.Next(0, 6);
            legion.AirRaidLevel = _random.Next(0, 6);
            legion.BombingLevel = _random.Next(0, 6);
            legion.StrategicBombingLevel = _random.Next(0, 6);
            legion.AirdropLevel = _random.Next(0, 6);
            legion.JetEngineLevel = _random.Next(0, 6);

            legion.BunkerLevel = _random.Next(0, 6);
            legion.FortressGunLevel = _random.Next(0, 6);
            legion.CoastalGunLevel = _random.Next(0, 6);
            legion.RocketLauncherLevel = _random.Next(0, 6);
            legion.FortificationLevel = _random.Next(0, 6);
            legion.AntiAircraftGunLevel = _random.Next(0, 6);
            legion.AntiAircraftCannonLevel = _random.Next(0, 6);
            legion.AntiAircraftMissileLevel = _random.Next(0, 6);
            legion.RadarLevel = _random.Next(0, 6);
            legion.MissileWarheadLevel = _random.Next(0, 6);
            legion.SolidRocketEngineLevel = _random.Next(0, 6);
            legion.NuclearBombBreakthroughLevel = _random.Next(0, 6);
            legion.NuclearFusionLevel = _random.Next(0, 6);

            legion.InitialEconomy = _random.Next(100, 5001);
            legion.InitialIndustry = _random.Next(10, 501);
            legion.InitialTech = _random.Next(0, 201);

            data.ReplaceLegion(i, legion);
            updatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.Randomized", updatedCount), updatedCount);
    }

    #endregion

    #region 应用默认颜色 (C键)

    public ModifierResult ApplyDefaultColorsToAllLegions()
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;

        int updatedCount = 0;
        for (int i = 0; i < data.Legions.Count; i++)
        {
            var legion = data.Legions[i];
            int color = DefaultLegionColors[i % DefaultLegionColors.Length];

            legion.ColorR = (byte)((color >> 16) & 0xFF);
            legion.ColorG = (byte)((color >> 8) & 0xFF);
            legion.ColorB = (byte)(color & 0xFF);

            data.ReplaceLegion(i, legion);
            updatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Legion.DefaultColorsApplied", updatedCount), updatedCount);
    }

    #endregion

    #region 从配置更新军团颜色 (U键)

    public ModifierResult ApplyAllLegionsColorFromSettings()
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;

        var cfg = ConfigManager.Instance;
        int updatedCount = 0;
        var notFoundIds = new List<int>();

        for (int i = 0; i < data.Legions.Count; i++)
        {
            var legion = data.Legions[i];
            int colorValue = cfg.GetCountryColor(legion.CountryId, -1);

            if (colorValue >= 0)
            {
                legion.ColorR = (byte)((colorValue >> 16) & 0xFF);
                legion.ColorG = (byte)((colorValue >> 8) & 0xFF);
                legion.ColorB = (byte)(colorValue & 0xFF);
                data.ReplaceLegion(i, legion);
                updatedCount++;
            }
            else
            {
                notFoundIds.Add(legion.CountryId);
            }
        }

        if (updatedCount > 0) MarkModified();
        string msg = Loc.F("Module.Legion.SettingsColorsApplied", updatedCount);
        if (notFoundIds.Count > 0)
            msg += Loc.F("Module.Legion.SettingsColorsMissing", string.Join(", ", notFoundIds.Distinct()));

        return ModifierResult.Ok(msg, updatedCount);
    }

    #endregion

    #region 重排行动顺序 (I键)

    /// <summary>
    /// I 键：按列表顺序把所有军团的行动顺序改为 1、2、3……（<see cref="FirstActionId"/> 起）。
    /// <para>
    /// VB 版这里把行动顺序改成国家 ID、并把归属值从"旧行动顺序"改写成国家 ID —— 那是按"归属 = 行动顺序"写的；
    /// 而归属实际是军团序号（BTL 规格"军团归属：军团序号"，官方地图的归属值也都是 0 ~ 军团数-1），
    /// 照搬会让小国家 ID 的格子解析到别的军团。行动顺序与归属无关，这里只重排行动顺序，归属保持不动。
    /// </para>
    /// </summary>
    public ModifierResult UpdateAllLegionsActionIdAndBelong()
    {
        if (RequireLegions() is { } failure) return failure;

        int changed = RenumberActionIds();
        return ModifierResult.Ok(Loc.F("Module.Legion.ActionIdsRenumbered", changed, _mapData!.Legions.Count), changed);
    }

    /// <summary>行动顺序 = 列表位置 + <see cref="FirstActionId"/>，返回改动的军团数。</summary>
    private int RenumberActionIds()
    {
        var data = _mapData!;
        int changed = 0;
        for (int i = 0; i < data.Legions.Count; i++)
        {
            var legion = data.Legions[i];
            int actionId = FirstActionId + i;
            if (legion.ActionId == actionId) continue;
            legion.ActionId = actionId;
            data.ReplaceLegion(i, legion);
            changed++;
        }
        if (changed > 0) MarkModified();
        return changed;
    }

    #endregion

    #region 设置玩家控制军团

    /// <summary>
    /// 按行动顺序设置玩家军团（脚本 / 命令行沿用的入口）：取第一个行动顺序匹配的军团，
    /// 具体规则见 <see cref="SetPlayerControlledLegionAt"/>。找不到时不做任何改动。
    /// </summary>
    public ModifierResult SetPlayerControlledLegion(int actionId)
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;

        for (int i = 0; i < data.Legions.Count; i++)
        {
            if (data.Legions[i].ActionId == actionId)
                return SetPlayerControlledLegionAt(i);
        }
        return ModifierResult.Fail(Loc.F("Module.Legion.ActionIdNotFound", actionId));
    }

    /// <summary>
    /// 把第 <paramref name="index"/> 个军团（0 起）设为玩家控制（控制值 0），其余全部改为 AI（控制值 1）；
    /// <paramref name="index"/> 小于 0 表示不设玩家军团（全部 AI，征服地图由开局选择决定玩家）。
    /// 对齐 Java 版 WC4BTLDAO.updatePlayer。
    /// </summary>
    public ModifierResult SetPlayerControlledLegionAt(int index)
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;
        if (index >= data.Legions.Count) return IndexOutOfRange(index);

        int changed = 0;
        for (int i = 0; i < data.Legions.Count; i++)
        {
            var legion = data.Legions[i];
            int value = i == index ? 0 : 1;
            if (legion.IsPlayerControlled == value) continue;
            legion.IsPlayerControlled = value;
            data.ReplaceLegion(i, legion);
            changed++;
        }

        if (changed > 0) MarkModified();
        if (index < 0)
            return ModifierResult.Ok(Loc.F("Module.Legion.PlayerCleared", data.Legions.Count), changed);

        var target = data.Legions[index];
        return ModifierResult.Ok(Loc.F("Module.Legion.PlayerSet", index + 1, target.CountryId, target.ActionId,
            data.Legions.Count - 1), changed);
    }

    #endregion

    #region 批量设置军团经济/工业/科技

    public ModifierResult SetAllLegionsEconomy(int value)
        => SetAllLegions(l => { l.InitialEconomy = value; return l; }, "Module.Legion.EconomySet", value);

    public ModifierResult SetAllLegionsIndustry(int value)
        => SetAllLegions(l => { l.InitialIndustry = value; return l; }, "Module.Legion.IndustrySet", value);

    public ModifierResult SetAllLegionsTech(int value)
        => SetAllLegions(l => { l.InitialTech = value; return l; }, "Module.Legion.TechSet", value);

    private ModifierResult SetAllLegions(Func<Legion, Legion> update, string messageKey, int value)
    {
        if (RequireLegions() is { } failure) return failure;
        var data = _mapData!;

        for (int i = 0; i < data.Legions.Count; i++)
            data.ReplaceLegion(i, update(data.Legions[i]));

        MarkModified();
        return ModifierResult.Ok(Loc.F(messageKey, data.Legions.Count, value), data.Legions.Count);
    }

    #endregion

    #region 更新征服国家配置 (F6键)

    /// <summary>
    /// 按当前军团列表更新 ConquerCountrySettings.json 里某个征服参数的条目并写回文件（对齐 VB 版 F6）。
    /// 文件为空或不存在时从空列表开始。只写 json，不改地图数据。
    /// </summary>
    public ModifierResult UpdateConquerCountrySettings(int conquerId, List<ConquerCountryConfig> countrySettings)
    {
        if (RequireLegions() is { } failure) return failure;
        if (countrySettings == null) return ModifierResult.Fail(Loc.T("Module.Legion.ConquerListNull", "征服国家配置列表不能为null"));

        var legionConfig = ConfigManager.Instance.GetLegionEditConfig();
        if (legionConfig == null) return ModifierResult.Fail(Loc.T("Module.Legion.LegionConfigMissing", "编辑军团配置未加载"));

        var lowStrength = legionConfig.LowStrengthCountry;
        var mediumStrength = legionConfig.MediumStrengthCountry;
        var highStrength = legionConfig.HighStrengthCountry;
        var lowGift = ToGiftRanges(legionConfig.LowStrengthGift);
        var mediumGift = ToGiftRanges(legionConfig.MediumStrengthGift);
        var highGift = ToGiftRanges(legionConfig.HighStrengthGift);

        var legions = _mapData!.Legions;
        var matchingObjects = countrySettings.Where(obj => obj.ConquerId == conquerId).ToList();

        if (matchingObjects.Count < legions.Count)
        {
            // 新条目只占位：编号、阵营、强度、花费等全部在下面的更新循环里按军团统一生成
            // （VB 在这里先按低强度礼物随机一遍，随后又被更新循环整体覆盖）
            int toAdd = legions.Count - matchingObjects.Count;
            for (int i = 0; i < toAdd; i++)
            {
                var newObj = new ConquerCountryConfig { ConquerId = conquerId };
                countrySettings.Add(newObj);
                matchingObjects.Add(newObj);
            }
        }
        else if (matchingObjects.Count > legions.Count)
        {
            matchingObjects = matchingObjects.OrderBy(obj => obj.Id).ToList();
            foreach (var obj in matchingObjects.Skip(legions.Count))
                countrySettings.Remove(obj);
            matchingObjects = matchingObjects.Take(legions.Count).ToList();
        }

        for (int i = 0; i < legions.Count && i < matchingObjects.Count; i++)
        {
            var legion = legions[i];
            var obj = matchingObjects[i];
            int countryId = legion.CountryId;

            bool high = highStrength.Contains(countryId);
            bool medium = !high && mediumStrength.Contains(countryId);
            var gift = high ? highGift : medium ? mediumGift : lowGift;

            obj.Camp = legion.Camp;
            obj.CountryId = countryId;
            obj.Star = high ? 3 : medium ? 2 : 1;
            obj.Seat = 99;
            obj.WarTurn = high ? 1 : medium ? 3 : 4;
            obj.PrizeExp = 99999;
            obj.PrizeGold = 99999;
            obj.PrizeIndustry = 99999;
            obj.PrizeEnergy = 99999;
            obj.PrizeTech = 99999;
            obj.CostMoney = _random.Next(gift[0].Min, gift[0].Max + 1);
            obj.CostGear = _random.Next(gift[1].Min, gift[1].Max + 1);
            obj.CostAtomic = _random.Next(gift[2].Min, gift[2].Max + 1);
            obj.Photo = $"conquest_{conquerId}";
            obj.Surrender = true;
            obj.Id = conquerId * 1000 + (i + 1);
        }

        // 同一征服参数的条目连续排在"最后一个征服参数更小的条目"之后（对齐 VB 的 InsertRange）
        var nonMatching = countrySettings.Where(obj => obj.ConquerId != conquerId).ToList();
        int insertIndex = 0;
        for (int i = 0; i < nonMatching.Count; i++)
        {
            if (nonMatching[i].ConquerId < conquerId)
                insertIndex = i + 1;
        }

        countrySettings.Clear();
        countrySettings.AddRange(nonMatching.Take(insertIndex));
        countrySettings.AddRange(matchingObjects);
        countrySettings.AddRange(nonMatching.Skip(insertIndex));

        bool saved = TrySaveConquerCountrySettings(countrySettings, out var savePath, out var saveError);

        // 导出条数取决于内存里的军团列表；文件头的军团数与之不同时一并报出，便于定位"条数对不上"
        int headerArmyCount = _mapData.Header?.ArmyCount ?? legions.Count;
        string countNote = headerArmyCount != legions.Count
            ? Loc.F("Module.Legion.ConquerCountMismatch", headerArmyCount, legions.Count)
            : string.Empty;

        return saved
            ? ModifierResult.Ok(Loc.F("Module.Legion.ConquerUpdated", conquerId, legions.Count, countNote, savePath), legions.Count)
            : ModifierResult.Fail(Loc.F("Module.Legion.ConquerSaveFailed", saveError));
    }

    /// <summary>
    /// 写回 ConquerCountrySettings.json。
    /// 路径优先由 AssetManager 解析（AssetEntry.FullPath），并兜底用 AssetsRoot 拼接。
    /// 输出格式与 VB 一致：数组内每个对象占一行、无缩进。
    /// </summary>
    private static bool TrySaveConquerCountrySettings(
        List<ConquerCountryConfig> countrySettings, out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;
        try
        {
            const string relativePath = "json/ConquerCountrySettings.json";

            var manager = AssetManager.Default;
            path = manager.Find(relativePath)?.FullPath ?? string.Empty;

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(manager.AssetsRoot))
                path = Path.Combine(manager.AssetsRoot, "json", "ConquerCountrySettings.json");

            if (string.IsNullOrEmpty(path))
            {
                error = Loc.T("Module.Legion.ConquerPathMissing", "未找到 ConquerCountrySettings.json 路径");
                return false;
            }

            var opts = new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < countrySettings.Count; i++)
            {
                sb.Append(JsonSerializer.Serialize(countrySettings[i], opts));
                sb.AppendLine(i < countrySettings.Count - 1 ? "," : string.Empty);
            }
            sb.AppendLine("]");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.WriteLine($"[LegionModifier] 已写回 {countrySettings.Count} 条征服国家配置 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.WriteLine($"[LegionModifier] 写回 ConquerCountrySettings.json 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 把 setting.txt 的礼物配置（如 <c>[(300,600),(200,500),(40,60)]</c>）换算成三段取值范围：
    /// [0] = 金钱（CostMoney），[1] = 齿轮（CostGear），[2] = 原子（CostAtomic）。不足三段时全部取 0-99999（对齐 VB）。
    /// <para>
    /// 配置解析器把每个"(a,b)"解析成一个两元素列表 [(a,0),(b,0)]，这里按"第 1 个元素 = 下限、第 2 个元素 = 上限"取值；
    /// 若某一项本身就是 (下限, 上限) 的单元素形式也能识别。以前直接要求外层第 0 项含 3 个元素，永远不满足，
    /// 三个花费一律随机 0-99999，setting.txt 的配置从未生效。
    /// </para>
    /// </summary>
    private static (int Min, int Max)[] ToGiftRanges(List<List<(int, int)>>? gifts)
    {
        var ranges = new List<(int Min, int Max)>();
        foreach (var gift in gifts ?? [])
        {
            if (gift.Count >= 2) ranges.Add(Ordered(gift[0].Item1, gift[1].Item1));
            else if (gift.Count == 1) ranges.Add(Ordered(gift[0].Item1, gift[0].Item2));
        }

        return ranges.Count >= 3
            ? ranges.Take(3).ToArray()
            : [(0, 99999), (0, 99999), (0, 99999)];

        static (int Min, int Max) Ordered(int a, int b) => a <= b ? (a, b) : (b, a);
    }

    #endregion

    #region 查询

    public IReadOnlyList<Legion> GetAllLegions()
    {
        return _mapData?.Legions ?? [];
    }

    public int GetLegionCount()
    {
        return _mapData?.Legions.Count ?? 0;
    }

    #endregion
}
