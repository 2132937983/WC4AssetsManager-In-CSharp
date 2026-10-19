using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 事件模块：地图事件列表（<see cref="MapData.Events"/>）的增删改、复制粘贴、排序、选择与引用维护。
/// <para>
/// 事件不在格子上，按坐标的 IModifier 接口（Remove / GetDataAt / SetDataAt）都不适用；
/// 事件编辑模式、事件设置窗口与命令行统一调用下面的具名方法。
/// 本模块只改数据并标记已修改，撤销由调用方用 <c>RecordCollectionChange(MapData.Events)</c>
/// （改到建筑 / 单位引用时用 <c>RecordEntitiesSnapshot</c>）包住。
/// </para>
/// <para>
/// "选中事件"记的是列表下标而不是序号：序号允许重复，按序号找会选错（VB 版的老问题）。
/// 显示与循环选择按 <see cref="GetDisplayOrder"/>（序号升序，同序号按列表位置），与事件设置窗口的列表顺序一致。
/// </para>
/// </summary>
public sealed class EventModifier : ModifierBase
{
    private MapEvent? _copiedEvent;
    private int _selectedEventIndex = -1;

    public override string Name => "event";
    public override string DisplayName => Loc.T("Module.Event.DisplayName", "事件修改器");

    public override void Initialize(MapData mapData)
    {
        base.Initialize(mapData);
        // 选中的是旧地图的列表下标，换图后必须清掉；复制的事件是纯数据，保留以便粘贴到另一张地图
        _selectedEventIndex = -1;
    }

    public override void Deinitialize()
    {
        base.Deinitialize();
        _selectedEventIndex = -1;
    }

    #region IModifier（事件不在格子上）

    /// <summary>添加事件：参数是 <see cref="MapEvent"/> 时原样添加，否则新建默认事件（同 <see cref="CreateEvent"/>）。新事件成为选中事件。</summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
        => parameter is MapEvent e ? AddEvent(e) : CreateEvent();

    public override ModifierResult Remove(int col, int row)
        => ModifierResult.Fail(Loc.T("Module.Event.NotOnMap", "事件不在格子上，请按序号或列表位置删除"));

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;
    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 查询

    public IReadOnlyList<MapEvent> GetAllEvents() => _mapData?.Events ?? (IReadOnlyList<MapEvent>)Array.Empty<MapEvent>();

    public MapEvent? GetEventBySequence(int sequence)
    {
        int index = GetEventIndex(sequence);
        return index >= 0 ? _mapData!.Events[index] : null;
    }

    /// <summary>第一个序号为 <paramref name="sequence"/> 的事件的列表下标；没有则 -1。</summary>
    public int GetEventIndex(int sequence)
    {
        if (_mapData == null) return -1;
        for (int i = 0; i < _mapData.Events.Count; i++)
            if (_mapData.Events[i].Sequence == sequence) return i;
        return -1;
    }

    /// <summary>使用该序号的事件个数（大于 1 说明序号重复，按序号的引用有歧义）。</summary>
    public int CountBySequence(int sequence) => _mapData?.Events.Count(e => e.Sequence == sequence) ?? 0;

    /// <summary>新事件的序号：现有最大序号 + 1（没有事件时为 1）。</summary>
    public int GetNextSequence()
    {
        if (_mapData == null || _mapData.Events.Count == 0) return 1;
        return _mapData.Events.Max(e => e.Sequence) + 1;
    }

    /// <summary>显示顺序：列表下标按（序号, 下标）升序排列。事件设置窗口的列表与 [ / ] 循环都按这个顺序。</summary>
    public List<int> GetDisplayOrder()
    {
        if (_mapData == null) return new List<int>();
        var events = _mapData.Events;
        return Enumerable.Range(0, events.Count).OrderBy(i => events[i].Sequence).ThenBy(i => i).ToList();
    }

