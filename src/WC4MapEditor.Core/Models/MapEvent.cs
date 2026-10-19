using System.Globalization;
using System.Runtime.InteropServices;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 事件数据结构 (44字节) - 使用结构体减少内存开销
/// <para>
/// 字段名沿用 VB 版，其他模块（建筑的占领事件等）直接读写这些字段，名字保持不变。
/// 各字段的实际含义按原版关卡数据核对过，写在字段注释里；有几个字段的含义比名字宽。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapEvent
{
    /// <summary>序号：事件的标识。建筑 / 单位的占领事件字段（1 字节）与其他事件的关联事件字段都按序号引用事件。</summary>
    public int Sequence;

    /// <summary>关联事件：低 16 位是被关联事件的序号（0 = 无），高 16 位是附加参数（原版关卡里出现过 1~6）。</summary>
    public int RelatedEvent;

    /// <summary>触发条件（setting.txt 的 TriggerList），例如 0 占地触发、1 部队触发、2 回合触发、4 连带触发。</summary>
    public int TriggerCondition;

    /// <summary>触发事件，即事件效果（setting.txt 的 EventList），例如 4 调用对话、10 给予经济。</summary>
    public int TriggerEvent;

    /// <summary>触发军团：军团的行动序号（<see cref="Legion.ActionId"/>，不是军团列表下标），0 = 不指定。</summary>
    public int TriggerLegion;

    /// <summary>受影响军团：军团的行动序号（<see cref="Legion.ActionId"/>），0 = 不指定。</summary>
    public int AffectedLegion;

    /// <summary>
    /// "阵营变换"：随触发事件变化的参数 —— 阵营变换 = 新阵营，行动方针转变 = 新方针，
    /// 军团向一个方向进攻 = 目标格坐标，给予经济 / 工业 / 科技 = 数量。
    /// </summary>
    public int CampChange;

    /// <summary>保留字段（原版关卡里绝大多数为 0）。</summary>
    public int Reserved;

    /// <summary>触发回合（回合触发时使用）。</summary>
    public int TriggerRound;

    /// <summary>对话代码：事件触发时显示的对话编号。</summary>
    public int DialogueCode;

    /// <summary>结束段：原版关卡固定为字节 00 CC CC CC（<see cref="DefaultEndSegment"/>）。</summary>
    public int EndSegment;

    /// <summary>结束段默认值：字节 00 CC CC CC（按小端 int 读出为 0xCCCCCC00），与原版关卡一致。</summary>
    public const int DefaultEndSegment = unchecked((int)0xCCCCCC00);

    /// <summary>触发条件"占地触发"：由建筑的占领事件指向它。</summary>
    public const int ConditionOccupation = 0;

    /// <summary>触发条件"回合触发"：<see cref="TriggerRound"/> 生效。</summary>
    public const int ConditionRound = 2;

    /// <summary>触发事件"军团向一个方向进攻"：<see cref="CampChange"/> 是目标格的下标（行 × 地图宽 + 列）。</summary>
    public const int EffectAttackTowards = 8;

    /// <summary>关联事件字段里"被关联事件的序号"（低 16 位）。</summary>
    public readonly int RelatedSequence => RelatedEvent & 0xFFFF;

    /// <summary>
    /// 从字节数组解析事件数据
    /// </summary>
    public static MapEvent FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<MapEvent>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认事件：只填序号，结束段取原版的 00 CC CC CC。
    /// <para>VB 版这里结束段写的是 0，而编辑器新建事件时写 00 CC CC CC，两处不一致；现统一为原版数据的写法。</para>
    /// </summary>
    public static MapEvent CreateDefault(int seq) => new() { Sequence = seq, EndSegment = DefaultEndSegment };

    /// <summary>触发条件名称（setting.txt 优先，见 <see cref="MapEventNames"/>）</summary>
    public readonly string GetTriggerConditionName() => MapEventNames.Condition(TriggerCondition);

    /// <summary>触发事件名称（setting.txt 优先，见 <see cref="MapEventNames"/>）</summary>
    public readonly string GetTriggerEventName() => MapEventNames.Effect(TriggerEvent);
}

/// <summary>
/// 事件"触发条件 / 触发事件"的名称与下拉选项。
/// <para>
/// 取名顺序：setting.txt 的 [事件修改]（TriggerList / EventList）→ 内置名称 → "未知(n)"。
/// setting.txt 里与内置中文名一字不差的项按界面语言显示（英文界面显示英文名），
/// 用户改过名字或自己加的项（如 (5,"测试")）照原样显示。setting.txt 缺这一节时退回内置列表（与 VB 版一致）。
/// </para>
/// </summary>
public static class MapEventNames
{
    // 内置名称 = VB 版 MapEvent 里硬编码的列表
    private static readonly (int Value, string Zh)[] BuiltInConditions =
    [
        (0, "占地触发"), (1, "部队触发"), (2, "回合触发"), (4, "连带触发"),
    ];

    private static readonly (int Value, string Zh)[] BuiltInEffects =
    [
        (0, "士气上升"), (1, "士气下降"), (2, "士气大降"), (3, "混乱"), (4, "调用对话"),
        (6, "军团部队行动方针转变"), (7, "阵营变换"), (8, "军团向一个方向进攻"),
        (10, "给予经济"), (11, "给予工业"), (12, "给予科技"),
    ];

