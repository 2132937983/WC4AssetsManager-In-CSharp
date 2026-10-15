using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WC4MapEditor.Views.Assist;

public sealed class DoubleInputDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TextBox? _inputTextBox1;
    private TextBox? _inputTextBox2;
    private TaskCompletionSource<(string? Value1, string? Value2)?>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = "输入";
    public string Description1 { get; set; } = "";
    public string Description2 { get; set; } = "";
    public string DefaultValue1 { get; set; } = "";
    public string DefaultValue2 { get; set; } = "";
    public string ConfirmText { get; set; } = "确定";
    public string CancelText { get; set; } = "取消";
    public double? MinValue1 { get; set; }
    public double? MaxValue1 { get; set; }
    public double? MinValue2 { get; set; }
    public double? MaxValue2 { get; set; }
    public List<double>? AllowedSpecialValues1 { get; set; }
    public List<double>? AllowedSpecialValues2 { get; set; }

    private string ValidationMessage1
    {
        get
        {
            if (MinValue1.HasValue && MaxValue1.HasValue)
                return $"请输入 {MinValue1} 到 {MaxValue1} 之间的数值";
            if (MinValue1.HasValue)
                return $"请输入大于等于 {MinValue1} 的数值";
            if (MaxValue1.HasValue)
                return $"请输入小于等于 {MaxValue1} 的数值";
            return "";
        }
    }

    private string ValidationMessage2
    {
        get
        {
            if (MinValue2.HasValue && MaxValue2.HasValue)
                return $"请输入 {MinValue2} 到 {MaxValue2} 之间的数值";
            if (MinValue2.HasValue)
                return $"请输入大于等于 {MinValue2} 的数值";
            if (MaxValue2.HasValue)
                return $"请输入小于等于 {MaxValue2} 的数值";
            return "";
        }
    }

    public DoubleInputDialog(Window owner)
    {
        _owner = owner;
    }

    public Task<(string? Value1, string? Value2)?> ShowAsync()
    {
        _tcs = new TaskCompletionSource<(string? Value1, string? Value2)?> ();
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
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);
        // Tab 在对话框内部循环切换焦点（输入框1 → 输入框2 → 按钮），
        // 避免焦点跑到覆盖层外面落到主界面的控件上
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);

        rootPanel.Children.Add(_overlay);

        _overlay.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        _overlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var dialogBorder = ((Grid)_overlay.Child).Children[0] as Border;
        var dialogScale = dialogBorder?.RenderTransform as ScaleTransform;
        if (dialogScale != null)
        {
            var scaleX = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var scaleY = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            dialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            dialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        _overlay.Focus();
        if (_inputTextBox1 != null)
            Keyboard.Focus(_inputTextBox1);

        return _tcs.Task;
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWithResult(null);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryConfirm();
        }
    }

    private UIElement CreateDialogPanel()
    {
        var container = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dialogBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            MinWidth = 340,
            MaxWidth = 480,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Opacity = 0.7,
                Color = Colors.Black
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.9, 0.9)
        };

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20)
        };

        var titleText = new TextBlock
        {
            Text = Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        stackPanel.Children.Add(titleText);

        var separator = new Separator
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stackPanel.Children.Add(separator);

        if (!string.IsNullOrEmpty(Description1))
        {
            var descText1 = new TextBlock
            {
                Text = Description1,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stackPanel.Children.Add(descText1);
        }

        _inputTextBox1 = new TextBox
        {
            Text = DefaultValue1,
            FontSize = 14,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 12),
            CaretBrush = Brushes.White
        };
        _inputTextBox1.KeyDown += Input_KeyDown;
        stackPanel.Children.Add(_inputTextBox1);

        if (!string.IsNullOrEmpty(Description2))
        {
            var descText2 = new TextBlock
            {
                Text = Description2,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stackPanel.Children.Add(descText2);
        }

        _inputTextBox2 = new TextBox
        {
            Text = DefaultValue2,
            FontSize = 14,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 20),
            CaretBrush = Brushes.White
        };
        _inputTextBox2.KeyDown += Input_KeyDown;
        stackPanel.Children.Add(_inputTextBox2);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = new Button
        {
            Content = CancelText,
            Width = 90,
            Height = 34,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        cancelButton.Click += (s, e) => CloseWithResult(null);

        var confirmButton = new Button
        {
            Content = ConfirmText,
            Width = 90,
            Height = 34,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 122, 204)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        confirmButton.Click += (s, e) => TryConfirm();

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(confirmButton);
        stackPanel.Children.Add(buttonPanel);

        dialogBorder.Child = stackPanel;
        container.Children.Add(dialogBorder);

        return container;
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryConfirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWithResult(null);
        }
    }

    private void TryConfirm()
    {
        if (_inputTextBox1 == null || _inputTextBox2 == null) return;

        string inputText1 = _inputTextBox1.Text.Trim();
        string inputText2 = _inputTextBox2.Text.Trim();

        if (MinValue1.HasValue || MaxValue1.HasValue)
        {
            if (!double.TryParse(inputText1, out double inputValue1))
            {
                ShowValidationError(_inputTextBox1, ValidationMessage1);
                return;
            }
            if (MinValue1.HasValue && inputValue1 < MinValue1.Value)
            {
                ShowValidationError(_inputTextBox1, ValidationMessage1);
                return;
            }
            if (MaxValue1.HasValue && inputValue1 > MaxValue1.Value)
            {
                ShowValidationError(_inputTextBox1, ValidationMessage1);
                return;
            }
        }

        if (MinValue2.HasValue || MaxValue2.HasValue)
        {
            if (!double.TryParse(inputText2, out double inputValue2))
            {
                ShowValidationError(_inputTextBox2, ValidationMessage2);
                return;
            }

            bool isSpecialValue = AllowedSpecialValues2 != null && AllowedSpecialValues2.Contains(inputValue2);
            if (!isSpecialValue)
            {
                if (MinValue2.HasValue && inputValue2 < MinValue2.Value)
                {
                    ShowValidationError(_inputTextBox2, ValidationMessage2);
                    return;
                }
                if (MaxValue2.HasValue && inputValue2 > MaxValue2.Value)
                {
                    ShowValidationError(_inputTextBox2, ValidationMessage2);
                    return;
                }
            }
        }

        CloseWithResult((inputText1, inputText2));
    }

    private void ShowValidationError(TextBox textBox, string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        var errorBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 180, 50, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, -16, 0, 8),
            Child = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = Brushes.White
            }
        };

        if (textBox.Parent is StackPanel stackPanel)
        {
            for (int i = 0; i < stackPanel.Children.Count; i++)
            {
                if (stackPanel.Children[i] is Border b && b.Tag as string == "validation")
                {
                    stackPanel.Children.RemoveAt(i);
                    break;
                }
            }
            errorBorder.Tag = "validation";
            int textBoxIndex = -1;
            for (int i = 0; i < stackPanel.Children.Count; i++)
            {
                if (stackPanel.Children[i] == textBox)
                {
                    textBoxIndex = i;
                    break;
                }
            }
            if (textBoxIndex >= 0)
                stackPanel.Children.Insert(textBoxIndex + 1, errorBorder);
        }

        textBox.Focus();
        textBox.SelectAll();
    }

    private void CloseWithResult((string? Value1, string? Value2)? result)
    {
        if (_isClosed) return;
        _isClosed = true;

        if (_overlay != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (s, e) =>
            {
                RemoveOverlay();
                _tcs?.SetResult(result);
            };
            _overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            _tcs?.SetResult(result);
        }
    }

    private void RemoveOverlay()
    {
        if (_overlay == null) return;

        var content = _owner.Content as FrameworkElement;
        Panel? rootPanel = content as Panel ?? content?.Parent as Panel;
        if (rootPanel != null)
            rootPanel.Children.Remove(_overlay);
        _overlay.KeyDown -= Overlay_KeyDown;
        _overlay = null;

        RestoreOwnerFocus();

        _owner.Dispatcher.BeginInvoke(() =>
        {
            RestoreOwnerFocus();
        }, DispatcherPriority.Input);
    }

    private void RestoreOwnerFocus()
    {
        var skElement = FindVisualChild<SkiaSharp.Views.WPF.SKElement>(_owner);
        if (skElement != null)
            Keyboard.Focus(skElement);
        else
            _owner.Focus();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    public void Dispose()
    {
        if (!_isClosed) CloseWithResult(null);
    }

    public static async Task<(string? Value1, string? Value2)?> ShowDialog(Window owner, string title,
        string description1, string description2,
        string defaultValue1 = "", string defaultValue2 = "",
        string confirmText = "确定", string cancelText = "取消",
        double? minValue1 = null, double? maxValue1 = null,
        double? minValue2 = null, double? maxValue2 = null,
        List<double>? allowedSpecialValues1 = null,
        List<double>? allowedSpecialValues2 = null)
    {
        using var dialog = new DoubleInputDialog(owner)
        {
            Title = title,
            Description1 = description1,
            Description2 = description2,
            DefaultValue1 = defaultValue1,
            DefaultValue2 = defaultValue2,
            ConfirmText = confirmText,
            CancelText = cancelText,
            MinValue1 = minValue1,
            MaxValue1 = maxValue1,
            MinValue2 = minValue2,
            MaxValue2 = maxValue2,
            AllowedSpecialValues1 = allowedSpecialValues1,
            AllowedSpecialValues2 = allowedSpecialValues2
        };
        return await dialog.ShowAsync();
    }

    public static async Task<(double Value1, double Value2)> ShowNumericDialog(Window owner, string title,
        string description1, string description2,
        double defaultValue1 = 0, double defaultValue2 = 0)
    {
        var result = await ShowDialog(owner, title, description1, description2,
            defaultValue1.ToString(), defaultValue2.ToString());
        if (result.HasValue)
        {
            double v1 = defaultValue1, v2 = defaultValue2;
            if (result.Value.Value1 != null) double.TryParse(result.Value.Value1, out v1);
            if (result.Value.Value2 != null) double.TryParse(result.Value.Value2, out v2);
            return (v1, v2);
        }
        return (defaultValue1, defaultValue2);
    }

    public static async Task<(int Value1, int Value2)> ShowIntegerDialog(Window owner, string title,
        string description1, string description2,
        int defaultValue1 = 0, int defaultValue2 = 0)
    {
        var result = await ShowDialog(owner, title, description1, description2,
            defaultValue1.ToString(), defaultValue2.ToString());
        if (result.HasValue)
        {
            int v1 = defaultValue1, v2 = defaultValue2;
            if (result.Value.Value1 != null) int.TryParse(result.Value.Value1, out v1);
            if (result.Value.Value2 != null) int.TryParse(result.Value.Value2, out v2);
            return (v1, v2);
        }
        return (defaultValue1, defaultValue2);
    }
}