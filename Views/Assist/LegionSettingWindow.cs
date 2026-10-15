using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 军团设置窗口 - 对齐 VB 版 Assist/LegionSetting。
/// <para>
/// 左侧为军团列表（含颜色块与行动顺序），右侧为所选军团的字段编辑面板，
/// 支持全部字段：行动顺序/国家ID/阵营/经济/工业/科技/玩家控制/战败条件/
/// 血率/税率/颜色/科技等级/未知字段1-4/激光等级。
/// </para>
/// <para>
/// 非模态窗口：修改后立即写回 MapData 并触发 <see cref="DataModified"/>，
/// 由场景层刷新军团领域与归属国旗渲染。
/// </para>
/// </summary>
public sealed class LegionSettingWindow : Window
{
    /// <summary>字段定义：显示名 / Legion 字段名 / 是否为浮点 / 说明</summary>
    private sealed record FieldDef(string Label, string FieldName, bool IsFloat);

    /// <summary>颜色字段（R/G/B 三字节）在编辑面板中的虚拟键名</summary>
    private const string ColorFieldKey = "__color";

    /// <summary>全部可编辑字段，顺序即界面顺序（对齐 VB 版 LegionSetting）</summary>
    private static readonly FieldDef[] FieldDefs =
    {
        new("行动顺序", nameof(Legion.ActionId), false),
        new("国家ID", nameof(Legion.CountryId), false),
        new("阵营", nameof(Legion.Camp), false),
        new("初始经济", nameof(Legion.InitialEconomy), false),
        new("初始工业", nameof(Legion.InitialIndustry), false),
        new("初始科技", nameof(Legion.InitialTech), false),
        new("玩家控制", nameof(Legion.IsPlayerControlled), false),
        new("战败条件", nameof(Legion.DefeatCondition), false),
        new("国家血率", nameof(Legion.CountryHpRate), true),
        new("国家税率", nameof(Legion.CountryTaxRate), true),
        new("颜色", ColorFieldKey, false),
        new("科技等级", nameof(Legion.InitialTechLevel), false),
        new("未知字段1", nameof(Legion.Unknown1), false),
        new("未知字段2", nameof(Legion.Unknown2), false),
        new("未知字段3", nameof(Legion.Unknown3), false),
        new("未知字段4", nameof(Legion.Unknown4), false),
        new("激光等级", nameof(Legion.InitialLaserGunLevel), false),
    };