    /// <summary>
    /// 一行摘要，例如 "#3  回合触发 R5 → 给予经济（苏联→德国）"：序号、触发条件（回合触发带回合数）、触发事件，
    /// 有军团时附上"触发军团→受影响军团"（0 显示为 -）。
    /// </summary>
    public string Describe(in MapEvent e)
    {
        string condition = MapEventNames.Condition(e.TriggerCondition);
        if (e.TriggerCondition == MapEvent.ConditionRound)
            condition = Loc.F("Module.Event.Describe.Round", condition, e.TriggerRound);
        string text = Loc.F("Module.Event.Describe", e.Sequence, condition, MapEventNames.Effect(e.TriggerEvent));
        if (e.TriggerLegion != 0 || e.AffectedLegion != 0)
            text += Loc.F("Module.Event.Describe.Legions", DescribeLegion(e.TriggerLegion), DescribeLegion(e.AffectedLegion));
        return text;
    }

    /// <summary>军团字段（行动序号）的显示：0 → "-"，找到军团 → 国家名，找不到 → "?行动序号"。</summary>
    public string DescribeLegion(int actionId)
    {
        if (actionId == 0) return "-";
        var legion = FindLegionByActionId(actionId);
        return legion.HasValue
            ? CountryNameProvider.Get(legion.Value.CountryId)
            : Loc.F("Module.Event.Describe.MissingLegion", actionId);
    }

    /// <summary>按行动序号（<see cref="Legion.ActionId"/>）找军团；事件的两个军团字段存的就是它。</summary>
    public Legion? FindLegionByActionId(int actionId)
    {
        if (_mapData == null) return null;
        foreach (var legion in _mapData.Legions)
            if (legion.ActionId == actionId) return legion;
        return null;
    }

    #endregion

    #region 选择

    /// <summary>
    /// 用户另选了事件（<see cref="SetSelectedEventIndex"/>、[ / ] 循环选择）。事件设置窗口据此跟随地图上的选择。
    /// 新建 / 粘贴 / 删除 / 排序带来的选中变化总伴随事件列表的变更通知，不再单独发出。
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>选中事件的列表下标（无效时为 -1）。</summary>
    public int SelectedEventIndex => IsValidIndex(_selectedEventIndex) ? _selectedEventIndex : -1;

    public MapEvent? GetSelectedEvent()
        => IsValidIndex(_selectedEventIndex) ? _mapData!.Events[_selectedEventIndex] : null;

    public ModifierResult SetSelectedEventIndex(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();
        Select(index);
        return ModifierResult.Ok(DescribeSelection());
    }

    /// <summary>按显示顺序选下一个事件（到末尾回到第一个）。</summary>
    public ModifierResult CycleSelectEvent() => Cycle(+1);

    /// <summary>按显示顺序选上一个事件（到开头回到最后一个）。</summary>
    public ModifierResult CyclePreviousEvent() => Cycle(-1);

    public void ResetSelection() => _selectedEventIndex = -1;

    /// <summary>选中事件的状态行，例如 "事件 3/9：#5 回合触发 R5 → 给予经济"。</summary>
    public string DescribeSelection()
    {
        if (!IsValidIndex(_selectedEventIndex)) return Loc.T("Module.Event.Msg.NoSelection", "没有选中的事件");
        var order = GetDisplayOrder();
        return Loc.F("Module.Event.Msg.Selected", order.IndexOf(_selectedEventIndex) + 1, order.Count,
            Describe(_mapData!.Events[_selectedEventIndex]));
    }

    private ModifierResult Cycle(int step)
    {
        if (_mapData == null) return NotReady();
        if (_mapData.Events.Count == 0) return ModifierResult.Fail(Loc.T("Module.Event.Msg.Empty", "没有事件可选择"));

        var order = GetDisplayOrder();
        int pos = order.IndexOf(SelectedEventIndex);
        pos = pos < 0 ? (step > 0 ? 0 : order.Count - 1) : (pos + step + order.Count) % order.Count;
        Select(order[pos]);
        return ModifierResult.Ok(DescribeSelection());
    }

