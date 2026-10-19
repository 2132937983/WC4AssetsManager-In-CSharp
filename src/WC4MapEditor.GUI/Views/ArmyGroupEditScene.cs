using WC4MapEditor.Core.Parsers.ArmyGroupSetting;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 集团军与援军编辑场景（对应 Python 工具的 ArmyGroup 编辑器，五表联动防闪退：
/// ArmyGroupSettings + ArmyGroupReinforcementSettings + EventSettings +
/// EventStageSettings + EventCalendarSettings，活动介绍写入 stringtable 的 event_{EventId}）。
/// </summary>
public class ArmyGroupEditScene : JsonEditorScene
{
    private const double LabelWidth = 200;

    private readonly ArmyGroupSettingParser _parser = ArmyGroupSettingParser.Instance;
    private readonly TableEditor<ArmyGroupSettingData> _editor;
    private readonly RecordForm<EventSettingData> _eventForm = new(LabelWidth);
    private readonly RecordForm<EventStageSettingData> _stageForm = new(LabelWidth);
    private readonly RecordForm<EventCalendarSettingData> _calendarForm = new(LabelWidth);
    private readonly StackPanel _rePanel = new() { Margin = new Thickness(6, 0, 6, 4) };
    private readonly TextBlock _reWarn = EditorTheme.Muted("");
    private readonly TextBlock _activityHint = EditorTheme.Muted("");

