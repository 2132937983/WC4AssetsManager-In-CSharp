using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>BTL 文件里一个数据块的位置：起始偏移、单条记录大小、记录数。</summary>
public readonly record struct BTLBlockSpan(BtlBlock Block, long Start, int RecordSize, int Count)
{
    public long Length => (long)RecordSize * Count;

    /// <summary>结束偏移（不含）。</summary>
    public long End => Start + Length;

    /// <summary>起始偏移，超出 int 范围时取 int.MaxValue（解析模块遇到越过文件末尾的偏移会直接停止）。</summary>
    public int StartInt => (int)Math.Min(Start, int.MaxValue);
}

/// <summary>
/// BTL 文件的数据块地址表（解析器、保存、检查器共用，BtlInspector 的布局识别也用它排块）。
/// <para>
/// 依据（对 assets/stage 下全部 1358 个文件逐一核对过，推算大小与文件大小精确相等）：
/// 块的排列顺序为 头部 → 军团 → [地形] → 省份 → 归属 → 建筑 → 单位 → 陷阱 → 方案 → 天气 → 事件 → 援军 → 空袭 → 单位放置
/// → 首都 → 战略建设 → 空中支援 → [尾部扩展块]；
/// 地形块的格数是 MapLength × MapWidth，省份 / 归属块的格数是 SelectableTileCount（官方文件常把它向上补齐到 8 的倍数，补齐格全为 0）；
/// 征服布局（MapNumber &gt; 0，以 world{MapNumber}.bin 为底图）没有地形块；
/// 单位 / 援军记录的大小随版本族变化（<see cref="BTLHeader.UsesV3RecordFamily"/>）。
/// </para>
/// </summary>
public sealed class BTLLayout
{
    private readonly BTLBlockSpan?[] _byBlock = new BTLBlockSpan?[Enum.GetValues<BtlBlock>().Length];

    private BTLLayout(List<BTLBlockSpan> spans, bool v3Records)
    {
        Spans = spans;
        UsesV3Records = v3Records;
        foreach (var span in spans) _byBlock[(int)span.Block] = span;
    }

    /// <summary>全部数据块，按文件顺序排列。</summary>
    public IReadOnlyList<BTLBlockSpan> Spans { get; }

    /// <summary>单位 / 援军记录是否为 v3 族（64 / 104 字节）。</summary>
    public bool UsesV3Records { get; }

    /// <summary>是否有地形块（战役布局有，征服布局没有）。</summary>
    public bool HasTerrain => _byBlock[(int)BtlBlock.Terrain] != null;

    /// <summary>最后一个数据块的结束偏移；其后的字节（尾部扩展块）不属于任何已知数据块。</summary>
    public long End => Spans[^1].End;

    public BTLBlockSpan this[BtlBlock block]
        => _byBlock[(int)block] ?? throw new KeyNotFoundException($"布局里没有数据块 {block}");

    /// <summary>按文件头判定布局：MapNumber &gt; 0 为征服布局（与 BtlTypeDetector 同一规则），否则为战役布局。</summary>
    public static BTLLayout For(BTLHeader header) => header.MapNumber > 0 ? ForConquest(header) : ForStage(header);

    /// <summary>战役布局：地形块格数取 MapLength × MapWidth。</summary>
    public static BTLLayout ForStage(BTLHeader header)
        => Create(header, header.UsesV3Records, ClampCount((long)header.MapLength * header.MapWidth), 0);

    /// <summary>征服布局：没有地形块。</summary>
    public static BTLLayout ForConquest(BTLHeader header) => Create(header, header.UsesV3Records, null, 0);

    /// <summary>
    /// 按指定参数排出全部数据块。<paramref name="terrainCount"/> 为 null 表示没有地形块；
    /// <paramref name="extensionLength"/> 大于 0 时在末尾加一个扩展块。
    /// </summary>
    public static BTLLayout Create(BTLHeader header, bool v3Records, int? terrainCount, int extensionLength)
    {
        var spans = new List<BTLBlockSpan>(20);
        long position = 0;

        void Add(BtlBlock block, int recordSize, int count)
        {
            var span = new BTLBlockSpan(block, position, recordSize, count);
            spans.Add(span);
            position = span.End;
        }

        int tiles = ClampCount(header.SelectableTileCount);

        Add(BtlBlock.Header, BTLSize.HEADER_SIZE, 1);
        Add(BtlBlock.Legion, BTLSize.LEGION_SIZE, ClampCount(header.ArmyCount));
        if (terrainCount != null)
            Add(BtlBlock.Terrain, BTLSize.TERRAIN_SIZE, terrainCount.Value);
        Add(BtlBlock.Province, BTLSize.PROVINCE_SIZE, tiles);
        Add(BtlBlock.Belong, 1, tiles);
        Add(BtlBlock.Building, BTLSize.BUILDING_SIZE, ClampCount(header.BuildingCount));
        Add(BtlBlock.Army, BTLArmyModule.GetArmySize(v3Records), ClampCount(header.TroopCount));
        Add(BtlBlock.Trap, BTLSize.TRAP_SIZE, ClampCount(header.TrapCount));
        Add(BtlBlock.Case, BTLSize.CASE_SIZE, ClampCount(header.PlanCount));
        Add(BtlBlock.Weather, BTLSize.WEATHER_SIZE, ClampCount(header.WeatherCount));
        Add(BtlBlock.Event, BTLSize.EVENT_SIZE, ClampCount(header.EventCount));
        Add(BtlBlock.Reinforcement, BTLReinforcementModule.GetReinforcementSize(v3Records), ClampCount(header.ReinforcementCount));
        Add(BtlBlock.AirRaid, BTLSize.AIRFORCE_SIZE, ClampCount(header.AirRaidCount));
        Add(BtlBlock.UnitPlacement, BTLSize.UNITPLACEMENT_SIZE,
            ClampCount((long)ClampCount(header.PlacementA) + ClampCount(header.PlacementB)));
        Add(BtlBlock.Capital, BTLSize.CAPITAL_SIZE, ClampCount(header.ConqueredFlagPosition));
        Add(BtlBlock.Strategy, BTLSize.STRATEGY_SIZE, ClampCount(header.StrategyCount));
        Add(BtlBlock.AirSupport, BTLSize.AIRSUPPORT_SIZE, ClampCount(header.AirSupportCount));
        if (extensionLength > 0)
            Add(BtlBlock.Extension, extensionLength, 1);

        return new BTLLayout(spans, v3Records);
    }

    /// <summary>头部里的数量字段可能是负数或大得离谱（损坏的文件）：夹到 [0, int.MaxValue] 再用。</summary>
    public static int ClampCount(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
