using System.Globalization;
using System.Reflection;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 头部数据编辑窗口 - 对齐 VB 版 Assist/HeaderSetting（军团编辑模式 E 键，<see cref="EditorKinds.HeaderSetting"/>）。
/// <para>
/// 编辑 BTL 文件头的 27 个整型字段（版本/地图尺寸/各类数量/回合数/累积资源等）。
/// "保存并关闭"只写回改过的字段（窗口打开期间别处改动的计数不会被旧值覆盖），整次保存记一条撤销；
/// "取消" / Esc / ✕ 不产生任何改动。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.HeaderSetting)]
public sealed class HeaderSettingWindow : Window, IAssistEditor
{
    /// <summary>字段定义：显示名 / BTLHeader 属性名 / 鼠标提示。按实例构建，文字随当前语言而不是缓存在类型里。</summary>
    private readonly (string Label, string Prop, string Tip)[] _fieldDefs = BuildFieldDefs();

    private static (string Label, string Prop, string Tip)[] BuildFieldDefs()
    {
        // 提示与显示名相同的字段只用一个键
        static (string Label, string Prop, string Tip) F(string labelKey, string prop, string? tipKey = null)
            => (Loc.T(labelKey), prop, Loc.T(tipKey ?? labelKey));

        return new[]
        {
            F("Assist.Header.Field.BtlVersion", nameof(BTLHeader.BtlVersion), "Assist.Header.Tip.BtlVersion"),
            F("Assist.Header.Field.MapNumber", nameof(BTLHeader.MapNumber)),
            F("Assist.Header.Field.MapClipX", nameof(BTLHeader.MapClipX), "Assist.Header.Tip.MapClipX"),
            F("Assist.Header.Field.MapClipY", nameof(BTLHeader.MapClipY), "Assist.Header.Tip.MapClipY"),
            F("Assist.Header.Field.MapLength", nameof(BTLHeader.MapLength), "Assist.Header.Tip.MapLength"),
            F("Assist.Header.Field.MapWidth", nameof(BTLHeader.MapWidth), "Assist.Header.Tip.MapWidth"),
            F("Assist.Header.Field.ArmyCount", nameof(BTLHeader.ArmyCount)),
            F("Assist.Header.Field.BuildingCount", nameof(BTLHeader.BuildingCount)),
            F("Assist.Header.Field.TroopCount", nameof(BTLHeader.TroopCount)),
            F("Assist.Header.Field.PlanCount", nameof(BTLHeader.PlanCount)),
            F("Assist.Header.Field.EventCount", nameof(BTLHeader.EventCount)),
            F("Assist.Header.Field.WeatherCount", nameof(BTLHeader.WeatherCount)),
            F("Assist.Header.Field.VictoryCondition", nameof(BTLHeader.VictoryCondition)),
            F("Assist.Header.Field.MinTurns", nameof(BTLHeader.MinTurns)),
            F("Assist.Header.Field.MaxTurns", nameof(BTLHeader.MaxTurns)),
            F("Assist.Header.Field.ReinforcementCount", nameof(BTLHeader.ReinforcementCount)),
            F("Assist.Header.Field.AirRaidCount", nameof(BTLHeader.AirRaidCount)),
            F("Assist.Header.Field.PlacementA", nameof(BTLHeader.PlacementA)),
            F("Assist.Header.Field.PlacementB", nameof(BTLHeader.PlacementB)),
            F("Assist.Header.Field.ConqueredFlagPosition", nameof(BTLHeader.ConqueredFlagPosition)),
            F("Assist.Header.Field.SelectableTileCount", nameof(BTLHeader.SelectableTileCount)),
            F("Assist.Header.Field.AccumulatedEconomy", nameof(BTLHeader.AccumulatedEconomy)),
            F("Assist.Header.Field.AccumulatedIndustry", nameof(BTLHeader.AccumulatedIndustry)),
            F("Assist.Header.Field.AccumulatedTech", nameof(BTLHeader.AccumulatedTech)),
            F("Assist.Header.Field.TrapCount", nameof(BTLHeader.TrapCount)),
            F("Assist.Header.Field.StrategyCount", nameof(BTLHeader.StrategyCount)),
            F("Assist.Header.Field.AirSupportCount", nameof(BTLHeader.AirSupportCount)),
        };
    }

