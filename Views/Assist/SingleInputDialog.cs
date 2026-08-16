using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WC4MapEditor.Views.Assist;

public sealed class SingleInputDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TextBox? _inputTextBox;
    private TaskCompletionSource<string?>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = "输入";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string ConfirmText { get; set; } = "确定";
    public string CancelText { get; set; } = "取消";
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }

    private string ValidationMessage
    {
        get
        {
            if (MinValue.HasValue && MaxValue.HasValue)
                return $"请输入 {MinValue} 到 {MaxValue} 之间的数值";
            if (MinValue.HasValue)
                return $"请输入大于等于 {MinValue} 的数值";
            if (MaxValue.HasValue)
                return $"请输入小于等于 {MaxValue} 的数值";
            return "";
        }
    }

    public SingleInputDialog(Window owner)
    {
        _owner = owner;
    }

    public Task<string?> ShowAsync()
    {
        _tcs = new TaskCompletionSource<string?>();
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
        if (_inputTextBox != null)
            Keyboard.Focus(_inputTextBox);

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

        if (!string.IsNullOrEmpty(Description))
        {
            var descText = new TextBlock
            {
                Text = Description,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stackPanel.Children.Add(descText);
        }

        _inputTextBox = new TextBox
        {
            Text = DefaultValue,
            FontSize = 14,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 20),
            CaretBrush = Brushes.White
        };
        _inputTextBox.KeyDown += (s, e) =>
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
        };
        stackPanel.Children.Add(_inputTextBox);

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

    private void TryConfirm()
    {
        if (_inputTextBox == null) return;

        string inputText = _inputTextBox.Text.Trim();

        if (MinValue.HasValue || MaxValue.HasValue)
        {
            if (!double.TryParse(inputText, out double inputValue))
            {
                ShowValidationError();
                return;
            }

            if (MinValue.HasValue && inputValue < MinValue.Value)
            {
                ShowValidationError();
                return;
            }

            if (MaxValue.HasValue && inputValue > MaxValue.Value)
            {
                ShowValidationError();
                return;
            }
        }

        CloseWithResult(inputText);
    }

    private void ShowValidationError()
    {
        if (_inputTextBox == null || _overlay == null) return;

        var msg = ValidationMessage;
        if (string.IsNullOrEmpty(msg)) return;

        var errorBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 180, 50, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, -16, 0, 8),
            Child = new TextBlock
            {
                Text = msg,
                FontSize = 12,
                Foreground = Brushes.White
            }
        };

        if (_inputTextBox.Parent is StackPanel stackPanel)
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
                if (stackPanel.Children[i] == _inputTextBox)
                {
                    textBoxIndex = i;
                    break;
                }
            }
            if (textBoxIndex >= 0)
                stackPanel.Children.Insert(textBoxIndex + 1, errorBorder);
        }

        _inputTextBox.Focus();
        _inputTextBox.SelectAll();
    }

    private void CloseWithResult(string? result)
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
    }

    public void Dispose()
    {
        if (!_isClosed) CloseWithResult(null);
    }

    public static async Task<string?> ShowDialog(Window owner, string title, string description,
        string defaultValue = "", string confirmText = "确定", string cancelText = "取消",
        double? minValue = null, double? maxValue = null)
    {
        using var dialog = new SingleInputDialog(owner)
        {
            Title = title,
            Description = description,
            DefaultValue = defaultValue,
            ConfirmText = confirmText,
            CancelText = cancelText,
            MinValue = minValue,
            MaxValue = maxValue
        };
        return await dialog.ShowAsync();
    }

    public static async Task<double> ShowNumericDialog(Window owner, string title, string description,
        double defaultValue = 0, double? minValue = null, double? maxValue = null)
    {
        var result = await ShowDialog(owner, title, description, defaultValue.ToString(), minValue: minValue, maxValue: maxValue);
        if (result != null && double.TryParse(result, out double value))
            return value;
        return defaultValue;
    }

    public static async Task<int> ShowIntegerDialog(Window owner, string title, string description,
        int defaultValue = 0, int? minValue = null, int? maxValue = null)
    {
        var result = await ShowDialog(owner, title, description, defaultValue.ToString(),
            minValue: minValue, maxValue: maxValue);
        if (result != null && int.TryParse(result, out int value))
            return value;
        return defaultValue;
    }
}