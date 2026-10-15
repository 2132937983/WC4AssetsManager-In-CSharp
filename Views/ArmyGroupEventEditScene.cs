using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.Views;

/// <summary>
/// 集团军事件编辑场景（对应 Python 工具的 ArmyGroupEvent 编辑器）。
/// 列表 + 属性面板完整 CRUD；名称/描述写入 stringtable 的
/// armygroup_event_{Id} / armygroup_event_desc_{Id}。
/// </summary>
public class ArmyGroupEventEditScene : UserControl
{
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
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
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
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _root.Children.Add(MakeTopBar());

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _eventCombo = new ComboBox
        {
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 13
        };
        _eventCombo.SelectionChanged += (_, _) => RefreshList();
        Grid.SetRow(_eventCombo, 0);
        leftGrid.Children.Add(_eventCombo);

        _searchBox = new TextBox
        {
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White
        };
        _searchBox.Text = "搜索 (Id / 名称 / EventId)...";
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = ""; };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 名称 / EventId)..."; };
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(_searchBox, 1);
        leftGrid.Children.Add(_searchBox);

        _listBox = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13
        };
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 2);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);
        _root.Children.Add(MakeSplitter());

        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        Grid.SetRow(right, 1); Grid.SetColumn(right, 2);
        BuildRightPanel();
        right.Child = _propScroll;
        _root.Children.Add(right);

        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _statusText = new TextBlock
        {
            Text = $"已加载 {_parser.Items.Count} 条集团军事件",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        status.Child = _statusText;
        _root.Children.Add(status);

        Content = _root;
    }

    private Grid MakeTopBar()
    {
        var bar = new Grid();
        Grid.SetRow(bar, 0); Grid.SetColumnSpan(bar, 3);
        bar.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        panel.Children.Add(MakeTopBtn("返回", (_, _) => _window.ReturnToMainScene()));
        panel.Children.Add(MakeTopBtn("新增", OnAdd));
        panel.Children.Add(MakeTopBtn("删除", OnDelete));
        panel.Children.Add(MakeTopBtn("保存", OnSave));
        panel.Children.Add(MakeTopBtn("重载", OnReload));
        panel.Children.Add(MakeTopBtn("校验", OnValidate));
        Grid.SetColumn(panel, 0);
        bar.Children.Add(panel);
        return bar;
    }

    private static GridSplitter MakeSplitter()
    {
        var sp = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(sp, 1); Grid.SetColumn(sp, 1);
        return sp;
    }

    private void BuildRightPanel()
    {
        _propPanel = new StackPanel { Margin = new Thickness(0) };
        _propScroll = new ScrollViewer
        {
            Content = _propPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };

        AddSection("基本信息");
        AddNum("Id", "事件ID(Id)", 0, int.MaxValue);
        AddText("NameIni", "名称(Name / INI armygroup_event_{Id})");
        AddText("DescIni", "描述(Desc / INI armygroup_event_desc_{Id})");
        AddNum("EventId", "所属集团军事件组ID(EventId)", 0, int.MaxValue);

        AddSection("Buff 1");
        AddNum("EventBuffId1", "Buff1 ID(EventBuffId1)", 0, int.MaxValue);
        AddNum("Round1", "Buff1 持续回合(Round1)", int.MinValue, int.MaxValue);
        AddText("CountryId1", "Buff1 作用国家(CountryId1，逗号分隔)");

        AddSection("Buff 2");
        AddNum("EventBuffId2", "Buff2 ID(EventBuffId2)", 0, int.MaxValue);
        AddNum("Round2", "Buff2 持续回合(Round2)", int.MinValue, int.MaxValue);
        AddText("CountryId2", "Buff2 作用国家(CountryId2，逗号分隔)");

        AddSection("触发条件");
        AddNum("Trigger", "触发类型(Trigger)", 0, int.MaxValue);
        AddText("TriggerValue", "触发参数(TriggerValue，逗号分隔)");
        AddText("Location", "触发地点(Location，逗号分隔)");
        AddNum("Chance", "触发概率(%)(Chance)", 0, 100);

        AddSection("限制");
        AddText("PlayerCountry", "玩家国家限制(PlayerCountry，逗号分隔)");
        AddText("DisableEvents", "禁用的事件ID组(DisableEvents，逗号分隔)");

        AddSection("JSON 预览");
        _jsonPreview = new TextBox
        {
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xD6, 0x6B)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 4, 8, 12),
            Height = 220
        };
        _propPanel.Children.Add(_jsonPreview);
    }

    // ============================================================= 控件辅助 =============================================================
    private static Button MakeTopBtn(string label, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = label,
            Height = 32,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13
        };
        btn.Click += handler;
        return btn;
    }

    private void AddSection(string title)
    {
        _propPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4E)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 10, 0, 4)
        });
    }

    private void AddRow(string label, UIElement content)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(content);
        border.Child = grid;
        _propPanel.Children.Add(border);
    }

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
        var tb = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            CaretBrush = Brushes.White
        };
        tb.TextChanged += (_, _) =>
        {
            if (_loadingUi || _current == null) return;
            ApplyPropCurrent();
            if (key == "NameIni") _parser.SetEventName(_current.Id, tb.Text);
            else if (key == "DescIni") _parser.SetEventDesc(_current.Id, tb.Text);
        };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshEvents()
    {
        var sel = _eventCombo.SelectedValue;
        _eventCombo.Items.Clear();
        _eventCombo.Items.Add(new EventGroupItem { Id = -1, Name = "全部事件组" });
        foreach (var ev in _parser.Items.Select(t => t.EventId).Distinct().OrderBy(x => x))
            _eventCombo.Items.Add(new EventGroupItem { Id = ev, Name = $"事件组 {ev}" });
        _eventCombo.DisplayMemberPath = "Name";
        _eventCombo.SelectedValuePath = "Id";
        _eventCombo.SelectedValue = sel ?? -1;
        if (_eventCombo.SelectedIndex < 0) _eventCombo.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        int evFilter = _eventCombo.SelectedValue is int v ? v : -1;
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
            if (string.IsNullOrEmpty(name)) name = $"事件_{ev.Id}";
            _listBox.Items.Add(new ArmyGroupEventListEntry
            {
                Id = ev.Id,
                Name = name,
                EventId = ev.EventId
            });
        }
        SetStatus($"已加载 {_parser.Items.Count} 条集团军事件，列表 {_listBox.Items.Count} 条");
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
        catch { _jsonPreview.Text = "(序列化失败)"; }
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
        _parser.SetEventName(newId, $"新事件{newId}");
        _parser.SetEventDesc(newId, "");
        RefreshEvents();
        RefreshList();
        foreach (ArmyGroupEventListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增事件 ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一个事件", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除事件 [{_current.Id}] {_parser.GetEventName(_current.Id)}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveEvent(id);
        _current = null;
        RefreshEvents();
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除事件 ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功：\nArmyGroupEventSettings.json: {_parser.ConfigPath}\nstringtable: {_parser.StringTablePath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 ArmyGroupEventSettings.json？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshEvents();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var ev in _parser.Items)
        {
            string pfx = $"[Id={ev.Id}]";
            if (seenIds.Contains(ev.Id)) problems.Add($"{pfx} ID 重复");
            else seenIds.Add(ev.Id);
            if (ev.Chance < 0 || ev.Chance > 100) problems.Add($"{pfx} Chance={ev.Chance} 不在 0~100");
            if (ev.EventBuffId1 < 0 || ev.EventBuffId2 < 0) problems.Add($"{pfx} Buff ID 为负");
        }
        if (problems.Count == 0)
            MessageBox.Show($"校验通过，共 {_parser.Items.Count} 条，未发现明显问题。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
        else
        {
            var msg = string.Join("\n", problems.Take(80));
            if (problems.Count > 80) msg += $"\n… 共 {problems.Count} 条，仅显示前 80 条";
            MessageBox.Show(msg, $"校验发现问题 {problems.Count} 条", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SetStatus($"校验完成：{problems.Count} 个问题");
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[ArmyGroupEventEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
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
        public override string ToString() => $"[{Id}] {Name}  (事件组:{EventId})";
    }
}
