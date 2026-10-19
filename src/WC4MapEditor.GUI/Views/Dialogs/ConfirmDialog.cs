using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WC4MapEditor.GUI.Views.Dialogs;

public sealed class ConfirmDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TaskCompletionSource<bool>? _tcs;
    private bool _isClosed;

    // 默认文字在构造时取当前语言：对话框对象每次使用都新建，不会缓存旧语言
    public string Title { get; set; } = Loc.T("Dialog.ConfirmTitle");
    public string Message { get; set; } = "";
    public string ConfirmText { get; set; } = Loc.T("Common.Confirm");
    public string CancelText { get; set; } = Loc.T("Common.Cancel");

    /// <summary>
    /// 确认按钮是否用危险色（默认是：删除 / 覆盖这类不可逆操作）。
    /// 普通的二选一（例如"返回主界面"时并没有未保存的修改）设为 false，用主色按钮。
    /// </summary>
    public bool Destructive { get; set; } = true;

    public ConfirmDialog(Window owner)
    {
        _owner = owner;
    }

    public Task<bool> ShowAsync()
    {
        _tcs = new TaskCompletionSource<bool>();
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
            Effect = null,
            Focusable = true
        };
        // 主窗口（渲染场景）本身不套编辑主题，对话框内的按钮/文字要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel();
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);
        // Tab 在对话框内部循环切换焦点（按钮之间），避免焦点跑到覆盖层外面落到主界面的控件上
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);

        rootPanel.Children.Add(_overlay);

        _overlay.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        _overlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var dialogScale = ((Border)((Grid)_overlay.Child).Children[0]).RenderTransform as ScaleTransform;
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
        Keyboard.Focus(_overlay);

        return _tcs.Task;
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWithResult(false);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CloseWithResult(true);
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
            Effect = CreateShadow(),
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

        var messageText = new TextBlock
        {
            Text = Message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24),
            LineHeight = 22
        };
        stackPanel.Children.Add(messageText);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = EditorTheme.Button(CancelText, (_, _) => CloseWithResult(false));
        cancelButton.MinWidth = 88;
        cancelButton.Margin = new Thickness(10, 0, 0, 0);

        var confirmButton = Destructive
            ? EditorTheme.DangerButton(ConfirmText, (_, _) => CloseWithResult(true))
            : EditorTheme.PrimaryButton(ConfirmText, (_, _) => CloseWithResult(true));
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

    private static DropShadowEffect CreateShadow() => new()
    {
        BlurRadius = 20,
        ShadowDepth = 4,
        Opacity = 0.6,
        Color = EditorTheme.ColorOf(EditorTheme.Bg0)
    };

    private void CloseWithResult(bool result)
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
        if (!_isClosed) CloseWithResult(false);
    }
}
