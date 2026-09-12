using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Views.Assist;

public sealed class LegionBelongListWindow : Window
{
    private readonly List<Legion> _legionList;
    private readonly Dictionary<int, BitmapImage> _flagCache = new();
    private ListBox? _listBox;
    private int _selectedIndex = -1;
    private bool _confirmed;
    private int? _selectedActionId;
    private bool _isClosing;

    public int? SelectedActionId => _selectedActionId;
    public bool IsConfirmed => _confirmed;

    public LegionBelongListWindow(List<Legion> legions)
    {
        _legionList = legions ?? new List<Legion>();

        PreloadFlags();

        Title = "军团列表";
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        FontFamily = new FontFamily("Microsoft YaHei");

        Content = CreateUI();

        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fadeIn);
        };

        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void PreloadFlags()
    {
        foreach (var legion in _legionList)
        {
            if (!_flagCache.ContainsKey(legion.CountryId))
            {
                var flagImage = LoadFlagImage(legion.CountryId);
                if (flagImage != null)
                    _flagCache[legion.CountryId] = flagImage;
            }
        }
    }

    private BitmapImage? LoadFlagImage(int countryId)
    {
        try
        {
            var tacticalMapParser = ConfigManager.Instance.TacticalMapParser;
            if (tacticalMapParser != null)
            {
                var imageDef = tacticalMapParser.GetImageDef($"flag_{countryId}.png");
                if (imageDef != null && tacticalMapParser.SurfaceData != null)
                {
                    return ExtractFlagFromTacticalMap(tacticalMapParser, imageDef);
                }
            }

            var basePath = System.IO.Path.Combine(ConfigManager.Instance.GetStageMarkPath(), "CountryFlag");
            var flagPath = System.IO.Path.Combine(basePath, $"flag_{countryId}.png");

            if (!System.IO.File.Exists(flagPath))
            {
                if (countryId == 0 || countryId == 255)
                    flagPath = System.IO.Path.Combine(basePath, "flag_1.png");
            }

            if (System.IO.File.Exists(flagPath))
            {
                var bitmap = new BitmapImage(new Uri(flagPath, UriKind.Absolute));
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch { }
        return null;
    }

    private BitmapImage? ExtractFlagFromTacticalMap(TacticalMapParser parser, TacticalMapImageDef imageDef)
    {
        try
        {
            var surfaceData = parser.SurfaceData;
            if (surfaceData == null) return null;

            using var stream = new System.IO.MemoryStream(surfaceData);
            using var surfaceBitmap = SKBitmap.Decode(stream);
            if (surfaceBitmap == null) return null;

            int x = imageDef.X;
            int y = imageDef.Y;
            int w = Math.Min(imageDef.Width, surfaceBitmap.Width - x);
            int h = Math.Min(imageDef.Height, surfaceBitmap.Height - y);

            if (w <= 0 || h <= 0) return null;

            using var subsetBitmap = new SKBitmap(w, h);
            using (var canvas = new SKCanvas(subsetBitmap))
            {
                canvas.DrawBitmap(surfaceBitmap, -x, -y);
            }

            using var skImage = SKImage.FromBitmap(subsetBitmap);
            using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var pngStream = encoded.AsStream();

            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = pngStream;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch { }
        return null;
    }

    private FrameworkElement CreateUI()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 8,
                BlurRadius = 20,
                Opacity = 0.4
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        var titlePanel = CreateTitlePanel();
        Grid.SetRow(titlePanel, 0);
        grid.Children.Add(titlePanel);

        var listPanel = CreateListPanel();
        Grid.SetRow(listPanel, 1);
        grid.Children.Add(listPanel);

        var buttonPanel = CreateButtonPanel();
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        border.Child = grid;
        return border;
    }

    private DockPanel CreateTitlePanel()
    {
        var panel = new DockPanel { Margin = new Thickness(15, 10, 15, 5) };

        var titleLabel = new TextBlock
        {
            Text = "军团列表",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(titleLabel, Dock.Left);
        panel.Children.Add(titleLabel);

        var closeButton = new Button
        {
            Content = "✕",
            Width = 30,
            Height = 30,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand,
            ToolTip = "关闭 (Esc)"
        };
        DockPanel.SetDock(closeButton, Dock.Right);
        closeButton.Click += (_, _) => CancelAndClose();
        panel.Children.Add(closeButton);

        return panel;
    }

    private ListBox CreateListPanel()
    {
        _listBox = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 63)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(15, 5, 15, 5),
            Padding = new Thickness(5),
            SelectionMode = SelectionMode.Single,
            FocusVisualStyle = null,
            ItemContainerStyle = CreateListBoxItemStyle()
        };

        for (int i = 0; i < _legionList.Count; i++)
        {
            var item = CreateLegionListItem(_legionList[i], i);
            _listBox.Items.Add(item);
        }

        _listBox.SelectionChanged += ListBox_SelectionChanged;
        _listBox.MouseDoubleClick += ListBox_MouseDoubleClick;

        return _listBox;
    }

    private Style CreateListBoxItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));

        style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 63))));
        style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(5)));
        style.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0, 2, 0, 2)));
        style.Setters.Add(new Setter(ListBoxItem.HeightProperty, 40.0));

        var selectedTrigger = new Trigger
        {
            Property = ListBoxItem.IsSelectedProperty,
            Value = true
        };
        selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 122, 204))));
        style.Triggers.Add(selectedTrigger);

        var hoverTrigger = new Trigger
        {
            Property = ListBoxItem.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(80, 80, 83))));
        style.Triggers.Add(hoverTrigger);

        return style;
    }

    private Border CreateLegionListItem(Legion legion, int index)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            Tag = legion,
            DataContext = index
        };

        var panel = new DockPanel { Margin = new Thickness(5) };

        var flagImage = new Image
        {
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(flagImage, Dock.Left);

        if (_flagCache.ContainsKey(legion.CountryId))
            flagImage.Source = _flagCache[legion.CountryId];

        panel.Children.Add(flagImage);

        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var countryName = GetCountryName(legion.CountryId);
        var nameText = new TextBlock
        {
            Text = $"军团 {legion.ActionId} - {countryName}",
            Foreground = Brushes.White,
            FontSize = 14
        };
        infoPanel.Children.Add(nameText);

        panel.Children.Add(infoPanel);
        border.Child = panel;

        return border;
    }

    private string GetCountryName(int countryId)
    {
        try
        {
            var value = ConfigManager.Instance.GetStringTableValue($"country_{countryId}");
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch { }
        return $"国家{countryId}";
    }

    private StackPanel CreateButtonPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(15, 10, 15, 15)
        };

        var confirmButton = new Button
        {
            Content = "确认",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "确认选择 (Enter)"
        };
        StyleButton(confirmButton);
        confirmButton.Click += (_, _) => ConfirmSelection();
        panel.Children.Add(confirmButton);

        var cancelButton = new Button
        {
            Content = "取消",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            ToolTip = "取消 (Esc)"
        };
        StyleButton(cancelButton);
        cancelButton.Click += (_, _) => CancelAndClose();
        panel.Children.Add(cancelButton);

        return panel;
    }

    private void StyleButton(Button button)
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var hoverTrigger = new Trigger
        {
            Property = Button.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(100, 149, 237))));
        template.Triggers.Add(hoverTrigger);

        button.Template = template;
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox == null) return;
        _selectedIndex = _listBox.SelectedIndex;
    }

    private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _legionList.Count)
            ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _legionList.Count)
        {
            _selectedActionId = _legionList[_selectedIndex].ActionId;
            _confirmed = true;
        }

        DialogResult = _confirmed;
        FadeOutAndClose();
    }

    private void CancelAndClose()
    {
        _confirmed = false;
        _selectedActionId = null;
        DialogResult = false;
        FadeOutAndClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ConfirmSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelAndClose();
                e.Handled = true;
                break;
            case Key.Tab:
                if (_legionList.Count > 0)
                {
                    _selectedIndex = _selectedIndex < 0 ? 0 : (_selectedIndex + 1) % _legionList.Count;
                    if (_listBox != null)
                    {
                        _listBox.SelectedIndex = _selectedIndex;
                        if (_listBox.Items.Count > _selectedIndex)
                            _listBox.ScrollIntoView(_listBox.Items[_selectedIndex]);
                    }
                }
                e.Handled = true;
                break;
        }
    }

    private void FadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}