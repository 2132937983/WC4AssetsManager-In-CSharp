using System.Buffers.Binary;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Analyzers;

/// <summary>被检查文件的种类（按文件头与文件大小判定，不看文件名）。</summary>
public enum InspectedFileKind
{
    Unknown,
    Stage,
    Conquest,
    World
}

/// <summary>布局覆盖：自动识别，或强制按战役 / 征服布局解释 BTL。</summary>
public enum InspectLayoutOverride
{
    Auto,
    Stage,
    Conquest
}

/// <summary>
/// 单位 / 援军记录的版本族：<see cref="V1"/> = 48 / 80 字节；<see cref="V3"/> = 64 / 104 字节。
/// 资源里的 BTL v2 文件同样使用 64 / 104 字节记录（按文件大小核实过全部 25 个 v2 关卡）。
/// </summary>
public enum BtlRecordFamily
{
    V1,
    V3
}

/// <summary>文件中的数据块（顺序即 BTL 文件里的排列顺序；世界文件只用到 Header / Terrain / Province）。</summary>
public enum BtlBlock
{
    Header,
    Legion,
    Terrain,
    Province,
    Belong,
    Building,
    Army,
    Trap,
    Case,
    Weather,
    Event,
    Reinforcement,
    AirRaid,
    UnitPlacement,
    Capital,
    Strategy,
    AirSupport,
    Extension,
    Unparsed
}

/// <summary>检查结果的严重程度。</summary>
public enum BtlInspectIssueLevel
{
    Info,
    Warning,
    Error
}

/// <summary>一条检查提示（文本已按当前语言生成）。</summary>
public sealed record BtlInspectIssue(BtlInspectIssueLevel Level, string Message);

/// <summary>世界文件（world.bin）头部：魔数 "YSAE"、版本、宽、高（各 4 字节）。</summary>
public sealed record WorldFileInfo(string Magic, int Version, int Width, int Height)
{
    public long TileCount => (long)Math.Max(Width, 0) * Math.Max(Height, 0);
}

/// <summary>
/// 一个数据块在文件中的位置：起始偏移、单条记录大小、头部声明的记录数，
/// 以及实际完整落在文件内的记录数（文件被截断时小于声明数）。
/// </summary>
public sealed class BtlSegment
{
    internal BtlSegment(BtlBlock block, long start, int recordSize, int count, long fileLength)
    {
        Block = block;
        Start = start;
        RecordSize = recordSize;
        Count = count;

        long available = Math.Max(0, fileLength - start);
        ReadableCount = recordSize <= 0 ? 0 : (int)Math.Min(count, available / recordSize);
    }

    public BtlBlock Block { get; }
    public long Start { get; }
    public int RecordSize { get; }

    /// <summary>头部声明（或布局推算）的记录数。</summary>
    public int Count { get; }

    /// <summary>完整落在文件内、可以解码的记录数。</summary>
    public int ReadableCount { get; }

    public long Length => (long)RecordSize * Count;

    /// <summary>结束偏移（不含）。</summary>
    public long End => Start + Length;

    public bool IsTruncated => ReadableCount < Count;

    public long RecordOffset(int index) => Start + (long)index * RecordSize;
}

/// <summary>识别出的文件布局：所有数据块的地址表，以及与文件实际大小的吻合情况。</summary>
public sealed class BtlLayout
{
    public required InspectedFileKind Kind { get; init; }
    public required BtlRecordFamily Family { get; init; }

    /// <summary>战役文件地形块的格数（征服 / 世界文件为 0 或世界格数）。</summary>
    public required int TerrainCount { get; init; }

    /// <summary>地形格数取自 MapLength × MapWidth（true）还是 SelectableTileCount（false）。</summary>
    public required bool TerrainFromMapArea { get; init; }

    /// <summary>是否识别出尾部扩展数据块（长度 = 头部 0x54）。</summary>
    public required bool HasExtension { get; init; }

    /// <summary>已知数据块（含扩展块）推算出的结束偏移。</summary>
    public required long ExpectedLength { get; init; }

    public required long FileLength { get; init; }
    public required IReadOnlyList<BtlSegment> Segments { get; init; }

