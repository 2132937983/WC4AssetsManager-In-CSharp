using System.IO;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 按文件头判断 .btl 的布局类型（对齐 VB BTLConstants.DetectBTLFileType）。
/// <para>
/// 0x04 处的 MapNumber 为 0 = 战役布局（文件自带地形块）；大于 0 = 征服布局
/// （没有地形块，以 world{MapNumber}.bin 为底图，省份/归属只覆盖 MapClip 数据窗口）。
/// </para>
/// <para>
/// 不能按文件名判断：官方数据里 event501xx-514xx、frontier*、stage5xx/600x/700x/80x 等
/// 300 多个文件都是征服布局，却不以 conquest 开头；当成战役解析时每个地形块之后的区段都会读错。
/// </para>
/// </summary>
public static class BtlTypeDetector
{
    /// <summary>BTL 文件头长度；不足一个文件头的文件视为无法识别。</summary>
    private const int HeaderSize = 128;

    private const int MapNumberOffset = 0x04;

    /// <summary>读取文件头判断布局类型；文件不存在、无法读取或不足一个文件头时返回 <see cref="BTLFileType.Unknown"/>。</summary>
    public static BTLFileType Detect(string filePath) => Classify(ReadMapNumber(filePath));

    /// <summary>读取文件头里的 MapNumber（世界地图编号）；无法读取时返回 null。</summary>
    public static int? ReadMapNumber(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            Span<byte> header = stackalloc byte[HeaderSize];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize) return null;
            return BitConverter.ToInt32(header[MapNumberOffset..]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>征服布局 = MapNumber 大于 0（负数是损坏的文件头，按无法识别处理）。</summary>
    private static BTLFileType Classify(int? mapNumber) => mapNumber switch
    {
        null => BTLFileType.Unknown,
        0 => BTLFileType.Campaign,
        > 0 => BTLFileType.Conquest,
        _ => BTLFileType.Unknown
    };
}
