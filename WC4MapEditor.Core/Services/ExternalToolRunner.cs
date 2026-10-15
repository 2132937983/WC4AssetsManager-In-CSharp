using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// Core/Lib 目录下打包外部工具（OCR 识别、zme 建筑脚本生成）的调用封装。
/// 工具随程序输出到 bin/Lib，无需用户额外安装 Python。
/// </summary>
public static class ExternalToolRunner
{
    public const string LibFolderName = "Lib";
    public const string OcrExeName = "ocr_map.exe";
    public const string GenBuildingsExeName = "gen_buildings_zme.exe";

    /// <summary>
    /// 定位 Lib 目录：优先程序输出目录，其次从执行目录向上查找源码目录中的 Lib。
    /// </summary>
    public static string? ResolveLibDirectory()
    {
        var baseDir = AppContext.BaseDirectory;

        var direct = Path.Combine(baseDir, LibFolderName);
        if (Directory.Exists(direct)) return direct;

        var current = baseDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(current); i++)
        {
            var inCore = Path.Combine(current, "WC4MapEditor.Core", LibFolderName);
            if (Directory.Exists(inCore)) return inCore;

            var nearby = Path.Combine(current, LibFolderName);
            if (File.Exists(Path.Combine(nearby, OcrExeName))) return nearby;

            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    /// <summary>获取 Lib 下指定工具的完整路径；不存在时返回 null。</summary>
    public static string? ResolveToolPath(string exeName)
    {
        var lib = ResolveLibDirectory();
        if (lib == null) return null;

        var path = Path.Combine(lib, exeName);
        return File.Exists(path) ? path : null;
    }

    public static bool IsOcrAvailable => ResolveToolPath(OcrExeName) != null;
    public static bool IsGenBuildingsAvailable => ResolveToolPath(GenBuildingsExeName) != null;

    /// <summary>运行外部工具并捕获输出。</summary>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string exePath, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 打包的 Python 工具在 stdout 被重定向为管道时，会退回系统区域编码（中文 Windows 为 GBK），
        // 一旦打印进度条等非 GBK 字符（如 ░ U+2591）就会抛 UnicodeEncodeError 并崩溃。
        // 这里强制其按 UTF-8 输出，与上面的 StandardOutputEncoding 保持一致。
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    /// <summary>
    /// OCR 识别地图图片，输出地名 JSON（name / position.x / position.y）。
    /// 默认使用内置的 v4 Mobile 模型，避免首次运行联网下载模型。
    /// </summary>
    public static async Task<(bool Ok, string Message, string OutputPath)> RunOcrAsync(
        string imagePath, string outputJsonPath, string model = "v4m",
        string language = "ch", CancellationToken ct = default)
    {
        var exe = ResolveToolPath(OcrExeName);
        if (exe == null)
            return (false, $"未找到 {OcrExeName}（应位于 Lib 目录）", outputJsonPath);

        if (!File.Exists(imagePath))
            return (false, $"图片不存在: {imagePath}", outputJsonPath);

        // --quiet：关闭进度条等控制台输出。
        // 打包后的 Python 工具在 stdout 被重定向为管道时会退回系统区域编码（中文 Windows 为 GBK），
        // 而进度条使用的 ░ / █ 无法用 GBK 编码，会抛 UnicodeEncodeError 使进程直接崩溃。
        // 静默模式下唯一的输出是一行结果数量（纯 ASCII），因此可彻底规避该问题。
        var args = new List<string> { imagePath, "-o", outputJsonPath, "--quiet" };
        if (!string.IsNullOrWhiteSpace(model)) { args.Add("-m"); args.Add(model); }
        if (!string.IsNullOrWhiteSpace(language)) { args.Add("-l"); args.Add(language); }

        var (code, stdOut, stdErr) = await RunAsync(exe, args, ct);

        if (code != 0)
            return (false, $"OCR 识别失败（退出码 {code}）: {Tail(stdErr, stdOut)}", outputJsonPath);

        if (!File.Exists(outputJsonPath))
            return (false, $"OCR 未生成结果文件: {outputJsonPath}", outputJsonPath);

        return (true, "OCR 识别完成", outputJsonPath);
    }

    /// <summary>
    /// 把 OCR 结果按图片比例映射到地图格子，生成 zme 建筑放置脚本。
    /// </summary>
    public static async Task<(bool Ok, string Message, string OutputPath)> RunGenBuildingsAsync(
        string mapJsonPath, string geoJsonPath, int imageWidth, int imageHeight,
        string outputZmePath, int buildingType = 11, double minConfidence = 0,
        bool snapToLand = true, CancellationToken ct = default)
    {
        var exe = ResolveToolPath(GenBuildingsExeName);
        if (exe == null)
            return (false, $"未找到 {GenBuildingsExeName}（应位于 Lib 目录）", outputZmePath);

        if (!File.Exists(mapJsonPath))
            return (false, $"地图网格 JSON 不存在: {mapJsonPath}", outputZmePath);
        if (!File.Exists(geoJsonPath))
            return (false, $"OCR 结果不存在: {geoJsonPath}", outputZmePath);

        var args = new List<string>
        {
            mapJsonPath,
            geoJsonPath,
            imageWidth.ToString(CultureInfo.InvariantCulture),
            imageHeight.ToString(CultureInfo.InvariantCulture),
            "-t", buildingType.ToString(CultureInfo.InvariantCulture),
            "-o", outputZmePath
        };

        if (minConfidence > 0)
        {
            args.Add("-c");
            args.Add(minConfidence.ToString(CultureInfo.InvariantCulture));
        }

        if (!snapToLand) args.Add("--no-snap");

        var (code, stdOut, stdErr) = await RunAsync(exe, args, ct);

        if (code != 0)
            return (false, $"生成 zme 失败（退出码 {code}）: {Tail(stdErr, stdOut)}", outputZmePath);

        if (!File.Exists(outputZmePath))
            return (false, $"未生成 zme 文件: {outputZmePath}", outputZmePath);

        return (true, Tail(stdOut, stdErr), outputZmePath);
    }

    /// <summary>截取末尾若干字符，用于把外部工具的报错精简后展示。</summary>
    private static string Tail(string primary, string secondary, int maxLength = 400)
    {
        var text = (string.IsNullOrWhiteSpace(primary) ? secondary : primary).Trim();
        return text.Length <= maxLength ? text : text[^maxLength..];
    }
}
