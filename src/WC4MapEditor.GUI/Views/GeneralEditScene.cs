using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SkiaSharp;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

public class GeneralEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly GeneralSettingParser _parser = GeneralSettingParser.Instance;

    private Grid _root = null!;
    private Border _topBar = null!;
    private Border _leftPanel = null!;
    private Border _rightPanel = null!;
    private GridSplitter _splitter = null!;

    private ListBox _listBox = null!;
    private TextBox _searchBox = null!;
    private TextBlock _statusText = null!;

    private GeneralSettingData? _current;
    private bool _loadingUi;

    private ScrollViewer _propScroll = null!;
    private StackPanel _propPanel = null!;

    private Dictionary<string, TextBox> _textBoxes = new(StringComparer.Ordinal);
    private Dictionary<string, NumericUpDown> _numBoxes = new(StringComparer.Ordinal);
    private Image _headPreview = null!;
    private Image _photoPreview = null!;
    private Image _tacticalHeadPreview = null!;
    private TextBox _jsonPreview = null!;

    // 图片缓存，避免每次切换将领都重新解码 webp
    // 使用 LRU 机制限制缓存大小，防止内存无限增长导致 GC 卡顿
    private readonly LinkedList<string> _imageCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _imageCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxImageCacheSize = 50;

    // 按 Image 控件区分的取消令牌，避免头像和大图加载互相取消
    private readonly Dictionary<Image, CancellationTokenSource> _imageLoadCtsMap = new();

    public GeneralEditScene(MainWindow window)
    {
        EditorTheme.Apply(this);
        _window = window;
        BuildUI();
        RefreshList();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_parser.All.Count > 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
        }, DispatcherPriority.Loaded);
    }

    // ===================================== UI 构建 =====================================
    private void BuildUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 顶栏只放增删存重载；随机 / 专长模板按钮数量多，放到属性面板上方的工具条里，避免顶栏挤不下
        _topBar = EditorTheme.TopBar(Loc.T("General.Title"), OnBack,
            EditorTheme.Button(Loc.T("General.Add"), OnAdd),
            EditorTheme.DangerButton(Loc.T("General.DeleteSelected"), OnDelete),
            EditorTheme.Button(Loc.T("Common.Reload"), OnReload),
            EditorTheme.PrimaryButton(Loc.T("Common.Save"), OnSave));
        Grid.SetRow(_topBar, 0); Grid.SetColumnSpan(_topBar, 3);
        _root.Children.Add(_topBar);

        // 左栏：搜索 + 列表
        _leftPanel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        Grid.SetRow(_leftPanel, 1); Grid.SetColumn(_leftPanel, 0);

        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = EditorTheme.SearchBox(Loc.T("General.SearchPlaceholder"), out _searchBox);
        search.Margin = new Thickness(8);
        _searchBox.TextChanged += (s, e) => RefreshList();
        Grid.SetRow(search, 0);
        leftGrid.Children.Add(search);

        _listBox = new ListBox();
        _listBox.SelectionChanged += ListBox_SelectionChanged;
        _listBox.KeyDown += ListBox_KeyDown;
        Grid.SetRow(_listBox, 1);
        leftGrid.Children.Add(_listBox);

        _leftPanel.Child = leftGrid;
        _root.Children.Add(_leftPanel);

        // Splitter
        _splitter = EditorTheme.VSplitter();
        Grid.SetRow(_splitter, 1); Grid.SetColumn(_splitter, 1);
        _root.Children.Add(_splitter);

        // 右栏：属性面板
        _rightPanel = new Border { Background = EditorTheme.Bg1 };
        Grid.SetRow(_rightPanel, 1); Grid.SetColumn(_rightPanel, 2);
        BuildRightPanel();
        _root.Children.Add(_rightPanel);

        // 底部状态栏
        var status = EditorTheme.StatusBar(out _statusText, Loc.F("General.StatusLoaded", _parser.All.Count));
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 3);
        _root.Children.Add(status);

        Content = _root;
    }

    private void BuildRightPanel()
    {
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 模板工具条：随机参数 | 兵种专长
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.RandomLow"), (_, _) => ApplyRandomTemplate(1)));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.RandomMid"), (_, _) => ApplyRandomTemplate(2)));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.RandomHigh"), (_, _) => ApplyRandomTemplate(3)));
        tools.Children.Add(EditorTheme.VSeparator());
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.SpecInfantry"), (_, _) => ApplySpecialtyTemplate("infantry")));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.SpecArmor"), (_, _) => ApplySpecialtyTemplate("armor")));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.SpecArtillery"), (_, _) => ApplySpecialtyTemplate("artillery")));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.SpecNavy"), (_, _) => ApplySpecialtyTemplate("navy")));
        tools.Children.Add(EditorTheme.ToolButton(Loc.T("General.SpecAirForce"), (_, _) => ApplySpecialtyTemplate("airforce")));
        var toolBar = EditorTheme.ToolBar(tools);
        Grid.SetRow(toolBar, 0);
        rightGrid.Children.Add(toolBar);

        _propScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16)
        };
        _propPanel = new StackPanel { Orientation = Orientation.Vertical };
        _propScroll.Content = _propPanel;
        Grid.SetRow(_propScroll, 1);
        rightGrid.Children.Add(_propScroll);
        _rightPanel.Child = rightGrid;

        // 顶部预览区：头像 + 半身像 + 战术地图头像 + JSON预览（横向排列，节省垂直空间）
        var previewGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });   // 头像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });   // 半身像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });   // 战术地图头像
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });    // 间距
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // JSON预览

        // 头像 (general_circle)
        _headPreview = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform, Source = null };
        var headWrap = EditorTheme.Card(_headPreview, new Thickness(8));
        headWrap.Margin = new Thickness(0);
        headWrap.HorizontalAlignment = HorizontalAlignment.Center;
        headWrap.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(headWrap, 0);
        previewGrid.Children.Add(headWrap);

        // 半身像 (generalphoto)
        var photoPanel = new StackPanel { Orientation = Orientation.Vertical };
        _photoPreview = new Image { Stretch = Stretch.Uniform, Height = 160, Cursor = Cursors.Hand };
        _photoPreview.MouseLeftButtonDown += OnPhotoPreviewClick;
        photoPanel.Children.Add(_photoPreview);
        var photoTip = EditorTheme.Muted(Loc.T("General.ClickToEditPhoto"));
        photoTip.FontSize = 10;
        photoTip.HorizontalAlignment = HorizontalAlignment.Center;
        photoTip.Margin = new Thickness(0, 4, 0, 0);
        photoPanel.Children.Add(photoTip);
        var photoWrap = EditorTheme.Card(photoPanel, new Thickness(8));
        photoWrap.Margin = new Thickness(0);
        photoWrap.HorizontalAlignment = HorizontalAlignment.Center;
        photoWrap.VerticalAlignment = VerticalAlignment.Center;
        photoWrap.MaxHeight = 200;
        Grid.SetColumn(photoWrap, 2);
        previewGrid.Children.Add(photoWrap);

        // 战术地图头像 (head_{EName} from tacticalmap)
        _tacticalHeadPreview = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform, Source = null };
        var tacticalHeadWrap = EditorTheme.Card(_tacticalHeadPreview, new Thickness(8));
        tacticalHeadWrap.Margin = new Thickness(0);
        tacticalHeadWrap.HorizontalAlignment = HorizontalAlignment.Center;
        tacticalHeadWrap.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tacticalHeadWrap, 4);
        previewGrid.Children.Add(tacticalHeadWrap);

        // JSON 预览（只读）
        _jsonPreview = EditorTheme.MonoTextBox(180);
        _jsonPreview.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _jsonPreview.Text = "{}";
        Grid.SetColumn(_jsonPreview, 6);
        previewGrid.Children.Add(_jsonPreview);

        _propPanel.Children.Add(previewGrid);

        // ---- 基本信息 ----
        Section(Loc.T("Common.BasicInfo"));
        Row(Loc.T("Common.Id"), AddNum("Id", 1, 99999, _ => ApplyProp()));
        Row(Loc.T("General.Field.Name"), AddText("Name", _ => ApplyProp()));
        Row(Loc.T("General.Field.EName"), AddText("EName", _ => ApplyProp()));
        Row(Loc.T("General.Field.Photo"), AddText("Photo", _ => { ApplyProp(); RefreshPreviews(); }));

        Section(Loc.T("General.Section.Stats"));

        void AddStat(string key, string label, int max)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var cur = AddNum(key, 0, max, _ => ApplyProp());
            cur.Width = 100;
            var sep = EditorTheme.Text_("  /  ", 13, EditorTheme.TextMuted);
            var maxCtrl = AddNum(key + "Max", 0, 99, _ => ApplyProp());
            maxCtrl.Width = 100;
            sp.Children.Add(cur);
            sp.Children.Add(sep);
            sp.Children.Add(maxCtrl);
            Row(label, sp);
        }
        AddStat("Infantry", Loc.T("General.Stat.Infantry"), 99);
        AddStat("Armor", Loc.T("General.Stat.Armor"), 99);
        AddStat("Artillery", Loc.T("General.Stat.Artillery"), 99);
        AddStat("Navy", Loc.T("General.Stat.Navy"), 99);
        AddStat("AirForce", Loc.T("General.Stat.AirForce"), 99);
        AddStat("March", Loc.T("General.Stat.March"), 99);

        Section(Loc.T("General.Section.RankHp"));
        Row(Loc.T("General.Field.MilitaryRank"), AddNum("MilitaryRank", 0, 20, _ => ApplyProp()));
        Row(Loc.T("General.Field.Hp"), AddNum("Hp", 0, 20, _ => ApplyProp()));

        Section(Loc.T("General.Section.ShopUi"));
        Row(Loc.T("General.Field.InShop"), AddNum("InShop", 0, 1, _ => ApplyProp()));
        Row(Loc.T("General.Field.Type"), AddNum("Type", 0, 99, _ => ApplyProp()));
        Row(Loc.T("General.Field.Evaluate"), AddNum("Evaluate", 0, 99, _ => ApplyProp()));
        Row(Loc.T("General.Field.Sequence"), AddNum("Sequence", 0, 9999, _ => ApplyProp()));
        Row(Loc.T("General.Field.UnlockHQLv"), AddNum("UnlockHQLv", 0, 999, _ => ApplyProp()));
        Row(Loc.T("General.Field.CostMedal"), AddNum("CostMedal", 0, 999999, _ => ApplyProp()));
        Row(Loc.T("General.Field.CostGold"), AddNum("CostGold", 0, 99999999, _ => ApplyProp()));

        Section(Loc.T("General.Section.SkillsMedals"));
        Row(Loc.T("General.Field.Skills"), AddText("Skills", _ => ApplyProp()));
        Row(Loc.T("General.Field.Medals"), AddText("Medals", _ => ApplyProp()));
        Row(Loc.T("General.Field.SkillsMax"), AddNum("SkillsMax", 0, 99, _ => ApplyProp()));
        Row(Loc.T("General.Field.ResetSkills"), AddNum("ResetSkills", 0, 1, _ => ApplyProp()));

        Section(Loc.T("General.Section.Portrait"));
        Row(Loc.T("General.Field.PortraitPosX"), AddNum("Portrait_PosX", -999, 999, _ => ApplyPortrait()));
        Row(Loc.T("General.Field.PortraitPosY"), AddNum("Portrait_PosY", -999, 999, _ => ApplyPortrait()));
        Row(Loc.T("General.Field.PortraitScale"), AddNum("Portrait_Scale", 0.01, 10.0, 0.01, _ => ApplyPortrait()));

        var tip = EditorTheme.Muted(Loc.T("General.PortraitTip"));
        tip.FontSize = 11;
        tip.TextWrapping = TextWrapping.Wrap;
        var tipCard = EditorTheme.Card(tip);
        tipCard.Margin = new Thickness(0, 12, 0, 0);
        _propPanel.Children.Add(tipCard);
    }

    // ===================================== UI 辅助 =====================================
    private void Section(string title) => _propPanel.Children.Add(EditorTheme.SectionHeader(title));

    private void Row(string label, UIElement value) => _propPanel.Children.Add(EditorTheme.FormRow(label, value, 160));

    private TextBox AddText(string key, Action<string> changed)
    {
        var tb = new TextBox();
        tb.TextChanged += (s, e) => changed?.Invoke(tb.Text);
        _textBoxes[key] = tb;
        return tb;
    }

    private NumericUpDown AddNum(string key, double min, double max, Action<double> changed)
        => AddNum(key, min, max, 1.0, changed);

    private NumericUpDown AddNum(string key, double min, double max, double step, Action<double> changed)
    {
        var n = new NumericUpDown
        {
            MinValue = min, MaxValue = max, Increment = step
        };
        n.ValueChanged += (s, e) => changed?.Invoke(n.Value);
        _numBoxes[key] = n;
        return n;
    }

    // ===================================== 列表/选中 =====================================
    private void RefreshList()
    {
        var query = _parser.All.AsEnumerable();
        var s = _searchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(s))
        {
            query = query.Where(g =>
                g.Id.ToString().Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (g.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (g.EName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        // 保持 JSON 文件读取顺序（先读的放最上面），不做排序
        var list = query.ToList();
        var previousId = (_listBox?.SelectedItem as GeneralListEntry)?.Id;
        _listBox.Items.Clear();
        foreach (var g in list)
        {
            var entry = new GeneralListEntry(g);
            _listBox.Items.Add(entry);
            if (previousId.HasValue && g.Id == previousId.Value) _listBox.SelectedItem = entry;
        }
        SetStatus(Loc.F("General.StatusLoadedFiltered", _parser.All.Count, _listBox.Items.Count));
    }

    private sealed class GeneralListEntry
    {
        public int Id { get; }
        public string Name { get; }
        public string EName { get; }
        public GeneralListEntry(GeneralSettingData g) { Id = g.Id; Name = g.Name ?? ""; EName = g.EName ?? ""; }
        public override string ToString() => $"[{Id}] {Name}  ({EName})";
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = _listBox.SelectedItem as GeneralListEntry;
        if (entry == null) { _current = null; ShowEmpty(); return; }
        var g = _parser.GetById(entry.Id);
        if (g == null) { _current = null; ShowEmpty(); return; }
        _current = g;
        LoadUiFromCurrent();
    }

    private void ListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_listBox.Items.Count == 0) return;
        int idx = _listBox.SelectedIndex;
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift+Tab = 向上
                if (idx > 0) _listBox.SelectedIndex = idx - 1;
            }
            else
            {
                // Tab = 向下
                if (idx < _listBox.Items.Count - 1) _listBox.SelectedIndex = idx + 1;
            }
            if (_listBox.SelectedItem != null) _listBox.ScrollIntoView(_listBox.SelectedItem);
        }
    }

    private void ShowEmpty()
    {
        _loadingUi = true;
        foreach (var kv in _textBoxes) kv.Value.Text = string.Empty;
        foreach (var kv in _numBoxes) kv.Value.Value = kv.Value.MinValue;
        _headPreview.Source = null;
        _photoPreview.Source = null;
        _tacticalHeadPreview.Source = null;
        if (_jsonPreview != null) _jsonPreview.Text = "{}";
        _loadingUi = false;
    }

    // ===================================== UI ↔ 数据 =====================================
    private void LoadUiFromCurrent()
    {
        if (_current == null) return;
        _loadingUi = true;
        try
        {
            SetText("Name", _current.Name);
            SetText("EName", _current.EName);
            SetText("Photo", _current.Photo);
            SetText("Skills", _current.Skills == null ? "" : string.Join(", ", _current.Skills));
            SetText("Medals", _current.Medals == null ? "" : string.Join(", ", _current.Medals));

            SetNum("Id", _current.Id);
            SetNum("MilitaryRank", _current.MilitaryRank);
            SetNum("Hp", _current.Hp);
            SetNum("InShop", _current.InShop);
            SetNum("Type", _current.Type);
            SetNum("Evaluate", _current.Evaluate);
            SetNum("Sequence", _current.Sequence);
            SetNum("UnlockHQLv", _current.UnlockHQLv);
            SetNum("CostMedal", _current.CostMedal);
            SetNum("CostGold", _current.CostGold);
            SetNum("SkillsMax", _current.SkillsMax);
            SetNum("ResetSkills", _current.ResetSkills);

            SetNum("Infantry", _current.Infantry);
            SetNum("Armor", _current.Armor);
            SetNum("Artillery", _current.Artillery);
            SetNum("Navy", _current.Navy);
            SetNum("AirForce", _current.AirForce);
            SetNum("March", _current.March);
            SetNum("InfantryMax", _current.InfantryMax);
            SetNum("ArmorMax", _current.ArmorMax);
            SetNum("ArtilleryMax", _current.ArtilleryMax);
            SetNum("NavyMax", _current.NavyMax);
            SetNum("AirForceMax", _current.AirForceMax);
            SetNum("MarchMax", _current.MarchMax);

            // PortraitPos
            var key = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
            var p = _parser.GetPortrait(key ?? "");
            SetNum("Portrait_PosX", p?.PosX ?? -30);
            SetNum("Portrait_PosY", p?.PosY ?? 40);
            SetNum("Portrait_Scale", p?.Scale ?? 1.0);

            RefreshPreviews();
            RefreshJsonPreview();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void ApplyProp()
    {
        if (_loadingUi || _current == null) return;
        try
        {
            _current.Name = GetText("Name") ?? "";
            _current.EName = GetText("EName") ?? "";
            _current.Photo = GetText("Photo") ?? "";
            _current.Id = (int)GetNum("Id");
            _current.MilitaryRank = (int)GetNum("MilitaryRank");
            _current.Hp = (int)GetNum("Hp");
            _current.InShop = (int)GetNum("InShop");
            _current.Type = (int)GetNum("Type");
            _current.Evaluate = (int)GetNum("Evaluate");
            _current.Sequence = (int)GetNum("Sequence");
            _current.UnlockHQLv = (int)GetNum("UnlockHQLv");
            _current.CostMedal = (int)GetNum("CostMedal");
            _current.CostGold = (int)GetNum("CostGold");
            _current.SkillsMax = (int)GetNum("SkillsMax");
            _current.ResetSkills = (int)GetNum("ResetSkills");

            _current.Infantry = (int)GetNum("Infantry");
            _current.Armor = (int)GetNum("Armor");
            _current.Artillery = (int)GetNum("Artillery");
            _current.Navy = (int)GetNum("Navy");
            _current.AirForce = (int)GetNum("AirForce");
            _current.March = (int)GetNum("March");
            _current.InfantryMax = (int)GetNum("InfantryMax");
            _current.ArmorMax = (int)GetNum("ArmorMax");
            _current.ArtilleryMax = (int)GetNum("ArtilleryMax");
            _current.NavyMax = (int)GetNum("NavyMax");
            _current.AirForceMax = (int)GetNum("AirForceMax");
            _current.MarchMax = (int)GetNum("MarchMax");

            _current.Skills = ParseIntList(GetText("Skills"));
            _current.Medals = ParseIntList(GetText("Medals"));

            // 列表项文字同步
            if (_listBox.SelectedItem is GeneralListEntry le && le.Id == _current.Id)
            {
                var idx = _listBox.SelectedIndex;
                _listBox.Items[idx] = new GeneralListEntry(_current);
                _listBox.SelectedIndex = idx;
            }
            SetStatus(Loc.F("General.StatusUpdated", _current.EName));
            RefreshJsonPreview();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void ApplyPortrait()
    {
        if (_loadingUi || _current == null) return;
        var key = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
        if (string.IsNullOrEmpty(key)) return;
        var p = _parser.EnsurePortraitDefault(key);
        p.PosX = (int)GetNum("Portrait_PosX");
        p.PosY = (int)GetNum("Portrait_PosY");
        p.Scale = GetNum("Portrait_Scale");
        SetStatus(Loc.F("General.StatusPortraitUpdated", key));
    }

    private static List<int> ParseIntList(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new List<int>();
        var list = new List<int>();
        var separators = new[] { ',', ' ', ';', '\t', '\n', '\r' };
        foreach (var part in s.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int v)) list.Add(v);
        }
        return list;
    }

    private void SetText(string key, string value)
    {
        if (_textBoxes.TryGetValue(key, out var tb)) tb.Text = value ?? "";
    }
    private string? GetText(string key) => _textBoxes.TryGetValue(key, out var tb) ? tb.Text : null;
    private void SetNum(string key, double value)
    {
        if (_numBoxes.TryGetValue(key, out var n)) n.Value = Math.Clamp(value, n.MinValue, n.MaxValue);
    }
    private double GetNum(string key) => _numBoxes.TryGetValue(key, out var n) ? n.Value : 0;

    // ===================================== 预览 =====================================
    private void RefreshPreviews()
    {
        if (_current == null) { _headPreview.Source = null; _photoPreview.Source = null; _tacticalHeadPreview.Source = null; return; }
        var key = string.IsNullOrEmpty(_current.Photo) ? _current.EName : _current.Photo;
        RefreshImageAsync(_headPreview, _parser.GetHeadPath(key));
        RefreshImageAsync(_photoPreview, _parser.GetGeneralPhotoPath(key));
        RefreshTacticalHeadAsync(key);
    }

    private void RefreshTacticalHeadAsync(string ename)
    {
        if (string.IsNullOrEmpty(ename))
        {
            _tacticalHeadPreview.Source = null;
            return;
        }

        var cacheKey = $"tactical_head_{ename}";
        if (_imageCache.TryGetValue(cacheKey, out var cached))
        {
            TouchCache(cacheKey);
            _tacticalHeadPreview.Source = cached;
            return;
        }

        if (_imageLoadCtsMap.TryGetValue(_tacticalHeadPreview, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _imageLoadCtsMap[_tacticalHeadPreview] = cts;
        var token = cts.Token;
        var imageName = $"head_{ename}";

        _ = Task.Run(() =>
        {
            var parser = Core.Config.ConfigManager.Instance.TacticalMapParser;
            if (parser == null)
            {
                Debug.WriteLine($"[GeneralEditScene] TacticalMapParser 为 null，无法加载 {imageName}");
                return null;
            }
            Debug.WriteLine($"[GeneralEditScene] TacticalMapParser 已加载，ImageDefinitions 数量: {parser.ImageDefinitions.Count}");

            var imageCache = Rendering.Skia.TacticalMapImageCache.Instance;
            if (!imageCache.IsInitialized)
            {
                Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 未初始化，正在初始化...");
                imageCache.Initialize();
                if (!imageCache.IsInitialized)
                {
                    Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 初始化失败");
                    return null;
                }
                Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 初始化成功");
            }

            var skImage = imageCache.GetImage(imageName)
                         ?? imageCache.GetImage(imageName + ".png")
                         ?? imageCache.GetImage(imageName + ".webp");
            if (skImage == null)
            {
                Debug.WriteLine($"[GeneralEditScene] 未在 TacticalMapImageCache 中找到图像: {imageName} / {imageName}.png / {imageName}.webp");
                return null;
            }
            Debug.WriteLine($"[GeneralEditScene] 找到战术地图头像: {imageName} (实际名称含后缀, {skImage.Width}x{skImage.Height})");
            return SkImageToBitmapSource(skImage);
        }, token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(cacheKey, bmp);
                _tacticalHeadPreview.Source = bmp;
            }
            else
            {
                _tacticalHeadPreview.Source = null;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static BitmapSource? SkImageToBitmapSource(SKImage skImage)
    {
        try
        {
            using var bitmap = SKBitmap.FromImage(skImage);
            if (bitmap == null) return null;
            var info = bitmap.Info;
            var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                int rowBytes = info.Width * 4;
                var pixelData = bitmap.GetPixelSpan().ToArray();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, y * bitmap.RowBytes, dstPtr, rowBytes);
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    private void RefreshImageAsync(Image imageControl, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            imageControl.Source = null;
            return;
        }
        // 缓存命中直接显示，并移到 LRU 头部（最近使用）
        if (_imageCache.TryGetValue(path, out var cached))
        {
            TouchCache(path);
            imageControl.Source = cached;
            return;
        }
        // 取消该控件之前的加载任务，避免快速滚动时任务堆积
        if (_imageLoadCtsMap.TryGetValue(imageControl, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _imageLoadCtsMap[imageControl] = cts;
        var token = cts.Token;

        // 后台加载
        _ = Task.Run(() => TryLoadImage(path), token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(path, bmp);
                imageControl.Source = bmp;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AddToCache(string path, BitmapSource bmp)
    {
        if (_imageCache.ContainsKey(path))
        {
            _imageCache[path] = bmp;
            TouchCache(path);
            return;
        }
        // 淘汰最久未使用的缓存项
        while (_imageCache.Count >= MaxImageCacheSize && _imageCacheOrder.Count > 0)
        {
            var oldest = _imageCacheOrder.Last!.Value;
            _imageCacheOrder.RemoveLast();
            _imageCacheNodes.Remove(oldest);
            if (_imageCache.Remove(oldest, out var oldBmp))
            {
                // WriteableBitmap 的内存由自身管理，从字典移除后可以被 GC 回收
                oldBmp = null;
            }
        }
        _imageCache[path] = bmp;
        var node = _imageCacheOrder.AddFirst(path);
        _imageCacheNodes[path] = node;
    }

    private void TouchCache(string path)
    {
        if (_imageCacheNodes.TryGetValue(path, out var node))
        {
            _imageCacheOrder.Remove(node);
            _imageCacheOrder.AddFirst(node);
        }
    }

    private void RefreshJsonPreview()
    {
        if (_current == null || _jsonPreview == null) return;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
                PropertyNamingPolicy = null
            };
            var json = System.Text.Json.JsonSerializer.Serialize(_current, options);
            _jsonPreview.Text = json;
        }
        catch (Exception ex)
        {
            _jsonPreview.Text = Loc.F("General.SerializeFailed", ex.Message);
        }
    }

    private static BitmapSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            // WPF 默认不支持 webp，用 SkiaSharp 解码再转 WriteableBitmap
            using var skia = SKBitmap.Decode(path);
            if (skia == null) return null;
            var info = skia.Info;
            var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                int rowBytes = info.Width * 4;
                // 一次性复制像素数据到临时数组，避免每行都分配临时数组
                var pixelData = skia.GetPixelSpan().ToArray();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, y * skia.RowBytes, dstPtr, rowBytes);
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }



    // ===================================== 工具栏动作 =====================================
    private void OnBack(object sender, RoutedEventArgs e)
    {
        _window.ReturnToMainScene();
    }

    private void OnPhotoPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (_current == null) return;
        var ename = string.IsNullOrEmpty(_current.EName) ? _current.Photo : _current.EName;
        if (string.IsNullOrEmpty(ename))
        {
            MessageBox.Show(Loc.T("General.NoEnameForPhoto"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var scene = new GeneralPhotoEditScene(_window, ename);
        _window.SetCurrentScene(scene);
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        using var enameDlg = new SingleInputDialog(_window)
        {
            Title = Loc.T("General.Add"),
            Description = Loc.T("General.AddEnamePrompt"),
            DefaultValue = "",
            MinValue = 0,
            MaxValue = 0
        };
        var ename = await enameDlg.ShowAsync();
        if (string.IsNullOrWhiteSpace(ename)) return;

        if (_parser.GetByEName(ename) != null)
        {
            MessageBox.Show(Loc.F("General.EnameExists", ename), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var nameDlg = new SingleInputDialog(_window)
        {
            Title = Loc.T("General.Add"),
            Description = Loc.F("General.AddNamePrompt", ename),
            DefaultValue = ename
        };
        var cname = await nameDlg.ShowAsync() ?? ename;

        var (g, p) = _parser.AddNewGeneral(cname, ename);
        RefreshList();
        foreach (GeneralListEntry item in _listBox.Items)
            if (item.Id == g.Id) { _listBox.SelectedItem = item; break; }
        SetStatus(Loc.F("General.StatusAdded", g.Name, g.EName, g.Id, p.PosX, p.PosY, p.Scale));
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var res = MessageBox.Show(Loc.F("General.DeleteConfirm", _current.Id, _current.Name, _current.EName),
            Loc.T("General.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        int id = _current.Id;
        string ename = _current.EName;
        if (_parser.DeleteGeneral(id))
        {
            RefreshList();
            _current = null;
            ShowEmpty();
            SetStatus(Loc.F("General.StatusDeleted", id, ename));
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var ok = _parser.SaveAll();
        if (ok)
        {
            SetStatus(Loc.F("General.StatusSaved", _parser.ConfigPath, _parser.PortraitPosPath));
            MessageBox.Show(Loc.F("General.SaveSuccessMessage", _parser.ConfigPath, _parser.PortraitPosPath),
                Loc.T("Common.SaveSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.T("Common.SaveFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(Loc.F("Common.ReloadConfirm", "GeneralSettings.json + def_portraitpos.xml"),
            Loc.T("Common.Reload"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _parser.LoadAll();
        RefreshList();
        ShowEmpty();
        SetStatus(Loc.T("Common.ReloadedFromDisk"));
    }

    private void SetStatus(string s)
    {
        _statusText.Text = s;
        Debug.WriteLine($"[GeneralEditScene] {s}");
    }

    // ===================================== 随机参数模板 =====================================
    private void ApplyRandomTemplate(int templateId)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var templates = ConfigManager.Instance.GetGeneralRandomTemplates();
        var template = templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            MessageBox.Show(Loc.F("General.RandomTemplateNotFound", templateId), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var random = new Random();
        _loadingUi = true; // 批量修改期间禁止触发 ApplyProp
        try
        {
            // 基础属性
            SetField("Evaluate", template.Evaluate);
            SetField("Sequence", random.Next(template.SequenceMin, template.SequenceMax + 1));
            SetField("UnlockHQLv", template.UnlockHQLv);
            SetField("CostMedal", random.Next(template.CostMedalMin, template.CostMedalMax + 1));
            SetField("MilitaryRank", random.Next(template.MilitaryRankMin, template.MilitaryRankMax + 1));
            SetField("March", random.Next(template.MarchMin, template.MarchMax + 1));
            SetField("Hp", random.Next(template.HpMin, template.HpMax + 1));

            // 兵种属性（基于当前值除以除数）
            if (_numBoxes.TryGetValue("Infantry", out var infantryBox))
                infantryBox.Value = (int)(infantryBox.Value / template.InfantryDivisor);
            if (_numBoxes.TryGetValue("Artillery", out var artilleryBox))
                artilleryBox.Value = (int)(artilleryBox.Value / template.ArtilleryDivisor);
            if (_numBoxes.TryGetValue("Armor", out var armorBox))
                armorBox.Value = (int)(armorBox.Value / template.ArmorDivisor);
            if (_numBoxes.TryGetValue("Navy", out var navyBox))
                navyBox.Value = (int)(navyBox.Value / template.NavyDivisor);
            if (_numBoxes.TryGetValue("AirForce", out var airBox))
                airBox.Value = (int)(airBox.Value / template.AirForceDivisor);

            // 技能等级随机化
            RandomizeSkillLevels(template.SkillLevelMin, template.SkillLevelMax, template.SkillCount);
        }
        finally
        {
            _loadingUi = false;
        }

        ApplyProp();
        SetStatus(Loc.F("General.StatusRandomApplied", template.Name));
    }

    private void RandomizeSkillLevels(int minLevel, int maxLevel, int count)
    {
        var random = new Random();
        for (int i = 0; i < count; i++)
        {
            var key = $"Skill{i}";
            if (_numBoxes.TryGetValue(key, out var box) && box.Value > 0)
            {
                int skillId = (int)box.Value;
                int baseId = (skillId / 10) * 10;
                int newLevel = random.Next(minLevel, maxLevel + 1);
                box.Value = baseId + newLevel;
            }
        }
    }

    // ===================================== 兵种专长模板 =====================================
    private void ApplySpecialtyTemplate(string specialtyKey)
    {
        if (_current == null) { MessageBox.Show(Loc.T("Common.SelectItemFirst"), Loc.T("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var templates = ConfigManager.Instance.GetGeneralSpecialtyTemplates();
        var template = templates.FirstOrDefault(t => t.Key.Equals(specialtyKey, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            MessageBox.Show(Loc.F("General.SpecialtyTemplateNotFound", specialtyKey), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var random = new Random();
        _loadingUi = true; // 批量修改期间禁止触发 ApplyProp
        try
        {
            // 设置主属性
            if (!string.IsNullOrEmpty(template.MainStat) && _numBoxes.TryGetValue(template.MainStat, out var mainBox))
                mainBox.Value = template.MainStatValue;

            // 设置副属性
            if (!string.IsNullOrEmpty(template.SubStat1) && _numBoxes.TryGetValue(template.SubStat1, out var sub1Box))
                sub1Box.Value = template.SubStat1Value;
            if (!string.IsNullOrEmpty(template.SubStat2) && _numBoxes.TryGetValue(template.SubStat2, out var sub2Box))
                sub2Box.Value = template.SubStat2Value;

            // 设置其他属性范围
            SetField("March", random.Next(template.MarchMin, template.MarchMax + 1));
            SetField("Hp", random.Next(template.HpMin, template.HpMax + 1));

            // 设置技能（从技能池中随机选择）
            if (template.SkillPool.Count > 0)
            {
                var shuffled = template.SkillPool.OrderBy(_ => random.Next()).ToList();
                int skillCount = Math.Min(template.SkillCount, shuffled.Count);
                for (int i = 0; i < skillCount; i++)
                {
                    var key = $"Skill{i}";
                    if (_numBoxes.TryGetValue(key, out var box))
                    {
                        int baseId = (shuffled[i] / 10) * 10;
                        int level = random.Next(template.SkillLevelMin, template.SkillLevelMax + 1);
                        box.Value = baseId + level;
                    }
                }
                // 清空剩余技能槽
                for (int i = skillCount; i < 5; i++)
                {
                    var key = $"Skill{i}";
                    if (_numBoxes.TryGetValue(key, out var box))
                        box.Value = 0;
                }
            }
        }
        finally
        {
            _loadingUi = false;
        }

        ApplyProp();
        SetStatus(Loc.F("General.StatusSpecialtyApplied", template.Name));
    }

    private void SetField(string key, int value)
    {
        if (_numBoxes.TryGetValue(key, out var box))
            box.Value = value;
    }
}
