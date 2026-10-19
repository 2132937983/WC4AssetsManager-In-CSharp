using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using WC4MapEditor.GUI.Global;
using WC4MapEditor.GUI.Services;
using WC4MapEditor.GUI.Views.Assist;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 开始界面的应用级功能：背景轮播（BackGround.json）、标题栏词云、背景音乐按钮、设置 / 推荐窗口、开发者模式按钮。
/// 开始界面保持自己的样式，这里只负责行为。
/// </summary>
public partial class BeginScene
{
    /// <summary>背景轮播的一张：图片路径与停留时长（毫秒）</summary>
    private sealed record BackgroundSlide(string Path, int DurationMs);

    private const int DefaultSlideDurationMs = 5000;
    private const int DefaultSlideTransitionMs = 1000;
    private const int MinSlideDurationMs = 500;
    private const int WordCloudIntervalSeconds = 5;

    /// <summary>战术图集（开发者"图块场景"按钮打开）在 assets 里可能的图片格式</summary>
    private static readonly string[] TacticalAtlasExtensions = { ".webp", ".png", ".jpg", ".jpeg", ".bmp" };

    private List<BackgroundSlide> _slides = new();
    private int _slideTransitionMs = DefaultSlideTransitionMs;
    private bool _slideLoop = true;
    private DispatcherTimer? _wordCloudTimer;
    private Button _musicButton = null!;
    private Button _tileSceneButton = null!;

    private void BeginScene_Unloaded(object? sender, RoutedEventArgs e)
    {
        _bgTimer?.Stop();
        _wordCloudTimer?.Stop();
        MusicPlayerService.Instance.StateChanged -= OnMusicStateChanged;
        DeveloperMode.Changed -= OnDeveloperModeChanged;
        _bgBrush1.ImageSource = null;
        _bgBrush2.ImageSource = null;
    }

    // ============================================================= 背景轮播 =============================================================

