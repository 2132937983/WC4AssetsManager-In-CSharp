using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 事件设置窗口（对应 VB Assist/EventSetting.vb，事件编辑模式按 Q 打开）。
/// <para>
/// 左边是按序号排序的事件列表（行首 ⚠ 表示有引用问题），右边是 11 个字段：触发条件 / 触发事件是可输入的下拉框
/// （选项来自 setting.txt 的 TriggerList / EventList），两个军团字段可点"选择"从军团列表里挑（写入军团的行动序号），
/// 关联事件、进攻目标格与军团字段旁实时显示它们指向谁。交互沿用 <see cref="ListEditorWindow"/>：切换列表项自动提交、
/// Enter 提交、Esc 撤回未提交的修改 / 再按关闭，每次提交、增删、复制各一步撤销。
/// </para>
/// <para>
/// 列表是排序后的视图：<see cref="_rows"/> 把视图行映射到 <c>MapData.Events</c> 的下标，文件里的事件顺序保持不变（与 VB 相同）。
/// 改了序号后若还有地方引用旧序号，会询问是否一起改过去（单独一步撤销）。
/// 窗口打开期间别处改了事件（地图上撤销 / 重做、模式按键、命令行），列表自动重新加载；地图上用 [ / ] 另选事件，列表跟着选；
/// 改了军团 / 建筑 / 单位，回到窗口时刷新引用提示。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.EventSetting)]
public sealed class EventSettingWindow : ListEditorWindow
{
    /// <summary>视图行 → MapData.Events 下标（按序号、再按下标排序）。</summary>
    private readonly List<int> _rows = new();
    private readonly Dictionary<MapEventField, TextBox> _boxes = new();
    private ComboBox _condition = null!;
    private ComboBox _effect = null!;
    private TextBlock _relatedInfo = null!;
    private TextBlock _campInfo = null!;
    private TextBlock _triggerLegionInfo = null!;
    private TextBlock _affectedLegionInfo = null!;
    private TextBlock _referenceInfo = null!;
    private List<EventReferenceIssue> _issues = new();

    /// <summary>字段面板正在显示的事件（MapData.Events 下标）与它加载时的序号，重新加载后据此找回同一个事件。</summary>
    private int _currentData = -1;
    private int _currentSequence;

    /// <summary>窗口最近一次写进模块的选中下标；模块的选中变了说明是地图上的按键（E / Ctrl+V）选了别的事件。</summary>
    private int _syncedSelection = -1;

    /// <summary>打开窗口前模块里的选中事件（首次填充列表会改掉模块的选中，所以先记下来）。</summary>
    private int _initialSelection = -1;

    /// <summary>本窗口发起的修改正在执行，期间的集合变更通知不算"别处改了"。</summary>
    private bool _applying;
    private bool _reloadQueued;
    private bool _externalChange;
    private bool _closed;
    private (string Text, bool Warning)? _pendingStatus;
    private (int Old, int New)? _pendingRenumber;
    /// <summary>上次计算引用提示时军团 / 建筑 / 单位的指纹。</summary>
    private int _referenceSignature;

    public EventSettingWindow()
    {
        Width = 880;
        Height = 700;
    }

    private EventModifier Events => Context.Module<EventModifier>()
        ?? throw new InvalidOperationException("EventModifier is not registered in EditModeManager");

    // ================================================================== ListEditorWindow

    protected override string TitleText => Loc.T("Assist.Event.Title", "事件参数设置");

    protected override int ItemCount => _rows.Count;

    protected override bool SupportsDuplicate => true;

    protected override string DescribeItem(int index)
    {
        int data = _rows[index];
        if (data >= Map.Events.Count) return "";
        var e = Map.Events[data];
        bool warn = EventReferenceChecker.ForEvent(_issues, data, e.Sequence).Any(i => i.Level == BTLIssueLevel.Warning);
        return (warn ? "⚠ " : "") + Events.Describe(e);
    }

