using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WC4MapEditor.GUI.Views.Dialogs;

public sealed class SingleInputDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private Border? _dialogPanel;
    private TextBox? _inputBox;
    private TaskCompletionSource<string?>? _tcs;
    private bool _isClosed;

    // 默认文字在构造时取当前语言：对话框对象每次使用都新建，不会缓存旧语言
    public string Title { get; set; } = Loc.T("Dialog.InputTitle");
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string ConfirmText { get; set; } = Loc.T("Common.Ok");
    public string CancelText { get; set; } = Loc.T("Common.Cancel");
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
            Background = CreateScrim(),
            Focusable = true
        };
        // 主窗口（渲染场景）本身不套编辑主题，对话框内的输入框/按钮要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel();
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
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Width = 340,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6,
                Color = EditorTheme.ColorOf(EditorTheme.Bg0)
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var stack = new StackPanel();

        var titleBlock = EditorTheme.Title(Title);
        titleBlock.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(titleBlock);

        var descBlock = EditorTheme.Muted(Description);
        descBlock.TextWrapping = TextWrapping.Wrap;
        descBlock.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(descBlock);

        _inputBox = new TextBox
        {
            Text = DefaultValue,
            Margin = new Thickness(0, 0, 0, 16)
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        stack.Children.Add(_inputBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = EditorTheme.Button(CancelText, CancelButton_Click);
        cancelButton.MinWidth = 80;
        cancelButton.Margin = new Thickness(0, 0, 8, 0);
        buttonPanel.Children.Add(cancelButton);

        var confirmButton = EditorTheme.PrimaryButton(ConfirmText, ConfirmButton_Click);
        confirmButton.MinWidth = 80;
        buttonPanel.Children.Add(confirmButton);

        stack.Children.Add(buttonPanel);
        _dialogPanel.Child = stack;

        return _dialogPanel;
    }

    /// <summary>覆盖层的半透明遮罩：由主题最深底色派生，不写死颜色。</summary>
    private static Brush CreateScrim()
    {
        var c = EditorTheme.ColorOf(EditorTheme.Bg0);
        return new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
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
                MessageBox.Show(GetValidationMessage(), Loc.T("Common.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _inputBox.Focus();
                _inputBox.SelectAll();
                return;
            }

            if (MinValue.HasValue && inputValue < MinValue.Value)
            {
                MessageBox.Show(GetValidationMessage(), Loc.T("Common.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _inputBox.Focus();
                _inputBox.SelectAll();
                return;
            }

            if (MaxValue.HasValue && inputValue > MaxValue.Value)
            {
                MessageBox.Show(GetValidationMessage(), Loc.T("Common.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            return Loc.F("Common.InputRange", MinValue, MaxValue);
        if (MinValue.HasValue)
            return Loc.F("Common.InputMin", MinValue);
        if (MaxValue.HasValue)
            return Loc.F("Common.InputMax", MaxValue);
        return Loc.T("Common.InvalidInput");
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
