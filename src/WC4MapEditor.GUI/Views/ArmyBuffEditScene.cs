using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WC4MapEditor.Core.Parsers.ArmySetting;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 军队 Buff 编辑场景（对应 Python 工具的 ArmyBuff 编辑器，ArmyBuffSettings.json）。
/// 列表 + 属性面板完整 CRUD；描述写入 stringtable 的 army_buff_desc_{Id}。
/// </summary>
public class ArmyBuffEditScene : UserControl
{
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly ArmyBuffSettingParser _parser = ArmyBuffSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private ArmyBuffSettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private readonly Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _checkBoxes = new(StringComparer.Ordinal);
    private TextBox _iniDescBox = null!;
    private TextBox _jsonPreview = null!;

    public ArmyBuffEditScene(MainWindow window)
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

        var topBar = EditorTheme.TopBar(Loc.T("ArmyBuff.Title"), (_, _) => _window.ReturnToMainScene(),
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

        var search = EditorTheme.SearchBox(Loc.T("ArmyBuff.SearchPlaceholder"), out _searchBox);
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

        AddSection(Loc.T("ArmyBuff.Section.Text"));
        AddRow(Loc.T("ArmyBuff.Field.Desc"), AddIniDesc());

        AddSection(Loc.T("Common.BasicInfo"));
        AddNum("Id", Loc.T("ArmyBuff.Field.Id"), 0, int.MaxValue);
        AddText("Name", Loc.T("ArmyBuff.Field.Name"));
        AddNum("Type", Loc.T("ArmyBuff.Field.Type"), 0, int.MaxValue);
        AddNum("Level", Loc.T("ArmyBuff.Field.Level"), 0, 99);

        AddSection(Loc.T("ArmyBuff.Section.Values"));
        AddNum("Value", Loc.T("ArmyBuff.Field.Value"), int.MinValue, int.MaxValue);
        AddNum("Value2", Loc.T("ArmyBuff.Field.Value2"), int.MinValue, int.MaxValue);

        AddSection(Loc.T("ArmyBuff.Section.Flags"));
        AddCheck("Debuff", Loc.T("ArmyBuff.Field.Debuff"));
        AddCheck("Trigger", Loc.T("ArmyBuff.Field.Trigger"));
        AddCheck("Hide", Loc.T("ArmyBuff.Field.Hide"));
        AddCheck("ShowRounds", Loc.T("ArmyBuff.Field.ShowRounds"));

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
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
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

    private TextBox AddIniDesc()
    {
        var tb = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top,
            Height = 90
        };
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _iniDescBox = tb;
        return tb;
    }

    private void AddCheck(string key, string label)
    {
        var cb = new CheckBox { Content = label, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        cb.Unchecked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _checkBoxes[key] = cb;
        AddRow(label, cb);
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(b =>
        {
            if (!searching) return true;
            if (b.Id.ToString().Contains(q)) return true;
            if ((b.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (b.Type.ToString().Contains(q)) return true;
            return false;
        });

        int? keepId = _current?.Id;
        _listBox.Items.Clear();
        foreach (var b in items)
        {
            var entry = new ArmyBuffListEntry { Id = b.Id, Name = b.Name ?? "", Type = b.Type, Level = b.Level };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && b.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus(Loc.F("ArmyBuff.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not ArmyBuffListEntry entry) { ShowEmpty(); return; }
        _current = _parser.GetById(entry.Id);
        if (_current == null) { ShowEmpty(); return; }
        LoadUiFromCurrent();
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var tb in _textBoxes.Values) tb.Text = "";
        foreach (var nb in _numBoxes.Values) nb.Value = nb.MinValue;
        foreach (var cb in _checkBoxes.Values) cb.IsChecked = false;
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
        SetNum("Type", _current.Type);
        SetNum("Level", _current.Level);
        SetNum("Value", _current.Value);
        SetNum("Value2", _current.Value2);
        _textBoxes["Name"].Text = _current.Name ?? "";
        _checkBoxes["Debuff"].IsChecked = _current.Debuff;
        _checkBoxes["Trigger"].IsChecked = _current.Trigger;
        _checkBoxes["Hide"].IsChecked = _current.Hide;
        _checkBoxes["ShowRounds"].IsChecked = _current.ShowRounds;
        _iniDescBox.Text = _parser.GetBuffDesc(_current.Id);
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyProp()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.Type = GetInt("Type");
        _current.Level = GetInt("Level");
        _current.Value = GetInt("Value");
        _current.Value2 = GetInt("Value2");
        _current.Name = _textBoxes["Name"].Text;
        _current.Debuff = _checkBoxes["Debuff"].IsChecked == true;
        _current.Trigger = _checkBoxes["Trigger"].IsChecked == true;
        _current.Hide = _checkBoxes["Hide"].IsChecked == true;
        _current.ShowRounds = _checkBoxes["ShowRounds"].IsChecked == true;
        _parser.SetBuffDesc(_current.Id, _iniDescBox.Text);

        if (_listBox.SelectedItem is ArmyBuffListEntry le && le.Id == _current.Id)
        {
            var idx = _listBox.SelectedIndex;
            _listBox.Items[idx] = new ArmyBuffListEntry { Id = _current.Id, Name = _current.Name, Type = _current.Type, Level = _current.Level };
            _listBox.SelectedIndex = idx;
        }
        SetStatus(Loc.F("ArmyBuff.Updated", _current.Id));
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
        var buff = new ArmyBuffSettingData { Id = newId, Name = $"NewBuff{newId}", Level = 1 };
        _parser.Items.Add(buff);
        _parser.SetBuffDesc(newId, "新Buff的描述");   // 写入 stringtable 的游戏文本，不属于界面语言
        RefreshList();
        foreach (ArmyBuffListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("ArmyBuff.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("ArmyBuff.DeleteConfirm", _current.Id, _current.Name), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveBuff(id);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("ArmyBuff.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus("✔ " + Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("ArmyBuff.SaveSuccessDetail", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "ArmyBuffSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        foreach (var b in _parser.Items)
        {
            string pfx = $"[Id={b.Id}]";
            if (!seenIds.Add(b.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (string.IsNullOrEmpty(b.Name)) problems.Add($"{pfx} {Loc.T("Common.NameEmpty")}");
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
        Debug.WriteLine($"[ArmyBuffEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class ArmyBuffListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Type { get; set; }
        public int Level { get; set; }
        public override string ToString() => Loc.F("ArmyBuff.ListEntry", Id, Name, Type, Level);
    }
}
