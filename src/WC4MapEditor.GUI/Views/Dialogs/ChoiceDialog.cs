using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WC4MapEditor.GUI.Views.Dialogs;

/// <summary>
/// 多选一对话框：标题 + 说明 + 一列选项按钮 + 取消。返回所选选项的序号，取消 / Esc 返回 -1。
/// <para>
/// 与 <see cref="ConfirmDialog"/> 一样挂在主窗口根面板上（ZIndex 9999，主窗口据此按模态处理窗口级快捷键），
/// 打开期间键盘焦点留在对话框内：场景快捷键不会在背后继续触发，重复按也不会叠出第二个对话框。
/// </para>
/// </summary>
public sealed class ChoiceDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TaskCompletionSource<int>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = Loc.T("Dialog.ConfirmTitle");
    public string Message { get; set; } = "";
    public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();
    public string CancelText { get; set; } = Loc.T("Common.Cancel");

    public ChoiceDialog(Window owner)
    {
        _owner = owner;
    }

    public Task<int> ShowAsync()
    {
        _tcs = new TaskCompletionSource<int>();
        _isClosed = false;

        var content = _owner.Content as FrameworkElement;
        Panel rootPanel = content as Panel
            ?? content?.Parent as Panel
            ?? throw new InvalidOperationException("Cannot find a suitable root panel for the dialog overlay.");

        _overlay = new Border { Background = CreateScrim(), Focusable = true };
        // 主窗口（渲染场景）本身不套编辑主题，对话框里的按钮要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel(out var firstButton);
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        rootPanel.Children.Add(_overlay);

        _overlay.Opacity = 0;
        _overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

        if (firstButton != null)
        {
            firstButton.Focus();
            Keyboard.Focus(firstButton);
        }
        else
        {
            _overlay.Focus();
            Keyboard.Focus(_overlay);
        }

        return _tcs.Task;
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseWithResult(-1);
    }

    private UIElement CreateDialogPanel(out Button? firstButton)
    {
        var dialogBorder = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinWidth = 340,
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.6, Color = EditorTheme.ColorOf(EditorTheme.Bg0) }
        };

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        var title = EditorTheme.Title(Title);
        title.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(title);
        stack.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });

        if (!string.IsNullOrEmpty(Message))
        {
            stack.Children.Add(new TextBlock
            {
                Text = Message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = EditorTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 16),
                LineHeight = 20
            });
        }

        firstButton = null;
        for (int i = 0; i < Choices.Count; i++)
        {
            int index = i;
            var button = i == 0
                ? EditorTheme.PrimaryButton(Choices[i], (_, _) => CloseWithResult(index))
                : EditorTheme.Button(Choices[i], (_, _) => CloseWithResult(index));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(button);
            firstButton ??= button;
        }

        var cancel = EditorTheme.Button(CancelText, (_, _) => CloseWithResult(-1));
        cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancel.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(cancel);

        dialogBorder.Child = stack;
        return dialogBorder;
    }

    /// <summary>覆盖层的半透明遮罩：由主题最深底色派生，不写死颜色。</summary>
    private static Brush CreateScrim()
    {
        var c = EditorTheme.ColorOf(EditorTheme.Bg0);
        return new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
    }

    private void CloseWithResult(int result)
    {
        if (_isClosed) return;
        _isClosed = true;

        if (_overlay == null)
        {
            _tcs?.TrySetResult(result);
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            RemoveOverlay();
            _tcs?.TrySetResult(result);
        };
        _overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null) return;
        var content = _owner.Content as FrameworkElement;
        (content as Panel ?? content?.Parent as Panel)?.Children.Remove(_overlay);
        _overlay.KeyDown -= Overlay_KeyDown;
        _overlay = null;
    }

    public void Dispose()
    {
        if (!_isClosed) CloseWithResult(-1);
    }
}
