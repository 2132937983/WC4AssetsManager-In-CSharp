using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 事件 Buff 编辑场景（对应 Python 工具中的 EventBuff 编辑器）。
/// 列表 + 属性面板完整 CRUD；描述写入 stringtable 的 event_buff_desc_{Id}（带 Type 回退）。
/// </summary>
public class EventBuffEditScene : UserControl
{
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly EventBuffSettingParser _parser = EventBuffSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private EventBuffData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _jsonPreview = null!;

    public EventBuffEditScene(MainWindow window)
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
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = EditorTheme.TopBar(Loc.T("EventBuff.Title"), (_, _) => _window.ReturnToMainScene(),
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

        var search = EditorTheme.SearchBox(Loc.T("EventBuff.SearchPlaceholder"), out _searchBox);
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

        AddSection(Loc.T("Common.BasicInfo"));
        AddNum("Id", Loc.T("EventBuff.Field.Id"), 0, int.MaxValue);
        AddNum("Type", Loc.T("EventBuff.Field.Type"), 0, int.MaxValue);
        AddNum("Value", Loc.T("EventBuff.Field.Value"), int.MinValue, int.MaxValue);
        AddNum("Round", Loc.T("EventBuff.Field.Round"), int.MinValue, int.MaxValue);

        AddSection(Loc.T("EventBuff.Section.Links"));
        AddText("ArmyBuffs", Loc.T("EventBuff.Field.ArmyBuffs"));

        AddSection(Loc.T("EventBuff.Section.Desc"));
        AddText("DescIni", Loc.T("Common.Description"));

        AddSection(Loc.T("Common.JsonPreview"));
        _jsonPreview = EditorTheme.MonoTextBox(220);
        _jsonPreview.Margin = new Thickness(10, 8, 10, 12);
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    // Add* 只负责把控件登记进字典并挂上变更回调，外观全部交给 EditorTheme 的隐式样式
    private void AddSection(string title) => _propPanel.Children.Add(EditorTheme.SectionHeader(title));

    private void AddRow(string label, UIElement content) => _propPanel.Children.Add(EditorTheme.FormRow(label, content, LabelWidth));

    private NumericUpDown AddNum(string key, string label, double min = int.MinValue, double max = int.MaxValue, double inc = 1)
    {
        var ctrl = new NumericUpDown { MinValue = min, MaxValue = max, Increment = inc };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
        _numBoxes[key] = ctrl;
        AddRow(label, ctrl);
        return ctrl;
    }

    private TextBox AddText(string key, string label)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loadingUi || _current == null) return;
            ApplyPropCurrent();
            if (key == "DescIni") _parser.SetBuffDesc(_current.Id, tb.Text);
        };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
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
            if (b.Type.ToString().Contains(q)) return true;
            if (b.Value.ToString().Contains(q)) return true;
            return false;
        }).OrderBy(b => b.Id);

        _listBox.Items.Clear();
        foreach (var b in items)
        {
            var desc = _parser.GetBuffDesc(b.Id, b.Type);
            if (string.IsNullOrEmpty(desc)) desc = $"Buff_{b.Id}";
            _listBox.Items.Add(new EventBuffListEntry
            {
                Id = b.Id,
                Desc = desc,
                Type = b.Type
            });
        }
        SetStatus(Loc.F("EventBuff.Loaded", _parser.Items.Count, _listBox.Items.Count));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not EventBuffListEntry entry) { ShowEmpty(); return; }
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
        SetNum("Type", _current.Type);
        SetNum("Value", _current.Value);
        SetNum("Round", _current.Round);
        _textBoxes["ArmyBuffs"].Text = FormatList(_current.ArmyBuffs);
        _textBoxes["DescIni"].Text = _parser.GetBuffDesc(_current.Id, _current.Type);
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyPropCurrent()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.Type = GetInt("Type");
        _current.Value = GetInt("Value");
        _current.Round = GetInt("Round");
        _current.ArmyBuffs = ParseIntList(_textBoxes["ArmyBuffs"].Text);
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
        var buff = new EventBuffData
        {
            Id = newId,
            Type = 0,
            Value = 0,
            Round = -1
        };
        _parser.AddBuff(buff);
        _parser.SetBuffDesc(newId, $"新Buff{newId}");   // 写入 stringtable 的游戏文本，不属于界面语言
        RefreshList();
        foreach (EventBuffListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("EventBuff.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("EventBuff.DeleteConfirm", _current.Id), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveBuff(id);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("EventBuff.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus("✔ " + Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("EventBuff.SaveSuccessDetail", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "EventBuffSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            if (seenIds.Contains(b.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            else seenIds.Add(b.Id);
            if (b.Round < -1) problems.Add($"{pfx} {Loc.F("EventBuff.Validate.RoundTooSmall", b.Round)}");
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
        Debug.WriteLine($"[EventBuffEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class EventBuffListEntry
    {
        public int Id { get; set; }
        public string Desc { get; set; } = "";
        public int Type { get; set; }
        public override string ToString() => Loc.F("EventBuff.ListEntry", Id, Desc, Type);
    }
}