    /// <summary>推算大小与文件大小完全一致：所有字节都有归属。</summary>
    public bool IsExact => ExpectedLength == FileLength;

    /// <summary>文件比推算多出、无法归属的字节数。</summary>
    public long UnparsedBytes => Math.Max(0, FileLength - ExpectedLength);

    /// <summary>头部声明的数据比文件多出的字节数（文件被截断）。</summary>
    public long MissingBytes => Math.Max(0, ExpectedLength - FileLength);
}

/// <summary>偏移定位结果：所在数据块、记录序号、记录内偏移、所在字段（可能为 null）。</summary>
public readonly record struct BtlLocation(BtlSegment Segment, int RecordIndex, int OffsetInRecord, BtlFieldDef? Field);

/// <summary>
/// BTL / BIN 文件只读检查器（数据检查场景、inspect / compare / convert 命令共用）。
/// <para>
/// 文件种类与布局<b>按内容判定</b>：世界文件看魔数 "YSAE"；BTL 用头部各数量字段推算战役 / 征服两种布局
/// 的总大小，与文件大小精确比对（见 <see cref="DetectBtlLayout"/>）。不看文件名 —— 资源里有不少
/// event5xxxx 关卡其实是征服布局（没有地形块）。
/// </para>
/// <para>
/// 检查器只读：<see cref="Data"/> 是读入的字节副本，任何方法都不会修改它，也不会写回文件。
/// </para>
/// </summary>
public sealed partial class BtlInspector
{
    /// <summary>一次最多读入的字节数（与 VB 版一致：256 MB，更大的文件只读前 256 MB）。</summary>
    public const long MaxLoadBytes = 256L * 1024 * 1024;

    private static readonly byte[] WorldMagic = { 0x59, 0x53, 0x41, 0x45 };   // "YSAE"

    private readonly Dictionary<BtlBlock, BtlSegment> _segmentByBlock = new();
    private readonly List<BtlSegment> _nonEmptySegments = new();

    private BtlInspector(string sourceName, byte[] data, long fileLength, BTLHeader? header, WorldFileInfo? world,
                         BtlLayout layout, List<BtlInspectIssue> issues)
    {
        SourceName = sourceName;
        Data = data;
        FileLength = fileLength;
        Header = header;
        World = world;
        Layout = layout;
        Issues = issues;

        foreach (var segment in layout.Segments)
        {
            _segmentByBlock[segment.Block] = segment;
            if (segment.Length > 0) _nonEmptySegments.Add(segment);
        }
    }

    /// <summary>来源（文件路径或调用方给的名字）。</summary>
    public string SourceName { get; }

    /// <summary>读入的文件字节（只读使用）。</summary>
    public byte[] Data { get; }

    /// <summary>文件在磁盘上的真实大小（超过 <see cref="MaxLoadBytes"/> 时大于 <see cref="Data"/> 的长度）。</summary>
    public long FileLength { get; }

    /// <summary>只读入了文件的前一部分。</summary>
    public bool IsPartial => Data.LongLength < FileLength;

    /// <summary>BTL 头部（世界 / 未知文件为 null）。</summary>
    public BTLHeader? Header { get; }

    /// <summary>世界文件头部（非世界文件为 null）。</summary>
    public WorldFileInfo? World { get; }

    public BtlLayout Layout { get; }
    public InspectedFileKind Kind => Layout.Kind;
    public IReadOnlyList<BtlInspectIssue> Issues { get; }

    /// <summary>BTL 版本号（非 BTL 为 0）。</summary>
    public int BtlVersion => Header?.BtlVersion ?? 0;

    /// <summary>文件头里的版本号：世界文件取世界文件版本，其余取 BTL 版本。</summary>
    public int FileVersion => World?.Version ?? BtlVersion;

    public bool IsBtl => Kind is InspectedFileKind.Stage or InspectedFileKind.Conquest;

    /// <summary>地图列数（坐标 = 行 × 列数 + 列）。BTL 取 MapLength，世界文件取宽度。</summary>
    public int MapColumns => World?.Width ?? Header?.MapLength ?? 0;

    /// <summary>地图行数。</summary>
    public int MapRows => World?.Height ?? Header?.MapWidth ?? 0;