    private void Select(int index)
    {
        if (_selectedEventIndex == index) return;
        _selectedEventIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region 增删改

    /// <summary>新建默认事件（序号 = 最大序号 + 1，结束段 00 CC CC CC），追加到列表末尾并选中。</summary>
    public ModifierResult CreateEvent()
    {
        if (_mapData == null) return NotReady();
        return Append(MapEvent.CreateDefault(GetNextSequence()), "Module.Event.Msg.Created");
    }

    /// <summary>以指定序号新建默认事件；该序号已被占用时失败（以前会返回成功，但什么也没建）。</summary>
    public ModifierResult CreateEventAtSequence(int sequence)
    {
        if (_mapData == null) return NotReady();
        if (!MapEventFields.Sequence.TryValidate(sequence, out var error)) return ModifierResult.Fail(error!);
        if (GetEventIndex(sequence) >= 0)
            return ModifierResult.Fail(Loc.F("Module.Event.Msg.SequenceExists", sequence));
        return Append(MapEvent.CreateDefault(sequence), "Module.Event.Msg.Created");
    }

    /// <summary>原样追加一个事件并选中。</summary>
    public ModifierResult AddEvent(MapEvent mapEvent)
    {
        if (_mapData == null) return NotReady();
        return Append(mapEvent, "Module.Event.Msg.Created");
    }

    public ModifierResult UpdateEvent(int index, MapEvent mapEvent)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();

        if (!_mapData.Events[index].Equals(mapEvent))
        {
            _mapData.Events[index] = mapEvent;
            MarkModified();
        }
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.Updated", mapEvent.Sequence));
    }

    /// <summary>改一个字段（值先按 <see cref="MapEventField.TryParse"/> 的范围检查）。</summary>
    public ModifierResult SetEventField(int index, MapEventField field, string valueText)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();
        if (!field.TryParse(valueText, out int value, out var error)) return ModifierResult.Fail(error!);

