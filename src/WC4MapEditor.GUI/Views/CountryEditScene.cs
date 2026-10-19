using System.IO;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Services;
using WC4MapEditor.GUI.Views.Dialogs;
using WC4MapEditor.GUI.Views.EditScenes;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 国家数据编辑场景：CountrySettings.json + stringtable 的国家真实名称 country_{Id}，
/// 右侧显示该国家参与的征服（ConquerCountrySettings.json，只读）。
/// 修改只记在内存里，统一用“保存”写盘（旧实现每改一个字段就立即写文件）。
/// </summary>
public class CountryEditScene : JsonEditorScene
{
    private readonly CountrySettingParser _parser = CountrySettingParser.Instance;
    private readonly IFlagImageService _flagService = new FlagImageService();
    private readonly TableEditor<CountrySettingData> _editor;
    private readonly ListBox _conquerList = new();
    private readonly StackPanel _conquerDetail = new() { Margin = new Thickness(0, 0, 0, 16) };
    private Image _flagPreview = null!;
    private Image _smallFlagPreview = null!;

    public CountryEditScene(MainWindow window) : base(window, "Country", CountrySettingParser.Instance)
    {
        _editor = Register(new TableEditor<CountrySettingData>(this, _parser.CountryTable, new TableEditorOptions<CountrySettingData>
        {
            Describe = DescribeCountry,
            Matches = (c, q) => c.Id.ToString().Contains(q)
                                || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || _parser.GetCountryRealName(c.Id).Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = c => c.Id,
            SetId = (c, id) => c.Id = id,
            NextId = _ => _parser.GetNextCountryId(),
            AfterPaste = (sourceId, copy) => _parser.Texts.Copy(sourceId, copy.Id),
            Remove = _parser.RemoveCountry,
            DeleteNote = Loc.T("JsonEditor.Country.DeleteNote"),
            SearchPlaceholder = Loc.T("JsonEditor.Country.SearchPlaceholder")
        }));
        BuildForm(_editor.Form);
        _editor.CurrentChanged += LoadConquerCountries;
        _conquerList.Margin = new Thickness(4);
        _conquerList.SelectionChanged += (_, _) => ShowConquerCountryDetail(_conquerList.SelectedItem as ListBoxItem);
        Content = BuildLayout();
        _editor.SelectFirst();
    }

    private string DescribeCountry(CountrySettingData c)
    {
        var realName = _parser.GetCountryRealName(c.Id);
        return string.IsNullOrEmpty(realName) || realName == c.Name ? $"[{c.Id}] {c.Name}" : $"[{c.Id}] {realName}  ({c.Name})";
    }

    // ============================================================= 布局 =============================================================

    private Grid BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        var top = CreateTopBar(Loc.T("Country.Title"));
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // 主体：国家列表 | 国家属性 | 参与的征服
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        Grid.SetRow(main, 1);

        main.Children.Add(_editor.ListPanel);
        var splitter1 = EditorTheme.VSplitter();
        Grid.SetColumn(splitter1, 1);
        main.Children.Add(splitter1);
        Grid.SetColumn(_editor.Form.View, 2);
        main.Children.Add(_editor.Form.View);
        var splitter2 = EditorTheme.VSplitter();
        Grid.SetColumn(splitter2, 3);
        main.Children.Add(splitter2);

        var conquerGrid = new Grid();
        conquerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        conquerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        conquerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = EditorTheme.SectionHeader(Loc.T("Country.ConquerList"));
        header.Margin = new Thickness(0);
        conquerGrid.Children.Add(header);
        Grid.SetRow(_conquerList, 1);
        conquerGrid.Children.Add(_conquerList);
        var detailScroll = new ScrollViewer { Content = _conquerDetail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(detailScroll, 2);
        conquerGrid.Children.Add(detailScroll);
        var conquerPanel = EditorTheme.Panel(new Thickness(1, 0, 0, 0));
        conquerPanel.Child = conquerGrid;
        Grid.SetColumn(conquerPanel, 4);
        main.Children.Add(conquerPanel);
        root.Children.Add(main);

        var status = CreateStatusBar();
        Grid.SetRow(status, 2);
        root.Children.Add(status);
        return root;
    }

