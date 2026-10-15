namespace WC4MapEditor.Core.Services;

/// <summary>
/// 地图截图请求。
/// <para>
/// Core 只描述"要什么样的图"，真正的渲染在 GUI 层（场景基类）完成 —— 因为渲染器在
/// Rendering 程序集里，而依赖方向是 GUI → Core，Core 不能反过来引用渲染器。
/// 于是截图和识别地形、经纬度换算一样，走"Core 发请求 → GUI 回调实现"的路子。
/// </para>
/// <para>
/// 典型用途：让 AI 看一眼某个区域的地形/势力/城市分布。所以默认带坐标标注
/// （<see cref="ShowHexLabels"/>），AI 才能把图像里的位置和格子号对上。
/// </para>
/// </summary>
public sealed record MapScreenshotRequest
{
    /// <summary>区域中心列；为负数表示"整幅地图"</summary>
    public int CenterCol { get; init; } = -1;

    /// <summary>区域中心行；为负数表示"整幅地图"</summary>
    public int CenterRow { get; init; } = -1;

    /// <summary>区域宽（格）。≤0 时按整幅地图处理</summary>
    public int CellsW { get; init; }

    /// <summary>区域高（格）。≤0 时按整幅地图处理</summary>
    public int CellsH { get; init; }

    /// <summary>
    /// 长边像素上限（默认 1400）。渲染时按它自动定缩放：格子越少放得越大、越看得清；
    /// 同时避免动辄上万像素的图喂给多模态模型（图片越大越费 token 也越可能被拒）。
    /// </summary>
    public int MaxPixels { get; init; } = 1400;

    /// <summary>输出 PNG 路径；为空时由实现方放到 exe 同目录的 screenshots/ 下</summary>
    public string? OutputPath { get; init; }

    /// <summary>是否画坐标标注（默认开：AI 靠它定位）</summary>
    public bool ShowHexLabels { get; init; } = true;

    /// <summary>是否画六角网格线</summary>
    public bool ShowGridLines { get; init; }

    /// <summary>是否在区域中心画一个十字标记（便于"我说的就是这一格"）</summary>
    public bool MarkCenter { get; init; }

    /// <summary>
    /// true = 截"整个场景"：把当前窗口的界面渲染成图（工具栏、编辑面板、地图视口、状态栏都在内），
    /// 而不是离屏渲染一张地图。
    /// <para>
    /// 用途完全不同：<see cref="CenterCol"/> 那一套是"把地图数据画出来"，
    /// 这个是"把屏幕上看到的界面拍下来" —— 给 AI 看界面、或者留一张"当时界面长什么样"的凭证。
    /// 本模式下区域相关参数（CenterCol/CenterRow/CellsW/CellsH/ShowHexLabels/ShowGridLines）全部忽略，
    /// 只保留 <see cref="MaxPixels"/>（按长边等比缩小）。
    /// </para>
    /// </summary>
    public bool SceneMode { get; init; }
}

/// <summary>截图结果。失败时 <see cref="Message"/> 说明原因。</summary>
public sealed record MapScreenshotResult(bool Ok, string Path, int PixelWidth, int PixelHeight, string Message)
{
    public static MapScreenshotResult Fail(string message) => new(false, "", 0, 0, message);
}
