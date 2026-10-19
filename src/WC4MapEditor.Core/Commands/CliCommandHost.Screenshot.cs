using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Screenshot 命令组（从原单文件按命令组拆出，内容未改）。
public sealed partial class CliCommandHost
{
    /// <summary>
    /// screenshot：把地图渲染成 PNG。
    /// 给中心点与宽高就是局部放大图（区域越小格子在图上越大），AI 视觉就靠这个看细节。
    /// </summary>
    private Command BuildScreenshotCommand()
    {
        var cmd = new Command("screenshot", "地图截图存成 PNG（整幅或局部区域，供 AI 视觉 / 人工核对）");

        var pathArg = new Argument<string?>("path")
        {
            Description = "输出 PNG 路径；省略则放到程序目录 screenshots\\ 下自动命名",
            Arity = ArgumentArity.ZeroOrOne
        };
        var colOpt = new Option<int>("--col", "-c") { Description = "区域中心列（省略=整幅地图）", DefaultValueFactory = _ => -1 };
        var rowOpt = new Option<int>("--row", "-r") { Description = "区域中心行（省略=整幅地图）", DefaultValueFactory = _ => -1 };
        var wOpt = new Option<int>("--w") { Description = "区域宽（格）", DefaultValueFactory = _ => 0 };
        var hOpt = new Option<int>("--h") { Description = "区域高（格）", DefaultValueFactory = _ => 0 };
        var pxOpt = new Option<int>("--pixels", "-p") { Description = "长边像素上限（默认 1400；越小越省视觉 token）", DefaultValueFactory = _ => 1400 };
        var noLabelOpt = new Option<bool>("--no-labels") { Description = "不画坐标标注（默认画，AI 靠它把图像和格子对上）" };
        var gridOpt = new Option<bool>("--grid") { Description = "画六角网格线" };
        var sceneOpt = new Option<bool>("--scene") { Description = "截整个场景（当前窗口界面：工具栏/面板/地图视口/状态栏），而不是离屏渲染地图" };

        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(colOpt);
        cmd.Options.Add(rowOpt);
        cmd.Options.Add(wOpt);
        cmd.Options.Add(hOpt);
        cmd.Options.Add(pxOpt);
        cmd.Options.Add(noLabelOpt);
        cmd.Options.Add(gridOpt);
        cmd.Options.Add(sceneOpt);

        cmd.SetAction(parseResult =>
        {
            // --scene 截的是窗口界面，跟地图数据无关 → 不走 WithModifiers（不要求已加载地图）。
            // 想少打字的话，同一个功能还有个专用命令：screenshotscene。
            if (parseResult.GetValue(sceneOpt))
            {
                CaptureSceneScreenshot(parseResult.GetValue(pathArg), parseResult.GetValue(pxOpt));
                return;
            }

            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(colOpt);
                int row = parseResult.GetValue(rowOpt);

                var result = mgr.CaptureScreenshot(new Services.MapScreenshotRequest
                {
                    CenterCol = col,
                    CenterRow = row,
                    CellsW = parseResult.GetValue(wOpt),
                    CellsH = parseResult.GetValue(hOpt),
                    MaxPixels = parseResult.GetValue(pxOpt),
                    OutputPath = parseResult.GetValue(pathArg),
                    ShowHexLabels = !parseResult.GetValue(noLabelOpt),
                    ShowGridLines = parseResult.GetValue(gridOpt),
                    MarkCenter = col >= 0 && row >= 0,
                });

                if (!result.Ok)
                {
                    _output.WriteLine(result.Message);
                    return;
                }

                _output.WriteLine($"截图已保存: {result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）");
                _output.WriteLine(result.Message);
            }, readOnly: true);
        });

        return cmd;
    }

    /// <summary>
    /// 场景（窗口界面）截图的共用实现：<c>screenshotscene</c> 和 <c>screenshot --scene</c> 都走它。
    /// <para>
    /// 关键点：<b>不经过 <see cref="WithModifiers"/></b>。它要的是"屏幕上现在是什么样"，
    /// 跟地图数据无关，所以没加载地图、甚至在军团 / 国家 / 建筑这类场景里也能跑 ——
    /// 这正是它和 <c>screenshot</c> 的区别。
    /// </para>
    /// </summary>
    private void CaptureSceneScreenshot(string? path, int maxPixels)
    {
        var result = EditModeManager.Instance.CaptureScreenshot(new Services.MapScreenshotRequest
        {
            SceneMode = true,
            MaxPixels = maxPixels,
            OutputPath = path,
        });

        if (!result.Ok)
        {
            _output.WriteLine(result.Message);
            return;
        }

        _output.WriteLine($"场景截图已保存: {result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）");
        _output.WriteLine(result.Message);
    }

    /// <summary>
    /// screenshotscene：截"整个场景"，即当前编辑器窗口的界面（工具栏、编辑面板、地图视口、状态栏）。
    /// <para>
    /// 与 <c>screenshot</c> 的分工：那个渲染的是<b>地图数据</b>（必须先加载地图，且得在地图场景里）；
    /// 这个截的是<b>窗口界面</b>，任何场景、哪怕没打开地图都能用。
    /// </para>
    /// <para>
    /// 局限：主窗口最小化时会失败（会给出提示）；独立弹出的窗口（AI 对话窗、右键菜单）不属于
    /// 主窗口视觉树，不会出现在图里。无界面启动（纯命令行）时没有主窗口，也会明确报"未注册"。
    /// </para>
    /// </summary>
    private Command BuildScreenshotSceneCommand()
    {
        var cmd = new Command("screenshotscene", "截整个场景（编辑器窗口界面）存成 PNG；不要求已加载地图");

        var pathArg = new Argument<string?>("path")
        {
            Description = "输出 PNG 路径；省略则放到程序目录 screenshots\\ 下自动命名",
            Arity = ArgumentArity.ZeroOrOne
        };
        var pxOpt = new Option<int>("--pixels", "-p")
        {
            Description = "长边像素上限（默认 1400；越小越省视觉 token）",
            DefaultValueFactory = _ => 1400
        };

        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(pxOpt);

        cmd.SetAction(parseResult =>
            CaptureSceneScreenshot(parseResult.GetValue(pathArg), parseResult.GetValue(pxOpt)));

        return cmd;
    }
}