    protected override void BuildFields(FieldPanel f)
    {
        f.LabelWidth = 112;

        f.Section(Loc.T("Assist.Event.Section.Event", "事件"));
        AddText(f, MapEventFields.Sequence);
        _relatedInfo = AddTextWithInfo(f, MapEventFields.RelatedEvent);

        f.Section(Loc.T("Assist.Event.Section.Trigger", "触发"));
        _condition = AddChoice(f, MapEventFields.TriggerCondition, MapEventNames.ConditionOptions());
        _triggerLegionInfo = AddLegion(f, MapEventFields.TriggerLegion);
        AddText(f, MapEventFields.TriggerRound);

        f.Section(Loc.T("Assist.Event.Section.Effect", "效果"));
        _effect = AddChoice(f, MapEventFields.TriggerEvent, MapEventNames.EffectOptions());
        _affectedLegionInfo = AddLegion(f, MapEventFields.AffectedLegion);
        _campInfo = AddTextWithInfo(f, MapEventFields.CampChange);
        AddText(f, MapEventFields.DialogueCode);

        f.Section(Loc.T("Assist.Event.Section.Other", "其他"));
        AddText(f, MapEventFields.Reserved);
        AddText(f, MapEventFields.EndSegment);

        f.Section(Loc.T("Assist.Event.Section.References", "引用与检查"));
        _referenceInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = EditorTheme.TextMuted };
        f.Custom(Loc.T("Assist.Event.References", "引用"), _referenceInfo, Loc.T("Assist.Event.ReferencesTooltip",
            "建筑 / 单位的占领事件与其他事件的关联事件按序号指向本事件；下面同时列出本事件的引用问题"));

