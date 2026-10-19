using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 事件 Buff 编辑场景（对应 Python 工具中的 EventBuff 编辑器，EventBuffSettings.json）。
/// 列表 + 属性面板；描述写入 stringtable 的 event_buff_desc_{Id}（没有时游戏回退到 event_buff_desc_{Type}）。
/// </summary>
public class EventBuffEditScene : JsonEditorScene
{
    private readonly EventBuffSettingParser _parser = EventBuffSettingParser.Instance;
    private readonly TableEditor<EventBuffData> _editor;

    public EventBuffEditScene(MainWindow window) : base(window, "EventBuff", EventBuffSettingParser.Instance)
    {
        _editor = Register(new TableEditor<EventBuffData>(this, _parser.Table, new TableEditorOptions<EventBuffData>
        {
            Describe = b => Loc.F("EventBuff.ListEntry", b.Id, DisplayDesc(b), b.Type),
            Matches = (b, q) => b.Id.ToString().Contains(q)
                                || b.Type.ToString().Contains(q)
                                || b.Value.ToString().Contains(q)
                                || _parser.GetBuffDesc(b.Id, b.Type).Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = b => b.Id,
            SetId = (b, id) => b.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (sourceId, copy) => _parser.Texts.Copy(sourceId, copy.Id),
            Remove = _parser.Remove,
            SearchPlaceholder = Loc.T("JsonEditor.EventBuff.SearchPlaceholder")
        }));
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("EventBuff.Title"), EventFormParts.LinkCheckButton(this)), _editor.ListPanel, _editor.Form.View, 320);
        _editor.SelectFirst();
    }

    private string DisplayDesc(EventBuffData b)
    {
        var desc = _parser.GetBuffDesc(b.Id, b.Type);
        return string.IsNullOrEmpty(desc) ? $"Buff_{b.Id}" : desc;
    }

    private void BuildForm(RecordForm<EventBuffData> f)
    {
        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("EventBuff.Field.Id"), b => b.Id, (b, id) => _parser.ChangeId(b, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Int(Loc.T("EventBuff.Field.Type"), b => b.Type, (b, v) => b.Type = v);
        f.Int(Loc.T("EventBuff.Field.Value"), b => b.Value, (b, v) => b.Value = v);
        f.Int(Loc.T("EventBuff.Field.Round"), b => b.Round, (b, v) => b.Round = v);

        f.Section(Loc.T("EventBuff.Section.Links"));
        f.IntList(Loc.T("EventBuff.Field.ArmyBuffs"), b => b.ArmyBuffs, (b, v) => b.ArmyBuffs = v,
            Loc.T("Picker.Tip.ArmyBuffList"), box => ReferenceFields.Multi(box, ReferenceKinds.ArmyBuff));

        f.Section(Loc.T("EventBuff.Section.Desc"));
        f.StringText(Loc.T("Common.Description"), b => _parser.GetOwnBuffDesc(b.Id), (b, v) => _parser.SetBuffDesc(b.Id, v), multiline: true);
        // 没有按 Id 的描述时，游戏显示 event_buff_desc_{Type}：在下面提示回退文本
        var fallback = EditorTheme.Muted("");
        fallback.TextWrapping = TextWrapping.Wrap;
        f.Row(Loc.T("JsonEditor.EventBuff.FallbackDesc"), fallback);
        f.OnLoad(b => fallback.Text = b == null || !string.IsNullOrEmpty(_parser.GetOwnBuffDesc(b.Id)) ? "" : _parser.GetBuffDesc(b.Id, b.Type));

        f.JsonPreview(height: 220);
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        var buff = new EventBuffData { Id = newId, Type = 0, Value = 0, Round = -1 };
        _parser.SetBuffDesc(newId, $"新Buff{newId}");   // 写入 stringtable 的游戏文本，不属于界面语言
        _editor.Insert(buff, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("EventBuff.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var b in _parser.Items)
        {
            string pfx = $"[Id={b.Id}]";
            if (!seenIds.Add(b.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (b.Round < -1) problems.Add($"{pfx} {Loc.F("EventBuff.Validate.RoundTooSmall", b.Round)}");
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
