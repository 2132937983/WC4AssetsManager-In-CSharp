using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.GUI.Views;

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
    private bool _showAllFiles;
    private Button? _allFilesButton;
    private MenuItem _dataCheckItem = null!;
    private readonly ObservableCollection<AssetEntry> _entries = new();

    public AssetBrowserScene(MainWindow window)
    {
        _window = window;
        _manager = AssetManager.Default;
        InitializeComponent();
        // 主题要在 SetupUI 之前合并进 Resources，后面创建的标准控件才会套上隐式样式
        EditorTheme.Apply(this);
        SetupUI();
        InitializeAssetCache();
    }

    private void SetupUI()
    {
        _mainGrid = new Grid();

        // 顶栏 44 / 主体 * / 状态栏 26
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        // ---- 顶栏：返回 + 标题 | 搜索 / 扩展名 / 刷新 ----
        var search = EditorTheme.SearchBox(Loc.T("Asset.SearchPlaceholder"), out _searchBox, 220);
        _searchBox.TextChanged += (s, e) => ApplyFilter();

        var extLabel = EditorTheme.Muted(Loc.T("Asset.ExtFilter"));
        extLabel.VerticalAlignment = VerticalAlignment.Center;
        extLabel.Margin = new Thickness(12, 0, 0, 0);

        _extFilter = EditorTheme.ComboBox(110);
        _extFilter.Items.Add(Loc.T("Common.All"));
        _extFilter.SelectedIndex = 0;
        _extFilter.SelectionChanged += (s, e) => ApplyFilter();

        var topBar = EditorTheme.TopBar(
            Loc.T("Asset.Title"),
            (s, e) => _window.ReturnToMainScene(),
            search,
            extLabel,
            _extFilter,
            EditorTheme.Button(Loc.T("Common.Refresh"), (s, e) => RefreshAssets()));
        Grid.SetRow(topBar, 0);
        _mainGrid.Children.Add(topBar);

        // ---- 主体：侧栏 (220) | 分隔条 (4) | 文件列表 (*) ----
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);

        var sidebar = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        var sideContent = new StackPanel();
        var sideTitle = EditorTheme.Section(Loc.T("Asset.Categories"));
        sideTitle.Margin = new Thickness(12, 12, 12, 6);
        sideContent.Children.Add(sideTitle);
        _categoryPanel = new StackPanel();
        sideContent.Children.Add(_categoryPanel);
        sidebar.Child = new ScrollViewer
        {
            Content = sideContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(sidebar, 0);
        body.Children.Add(sidebar);

        var splitter = EditorTheme.VSplitter();
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        _fileList = new ListView { SelectionMode = SelectionMode.Extended };

        var gv = new GridView();
        gv.Columns.Add(CreateColumn(Loc.T("Asset.Column.FileName"), 250, nameof(AssetEntry.FileName)));
        gv.Columns.Add(CreateColumn(Loc.T("Common.Size"), 90, nameof(AssetEntry.Size)));
        gv.Columns.Add(CreateColumn(Loc.T("Asset.Column.Kind"), 110, nameof(AssetEntry.Kind)));
        gv.Columns.Add(CreateColumn(Loc.T("Common.ModifiedTime"), 150, nameof(AssetEntry.LastModifiedUtc)));
        gv.Columns.Add(CreateColumn(Loc.T("Common.Path"), 0, nameof(AssetEntry.RelativePath)));
        _fileList.View = gv;
        _fileList.MouseDoubleClick += FileList_MouseDoubleClick;
        _fileList.KeyDown += FileList_KeyDown;
        _fileList.ContextMenu = BuildFileMenu();
        _fileList.ContextMenuOpening += FileList_ContextMenuOpening;
        _fileList.ItemsSource = _entries;
        Grid.SetColumn(_fileList, 2);
        body.Children.Add(_fileList);

        _mainGrid.Children.Add(body);

        // ---- 状态栏 ----
        var statusBar = EditorTheme.StatusBar(out _statusText, Loc.T("Common.Ready"));
        Grid.SetRow(statusBar, 2);
        _mainGrid.Children.Add(statusBar);

        Content = _mainGrid;
    }

    /// <summary>右键菜单：用数据检查场景只读打开 .btl / .bin 文件。</summary>
    private ContextMenu BuildFileMenu()
    {
        var menu = new ContextMenu();
        // 菜单是独立弹层，拿不到场景树上的隐式样式，需把主题字典合并进来
        menu.Resources.MergedDictionaries.Add(EditorTheme.Dictionary);
        _dataCheckItem = new MenuItem { Header = Loc.T("Asset.OpenInDataCheck") };
        _dataCheckItem.Click += (_, _) =>
        {
            if (_dataCheckItem.Tag is AssetEntry entry) _window.SetCurrentScene(new DataCheckScene(_window, entry.FullPath));
        };
        menu.Items.Add(_dataCheckItem);
        return menu;
    }

    /// <summary>右键点到 .btl / .bin 文件才弹出菜单，并选中被点的那一行；其它位置不弹。</summary>
    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = e.OriginalSource is DependencyObject source ? ItemsControl.ContainerFromElement(_fileList, source) as ListViewItem : null;
        if (item?.Content is not AssetEntry entry
            || !(entry.Extension.Equals("btl", StringComparison.OrdinalIgnoreCase) || entry.Extension.Equals("bin", StringComparison.OrdinalIgnoreCase)))
        {
            e.Handled = true;
            return;
        }
        _fileList.SelectedItem = entry;
        _dataCheckItem.Tag = entry;
    }

    private static GridViewColumn CreateColumn(string header, double width, string binding)
    {
        var col = new GridViewColumn { Header = header, Width = width };
        var b = new Binding(binding);
        if (binding == nameof(AssetEntry.Size))
            b.StringFormat = "{0:N0} B";
        else if (binding == nameof(AssetEntry.LastModifiedUtc))
            b.StringFormat = "yyyy-MM-dd HH:mm:ss";
        col.DisplayMemberBinding = b;
        return col;
    }

    private void InitializeAssetCache()
    {
        try
        {
            _manager.ScanDefault(forceReload: true);
            PopulateCategories();
            PopulateExtensions();
            UpdateStatus(Loc.F("Asset.LoadedFiles", _manager.Count));
        }
        catch (Exception ex)
        {
            UpdateStatus(Loc.F("Common.LoadFailed", ex.Message));
        }
    }

    private void PopulateCategories()
    {
        _categoryPanel.Children.Clear();

        // "全部文件" 按钮（按修改时间排序）
        _allFilesButton = EditorTheme.NavButton(Loc.F("Asset.AllFiles", _manager.Count), AllFilesButton_Click);
        EditorTheme.SetNavSelected(_allFilesButton, _showAllFiles);
        _categoryPanel.Children.Add(_allFilesButton);

        _categoryPanel.Children.Add(new Separator { Margin = new Thickness(8, 6, 8, 6) });

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

        _categoryPanel.Children.Add(new Separator { Margin = new Thickness(8, 6, 8, 6) });

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
        var btn = EditorTheme.NavButton($"{GetKindDisplayName(kind)} ({count})", CategoryButton_Click);
        btn.Tag = kind;
        EditorTheme.SetNavSelected(btn, !_showAllFiles && _selectedKind == kind);
        _categoryPanel.Children.Add(btn);
    }

    private void AllFilesButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllFiles = true;
        _selectedKind = null;
        RefreshCategorySelection();
        ApplyFilter();
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AssetKind kind) return;

        _showAllFiles = false;
        _selectedKind = kind;
        RefreshCategorySelection();
        ApplyFilter();
    }

    /// <summary>按当前 _showAllFiles / _selectedKind 同步侧栏按钮的选中外观。</summary>
    private void RefreshCategorySelection()
    {
        if (_allFilesButton != null)
            EditorTheme.SetNavSelected(_allFilesButton, _showAllFiles);

        foreach (var child in _categoryPanel.Children)
        {
            if (child is Button b && b.Tag is AssetKind k)
                EditorTheme.SetNavSelected(b, !_showAllFiles && _selectedKind == k);
        }
    }

    private void ApplyFilter()
    {
        _entries.Clear();

        string? ext = _extFilter.SelectedIndex > 0 ? _extFilter.SelectedItem as string : null;
        string? search = _searchBox.Text;

        List<AssetEntry> results;
        if (_showAllFiles)
        {
            // 全部文件模式：按修改时间降序排列
            results = _manager.GetAllSortedByModifiedTime().ToList();
        }
        else
        {
            results = _manager.Query(
                _selectedKind,
                ext,
                null,
                search).ToList();
        }

        // 应用扩展名和搜索过滤（全部文件模式下也需要）
        if (ext != null)
            results = results.Where(r => string.Equals(r.Extension, ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(search))
            results = results.Where(r => r.FileNameWithoutExtension.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var entry in results)
            _entries.Add(entry);

        UpdateStatus(Loc.F("Asset.Showing", results.Count, _manager.Count));
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

    private void FileList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedFilesToClipboard();
            e.Handled = true;
        }
    }

    private void CopySelectedFilesToClipboard()
    {
        var selectedItems = _fileList.SelectedItems.Cast<AssetEntry>().ToList();
        if (selectedItems.Count == 0) return;

        var fileDropList = new System.Collections.Specialized.StringCollection();
        foreach (var entry in selectedItems)
        {
            if (File.Exists(entry.FullPath))
                fileDropList.Add(entry.FullPath);
        }

        if (fileDropList.Count > 0)
        {
            var data = new System.Windows.DataObject();
            data.SetFileDropList(fileDropList);
            System.Windows.Clipboard.SetDataObject(data, true);
            UpdateStatus(Loc.F("Asset.CopiedToClipboard", fileDropList.Count));
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "webp", "jpg", "jpeg", "bmp", "gif", "tga", "pkm"
    };

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_fileList.SelectedItem is not AssetEntry entry) return;

        if (entry.Extension.Equals("btl", StringComparison.OrdinalIgnoreCase))
        {
            if (entry.Kind == AssetKind.Conquest)
            {
                var scene = new ConquestRenderScene(_window, entry.FullPath);
                _window.SetCurrentScene(scene);
            }
            else
            {
                var scene = new StageRenderScene(_window, entry.FullPath);
                _window.SetCurrentScene(scene);
            }
        }
        else if (ImageExtensions.Contains(entry.Extension))
        {
            string? xmlPath = FindAssociatedXml(entry.FullPath);
            string? sourceDir = System.IO.Path.GetDirectoryName(entry.FullPath);
            if (string.IsNullOrEmpty(sourceDir)) sourceDir = Environment.CurrentDirectory;
            var scene = new TacticalMapEditScene(_window, entry.FullPath, xmlPath, sourceDir);
            _window.SetCurrentScene(scene);
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

    private static string? FindAssociatedXml(string imageFilePath)
    {
        string dir = IOPath.GetDirectoryName(imageFilePath) ?? "";
        string name = IOPath.GetFileNameWithoutExtension(imageFilePath);
        string xmlPath = IOPath.Combine(dir, name + ".xml");
        if (File.Exists(xmlPath)) return xmlPath;
        return null;
    }

    private void RefreshAssets()
    {
        try
        {
            _manager.ScanDefault(forceReload: true);
            _entries.Clear();
            _extFilter.Items.Clear();
            _extFilter.Items.Add(Loc.T("Common.All"));
            _extFilter.SelectedIndex = 0;
            PopulateCategories();
            PopulateExtensions();
            UpdateStatus(Loc.F("Asset.Refreshed", _manager.Count));
        }
        catch (Exception ex)
        {
            UpdateStatus(Loc.F("Asset.RefreshFailed", ex.Message));
        }
    }

    private void UpdateStatus(string text)
    {
        if (Dispatcher.CheckAccess())
            _statusText.Text = text;
        else
            Dispatcher.Invoke(() => _statusText.Text = text);
    }

    // 每次调用时才查 Loc，不缓存：语言可在运行时切换，场景会被重建
    private static string GetKindDisplayName(AssetKind kind) => kind switch
    {
        AssetKind.Stage => Loc.T("Asset.Kind.Stage"),
        AssetKind.Conquest => Loc.T("Asset.Kind.Conquest"),
        AssetKind.Event => Loc.T("Asset.Kind.Event"),
        AssetKind.Frontier => Loc.T("Asset.Kind.Frontier"),
        AssetKind.GeneralStage => Loc.T("Asset.Kind.GeneralStage"),
        AssetKind.Legend => Loc.T("Asset.Kind.Legend"),
        AssetKind.Warzone => Loc.T("Asset.Kind.Warzone"),
        AssetKind.InvadeCorps => Loc.T("Asset.Kind.InvadeCorps"),
        AssetKind.OtherStageBtl => Loc.T("Asset.Kind.OtherStageBtl"),
        AssetKind.Texture => Loc.T("Asset.Kind.Texture"),
        AssetKind.Xml => Loc.T("Asset.Kind.Xml"),
        AssetKind.Json => Loc.T("Asset.Kind.Json"),
        AssetKind.Binary => Loc.T("Asset.Kind.Binary"),
        AssetKind.Audio => Loc.T("Asset.Kind.Audio"),
        AssetKind.Font => Loc.T("Asset.Kind.Font"),
        AssetKind.Shader => Loc.T("Asset.Kind.Shader"),
        AssetKind.StringTable => Loc.T("Asset.Kind.StringTable"),
        _ => Loc.T("Asset.Kind.Other")
    };
}
