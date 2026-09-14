using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Views.Assist;

/// <summary>
/// 头部数据编辑窗口 - 对齐 VB 版 Assist/HeaderSetting。
/// <para>
/// 编辑 BTL 文件头的 27 个整型字段（版本/地图尺寸/各类数量/回合数/累积资源等）。
/// 窗口内编辑的是副本，点击"保存并关闭"才写回原对象，因此"取消"不会产生任何改动。
/// </para>
/// </summary>
public sealed class HeaderSettingWindow : Window
{
    /// <summary>字段定义：显示名 / BTLHeader 属性名 / 鼠标提示</summary>
    private static readonly (string Label, string Prop, string Tip)[] FieldDefs =
    {
        ("BTL版本", nameof(BTLHeader.BtlVersion), "BTL文件版本号"),
        ("地图编号", nameof(BTLHeader.MapNumber), "地图编号"),
        ("地图裁剪X", nameof(BTLHeader.MapClipX), "地图裁剪X坐标"),
        ("地图裁剪Y", nameof(BTLHeader.MapClipY), "地图裁剪Y坐标"),
        ("地图长度", nameof(BTLHeader.MapLength), "地图长度（列数，水平方向）"),
        ("地图宽度", nameof(BTLHeader.MapWidth), "地图宽度（行数，垂直方向）"),
        ("军团数量", nameof(BTLHeader.ArmyCount), "军团数量"),
        ("建筑数量", nameof(BTLHeader.BuildingCount), "建筑数量"),
        ("兵种数量", nameof(BTLHeader.TroopCount), "兵种数量"),
        ("方案数量", nameof(BTLHeader.PlanCount), "方案数量"),
        ("事件数量", nameof(BTLHeader.EventCount), "事件数量"),
        ("天气数量", nameof(BTLHeader.WeatherCount), "天气数量"),
        ("胜利条件", nameof(BTLHeader.VictoryCondition), "胜利条件"),
        ("最小回合数", nameof(BTLHeader.MinTurns), "最小回合数"),
        ("最大回合数", nameof(BTLHeader.MaxTurns), "最大回合数"),
        ("援军数量", nameof(BTLHeader.ReinforcementCount), "援军数量"),
        ("空袭数量", nameof(BTLHeader.AirRaidCount), "空袭数量"),
        ("放置位甲", nameof(BTLHeader.PlacementA), "放置位甲"),
        ("放置位乙", nameof(BTLHeader.PlacementB), "放置位乙"),
        ("征服旗帜位置", nameof(BTLHeader.ConqueredFlagPosition), "征服旗帜位置"),
        ("可选格子数量", nameof(BTLHeader.SelectableTileCount), "可选格子数量"),
        ("累积经济", nameof(BTLHeader.AccumulatedEconomy), "累积经济"),
        ("累积工业", nameof(BTLHeader.AccumulatedIndustry), "累积工业"),
        ("累积科技", nameof(BTLHeader.AccumulatedTech), "累积科技"),
        ("陷阱数量", nameof(BTLHeader.TrapCount), "陷阱数量"),
        ("战略数量", nameof(BTLHeader.StrategyCount), "战略数量"),
        ("空中支援数量", nameof(BTLHeader.AirSupportCount), "空中支援数量"),
    };

    private static readonly Dictionary<string, PropertyInfo> HeaderProps =
        typeof(BTLHeader)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.CanWrite)
            .ToDictionary(p => p.Name);

    private readonly BTLHeader _target;
    private readonly BTLHeader _working;
    private readonly Dictionary<string, TextBox> _boxes = new();

    public HeaderSettingWindow(BTLHeader header)
    {
        _target = header ?? throw new ArgumentNullException(nameof(header));
        // 通过序列化往返得到独立副本，保证取消不产生副作用
        _working = BTLHeader.Parse(_target.ToBytes());

        Title = "头部数据编辑";
        Width = 620;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 12;

        Content = CreateUI();
        LoadValues();

        PreviewKeyDown += OnPreviewKeyDown;

        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }

    /// <summary>以模态方式显示头部数据编辑窗口，返回是否保存了修改</summary>
    public static bool ShowDialog(Window? owner, BTLHeader header)
    {
        var window = new HeaderSettingWindow(header);
        if (owner != null && !ReferenceEquals(owner, window))
            window.Owner = owner;
        return window.ShowDialog() == true;
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
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

        var title = CreateTitleBar();
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var body = CreateFieldGrid();
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var buttons = CreateButtonBar();
        Grid.SetRow(buttons, 2);
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
            Text = "头部数据编辑",
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
            ToolTip = "取消 (Esc)"
        };
        close.Click += (_, _) => CancelAndClose();
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);

        return panel;
    }

    private FrameworkElement CreateFieldGrid()
    {
        var panel = new Grid
        {
            Margin = new Thickness(16, 4, 16, 4)
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 两列排布：左列放前半，右列放后半
        int half = (FieldDefs.Length + 1) / 2;
        for (int i = 0; i < FieldDefs.Length; i++)
        {
            bool rightColumn = i >= half;
            int rowIndex = rightColumn ? i - half : i;
            var def = FieldDefs[i];

            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = def.Label,
                Foreground = Brushes.White,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = def.Tip
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
                ToolTip = def.Tip
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            _boxes[def.Prop] = box;

            Grid.SetColumn(row, rightColumn ? 2 : 0);
            Grid.SetRow(row, rowIndex);
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(row);
        }

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
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

        panel.Children.Add(CreateButton("重置", Color.FromRgb(80, 80, 80), (_, _) => LoadValues()));
        panel.Children.Add(CreateButton("保存并关闭", Color.FromRgb(0, 122, 204), (_, _) => SaveAndClose()));
        panel.Children.Add(CreateButton("取消", Color.FromRgb(80, 80, 80), (_, _) => CancelAndClose()));

        return panel;
    }

    private static Button CreateButton(string content, Color background, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = content,
            Width = 96,
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

    // -------------------------------------------------------------- 数据

    private void LoadValues()
    {
        foreach (var def in FieldDefs)
        {
            if (!_boxes.TryGetValue(def.Prop, out var box)) continue;
            if (!HeaderProps.TryGetValue(def.Prop, out var prop)) continue;

            object? value = prop.GetValue(_working);
            box.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        }
    }

    private void SaveAndClose()
    {
        var errors = new List<string>();

        foreach (var def in FieldDefs)
        {
            if (!_boxes.TryGetValue(def.Prop, out var box)) continue;
            if (!HeaderProps.TryGetValue(def.Prop, out var prop)) continue;

            string text = box.Text.Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                errors.Add($"{def.Label}: 需要整数");
                continue;
            }

            prop.SetValue(_working, value);
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "输入有误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 写回原对象
        foreach (var prop in HeaderProps.Values)
            prop.SetValue(_target, prop.GetValue(_working));

        DialogResult = true;
        Close();
    }

    private void CancelAndClose()
    {
        DialogResult = false;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CancelAndClose();
                e.Handled = true;
                break;
            case Key.Enter:
                SaveAndClose();
                e.Handled = true;
                break;
        }
    }
}
