using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 军队 Buff 编辑场景（对应 Python 工具的 ArmyBuff 编辑器，ArmyBuffSettings.json）。
/// 列表 + 属性面板；描述写入 stringtable 的 army_buff_desc_{Id}。
/// </summary>
public class ArmyBuffEditScene : JsonEditorScene
{
    private readonly ArmyBuffSettingParser _parser = ArmyBuffSettingParser.Instance;
    private readonly TableEditor<ArmyBuffSettingData> _editor;

    public ArmyBuffEditScene(MainWindow window) : base(window, "ArmyBuff", ArmyBuffSettingParser.Instance)
    {
        _editor = Register(new TableEditor<ArmyBuffSettingData>(this, _parser.Table, new TableEditorOptions<ArmyBuffSettingData>
        {
            Describe = b => Loc.F("ArmyBuff.ListEntry", b.Id, b.Name, b.Type, b.Level),
            Matches = (b, q) => b.Id.ToString().Contains(q)
                                || b.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || b.Type.ToString().Contains(q)
                                || _parser.GetBuffDesc(b.Id).Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = b => b.Id,
            SetId = (b, id) => b.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (sourceId, copy) => _parser.Texts.Copy(sourceId, copy.Id),
            Remove = _parser.Remove,
            SearchPlaceholder = Loc.T("ArmyBuff.SearchPlaceholder")
        }));
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("ArmyBuff.Title")), _editor.ListPanel, _editor.Form.View, 280);
        _editor.SelectFirst();
    }

    private void BuildForm(RecordForm<ArmyBuffSettingData> f)
    {
        f.Section(Loc.T("ArmyBuff.Section.Text"));
        f.StringText(Loc.T("ArmyBuff.Field.Desc"), b => _parser.GetBuffDesc(b.Id), (b, v) => _parser.SetBuffDesc(b.Id, v), multiline: true);

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("ArmyBuff.Field.Id"), b => b.Id, (b, id) => _parser.ChangeId(b, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Text(Loc.T("ArmyBuff.Field.Name"), b => b.Name, (b, v) => b.Name = v);
        f.Int(Loc.T("ArmyBuff.Field.Type"), b => b.Type, (b, v) => b.Type = v);
        f.Int(Loc.T("ArmyBuff.Field.Level"), b => b.Level, (b, v) => b.Level = v);

        f.Section(Loc.T("ArmyBuff.Section.Values"));
        f.Int(Loc.T("ArmyBuff.Field.Value"), b => b.Value, (b, v) => b.Value = v);
        f.Int(Loc.T("ArmyBuff.Field.Value2"), b => b.Value2, (b, v) => b.Value2 = v);

        f.Section(Loc.T("ArmyBuff.Section.Flags"));
        f.Bool(Loc.T("ArmyBuff.Field.Debuff"), b => b.Debuff, (b, v) => b.Debuff = v);
        f.Bool(Loc.T("ArmyBuff.Field.Trigger"), b => b.Trigger, (b, v) => b.Trigger = v);
        f.Bool(Loc.T("ArmyBuff.Field.Hide"), b => b.Hide, (b, v) => b.Hide = v);
        f.Bool(Loc.T("ArmyBuff.Field.ShowRounds"), b => b.ShowRounds, (b, v) => b.ShowRounds = v);

        f.JsonPreview();
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        var buff = new ArmyBuffSettingData { Id = newId, Name = $"NewBuff{newId}", Level = 1 };
        _parser.SetBuffDesc(newId, "新Buff的描述");   // 写入 stringtable 的游戏文本，不属于界面语言
        _editor.Insert(buff, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("ArmyBuff.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var b in _parser.Items)
        {
            string pfx = $"[Id={b.Id}]";
            if (!seenIds.Add(b.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (string.IsNullOrEmpty(b.Name)) problems.Add($"{pfx} {Loc.T("Common.NameEmpty")}");
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
