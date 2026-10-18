using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 集团军事件编辑场景（对应 Python 工具的 ArmyGroupEvent 编辑器）。
/// 列表 + 属性面板完整 CRUD；名称/描述写入 stringtable 的
/// armygroup_event_{Id} / armygroup_event_desc_{Id}。
/// </summary>
public class ArmyGroupEventEditScene : UserControl
{
    /// <summary>表单行字段名列宽：这里的标签带英文属性名，比默认的 180 宽一点。</summary>
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly ArmyGroupEventSettingParser _parser = ArmyGroupEventSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private ComboBox _eventCombo = null!;
    private TextBlock _statusText = null!;

    private ArmyGroupEventData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _jsonPreview = null!;

    public ArmyGroupEventEditScene(MainWindow window)
    {
        _window = window;
        EditorTheme.Apply(this);
        BuildUI();
        RefreshEvents();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_parser.Items.Count > 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
        }, DispatcherPriority.Loaded);
    }

    // ============================================================= 布局 =============================================================
    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = EditorTheme.TopBar(Loc.T("ArmyGroupEvent.Title"), (_, _) => _window.ReturnToMainScene(),
            EditorTheme.Button(Loc.T("Common.Add"), OnAdd),
            EditorTheme.DangerButton(Loc.T("Common.Delete"), OnDelete),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.Button(Loc.T("Common.Validate"), OnValidate));
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        _root.Children.Add(topBar);

        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _eventCombo = EditorTheme.ComboBox();
        _eventCombo.Margin = new Thickness(8, 8, 8, 0);
        _eventCombo.SelectionChanged += (_, _) => RefreshList();
        Grid.SetRow(_eventCombo, 0);
        leftGrid.Children.Add(_eventCombo);

        var search = EditorTheme.SearchBox(Loc.T("ArmyGroupEvent.SearchPlaceholder"), out _searchBox);
        search.Margin = new Thickness(8);
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(search, 1);
        leftGrid.Children.Add(search);

        _listBox = new ListBox();
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 2);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);

        var splitter = EditorTheme.VSplitter();
        Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        BuildRightPanel();
        Grid.SetRow(_propScroll, 1); Grid.SetColumn(_propScroll, 2);
        _root.Children.Add(_propScroll);

        var status = EditorTheme.StatusBar(out _statusText, Loc.T("Common.Ready"));
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _root.Children.Add(status);

        Content = _root;
    }

    private void BuildRightPanel()
    {
        _propPanel = new StackPanel();
        _propScroll = new ScrollViewer
        {
            Content = _propPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.BasicInfo")));
        AddNum("Id", Loc.T("ArmyGroupEvent.Field.Id"), 0, int.MaxValue);
        AddText("NameIni", Loc.T("ArmyGroupEvent.Field.NameIni"));
        AddText("DescIni", Loc.T("ArmyGroupEvent.Field.DescIni"));
        AddNum("EventId", Loc.T("ArmyGroupEvent.Field.EventId"), 0, int.MaxValue);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.F("ArmyGroupEvent.Section.Buff", 1)));
        AddNum("EventBuffId1", Loc.F("ArmyGroupEvent.Field.BuffId", 1), 0, int.MaxValue);
        AddNum("Round1", Loc.F("ArmyGroupEvent.Field.BuffRound", 1), int.MinValue, int.MaxValue);
        AddText("CountryId1", Loc.F("ArmyGroupEvent.Field.BuffCountry", 1));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.F("ArmyGroupEvent.Section.Buff", 2)));
        AddNum("EventBuffId2", Loc.F("ArmyGroupEvent.Field.BuffId", 2), 0, int.MaxValue);
        AddNum("Round2", Loc.F("ArmyGroupEvent.Field.BuffRound", 2), int.MinValue, int.MaxValue);
        AddText("CountryId2", Loc.F("ArmyGroupEvent.Field.BuffCountry", 2));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroupEvent.Section.Trigger")));
        AddNum("Trigger", Loc.T("ArmyGroupEvent.Field.Trigger"), 0, int.MaxValue);
        AddText("TriggerValue", Loc.T("ArmyGroupEvent.Field.TriggerValue"));
        AddText("Location", Loc.T("ArmyGroupEvent.Field.Location"));
        AddNum("Chance", Loc.T("ArmyGroupEvent.Field.Chance"), 0, 100);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ArmyGroupEvent.Section.Limits")));
        AddText("PlayerCountry", Loc.T("ArmyGroupEvent.Field.PlayerCountry"));
        AddText("DisableEvents", Loc.T("ArmyGroupEvent.Field.DisableEvents"));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.JsonPreview")));
        _jsonPreview = EditorTheme.MonoTextBox(220);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    private NumericUpDown AddNum(string key, string label, double min = int.MinValue, double max = int.MaxValue, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
        _numBoxes[key] = ctrl;
        _propPanel.Children.Add(EditorTheme.FormRow(label, ctrl, LabelWidth));
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loadingUi || _current == null) return;
            ApplyPropCurrent();
            if (key == "NameIni") _parser.SetEventName(_current.Id, tb.Text);
            else if (key == "DescIni") _parser.SetEventDesc(_current.Id, tb.Text);
        };
        _textBoxes[key] = tb;
        _propPanel.Children.Add(EditorTheme.FormRow(label, tb, LabelWidth));
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshEvents()
    {
        var sel = _eventCombo.SelectedValue;
        _eventCombo.Items.Clear();
        _eventCombo.Items.Add(new EventGroupItem { Id = -1, Name = Loc.T("ArmyGroupEvent.AllGroups") });
        foreach (var ev in _parser.Items.Select(t => t.EventId).Distinct().OrderBy(x => x))
            _eventCombo.Items.Add(new EventGroupItem { Id = ev, Name = Loc.F("ArmyGroupEvent.GroupItem", ev) });
        _eventCombo.DisplayMemberPath = "Name";
        _eventCombo.SelectedValuePath = "Id";
        _eventCombo.SelectedValue = sel ?? -1;
        if (_eventCombo.SelectedIndex < 0) _eventCombo.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        int evFilter = _eventCombo.SelectedValue is int v ? v : -1;
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(ev =>
        {
            if (evFilter >= 0 && ev.EventId != evFilter) return false;
            if (!searching) return true;
            if (ev.Id.ToString().Contains(q)) return true;
            if (ev.EventId.ToString().Contains(q)) return true;
            if (_parser.GetEventName(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }).OrderBy(ev => ev.EventId).ThenBy(ev => ev.Id);

        _listBox.Items.Clear();
        foreach (var ev in items)
        {
            var name = _parser.GetEventName(ev.Id);
            if (string.IsNullOrEmpty(name)) name = Loc.F("ArmyGroupEvent.UnnamedEvent", ev.Id);
            _listBox.Items.Add(new ArmyGroupEventListEntry
            {
                Id = ev.Id,
                Name = name,
                EventId = ev.EventId
            });
        }
        SetStatus(Loc.F("ArmyGroupEvent.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not ArmyGroupEventListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        LoadUiFromCurrent();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = 0;
        _jsonPreview.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 属性读写 =============================================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        SetNum("Id", _current.Id);
        SetNum("EventId", _current.EventId);
        SetNum("EventBuffId1", _current.EventBuffId1);
        SetNum("Round1", _current.Round1);
        SetNum("EventBuffId2", _current.EventBuffId2);
        SetNum("Round2", _current.Round2);
        SetNum("Trigger", _current.Trigger);
        SetNum("Chance", _current.Chance);
        _textBoxes["NameIni"].Text = _parser.GetEventName(_current.Id);
        _textBoxes["DescIni"].Text = _parser.GetEventDesc(_current.Id);
        _textBoxes["CountryId1"].Text = FormatList(_current.CountryId1);
        _textBoxes["CountryId2"].Text = FormatList(_current.CountryId2);
        _textBoxes["TriggerValue"].Text = FormatList(_current.TriggerValue);
        _textBoxes["Location"].Text = FormatList(_current.Location);
        _textBoxes["PlayerCountry"].Text = FormatList(_current.PlayerCountry);
        _textBoxes["DisableEvents"].Text = FormatList(_current.DisableEvents);
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyPropCurrent()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.EventId = GetInt("EventId");
        _current.EventBuffId1 = GetInt("EventBuffId1");
        _current.Round1 = GetInt("Round1");
        _current.EventBuffId2 = GetInt("EventBuffId2");
        _current.Round2 = GetInt("Round2");
        _current.Trigger = GetInt("Trigger");
        _current.Chance = GetInt("Chance");
        _current.CountryId1 = ParseIntList(_textBoxes["CountryId1"].Text);
        _current.CountryId2 = ParseIntList(_textBoxes["CountryId2"].Text);
        _current.TriggerValue = ParseIntList(_textBoxes["TriggerValue"].Text);
        _current.Location = ParseIntList(_textBoxes["Location"].Text);
        _current.PlayerCountry = ParseIntList(_textBoxes["PlayerCountry"].Text);
        _current.DisableEvents = ParseIntList(_textBoxes["DisableEvents"].Text);
        UpdateJsonPreview();
    }

    private void SetNum(string key, int v) { if (_numBoxes.TryGetValue(key, out var b)) b.Value = v; }
    private int GetInt(string key) => _numBoxes.TryGetValue(key, out var b) ? (int)Math.Round(b.Value) : 0;

    private static string FormatList<T>(List<T> list) => list == null ? "" : string.Join(", ", list);

    private static List<int> ParseIntList(string s)
    {
        var r = new List<int>();
        if (string.IsNullOrWhiteSpace(s)) return r;
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) r.Add(v);
        return r;
    }

    private void UpdateJsonPreview()
    {
        if (_current == null) { _jsonPreview.Text = ""; return; }
        try { _jsonPreview.Text = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }); }
        catch { _jsonPreview.Text = Loc.T("Common.SerializeFailed"); }
    }

    // ============================================================= 操作 =============================================================
    private void OnAdd(object sender, RoutedEventArgs e)
    {
        int newId = _parser.GetNextId();
        int group = _eventCombo.SelectedValue is int c && c >= 0 ? c : 0;
        var ev = new ArmyGroupEventData
        {
            Id = newId,
            EventId = group,
            EventBuffId1 = 0,
            Round1 = 0,
            EventBuffId2 = 0,
            Round2 = 0,
            Trigger = 1,
            Chance = 100
        };
        _parser.AddEvent(ev);
        // 默认名称会写进 stringtable，是数据不是界面文字，保持原样
        _parser.SetEventName(newId, $"新事件{newId}");
        _parser.SetEventDesc(newId, "");
        RefreshEvents();
        RefreshList();
        foreach (ArmyGroupEventListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("ArmyGroupEvent.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("ArmyGroupEvent.SelectFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("ArmyGroupEvent.DeleteConfirm", _current.Id, _parser.GetEventName(_current.Id)), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveEvent(id);
        _current = null;
        RefreshEvents();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("ArmyGroupEvent.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus(Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("ArmyGroupEvent.SaveSuccessMessage", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "ArmyGroupEventSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshEvents();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var ev in _parser.Items)
        {
            if (seenIds.Contains(ev.Id)) problems.Add(Loc.F("ArmyGroupEvent.Validate.DuplicateId", ev.Id));
            else seenIds.Add(ev.Id);
            if (ev.Chance < 0 || ev.Chance > 100) problems.Add(Loc.F("ArmyGroupEvent.Validate.ChanceRange", ev.Id, ev.Chance));
            if (ev.EventBuffId1 < 0 || ev.EventBuffId2 < 0) problems.Add(Loc.F("ArmyGroupEvent.Validate.NegativeBuff", ev.Id));
        }
        if (problems.Count == 0)
            MessageBox.Show(Loc.F("Common.ValidationPassed", _parser.Items.Count), Loc.T("Common.Validate"), MessageBoxButton.OK, MessageBoxImage.Information);
        else
        {
            var msg = string.Join("\n", problems.Take(80));
            if (problems.Count > 80) msg += "\n" + Loc.F("Common.ValidationMore", problems.Count, 80);
            MessageBox.Show(msg, Loc.F("Common.ValidationProblems", problems.Count), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SetStatus(Loc.F("Common.ValidationDone", problems.Count));
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[ArmyGroupEventEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    /// <summary>左上角事件组筛选下拉框的条目（Name 在填充时按当前语言生成）。</summary>
    private class EventGroupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class ArmyGroupEventListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int EventId { get; set; }
        public override string ToString() => Loc.F("ArmyGroupEvent.ListEntry", Id, Name, EventId);
    }
}
