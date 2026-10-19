using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WC4MapEditor.GUI.Services;

/// <summary>
/// 全局背景音乐播放器（对齐 VB 版 Assist.MusicPlayer）：WPF MediaPlayer + DispatcherTimer 淡入淡出，切换场景不中断。
/// <para>
/// 播放列表来自 Resource/Music/WindowMusic.json（<see cref="ConfigManager.GetMusicConfig"/>）；
/// 开关与主音量记在 setting.txt [全局编辑] 的 <see cref="EnabledSettingKey"/> / <see cref="VolumeSettingKey"/>，
/// 下次启动按开关自动播放。
/// </para>
/// <para>
/// 与 VB 版的差异（有意修正）：停止不再在 UI 线程上忙等淡出；文件缺失时每首最多试一次，
/// 全部缺失就停下（VB 会无限递归）；列表循环关闭时播完最后一首即停止。
/// </para>
/// 只能在 UI 线程使用（MediaPlayer 依附于创建它的 Dispatcher）。
/// </summary>
public sealed class MusicPlayerService
{
    /// <summary>setting.txt 里的背景音乐开关（true = 启动时自动播放）</summary>
    public const string EnabledSettingKey = "BackgroundMusic";

    /// <summary>setting.txt 里的主音量（0-100）</summary>
    public const string VolumeSettingKey = "MusicVolume";

    private const string SettingSection = "全局编辑";

    /// <summary>暂停 / 继续 / 停止的淡入淡出时长上限（毫秒）：按下就要有反应，不用切歌那么长的过渡</summary>
    private const int ToggleFadeMs = 400;

    /// <summary>"上一首"回溯记录的最大长度</summary>
    private const int MaxHistory = 50;

    private static MusicPlayerService? _instance;

    /// <summary>全局唯一实例（首次访问须在 UI 线程）</summary>
    public static MusicPlayerService Instance => _instance ??= new MusicPlayerService();

    public enum PlaybackState { Stopped, Playing, Paused }

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _fadeTimer;
    private readonly Random _random = new();
    private readonly List<int> _history = new();
    private ConfigManager.MusicConfig _config = new();
    private string _folder = "";
    private int _index = -1;
    private double _masterVolume;

    /// <summary>主音量改过、还没写进 setting.txt（只开关播放器窗口不会写盘）</summary>
    private bool _volumeUnsaved;
    private int _consecutiveFailures;

    // 淡入淡出：_gain 是当前增益（0~1），从 _fadeFrom 过渡到 _fadeTo，结束后执行 _afterFade
    private double _gain;
    private double _fadeFrom;
    private double _fadeTo;
    private double _fadeMs;
    private DateTime _fadeStart;
    private Action? _afterFade;

    /// <summary>播放状态、曲目或音量变化后触发（UI 线程）</summary>
    public event EventHandler? StateChanged;

    private MusicPlayerService()
    {
        _player.MediaEnded += (_, _) => OnTrackEnded();
        _player.MediaFailed += (_, e) => OnTrackFailed(e.ErrorException);
        _player.MediaOpened += (_, _) => _consecutiveFailures = 0;
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _fadeTimer.Tick += (_, _) => OnFadeTick();
        LoadConfig();
        _masterVolume = ReadSavedVolume() ?? Clamp01(_config.Settings.DefaultVolume);
    }

    // ============================================================= 状态 =============================================================

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public bool IsPlaying => State == PlaybackState.Playing;

    public bool IsPaused => State == PlaybackState.Paused;

    public IReadOnlyList<ConfigManager.MusicTrack> Tracks => _config.Tracks;

    /// <summary>当前曲目在列表中的序号；没有选中曲目时为 -1</summary>
    public int CurrentIndex => _index;

    /// <summary>当前曲目名（不含扩展名）；没有曲目时为空串</summary>
    public string CurrentTrackName => Current is { } t ? IOPath.GetFileNameWithoutExtension(t.Filename) : "";

    public bool RandomOrder => _config.Settings.Random;

    public bool LoopPlaylist => _config.Settings.Loop;

    /// <summary>音乐文件夹（Resource/Music）</summary>
    public string MusicFolder => _folder;

    /// <summary>主音量 0~1（与每首曲目自己的音量相乘）。改完需要持久化时调用 <see cref="SaveVolume"/>。</summary>
    public double Volume
    {
        get => _masterVolume;
        set
        {
            double v = Clamp01(value);
            if (Math.Abs(v - _masterVolume) < 0.0001) return;
            _masterVolume = v;
            _volumeUnsaved = true;
            ApplyVolume();
            RaiseStateChanged();
        }
    }

    private ConfigManager.MusicTrack? Current => _index >= 0 && _index < _config.Tracks.Count ? _config.Tracks[_index] : null;

    // ============================================================= 用户操作 =============================================================

    /// <summary>启动时调用：setting.txt 里背景音乐是打开的就开始播放（不改写设置）。</summary>
    public void RestoreFromSettings()
    {
        if (ReadSavedEnabled()) StartOrResume();
    }

