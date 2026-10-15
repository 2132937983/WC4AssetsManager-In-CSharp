using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WC4MapEditor.Views.Dialogs;

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

    public string Title { get; set; } = "编队/等级数值模拟";
    public string Description { get; set; } = "按等级与编队重算 HP / 攻击 / 防御（可重复叠加，与原工具一致）。";
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
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            Child = CreateDialogPanel(),
            Focusable = true
        };
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

        stack.Children.Add(new TextBlock
        {
            Text = Title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        _levelCombo = MakeCombo(new[] { "1级", "2级", "3级", "4级", "5级", "6级" });
        stack.Children.Add(MakeRow("选择等级", _levelCombo));

        _formationCombo = MakeCombo(new[] { "单编队", "两编队", "三编队", "四编队" });
        stack.Children.Add(MakeRow("选择编队", _formationCombo));

        var scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
        _scopeCurrent = new RadioButton
        {
            Content = "当前兵种",
            GroupName = "simScope",
            IsChecked = true,
            IsEnabled = HasCurrentUnit,
            Foreground = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(0, 0, 16, 0)
        };
        _scopeAll = new RadioButton
        {
            Content = "列表全部",
            GroupName = "simScope",
            Foreground = Brushes.White,
            FontSize = 12
        };
        if (!HasCurrentUnit) _scopeAll.IsChecked = true;
        scopePanel.Children.Add(_scopeCurrent);
        scopePanel.Children.Add(_scopeAll);
        stack.Children.Add(MakeRow("作用范围", scopePanel));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "取消",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        cancelButton.Click += (_, _) => CloseDialog(null);
        buttonPanel.Children.Add(cancelButton);

        var applyButton = new Button
        {
            Content = "应用到兵种",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0xA2, 0xB8)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6, 16, 6),
            Cursor = Cursors.Hand
        };
        applyButton.Click += (_, _) => CloseDialog(new SimulatorResult(
            (_levelCombo?.SelectedIndex ?? 0) + 1,
            (_formationCombo?.SelectedIndex ?? 0) + 1,
            _scopeAll?.IsChecked == true));
        buttonPanel.Children.Add(applyButton);

        stack.Children.Add(buttonPanel);

        _dialogPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Width = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = Colors.Black
            },
            Child = stack
        };

        return _dialogPanel;
    }

    private static ComboBox MakeCombo(string[] items)
    {
        var combo = new ComboBox
        {
            Width = 180,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90))
        };
        foreach (var i in items) combo.Items.Add(i);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static UIElement MakeRow(string label, UIElement content)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
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