    private static readonly Dictionary<string, PropertyInfo> HeaderProps =
        typeof(BTLHeader)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.CanWrite)
            .ToDictionary(p => p.Name);

    private readonly Dictionary<string, TextBox> _boxes = new();
    private AssistEditorContext _context = null!;

    /// <summary>打开（或重置）时的头部数据副本：保存时只写回与它不同的字段</summary>
    private BTLHeader _loaded = new();

    public HeaderSettingWindow()
    {
        EditorTheme.Apply(this);

        Title = Loc.T("Assist.Header.Title");
        Width = 620;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        PreviewKeyDown += OnPreviewKeyDown;

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    void IAssistEditor.Attach(AssistEditorContext context)
    {
        _context = context;
        Content = CreateUI();
        LoadValues();
    }

    private BTLHeader Target => _context.MapData.Header;

    // ------------------------------------------------------------------ UI

    private FrameworkElement CreateUI()
    {
        var outer = new Border
        {
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Bg0),
                Direction = 270,
                ShadowDepth = 6,
                BlurRadius = 20,
                Opacity = 0.6
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

        var label = EditorTheme.Title(Loc.T("Assist.Header.Title"));
        Grid.SetColumn(label, 0);
        panel.Children.Add(label);

        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 28;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.ToolTip = Loc.T("Assist.Common.CancelEsc");
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);

        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
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
        int half = (_fieldDefs.Length + 1) / 2;
        for (int i = 0; i < _fieldDefs.Length; i++)
        {
            bool rightColumn = i >= half;
            int rowIndex = rightColumn ? i - half : i;
            var def = _fieldDefs[i];

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = EditorTheme.FieldLabel(def.Label);
            label.ToolTip = def.Tip;
            label.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var box = new TextBox
            {
                ToolTip = def.Tip
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            _boxes[def.Prop] = box;

            Grid.SetColumn(row, rightColumn ? 2 : 0);
            Grid.SetRow(row, rowIndex);
            if (panel.RowDefinitions.Count <= rowIndex)
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

        var reset = EditorTheme.Button(Loc.T("Common.Reset"), (_, _) => LoadValues());
        reset.MinWidth = 96;
        reset.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(reset);

        var save = EditorTheme.PrimaryButton(Loc.T("Assist.Header.SaveAndClose"), (_, _) => SaveAndClose());
        save.MinWidth = 96;
        save.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(save);

        var cancel = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => Close());
        cancel.MinWidth = 96;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(cancel);

        return panel;
    }

    // -------------------------------------------------------------- 数据

    /// <summary>从地图的当前头部数据重新载入（打开时与"重置"）。</summary>
    private void LoadValues()
    {
        _loaded = Copy(Target);
        foreach (var def in _fieldDefs)
        {
            if (!_boxes.TryGetValue(def.Prop, out var box)) continue;
            if (!HeaderProps.TryGetValue(def.Prop, out var prop)) continue;

            object? value = prop.GetValue(_loaded);
            box.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        }
    }

    /// <summary>
    /// 校验全部输入，把改过的字段写回地图头部数据并记录撤销（撤销时整体还原保存前的头部数据），然后关闭。
    /// 头部里的裁剪区与尺寸会影响归属换算和红叉显示，所以按"全部数据"通知场景刷新。
    /// </summary>
    private void SaveAndClose()
    {
        var errors = new List<string>();
        var changes = new List<(PropertyInfo Prop, int Value)>();

        foreach (var def in _fieldDefs)
        {
            if (!_boxes.TryGetValue(def.Prop, out var box)) continue;
            if (!HeaderProps.TryGetValue(def.Prop, out var prop)) continue;

            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                errors.Add(Loc.F("Assist.Header.IntegerRequired", def.Label));
                continue;
            }

            if (value != (int)prop.GetValue(_loaded)!) changes.Add((prop, value));
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), Loc.T("Common.InputError"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (changes.Count > 0)
        {
            var before = Copy(Target);
            var after = Copy(Target);
            foreach (var (prop, value) in changes) prop.SetValue(after, value);

            var map = _context.MapData;
            _context.Editor.RecordEntityChange(Loc.T("Assist.Header.UndoEdit", "修改头部数据"),
                () => Restore(map, after), () => Restore(map, before));
            _context.NotifyChanged(EditorDataScope.All);
            _context.Status(Loc.F("Assist.Header.Updated", changes.Count));
        }

        Close();
    }

    private static void Restore(MapData map, BTLHeader snapshot)
    {
        CopyInto(snapshot, map.Header);
        map.IsModified = true;
    }

    private static BTLHeader Copy(BTLHeader source)
    {
        var copy = new BTLHeader();
        CopyInto(source, copy);
        return copy;
    }

    private static void CopyInto(BTLHeader source, BTLHeader target)
    {
        foreach (var prop in HeaderProps.Values)
            prop.SetValue(target, prop.GetValue(source));
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
                SaveAndClose();
                e.Handled = true;
                break;
        }
    }
}
