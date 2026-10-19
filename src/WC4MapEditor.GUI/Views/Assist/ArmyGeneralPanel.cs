using System.Diagnostics;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 单位设置对话框左侧的将领信息面板（v1 / v3 共用）：头像、名字、兵种专长、军衔、HP 等级与技能，
/// 数据来自 GeneralSettings.json。"按将领填写"按钮把对话框里的军衔 / HP 等级 / 技能等级填成该将领的数据。
/// </summary>
internal sealed class ArmyGeneralPanel : Border
{
    private const double AvatarSize = 100;

    private readonly Image _avatar;
    private readonly TextBlock _name;
    private readonly TextBlock _subtitle;
    private readonly StackPanel _details = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _fillButton;
    private GeneralSettingData? _general;
    private int? _shownId;

    /// <summary>点击"按将领填写"时触发，参数为面板当前显示的将领。</summary>
    public event Action<GeneralSettingData>? FillRequested;

    public ArmyGeneralPanel()
    {
        Background = EditorTheme.Bg2;
        BorderBrush = EditorTheme.Border;
        BorderThickness = new Thickness(0, 0, 1, 0);

        var avatarHost = new Grid
        {
            Width = AvatarSize,
            Height = AvatarSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        avatarHost.Children.Add(new Ellipse
        {
            Fill = EditorTheme.Bg3,
            Stroke = EditorTheme.Accent,
            StrokeThickness = 3,
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Accent),
                ShadowDepth = 0,
                BlurRadius = 15,
                Opacity = 0.7
            }
        });
        double inner = AvatarSize - 6;
        _avatar = new Image
        {
            Width = inner,
            Height = inner,
            Stretch = Stretch.UniformToFill,
            Clip = new EllipseGeometry(new Point(inner / 2, inner / 2), inner / 2, inner / 2),
            Visibility = Visibility.Collapsed
        };
        avatarHost.Children.Add(_avatar);

        _name = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = EditorTheme.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _subtitle = EditorTheme.Muted("");
        _subtitle.FontSize = 11;
        _subtitle.TextAlignment = TextAlignment.Center;
        _subtitle.TextWrapping = TextWrapping.Wrap;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitle.Margin = new Thickness(0, 2, 0, 0);

