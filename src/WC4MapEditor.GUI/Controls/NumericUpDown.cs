using System.Globalization;
using WC4MapEditor.GUI.Theme;

namespace WC4MapEditor.GUI.Controls;

/// <summary>
/// 数字输入框：文本框 + 右侧上下微调按钮，支持范围与步长。
/// <para>原先定义在 GeneralEditScene.cs 末尾、被十个场景共用，移到这里并改用主题配色。</para>
/// </summary>
public class NumericUpDown : UserControl
{
    private readonly TextBox _textBox;
    private double _value;
    private bool _updating;

    public double MinValue { get; set; } = 0;
    public double MaxValue { get; set; } = 100;
    public double Increment { get; set; } = 1;

    public event EventHandler<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, MinValue, MaxValue);
            if (Math.Abs(v - _value) < 1e-12) return;
            _value = v;
            UpdateTextBox();
            ValueChanged?.Invoke(this, _value);
        }
    }

    public NumericUpDown()
    {
        Focusable = false;
        Height = 28;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _textBox = new TextBox
        {
            BorderThickness = new Thickness(1, 1, 0, 1),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 13,
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _textBox.TextChanged += TextBox_TextChanged;
        _textBox.LostKeyboardFocus += (_, _) => UpdateTextBox();
        _textBox.PreviewMouseWheel += (_, e) =>
        {
            if (!_textBox.IsKeyboardFocused) return;
            Value += e.Delta > 0 ? Increment : -Increment;
            e.Handled = true;
        };
        _textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Value += Increment; e.Handled = true; }
            else if (e.Key == Key.Down) { Value -= Increment; e.Handled = true; }
        };

        var spinner = new Border
        {
            Background = EditorTheme.Bg3,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 1, 1),
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            Width = 18
        };
        var rightPanel = new Grid();
        rightPanel.RowDefinitions.Add(new RowDefinition());
        rightPanel.RowDefinitions.Add(new RowDefinition());
        var up = MakeSpin("▲", (_, _) => Value += Increment);
        var down = MakeSpin("▼", (_, _) => Value -= Increment);
        Grid.SetRow(down, 1);
        rightPanel.Children.Add(up);
        rightPanel.Children.Add(down);
        spinner.Child = rightPanel;

        Grid.SetColumn(_textBox, 0);
        Grid.SetColumn(spinner, 1);
        g.Children.Add(_textBox);
        g.Children.Add(spinner);
        Content = g;
        Loaded += (_, _) => UpdateTextBox();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (double.TryParse(_textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            var old = _value;
            _value = Math.Clamp(v, MinValue, MaxValue);
            if (Math.Abs(old - _value) > 1e-12) ValueChanged?.Invoke(this, _value);
        }
    }

    private void UpdateTextBox()
    {
        _updating = true;
        string format = Math.Abs(Increment - Math.Truncate(Increment)) < 1e-12 ? "F0" : "0.0##";
        _textBox.Text = _value.ToString(format, CultureInfo.InvariantCulture);
        _updating = false;
    }

    private static Button MakeSpin(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = EditorTheme.TextMuted,
            FontSize = 7,
            Padding = new Thickness(0),
            MinHeight = 0,
            Cursor = Cursors.Hand,
            Focusable = false
        };
        b.Click += click;
        return b;
    }
}
