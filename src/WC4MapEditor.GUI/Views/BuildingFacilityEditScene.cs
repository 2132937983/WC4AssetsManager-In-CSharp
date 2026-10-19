using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 建筑与设施编辑场景（对应 Python 工具的 BuildingFacility 编辑器，双模合一：
/// BuildingSettings.json + FacilitySettings.json，名称直接来自 JSON）。
/// 两张表共用一个编辑会话（一起撤销 / 保存），顶栏单选按钮切换当前编辑的表。
/// </summary>
public class BuildingFacilityEditScene : JsonEditorScene
{
    private const double LabelWidth = 200;

    private readonly BuildingFacilitySettingParser _parser = BuildingFacilitySettingParser.Instance;
    private readonly TableEditor<BuildingSettingData> _buildings;
    private readonly TableEditor<FacilitySettingData> _facilities;
    private readonly RadioButton _buildingRadio;
    private readonly RadioButton _facilityRadio;
    private readonly ContentControl _listHost = new();
    private readonly ContentControl _formHost = new();
    private Image _buildingPreview = null!;
    private TextBlock _buildingPreviewTip = null!;
    private bool _facilityMode;

    public BuildingFacilityEditScene(MainWindow window) : base(window, "Building", BuildingFacilitySettingParser.Instance)
    {
        _buildings = Register(new TableEditor<BuildingSettingData>(this, _parser.BuildingTable, new TableEditorOptions<BuildingSettingData>
        {
            Describe = b => $"[{b.Id}] {b.Name}",
            GetId = b => b.Id,
            SetId = (b, id) => b.Id = id,
            NextId = _ => _parser.GetNextBuildingId(),
            SearchPlaceholder = Loc.T("Building.SearchPlaceholder")
        }, LabelWidth));
        _facilities = Register(new TableEditor<FacilitySettingData>(this, _parser.FacilityTable, new TableEditorOptions<FacilitySettingData>
        {
            Describe = f => $"[{f.Id}] {f.Name}",
            GetId = f => f.Id,
            SetId = (f, id) => f.Id = id,
            NextId = _ => _parser.GetNextFacilityId(),
            SearchPlaceholder = Loc.T("Building.SearchPlaceholder")
        }, LabelWidth));
        BuildBuildingForm(_buildings.Form);
        BuildFacilityForm(_facilities.Form);

        // 建筑 / 设施 两种模式的切换放在顶栏右侧、操作按钮之前
        _buildingRadio = new RadioButton { Content = Loc.T("Building.ModeBuilding"), GroupName = "buildingMode", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        _facilityRadio = new RadioButton { Content = Loc.T("Building.ModeFacility"), GroupName = "buildingMode", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Focusable = false };
        _buildingRadio.Checked += (_, _) => SwitchMode(false);
        _facilityRadio.Checked += (_, _) => SwitchMode(true);

        Content = CreateListFormLayout(CreateTopBar(Loc.T("Building.Title"), _buildingRadio, _facilityRadio), _listHost, _formHost);
        _facilities.SelectFirst();
        _buildings.SelectFirst();
        ShowMode();
    }

    // ============================================================= 表单 =============================================================

    private void BuildBuildingForm(RecordForm<BuildingSettingData> f)
    {
        f.Section(Loc.T("Building.Section.Preview"));
        f.Row(Loc.T("Building.Field.Texture"), BuildBuildingPreview());
        f.OnLoad(RefreshBuildingPreview);

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Building.Field.BuildingId"), b => b.Id, (b, id) =>
        {
            if (!_parser.ChangeBuildingId(b, id)) return Loc.F("JsonEditor.Error.IdTaken", id);
            RefreshBuildingPreview(b);
            return null;
        });
        f.Text(Loc.T("Building.Field.Name"), b => b.Name, (b, v) => b.Name = v);
        f.Int(Loc.T("Building.Field.Type"), b => b.Type, (b, v) => b.Type = v);

        f.Section(Loc.T("Building.Section.Produce"));
        f.Int(Loc.T("Building.Field.ProduceMoney"), b => b.ProduceMoney, (b, v) => b.ProduceMoney = v);
        f.Int(Loc.T("Building.Field.ProduceGear"), b => b.ProduceGear, (b, v) => b.ProduceGear = v);
        f.Int(Loc.T("Building.Field.ProduceAtomic"), b => b.ProduceAtomic, (b, v) => b.ProduceAtomic = v);

        f.Section(Loc.T("Building.Section.Links"));
        f.Int(Loc.T("Building.Field.ArmyId"), b => b.ArmyId, (b, v) => b.ArmyId = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.Unit));
        f.Int(Loc.T("Building.Field.FacilityIdLink"), b => b.FacilityId, (b, v) => b.FacilityId = v, decorate: box =>
        {
            box.Width = 120;
            var jump = EditorTheme.Button(Loc.T("Building.JumpToFacility"), (_, _) => JumpToFacility((int)Math.Round(box.Value)));
            jump.Margin = new Thickness(8, 0, 0, 0);
            jump.Focusable = false;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(box);
            panel.Children.Add(jump);
            return panel;
        });

