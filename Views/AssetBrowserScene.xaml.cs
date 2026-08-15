using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Views;

public partial class AssetBrowserScene : UserControl
{
    private readonly MainWindow _window;
    private readonly AssetManager _manager;

    private Grid _mainGrid = null!;
    private ListView _fileList = null!;
    private TextBox _searchBox = null!;
    private ComboBox _extFilter = null!;
    private TextBlock _statusText = null!;
    private StackPanel _categoryPanel = null!;

    private AssetKind? _selectedKind;
    private readonly ObservableCollection<AssetEntry> _entries = new();

    public AssetBrowserScene(MainWindow window)
    {
        _window = window;
        _manager = AssetManager.Default;
        InitializeComponent();
        SetupUI();
        InitializeAssetCache();
    }

    private void SetupUI()
    {
        _mainGrid = new Grid();

        // Column: sidebar (200) | splitter (4) | content (*)
        _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row: top bar (40) | content (*) | status (28)
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        // ---- Sidebar ----
        var sidebar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 40)),
            Margin = new Thickness(0)
        };
        Grid.SetColumn(sidebar, 0);
        Grid.SetRowSpan(sidebar, 2);

        var sideContent = new StackPanel();

        var sideTitle = new TextBlock
        {
            Text = "资源类别",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 8)
        };
        sideContent.Children.Add(sideTitle);

        _categoryPanel = new StackPanel();
        sideContent.Children.Add(_categoryPanel);

        sidebar.Child = sideContent;
        _mainGrid.Children.Add(sidebar);

        // ---- Splitter ----
        var splitter = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 60))
        };
        Grid.SetColumn(splitter, 1);
        Grid.SetRowSpan(splitter, 2);
        _mainGrid.Children.Add(splitter);

        // ---- Content area ----
        var contentArea = new Grid();
        Grid.SetColumn(contentArea, 2);

        contentArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        contentArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Top bar
        var topBar = new Grid { Background = new SolidColorBrush(Color.FromRgb(35, 35, 45)), Margin = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(topBar, 0);

        var topPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };

        _searchBox = new TextBox
        {
            Width = 200,
            Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 60)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 13
        };
        _searchBox.GotFocus += (s, e) =>
        {
            if (_searchBox.Tag as string == "搜索文件名...") _searchBox.Text = "";
        };
        _searchBox.LostFocus += (s, e) =>
        {
            if (string.IsNullOrEmpty(_searchBox.Text)) { _searchBox.Text = "搜索文件名..."; _searchBox.Tag = "搜索文件名..."; }
        };
        _searchBox.Text = "搜索文件名...";
        _searchBox.Tag = "搜索文件名...";
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        topPanel.Children.Add(_searchBox);

        topPanel.Children.Add(new TextBlock
        {
            Text = "扩展名:",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 6, 0),
            FontSize = 13
        });

        _extFilter = new ComboBox
        {
            Width = 100,
            Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 60)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
            FontSize = 13
        };
        _extFilter.Items.Add("全部");
        _extFilter.SelectedIndex = 0;
        _extFilter.SelectionChanged += (s, e) => ApplyFilter();
        topPanel.Children.Add(_extFilter);

        var refreshBtn = new Button
        {
            Content = "刷新",
            Width = 60,
            Height = 26,
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 13
        };
        refreshBtn.Click += (s, e) => RefreshAssets();
        topPanel.Children.Add(refreshBtn);

        var backBtn = new Button
        {
            Content = "← 返回",
            Width = 70,
            Height = 26,
            Margin = new Thickness(20, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        backBtn.Click += (s, e) => _window.ReturnToMainScene();
        topPanel.Children.Add(backBtn);

        topBar.Children.Add(topPanel);
        contentArea.Children.Add(topBar);

        // File list
        _fileList = new ListView
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 36)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Foreground = Brushes.White,
        };

        var gv = new GridView();
        gv.Columns.Add(CreateColumn("文件名", 250, nameof(AssetEntry.FileName)));
        gv.Columns.Add(CreateColumn("大小", 90, nameof(AssetEntry.Size)));
        gv.Columns.Add(CreateColumn("类别", 110, nameof(AssetEntry.Kind)));
        gv.Columns.Add(CreateColumn("路径", 0, nameof(AssetEntry.RelativePath)));
        _fileList.View = gv;
        _fileList.MouseDoubleClick += FileList_MouseDoubleClick;
        _fileList.ItemsSource = _entries;

        contentArea.Children.Add(_fileList);
        Grid.SetRow(_fileList, 1);

        _mainGrid.Children.Add(contentArea);
        Grid.SetColumn(contentArea, 2);
        Grid.SetRowSpan(contentArea, 2);

        // Status bar
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 25, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(statusBar, 2);
        Grid.SetColumnSpan(statusBar, 3);

        _statusText = new TextBlock
        {
            Text = "就绪",
            Foreground = Brushes.Gray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        statusBar.Child = _statusText;
        _mainGrid.Children.Add(statusBar);

        Content = _mainGrid;
    }

    private static GridViewColumn CreateColumn(string header, double width, string binding)
    {
        var col = new GridViewColumn { Header = header, Width = width };
        var b = new Binding(binding);
        if (binding == nameof(AssetEntry.Size))
            b.StringFormat = "{0:N0} B";
        col.DisplayMemberBinding = b;
        return col;
    }

    private void InitializeAssetCache()
    {
        try
        {
            _manager.ScanDefault();
            PopulateCategories();
            PopulateExtensions();
            UpdateStatus($"已加载 {_manager.Count} 个文件");
        }
        catch (Exception ex)
        {
            UpdateStatus($"加载失败: {ex.Message}");
        }
    }

    private void PopulateCategories()
    {
        _categoryPanel.Children.Clear();

        var counts = _manager.GetKindCounts();
        if (counts.Count == 0) return;

        // Stage types first, ordered by count desc
        var stageKinds = new[]
        {
            AssetKind.Stage, AssetKind.Conquest, AssetKind.Event, AssetKind.Frontier,
            AssetKind.Legend, AssetKind.GeneralStage, AssetKind.Warzone, AssetKind.InvadeCorps,
            AssetKind.OtherStageBtl
        };

        foreach (var kind in stageKinds)
        {
            if (!counts.TryGetValue(kind, out int c) || c == 0) continue;
            AddCategoryButton(kind, c);
        }

        // Separator
        _categoryPanel.Children.Add(new Separator { Margin = new Thickness(8, 6, 8, 6), Background = new SolidColorBrush(Color.FromRgb(60, 60, 70)) });

        // Generic types
        var genericKinds = new[]
        {
            AssetKind.Texture, AssetKind.Json, AssetKind.Xml, AssetKind.Audio,
            AssetKind.Binary, AssetKind.Font, AssetKind.Shader, AssetKind.StringTable,
            AssetKind.Unknown
        };

        foreach (var kind in genericKinds)
        {
            if (!counts.TryGetValue(kind, out int c) || c == 0) continue;
            AddCategoryButton(kind, c);
        }
    }

    private void AddCategoryButton(AssetKind kind, int count)
    {
        var btn = new Button
        {
            Content = $"{GetKindDisplayName(kind)} ({count})",
            Tag = kind,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 13,
            Cursor = Cursors.Hand,
            Margin = new Thickness(4, 1, 4, 1)
        };

        btn.Click += CategoryButton_Click;
        btn.MouseEnter += (s, e) =>
        {
            if (_selectedKind != kind)
                btn.Background = new SolidColorBrush(Color.FromRgb(50, 50, 65));
        };
        btn.MouseLeave += (s, e) =>
        {
            if (_selectedKind != kind)
                btn.Background = Brushes.Transparent;
        };

        _categoryPanel.Children.Add(btn);
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AssetKind kind) return;

        // Update visual selection
        foreach (var child in _categoryPanel.Children)
        {
            if (child is Button b)
                b.Background = b.Tag is AssetKind k && k == kind
                    ? new SolidColorBrush(Color.FromRgb(60, 70, 90))
                    : Brushes.Transparent;
        }

        _selectedKind = kind;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _entries.Clear();

        string? ext = _extFilter.SelectedIndex > 0 ? _extFilter.SelectedItem as string : null;
        string? search = _searchBox.Text;
        if (search == "搜索文件名...") search = null;

        var results = _manager.Query(
            _selectedKind,
            ext,
            null,
            search);

        foreach (var entry in results)
            _entries.Add(entry);

        UpdateStatus($"显示 {results.Count} / {_manager.Count} 个文件");
    }

    private void PopulateExtensions()
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _manager.ListAll())
        {
            if (!string.IsNullOrEmpty(entry.Extension))
                exts.Add(entry.Extension);
        }

        foreach (var ext in exts.OrderBy(e => e))
            _extFilter.Items.Add("." + ext);
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_fileList.SelectedItem is not AssetEntry entry) return;

        if (entry.Extension.Equals("btl", StringComparison.OrdinalIgnoreCase))
        {
            // Open in map editor
            try
            {
                var mapData = Parsers.BTL.BTLParser.LoadFromFile(entry.FullPath);
                // Navigate to editor with loaded map
                UpdateStatus($"已加载地图: {entry.FileName} ({mapData.MapWidth}x{mapData.MapHeight})");
            }
            catch (Exception ex)
            {
                UpdateStatus($"加载失败: {ex.Message}");
            }
        }
        else if (entry.Extension.Equals("json", StringComparison.OrdinalIgnoreCase) ||
                 entry.Extension.Equals("xml", StringComparison.OrdinalIgnoreCase) ||
                 entry.Extension.Equals("ini", StringComparison.OrdinalIgnoreCase))
        {
            // Open in default text editor
            try
            {
                Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            }
            catch { }
        }
        else
        {
            // Open containing folder
            try
            {
                Process.Start("explorer.exe", $"/select,\"{entry.FullPath}\"");
            }
            catch { }
        }
    }

    private void RefreshAssets()
    {
        try
        {
            _manager.ScanDefault(forceReload: true);
            _entries.Clear();
            _extFilter.Items.Clear();
            _extFilter.Items.Add("全部");
            _extFilter.SelectedIndex = 0;
            PopulateCategories();
            PopulateExtensions();
            UpdateStatus($"已刷新，共 {_manager.Count} 个文件");
        }
        catch (Exception ex)
        {
            UpdateStatus($"刷新失败: {ex.Message}");
        }
    }

    private void UpdateStatus(string text)
    {
        if (Dispatcher.CheckAccess())
            _statusText.Text = text;
        else
            Dispatcher.Invoke(() => _statusText.Text = text);
    }

    private static string GetKindDisplayName(AssetKind kind) => kind switch
    {
        AssetKind.Stage => "战役关卡",
        AssetKind.Conquest => "征服",
        AssetKind.Event => "事件关卡",
        AssetKind.Frontier => "前线",
        AssetKind.GeneralStage => "名将关卡",
        AssetKind.Legend => "传奇",
        AssetKind.Warzone => "战区",
        AssetKind.InvadeCorps => "入侵军团",
        AssetKind.OtherStageBtl => "其它关卡",
        AssetKind.Texture => "纹理图片",
        AssetKind.Xml => "XML 文件",
        AssetKind.Json => "JSON 配置",
        AssetKind.Binary => "二进制数据",
        AssetKind.Audio => "音频",
        AssetKind.Font => "字体",
        AssetKind.Shader => "着色器",
        AssetKind.StringTable => "字符串表",
        _ => "其它"
    };
}