    /// <summary>
    /// 背景轮播：按 ZSEditor_BackGround/BackGround.json 的顺序与每张的停留时长播放（settings 里的 random / loop /
    /// transitionDuration 都生效）；没有 JSON 或其中的图片都不存在时扫描目录；一张都加载不了时用深色渐变底。
    /// </summary>
    private void InitializeBackgroundSlideshow()
    {
        LoadSlides(ConfigManager.Instance.GetBackgroundPath());

        BitmapImage? firstImage = null;
        int first = -1;
        while (firstImage == null && ++first < _slides.Count)
            firstImage = LoadSlideImage(_slides[first]);
        if (firstImage == null)
        {
            _mainGrid.Background = new LinearGradientBrush(Color.FromRgb(30, 30, 35), Color.FromRgb(15, 15, 20), new Point(0, 0), new Point(1, 1));
            return;
        }

        _bgIndex = first;
        _useFirstBrush = true;
        _bgBrush1.ImageSource = firstImage;
        if (_slides.Count < 2) return;

        _bgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_slides[first].DurationMs) };
        _bgTimer.Tick += (_, _) => ShowNextSlide();
        _bgTimer.Start();
    }

    private void LoadSlides(string folder)
    {
        _slides = new List<BackgroundSlide>();
        if (!Directory.Exists(folder)) return;

        string jsonPath = IOPath.Combine(folder, "BackGround.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                ReadSlideConfig(doc.RootElement, folder);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                Debug.WriteLine($"[BeginScene] BackGround.json 解析失败，改为扫描目录: {ex.Message}");
                _slides.Clear();
            }
        }
        if (_slides.Count > 0) return;

        foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
        {
            var files = Directory.GetFiles(folder, "*" + ext);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            _slides.AddRange(files.Select(f => new BackgroundSlide(f, DefaultSlideDurationMs)));
        }
    }

    private void ReadSlideConfig(JsonElement root, string folder)
    {
        if (root.TryGetProperty("backgrounds", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                string? file = item.TryGetProperty("filename", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                if (string.IsNullOrWhiteSpace(file)) continue;
                string path = IOPath.Combine(folder, file);
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"[BeginScene] 背景图片不存在，跳过: {path}");
                    continue;
                }
                int duration = item.TryGetProperty("duration", out var d) && d.TryGetInt32(out int ms) ? ms : DefaultSlideDurationMs;
                _slides.Add(new BackgroundSlide(path, Math.Max(MinSlideDurationMs, duration)));
            }
        }

        if (!root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object) return;
        if (settings.TryGetProperty("loop", out var loop) && loop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _slideLoop = loop.GetBoolean();
        if (settings.TryGetProperty("transitionDuration", out var t) && t.TryGetInt32(out int transition) && transition > 0)
            _slideTransitionMs = transition;
        if (settings.TryGetProperty("random", out var random) && random.ValueKind == JsonValueKind.True)
            Shuffle(_slides);
    }

    private void ShowNextSlide()
    {
        if (_bgTimer == null) return;
        int next = _bgIndex + 1;
        if (next >= _slides.Count)
        {
            if (!_slideLoop)
            {
                _bgTimer.Stop();
                return;
            }
            next = 0;
        }

        _bgIndex = next;
        _bgTimer.Interval = TimeSpan.FromMilliseconds(_slides[next].DurationMs);
        var image = LoadSlideImage(_slides[next]);
        if (image == null) return;   // 读不出来的图直接跳过，下一跳接着轮播

        var (incoming, outgoing, brush) = _useFirstBrush ? (_bgBorder2, _bgBorder1, _bgBrush2) : (_bgBorder1, _bgBorder2, _bgBrush1);
        brush.ImageSource = image;
        CrossfadeBackgrounds(incoming, outgoing);
        _useFirstBrush = !_useFirstBrush;
    }

    /// <summary>交叉淡入淡出：新图同时从模糊（20）变清晰，与 VB 版一致；过渡结束后撤掉模糊效果。</summary>
    private void CrossfadeBackgrounds(Border incoming, Border outgoing)
    {
        var duration = TimeSpan.FromMilliseconds(_slideTransitionMs);
        var blur = new BlurEffect { Radius = 20 };
        incoming.Effect = blur;
        var unblur = new DoubleAnimation(20, 0, duration);
        unblur.Completed += (_, _) =>
        {
            if (incoming.Effect == blur) incoming.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
        incoming.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
        outgoing.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, duration));
    }

    private static BitmapImage? LoadSlideImage(BackgroundSlide slide)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(slide.Path, UriKind.Absolute);
            bitmap.DecodePixelWidth = 1920;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BeginScene] 背景图片读取失败: {slide.Path} {ex.Message}");
            return null;
        }
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ============================================================= 词云 =============================================================

    /// <summary>
    /// 标题栏词云：词条随机打乱后每 5 秒淡入淡出换一条，一轮播完重新打乱（避免同一条连播两次）；
    /// 没有词条时显示主标语 Main_Token。
    /// </summary>
    private void StartWordCloudRotation(TextBlock textBlock)
    {
        var tokens = ConfigManager.Instance.GetWordCloudConfig().Tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tokens.Count == 0)
        {
            SetWordCloudText(textBlock, ConfigManager.Instance.GetMainToken());
            return;
        }

        Shuffle(tokens);
        int index = 0;
        SetWordCloudText(textBlock, tokens[0]);
        if (tokens.Count == 1) return;

        _wordCloudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(WordCloudIntervalSeconds) };
        _wordCloudTimer.Tick += (_, _) =>
        {
            string previous = tokens[index];
            index++;
            if (index >= tokens.Count)
            {
                index = 0;
                Shuffle(tokens);
                if (tokens[0] == previous) (tokens[0], tokens[^1]) = (tokens[^1], tokens[0]);
            }

            string next = tokens[index];
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) =>
            {
                SetWordCloudText(textBlock, next);
                textBlock.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            };
            textBlock.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        _wordCloudTimer.Start();
    }

    /// <summary>词条太长时标题栏里会被截断，悬停可看全文</summary>
    private static void SetWordCloudText(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        textBlock.ToolTip = string.IsNullOrEmpty(text) ? null : text;
    }

    // ============================================================= 背景音乐 =============================================================

    /// <summary>音乐按钮的符号：正在播放显示 ❚❚（点了暂停），否则显示 ▶</summary>
    private static string MusicGlyph => MusicPlayerService.Instance.IsPlaying ? "❚❚" : "▶";

    /// <summary>右上角音乐按钮：左键播放 / 暂停（<see cref="MusicToggleButton_Click"/>），右键打开播放器窗口，符号随播放状态变化</summary>
    private void AttachMusicButton(Button button)
    {
        _musicButton = button;
        button.MouseRightButtonUp += (_, e) =>
        {
            MusicPlayerWindow.ShowFor(_window);
            e.Handled = true;
        };
    }

    private void OnMusicStateChanged(object? sender, EventArgs e)
    {
        if (_musicButton.Content is TextBlock glyph) glyph.Text = MusicGlyph;
    }

    // ============================================================= 设置 / 推荐 =============================================================

    private void ShowSettingsWindow() => new SettingsWindow { Owner = _window }.ShowDialog();

    private void ShowRecommendationWindow() => new RecommendationWindow { Owner = _window }.ShowDialog();

    // ============================================================= 开发者模式 =============================================================

    /// <summary>开发者按钮"图块场景"（控制台 system dev true 后出现在"检查数据"后面）</summary>
    private Button CreateDeveloperButtons()
    {
        _tileSceneButton = CreateNavButton(Loc.T("Begin.DevTileScene"), TileSceneButton_Click);
        _tileSceneButton.ToolTip = Loc.T("Begin.DevTileSceneTooltip");
        if (DeveloperMode.Enabled) InsertDeveloperButtons();
        return _tileSceneButton;
    }

    private void InsertDeveloperButtons()
    {
        int anchor = _navPanel.Children.IndexOf(_checkDataButton);
        _navPanel.Children.Insert(anchor >= 0 ? anchor + 1 : _navPanel.Children.Count, _tileSceneButton);
    }

    private void OnDeveloperModeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnDeveloperModeChanged);
            return;
        }

        bool shown = _navPanel.Children.Contains(_tileSceneButton);
        if (DeveloperMode.Enabled == shown) return;
        if (DeveloperMode.Enabled) InsertDeveloperButtons();
        else _navPanel.Children.Remove(_tileSceneButton);
        RecalculateNav();
    }

    /// <summary>
    /// 图块场景：用战术图编辑场景打开游戏资源里的战术图集（tacticalmap 图片 + tacticalmap.xml），
    /// 查看全部图块（VB 版开发者按钮"图块场景"的对应功能）。返回时回到开始界面。
    /// </summary>
    private void TileSceneButton_Click(object sender, RoutedEventArgs e)
    {
        var assets = Core.Assets.AssetManager.Default;
        var image = TacticalAtlasExtensions.Select(ext => assets.Find("tacticalmap" + ext)).FirstOrDefault(entry => entry != null);
        if (image == null)
        {
            MessageBox.Show(Loc.T("Begin.DevTileSceneMissing"), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _window.SetCurrentScene(new TacticalMapEditScene(_window, image.FullPath, assets.Find("tacticalmap.xml")?.FullPath));
    }
}
