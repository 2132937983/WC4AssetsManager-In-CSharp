using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WC4MapEditor.GUI.Views.Dialogs;

/// <summary>
/// 兵种「编队/等级数值模拟」对话框（对应 Python 工具的 simulate_stats）。
/// 选择等级与编队后，按公式重算 HP / MinAttack / MaxAttack / Defence 并返回。
/// 与 <see cref="SingleInputDialog"/> 相同的覆盖层交互方式。
/// </summary>
public sealed class UnitStatsSimulatorDialog : IDisposable
{
    /// <summary>模拟结果：等级(1~6)、编队(1~4)、是否应用到全部。</summary>
    public sealed record SimulatorResult(int Level, int Formation, bool ApplyToAll);

    private readonly Window _owner;
    private Border? _overlay;
    private Border? _dialogPanel;
    private ComboBox? _levelCombo;
    private ComboBox? _formationCombo;
    private RadioButton? _scopeCurrent;
    private RadioButton? _scopeAll;
    private TaskCompletionSource<SimulatorResult?>? _tcs;
    private bool _isClosed;

    // 默认文字在构造时取当前语言：对话框对象每次使用都新建，不会缓存旧语言
    public string Title { get; set; } = Loc.T("Dialog.Sim.Title");
    public string Description { get; set; } = Loc.T("Dialog.Sim.Description");
    public bool HasCurrentUnit { get; set; } = true;

    public UnitStatsSimulatorDialog(Window owner) => _owner = owner;

    public Task<SimulatorResult?> ShowAsync()
    {
        _tcs = new TaskCompletionSource<SimulatorResult?>();
        _isClosed = false;

        var content = _owner.Content as FrameworkElement;
        Panel rootPanel;
        if (content is Panel panel)
            rootPanel = panel;
        else if (content?.Parent is Panel parentPanel)
            rootPanel = parentPanel;
        else
            throw new InvalidOperationException("Cannot find a suitable root panel for the dialog overlay.");

        _overlay = new Border
        {
            Background = CreateScrim(),
            Focusable = true
        };
        // 主窗口本身不套编辑主题，对话框内的下拉框/单选框/按钮要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel();
        _overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseDialog(null); }
        };
        Panel.SetZIndex(_overlay, 9999);
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        rootPanel.Children.Add(_overlay);

        _levelCombo?.Focus();
        return _tcs.Task;
    }

    private UIElement CreateDialogPanel()
    {
        var stack = new StackPanel();

        var title = EditorTheme.Title(Title);
        title.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(title);

        var desc = EditorTheme.Muted(Description);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(desc);

        // 等级 / 编队的显示文字只用于展示，结果取 SelectedIndex，与语言无关
        _levelCombo = MakeCombo(Enumerable.Range(1, 6).Select(n => Loc.F("Dialog.Sim.LevelItem", n)).ToArray());
        stack.Children.Add(MakeRow(Loc.T("Dialog.Sim.SelectLevel"), _levelCombo));

        _formationCombo = MakeCombo(new[]
        {
            Loc.T("Dialog.Sim.Formation1"),
            Loc.T("Dialog.Sim.Formation2"),
            Loc.T("Dialog.Sim.Formation3"),
            Loc.T("Dialog.Sim.Formation4")
        });
        stack.Children.Add(MakeRow(Loc.T("Dialog.Sim.SelectFormation"), _formationCombo));

        var scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
        _scopeCurrent = new RadioButton
        {
            Content = Loc.T("Dialog.Sim.ScopeCurrent"),
            GroupName = "simScope",
            IsChecked = true,
            IsEnabled = HasCurrentUnit,
            Margin = new Thickness(0, 0, 16, 0)
        };
        _scopeAll = new RadioButton
        {
            Content = Loc.T("Dialog.Sim.ScopeAll"),
            GroupName = "simScope"
        };
        if (!HasCurrentUnit) _scopeAll.IsChecked = true;
        scopePanel.Children.Add(_scopeCurrent);
        scopePanel.Children.Add(_scopeAll);
        stack.Children.Add(MakeRow(Loc.T("Dialog.Sim.Scope"), scopePanel));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CloseDialog(null));
        cancelButton.MinWidth = 80;
        cancelButton.Margin = new Thickness(0, 0, 8, 0);
        buttonPanel.Children.Add(cancelButton);

        var applyButton = EditorTheme.PrimaryButton(Loc.T("Dialog.Sim.Apply"), (_, _) => CloseDialog(new SimulatorResult(
            (_levelCombo?.SelectedIndex ?? 0) + 1,
            (_formationCombo?.SelectedIndex ?? 0) + 1,
            _scopeAll?.IsChecked == true)));
        buttonPanel.Children.Add(applyButton);

        stack.Children.Add(buttonPanel);

        _dialogPanel = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Width = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6,
                Color = EditorTheme.ColorOf(EditorTheme.Bg0)
            },
            Child = stack
        };

        return _dialogPanel;
    }

    /// <summary>覆盖层的半透明遮罩：由主题最深底色派生，不写死颜色。</summary>
    private static Brush CreateScrim()
    {
        var c = EditorTheme.ColorOf(EditorTheme.Bg0);
        return new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
    }

    private static ComboBox MakeCombo(string[] items)
    {
        var combo = EditorTheme.ComboBox(180);
        foreach (var i in items) combo.Items.Add(i);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static UIElement MakeRow(string label, UIElement content)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = EditorTheme.FieldLabel(label);
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(content);
        return grid;
    }

    private void CloseDialog(SimulatorResult? result)
    {
        if (_isClosed) return;
        _isClosed = true;

        var content = _owner.Content as FrameworkElement;
        if (content is Panel rootPanel && _overlay != null)
            rootPanel.Children.Remove(_overlay);

        _tcs?.SetResult(result);
    }

    public void Dispose()
    {
        if (!_isClosed) CloseDialog(null);
    }
}
