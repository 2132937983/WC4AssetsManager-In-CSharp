using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.GUI.Views.Dialogs;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 兵种编辑场景（对应 Python 工具的 Unit 编辑器，三文件联动：
/// ArmySettings.json + def_armypos.xml + stringtable 的 unit_name_{Id}）。
/// def_armypos.xml 只在用户改了某个兵种的偏移时才新增一行。
/// </summary>
public class ArmyEditScene : JsonEditorScene
{
    private readonly ArmySettingParser _parser = ArmySettingParser.Instance;
    private readonly TableEditor<ArmySettingData> _editor;

    public ArmyEditScene(MainWindow window) : base(window, "Army", ArmySettingParser.Instance)
    {
        _editor = Register(new TableEditor<ArmySettingData>(this, _parser.Table, new TableEditorOptions<ArmySettingData>
        {
            Describe = u => Loc.F("Army.ListEntry", u.Id, DisplayName(u), u.Army, u.Type),
            Matches = (u, q) => u.Id.ToString().Contains(q)
                                || u.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || _parser.GetUnitName(u.Id).Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = u => u.Id,
            SetId = (u, id) => u.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (sourceId, copy) => _parser.CopyExtras(sourceId, copy.Id),
            Remove = _parser.Remove,
            DeleteNote = Loc.T("JsonEditor.Army.DeleteNote"),
            SearchPlaceholder = Loc.T("Army.SearchPlaceholder")
        }));
        BuildForm(_editor.Form);
        var top = CreateTopBar(Loc.T("Army.Title"), SceneButton(Loc.T("Army.Simulate"), OnSimulate));
        Content = CreateListFormLayout(top, _editor.ListPanel, _editor.Form.View);
        _editor.SelectFirst();
    }

    private static Button SceneButton(string text, Action action)
    {
        var b = EditorTheme.Button(text, (_, _) => action());
        b.Focusable = false;
        return b;
    }

    private string DisplayName(ArmySettingData u)
    {
        var name = _parser.GetUnitName(u.Id);
        if (!string.IsNullOrEmpty(name)) return name;
        return string.IsNullOrEmpty(u.Name) ? Loc.F("Army.UnnamedEntry", u.Id) : u.Name;
    }

