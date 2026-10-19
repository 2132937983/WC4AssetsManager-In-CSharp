using System.Diagnostics;
using System.IO;
using System.Windows.Controls.Primitives;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 背景音乐播放器窗口：当前曲目、上一首 / 播放暂停 / 停止 / 下一首、主音量、播放列表（双击播放）。
/// <para>
/// 非模态，全局只开一个；从开始界面音乐按钮右键或控制台 <c>music ui</c> 打开。
/// 播放状态全部来自 <see cref="MusicPlayerService"/>，控制台命令或开始界面按钮改了状态这里会同步刷新。
/// </para>
/// </summary>
public sealed class MusicPlayerWindow : Window
{
    private static MusicPlayerWindow? _open;

    /// <summary>打开播放器窗口（已打开则激活）。</summary>
    public static void ShowFor(Window? owner)
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }
        _open = new MusicPlayerWindow();
        if (owner != null && owner.IsLoaded) _open.Owner = owner;
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private readonly MusicPlayerService _music = MusicPlayerService.Instance;
    private readonly TextBlock _trackText = new();
    private readonly TextBlock _stateText = new();
    private readonly TextBlock _volumeText = new();
    private readonly TextBlock _playlistHeader = new();
    private readonly Slider _volumeSlider = new();
    private readonly ListBox _playlist = new();
    private Button _toggleButton = null!;
    private IReadOnlyList<ConfigManager.MusicTrack>? _shownTracks;
    private bool _syncing;

    private MusicPlayerWindow()
    {
        EditorTheme.Apply(this);
        Title = Loc.T("Assist.Music.Title");
        Width = 440;
        Height = 540;
        MinWidth = 380;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = EditorTheme.Bg1;

        Content = CreateUI();
        RefreshPlaylist();
        RefreshState();

        _music.StateChanged += OnMusicStateChanged;
        Closed += (_, _) =>
        {
            _music.StateChanged -= OnMusicStateChanged;
            _music.SaveVolume();
        };
        PreviewKeyDown += OnPreviewKeyDown;

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    // ============================================================= 界面 =============================================================

    private FrameworkElement CreateUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = CreateTitleBar();
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var nowPlaying = CreateNowPlaying();
        Grid.SetRow(nowPlaying, 1);
        root.Children.Add(nowPlaying);

        // 与 EditorTheme.SectionHeader 同样的分组标题条，标题文字随列表刷新
        _playlistHeader.FontSize = 12;
        _playlistHeader.FontWeight = FontWeights.SemiBold;
        _playlistHeader.Foreground = EditorTheme.Accent;
        var header = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _playlistHeader
        };
        Grid.SetRow(header, 2);
        root.Children.Add(header);

        _playlist.Margin = new Thickness(6);
        _playlist.MouseDoubleClick += (_, _) => PlaySelected();
        _playlist.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            PlaySelected();
            e.Handled = true;
        };
        Grid.SetRow(_playlist, 3);
        root.Children.Add(_playlist);

        var footer = CreateFooter();
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = root
        };
    }

    /// <summary>标题栏：按住可拖动窗口，右侧 ✕ 关闭。</summary>
    private Border CreateTitleBar()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = EditorTheme.Title(Loc.T("Assist.Music.Title"));
        label.Margin = new Thickness(12, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(label);

        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 32;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(0, 0, 6, 0);
        close.ToolTip = Loc.T("Common.Close");
        Grid.SetColumn(close, 1);
        panel.Children.Add(close);

        var bar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return bar;
    }

    /// <summary>当前曲目 + 控制按钮 + 音量</summary>
    private Border CreateNowPlaying()
    {
        var stack = new StackPanel();

        _trackText.FontSize = 15;
        _trackText.FontWeight = FontWeights.SemiBold;
        _trackText.Foreground = EditorTheme.Text;
        _trackText.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(_trackText);

        _stateText.FontSize = 12;
        _stateText.Foreground = EditorTheme.TextMuted;
        _stateText.Margin = new Thickness(0, 2, 0, 10);
        stack.Children.Add(_stateText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(ControlButton("⏮", Loc.T("Assist.Music.Previous"), (_, _) => _music.Previous()));
        _toggleButton = ControlButton("▶", Loc.T("Assist.Music.PlayPause"), (_, _) => _music.Toggle());
        _toggleButton.Style = EditorTheme.PrimaryButtonStyle;
        buttons.Children.Add(_toggleButton);
        buttons.Children.Add(ControlButton("■", Loc.T("Assist.Music.Stop"), (_, _) => _music.Stop()));
        buttons.Children.Add(ControlButton("⏭", Loc.T("Assist.Music.Next"), (_, _) => _music.Next()));
        stack.Children.Add(buttons);

        var volumeRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        var volumeLabel = EditorTheme.FieldLabel(Loc.T("Assist.Music.Volume"));
        volumeLabel.Margin = new Thickness(0, 0, 10, 0);
        volumeLabel.VerticalAlignment = VerticalAlignment.Center;
        volumeRow.Children.Add(volumeLabel);

        _volumeSlider.Minimum = 0;
        _volumeSlider.Maximum = 100;
        _volumeSlider.SmallChange = 1;
        _volumeSlider.LargeChange = 10;
        _volumeSlider.IsMoveToPointEnabled = true;
        _volumeSlider.VerticalAlignment = VerticalAlignment.Center;
        _volumeSlider.ValueChanged += (_, e) =>
        {
            _volumeText.Text = $"{(int)Math.Round(e.NewValue)}%";
            if (!_syncing) _music.Volume = e.NewValue / 100.0;
        };
        // 拖完 / 点完再写盘，拖动过程中不反复写 setting.txt
        _volumeSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _music.SaveVolume()));
        _volumeSlider.PreviewMouseLeftButtonUp += (_, _) => _music.SaveVolume();
        Grid.SetColumn(_volumeSlider, 1);
        volumeRow.Children.Add(_volumeSlider);

        _volumeText.Foreground = EditorTheme.TextMuted;
        _volumeText.VerticalAlignment = VerticalAlignment.Center;
        _volumeText.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_volumeText, 2);
        volumeRow.Children.Add(_volumeText);
        stack.Children.Add(volumeRow);

        return new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Child = stack
        };
    }

    private static Button ControlButton(string glyph, string tooltip, RoutedEventHandler onClick)
    {
        var b = EditorTheme.Button(glyph, onClick);
        b.Width = 52;
        b.Height = 34;
        b.FontSize = 15;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = tooltip;
        return b;
    }

    private Border CreateFooter()
    {
        var grid = new Grid { Margin = new Thickness(10, 6, 10, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mode = EditorTheme.Muted(Loc.F("Assist.Music.ModeInfo",
            _music.RandomOrder ? Loc.T("Assist.Music.RandomOrder") : Loc.T("Assist.Music.SequentialOrder"),
            _music.LoopPlaylist ? Loc.T("Assist.Music.LoopOn") : Loc.T("Assist.Music.LoopOff")));
        mode.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(mode);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var openFolder = EditorTheme.GhostButton(Loc.T("Assist.Music.OpenFolder"), (_, _) => OpenMusicFolder());
        openFolder.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(openFolder);
        buttons.Children.Add(EditorTheme.Button(Loc.T("Common.Close"), (_, _) => Close()));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid
        };
    }

    // ============================================================= 同步 =============================================================

    private void OnMusicStateChanged(object? sender, EventArgs e)
    {
        // 配置热重载会换一份新的播放列表
        if (!ReferenceEquals(_shownTracks, _music.Tracks)) RefreshPlaylist();
        RefreshState();
    }

    private void RefreshPlaylist()
    {
        _playlist.Items.Clear();
        var tracks = _music.Tracks;
        _shownTracks = tracks;
        for (int i = 0; i < tracks.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {IOPath.GetFileNameWithoutExtension(tracks[i].Filename)}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (tracks[i].Loop)
                row.Children.Add(EditorTheme.Badge(Loc.T("Assist.Music.LoopBadge")));
            if (!File.Exists(IOPath.Combine(_music.MusicFolder, tracks[i].Filename)))
                row.Children.Add(EditorTheme.Badge(Loc.T("Assist.Music.MissingBadge"), EditorTheme.Danger));
            _playlist.Items.Add(new ListBoxItem { Content = row, ToolTip = tracks[i].Filename });
        }
        _playlistHeader.Text = Loc.F("Assist.Music.PlaylistHeader", tracks.Count);
        if (tracks.Count == 0)
            _playlist.Items.Add(new ListBoxItem { Content = Loc.T("Assist.Music.EmptyPlaylist"), IsEnabled = false });
    }

    private void RefreshState()
    {
        _syncing = true;
        try
        {
            bool hasTrack = _music.CurrentIndex >= 0;
            _trackText.Text = hasTrack ? _music.CurrentTrackName : Loc.T("Assist.Music.NoTrack");
            string state = _music.State switch
            {
                MusicPlayerService.PlaybackState.Playing => Loc.T("Assist.Music.StatePlaying"),
                MusicPlayerService.PlaybackState.Paused => Loc.T("Assist.Music.StatePaused"),
                _ => Loc.T("Assist.Music.StateStopped")
            };
            _stateText.Text = hasTrack ? Loc.F("Assist.Music.StateWithPosition", state, _music.CurrentIndex + 1, _music.Tracks.Count) : state;
            _toggleButton.Content = _music.IsPlaying ? "❚❚" : "▶";
            _volumeSlider.Value = Math.Round(_music.Volume * 100);
            _volumeText.Text = $"{(int)Math.Round(_volumeSlider.Value)}%";
            if (_music.Tracks.Count == _playlist.Items.Count)
                _playlist.SelectedIndex = _music.CurrentIndex;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ============================================================= 操作 =============================================================

    private void PlaySelected()
    {
        if (_playlist.SelectedIndex >= 0 && _playlist.SelectedIndex < _music.Tracks.Count)
            _music.PlayTrack(_playlist.SelectedIndex);
    }

    private void OpenMusicFolder()
    {
        if (!Directory.Exists(_music.MusicFolder)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_music.MusicFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicPlayerWindow] 打开音乐文件夹失败: {ex.Message}");
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && e.OriginalSource is not TextBox)
        {
            _music.Toggle();
            e.Handled = true;
        }
    }
}
