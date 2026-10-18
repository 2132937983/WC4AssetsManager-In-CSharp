using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 技能编辑场景（对应 Python 工具的 Skill 编辑器，SkillSettings.json）。
/// 列表 + 属性面板完整 CRUD；名称/描述写入 stringtable 的 skill_name_{Type} / skill_info_{Type}。
/// </summary>
public class SkillEditScene : UserControl
{
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly SkillSettingParser _parser = SkillSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private SkillSettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _iniNameBox = null!;
    private TextBox _iniDescBox = null!;
    private TextBox _jsonPreview = null!;

    public SkillEditScene(MainWindow window)
    {
        EditorTheme.Apply(this);
        _window = window;
        BuildUI();
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
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = EditorTheme.TopBar(Loc.T("Skill.Title"), (_, _) => _window.ReturnToMainScene(),
            EditorTheme.Button(Loc.T("Common.Add"), OnAdd),
            EditorTheme.DangerButton(Loc.T("Common.Delete"), OnDelete),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.Button(Loc.T("Common.Validate"), OnValidate));
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        _root.Children.Add(topBar);

        // 左栏：搜索 + 列表
        var left = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = EditorTheme.SearchBox(Loc.T("Skill.SearchPlaceholder"), out _searchBox);
        search.Margin = new Thickness(8);
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(search, 0);
        leftGrid.Children.Add(search);

        _listBox = new ListBox();
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);

        var splitter = EditorTheme.VSplitter();
        Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        // 右栏：属性面板
        BuildRightPanel();
        Grid.SetRow(_propScroll, 1); Grid.SetColumn(_propScroll, 2);
        _root.Children.Add(_propScroll);

        var status = EditorTheme.StatusBar(out _statusText);
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

        AddSection(Loc.T("Skill.Section.Text"));
        AddRow(Loc.T("Skill.Field.IniName"), AddIniText(out _iniNameBox, false));
        AddRow(Loc.T("Skill.Field.IniDesc"), AddIniText(out _iniDescBox, true));

        AddSection(Loc.T("Common.BasicInfo"));
        AddNum("Id", Loc.T("Skill.Field.Id"), 0, int.MaxValue);
        AddText("Name", Loc.T("Skill.Field.Name"));
        AddNum("Level", Loc.T("Skill.Field.Level"), 0, 99);
        AddNum("Type", Loc.T("Skill.Field.Type"), 0, int.MaxValue);
        AddNum("Series", Loc.T("Skill.Field.Series"), 0, 99);

        AddSection(Loc.T("Skill.Section.Effect"));
        AddNum("ActivatesChance", Loc.T("Skill.Field.ActivatesChance"), 0, 100);
        AddNum("IfPercent", Loc.T("Skill.Field.IfPercent"), 0, 1);
        AddNum("SkillEffect", Loc.T("Skill.Field.SkillEffect"), int.MinValue, int.MaxValue);
        AddNum("UpgradeId", Loc.T("Skill.Field.UpgradeId"), 0, int.MaxValue);
        AddNum("ArmyBuff", Loc.T("Skill.Field.ArmyBuff"), 0, int.MaxValue);
        AddNum("AurasRange", Loc.T("Skill.Field.AurasRange"), 0, 99);

        AddSection(Loc.T("Skill.Section.Cost"));
        AddNum("CostMedal", Loc.T("Skill.Field.CostMedal"), 0, int.MaxValue);
        AddNum("OpenDefault", Loc.T("Skill.Field.OpenDefault"), 0, 1);
        AddNum("NeedStageId", Loc.T("Skill.Field.NeedStageId"), 0, int.MaxValue);
        AddNum("NeedScenarioId", Loc.T("Skill.Field.NeedScenarioId"), 0, int.MaxValue);
        AddNum("Score", Loc.T("Skill.Field.Score"), int.MinValue, int.MaxValue);

        AddSection(Loc.T("Common.JsonPreview"));
        _jsonPreview = EditorTheme.MonoTextBox(200);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    // Add* 只负责把控件登记进字典并挂上变更回调，外观全部交给 EditorTheme 的隐式样式
    private void AddSection(string title) => _propPanel.Children.Add(EditorTheme.SectionHeader(title));

    private void AddRow(string label, UIElement content) => _propPanel.Children.Add(EditorTheme.FormRow(label, content, LabelWidth));

