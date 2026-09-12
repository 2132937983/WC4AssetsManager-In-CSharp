using WC4MapEditor.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class EventModifier : ModifierBase
{
    public override string Name => "event";
    public override string DisplayName => "事件修改器";

    private MapEvent? _copiedEvent;

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        MapEvent mapEvent;
        if (parameter is MapEvent e)
        {
            mapEvent = e;
        }
        else
        {
            int newSeq = GetNextSequence();
            mapEvent = MapEvent.CreateDefault(newSeq);
        }

        _mapData.Events.Add(mapEvent);
        MarkModified();
        return ModifierResult.Ok($"已创建事件: 序号={mapEvent.Sequence}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        return ModifierResult.Fail("事件不支持按坐标删除，请使用DeleteEvent");
    }

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;

    public override object? GetDataAt(int col, int row) => null;

    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 事件CRUD

    public ModifierResult CreateEvent()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int newSeq = GetNextSequence();
        var newEvent = MapEvent.CreateDefault(newSeq);
        newEvent.EndSegment = unchecked((int)0xCCCCCC00);

        _mapData.Events.Add(newEvent);
        MarkModified();
        return ModifierResult.Ok($"已创建事件: 序号={newSeq}");
    }

    public ModifierResult CreateEventAtSequence(int sequence)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var existing = GetEventBySequence(sequence);
        if (existing != null)
            return ModifierResult.Ok($"序号 {sequence} 的事件已存在");

        var newEvent = MapEvent.CreateDefault(sequence);
        newEvent.EndSegment = unchecked((int)0xCCCCCC00);

        _mapData.Events.Add(newEvent);
        MarkModified();
        return ModifierResult.Ok($"已创建事件: 序号={sequence}");
    }

    public ModifierResult UpdateEvent(int index, MapEvent mapEvent)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Events.Count) return ModifierResult.Fail("事件索引超出范围");

        _mapData.Events[index] = mapEvent;
        MarkModified();
        return ModifierResult.Ok($"已更新事件: 序号={mapEvent.Sequence}");
    }

    public ModifierResult DeleteEvent(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Events.Count) return ModifierResult.Fail("事件索引超出范围");

        int seq = _mapData.Events[index].Sequence;
        _mapData.Events.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok($"已删除事件: 序号={seq}");
    }

    public ModifierResult DeleteEventBySequence(int sequence)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Events.Count; i++)
        {
            if (_mapData.Events[i].Sequence == sequence)
            {
                _mapData.Events.RemoveAt(i);
                MarkModified();
                return ModifierResult.Ok($"已删除事件: 序号={sequence}");
            }
        }

        return ModifierResult.Fail($"未找到序号为 {sequence} 的事件");
    }

    public ModifierResult DeleteAllEvents()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Events.Count;
        _mapData.Events.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有事件，共 {count} 个");
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyEvent(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Events.Count) return ModifierResult.Fail("事件索引超出范围");

        _copiedEvent = _mapData.Events[index];
        return ModifierResult.Ok($"已复制事件: 序号={_copiedEvent.Value.Sequence}");
    }

    public ModifierResult CopyEventBySequence(int sequence)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Events.Count; i++)
        {
            if (_mapData.Events[i].Sequence == sequence)
            {
                _copiedEvent = _mapData.Events[i];
                return ModifierResult.Ok($"已复制事件: 序号={sequence}");
            }
        }

        return ModifierResult.Fail($"未找到序号为 {sequence} 的事件");
    }

    public ModifierResult PasteEvent()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedEvent == null) return ModifierResult.Fail("没有已复制的事件数据");

        int newSeq = GetNextSequence();
        var newEvent = _copiedEvent.Value;
        newEvent.Sequence = newSeq;

        _mapData.Events.Add(newEvent);
        MarkModified();
        return ModifierResult.Ok($"已粘贴事件: 新序号={newSeq}");
    }

    #endregion

    #region 查询

    public MapEvent? GetEventBySequence(int sequence)
    {
        if (_mapData == null) return null;

        for (int i = 0; i < _mapData.Events.Count; i++)
        {
            if (_mapData.Events[i].Sequence == sequence)
                return _mapData.Events[i];
        }

        return null;
    }

    public int GetEventIndex(int sequence)
    {
        if (_mapData == null) return -1;

        for (int i = 0; i < _mapData.Events.Count; i++)
        {
            if (_mapData.Events[i].Sequence == sequence)
                return i;
        }

        return -1;
    }

    public IReadOnlyList<MapEvent> GetAllEvents()
    {
        return _mapData?.Events ?? [];
    }

    #region 排序

    public ModifierResult SortEventsBySequence()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Events.Count < 2) return ModifierResult.Ok("事件数量不足，无需排序");

        var sorted = _mapData.Events.OrderBy(e => e.Sequence).ToList();
        _mapData.Events.Clear();
        foreach (var e in sorted) _mapData.Events.Add(e);

        MarkModified();
        return ModifierResult.Ok("事件已按序号排序");
    }

    public ModifierResult SortEventsByTriggerRound()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Events.Count < 2) return ModifierResult.Ok("事件数量不足，无需排序");

        var sorted = _mapData.Events.OrderBy(e => e.TriggerRound).ToList();
        _mapData.Events.Clear();
        foreach (var e in sorted) _mapData.Events.Add(e);

        MarkModified();
        return ModifierResult.Ok("事件已按触发回合排序");
    }

    #endregion

    #region 循环选择

    private int _selectedEventIndex = -1;

    public int SelectedEventIndex => _selectedEventIndex;

    public ModifierResult CycleSelectEvent()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Events.Count == 0) return ModifierResult.Fail("没有事件可选择");

        if (_selectedEventIndex < 0 || _selectedEventIndex >= _mapData.Events.Count - 1)
            _selectedEventIndex = 0;
        else
            _selectedEventIndex++;

        var selected = _mapData.Events[_selectedEventIndex];
        return ModifierResult.Ok($"已选中事件 {_selectedEventIndex + 1}/{_mapData.Events.Count}: 序号={selected.Sequence}");
    }

    public ModifierResult SetSelectedEventIndex(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Events.Count) return ModifierResult.Fail("事件索引超出范围");

        _selectedEventIndex = index;
        var selected = _mapData.Events[index];
        return ModifierResult.Ok($"已选中事件 {index + 1}/{_mapData.Events.Count}: 序号={selected.Sequence}");
    }

    public MapEvent? GetSelectedEvent()
    {
        if (_mapData == null || _selectedEventIndex < 0 || _selectedEventIndex >= _mapData.Events.Count)
            return null;
        return _mapData.Events[_selectedEventIndex];
    }

    public void ResetSelection()
    {
        _selectedEventIndex = -1;
    }

    #endregion

    private int GetNextSequence()
    {
        if (_mapData == null || _mapData.Events.Count == 0) return 1;

        int maxSeq = 0;
        foreach (var e in _mapData.Events)
        {
            if (e.Sequence > maxSeq) maxSeq = e.Sequence;
        }

        return maxSeq + 1;
    }

    #endregion
}