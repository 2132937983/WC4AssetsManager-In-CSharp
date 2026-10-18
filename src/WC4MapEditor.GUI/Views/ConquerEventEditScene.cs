using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 征服事件编辑场景（ConquerEventSettings.json）。
/// 列表 + 属性面板完整 CRUD；名称/描述写入 stringtable 的
/// conquest_event_{Id} / conquest_event_intro_{Id}。
/// </summary>
public class ConquerEventEditScene : UserControl
{
    /// <summary>表单行字段名列宽：这里的标签带英文属性名，比默认的 180 宽一点。</summary>
    private const double LabelWidth = 200;

    private readonly MainWindow _window;
    private readonly ConquerEventSettingParser _parser = ConquerEventSettingParser.Instance;

    private Grid _root = null!;
    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private ComboBox _conquerCombo = null!;
    private TextBlock _statusText = null!;

    private ConquerEventData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;
    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private TextBox _jsonPreview = null!;

    public ConquerEventEditScene(MainWindow window)
    {
        _window = window;
        EditorTheme.Apply(this);
        BuildUI();
        Debug.WriteLine($"[ConquerEventEditScene] 构造: parser.Items.Count={_parser.Items.Count}, ConfigPath={_parser.ConfigPath}, StringTablePath={_parser.StringTablePath}");
        RefreshConquers();
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

        var topBar = EditorTheme.TopBar(Loc.T("ConquerEvent.Title"), (_, _) => _window.ReturnToMainScene(),
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

        _conquerCombo = EditorTheme.ComboBox();
        _conquerCombo.Margin = new Thickness(8, 8, 8, 0);
        _conquerCombo.SelectionChanged += (_, _) => RefreshList();
        Grid.SetRow(_conquerCombo, 0);
        leftGrid.Children.Add(_conquerCombo);

        var search = EditorTheme.SearchBox(Loc.T("ConquerEvent.SearchPlaceholder"), out _searchBox);
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
        AddNum("Id", Loc.T("ConquerEvent.Field.Id"), 0, int.MaxValue);
        AddText("NameIni", Loc.T("ConquerEvent.Field.NameIni"));
        AddText("DescIni", Loc.T("ConquerEvent.Field.DescIni"));
        AddNum("ConquerId", Loc.T("ConquerEvent.Field.ConquerId"), 0, int.MaxValue);

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.F("ConquerEvent.Section.Buff", 1)));
        AddBuffField("EventBuffId1", Loc.F("ConquerEvent.Field.BuffId", 1));
        AddNum("Round1", Loc.F("ConquerEvent.Field.BuffRound", 1), int.MinValue, int.MaxValue);
        AddCountryField("CountryId1", Loc.F("ConquerEvent.Field.BuffCountry", 1));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.F("ConquerEvent.Section.Buff", 2)));
        AddBuffField("EventBuffId2", Loc.F("ConquerEvent.Field.BuffId", 2));
        AddNum("Round2", Loc.F("ConquerEvent.Field.BuffRound", 2), int.MinValue, int.MaxValue);
        AddCountryField("CountryId2", Loc.F("ConquerEvent.Field.BuffCountry", 2));

        _propPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("ConquerEvent.Section.Trigger")));
        AddNum("Trigger", Loc.T("ConquerEvent.Field.Trigger"), 0, int.MaxValue);
        AddText("TriggerValue", Loc.T("ConquerEvent.Field.TriggerValue"));
        AddText("Location", Loc.T("ConquerEvent.Field.Location"));
        AddNum("Chance", Loc.T("ConquerEvent.Field.Chance"), 0, 100);

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

    // ============================================================= 带选择器的字段 =============================================================
    /// <summary>字段右侧的小下拉按钮：高度与输入框 / 数字框一致。</summary>
    private static Button MakePickButton()
    {
        var b = EditorTheme.Button("▼");
        b.Width = 28;
        b.Height = 28;
        b.MinHeight = 0;
        b.Padding = new Thickness(0);
        b.Margin = new Thickness(4, 0, 0, 0);
        b.FontSize = 10;
        return b;
    }

    private TextBox AddCountryField(string key, string label)
    {
        var tb = new TextBox { ToolTip = Loc.T("ConquerEvent.CountryTooltip") };
        tb.TextChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
        _textBoxes[key] = tb;

        var pickBtn = MakePickButton();
        pickBtn.Click += (_, _) => ShowCountryMultiPicker(pickBtn, tb);

        var fg = new Grid();
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(tb, 0); Grid.SetColumn(pickBtn, 1);
        fg.Children.Add(tb); fg.Children.Add(pickBtn);
        _propPanel.Children.Add(EditorTheme.FormRow(label, fg, LabelWidth));
        return tb;
    }

    private NumericUpDown AddBuffField(string key, string label)
    {
        var ctrl = new NumericUpDown { MinValue = 0, MaxValue = int.MaxValue, Increment = 1 };
        ctrl.ValueChanged += (_, _) => { if (!_loadingUi && _current != null) ApplyPropCurrent(); };
        _numBoxes[key] = ctrl;

        var pickBtn = MakePickButton();
        pickBtn.Click += (_, _) => ShowBuffPicker(pickBtn, ctrl);

        var fg = new Grid();
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(ctrl, 0); Grid.SetColumn(pickBtn, 1);
        fg.Children.Add(ctrl); fg.Children.Add(pickBtn);
        _propPanel.Children.Add(EditorTheme.FormRow(label, fg, LabelWidth));
        return ctrl;
    }

    /// <summary>
    /// 弹出面板的外框。Popup 的内容不在场景的可视树里，拿不到场景合并的隐式样式，
    /// 所以这里单独把主题字典合并进外框，里面的输入框 / 列表 / 按钮才会是深色外观。
    /// </summary>
    private static Border MakePopupBorder(UIElement content)
    {
        var border = new Border
        {
            Child = content,
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 }
        };
        EditorTheme.Apply(border);
        return border;
    }

    /// <summary>弹出国家多选菜单：搜索栏 + 多选列表，确认后回填逗号分隔 ID。</summary>
    private void ShowCountryMultiPicker(Button anchor, TextBox target)
    {
        var countries = CountrySettingParser.Instance.Countries.OrderBy(c => c.Id).ToList();
        var selectedSet = new HashSet<int>(ParseIntList(target.Text));

        var search = EditorTheme.SearchBox(Loc.T("Common.SearchPlaceholder"), out var searchBox);
        search.Margin = new Thickness(6, 6, 6, 4);

        var listBox = new ListBox
        {
            MaxHeight = 320,
            Margin = new Thickness(6, 0, 6, 4),
            SelectionMode = SelectionMode.Multiple
        };

        var okBtn = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"));
        okBtn.Width = 70;
        okBtn.Margin = new Thickness(6, 0, 6, 8);
        okBtn.HorizontalAlignment = HorizontalAlignment.Right;

        var panel = new StackPanel();
        panel.Children.Add(search);
        panel.Children.Add(listBox);
        panel.Children.Add(okBtn);

        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, Width = 300, Child = MakePopupBorder(panel)
        };

        void CollectSelected()
        {
            selectedSet.Clear();
            foreach (ListBoxItem item in listBox.Items)
                if (item.IsSelected && item.Tag is int id) selectedSet.Add(id);
        }

        void FillList(string keyword)
        {
            CollectSelected();
            listBox.Items.Clear();
            foreach (var c in countries)
            {
                string display = $"{c.Id} - {c.Name}";
                if (keyword.Length > 0 && display.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var item = new ListBoxItem { Content = display, Tag = c.Id };
                if (selectedSet.Contains(c.Id)) item.IsSelected = true;
                listBox.Items.Add(item);
            }
        }

        void Commit()
        {
            CollectSelected();
            target.Text = string.Join(", ", selectedSet.OrderBy(x => x));
            popup.IsOpen = false;
        }

        searchBox.TextChanged += (_, _) => FillList(searchBox.Text.Trim());
        okBtn.Click += (_, _) => Commit();
        listBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        };

        FillList(string.Empty);
        popup.IsOpen = true;
        searchBox.Focus();
    }

    /// <summary>弹出 Buff 单选菜单：搜索栏 + 列表（Id - 描述），确认回填到数值框。</summary>
    private void ShowBuffPicker(Button anchor, NumericUpDown target)
    {
        var bp = EventBuffSettingParser.Instance;
        var buffs = bp.Items.OrderBy(b => b.Id).ToList();

        var search = EditorTheme.SearchBox(Loc.T("Common.SearchPlaceholder"), out var searchBox);
        search.Margin = new Thickness(6, 6, 6, 4);

        var listBox = new ListBox
        {
            MaxHeight = 320,
            Margin = new Thickness(6, 0, 6, 6)
        };

        var panel = new StackPanel();
        panel.Children.Add(search);
        panel.Children.Add(listBox);

        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, Width = 320, Child = MakePopupBorder(panel)
        };

        void Commit()
        {
            if (listBox.SelectedItem == null && listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
            if (listBox.SelectedItem is ListBoxItem item && item.Tag is int id)
            {
                target.Value = id;
                popup.IsOpen = false;
            }
        }

        void FillList(string keyword)
        {
            listBox.Items.Clear();
            foreach (var b in buffs)
            {
                string desc = bp.GetBuffDesc(b.Id, b.Type);
                string display = string.IsNullOrEmpty(desc) ? $"{b.Id}" : $"{b.Id} - {desc}";
                if (keyword.Length > 0 && display.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                listBox.Items.Add(new ListBoxItem { Content = display, Tag = b.Id });
            }
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;
        }

        searchBox.TextChanged += (_, _) => FillList(searchBox.Text.Trim());
        searchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && listBox.Items.Count > 0) { listBox.Focus(); listBox.SelectedIndex = 0; e.Handled = true; }
            else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        };
        listBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        };
        listBox.MouseDoubleClick += (_, _) => Commit();

        FillList(string.Empty);
        popup.IsOpen = true;
        searchBox.Focus();
    }

    // ============================================================= 列表 =============================================================
    private void RefreshConquers()
    {
        var sel = _conquerCombo.SelectedValue;
        _conquerCombo.Items.Clear();
        _conquerCombo.Items.Add(new ConquerGroupItem { Id = -1, Name = Loc.T("ConquerEvent.AllConquests") });
        foreach (var cq in _parser.Items.Select(t => t.ConquerId).Distinct().OrderBy(x => x))
            _conquerCombo.Items.Add(new ConquerGroupItem { Id = cq, Name = Loc.F("ConquerEvent.ConquestItem", cq) });
        _conquerCombo.DisplayMemberPath = "Name";
        _conquerCombo.SelectedValuePath = "Id";
        _conquerCombo.SelectedValue = sel ?? -1;
        if (_conquerCombo.SelectedIndex < 0) _conquerCombo.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        int cqFilter = _conquerCombo.SelectedValue is int v ? v : -1;
        var q = _searchBox.Text;
        bool searching = !string.IsNullOrWhiteSpace(q);

        var items = _parser.Items.Where(ev =>
        {
            if (cqFilter >= 0 && ev.ConquerId != cqFilter) return false;
            if (!searching) return true;
            if (ev.Id.ToString().Contains(q)) return true;
            if (ev.ConquerId.ToString().Contains(q)) return true;
            if (_parser.GetEventName(ev.Id).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }).OrderBy(ev => ev.ConquerId).ThenBy(ev => ev.Id);

        _listBox.Items.Clear();
        foreach (var ev in items)
        {
            var name = _parser.GetEventName(ev.Id);
            if (string.IsNullOrEmpty(name)) name = Loc.F("ConquerEvent.UnnamedEvent", ev.Id);
            _listBox.Items.Add(new ConquerEventListEntry
            {
                Id = ev.Id,
                Name = name,
                ConquerId = ev.ConquerId
            });
        }
        SetStatus(Loc.F("ConquerEvent.Loaded", _parser.Items.Count, _listBox.Items.Count));
        Debug.WriteLine($"[ConquerEventEditScene] RefreshList: parser.Items.Count={_parser.Items.Count}, listBox.Items.Count={_listBox.Items.Count}, cqFilter={cqFilter}, searching={searching}");
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox.SelectedItem is not ConquerEventListEntry entry) { ShowEmpty(); return; }
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
        SetNum("ConquerId", _current.ConquerId);
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
        _loadingUi = false;
        UpdateJsonPreview();
    }

    private void ApplyPropCurrent()
    {
        if (_current == null) return;
        _current.Id = GetInt("Id");
        _current.ConquerId = GetInt("ConquerId");
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
        int cq = _conquerCombo.SelectedValue is int c && c >= 0 ? c : 0;
        var ev = new ConquerEventData
        {
            Id = newId,
            ConquerId = cq,
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
        RefreshConquers();
        RefreshList();
        foreach (ConquerEventListEntry item in _listBox.Items)
            if (item.Id == newId) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("ConquerEvent.Added", newId));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("ConquerEvent.SelectFirst"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("ConquerEvent.DeleteConfirm", _current.Id, _parser.GetEventName(_current.Id)), Loc.T("Common.Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        _parser.RemoveEvent(id);
        _current = null;
        RefreshConquers();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.F("ConquerEvent.Deleted", id));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_parser.SaveAll())
        {
            SetStatus(Loc.F("Common.SavedTo", _parser.ConfigPath));
            MessageBox.Show(Loc.F("ConquerEvent.SaveSuccessMessage", _parser.ConfigPath, _parser.StringTablePath), Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "ConquerEventSettings.json"), Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshConquers();
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
            if (seenIds.Contains(ev.Id)) problems.Add(Loc.F("ConquerEvent.Validate.DuplicateId", ev.Id));
            else seenIds.Add(ev.Id);
            if (ev.Chance < 0 || ev.Chance > 100) problems.Add(Loc.F("ConquerEvent.Validate.ChanceRange", ev.Id, ev.Chance));
            if (ev.EventBuffId1 < 0 || ev.EventBuffId2 < 0) problems.Add(Loc.F("ConquerEvent.Validate.NegativeBuff", ev.Id));
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
        Debug.WriteLine($"[ConquerEventEditScene] {s}");
    }

    // ============================================================= 列表项类型 =============================================================
    /// <summary>左上角征服筛选下拉框的条目（Name 在填充时按当前语言生成）。</summary>
    private class ConquerGroupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class ConquerEventListEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int ConquerId { get; set; }
        public override string ToString() => Loc.F("ConquerEvent.ListEntry", Id, Name, ConquerId);
    }
}
