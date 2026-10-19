using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.GUI.Views.Dialogs;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 将领编辑场景（GeneralSettings.json + config/def_portraitpos.xml）。
/// 立绘位置只在编辑时写入 def_portraitpos.xml；EName 改名时立绘位置跟着改名。
/// </summary>
public class GeneralEditScene : JsonEditorScene
{
    private const int MaxImageCacheSize = 50;

    private readonly GeneralSettingParser _parser = GeneralSettingParser.Instance;
    private readonly TableEditor<GeneralSettingData> _editor;
    private Image _headPreview = null!;
    private Image _photoPreview = null!;
    private Image _tacticalHeadPreview = null!;

    // 图片缓存，避免每次切换将领都重新解码 webp；LRU 限制大小，防止内存无限增长导致 GC 卡顿
    private readonly LinkedList<string> _imageCacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _imageCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    // 按 Image 控件区分的取消令牌，避免头像和大图加载互相取消
    private readonly Dictionary<Image, CancellationTokenSource> _imageLoadCtsMap = new();

    public GeneralEditScene(MainWindow window) : base(window, "General", GeneralSettingParser.Instance)
    {
        _editor = Register(new TableEditor<GeneralSettingData>(this, _parser.Table, new TableEditorOptions<GeneralSettingData>
        {
            Describe = g => $"[{g.Id}] {g.Name}  ({g.EName})",
            Matches = (g, q) => g.Id.ToString().Contains(q)
                                || g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || g.EName.Contains(q, StringComparison.OrdinalIgnoreCase),
            GetId = g => g.Id,
            SetId = (g, id) => g.Id = id,
            NextId = _ => _parser.GetNextId(),
            AfterPaste = (_, copy) => _parser.PrepareCopy(copy, copy.EName),
            Remove = g => _parser.DeleteGeneral(g),
            DeleteNote = Loc.T("JsonEditor.General.DeleteNote"),
            SearchPlaceholder = Loc.T("General.SearchPlaceholder")
        }, 160));
        BuildForm(_editor.Form);
        _editor.FieldEdited += (_, _) => RefreshPreviews(_editor.Current);
        Content = CreateListFormLayout(CreateTopBar(Loc.T("General.Title")), _editor.ListPanel, BuildRightPanel());
        _editor.SelectFirst();
    }

    // ===================================== UI 构建 =====================================