    private void BuildForm(RecordForm<CountrySettingData> f)
    {
        // 国旗预览 + 国旗操作
        f.Section(Loc.T("Country.Section.Flag"));
        _flagPreview = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        _smallFlagPreview = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        var editFlagBtn = EditorTheme.Button(Loc.T("Country.EditFlag"), (_, _) =>
        {
            if (_editor.Current is { } c) AppWindow.SetCurrentScene(new FlagEditScene(AppWindow, c.Id, c.Name));
        });
        var useExistingFlagBtn = EditorTheme.Button(Loc.T("Country.UseExistingFlag"), (_, _) =>
        {
            if (_editor.Current is { } c) OnUseExistingFlag(c.Id);
        });
        useExistingFlagBtn.Margin = new Thickness(6, 0, 0, 0);
        var flagButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        flagButtons.Children.Add(editFlagBtn);
        flagButtons.Children.Add(useExistingFlagBtn);
        var flagRow = new StackPanel { Orientation = Orientation.Horizontal };
        flagRow.Children.Add(CaptionedFlag(_flagPreview, Loc.T("Country.Flag.Hd")));
        flagRow.Children.Add(CaptionedFlag(_smallFlagPreview, Loc.T("Country.Flag.Small")));
        flagRow.Children.Add(flagButtons);
        var flagCard = EditorTheme.Card(flagRow);
        flagCard.Margin = new Thickness(10, 8, 10, 0);
        f.Add(flagCard);
        f.OnLoad(c => LoadFlagPreview(c?.Id));

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Common.Id"), c => c.Id, (c, id) =>
        {
            if (!_parser.IsCountryIdFree(id)) return Loc.F("JsonEditor.Error.IdTaken", id);
            _parser.ChangeCountryId(c, id);
            return null;
        });
        var nameBox = f.Text(Loc.T("Common.Name"), c => c.Name, (c, v) => c.Name = v);
        var realNameBox = f.StringText(Loc.T("Country.Field.RealName"), c => _parser.GetCountryRealName(c.Id), (c, v) => _parser.SetCountryRealName(c.Id, v));
        var syncRow = new StackPanel { Orientation = Orientation.Horizontal };
        // 名称 ← 真实名称；真实名称 ← 名称（改的是输入框，走正常的字段写回与历史）
        syncRow.Children.Add(EditorTheme.ToolButton(Loc.T("Country.SyncToRealName"), (_, _) => nameBox.Text = realNameBox.Text));
        var toName = EditorTheme.ToolButton(Loc.T("Country.SyncToName"), (_, _) => realNameBox.Text = nameBox.Text);
        toName.Margin = new Thickness(6, 0, 0, 0);
        syncRow.Children.Add(toName);
        f.Row("", syncRow);
        f.Int(Loc.T("Country.Field.Camp"), c => c.Camp, (c, v) => c.Camp = v);
        f.Int(Loc.T("Country.Field.InfantryMod"), c => c.InfantryMod, (c, v) => c.InfantryMod = v);
        f.Int(Loc.T("Country.Field.ArmoredMod"), c => c.ArmoredMod, (c, v) => c.ArmoredMod = v);
        f.Int(Loc.T("Country.Field.ArtilleryMod"), c => c.ArtilleryMod, (c, v) => c.ArtilleryMod = v);
        f.Int(Loc.T("Country.Field.Logo"), c => c.Logo, (c, v) => c.Logo = v);