    /// <summary>地图总格数（坐标的合法上限）。</summary>
    public long MapArea => (long)Math.Max(MapColumns, 0) * Math.Max(MapRows, 0);

    /// <summary>非空数据块，按起始偏移排序。</summary>
    public IReadOnlyList<BtlSegment> NonEmptySegments => _nonEmptySegments;

    // ============================================================= 加载 =============================================================

    /// <summary>只读打开并检查一个文件（允许其它程序同时打开；超过 256 MB 只读前 256 MB）。</summary>
    public static BtlInspector Load(string path, InspectLayoutOverride layoutOverride = InspectLayoutOverride.Auto)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long fileLength = stream.Length;
        int toRead = (int)Math.Min(fileLength, MaxLoadBytes);
        var data = new byte[toRead];
        int offset = 0;
        while (offset < toRead)
        {
            int read = stream.Read(data, offset, toRead - offset);
            if (read == 0) break;
            offset += read;
        }
        if (offset < toRead) Array.Resize(ref data, offset);

        return FromBytes(data, path, layoutOverride, fileLength);
    }

    /// <summary>检查一段内存中的字节（<paramref name="fileLength"/> 为空时等于数据长度）。</summary>
    public static BtlInspector FromBytes(byte[] data, string sourceName,
                                         InspectLayoutOverride layoutOverride = InspectLayoutOverride.Auto,
                                         long? fileLength = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        long length = fileLength ?? data.LongLength;
        var issues = new List<BtlInspectIssue>();

        if (data.LongLength < length)
            issues.Add(new(BtlInspectIssueLevel.Warning, Loc.F("DataCheck.Issue.Partial", MaxLoadBytes / (1024 * 1024), length)));

        if (layoutOverride == InspectLayoutOverride.Auto && IsWorldFile(data))
        {
            var world = ReadWorldHeader(data);
            var worldLayout = BuildWorldLayout(world, data.LongLength, issues);
            return new BtlInspector(sourceName, data, length, null, world, worldLayout, issues);
        }

        if (data.Length < BTLSize.HEADER_SIZE)
        {
            issues.Add(new(BtlInspectIssueLevel.Error, Loc.F("DataCheck.Issue.TooSmall", data.Length, BTLSize.HEADER_SIZE)));
            return new BtlInspector(sourceName, data, length, null, null, BuildUnknownLayout(data.LongLength), issues);
        }

        var header = BTLHeader.Parse(data);
        var layout = DetectBtlLayout(header, data.LongLength, layoutOverride, issues);
        return new BtlInspector(sourceName, data, length, header, null, layout, issues);
    }

    private static bool IsWorldFile(byte[] data)
        => data.Length >= 16 && data.AsSpan(0, 4).SequenceEqual(WorldMagic);

    private static WorldFileInfo ReadWorldHeader(byte[] data)
    {
        var span = data.AsSpan();
        return new WorldFileInfo(
            System.Text.Encoding.ASCII.GetString(data, 0, 4),
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
    }

    // ============================================================= 查询 =============================================================

    /// <summary>取数据块（布局里没有该块时返回 null）。</summary>
    public BtlSegment? GetSegment(BtlBlock block)
        => _segmentByBlock.TryGetValue(block, out var segment) ? segment : null;

    /// <summary>可解码的记录数（块不存在为 0）。</summary>
    public int GetRecordCount(BtlBlock block) => GetSegment(block)?.ReadableCount ?? 0;

    /// <summary>该块记录的字段定义（扩展块 / 未解析数据没有固定字段，返回空）。</summary>
    public IReadOnlyList<BtlFieldDef> GetSchema(BtlBlock block) => SchemaFor(block, Kind, Layout.Family);

    /// <summary>把文件偏移定位到数据块 / 记录 / 字段。偏移不属于任何数据块时返回 null。</summary>
    public BtlLocation? Locate(long offset)
    {
        if (offset < 0 || offset >= Data.LongLength) return null;

        int lo = 0, hi = _nonEmptySegments.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            var segment = _nonEmptySegments[mid];
            if (offset < segment.Start) hi = mid - 1;
            else if (offset >= segment.End) lo = mid + 1;
            else
            {
                long rel = offset - segment.Start;
                int index = (int)(rel / segment.RecordSize);
                int inRecord = (int)(rel % segment.RecordSize);
                BtlFieldDef? field = null;
                foreach (var def in GetSchema(segment.Block))
                {
                    if (inRecord >= def.Offset && inRecord < def.Offset + def.Size) { field = def; break; }
                }
                return new BtlLocation(segment, index, inRecord, field);
            }
        }
        return null;
    }

    /// <summary>"数据块 · 第 n 条 · +偏移 · 字段" 形式的定位描述（不在任何块内时给出提示）。</summary>
    public string DescribeLocation(long offset)
    {
        var location = Locate(offset);
        if (location is not { } loc) return Loc.T("DataCheck.Locate.Outside");

        string block = BlockDisplayName(loc.Segment.Block);
        string field = loc.Field?.DisplayName ?? "—";
        return loc.Segment.Count > 1 || loc.Segment.RecordSize <= 4
            ? Loc.F("DataCheck.Locate.Record", block, loc.RecordIndex, loc.OffsetInRecord, field)
            : Loc.F("DataCheck.Locate.Blob", block, loc.OffsetInRecord, field);
    }

    /// <summary>数据块的显示名。</summary>
    public static string BlockDisplayName(BtlBlock block) => Loc.T("DataCheck.Block." + block);

    /// <summary>文件种类的显示名。</summary>
    public static string KindDisplayName(InspectedFileKind kind) => Loc.T("DataCheck.Kind." + kind);

    /// <summary>坐标（格子序号）换算成 (列,行)；超出地图返回 null。</summary>
    public (int Col, int Row)? CoordinateToCell(long coordinate)
    {
        int columns = MapColumns;
        if (columns <= 0 || coordinate < 0 || coordinate >= MapArea) return null;
        return ((int)(coordinate % columns), (int)(coordinate / columns));
    }

    /// <summary>
    /// 归属数组序号换算成 (列,行)。征服地图的归属只覆盖 MapClip 裁剪区，序号是裁剪区内的相对位置
    /// （与 MapData.GetBelongIndex 互逆）；无裁剪时等同于普通坐标。
    /// 落在地图外的序号（裁剪区右侧、末尾补齐的格子）返回 null。
    /// </summary>
    public (int Col, int Row)? BelongIndexToCell(int index)
    {
        if (Header == null) return CoordinateToCell(index);

        int columns = Header.MapLength;
        if (columns <= 0 || index < 0) return null;

        int col = Math.Max(0, Header.MapClipX) + index % columns;
        int row = Math.Max(0, Header.MapClipY) + index / columns;
        return col < columns && row < Header.MapWidth ? (col, row) : null;
    }

    /// <summary>
    /// 找到位于 (列,行) 的第一条记录：地形 / 省份按格子序号直接定位，归属按 MapClip 裁剪区换算
    /// （<see cref="BelongIndexToCell"/> 的逆运算），其余带坐标字段的块逐条比对坐标。找不到返回 -1。
    /// </summary>
    public int FindRecordAt(BtlBlock block, int col, int row)
    {
        if (col < 0 || row < 0 || col >= MapColumns || row >= MapRows) return -1;

        int count = GetRecordCount(block);
        long coordinate = (long)row * MapColumns + col;
        switch (block)
        {
            case BtlBlock.Terrain:
            case BtlBlock.Province:
                return coordinate < count ? (int)coordinate : -1;
            case BtlBlock.Belong:
            {
                int clipX = Math.Max(0, Header?.MapClipX ?? 0);
                int clipY = Math.Max(0, Header?.MapClipY ?? 0);
                if (col < clipX || row < clipY) return -1;
                long index = (long)(row - clipY) * MapColumns + (col - clipX);
                return index < count ? (int)index : -1;
            }
        }

        var coordinateField = GetSchema(block).FirstOrDefault(f => f.Meaning == BtlValueKind.Coordinate);
        var segment = GetSegment(block);
        if (coordinateField == null || segment == null) return -1;

        for (int i = 0; i < count; i++)
        {
            if (ReadRaw(coordinateField, segment.RecordOffset(i)) == coordinate) return i;
        }
        return -1;
    }
}
