using System.Globalization;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 归属列表对话框（归属编辑模式 Q 键）—— 对齐 VB 版 Assist/BelionList。
/// <para>
/// 选择一个军团（归属值 = 军团在列表里的序号，0 起），或在输入框里直接输入归属值（0-255，255 = 无归属）。
/// 输入可以是十进制、带 0x 的十六进制，或含 A-F 的十六进制；超出 0-255 时在框下提示、不关闭。
/// 地图没有军团时列表为空，仍可手动输入。
/// </para>
/// </summary>
public sealed class BelongListWindow : Window
{
    private readonly List<Legion> _legionList;
    private readonly ListBox _listBox = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBox _inputTextBox = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private bool _syncingInput;

    public int? SelectedValue { get; private set; }
    public bool IsConfirmed { get; private set; }

    public BelongListWindow(List<Legion> legions)
    {
        EditorTheme.Apply(this);
        _legionList = legions ?? new List<Legion>();

        Title = Loc.T("Assist.BelongList.Title");
        Width = 420;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        Content = CreateUI();

        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            fadeIn.Completed += (_, _) =>
            {
                _inputTextBox.Focus();
                _inputTextBox.SelectAll();
            };
            BeginAnimation(OpacityProperty, fadeIn);
        };

        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private FrameworkElement CreateUI()
    {
        var border = new Border
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
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        var titlePanel = CreateTitlePanel();
        Grid.SetRow(titlePanel, 0);
        grid.Children.Add(titlePanel);

        var inputPanel = CreateInputPanel();
        Grid.SetRow(inputPanel, 1);
        grid.Children.Add(inputPanel);

        var listPanel = CreateListPanel();
        Grid.SetRow(listPanel, 2);
        grid.Children.Add(listPanel);

        var buttonPanel = CreateButtonPanel();
        Grid.SetRow(buttonPanel, 3);
        grid.Children.Add(buttonPanel);

        border.Child = grid;
        return border;
    }

    private Grid CreateTitlePanel()
    {
        var panel = new Grid { Margin = new Thickness(15, 10, 15, 5) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleLabel = EditorTheme.Title(Loc.T("Assist.BelongList.Title"));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(titleLabel, 0);
        panel.Children.Add(titleLabel);

        var closeButton = EditorTheme.GhostButton("✕", (_, _) => CancelAndClose());
        closeButton.Width = 28;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.ToolTip = Loc.T("Assist.Common.CloseEsc");
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(closeButton, 1);
        panel.Children.Add(closeButton);

        return panel;
    }

    private StackPanel CreateInputPanel()
    {
        var row = new DockPanel();

        var label = EditorTheme.FieldLabel(Loc.T("Assist.BelongList.ValueLabel"));
        label.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);

        _inputTextBox.ToolTip = Loc.T("Assist.BelongList.InputTip");
        _inputTextBox.TextChanged += (_, _) => OnInputChanged();
        _inputTextBox.PreviewTextInput += InputTextBox_PreviewTextInput;
        row.Children.Add(_inputTextBox);

        _error.Foreground = EditorTheme.Danger;
        _error.FontSize = 11;
        _error.Margin = new Thickness(0, 4, 0, 0);

        var panel = new StackPanel { Margin = new Thickness(15, 5, 15, 5) };
        panel.Children.Add(row);
        panel.Children.Add(_error);
        return panel;
    }

    private Border CreateListPanel()
    {
        // 列表项是直接塞进 Items 的 UIElement，容器回收模式下复用容器会把同一个元素挂到两个父级上，改回标准虚拟化
        VirtualizingPanel.SetVirtualizationMode(_listBox, VirtualizationMode.Standard);

        for (int i = 0; i < _legionList.Count; i++)
            _listBox.Items.Add(CreateLegionListItem(_legionList[i], i));

        _listBox.SelectionChanged += ListBox_SelectionChanged;
        _listBox.MouseDoubleClick += (_, _) =>
        {
            if (_listBox.SelectedIndex >= 0) ConfirmSelection();
        };

        // 主题里的 ListBox 本身无底无框，用一层面板画出列表区域
        var host = EditorTheme.Panel(new Thickness(1));
        host.CornerRadius = new CornerRadius(4);
        host.Margin = new Thickness(15, 5, 15, 5);
        host.Child = _listBox;
        return host;
    }