        f.JsonPreview(height: 160);
    }

    /// <summary>国旗预览图 + 下方的小标题（高清图集旗 / 战术地图小旗）。</summary>
    private static StackPanel CaptionedFlag(Image image, string caption)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(image);
        var text = EditorTheme.Muted(caption);
        text.FontSize = 11;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(text);
        return panel;
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    // ============================================================= 征服 =============================================================

    private void LoadConquerCountries(CountrySettingData? country)
    {
        _conquerList.Items.Clear();
        _conquerDetail.Children.Clear();
        if (country == null) return;
        foreach (var cc in _parser.GetConquerCountriesByCountryId(country.Id))
        {
            _conquerList.Items.Add(new ListBoxItem
            {
                Content = Loc.F("Country.ConquerListItem", cc.ConquerId, _parser.GetConquerName(cc.ConquerId), cc.Star),
                Tag = cc
            });
        }
    }

    private void ShowConquerCountryDetail(ListBoxItem? item)
    {
        _conquerDetail.Children.Clear();
        if (item?.Tag is not ConquerCountrySettingData cc) return;

        _conquerDetail.Children.Add(EditorTheme.SectionHeader(Loc.T("Country.Section.ConquerInfo")));
        void Info(string label, string value)
        {
            var text = EditorTheme.Text_(value);
            text.TextWrapping = TextWrapping.Wrap;
            _conquerDetail.Children.Add(EditorTheme.FormRow(label, text, 110));
        }
        Info(Loc.T("Common.Id"), cc.Id.ToString());
        Info(Loc.T("Country.Field.Conquer"), $"[{cc.ConquerId}] {_parser.GetConquerName(cc.ConquerId)}");
        Info(Loc.T("Country.Field.Country"), $"[{cc.CountryId}] {_parser.GetCountryName(cc.CountryId)}");
        Info(Loc.T("Country.Field.Seat"), cc.Seat.ToString());
        Info(Loc.T("Country.Field.Star"), cc.Star.ToString());
        Info(Loc.T("Country.Field.Camp"), cc.Camp.ToString());
        Info(Loc.T("Country.Field.WarTurn"), cc.WarTurn.ToString());
        Info(Loc.T("Country.Field.PrizeExp"), cc.PrizeExp.ToString());
        Info(Loc.T("Country.Field.PrizeGold"), cc.PrizeGold.ToString());
        Info(Loc.T("Country.Field.PrizeIndustry"), cc.PrizeIndustry.ToString());
        Info(Loc.T("Country.Field.PrizeEnergy"), cc.PrizeEnergy.ToString());
        Info(Loc.T("Country.Field.PrizeTech"), cc.PrizeTech.ToString());
        Info(Loc.T("Country.Field.Photo"), cc.Photo);
        Info(Loc.T("Country.Field.CostMoney"), cc.CostMoney.ToString());
        Info(Loc.T("Country.Field.CostGear"), cc.CostGear.ToString());
        Info(Loc.T("Country.Field.CostAtomic"), cc.CostAtomic.ToString());
        Info(Loc.T("Country.Field.Coefficient"), cc.Coefficient.ToString("F2"));
        Info(Loc.T("Country.Field.Surrender"), Loc.T(cc.Surrender ? "Common.Yes" : "Common.No"));
    }

    // ============================================================= 新增 =============================================================

    protected override async void OnAddCommand()
    {
        Session.Flush();
        var nextId = _parser.GetNextCountryId();
        using var dialog = new SingleInputDialog(AppWindow)
        {
            Title = Loc.T("Country.AddCountry"),
            Description = Loc.F("Country.AddPrompt", nextId),
            DefaultValue = "",
            ConfirmText = Loc.T("Common.Ok"),
            CancelText = Loc.T("Common.Cancel")
        };
        var name = (await dialog.ShowAsync())?.Trim();
        if (name == null) return;
        if (name.Length == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("Country.NameRequired"));
            return;
        }

        // 名称同时写进 JSON 的 Name 与 stringtable 的 country_{Id}（游戏显示的是后者）
        _parser.SetCountryRealName(nextId, name);
        _editor.Insert(new CountrySettingData { Id = nextId, Name = name }, Loc.F("JsonEditor.History.Add", nextId));
        SetStatus(Loc.F("Country.Added", nextId, name));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seen = new HashSet<int>();
        foreach (var c in _parser.Countries)
        {
            string pfx = $"[Id={c.Id}]";
            if (!seen.Add(c.Id)) problems.Add($"{pfx} {Loc.T("Common.DuplicateId")}");
            if (string.IsNullOrEmpty(c.Name)) problems.Add($"{pfx} {Loc.T("Common.NameEmpty")}");
            if (string.IsNullOrEmpty(_parser.GetCountryRealName(c.Id))) problems.Add($"{pfx} {Loc.F("JsonEditor.Country.RealNameMissing", c.Id)}");
        }
        foreach (var cc in _parser.ConquerCountries)
        {
            if (_parser.GetCountryById(cc.CountryId) == null) problems.Add(Loc.F("JsonEditor.Country.ConquerCountryMissing", cc.Id, cc.CountryId));
        }
        ShowValidation(problems, _parser.Countries.Count);
    }

    // ============================================================= 国旗 =============================================================

    private void LoadFlagPreview(int? countryId)
    {
        _flagPreview.Source = null;
        _smallFlagPreview.Source = null;
        if (countryId == null) return;
        try
        {
            using var flag = _flagService.LoadFlagFromHdAtlas(countryId.Value) ?? _flagService.LoadBigFlag(countryId.Value);
            if (flag != null) _flagPreview.Source = SkiaBitmaps.ToBitmapSource(flag);
            using var small = _flagService.LoadSmallFlag(countryId.Value);
            if (small != null) _smallFlagPreview.Source = SkiaBitmaps.ToBitmapSource(small);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"[CountryEditScene] 国旗预览失败: {ex.Message}");
        }
    }

    private void OnUseExistingFlag(int targetCountryId)
    {
        var inputDialog = EditorDialogs.CreateWindow(DialogOwner, Loc.T("Country.UseExistingFlag"), 380);
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
        idBox.Focus();
        inputDialog.ShowDialog();

        if (sourceId == null || sourceId.Value <= 0)
        {
            SetStatus(Loc.T("Country.CopyFlagCancelled"));
            return;
        }
        if (sourceId.Value == targetCountryId)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("Country.CopyFlagSameId"));
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
                EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.F("Country.CopyFlagNoSource", sourceId.Value));
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
                        if (bigFlag != null) imagesToAdd.Add(($"flag_{targetCountryId}.png", bigFlag.Copy()));
                        if (smallFlag != null) imagesToAdd.Add(($"f_{targetCountryId:D2}.png", smallFlag.Copy()));
                        var ok = tmEditor.AddImagesAndArrange(imagesToAdd, tacticalImagePath, tacticalXmlPath);
                        SetStatus(Loc.T(ok ? "Country.TacticalMapAddOk" : "Country.TacticalMapAddFailed"));
                        foreach (var (_, img) in imagesToAdd) img.Dispose();
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
            LoadFlagPreview(_editor.Current?.Id);

            EditorDialogs.Info(DialogOwner, Loc.T("Common.Success"), Loc.F("Country.CopyFlagDone", sourceId.Value, targetCountryId));
            SetStatus(Loc.F("Country.CopyFlagDoneStatus", sourceId.Value, targetCountryId));
        }
        catch (Exception ex)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.F("Country.CopyFlagFailed", ex.Message));
            SetStatus(Loc.F("Country.CopyFlagFailed", ex.Message));
        }
    }

    private (string? imagePath, string? xmlPath) FindTacticalMapFiles()
    {
        var am = AssetManager.Default;
        if (am.IsLoaded)
        {
            var imgEntry = am.Find("tacticalmap.webp") ?? am.Find("tacticalmap.png");
            var xmlEntry = am.Find("tacticalmap.xml");
            if (imgEntry != null && xmlEntry != null)
                return (imgEntry.FullPath, xmlEntry.FullPath);
        }

        var dir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(dir)) return (null, null);

        var dirs = new[] { dir, IOPath.GetDirectoryName(dir) ?? "" };
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            foreach (var ext in new[] { ".png", ".webp" })
            {
                var imgPath = IOPath.Combine(d, $"tacticalmap{ext}");
                var xmlPath = IOPath.Combine(d, "tacticalmap.xml");
                if (File.Exists(imgPath) && File.Exists(xmlPath))
                    return (imgPath, xmlPath);
            }
        }
        return (null, null);
    }

    private (string? imagePath, string? xmlPath) FindHdAtlasFiles()
    {
        var hdImg = _parser.FlagsHdImagePath;
        var hdXml = _parser.FlagsHdXmlPath;

        if (!string.IsNullOrEmpty(hdImg) && !string.IsNullOrEmpty(hdXml) && File.Exists(hdImg) && File.Exists(hdXml))
            return (hdImg, hdXml);

        if (!string.IsNullOrEmpty(hdXml) && File.Exists(hdXml))
        {
            var dir = IOPath.GetDirectoryName(hdImg) ?? "";
            var pngPath = IOPath.Combine(dir, "image_flags_hd.png");
            if (File.Exists(pngPath)) return (pngPath, hdXml);
        }
        return (null, null);
    }
}
