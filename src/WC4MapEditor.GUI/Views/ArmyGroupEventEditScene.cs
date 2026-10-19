using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 集团军事件编辑场景（对应 Python 工具的 ArmyGroupEvent 编辑器，ArmyGroupEventSettings.json）。
/// 列表 + 属性面板；标题 / 描述写入 stringtable 的 armygroup_event_{Id} / armygroup_event_desc_{Id}。
/// 列表按文件顺序显示，可按事件组筛选。
/// </summary>
public class ArmyGroupEventEditScene : JsonEditorScene
{
    private readonly ArmyGroupEventSettingParser _parser = ArmyGroupEventSettingParser.Instance;
    private readonly GroupFilter _groups;
    private readonly TableEditor<ArmyGroupEventData> _editor;

    public ArmyGroupEventEditScene(MainWindow window) : base(window, "ArmyGroupEvent", ArmyGroupEventSettingParser.Instance)
    {
        _groups = new GroupFilter(Loc.T("ArmyGroupEvent.AllGroups"), g => GroupLabel(g));
        _editor = Register(new TableEditor<ArmyGroupEventData>(this, _parser.Table, new TableEditorOptions<ArmyGroupEventData>
        {
            Describe = ev => Loc.F("ArmyGroupEvent.ListEntry", ev.Id, DisplayName(ev), GroupLabel(ev.EventId, compact: true)),
            Matches = (ev, q) => ev.Id.ToString().Contains(q)
                                 || ev.EventId.ToString().Contains(q)
                                 || ReferenceKinds.EventGroupName(ev.EventId).Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || _parser.GetEventName(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || _parser.GetEventDesc(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase),
            Filter = ev => _groups.Matches(ev.EventId),
            GetId = ev => ev.Id,
            SetId = (ev, id) => ev.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (sourceId, copy) => _parser.Texts.Copy(sourceId, copy.Id),
            Remove = _parser.Remove,
            SearchPlaceholder = Loc.T("ArmyGroupEvent.SearchPlaceholder"),
            Header = _groups.Combo
        }));
        _groups.Changed += () => _editor.RefreshList();
        _editor.RecordsChanged += RefreshGroups;
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("ArmyGroupEvent.Title"), EventFormParts.LinkCheckButton(this)), _editor.ListPanel, _editor.Form.View, 320);
        RefreshGroups();
        _editor.SelectFirst();
    }

    private string DisplayName(ArmyGroupEventData ev)
    {
        var name = _parser.GetEventName(ev.Id);
        return string.IsNullOrEmpty(name) ? Loc.F("ArmyGroupEvent.UnnamedEvent", ev.Id) : name;
    }

    /// <summary>事件组的显示：编号 + 活动名称（名称来自集团军活动的介绍）。<paramref name="compact"/> 时只在有名称时写“编号 名称”，用在列表行里。</summary>
    private static string GroupLabel(int groupId, bool compact = false)
    {
        var name = ReferenceKinds.EventGroupName(groupId);
        if (name.Length == 0) return compact ? groupId.ToString() : Loc.F("ArmyGroupEvent.GroupItem", groupId);
        return compact ? $"{groupId} {name}" : Loc.F("Picker.EventGroupNamed", groupId, name);
    }

    private void RefreshGroups() => _groups.Refresh(_parser.Items.Select(e => e.EventId));

    private void BuildForm(RecordForm<ArmyGroupEventData> f)
    {
        string eventIdLabel = Loc.T("ArmyGroupEvent.Field.EventId");

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("ArmyGroupEvent.Field.Id"), ev => ev.Id, (ev, id) => _parser.ChangeId(ev, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.StringText(Loc.T("ArmyGroupEvent.Field.NameIni"), ev => _parser.GetEventName(ev.Id), (ev, v) => _parser.SetEventName(ev.Id, v));
        f.StringText(Loc.T("ArmyGroupEvent.Field.DescIni"), ev => _parser.GetEventDesc(ev.Id), (ev, v) => _parser.SetEventDesc(ev.Id, v), multiline: true);
        f.KeyInt(eventIdLabel, ev => ev.EventId, (ev, v) => { ev.EventId = v; return null; },
            decorate: box => ReferenceFields.Single(box, ReferenceKinds.EventGroup));
        f.Int(Loc.T("JsonEditor.ArmyGroupEvent.StageId"), ev => ev.StageId, (ev, v) => ev.StageId = v);

        for (int slot = 1; slot <= 2; slot++)
        {
            bool first = slot == 1;
            EventFormParts.AddBuffSlot(f,
                new EventFormParts.SlotLabels(Loc.F("ArmyGroupEvent.Section.Buff", slot), Loc.F("ArmyGroupEvent.Field.BuffId", slot),
                    Loc.F("ArmyGroupEvent.Field.BuffRound", slot), Loc.F("ArmyGroupEvent.Field.BuffCountry", slot)),
                ev => first ? ev.EventBuffId1 : ev.EventBuffId2, (ev, v) => { if (first) ev.EventBuffId1 = v; else ev.EventBuffId2 = v; },
                ev => first ? ev.Round1 : ev.Round2, (ev, v) => { if (first) ev.Round1 = v; else ev.Round2 = v; },
                ev => first ? ev.CountryId1 : ev.CountryId2, (ev, v) => { if (first) ev.CountryId1 = v; else ev.CountryId2 = v; });
        }

        f.Section(Loc.T("ArmyGroupEvent.Section.Trigger"));
        EventFormParts.AddTrigger(f, Loc.T("ArmyGroupEvent.Field.Trigger"), ev => ev.Trigger, (ev, v) => ev.Trigger = v);
        f.IntList(Loc.T("ArmyGroupEvent.Field.TriggerValue"), ev => ev.TriggerValue, (ev, v) => ev.TriggerValue = v);
        f.IntList(Loc.T("ArmyGroupEvent.Field.Location"), ev => ev.Location, (ev, v) => ev.Location = v);
        f.Int(Loc.T("ArmyGroupEvent.Field.Chance"), ev => ev.Chance, (ev, v) => ev.Chance = v);

        f.Section(Loc.T("ArmyGroupEvent.Section.Limits"));
        f.IntList(Loc.T("ArmyGroupEvent.Field.PlayerCountry"), ev => ev.PlayerCountry, (ev, v) => ev.PlayerCountry = v,
            Loc.T("Picker.Tip.CountryList"), box => ReferenceFields.Multi(box, ReferenceKinds.Country));
        f.IntList(Loc.T("ArmyGroupEvent.Field.DisableEvents"), ev => ev.DisableEvents, (ev, v) => ev.DisableEvents = v);

        f.JsonPreview(height: 220);

        _editor.FieldEdited += (_, label) => { if (label == eventIdLabel) RefreshGroups(); };
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        var ev = new ArmyGroupEventData
        {
            Id = newId,
            EventId = _groups.IsAll ? 0 : _groups.Selected,
            Trigger = 1,
            Chance = 100
        };
        // 默认名称会写进 stringtable，是数据不是界面文字，保持原样
        _parser.SetEventName(newId, $"新事件{newId}");
        _parser.SetEventDesc(newId, "");
        _editor.Insert(ev, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("ArmyGroupEvent.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var ev in _parser.Items)
        {
            if (!seenIds.Add(ev.Id)) problems.Add(Loc.F("ArmyGroupEvent.Validate.DuplicateId", ev.Id));
            if (ev.Chance < 0 || ev.Chance > 100) problems.Add(Loc.F("ArmyGroupEvent.Validate.ChanceRange", ev.Id, ev.Chance));
            if (ev.EventBuffId1 < 0 || ev.EventBuffId2 < 0) problems.Add(Loc.F("ArmyGroupEvent.Validate.NegativeBuff", ev.Id));
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