    private NumericUpDown AddNum(string key, string label, double min, double max, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, v) =>
        {
            if (_loadingUi || _current == null) return;
            if (key == "Type")
            {
                // Type 是 INI 名称/描述的索引，切换时刷新文本框到新 Type 的内容
                _loadingUi = true;
                int t = (int)v;
                _iniNameBox.Text = _parser.GetSkillName(t);
                _iniDescBox.Text = _parser.GetSkillDesc(t);
                _loadingUi = false;
            }
            ApplyProp();
        };
        _numBoxes[key] = ctrl;
        AddRow(label, ctrl);
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddIniText(out TextBox box, bool multiline)
    {
        var tb = new TextBox();
        if (multiline)
        {
            tb.TextWrapping = TextWrapping.Wrap;
            tb.AcceptsReturn = true;
            tb.VerticalContentAlignment = VerticalAlignment.Top;
            tb.Height = 70;
        }
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        box = tb;
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(s =>
        {
            if (!searching) return true;
            if (s.Id.ToString().Contains(q)) return true;
            if ((s.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (_parser.GetSkillName(s.Type).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Type.ToString().Contains(q)) return true;
            return false;
        });

        int? keepId = _current?.Id;
        _listBox.Items.Clear();
        foreach (var s in items)
        {
            var name = _parser.GetSkillName(s.Type);
            if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(s.Name) ? Loc.F("Skill.UnnamedEntry", s.Id) : s.Name;
            var entry = new SkillListEntry { Id = s.Id, Name = name, Type = s.Type, Level = s.Level };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && s.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus(Loc.F("Skill.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not SkillListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        LoadUiFromCurrent();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = nb.MinValue;
        _iniNameBox.Text = "";
        _iniDescBox.Text = "";
        _jsonPreview.Text = "";
        _loadingUi = false;
    }

    // ============================================================= 属性读写 =============================================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        SetNum("Id", _current.Id);
        SetNum("Level", _current.Level);
        SetNum("Type", _current.Type);
        SetNum("Series", _current.Series);
        SetNum("ActivatesChance", _current.ActivatesChance);
        SetNum("IfPercent", _current.IfPercent);
        SetNum("SkillEffect", _current.SkillEffect);
        SetNum("UpgradeId", _current.UpgradeId);
        SetNum("CostMedal", _current.CostMedal);
        SetNum("OpenDefault", _current.OpenDefault);
        SetNum("NeedStageId", _current.NeedStageId);
        SetNum("NeedScenarioId", _current.NeedScenarioId);
        SetNum("Score", _current.Score);
        SetNum("ArmyBuff", _current.ArmyBuff);
        SetNum("AurasRange", _current.AurasRange);
        _textBoxes["Name"].Text = _current.Name ?? "";
        _iniNameBox.Text = _parser.GetSkillName(_current.Type);
        _iniDescBox.Text = _parser.GetSkillDesc(_current.Type);
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyProp()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.Level = GetInt("Level");
        _current.Type = GetInt("Type");
        _current.Series = GetInt("Series");
        _current.ActivatesChance = GetInt("ActivatesChance");
        _current.IfPercent = GetInt("IfPercent");
        _current.SkillEffect = GetInt("SkillEffect");
        _current.UpgradeId = GetInt("UpgradeId");
        _current.CostMedal = GetInt("CostMedal");
        _current.OpenDefault = GetInt("OpenDefault");
        _current.NeedStageId = GetInt("NeedStageId");
        _current.NeedScenarioId = GetInt("NeedScenarioId");
        _current.Score = GetInt("Score");
        _current.ArmyBuff = GetInt("ArmyBuff");
        _current.AurasRange = GetInt("AurasRange");
        _current.Name = _textBoxes["Name"].Text;
        _parser.SetSkillName(_current.Type, _iniNameBox.Text);
        _parser.SetSkillDesc(_current.Type, _iniDescBox.Text);

        // 同步列表项文字
        if (_listBox.SelectedItem is SkillListEntry le && le.Id == _current.Id)
        {
            var idx = _listBox.SelectedIndex;
            _listBox.Items[idx] = new SkillListEntry
            {
                Id = _current.Id,
                Name = string.IsNullOrEmpty(_iniNameBox.Text) ? _current.Name : _iniNameBox.Text,
                Type = _current.Type,
                Level = _current.Level
            };
            _listBox.SelectedIndex = idx;
        }
        SetStatus(Loc.F("Skill.Updated", _current.Id));
        UpdateJsonPreview();
    }

    private void SetNum(string key, int v) { if (_numBoxes.TryGetValue(key, out var b)) b.Value = v; }
    private int GetInt(string key) => _numBoxes.TryGetValue(key, out var b) ? (int)Math.Round(b.Value) : 0;

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
        var skill = new SkillSettingData
        {
            Id = newId,
            Name = $"NewSkill{newId}",
            Level = 1,
            Type = newId,
            ActivatesChance = 100
        };
        _parser.Items.Add(skill);
        // 写入 stringtable 的游戏文本，不属于界面语言
        _parser.SetSkillName(newId, $"新建技能{newId}");
        _parser.SetSkillDesc(newId, "新建描述");
        RefreshList();
        foreach (SkillListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("Skill.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("Skill.DeleteConfirm", _current.Id, _parser.GetSkillName(_current.Type)), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.Items.Remove(_current);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("Skill.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus("✔ " + Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("Skill.SaveSuccessDetail", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "SkillSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void OnValidate(object sender, RoutedEventArgs e)
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
        Debug.WriteLine($"[SkillEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class SkillListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Type { get; set; }
        public int Level { get; set; }
        public override string ToString() => Loc.F("Skill.ListEntry", Id, Name, Type, Level);
    }
}