    public ArmyGroupEditScene(MainWindow window) : base(window, "ArmyGroup", ArmyGroupSettingParser.Instance)
    {
        _editor = Register(new TableEditor<ArmyGroupSettingData>(this, _parser.GroupTable, new TableEditorOptions<ArmyGroupSettingData>
        {
            Describe = a => Loc.F("ArmyGroup.ListEntry", a.Id, a.ShortName, a.EventId),
            Matches = (a, q) => a.Id.ToString().Contains(q)
                                || a.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || a.EventId.ToString().Contains(q)
                                || _parser.GetEventDesc(a.EventId).Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = a => a.Id,
            SetId = (a, id) => a.Id = id,
            NextId = _ => _parser.GetNextArmyGroupId(),
            AfterPaste = (sourceId, copy) => _parser.CopyReinforcements(sourceId, copy.Id),
            Remove = _parser.RemoveGroup,
            DeleteNote = Loc.T("JsonEditor.ArmyGroup.DeleteNote"),
            SearchPlaceholder = Loc.T("JsonEditor.ArmyGroup.SearchPlaceholder")
        }, LabelWidth));
        BuildForm(_editor.Form);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("ArmyGroup.Title")), _editor.ListPanel, _editor.Form.View);
        _editor.SelectFirst();
    }

    private static Button SceneButton(string text, Action action)
    {
        var b = EditorTheme.Button(text, (_, _) => action());
        b.Focusable = false;
        return b;
    }

    // ============================================================= 表单 =============================================================

    private void BuildForm(RecordForm<ArmyGroupSettingData> f)
    {
        f.Section(Loc.T("ArmyGroup.Section.Intro"));
        f.StringText(Loc.T("ArmyGroup.Field.IntroIni"), a => _parser.GetEventDesc(a.EventId), (a, v) => _parser.SetEventDesc(a.EventId, v), multiline: true);

        f.Section(Loc.T("ArmyGroup.Section.Core"));
        f.KeyInt(Loc.T("ArmyGroup.Field.Id"), a => a.Id, (a, id) => _parser.ChangeGroupId(a, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        // 活动 Id 换了以后，活动介绍与三张底层表都改看新活动（多个集团军可能共用一个活动，文本不跟着移动）
        f.KeyInt(Loc.T("ArmyGroup.Field.EventId"), a => a.EventId, (a, v) =>
        {
            a.EventId = v;
            Dispatcher.BeginInvoke(() => f.Reload());
            return null;
        });
        f.Int(Loc.T("ArmyGroup.Field.StageId"), a => a.StageId, (a, v) => a.StageId = v);
        f.Int(Loc.T("ArmyGroup.Field.Seat"), a => a.Seat, (a, v) => a.Seat = v);
        f.Int(Loc.T("ArmyGroup.Field.Star"), a => a.Star, (a, v) => a.Star = v);
        f.Int(Loc.T("ArmyGroup.Field.Camp"), a => a.Camp, (a, v) => a.Camp = v);
        f.Int(Loc.T("ArmyGroup.Field.Difficulty"), a => a.Difficulty, (a, v) => a.Difficulty = v);
        f.Int(Loc.T("ArmyGroup.Field.MeritPoint"), a => a.MeritPoint, (a, v) => a.MeritPoint = v);
        f.Int(Loc.T("ArmyGroup.Field.TechAcquire"), a => a.TechAcquire, (a, v) => a.TechAcquire = v);
        f.Int(Loc.T("ArmyGroup.Field.CountryId"), a => a.CountryId, (a, v) => a.CountryId = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.Country));
        f.Int(Loc.T("ArmyGroup.Field.GroupId"), a => a.GroupId, (a, v) => a.GroupId = v);

        f.Section(Loc.T("ArmyGroup.Section.Prize"));
        f.Int(Loc.T("ArmyGroup.Field.PrizeGold"), a => a.PrizeGold, (a, v) => a.PrizeGold = v);
        f.Int(Loc.T("ArmyGroup.Field.PrizeIndustry"), a => a.PrizeIndustry, (a, v) => a.PrizeIndustry = v);
        f.Int(Loc.T("ArmyGroup.Field.PrizeEnergy"), a => a.PrizeEnergy, (a, v) => a.PrizeEnergy = v);
        f.Int(Loc.T("ArmyGroup.Field.PrizeTech"), a => a.PrizeTech, (a, v) => a.PrizeTech = v);

        f.Section(Loc.T("ArmyGroup.Section.Cost"));
        f.Int(Loc.T("ArmyGroup.Field.CostMoney"), a => a.CostMoney, (a, v) => a.CostMoney = v);
        f.Int(Loc.T("ArmyGroup.Field.CostGear"), a => a.CostGear, (a, v) => a.CostGear = v);
        f.Int(Loc.T("ArmyGroup.Field.CostAtomic"), a => a.CostAtomic, (a, v) => a.CostAtomic = v);
        f.Int(Loc.T("ArmyGroup.Field.Coefficient"), a => a.Coefficient, (a, v) => a.Coefficient = v);
        f.Int(Loc.T("ArmyGroup.Field.CityNum"), a => a.CityNum, (a, v) => a.CityNum = v);

        f.Section(Loc.T("ArmyGroup.Section.Text"));
        f.Text(Loc.T("ArmyGroup.Field.Photo"), a => a.Photo, (a, v) => a.Photo = v);
        f.Text(Loc.T("ArmyGroup.Field.ShortName"), a => a.ShortName, (a, v) => a.ShortName = v);

        f.Section(Loc.T("ArmyGroup.Section.Lists"));
        f.IntList(Loc.T("ArmyGroup.Field.TechCategoryIds"), a => a.TechCategoryIds, (a, v) => a.TechCategoryIds = v);
        f.IntList(Loc.T("ArmyGroup.Field.CloseTechTypes"), a => a.CloseTechTypes, (a, v) => a.CloseTechTypes = v);
        f.IntList(Loc.T("ArmyGroup.Field.CloseCardTypes"), a => a.CloseCardTypes, (a, v) => a.CloseCardTypes = v);
        f.IntList(Loc.T("ArmyGroup.Field.GeneralId"), a => a.GeneralId, (a, v) => a.GeneralId = v, decorate: box => ReferenceFields.Multi(box, ReferenceKinds.General));

        // ---- 专属援军 ----
        f.Section(Loc.T("ArmyGroup.Section.Reinforcements"));
        _reWarn.Margin = new Thickness(10, 6, 10, 4);
        _reWarn.TextWrapping = TextWrapping.Wrap;
        f.Add(_reWarn);
        f.Add(_rePanel);
        var addRe = SceneButton(Loc.T("ArmyGroup.AddReinforcement"), OnAddReinforcement);
        addRe.HorizontalAlignment = HorizontalAlignment.Left;
        addRe.Margin = new Thickness(10, 4, 0, 8);
        f.Add(addRe);
        f.OnLoad(RebuildReinforcements);

        // ---- 底层活动参数：按 EventId 找到的三条记录，各自一个子表单 ----
        f.Section(Loc.T("ArmyGroup.Section.Activity"));
        _activityHint.Foreground = EditorTheme.Warning;
        _activityHint.Margin = new Thickness(10, 6, 10, 6);
        _activityHint.TextWrapping = TextWrapping.Wrap;
        f.Add(_activityHint);
        BuildActivityForms();
        f.Add(TableHeader("EventSettings"));
        f.Add(_eventForm.Panel);
        f.Add(TableHeader("EventStageSettings"));
        f.Add(_stageForm.Panel);
        f.Add(TableHeader("EventCalendarSettings"));
        f.Add(_calendarForm.Panel);
        f.OnLoad(LoadActivity);

        f.JsonPreview(a => new
        {
            ArmyGroup = a,
            Reinforcements = _parser.GetReinforcements(a.Id),
            Event = _parser.GetEventByEventId(a.EventId),
            Stage = _parser.GetStageByEventId(a.EventId),
            Calendar = _parser.GetCalendarByEventId(a.EventId)
        });
    }

    /// <summary>底层表的小标题（分组标题条之下的二级标题；表名就是 JSON 文件名，属于技术标识，不进翻译表）。</summary>
    private static TextBlock TableHeader(string tableName)
    {
        var tb = EditorTheme.Section("■ " + tableName);
        tb.Margin = new Thickness(10, 10, 0, 2);
        return tb;
    }

    private void BuildActivityForms()
    {
        var e = _eventForm;
        e.Int("Type", x => x.Type, (x, v) => x.Type = v);
        e.Int("NormalId", x => x.NormalId, (x, v) => x.NormalId = v);
        e.Int("NeedHQLv", x => x.NeedHQLv, (x, v) => x.NeedHQLv = v);
        e.Int(Loc.F("ArmyGroup.Hint.Single", "GeneralId"), x => x.GeneralId, (x, v) => x.GeneralId = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.General));
        e.Int("DifficultStar", x => x.DifficultStar, (x, v) => x.DifficultStar = v);
        e.IntList(Loc.F("ArmyGroup.Hint.CommaList", "Points"), x => x.Points, (x, v) => x.Points = v);

        var s = _stageForm;
        s.Int("Id", x => x.Id, (x, v) => x.Id = v);
        s.Int("Type", x => x.Type, (x, v) => x.Type = v);
        s.Int("CountryId", x => x.CountryId, (x, v) => x.CountryId = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.Country));
        s.Int("UnlockStageId", x => x.UnlockStageId, (x, v) => x.UnlockStageId = v);
        s.Int("RefStageId", x => x.RefStageId, (x, v) => x.RefStageId = v);
        s.Int("Difficulty", x => x.Difficulty, (x, v) => x.Difficulty = v);
        s.Int("Seat", x => x.Seat, (x, v) => x.Seat = v);
        s.Int("GeneralID", x => x.GeneralID, (x, v) => x.GeneralID = v, decorate: box => ReferenceFields.Single(box, ReferenceKinds.General));
        s.Int("X", x => x.X, (x, v) => x.X = v);
        s.Int("Y", x => x.Y, (x, v) => x.Y = v);
        s.Int("PrizeMedals", x => x.PrizeMedals, (x, v) => x.PrizeMedals = v);
        s.Int("PrizeTicket", x => x.PrizeTicket, (x, v) => x.PrizeTicket = v);
        s.Int("EnableRoundEvent", x => x.EnableRoundEvent, (x, v) => x.EnableRoundEvent = v);
        s.Text(Loc.F("ArmyGroup.Hint.Nullable", "RoundPhoto"), x => x.RoundPhoto, (x, v) => x.RoundPhoto = string.IsNullOrWhiteSpace(v) ? null : v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeItem"), x => x.PrizeItem, (x, v) => x.PrizeItem = v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeGold"), x => x.PrizeGold, (x, v) => x.PrizeGold = v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeIndustry"), x => x.PrizeIndustry, (x, v) => x.PrizeIndustry = v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeEnergy"), x => x.PrizeEnergy, (x, v) => x.PrizeEnergy = v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeTech"), x => x.PrizeTech, (x, v) => x.PrizeTech = v);
        s.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeMerit"), x => x.PrizeMerit, (x, v) => x.PrizeMerit = v);

        var c = _calendarForm;
        c.Int("Id", x => x.Id, (x, v) => x.Id = v);
        c.Int("LastingDays", x => x.LastingDays, (x, v) => x.LastingDays = v);
        c.Int("NoticeDays", x => x.NoticeDays, (x, v) => x.NoticeDays = v);
        c.Int("PeriodDays", x => x.PeriodDays, (x, v) => x.PeriodDays = v);
        c.Int("NeedHQLv", x => x.NeedHQLv, (x, v) => x.NeedHQLv = v);
        c.Int("Version", x => x.Version, (x, v) => x.Version = v);
        c.IntList(Loc.T("ArmyGroup.Field.StartDate"), x => x.StartDate, (x, v) => x.StartDate = v);
        c.IntList(Loc.F("ArmyGroup.Hint.CommaList", "PrizeIds"), x => x.PrizeIds, (x, v) => x.PrizeIds = v);
        c.IntList(Loc.F("ArmyGroup.Hint.CommaList", "HardBuffs"), x => x.HardBuffs, (x, v) => x.HardBuffs = v);
        c.IntList(Loc.F("ArmyGroup.Hint.CommaList", "MyBuffPool"), x => x.MyBuffPool, (x, v) => x.MyBuffPool = v);
        c.IntList(Loc.F("ArmyGroup.Hint.CommaList", "HardPrizeIds"), x => x.HardPrizeIds, (x, v) => x.HardPrizeIds = v);

        _eventForm.FieldChanged += (x, label, now) => OnActivityEdited("EventSettings", x.Id, label, now);
        _stageForm.FieldChanged += (x, label, now) => OnActivityEdited("EventStageSettings", x.EventId, label, now);
        _calendarForm.FieldChanged += (x, label, now) => OnActivityEdited("EventCalendarSettings", x.EventId, label, now);
    }

    private void OnActivityEdited(string table, int eventId, string label, bool immediate)
    {
        string step = Loc.F("JsonEditor.History.EditActivity", table, eventId, label);
        if (immediate) Session.Commit(step);
        else Session.CommitLater(step);
        _editor.Form.UpdatePreview();
        SetStatus(Loc.F("JsonEditor.Status.Edited", $"{table} {eventId}", label));
    }

    private void LoadActivity(ArmyGroupSettingData? group)
    {
        var ev = group == null ? null : _parser.GetEventByEventId(group.EventId);
        var stage = group == null ? null : _parser.GetStageByEventId(group.EventId);
        var calendar = group == null ? null : _parser.GetCalendarByEventId(group.EventId);
        _eventForm.Load(ev);
        _stageForm.Load(stage);
        _calendarForm.Load(calendar);

        var missing = new List<string>();
        if (group != null && ev == null) missing.Add("EventSettings");
        if (group != null && stage == null) missing.Add("EventStageSettings");
        if (group != null && calendar == null) missing.Add("EventCalendarSettings");
        _activityHint.Text = missing.Count == 0 ? "" : Loc.F("ArmyGroup.ActivityMissing", group?.EventId, string.Join(" / ", missing));
    }

    protected override IReadOnlyList<string> InvalidFields()
        => _editor.Form.InvalidFields.Concat(_eventForm.InvalidFields).Concat(_stageForm.InvalidFields).Concat(_calendarForm.InvalidFields).ToList();

    // ============================================================= 援军子面板 =============================================================

    private void RebuildReinforcements(ArmyGroupSettingData? group)
    {
        _rePanel.Children.Clear();
        if (group == null)
        {
            _reWarn.Text = "";
            return;
        }
        var list = _parser.GetReinforcements(group.Id);
        // 没有援军会导致游戏闪退，用危险色把警告顶出来；正常情况只是普通灰字说明
        _reWarn.Foreground = list.Count == 0 ? EditorTheme.Danger : EditorTheme.TextMuted;
        _reWarn.Text = list.Count == 0 ? Loc.T("ArmyGroup.NoReinforcementWarning") : Loc.F("ArmyGroup.ReinforcementCount", list.Count);

        foreach (var r in list)
        {
            var grid = new Grid();
            for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            AddReField(grid, 0, Loc.T("ArmyGroup.Re.Id"), ReNum(r, x => x.Id, (x, v) => x.Id = v, "Id"));
            AddReField(grid, 1, "MapId", ReNum(r, x => x.MapId, (x, v) => x.MapId = v, "MapId"));
            var armyBox = ReNum(r, x => x.ArmyId, (x, v) => x.ArmyId = v, "ArmyId");
            var armyField = new StackPanel { Orientation = Orientation.Horizontal };
            armyField.Children.Add(armyBox);
            armyField.Children.Add(ReferenceFields.PickButton(armyBox, ReferenceKinds.Unit));
            AddReField(grid, 2, Loc.T("ArmyGroup.Re.ArmyId"), armyField);
            AddReField(grid, 3, Loc.T("ArmyGroup.Re.CostPoint"), ReNum(r, x => x.CostPoint, (x, v) => x.CostPoint = v, "CostPoint"));
            AddReField(grid, 4, Loc.T("Common.Count"), ReNum(r, x => x.Num, (x, v) => x.Num = v, "Num"));

            // 改 ArmyGroupId = 把这队援军移到别的集团军：失去焦点 / 回车时才生效，避免键入中途就从列表里消失
            var tail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var agLabel = EditorTheme.Muted(Loc.T("ArmyGroup.Re.ArmyGroupId") + ":");
            agLabel.FontSize = 11;
            agLabel.VerticalAlignment = VerticalAlignment.Center;
            agLabel.Margin = new Thickness(0, 0, 3, 0);
            tail.Children.Add(agLabel);
            var boxAg = ReNum(r, x => x.ArmyGroupId, (x, v) => x.ArmyGroupId = v, "ArmyGroupId", deferred: true);
            tail.Children.Add(boxAg);

            var del = EditorTheme.DangerButton(Loc.T("Common.Delete"));
            del.Margin = new Thickness(8, 0, 0, 0);
            del.Padding = new Thickness(8, 2, 8, 2);
            del.MinHeight = 0;
            del.FontSize = 11;
            del.Focusable = false;
            del.Click += (_, _) =>
            {
                Session.Flush();
                _parser.Reinforcements.Remove(r);
                RefreshReinforcements();
                Session.Commit(Loc.F("JsonEditor.History.RemoveReinforcement", r.Id));
                SetStatus(Loc.F("ArmyGroup.ReinforcementDeleted", r.Id));
            };
            tail.Children.Add(del);
            Grid.SetColumn(tail, 5);
            grid.Children.Add(tail);

            // 兵种名称写在这一行下面（输入框太窄，放不下名字）
            var armyName = ReferenceFields.NameLabel(armyBox, ReferenceKinds.Unit);
            armyName.Margin = new Thickness(8, 4, 0, 0);
            var content = new StackPanel();
            content.Children.Add(grid);
            content.Children.Add(armyName);
            var row = EditorTheme.Card(content, new Thickness(6));
            row.Margin = new Thickness(0, 3, 0, 3);
            _rePanel.Children.Add(row);
        }
    }

    private void RefreshReinforcements()
    {
        RebuildReinforcements(_editor.Current);
        _editor.Form.UpdatePreview();
    }

    /// <summary>援军行里的数字框：直接改援军记录并记一步历史；<paramref name="deferred"/> 时失去焦点才提交。</summary>
    private NumericUpDown ReNum(ArmyGroupReinforcementData r, Func<ArmyGroupReinforcementData, int> get, Action<ArmyGroupReinforcementData, int> set, string field, bool deferred = false)
    {
        var box = new NumericUpDown { MinValue = int.MinValue, MaxValue = int.MaxValue, Increment = 1, Width = 70, Height = 24, Value = get(r) };
        void Commit()
        {
            int value = (int)Math.Round(box.Value);
            if (get(r) == value) return;
            set(r, value);
            string step = Loc.F("JsonEditor.History.EditReinforcement", r.Id, field);
            if (deferred)
            {
                Session.Commit(step);
                Dispatcher.BeginInvoke(RefreshReinforcements);
            }
            else
            {
                Session.CommitLater(step);
                _editor.Form.UpdatePreview();
            }
        }
        if (deferred)
        {
            box.ValueChanged += (_, _) => { if (!box.IsKeyboardFocusWithin) Commit(); };
            box.IsKeyboardFocusWithinChanged += (_, e) => { if (!(bool)e.NewValue) Commit(); };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                Commit();
                e.Handled = true;
            };
        }
        else
        {
            box.ValueChanged += (_, _) => Commit();
        }
        return box;
    }

    private static void AddReField(Grid grid, int col, string label, UIElement field)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
        var lbl = EditorTheme.Muted(label + ":");
        lbl.FontSize = 11;
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.Margin = new Thickness(0, 0, 3, 0);
        sp.Children.Add(lbl);
        sp.Children.Add(field);
        Grid.SetColumn(sp, col);
        grid.Children.Add(sp);
    }

    private void OnAddReinforcement()
    {
        var group = _editor.Current;
        if (group == null)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("ArmyGroup.SelectFirst"));
            return;
        }
        Session.Flush();
        int newId = _parser.GetNextReinforcementId();
        var stage = _parser.GetStageByEventId(group.EventId);
        _parser.Reinforcements.Add(new ArmyGroupReinforcementData
        {
            Id = newId,
            MapId = stage?.Id ?? (group.EventId * 100 + 1),
            ArmyId = 303001,
            ArmyGroupId = group.Id,
            CostPoint = 6,
            Num = 1
        });
        RefreshReinforcements();
        Session.Commit(Loc.F("JsonEditor.History.AddReinforcement", newId));
        SetStatus(Loc.F("ArmyGroup.ReinforcementAdded", newId));
    }

    // ============================================================= 命令 =============================================================

    /// <summary>“新增”就是一键生成模板：集团军单独存在会缺活动数据与援军。</summary>
    protected override string AddButtonText => Loc.T("ArmyGroup.CreateTemplate");

    /// <summary>一键生成：集团军 + 活动 / 关卡 / 日历 + 一条保底援军 + 活动介绍。</summary>
    protected override void OnAddCommand()
    {
        Session.Flush();
        int agId = _parser.GetNextArmyGroupId();
        int eventId = _parser.GetNextEventId();
        int stageId = eventId * 100 + 1;
        int calendarId = _parser.GetNextCalendarId();
        int reId = _parser.GetNextReinforcementId();

        _parser.Events.Add(new EventSettingData { Id = eventId, Type = 8, NormalId = 0, NeedHQLv = 1, Points = new List<int>(), GeneralId = 0, DifficultStar = 0 });
        _parser.Stages.Add(new EventStageSettingData { Id = stageId, EventId = eventId, Type = 15, RoundPhoto = null });
        _parser.Calendars.Add(new EventCalendarSettingData { Id = calendarId, EventId = eventId, StartDate = new List<int> { 2025, 6, 20 }, LastingDays = 20000, NoticeDays = 3 });
        _parser.Reinforcements.Add(new ArmyGroupReinforcementData { Id = reId, MapId = stageId, ArmyId = 303001, ArmyGroupId = agId, CostPoint = 6, Num = 1 });
        // ShortName 与活动介绍都会写进 JSON / stringtable，是数据不是界面文字，保持原样
        _parser.SetEventDesc(eventId, $"自动生成_集团军_{eventId}_介绍");
        var ag = new ArmyGroupSettingData
        {
            Id = agId,
            EventId = eventId,
            StageId = stageId,
            Seat = 50,
            Star = 3,
            Camp = 1,
            Difficulty = 3,
            MeritPoint = 5,
            CountryId = 0,
            PrizeGold = 10000,
            PrizeIndustry = 1000,
            PrizeEnergy = 400,
            PrizeTech = 20,
            Photo = "conquest_1001_1",
            ShortName = $"新军_{agId}",
            CostMoney = 1000,
            CostGear = 750,
            CostAtomic = 300,
            Coefficient = 100,
            CityNum = 15
        };
        _editor.Insert(ag, Loc.F("JsonEditor.History.Template", agId));
        SetStatus(Loc.F("ArmyGroup.TemplateCreated", agId, eventId));
    }

    /// <summary>没有援军的集团军会让游戏闪退：保存前拦下。</summary>
    protected override string? CheckBeforeSave()
    {
        var missing = _parser.GetGroupsWithoutReinforcement();
        if (missing.Count == 0) return null;
        var ids = string.Join(", ", missing.Take(20));
        if (missing.Count > 20) ids += " …";
        return Loc.F("ArmyGroup.SaveBlockedMessage", missing.Count, ids);
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seen = new HashSet<int>();
        foreach (var a in _parser.Items)
        {
            if (!seen.Add(a.Id)) problems.Add(Loc.F("ArmyGroup.Validate.DuplicateId", a.Id));
            if (_parser.GetReinforcements(a.Id).Count == 0) problems.Add(Loc.F("ArmyGroup.Validate.NoReinforcement", a.Id));
            if (_parser.GetEventByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventSettings"));
            if (_parser.GetStageByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventStageSettings"));
            if (_parser.GetCalendarByEventId(a.EventId) == null) problems.Add(Loc.F("ArmyGroup.Validate.MissingTable", a.Id, a.EventId, "EventCalendarSettings"));
        }
        ShowValidation(problems, _parser.Items.Count);
    }
}