        f.Section(Loc.T("Building.Section.Other"));
        f.Text(Loc.T("Building.Field.ResName"), b => b.ResName, (b, v) => b.ResName = string.IsNullOrWhiteSpace(v) ? null : v);
        f.Int(Loc.T("Building.Field.StyleCount"), b => b.StyleCount, (b, v) => b.StyleCount = v);
        f.Int(Loc.T("Building.Field.MeritExp"), b => b.MeritExp, (b, v) => b.MeritExp = v);

        f.JsonPreview(height: 180);
    }

    private void BuildFacilityForm(RecordForm<FacilitySettingData> f)
    {
        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Building.Field.FacilityIdSelf"), x => x.Id, (x, id) => _parser.ChangeFacilityId(x, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Text(Loc.T("Building.Field.Name"), x => x.Name, (x, v) => x.Name = v);
        f.Int(Loc.T("Building.Field.Level"), x => x.Level, (x, v) => x.Level = v);
        f.Int(Loc.T("Building.Field.Type"), x => x.Type, (x, v) => x.Type = v);
        // Type 的含义来自 stringtable 的 facility_*（游戏里显示的设施名称），不是写死的文字
        var typeMeaning = EditorTheme.Muted("");
        typeMeaning.VerticalAlignment = VerticalAlignment.Center;
        typeMeaning.TextWrapping = TextWrapping.Wrap;
        f.Row(Loc.T("Building.Field.TypeMeaning"), typeMeaning);
        f.OnLoad(_ => typeMeaning.Text = string.Join("   ", _parser.FacilityTypeNames().Select(t => $"{t.Type}: {t.Name}")));

        f.Section(Loc.T("Building.Section.Recovery"));
        f.Int(Loc.T("Building.Field.CityRecovery"), x => x.CityRecovery, (x, v) => x.CityRecovery = v);
        f.Int(Loc.T("Building.Field.ArmyRecovery"), x => x.ArmyRecovery, (x, v) => x.ArmyRecovery = v);

        f.Section(Loc.T("Building.Section.Produce"));
        f.Int(Loc.T("Building.Field.ProduceMoney"), x => x.ProduceMoney, (x, v) => x.ProduceMoney = v);
        f.Int(Loc.T("Building.Field.ProduceGear"), x => x.ProduceGear, (x, v) => x.ProduceGear = v);
        f.Int(Loc.T("Building.Field.ProduceAtomic"), x => x.ProduceAtomic, (x, v) => x.ProduceAtomic = v);

        f.Section(Loc.T("Building.Section.Cost"));
        f.Int(Loc.T("Building.Field.CostMoney"), x => x.CostMoney, (x, v) => x.CostMoney = v);
        f.Int(Loc.T("Building.Field.CostGear"), x => x.CostGear, (x, v) => x.CostGear = v);
        f.Int(Loc.T("Building.Field.CostAtomic"), x => x.CostAtomic, (x, v) => x.CostAtomic = v);
        f.Int(Loc.T("Building.Field.Only"), x => x.Only, (x, v) => x.Only = v);

        f.Section(Loc.T("Building.Section.Unlock"));
        f.IntList(Loc.T("Building.Field.UnlockArmy"), x => x.UnlockArmy, (x, v) => x.UnlockArmy = v, decorate: box => ReferenceFields.Multi(box, ReferenceKinds.Unit));
        f.IntList(Loc.T("Building.Field.UnlockArmyType"), x => x.UnlockArmyType, (x, v) => x.UnlockArmyType = v);

        f.JsonPreview(height: 180);
    }

    private UIElement BuildBuildingPreview()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        _buildingPreview = new Image { Width = 160, Height = 160, Stretch = Stretch.Uniform };
        var card = EditorTheme.Card(_buildingPreview, new Thickness(6));
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(0);
        _buildingPreviewTip = EditorTheme.Muted("");
        _buildingPreviewTip.FontSize = 11;
        _buildingPreviewTip.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(card);
        panel.Children.Add(_buildingPreviewTip);
        return panel;
    }

    private void RefreshBuildingPreview(BuildingSettingData? building)
    {
        if (building == null)
        {
            _buildingPreview.Source = null;
            _buildingPreviewTip.Text = "";
            return;
        }
        string name = $"building_{building.Id}.png";
        var img = HdAtlasImageLoader.Load("buildings_hd", name);
        _buildingPreview.Source = img;
        _buildingPreviewTip.Text = img == null ? Loc.F("Building.TextureNotFound", name) : name;
    }

    // ============================================================= 模式 =============================================================

    private void SwitchMode(bool facility)
    {
        if (_facilityMode == facility) return;
        Session.Flush();
        _facilityMode = facility;
        ShowMode();
        SetStatus(facility ? Loc.T("Building.SwitchedToFacility") : Loc.T("Building.SwitchedToBuilding"));
    }

    private void ShowMode()
    {
        ActiveEditor = _facilityMode ? _facilities : _buildings;
        _listHost.Content = _facilityMode ? _facilities.ListPanel : _buildings.ListPanel;
        _formHost.Content = _facilityMode ? _facilities.Form.View : _buildings.Form.View;
    }

    /// <summary>跳到建筑引用的设施：切换单选按钮（触发模式切换），再选中该设施。</summary>
    private void JumpToFacility(int id)
    {
        var target = _parser.GetFacilityById(id);
        if (id <= 0 || target == null)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.F("Building.FacilityNotFound", id));
            return;
        }
        _facilityRadio.IsChecked = true;
        _facilities.Select(target);
        SetStatus(Loc.F("Building.JumpedToFacility", id));
    }

    // ============================================================= 命令 =============================================================

    protected override IReadOnlyList<string> InvalidFields() => _buildings.Form.InvalidFields.Concat(_facilities.Form.InvalidFields).ToList();

    /// <summary>新增：选中了记录时以它为模板（字段顺序与未知字段一起继承），否则用默认值。</summary>
    protected override void OnAddCommand()
    {
        Session.Flush();
        // 默认名称会写进 JSON，是数据不是界面文字，保持原样
        if (_facilityMode)
        {
            int newId = _parser.GetNextFacilityId();
            var source = _facilities.Current;
            var facility = source != null ? _parser.FacilityTable.Clone(source) : new FacilitySettingData { Name = $"NewFacility{newId}", Level = 1, Type = 1 };
            facility.Id = newId;
            _facilities.Insert(facility, Loc.F("JsonEditor.History.Add", newId));
            SetStatus(Loc.F("Building.FacilityAdded", newId));
        }
        else
        {
            int newId = _parser.GetNextBuildingId();
            var source = _buildings.Current;
            var building = source != null ? _parser.BuildingTable.Clone(source) : new BuildingSettingData { Name = $"NewBuilding{newId}", Type = 1 };
            building.Id = newId;
            _buildings.Insert(building, Loc.F("JsonEditor.History.Add", newId));
            SetStatus(Loc.F("Building.BuildingAdded", newId));
        }
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenB = new HashSet<int>();
        foreach (var b in _parser.BuildingItems)
        {
            if (!seenB.Add(b.Id)) problems.Add(Loc.F("Building.Validate.BuildingDuplicateId", b.Id));
            if (b.FacilityId != 0 && _parser.GetFacilityById(b.FacilityId) == null)
                problems.Add(Loc.F("Building.Validate.FacilityMissing", b.Id, b.FacilityId));
        }
        var seenF = new HashSet<int>();
        foreach (var f in _parser.FacilityItems)
        {
            if (!seenF.Add(f.Id)) problems.Add(Loc.F("Building.Validate.FacilityDuplicateId", f.Id));
            if (string.IsNullOrEmpty(f.Name)) problems.Add(Loc.F("Building.Validate.FacilityNameEmpty", f.Id));
        }
        if (problems.Count == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Validate"), Loc.F("Building.ValidationPassed", _parser.BuildingItems.Count, _parser.FacilityItems.Count));
            SetStatus(Loc.F("Common.ValidationDone", 0));
            return;
        }
        ShowValidation(problems, _parser.BuildingItems.Count + _parser.FacilityItems.Count);
    }
}
