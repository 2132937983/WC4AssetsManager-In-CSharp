using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 技能编辑场景（对应 Python 工具的 Skill 编辑器，SkillSettings.json）。
/// 列表 + 属性面板；名称 / 描述按 Type 写入 stringtable 的 skill_name_{Type} / skill_info_{Type}（同一 Type 的各级技能共用）。
/// </summary>
public class SkillEditScene : JsonEditorScene
{
    private readonly SkillSettingParser _parser = SkillSettingParser.Instance;
    private readonly TableEditor<SkillSettingData> _editor;

    public SkillEditScene(MainWindow window) : base(window, "Skill", SkillSettingParser.Instance)
    {
        _editor = Register(new TableEditor<SkillSettingData>(this, _parser.Table, new TableEditorOptions<SkillSettingData>
        {
            Describe = s => Loc.F("Skill.ListEntry", s.Id, DisplayName(s), s.Type, s.Level),
            Matches = (s, q) => s.Id.ToString().Contains(q)
                                || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || _parser.GetSkillName(s.Type).Contains(q, StringComparison.OrdinalIgnoreCase)
                                || s.Type.ToString().Contains(q),
            GetId = s => s.Id,
            SetId = (s, id) => s.Id = id,
            NextId = _ => _parser.GetNextId(),
            SearchPlaceholder = Loc.T("Skill.SearchPlaceholder")
        }));
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("Skill.Title")), _editor.ListPanel, _editor.Form.View, 280);
        _editor.SelectFirst();
    }

    private string DisplayName(SkillSettingData s)
    {
        var name = _parser.GetSkillName(s.Type);
        if (!string.IsNullOrEmpty(name)) return name;
        return string.IsNullOrEmpty(s.Name) ? Loc.F("Skill.UnnamedEntry", s.Id) : s.Name;
    }

    private void BuildForm(RecordForm<SkillSettingData> f)
    {
        string typeLabel = Loc.T("Skill.Field.Type");
        string nameLabel = Loc.T("Skill.Field.IniName");

        f.Section(Loc.T("Skill.Section.Icon"));
        var icon = new Image { Width = 96, Height = 96, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var iconCard = EditorTheme.Card(icon, new Thickness(8));
        iconCard.HorizontalAlignment = HorizontalAlignment.Left;
        var iconNote = EditorTheme.Muted("");
        iconNote.FontSize = 11;
        var iconPanel = new StackPanel();
        iconPanel.Children.Add(iconCard);
        iconPanel.Children.Add(iconNote);
        f.Row(Loc.T("Skill.Field.Icon"), iconPanel);
        f.OnLoad(s =>
        {
            var path = s == null ? null : _parser.GetIconPath(s.Type);
            icon.Source = SkiaBitmaps.Load(path);
            iconNote.Text = s == null ? "" : path == null ? Loc.F("Skill.IconMissing", s.Type) : IOPath.GetFileName(path);
        });

        f.Section(Loc.T("Skill.Section.Text"));
        f.StringText(nameLabel, s => _parser.GetSkillName(s.Type), (s, v) => _parser.SetSkillName(s.Type, v));
        f.StringText(Loc.T("Skill.Field.IniDesc"), s => _parser.GetSkillDesc(s.Type), (s, v) => _parser.SetSkillDesc(s.Type, v), multiline: true);

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Skill.Field.Id"), s => s.Id, (s, id) => _parser.ChangeId(s, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Text(Loc.T("Skill.Field.Name"), s => s.Name, (s, v) => s.Name = v);
        f.Int(Loc.T("Skill.Field.Level"), s => s.Level, (s, v) => s.Level = v);
        f.Int(typeLabel, s => s.Type, (s, v) => s.Type = v, Loc.T("JsonEditor.Skill.TypeTip"));
        f.Int(Loc.T("Skill.Field.Series"), s => s.Series, (s, v) => s.Series = v);

        f.Section(Loc.T("Skill.Section.Effect"));
        f.Int(Loc.T("Skill.Field.ActivatesChance"), s => s.ActivatesChance, (s, v) => s.ActivatesChance = v);
        f.Int(Loc.T("Skill.Field.IfPercent"), s => s.IfPercent, (s, v) => s.IfPercent = v);
        f.Int(Loc.T("Skill.Field.SkillEffect"), s => s.SkillEffect, (s, v) => s.SkillEffect = v);
        f.Int(Loc.T("Skill.Field.UpgradeId"), s => s.UpgradeId, (s, v) => s.UpgradeId = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.Skill));
        f.Int(Loc.T("Skill.Field.ArmyBuff"), s => s.ArmyBuff, (s, v) => s.ArmyBuff = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.ArmyBuff));
        f.Int(Loc.T("Skill.Field.AurasRange"), s => s.AurasRange, (s, v) => s.AurasRange = v);

        f.Section(Loc.T("Skill.Section.Cost"));
        f.Int(Loc.T("Skill.Field.CostMedal"), s => s.CostMedal, (s, v) => s.CostMedal = v);
        f.Int(Loc.T("Skill.Field.OpenDefault"), s => s.OpenDefault, (s, v) => s.OpenDefault = v);
        f.Int(Loc.T("Skill.Field.NeedStageId"), s => s.NeedStageId, (s, v) => s.NeedStageId = v);
        f.Int(Loc.T("Skill.Field.NeedScenarioId"), s => s.NeedScenarioId, (s, v) => s.NeedScenarioId = v);
        f.Int(Loc.T("Skill.Field.Score"), s => s.Score, (s, v) => s.Score = v);

        f.JsonPreview();

        _editor.FieldEdited += (_, label) =>
        {
            // Type 是名称 / 描述的索引：换 Type 后文本框显示新 Type 的文本
            if (label == typeLabel) f.Reload();
            // 名称按 Type 共享：同 Type 的其它等级也要更新列表文字
            else if (label == nameLabel) _editor.RefreshRows();
        };
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        // Type 是技能效果与文本索引：沿用当前技能的 Type，不能拿新 Id 当 Type（会指向不存在的效果并改写别的文本键）
        var skill = new SkillSettingData
        {
            Id = newId,
            Name = $"NewSkill{newId}",
            Level = 1,
            Type = _editor.Current?.Type ?? 0,
            ActivatesChance = 100
        };
        _editor.Insert(skill, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("Skill.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var s in _parser.Items)
        {
            string pfx = $"[Id={s.Id}]";
            if (!seenIds.Add(s.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (s.ActivatesChance < 0 || s.ActivatesChance > 100) problems.Add($"{pfx} {Loc.F("Skill.Validate.ChanceRange", s.ActivatesChance)}");
            if (s.IfPercent != 0 && s.IfPercent != 1) problems.Add($"{pfx} {Loc.F("Skill.Validate.IfPercentNotBool", s.IfPercent)}");
            if (s.OpenDefault != 0 && s.OpenDefault != 1) problems.Add($"{pfx} {Loc.F("Skill.Validate.OpenDefaultNotBool", s.OpenDefault)}");
            if (s.UpgradeId != 0 && _parser.GetById(s.UpgradeId) == null) problems.Add($"{pfx} {Loc.F("Skill.Validate.UpgradeTargetMissing", s.UpgradeId)}");
            if (string.IsNullOrEmpty(_parser.GetSkillName(s.Type))) problems.Add($"{pfx} {Loc.F("Skill.Validate.NameMissing", s.Type)}");
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
