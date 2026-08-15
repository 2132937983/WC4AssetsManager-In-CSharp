using System.Runtime.InteropServices;

namespace WC4MapEditor.Models;

/// <summary>
/// 事件数据结构 (44字节) - 使用结构体减少内存开销
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapEvent
{
    public int Sequence;
    public int RelatedEvent;
    public int TriggerCondition;
    public int TriggerEvent;
    public int TriggerLegion;
    public int AffectedLegion;
    public int CampChange;
    public int Reserved;
    public int TriggerRound;
    public int DialogueCode;
    public int EndSegment;

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
    /// 创建默认事件
    /// </summary>
    public static MapEvent CreateDefault(int seq) => new MapEvent { Sequence = seq };

    /// <summary>
    /// 获取触发条件名称
    /// </summary>
    public readonly string GetTriggerConditionName() => TriggerCondition switch
    {
        0 => "占地触发",
        1 => "部队触发",
        2 => "回合触发",
        4 => "连带触发",
        _ => $"未知({TriggerCondition})"
    };

    /// <summary>
    /// 获取触发事件名称
    /// </summary>
    public readonly string GetTriggerEventName() => TriggerEvent switch
    {
        0 => "士气上升",
        1 => "士气下降",
        2 => "士气大降",
        3 => "混乱",
        4 => "调用对话",
        6 => "军团部队行动方针转变",
        7 => "阵营变换",
        8 => "军团向一个方向进攻",
        10 => "给予经济",
        11 => "给予工业",
        12 => "给予科技",
        _ => $"未知({TriggerEvent})"
    };
}