        _fillButton = EditorTheme.Button(Loc.T("Assist.Army.General.Fill"), (_, _) =>
        {
            if (_general != null) FillRequested?.Invoke(_general);
        });
        _fillButton.ToolTip = Loc.T("Assist.Army.General.FillTooltip");
        _fillButton.Margin = new Thickness(0, 12, 0, 0);
        // 键盘焦点留在右侧表单里（Enter 逐项前进），按钮只用鼠标点
        _fillButton.IsTabStop = false;

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(avatarHost);
        stack.Children.Add(_name);
        stack.Children.Add(_subtitle);
        stack.Children.Add(_details);
        stack.Children.Add(_fillButton);

        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };

        ShowGeneral(0);
    }

    /// <summary>显示指定将领（0 = 无将领）；与当前显示的相同则不重复加载。</summary>
    public void ShowGeneral(int generalId)
    {
        if (_shownId == generalId) return;
        _shownId = generalId;

        _details.Children.Clear();
        _general = generalId == 0 ? null : GeneralSettingParser.Instance.GetById(generalId);
        _fillButton.Visibility = _general != null ? Visibility.Visible : Visibility.Collapsed;

        if (_general == null)
        {
            SetAvatar(null);
            _name.Text = generalId == 0 ? Loc.T("Assist.Army.NoGeneral") : Loc.F("Assist.Army.GeneralId", generalId);
            _subtitle.Text = generalId == 0 ? "" : Loc.T("Assist.Army.General.NotFound");
            _subtitle.Visibility = generalId == 0 ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        var general = _general;
        SetAvatar(GeneralPortraitLoader.Load(general.EName));
        _name.Text = string.IsNullOrWhiteSpace(general.Name) ? general.EName : general.Name;
        string idLine = Loc.F("Assist.Army.General.IdLine", generalId);
        _subtitle.Text = string.IsNullOrWhiteSpace(general.EName) || general.EName == _name.Text
            ? idLine
            : $"{idLine} · {general.EName}";
        _subtitle.Visibility = Visibility.Visible;

        AddRow(Loc.T("General.Stat.Infantry"), general.Infantry);
        AddRow(Loc.T("General.Stat.Armor"), general.Armor);
        AddRow(Loc.T("General.Stat.Artillery"), general.Artillery);
        AddRow(Loc.T("General.Stat.Navy"), general.Navy);
        AddRow(Loc.T("General.Stat.AirForce"), general.AirForce);
        AddRow(Loc.T("Assist.Army.Field.Rank"), general.MilitaryRank).Margin = new Thickness(0, 8, 0, 2);
        AddRow(Loc.T("Assist.Army.Field.HpLevel"), general.Hp);

        if (general.Skills is { Count: > 0 } skills)
        {
            var header = EditorTheme.Muted(Loc.T("Assist.Army.Section.Skills"));
            header.FontSize = 11;
            header.Margin = new Thickness(0, 8, 0, 2);
            _details.Children.Add(header);
            foreach (int skillId in skills)
            {
                _details.Children.Add(new TextBlock
                {
                    Text = DescribeSkill(skillId),
                    FontSize = 12,
                    Foreground = EditorTheme.Text,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }
        }
    }

    private void SetAvatar(BitmapSource? image)
    {
        _avatar.Source = image;
        _avatar.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid AddRow(string label, int value)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = EditorTheme.Muted(label);
        labelText.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = EditorTheme.Text,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);

        _details.Children.Add(row);
        return row;
    }

    /// <summary>技能 ID（如 1431）→ "阵地战 Lv1"：名字取 string table 的 skill_name_{Type}，等级取 SkillSettings 的 Level。</summary>
    private static string DescribeSkill(int skillId)
    {
        var parser = SkillSettingParser.Instance;
        var skill = parser.GetById(skillId);
        if (skill == null) return Loc.F("Assist.Army.General.UnknownSkill", skillId);

        string name = parser.GetSkillName(skill.Type);
        if (string.IsNullOrWhiteSpace(name)) name = skill.Name;
        int level = skill.Level > 0 ? skill.Level : GeneralSettingParser.CalculateSkillLevel(skillId);
        return Loc.F("Assist.Army.General.SkillLine", name, level);
    }
}

/// <summary>
/// 将领头像：优先取战术地图图集里的 <c>head_{EName}.png</c>（与地图上单位头顶的头像同源），
/// 没有时退回 image/heads 下的 <c>general_circle_{EName}.webp/png</c>。只缓存找到的结果，之后补上资源重开对话框即可显示。
/// </summary>
internal static class GeneralPortraitLoader
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? Load(string ename)
    {
        if (string.IsNullOrWhiteSpace(ename)) return null;
        if (Cache.TryGetValue(ename, out var cached)) return cached;

        var image = LoadFromTacticalMap(ename) ?? LoadFromHeadsDirectory(ename);
        if (image != null) Cache[ename] = image;
        return image;
    }

    private static BitmapSource? LoadFromTacticalMap(string ename)
    {
        var cache = TacticalMapImageCache.Instance;
        if (!cache.IsInitialized) cache.Initialize();
        var head = cache.GetImage($"head_{ename}.png");
        return head == null ? null : ToBitmapSource(head);
    }

    private static BitmapSource? LoadFromHeadsDirectory(string ename)
    {
        string? path = GeneralSettingParser.Instance.GetHeadPath(ename);
        if (path == null) return null;
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) return null;
            using var image = SKImage.FromBitmap(decoded);
            return ToBitmapSource(image);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralPortraitLoader] 读取头像失败 {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>SKImage → 冻结的 WPF 位图（预乘 BGRA 与 Pbgra32 的内存布局一致）。</summary>
    private static BitmapSource? ToBitmapSource(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) return null;

        var source = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null,
            bitmap.GetPixels(), info.Height * bitmap.RowBytes, bitmap.RowBytes);
        source.Freeze();
        return source;
    }
}