    /// <summary>右栏：模板工具条（随机参数 | 兵种专长）+ 表单。</summary>
    private Grid BuildRightPanel()
    {
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(Tool(Loc.T("General.RandomLow"), () => ApplyRandomTemplate(1)));
        tools.Children.Add(Tool(Loc.T("General.RandomMid"), () => ApplyRandomTemplate(2)));
        tools.Children.Add(Tool(Loc.T("General.RandomHigh"), () => ApplyRandomTemplate(3)));
        tools.Children.Add(EditorTheme.VSeparator());
        tools.Children.Add(Tool(Loc.T("General.SpecInfantry"), () => ApplySpecialtyTemplate("infantry")));
        tools.Children.Add(Tool(Loc.T("General.SpecArmor"), () => ApplySpecialtyTemplate("armor")));
        tools.Children.Add(Tool(Loc.T("General.SpecArtillery"), () => ApplySpecialtyTemplate("artillery")));
        tools.Children.Add(Tool(Loc.T("General.SpecNavy"), () => ApplySpecialtyTemplate("navy")));
        tools.Children.Add(Tool(Loc.T("General.SpecAirForce"), () => ApplySpecialtyTemplate("airforce")));

        var grid = new Grid { Background = EditorTheme.Bg1 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(EditorTheme.ToolBar(tools));
        _editor.Form.View.Padding = new Thickness(16);
        Grid.SetRow(_editor.Form.View, 1);
        grid.Children.Add(_editor.Form.View);
        return grid;
    }

    private static Button Tool(string text, Action action)
    {
        var b = EditorTheme.ToolButton(text, (_, _) => action());
        b.Focusable = false;
        return b;
    }

    private void BuildForm(RecordForm<GeneralSettingData> f)
    {
        f.Add(BuildPreviewRow());
        f.OnLoad(RefreshPreviews);

        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("Common.Id"), g => g.Id, (g, id) => _parser.ChangeId(g, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.Text(Loc.T("General.Field.Name"), g => g.Name, (g, v) => g.Name = v);
        f.KeyText(Loc.T("General.Field.EName"), g => g.EName, ChangeEName);
        f.Text(Loc.T("General.Field.Photo"), g => g.Photo, (g, v) => g.Photo = v);

        f.Section(Loc.T("General.Section.Stats"));
        var syncMax = EditorTheme.CheckBox(Loc.T("General.SyncMax"));
        syncMax.ToolTip = Loc.T("General.SyncMaxTip");
        f.Row("", syncMax);
        string max = Loc.T("JsonEditor.General.Max");
        void Ability(string label, string field, Func<GeneralSettingData, int> get, Action<GeneralSettingData, int> set, Func<GeneralSettingData, int> getMax, Action<GeneralSettingData, int> setMax)
            => f.IntPair(label, get, set, $"{field}Max ({max})", getMax, setMax, (value, cap) => GeneralFormParts.AbilityStars(value, cap, syncMax));
        Ability(Loc.T("General.Stat.Infantry"), "Infantry", g => g.Infantry, (g, v) => g.Infantry = v, g => g.InfantryMax, (g, v) => g.InfantryMax = v);
        Ability(Loc.T("General.Stat.Armor"), "Armor", g => g.Armor, (g, v) => g.Armor = v, g => g.ArmorMax, (g, v) => g.ArmorMax = v);
        Ability(Loc.T("General.Stat.Artillery"), "Artillery", g => g.Artillery, (g, v) => g.Artillery = v, g => g.ArtilleryMax, (g, v) => g.ArtilleryMax = v);
        Ability(Loc.T("General.Stat.Navy"), "Navy", g => g.Navy, (g, v) => g.Navy = v, g => g.NavyMax, (g, v) => g.NavyMax = v);
        Ability(Loc.T("General.Stat.AirForce"), "AirForce", g => g.AirForce, (g, v) => g.AirForce = v, g => g.AirForceMax, (g, v) => g.AirForceMax = v);
        Ability(Loc.T("General.Stat.March"), "March", g => g.March, (g, v) => g.March = v, g => g.MarchMax, (g, v) => g.MarchMax = v);

        f.Section(Loc.T("General.Section.RankHp"));
        f.Int(Loc.T("General.Field.MilitaryRank"), g => g.MilitaryRank, (g, v) => g.MilitaryRank = v);
        f.Int(Loc.T("General.Field.Hp"), g => g.Hp, (g, v) => g.Hp = v);

        f.Section(Loc.T("General.Section.ShopUi"));
        f.Int(Loc.T("General.Field.InShop"), g => g.InShop, (g, v) => g.InShop = v);
        f.Int(Loc.T("General.Field.Type"), g => g.Type, (g, v) => g.Type = v);
        f.Int(Loc.T("General.Field.Evaluate"), g => g.Evaluate, (g, v) => g.Evaluate = v);
        f.Int(Loc.T("General.Field.Sequence"), g => g.Sequence, (g, v) => g.Sequence = v);
        f.Int(Loc.T("General.Field.UnlockHQLv"), g => g.UnlockHQLv, (g, v) => g.UnlockHQLv = v);
        f.Int(Loc.T("General.Field.CostMedal"), g => g.CostMedal, (g, v) => g.CostMedal = v);
        f.Int(Loc.T("General.Field.CostGold"), g => g.CostGold, (g, v) => g.CostGold = v);

        f.Section(Loc.T("General.Section.SkillsMedals"));
        NumericUpDown? skillsMax = null;
        f.IntList(Loc.T("General.Field.Skills"), g => g.Skills, (g, v) => g.Skills = v, Loc.T("General.Skill.ListTip"), box => GeneralFormParts.SkillSlots(box, () => skillsMax));
        f.IntList(Loc.T("General.Field.Medals"), g => g.Medals, (g, v) => g.Medals = v);
        skillsMax = f.Int(Loc.T("General.Field.SkillsMax"), g => g.SkillsMax, (g, v) => g.SkillsMax = v);
        f.Int(Loc.T("General.Field.ResetSkills"), g => g.ResetSkills, (g, v) => g.ResetSkills = v);

        // 立绘位置按 EName（没有时用 Photo）索引；只有编辑时才新增 def_portraitpos.xml 的行
        f.Section(Loc.T("General.Section.Portrait"));
        f.Int(Loc.T("General.Field.PortraitPosX"), g => Portrait(g)?.PosX ?? -30, (g, v) => { if (EnsurePortrait(g) is { } p) p.PosX = v; });
        f.Int(Loc.T("General.Field.PortraitPosY"), g => Portrait(g)?.PosY ?? 40, (g, v) => { if (EnsurePortrait(g) is { } p) p.PosY = v; });
        f.Double(Loc.T("General.Field.PortraitScale"), g => Portrait(g)?.Scale ?? 1.0, (g, v) => { if (EnsurePortrait(g) is { } p) p.Scale = v; });

        var tip = EditorTheme.Muted(Loc.T("General.PortraitTip"));
        tip.FontSize = 11;
        tip.TextWrapping = TextWrapping.Wrap;
        var tipCard = EditorTheme.Card(tip);
        tipCard.Margin = new Thickness(0, 12, 0, 0);
        f.Add(tipCard);

        f.JsonPreview(height: 180);
    }

    private static string PortraitKey(GeneralSettingData g) => string.IsNullOrEmpty(g.EName) ? g.Photo : g.EName;

    private PortraitPosEntry? Portrait(GeneralSettingData g) => _parser.GetPortrait(PortraitKey(g));

    private PortraitPosEntry? EnsurePortrait(GeneralSettingData g)
    {
        var key = PortraitKey(g);
        return string.IsNullOrEmpty(key) ? null : _parser.EnsurePortraitDefault(key);
    }

    /// <summary>EName 改名：不能与别的将领重复；立绘位置跟着改名。</summary>
    private string? ChangeEName(GeneralSettingData g, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return Loc.T("JsonEditor.General.ENameRequired");
        var other = _parser.GetByEName(value);
        if (other != null && !ReferenceEquals(other, g)) return Loc.F("JsonEditor.General.ENameTaken", value);
        _parser.RenamePortrait(g.EName, value);
        g.EName = value;
        return null;
    }

    /// <summary>顶部预览区：头像 + 半身像（点击编辑） + 战术地图头像。</summary>
    private UIElement BuildPreviewRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        _headPreview = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform };
        var headWrap = EditorTheme.Card(_headPreview, new Thickness(8));
        headWrap.Margin = new Thickness(0, 0, 12, 0);
        headWrap.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(headWrap);

        var photoPanel = new StackPanel { Orientation = Orientation.Vertical };
        _photoPreview = new Image { Stretch = Stretch.Uniform, Height = 160, Width = 160, Cursor = Cursors.Hand };
        _photoPreview.MouseLeftButtonDown += OnPhotoPreviewClick;
        photoPanel.Children.Add(_photoPreview);
        var photoTip = EditorTheme.Muted(Loc.T("General.ClickToEditPhoto"));
        photoTip.FontSize = 10;
        photoTip.HorizontalAlignment = HorizontalAlignment.Center;
        photoTip.Margin = new Thickness(0, 4, 0, 0);
        photoPanel.Children.Add(photoTip);
        var photoWrap = EditorTheme.Card(photoPanel, new Thickness(8));
        photoWrap.Margin = new Thickness(0, 0, 12, 0);
        photoWrap.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(photoWrap);

        _tacticalHeadPreview = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform };
        var tacticalWrap = EditorTheme.Card(_tacticalHeadPreview, new Thickness(8));
        tacticalWrap.Margin = new Thickness(0);
        tacticalWrap.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(tacticalWrap);
        return row;
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    // ===================================== 预览 =====================================