    /// <summary>Legion 的公开字段索引，供读写</summary>
    private static readonly Dictionary<string, FieldInfo> LegionFields =
        typeof(Legion)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(float))
            .ToDictionary(f => f.Name);

    private readonly MapData _mapData;
    private readonly LegionModifier _modifier;
    private readonly ListBox _legionListBox = new();
    private readonly StackPanel _editorPanel = new();
    private readonly Dictionary<string, TextBox> _fieldBoxes = new();
    private readonly Border _colorPreview = new();

    /// <summary>批量操作栏里共用的数值输入框（统一经济/工业/科技都用它）</summary>
    private readonly TextBox _batchValueBox = new();

    private int _selectedIndex = -1;
    private bool _isSyncing;

    /// <summary>数据被修改时触发，供场景层即时刷新渲染</summary>
    public event EventHandler? DataModified;

    public LegionSettingWindow(MapData mapData, LegionModifier modifier)
    {
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));

        Title = "军团列表管理";
        Width = 600;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 12;

        // 清掉上次"没找到国旗"的缓存：之后补上了国旗资源，重开窗口就能显示出来
        FlagImageLoader.ClearMisses();

        Content = CreateUI();
        LoadLegionList();

        PreviewKeyDown += OnPreviewKeyDown;
        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }

    // ------------------------------------------------------------------ UI

    private FrameworkElement CreateUI()
    {
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 8,
                BlurRadius = 20,
                Opacity = 0.4
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

        var title = CreateTitleBar();
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var body = CreateBody();
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var batch = CreateBatchBar();
        Grid.SetRow(batch, 2);
        grid.Children.Add(batch);

        var buttons = CreateButtonBar();
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        outer.Child = grid;
        return outer;
    }

    private Grid CreateTitleBar()
    {
        var panel = new Grid { Margin = new Thickness(15, 8, 12, 4) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "军团列表管理",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        panel.Children.Add(label);

        var close = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            ToolTip = "关闭 (Esc)"
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);

        return panel;
    }

    private Grid CreateBody()
    {
        var grid = new Grid { Margin = new Thickness(12, 4, 12, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧：军团列表 + 增删按钮
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _legionListBox.Background = new SolidColorBrush(Color.FromRgb(58, 58, 61));
        _legionListBox.Foreground = Brushes.White;
        _legionListBox.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        _legionListBox.BorderThickness = new Thickness(1);
        _legionListBox.Padding = new Thickness(4);
        _legionListBox.SelectionChanged += OnLegionSelectionChanged;
        _legionListBox.ItemContainerStyle = CreateListItemStyle();
        Grid.SetRow(_legionListBox, 0);
        leftGrid.Children.Add(_legionListBox);

        // 底部增删按钮（对齐 VB 版 LegionSetting 的 "+" / "-"）
        var addRemovePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        addRemovePanel.Children.Add(CreateSmallButton("+", Color.FromRgb(0, 122, 204), "新增军团",
            (_, _) => OnAddLegion()));
        addRemovePanel.Children.Add(CreateSmallButton("-", Color.FromRgb(200, 50, 50), "删除当前军团",
            (_, _) => OnDeleteLegion()));
        Grid.SetRow(addRemovePanel, 1);
        leftGrid.Children.Add(addRemovePanel);

        var leftBorder = new Border
        {
            Child = leftGrid,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(leftBorder, 0);
        grid.Children.Add(leftBorder);

        // 右侧：字段编辑面板
        _editorPanel.Margin = new Thickness(6, 2, 6, 2);
        BuildEditorFields();

        var scroll = new ScrollViewer
        {
            Content = _editorPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(52, 52, 55)),
            Padding = new Thickness(8)
        };

        var rightBorder = new Border
        {
            Child = scroll,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(rightBorder, 1);
        grid.Children.Add(rightBorder);

        return grid;
    }

    private static Style CreateListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(58, 58, 61))));
        style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(4)));

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 122, 204))));
        style.Triggers.Add(selected);

        var hover = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(80, 80, 83))));
        style.Triggers.Add(hover);

        return style;
    }

    private void BuildEditorFields()
    {
        _editorPanel.Children.Clear();
        _fieldBoxes.Clear();

        foreach (var def in FieldDefs)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = def.Label,
                Foreground = Brushes.White,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var box = new TextBox
            {
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12,
                IsEnabled = false
            };

            if (def.FieldName == ColorFieldKey)
            {
                box.ToolTip = "RRGGBB 十六进制，例如 FF0000";
                box.TextChanged += (_, _) => UpdateColorPreview();

                var colorRow = new Grid();
                colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

                Grid.SetColumn(box, 0);
                colorRow.Children.Add(box);

                _colorPreview.Width = 44;
                _colorPreview.Height = 22;
                _colorPreview.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
                _colorPreview.BorderThickness = new Thickness(1);
                _colorPreview.Background = Brushes.Transparent;
                _colorPreview.Margin = new Thickness(4, 0, 0, 0);
                _colorPreview.Cursor = Cursors.Hand;
                _colorPreview.ToolTip = "点击打开调色板";
                _colorPreview.MouseLeftButtonDown += (_, _) => PickColor();

                Grid.SetColumn(_colorPreview, 1);
                colorRow.Children.Add(_colorPreview);

                Grid.SetColumn(colorRow, 1);
                row.Children.Add(colorRow);
            }
            else if (def.FieldName == nameof(Legion.CountryId))
            {
                // 国家ID：输入框收窄，右侧让出一个按钮，点开带搜索栏的国家列表
                box.ToolTip = "国家 ID，也可点右侧按钮从列表中选择";

                var countryRow = new Grid();
                countryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                countryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

                Grid.SetColumn(box, 0);
                countryRow.Children.Add(box);

                var pickButton = new Button
                {
                    Content = "▼",
                    Width = 26,
                    Height = 24,
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 10,
                    Cursor = Cursors.Hand,
                    ToolTip = "从国家列表中选择"
                };
                pickButton.Click += (_, _) => ShowCountryPicker(pickButton, box);

                Grid.SetColumn(pickButton, 1);
                countryRow.Children.Add(pickButton);

                Grid.SetColumn(countryRow, 1);
                row.Children.Add(countryRow);
            }
            else
            {
                box.ToolTip = def.Label;
                Grid.SetColumn(box, 1);
                row.Children.Add(box);
            }

            _fieldBoxes[def.FieldName] = box;
            _editorPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// 弹出国家选择菜单：首行是搜索栏，下方是按 ID 排序的国家列表（ID - 名称）。
    /// 国家名取 string table 的 <c>country_{id}</c>，不取 CountrySettings.json 的 Name。
    /// 回车 / 双击确认后回填到目标输入框（会触发既有的写回逻辑）。
    /// </summary>
    private void ShowCountryPicker(Button anchor, TextBox target)
    {
        var countries = WC4MapEditor.Core.Parsers.Country.CountrySettingParser.Instance.Countries
            .OrderBy(c => c.Id)
            .ToList();

        // 显示名取 string table 的 country_{id}，不取 CountrySettings.json 的 Name
        var displayNames = new Dictionary<int, string>();
        foreach (var c in countries)
        {
            var name = WC4MapEditor.Core.Config.ConfigManager.Instance.GetStringTableValue($"country_{c.Id}");
            if (string.IsNullOrWhiteSpace(name)) name = $"国家{c.Id}";
            displayNames[c.Id] = name;
        }

        // 第一行：搜索栏（按国家名或 ID 过滤）
        var searchBox = new TextBox
        {
            Height = 24,
            Margin = new Thickness(6, 6, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            ToolTip = "输入国家名或 ID 进行搜索"
        };

        var listBox = new ListBox
        {
            MaxHeight = 300,
            Margin = new Thickness(6, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromRgb(58, 58, 61)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            ItemContainerStyle = CreateListItemStyle()
        };

        var panel = new StackPanel();
        panel.Children.Add(searchBox);
        panel.Children.Add(listBox);

        var border = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 }
        };

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Width = 280,
            Child = border
        };

        void Commit()
        {
            // 没手动选时取第一项，方便"搜出来直接回车"
            if (listBox.SelectedItem == null && listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;

            if (listBox.SelectedItem is ListBoxItem item && item.Tag is int id)
            {
                target.Text = id.ToString();
                popup.IsOpen = false;
            }
        }

        void FillList(string keyword)
        {
            listBox.Items.Clear();

            foreach (var country in countries)
            {
                string display = $"{country.Id} - {displayNames[country.Id]}";
                if (keyword.Length > 0 &&
                    display.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                listBox.Items.Add(new ListBoxItem { Content = display, Tag = country.Id });
            }

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        searchBox.TextChanged += (_, _) => FillList(searchBox.Text.Trim());

        searchBox.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when listBox.Items.Count > 0:
                    listBox.Focus();
                    listBox.SelectedIndex = 0;
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Commit();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    popup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
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

    /// <summary>
    /// 批量操作栏：一个共用的数值输入框 + 三个"统一"按钮 + 两个直接执行的按钮。
    /// <para>
    /// 这一栏作用于【所有军团】，与右侧编辑面板（只改当前选中的那一个）语义不同，
    /// 所以放在编辑区之外、底部按钮栏之上，免得误以为是在改选中的军团。
    /// </para>
    /// <para>
    /// 经济/工业/科技共用一个输入框：三者语义相同（把某个字段统一成同一个值），
    /// 分开三个框反而更啰嗦。
    /// </para>
    /// </summary>
    private StackPanel CreateBatchBar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };

        var label = new TextBlock
        {
            Text = "批量：",
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        panel.Children.Add(label);

        _batchValueBox.Width = 70;
        _batchValueBox.Height = 26;
        _batchValueBox.Background = new SolidColorBrush(Color.FromRgb(70, 70, 73));
        _batchValueBox.Foreground = Brushes.White;
        _batchValueBox.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        _batchValueBox.BorderThickness = new Thickness(1);
        _batchValueBox.Padding = new Thickness(4, 1, 4, 1);
        _batchValueBox.VerticalContentAlignment = VerticalAlignment.Center;
        _batchValueBox.FontSize = 12;
        _batchValueBox.ToolTip = "统一经济 / 工业 / 科技时使用的数值";
        _batchValueBox.Margin = new Thickness(0, 0, 8, 0);
        panel.Children.Add(_batchValueBox);

        // 按钮做得比底部那排窄一些，五个并排才放得下
        void AddBatchButton(string text, Color background, Action onClick)
        {
            var button = CreateButton(text, background, (_, _) => onClick());
            button.Width = 76;
            button.Height = 26;
            button.FontSize = 11;
            button.Margin = new Thickness(4, 0, 0, 0);
            button.ToolTip = text;
            panel.Children.Add(button);
        }

        AddBatchButton("统一经济", Color.FromRgb(70, 110, 70),
            () => ApplyBatchValue("经济", _modifier.SetAllLegionsEconomy));
        AddBatchButton("统一工业", Color.FromRgb(70, 110, 70),
            () => ApplyBatchValue("工业", _modifier.SetAllLegionsIndustry));
        AddBatchButton("统一科技", Color.FromRgb(70, 110, 70),
            () => ApplyBatchValue("科技", _modifier.SetAllLegionsTech));
        AddBatchButton("随机化等级", Color.FromRgb(125, 90, 40), OnRandomizeAllLegionLevels);
        AddBatchButton("应用默认颜色", Color.FromRgb(125, 90, 40), OnApplyDefaultColors);

        return panel;
    }

    /// <summary>
    /// 把批量输入框里的整数应用到所有军团的指定字段。
    /// </summary>
    private void ApplyBatchValue(string fieldLabel, Func<int, ModifierResult> apply)
    {
        if (!int.TryParse(_batchValueBox.Text.Trim(), out int value))
        {
            MessageBox.Show(
                $"请先在「批量」输入框里填一个整数（用于统一{fieldLabel}）",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            _batchValueBox.Focus();
            return;
        }

        var result = apply(value);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? $"统一{fieldLabel}失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    private void OnRandomizeAllLegionLevels()
    {
        var confirm = MessageBox.Show(
            "随机化所有军团的等级与经济？\n\n此操作会覆盖所有军团的现有数值。",
            "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _modifier.RandomizeAllLegionLevels();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? "随机化失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    private void OnApplyDefaultColors()
    {
        var result = _modifier.ApplyDefaultColorsToAllLegions();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? "应用默认颜色失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadAfterBatchChange();
    }

    /// <summary>
    /// 批量操作后整体刷新。
    /// <para>
    /// 必须重载整张列表，而不是只刷当前项：这些操作一次性改动所有军团，
    /// 而编辑面板只绑定"当前选中"的那一个，不重载的话其它项显示的还是旧值
    /// （颜色类操作尤其明显 —— 左侧列表的色块全都得跟着变）。
    /// 顺便尽量保住原来的选中位置。
    /// </para>
    /// </summary>
    private void ReloadAfterBatchChange()
    {
        int keep = _selectedIndex;
        if (keep < 0 || keep >= _mapData.Legions.Count) keep = 0;

        ReloadLegionList(keep);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private StackPanel CreateButtonBar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 12)
        };

        panel.Children.Add(CreateButton("重置", Color.FromRgb(80, 80, 80), (_, _) => ReloadSelected()));
        panel.Children.Add(CreateButton("应用", Color.FromRgb(0, 122, 204), (_, _) => ApplyToSelected()));
        panel.Children.Add(CreateButton("关闭", Color.FromRgb(80, 80, 80), (_, _) => Close()));

        return panel;
    }

    private static Button CreateButton(string content, Color background, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = content,
            Width = 76,
            Height = 30,
            Background = new SolidColorBrush(background),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0)
        };
        button.Click += onClick;

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(100, 149, 237))));
        template.Triggers.Add(hover);
        button.Template = template;

        return button;
    }

    /// <summary>小尺寸按钮（复用标准按钮的圆角模板）</summary>
    private static Button CreateSmallButton(string content, Color background, string tooltip, RoutedEventHandler onClick)
    {
        var button = CreateButton(content, background, onClick);
        button.Width = 32;
        button.Height = 26;
        button.FontSize = 14;
        button.FontWeight = FontWeights.Bold;
        button.Margin = new Thickness(4, 0, 4, 0);
        button.ToolTip = tooltip;
        return button;
    }

    // -------------------------------------------------------------- 数据

    private void LoadLegionList()
    {
        _isSyncing = true;
        _legionListBox.Items.Clear();

        for (int i = 0; i < _mapData.Legions.Count; i++)
            _legionListBox.Items.Add(CreateLegionListItem(i, _mapData.Legions[i]));

        _isSyncing = false;

        if (_legionListBox.Items.Count > 0)
            _legionListBox.SelectedIndex = 0;
        else
            SetEditorEnabled(false);
    }

    /// <summary>
    /// 构建单个军团列表项：压缩后的颜色条 + 国旗 + 两行文本。
    /// 第二行用 string table 里的国家名称（与 ini 配置一致）替代原先固定的"国家{id}"。
    /// </summary>
    private FrameworkElement CreateLegionListItem(int index, Legion legion)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // 颜色条
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });   // 国旗
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 军团颜色条：宽度压缩到 5px，把位置让给国旗
        var colorBar = new Border
        {
            Width = 5,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(legion.ColorR, legion.ColorG, legion.ColorB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"军团颜色 #{legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2}"
        };
        Grid.SetColumn(colorBar, 0);
        row.Children.Add(colorBar);

        // 国旗 flag_{countryId}.png
        var flag = GetFlagImage(legion.CountryId);
        FrameworkElement flagElement;
        if (flag != null)
        {
            flagElement = new Image
            {
                Source = flag,
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = $"flag_{legion.CountryId}.png"
            };
        }
        else
        {
            flagElement = new TextBlock
            {
                Text = "无旗",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = $"未找到 flag_{legion.CountryId}.png"
            };
        }
        Grid.SetColumn(flagElement, 1);
        row.Children.Add(flagElement);

        var text = new TextBlock
        {
            Text = $"军团 {index + 1}  顺序{legion.ActionId}\n{GetCountryName(legion.CountryId)} | 阵营{legion.Camp}",
            Foreground = Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(text, 2);
        row.Children.Add(text);

        return row;
    }

    /// <summary>
    /// 加载国家国旗 flag_{countryId}.png。
    /// 实际由 <see cref="FlagImageLoader"/> 完成：战术地图图集优先，文件回退。
    /// </summary>
    private static BitmapSource? GetFlagImage(int countryId) => FlagImageLoader.Load(countryId);

    /// <summary>
    /// 取国家名称（CountrySettings.json → string table 的 country_{id} → 国家{id}）。
    /// 解析顺序收进 <see cref="WC4MapEditor.Core.Config.CountryNameProvider"/>，
    /// 与 CLI 的 legion list 共用一份，避免两处各写一套。
    /// </summary>
    private static string GetCountryName(int countryId)
        => WC4MapEditor.Core.Config.CountryNameProvider.Get(countryId);

    /// <summary>
    /// 新增军团。行动顺序 / 国家 ID 自动取现有最大值 +1，
    /// 其余字段取 Legion.CreateDefault 的默认值（经济 1000 / 工业 100 / 科技 50 /
    /// 血率 1.0 / 税率 0.1 / 颜色白色）。
    /// </summary>
    private void OnAddLegion()
    {
        var result = _modifier.AddLegion();
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? "新增军团失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadLegionList(_mapData.Legions.Count - 1);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>删除当前选中的军团（对齐 VB 版 "-" 按钮）</summary>
    private void OnDeleteLegion()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _mapData.Legions.Count)
        {
            MessageBox.Show("请先在左侧选择一个军团", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要删除军团 {_selectedIndex + 1} 吗？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _modifier.RemoveLegionAt(_selectedIndex);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? "删除军团失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReloadLegionList(Math.Min(_selectedIndex, _mapData.Legions.Count - 1));
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>重新加载列表并选中指定项</summary>
    private void ReloadLegionList(int selectIndex)
    {
        _selectedIndex = -1;
        LoadLegionList();

        if (_legionListBox.Items.Count == 0)
        {
            SetEditorEnabled(false);
            return;
        }

        _legionListBox.SelectedIndex = Math.Clamp(selectIndex, 0, _legionListBox.Items.Count - 1);
        ReloadSelected();
    }

    private void SetEditorEnabled(bool enabled)
    {
        foreach (var box in _fieldBoxes.Values)
            box.IsEnabled = enabled;
    }

    private void OnLegionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing) return;
        ReloadSelected();
    }

    private void ReloadSelected()
    {
        int index = _legionListBox.SelectedIndex;
        if (index < 0 || index >= _mapData.Legions.Count)
        {
            _selectedIndex = -1;
            SetEditorEnabled(false);
            return;
        }

        _selectedIndex = index;
        SetEditorEnabled(true);

        var legion = _mapData.Legions[index];
        _isSyncing = true;

        foreach (var def in FieldDefs)
        {
            if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;

            if (def.FieldName == ColorFieldKey)
            {
                box.Text = $"{legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2}";
                continue;
            }

            if (!LegionFields.TryGetValue(def.FieldName, out var field)) continue;
            object? value = field.GetValue(legion);
            box.Text = value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _isSyncing = false;
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box)) return;

        if (TryParseColor(box.Text, out byte r, out byte g, out byte b))
            _colorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        else
            _colorPreview.Background = Brushes.Transparent;
    }

    /// <summary>打开调色板，把结果写回颜色输入框与预览</summary>
    private void PickColor()
    {
        if (!_fieldBoxes.TryGetValue(ColorFieldKey, out var box)) return;

        Color initial = TryParseColor(box.Text, out byte r, out byte g, out byte b)
            ? Color.FromRgb(r, g, b)
            : Colors.White;

        var picked = ColorPickerDialog.Pick(Window.GetWindow(this), initial);
        if (picked == null) return;

        box.Text = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        UpdateColorPreview();
    }

    private static bool TryParseColor(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;

        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return false;
        if (!byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return false;
        return true;
    }

    /// <summary>把编辑面板的输入写回当前选中的军团</summary>
    private void ApplyToSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _mapData.Legions.Count) return;

        // Legion 是值类型：先装箱修改，再整体写回
        object boxed = _mapData.Legions[_selectedIndex];
        var errors = new List<string>();

        foreach (var def in FieldDefs)
        {
            if (!_fieldBoxes.TryGetValue(def.FieldName, out var box)) continue;
            string text = box.Text.Trim();
            if (text.Length == 0) continue;

            if (def.FieldName == ColorFieldKey)
            {
                if (!TryParseColor(text, out byte r, out byte g, out byte b))
                {
                    errors.Add("颜色格式应为 6 位十六进制(RRGGBB)");
                    continue;
                }

                var legion = (Legion)boxed;
                legion.ColorR = r;
                legion.ColorG = g;
                legion.ColorB = b;
                boxed = legion;
                continue;
            }

            if (!LegionFields.TryGetValue(def.FieldName, out var field)) continue;

            if (def.IsFloat)
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    errors.Add($"{def.Label}: 需要数字");
                else
                    field.SetValue(boxed, f);
            }
            else
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    errors.Add($"{def.Label}: 需要整数");
                else
                    field.SetValue(boxed, v);
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "输入有误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updated = (Legion)boxed;

        // 按列表索引写回（不能用按 CountryId 查表的 UpdateLegion，
        // 否则改了 CountryId 就会被当成新军团插入）
        var result = _modifier.UpdateLegionAt(_selectedIndex, updated);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? "保存失败", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshListItem(_selectedIndex, updated);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshListItem(int index, Legion legion)
    {
        if (index < 0 || index >= _legionListBox.Items.Count) return;

        // 直接重建该项：CountryId 可能已变，国旗需要一起换
        bool wasSelected = _legionListBox.SelectedIndex == index;

        _isSyncing = true;
        _legionListBox.Items[index] = CreateLegionListItem(index, legion);
        if (wasSelected) _legionListBox.SelectedIndex = index;
        _isSyncing = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                ApplyToSelected();
                e.Handled = true;
                break;
        }
    }
}