    /// <summary>播放 / 继续；并记住"背景音乐开"。没有可播放的曲目时返回 false。</summary>
    public bool Play()
    {
        if (!StartOrResume()) return false;
        SaveEnabled(true);
        return true;
    }

    /// <summary>暂停（淡出后暂停）；并记住"背景音乐关"。</summary>
    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        PauseCore();
        SaveEnabled(false);
    }

    /// <summary>停止（淡出后停止，不阻塞界面）；并记住"背景音乐关"。</summary>
    public void Stop()
    {
        if (State == PlaybackState.Stopped) return;
        State = PlaybackState.Stopped;
        FadeTo(0, ToggleFade, () =>
        {
            _player.Stop();
            _player.Close();
        });
        SaveEnabled(false);
        RaiseStateChanged();
    }

    /// <summary>播放中则暂停，否则播放（开始界面的音乐按钮）。</summary>
    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>下一首（随机模式下随机挑一首）。返回是否有曲目可播。</summary>
    public bool Next()
    {
        int next = NextIndex(userRequested: true);
        if (next < 0) return false;
        Remember(_index);
        SwitchTo(next);
        return true;
    }

    /// <summary>上一首（随机模式下回到上一次播放的那首）。返回是否有曲目可播。</summary>
    public bool Previous()
    {
        int count = _config.Tracks.Count;
        if (count == 0) return false;
        int prev;
        if (_config.Settings.Random && _history.Count > 0)
        {
            prev = _history[^1];
            _history.RemoveAt(_history.Count - 1);
        }
        else
        {
            prev = _index < 0 ? count - 1 : (_index - 1 + count) % count;
        }
        SwitchTo(prev);
        return true;
    }

    /// <summary>播放列表中的指定曲目。</summary>
    public bool PlayTrack(int index)
    {
        if (index < 0 || index >= _config.Tracks.Count) return false;
        if (index != _index) Remember(_index);
        SwitchTo(index);
        return true;
    }

    /// <summary>主音量改过时写进 setting.txt（拖完音量条 / 控制台 vol 命令 / 关闭播放器窗口时调用；没改过不写盘）。</summary>
    public void SaveVolume()
    {
        if (!_volumeUnsaved) return;
        _volumeUnsaved = false;
        SaveSetting(VolumeSettingKey, ((int)Math.Round(_masterVolume * 100)).ToString(CultureInfo.InvariantCulture),
                    "背景音乐主音量（0-100）");
    }

    /// <summary>
    /// 配置热重载后调用：换用新的播放列表（正在播的曲目不打断），并按 setting.txt 里的开关与音量同步播放状态。
    /// </summary>
    public void ReloadConfig()
    {
        string? playing = Current?.Filename;
        LoadConfig();
        _index = playing == null ? -1 : _config.Tracks.FindIndex(t => t.Filename == playing);
        _history.Clear();

        if (ReadSavedVolume() is { } volume)
        {
            _masterVolume = volume;
            _volumeUnsaved = false;
        }
        ApplyVolume();

        bool enabled = ReadSavedEnabled();
        if (enabled && State != PlaybackState.Playing) StartOrResume();
        else if (!enabled && State == PlaybackState.Playing) PauseCore();
        RaiseStateChanged();
    }

    // ============================================================= 播放控制 =============================================================

    private bool StartOrResume()
    {
        if (State == PlaybackState.Playing) return true;
        if (State == PlaybackState.Paused)
        {
            State = PlaybackState.Playing;
            _player.Play();
            FadeTo(1, ToggleFade);
            RaiseStateChanged();
            return true;
        }
        if (_config.Tracks.Count == 0) return false;
        int first = _config.Settings.Random ? _random.Next(_config.Tracks.Count) : 0;
        return StartTrack(first);
    }

    private void PauseCore()
    {
        State = PlaybackState.Paused;
        FadeTo(0, ToggleFade, () => _player.Pause());
        RaiseStateChanged();
    }

    /// <summary>
    /// 切到指定曲目（切歌总会进入播放状态，所以同时记住"背景音乐开"）：
    /// 正在出声时先淡出当前曲目再切（过渡总长 = crossfadeDuration），否则直接切。
    /// </summary>
    private void SwitchTo(int index)
    {
        if (State == PlaybackState.Playing && _gain > 0.01 && TrackFade > 0)
            FadeTo(0, TrackFade / 2.0, () => StartTrack(index));
        else
            StartTrack(index);
        if (State != PlaybackState.Stopped) SaveEnabled(true);
    }

    /// <summary>
    /// 从 <paramref name="index"/> 起找第一首文件存在的曲目播放；每首最多试一次，全部缺失时停止并返回 false。
    /// </summary>
    private bool StartTrack(int index)
    {
        int count = _config.Tracks.Count;
        for (int tried = 0; tried < count; tried++)
        {
            int i = ((index + tried) % count + count) % count;
            string path = IOPath.Combine(_folder, _config.Tracks[i].Filename);
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[MusicPlayer] 音乐文件不存在，跳过: {path}");
                continue;
            }

            _index = i;
            _player.Open(new Uri(path, UriKind.Absolute));
            _gain = 0;
            ApplyVolume();
            _player.Play();
            State = PlaybackState.Playing;
            FadeTo(1, TrackFade / 2.0);
            RaiseStateChanged();
            Debug.WriteLine($"[MusicPlayer] 正在播放: {_config.Tracks[i].Filename}");
            return true;
        }

        Debug.WriteLine("[MusicPlayer] 播放列表里没有可播放的文件");
        StopNow();
        return false;
    }

    private void OnTrackEnded()
    {
        if (State != PlaybackState.Playing) return;
        int next = NextIndex(userRequested: false);
        if (next < 0)
        {
            // 列表循环关闭、已播完最后一首：停下但不改写开关（下次启动照常播放）
            StopNow();
            return;
        }
        Remember(_index);
        StartTrack(next);
    }

    private void OnTrackFailed(Exception? error)
    {
        Debug.WriteLine($"[MusicPlayer] 播放失败: {Current?.Filename} {error?.Message}");
        _consecutiveFailures++;
        if (_consecutiveFailures >= Math.Max(1, _config.Tracks.Count))
        {
            StopNow();
            return;
        }
        StartTrack(_index + 1);
    }

    /// <summary>
    /// 自动接下一首（<paramref name="userRequested"/> = false）时遵守列表循环 / 单曲循环；
    /// 用户点"下一首"时总是能往下走（到尾部回到开头）。没有可播曲目返回 -1。
    /// </summary>
    private int NextIndex(bool userRequested)
    {
        int count = _config.Tracks.Count;
        if (count == 0) return -1;
        if (count == 1)
            return userRequested || _config.Tracks[0].Loop || _config.Settings.Loop ? 0 : -1;
        if (_config.Settings.Random)
        {
            int next;
            do next = _random.Next(count); while (next == _index);
            return next;
        }
        int n = _index + 1;
        if (n < count) return n;
        return userRequested || _config.Settings.Loop ? 0 : -1;
    }

    private void Remember(int index)
    {
        if (index < 0) return;
        _history.Add(index);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
    }

    private void StopNow()
    {
        _afterFade = null;
        _fadeTimer.Stop();
        _player.Stop();
        _player.Close();
        _gain = 0;
        State = PlaybackState.Stopped;
        RaiseStateChanged();
    }

    // ============================================================= 淡入淡出 =============================================================

    /// <summary>切歌的过渡时长（WindowMusic.json 的 crossfadeDuration，限制在 0~10 秒）</summary>
    private int TrackFade => Math.Clamp(_config.Settings.CrossfadeDuration, 0, 10000);

    private double ToggleFade => Math.Min(TrackFade, ToggleFadeMs);

    private void FadeTo(double target, double ms, Action? after = null)
    {
        _afterFade = after;
        _fadeFrom = _gain;
        _fadeTo = target;
        _fadeMs = ms;
        _fadeStart = DateTime.UtcNow;
        if (ms <= 0 || Math.Abs(_fadeFrom - _fadeTo) < 0.001)
        {
            FinishFade();
            return;
        }
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        double t = Math.Min(1.0, (DateTime.UtcNow - _fadeStart).TotalMilliseconds / _fadeMs);
        // EaseInOutQuad（与 VB 版一致），起止更柔和
        double eased = t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;
        _gain = _fadeFrom + (_fadeTo - _fadeFrom) * eased;
        ApplyVolume();
        if (t >= 1.0) FinishFade();
    }

    private void FinishFade()
    {
        _fadeTimer.Stop();
        _gain = _fadeTo;
        ApplyVolume();
        var after = _afterFade;
        _afterFade = null;
        after?.Invoke();
    }

    private void ApplyVolume()
        => _player.Volume = Clamp01(Clamp01(Current?.Volume ?? 1.0) * _masterVolume * _gain);

    // ============================================================= 配置与持久化 =============================================================

    private void LoadConfig()
    {
        _config = ConfigManager.Instance.GetMusicConfig();
        _folder = ConfigManager.Instance.GetMusicPath();
    }

    private static bool ReadSavedEnabled()
    {
        string v = ConfigManager.Instance.GetText(EnabledSettingKey, "").Trim();
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"
               || v.Equals("on", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static double? ReadSavedVolume()
        => int.TryParse(ConfigManager.Instance.GetText(VolumeSettingKey, "").Trim(), out int v)
            ? Math.Clamp(v, 0, 100) / 100.0
            : null;

    private static void SaveEnabled(bool enabled)
        => SaveSetting(EnabledSettingKey, enabled ? "true" : "false",
                       "背景音乐开关（true = 启动时自动播放；开始界面的音乐按钮会同步修改此项）");

    private static void SaveSetting(string key, string value, string comment)
    {
        try
        {
            var doc = SettingTxtDocument.Load(ConfigManager.SettingFilePath);
            if (doc.SetOrAdd(SettingSection, key, value, comment)) doc.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicPlayer] 写入 setting.txt 的 {key} 失败: {ex.Message}");
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);
}
