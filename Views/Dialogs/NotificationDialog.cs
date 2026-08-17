using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WC4MapEditor.Views.Dialogs;

public sealed class NotificationDialog : IDisposable
{
    private readonly Window _owner;
    private Border? _overlay;
    private TaskCompletionSource<bool>? _tcs;
    private bool _isClosed;

    public string Title { get; set; } = "通知";
    public string Message { get; set; } = "";
    public string ButtonText { get; set; } = "确定";
    public Brush AccentBrush { get; set; } = new SolidColorBrush(Color.FromArgb(180, 80, 160, 220));

    public NotificationDialog(Window owner)
    {
        _owner = owner;
    }

    public Task ShowAsync()
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
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            e.Handled = true;
            CloseWithResult();
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

        var confirmButton = new Button
        {
            Content = ButtonText,
            Width = 90,
            Height = 34,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            Background = AccentBrush,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        confirmButton.Click += (s, e) => CloseWithResult();

        buttonPanel.Children.Add(confirmButton);

        stackPanel.Children.Add(titleBar);
        stackPanel.Children.Add(separator);
        stackPanel.Children.Add(messageText);
        stackPanel.Children.Add(buttonPanel);

        dialogBorder.Child = stackPanel;
        container.Children.Add(dialogBorder);

        return container;
    }

    private void CloseWithResult()
    {
        if (_isClosed) return;
        _isClosed = true;

        if (_overlay != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (s, e) =>
            {
                RemoveOverlay();
                _tcs?.SetResult(true);
            };
            _overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            _tcs?.SetResult(true);
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
        if (!_isClosed) CloseWithResult();
    }
}