    private void RefreshPreviews(GeneralSettingData? g)
    {
        if (g == null)
        {
            _headPreview.Source = null;
            _photoPreview.Source = null;
            _tacticalHeadPreview.Source = null;
            return;
        }
        var key = string.IsNullOrEmpty(g.Photo) ? g.EName : g.Photo;
        RefreshImageAsync(_headPreview, _parser.GetHeadPath(key));
        RefreshImageAsync(_photoPreview, _parser.GetGeneralPhotoPath(key));
        RefreshTacticalHeadAsync(key);
    }

    private void RefreshTacticalHeadAsync(string ename)
    {
        if (string.IsNullOrEmpty(ename))
        {
            _tacticalHeadPreview.Source = null;
            return;
        }

        var cacheKey = $"tactical_head_{ename}";
        if (_imageCache.TryGetValue(cacheKey, out var cached))
        {
            TouchCache(cacheKey);
            _tacticalHeadPreview.Source = cached;
            return;
        }

        var token = RestartLoad(_tacticalHeadPreview);
        var imageName = $"head_{ename}";
        _ = Task.Run(() =>
        {
            if (ConfigManager.Instance.TacticalMapParser == null)
            {
                Debug.WriteLine($"[GeneralEditScene] TacticalMapParser 为 null，无法加载 {imageName}");
                return null;
            }
            var imageCache = TacticalMapImageCache.Instance;
            if (!imageCache.IsInitialized)
            {
                imageCache.Initialize();
                if (!imageCache.IsInitialized)
                {
                    Debug.WriteLine("[GeneralEditScene] TacticalMapImageCache 初始化失败");
                    return null;
                }
            }
            var skImage = imageCache.GetImage(imageName)
                         ?? imageCache.GetImage(imageName + ".png")
                         ?? imageCache.GetImage(imageName + ".webp");
            return skImage == null ? null : SkImageToBitmapSource(skImage);
        }, token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(cacheKey, bmp);
                _tacticalHeadPreview.Source = bmp;
            }
            else
            {
                _tacticalHeadPreview.Source = null;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RefreshImageAsync(Image imageControl, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            imageControl.Source = null;
            return;
        }
        // 缓存命中直接显示，并移到 LRU 头部（最近使用）
        if (_imageCache.TryGetValue(path, out var cached))
        {
            TouchCache(path);
            imageControl.Source = cached;
            return;
        }
        var token = RestartLoad(imageControl);
        _ = Task.Run(() => SkiaBitmaps.Load(path), token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            if (t.Result is BitmapSource bmp)
            {
                AddToCache(path, bmp);
                imageControl.Source = bmp;
            }
        }, CancellationToken.None, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>取消该控件之前的加载任务（快速切换记录时任务不堆积），返回新任务的令牌。</summary>
    private CancellationToken RestartLoad(Image imageControl)
    {
        if (_imageLoadCtsMap.TryGetValue(imageControl, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _imageLoadCtsMap[imageControl] = cts;
        return cts.Token;
    }

    private void AddToCache(string path, BitmapSource bmp)
    {
        if (_imageCache.ContainsKey(path))
        {
            _imageCache[path] = bmp;
            TouchCache(path);
            return;
        }
        // 淘汰最久未使用的缓存项（WriteableBitmap 从字典移除后由 GC 回收）
        while (_imageCache.Count >= MaxImageCacheSize && _imageCacheOrder.Count > 0)
        {
            var oldest = _imageCacheOrder.Last!.Value;
            _imageCacheOrder.RemoveLast();
            _imageCacheNodes.Remove(oldest);
            _imageCache.Remove(oldest);
        }
        _imageCache[path] = bmp;
        _imageCacheNodes[path] = _imageCacheOrder.AddFirst(path);
    }

    private void TouchCache(string path)
    {
        if (_imageCacheNodes.TryGetValue(path, out var node))
        {
            _imageCacheOrder.Remove(node);
            _imageCacheOrder.AddFirst(node);
        }
    }

    private static BitmapSource? SkImageToBitmapSource(SKImage skImage)
    {
        using var bitmap = SKBitmap.FromImage(skImage);
        return bitmap == null ? null : SkiaBitmaps.ToBitmapSource(bitmap);
    }

    protected override void DisposeScene()
    {
        foreach (var cts in _imageLoadCtsMap.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _imageLoadCtsMap.Clear();
    }

    // ===================================== 动作 =====================================

    /// <summary>点半身像进入立绘裁剪场景（编辑中的数据留在解析器里，回来后仍在）。</summary>
    private void OnPhotoPreviewClick(object sender, MouseButtonEventArgs e)
    {
        var g = _editor.Current;
        if (g == null) return;
        var ename = string.IsNullOrEmpty(g.EName) ? g.Photo : g.EName;
        if (string.IsNullOrEmpty(ename))
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Info"), Loc.T("General.NoEnameForPhoto"));
            return;
        }
        Session.Flush();
        AppWindow.SetCurrentScene(new GeneralPhotoEditScene(AppWindow, ename));
    }

    protected override async void OnAddCommand()
    {
        Session.Flush();
        using var enameDlg = new SingleInputDialog(AppWindow)
        {
            Title = Loc.T("General.Add"),
            Description = Loc.T("General.AddEnamePrompt"),
            DefaultValue = "",
            MinValue = 0,
            MaxValue = 0
        };
        var ename = (await enameDlg.ShowAsync())?.Trim();
        if (string.IsNullOrEmpty(ename)) return;
        if (_parser.GetByEName(ename) != null)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.F("General.EnameExists", ename));
            return;
        }

        using var nameDlg = new SingleInputDialog(AppWindow)
        {
            Title = Loc.T("General.Add"),
            Description = Loc.F("General.AddNamePrompt", ename),
            DefaultValue = ename
        };
        var cname = await nameDlg.ShowAsync() ?? ename;

        var (g, p) = _parser.AddNewGeneral(cname, ename);
        _editor.Select(g);
        Session.Commit(Loc.F("JsonEditor.History.Add", g.Id));
        SetStatus(Loc.F("General.StatusAdded", g.Name, g.EName, g.Id, p.PosX, p.PosY, p.Scale));
    }

    protected override void OnValidateCommand()
    {
        var problems = new List<string>();
        var seenIds = new HashSet<int>();
        var skillIds = SkillSettingParser.Instance.Items.Select(s => s.Id).ToHashSet();
        foreach (var g in _parser.Items)
        {
            string prefix = $"[Id={g.Id}]";
            if (!seenIds.Add(g.Id)) problems.Add($"{prefix} {Loc.T("Common.DuplicateId")}");
            // 只有名将（Type 1）的能力值受上限约束；其它类型的上限字段在游戏数据里本来就是 0
            if (g.Type == 1)
            {
                foreach (var (field, value, cap) in Abilities(g).Where(a => a.Value > a.Max))
                    problems.Add($"{prefix} {Loc.F("General.Validate.AboveMax", field, value, cap)}");
            }
            foreach (int skill in g.Skills.Where(id => id != 0 && !skillIds.Contains(id)))
                problems.Add($"{prefix} {Loc.F("General.Validate.SkillMissing", skill)}");
        }
        ShowValidation(problems, _parser.Items.Count);
    }

    private static IEnumerable<(string Field, int Value, int Max)> Abilities(GeneralSettingData g)
    {
        yield return (nameof(g.Infantry), g.Infantry, g.InfantryMax);
        yield return (nameof(g.Armor), g.Armor, g.ArmorMax);
        yield return (nameof(g.Artillery), g.Artillery, g.ArtilleryMax);
        yield return (nameof(g.Navy), g.Navy, g.NavyMax);
        yield return (nameof(g.AirForce), g.AirForce, g.AirForceMax);
        yield return (nameof(g.March), g.March, g.MarchMax);
    }

    // ===================================== 随机参数模板 =====================================

    /// <summary>按模板随机改写当前将领的数值与技能等级（直接改记录，可撤销）。</summary>
    private void ApplyRandomTemplate(int templateId)
    {
        var g = _editor.Current;
        if (g == null)
        {
            SetStatus(Loc.T("Common.SelectItemFirst"));
            return;
        }
        var template = ConfigManager.Instance.GetGeneralRandomTemplates().FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.F("General.RandomTemplateNotFound", templateId));
            return;
        }

        Session.Flush();
        var random = Random.Shared;
        g.Evaluate = template.Evaluate;
        g.Sequence = random.Next(template.SequenceMin, template.SequenceMax + 1);
        g.UnlockHQLv = template.UnlockHQLv;
        g.CostMedal = random.Next(template.CostMedalMin, template.CostMedalMax + 1);
        g.MilitaryRank = random.Next(template.MilitaryRankMin, template.MilitaryRankMax + 1);
        g.March = random.Next(template.MarchMin, template.MarchMax + 1);
        g.Hp = random.Next(template.HpMin, template.HpMax + 1);

        // 兵种能力：当前值除以模板除数
        g.Infantry = Divide(g.Infantry, template.InfantryDivisor);
        g.Artillery = Divide(g.Artillery, template.ArtilleryDivisor);
        g.Armor = Divide(g.Armor, template.ArmorDivisor);
        g.Navy = Divide(g.Navy, template.NavyDivisor);
        g.AirForce = Divide(g.AirForce, template.AirForceDivisor);

        // 技能等级随机化：前 SkillCount 个非空技能保留技能类别，只换等级（Id 个位）
        var skills = g.Skills.ToList();
        for (int i = 0; i < Math.Min(template.SkillCount, skills.Count); i++)
        {
            if (skills[i] > 0) skills[i] = skills[i] / 10 * 10 + random.Next(template.SkillLevelMin, template.SkillLevelMax + 1);
        }
        g.Skills = skills;

        AfterTemplate(g, Loc.F("General.StatusRandomApplied", template.Name));
    }

