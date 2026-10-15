namespace WC4MapEditor.Core.Assets;

/// <summary>
/// 单个资源文件的元数据（缓存条目）。
/// </summary>
public sealed class AssetEntry
{
    /// <summary>文件名（含扩展名），例如 stage10101.btl。</summary>
    public string FileName { get; init; } = "";

    /// <summary>不含扩展名的文件名，例如 stage10101。</summary>
    public string FileNameWithoutExtension { get; init; } = "";

    /// <summary>小写扩展名（不含点），例如 btl、png、xml。</summary>
    public string Extension { get; init; } = "";

    /// <summary>相对 assets 根目录的路径（用 / 分隔），例如 stage/stage10101.btl。</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>绝对路径。</summary>
    public string FullPath { get; init; } = "";

    /// <summary>相对 assets 根目录的直属子目录（顶层分类），根目录下的文件为空字符串。例如 stage、map、audio。</summary>
    public string TopDirectory { get; init; } = "";

    /// <summary>文件字节大小。</summary>
    public long Size { get; init; }

    /// <summary>最后写入时间（UTC）。</summary>
    public DateTime LastModifiedUtc { get; init; }

    /// <summary>语义资源类别（依据目录与文件名推断）。</summary>
    public AssetKind Kind { get; init; }

    public override string ToString() => RelativePath;
}