    /// <summary>列表项：国旗 + "归属值 - 国家名"（显示的就是选中后写入的归属值，即军团序号）。</summary>
    private static Border CreateLegionListItem(Legion legion, int index)
    {
        var panel = new DockPanel { Margin = new Thickness(5) };

        var flagImage = new Image
        {
            Width = 56,
            Height = 42,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0),
            Source = FlagImageLoader.Load(legion.CountryId)
        };
        DockPanel.SetDock(flagImage, Dock.Left);
        panel.Children.Add(flagImage);

        var nameText = new TextBlock
        {
            Text = Loc.F("Assist.BelongList.Item", index, WC4MapEditor.Core.Config.CountryNameProvider.Get(legion.CountryId)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(nameText);

        return new Border { Background = Brushes.Transparent, Child = panel };
    }

    private StackPanel CreateButtonPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(15, 10, 15, 15)
        };

        var confirmButton = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) => ConfirmSelection());
        confirmButton.MinWidth = 80;
        confirmButton.Margin = new Thickness(0, 0, 10, 0);
        confirmButton.ToolTip = Loc.T("Assist.Common.ConfirmEnter");
        panel.Children.Add(confirmButton);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.MinWidth = 80;
        cancelButton.ToolTip = Loc.T("Assist.Common.CancelEsc");
        panel.Children.Add(cancelButton);

        return panel;
    }

    // ------------------------------------------------------------------ 输入

    /// <summary>
    /// 解析归属值：带 0x 的、或含 A-F 字母的按十六进制，否则按十进制；必须在 0-255 内。
    /// VB 版把不带 0x 的十六进制当十进制解析失败（确认等于取消），又把 0x 值按十进制检查范围（0x100 被放行）。
    /// </summary>
    private static bool TryParseBelong(string text, out int value)
    {
        value = -1;
        string s = text.Trim();
        if (s.Length == 0) return false;

        bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) s = s[2..];
        if (!hex && s.Any(c => !char.IsDigit(c))) hex = true;

        bool ok = hex
            ? int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return ok && value >= 0 && value <= 255;
    }

    private void OnInputChanged()
    {
        if (_syncingInput) return;
        string text = _inputTextBox.Text.Trim();
        if (text.Length == 0 || TryParseBelong(text, out _))
        {
            _error.Visibility = Visibility.Collapsed;
            return;
        }
        ShowError(Loc.T("Assist.BelongList.InvalidValue", "归属值应为 0-255（十进制，或 0x 开头的十六进制）"));
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    /// <summary>只允许输入数字、十六进制字母与 0x 前缀。</summary>
    private void InputTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var newText = _inputTextBox.Text + e.Text;
        if (newText.Equals("0x", StringComparison.OrdinalIgnoreCase)) return;

        var checkText = newText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? newText[2..] : newText;
        if (checkText.ToUpperInvariant().Any(c => !char.IsDigit(c) && (c < 'A' || c > 'F')))
            e.Handled = true;
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = _listBox.SelectedIndex;
        if (index < 0 || index >= _legionList.Count) return;

        // 归属值 = 军团在列表中的位置索引
        _syncingInput = true;
        _inputTextBox.Text = index.ToString(CultureInfo.InvariantCulture);
        _syncingInput = false;
        _error.Visibility = Visibility.Collapsed;
    }

    /// <summary>输入框有值时以输入为准（必须是 0-255），否则取列表选中项的序号；都没有时提示而不是当成取消。</summary>
    private void ConfirmSelection()
    {
        string text = _inputTextBox.Text.Trim();
        int value;
        if (text.Length > 0)
        {
            if (!TryParseBelong(text, out value))
            {
                ShowError(Loc.T("Assist.BelongList.InvalidValue", "归属值应为 0-255（十进制，或 0x 开头的十六进制）"));
                return;
            }
        }
        else if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _legionList.Count)
        {
            value = _listBox.SelectedIndex;
        }
        else
        {
            ShowError(Loc.T("Assist.BelongList.NothingChosen", "请输入归属值，或在列表中选择一个军团"));
            return;
        }

        SelectedValue = value;
        IsConfirmed = true;
        DialogResult = true;
    }

    private void CancelAndClose()
    {
        IsConfirmed = false;
        SelectedValue = null;
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ConfirmSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelAndClose();
                e.Handled = true;
                break;
            case Key.Tab when _legionList.Count > 0:
                _listBox.SelectedIndex = (_listBox.SelectedIndex + 1) % _legionList.Count;
                _listBox.ScrollIntoView(_listBox.SelectedItem);
                e.Handled = true;
                break;
        }
    }
}
