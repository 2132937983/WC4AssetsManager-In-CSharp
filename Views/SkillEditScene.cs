using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Views;

/// <summary>
/// 技能编辑场景（对应 Python 工具的 Skill 编辑器，SkillSettings.json）。
/// 列表 + 属性面板完整 CRUD；名称/描述写入 stringtable 的 skill_name_{Type} / skill_info_{Type}。
/// </summary>
public class SkillEditScene : UserControl
{
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

        // 顶部工具栏
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
            Text = "技能编辑",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        });
        topBar.Child = topPanel;
        _root.Children.Add(topBar);

        // 左栏：搜索 + 列表
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

        // 右栏：属性面板
        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        Grid.SetRow(right, 1); Grid.SetColumn(right, 2);
        BuildRightPanel();
        right.Child = _propScroll;
        _root.Children.Add(right);

        // 底部状态栏
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _statusText = new TextBlock
        {
            Text = $"已加载 {_parser.Items.Count} 条技能",
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
        AddRow("技能名 (skill_name_{Type})", AddIniText(out _iniNameBox, false));
        AddRow("技能描述 (skill_info_{Type})", AddIniText(out _iniDescBox, true));

        AddSection("基本信息");
        AddNum("Id", "技能ID(Id)", 0, int.MaxValue);
        AddText("Name", "内部名称(Name)");
        AddNum("Level", "等级(Level)", 0, 99);
        AddNum("Type", "类型/图标(Type)", 0, int.MaxValue);
        AddNum("Series", "系列(Series)", 0, 99);

        AddSection("触发与效果");
        AddNum("ActivatesChance", "触发概率%(ActivatesChance)", 0, 100);
        AddNum("IfPercent", "是否百分比(IfPercent)", 0, 1);
        AddNum("SkillEffect", "技能效果值(SkillEffect)", int.MinValue, int.MaxValue);
        AddNum("UpgradeId", "升级指向ID(UpgradeId)", 0, int.MaxValue);
        AddNum("ArmyBuff", "战斗Buff(ArmyBuff)", 0, int.MaxValue);
        AddNum("AurasRange", "光环范围(AurasRange)", 0, 99);

        AddSection("消耗与解锁");
        AddNum("CostMedal", "消耗勋章(CostMedal)", 0, int.MaxValue);
        AddNum("OpenDefault", "默认开启(OpenDefault)", 0, 1);
        AddNum("NeedStageId", "需求关卡ID(NeedStageId)", 0, int.MaxValue);
        AddNum("NeedScenarioId", "需求战役ID(NeedScenarioId)", 0, int.MaxValue);
        AddNum("Score", "AI评分(Score)", int.MinValue, int.MaxValue);

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

    private TextBox AddIniText(out TextBox box, bool multiline)
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
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline,
            Height = multiline ? 70 : 28
        };
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyProp(); };
        box = tb;
        return tb;
    }

    // ============================================================= 列表 =============================================================
    private void RefreshList()
    {
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q) && !q.StartsWith("搜索");

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
            if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(s.Name) ? $"技能_{s.Id}" : s.Name;
            var entry = new SkillListEntry { Id = s.Id, Name = name, Type = s.Type, Level = s.Level };
            _listBox.Items.Add(entry);
            if (keepId.HasValue && s.Id == keepId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus($"已加载 {_parser.Items.Count} 条技能，列表 {_listBox.Items.Count} 条");
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
        SetStatus($"已更新技能 {_current.Id}（未保存）");
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
        var skill = new SkillSettingData
        {
            Id = newId,
            Name = $"NewSkill{newId}",
            Level = 1,
            Type = newId,
            ActivatesChance = 100
        };
        _parser.Items.Add(skill);
        _parser.SetSkillName(newId, $"新建技能{newId}");
        _parser.SetSkillDesc(newId, "新建描述");
        RefreshList();
        foreach (SkillListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus($"新增技能 ID={newId}（未保存）");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("请先在左侧选中一条技能", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show($"确定删除技能 [{_current.Id}] {_parser.GetSkillName(_current.Type)}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.Items.Remove(_current);
        _current = null;
        RefreshList();
        ShowEmpty();
        SetStatus($"已删除技能 ID={id}（未保存）");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus($"✔ 保存成功：{_parser.ConfigPath}");
            MessageBox.Show($"保存成功：\nSkillSettings.json: {_parser.ConfigPath}\nstringtable: {_parser.StringTablePath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("保存失败，请检查权限或 Debug 输出", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("重新加载磁盘上的 SkillSettings.json？未保存的更改将丢失。", "重载", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        foreach (var s in _parser.Items)
        {
            if (!seenIds.Add(s.Id)) problems.Add($"[Id={s.Id}] ID 重复");
            if (s.ActivatesChance < 0 || s.ActivatesChance > 100) problems.Add($"[Id={s.Id}] ActivatesChance={s.ActivatesChance} 不在 0~100");
            if (s.IfPercent != 0 && s.IfPercent != 1) problems.Add($"[Id={s.Id}] IfPercent={s.IfPercent} 不是 0/1");
            if (s.OpenDefault != 0 && s.OpenDefault != 1) problems.Add($"[Id={s.Id}] OpenDefault={s.OpenDefault} 不是 0/1");
            if (s.UpgradeId != 0 && _parser.GetById(s.UpgradeId) == null) problems.Add($"[Id={s.Id}] UpgradeId={s.UpgradeId} 找不到升级目标技能");
            if (string.IsNullOrEmpty(_parser.GetSkillName(s.Type))) problems.Add($"[Id={s.Id}] Type={s.Type} 缺少技能名(skill_name_{s.Type})");
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
        Debug.WriteLine($"[SkillEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    private class SkillListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Type { get; set; }
        public int Level { get; set; }
        public override string ToString() => $"[{Id}] {Name}  (Type:{Type} Lv:{Level})";
    }
}
