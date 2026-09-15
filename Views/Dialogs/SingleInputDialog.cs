using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WC4MapEditor.Views.Dialogs;

public sealed class SingleInputDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private Border? _dialogPanel;
    private TextBox? _inputBox;
    private TaskCompletionSource<string?>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = "输入";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string ConfirmText { get; set; } = "确定";
    public string CancelText { get; set; } = "取消";
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }

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
        // Tab 在对话框内部循环切换焦点，避免焦点跑到覆盖层外面落到主界面的控件上
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        rootPanel.Children.Add(_overlay);

        _inputBox?.Focus();
        _inputBox?.SelectAll();

        return _tcs.Task;
    }

    private UIElement CreateDialogPanel()
    {
        _dialogPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Width = 340,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        _dialogPanel.HorizontalAlignment = HorizontalAlignment.Center;
        _dialogPanel.VerticalAlignment = VerticalAlignment.Center;

        var stack = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = Title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(titleBlock);

        var descBlock = new TextBlock
        {
            Text = Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(descBlock);

        _inputBox = new TextBox
        {
            Text = DefaultValue,
            FontSize = 13,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16),
            CaretBrush = Brushes.White
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        stack.Children.Add(_inputBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = new Button
        {
            Content = CancelText,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        cancelButton.Click += CancelButton_Click;
        buttonPanel.Children.Add(cancelButton);

        var confirmButton = new Button
        {
            Content = ConfirmText,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6, 16, 6),
            Cursor = Cursors.Hand
        };
        confirmButton.Click += ConfirmButton_Click;
        buttonPanel.Children.Add(confirmButton);

        stack.Children.Add(buttonPanel);
        _dialogPanel.Child = stack;

        return _dialogPanel;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelButton_Click(sender, e);
        }
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelButton_Click(sender, e);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputBox == null) return;

        string inputText = _inputBox.Text.Trim();

        if (MinValue.HasValue || MaxValue.HasValue)
        {
            if (!double.TryParse(inputText, out double inputValue))
            {
                MessageBox.Show(GetValidationMessage(), "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                _inputBox.Focus();
                _inputBox.SelectAll();
                return;
            }

            if (MinValue.HasValue && inputValue < MinValue.Value)
            {
                MessageBox.Show(GetValidationMessage(), "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                _inputBox.Focus();
                _inputBox.SelectAll();
                return;
            }

            if (MaxValue.HasValue && inputValue > MaxValue.Value)
            {
                MessageBox.Show(GetValidationMessage(), "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                _inputBox.Focus();
                _inputBox.SelectAll();
                return;
            }
        }

        CloseDialog(inputText);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog(null);
    }

    private string GetValidationMessage()
    {
        if (MinValue.HasValue && MaxValue.HasValue)
            return $"请输入 {MinValue} 到 {MaxValue} 之间的数值";
        if (MinValue.HasValue)
            return $"请输入大于等于 {MinValue} 的数值";
        if (MaxValue.HasValue)
            return $"请输入小于等于 {MaxValue} 的数值";
        return "输入无效";
    }

    private void CloseDialog(string? result)
    {
        if (_isClosed) return;
        _isClosed = true;

        var content = _owner.Content as FrameworkElement;
        if (content is Panel rootPanel && _overlay != null)
        {
            rootPanel.Children.Remove(_overlay);
        }

        RestoreOwnerFocus();

        _owner.Dispatcher.BeginInvoke(() =>
        {
            RestoreOwnerFocus();
        }, System.Windows.Threading.DispatcherPriority.Input);

        _tcs?.SetResult(result);
    }

    private void RestoreOwnerFocus()
    {
        var skElement = FindVisualChild<SkiaSharp.Views.WPF.SKElement>(_owner);
        if (skElement != null)
        {
            Keyboard.Focus(skElement);
        }
        else
        {
            _owner.Focus();
        }
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
        if (!_isClosed) CloseDialog(null);
    }
}