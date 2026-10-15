using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views;

public class CountryEditScene : UserControl
{
    private readonly MainWindow _window;
    private readonly CountrySettingParser _parser = CountrySettingParser.Instance;
    private readonly IFlagImageService _flagService = new FlagImageService();

    private Grid _root = null!;
    private ListBox _countryList = null!;
    private ListBox _conquerList = null!;
    private StackPanel _detailPanel = null!;
    private Image _flagPreview = null!;
    private TextBlock _statusText = null!;

    private CountrySettingData? _currentCountry;

    public CountryEditScene(MainWindow window)
    {
        _window = window;
        InitializeUI();
        LoadCountries();
        LoadConquers();
    }

    private void InitializeUI()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top bar
        var backBtn = new Button
        {
            Content = "返回",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        backBtn.Click += (_, _) => _window.SetCurrentScene(new BeginScene(_window));

        var addBtn = new Button
        {
            Content = "+ 新增国家",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        addBtn.Click += OnAddCountry;

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Height = 50,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    backBtn,
                    addBtn,
                    new TextBlock
                    {
                        Text = "国家数据编辑",
                        Foreground = Brushes.White,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(20, 0, 0, 0)
                    }
                }
            }
        };
        Grid.SetRow(topBar, 0);
        _root.Children.Add(topBar);

        // Main content
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(mainGrid, 1);

        // Country list
        var countryBorder = CreatePanel("国家列表");
        _countryList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(5)
        };
        _countryList.SelectionChanged += OnCountrySelected;
        countryBorder.Child = _countryList;
        Grid.SetColumn(countryBorder, 0);
        mainGrid.Children.Add(countryBorder);

        // Conquer list for selected country
        var conquerBorder = CreatePanel("征服中的国家");
        _conquerList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(5)
        };
        _conquerList.SelectionChanged += OnConquerCountrySelected;
        conquerBorder.Child = _conquerList;
        Grid.SetColumn(conquerBorder, 1);
        mainGrid.Children.Add(conquerBorder);

        // Detail panel
        _detailPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Margin = new Thickness(5)
        };
        var detailBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _detailPanel
        };
        Grid.SetColumn(detailBorder, 2);
        mainGrid.Children.Add(detailBorder);

        _root.Children.Add(mainGrid);

        // Status bar
        _statusText = new TextBlock
        {
            Foreground = Brushes.Gray,
            Margin = new Thickness(10, 5, 10, 5)
        };
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Child = _statusText
        };
        Grid.SetRow(statusBar, 2);
        _root.Children.Add(statusBar);

        Content = _root;
    }

    private static Border CreatePanel(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(5)
        });

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(5),
            Child = panel
        };
    }

    private void LoadCountries()
    {
        _countryList.Items.Clear();
        foreach (var country in _parser.Countries)
        {
            var item = new ListBoxItem
            {
                Content = $"[{country.Id}] {country.Name}",
                Tag = country,
                Foreground = Brushes.White,
                Background = Brushes.Transparent
            };
            _countryList.Items.Add(item);
        }
        SetStatus($"已加载 {_parser.Countries.Count} 个国家");
    }

    private void LoadConquers()
    {
        // Conquers are loaded in the list when a country is selected
    }

    private void OnCountrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_countryList.SelectedItem is not ListBoxItem item) return;
        _currentCountry = item.Tag as CountrySettingData;
        if (_currentCountry == null) return;

        LoadConquerCountries(_currentCountry.Id);
        ShowCountryDetail(_currentCountry);
    }

    private void LoadConquerCountries(int countryId)
    {
        _conquerList.Items.Clear();
        var conquerCountries = _parser.GetConquerCountriesByCountryId(countryId);
        foreach (var cc in conquerCountries)
        {
            var conquerName = _parser.GetConquerName(cc.ConquerId);
            var item = new ListBoxItem
            {
                Content = $"[{cc.ConquerId}] {conquerName} - 星级:{cc.Star}",
                Tag = cc,
                Foreground = Brushes.White,
                Background = Brushes.Transparent
            };
            _conquerList.Items.Add(item);
        }
    }

    private void OnConquerCountrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_conquerList.SelectedItem is not ListBoxItem item) return;
        var cc = item.Tag as ConquerCountrySettingData;
        if (cc == null) return;

        ShowConquerCountryDetail(cc);
    }

    private void ShowCountryDetail(CountrySettingData country)
    {
        _detailPanel.Children.Clear();

        // Flag preview
        _flagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };
        LoadFlagPreview(country.Id);
        _detailPanel.Children.Add(_flagPreview);

        // Edit flag button
        var editFlagBtn = new Button
        {
            Content = "编辑国旗",
            Margin = new Thickness(5),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editFlagBtn.Click += (_, _) =>
        {
            var scene = new FlagEditScene(_window, country.Id, country.Name);
            _window.SetCurrentScene(scene);
        };

        // 按钮行：编辑国旗 + 使用现有国旗
        var flagBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        flagBtnPanel.Children.Add(editFlagBtn);

        var useExistingFlagBtn = new Button
        {
            Content = "使用现有国旗",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        useExistingFlagBtn.Click += (_, _) => OnUseExistingFlag(country.Id);
        flagBtnPanel.Children.Add(useExistingFlagBtn);

        _detailPanel.Children.Add(flagBtnPanel);

        // Country info with edit buttons
        var realName = _parser.GetCountryRealName(country.Id);
        _detailPanel.Children.Add(CreateEditableInfoBlockWithSync("名称", country.Name,
            v => { country.Name = v; SaveAndReload(country); },
            () => { if (_parser.SyncRealNameToName(country.Id)) { SaveAndReload(country); SetStatus($"已将真实名称同步为名称: {country.Name}"); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlockWithSync("真实名称", realName,
            v => { if (_parser.SetCountryRealName(country.Id, v)) { ShowCountryDetail(country); SetStatus($"已更新真实名称: {v}"); } },
            () => { if (_parser.SyncNameToRealName(country.Id)) { ShowCountryDetail(country); SetStatus($"已将名称同步为真实名称: {country.Name}"); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("阵营", country.Camp.ToString(), v => { if (int.TryParse(v, out var n)) { country.Camp = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("步兵模组", country.InfantryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.InfantryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("装甲模组", country.ArmoredMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArmoredMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("炮兵模组", country.ArtilleryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArtilleryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("Logo", country.Logo.ToString(), v => { if (int.TryParse(v, out var n)) { country.Logo = n; SaveAndReload(country); } }));

        // Delete country button
        var deleteBtn = new Button
        {
            Content = "删除国家",
            Margin = new Thickness(5, 15, 5, 5),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        deleteBtn.Click += async (_, _) =>
        {
            var result = MessageBox.Show($"确定要删除国家 [{country.Id}] {country.Name} 吗？\n这将同时删除该国家在所有征服中的数据。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (_parser.RemoveCountry(country.Id))
            {
                _currentCountry = null;
                LoadCountries();
                _detailPanel.Children.Clear();
                SetStatus($"已删除国家: [{country.Id}] {country.Name}");
            }
        };
        _detailPanel.Children.Add(deleteBtn);
    }

    private void SaveAndReload(CountrySettingData country)
    {
        _parser.SaveCountries();
        LoadCountries();
        SetStatus($"已更新: [{country.Id}] {country.Name}");
        foreach (ListBoxItem item in _countryList.Items)
        {
            if (item.Tag is CountrySettingData c && c.Id == country.Id)
            {
                _countryList.SelectedItem = item;
                break;
            }
        }
    }

    private StackPanel CreateEditableInfoBlock(string label, string value, Action<string> onSave)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 80
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            Width = 120
        };
        panel.Children.Add(valueBlock);

        var editBtn = new Button
        {
            Content = "编辑",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        editBtn.Click += async (_, _) =>
        {
            using var dialog = new Views.Dialogs.SingleInputDialog(_window)
            {
                Title = $"修改 {label}",
                Description = $"请输入新的{label}：",
                DefaultValue = value,
                ConfirmText = "保存",
                CancelText = "取消"
            };
            var newValue = await dialog.ShowAsync();
            if (newValue != null)
            {
                onSave(newValue.Trim());
            }
        };
        panel.Children.Add(editBtn);
        return panel;
    }

    private StackPanel CreateEditableInfoBlockWithSync(string label, string value, Action<string> onSave, Action onSync)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 80
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            Width = 120
        };
        panel.Children.Add(valueBlock);

        var editBtn = new Button
        {
            Content = "编辑",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0)
        };
        editBtn.Click += async (_, _) =>
        {
            using var dialog = new Views.Dialogs.SingleInputDialog(_window)
            {
                Title = $"修改 {label}",
                Description = $"请输入新的{label}：",
                DefaultValue = value,
                ConfirmText = "保存",
                CancelText = "取消"
            };
            var newValue = await dialog.ShowAsync();
            if (newValue != null)
            {
                onSave(newValue.Trim());
            }
        };
        panel.Children.Add(editBtn);

        var syncBtn = new Button
        {
            Content = label == "名称" ? "同步为真实名称" : "同步为名称",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        syncBtn.Click += (_, _) => onSync();
        panel.Children.Add(syncBtn);

        return panel;
    }

    private void ShowConquerCountryDetail(ConquerCountrySettingData cc)
    {
        _detailPanel.Children.Clear();

        var countryName = _parser.GetCountryName(cc.CountryId);
        var conquerName = _parser.GetConquerName(cc.ConquerId);

        _detailPanel.Children.Add(CreateInfoBlock("ID", cc.Id.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("征服", $"[{cc.ConquerId}] {conquerName}"));
        _detailPanel.Children.Add(CreateInfoBlock("国家", $"[{cc.CountryId}] {countryName}"));
        _detailPanel.Children.Add(CreateInfoBlock("座位", cc.Seat.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("星级", cc.Star.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("阵营", cc.Camp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("战争回合", cc.WarTurn.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("经验奖励", cc.PrizeExp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("金币奖励", cc.PrizeGold.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("工业奖励", cc.PrizeIndustry.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("能源奖励", cc.PrizeEnergy.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("科技奖励", cc.PrizeTech.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("照片", cc.Photo));
        _detailPanel.Children.Add(CreateInfoBlock("费用-金钱", cc.CostMoney.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("费用-齿轮", cc.CostGear.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("费用-原子", cc.CostAtomic.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("系数", cc.Coefficient.ToString("F2")));
        _detailPanel.Children.Add(CreateInfoBlock("投降", cc.Surrender ? "是" : "否"));
    }

    private static StackPanel CreateInfoBlock(string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 100
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = Brushes.White
        });
        return panel;
    }

    private void LoadFlagPreview(int countryId)
    {
        try
        {
            var flag = _flagService.LoadFlagFromHdAtlas(countryId);
            if (flag == null)
                flag = _flagService.LoadBigFlag(countryId);

            if (flag != null)
            {
                _flagPreview.Source = SkBitmapToBitmapSource(flag);
                flag.Dispose();
            }
        }
        catch { }
    }

    private static System.Windows.Media.Imaging.BitmapSource? SkBitmapToBitmapSource(SkiaSharp.SKBitmap bitmap)
    {
        try
        {
            var info = bitmap.Info;
            var wb = new System.Windows.Media.Imaging.WriteableBitmap(info.Width, info.Height, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32, null);
            wb.Lock();
            try
            {
                using var pixmap = bitmap.PeekPixels();
                var srcPtr = pixmap.GetPixels();
                for (int y = 0; y < info.Height; y++)
                {
                    var dstPtr = System.IntPtr.Add(wb.BackBuffer, y * wb.BackBufferStride);
                    var row = new byte[info.Width * 4];
                    System.Runtime.InteropServices.Marshal.Copy(System.IntPtr.Add(srcPtr, y * pixmap.RowBytes), row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4)
                    {
                        byte r = row[i], g = row[i + 1], b = row[i + 2], a = row[i + 3];
                        if (a < 255)
                        {
                            r = (byte)(r * a / 255); g = (byte)(g * a / 255); b = (byte)(b * a / 255);
                        }
                        row[i] = b; row[i + 1] = g; row[i + 2] = r; row[i + 3] = a;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, dstPtr, row.Length);
                }
                wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                wb.Unlock();
            }
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    private async void OnAddCountry(object sender, RoutedEventArgs e)
    {
        var nextId = _parser.GetNextCountryId();
        using var dialog = new Views.Dialogs.SingleInputDialog(_window)
        {
            Title = "新增国家",
            Description = $"将要新增一个国家，建议ID为 {nextId}。\n请输入国家名称：",
            DefaultValue = "",
            ConfirmText = "确定",
            CancelText = "取消"
        };
        var name = await dialog.ShowAsync();
        if (name == null) return;

        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("国家名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var country = new CountrySettingData
        {
            Id = nextId,
            Name = name,
            Camp = 0,
            InfantryMod = 0,
            ArmoredMod = 0,
            ArtilleryMod = 0,
            Logo = 0
        };

        if (_parser.AddCountry(country))
        {
            LoadCountries();
            SetStatus($"新增国家成功: [{nextId}] {name}");
            foreach (ListBoxItem item in _countryList.Items)
            {
                if (item.Tag is CountrySettingData c && c.Id == nextId)
                {
                    _countryList.SelectedItem = item;
                    break;
                }
            }
        }
        else
        {
            MessageBox.Show("新增国家失败，可能ID已存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void OnUseExistingFlag(int targetCountryId)
    {
        var inputDialog = new Window
        {
            Title = "使用现有国旗",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _window,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
        };

        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock
        {
            Text = $"输入要复制的源国家ID（将复制该国家的国旗到当前国家ID={targetCountryId}）",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var idBox = new TextBox
        {
            Text = "",
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White
        };
        panel.Children.Add(idBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button
        {
            Content = "确定",
            Padding = new Thickness(20, 5, 20, 5),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(20, 5, 20, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        inputDialog.Content = panel;

        int? sourceId = null;
        okBtn.Click += (_, _) => { if (int.TryParse(idBox.Text, out int id)) sourceId = id; inputDialog.Close(); };
        cancelBtn.Click += (_, _) => inputDialog.Close();

        inputDialog.ShowDialog();

        if (sourceId == null || sourceId.Value <= 0)
        {
            SetStatus("已取消或ID无效");
            return;
        }

        if (sourceId.Value == targetCountryId)
        {
            MessageBox.Show("源国家ID与当前国家ID相同，无需复制", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetStatus($"正在复制国家{sourceId.Value}的国旗到国家{targetCountryId}...");

        try
        {
            var bigFlag = _flagService.LoadBigFlag(sourceId.Value);
            var smallFlag = _flagService.LoadSmallFlag(sourceId.Value);
            var hdFlag = _flagService.LoadFlagFromHdAtlas(sourceId.Value);

            if (bigFlag == null && smallFlag == null && hdFlag == null)
            {
                MessageBox.Show($"国家{sourceId.Value}没有任何国旗数据，无法复制", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (bigFlag != null || smallFlag != null)
            {
                var (tacticalImagePath, tacticalXmlPath) = FindTacticalMapFiles();
                if (!string.IsNullOrEmpty(tacticalImagePath) && !string.IsNullOrEmpty(tacticalXmlPath))
                {
                    var tmEditor = new TacticalMapEditor();
                    if (tmEditor.LoadFromFiles(tacticalImagePath, tacticalXmlPath))
                    {
                        var imagesToAdd = new List<(string name, SKBitmap image)>();

                        if (bigFlag != null)
                            imagesToAdd.Add(($"flag_{targetCountryId}.png", bigFlag.Copy()));

                        if (smallFlag != null)
                            imagesToAdd.Add(($"f_{targetCountryId:D2}.png", smallFlag.Copy()));

                        var ok = tmEditor.AddImagesAndArrange(imagesToAdd, tacticalImagePath, tacticalXmlPath);
                        SetStatus(ok ? "TacticalMap添加成功" : "TacticalMap添加失败");

                        foreach (var (_, img) in imagesToAdd)
                            img.Dispose();
                    }
                }
            }

            if (hdFlag != null)
            {
                var (hdImagePath, hdXmlPath) = FindHdAtlasFiles();
                if (!string.IsNullOrEmpty(hdImagePath) && !string.IsNullOrEmpty(hdXmlPath))
                {
                    var hdEditor = new HdAtlasEditor();
                    if (hdEditor.LoadFromFiles(hdImagePath, hdXmlPath))
                    {
                        var hdCopy = hdFlag.Copy();
                        var ok = hdEditor.AddImageAndSave(targetCountryId, hdCopy, hdImagePath, hdXmlPath);
                        hdCopy.Dispose();
                        SetStatus(ok ? "HD图集添加成功" : "HD图集添加失败");
                    }
                }
            }

            bigFlag?.Dispose();
            smallFlag?.Dispose();
            hdFlag?.Dispose();

            _flagService.RefreshCache();

            if (_currentCountry != null)
                LoadFlagPreview(_currentCountry.Id);

            MessageBox.Show($"已将国家{sourceId.Value}的国旗复制到国家{targetCountryId}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"国旗复制完成: 源ID={sourceId.Value} → 目标ID={targetCountryId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制国旗失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"复制失败: {ex.Message}");
        }
    }

    private (string? imagePath, string? xmlPath) FindTacticalMapFiles()
    {
        var am = Core.Assets.AssetManager.Default;
        if (am.IsLoaded)
        {
            var imgEntry = am.Find("tacticalmap.webp") ?? am.Find("tacticalmap.png");
            var xmlEntry = am.Find("tacticalmap.xml");
            if (imgEntry != null && xmlEntry != null)
                return (imgEntry.FullPath, xmlEntry.FullPath);
        }

        var dir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(dir)) return (null, null);

        var dirs = new[] { dir, System.IO.Path.GetDirectoryName(dir) ?? "" };
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d) || !System.IO.Directory.Exists(d)) continue;
            foreach (var ext in new[] { ".png", ".webp" })
            {
                var imgPath = System.IO.Path.Combine(d, $"tacticalmap{ext}");
                var xmlPath = System.IO.Path.Combine(d, "tacticalmap.xml");
                if (System.IO.File.Exists(imgPath) && System.IO.File.Exists(xmlPath))
                    return (imgPath, xmlPath);
            }
        }
        return (null, null);
    }

    private (string? imagePath, string? xmlPath) FindHdAtlasFiles()
    {
        var hdImg = _parser.FlagsHdImagePath;
        var hdXml = _parser.FlagsHdXmlPath;

        if (!string.IsNullOrEmpty(hdImg) && !string.IsNullOrEmpty(hdXml) && System.IO.File.Exists(hdImg) && System.IO.File.Exists(hdXml))
            return (hdImg, hdXml);

        if (!string.IsNullOrEmpty(hdXml) && System.IO.File.Exists(hdXml))
        {
            var dir = System.IO.Path.GetDirectoryName(hdImg) ?? "";
            var pngPath = System.IO.Path.Combine(dir, "image_flags_hd.png");
            if (System.IO.File.Exists(pngPath)) return (pngPath, hdXml);
        }
        return (null, null);
    }
}