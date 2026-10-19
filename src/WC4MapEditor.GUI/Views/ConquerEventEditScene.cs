using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 征服事件编辑场景（ConquerEventSettings.json）。
/// 列表 + 属性面板；标题 / 描述写入 stringtable 的 conquest_event_{Id} / conquest_event_desc_{Id}
/// （旧实现误用 event_intro_{Id}，那是活动介绍，与征服事件 101–109 撞号）。
/// </summary>
public class ConquerEventEditScene : JsonEditorScene
{
    private readonly ConquerEventSettingParser _parser = ConquerEventSettingParser.Instance;
    private readonly GroupFilter _conquers;
    private readonly TableEditor<ConquerEventData> _editor;

    public ConquerEventEditScene(MainWindow window) : base(window, "ConquerEvent", ConquerEventSettingParser.Instance)
    {
        _conquers = new GroupFilter(Loc.T("ConquerEvent.AllConquests"), c => Loc.F("ConquerEvent.ConquestItem", c));
        _editor = Register(new TableEditor<ConquerEventData>(this, _parser.Table, new TableEditorOptions<ConquerEventData>
        {
            Describe = ev => Loc.F("ConquerEvent.ListEntry", ev.Id, DisplayName(ev), ev.ConquerId),
            Matches = (ev, q) => ev.Id.ToString().Contains(q)
                                 || ev.ConquerId.ToString().Contains(q)
                                 || _parser.GetEventName(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || _parser.GetEventDesc(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase),
            Filter = ev => _conquers.Matches(ev.ConquerId),
            GetId = ev => ev.Id,
            SetId = (ev, id) => ev.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (sourceId, copy) => _parser.Texts.Copy(sourceId, copy.Id),
            Remove = _parser.Remove,
            SearchPlaceholder = Loc.T("ConquerEvent.SearchPlaceholder"),
            Header = _conquers.Combo
        }));
        _conquers.Changed += () => _editor.RefreshList();
        _editor.RecordsChanged += RefreshConquers;
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("ConquerEvent.Title"), EventFormParts.LinkCheckButton(this)), _editor.ListPanel, _editor.Form.View, 320);
        RefreshConquers();
        _editor.SelectFirst();
    }

    private string DisplayName(ConquerEventData ev)
    {
        var name = _parser.GetEventName(ev.Id);
        return string.IsNullOrEmpty(name) ? Loc.F("ConquerEvent.UnnamedEvent", ev.Id) : name;
    }

    private void RefreshConquers() => _conquers.Refresh(_parser.Items.Select(e => e.ConquerId));

    private void BuildForm(RecordForm<ConquerEventData> f)
    {
        string conquerLabel = Loc.T("ConquerEvent.Field.ConquerId");

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("ConquerEvent.Field.Id"), ev => ev.Id, (ev, id) => _parser.ChangeId(ev, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.StringText(Loc.T("ConquerEvent.Field.NameIni"), ev => _parser.GetEventName(ev.Id), (ev, v) => _parser.SetEventName(ev.Id, v));
        f.StringText(Loc.T("JsonEditor.ConquerEvent.DescIni"), ev => _parser.GetEventDesc(ev.Id), (ev, v) => _parser.SetEventDesc(ev.Id, v), multiline: true);
        f.KeyInt(conquerLabel, ev => ev.ConquerId, (ev, v) => { ev.ConquerId = v; return null; });

        for (int slot = 1; slot <= 2; slot++)
        {
            bool first = slot == 1;
            EventFormParts.AddBuffSlot(f,
                new EventFormParts.SlotLabels(Loc.F("ConquerEvent.Section.Buff", slot), Loc.F("ConquerEvent.Field.BuffId", slot),
                    Loc.F("ConquerEvent.Field.BuffRound", slot), Loc.F("ConquerEvent.Field.BuffCountry", slot)),
                ev => first ? ev.EventBuffId1 : ev.EventBuffId2, (ev, v) => { if (first) ev.EventBuffId1 = v; else ev.EventBuffId2 = v; },
                ev => first ? ev.Round1 : ev.Round2, (ev, v) => { if (first) ev.Round1 = v; else ev.Round2 = v; },
                ev => first ? ev.CountryId1 : ev.CountryId2, (ev, v) => { if (first) ev.CountryId1 = v; else ev.CountryId2 = v; });
        }

        f.Section(Loc.T("ConquerEvent.Section.Trigger"));
        EventFormParts.AddTrigger(f, Loc.T("ConquerEvent.Field.Trigger"), ev => ev.Trigger, (ev, v) => ev.Trigger = v);
        f.IntList(Loc.T("ConquerEvent.Field.TriggerValue"), ev => ev.TriggerValue, (ev, v) => ev.TriggerValue = v);
        f.IntList(Loc.T("ConquerEvent.Field.Location"), ev => ev.Location, (ev, v) => ev.Location = v);
        f.Int(Loc.T("ConquerEvent.Field.Chance"), ev => ev.Chance, (ev, v) => ev.Chance = v);

        f.JsonPreview(height: 220);

        _editor.FieldEdited += (_, label) => { if (label == conquerLabel) RefreshConquers(); };
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        var ev = new ConquerEventData
        {
            Id = newId,
            ConquerId = _conquers.IsAll ? 0 : _conquers.Selected,
            Trigger = 1,
            Chance = 100
        };
        // 默认名称会写进 stringtable，是数据不是界面文字，保持原样
        _parser.SetEventName(newId, $"新事件{newId}");
        _parser.SetEventDesc(newId, "");
        _editor.Insert(ev, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("ConquerEvent.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var ev in _parser.Items)
        {
            if (!seenIds.Add(ev.Id)) problems.Add(Loc.F("ConquerEvent.Validate.DuplicateId", ev.Id));
            if (ev.Chance < 0 || ev.Chance > 100) problems.Add(Loc.F("ConquerEvent.Validate.ChanceRange", ev.Id, ev.Chance));
            if (ev.EventBuffId1 < 0 || ev.EventBuffId2 < 0) problems.Add(Loc.F("ConquerEvent.Validate.NegativeBuff", ev.Id));
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
