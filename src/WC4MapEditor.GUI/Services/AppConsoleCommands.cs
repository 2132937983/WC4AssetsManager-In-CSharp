using System.IO;
using WC4MapEditor.GUI.Global;
using WC4MapEditor.GUI.Views.Assist;
using CoreCommandManager = WC4MapEditor.Core.Commands.CommandManager;

namespace WC4MapEditor.GUI.Services;

/// <summary>
/// 与场景无关的控制台命令：<c>music</c>（背景音乐，对齐 VB 版 MusicCommand）与 <c>system dev</c>（开发者模式，
/// 对齐 VB 版 SystemCommand）。主窗口创建时注册一次，开始界面、编辑场景、渲染场景里都能用；
/// 渲染场景自己的命令仍由 RenderSceneBase 注册。
/// </summary>
internal static class AppConsoleCommands
{
    public static void Register(CoreCommandManager commands, DebugConsole console)
    {
        commands.RegisterCommand("music", args => Music(args, console), "背景音乐播放器控制",
            "<play|pause|stop|next|prev|status|list|vol <0-100>|ui>");
        commands.RegisterCommand("system", args => SystemCommand(args, console), "系统控制（开发者模式）",
            "dev <true|false>");
    }

    // ============================================================= music =============================================================

    private static void Music(string[] args, DebugConsole console)
    {
        var music = MusicPlayerService.Instance;
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (action)
        {
            case "play":
            case "p":
                if (music.IsPlaying)
                    console.WriteLine(Loc.F("Console.Music.AlreadyPlaying", music.CurrentTrackName));
                else if (music.IsPaused)
                    console.WriteLine(music.Play() ? Loc.T("Console.Music.Resumed") : Loc.T("Console.Music.NoPlayableTrack"));
                else
                    console.WriteLine(music.Play() ? Loc.F("Console.Music.Started", music.CurrentTrackName) : Loc.T("Console.Music.NoPlayableTrack"));
                break;

            case "pause":
            case "pa":
                if (!music.IsPlaying)
                {
                    console.WriteLine(Loc.T("Console.Music.NothingPlaying"));
                    break;
                }
                music.Pause();
                console.WriteLine(Loc.T("Console.Music.Paused"));
                break;

            case "stop":
            case "s":
                if (music.State == MusicPlayerService.PlaybackState.Stopped)
                {
                    console.WriteLine(Loc.T("Console.Music.NothingPlaying"));
                    break;
                }
                music.Stop();
                console.WriteLine(Loc.T("Console.Music.Stopped"));
                break;

            case "next":
            case "n":
                console.WriteLine(music.Next() ? Loc.T("Console.Music.Next") : Loc.T("Console.Music.EmptyPlaylist"));
                break;

            case "prev":
            case "previous":
            case "pr":
                console.WriteLine(music.Previous() ? Loc.T("Console.Music.Previous") : Loc.T("Console.Music.EmptyPlaylist"));
                break;

            case "status":
            case "st":
                WriteStatus(music, console);
                break;

            case "list":
            case "ls":
            case "l":
                WriteList(music, console);
                break;

            case "vol":
            case "volume":
            case "v":
                SetVolume(music, args, console);
                break;

            case "ui":
                MusicPlayerWindow.ShowFor(Application.Current?.MainWindow);
                break;

            default:
                if (action.Length > 0) console.WriteLine(Loc.F("Console.Music.UnknownAction", action));
                WriteMusicUsage(console);
                break;
        }
    }

    private static void WriteStatus(MusicPlayerService music, DebugConsole console)
    {
        string state = music.State switch
        {
            MusicPlayerService.PlaybackState.Playing => Loc.T("Console.Music.StatePlaying"),
            MusicPlayerService.PlaybackState.Paused => Loc.T("Console.Music.StatePaused"),
            _ => Loc.T("Console.Music.StateStopped")
        };
        console.WriteLine(Loc.F("Console.Music.Status", state));
        if (music.CurrentIndex >= 0)
            console.WriteLine(Loc.F("Console.Music.CurrentTrack", music.CurrentTrackName, music.CurrentIndex + 1, music.Tracks.Count));
        console.WriteLine(Loc.F("Console.Music.VolumeLine", (int)Math.Round(music.Volume * 100)));
        console.WriteLine(Loc.F("Console.Music.ModeLine",
            music.RandomOrder ? Loc.T("Assist.Music.RandomOrder") : Loc.T("Assist.Music.SequentialOrder"),
            music.LoopPlaylist ? Loc.T("Common.Yes") : Loc.T("Common.No")));
    }