        var e = _mapData.Events[index];
        field.Set(ref e, value);
        UpdateEvent(index, e);
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.FieldSet", e.Sequence, field.Label, field.Format(value)));
    }

    /// <summary>删除事件。选中的事件被删时清除选择；删的是它前面的事件时选择跟着前移，仍指向同一个事件。</summary>
    public ModifierResult DeleteEvent(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();

        int seq = _mapData.Events[index].Sequence;
        _mapData.Events.RemoveAt(index);
        if (_selectedEventIndex == index) _selectedEventIndex = -1;
        else if (_selectedEventIndex > index) _selectedEventIndex--;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.Deleted", seq));
    }

    public ModifierResult DeleteEventBySequence(int sequence)
    {
        if (_mapData == null) return NotReady();
        int index = GetEventIndex(sequence);
        return index < 0 ? ModifierResult.Fail(Loc.F("Module.Event.Msg.NotFound", sequence)) : DeleteEvent(index);
    }

    public ModifierResult DeleteAllEvents()
    {
        if (_mapData == null) return NotReady();
        int count = _mapData.Events.Count;
        if (count > 0)
        {
            _mapData.Events.Clear();
            MarkModified();
        }
        _selectedEventIndex = -1;
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.DeletedAll", count), count);
    }

    /// <summary>复制一个事件为新事件（序号 = 最大序号 + 1，其余字段相同），追加并选中。</summary>
    public ModifierResult DuplicateEvent(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();

        var copy = _mapData.Events[index];
        int source = copy.Sequence;
        copy.Sequence = GetNextSequence();
        _mapData.Events.Add(copy);
        _selectedEventIndex = _mapData.Events.Count - 1;
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.Duplicated", source, copy.Sequence));
    }

    #endregion

    #region 复制 / 粘贴

    public bool HasCopiedEvent => _copiedEvent.HasValue;

    public ModifierResult CopyEvent(int index)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();

        _copiedEvent = _mapData.Events[index];
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.Copied", _copiedEvent.Value.Sequence));
    }

    public ModifierResult CopyEventBySequence(int sequence)
    {
        if (_mapData == null) return NotReady();
        int index = GetEventIndex(sequence);
        return index < 0 ? ModifierResult.Fail(Loc.F("Module.Event.Msg.NotFound", sequence)) : CopyEvent(index);
    }

    /// <summary>把复制的事件粘贴为新事件（换成新序号），追加并选中。</summary>
    public ModifierResult PasteEvent()
    {
        if (_mapData == null) return NotReady();
        if (_copiedEvent == null) return ModifierResult.Fail(Loc.T("Module.Event.Msg.NothingCopied", "没有已复制的事件数据"));

        var e = _copiedEvent.Value;
        e.Sequence = GetNextSequence();
        return Append(e, "Module.Event.Msg.Pasted");
    }

    #endregion

    #region 排序

    /// <summary>按序号重排事件列表（稳定排序；选中的仍是同一个事件）。</summary>
    public ModifierResult SortEventsBySequence()
        => SortBy(e => e.Sequence, Loc.T("Module.Event.Msg.SortedBySequence", "事件已按序号排序"));

    /// <summary>按触发回合重排事件列表（稳定排序；选中的仍是同一个事件）。</summary>
    public ModifierResult SortEventsByTriggerRound()
        => SortBy(e => e.TriggerRound, Loc.T("Module.Event.Msg.SortedByRound", "事件已按触发回合排序"));

    /// <summary>
    /// 用 Move 原地重排：只移动位置不对的项，每次移动一条通知（以前是 Clear + 逐个 Add，一次 Reset 加 N 条 Add），
    /// 并把选中下标换算到新位置。
    /// </summary>
    private ModifierResult SortBy(Func<MapEvent, int> key, string doneMessage)
    {
        if (_mapData == null) return NotReady();
        var events = _mapData.Events;
        var order = Enumerable.Range(0, events.Count).OrderBy(i => key(events[i])).ThenBy(i => i).ToList();

        // current[p] = 位置 p 上那一项排序前的下标
        var current = Enumerable.Range(0, events.Count).ToList();
        bool moved = false;
        for (int target = 0; target < order.Count; target++)
        {
            int from = current.IndexOf(order[target], target);
            if (from == target) continue;
            events.Move(from, target);
            current.RemoveAt(from);
            current.Insert(target, order[target]);
            moved = true;
        }

        if (IsValidIndex(_selectedEventIndex)) _selectedEventIndex = order.IndexOf(_selectedEventIndex);
        if (moved) MarkModified();
        return ModifierResult.Ok(doneMessage, moved ? 1 : 0);
    }

    #endregion

    #region 引用

    /// <summary>引用某个序号的地方：其他事件的关联事件（低 16 位）、建筑与 v1/v2 单位的占领事件。</summary>
    public readonly record struct EventReferences(int RelatedEvents, int Buildings, int Armies)
    {
        public int Total => RelatedEvents + Buildings + Armies;
    }

    /// <summary>统计引用 <paramref name="sequence"/> 的地方（<paramref name="excludeIndex"/> 这一项自身的关联事件不算）。</summary>
    public EventReferences CountReferences(int sequence, int excludeIndex = -1)
    {
        if (_mapData == null || sequence <= 0) return default;

        int related = 0;
        for (int i = 0; i < _mapData.Events.Count; i++)
            if (i != excludeIndex && _mapData.Events[i].RelatedSequence == sequence) related++;
        int buildings = sequence <= byte.MaxValue ? _mapData.Buildings.Count(b => b.OccupationEvent == sequence) : 0;
        int armies = sequence <= byte.MaxValue ? _mapData.Armies.Count(a => a.OccupationEvent == sequence) : 0;
        return new EventReferences(related, buildings, armies);
    }

    /// <summary>
    /// 改了事件序号后把引用一起改过去：其他事件关联事件的低 16 位（高位参数保留）、建筑与 v1/v2 单位的占领事件。
    /// 建筑 / 单位的占领事件只有 1 字节，新序号大于 255 时这两类改不过去，保持原样（结果消息里会说明）。
    /// 会同时改到事件、建筑、单位三个列表，调用方用 <c>RecordEntitiesSnapshot</c> 记录撤销。
    /// </summary>
    public ModifierResult RetargetReferences(int oldSequence, int newSequence)
    {
        if (_mapData == null) return NotReady();
        if (oldSequence <= 0 || oldSequence == newSequence) return ModifierResult.Ok(null, 0);

        int related = 0, buildings = 0, armies = 0;
        var events = _mapData.Events;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.RelatedSequence != oldSequence) continue;
            e.RelatedEvent = (int)((uint)e.RelatedEvent & 0xFFFF0000u) | (newSequence & 0xFFFF);
            events[i] = e;
            related++;
        }

        bool byteFits = newSequence >= 0 && newSequence <= byte.MaxValue;
        if (oldSequence <= byte.MaxValue && byteFits)
        {
            for (int i = 0; i < _mapData.Buildings.Count; i++)
            {
                var b = _mapData.Buildings[i];
                if (b.OccupationEvent != oldSequence) continue;
                b.OccupationEvent = (byte)newSequence;
                _mapData.ReplaceBuilding(i, b);
                buildings++;
            }
            for (int i = 0; i < _mapData.Armies.Count; i++)
            {
                var a = _mapData.Armies[i];
                if (a.OccupationEvent != oldSequence) continue;
                a.OccupationEvent = (byte)newSequence;
                _mapData.ReplaceArmy(i, a);
                armies++;
            }
        }

        int total = related + buildings + armies;
        if (total > 0) MarkModified();
        string message = Loc.F("Module.Event.Msg.Retargeted", oldSequence, newSequence, related, buildings, armies);
        if (!byteFits)
        {
            var left = CountReferences(oldSequence);
            if (left.Buildings + left.Armies > 0) message += Loc.T("Module.Event.Msg.RetargetByteLimit");
        }
        return ModifierResult.Ok(message, total);
    }

    /// <summary>改序号（<paramref name="updateReferences"/> 时连带改引用，见 <see cref="RetargetReferences"/>）。</summary>
    public ModifierResult RenumberEvent(int index, int newSequence, bool updateReferences)
    {
        if (_mapData == null) return NotReady();
        if (!IsValidIndex(index)) return OutOfRange();
        if (!MapEventFields.Sequence.TryValidate(newSequence, out var error)) return ModifierResult.Fail(error!);

        var e = _mapData.Events[index];
        int oldSequence = e.Sequence;
        if (oldSequence == newSequence) return ModifierResult.Ok(Loc.F("Module.Event.Msg.Updated", newSequence), 0);

        e.Sequence = newSequence;
        UpdateEvent(index, e);
        // 还有别的事件用着旧序号时，引用仍指向它们，不能改走
        if (updateReferences && CountBySequence(oldSequence) == 0)
            return ModifierResult.Ok(Loc.F("Module.Event.Msg.RenumberedRefs", oldSequence, newSequence,
                RetargetReferences(oldSequence, newSequence).Message));
        return ModifierResult.Ok(Loc.F("Module.Event.Msg.Renumbered", oldSequence, newSequence));
    }

    #endregion

    private ModifierResult Append(MapEvent mapEvent, string messageKey)
    {
        _mapData!.Events.Add(mapEvent);
        _selectedEventIndex = _mapData.Events.Count - 1;
        MarkModified();
        return ModifierResult.Ok(Loc.F(messageKey, mapEvent.Sequence));
    }

    private bool IsValidIndex(int index) => _mapData != null && index >= 0 && index < _mapData.Events.Count;

    private static ModifierResult NotReady() => ModifierResult.Fail(Loc.T("Module.Event.Msg.NotReady", "地图数据未初始化"));

    private static ModifierResult OutOfRange() => ModifierResult.Fail(Loc.T("Module.Event.Msg.IndexOutOfRange", "事件索引超出范围"));
}