    /// <summary>触发条件下拉选项（值 → 名称）。</summary>
    public static IReadOnlyList<ConfigKeyValue> ConditionOptions()
        => Options(ConfigManager.Instance.GetEventEditConfig().TriggerList, BuiltInConditions, "Condition");

    /// <summary>触发事件下拉选项（值 → 名称）。</summary>
    public static IReadOnlyList<ConfigKeyValue> EffectOptions()
        => Options(ConfigManager.Instance.GetEventEditConfig().EventList, BuiltInEffects, "Effect");

    /// <summary>触发条件的显示名。</summary>
    public static string Condition(int value)
        => Name(ConfigManager.Instance.GetEventEditConfig().TriggerList, BuiltInConditions, "Condition", value);

    /// <summary>触发事件的显示名。</summary>
    public static string Effect(int value)
        => Name(ConfigManager.Instance.GetEventEditConfig().EventList, BuiltInEffects, "Effect", value);

    private static List<ConfigKeyValue> Options(List<ConfigKeyValue> configured, (int Value, string Zh)[] builtIn, string kind)
    {
        if (configured.Count == 0)
            return builtIn.Select(b => new ConfigKeyValue { Value = b.Value, Name = BuiltInName(kind, b.Value, b.Zh) }).ToList();
        return configured.Select(c => new ConfigKeyValue { Value = c.Value, Name = Localize(kind, c.Value, c.Name, builtIn) }).ToList();
    }

    private static string Name(List<ConfigKeyValue> configured, (int Value, string Zh)[] builtIn, string kind, int value)
    {
        string? name = ConfigKeyValue.NameOf(configured, value);
        if (name != null) return Localize(kind, value, name, builtIn);
        foreach (var b in builtIn)
            if (b.Value == value) return BuiltInName(kind, value, b.Zh);
        return Loc.F("Module.Event.Unknown", value);
    }

    /// <summary>setting.txt 的名字与内置中文名相同 → 用内置名的译文；否则保留用户的写法。</summary>
    private static string Localize(string kind, int value, string configuredName, (int Value, string Zh)[] builtIn)
    {
        foreach (var b in builtIn)
            if (b.Value == value && b.Zh == configuredName) return BuiltInName(kind, value, b.Zh);
        return configuredName;
    }

    private static string BuiltInName(string kind, int value, string zh)
        => Loc.T($"Module.Event.{kind}.{value.ToString(CultureInfo.InvariantCulture)}", zh);
}

/// <summary>
/// 事件的一个可编辑字段：名称、取值范围、显示 / 解析方式。事件设置窗口与命令行共用这张表，
/// 两边的字段名、范围与报错完全一致。
/// <para>
/// 范围按原版关卡数据定：VB 版声明的 0~99999 / 0~255 会拒收原版数据（关联事件与保留字段带高位参数，
/// "阵营变换"在给予经济时可达 20000，部分触发条件下触发回合也是打包值），只对确实是小整数的字段收窄。
/// </para>
/// </summary>
public sealed class MapEventField
{
    public delegate void Setter(ref MapEvent e, int value);

    private readonly string _zhLabel;
    private readonly string _zhTooltip;
    private readonly Func<MapEvent, int> _get;
    private readonly Setter _set;

    internal MapEventField(string name, string zhLabel, string zhTooltip, long min, long max, bool hex,
        Func<MapEvent, int> get, Setter set)
    {
        Name = name;
        _zhLabel = zhLabel;
        _zhTooltip = zhTooltip;
        Min = min;
        Max = max;
        IsHex = hex;
        _get = get;
        _set = set;
    }

    /// <summary>字段名（= <see cref="MapEvent"/> 的字段名，命令行里也用它）。</summary>
    public string Name { get; }

    public long Min { get; }
    public long Max { get; }

    /// <summary>按十六进制显示、按无符号 32 位解释（结束段）。</summary>
    public bool IsHex { get; }

    /// <summary>字段名（当前语言）。</summary>
    public string Label => Loc.T($"Module.Event.Field.{Name}", _zhLabel);

    /// <summary>字段说明（当前语言）。</summary>
    public string Tooltip => Loc.T($"Module.Event.Field.{Name}.Tip", _zhTooltip);

    public int Get(in MapEvent e) => _get(e);

    public void Set(ref MapEvent e, int value) => _set(ref e, value);

    public string Format(int value)
        => IsHex ? "0x" + ((uint)value).ToString("X8", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 解析输入（十进制，或 0x 开头的十六进制）并检查范围。失败时 <paramref name="error"/> 是可直接显示的说明，
    /// 绝不把无法解析的输入当成 0（VB 版会静默写 0）。
    /// </summary>
    public bool TryParse(string? text, out int value, out string? error)
    {
        value = 0;
        error = null;
        string s = (text ?? "").Trim();
        long parsed;
        bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(s.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed)
            : long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed);
        if (!ok)
        {
            error = Loc.F("Module.Event.Error.NotNumber", Label);
            return false;
        }
        if (!TryValidate(parsed, out error)) return false;
        value = unchecked((int)parsed);
        return true;
    }

