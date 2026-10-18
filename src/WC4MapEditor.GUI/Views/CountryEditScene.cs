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

namespace WC4MapEditor.GUI.Views;

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
        EditorTheme.Apply(this);
        _window = window;
        InitializeUI();
        LoadCountries();
        LoadConquers();
    }

    private void InitializeUI()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        var topBar = EditorTheme.TopBar(
            Loc.T("Country.Title"),
            (_, _) => _window.SetCurrentScene(new BeginScene(_window)),
            EditorTheme.PrimaryButton("+ " + Loc.T("Country.AddCountry"), OnAddCountry));
        Grid.SetRow(topBar, 0);
        _root.Children.Add(topBar);

        // 主体：国家列表 | 征服列表 | 详情
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(mainGrid, 1);

        _countryList = new ListBox();
        _countryList.SelectionChanged += OnCountrySelected;
        var countryPanel = CreateListPanel(Loc.T("Country.CountryList"), _countryList);
        Grid.SetColumn(countryPanel, 0);
        mainGrid.Children.Add(countryPanel);

        var splitter1 = EditorTheme.VSplitter();
        Grid.SetColumn(splitter1, 1);
        mainGrid.Children.Add(splitter1);

        _conquerList = new ListBox();
        _conquerList.SelectionChanged += OnConquerCountrySelected;
        var conquerPanel = CreateListPanel(Loc.T("Country.ConquerList"), _conquerList);
        Grid.SetColumn(conquerPanel, 2);
        mainGrid.Children.Add(conquerPanel);

        var splitter2 = EditorTheme.VSplitter();
        Grid.SetColumn(splitter2, 3);
        mainGrid.Children.Add(splitter2);

        _detailPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var detailScroll = new ScrollViewer
        {
            Content = _detailPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(detailScroll, 4);
        mainGrid.Children.Add(detailScroll);

        _root.Children.Add(mainGrid);

        var statusBar = EditorTheme.StatusBar(out _statusText);
        Grid.SetRow(statusBar, 2);
        _root.Children.Add(statusBar);

        Content = _root;
    }

    /// <summary>左侧列表面板：标题条 + 列表。</summary>
    private static Border CreateListPanel(string title, ListBox list)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = EditorTheme.SectionHeader(title);
        header.Margin = new Thickness(0);   // 面板顶部紧贴顶栏，不要分组标题自带的上边距
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        list.Margin = new Thickness(4);
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        var panel = EditorTheme.Panel(new Thickness(0, 0, 1, 0));
        panel.Child = grid;
        return panel;
    }

    private void LoadCountries()
    {
        _countryList.Items.Clear();
        foreach (var country in _parser.Countries)
        {
            _countryList.Items.Add(new ListBoxItem
            {
                Content = $"[{country.Id}] {country.Name}",
                Tag = country
            });
        }
        SetStatus(Loc.F("Country.CountriesLoaded", _parser.Countries.Count));
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
            _conquerList.Items.Add(new ListBoxItem
            {
                Content = Loc.F("Country.ConquerListItem", cc.ConquerId, conquerName, cc.Star),
                Tag = cc
            });
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

        // 国旗预览 + 国旗操作
        _detailPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Country.Section.Flag")));
        _flagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        LoadFlagPreview(country.Id);

        var editFlagBtn = EditorTheme.Button(Loc.T("Country.EditFlag"), (_, _) =>
        {
            var scene = new FlagEditScene(_window, country.Id, country.Name);
            _window.SetCurrentScene(scene);
        });
        var useExistingFlagBtn = EditorTheme.Button(Loc.T("Country.UseExistingFlag"), (_, _) => OnUseExistingFlag(country.Id));
        useExistingFlagBtn.Margin = new Thickness(6, 0, 0, 0);

        var flagBtnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        flagBtnPanel.Children.Add(editFlagBtn);
        flagBtnPanel.Children.Add(useExistingFlagBtn);

        var flagRow = new StackPanel { Orientation = Orientation.Horizontal };
        flagRow.Children.Add(_flagPreview);
        flagRow.Children.Add(flagBtnPanel);
        var flagCard = EditorTheme.Card(flagRow);
        flagCard.Margin = new Thickness(10, 8, 10, 0);
        _detailPanel.Children.Add(flagCard);

        // 基本信息（可编辑）
        _detailPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Common.BasicInfo")));
        var realName = _parser.GetCountryRealName(country.Id);
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Common.Name"), country.Name,
            v => { country.Name = v; SaveAndReload(country); },
            Loc.T("Country.SyncToRealName"),
            () => { if (_parser.SyncRealNameToName(country.Id)) { SaveAndReload(country); SetStatus(Loc.F("Country.RealNameSyncedToName", country.Name)); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.RealName"), realName,
            v => { if (_parser.SetCountryRealName(country.Id, v)) { ShowCountryDetail(country); SetStatus(Loc.F("Country.RealNameUpdated", v)); } },
            Loc.T("Country.SyncToName"),
            () => { if (_parser.SyncNameToRealName(country.Id)) { ShowCountryDetail(country); SetStatus(Loc.F("Country.NameSyncedToRealName", country.Name)); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.Camp"), country.Camp.ToString(), v => { if (int.TryParse(v, out var n)) { country.Camp = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.InfantryMod"), country.InfantryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.InfantryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.ArmoredMod"), country.ArmoredMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArmoredMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.ArtilleryMod"), country.ArtilleryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArtilleryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock(Loc.T("Country.Field.Logo"), country.Logo.ToString(), v => { if (int.TryParse(v, out var n)) { country.Logo = n; SaveAndReload(country); } }));

        // 删除国家
        var deleteBtn = EditorTheme.DangerButton(Loc.T("Country.DeleteCountry"), (_, _) =>
        {
            var result = MessageBox.Show(Loc.F("Country.DeleteConfirm", country.Id, country.Name), Loc.T("Country.DeleteConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (_parser.RemoveCountry(country.Id))
            {
                _currentCountry = null;
                LoadCountries();
                _detailPanel.Children.Clear();
                SetStatus(Loc.F("Country.Deleted", country.Id, country.Name));
            }
        });
        deleteBtn.HorizontalAlignment = HorizontalAlignment.Left;
        deleteBtn.Margin = new Thickness(10, 16, 10, 8);
        _detailPanel.Children.Add(deleteBtn);
    }

    private void SaveAndReload(CountrySettingData country)
    {
        _parser.SaveCountries();
        LoadCountries();
        SetStatus(Loc.F("Country.Updated", country.Id, country.Name));
        foreach (ListBoxItem item in _countryList.Items)
        {
            if (item.Tag is CountrySettingData c && c.Id == country.Id)
            {
                _countryList.SelectedItem = item;
                break;
            }
        }
    }

    /// <summary>
    /// 可编辑字段行：当前值 + 「编辑」按钮，可选再带一个同步按钮（名称 / 真实名称互相同步）。
    /// 同步按钮文字由调用方传入——字段名已本地化，不能再拿它当标识来判断。
    /// </summary>
    private Border CreateEditableInfoBlock(string label, string value, Action<string> onSave, string? syncLabel = null, Action? onSync = null)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var valueBlock = EditorTheme.Text_(value);
        valueBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(valueBlock, 0);
        row.Children.Add(valueBlock);

        var editBtn = EditorTheme.ToolButton(Loc.T("Common.Edit"), async (_, _) =>
        {
            using var dialog = new Views.Dialogs.SingleInputDialog(_window)
            {
                Title = Loc.F("Country.EditTitle", label),
                Description = Loc.F("Country.EditPrompt", label),
                DefaultValue = value,
                ConfirmText = Loc.T("Common.Save"),
                CancelText = Loc.T("Common.Cancel")
            };
            var newValue = await dialog.ShowAsync();
            if (newValue != null)
            {
                onSave(newValue.Trim());
            }
        });
        editBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(editBtn, 1);
        row.Children.Add(editBtn);

        if (syncLabel != null && onSync != null)
        {
            var syncBtn = EditorTheme.ToolButton(syncLabel, (_, _) => onSync());
            syncBtn.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(syncBtn, 2);
            row.Children.Add(syncBtn);
        }

        return EditorTheme.FormRow(label, row, 110);
    }

    private void ShowConquerCountryDetail(ConquerCountrySettingData cc)
    {
        _detailPanel.Children.Clear();

        var countryName = _parser.GetCountryName(cc.CountryId);
        var conquerName = _parser.GetConquerName(cc.ConquerId);

        _detailPanel.Children.Add(EditorTheme.SectionHeader(Loc.T("Country.Section.ConquerInfo")));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Common.Id"), cc.Id.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Conquer"), $"[{cc.ConquerId}] {conquerName}"));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Country"), $"[{cc.CountryId}] {countryName}"));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Seat"), cc.Seat.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Star"), cc.Star.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Camp"), cc.Camp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.WarTurn"), cc.WarTurn.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.PrizeExp"), cc.PrizeExp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.PrizeGold"), cc.PrizeGold.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.PrizeIndustry"), cc.PrizeIndustry.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.PrizeEnergy"), cc.PrizeEnergy.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.PrizeTech"), cc.PrizeTech.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Photo"), cc.Photo));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.CostMoney"), cc.CostMoney.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.CostGear"), cc.CostGear.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.CostAtomic"), cc.CostAtomic.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Coefficient"), cc.Coefficient.ToString("F2")));
        _detailPanel.Children.Add(CreateInfoBlock(Loc.T("Country.Field.Surrender"), Loc.T(cc.Surrender ? "Common.Yes" : "Common.No")));
    }

    private static Border CreateInfoBlock(string label, string value)
    {
        var text = EditorTheme.Text_(value);
        text.TextWrapping = TextWrapping.Wrap;
        return EditorTheme.FormRow(label, text, 130);
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
            Title = Loc.T("Country.AddCountry"),
            Description = Loc.F("Country.AddPrompt", nextId),
            DefaultValue = "",
            ConfirmText = Loc.T("Common.Ok"),
            CancelText = Loc.T("Common.Cancel")
        };
        var name = await dialog.ShowAsync();
        if (name == null) return;

        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(Loc.T("Country.NameRequired"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            SetStatus(Loc.F("Country.Added", nextId, name));
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
            MessageBox.Show(Loc.T("Country.AddFailed"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            Title = Loc.T("Country.UseExistingFlag"),
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _window,
            ResizeMode = ResizeMode.NoResize,
            Background = EditorTheme.Bg1
        };
        EditorTheme.Apply(inputDialog);

        var panel = new StackPanel { Margin = new Thickness(16) };
        var prompt = EditorTheme.Text_(Loc.F("Country.CopyFlagPrompt", targetCountryId));
        prompt.TextWrapping = TextWrapping.Wrap;
        prompt.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(prompt);

        var idBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(idBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = EditorTheme.PrimaryButton(Loc.T("Common.Ok"));
        okBtn.MinWidth = 80;
        okBtn.Margin = new Thickness(0, 0, 8, 0);
        okBtn.IsDefault = true;
        var cancelBtn = EditorTheme.Button(Loc.T("Common.Cancel"));
        cancelBtn.MinWidth = 80;
        cancelBtn.IsCancel = true;
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
            SetStatus(Loc.T("Country.CopyFlagCancelled"));
            return;
        }

        if (sourceId.Value == targetCountryId)
        {
            MessageBox.Show(Loc.T("Country.CopyFlagSameId"), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetStatus(Loc.F("Country.CopyFlagProgress", sourceId.Value, targetCountryId));

        try
        {
            var bigFlag = _flagService.LoadBigFlag(sourceId.Value);
            var smallFlag = _flagService.LoadSmallFlag(sourceId.Value);
            var hdFlag = _flagService.LoadFlagFromHdAtlas(sourceId.Value);

            if (bigFlag == null && smallFlag == null && hdFlag == null)
            {
                MessageBox.Show(Loc.F("Country.CopyFlagNoSource", sourceId.Value), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                        SetStatus(Loc.T(ok ? "Country.TacticalMapAddOk" : "Country.TacticalMapAddFailed"));

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
                        SetStatus(Loc.T(ok ? "Country.HdAtlasAddOk" : "Country.HdAtlasAddFailed"));
                    }
                }
            }

            bigFlag?.Dispose();
            smallFlag?.Dispose();
            hdFlag?.Dispose();

            _flagService.RefreshCache();

            if (_currentCountry != null)
                LoadFlagPreview(_currentCountry.Id);

            MessageBox.Show(Loc.F("Country.CopyFlagDone", sourceId.Value, targetCountryId), Loc.T("Common.Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(Loc.F("Country.CopyFlagDoneStatus", sourceId.Value, targetCountryId));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Country.CopyFlagFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.F("Country.CopyFlagFailed", ex.Message));
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
