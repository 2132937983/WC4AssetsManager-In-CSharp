using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WC4MapEditor.Views.Dialogs;

public sealed class ConfirmDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private Border? _dialogPanel;
    private TaskCompletionSource<bool>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = "确认";
    public string Message { get; set; } = "";
    public string ConfirmText { get; set; } = "确认";
    public string CancelText { get; set; } = "取消";

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
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            Child = CreateDialogPanel(),
            Effect = null,
            Focusable = true
        };
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);

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

        var titleBar = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        var titleText = new TextBlock
        {
            Text = Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(titleText, Dock.Left);
        titleBar.Children.Add(titleText);

        var separator = new Separator
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var messageText = new TextBlock
        {
            Text = Message,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24),
            LineHeight = 22
        };

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
        cancelButton.Click += (s, e) => CloseWithResult(false);

        var confirmButton = new Button
        {
            Content = ConfirmText,
            Width = 90,
            Height = 34,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(180, 220, 80, 60)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        confirmButton.Click += (s, e) => CloseWithResult(true);

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(confirmButton);

        stackPanel.Children.Add(titleBar);
        stackPanel.Children.Add(separator);
        stackPanel.Children.Add(messageText);
        stackPanel.Children.Add(buttonPanel);

        dialogBorder.Child = stackPanel;
        container.Children.Add(dialogBorder);

        return container;
    }

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