    private static void WriteList(MusicPlayerService music, DebugConsole console)
    {
        var tracks = music.Tracks;
        if (tracks.Count == 0)
        {
            console.WriteLine(Loc.T("Console.Music.EmptyPlaylist"));
            return;
        }
        console.WriteLine(Loc.F("Console.Music.ListHeader", tracks.Count));
        console.WriteLine(new string('-', 40));
        for (int i = 0; i < tracks.Count; i++)
        {
            string prefix = i == music.CurrentIndex ? "▶ " : "  ";
            string loop = tracks[i].Loop ? Loc.T("Console.Music.LoopMark") : "";
            string missing = File.Exists(IOPath.Combine(music.MusicFolder, tracks[i].Filename)) ? "" : Loc.T("Console.Music.MissingMark");
            console.WriteLine($"{prefix}{i + 1}. {IOPath.GetFileNameWithoutExtension(tracks[i].Filename)}{loop}{missing}");
        }
    }

    private static void SetVolume(MusicPlayerService music, string[] args, DebugConsole console)
    {
        if (args.Length < 2)
        {
            console.WriteLine(Loc.F("Console.Music.VolumeLine", (int)Math.Round(music.Volume * 100)));
            console.WriteLine(Loc.T("Console.Music.VolumeUsage"));
            return;
        }
        if (!int.TryParse(args[1], out int volume) || volume < 0 || volume > 100)
        {
            console.WriteLine(Loc.T("Console.Music.VolumeRange"));
            return;
        }
        music.Volume = volume / 100.0;
        music.SaveVolume();
        console.WriteLine(Loc.F("Console.Music.VolumeSet", volume));
    }

    private static void WriteMusicUsage(DebugConsole console)
    {
        console.WriteLine(Loc.T("Console.Music.Usage"));
        console.WriteLine(Loc.T("Console.Music.UsagePlay"));
        console.WriteLine(Loc.T("Console.Music.UsagePause"));
        console.WriteLine(Loc.T("Console.Music.UsageStop"));
        console.WriteLine(Loc.T("Console.Music.UsageNext"));
        console.WriteLine(Loc.T("Console.Music.UsagePrev"));
        console.WriteLine(Loc.T("Console.Music.UsageStatus"));
        console.WriteLine(Loc.T("Console.Music.UsageList"));
        console.WriteLine(Loc.T("Console.Music.UsageVolume"));
        console.WriteLine(Loc.T("Console.Music.UsageUi"));
    }

    // ============================================================= system =============================================================

    private static void SystemCommand(string[] args, DebugConsole console)
    {
        string option = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (option != "dev")
        {
            if (option.Length > 0) console.WriteLine(Loc.F("Console.System.UnknownOption", option));
            console.WriteLine(Loc.T("Console.System.Usage"));
            console.WriteLine(Loc.T("Console.System.UsageDev"));
            return;
        }

        if (args.Length < 2)
        {
            console.WriteLine(Loc.F("Console.System.DevState", DeveloperMode.Enabled ? Loc.T("Common.Yes") : Loc.T("Common.No")));
            console.WriteLine(Loc.T("Console.System.UsageDev"));
            return;
        }

        bool? enable = args[1].ToLowerInvariant() switch
        {
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => null
        };
        if (enable == null)
        {
            console.WriteLine(Loc.F("Console.System.InvalidValue", args[1]));
            return;
        }

        DeveloperMode.Enabled = enable.Value;
        if (enable.Value)
        {
            console.WriteLine(Loc.T("Console.System.DevEntered"));
            console.WriteLine(Loc.T("Console.System.DevButtonsShown"));
        }
        else
        {
            console.WriteLine(Loc.T("Console.System.DevExited"));
            console.WriteLine(Loc.T("Console.System.DevButtonsHidden"));
        }
    }
}
