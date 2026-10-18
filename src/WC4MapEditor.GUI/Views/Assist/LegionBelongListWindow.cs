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

namespace WC4MapEditor.GUI.Views.Assist;

public sealed class LegionBelongListWindow : Window
{
    private readonly List<Legion> _legionList;
    private readonly Dictionary<int, BitmapSource> _flagCache = new();
    private readonly Dictionary<int, string> _countryNameCache = new();
    private SKBitmap? _tacticalSurfaceBitmap;
    private bool _tacticalSurfaceLoadAttempted;
    private ListBox? _listBox;
    private int _selectedIndex = -1;
    private bool _confirmed;
    private int? _selectedActionId;
    private bool _isClosing;

    public int? SelectedActionId => _selectedActionId;
    public bool IsConfirmed => _confirmed;

    public LegionBelongListWindow(List<Legion> legions)
    {
        EditorTheme.Apply(this);
        _legionList = legions ?? new List<Legion>();

        PreloadFlags();

        Title = Loc.T("Assist.LegionList.Title");
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        AllowsTransparency = true;

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

    private BitmapSource? LoadFlagImage(int countryId)
    {
        try
        {
            var tacticalMapParser = ConfigManager.Instance.TacticalMapParser;
            if (tacticalMapParser != null)
            {
                var imageDef = tacticalMapParser.GetImageDef($"flag_{countryId}.png");
                if (imageDef != null)
                {
                    var surfaceBitmap = GetTacticalSurfaceBitmap(tacticalMapParser);
                    if (surfaceBitmap != null)
                        return ExtractFlagFromTacticalMap(surfaceBitmap, imageDef);
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

    /// <summary>
    /// 惰性解码战术地图大图，**整张图只解码一次**供所有国旗裁剪复用。
    /// <para>
    /// 原实现对每个国家都执行一次 MemoryStream + SKBitmap.Decode（都是整张大图），
    /// 国家数量上百时会让窗口弹出前卡顿数秒。
    /// </para>
    /// </summary>
    private SKBitmap? GetTacticalSurfaceBitmap(TacticalMapParser parser)
    {
        if (_tacticalSurfaceLoadAttempted) return _tacticalSurfaceBitmap;
        _tacticalSurfaceLoadAttempted = true;

        var surfaceData = parser.SurfaceData;
        if (surfaceData == null) return null;

        try
        {
            using var stream = new System.IO.MemoryStream(surfaceData);
            _tacticalSurfaceBitmap = SKBitmap.Decode(stream);
        }
        catch
        {
            _tacticalSurfaceBitmap = null;
        }

        return _tacticalSurfaceBitmap;
    }

    /// <summary>
    /// 从战术地图大图上裁剪出指定国旗。
    /// 直接用 Bgra8888 像素构造 <see cref="BitmapSource"/>，跳过 PNG 编解码
    /// （每个国家一面旗、数量可达数百，PNG 编解码会成为主要开销）。
    /// </summary>
    private static BitmapSource? ExtractFlagFromTacticalMap(SKBitmap surfaceBitmap, TacticalMapImageDef imageDef)
    {
        try
        {
            int x = imageDef.X;
            int y = imageDef.Y;
            int w = Math.Min(imageDef.Width, surfaceBitmap.Width - x);
            int h = Math.Min(imageDef.Height, surfaceBitmap.Height - y);

            if (x < 0 || y < 0 || w <= 0 || h <= 0) return null;

            // 预乘 Bgra8888 的内存布局与 WPF 的 Pbgra32 一致
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var subsetBitmap = new SKBitmap(info);
            using (var canvas = new SKCanvas(subsetBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(surfaceBitmap, -x, -y);
            }

            var result = BitmapSource.Create(
                w, h, 96, 96,
                PixelFormats.Pbgra32, null,
                subsetBitmap.GetPixels(), h * subsetBitmap.RowBytes, subsetBitmap.RowBytes);
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
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Bg0),
                Direction = 270,
                ShadowDepth = 6,
                BlurRadius = 20,
                Opacity = 0.6
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

        var closeButton = EditorTheme.GhostButton("✕", (_, _) => CancelAndClose());
        closeButton.Width = 28;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.ToolTip = Loc.T("Assist.Common.CloseEsc");
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(closeButton, Dock.Right);
        panel.Children.Add(closeButton);

        var titleLabel = EditorTheme.Title(Loc.T("Assist.LegionList.Title"));
        DockPanel.SetDock(titleLabel, Dock.Left);
        panel.Children.Add(titleLabel);

        return panel;
    }

    private Border CreateListPanel()
    {
        _listBox = new ListBox
        {
            SelectionMode = SelectionMode.Single
        };
        // 列表项是直接塞进 Items 的 UIElement，容器回收模式下复用容器会把同一个元素挂到两个父级上，改回标准虚拟化
        VirtualizingPanel.SetVirtualizationMode(_listBox, VirtualizationMode.Standard);

        for (int i = 0; i < _legionList.Count; i++)
        {
            var item = CreateLegionListItem(_legionList[i], i);
            _listBox.Items.Add(item);
        }

        _listBox.SelectionChanged += ListBox_SelectionChanged;
        _listBox.MouseDoubleClick += ListBox_MouseDoubleClick;

        // 主题里的 ListBox 本身无底无框，用一层面板画出列表区域
        var host = EditorTheme.Panel(new Thickness(1));
        host.CornerRadius = new CornerRadius(4);
        host.Margin = new Thickness(15, 5, 15, 5);
        host.Child = _listBox;
        return host;
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
            Text = Loc.F("Assist.Common.LegionItem", legion.ActionId, countryName),
            FontSize = 14
        };
        infoPanel.Children.Add(nameText);

        panel.Children.Add(infoPanel);
        border.Child = panel;

        return border;
    }

    private string GetCountryName(int countryId)
    {
        if (_countryNameCache.TryGetValue(countryId, out var cached))
            return cached;

        var name = Loc.F("Assist.Common.CountryFallback", countryId);
        try
        {
            var value = ConfigManager.Instance.GetStringTableValue($"country_{countryId}");
            if (!string.IsNullOrEmpty(value))
                name = value;
        }
        catch { }

        _countryNameCache[countryId] = name;
        return name;
    }

    private StackPanel CreateButtonPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(15, 10, 15, 15)
        };

        var confirmButton = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) => ConfirmSelection());
        confirmButton.MinWidth = 80;
        confirmButton.Margin = new Thickness(0, 0, 10, 0);
        confirmButton.ToolTip = Loc.T("Assist.Common.ConfirmEnter");
        panel.Children.Add(confirmButton);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => CancelAndClose());
        cancelButton.MinWidth = 80;
        cancelButton.ToolTip = Loc.T("Assist.Common.CancelEsc");
        panel.Children.Add(cancelButton);

        return panel;
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

    protected override void OnClosed(EventArgs e)
    {
        // 释放复用的战术地图位图（_flagCache 中的 BitmapImage 已 Freeze，无需手动释放）
        _tacticalSurfaceBitmap?.Dispose();
        _tacticalSurfaceBitmap = null;
        _flagCache.Clear();
        _countryNameCache.Clear();
        base.OnClosed(e);
    }
}
