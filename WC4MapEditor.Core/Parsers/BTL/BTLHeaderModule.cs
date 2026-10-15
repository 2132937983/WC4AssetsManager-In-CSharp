using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 文件头解析模块。
/// </summary>
public static class BTLHeaderModule
{
    public static BTLHeader Parse(byte[] data)
    {
        if (data == null || data.Length < BTLSize.HEADER_SIZE)
            throw new ArgumentException("数据长度不足，无法解析文件头");

        Debug.WriteLine("[BTLHeaderModule] 开始解析文件头...");
        var header = BTLHeader.Parse(data);
        Debug.WriteLine($"[BTLHeaderModule] 解析完成：宽度={header.MapWidth}, 高度={header.MapLength}, 版本={header.BtlVersion}");
        return header;
    }
}