    private static int Divide(int value, double divisor) => divisor == 0 ? value : (int)(value / divisor);

    // ===================================== 兵种专长模板 =====================================

    private void ApplySpecialtyTemplate(string specialtyKey)
    {
        var g = _editor.Current;
        if (g == null)
        {
            SetStatus(Loc.T("Common.SelectItemFirst"));
            return;
        }
        var template = ConfigManager.Instance.GetGeneralSpecialtyTemplates().FirstOrDefault(t => t.Key.Equals(specialtyKey, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.F("General.SpecialtyTemplateNotFound", specialtyKey));
            return;
        }

        Session.Flush();
        var random = Random.Shared;
        SetStat(g, template.MainStat, template.MainStatValue);
        SetStat(g, template.SubStat1, template.SubStat1Value);
        SetStat(g, template.SubStat2, template.SubStat2Value);
        g.March = random.Next(template.MarchMin, template.MarchMax + 1);
        g.Hp = random.Next(template.HpMin, template.HpMax + 1);

        // 技能：从技能池随机选 SkillCount 个（技能类别 + 随机等级），其余槽位清零，槽位数保持不变
        if (template.SkillPool.Count > 0)
        {
            var picked = template.SkillPool.OrderBy(_ => random.Next()).Take(template.SkillCount)
                .Select(id => id / 10 * 10 + random.Next(template.SkillLevelMin, template.SkillLevelMax + 1))
                .ToList();
            while (picked.Count < g.Skills.Count) picked.Add(0);
            g.Skills = picked;
        }

        AfterTemplate(g, Loc.F("General.StatusSpecialtyApplied", template.Name));
    }

    /// <summary>模板里的能力名（Infantry / Armor …）对应同名的整数字段。</summary>
    private static void SetStat(GeneralSettingData g, string field, int value)
    {
        if (string.IsNullOrEmpty(field)) return;
        var property = typeof(GeneralSettingData).GetProperty(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(int) && property.CanWrite) property.SetValue(g, value);
    }

    private void AfterTemplate(GeneralSettingData g, string status)
    {
        _editor.Form.Reload();
        _editor.RefreshRow(g);
        Session.Commit(status);
        SetStatus(status);
    }
}