        // 基类在这之后立刻填充列表，而视图行映射要先建好；模块里的选中事件也要在填充改掉它之前记下
        _initialSelection = Events.SelectedEventIndex;
        RebuildRows();
    }

    protected override void LoadFields(int index)
    {
        int data = _rows[index];
        if (data >= Map.Events.Count)
        {
            QueueReload(external: true);
            return;
        }

        _currentData = data;
        var e = Map.Events[data];
        _currentSequence = e.Sequence;
        Events.SetSelectedEventIndex(data);
        _syncedSelection = data;

        foreach (var (field, box) in _boxes) box.Text = field.Format(field.Get(e));
        SetChoice(_condition, e.TriggerCondition);
        SetChoice(_effect, e.TriggerEvent);
        UpdateFieldInfo();
        UpdateReferenceInfo();
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        int data = _rows[index];
        if (data >= Map.Events.Count)
        {
            errors.Add(Loc.T("Assist.Event.Stale", "事件列表已在别处修改，请重新选择"));
            return false;
        }

        var before = Map.Events[data];
        var e = before;
        foreach (var (field, box) in _boxes)
        {
            if (field.TryParse(box.Text, out int value, out var error)) field.Set(ref e, value);
            else errors.Add(error!);
        }
        if (TryChoice(_condition, MapEventFields.TriggerCondition.Label, (int)MapEventFields.TriggerCondition.Min,
                (int)MapEventFields.TriggerCondition.Max, errors, out int condition))
            e.TriggerCondition = condition;
        if (TryChoice(_effect, MapEventFields.TriggerEvent.Label, (int)MapEventFields.TriggerEvent.Min,
                (int)MapEventFields.TriggerEvent.Max, errors, out int effect))
            e.TriggerEvent = effect;
        if (errors.Count > 0) return false;

        var result = Events.UpdateEvent(data, e);
        if (!result.Success)
        {
            errors.Add(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
            return false;
        }

        if (data == _currentData) _currentSequence = e.Sequence;
        _pendingStatus = Events.CountBySequence(e.Sequence) > 1
            ? (Loc.F("Assist.Event.DuplicateWarning", e.Sequence), true)
            : (Loc.T("Assist.ListEditor.Applied", "已应用"), false);
        if (before.Sequence != e.Sequence && before.Sequence > 0 && Events.CountBySequence(before.Sequence) == 0
            && Events.CountReferences(before.Sequence).Total > 0)
            _pendingRenumber = (before.Sequence, e.Sequence);
        // 序号可能变了（排序位置跟着变），引用提示也要按新数据重算：提交完成后整体刷新一次
        QueueReload(external: false);
        return true;
    }

    protected override int AddItem()
    {
        Events.CreateEvent();
        RebuildRows();
        return _rows.IndexOf(Events.SelectedEventIndex);
    }

    protected override void RemoveItem(int index)
    {
        Events.DeleteEvent(_rows[index]);
        RebuildRows();
    }

    protected override int DuplicateItem(int index)
    {
        Events.DuplicateEvent(_rows[index]);
        RebuildRows();
        return _rows.IndexOf(Events.SelectedEventIndex);
    }

    protected override void RecordChange(string description, Action apply)
    {
        _applying = true;
        try
        {
            Context.Editor.RecordCollectionChange(description, Map.Events, apply);
        }
        finally
        {
            _applying = false;
        }
    }

    protected override void OnAttached()
    {
        Map.Events.CollectionChanged += OnEventsChanged;
        Events.SelectionChanged += OnModuleSelectionChanged;
        Activated += OnWindowActivated;

        int target = PreferredSelection();
        int row = _rows.IndexOf(target);
        if (row >= 0) SelectItem(row);
        ShowSummary();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        Map.Events.CollectionChanged -= OnEventsChanged;
        Events.SelectionChanged -= OnModuleSelectionChanged;
        Activated -= OnWindowActivated;
        base.OnClosed(e);
    }

    // ================================================================== 字段控件

    private void AddText(FieldPanel f, MapEventField field)
    {
        var box = f.Text(field.Label, field.Tooltip);
        _boxes[field] = box;
    }

    /// <summary>输入框 + 右侧实时说明（关联事件指向谁、进攻目标是哪一格）。</summary>
    private TextBlock AddTextWithInfo(FieldPanel f, MapEventField field)
    {
        var box = new TextBox { ToolTip = field.Tooltip, Width = 110 };
        var info = InfoText();
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(info);
        box.TextChanged += (_, _) => { f.Changed(); UpdateFieldInfo(); };
        _boxes[field] = box;
        f.Custom(field.Label, row, field.Tooltip);
        return info;
    }

    /// <summary>军团字段：输入框 + "选择"按钮（军团列表，写入行动序号）+ 右侧实时说明。</summary>
    private TextBlock AddLegion(FieldPanel f, MapEventField field)
    {
        var box = new TextBox { ToolTip = field.Tooltip, Width = 70 };
        var pick = EditorTheme.Button(Loc.T("Assist.Event.PickLegion", "选择"));
        pick.Margin = new Thickness(6, 0, 0, 0);
        pick.Padding = new Thickness(10, 2, 10, 2);
        pick.ToolTip = Loc.T("Assist.Event.PickLegionTooltip", "从军团列表中选择（写入军团的行动序号）");
        pick.Click += (_, _) => PickLegion(box);
        var info = InfoText();
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(box, Dock.Left);
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(pick);
        row.Children.Add(info);
        box.TextChanged += (_, _) => { f.Changed(); UpdateFieldInfo(); };
        _boxes[field] = box;
        f.Custom(field.Label, row, field.Tooltip);
        return info;
    }

    private ComboBox AddChoice(FieldPanel f, MapEventField field, IReadOnlyList<ConfigKeyValue> options)
    {
        var combo = f.Choice(field.Label, options, field.Tooltip);
        combo.SelectionChanged += (_, _) => UpdateFieldInfo();
        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => UpdateFieldInfo()));
        return combo;
    }

    private static TextBlock InfoText() => new()
    {
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = EditorTheme.TextMuted
    };

    private void PickLegion(TextBox target)
    {
        var picker = new LegionBelongListWindow(Map.Legions.ToList()) { Owner = this };
        picker.ShowDialog();
        if (picker.IsConfirmed && picker.SelectedActionId is int actionId)
            target.Text = actionId.ToString(CultureInfo.InvariantCulture);
    }

    // ================================================================== 实时说明

    /// <summary>按输入框里当前（未提交）的值，更新关联事件、进攻目标格与两个军团字段旁的说明。</summary>
    private void UpdateFieldInfo()
    {
        DescribeRelated();
        DescribeTarget();
        DescribeLegionField(MapEventFields.TriggerLegion, _triggerLegionInfo);
        DescribeLegionField(MapEventFields.AffectedLegion, _affectedLegionInfo);
    }

    /// <summary>读输入框里的值；无法解析或超出范围时在说明处显示原因（空框不提示）并返回 false。</summary>
    private bool TryReadField(MapEventField field, TextBlock info, out int value)
    {
        value = 0;
        if (!_boxes.TryGetValue(field, out var box)) return false;
        if (field.TryParse(box.Text, out value, out var error)) return true;
        SetInfo(info, string.IsNullOrWhiteSpace(box.Text) ? "" : "⚠ " + error, true);
        return false;
    }

    private void DescribeRelated()
    {
        if (!TryReadField(MapEventFields.RelatedEvent, _relatedInfo, out int value)) return;
        int seq = value & 0xFFFF;
        int extra = (int)((uint)value >> 16);
        if (seq == 0)
        {
            SetInfo(_relatedInfo, Loc.T("Assist.Event.Info.None", "无"), false);
            return;
        }
        string suffix = extra != 0 ? Loc.F("Assist.Event.Info.RelatedExtra", extra) : "";
        int index = Events.GetEventIndex(seq);
        if (index < 0) SetInfo(_relatedInfo, Loc.F("Assist.Event.Info.MissingEvent", seq) + suffix, true);
        else SetInfo(_relatedInfo, "→ " + Events.Describe(Map.Events[index]) + suffix, false);
    }

    /// <summary>触发事件是"军团向一个方向进攻"时，阵营变换字段是目标格下标：显示它是哪一格。</summary>
    private void DescribeTarget()
    {
        if (!TryChoice(_effect, MapEventFields.TriggerEvent.Label, 0, int.MaxValue, new List<string>(), out int effect)
            || effect != MapEvent.EffectAttackTowards)
        {
            SetInfo(_campInfo, "", false);
            return;
        }
        if (!TryReadField(MapEventFields.CampChange, _campInfo, out int index)) return;
        if (index >= Map.MapWidth * Map.MapHeight)
        {
            SetInfo(_campInfo, Loc.F("Assist.Event.Info.TargetOutsideMap", index), true);
            return;
        }
        var cell = HexCoord.FromIndex(index, Map.MapWidth);
        SetInfo(_campInfo, Loc.F("Assist.Event.Info.TargetHex", cell.Col, cell.Row), false);
    }

    private void DescribeLegionField(MapEventField field, TextBlock info)
    {
        if (!TryReadField(field, info, out int actionId)) return;
        if (actionId == 0)
        {
            SetInfo(info, Loc.T("Assist.Event.Info.NoLegion", "不指定"), false);
            return;
        }
        var legion = Events.FindLegionByActionId(actionId);
        if (legion.HasValue)
            SetInfo(info, Loc.F("Assist.Event.Info.Legion", CountryNameProvider.Get(legion.Value.CountryId), legion.Value.CountryId), false);
        else
            SetInfo(info, Loc.F("Assist.Event.Info.MissingLegion", actionId), true);
    }

    private static void SetInfo(TextBlock info, string text, bool warning)
    {
        info.Text = text;
        info.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        info.Foreground = warning ? EditorTheme.Warning : EditorTheme.TextMuted;
    }

    /// <summary>当前事件（已提交的数据）被谁引用，以及与它有关的检查问题。</summary>
    private void UpdateReferenceInfo()
    {
        if (_currentData < 0 || _currentData >= Map.Events.Count)
        {
            _referenceInfo.Text = "";
            return;
        }

        var e = Map.Events[_currentData];
        var refs = Events.CountReferences(e.Sequence, _currentData);
        var lines = new List<string> { Loc.F("Assist.Event.ReferencedBy", refs.Buildings, refs.Armies, refs.RelatedEvents) };
        var related = EventReferenceChecker.ForEvent(_issues, _currentData, e.Sequence).ToList();
        foreach (var issue in related)
            lines.Add((issue.Level == BTLIssueLevel.Warning ? "⚠ " : "· ") + issue.Message);
        _referenceInfo.Text = string.Join("\n", lines);
        _referenceInfo.Foreground = related.Any(i => i.Level == BTLIssueLevel.Warning) ? EditorTheme.Warning : EditorTheme.TextMuted;
    }

    // ================================================================== 列表视图与同步

    private void RebuildRows()
    {
        _rows.Clear();
        _rows.AddRange(Events.GetDisplayOrder());
        _issues = EventReferenceChecker.Check(Map);
        _referenceSignature = ComputeReferenceSignature();
    }

    /// <summary>打开时选中谁：打开前模块里选中的事件 → 焦点格上建筑 / 单位的占领事件 → 第一项。</summary>
    private int PreferredSelection()
    {
        if (_initialSelection >= 0 && _initialSelection < Map.Events.Count) return _initialSelection;

        var request = Context.Request;
        if (request.Col < 0 || request.Row < 0 || request.Col >= Map.MapWidth || request.Row >= Map.MapHeight) return -1;
        int coord = request.Row * Map.MapWidth + request.Col;
        foreach (var b in Map.Buildings)
            if (b.Coordinate == coord && b.OccupationEvent != 0) return Events.GetEventIndex(b.OccupationEvent);
        foreach (var a in Map.Armies)
            if (a.Coordinate == coord && a.OccupationEvent != 0) return Events.GetEventIndex(a.OccupationEvent);
        return -1;
    }

    private void ShowSummary()
    {
        int warnings = _issues.Count(i => i.Level == BTLIssueLevel.Warning);
        SetStatus(warnings == 0
            ? Loc.F("Assist.Event.Summary", Map.Events.Count)
            : Loc.F("Assist.Event.SummaryIssues", Map.Events.Count, warnings), warnings > 0);
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_applying) return;
        QueueReload(external: true);
    }

    /// <summary>地图上用 [ / ] 另选了事件：列表跟过去（与切换列表项一样，先自动提交当前项）。</summary>
    private void OnModuleSelectionChanged(object? sender, EventArgs e)
    {
        // 重载已排队时由重载按模块的选中定位；窗口自己载入某项时选中的就是当前项
        if (_applying || _reloadQueued || _closed) return;
        int selected = Events.SelectedEventIndex;
        if (selected < 0 || selected == _currentData) return;
        int row = _rows.IndexOf(selected);
        if (row >= 0) SelectItem(row);
    }

    /// <summary>
    /// 回到窗口时检查军团 / 建筑 / 单位是否在别处改过（事件本身的变化由列表通知处理），改过就重算引用提示。
    /// 有未提交的输入时只刷新字段旁的说明，不重新载入（会冲掉输入）。
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_closed || _reloadQueued) return;
        int now = ComputeReferenceSignature();
        if (now == _referenceSignature) return;
        _referenceSignature = now;
        _issues = EventReferenceChecker.Check(Map);
        if (HasUncommittedChanges)
        {
            UpdateFieldInfo();
            UpdateReferenceInfo();
            return;
        }
        RefreshList(CurrentIndex);
        ShowSummary();
    }

    /// <summary>合并多次通知，在当前操作结束后（输入处理之前）重载一次列表。</summary>
    private void QueueReload(bool external)
    {
        _externalChange |= external;
        if (_reloadQueued || _closed) return;
        _reloadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Reload));
    }

    private void Reload()
    {
        _reloadQueued = false;
        if (_closed) return;

        bool external = _externalChange;
        _externalChange = false;
        int keep = external ? FindSelectionAfterExternalChange() : FindCurrentAfterChange();
        int previousRow = CurrentIndex;
        RebuildRows();
        int row = _rows.IndexOf(keep);
        RefreshList(row >= 0 ? row : Math.Max(0, Math.Min(previousRow, _rows.Count - 1)));

        var status = external ? (Loc.T("Assist.Event.Reloaded", "事件列表已在别处修改，已重新加载"), false) : _pendingStatus;
        _pendingStatus = null;
        if (status is { } s) SetStatus(s.Text, s.Warning);

        if (_pendingRenumber is { } renumber)
        {
            _pendingRenumber = null;
            OfferRetarget(renumber.Old, renumber.New);
        }
    }

    /// <summary>别处改了事件：地图上的按键另选了事件（新建 / 粘贴）就跟过去，否则留在原来的事件上。</summary>
    private int FindSelectionAfterExternalChange()
    {
        int selected = Events.SelectedEventIndex;
        return selected >= 0 && selected != _syncedSelection ? selected : FindCurrentAfterChange();
    }

    /// <summary>重载前找回"当前事件"：下标还在且序号没变就用原下标，否则按序号找第一个。</summary>
    private int FindCurrentAfterChange()
    {
        if (_currentData >= 0 && _currentData < Map.Events.Count && Map.Events[_currentData].Sequence == _currentSequence)
            return _currentData;
        return Events.GetEventIndex(_currentSequence);
    }

    /// <summary>序号改了而旧序号仍被引用：询问是否把引用一起改到新序号（建筑 / 单位一起改，记一步全实体撤销）。</summary>
    private void OfferRetarget(int oldSequence, int newSequence)
    {
        var refs = Events.CountReferences(oldSequence);
        if (refs.Total == 0) return;

        var answer = MessageBox.Show(this,
            Loc.F("Assist.Event.RetargetConfirm", oldSequence, newSequence, refs.Total, refs.RelatedEvents, refs.Buildings, refs.Armies),
            TitleText, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ModifierResult result = default;
        _applying = true;
        try
        {
            Context.Editor.RecordEntitiesSnapshot(Loc.F("Assist.Event.UndoRetarget", oldSequence, newSequence),
                () => result = Events.RetargetReferences(oldSequence, newSequence));
        }
        finally
        {
            _applying = false;
        }
        NotifyChanged();
        RebuildRows();
        RefreshList(Math.Max(0, _rows.IndexOf(_currentData)));
        if (result.Message != null) SetStatus(result.Message);
    }

    /// <summary>会影响引用提示的事件以外数据的指纹：军团的行动序号与国家、建筑与单位的占领事件。</summary>
    private int ComputeReferenceSignature()
    {
        var hash = new HashCode();
        foreach (var l in Map.Legions)
        {
            hash.Add(l.ActionId);
            hash.Add(l.CountryId);
        }
        foreach (var b in Map.Buildings)
        {
            hash.Add(b.Coordinate);
            hash.Add(b.OccupationEvent);
        }
        foreach (var a in Map.Armies) hash.Add(a.OccupationEvent);
        return hash.ToHashCode();
    }
}