    private void BuildForm(RecordForm<ArmySettingData> f)
    {
        f.Section(Loc.T("Army.Section.Text"));
        f.StringText(Loc.T("Army.Field.IniName"), u => _parser.GetUnitName(u.Id), (u, v) => _parser.SetUnitName(u.Id, v));

        // 偏移只在编辑时写入：没有偏移行的兵种显示默认值，不会因为打开就多出一行
        f.Section(Loc.T("Army.Section.Xml"));
        f.Int(Loc.T("Army.Field.XmlX"), u => _parser.GetPos(u.Id)?.PosX ?? 0, (u, v) => _parser.EnsurePos(u.Id).PosX = v);
        f.Int(Loc.T("Army.Field.XmlY"), u => _parser.GetPos(u.Id)?.PosY ?? 0, (u, v) => _parser.EnsurePos(u.Id).PosY = v);
        f.Double(Loc.T("Army.Field.XmlScale"), u => _parser.GetPos(u.Id)?.Scale ?? 1.0, (u, v) => _parser.EnsurePos(u.Id).Scale = v, 0.05);

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Army.Field.Id"), u => u.Id, (u, id) => _parser.ChangeId(u, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Text(Loc.T("Army.Field.Name"), u => u.Name, (u, v) => u.Name = v);
        f.Text(Loc.T("Army.Field.Anim"), u => u.Anim, (u, v) => u.Anim = v);
        f.Int(Loc.T("Army.Field.Army"), u => u.Army, (u, v) => u.Army = v);
        f.Int(Loc.T("Army.Field.SubType"), u => u.SubType, (u, v) => u.SubType = v);
        f.Int(Loc.T("Army.Field.Elite"), u => u.Elite, (u, v) => u.Elite = v);
        f.Int(Loc.T("Army.Field.Type"), u => u.Type, (u, v) => u.Type = v);
        f.Int(Loc.T("Army.Field.Ranking"), u => u.Ranking, (u, v) => u.Ranking = v);

        f.Section(Loc.T("Army.Section.Combat"));
        f.Int(Loc.T("Army.Field.MinAttack"), u => u.MinAttack, (u, v) => u.MinAttack = v);
        f.Int(Loc.T("Army.Field.MaxAttack"), u => u.MaxAttack, (u, v) => u.MaxAttack = v);
        f.Int(Loc.T("Army.Field.MinRange"), u => u.MinRange, (u, v) => u.MinRange = v);
        f.Int(Loc.T("Army.Field.MaxRange"), u => u.MaxRange, (u, v) => u.MaxRange = v);
        f.Int(Loc.T("Army.Field.HP"), u => u.HP, (u, v) => u.HP = v);
        f.Int(Loc.T("Army.Field.Defence"), u => u.Defence, (u, v) => u.Defence = v);
        f.Int(Loc.T("Army.Field.Mobility"), u => u.Mobility, (u, v) => u.Mobility = v);
        f.Int(Loc.T("Army.Field.MeritExp"), u => u.MeritExp, (u, v) => u.MeritExp = v);

        f.Section(Loc.T("Army.Section.Cost"));
        f.Int(Loc.T("Army.Field.CostMoney"), u => u.CostMoney, (u, v) => u.CostMoney = v);
        f.Int(Loc.T("Army.Field.CostGear"), u => u.CostGear, (u, v) => u.CostGear = v);
        f.Int(Loc.T("Army.Field.CostAtomic"), u => u.CostAtomic, (u, v) => u.CostAtomic = v);
        f.Int(Loc.T("Army.Field.CostPoints"), u => u.CostPoints, (u, v) => u.CostPoints = v);

        f.Section(Loc.T("Army.Section.Formation"));
        f.Int(Loc.T("Army.Field.Carrier"), u => u.Carrier, (u, v) => u.Carrier = v);
        f.Int(Loc.T("Army.Field.BuildTime"), u => u.BuildTime, (u, v) => u.BuildTime = v);
        f.Int(Loc.T("Army.Field.BuildCD"), u => u.BuildCD, (u, v) => u.BuildCD = v);
        f.Int(Loc.T("Army.Field.AOE1"), u => u.AOE1, (u, v) => u.AOE1 = v);
        f.Int(Loc.T("Army.Field.AOE2"), u => u.AOE2, (u, v) => u.AOE2 = v);
        f.Int(Loc.T("Army.Field.MaxElite"), u => u.MaxElite, (u, v) => u.MaxElite = v);
        f.Int(Loc.T("Army.Field.MaxFormation"), u => u.MaxFormation, (u, v) => u.MaxFormation = v);
        f.Bool(Loc.T("Army.Field.Formation"), u => u.Formation, (u, v) => u.Formation = v);

        f.Section(Loc.T("Army.Section.Lists"));
        f.IntList(Loc.T("Army.Field.Feature"), u => u.Feature, (u, v) => u.Feature = v);
        f.IntList(Loc.T("Army.Field.FeatureLevel"), u => u.FeatureLevel, (u, v) => u.FeatureLevel = v);
        f.IntList(Loc.T("Army.Field.Country"), u => u.Country, (u, v) => u.Country = v, decorate: box => ReferenceFields.Multi(box, ReferenceKinds.Country));
        f.IntList(Loc.T("Army.Field.FormationChance"), u => u.FormationChance, (u, v) => u.FormationChance = v);

        f.JsonPreview();
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnAddCommand()
    {
        Session.Flush();
        int newId = _parser.GetNextId();
        var source = _editor.Current;
        ArmySettingData unit;
        if (source != null)
        {
            // 以当前兵种为模板（字段顺序、模型不认识的字段一起继承），偏移行只在原兵种有时复制
            unit = _parser.Table.Clone(source);
            unit.Id = newId;
            _parser.CopyPos(source.Id, newId);
        }
        else
        {
            unit = new ArmySettingData { Id = newId, Name = $"NewUnit{newId}", Anim = $"NewUnit{newId}", HP = 100, Type = 1 };
        }
        _parser.SetUnitName(newId, $"新建兵种_{newId}");   // 写入 stringtable 的游戏文本，不属于界面语言
        _editor.Insert(unit, Loc.F("JsonEditor.History.Add", newId));
        SetStatus(Loc.F("Army.Added", newId));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var u in _parser.Items)
        {
            string pfx = $"[Id={u.Id}]";
            if (!seenIds.Add(u.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (u.HP <= 0) problems.Add($"{pfx} {Loc.F("Army.Validate.HpNotPositive", u.HP)}");
            if (u.MinAttack > u.MaxAttack) problems.Add($"{pfx} {Loc.F("Army.Validate.AttackOrder", u.MinAttack, u.MaxAttack)}");
            if (u.MinRange > u.MaxRange) problems.Add($"{pfx} {Loc.F("Army.Validate.RangeOrder", u.MinRange, u.MaxRange)}");
            if (u.CostMoney < 0 || u.CostGear < 0 || u.CostAtomic < 0) problems.Add($"{pfx} {Loc.T("Army.Validate.NegativeCost")}");
            if (string.IsNullOrEmpty(_parser.GetUnitName(u.Id))) problems.Add($"{pfx} {Loc.F("Army.Validate.NameMissing", u.Id)}");
        }
        ShowValidation(problems, _parser.Items.Count);
    }

    // ============================================================= 编队/等级数值模拟 =============================================================
    // 与 Python 工具 simulate_stats 完全一致的公式
    private static readonly (int addHp, int addAtk, int addDef)[] LevelData =
    {
        (0, 0, 0),      // 占位（等级从 1 开始）
        (0, 0, 0),      // 1级
        (30, 6, 2),     // 2级
        (45, 8, 4),     // 3级
        (65, 10, 6),    // 4级
        (80, 14, 8),    // 5级
        (120, 30, 14)   // 6级
    };

    private static readonly (double atkPct, double hpPct)[] FormationData =
    {
        (1, 1),         // 占位（编队从 1 开始）
        (1, 1),         // 单编队
        (1.25, 1.6),    // 两编队
        (1.5, 2.1),     // 三编队
        (1.75, 2.5)     // 四编队
    };

    /// <summary>模拟：作用于选中的兵种（可多选）或全部兵种，可撤销。</summary>
    private async void OnSimulate()
    {
        var selected = _editor.SelectedItems;
        using var dlg = new UnitStatsSimulatorDialog(DialogOwner) { HasCurrentUnit = selected.Count > 0 };
        var result = await dlg.ShowAsync();
        if (result == null) return;

        var targets = result.ApplyToAll ? _parser.Items.ToList() : selected.ToList();
        if (targets.Count == 0)
        {
            SetStatus(Loc.T("JsonEditor.Status.NothingSelected"));
            return;
        }

        int level = Math.Clamp(result.Level, 1, LevelData.Length - 1);
        int formation = Math.Clamp(result.Formation, 1, FormationData.Length - 1);
        var (addHp, addAtk, addDef) = LevelData[level];
        var (atkPct, hpPct) = FormationData[formation];
        Session.Flush();
        foreach (var u in targets)
        {
            u.HP = (int)(u.HP * hpPct + 0.5) + addHp;
            u.MinAttack = (int)(u.MinAttack * atkPct + 0.5) + addAtk;
            u.MaxAttack = (int)(u.MaxAttack * atkPct + 0.5) + addAtk;
            u.Defence += addDef;
        }
        _editor.Form.Reload();
        Session.Commit(Loc.F("JsonEditor.History.Simulate", targets.Count));
        SetStatus(Loc.F("Army.Simulated", level, formation, targets.Count));
    }
}
