using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.ArmySetting;

namespace WC4MapEditor.Views;

/// <summary>
/// 军队 Buff 编辑场景（对应 Python 工具的 ArmyBuff 编辑器，ArmyBuffSettings.json）。
/// 列表 + 属性面板完整 CRUD；描述写入 stringtable 的 army_buff_desc_{Id}。
/// </summary>
public class ArmyBuffEditScene : UserControl
{
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
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
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
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid.SetRow(topBar, 0); Grid.SetColumnSpan(topBar, 3);
        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        topPanel.Children.Add(MakeTopBtn("返回", (_, _) => _window.ReturnToMainScene()));
        topPanel.Children.Add(MakeTopBtn("新增", OnAdd));
        topPanel.Children.Add(MakeTopBtn("删除", OnDelete));
        topPanel.Children.Add(MakeTopBtn("保存", OnSave));
        topPanel.Children.Add(MakeTopBtn("重载", OnReload));
        topPanel.Children.Add(MakeTopBtn("校验", OnValidate));
        topPanel.Children.Add(new TextBlock
        {
            Text = "军队 Buff 编辑",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        });
        topBar.Child = topPanel;
        _root.Children.Add(topBar);

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _searchBox = new TextBox
        {
            Margin = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White
        };
        _searchBox.Text = "搜索 (Id / 名称 / Type)...";
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text.StartsWith("搜索")) _searchBox.Text = ""; };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(_searchBox.Text)) _searchBox.Text = "搜索 (Id / 名称 / Type)..."; };
        _searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(_searchBox, 0);
        leftGrid.Children.Add(_searchBox);

        _listBox = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13
        };
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        left.Child = leftGrid;
        _root.Children.Add(left);

        var splitter = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

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
            Text = $"已加载 {_parser.Items.Count} 条军队 Buff",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        status.Child = _statusText;
        _root.Children.Add(status);

        Content = _root;
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

        AddSection("文本 (stringtable)");
        AddRow("Buff描述 (army_buff_desc_{Id})", AddIniDesc());

        AddSection("基本信息");
        AddNum("Id", "Buff ID(Id)", 0, int.MaxValue);
        AddText("Name", "内部名称(Name)");
        AddNum("Type", "类型(Type)", 0, int.MaxValue);
        AddNum("Level", "等级(Level)", 0, 99);

        AddSection("数值");
        AddNum("Value", "数值1(Value)", int.MinValue, int.MaxValue);
        AddNum("Value2", "数值2(Value2)", int.MinValue, int.MaxValue);

        AddSection("开关");
        AddCheck("Debuff", "Debuff");
        AddCheck("Trigger", "Trigger");
        AddCheck("Hide", "Hide");
        AddCheck("ShowRounds", "ShowRounds");

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
            Height = 200
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
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _textBoxes[key] = tb;
        AddRow(label, tb);
        return tb;
    }

    private TextBox AddIniDesc()
    {
        var tb = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            CaretBrush = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 90
        };
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _iniDescBox = tb;
        return tb;
    }

    private void AddCheck(string key, string label)
    {
        var cb = new CheckBox
        {
            Content = label,
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        cb.Unchecked += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        _checkBoxes[key] = cb;
        AddRow(label, cb);
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
        SetStatus($"已加载 {_parser.Items.Count} 条军队 Buff，列表 {_listBox.Items.Count} 条");
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
        SetStatus($"已更新 Buff {_current.Id}（未保存）");
        UpdateJsonPreview();
    }

    private void SetNum(string key, int v) { if (_numBoxes.TryGetValue(key, out var b)) b.Value = v; }
    private int GetInt(string key) => _numBoxes.TryGetValue(key, out var b) ? (int)Math.Round(b.Value) : 0;

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
        var buff = new ArmyBuffSettingData { Id = newId, Name = $"NewBuff{newId}", Level = 1 };
        _parser.Items.Add(buff);
        _parser.SetBuffDesc(newId, "新Buff的描述");
        RefreshList();
        foreach (ArmyBuffListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增 Buff ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一条 Buff", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除 Buff [{_current.Id}] {_current.Name}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveBuff(id);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除 Buff ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功：\nArmyBuffSettings.json: {_parser.ConfigPath}\nstringtable: {_parser.StringTablePath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 ArmyBuffSettings.json？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus("已从磁盘重新加载");
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        foreach (var b in _parser.Items)
        {
            if (!seenIds.Add(b.Id)) problems.Add($"[Id={b.Id}] ID 重复");
            if (string.IsNullOrEmpty(b.Name)) problems.Add($"[Id={b.Id}] 名称为空");
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
        Debug.WriteLine($"[ArmyBuffEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class ArmyBuffListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Type { get; set; }
        public int Level { get; set; }
        public override string ToString() => $"[{Id}] {Name}  (Type:{Type} Lv:{Level})";
    }
}
