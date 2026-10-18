using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WC4MapEditor.GUI.Views.Assist;

public sealed class DoubleInputDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TextBox? _inputTextBox1;
    private TextBox? _inputTextBox2;
    private TaskCompletionSource<(string? Value1, string? Value2)?>? _tcs;
    private bool _isClosed;

    // 默认文字在构造时取当前语言：对话框对象每次使用都新建，不会缓存旧语言
    public string Title { get; set; } = Loc.T("Dialog.InputTitle");
    public string Description1 { get; set; } = "";
    public string Description2 { get; set; } = "";
    public string DefaultValue1 { get; set; } = "";
    public string DefaultValue2 { get; set; } = "";
    public string ConfirmText { get; set; } = Loc.T("Common.Ok");
    public string CancelText { get; set; } = Loc.T("Common.Cancel");
    public double? MinValue1 { get; set; }
    public double? MaxValue1 { get; set; }
    public double? MinValue2 { get; set; }
    public double? MaxValue2 { get; set; }
    public List<double>? AllowedSpecialValues1 { get; set; }
    public List<double>? AllowedSpecialValues2 { get; set; }

    private string ValidationMessage1 => BuildRangeMessage(MinValue1, MaxValue1);
    private string ValidationMessage2 => BuildRangeMessage(MinValue2, MaxValue2);

    private static string BuildRangeMessage(double? min, double? max)
    {
        if (min.HasValue && max.HasValue)
            return Loc.F("Common.InputRange", min, max);
        if (min.HasValue)
            return Loc.F("Common.InputMin", min);
        if (max.HasValue)
            return Loc.F("Common.InputMax", max);
        return "";
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
            Background = CreateScrim(),
            Focusable = true
        };
        // 主窗口（渲染场景）本身不套编辑主题，对话框内的输入框/按钮要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel();
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
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinWidth = 340,
            MaxWidth = 480,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6,
                Color = EditorTheme.ColorOf(EditorTheme.Bg0)
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.9, 0.9)
        };

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20)
        };

        var titleText = EditorTheme.Title(Title);
        titleText.Margin = new Thickness(0, 0, 0, 10);
        stackPanel.Children.Add(titleText);

        stackPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });

        if (!string.IsNullOrEmpty(Description1))
        {
            var descText1 = EditorTheme.FieldLabel(Description1);
            descText1.Margin = new Thickness(0, 0, 0, 6);
            stackPanel.Children.Add(descText1);
        }

        _inputTextBox1 = new TextBox
        {
            Text = DefaultValue1,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _inputTextBox1.KeyDown += Input_KeyDown;
        stackPanel.Children.Add(_inputTextBox1);

        if (!string.IsNullOrEmpty(Description2))
        {
            var descText2 = EditorTheme.FieldLabel(Description2);
            descText2.Margin = new Thickness(0, 0, 0, 6);
            stackPanel.Children.Add(descText2);
        }

        _inputTextBox2 = new TextBox
        {
            Text = DefaultValue2,
            Margin = new Thickness(0, 0, 0, 20)
        };
        _inputTextBox2.KeyDown += Input_KeyDown;
        stackPanel.Children.Add(_inputTextBox2);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = EditorTheme.Button(CancelText, (_, _) => CloseWithResult(null));
        cancelButton.MinWidth = 88;
        cancelButton.Margin = new Thickness(10, 0, 0, 0);

        var confirmButton = EditorTheme.PrimaryButton(ConfirmText, (_, _) => TryConfirm());
        confirmButton.MinWidth = 88;
        confirmButton.Margin = new Thickness(10, 0, 0, 0);

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(confirmButton);
        stackPanel.Children.Add(buttonPanel);

        dialogBorder.Child = stackPanel;
        container.Children.Add(dialogBorder);

        return container;
    }

    /// <summary>覆盖层的半透明遮罩：由主题最深底色派生，不写死颜色。</summary>
    private static Brush CreateScrim()
    {
        var c = EditorTheme.ColorOf(EditorTheme.Bg0);
        return new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
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
            Background = EditorTheme.Bg3,
            BorderBrush = EditorTheme.Danger,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, -8, 0, 8),
            Child = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = EditorTheme.Danger,
                TextWrapping = TextWrapping.Wrap
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

    // 按钮文字默认值不能是编译期常量的 Loc 调用，所以用 null 表示"取当前语言的默认文字"
    public static async Task<(string? Value1, string? Value2)?> ShowDialog(Window owner, string title,
        string description1, string description2,
        string defaultValue1 = "", string defaultValue2 = "",
        string? confirmText = null, string? cancelText = null,
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
            ConfirmText = confirmText ?? Loc.T("Common.Ok"),
            CancelText = cancelText ?? Loc.T("Common.Cancel"),
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