    /// <summary>检查数值是否在本字段的范围内；不在时 <paramref name="error"/> 是可直接显示的说明。</summary>
    public bool TryValidate(long value, out string? error)
    {
        if (value >= Min && value <= Max)
        {
            error = null;
            return true;
        }
        error = Loc.F("Module.Event.Error.OutOfRange", Label, Format(unchecked((int)Min)), Format(unchecked((int)Max)));
        return false;
    }
}

/// <summary>事件的 11 个字段（按文件中的顺序）。</summary>
public static class MapEventFields
{
    public static readonly MapEventField Sequence = new(nameof(MapEvent.Sequence), "序号",
        "事件的唯一标识序号。建筑 / 单位的占领事件与其他事件的关联事件都按序号引用它（建筑与单位只能引用 1~255）",
        0, 65535, false, e => e.Sequence, (ref MapEvent e, int v) => e.Sequence = v);

    public static readonly MapEventField RelatedEvent = new(nameof(MapEvent.RelatedEvent), "关联事件",
        "关联的事件序号（0 = 无）。低 16 位是事件序号，高 16 位是附加参数（原版关卡里出现过 1~6），可用 0x 写法输入",
        0, int.MaxValue, false, e => e.RelatedEvent, (ref MapEvent e, int v) => e.RelatedEvent = v);

    public static readonly MapEventField TriggerCondition = new(nameof(MapEvent.TriggerCondition), "触发条件",
        "事件的触发方式，可从下拉列表（setting.txt 的 TriggerList）中选择：0=占地触发,1=部队触发,2=回合触发,4=连带触发",
        0, 255, false, e => e.TriggerCondition, (ref MapEvent e, int v) => e.TriggerCondition = v);

    public static readonly MapEventField TriggerEvent = new(nameof(MapEvent.TriggerEvent), "触发事件",
        "事件触发后的效果，可从下拉列表（setting.txt 的 EventList）中选择：0=士气上升,1=士气下降,2=士气大降,3=混乱,4=调用对话,6=军团部队行动方针转变,7=阵营变换,8=军团向一个方向进攻,10=给予经济,11=给予工业,12=给予科技",
        0, 255, false, e => e.TriggerEvent, (ref MapEvent e, int v) => e.TriggerEvent = v);

    public static readonly MapEventField TriggerLegion = new(nameof(MapEvent.TriggerLegion), "触发军团",
        "触发事件的军团：填军团的行动序号（ActionId，不是列表位置），0 = 不指定",
        0, 255, false, e => e.TriggerLegion, (ref MapEvent e, int v) => e.TriggerLegion = v);

    public static readonly MapEventField AffectedLegion = new(nameof(MapEvent.AffectedLegion), "受影响军团",
        "受事件影响的军团：填军团的行动序号（ActionId，不是列表位置），0 = 不指定",
        0, 255, false, e => e.AffectedLegion, (ref MapEvent e, int v) => e.AffectedLegion = v);

    public static readonly MapEventField CampChange = new(nameof(MapEvent.CampChange), "阵营变换",
        "随触发事件变化的参数：阵营变换=新阵营，行动方针转变=新方针，军团向一个方向进攻=目标格下标（行 × 地图宽 + 列），给予经济/工业/科技=数量",
        0, int.MaxValue, false, e => e.CampChange, (ref MapEvent e, int v) => e.CampChange = v);

    public static readonly MapEventField Reserved = new(nameof(MapEvent.Reserved), "保留字段",
        "保留字段（原版关卡里绝大多数为 0）",
        0, int.MaxValue, false, e => e.Reserved, (ref MapEvent e, int v) => e.Reserved = v);

    public static readonly MapEventField TriggerRound = new(nameof(MapEvent.TriggerRound), "触发回合",
        "触发事件的回合数（回合触发时使用）",
        0, int.MaxValue, false, e => e.TriggerRound, (ref MapEvent e, int v) => e.TriggerRound = v);

    public static readonly MapEventField DialogueCode = new(nameof(MapEvent.DialogueCode), "对话代码",
        "调用的对话代码（0 = 无）",
        0, int.MaxValue, false, e => e.DialogueCode, (ref MapEvent e, int v) => e.DialogueCode = v);

    public static readonly MapEventField EndSegment = new(nameof(MapEvent.EndSegment), "结束段",
        "默认结束段(00 CC CC CC)，按十六进制显示为 0xCCCCCC00",
        0, uint.MaxValue, true, e => e.EndSegment, (ref MapEvent e, int v) => e.EndSegment = v);

    /// <summary>全部字段（文件中的顺序）。</summary>
    public static IReadOnlyList<MapEventField> All { get; } =
    [
        Sequence, RelatedEvent, TriggerCondition, TriggerEvent, TriggerLegion, AffectedLegion,
        CampChange, Reserved, TriggerRound, DialogueCode, EndSegment,
    ];

    /// <summary>按字段名查找（不区分大小写）。</summary>
    public static MapEventField? Find(string name)
        => All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}
